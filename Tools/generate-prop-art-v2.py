# -*- coding: utf-8 -*-
"""道具立绘生成与归一化（MAT-04，2026-10-14 架构师七七）。

与 Tools/generate-building-art.py 同源，差别只在参数：
  - 建筑：占高 88%、画布宽高比按地块
  - 道具：占高 96%（物件几乎占满画布）、画布宽高比按 plot
两个口径都遵循同一条渲染契约（FrontEndWorld.cs:735-742）：
  **图被拉伸铺满 plot，图底边 = 地面** ⇒ 内容必须**贴底**（bottom≈99%），否则物件浮空。

用法：
  python Tools/generate-prop-art.py stone-lion              # 生图 + 抠底 + 归一化
  python Tools/generate-prop-art.py --verify                # 只验收 candidates/

串行调用生图通道；每次只跑一件（不并发）。
"""
import base64
import io
import json
import math
import sys
from collections import deque
from pathlib import Path

import urllib.request
import urllib.error

from PIL import Image

for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, 'reconfigure'):
        _stream.reconfigure(encoding='utf-8', errors='replace')

REPO = Path(__file__).resolve().parents[1]
ART_ROOT = REPO / "GameClient/TwelveJade/Assets/Resources/Art/Props"
CAND_DIR = REPO / "Tools/logs/mat-04/candidates"
RAW_DIR = REPO / "Tools/logs/mat-04/raw"
PLOTS_JSON = REPO / "GameClient/TwelveJade/Assets/Resources/Config/building-plots.json"
KEY_FILE = Path(r"D:\tmp\ddyu-art-key.txt")
BASE = "https://hub.oaifree.com"
MODEL = "gpt-image-2.5"

CONTENT_H_RATIO = 0.96     # 道具：内容占画布高度
BOTTOM_MARGIN_RATIO = 0.01  # 贴底并留 1% 安全边 -> bottom_pct ≈ 99%
CANVAS_H = 1024            # 画布高度基准（宽度 = 高度 x 目标宽高比）

STYLE = ("Asset type: a single hand-painted game prop illustration, fine ink linework with soft "
         "watercolor washes on warm ivory paper, muted natural colors, consistent with an elegant "
         "hand-painted historical Chinese indie game set in the late Yuan / early Ming era. Subject: ")

# tag -> (prompt 主体, 目标 plot 引用名, 目标宽高比列表)
PROPS = {
    "stone-lion": (
        "a single carved stone guardian lion on a plinth, weathered mossy, playful fierce pose, "
        "compact upright silhouette",
        "stone-lion", [0.6667],
    ),
    "lantern-pole": (
        "a tall slender wooden lantern pole with two red paper lanterns hanging from a short crossbar, "
        "tassels below, a small carved stone base, very tall narrow silhouette",
        "lantern-pole", [0.4],
    ),
    "market-stall": (
        "a street market stall with a draped blue-grey cloth awning on bamboo poles, wooden counter "
        "stacked with vegetables, a rice sack and a clay teapot",
        "market-stall", [0.7778],
    ),
    "market-cart": (
        "a peddler's wooden two-wheeled handcart with a flat plank bed and low side rails, "
        "a few stacked baskets and cloth bundles on it, a short pushing handle at the back, "
        "wider than tall, low ground clearance",
        "market-cart", [1.1667],
    ),
}

CAM = (" Front three-quarter view, centered, complete silhouette with nothing cropped, isolated on "
       "a flat uniform warm ivory background #EDE6D6, no scene, no ground surface, no cast shadow. "
       "Soft diffuse light. No text, labels, logo, watermark, no modern elements. "
       "The object is anchored to the BOTTOM edge of the frame: its base rests on the very bottom of "
       "the image, filling the frame from the bottom upward, and nothing extends below it.")


def read_key():
    return KEY_FILE.read_text(encoding="utf-8").strip()


def pick_size(aspect):
    """按目标宽高比挑 gpt-image 的标准尺寸档。"""
    opts = [(1024, 1024, 1.0), (1024, 1536, 0.6667), (1536, 1024, 1.5)]
    return min(opts, key=lambda o: abs(o[2] - aspect))[:2]


def generate(prompt, size, key):
    body = json.dumps({
        "model": MODEL, "prompt": prompt, "n": 1,
        "size": "%dx%d" % size, "background": "transparent", "output_format": "png",
    }).encode()
    req = urllib.request.Request(BASE + "/v1/images/generations", data=body,
                                headers={"Authorization": "Bearer " + key,
                                         "Content-Type": "application/json"})
    op = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    d = json.loads(op.open(req, timeout=420).read().decode())
    item = d["data"][0]
    if item.get("b64_json"):
        return base64.b64decode(item["b64_json"])
    u = item.get("url")
    if not u:
        raise RuntimeError("生图响应既无 b64_json 也无 url: %s" % json.dumps(d)[:200])
    r = urllib.request.Request(u, headers={"Authorization": "Bearer " + key})
    return op.open(r, timeout=180).read()


