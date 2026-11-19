# -*- coding: utf-8 -*-
"""MAP-20 建筑体量对照图：把 6 栋"长高后"的建筑 vs 玩家画成一眼能验的图。

口径（全部来自运行时代码 / 门禁，非自定义）：
- PlaceInWorld(FrontEndWorld.cs:735) = RawImage 拉伸铺满 plot.width × plot.height，
  pivot=(0.5,1) → 立绘底边对齐 footY。本工具把立绘拉伸到与 plot 完全同尺寸，
  底边对齐同一条基线，即运行时画面（同尺码、无 worldScale 放大）。
- 玩家参照 = 170px 等身剪影（TravelerHeight=170f），自绘图形，不放真人素材。
- h_over_player 口径 = 立绘 alpha bbox 占高比例 × plot.height / 170
  （与 generate-building-art.py judge() 逐字一致：final_bbox_h_pct/100 × plot_h / PLAYER_HEIGHT）。
  plot.height 即渲染后立绘画布高度，因本工具就是把立绘拉伸到 plot.width×height。
- 目标区间 = [1.5, 3.5]（check-building-scale.py 允许区间）。

只读配置与 Art/，不写任何配置与素材。输出 Tools/logs/map-20/scale-reference.png 与 report.md。
"""
import json
import os
import sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont

# GBK 控制台下 print 中文会 UnicodeEncodeError 崩在结论之前（全仓统一守卫）。
for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, 'reconfigure'):
        _stream.reconfigure(encoding='utf-8', errors='replace')

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CONFIG_PATH = os.path.join(ROOT, 'GameClient', 'TwelveJade', 'Assets', 'Resources', 'Config',
                           'building-plots.json')
ART_ROOT = os.path.join(ROOT, 'GameClient', 'TwelveJade', 'Assets', 'Resources', 'Art', 'Props')

OUT_DIR = os.path.join(ROOT, 'Tools', 'logs', 'map-20')
OUT_PNG = os.path.join(OUT_DIR, 'scale-reference.png')

PLAYER_HEIGHT = 170.0          # FrontEndWorld.TravelerHeight
PLAYER_W = 96.0                # 剪影宽（TravelerHeight * .58 的比例）
H_OVER_PLAYER_MIN, H_OVER_PLAYER_MAX = 1.5, 3.5   # check-building-scale.py 允许区间

# 六件建筑：plot id → 该件目标 h_over_player（art 文件名从 building-plots.json 的 art 字段实读，
# 不硬编码——MAP-22 起 art 可指向 -v2 等新文件，写死会读到旧占位图）
TARGETS = {
    'plot-herbal-garden': 1.50,
    'plot-stone-house': 1.60,
    'plot-blacksmith': 1.80,
    'plot-teahouse': 2.00,
    'plot-temple-gate': 2.15,
    'plot-paifang': 2.80,
}

COLS, ROWS = 3, 2
GRID_W = 1280          # 输出宽
GRID_H = 960           # 输出高（比例 4:3）
CELL_W = GRID_W // COLS   # 426
CELL_H = GRID_H // ROWS   # 480
LABEL_H = 74           # 格顶标注区高
VIEW_TOP = LABEL_H + 8      # 画像区顶边（含边距）
VIEW_BOTTOM = CELL_H - 12   # 画像区底边（基线）

DARK = (40, 40, 40)
MUTED = (96, 96, 96)
LIGHT = (237, 230, 214)   # 背景色与画布一脉：ArtSource 的原色

