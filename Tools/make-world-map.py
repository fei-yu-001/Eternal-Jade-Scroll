# -*- coding: utf-8 -*-
"""青石镇大世界：程序化生成 5x5 分块地形图 + 世界概览图 + 同步产出坐标配置。

为什么程序化而不是生图：大世界的硬性要求是**分块之间无缝**（路要连续、河要连续、
山体边界要跨块对齐）。AI 生图无法保证 25 块之间像素级连续；程序化按**世界坐标**
统一求值再按块采样，天然无缝。每块 1024px PNG 独立成文件，后期可逐块替换成
手绘/AI 美术而不影响坐标（本脚本同时产出配置，图与坐标不会脱节）。

世界布局（世界单位 = 像素，5120 x 5120）：
    y    0- 950   后山（不可通行，山径除外）
    y  950-1980   山脚丘陵（可走，碎石点缀）
    y 1980-2300   青石河（不可通行，北桥/西桥/渡口栈桥可过）
    y 2300-3400   北岸田野（可走）+ 北街
    y 3400-4250   镇中心/集市（可走开阔地）
    y 4250-5120   南郊农田 + 南门 + 镇外官道（出画）
横向：x 2000-3100 主街；x <2000 西街/西坡；x >3100 东街/东岸。
输出：
    Assets/Resources/World/chunk-r{row}-c{col}.png   （25 块，1024x1024）
    Assets/Resources/World/world-overview.png        （概览，小地图用）
    Resources/Config/town-world-gen.json            （坐标配置片段）
"""
import json, math, os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

# Windows GBK 控制台下 print 中文/箭头会 UnicodeEncodeError 把整道门禁崩掉——
# 一个会崩的门禁比一个红的门禁危险得多（架构师 2026-10-05 定规，全仓 Tools/*.py 入口统一）。
import sys
for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, 'reconfigure'):
        _stream.reconfigure(encoding='utf-8', errors='replace')

CHUNKS = 5
CHUNK_PX = 1024
WORLD = CHUNKS * CHUNK_PX          # 5120
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_IMG = os.path.join(ROOT, 'GameClient', 'TwelveJade', 'Assets', 'Resources', 'World')
OUT_CFG = os.path.join(ROOT, 'GameClient', 'TwelveJade', 'Assets', 'Resources', 'Config', 'town-world-gen.json')

# ---- 调色（水彩淡彩，贴 #EDE6D6 纸底）----
PAPER = (237, 230, 214)
C = {
    'mountain': (126, 134, 118),   # 山体 灰绿
    'rock':     (158, 160, 146),   # 岩石点缀
    'hill':     (172, 176, 142),   # 山脚丘陵
    'water':    (150, 182, 184),   # 河水
    'bank':     (206, 194, 158),   # 河滩
    'field':    (206, 194, 146),   # 田
    'field2':   (192, 186, 132),
    'grass':    (176, 186, 140),   # 草地
    'grass2':   (166, 178, 132),
    'road':     (214, 202, 176),   # 土路
    'stone':    (198, 198, 190),   # 石板主街
    'plaza':    (206, 202, 192),   # 集市广场 圆石
}

# 类型码 0..14 的颜色表（顺序必须与 render_window 里的赋值一致）
SUB_COLORS = [
    C['grass'],      # 0 草地 A
    C['mountain'],   # 1 山体
    C['rock'],       # 2 岩石
    C['road'],       # 3 山径
    C['bank'],       # 4 河滩
    C['water'],      # 5 河水
    C['stone'],      # 6 桥面
    C['plaza'],      # 7 集市广场
    C['road'],       # 8 横街
    C['stone'],      # 9 主街
    C['road'],       # 10 东西街
    C['field'],      # 11 田 A
    C['field2'],     # 12 田 B
    C['hill'],       # 13 山脚丘陵
    C['grass2'],     # 14 草地 B
]

# ---- 世界特征（全部是 x/y 的确定性函数 → 跨块天然连续）----
def river_center(x):
    return 2140 + 150 * math.sin(x / 950.0) + 70 * math.sin(x / 420.0 + 1.2)

def mountain_edge(x):
    return 900 + 130 * math.sin(x / 1100.0 + .4) + 55 * math.sin(x / 430.0)

def main_road_x(y):
    return 2560 + 120 * math.sin(y / 1400.0)

WEST_ROAD_X = 1500
EAST_ROAD_X = 3700
CROSS_ROADS = [3450, 2750]          # 两条横街
ROAD_HALF = 60
PLAZA = (2560, 3860, 430)           # 镇中心广场 (cx, cy, r)
MARKET = (1900, 3950)               # 集市
BRIDGES = [(2560, 70, 'north'), (1180, 55, 'west')]   # (x, halfwidth, name)
FERRY = (3320, 2290)               # 渡口栈桥（南岸）
TRAIL_TOP = (2380, 60)             # 山径顶端

