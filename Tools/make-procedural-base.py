# -*- coding: utf-8 -*-
"""Gate B-fix2 第二步：程序化平铺基底生成（任务书 §5）。

结构由程序控制，AI 只负责笔触。本脚本只产出"基底"，不含 overlay：
    Assets/ArtSource/world/materials/procedural/{name}.base.png   1024x1024 RGB 不透明
    Assets/ArtSource/world/materials/procedural/previews/{name}.base.png.2x2.png

硬约束（任务书 §4）：
  - 不用 random / np.random / time；全部噪声来自世界/像素坐标的确定性 hash 或 sin。
    同一输入重复运行必须逐像素一致。
  - 不硬编码 size / cellMin / cellMax / palette / opacity，全部读
    GameClient/TwelveJade/Assets/Resources/Config/world-material-gen.json。

生成器：
  turf-dirt                 草地/泥土基底（§5.2）
  torus-voronoi-flagstone   环面 Voronoi 石板路基底（§5.3）

用法：
    python Tools/make-procedural-base.py --names grass stone-road
    python Tools/make-procedural-base.py --all
"""
import argparse, io, json, math, os, sys
import numpy as np
from PIL import Image

# 健壮性（夜1 G-3，2026-10-06）：zh-CN Windows 的 GBK 控制台下 print 中文/箭头会
# UnicodeEncodeError 崩在出结论之前——**会崩的脚本比会红的脚本危险得多**。
# 故统一强制 stdout/stderr 为 UTF-8；先例见 SaveChecks.csproj 的 <CodePage>65001</CodePage>。
import sys
for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, 'reconfigure'):
        _stream.reconfigure(encoding='utf-8', errors='replace')

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, 'Tools'))
import world_paths as wp

CFG = os.path.join(wp.config_root(), 'world-material-gen.json')

# 接缝阈值与 check-world-materials.py 共用同一份配置（Gate C1-A §2.3-B）：
# world-material-gen.json.seamThresholds，缺失才回落到内置默认值并告警。
_DEFAULT_PASS_LIMITS = (12.0, 35.0, 90.0)
_DEFAULT_WARN_LIMITS = (18.0, 50.0, 120.0)


def _load_seam_limits():
    st = {}
    if os.path.exists(CFG):
        try:
            with io.open(CFG, encoding='utf-8') as fh:
                st = (json.load(fh) or {}).get('seamThresholds') or {}
        except Exception:
            st = {}

    def pick(block, fallback):
        src = st.get(block) or {}
        try:
            return (float(src['meanMax']), float(src['p95Max']), float(src['maxMax']))
        except (KeyError, TypeError, ValueError):
            return fallback

    if not st:
        sys.stderr.write(
            'WARNING: world-material-gen.json seamThresholds missing, using built-in defaults.\n')
    return pick('finalPass', _DEFAULT_PASS_LIMITS), pick('finalWarn', _DEFAULT_WARN_LIMITS)


PASS_LIMITS, WARN_LIMITS = _load_seam_limits()


# ---------------------------------------------------------------- 噪声原语 §5.1
def hash2d(x, y, period):
    """确定性 2D hash → [0,1)。period 为环绕周期（像素/格）。"""
    x = int(x) % int(period)
    y = int(y) % int(period)
    n = x * 374761393 + y * 668265263
    n = (n ^ (n >> 13)) * 1274126177
    n = n ^ (n >> 16)
    return (n & 0xFFFFFFFF) / 4294967295.0


def smooth(t):
    return t * t * (3.0 - 2.0 * t)


def periodic_value_noise(x, y, scale, period, amplitude=1.0):
    """周期性 value noise：period = size/scale 时天然环绕，无需后期接缝处理。

    向量化版本（x/y 为同形 ndarray），语义与标量版完全一致。
    """
    gx = x / float(scale)
    gy = y / float(scale)
    x0 = np.floor(gx).astype(np.int64)
    y0 = np.floor(gy).astype(np.int64)
    fx = smooth(gx - x0)
    fy = smooth(gy - y0)
    v00 = np.vectorize(lambda a, b: hash2d(a, b, period))(x0, y0)
    v10 = np.vectorize(lambda a, b: hash2d(a, b, period))(x0 + 1, y0)
    v01 = np.vectorize(lambda a, b: hash2d(a, b, period))(x0, y0 + 1)
    v11 = np.vectorize(lambda a, b: hash2d(a, b, period))(x0 + 1, y0 + 1)
    a = v00 * (1.0 - fx) + v10 * fx
    b = v01 * (1.0 - fx) + v11 * fx
    return (a * (1.0 - fy) + b * fy) * amplitude


def hash_grid(period):
    """整张 period×period 的 hash 查找表，供向量化取用（避免 vectorize 开销）。"""
    xs = np.arange(period, dtype=np.int64)
    return np.array([[hash2d(i, j, period) for i in range(period)] for j in range(period)],
                    dtype=np.float32)


# 噪声查找表缓存：同一 period 只算一次
_LUT = {}


def lut(period):
    if period not in _LUT:
        _LUT[period] = hash_grid(int(period))
    return _LUT[period]


def value_noise_fast(x, y, scale, period, amplitude=1.0):
    """与 periodic_value_noise 同语义，但走查找表 + fancy indexing（快得多）。"""
    t = lut(period)
    P = int(period)
    gx = x / float(scale)
    gy = y / float(scale)
    x0 = np.floor(gx).astype(np.int64)
    y0 = np.floor(gy).astype(np.int64)
    fx = smooth(gx - x0)
    fy = smooth(gy - y0)
    ix0 = np.mod(x0, P); ix1 = np.mod(x0 + 1, P)
    iy0 = np.mod(y0, P); iy1 = np.mod(y0 + 1, P)
    v00 = t[iy0, ix0]; v10 = t[iy0, ix1]
    v01 = t[iy1, ix0]; v11 = t[iy1, ix1]
    a = v00 * (1.0 - fx) + v10 * fx
    b = v01 * (1.0 - fx) + v11 * fx
    return (a * (1.0 - fy) + b * fy) * amplitude


# ---------------------------------------------------------------- 工具
def hex2rgb(s):
    s = s.lstrip('#')
    return np.array([int(s[i:i + 2], 16) for i in (0, 2, 4)], dtype=np.float32)


def pixel_grid(size):
    yy, xx = np.mgrid[0:size, 0:size]
    return xx.astype(np.float32), yy.astype(np.float32)


def reduce_contrast(rgb, amount):
    """把对比度朝均值收敛（amount=0 不动，1 完全变平）。"""
    mean = rgb.mean(axis=(0, 1), keepdims=True)
    return mean + (rgb - mean) * (1.0 - float(amount))