# 玩家剪影（170px 高）——胶囊身体 + 圆头，简洁可读
def draw_traveler(draw, x_center, foot_y):
    h = int(PLAYER_HEIGHT)
    w_shoulder = int(PLAYER_HEIGHT * .30)   # 肩宽 ~51px
    head_r = int(PLAYER_HEIGHT * .125)      # 头半径 ~21px
    body_top = foot_y - h
    head_c = (x_center, body_top + head_r + 2)
    # 身体（胶囊）：从肩到脚，圆角矩形模拟躯干
    body_l = x_center - w_shoulder // 2
    body_r = x_center + w_shoulder // 2
    neck_y = head_c[1] + head_r
    hip_y = foot_y - int(PLAYER_HEIGHT * .22)
    draw.ellipse([head_c[0]-head_r, head_c[1]-head_r, head_c[0]+head_r, head_c[1]+head_r], fill=DARK)
    draw.rounded_rectangle([body_l, neck_y, body_r, hip_y], radius=w_shoulder//4, fill=DARK)
    # 腿
    draw.rounded_rectangle([body_l + 2, hip_y, x_center - 2, foot_y], radius=6, fill=DARK)
    draw.rounded_rectangle([x_center + 2, hip_y, body_r - 2, foot_y], radius=6, fill=DARK)


def alpha_bbox_h_pct(im):
    """与 generate-building-art.py 同口径：alpha>128 的首末不透明行占画布高。"""
    a = np.array(im.convert('RGBA'))[:, :, 3]
    ys = np.where(a > 128)[0]
    if len(ys) == 0:
        return 0.0
    return (ys.max() - ys.min() + 1) / float(im.size[1])


def load_plot(key):
    base = json.load(open(CONFIG_PATH, encoding='utf-8'))
    for p in base['plots']:
        if p['id'] == key:
            return p
    raise KeyError(key)


def plot_art_file(key):
    """plot 的 art 字段（Art/Props/<name>）→ 文件名 <name>.png。"""
    art = load_plot(key).get('art') or ''
    return os.path.basename(art.replace('\\', '/')) + '.png'


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    targets = list(TARGETS.items())

    # 统一像素尺度：一幅图内所有元素同一 world→px 比例，才能"一眼看出相对高度"。
    max_plot_h = max(load_plot(pid)['height'] for pid in TARGETS)
    avail_h = VIEW_BOTTOM - VIEW_TOP
    scale = avail_h / (max_plot_h * 1.06)   # 最高那栋(牌坊 541)留 6% 天

    grid = Image.new('RGB', (GRID_W, GRID_H), LIGHT)
    draw = ImageDraw.Draw(grid)
    # 清一色浅背景，不画竖线分隔（浅色对比足够）

    # 字体（Windows 常见字体；缺失时退用默认）
    font_path = 'C:/Windows/Fonts/msyh.ttc'
    if os.path.exists(font_path):
        font_l = ImageFont.truetype(font_path, 17)
        font_m = ImageFont.truetype(font_path, 13)
        print('font: %s (中文标注可用)' % font_path)
    else:
        font_l = font_m = ImageFont.load_default()
        print('font: PIL default（缺 msyh.ttc，中文标注可能不可读）')

    rows = []
    for i, (pid, target) in enumerate(targets):
        plot = load_plot(pid)
        w, h = int(plot['width']), int(plot['height'])
        artfile = plot_art_file(pid)
        art = Image.open(os.path.join(ART_ROOT, artfile)).convert('RGBA')
        # 运行时同款：立绘拉伸铺满 plot.width×plot.height（PlaceInWorld stretch）
        art = art.resize((w, h), Image.LANCZOS)
        bbox_pct = alpha_bbox_h_pct(Image.open(os.path.join(ART_ROOT, artfile)))
        h_over_player = bbox_pct * h / PLAYER_HEIGHT

        # 格内像素坐标
        col, row = i % COLS, i // COLS
        cx = col * CELL_W          # 格左
        cy = row * CELL_H          # 格顶
        # 建筑底边对齐基线；foot 在底部(墙脚/底座)，正轴朝上
        pw, ph = w * scale, h * scale
        x0 = cx + (CELL_W - pw) / 2   # 水平居中
        foot_y = cy + VIEW_BOTTOM     # 每行基线随行走
        art_top = foot_y - ph

        # 画建筑（透明像素用 alpha mask 混到底色）
        grid.paste(art, (int(x0), int(art_top)), art)

        # 玩家参照：建筑右侧，同一基线站定（画在建筑之上，不被盖住）
        p_scale = scale
        ply_x = x0 + pw + 30 * 1       # 靠右固定间距
        # 保证不超格右界，必要时右对齐压缩
        if ply_x + 90 > cx + CELL_W - 8:
            ply_x = cx + CELL_W - 8 - 90
        if ply_x < x0 + pw + 8:
            ply_x = x0 + pw + 8
        draw_traveler(draw, int(ply_x), foot_y)
        draw.text((int(ply_x) - 22, foot_y - int(PLAYER_HEIGHT * scale) - 16),
                  '玩家 170', font=font_m, fill=MUTED, anchor='la')

        # 标注区（格顶）
        label_cx = cx + CELL_W / 2
        draw.text((label_cx - CELL_W/2 + 10, cy + 6), '%s' % pid,
                  font=font_l, fill=DARK, anchor='la')
        draw.text((label_cx - CELL_W/2 + 10, cy + 28),
                  'plot %dw x %dh' % (w, h), font=font_m, fill=MUTED, anchor='la')
        draw.text((label_cx - CELL_W/2 + 10, cy + 46),
                  'h/player=%.2f  target=%.2f  [%s,%s]' % (
                      h_over_player, target, H_OVER_PLAYER_MIN, H_OVER_PLAYER_MAX),
                  font=font_m, fill=DARK, anchor='la')
        draw.text((label_cx + 8, cy + 46),
                  'OK' if H_OVER_PLAYER_MIN <= h_over_player <= H_OVER_PLAYER_MAX else 'short',
                  font=font_m, fill=(180, 60, 60) if h_over_player < H_OVER_PLAYER_MIN else (60, 130, 60),
                  anchor='la')

        rows.append({
            'id': pid, 'plot': [w, h], 'art': artfile,
            'bbox_h_pct': round(bbox_pct, 4),
            'h_over_player': round(h_over_player, 3),
            'target_h_over_player': target,
            'range': [H_OVER_PLAYER_MIN, H_OVER_PLAYER_MAX],
        })

    grid.save(OUT_PNG)
    print('written: %s  %dx%d' % (OUT_PNG, GRID_W, GRID_H))
    for r in rows:
        print('  %-22s plot=%s art=%-16s bbox_h=%.0f%%  h/player=%.3f  target=%.2f  %s' % (
            r['id'], r['plot'], r['art'], r['bbox_h_pct']*100, r['h_over_player'],
            r['target_h_over_player'], 'PASS' if r['h_over_player'] >= H_OVER_PLAYER_MIN else 'SHORT'))
    return 0


if __name__ == '__main__':
    sys.exit(main())
