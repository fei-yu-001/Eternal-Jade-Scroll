# -*- coding: utf-8 -*-
"""行走序列帧提取与归一化（V-02，2026-10-06 架构师）。

为什么需要它：AI 出的 sprite sheet 里 8 个小人**不在等距网格上**（实测中心 x=127/283/422/585/
745/880/1034/1183，间距 135~163），按 1280/8 固定切格会切歪。所以必须按**连通块**提取，
再统一归一化：同高度、同脚底线、水平居中。

产出（落 Tools/logs/v-01-walk-pilot/，该目录已跟踪、能被取证）：
  walk-front-00.png .. 07.png   每帧独立 PNG（透明底）
  contact-sheet.png             8 帧并排 2 倍放大，供人眼过目
  manifest.json                 每帧来源 bbox / 尺寸 / 缩放，可复算

用法：python Tools/extract-walk-sheet.py <sheet.png> <输出目录> [帧数]
"""
import json
import os
import sys
from collections import deque

from PIL import Image

for _s in (sys.stdout, sys.stderr):
    if hasattr(_s, "reconfigure"):
        _s.reconfigure(encoding="utf-8", errors="replace")

TOL = 70
MIN_BLOB = 200
CANVAS_W = 256
CANVAS_H = 384
PAD_BOTTOM = 8
# 透明底交付件（RGBA）喂进来时，先合成回宣纸色再取前景基准：
# 本闸的基准是 px[1,1] + TOL，直接 convert("RGB") 会把透明区填成黑，
# 深色纸偶会连背景一起被当成前景（历史上就因此把真周期判成"没动"）。
# 判据与阈值一律未动，只是把"交付形态"从判定输入里剔出去。
# 透明底交付件的前景就是 alpha>0，故合成到纯品红键控色，让「按背景色键控」等价于「alpha 即前景」。
KEY_MAGENTA = (255, 0, 255)


def flatten_paper(im):
    """有 alpha 就合成到宣纸色底上，否则直接转 RGB。"""
    if im.mode in ("RGBA", "LA") or (im.mode == "P" and "transparency" in im.info):
        rgba = im.convert("RGBA")
        bg = Image.new("RGB", rgba.size, KEY_MAGENTA)
        bg.paste(rgba, (0, 0), rgba)
        return bg
    return im.convert("RGB")


def background_mask(im):
    w, h = im.size
    px = im.load()
    base = px[1, 1]

    def is_background(c):
        return max(abs(c[0] - base[0]), abs(c[1] - base[1]), abs(c[2] - base[2])) <= TOL

    seen = [[False] * w for _ in range(h)]
    queue = deque()

    def push(x, y):
        if 0 <= x < w and 0 <= y < h and not seen[y][x] and is_background(px[x, y]):
            seen[y][x] = True
            queue.append((x, y))

    for x in range(w):
        push(x, 0)
        push(x, h - 1)
    for y in range(h):
        push(0, y)
        push(w - 1, y)
    while queue:
        x, y = queue.popleft()
        push(x + 1, y)
        push(x - 1, y)
        push(x, y + 1)
        push(x, y - 1)
    return seen


def blobs(im, seen):
    w, h = im.size
    px = im.load()
    visited = [[False] * w for _ in range(h)]
    found = []
    for y in range(h):
        for x in range(w):
            if seen[y][x] or visited[y][x]:
                continue
            queue = deque([(x, y)])
            visited[y][x] = True
            count = 0
            x0, y0, x1, y1 = x, y, x, y
            while queue:
                cx, cy = queue.popleft()
                count += 1
                x0 = min(x0, cx)
                x1 = max(x1, cx)
                y0 = min(y0, cy)
                y1 = max(y1, cy)
                for nx, ny in ((cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1)):
                    if 0 <= nx < w and 0 <= ny < h and not seen[ny][nx] and not visited[ny][nx]:
                        visited[ny][nx] = True
                        queue.append((nx, ny))
            if count >= MIN_BLOB:
                found.append({"px": count, "box": (x0, y0, x1, y1)})
    found.sort(key=lambda b: b["box"][0])
    return found


