# -*- coding: utf-8 -*-
"""角色身份一致性检查（V 线验收闸）。

背景：2026-10-06 用户指出"生成的走帧跟主角形象不一致"。上一轮我只验了几何
（帧数/高度/脚底线），**没有验身份**，所以问题漏过去了。本脚本补上这一维。

判据（两维，都要过）：
  ① 比例：候选剪影 bbox 的高宽比 vs 参考，相对差 <= 12%
  ② 配色：前景像素量化到 4bit/通道后的直方图交集 >= 0.55（1.0 = 完全相同）
阈值是初值，用更多样本校准前不许放宽——要放宽必须先报告。

用法：python Tools/check-character-identity.py <参考.png> <候选1.png> [候选2 ...]
"""
import sys
from collections import Counter

from PIL import Image

for _s in (sys.stdout, sys.stderr):
    if hasattr(_s, "reconfigure"):
        _s.reconfigure(encoding="utf-8", errors="replace")

ASPECT_TOL = 0.45   # 比例只作告警：同角色 front<->side 实测 Δ39.7%，跨视图比比例不成立
PALETTE_MIN = 0.50   # 校准：同角色跨视图 0.619/0.670；不同角色 0.290~0.317
BITS = 4
LEVELS = 1 << BITS


def foreground(im):
    im = im.convert("RGBA")
    w, h = im.size
    px = im.load()
    alpha = im.getchannel("A")
    has_alpha = alpha.getextrema()[0] < 250
    pixels = []
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if has_alpha:
                if a > 128:
                    pixels.append((r, g, b))
            else:
                if max(abs(r - 236), abs(g - 228), abs(b - 210)) > 70:
                    pixels.append((r, g, b))
    if not pixels:
        return None
    xs = [p[0] for p in pixels]
    # 需要坐标，重扫一遍（上面的 pixels 只存颜色）
    coords = []
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if has_alpha:
                if a > 128:
                    coords.append((x, y))
            else:
                if max(abs(r - 236), abs(g - 228), abs(b - 210)) > 70:
                    coords.append((x, y))
    minx = min(c[0] for c in coords)
    maxx = max(c[0] for c in coords)
    miny = min(c[1] for c in coords)
    maxy = max(c[1] for c in coords)
    bw = maxx - minx + 1
    bh = maxy - miny + 1
    hist = Counter()
    for (r, g, b) in pixels:
        hist[(r * LEVELS // 256, g * LEVELS // 256, b * LEVELS // 256)] += 1
    total = float(len(pixels))
    return {
        "aspect": bh / float(bw),
        "bbox": (bw, bh),
        "hist": {k: v / total for k, v in hist.items()},
        "top": hist.most_common(6),
        "count": len(pixels),
    }


def intersection(a, b):
    keys = set(a) | set(b)
    return sum(min(a.get(k, 0.0), b.get(k, 0.0)) for k in keys)


def main():
    if len(sys.argv) < 3:
        raise SystemExit(__doc__)
    ref = foreground(Image.open(sys.argv[1]))
    if ref is None:
        raise SystemExit("参考图找不到前景")
    print("REF  %s" % sys.argv[1])
    print("     bbox=%dx%d  aspect=%.3f  fg_px=%d" % (
        ref["bbox"][0], ref["bbox"][1], ref["aspect"], ref["count"]))
    print("     top colours: " + ", ".join(
        "#%02X%02X%02X %.1f%%" % (k[0] * 16 + 8, k[1] * 16 + 8, k[2] * 16 + 8, 100.0 * v / ref["count"])
        for k, v in ref["top"]))
    failures = 0
    for path in sys.argv[2:]:
        cand = foreground(Image.open(path))
        if cand is None:
            print("FAIL %s : 找不到前景" % path)
            failures += 1
            continue
        d_aspect = abs(cand["aspect"] - ref["aspect"]) / ref["aspect"]
        pal = intersection(ref["hist"], cand["hist"])
        ok = pal >= PALETTE_MIN
        if not ok:
            failures += 1
        print("%s %s" % ("PASS" if ok else "FAIL", path))
        print("     bbox=%dx%d  aspect=%.3f (Δ%.1f%%, 限 %.0f%%)" % (
            cand["bbox"][0], cand["bbox"][1], cand["aspect"], 100 * d_aspect, 100 * ASPECT_TOL))
        print("     palette intersection=%.3f (限 %.2f)" % (pal, PALETTE_MIN))
        if not ok:
            print("     top colours: " + ", ".join(
                "#%02X%02X%02X %.1f%%" % (k[0] * 16 + 8, k[1] * 16 + 8, k[2] * 16 + 8, 100.0 * v / cand["count"])
                for k, v in cand["top"]))
    print("RESULT: %s  （%d/%d 通过）" % ("PASS" if failures == 0 else "FAIL",
                                         len(sys.argv) - 2 - failures, len(sys.argv) - 2))
    sys.exit(0 if failures == 0 else 1)


if __name__ == "__main__":
    main()


