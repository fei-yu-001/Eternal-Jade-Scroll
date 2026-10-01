# -*- coding: utf-8 -*-
"""全景底图占位：程序化画一张 45° 斜视角透视街道（等真图到位后直接覆盖同名文件）。

布局契约（与 town-map.json 的走廊/道具配置一一对应，改这里要同步那边）：
  - 近景（y 820-1080）：宽阔石板空地，留给大比例建筑构件（residence/shop-front）。
  - 中景（y 480-820）：街道收窄，两侧草地留摆摊位/灯杆。
  - 远景（y 260-480）：街道尽头，画进底图的建筑群剪影 + 远山，人物走不到。
"""
from PIL import Image, ImageDraw, ImageFilter

W, H = 1920, 1080
IVORY = (237, 230, 214)      # #EDE6D6 底色
GRASS = (184, 188, 144)      # 草地
ROAD = (217, 201, 164)       # 石板路
ROAD_LINE = (196, 178, 140)  # 板缝
ROOF = (142, 148, 142)       # 远景瓦顶
HILL = (154, 167, 142)       # 远山

img = Image.new('RGB', (W, H), IVORY)
d = ImageDraw.Draw(img)

# 远景天带与远山
d.rectangle([0, 0, W, 300], fill=(232, 224, 204))
d.polygon([(0, 300), (260, 150), (520, 300)], fill=HILL)
d.polygon([(420, 300), (760, 120), (1100, 300)], fill=(150, 163, 140))
d.polygon([(980, 300), (1350, 160), (1700, 300)], fill=HILL)
d.polygon([(1550, 300), (1800, 190), (1920, 300)], fill=(150, 163, 140))

# 远景建筑群剪影（画进底图的中远景，人物不可达）
row_y, row_h = 210, 92
x = 40
random_x = 0.0
import random
random.seed(7)
while x < W:
    w = random.randint(90, 170)
    h = random.randint(64, row_h)
    y0 = 302 - h
    d.rectangle([x, y0 + h * .45, x + w, 302], fill=(226, 216, 196))
    d.polygon([(x - 8, y0 + h * .45), (x + w / 2, y0), (x + w + 8, y0 + h * .45)],
              fill=ROOF)
    x += w + random.randint(14, 44)

# 街外草地
d.rectangle([0, 300, W, H], fill=GRASS)

# 主街梯形：底 (640,1080)-(1280,1080) → 顶 (880,300)-(1040,300)
d.polygon([(640, 1080), (1280, 1080), (1040, 300), (880, 300)], fill=ROAD)
# 路缘
d.line([(640, 1080), (880, 300)], fill=(120, 112, 88), width=6)
d.line([(1280, 1080), (1040, 300)], fill=(120, 112, 88), width=6)

# 支巷（与 town-map.json 的 G1/G2 走廊契约一致）：西巷 y 690-770 向西、东巷 y 560-640 向东。
LANE = (205, 189, 152)
d.polygon([(560, 690), (560, 770), (860, 770), (860, 690)], fill=LANE)
d.line([(560, 690), (860, 690)], fill=(150, 140, 110), width=3)
d.line([(560, 770), (860, 770)], fill=(150, 140, 110), width=3)
d.polygon([(1010, 560), (1010, 640), (1400, 640), (1400, 560)], fill=LANE)
d.line([(1010, 560), (1400, 560)], fill=(150, 140, 110), width=3)
d.line([(1010, 640), (1400, 640)], fill=(150, 140, 110), width=3)
# 西北民居的房基（构件立绘落在它上面）
d.polygon([(390 - 160, 950), (390 + 160, 950), (390 + 130, 870), (390 - 130, 870)],
          fill=(210, 196, 162))

# 石板横缝：透视间隔（近疏远密）——t 幂次参数化，避免几何级数永不到达的死循环。
for i in range(38):
    t = (i / 37.0) ** 1.55
    y = 1080 - 780 * t
    xl = 640 + (880 - 640) * t
    xr = 1280 + (1040 - 1280) * t
    d.line([(xl, y), (xr, y)], fill=ROAD_LINE, width=3)
# 纵缝线束
for f in (.25, .5, .75):
    xb = 640 + 640 * f
    xt = 880 + 160 * f
    d.line([(xb, 1080), (xt, 300)], fill=ROAD_LINE, width=2)

# 轻噪点与柔化，去掉"矢量图"的干净感
import math
px = img.load()
for _ in range(26000):
    x, y = random.randint(0, W - 1), random.randint(300, H - 1)
    r, g, b = px[x, y]
    k = random.randint(-9, 9)
    px[x, y] = (max(0, r + k), max(0, g + k), max(0, b + k))
img = img.filter(ImageFilter.GaussianBlur(.6))

out = 'GameClient/TwelveJade/Assets/Resources/Art/town-map.jpg'
img.save(out, quality=93)
print('placeholder written:', out, img.size)