def split_wide(im, seen, found, expected):
    """把过宽的连通块在竖直最窄处切开（AI 常把相邻两帧画得挨在一起）。
    判据：宽度 > 中位宽 * 1.55 视为"两帧粘连"，在其中段找前景像素最少的列切一刀。"""
    if not found:
        return found
    median_w = sorted((b["box"][2] - b["box"][0] + 1) for b in found)[len(found) // 2]
    limit = median_w * 1.55
    result = []
    for b in found:
        x0, y0, x1, y1 = b["box"]
        width = x1 - x0 + 1
        if width <= limit:
            result.append(b)
            continue
        lo = x0 + int(width * 0.30)
        hi = x0 + int(width * 0.70)
        best_cut, best_count = None, None
        for cx in range(lo, max(lo + 1, hi + 1)):
            n = 0
            for y in range(y0, y1 + 1):
                if not seen[y][cx]:
                    n += 1
            if best_count is None or n < best_count:
                best_count, best_cut = n, cx
        for left, right in ((x0, best_cut - 1), (best_cut, x1)):
            ax0, ax1, ay0, ay1 = left, right, y1, y0
            count = 0
            for y in range(y0, y1 + 1):
                for x in range(left, right + 1):
                    if not seen[y][x]:
                        count += 1
                        ax0 = min(ax0, x); ax1 = max(ax1, x)
                        ay0 = min(ay0, y); ay1 = max(ay1, y)
            if count >= MIN_BLOB:
                result.append({"px": count, "box": (ax0, ay0, ax1, ay1)})
    result.sort(key=lambda b: b["box"][0])
    return result


def split_until(im, seen, found, expected, max_rounds=16):
    """反复把最宽的那块在"最窄列"切开，直到块数达到期望。
    AI 可能把 8 帧画成一整条连在一起（实测一次 1235x616），单刀切不开，必须反复切。"""
    for _ in range(max_rounds):
        if len(found) >= expected:
            break
        widths = [(b["box"][2] - b["box"][0] + 1) for b in found]
        if not widths:
            break
        idx = max(range(len(found)), key=lambda i: widths[i])
        x0, y0, x1, y1 = found[idx]["box"]
        width = x1 - x0 + 1
        if width < 24:
            break
        lo = x0 + int(width * 0.15)
        hi = x0 + int(width * 0.85)
        best_cut, best_count = None, None
        for cx in range(lo, max(lo + 1, hi + 1)):
            n = 0
            for y in range(y0, y1 + 1):
                if not seen[y][cx]:
                    n += 1
            if best_count is None or n < best_count:
                best_count, best_cut = n, cx
        if best_cut is None:
            break
        parts = []
        for left, right in ((x0, best_cut - 1), (best_cut, x1)):
            ax0, ax1, ay0, ay1 = left, right, y1, y0
            count = 0
            for y in range(y0, y1 + 1):
                for x in range(left, right + 1):
                    if not seen[y][x]:
                        count += 1
                        ax0 = min(ax0, x); ax1 = max(ax1, x)
                        ay0 = min(ay0, y); ay1 = max(ay1, y)
            if count >= MIN_BLOB:
                parts.append({"px": count, "box": (ax0, ay0, ax1, ay1)})
        if len(parts) < 2:
            break
        found = found[:idx] + parts + found[idx + 1:]
        found.sort(key=lambda b: b["box"][0])
    return found


def main():
    sheet_path = sys.argv[1] if len(sys.argv) > 1 else (
        r"D:\qiqi_me\Eternal Jade Scroll\GameClient\TwelveJade\Assets\ArtSource\originals\characters-rig"
        r"\farmer-male-0-walksheet-v1.png")
    out_dir = sys.argv[2] if len(sys.argv) > 2 else (
        r"D:\qiqi_me\Eternal Jade Scroll\Tools\logs\v-01-walk-pilot")
    expected = int(sys.argv[3]) if len(sys.argv) > 3 else 8
    os.makedirs(out_dir, exist_ok=True)

    source = flatten_paper(Image.open(sheet_path))
    seen = background_mask(source)
    found = blobs(source, seen)
    print("检出连通块 %d 个（期望 %d）" % (len(found), expected))
    for i, b in enumerate(found):
        x0, y0, x1, y1 = b["box"]
        print("  #%d px=%-7d box=(%d,%d)-(%d,%d) w=%d h=%d" % (
            i, b["px"], x0, y0, x1, y1, x1 - x0 + 1, y1 - y0 + 1))

    # 合并过近的块（AI 常把两帧画挨着）；若块数多余，取最大的 expected 个。
    if len(found) > expected:
        print("WARN 块数多于期望，按像素数取前 %d 个" % expected)
        found = sorted(found, key=lambda b: -b["px"])[:expected]
        found.sort(key=lambda b: b["box"][0])

    if len(found) < expected:
        found = split_until(source, seen, found, expected)
        print('  切分后得到 %d 块' % len(found))
    ok = len(found) == expected
    heights = [(b["box"][3] - b["box"][1] + 1) for b in found]
    ground = [b["box"][3] for b in found]

    manifest = {
        "sheet": os.path.basename(sheet_path),
        "sheetSize": list(source.size),
        "tolerance": TOL,
        "expected": expected,
        "found": len(found),
        "heightMin": min(heights) if heights else 0,
        "heightMax": max(heights) if heights else 0,
        "groundSpreadPx": (max(ground) - min(ground)) if ground else 0,
        "frames": [],
    }

    target_h = CANVAS_H - PAD_BOTTOM * 2
    for i, b in enumerate(found):
        x0, y0, x1, y1 = b["box"]
        part = source.crop((x0, y0, x1 + 1, y1 + 1))
        # 用背景色做键控而不是硬 mask：保留边缘抗锯齿，避免锯齿边
        # 【AA-6.2 修，2026-10-07】键控底色不再写死 (217,185,128)——新素材背景是 (237,230,210)，
        # 写死会把背景判成前景（alpha=48 灰底）。改为**实测底色**：本帧 bbox 外扩一圈的中位数，
        # 抄 prep-external-art.py 的 background_color() 思路。只改常量来源，判据（TOL/alpha 曲线）一字未动。
        ring = source.crop((max(0, x0 - 24), max(0, y0 - 24), min(source.width, x1 + 25), min(source.height, y1 + 25)))
        rpx = list(ring.getdata())
        rs = sorted(rpx)
        bgc = rs[len(rs) // 2]
        keyed = Image.new("RGBA", part.size)
        ppx = part.load()
        kpx = keyed.load()
        for y in range(part.size[1]):
            for x in range(part.size[0]):
                c = ppx[x, y]
                dist = max(abs(c[0] - bgc[0]), abs(c[1] - bgc[1]), abs(c[2] - bgc[2]))
                alpha = 0 if dist <= TOL else min(255, int((dist - TOL) * 4))
                kpx[x, y] = (c[0], c[1], c[2], alpha)
        scale = target_h / float(part.size[1])
        new_size = (max(1, int(round(part.size[0] * scale))), target_h)
        resized = keyed.resize(new_size, Image.LANCZOS)
        canvas = Image.new("RGBA", (CANVAS_W, CANVAS_H), (0, 0, 0, 0))
        left = (CANVAS_W - new_size[0]) // 2
        canvas.paste(resized, (left, CANVAS_H - PAD_BOTTOM - new_size[1]), resized)
        name = "walk-front-%02d.png" % i
        canvas.save(os.path.join(out_dir, name))
        manifest["frames"].append({
            "index": i, "file": name, "sourceBox": [x0, y0, x1, y1],
            "sourceW": x1 - x0 + 1, "sourceH": y1 - y0 + 1,
            "scale": round(scale, 4), "px": b["px"],
        })

    if found:
        scale_up = 2
        sheet = Image.new("RGBA", (CANVAS_W * len(found) * scale_up, CANVAS_H * scale_up), (245, 240, 228, 255))
        for i in range(len(found)):
            frame = Image.open(os.path.join(out_dir, "walk-front-%02d.png" % i))
            frame = frame.resize((CANVAS_W * scale_up, CANVAS_H * scale_up), Image.NEAREST)
            sheet.paste(frame, (i * CANVAS_W * scale_up, 0), frame)
        sheet.convert("RGB").save(os.path.join(out_dir, "contact-sheet.png"))

    with open(os.path.join(out_dir, "manifest.json"), "w", encoding="utf-8") as fh:
        json.dump(manifest, fh, ensure_ascii=False, indent=2)

    print("高度 min %d / max %d，脚底线散布 %d px" % (
        manifest["heightMin"], manifest["heightMax"], manifest["groundSpreadPx"]))
    verdict = "PASS" if (ok and manifest["groundSpreadPx"] <= 6) else "FAIL"
    print("RESULT: %s  （帧数 %d/%d，脚底线散布 %d<=6，高度差 %d）" % (
        verdict, len(found), expected, manifest["groundSpreadPx"],
        manifest["heightMax"] - manifest["heightMin"]))
    print("输出目录: " + out_dir)


if __name__ == "__main__":
    main()


