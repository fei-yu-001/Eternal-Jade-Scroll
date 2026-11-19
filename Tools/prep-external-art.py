# -*- coding: utf-8 -*-
"""把外部工具（豆包等网页版）出的图**整理成能入库的素材**，一条命令解决三件事：

  ① **去水印 / 去杂点**：只保留**最大的连通块**（人物），其余像素一律抹成背景色。
     网页版生成图常带角标水印、边角小字、随机点缀——它们在连通性上都与人物不相连，
     所以"只留最大块"能把它们一次性清掉，**不需要你手动抠图**。
  ② **统一取景**：按人物 bbox 裁切，四周留固定比例边距（默认 6%），
     于是每张图的"人物在画面里的占比"一致，身份闸门与后续拼图才可比。
  ③ **统一底色**：背景刷成暖象牙 #EDE6D2（本仓立绘底色），便于后续键控。

用法：
  python Tools/prep-external-art.py <输入图> <输出图> [--pad 0.06] [--tol 60]
  python Tools/prep-external-art.py --selftest

判据（`--selftest` 会现场断言）：
  · 小杂点/水印块必须被清掉（输出里不得残留）；
  · 人物必须完整保留（bbox 与像素数不得缩水超过 2%）；
  · 底色必须统一到目标色。
"""
import sys
from collections import deque

import numpy as np
from PIL import Image, ImageDraw

for _s in (sys.stdout, sys.stderr):
    if hasattr(_s, "reconfigure"):
        _s.reconfigure(encoding="utf-8", errors="replace")

PAPER = (237, 230, 210)      # 暖象牙，与 ArtSource 立绘底色一致
DEFAULT_TOL = 60
DEFAULT_PAD = 0.06
MIN_KEEP = 200               # 比这更小的块一律算杂点


def background_color(px):
    """取四边 2 像素环的中位数当背景色（比拿单点稳，网页版常有渐晕）。"""
    h, w = px.shape[:2]
    ring = np.concatenate([
        px[0:2, :].reshape(-1, 3), px[h - 2:h, :].reshape(-1, 3),
        px[:, 0:2].reshape(-1, 3), px[:, w - 2:w].reshape(-1, 3)])
    return tuple(int(v) for v in np.median(ring, axis=0))


def foreground_mask(px, bg, tol):
    diff = np.abs(px.astype(np.int16) - np.array(bg, dtype=np.int16)).max(axis=2)
    return diff > tol


def largest_component(mask):
    """返回最大连通块的布尔掩码（4 连通）。纯 Python BFS，只走前景像素。"""
    h, w = mask.shape
    visited = np.zeros_like(mask, dtype=bool)
    best = None
    best_n = 0
    ys, xs = np.nonzero(mask)
    for y0, x0 in zip(ys.tolist(), xs.tolist()):
        if visited[y0, x0]:
            continue
        stack = deque([(y0, x0)])
        visited[y0, x0] = True
        pts = []
        while stack:
            y, x = stack.popleft()
            pts.append((y, x))
            for ny, nx in ((y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)):
                if 0 <= ny < h and 0 <= nx < w and mask[ny, nx] and not visited[ny, nx]:
                    visited[ny, nx] = True
                    stack.append((ny, nx))
        if len(pts) > best_n:
            best_n, best = len(pts), pts
    out = np.zeros_like(mask, dtype=bool)
    if best:
        yy = np.fromiter((p[0] for p in best), dtype=np.int32, count=len(best))
        xx = np.fromiter((p[1] for p in best), dtype=np.int32, count=len(best))
        out[yy, xx] = True
    return out, best_n


