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
import json, math, os
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

CHUNKS = 8
CHUNK_PX = 1024
WORLD = CHUNKS * CHUNK_PX
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
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
ROAD_HALF = 70.0
PLAZA = (4096.0, 4700.0, 620.0)     # 镇中心 cx,cy,r
MARKET = (3100.0, 4950.0, 340.0)    # 集市 cx,cy,r
TOWN_R = 1100.0            # 镇区（可走硬地）半径参考
CROSS_Y = [4300.0, 3500.0, 6000.0]
WEST_X, EAST_X = 2400.0, 5800.0
BRIDGE_X = [4096.0, 2400.0, 5800.0]  # 主桥 + 两便桥
FERRY_X = 5100.0
SOUTH_GATE_Y = 6300.0
SPAWN = (MAIN_X, 7900.0)   # 世界南缘的镇外：到后山 7700 单位，奔跑 ~16 秒、步行 ~30 秒   # 南门外的镇外官道：离北侧后山 ~5900 单位，奔跑约 13 秒

def mtn_edge(x):
    return MOUNTAIN_Y + 150 * np.sin(np.asarray(x) / 1200.0 + .3) + 60 * np.sin(np.asarray(x) / 470.0)

def river_center(x):
    return RIVER_Y + 170 * np.sin(np.asarray(x) / 980.0) + 80 * np.sin(np.asarray(x) / 430.0 + 1.1)

def main_x(y):
    return MAIN_X + 140 * np.sin(np.asarray(y) / 1500.0)

CODES = ['grass', 'grass2', 'field', 'hill', 'mountain', 'rock', 'water', 'bank',
         'road', 'stone', 'plaza', 'town', 'trail']
LUT = np.array([K[c] for c in CODES], dtype=np.float32)

def render(x0, y0, w, h, scale=1.0):
    """灰盒渲染：世界坐标 -> 类型码 -> LUT 上色。scale = 世界单位/像素。"""
    xs = x0 + (np.arange(w) + .5) * scale
    ys = y0 + (np.arange(h) + .5) * scale
    gx = np.repeat(xs[None, :], h, axis=0)
    gy = np.repeat(ys[:, None], w, axis=1)

    code = np.zeros((h, w), dtype=np.uint8)          # 0 grass
    cy = river_center(gx)
    dist_town = np.sqrt((gx - PLAZA[0]) ** 2 + (gy - PLAZA[1]) ** 2)
    dist_mkt = np.sqrt((gx - MARKET[0]) ** 2 + (gy - MARKET[1]) ** 2)

    is_mtn = gy < mtn_edge(gx)
    code[is_mtn] = 4
    code[is_mtn & ((np.sin(gx / 200.) + np.sin(gy / 170.)) > 1.2)] = 5
    # 山道（穿山通路，可走）
    code[np.abs(gx - (MAIN_X - 300 - 60 * np.sin(gy / 600.0))) < 55] = 12
    # 河与岸
    code[np.abs(gy - cy) < 185] = 7
    code[np.abs(gy - cy) < 125] = 6
    # 桥
    for bx in BRIDGE_X:
        code[(np.abs(gx - bx) < 85) & (np.abs(gy - river_center(bx)) < 250)] = 9
    code[(np.abs(gx - FERRY_X) < 55) & (np.abs(gy - river_center(FERRY_X)) < 260)] = 9
    # 镇区硬地 / 集市 / 广场
    code[dist_town < TOWN_R] = 11
    code[dist_mkt < MARKET[2]] = 11
    code[dist_town < PLAZA[2]] = 10
    # 道路（后写覆盖）
    for ry in CROSS_Y:
        code[np.abs(gy - (ry + 45 * np.sin(gx / 800.0))) < 60] = 8
    code[np.abs(gx - (WEST_X - 40 * np.sin(gy / 900.0))) < 55] = 8
    code[np.abs(gx - (EAST_X + 40 * np.sin(gy / 900.0 + 1.0))) < 55] = 8
    code[np.abs(gx - main_x(gy)) < ROAD_HALF] = 9
    # 农田
    field = ((gy > 2900) & (gy < 4250) & ((gx < 3200) | (gx > 5000))) | \
            ((gy > 5500) & (gy < 6600) & ((gx < 3200) | (gx > 5000)))
    code[field] = np.where((((np.floor(gx / 300) + np.floor(gy / 320)) % 2) == 0)[field], 2, 0)
    # 山脚丘陵
    code[(gy > 1600) & (gy < 2500)] = 3
    # 草地交替
    code[code == 0] = np.where((((np.floor(gx / 320) + np.floor(gy / 320)) % 2) == 0)[code == 0], 0, 1)

    out = LUT[code].copy()
    # 边界线：不可通行区描深色边（灰盒阶段要一眼看出边界）
    img = Image.fromarray(out.astype(np.uint8), 'RGB')
    d = ImageDraw.Draw(img)
    s = w / CHUNK_PX * scale if scale else 1.0
    for x in range(int(x0 - 200), int(x0 + CHUNK_PX + 200), 30):
        px = (x - x0) / scale
        py = (float(mtn_edge(px)) - y0) / scale
        if 0 <= py < h:
            d.line([((x - x0) / scale, py), ((x - x0) / scale, py + 3 / scale)], fill=(70, 74, 80), width=2)
    for x in range(int(x0 - 200), int(x0 + CHUNK_PX + 200), 20):
        px = (x - x0) / scale
        cyv = float(river_center(px))
        for off, col in ((-185, (150, 130, 96)), (125, (120, 150, 176))):
            py = (cyv + off - y0) / scale
            if 0 <= py < h:
                d.line([((x - x0) / scale, py), ((x - x0) / scale, py + 3 / scale)], fill=col, width=2)
    return img.filter(ImageFilter.GaussianBlur(0.8))

