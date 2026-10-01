# -*- coding: utf-8 -*-
"""修补正面立绘的腿部断层：灯笼裤下缘与绑腿之间的透明缺口（AI 生图缺陷），
在腿部 x 范围内对垂直透明段做上下像素线性插值桥接。只处理 v>0.68 的腿部区，
段长上限 46px（约 6% 身高），避免误伤背景。"""
import numpy as np
from PIL import Image
import sys

files = sys.argv[1:] or [
    'GameClient/TwelveJade/Assets/Resources/Art/farmer-female-0-front.png',
    'GameClient/TwelveJade/Assets/Resources/Art/farmer-male-0-front.png',
    'GameClient/TwelveJade/Assets/Resources/Art/merchant-male-0-front.png',
]
MAX_GAP = 46
for path in files:
    img = np.array(Image.open(path).convert('RGBA'))
    h, w = img.shape[:2]
    alpha = img[:, :, 3]
    filled = 0
    for x in range(int(.25 * w), int(.75 * w)):
        col = alpha[:, x]
        y = int(.68 * h)
        while y < h:
            if col[y] < 10:
                y0 = y
                while y < h and col[y] < 10:
                    y += 1
                y1 = y
                if y0 > int(.68 * h) and y1 < h and col[y0 - 1] > 10 and col[y1] > 10 \
                        and (y1 - y0) <= MAX_GAP:
                    top = img[y0 - 1, x].astype(float)
                    bot = img[y1, x].astype(float)
                    for i, yy in enumerate(range(y0, y1)):
                        t = (i + 1) / (y1 - y0 + 1)
                        img[yy, x] = (top * (1 - t) + bot * t).astype(np.uint8)
                    filled += 1
            else:
                y += 1
    Image.fromarray(img).save(path)
    print(path.split('/')[-1], 'bridged columns:', filled)