def seam_metrics(arr):
    """2×2 平铺中心十字接缝（band 4px）——与 make-tileable-texture.py 同算法。"""
    t = np.asarray(arr, dtype=np.int16)
    tiled = np.tile(t, (2, 2, 1))
    h, w, _ = tiled.shape
    cy, cx, band = h // 2, w // 2, 4
    hdiff = np.abs(tiled[cy - band:cy, :, :] - tiled[cy:cy + band, :, :])
    vdiff = np.abs(tiled[:, cx - band:cx, :] - tiled[:, cx:cx + band, :])
    out = []
    for d in (hdiff, vdiff):
        flat = d.reshape(-1, 3)
        out.append((float(flat.mean()), float(np.percentile(flat, 95)), float(flat.max())))
    return {'mean': round(max(out[0][0], out[1][0]), 1),
            'p95': round(max(out[0][1], out[1][1]), 1),
            'max': round(max(out[0][2], out[1][2]), 1)}


def verdict(seam):
    m, p, x = seam['mean'], seam['p95'], seam['max']
    if m <= PASS_LIMITS[0] and p <= PASS_LIMITS[1] and x <= PASS_LIMITS[2]:
        return 'PASS'
    if m <= WARN_LIMITS[0] and p <= WARN_LIMITS[1] and x <= WARN_LIMITS[2]:
        return 'WARN'
    return 'FAIL'


def wrap_error(arr, band=4):
    a = arr.astype(np.float32)
    v = float(np.abs(a[-band:].mean(axis=0) - a[:band].mean(axis=0)).mean())
    h = float(np.abs(a[:, -band:].mean(axis=1) - a[:, :band].mean(axis=1)).mean())
    return max(v, h)


# ---------------------------------------------------------------- §5.2 turf-dirt
def gen_turf_dirt(size, g):
    """短草/压实泥土/苔藓基底：全部细节 < 8px，低对比，无植物个体。"""
    pal = {k: hex2rgb(v) for k, v in g['palette'].items()}
    noise_scale = float(g['noiseScale'])
    speckle_scale = int(g['speckleScale'])
    speckle_density = float(g['speckleDensity'])
    contrast = float(g['contrast'])
    grain_strength = float(g.get('grainStrength', 0.0))

    xx, yy = pixel_grid(size)
    period = max(2, int(round(size / noise_scale)))          # 大尺度噪声的环绕周期

    n_large = value_noise_fast(xx, yy, noise_scale, period)
    # 中尺度：同一周期内 4 倍频（scale/4 → period*4），仍严格环绕
    n_mid = value_noise_fast(xx, yy, noise_scale / 4.0, period * 4)
    # 高频：scale=2px → 周期 512，逐像素颗粒（确定性，可平铺）
    n_small = value_noise_fast(xx, yy, 2.0, max(2, int(round(size / 2.0))))

    greenness = np.clip(n_large * 0.7 + n_mid * 0.3, 0.0, 1.0)
    dirtiness = np.clip(1.0 - greenness + (n_mid - 0.5) * 0.25, 0.0, 1.0)

    # 颜色混合：权重归一（凸组合），亮度落在调色板中值附近。
    # 早前的写法把权重和 ~0.5 的颜色再除以 1.1，整体亮度掉到 ~64，画面发黑。
    col_grass = pal['celadon'] * 0.70 + pal['moss'] * 0.30
    col_dirt = pal['dryOchre'] * 0.62 + pal['grayBrown'] * 0.38
    mix = np.clip(greenness * 0.75 + 0.25, 0.0, 1.0)
    rgb = col_grass[None, None, :] * mix[:, :, None] + col_dirt[None, None, :] * (1.0 - mix)[:, :, None]
    # 稻纸高光：只挂在高频噪声上（微米级），避免出现 16–32px 的粉白斑块
    rgb += pal['paper'][None, None, :] * (np.clip(n_small - 0.80, 0, 1) * 0.14)[:, :, None]

    # 细小斑点（1–2px），无叶片形状、无花、无树冠。
    # 格数取 size/scale 后**取整**，格宽 = size/格数 → 格阵严格环绕（否则会出现
    # 周期 896px 的可见重复花纹，1024 处对不上）。
    sp_cells = max(1, int(round(size / float(speckle_scale))))
    sp_cell = size / float(sp_cells)
    sx = np.floor(xx / sp_cell).astype(np.int64) % sp_cells
    sy = np.floor(yy / sp_cell).astype(np.int64) % sp_cells
    st = np.array([[hash2d(i, j, sp_cells) for i in range(sp_cells)] for j in range(sp_cells)],
                  dtype=np.float32)
    sp = st[sy, sx]
    dark = sp < speckle_density
    light = (sp >= speckle_density) & (sp < speckle_density * 1.7)
    rgb[dark] *= 0.92
    rgb[light] = np.clip(rgb[light] * 1.04 + 4.0, 0, 255)

    # 逐像素颗粒（grain）：确定性 hash，周期 1px，仅扰动亮度。
    # 纯噪声纹理在 1024 尺寸下视觉过于均匀，需要极弱的逐像素颗粒把它"拉回"手绘质感。
    if grain_strength > 0:
        gn = np.array([[hash2d(i, j, size) for i in range(size)] for j in range(size)],
                      dtype=np.float32)
        rgb *= (1.0 + (gn - 0.5) * 2.0 * grain_strength)[:, :, None]

    rgb = reduce_contrast(rgb, contrast)
    # 宏观调制（Gate C1-A §5，默认关闭）。turf-dirt 的微观颗粒可过检，但
    # colStd/rowStd 仅 0.30/0.28，宏观偏平。这层用低频噪声调制亮度与通道，
    # 只在 macroAmount/macroChromaAmount 非 0 时生效，默认 0 时输出与之前等价。
    macro_scale = float(g.get('macroScale', 128))
    macro_amount = float(g.get('macroAmount', 0.0))
    macro_chroma = float(g.get('macroChromaAmount', 0.0))
    if macro_amount > 0.0 or macro_chroma > 0.0:
        period = max(2, int(round(size / macro_scale)))
        macro = value_noise_fast(xx, yy, macro_scale, period)
        if macro_amount > 0.0:
            lum = 1.0 + macro_amount * (macro - 0.5) * 2.0
            rgb = rgb * lum[:, :, None]
        if macro_chroma > 0.0:
            # 第二层噪声做通道去相关，避免整体亮度变化看起来像单纯提亮/压暗
            chroma = value_noise_fast(xx, yy, macro_scale, period * 2)
            delta = macro_chroma * (chroma - 0.5)
            rgb[:, :, 0] *= 1.0 + delta
            rgb[:, :, 2] *= 1.0 - delta
    return np.clip(rgb, 0, 255).astype(np.uint8)


