# -*- coding: utf-8 -*-
"""青石镇大世界 · 第一阶段灰盒：生成灰盒分块图 + 世界坐标配置。

这是**工程验证用的灰盒**，不是最终美术（第二阶段由 Grok 母图替换 Art 目录下的同名
分块即可，配置无需改动）。之所以仍然由脚本产出地块与坐标，是为了保证"图与坐标
同源"——灰盒阶段只验证空间结构与跑图，画质故意做成区分度高的色块。

世界区域（世界单位）：
    后山(不可通行) ─ 山道 ─ 山脚 ─ 河(不可通行，桥可过) ─ 北岸田 ─
    集市 ─ 镇中心 ─ 主街 ─ 南门 ─ 镇外官道
东西向另有西街/东街，东南农田，可继续向南/向东扩张。
"""
import json, math, os, argparse
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

# Windows GBK 控制台下 print 中文/箭头会 UnicodeEncodeError 把整道门禁崩掉——
# 一个会崩的门禁比一个红的门禁危险得多（架构师 2026-10-05 定规，全仓 Tools/*.py 入口统一）。
import sys
for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, 'reconfigure'):
        _stream.reconfigure(encoding='utf-8', errors='replace')

CHUNKS = 8
CHUNK_PX = 1024
WORLD = CHUNKS * CHUNK_PX
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
from world_geometry import main_road_half
QINGSHI_SPEC = json.load(open(os.path.join(ROOT, 'Tools', 'qingshi-map', 'layout-spec.json'), encoding='utf-8'))
OUT_GREY = os.path.join(ROOT, 'GameClient', 'TwelveJade', 'Assets', 'Resources', 'WorldGreybox')
OUT_CFG = os.path.join(ROOT, 'GameClient', 'TwelveJade', 'Assets', 'Resources', 'Config',
                        'town-world-gen.json')

# ---- 灰盒配色（高区分度、便于肉眼核对区域与不可通行边界）----
K = {
    'grass':   (150, 178, 122),
    'grass2':  (138, 166, 112),
    'field':   (198, 178, 112),
    'hill':    (168, 158, 118),
    'mountain':(112, 118, 126),
    'rock':    (138, 142, 148),
    'water':   (108, 150, 178),
    'bank':    (206, 196, 156),
    'road':    (198, 178, 142),
    'stone':   (188, 184, 176),
    'plaza':   (206, 200, 186),
    'town':    (196, 190, 176),
    'trail':   (188, 172, 140),
}

# ---- 世界要素（全部为 x/y 的确定性函数 → 跨块天然连续）----
MOUNTAIN_Y = 1400.0        # 山体南缘基准（带波动）
RIVER_Y = 2700.0           # 河中心基准
MAIN_X = 4096.0            # 主街中轴
ROAD_HALF = 250.0          # 主街半宽 70->250(总宽 5 米)：MAT-07 街道加宽，1.4 米放不下第二排铺面是"荒野感"的物理根因
PLAZA = (4096.0, 4700.0, 620.0)     # 镇中心 cx,cy,r
# 十字街口广场（2026-10-24 法则 2 收尾）：主街×东西大街交于镇中心，交点扩成十字口广场。
# 落在既有 plaza 圆内（对角线 sqrt(400²+380²)=552 < 620），不越出 plaza 边界逻辑。
CROSS_PLAZA = (4096.0, 4700.0, 400.0, 380.0)  # cx, cy, halfX, halfY
MARKET = (3100.0, 4950.0, 340.0)    # 集市 cx,cy,r
TOWN_R = 1600.0            # 镇区（可走硬地）半径参考（2026-10-14 1100->1600：覆盖北街-镇心-集市-南门整段街市）
# ---- MAT-07：街道/桥/山道的半宽统一在这里定义（绘制与 terrainShape 导出**同源**）----
# 教训：此前绘制段把这些数硬编码（60/55/85），改 terrainShape 时只改了导出、没改绘制，
# 结果"配置说街宽 5 米、blocked 和灰盒图还是 1.1 米"。数字只能有一处。
CROSS_HALF = 200.0       # 横街（原 60）
# 2026-10-24 方案 A：4300 降为次街（东西门大街，half=100）。
# 根因：4300 与 4700 两条横街各 half=200，中线间距正好 400 -> 路面在 y=4500
# 严丝合缝相接、间隙 0，y∈[4100,4900] 整条 800 高带子 100% 是路面，
# 三个街坊（blk-north-west / blk-school / blk-north-east）几何上不可能放建筑。
# 4300 是东西门大街（次），4700 是镇心十字横臂（主，保持 200）。
CROSS_HALF_BY_Y = {4300.0: 100.0}


