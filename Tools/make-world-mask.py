# -*- coding: utf-8 -*-
"""生成青石镇大世界的语义 mask（第一级：地形类型图）。

输入（全部来自配置，脚本内不写死任何坐标）：
    GameClient/TwelveJade/Assets/Resources/Config/terrain-types.json   地形类型表 + maskScale
    GameClient/TwelveJade/Assets/Resources/Config/town-map.json        世界尺寸 / blocked / terrain 形状参数
    GameClient/TwelveJade/Assets/Resources/Config/world-compose.json   过渡带扩张半径

输出：
    Assets/ArtSource/world/masks/world-mask.png        单通道 uint8，像素值 = 地形类型 value
    Assets/ArtSource/world/masks/world-mask-debug.png  RGB 伪彩，仅供人工检查

可复现：不用 random、不用时间戳、不用块 ID；所有形状由配置里的解析参数求值。
"""
import argparse, json, math, os, sys
import numpy as np
from PIL import Image, ImageFilter

# Windows GBK 控制台下 print 中文/箭头会 UnicodeEncodeError 把整道门禁崩掉——
# 一个会崩的门禁比一个红的门禁危险得多（架构师 2026-10-05 定规，全仓 Tools/*.py 入口统一）。
import sys
for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, 'reconfigure'):
        _stream.reconfigure(encoding='utf-8', errors='replace')

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, 'Tools'))
import world_paths as wp
from world_geometry import main_road_half

CFG_DIR = wp.config_root()
UNITY_ROOT = wp.unity_project_root()
REPO_PREFIX = 'GameClient/TwelveJade/'


# ---------------------------------------------------------------- 基础 IO
def load_json(path):
    with open(path, encoding='utf-8') as handle:
        return json.load(handle)


def load_terrain_types(path):
    cfg = load_json(path)
    return cfg, {t['id']: t for t in cfg['types']}


def find_world_config():
    """优先用 town-map.json（Unity 权威配置），回退到生成器产物。"""
    for name in ('town-map.json', 'town-world-gen.json'):
        path = os.path.join(CFG_DIR, name)
        if os.path.exists(path):
            return path, load_json(path)
    raise SystemExit('找不到世界配置：' + CFG_DIR)


def resolve_path(p, allow_missing=False):
    """把配置里的路径解析成绝对路径（统一经 Tools/world_paths.py）。

    allow_missing=True 用于输出文件：文件还不存在时返回将要写入的位置。
    """
    if os.path.isabs(p):
        return p
    cand = wp.resolve_repo_path(p, allow_missing=allow_missing)
    if os.path.exists(cand) or allow_missing:
        return cand
    raise SystemExit('路径不存在：' + p)


# ---------------------------------------------------------------- 尺寸
def get_world_size(config, terrain_types):
    for key in ('worldSize',):
        if key in terrain_types:
            return int(terrain_types[key]), int(terrain_types[key])
    w = int(config.get('worldWidth') or 0)
    h = int(config.get('worldHeight') or 0)
    if w <= 0 or h <= 0:
        raise SystemExit('配置缺少 worldWidth/worldHeight，且 terrain-types.json 无 worldSize')
    return w, h


def get_mask_size(world_size, mask_scale):
    w = (int(world_size[0]) + mask_scale - 1) // mask_scale
    h = (int(world_size[1]) + mask_scale - 1) // mask_scale
    return w, h


def world_rect_to_mask_rect(rect, mask_scale, mask_size):
    x0 = int(math.floor(rect['x'] / mask_scale))
    y0 = int(math.floor(rect['y'] / mask_scale))
    x1 = int(math.ceil((rect['x'] + rect['w']) / mask_scale))
    y1 = int(math.ceil((rect['y'] + rect['h']) / mask_scale))
    x0 = max(0, min(mask_size, x0)); y0 = max(0, min(mask_size, y0))
    x1 = max(0, min(mask_size, x1)); y1 = max(0, min(mask_size, y1))
    return x0, y0, max(0, x1 - x0), max(0, y1 - y0)