def classify(x, y):
    """返回 (类型, 次类型)。世界坐标 → 地形类型。"""
    # 山
    if y < mountain_edge(x):
        return ('mountain', 'rock' if (math.sin(x / 180.0) + math.sin(y / 150.0)) > 1.3 else 'mtn2')
    # 山径
    if abs(x - TRAIL_TOP[0] - 40 * math.sin(y / 500.0)) < 46:
        return ('road', 'trail')
    # 河
    if abs(y - river_center(x)) < 130:
        return ('water', 'deep')
    if abs(y - river_center(x)) < 190:
        return ('bank', 'bank')
    # 桥
    for bx, hw, _ in BRIDGES:
        if abs(x - bx) < hw and abs(y - river_center(x)) < 230:
            return ('stone', 'bridge')
    # 渡口栈桥
    if abs(x - FERRY[0]) < 45 and abs(y - river_center(FERRY[0])) < 240:
        return ('stone', 'bridge')
    # 广场 / 集市
    if (x - PLAZA[0]) ** 2 + (y - PLAZA[1]) ** 2 < PLAZA[2] ** 2:
        return ('plaza', 'cobble')
    # 道路
    if abs(x - main_road_x(y)) < ROAD_HALF:
        return ('stone', 'main')
    for ry in CROSS_ROADS:
        if abs(y - ry - 40 * math.sin(x / 700.0)) < 52:
            return ('road', 'cross')
    if abs(x - WEST_ROAD_X - 30 * math.sin(y / 800.0)) < 46:
        return ('road', 'west')
    if abs(x - EAST_ROAD_X - 30 * math.sin(y / 800.0 + 1.0)) < 46:
        return ('road', 'east')
    # 田 / 草
    if 2300 < y < 3400 and (x < 2050 or x > 3100):
        grid = ((x // 260) + (y // 300)) % 2
        return ('field', 'f1' if grid else 'f2')
    if 4300 < y and (x < 2150 or x > 3000):
        grid = ((x // 240) + (y // 280)) % 2
        return ('field', 'f1' if grid else 'f2')
    if 950 < y < 1980:
        return ('hill', 'hill')
    if y < 1980:
        return ('grass', 'g1' if ((x // 300) + (y // 300)) % 2 else 'g2')
    return ('grass', 'g1' if ((x // 300) + (y // 300)) % 2 else 'g2')

# 类型 → 颜色
COLOR_OF = {
    'mtn2': C['mountain'], 'rock': C['rock'], 'trail': C['road'],
    'deep': C['water'], 'bank': C['bank'], 'bridge': C['stone'],
    'cobble': C['plaza'], 'main': C['stone'], 'cross': C['road'],
    'west': C['road'], 'east': C['road'],
    'f1': C['field'], 'f2': C['field2'],
    'g1': C['grass'], 'g2': C['grass2'], 'hill': C['hill'],
}

def render_window(x0, y0, w, h, scale=1):
    """渲染世界坐标窗口为 RGB 图（w,h 输出像素；scale=世界单位/像素）。全 numpy 向量化。

    用整数类型码 + 颜色 LUT 上色：对象数组的布尔索引在混用 np.where 时会广播出错，
    整数编码没有这类坑。
    """
    # scale = 世界单位/像素：世界坐标 = 起点 + 像素序号 * scale（写反会只渲染左上角）
    xs = x0 + (np.arange(w) + .5) * scale
    ys = y0 + (np.arange(h) + .5) * scale
    gx = np.repeat(xs[None, :], h, axis=0)
    gy = np.repeat(ys[:, None], w, axis=1)

    # 类型码：0=草地 其余见 SUB_NAMES
    code = np.zeros((h, w), dtype=np.uint8)
    cy = river_center_arr(gx)
    is_water = np.abs(gy - cy) < 130
    is_bank = (~is_water) & (np.abs(gy - cy) < 190)

    # 顺序：山 → 山径 → 河/岸 → 桥 → 广场 → 路 → 田 → 丘陵 → 草（后写覆盖先写）
    mtn_edge = 900 + 130 * np.sin(gx / 1100.0 + .4) + 55 * np.sin(gx / 430.0)
    is_mtn = gy < mtn_edge
    code[is_mtn] = 1                                          # mountain
    code[is_mtn & ((np.sin(gx / 180.0) + np.sin(gy / 150.0)) > 1.3)] = 2  # rock（只在山体内）
    code[np.abs(gx - (TRAIL_TOP[0] - 40 * np.sin(gy / 500.0))) < 46] = 3   # trail
    code[is_bank] = 4                                          # bank
    code[is_water] = 5                                         # water
    for bx, hw, _ in BRIDGES:
        code[(np.abs(gx - bx) < hw) & (np.abs(gy - river_center(bx)) < 230)] = 6
    code[(np.abs(gx - FERRY[0]) < 45) & (np.abs(gy - river_center(FERRY[0])) < 240)] = 6
    code[((gx - PLAZA[0]) ** 2 + (gy - PLAZA[1]) ** 2) < PLAZA[2] ** 2] = 7   # plaza
    code[np.abs(gy - (CROSS_ROADS[0] + 40 * np.sin(gx / 700.0))) < 52] = 8    # 交叉口
    code[np.abs(gy - (CROSS_ROADS[1] + 40 * np.sin(gx / 700.0))) < 52] = 8
    code[np.abs(gx - main_road_x_arr(gy)) < ROAD_HALF] = 9                      # main
    code[np.abs(gx - (WEST_ROAD_X - 30 * np.sin(gy / 800.0))) < 46] = 10
    code[np.abs(gx - (EAST_ROAD_X + 30 * np.sin(gy / 800.0 + 1.0))) < 46] = 10
    field = ((gy > 2300) & (gy < 3400) & ((gx < 2050) | (gx > 3100))) | \
            ((gy > 4300) & ((gx < 2150) | (gx > 3000)))
    code[field] = np.where((((np.floor(gx / 260) + np.floor(gy / 300)) % 2) == 0)[field], 11, 12)
    code[(gy > 950) & (gy < 1980)] = 13                       # 山脚丘陵
    # 草：交替色，只覆盖仍是默认（0）的像素
    code[code == 0] = np.where((((np.floor(gx / 300) + np.floor(gy / 300)) % 2) == 0)[code == 0], 0, 14)

    lut = np.array(SUB_COLORS, dtype=np.float32)
    out = lut[code]
    rng = np.random.default_rng(abs(int(x0 * 31 + y0 * 17)) % (2 ** 31))
    out = np.clip(out + rng.normal(0, 9, (h, w, 1)), 0, 255)
    img = Image.fromarray(out.astype(np.uint8), 'RGB')
    return img.filter(ImageFilter.GaussianBlur(1.6))   # 柔化去硬边，出水彩洗色感

def river_center_arr(x):
    return 2140 + 150 * np.sin(x / 950.0) + 70 * np.sin(x / 420.0 + 1.2)

def main_road_x_arr(y):
    return 2560 + 120 * np.sin(y / 1400.0)

def draw_details(img, x0, y0):
    """在块上叠矢量细节：路缘、河岸、桥板、田埂。"""
    d = ImageDraw.Draw(img, 'RGBA')
    s = img.width / CHUNK_PX
    def P(x, y):
        return ((x - x0) * s, (y - y0) * s)
    # 主街两侧路缘
    pts = [P(main_road_x(y), y) for y in range(y0 - 80, y0 + CHUNK_PX + 80, 40)]
    for side in (-1, 1):
        d.line([(px + side * ROAD_HALF * s, py) for px, py in pts], fill=(150, 140, 116, 120), width=max(1, int(2 * s)))
    # 横街
    for ry in CROSS_ROADS:
        pts = [P(x, ry + 40 * math.sin(x / 700.0)) for x in range(x0 - 80, x0 + CHUNK_PX + 80, 40)]
        d.line(pts, fill=(150, 140, 116, 110), width=max(1, int(2 * s)))
    # 河岸线
    pts = [P(x, river_center(x) - 190) for x in range(x0 - 40, x0 + CHUNK_PX + 40, 24)]
    d.line(pts, fill=(140, 132, 104, 130), width=max(1, int(2 * s)))
    pts = [P(x, river_center(x) + 190) for x in range(x0 - 40, x0 + CHUNK_PX + 40, 24)]
    d.line(pts, fill=(140, 132, 104, 130), width=max(1, int(2 * s)))
    # 桥板
    for bx, hw, _ in BRIDGES + [(FERRY[0], 45, 'ferry')]:
        cy = river_center(bx)
        for yy in range(int(cy - 220), int(cy + 220), 26):
            d.line([P(bx - hw, yy), P(bx + hw, yy)], fill=(150, 148, 140, 150), width=max(1, int(1.6 * s)))
    # 田埂
    for gy in range(2320, 3400, 120):
        if any(abs(gy - ry) < 80 for ry in CROSS_ROADS):
            continue
        d.line([P(x0, gy), P(x0 + CHUNK_PX, gy)], fill=(178, 166, 128, 70), width=1)
    for gy in range(4320, WORLD, 120):
        d.line([P(x0, gy), P(x0 + CHUNK_PX, gy)], fill=(178, 166, 128, 70), width=1)
    # 山脊线
    pts = [P(x, mountain_edge(x)) for x in range(x0 - 40, x0 + CHUNK_PX + 40, 24)]
    d.line(pts, fill=(104, 112, 100, 160), width=max(1, int(2.4 * s)))
    return img

def main():
    os.makedirs(OUT_IMG, exist_ok=True)
    os.makedirs(os.path.dirname(OUT_CFG), exist_ok=True)
    # 1) 分块
    for row in range(CHUNKS):
        for col in range(CHUNKS):
            x0, y0 = col * CHUNK_PX, row * CHUNK_PX
            img = render_window(x0, y0, CHUNK_PX, CHUNK_PX)
            img = draw_details(img, x0, y0)
            path = os.path.join(OUT_IMG, f'chunk-r{row}-c{col}.png')
            img.save(path)
            print('chunk', row, col, 'ok', flush=True)
    # 2) 世界概览（1/5 采样，直接反映真实地形）
    ov = render_window(0, 0, WORLD // 5, WORLD // 5, scale=5)
    ov = draw_details(ov, 0, 0)
    ov.save(os.path.join(OUT_IMG, 'world-overview.png'))
    print('overview ok', ov.size, flush=True)
    # 3) 坐标配置：blocked（山/河/水塘）+ 地标
    blocked = []
    # 山：按 64px 网格采样判定
    for gy in range(0, 1000, 64):
        for gx in range(0, WORLD, 64):
            if classify(gx + 32, gy + 32)[0] == 'mountain' and abs(gx + 32 - (TRAIL_TOP[0] - 40 * math.sin((gy + 32) / 500.0))) > 44:
                blocked.append({'x': gx, 'y': gy, 'width': 64, 'height': 64, 'why': 'mountain'})
    # 河：逐列 32px 高条，桥与渡口留口
    for gx in range(0, WORLD, 32):
        cy = river_center(gx + 16)
        top, bot = int(cy - 200), int(cy + 200)
        for gy in range(max(0, top), min(WORLD, bot), 32):
            x = gx + 16
            on_bridge = any(abs(x - bx) < hw + 6 for bx, hw, _ in BRIDGES) or abs(x - FERRY[0]) < 52
            if on_bridge:
                continue
            blocked.append({'x': gx, 'y': gy, 'width': 32, 'height': 32, 'why': 'river'})
    # 山径可走通道不封
    landmarks = [
        {'id': 'south-gate', 'name': '南门', 'x': 2560, 'y': 4700},
        {'id': 'town-center', 'name': '镇中心', 'x': PLAZA[0], 'y': PLAZA[1]},
        {'id': 'market', 'name': '集市', 'x': MARKET[0], 'y': MARKET[1]},
        {'id': 'north-street', 'name': '北街', 'x': 2560, 'y': 3000},
        {'id': 'north-bridge', 'name': '北桥', 'x': 2560, 'y': int(river_center(2560))},
        {'id': 'west-bridge', 'name': '西桥', 'x': 1180, 'y': int(river_center(1180))},
        {'id': 'ferry', 'name': '渡口', 'x': FERRY[0], 'y': FERRY[1]},
        {'id': 'foothill', 'name': '山脚', 'x': 2560, 'y': 1500},
        {'id': 'back-mountain', 'name': '后山', 'x': TRAIL_TOP[0], 'y': 120},
        {'id': 'east-street', 'name': '东街', 'x': EAST_ROAD_X, 'y': 3400},
        {'id': 'west-street', 'name': '西街', 'x': WEST_ROAD_X, 'y': 3200},
    ]
    chunks = [{'id': f'r{r}-c{c}', 'row': r, 'col': c, 'art': f'World/chunk-r{r}-c{c}',
               'x': c * CHUNK_PX, 'y': r * CHUNK_PX, 'width': CHUNK_PX, 'height': CHUNK_PX}
              for r in range(CHUNKS) for c in range(CHUNKS)]
    cfg = {
        'worldWidth': WORLD, 'worldHeight': WORLD, 'chunkSize': CHUNK_PX,
        'chunks': chunks, 'blocked': blocked, 'landmarks': landmarks,
        'spawn': {'x': 2560, 'y': 4980},   # 南门外官道
    }
    with open(OUT_CFG, 'w', encoding='utf-8') as f:
        json.dump(cfg, f, ensure_ascii=False, indent=1)
    print('config written:', OUT_CFG, 'blocked rects:', len(blocked))

if __name__ == '__main__':
    main()