def cutout_bytes(png_bytes, tolerance=26):
    """四角均值取底色 + 连通泛洪只清与边缘相连的底色（与 cutout-props.py 同口径）。"""
    im = Image.open(io.BytesIO(png_bytes)).convert("RGBA")
    w, h = im.size
    px = im.load()
    corners = [(0, 0), (w - 8, 0), (0, h - 8), (w - 8, h - 8)]
    samples = [px[x + dx, y + dy] for x, y in corners for dx in range(8) for dy in range(8)]
    ref = tuple(sum(p[i] for p in samples) // len(samples) for i in range(3))

    def is_bg(p):
        return all(abs(p[i] - ref[i]) <= tolerance for i in range(3))

    vis = bytearray(w * h)
    q = deque()
    for x in range(w):
        for y in (0, h - 1):
            if is_bg(px[x, y]) and not vis[y * w + x]:
                vis[y * w + x] = 1
                q.append((x, y))
    for y in range(h):
        for x in (0, w - 1):
            if is_bg(px[x, y]) and not vis[y * w + x]:
                vis[y * w + x] = 1
                q.append((x, y))
    while q:
        x, y = q.popleft()
        px[x, y] = (0, 0, 0, 0)
        for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
            if 0 <= nx < w and 0 <= ny < h and not vis[ny * w + nx] and is_bg(px[nx, ny]):
                vis[ny * w + nx] = 1
                q.append((nx, ny))
    return im


def alpha_bbox(im):
    a = im.getchannel("A")
    return a.getbbox()


def normalize(cut_im, canvas_w, canvas_h):
    """按占高缩放 -> 居中横裁到目标宽高比 -> **竖直贴底**（留 1% 安全边）。"""
    box = alpha_bbox(cut_im)
    if box is None:
        raise RuntimeError("抠底后无内容")
    content = cut_im.crop(box)
    src_w, src_h = content.size

    target_h = int(round(CONTENT_H_RATIO * canvas_h))
    scale = target_h / src_h
    scaled_w = int(round(src_w * scale))

    cropped = 0
    if scaled_w > canvas_w:
        # 宽度超出画布：改为**按宽度适配** —— 道具优先保证内容完整（不裁切）。
        # 代价：极窄画布（如 lantern-pole 的 ar=0.400）下占高会低于 CONTENT_H_RATIO，
        # 这对细长物件（灯笼杆）是有意为之：宁可杆子矮一点也不切掉两端的灯笼。
        scale = canvas_w / float(src_w)
        scaled_w = canvas_w
    scaled_h = int(round(src_h * scale))
    resized = content.resize((scaled_w, scaled_h), Image.LANCZOS)

    canvas = Image.new("RGBA", (canvas_w, canvas_h), (0, 0, 0, 0))
    margin_y = int(round(BOTTOM_MARGIN_RATIO * canvas_h))
    canvas.paste(resized, ((canvas_w - scaled_w) // 2, canvas_h - scaled_h - margin_y), resized)
    return canvas, cropped


def measure(im):
    w, h = im.size
    px = im.load()
    xs = [x for y in range(0, h, 2) for x in range(0, w, 2) if px[x, y][3] > 128]
    ys = [y for y in range(0, h, 2) for x in range(0, w, 2) if px[x, y][3] > 128]
    if not xs:
        return None
    l, r, t, b = min(xs), max(xs), min(ys), max(ys)
    corners = [px[0, 0][3], px[w - 1, 0][3], px[0, h - 1][3], px[w - 1, h - 1][3]]
    return {
        "size": [w, h], "ar": round(w / h, 4),
        "h_pct": round((b - t + 1) * 100.0 / h, 1),
        "top_pct": round(t * 100.0 / h, 1), "bottom_pct": round((b + 1) * 100.0 / h, 1),
        "w_pct": round((r - l + 1) * 100.0 / w, 1),
        "corners_alpha": corners,
    }


def run_one(tag, key):
    subject, ref, aspects = PROPS[tag]
    ar = aspects[0]
    size = pick_size(ar)
    prompt = STYLE + subject + CAM
    RAW_DIR.mkdir(parents=True, exist_ok=True)
    CAND_DIR.mkdir(parents=True, exist_ok=True)

    print("[%s] 生图 size=%dx%d 目标 ar=%.4f" % (tag, size[0], size[1], ar))
    raw = generate(prompt, size, key)
    raw_path = RAW_DIR / (tag + "-raw.png")
    raw_path.write_bytes(raw)
    print("  原始 ->", raw_path.name, Image.open(io.BytesIO(raw)).size)

    cut = cutout_bytes(raw)
    cut_path = RAW_DIR / (tag + "-cut.png")
    cut.save(cut_path)

    canvas_w = int(round(CANVAS_H * ar))
    canvas, cropped = normalize(cut, canvas_w, CANVAS_H)
    out = CAND_DIR / (tag + "-v1.png")
    canvas.save(out)
    m = measure(canvas)
    print("  归一 -> %s  %s  cropped_px=%d" % (out.name, m, cropped))
    return {"tag": tag, "raw_size": list(Image.open(io.BytesIO(raw)).size),
            "canvas": [canvas_w, CANVAS_H], "metrics": m, "cropped_px": cropped}


def verify():
    rows = []
    for p in sorted(CAND_DIR.glob("*.png")):
        im = Image.open(p).convert("RGBA")
        rows.append((p.name, measure(im)))
    for n, m in rows:
        print("%-22s %s" % (n, m))
    return rows


def main():
    args = sys.argv[1:]
    if not args or args[0] == "--verify":
        verify()
        return 0
    key = read_key()
    results = []
    for tag in args:
        if tag not in PROPS:
            print("未知资产:", tag, "可选:", ", ".join(PROPS)); return 2
        results.append(run_one(tag, key))
    Path(REPO / "Tools/logs/mat-04/metrics.json").parent.mkdir(parents=True, exist_ok=True)
    Path(REPO / "Tools/logs/mat-04/metrics.json").write_text(
        json.dumps(results, ensure_ascii=False, indent=1), encoding="utf-8")
    return 0


if __name__ == "__main__":
    sys.exit(main())