def fill_rect(mask, rect, value):
    x0, y0, w, h = rect
    if w > 0 and h > 0:
        mask[y0:y0 + h, x0:x0 + w] = value


# ---------------------------------------------------------------- 二值膨胀（只用 Pillow + numpy）
def binary_dilate(arr, radius):
    if radius <= 0:
        return arr.astype(bool)
    img = Image.fromarray((arr.astype(np.uint8) * 255), mode='L')
    img = img.filter(ImageFilter.MaxFilter(radius * 2 + 1))
    return np.array(img) > 127


# ---------------------------------------------------------------- 地形形状（全部按 terrain 参数求值）
def wave_sum(x, waves, key_y=None):
    total = np.zeros_like(np.asarray(x, dtype=np.float64))
    for wv in waves:
        total = total + wv['amp'] * np.sin(np.asarray(x, dtype=np.float64) / wv['period'] + wv.get('phase', 0.0))
    return total


def mtn_edge_x(x, terrain):
    return terrain['mountain']['baseY'] + wave_sum(x, terrain['mountain']['wave'])


def river_center_x(x, terrain):
    return terrain['river']['baseY'] + wave_sum(x, terrain['river']['wave'])


def main_road_x(y, terrain):
    mr = terrain['mainRoad']
    return mr['baseX'] + mr['amp'] * np.sin(np.asarray(y, dtype=np.float64) / mr['periodY'])