def main():
    os.makedirs(OUT_GREY, exist_ok=True)
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
    for gx in range(0, WORLD, 16):
        xm = gx + 8
        cyv = float(river_center(xm))
        top, bot = int(cyv - 200), int(cyv + 200)
        on_bridge = any(abs(xm - bx) < 90 for bx in BRIDGE_X) or abs(xm - FERRY_X) < 60
        for gy in range(max(0, top), min(WORLD, bot), 16):
            if on_bridge:
                continue
            blocked.append({'x': gx, 'y': gy, 'width': 16, 'height': 16, 'why': 'river'})

    chunks = [{'id': f'r{r}-c{c}', 'row': r, 'col': c,
               'art': f'WorldGreybox/chunk-r{r}-c{c}',
               'overview': f'WorldGreybox/world-overview',
               'x': c * CHUNK_PX, 'y': r * CHUNK_PX, 'width': CHUNK_PX, 'height': CHUNK_PX}
              for r in range(CHUNKS) for c in range(CHUNKS)]
    landmarks = [
        {'id': 'south-gate', 'name': '南门', 'x': 4096, 'y': 6300},
        {'id': 'town-center', 'name': '镇中心', 'x': 4096, 'y': 4700},
        {'id': 'market', 'name': '集市', 'x': 3100, 'y': 4950},
        {'id': 'north-street', 'name': '北街', 'x': 4096, 'y': 3600},
        {'id': 'river-north', 'name': '北桥', 'x': 4096, 'y': int(river_center(4096))},
        {'id': 'ferry', 'name': '渡口', 'x': FERRY_X, 'y': int(river_center(FERRY_X)) + 240},
        {'id': 'foothill', 'name': '山脚', 'x': 4096, 'y': 2100},
        {'id': 'back-mountain', 'name': '后山', 'x': 3800, 'y': 300},
        {'id': 'east-street', 'name': '东街', 'x': EAST_X, 'y': 4300},
        {'id': 'west-street', 'name': '西街', 'x': WEST_X, 'y': 4200},
        {'id': 'farmland', 'name': '南郊农田', 'x': 2400, 'y': 5900},
        {'id': 'outskirts', 'name': '镇外', 'x': 4096, 'y': 7900},
    ]
    json.dump({'worldWidth': WORLD, 'worldHeight': WORLD, 'chunkSize': CHUNK_PX,
               'chunks': chunks, 'blocked': blocked, 'landmarks': landmarks,
               'spawn': {'x': SPAWN[0], 'y': SPAWN[1]}},
              open(OUT_CFG, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    print('config written, blocked:', len(blocked))

if __name__ == '__main__':
    main()