# ------------------------------------------------- §5.3 torus-voronoi-flagstone
def gen_flagstone(size, g):
    """环面 Voronoi 石板：无跨边界大板、细缝、低对比、老镇青石感。"""
    pal = {k: hex2rgb(v) for k, v in g['palette'].items()}
    cell_min = float(g['cellMin'])
    cell_max = float(g['cellMax'])
    jitter = float(g['jitter'])
    mortar_width = float(g['mortarWidth'])
    mortar_softness = float(g['mortarSoftness'])
    color_var = float(g['colorVariance'])
    surf_noise = float(g['surfaceNoise'])

    avg_cell = (cell_min + cell_max) * 0.5
    cols = max(6, int(round(size / avg_cell)))
    rows = cols
    cw = size / float(cols)
    ch = size / float(rows)

    # 每格抖动种子（确定性），坐标对 size 取模 → 环面
    seeds = np.zeros((rows, cols, 2), dtype=np.float32)
    for iy in range(rows):
        for ix in range(cols):
            hx = hash2d(ix, iy, cols)
            hy = hash2d(ix + 7919, iy + 104729, rows)
            sx = (ix + 0.5 + (hx - 0.5) * 2.0 * jitter) * cw
            sy = (iy + 0.5 + (hy - 0.5) * 2.0 * jitter) * ch
            seeds[iy, ix, 0] = sx % size
            seeds[iy, ix, 1] = sy % size

    xx, yy = pixel_grid(size)
    best = np.full((size, size), 1e9, dtype=np.float32)
    second = np.full((size, size), 1e9, dtype=np.float32)
    owner = np.zeros((size, size), dtype=np.int32)

    # 3×3 环绕邻域：取每个像素所在格及其邻格
    cx = np.floor(xx / cw).astype(np.int64)
    cy = np.floor(yy / ch).astype(np.int64)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            gx = np.mod(cx + dx, cols)
            gy = np.mod(cy + dy, rows)
            px = seeds[gy, gx, 0]
            py = seeds[gy, gx, 1]
            ddx = np.abs(px - xx)
            ddy = np.abs(py - yy)
            ddx = np.minimum(ddx, size - ddx)          # 环面距离
            ddy = np.minimum(ddy, size - ddy)
            d = np.sqrt(ddx * ddx + ddy * ddy)
            closer = d < best
            second = np.where(closer, best, np.minimum(second, d))
            owner = np.where(closer, gy * cols + gx, owner)
            best = np.where(closer, d, best)

    edge = second - best
    # 砂浆掩码：edge 小 → 靠近石板缝
    t = np.clip(edge / max(1e-6, mortar_width * mortar_softness), 0.0, 1.0)
    mortar_mask = 1.0 - smooth(t)

    # 每块石板一个确定性色。走格表查找而非逐像素 vectorize（owner 只取 cols×rows 个值）。
    # 早前把三色按权重相加再除 1.5，亮度落到调色板的 0.26–0.80，石板发黑、块间反差过大，
    # 看起来像彩色玻璃马赛克而不是老青石。改为"凸组合"：亮度始终贴着调色板，只做小幅偏移。
    P = max(cols, rows)
    ox = owner % cols
    oy = owner // cols
    tbl = np.array([[hash2d(i + 31337, j + 65537, P) for i in range(P)] for j in range(P)],
                   dtype=np.float32)
    tbl2 = np.array([[hash2d(i + 99991, j + 77773, P) for i in range(P)] for j in range(P)],
                    dtype=np.float32)
    hv = tbl[oy, ox]
    hv2 = tbl2[oy, ox]

    base = pal['slate'][None, None, :] * (1.0 - hv)[:, :, None] + pal['warmGray'][None, None, :] * hv[:, :, None]
    base = base * (1.0 - 0.5 * hv2)[:, :, None] + pal['blueGray'][None, None, :] * (0.5 * hv2)[:, :, None]
    # 逐板亮度微差（±colorVariance/2，不做整块大色差）
    bright = 1.0 + ((hv - 0.5) + (hv2 - 0.5) * 0.6) * color_var
    base *= bright[:, :, None]

    # 板内轻微倒角：靠近缝处略暗，读起来是石头而不是平涂色块
    bev = np.clip(edge / max(1e-6, mortar_width * mortar_softness * 3.0), 0.0, 1.0)
    base *= (0.94 + 0.06 * bev)[:, :, None]

    rgb = base * (1.0 - mortar_mask)[:, :, None] + pal['mortar'][None, None, :] * mortar_mask[:, :, None]

    # 表面噪声：只改亮度，不改结构；周期与图像一致 → 环绕
    sp = max(2, int(round(size / 128.0)))
    n = value_noise_fast(xx, yy, 128.0, sp) - 0.5
    rgb *= (1.0 + n * 2.0 * surf_noise)[:, :, None]

    # 极弱高光，避免水洼/大亮斑
    hl = np.clip(value_noise_fast(xx, yy, 256.0, max(2, int(round(size / 256.0)))) - 0.80, 0, 1)
    rgb += pal['highlight'][None, None, :] * (hl * 0.10)[:, :, None]

    return np.clip(rgb, 0, 255).astype(np.uint8)


# ---------------------------------------------------------------- Gate C1-A 新增 generator
# 通用约定（任务书 §4）：
#   - 全部噪声走 value_noise_fast / hash2d，scale 优先取 1024 的因数，靠 np.mod 保证 wrap；
#   - 颜色混合一律凸组合，权重和恒为 1，不做「相加后除系数」；
#   - palette / scale / density / contrast 全部读配置，脚本内不硬编码。
def _speckle_mask(xx, yy, size, scale, density):
    """格阵 speckle：返回 (暗点掩码, 亮点掩码)。格数取 size/scale 的整数因数，保证环绕。

    T093（方块状伪影的真身）：原来每个格子**整格填一个常数**，于是每一颗 speckle 都是
    一个边与像素网格对齐的硬边正方形。在高对比材质上看不出来，在 dirt-road 这种
    对比度只有 0.16、调色板三色又很接近的低对比土路上，它就变成玩家眼里的"一格格明暗马赛克"
    （截图 p02-01-town.png 的土路带，放大看是清一色的小方块）。
    现在改成"格内圆盘"：格心与 gate 用的 hash 表完全不变（位置、密度、确定性、可平铺都不变），
    只把形状从填满整格改成半径 0.34 格的圆——方角消失，speckle 变成点而不是块。
    """
    cells = max(1, int(round(size / float(scale))))
    cell = size / float(cells)
    gx = np.floor(xx / cell).astype(np.int64)
    gy = np.floor(yy / cell).astype(np.int64)
    sx = gx % cells
    sy = gy % cells
    table = np.array([[hash2d(i, j, cells) for i in range(cells)] for j in range(cells)],
                     dtype=np.float32)
    sp = table[sy, sx]
    # 到所在格心的距离（换算成"格"为单位）；圆盘半径 0.34 格，留出格与格之间的空隙，
    # 所以相邻格的点不会连成方格网——这正是原来"整格填充"最刺眼的地方。
    cx = (gx + .5) * cell
    cy = (gy + .5) * cell
    dist = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2) / cell
    inside = dist <= 0.34
    return (sp < density) & inside, ((sp >= density) & (sp < density + density * 0.5)) & inside


