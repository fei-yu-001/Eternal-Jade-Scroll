# -*- coding: utf-8 -*-
"""修补正面立绘的腿部断层：灯笼裤/袍摆下缘与绑腿之间的透明缺口（AI 生图缺陷）。

两种填充策略（对腿部 x 范围内 v>0.68 的垂直透明段）：
  - 段长 ≤ 20px：上下像素线性插值（小缝平滑缝合）；
  - 段长 ≤ 90px：用缺口上缘的末 3px 平均色**向下延伸**（裤腿/袍摆自然变长），
    底部 25% 与下缘颜色混合过渡——大缺口插值会出现模糊带，延伸更自然。
只处理 v>0.68 的腿部区，避免误伤背景。可重复运行（幂等：修完就没有透明段了）。"""
import numpy as np
from PIL import Image
import sys

files = sys.argv[1:] or [
    'GameClient/TwelveJade/Assets/Resources/Art/farmer-female-0-front.png',
    'GameClient/TwelveJade/Assets/Resources/Art/farmer-male-0-front.png',
    'GameClient/TwelveJade/Assets/Resources/Art/merchant-male-0-front.png',
    'GameClient/TwelveJade/Assets/Resources/Art/farmer-female-1-front.png',
    'GameClient/TwelveJade/Assets/Resources/Art/farmer-male-1-front.png',
    'GameClient/TwelveJade/Assets/Resources/Art/merchant-female-0-front.png',
    'GameClient/TwelveJade/Assets/Resources/Art/merchant-female-1-front.png',
    'GameClient/TwelveJade/Assets/Resources/Art/merchant-male-1-front.png',
    'GameClient/TwelveJade/Assets/Resources/Art/traveller-female-1-front.png',
    'GameClient/TwelveJade/Assets/Resources/Art/traveller-male-0-front.png',
    'GameClient/TwelveJade/Assets/Resources/Art/traveller-male-1-front.png',
    'GameClient/TwelveJade/Assets/Resources/Art/traveller-female-0-front.png',
]
MAX_GAP, SMOOTH = 90, 20
for path in files:
    try:
        img = np.array(Image.open(path).convert('RGBA'))
    except FileNotFoundError:
        print(path.split('/')[-1], 'skip (missing)')
        continue
    h, w = img.shape[:2]
    alpha = img[:, :, 3]
    filled = 0
    for x in range(int(.22 * w), int(.80 * w)):
        col = alpha[:, x]
        y = int(.68 * h)
        while y < h:
            if col[y] < 10:
                y0 = y
                while y < h and col[y] < 10:
                    y += 1
                y1 = y
                gap = y1 - y0
                if y0 > int(.68 * h) and y1 < h and col[y0 - 1] > 10 and col[y1] > 10 \
                        and gap <= MAX_GAP:
                    if gap <= SMOOTH:
                        top = img[y0 - 1, x].astype(float)
                        bot = img[y1, x].astype(float)
                        for i, yy in enumerate(range(y0, y1)):
                            t = (i + 1) / (gap + 1)
                            img[yy, x] = (top * (1 - t) + bot * t).astype(np.uint8)
                    else:
                        band = img[y0 - 3:y0, x].astype(float).mean(axis=0)
                        for i, yy in enumerate(range(y0, y1)):
                            t = i / gap
                            blend = min(1.0, max(0.0, (t - .75) / .25))
                            c = band if t < .75 else band * (1 - blend) + img[y1, x].astype(float) * blend
                            a = alpha[y0 - 1, x] if t < .75 else max(alpha[y0 - 1, x], alpha[y1, x])
                            img[yy, x, :3] = c[:3].astype(np.uint8)
                            img[yy, x, 3] = a
                    filled += 1
            else:
                y += 1
    # 注意：不要加"水平方向"的修补——腿间空隙在单行上与断层几何特征相同
    # （左右都是腿、中间透明），水平填充会把两腿粘成一块补丁（已在 v5 布局轮验证过）。
    # 侧向开放的少量残留缺口靠 turnaround prompt 的服装连续性要求重新生图治本。
    Image.fromarray(img).save(path)
    print(path.split('/')[-1], 'bridged columns:', filled)
