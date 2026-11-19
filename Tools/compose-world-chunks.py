# -*- coding: utf-8 -*-
"""C0-D 4.4：按 world-mask.png 权重混合正式材质，合成中心 2×2 地形块（预检）。

只合成 chunk_3_3 / chunk_4_3 / chunk_3_4 / chunk_4_4（镇中心），输出到
ArtSource/world/chunks-preview/（不写 Unity Resources 正式目录）。

合成规则（任务书 §20）：
  - mask（2048²，1px = 4 world units）决定每像素的地形类型权重（高斯软化）；
  - 各地形类型的材质按世界坐标确定性采样（UV = 世界坐标 % 材质周期；原生像素只允许缩小采样）；
  - 权重归一后加权混合；overlap=128 保证跨块一致（噪声/采样均由世界坐标决定）；
  - 碎石/细砂/苔草三层自然边缘；裁掉 overlap 只保留中心 1024²。

缺材质立即失败；禁止使用色块或程序噪声掩盖素材缺失。
"""
import argparse, hashlib, json, os, sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageFilter
from scipy.ndimage import distance_transform_edt

# Windows GBK 控制台下 print 中文/箭头会 UnicodeEncodeError 把整道门禁崩掉——
# 一个会崩的门禁比一个红的门禁危险得多（架构师 2026-10-05 定规，全仓 Tools/*.py 入口统一）。
import sys
for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, 'reconfigure'):
        _stream.reconfigure(encoding='utf-8', errors='replace')

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CFG = os.path.join(ROOT, 'GameClient', 'TwelveJade', 'Assets', 'Resources', 'Config')
# T001 教训：mask 正式位置在 Unity 工程 ArtSource（与 make-world-mask 输出同源），
# 不读仓库根旧树（已删除，存在过会导致读到残缺/错版本）。
sys.path.insert(0, os.path.join(ROOT, 'Tools'))
import world_paths as wp
from world_elevation_art import decorate_terrain
import world_river_surface as river_surface
import world_build_manifest as provenance
MASK = wp.mask_path('world-mask.png')
MAT_DIR = os.path.join(ROOT, 'GameClient', 'TwelveJade', 'Assets', 'ArtSource', 'world', 'materials')
OUT_DIR = None  # 由 main() 从 world-compose.json 的 outputDir 解析（Gate C1-B-1）

CENTER = [(3, 3), (4, 3), (3, 4), (4, 4)]


def load_cfg():
    types = json.load(open(os.path.join(CFG, 'terrain-types.json'), encoding='utf-8'))
    compose = json.load(open(os.path.join(CFG, 'world-compose.json'), encoding='utf-8'))
    return types, compose


def make_overview(out_dir, columns, rows, size=128):
    """64 块各缩到 size×size，拼成 columns×rows 总览图（Gate C1-B-1 交付物）。"""
    thumb = Image.new('RGB', (columns * size, rows * size))
    for cy in range(rows):
        for cx in range(columns):
            p = os.path.join(out_dir, 'chunk_%d_%d.png' % (cx, cy))
            if not os.path.exists(p):
                continue
            t = Image.open(p).convert('RGB').resize((size, size), Image.BILINEAR)
            thumb.paste(t, (cx * size, cy * size))
    path = os.path.join(out_dir, 'world-overview-thumb.png')
    thumb.save(path)
    print('overview thumb -> %s (%dx%d)' % (os.path.relpath(path, ROOT).replace('\\', '/'),
                                           thumb.width, thumb.height))
    return path



PAVED_TYPES = frozenset(('stone-road', 'plaza', 'bridge'))


def normalize_paving_weights(weights, coverage, paved_values):
    """按像素归一；铺装合计不低于未模糊覆盖，组内比例及外侧羽化保留。"""
    total = sum(weights.values())
    if not paved_values:
        for weight in weights.values():
            weight /= np.maximum(total, 1e-6)
        return
    paved = sum(weights[v] for v in paved_values)
    target = np.clip(np.maximum(coverage, paved / np.maximum(total, 1e-6)), 0, 1)
    if np.any((paved == 0) & (coverage > 0)):
        raise ValueError('Blur removed all paving support; reduce blurRadiusPixels')
    paved_factor = np.divide(target, paved, out=np.zeros_like(paved), where=paved > 0)
    other = np.maximum(total - paved, 0)
    other_factor = np.divide(1 - target, other, out=np.zeros_like(other), where=other > 0)
    for value, weight in weights.items():
        weight *= paved_factor if value in paved_values else other_factor