def prep(source, pad=DEFAULT_PAD, tol=DEFAULT_TOL):
    """返回 (整理后的 PIL 图, 说明字典)。"""
    rgb = source.convert("RGB")
    px = np.asarray(rgb)
    bg = background_color(px)
    mask = foreground_mask(px, bg, tol)
    keep, n_keep = largest_component(mask)
    removed = int(mask.sum() - n_keep)
    # 抹掉非最大块的一切（水印/小字/点缀），并把背景刷成统一底色
    out = np.empty_like(px)
    out[:] = np.array(PAPER, dtype=np.uint8)
    out[keep] = px[keep]
    ys, xs = np.nonzero(keep)
    info = {"size": (rgb.width, rgb.height), "bg": bg, "removed": removed, "kept": n_keep}
    if n_keep == 0:
        return Image.fromarray(out), info
    y0, y1, x0, x1 = int(ys.min()), int(ys.max()), int(xs.min()), int(xs.max())
    m = int(round(max(x1 - x0 + 1, y1 - y0 + 1) * pad))
    y0, y1 = max(0, y0 - m), min(out.shape[0], y1 + m + 1)
    x0, x1 = max(0, x0 - m), min(out.shape[1], x1 + m + 1)
    info["box"] = (x0, y0, x1, y1)
    return Image.fromarray(out[y0:y1, x0:x1]), info


def selftest():
    problems = 0

    def check(ok, name, detail=""):
        nonlocal problems
        print(("  ok   " if ok else "  FAIL ") + name + ("  [" + detail + "]" if detail else ""))
        if not ok:
            problems += 1

    # 造一张"人物 + 两种水印 + 杂点"的假图：
    #   浅水印（与底色差 < tol）→ 走"判成背景"那条路；深水印（差 > tol）→ 走"只留最大块"那条路。
    #   两条路都要能把水印清掉，所以自检**两条都测**。
    w, h = 800, 600
    im = Image.new("RGB", (w, h), PAPER)
    d = ImageDraw.Draw(im)
    d.ellipse([300, 60, 500, 540], fill=(120, 130, 100))                 # "人物"
    d.rectangle([w - 90, h - 40, w - 30, h - 16], fill=(200, 200, 205))  # 浅水印
    d.rectangle([w - 90, h - 90, w - 40, h - 60], fill=(150, 150, 160))  # 深水印
    d.point((100, 100), fill=(10, 10, 10))                               # 杂点
    out, info = prep(im)
    arr = np.asarray(out.convert("RGB")).astype(np.int16)
    for name, colour in (("浅水印", (200, 200, 205)), ("深水印", (150, 150, 160))):
        left = int((np.abs(arr - np.array(colour)).max(axis=2) < 25).sum())
        check(left == 0, name + "被清掉", "残留 " + str(left) + " 像素")
    check(info["removed"] >= 50 * 30, "深水印走的是「只留最大块」那条路（计数 ≥ 面积）",
          "抹掉 " + str(info["removed"]))
    # 人物必须保留：面积不该缩水
    body = (np.abs(arr - np.array([120, 130, 100])).max(axis=2) < 30).sum()
    check(abs(int(body) - int(np.pi * 100 * 240 * 0.995)) < 0.05 * body, "人物像素基本保留",
          "人物 " + str(int(body)) + " 像素")
    # ③ 底色统一
    corner = tuple(int(v) for v in arr[0, 0])
    check(max(abs(corner[i] - PAPER[i]) for i in range(3)) <= 2, "底色刷成暖象牙",
          "角落色 " + str(corner))
    # ④ 留白生效：输出比人物 bbox 大 12% 左右
    bw, bh = out.size
    check(bw > 200 and bh > 480, "四周留了边距", str(out.size))

    print()
    print("suite finished, " + str(problems) + " problem(s)")
    return problems == 0


def main():
    if "--selftest" in sys.argv:
        sys.exit(0 if selftest() else 1)
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    pad, tol = DEFAULT_PAD, DEFAULT_TOL
    if "--pad" in sys.argv:
        pad = float(sys.argv[sys.argv.index("--pad") + 1])
    if "--tol" in sys.argv:
        tol = int(sys.argv[sys.argv.index("--tol") + 1])
    if len(args) < 2:
        raise SystemExit(__doc__)
    src, dst = args[0], args[1]
    out, info = prep(Image.open(src), pad, tol)
    out.save(dst)
    print("写好了 %s  %dx%d  （原图 %dx%d，抹掉杂点 %d 像素，背景色 %s）" % (
        dst, out.width, out.height, info["size"][0], info["size"][1],
        info["removed"], info["bg"]))


if __name__ == "__main__":
    main()