def cross_half(y):
    return CROSS_HALF_BY_Y.get(float(y), CROSS_HALF)
SIDE_HALF = 150.0        # 支路（原 55）
MOUNT_PATH_HALF = 120.0  # 山道（原 55）
# 山道只存在于山区（后山 → 山脚），必须在山脚南缘截止。
# 2026-10-24 架构师修：此前山道是一条**无 y 限制的竖直带**，一路捅到世界南缘，
# 且写入优先级高于 stone-road，**把镇子从中间劈成两半**（路网 7 个连通块、最大块只占 59%，
# 西门与三条巷成孤立小岛）。改动前的掩码即如此，属存量缺陷。
MOUNT_PATH_Y1 = 2500.0   # 山脚南缘（与 terrainShape.foothill.y1 一致）
BRIDGE_HALF = 120.0      # 桥面（原 85）
FERRY_HALF = 90.0        # 渡口（原 55）

# ---- MAT-07：节点铺装场地（南门/山门/桥头/渡口）。镇界是圆，节点在圆外时脚下会是草地，
# 而"牌坊立在草地里"正是斐哥截图里最刺眼的一处。给节点各铺一块硬地。----
PAVED_NODES = [
    (4236.0, 6280.0, 520.0),   # 南门牌坊前
    (4400.0, 3280.0, 420.0),   # 山门前
    (4236.0, 2560.0, 380.0),   # 双桥桥头
    (5100.0, 2820.0, 320.0),   # 渡口
]

CROSS_Y = [4300.0, 3500.0, 6000.0, 4700.0]
WEST_X, EAST_X = 2400.0, 5800.0
BRIDGE_X = [4236.0, 2400.0, 5800.0]  # 主桥 + 两便桥

# ---- 明朝乡镇布局（2026-10-24 架构师落地）：巷网 ----
# 里坊制遗意：主街两侧前店后宅，巷深入分宅。巷比街窄，**必须限定在镇内**——
# 既有 crossRoads/sideRoads 是无范围的满世界横穿（世界地理），巷不是。
# 每条巷带 axis/中心/半宽/起止，绘制与 terrainShape 导出共用这一份定义（数字只能有一处）。
ALLEY_HALF = 100.0
ALLEYS = [
    {'id': 'alley-upper-west', 'axis': 'ns', 'x': 3100.0, 'half': ALLEY_HALF, 'y0': 2900.0, 'y1': 4260.0},
    {'id': 'alley-upper-east', 'axis': 'ns', 'x': 4900.0, 'half': ALLEY_HALF, 'y0': 2900.0, 'y1': 4260.0},
    {'id': 'alley-plaza-south', 'axis': 'ew', 'y': 5350.0, 'half': ALLEY_HALF, 'x0': 2900.0, 'x1': 4600.0},
    {'id': 'alley-south-west', 'axis': 'ns', 'x': 3400.0, 'half': ALLEY_HALF, 'y0': 5350.0, 'y1': 6200.0},
    {'id': 'alley-south-east', 'axis': 'ns', 'x': 4700.0, 'half': ALLEY_HALF, 'y0': 5350.0, 'y1': 6200.0},
    {'id': 'alley-shuikou', 'axis': 'ns', 'x': 5500.0, 'half': ALLEY_HALF, 'y0': 2900.0, 'y1': 3500.0},
]
# 主桥 4096->4236：MAP-05 桥位对齐主街的决策；MAP-18 已裁定"运行时配置(town-map.json)的 4236 是对的、
# world-mask 资产过期"，但真源脚本当时没跟上（只重烘了 mask）。2026-10-14 补齐真源，三方（脚本/导出/json/mask）一致。
FERRY_X = 5100.0
SOUTH_GATE_Y = 6300.0
SPAWN = (MAIN_X, 7900.0)   # 世界南缘的镇外：到后山 7700 单位，奔跑 ~16 秒、步行 ~30 秒   # 南门外的镇外官道：离北侧后山 ~5900 单位，奔跑约 13 秒

# Data overrides are shared by the preview and exported geometry.
_qt = QINGSHI_SPEC['terrainOverrides']
PLAZA = tuple(_qt['plaza'][k] for k in ('x','y','r'))
MARKET = tuple(_qt['market'][k] for k in ('x','y','r'))
CROSS_PLAZA = tuple(_qt['crossPlaza'][k] for k in ('x','y','halfX','halfY'))
CROSS_HALF_BY_Y = {r['baseY']:r['half'] for r in _qt['crossRoads']}
PAVED_NODES = [tuple(r[k] for k in ('x','y','r')) for r in _qt['pavedNodes']]