def build_weight_fields(mask, terrain_types, world_size, blur, verbose=False, paving_core_inset=0):
    """全图统一 resize/blur；分行归一只减少临时内存，不改变采样网格。"""
    fields, used_types = {}, []
    paved_values = set()
    for terrain_type in terrain_types:
        value = terrain_type['value']
        binary = (mask == value).astype(np.uint8) * 255
        if not binary.any():
            continue
        up = Image.fromarray(binary, 'L').resize(world_size, Image.BILINEAR)
        fields[value] = np.asarray(up.filter(ImageFilter.GaussianBlur(blur)), dtype=np.float32) / 255.0
        used_types.append(terrain_type)
        if terrain_type['id'] in PAVED_TYPES:
            paved_values.add(value)
        if verbose:
            print('soft built: %s (%s)' % (terrain_type['id'], terrain_type['name']), flush=True)
    paved_mask = np.isin(mask, list(paved_values)).astype(np.uint8) * 255
    # Protect the paved INTERIOR, leaving the edge free for the approved natural blend.
    # The semantic/navigation mask itself remains untouched.
    core = Image.fromarray(paved_mask, 'L')
    if paving_core_inset > 0:
        scale = float(world_size[0]) / mask.shape[1]
        radius = int(np.ceil(float(paving_core_inset) / scale))
        core = core.filter(ImageFilter.MinFilter(2 * radius + 1))
    coverage = np.asarray(core.resize(world_size, Image.BILINEAR))
    for y in range(0, world_size[1], 256):
        rows = slice(y, y + 256)
        normalize_paving_weights({v: field[rows] for v, field in fields.items()},
                                 coverage[rows].astype(np.float32) / 255.0, paved_values)
    return fields, used_types


def build_road_verge_alpha(mask, terrain_types, world_size, settings):
    """A separate continuous alpha layer; preserve the road core and semantic mask.

    Compute at the global mask grid, then resize globally so chunk borders use the
    same samples. The ring is zero in the road interior and on disallowed terrain.
    """
    if not settings.get('enabled', False):
        return None
    values = {t['id']: t['value'] for t in terrain_types}
    source = (mask == values[settings['sourceType']]).astype(np.uint8) * 255
    scale = float(world_size[0]) / mask.shape[1]
    soft = np.asarray(Image.fromarray(source).filter(
        ImageFilter.GaussianBlur(float(settings['radiusWorld']) / scale)), dtype=np.float32) / 255.0
    allowed = np.isin(mask, [values[t] for t in settings['allowedTypes']])
    alpha = 4.0 * soft * (1.0 - soft) * float(settings['maxAlpha']) * allowed
    return np.asarray(Image.fromarray(alpha.astype(np.float32)).resize(
        world_size, Image.Resampling.BILINEAR), dtype=np.float32)


def load_native_material(name, compose):
    """Keep source resolution and minify at most once; enlargement is an error."""
    path = os.path.join(MAT_DIR, name)
    if not os.path.isfile(path):
        raise FileNotFoundError('Required photographed material is missing: ' + path)
    with Image.open(path) as original:
        image = original.convert('RGB')
        period = int((compose.get('materialSampling', {}).get(name) or {}).get('periodWorldUnits', image.width))
        if period < 1 or period > min(image.size):
            raise ValueError('Material enlargement is forbidden: ' + name + ' ' + str(image.size) + ' -> ' + str(period))
        if image.size != (period, period):
            image = image.resize((period, period), Image.Resampling.LANCZOS)
        return np.asarray(image, dtype=np.float32) * float(compose.get('materialBrightness', {}).get(name, 1))


def sample_material(material, x, y):
    return material[np.mod(y, material.shape[0]).astype(np.int32), np.mod(x, material.shape[1]).astype(np.int32)]