def _palette(g):
    return {k: hex2rgb(v) for k, v in g['palette'].items()}


def gen_packed_earth(size, g):
    """踩踏土路：低频土色明暗 + 中频踩踏斑 + 高频细砾，无方向性。"""
    pal = _palette(g)
    xx, yy = pixel_grid(size)
    n_low = value_noise_fast(xx, yy, float(g['lowFreqScale']), max(2, size // int(g['lowFreqScale'])))
    n_mid = value_noise_fast(xx, yy, float(g['midFreqScale']), max(2, size // int(g['midFreqScale'])))
    t = np.clip(float(g['lowFreqWeight']) * n_low + float(g['midFreqWeight']) * n_mid, 0.0, 1.0)

    # 两段凸组合：t<0.5 在 dustyBrown→warmOchre，t>=0.5 在 warmOchre→fadedEarthYellow
    lo = np.clip(t / 0.5, 0.0, 1.0)
    hi = np.clip((t - 0.5) / 0.5, 0.0, 1.0)
    base = (pal['dustyBrown'][None, None, :] * (1.0 - lo)[:, :, None] +
            pal['warmOchre'][None, None, :] * lo[:, :, None])
    base = (base * (1.0 - hi)[:, :, None] +
            pal['fadedEarthYellow'][None, None, :] * hi[:, :, None])
    # paleGray 磨损亮斑：权重封顶 0.12，保持凸组合
    wear = np.clip((n_mid - 0.78) * 3.0, 0.0, 1.0) * 0.12
    base = base * (1.0 - wear)[:, :, None] + pal['paleGray'][None, None, :] * wear[:, :, None]

    dark, light = _speckle_mask(xx, yy, size, g['speckleScale'], float(g['speckleDensity']))
    rgb = base.copy()
    rgb[dark] -= float(g['speckleDarkAmount'])
    rgb[light] = np.clip(rgb[light] + float(g['speckleLightAmount']), 0, 255)
    return np.clip(reduce_contrast(rgb, float(g['contrast'])), 0, 255).astype(np.uint8)


def gen_open_plaza(size, g):
    """广场：必须各向同性 2D 起伏（Gate C1-A §5.2 —— 中心 2×2 失败缝的根因修复对象）。

    旧 TEMP fallback 是 20.0*sin(...)[:, None]，只沿 y 变化、列方向完全均匀，
    于是纯广场互相接缝看似正常，一旦与其它地形按权重混合就把差异放大成可见接缝。
    这里用三层各向同性 value_noise_fast + 极稀疏 1px 暗点，保证行列都有起伏。
    """
    pal = _palette(g)
    xx, yy = pixel_grid(size)
    n_large = value_noise_fast(xx, yy, float(g['largeScale']), max(2, size // int(g['largeScale'])))
    n_mid = value_noise_fast(xx, yy, float(g['midScale']), max(2, size // int(g['midScale'])))
    n_fine = value_noise_fast(xx, yy, float(g['fineScale']), max(2, size // int(g['fineScale'])))
    t = np.clip(float(g['largeWeight']) * n_large + float(g['midWeight']) * n_mid +
                float(g['fineWeight']) * n_fine, 0.0, 1.0)
    # value_noise 的输出集中在 0.5 附近（实测 std≈0.16），直接当 mix 因子会让 t 只在
    # 0.42~0.58 间活动，四色亮度又接近，最终 std 只有 10。这里把 t 按配置权重重映射到
    # 满量程，保持凸组合不变（只做线性拉伸，不引入除法）。
    t = np.clip((t - 0.5) * 2.6 + 0.5, 0.0, 1.0)

    # 四色低对比凸组合：t 经两段斜坡拆成 a/b 三个非负分量。
    # 先按各色设计权重混合，再除以权重和，保证每个像素始终是凸组合。
    a = np.clip(t * 2.0, 0.0, 1.0)
    b = np.clip((t - 0.5) * 2.0, 0.0, 1.0)
    weights = np.stack((
        0.55 * a,
        0.45 * a * (1.0 - b),
        0.50 * b,
        1.0 - 0.5 * a - 0.5 * b,
    ), axis=2)
    weights /= np.maximum(weights.sum(axis=2, keepdims=True), 1e-6)
    rgb = (pal['paleBeige'][None, None, :] * weights[:, :, 0, None] +
           pal['lightGray'][None, None, :] * weights[:, :, 1, None] +
           pal['softOchre'][None, None, :] * weights[:, :, 2, None] +
           pal['mutedStoneWhite'][None, None, :] * weights[:, :, 3, None])

    # 极稀疏 1px 暗点（不成缝、不成网）
    crack, _ = _speckle_mask(xx, yy, size, g['speckleScale'], float(g['crackDensity']))
    rgb[crack] -= float(g['crackAmount'])
    dark, light = _speckle_mask(xx, yy, size, g['speckleScale'], float(g['speckleDensity']))
    rgb[dark] -= float(g['speckleDarkAmount'])
    rgb[light] = np.clip(rgb[light] + float(g['speckleLightAmount']), 0, 255)
    return np.clip(reduce_contrast(rgb, float(g['contrast'])), 0, 255).astype(np.uint8)


def gen_bridge_deck(size, g):
    """桥面石板：环面 Voronoi + 磨损点。不改 torus-voronoi-flagstone，本函数自带一份。"""
    pal = _palette(g)
    cell_min = float(g['cellMin'])
    cell_max = float(g['cellMax'])
    jitter = float(g['jitter'])
    mortar_width = float(g['mortarWidth'])
    mortar_softness = float(g['mortarSoftness'])
    color_var = float(g['colorVariance'])
    surf_noise = float(g['surfaceNoise'])

    avg_cell = (cell_min + cell_max) * 0.5
    cols = max(6, int(round(size / avg_cell)))
    rows = cols
    cw, ch = size / float(cols), size / float(rows)

    seeds = np.zeros((rows, cols, 2), dtype=np.float32)
    for iy in range(rows):
        for ix in range(cols):
            hx = hash2d(ix, iy, cols)
            hy = hash2d(ix + 7919, iy + 104729, rows)
            sx = (ix + 0.5 + (hx - 0.5) * 2.0 * jitter) * cw
            sy = (iy + 0.5 + (hy - 0.5) * 2.0 * jitter) * ch
            seeds[iy, ix, 0] = sx % size
            seeds[iy, ix, 1] = sy % size

    xx, yy = pixel_grid(size)
    best = np.full((size, size), 1e9, dtype=np.float32)
    second = np.full((size, size), 1e9, dtype=np.float32)
    owner = np.zeros((size, size), dtype=np.int32)
    cx = np.floor(xx / cw).astype(np.int64)
    cy = np.floor(yy / ch).astype(np.int64)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            gx = np.mod(cx + dx, cols)
            gy = np.mod(cy + dy, rows)
            px, py = seeds[gy, gx, 0], seeds[gy, gx, 1]
            ddx = np.abs(px - xx)
            ddy = np.abs(py - yy)
            ddx = np.minimum(ddx, size - ddx)
            ddy = np.minimum(ddy, size - ddy)
            d = np.sqrt(ddx * ddx + ddy * ddy)
            closer = d < best
            second = np.where(closer, best, np.minimum(second, d))
            owner = np.where(closer, gy * cols + gx, owner)
            best = np.where(closer, d, best)

    edge = second - best
    t = np.clip(edge / max(1e-6, mortar_width * mortar_softness), 0.0, 1.0)
    mortar_mask = 1.0 - smooth(t)

    P = max(cols, rows)
    ox, oy = owner % cols, owner // cols
    tbl = np.array([[hash2d(i + 31337, j + 65537, P) for i in range(P)] for j in range(P)], dtype=np.float32)
    tbl2 = np.array([[hash2d(i + 99991, j + 77773, P) for i in range(P)] for j in range(P)], dtype=np.float32)
    hv, hv2 = tbl[oy, ox], tbl2[oy, ox]

    # 凸组合：三项权重 (1-hv) + hv 在两种石色间过渡，再按 hv2 少量偏向第三色
    base = (pal['fadedSlate'][None, None, :] * (1.0 - hv)[:, :, None] +
            pal['warmGray'][None, None, :] * hv[:, :, None])
    base = (base * (1.0 - 0.45 * hv2)[:, :, None] +
            pal['brownGrayStone'][None, None, :] * (0.45 * hv2)[:, :, None])
    base *= (1.0 + ((hv - 0.5) + (hv2 - 0.5) * 0.6) * color_var)[:, :, None]

    bev = np.clip(edge / max(1e-6, mortar_width * mortar_softness * 3.0), 0.0, 1.0)
    base *= (0.94 + 0.06 * bev)[:, :, None]
    rgb = base * (1.0 - mortar_mask)[:, :, None] + pal['mortar'][None, None, :] * mortar_mask[:, :, None]

    sp = max(2, int(round(size / 128.0)))
    n = value_noise_fast(xx, yy, 128.0, sp) - 0.5
    rgb *= (1.0 + n * 2.0 * surf_noise)[:, :, None]
    hl = np.clip(value_noise_fast(xx, yy, 256.0, max(2, int(round(size / 256.0)))) - 0.82, 0, 1)
    rgb += pal['earthyHighlight'][None, None, :] * (hl * 0.08)[:, :, None]

    dark, light = _speckle_mask(xx, yy, size, g['speckleScale'], float(g['speckleDensity']))
    rgb[dark] -= float(g['speckleDarkAmount'])
    rgb[light] = np.clip(rgb[light] + float(g['speckleLightAmount']), 0, 255)
    return np.clip(reduce_contrast(rgb, float(g['contrast'])), 0, 255).astype(np.uint8)


def gen_rock_face(size, g):
    """山体岩面：低频大块 + 中频薄裂隙 + 矿物点，偏暗但不死黑。"""
    pal = _palette(g)
    xx, yy = pixel_grid(size)
    n_large = value_noise_fast(xx, yy, float(g['largeScale']), max(2, size // int(g['largeScale'])))
    n_mid = value_noise_fast(xx, yy, float(g['midScale']), max(2, size // int(g['midScale'])))
    n_fine = value_noise_fast(xx, yy, float(g['fineScale']), max(2, size // int(g['fineScale'])))
    t = np.clip(float(g['largeWeight']) * n_large + float(g['midWeight']) * n_mid +
                float(g['fineWeight']) * n_fine, 0.0, 1.0)
    t = np.clip((t - 0.5) * 2.4 + 0.5, 0.0, 1.0)

    a = np.clip(t * 2.0, 0.0, 1.0)
    b = np.clip((t - 0.5) * 2.0, 0.0, 1.0)
    w_dark = 1.0 - 0.55 * a - 0.45 * b
    rgb = (pal['darkCyanGray'][None, None, :] * (0.55 * a)[:, :, None] +
           pal['coldGrayBrown'][None, None, :] * (0.45 * a * (1.0 - b))[:, :, None] +
           pal['inkBlack'][None, None, :] * (0.45 * b)[:, :, None] +
           pal['mossGreen'][None, None, :] * w_dark[:, :, None])

    # 薄裂隙：只压暗 n_mid 高位窄带，长度受 fineScale 8px 限制，不成大裂缝
    crev = np.clip((n_mid - float(g['creviceThreshold'])) * 6.0, 0.0, 1.0)
    rgb *= (1.0 - float(g['creviceAmount']) / 255.0 * crev)[:, :, None]

    mineral, _ = _speckle_mask(xx, yy, size, 16, float(g['mineralDensity']))
    rgb[mineral] = np.clip(rgb[mineral] + float(g['mineralAmount']), 0, 255)
    return np.clip(reduce_contrast(rgb, float(g['contrast'])), 0, 255).astype(np.uint8)


def gen_trail_stone_dirt(size, g):
    """山径：以土为底 + 碎石斑块 + 更高密度细砾，比 dirt-road 更石更干。"""
    pal = _palette(g)
    xx, yy = pixel_grid(size)
    n_low = value_noise_fast(xx, yy, float(g['lowFreqScale']), max(2, size // int(g['lowFreqScale'])))
    n_mid = value_noise_fast(xx, yy, float(g['midFreqScale']), max(2, size // int(g['midFreqScale'])))
    t = np.clip(float(g['lowFreqWeight']) * n_low + float(g['midFreqWeight']) * n_mid, 0.0, 1.0)
    t = np.clip((t - 0.5) * 2.2 + 0.5, 0.0, 1.0)

    a = np.clip(t * 2.0, 0.0, 1.0)
    b = np.clip((t - 0.5) * 2.0, 0.0, 1.0)
    w_olive = 1.0 - 0.55 * a - 0.45 * b
    rgb = (pal['earthyBrown'][None, None, :] * (0.55 * a)[:, :, None] +
           pal['fadedOchre'][None, None, :] * (0.45 * a * (1.0 - b))[:, :, None] +
           pal['dryOlive'][None, None, :] * (0.45 * b)[:, :, None] +
           pal['earthyBrown'][None, None, :] * w_olive[:, :, None])

    # 碎石斑块：n_mid 高位混入灰石，权重封顶，不成大石轮廓
    patch = np.clip((n_mid - float(g['stonePatchThreshold'])) * 5.0, 0.0, 1.0)
    pw = patch * (float(g['stonePatchAmount']) / 255.0)
    rgb = rgb * (1.0 - pw)[:, :, None] + pal['grayStone'][None, None, :] * pw[:, :, None]

    gravel, gravel_l = _speckle_mask(xx, yy, size, g['gravelScale'], float(g['gravelDensity']))
    rgb[gravel] -= float(g['speckleDarkAmount'])
    rgb[gravel_l] = np.clip(rgb[gravel_l] + float(g['speckleLightAmount']), 0, 255)
    hl = np.clip(value_noise_fast(xx, yy, 16.0, max(2, size // 16)) - 0.86, 0, 1)
    rgb += pal['gravelLight'][None, None, :] * (hl * 0.06)[:, :, None]
    return np.clip(reduce_contrast(rgb, float(g['contrast'])), 0, 255).astype(np.uint8)


def gen_furrow_field(size, g):
    """农田：水平垄线 + 地块低频 + 稀作物点。

    stripeScaleX=1024 让 x 方向近乎连续、stripeScaleY=64 形成水平周期垄线。
    但纯横向条纹会让 columnMeanStd 归零，所以再叠各向同性的 patch 与 fine 噪声，
    保证列方向也有起伏（任务书 §5.6 要求 columnMeanStd >= 1.0）。
    """
    pal = _palette(g)
    xx, yy = pixel_grid(size)
    sy_scale = float(g['stripeScaleY'])
    stripe = value_noise_fast(xx, yy, sy_scale, max(2, size // int(sy_scale)))
    patch = value_noise_fast(xx, yy, float(g['patchScaleX']), max(2, size // int(g['patchScaleX'])))
    fine = value_noise_fast(xx, yy, float(g['fineScale']), max(2, size // int(g['fineScale'])))
    t = np.clip(float(g['stripeWeight']) * stripe + float(g['patchWeight']) * patch +
                float(g['fineWeight']) * fine, 0.0, 1.0)
    t = np.clip((t - 0.5) * 2.3 + 0.5, 0.0, 1.0)

    a = np.clip(t * 2.0, 0.0, 1.0)
    b = np.clip((t - 0.5) * 2.0, 0.0, 1.0)
    w_wheat = 1.0 - 0.5 * a - 0.5 * b
    rgb = (pal['yellowGreen'][None, None, :] * (0.5 * a)[:, :, None] +
           pal['fadedOlive'][None, None, :] * (0.5 * a * (1.0 - b))[:, :, None] +
           pal['lightBrown'][None, None, :] * (0.5 * b)[:, :, None] +
           pal['paleWheat'][None, None, :] * w_wheat[:, :, None])

    crop, _ = _speckle_mask(xx, yy, size, g['cropSpeckleScale'], float(g['cropSpeckleDensity']))
    rgb[crop] = np.clip(rgb[crop] + float(g['cropSpeckleAmount']), 0, 255)
    return np.clip(reduce_contrast(rgb, float(g['contrast'])), 0, 255).astype(np.uint8)


def gen_wet_shore(size, g):
    """河岸：湿土底 + 细砾 + 短草点，比 grass 更湿更土、比 dirt-road 更绿。"""
    pal = _palette(g)
    xx, yy = pixel_grid(size)
    n_low = value_noise_fast(xx, yy, float(g['lowFreqScale']), max(2, size // int(g['lowFreqScale'])))
    n_mid = value_noise_fast(xx, yy, float(g['midFreqScale']), max(2, size // int(g['midFreqScale'])))
    t = np.clip(float(g['lowFreqWeight']) * n_low + float(g['midFreqWeight']) * n_mid, 0.0, 1.0)
    t = np.clip((t - 0.5) * 2.2 + 0.5, 0.0, 1.0)

    a = np.clip(t * 2.0, 0.0, 1.0)
    b = np.clip((t - 0.5) * 2.0, 0.0, 1.0)
    w_gray = 1.0 - 0.55 * a - 0.45 * b
    rgb = (pal['wetBrown'][None, None, :] * (0.55 * a)[:, :, None] +
           pal['paleSand'][None, None, :] * (0.45 * a * (1.0 - b))[:, :, None] +
           pal['oliveGreen'][None, None, :] * (0.45 * b)[:, :, None] +
           pal['softGrayGreen'][None, None, :] * w_gray[:, :, None])

    pebble, pebble_l = _speckle_mask(xx, yy, size, g['pebbleScale'], float(g['pebbleDensity']))
    grass, _ = _speckle_mask(xx, yy, size, g['grassSpeckleScale'], float(g['grassSpeckleDensity']))
    rgb[pebble] -= float(g['speckleDarkAmount'])
    rgb[pebble_l] = np.clip(rgb[pebble_l] + float(g['speckleLightAmount']), 0, 255)
    gw = grass * 0.5          # 短草只染 1px 点，不成草簇
    rgb = rgb * (1.0 - gw)[:, :, None] + pal['mutedMoss'][None, None, :] * gw[:, :, None]
    return np.clip(reduce_contrast(rgb, float(g['contrast'])), 0, 255).astype(np.uint8)


def gen_shoulder_transition(size, g):
    """路肩：土为主 + 稀疏草点 + 细砾，介于 dirt-road 与 grass 之间但偏土。"""
    pal = _palette(g)
    xx, yy = pixel_grid(size)
    n_soil = value_noise_fast(xx, yy, float(g['soilScale']), max(2, size // int(g['soilScale'])))
    n_grass = value_noise_fast(xx, yy, float(g['grassScale']), max(2, size // int(g['grassScale'])))
    n_gravel = value_noise_fast(xx, yy, float(g['gravelScale']), max(2, size // int(g['gravelScale'])))
    t = np.clip(float(g['soilWeight']) * n_soil + float(g['grassWeight']) * n_grass +
                float(g['gravelWeight']) * n_gravel, 0.0, 1.0)
    t = np.clip((t - 0.5) * 2.2 + 0.5, 0.0, 1.0)

    a = np.clip(t * 2.0, 0.0, 1.0)
    b = np.clip((t - 0.5) * 2.0, 0.0, 1.0)
    w_yellow = 1.0 - 0.55 * a - 0.45 * b
    rgb = (pal['grayBrown'][None, None, :] * (0.55 * a)[:, :, None] +
           pal['ochre'][None, None, :] * (0.45 * a * (1.0 - b))[:, :, None] +
           pal['dustyYellow'][None, None, :] * (0.45 * b)[:, :, None] +
           pal['ochre'][None, None, :] * w_yellow[:, :, None])

    grass, _ = _speckle_mask(xx, yy, size, g['gravelScale'], float(g['grassDensity']))
    gw = grass * 0.45
    rgb = rgb * (1.0 - gw)[:, :, None] + pal['mutedGreen'][None, None, :] * gw[:, :, None]
    gravel, gravel_l = _speckle_mask(xx, yy, size, g['gravelScale'], float(g['gravelDensity']))
    rgb[gravel] -= float(g['speckleDarkAmount'])
    rgb[gravel_l] = np.clip(rgb[gravel_l] + float(g['speckleLightAmount']), 0, 255)
    return np.clip(reduce_contrast(rgb, float(g['contrast'])), 0, 255).astype(np.uint8)


def gen_slope_transition(size, g):
    """山脚：碎石土 + 稀疏短草，比 mountain 亮、比 grass 石。"""
    pal = _palette(g)
    xx, yy = pixel_grid(size)
    n_rock = value_noise_fast(xx, yy, float(g['rockScale']), max(2, size // int(g['rockScale'])))
    n_soil = value_noise_fast(xx, yy, float(g['soilScale']), max(2, size // int(g['soilScale'])))
    n_grass = value_noise_fast(xx, yy, float(g['grassScale']), max(2, size // int(g['grassScale'])))
    t = np.clip(float(g['rockWeight']) * n_rock + float(g['soilWeight']) * n_soil +
                float(g['grassWeight']) * n_grass, 0.0, 1.0)
    t = np.clip((t - 0.5) * 2.3 + 0.5, 0.0, 1.0)

    a = np.clip(t * 2.0, 0.0, 1.0)
    b = np.clip((t - 0.5) * 2.0, 0.0, 1.0)
    w_ochre = 1.0 - 0.55 * a - 0.45 * b
    rgb = (pal['inkGray'][None, None, :] * (0.55 * a)[:, :, None] +
           pal['grayBrown'][None, None, :] * (0.45 * a * (1.0 - b))[:, :, None] +
           pal['dryOchre'][None, None, :] * (0.45 * b)[:, :, None] +
           pal['grayBrown'][None, None, :] * w_ochre[:, :, None])

    grass, _ = _speckle_mask(xx, yy, size, g['gravelScale'], float(g['grassDensity']))
    gw = grass * 0.4
    rgb = rgb * (1.0 - gw)[:, :, None] + pal['mossGreen'][None, None, :] * gw[:, :, None]
    gravel, gravel_l = _speckle_mask(xx, yy, size, g['gravelScale'], float(g['gravelDensity']))
    rgb[gravel] -= float(g['speckleDarkAmount'])
    rgb[gravel_l] = np.clip(rgb[gravel_l] + float(g['speckleLightAmount']), 0, 255)
    hl = np.clip(value_noise_fast(xx, yy, 16.0, max(2, size // 16)) - 0.88, 0, 1)
    rgb += pal['gravelHighlight'][None, None, :] * (hl * 0.06)[:, :, None]
    return np.clip(reduce_contrast(rgb, float(g['contrast'])), 0, 255).astype(np.uint8)


def load_cfg():
    if not os.path.exists(CFG):
        sys.exit('缺少配置：' + CFG)
    with io.open(CFG, encoding='utf-8') as fh:
        return json.load(fh)


GEN = {'turf-dirt': gen_turf_dirt, 'torus-voronoi-flagstone': gen_flagstone,
       'packed-earth': gen_packed_earth, 'open-plaza': gen_open_plaza,
       'bridge-deck': gen_bridge_deck, 'rock-face': gen_rock_face,
       'trail-stone-dirt': gen_trail_stone_dirt, 'furrow-field': gen_furrow_field,
       'wet-shore': gen_wet_shore, 'shoulder-transition': gen_shoulder_transition,
       'slope-transition': gen_slope_transition}



def validate_generator_output(name, arr, size):
    """Gate C1-A §六 守卫：generator 必须返回形状正确的有限值数组。

    起因是 C1-A 实现过程中 open-plaza 被重排截断、返回 None，脚本直到
    Image.fromarray 才炸，错误现场与根因隔得很远。这里在 generator 出口就地拦截。
    """
    assert arr is not None, '%s returned None（函数体可能被截断或缺少 return）' % name
    assert hasattr(arr, 'shape'), '%s did not return ndarray' % name
    assert tuple(arr.shape) == (size, size, 3), \
        '%s shape %s != %s' % (name, tuple(arr.shape), (size, size, 3))
    f = np.asarray(arr, dtype=np.float32)
    assert np.isfinite(f).all(), '%s contains NaN/Inf' % name
    lum = f.mean(axis=2)
    col_std = float(lum.mean(axis=0).std())
    row_std = float(lum.mean(axis=1).std())
    overall = float(f.std())
    # 纯色死块：行列都必须有起伏。必须用 AND —— open-plaza 当初就是
    # columnMeanStd=0 而 rowStd>0 的单向条纹，用 OR 会把它放行。
    assert overall > 1.0, '%s 是纯色死块 overallStd=%.2f' % (name, overall)
    assert col_std > 0.0 and row_std > 0.0, \
        '%s 行列单侧无起伏 colStd=%.2f rowStd=%.2f' % (name, col_std, row_std)
    return {'overallStd': round(overall, 2),
            'columnMeanStd': round(col_std, 2),
            'rowMeanStd': round(row_std, 2)}


def run_generator_selftest(cfg):
    """遍历 GEN 里所有 generator，输出指标并校验守卫。"""
    size = int(cfg['size'])
    lines = ['=== procedural base generator selftest ===']
    bad = []
    for gen_name in sorted(GEN):
        gcfg = (cfg.get('generators') or {}).get(gen_name)
        if gcfg is None:
            lines.append('%-24s SKIP（配置中无 %s 定义）' % (gen_name, gen_name))
            continue
        try:
            arr = GEN[gen_name](size, gcfg)
            m = validate_generator_output(gen_name, arr, size)
            seam = seam_metrics(np.asarray(arr, dtype=np.uint8))
            st = verdict(seam)
            lines.append('%-24s OK  shape=%s std=%s colStd=%s rowStd=%s seam=%s/%s/%s %s' % (
                gen_name, tuple(np.asarray(arr).shape), m['overallStd'],
                m['columnMeanStd'], m['rowMeanStd'], seam['mean'], seam['p95'], seam['max'], st))
            if st == 'FAIL':
                bad.append('%s seam FAIL' % gen_name)
        except AssertionError as error:
            lines.append('%-24s FAIL %s' % (gen_name, error))
            bad.append(gen_name)
        except Exception as error:
            lines.append('%-24s ERROR %s: %s' % (gen_name, type(error).__name__, error))
            bad.append(gen_name)
    lines.append('')
    lines.append('generators checked: %d' % len(GEN))
    lines.append('RESULT: %s' % ('PASS' if not bad else 'FAIL'))
    for b in bad:
        lines.append('  FAILED: %s' % b)

    log_dir = wp.tools_log_root()
    wp.ensure_dir(log_dir)
    out = os.path.join(log_dir, 'procedural-base-generator-selftest.log')
    with io.open(out, 'w', encoding='utf-8', newline='\n') as fh:
        fh.write('\n'.join(lines) + '\n')
    print('\n'.join(lines))
    return 1 if bad else 0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--names', nargs='*', default=[])
    ap.add_argument('--all', action='store_true')
    ap.add_argument('--selftest-generators', action='store_true')
    args = ap.parse_args()

    cfg = load_cfg()
    if args.selftest_generators:
        return run_generator_selftest(cfg)
    size = int(cfg['size'])
    out_dir = wp.resolve_repo_path(cfg['outputs']['proceduralDir'], allow_missing=True)
    wp.assert_canonical_output_path(out_dir)
    prev_dir = os.path.join(out_dir, 'previews')
    wp.ensure_dir(prev_dir)

    names = [n[:-4] if n.endswith('.png') else n for n in args.names]
    if args.all or not names:
        names = [k[:-4] for k, v in cfg['materials'].items() if v.get('baseGenerator')]

    # 增量合并：--names 只跑子集时保留报告中其它材质的既有结论，
    # 否则单跑一张会把报告冲成只剩那张，反平涂证据链（variationOk）在报告里断掉。
    report_path = os.path.join(wp.tools_log_root(), 'world-procedural-base-report.json')
    report = {}
    if os.path.exists(report_path):
        try:
            report = json.load(io.open(report_path, encoding='utf-8'))
        except Exception:
            report = {}
    if args.names and not args.all:
        print('增量模式：仅重算 %s，保留报告中其余 %d 项' % (
            ', '.join(names), len([k for k in report if k not in names])))
    for name in names:
        key = name + '.png'
        m = cfg['materials'].get(key)
        if not m or not m.get('baseGenerator'):
            print('%-12s SKIP（无 baseGenerator）' % name)
            continue
        gen_name = m['baseGenerator']
        if gen_name not in GEN:
            print('%-12s SKIP（未知生成器 %s）' % (name, gen_name))
            continue
        gcfg = cfg['generators'][gen_name]
        arr = GEN[gen_name](size, gcfg)
        validate_generator_output(name, arr, size)
        out_path = os.path.join(out_dir, name + '.base.png')
        Image.fromarray(arr, 'RGB').save(out_path)
        prev = os.path.join(prev_dir, name + '.base.png.2x2.png')
        Image.fromarray(np.tile(arr, (2, 2, 1)), 'RGB').save(prev)
        seam = seam_metrics(arr)
        st = verdict(seam)
        # Gate C1-A §4.4：base 自检除接缝外还要报 std 与行列均值标准差。
        # plaza 必须靠 colStd/rowStd 证明自己不是平涂——上一轮 TEMP fallback 只沿 y 变化，
        # 列方向完全均匀，正是中心 2×2 失败缝的根因。
        lum = arr.astype(np.float32).mean(axis=2)
        col_std = float(lum.mean(axis=0).std())
        row_std = float(lum.mean(axis=1).std())
        min_col = float(gcfg.get('minColumnMeanStd', 1.0))
        min_row = float(gcfg.get('minRowMeanStd', 1.0))
        vary_ok = col_std >= min_col and row_std >= min_row
        report[name] = {'generator': gen_name, 'size': size,
                        'material': os.path.relpath(out_path, ROOT).replace('\\', '/'),
                        'preview': os.path.relpath(prev, ROOT).replace('\\', '/'),
                        'wrapError': round(wrap_error(arr), 1),
                        'seam': seam, 'status': st,
                        'overallStd': round(float(arr.astype(np.float32).std()), 2),
                        'columnMeanStd': round(col_std, 2),
                        'rowMeanStd': round(row_std, 2),
                        'variationOk': vary_ok,
                        'minColumnMeanStd': min_col, 'minRowMeanStd': min_row}
        print('%-12s %-5s %-22s seam mean=%s p95=%s max=%s  std=%s colStd=%s rowStd=%s%s  wrap=%s' % (
            name, st, gen_name, seam['mean'], seam['p95'], seam['max'],
            report[name]['overallStd'], report[name]['columnMeanStd'],
            report[name]['rowMeanStd'], '' if vary_ok else '  [VARIATION-LOW]',
            report[name]['wrapError']))

    log_dir = wp.tools_log_root()
    wp.ensure_dir(log_dir)
    with io.open(report_path, 'w',
                 encoding='utf-8', newline='\n') as fh:
        json.dump(report, fh, ensure_ascii=False, indent=2)
    worst = [r['status'] for r in report.values()]
    overall = 'FAIL' if 'FAIL' in worst else ('WARN' if 'WARN' in worst else 'PASS')
    # variationOk 必须参与退出码：否则 colStd/rowStd 跌破配置阈值只打印一行提示，
    # exit 仍是 0，等于这道保险没接上（Gate C1-A 对抗性复核发现）。
    low_variation = [k for k, r in report.items() if not r.get('variationOk', True)]
    if low_variation:
        overall = 'FAIL'
        print('VARIATION-FAIL: %s (colStd/rowStd below configured minimum)'
              % ', '.join(sorted(low_variation)))
    print('OVERALL: %s' % overall)
    return 1 if overall == 'FAIL' else 0


if __name__ == '__main__':
    sys.exit(main())
