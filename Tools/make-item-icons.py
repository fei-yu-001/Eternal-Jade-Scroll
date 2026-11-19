# -*- coding: utf-8 -*-
"""物品图标后处理：把生图渠道吐出的原图裁成 H-5 规格的图标（256×256、透明底、居中占画布 70~85%）。

这不是"程序化当美术"——**画面内容是生图模型画的**（渠道 A：本地 grok2api 网关，
或等价的生图接口），本脚本只做三件机械活：方形裁切、把背景抠成 alpha、按占比缩放。
之所以要有这么一份脚本而不是每张手 P：
  1. 图标族是"按清单批量出"的（H-6 批 6），手工会在 20 张里做出 20 种留白口径；
  2. 抠底参数一旦写进代码，下一批与这一批可比——这也是 H-4 反向棘轮能数孤儿的前提
     （产物形状稳定，才谈得上"引用没引用"）。

用法：
  python Tools/make-item-icons.py <原图目录> <输出目录> [id1 id2 ...]
  不给 id 就处理目录里所有 icon-*.png。

留痕：每张会在 stdout 打一行 `源尺寸 / 抠底命中像素比例 / 内容包围盒 / 输出 md5`，
交付表里的"是否透明底""尺寸"两列就从这里抄，不靠回忆。
"""
import hashlib
import io
import os
import struct
import sys
from collections import deque

from PIL import Image

for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, "reconfigure"):
        _stream.reconfigure(encoding="utf-8", errors="replace")

SIDE = 256            # H-5：图标 256×256
FILL = 0.78           # 占画布 70~85%，取中值：行囊格子小，太满会顶到边框
TOLERANCE = 30        # 与背景色的欧氏距离阈值（纸本颗粒有噪声，太紧会留一圈边）
MIN_ALPHA_HIT = 0.02  # 抠底至少要动到 2% 像素，否则判定"背景没抠动"并报警


def corners(img):
    w, h = img.size
    pts = [(2, 2), (w - 3, 2), (2, h - 3), (w - 3, h - 3)]
    px = img.load()
    return [px[p] for p in pts]


def matte(img):
    """从四条边往里漫填，把接近背景色的连通区域变成透明。

    为什么漫填而不是"按颜色全局替换"：物品本体上常有与背景同色的浅色块
    （宣纸、白瓷、面粉），全局替换会在物件上打出洞来。
    """
    rgb = img.convert("RGB")
    w, h = rgb.size
    px = rgb.load()
    ref = corners(rgb)
    seen = [[False] * w for _ in range(h)]
    keep = [[True] * w for _ in range(h)]
    queue = deque()
    for x in range(w):
        for y in (0, h - 1):
            queue.append((x, y))
    for y in range(h):
        for x in (0, w - 1):
            queue.append((x, y))
    hits = 0
    while queue:
        x, y = queue.popleft()
        if seen[y][x]:
            continue
        seen[y][x] = True
        r, g, b = px[x, y][:3]
        if min((r - cr) ** 2 + (g - cg) ** 2 + (b - cb) ** 2 for cr, cg, cb in ref) > TOLERANCE ** 2:
            continue
        hits += 1
        keep[y][x] = False
        if x > 0:
            queue.append((x - 1, y))
        if x < w - 1:
            queue.append((x + 1, y))
        if y > 0:
            queue.append((x, y - 1))
        if y < h - 1:
            queue.append((x, y + 1))
    out = rgb.convert("RGBA")
    op = out.load()
    for y in range(h):
        for x in range(w):
            if not keep[y][x]:
                r, g, b, _ = op[x, y]
                op[x, y] = (r, g, b, 0)
    return out, hits / float(w * h)


def square_fit(img):
    """按短边居中裁方——生图网关给的是竖构图，直接缩放会把物件压扁。"""
    w, h = img.size
    side = min(w, h)
    left, top = (w - side) // 2, (h - side) // 2
    return img.crop((left, top, left + side, top + side))


def content_box(img):
    box = img.getbbox()
    if box is None:
        return None
    return box


def build(src_path, dst_dir):
    name = os.path.splitext(os.path.basename(src_path))[0]
    with Image.open(src_path) as raw:
        src_size = (raw.width, raw.height)
        fitted = square_fit(raw)
        matted, hit_ratio = matte(fitted)
        box = content_box(matted)
        if box is None:
            print("!! %s 抠完是空的（背景判定失败），跳过" % name)
            return None
        cw, ch = box[2] - box[0], box[3] - box[1]
        scale = (SIDE * FILL) / float(max(cw, ch))
        canvas = Image.new("RGBA", (SIDE, SIDE), (0, 0, 0, 0))
        piece = matted.crop(box).resize(
            (max(1, int(round(cw * scale))), max(1, int(round(ch * scale)))), Image.LANCZOS)
        ox, oy = (SIDE - piece.width) // 2, (SIDE - piece.height) // 2
        canvas.paste(piece, (ox, oy), piece)
        out = os.path.join(dst_dir, name + ".png")
        canvas.save(out, "PNG", optimize=True)
        digest = hashlib.md5(io.open(out, "rb").read()).hexdigest()
        warn = "" if hit_ratio >= MIN_ALPHA_HIT else "  ⚠ 背景几乎没抠动，可能整张是纯色物件"
        print("%-26s 源 %sx%s 抠底 %.1f%% 内容 %sx%s → %sx%s alpha=%s md5=%s%s" % (
            name, src_size[0], src_size[1], hit_ratio * 100, cw, ch, SIDE, SIDE,
            "RGBA" if canvas.mode == "RGBA" else canvas.mode, digest[:12], warn))
        return out


def main():
    if len(sys.argv) < 3:
        sys.exit(__doc__)
    src_dir, dst_dir = sys.argv[1], sys.argv[2]
    wanted = sys.argv[3:]
    os.makedirs(dst_dir, exist_ok=True)
    files = sorted(
        os.path.join(src_dir, f) for f in os.listdir(src_dir)
        if f.lower().endswith(".png") and (not wanted or any(w in f for w in wanted)))
    done = [p for p in (build(f, dst_dir) for f in files) if p]
    print("处理完成 %d/%d → %s" % (len(done), len(files), dst_dir))


if __name__ == "__main__":
    main()