def mtn_edge(x):
    return MOUNTAIN_Y + 150 * np.sin(np.asarray(x) / 1200.0 + .3) + 60 * np.sin(np.asarray(x) / 470.0)

def river_center(x):
    return RIVER_Y + 170 * np.sin(np.asarray(x) / 980.0) + 80 * np.sin(np.asarray(x) / 430.0 + 1.1)

def main_x(y):
    return MAIN_X + 140 * np.sin(np.asarray(y) / 1500.0)

CODES = ['grass', 'grass2', 'field', 'hill', 'mountain', 'rock', 'water', 'bank',
         'road', 'stone', 'plaza', 'town', 'trail']
LUT = np.array([K[c] for c in CODES], dtype=np.float32)

def terrain_parameters():
    terrain = {
        'version': 1,
        'comment': ('由 make-world-greybox.py 导出的地形形状参数。make-world-mask.py 按此表'
                    '求值生成语义 mask；坐标全部来自本文件，脚本内不得再写死。'
                    '所有函数为 world units，y 轴向下增大。'),
        'mountain': {'baseY': MOUNTAIN_Y,
                     'wave': [{'amp': 150, 'period': 1200, 'phase': .3},
                              {'amp': 60, 'period': 470, 'phase': .0}]},
        'river': {'baseY': RIVER_Y,
                  'wave': [{'amp': 170, 'period': 980, 'phase': .0},
                           {'amp': 80, 'period': 430, 'phase': 1.1}],
                  'waterHalf': 125, 'bankHalf': 185},
        'mainRoad': {'baseX': MAIN_X, 'amp': 140, 'periodY': 1500, 'half': ROAD_HALF},
        'crossRoads': [{'baseY': y, 'amp': 45, 'period': 800, 'half': cross_half(y)} for y in CROSS_Y],
        'sideRoads': [{'baseX': WEST_X, 'amp': 40, 'periodY': 900, 'half': SIDE_HALF, 'phase': .0},
                      {'baseX': EAST_X, 'amp': 40, 'periodY': 900, 'half': SIDE_HALF, 'phase': 1.0}],
        # 明朝巷网（2026-10-24）：带起止范围，下游 make-world-mask.py 按 axis 求值。
        # 不加 span 的旧 crossRoads/sideRoads 保持满世界横穿（世界地理），巷则限定镇内。
        'alleys': [dict(al) for al in ALLEYS],
        'bridges': [{'x': bx, 'half': BRIDGE_HALF} for bx in BRIDGE_X],
        'ferry': {'x': FERRY_X, 'half': FERRY_HALF},
        'plaza': {'x': PLAZA[0], 'y': PLAZA[1], 'r': PLAZA[2]},
        'crossPlaza': {'x': CROSS_PLAZA[0], 'y': CROSS_PLAZA[1],
                       'halfX': CROSS_PLAZA[2], 'halfY': CROSS_PLAZA[3]},
        'market': {'x': MARKET[0], 'y': MARKET[1], 'r': MARKET[2]},
        'townRadius': TOWN_R,
        'mountainPath': {'baseX': MAIN_X - 300, 'amp': 60, 'periodY': 600, 'half': MOUNT_PATH_HALF,
                         'y0': 0.0, 'y1': MOUNT_PATH_Y1},
        'pavedNodes': [{'x': nx, 'y': ny, 'r': nr} for nx, ny, nr in PAVED_NODES],
        'foothill': {'y0': 1600, 'y1': 2500},
        'farmland': [{'y0': 2900, 'y1': 4250, 'x0': 0, 'x1': 3200},
                     {'y0': 2900, 'y1': 4250, 'x0': 5000, 'x1': WORLD},
                     {'y0': 5500, 'y1': 6600, 'x0': 0, 'x1': 3200},
                     {'y0': 5500, 'y1': 6600, 'x0': 5000, 'x1': WORLD}],
    }
    terrain.update(QINGSHI_SPEC['terrainOverrides'])
    return terrain