def build_type_map(world_size, terrain, values, mask_scale, origin=(0,0), output_size=None):
    """逐地形类型求值，返回各类型的布尔掩膜（mask 像素坐标）。"""
    width,height = output_size or (int(world_size//mask_scale),int(world_size//mask_scale))
    xs = origin[0]+(np.arange(width,dtype=np.float64)+.5)*mask_scale
    ys = origin[1]+(np.arange(height,dtype=np.float64)+.5)*mask_scale
    gx = np.repeat(xs[None,:],height,axis=0)
    gy = np.repeat(ys[:,None],width,axis=1)

    out = {}
    river = river_center_x(gx, terrain)
    mr = terrain['mainRoad']

    out[values['mountain']] = gy < mtn_edge_x(gx, terrain)
    trail = terrain['mountainPath']
    # 山道带 y 范围（y1 缺省 = 无限制，兼容旧配置）。2026-10-24 架构师修：山道曾是一条
    # 无 y 限制的竖直带，写入优先级高于 stone-road，会把镇子从中间劈成两半。
    trail_band = np.abs(gx - (trail['baseX'] + trail['amp'] * np.sin(gy / trail['periodY']))) < trail['half']
    if 'y0' in trail or 'y1' in trail:
        trail_band &= (gy >= trail.get('y0', 0.0)) & (gy <= trail.get('y1', 1e9))
    out[values['mountain-path']] = trail_band
    out[values['water']] = np.abs(gy - river) < terrain['river']['waterHalf']
    out[values['riverbank']] = (np.abs(gy - river) < terrain['river']['bankHalf']) & \
                              (np.abs(gy - river) >= terrain['river']['waterHalf'])

    bridge = np.zeros_like(out[values['water']])
    for br in terrain['bridges']:
        bridge |= (np.abs(gx - br['x']) < br['half']) & \
                  (np.abs(gy - river_center_x(br['x'], terrain)) < terrain['river']['bankHalf'] + 20)
    fe = terrain['ferry']
    bridge |= (np.abs(gx - fe['x']) < fe['half']) & \
              (gy >= river_center_x(fe['x'], terrain)+50) & (gy < river_center_x(fe['x'], terrain)+terrain['river']['bankHalf']+30)
    out[values['bridge']] = bridge

    plaza = terrain['plaza']
    mkt = terrain['market']
    town = ((gx - plaza['x']) ** 2 + (gy - plaza['y']) ** 2 < terrain['townRadius'] ** 2) | \
          ((gx - mkt['x']) ** 2 + (gy - mkt['y']) ** 2 < mkt['r'] ** 2)
    # MAT-07：节点铺装（南门牌坊前/山门前/桥头/渡口）—— 镇界是圆，节点常在圆外的草地上，
    # 而『牌坊立在草地里』是最刺眼的一处。节点坐标来自真源 terrainShape.pavedNodes。
    for nd in terrain.get('pavedNodes', []):
        town |= ((gx - nd['x']) ** 2 + (gy - nd['y']) ** 2) < nd['r'] ** 2
    public_paving = ((gx-plaza['x'])**2+(gy-plaza['y'])**2 < plaza['r']**2) | ((gx-mkt['x'])**2+(gy-mkt['y'])**2 < mkt['r']**2)
    for nd in terrain.get('pavedNodes', []):
        public_paving |= ((gx-nd['x'])**2+(gy-nd['y'])**2) < nd['r']**2
    for r in terrain.get('pavingRects', []):
        public_paving |= (gx>=r['x0']) & (gx<=r['x1']) & (gy>=r['y0']) & (gy<=r['y1'])
    out[values['plaza']] = (town if terrain.get('townPaving',True) else public_paving)
    # 十字街口广场（2026-10-24）：交点处让位于 plaza，道路在广场处断开。
    cp = terrain.get('crossPlaza')
    cross_plaza = np.zeros_like(town, dtype=bool)
    if cp:
        cross_plaza = (np.abs(gx - cp['x']) < cp['halfX']) & (np.abs(gy - cp['y']) < cp['halfY'])
        out[values['plaza']] |= cross_plaza

    road = np.abs(gx - main_road_x(gy, terrain)) < main_road_half(gy, terrain)
    for cr in terrain['crossRoads']:
        road |= np.abs(gy - (cr['baseY'] + cr['amp'] * np.sin(gx / cr['period']))) < cr['half']
    for sr in terrain['sideRoads']:
        road |= np.abs(gx - (sr['baseX'] + sr['amp'] * np.sin(gy / sr['periodY'] + sr.get('phase', 0.0)))) \
                < sr['half']
    # 明朝巷网（2026-10-24）：带起止范围，不是满世界横穿。巷计入 road，
    # 于是按下方 MAT-07 的既有规则，镇内巷自动成 stone-road（青石），镇外段成 dirt-road。
    # 起点包含、终点排除；单侧边界独立生效，不推断或延长未连接的巷道。
    for al in terrain.get('alleys', []):
        half = al['half']
        if al.get('axis') == 'ns':
            band = np.abs(gx - al['x']) < half
            if 'y0' in al:
                band &= gy >= al['y0']
            if 'y1' in al:
                band &= gy < al['y1']
        else:
            band = np.abs(gy - al['y']) < half
            if 'x0' in al:
                band &= gx >= al['x0']
            if 'x1' in al:
                band &= gx < al['x1']
        road |= band
    # 十字街口广场：road 层让位，交点成 plaza。
    if cp:
        road &= ~cross_plaza
    # MAT-07：镇内的路不打土路标签（保留 plaza 硬地）——『镇里是土路』是地面不合理的一条；
    # 主街仍由下一行覆盖成 stone-road，于是：镇内主街石板 / 镇内横街支路是 plaza 硬地 / 镇外才走土路。
    out[values['dirt-road']] = road & ~out[values['bridge']] & ~town
    # MAT-07：主街 + **镇内的所有路**都算石板（镇里不该是土路）；镇外的路走 dirt-road。
    out[values['stone-road']] = ((np.abs(gx - main_road_x(gy, terrain)) < main_road_half(gy, terrain)) | (road & town)) & \
                               ~out[values['bridge']] & ~cross_plaza

    farm = np.zeros_like(road)
    for fb in terrain['farmland']:
        farm |= (gy > fb['y0']) & (gy < fb['y1']) & (gx > fb['x0']) & (gx < fb['x1'])
    out[values['farmland']] = farm

    fh = terrain['foothill']
    out[values['mountain-foot']] = (gy > fh['y0']) & (gy < fh['y1'])

    out[values['grass']] = np.ones_like(road, dtype=bool)
    out[values['void']] = np.zeros_like(road, dtype=bool)
    return out


def apply_terrain_zones(mask, zones, type_by_id, mask_scale, mask_size):
    for zone in zones:
        tid = zone.get('type')
        if tid not in type_by_id:
            raise SystemExit('terrainZones 含未知类型：' + str(tid))
        fill_rect(mask, world_rect_to_mask_rect(zone['rect'], mask_scale, mask_size),
                  type_by_id[tid]['value'])
    return mask


def apply_blocked_as_mountains(mask, blocked, type_by_id, mask_scale, mask_size):
    """把 blocked 映射为山体/水（blocked 项带 why 字段时按其语义）。"""
    for item in blocked:
        why = item.get('type') or item.get('why') or ''
        tid = 'water' if 'river' in str(why) or 'water' in str(why) else 'mountain'
        rect = {'x': item['x'], 'y': item['y'], 'w': item['width'], 'h': item['height']}
        fill_rect(mask, world_rect_to_mask_rect(rect, mask_scale, mask_size), type_by_id[tid]['value'])
    return mask


def apply_corridors_as_roads(mask, corridors, type_by_id, mask_scale, mask_size):
    for item in corridors or []:
        tid = item.get('type') or 'dirt-road'
        if tid not in type_by_id:
            tid = 'dirt-road'
        rect = item.get('rect', item)
        fill_rect(mask, world_rect_to_mask_rect(
            {'x': rect['x'], 'y': rect['y'], 'w': rect.get('w', rect.get('width', 0)),
             'h': rect.get('h', rect.get('height', 0))}, mask_scale, mask_size),
            type_by_id[tid]['value'])
    return mask


# 草地类型值：过渡带只允许写草地像素。由 main() 按 terrain-types.json 设置，
# 不在函数里假定具体数字。
GRASS_VALUE = [None]


def add_transition_band(mask, source_values, target_value, radius_world, mask_scale):
    """扩张源地形，减去源本身，只写入当前为草地的像素（不覆盖更重要的地形）。

    source_values 只放"被扩张的源地形"值；草地值取全局 GRASS_VALUE。
    （早期版本把草地混进 source_values 当 key，等于把草地也当源，扩张后与源相减
      再与草地相交恒为空集——过渡带全部为 0。）
    """
    radius_px = max(1, int(round(radius_world / mask_scale)))
    src = np.isin(mask, list(source_values))
    grown = binary_dilate(src, radius_px) & ~src
    mask[grown & (mask == GRASS_VALUE[0])] = target_value
    return mask


def colorize_debug(mask, terrain_types):
    size = mask.shape[0]
    out = np.zeros((size, size, 3), dtype=np.uint8)
    for t in terrain_types['types']:
        color = t['debugColor'].lstrip('#')
        rgb = tuple(int(color[i:i + 2], 16) for i in (0, 2, 4))
        out[mask == t['value']] = rgb
    return Image.fromarray(out, 'RGB')


# ---------------------------------------------------------------- main
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--out-dir', help='隔离输出 world-mask.png 与 debug 图，不覆盖正式 mask')
    args = ap.parse_args()
    types_cfg, type_by_id = load_terrain_types(os.path.join(CFG_DIR, 'terrain-types.json'))
    cfg_path, config = find_world_config()
    compose_path = os.path.join(CFG_DIR, 'world-compose.json')
    compose = load_json(compose_path) if os.path.exists(compose_path) else {}

    world_w, world_h = get_world_size(config, types_cfg)
    if world_w != world_h:
        raise SystemExit('本实现假设正方形世界：%d x %d' % (world_w, world_h))
    mask_scale = int(types_cfg['maskScale'])
    mask_size, _ = get_mask_size((world_w, world_h), mask_scale)
    values = {t['id']: t['value'] for t in types_cfg['types']}

    print('world config: %s' % os.path.basename(cfg_path))
    print('world %dx%d  chunk %d  mask %dx%d  (maskScale=%d)'
          % (world_w, world_h, int(config.get('chunkSize', 0)), mask_size, mask_size, mask_scale))

    # 1) 基底：草地（不是 void —— 世界大部分必须可走）
    mask = np.full((mask_size, mask_size), values['grass'], dtype=np.uint8)

    # 2) 显式 terrainZones（若配置提供）
    zones = config.get('terrainZones') or []
    if zones:
        apply_terrain_zones(mask, zones, type_by_id, mask_scale, mask_size)
        print('terrainZones applied: %d' % len(zones))
    else:
        # 3) 由 terrain 形状参数求值（唯一真源：make-world-greybox.py 导出的参数）
        terrain = config.get('terrainShape') or config.get('terrain')   # 兼容旧键名
        if not terrain:
            raise SystemExit('配置缺少 terrainZones 与 terrain 形状参数，无法生成完整 mask')
        masks = build_type_map(world_w, terrain, values, mask_scale)
        # 覆盖顺序 = 任务书 §7.2 的优先级（低 → 高）：
        #   grass < 过渡带 < farmland/plaza/road < mountain-path/mountain < water < bridge
        # bridge 必须最后画，否则水会盖掉桥面（曾经这样，桥在 mask 里根本不存在）。
        # MAT-07：road 系提到 plaza 之上 —— 节点铺装与镇界都是"面"，
        # 若 plaza 后画就会把穿过它的街盖掉，于是"街中心点"判据会读到 plaza 而非路面。
        # 旱岸先铺底，路面覆盖河岸；水、山体和桥仍保持高优先级。
        for tid in ('grass', 'mountain-foot', 'farmland', 'plaza', 'riverbank', 'dirt-road',
                    'stone-road', 'mountain-path', 'water', 'mountain', 'bridge'):
            mask[masks[values[tid]]] = values[tid]
        for region in config.get('elevation',{}).get('regions',[]):
            if region['shape']=='ramp' or region['id']=='ferry-landing':
                x0,y0,x1,y1=region['rect']
                fill_rect(mask,world_rect_to_mask_rect(dict(x=x0,y=y0,w=x1-x0,h=y1-y0),mask_scale,mask.shape[0]),values['stone-road'])
        print('terrain shape params applied (types=%d)' % len(masks))

        # 4) 过渡带（只写草地像素）
        GRASS_VALUE[0] = values['grass']
        edge = compose.get('edgeDilate', {})
        mask = add_transition_band(mask, [values['water']], values['riverbank'],
                                   edge.get('waterToRiverbank', 14), mask_scale)
        mask = add_transition_band(mask,
                                   [values['dirt-road'], values['stone-road'], values['mountain-path']],
                                   values['road-edge'], edge.get('roadToRoadEdge', 8), mask_scale)
        mask = add_transition_band(mask, [values['mountain']], values['mountain-foot'],
                                   edge.get('mountainToMountainFoot', 18), mask_scale)
        print('transition bands added')

    if args.out_dir:
        out_mask = os.path.join(wp.resolve_repo_path(args.out_dir, allow_missing=True), 'world-mask.png')
    else:
        out_mask = resolve_path(compose.get('maskPath', 'GameClient/TwelveJade/Assets/ArtSource/world/masks/world-mask.png'),
                                allow_missing=True)
    wp.assert_canonical_output_path(out_mask)
    os.makedirs(os.path.dirname(out_mask), exist_ok=True)
    Image.fromarray(mask, 'L').save(out_mask)              # 单通道，无 alpha
    debug_path = os.path.join(os.path.dirname(out_mask), 'world-mask-debug.png')
    colorize_debug(mask, types_cfg).save(debug_path)
    print('mask written: %s  %dx%d' % (out_mask, mask_size, mask_size))
    print('debug written: %s' % debug_path)

    counts = {t['id']: int((mask == t['value']).sum()) for t in types_cfg['types']}
    total = mask_size * mask_size
    print('type ratios: ' + '  '.join('%s=%.2f%%' % (k, v * 100.0 / total) for k, v in counts.items()))
    return 0


if __name__ == '__main__':
    sys.exit(main())
