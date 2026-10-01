# -*- coding: utf-8 -*-
"""城镇布局遮挡检查：建筑立绘矩形 × 走廊矩形，报告"人物会被贴图盖住"的区域。

遮挡按对象排序（BaseY）：人物 y < 建筑基座且落在立绘矩形内 → 被盖。
立绘方图四角通常透明，按 72% 的内缩框判定房子本体。
用法：python Tools/check-town-overlap.py [config 路径]
"""
import json, sys, collections

path = sys.argv[1] if len(sys.argv) > 1 else \
    'GameClient/TwelveJade/Assets/Resources/Config/town-map.json'
cfg = json.load(open(path, encoding='utf-8'), object_pairs_hook=collections.OrderedDict)

INSET = .72  # 立绘本体占方图的比例（两侧各留 14% 透明边）

def rect_of(prop):
    h = prop['height'] * INSET
    return (prop['x'] - h / 2, prop['y'] - h, prop['x'] + h / 2, prop['y'])

def intersect(a, b):
    x0, y0 = max(a[0], b[0]), max(a[1], b[1])
    x1, y1 = min(a[2], b[2]), min(a[3], b[3])
    return (x0, y0, x1, y1) if x0 < x1 and y0 < y1 else None

bad = 0
for prop in cfg['props']:
    body = rect_of(prop)
    base = prop['y']
    for w in cfg['walkable']:
        wr = (w['x'], w['y'], w['x'] + w['width'], w['y'] + w['height'])
        hit = intersect(body, wr)
        if not hit: continue
        # 冲突区域 = 交集内"基座之后"的部分（人 y < base → 被盖）
        clash = (hit[0], hit[1], hit[2], min(hit[3], base))
        if clash[1] < clash[3]:
            area = (clash[2] - clash[0]) * (clash[3] - clash[1])
            if area > 400:  # 忽略细缝
                bad += 1
                print('CONFLICT %-14s 基座(%d,%d) × 走廊[%s] 遮挡区 x %d-%d y %d-%d（%d px²）'
                      % (prop['slug'], prop['x'], base, w['comment'][:8],
                         clash[0], clash[2], clash[1], clash[3], area))
print('clean' if bad == 0 else '%d 处冲突' % bad)
sys.exit(1 if bad else 0)