def render(x0,y0,w,h,scale=1.0):
    """灰盒与正式 mask 共用几何求值，不再维护第二套铺装和道路公式。"""
    import importlib.util
    spec=importlib.util.spec_from_file_location('world_mask',os.path.join(ROOT,'Tools','make-world-mask.py'))
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    with open(os.path.join(ROOT,'GameClient','TwelveJade','Assets','Resources','Config','terrain-types.json'),encoding='utf-8') as f:types=json.load(f)
    values={t['id']:t['value'] for t in types['types']}
    masks=module.build_type_map(WORLD,terrain_parameters(),values,scale,origin=(x0,y0),output_size=(w,h))
    mask=np.zeros((h,w),dtype=np.uint8)
    for name in ('grass','mountain-foot','farmland','plaza','dirt-road','stone-road','mountain-path','riverbank','water','mountain','bridge'):
        mask[masks[values[name]]]=values[name]
    return module.colorize_debug(mask,types)


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--config-only',action='store_true')
    args=parser.parse_args()
    os.makedirs(OUT_GREY, exist_ok=True)
    if not args.config_only:
        for r in range(CHUNKS):
            for c in range(CHUNKS):
                render(c * CHUNK_PX, r * CHUNK_PX, CHUNK_PX, CHUNK_PX).save(
                    os.path.join(OUT_GREY, f'chunk-r{r}-c{c}.png'))
        print('greybox chunks done', flush=True)
        ov = render(0, 0, WORLD // 5, WORLD // 5, scale=5)
        ov.save(os.path.join(OUT_GREY, 'world-overview.png'))
        print('overview done', flush=True)

    # ---- 不可通行区（山/河），与图同源 ----
    blocked = []
    for gy in range(0, 1300, 32):
        for gx in range(0, WORLD, 32):
            xm, ym = gx + 16, gy + 16
            if ym < float(mtn_edge(xm)):
                trail = abs(xm - (MAIN_X - 300 - 60 * math.sin(ym / 600.0))) < 50
                if not trail:
                    blocked.append({'x': gx, 'y': gy, 'width': 32, 'height': 32, 'why': 'mountain'})
    # Memo chapter 7: collision uses the same river and bridge widths as the visible terrain.
    # Preserve exact bridge x boundaries instead of the former hardcoded 90-unit half opening.
    geometry = terrain_parameters()
    water_half = geometry['river']['waterHalf']
    for gx in range(0, WORLD, 16):
        spans = [(float(gx), float(min(gx+16,WORLD)))]
        for bridge in geometry['bridges']:
            low,high=bridge['x']-bridge['half'],bridge['x']+bridge['half']
            remaining=[]
            for left,right in spans:
                if right<=low or left>=high:remaining.append((left,right))
                else:
                    if left<low:remaining.append((left,low))
                    if right>high:remaining.append((high,right))
            spans=remaining
        for left,right in spans:
            xm=(left+right)/2
            cyv=float(river_center(xm));top=max(0,cyv-water_half);bot=min(WORLD,cyv+water_half)
            for gy in np.arange(top,bot,16):
                if abs(xm-geometry['ferry']['x'])<geometry['ferry']['half'] and gy>=float(river_center(geometry['ferry']['x']))+50:continue
                blocked.append({'x':round(left,4),'y':round(float(gy),4),'width':round(right-left,4),
                                'height':round(min(16,bot-float(gy)),4),'why':'river'})

    chunks = [{'id': f'r{r}-c{c}', 'row': r, 'col': c,
               'art': f'WorldGreybox/chunk-r{r}-c{c}',
               'overview': f'WorldGreybox/world-overview',
               'x': c * CHUNK_PX, 'y': r * CHUNK_PX, 'width': CHUNK_PX, 'height': CHUNK_PX}
              for r in range(CHUNKS) for c in range(CHUNKS)]
    landmarks = [
        {'id': 'south-gate', 'name': '南门', 'x': 3974, 'y': 6300},
        {'id': 'town-center', 'name': '镇中心', 'x': 4096, 'y': 4700},
        {'id': 'market', 'name': '集市', 'x': 3100, 'y': 4950},
        {'id': 'north-street', 'name': '北街', 'x': 4096, 'y': 3600},
        {'id': 'river-north', 'name': '北桥', 'x': BRIDGE_X[0], 'y': int(river_center(BRIDGE_X[0]))},
        {'id': 'ferry', 'name': '渡口', 'x': FERRY_X, 'y': int(river_center(FERRY_X)) + 240},
        {'id': 'foothill', 'name': '山脚', 'x': 4096, 'y': 2100},
        # T065：原锚 (3800,300) 实测离最近山体只有 8px 余距，以它为圆心画 64px 探针盘会压住 3 块
        # 不可通行 tile（地标自己"站不住"的任何消费者都要额外退让）。沿山脊凹口西移到 (3760,*)：
        # 那里余距 48px，32px 半径的盘完全落在可行走地上。台账当年猜的"沿山脚南移 y≈340-380"
        # 实测是反的——x=3800 这一列从 y≈680 起就整段进山体（余距 0），南移要一路到 y≈1380 才出得去。
        {'id': 'back-mountain', 'name': '后山', 'x': 3760, 'y': 260},
        {'id': 'east-street', 'name': '东街', 'x': EAST_X, 'y': 4300},
        {'id': 'west-street', 'name': '西街', 'x': WEST_X, 'y': 4200},
        # ---- 明朝布局落地（2026-10-24）：四门 / 中轴 / 庙宇 / 水口 ----
        # 法则 7「寨墙与四门」：南门已有，这里补齐东/西/北门，四门齐。
        # 北门坐标不能压在山门地块（plot-temple-gate，foot 4400,3060）上——
        # check-plot-overlap 要求地标↔地块 foot 净距 ≥60；取主街上、山门南侧 317 px 处。
        {'id': 'east-gate', 'name': '东门', 'x': int(EAST_X), 'y': 4337},
        {'id': 'west-gate', 'name': '西门', 'x': int(WEST_X), 'y': 4306},
        {'id': 'north-gate', 'name': '北门', 'x': 4217, 'y': 3150},
        # 法则 3「中轴定尊卑」：衙署居尊、文昌阁分列中轴两侧；鼓楼为镇中心视线锚点。
        {'id': 'yamen', 'name': '青石县衙', 'x': 3320, 'y': 3100},
        {'id': 'wenchang', 'name': '文昌阁', 'x': 5200, 'y': 3100},
        {'id': 'drum-tower', 'name': '鼓楼', 'x': 4230, 'y': 4640},
        {'id': 'ancestral-hall', 'name': '张氏祠堂', 'x': 3550, 'y': 4100},
        # 法则 5「庙宇临街口」：四庙——城隍/关帝/土地/娘娘。
        {'id': 'citygods-temple', 'name': '城隍庙', 'x': 5250, 'y': 5650},
        {'id': 'guandi-temple', 'name': '关帝庙', 'x': 5600, 'y': 4100},
        {'id': 'tudi-temple', 'name': '土地庙', 'x': 4650, 'y': 6300},
        {'id': 'niangniang-temple', 'name': '娘娘庙', 'x': 2850, 'y': 6300},
        # 法则 6「水口在镇口」：河东南折处；与南门牌坊构成南北双门。
        # y 不能落在水面/河岸 blocking 上：check-world-mask 的 anchor clearance 用 PROBE_R=32
        # 的圆盘查 `blocked` 单元；x≈5568 处 river blocked 最高到 y=2902，故取 2960（已实测清空）。
        {'id': 'shuikou', 'name': '水口', 'x': 5568, 'y': 2960},
        {'id': 'farmland', 'name': '南郊农田', 'x': 2400, 'y': 5900},
        {'id': 'outskirts', 'name': '镇外', 'x': 4096, 'y': 7900},
        # 双桥：主桥所在的过河点。npc-schedules.json 的黑市窗口（bridges-night）以
        # place="bridges" 引用它，ScheduleTable 对表要求这个地标真实存在。
        # 地标属"世界布局派生项"，只能在生成器里定义、经 sync 派生进 town-map.json
        # （ADR-017：不准手改 town-map.json 的 landmarks）。
        {'id': 'bridges', 'name': '双桥', 'x': BRIDGE_X[0], 'y': 2560},
    ]
    # 地形形状参数：世界布局的解析式描述。本文件是唯一真源（灰盒图、blocked、mask 都由它
    # 派生），下游脚本只读配置参数求值，不在别处重复写坐标。
    terrain = terrain_parameters()
    for mark in landmarks:
        if mark['id'] in QINGSHI_SPEC['landmarkOverrides']:
            mark['x'], mark['y'] = QINGSHI_SPEC['landmarkOverrides'][mark['id']]
    json.dump({'worldWidth': WORLD, 'worldHeight': WORLD, 'chunkSize': CHUNK_PX,
               'chunks': chunks, 'blocked': blocked, 'landmarks': landmarks,
               'spawn': {'x': SPAWN[0], 'y': SPAWN[1]},
               # 键名 terrainShape 而非 terrain：TownMap.cs 用 Elements(root, "terrain")
               # 读取地形**分区数组**，配置里再放一个叫 terrain 的对象会让运行时抛
               # "字段 terrain 应是数组"。形状参数改名，运行时解析不受影响。
               'terrainShape': terrain, 'elevation': QINGSHI_SPEC['elevation']},
              open(OUT_CFG, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    print('config written, blocked:', len(blocked))

if __name__ == '__main__':
    main()