def build_organic_ground_distance(mask, terrain_types, world_size, settings):
    if not settings.get('enabled', False):
        return None
    values = {t['id']: t['value'] for t in terrain_types}
    paved = np.isin(mask, [values[t] for t in settings['sourceTypes']])
    scale = float(world_size[0]) / mask.shape[1]
    distance = (distance_transform_edt(~paved) - distance_transform_edt(paved)).astype(np.float32) * scale
    return np.asarray(Image.fromarray(distance).resize(world_size, Image.Resampling.BILINEAR), dtype=np.float32)


def capture_generation_input(mask_path):
    """候选绑定实际 mask；默认正式链仍使用原 provenance 契约。"""
    snapshot = provenance.snapshot()
    if Path(mask_path).resolve() != Path(MASK).resolve():
        inputs = snapshot['inputs']
        inputs['files'].pop(wp.rel_repo(MASK))
        inputs['files'][wp.rel_repo(mask_path)] = provenance.sha(mask_path)
        inputs['maskPath'] = wp.rel_repo(mask_path)
        encoded = json.dumps(inputs, sort_keys=True, ensure_ascii=False, separators=(',', ':')).encode('utf-8')
        snapshot['sha256'] = hashlib.sha256(encoded).hexdigest()
    return snapshot


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--chunks', nargs='*', default=[])
    ap.add_argument('--center-preview', action='store_true')
    ap.add_argument('--full-grid', action='store_true',
                    help='生成 8x8 全网格 64 块（Gate C1-B-1）')
    ap.add_argument('--out-dir', default=None, help='覆盖输出目录')
    ap.add_argument('--mask', help='读取隔离 mask；必须同时指定 Unity 工程外的 --out-dir，产物不可安装')
    ap.add_argument('--no-thumb', action='store_true', help='跳过总览图')
    args = ap.parse_args()

    if args.mask and not args.out_dir:
        ap.error('--mask requires an isolated --out-dir')
    mask_path = wp.resolve_repo_path(args.mask) if args.mask else MASK
    types, compose = load_cfg()
    out_dir = wp.resolve_repo_path(args.out_dir or compose['outputDir'], allow_missing=True)
    if args.mask and Path(out_dir).resolve().is_relative_to(Path(wp.unity_project_root()).resolve()):
        ap.error('--mask candidate output must be outside the Unity project')
    generation_input = capture_generation_input(mask_path)
    with open(os.path.join(CFG,'town-map.json'),encoding='utf-8') as f: world_config=json.load(f)
    values = {t['id']: t['value'] for t in types['types']}
    materials_by_value = {t['value']: t.get('material') for t in types['types']}
    debug_color = {t['value']: t['debugColor'].lstrip('#') for t in types['types']}
    chunk_size = int(compose['chunkSize'])
    overlap = int(compose['overlap'])
    blur = float(compose['blurRadiusPixels'])
    mask = np.asarray(Image.open(mask_path).convert('L'))
    mask_scale = world_w = None
    tt = json.load(open(os.path.join(CFG, 'terrain-types.json'), encoding='utf-8'))
    mask_scale = int(tt['maskScale'])
    world_w = int(tt['worldSize'])
    world_h = world_w

    columns = int(compose.get('columns', 8))
    rows = int(compose.get('rows', 8))
    wp.ensure_dir(out_dir)
    manifest_path=os.path.join(out_dir,'world-build-manifest.json')
    with open(manifest_path,'w',encoding='utf-8') as f:
        json.dump({'status':'incomplete','candidate':bool(args.mask),
                   'mask_input':{'file':wp.rel_repo(mask_path),'sha256':provenance.sha(mask_path)},
                   'generation_input':generation_input},f,ensure_ascii=False,indent=2)

    if args.full_grid:
        targets = [(cx, cy) for cy in range(rows) for cx in range(columns)]
    elif args.center_preview or not args.chunks:
        targets = CENTER
    else:
        targets = [tuple(int(v) for v in c.split(',')) for c in args.chunks]

    # ---- 权重场：全局统一（Gate C0-D 接缝修复的核心）----
    # 早期实现每个 chunk 独立"裁 mask → resize → blur"，BILINEAR 放大的采样网格在
    # 相邻 chunk 间不连续，接缝两侧的过渡权重不同 → 水平缝 mean 超 4.0。
    # 正确做法：权重场按**全图**算一次（blur 也在全图上），各 chunk 从同一连续场裁剪。
    full_soft, used_types = build_weight_fields(mask, types['types'], (world_w, world_h), blur, verbose=True, paving_core_inset=compose.get("pavingCoreInsetWorld", 0))

    material_cache = {t['material']: load_native_material(t['material'], compose) for t in used_types if t.get('material')}
    # 水按更细的八度采样：1024 周期的一张水面里墨团有 300–400 世界单位，正好铺满河道成"大墨块"。
    # 周期仍从配置派生（不另立真源），只在本模块换取样密度，不动灵活值与 PNG。
    water_layers = water_base = None
    water_name = next((t['material'] for t in used_types if t['id'] == 'water'), None)
    if water_name:
        period = float((compose.get('materialSampling', {}).get(water_name) or {}).get('periodWorldUnits', 1024))
        water_layers = river_surface.water_layers(material_cache[water_name], period)
        water_base = material_cache[water_name].reshape(-1, 3).mean(0)[None, None, :]
    organic_settings = compose.get('organicGroundTransition', {})
    organic_distance = build_organic_ground_distance(mask, types['types'], (world_w, world_h), organic_settings)
    breakup_material = None
    if organic_distance is not None:
        for layer in organic_settings['layers']:
            if layer['material'] not in material_cache:
                material_cache[layer['material']] = load_native_material(layer['material'], compose)
        breakup = material_cache[organic_settings['breakupMaterial']]
        breakup_image = Image.fromarray(np.uint8(np.clip(breakup, 0, 255))).convert('L')
        breakup_image = breakup_image.resize((512, 512), Image.Resampling.LANCZOS).filter(ImageFilter.GaussianBlur(2))
        breakup_material = np.asarray(breakup_image, dtype=np.float32)
        lo, hi = np.percentile(breakup_material, [8, 92])
        breakup_material = np.clip((breakup_material - lo) / max(1, hi - lo), 0, 1)

    # 按块组装
    hashes = []
    missing_materials = []
    for cx, cy in targets:
        wx0, wy0 = cx * chunk_size, cy * chunk_size
        rw = rh = chunk_size + overlap * 2
        rx0, ry0 = wx0 - overlap, wy0 - overlap

        canvas = np.zeros((rh, rw, 3), dtype=np.float32)
        weight_sum = np.zeros((rh, rw), dtype=np.float32)
        X = np.arange(rx0, rx0 + rw, dtype=np.float64)[None, :]
        Y = np.arange(ry0, ry0 + rh, dtype=np.float64)[:, None]
        Xb = np.broadcast_to(X, (rh, rw))
        Yb = np.broadcast_to(Y, (rh, rw))

        breakup = None
        if breakup_material is not None:
            breakup = breakup_material[np.mod(Yb, 512).astype(np.int32), np.mod(Xb, 512).astype(np.int32)]

        for t in used_types:
            v = t['value']
            soft = full_soft[v]
            # 从全图 soft 裁 overlap 窗口（世界坐标 → 输出像素 1:1）。
            # 边缘 chunk 的 rx0/ry0 为负（overlap 溢出世界边界），直接切片会落空，
            # 必须按偏移零填充——否则世界四角权重全 0，合成为纯黑。
            weight = np.zeros((rh, rw), dtype=np.float32)
            sx0, sy0 = max(0, rx0), max(0, ry0)
            sx1, sy1 = min(soft.shape[1], rx0 + rw), min(soft.shape[0], ry0 + rh)
            if sx1 > sx0 and sy1 > sy0:
                weight[sy0 - ry0:sy1 - ry0, sx0 - rx0:sx1 - rx0] = soft[sy0:sy1, sx0:sx1]

            mat_name = t.get('material')
            mat = material_cache[mat_name]
            if breakup is not None:
                # Photographed turf creates irregular transition coverage, never a procedural surface.
                blend = float(organic_settings.get('blendBreakupStrength', .45))
                edge_texture = breakup_material[np.mod(Yb + v * 71, 512).astype(np.int32), np.mod(Xb + v * 137, 512).astype(np.int32)]
                weight = np.clip(weight + (edge_texture - .5) * blend * 4 * weight * (1 - weight), 0, 1)
            if t['id'] == 'water' and water_layers is not None:
                sampled = river_surface.sample_water(water_layers, Xb, Yb, water_base)
            else:
                sampled = sample_material(mat, Xb, Yb)
            canvas += sampled * weight[:, :, None]
            weight_sum += weight

        canvas /= np.maximum(weight_sum, 1e-6)[:, :, None]
        if organic_distance is not None:
            signed = np.full((rh, rw), 10000, dtype=np.float32)
            sx0, sy0 = max(0, rx0), max(0, ry0)
            sx1, sy1 = min(world_w, rx0 + rw), min(world_h, ry0 + rh)
            if sx1 > sx0 and sy1 > sy0:
                signed[sy0-ry0:sy1-ry0, sx0-rx0:sx1-rx0] = organic_distance[sy0:sy1, sx0:sx1]
            signed += (breakup - .5) * float(organic_settings['edgeBreakupWorld'])
            mx = np.clip(np.floor(Xb / mask_scale).astype(np.int32), 0, mask.shape[1]-1)
            my = np.clip(np.floor(Yb / mask_scale).astype(np.int32), 0, mask.shape[0]-1)
            allowed = np.isin(mask[my, mx], [values[t] for t in organic_settings['allowedTypes']])
            # Grass/moss -> fine sand -> loose stone chips, overlapping feathered rings.
            # Every alpha sample uses absolute world coordinates, so chunk cuts cannot appear.
            for layer in organic_settings['layers']:
                start, peak, end = (float(layer[k]) for k in ('startWorld', 'peakWorld', 'endWorld'))
                enter = np.clip((signed-start) / (peak-start), 0, 1)
                leave = np.clip((end-signed) / (end-peak), 0, 1)
                alpha = (enter*enter*(3-2*enter)) * (leave*leave*(3-2*leave)) * float(layer['maxAlpha'])
                alpha *= (.65 + .35 * breakup) * allowed
                texture = sample_material(material_cache[layer['material']], Xb, Yb)
                canvas = canvas * (1-alpha[:,:,None]) + texture * alpha[:,:,None]
        # Surface detail comes from native material photographs; no sine paper grain or UV warp.
        canvas = decorate_terrain(canvas, Xb, Yb, world_config)
        canvas = np.clip(canvas, 0, 255).astype(np.uint8)
        chunk = canvas[overlap:overlap + chunk_size, overlap:overlap + chunk_size]

        out_path = os.path.join(out_dir, 'chunk_%d_%d.png' % (cx, cy))
        Image.fromarray(chunk, 'RGB').save(out_path)
        hashes.append((out_path, chunk.tobytes()))

    if missing_materials:
        for mm in missing_materials:
            print('  TEMP fallback:', mm)
    else:
        print('missingMaterials: [] (no TEMP fallback)')
    print('chunks written: %d -> %s' % (len(targets), wp.rel_repo(out_dir)))
    if args.full_grid and not args.no_thumb:
        make_overview(out_dir, columns, rows)
    if args.mask or (args.full_grid and not args.no_thumb):
        if capture_generation_input(mask_path)!=generation_input:
            raise RuntimeError('Source changed while composing; outputs are NOT certified')
        manifest={'status':'candidate' if args.mask else 'complete','generation_input':generation_input,
            'candidate':bool(args.mask),
            'mask_input':{'file':wp.rel_repo(mask_path),'sha256':provenance.sha(mask_path)},
            'chunks':[{'file':'chunk_%d_%d.png'%(x,y),'sha256':provenance.sha(os.path.join(out_dir,'chunk_%d_%d.png'%(x,y)))} for x,y in targets]}
        if args.mask:
            manifest['doNotInstall'] = True
        if args.full_grid and not args.no_thumb:
            manifest['overview'] = {'file':'world-overview-thumb.png','sha256':provenance.sha(os.path.join(out_dir,'world-overview-thumb.png'))}
        with open(manifest_path,'w',encoding='utf-8') as f:json.dump(manifest,f,ensure_ascii=False,indent=2)
        print('Generation source SHA256: '+generation_input['sha256'])
    return 0


if __name__ == '__main__':
    sys.exit(main())
