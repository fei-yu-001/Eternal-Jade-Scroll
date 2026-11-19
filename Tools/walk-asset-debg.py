# -*- coding: utf-8 -*-
"""WALK-ASSET ｜ 走帧去底产出（只读 Art/，全部输出落 Tools/logs/walk-asset/）。

━━ 为什么不能只靠 alpha 通道 ━━
任务书原话是"merchant/traveller 六张透明底正常"。实测**不成立**：
这六张的 alpha 通道在每格里都是一个**实心矩形**（`fill=1.0000`，逐格 alpha 只有 {0,255} 两个值），
矩形内部仍是米色底（矩形角落取样 (240,229,209) / (247,238,217) 等，见 report.md 表）。
也就是说运行时看到的是一块**比人物略大的米色矩形**，不是人物剪影。
所以本脚本对 9 张走同一套背景遮罩：

    passable = (alpha == 0) 或 (与实测底色的最大通道差 <= TOL=70)
    背景     = 从画布四边 flood fill 能走到的 passable 像素

· farmer 三张 alpha 恒 255 ⇒ 退化成"米色 flood fill"（与 extract-walk-sheet.py 的 background_mask 同判据）。
· merchant/traveller 六张 ⇒ alpha==0 的外圈先被填掉，填充再穿过矩形边上那一圈米色渗进矩形内部。
两种口径下**人物自身**都不可达（皮肤/米白上衣与底色差 <70，但不与画布边缘连通），不会被挖洞。

━━ 产出（每张 sheet 一个子目录）━━
  sheet-debg-<tag>.png            整幅去底 sheet，**原始规格 6528×1088**，RGBA
  frames/walk-<tag>-00..07.png    8 帧独立 PNG，256×384（高 368 + 底 8px，水平居中，与
                                  extract-walk-sheet.py 的归一化画布规格一致）
  sheet-assembled-<tag>.png       8 帧重拼的 sheet，**2048×384**（8 × 256 格，gap=0）
  sheet-assembled-flat-<tag>.png  同一张，平铺到米色底转 RGB —— 闸门脚本走
                                  `Image.open(...).convert("RGB")`，RGBA 直接喂进去会把
                                  透明区的 RGB 当成有效像素，故另出一张给闸门读
  manifest.json                   每帧来源格子 / bbox / 缩放 / alpha 统计，可复算
另外在 --montage 目录写拼版图（棋盘底，透明一眼可见）。

用法：
    python Tools/walk-asset-debg.py                 # 9 张全跑
    python Tools/walk-asset-debg.py --only farmer   # 只跑 farmer 三张
    python Tools/walk-asset-debg.py --bleed 0       # 关闭剪影边缘溢色（默认 2px）
"""
import json
import os
import sys

import numpy as np
from PIL import Image

for _s in (sys.stdout, sys.stderr):
    if hasattr(_s, "reconfigure"):
        _s.reconfigure(encoding="utf-8", errors="replace")

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
ART = os.path.join(REPO, "GameClient/TwelveJade/Assets/Resources/Art/Characters")
OUT = os.path.join(REPO, "Tools/logs/walk-asset")

TOL = 70
FRAMES = 8
CANVAS_W, CANVAS_H = 256, 384
TARGET_H = 368
BOTTOM_PAD = 8
KEY_COLOR = (255, 0, 255)  # 仅给闸门读的原规格平铺版用
BCOLOR = (237, 230, 210)   # 实测底色，与 Art/ 里的原图一致（px[1,1]）

SHEETS = [(k, v) for k in ("farmer", "merchant", "traveller") for v in ("back", "front", "side")]


def sheet_file(kind, view):
    idx = "0" if kind == "farmer" else "1"
    return os.path.join(ART, "%s-male-%s-walksheet-%s-v1.png" % (kind, idx, view))


def tag_of(kind, view):
    return "%s-%s" % (kind, view)


def base_color(rgb, alpha):
    """实测底色：取 alpha 区域内**众数色**（量化到 8 级后取最大桶的均值）。

    为什么不用矩形角： merchant-front 的头发直接压在 alpha 矩形左上角上，
    取四角会拿到 (0,0,0)，passable 就认不出米色底（实测前景 38.6% ≈ alpha 矩形的 38.8%，
    等于一点底都没去掉）。众数色对"矩形边上有东西压着"免疫。
    """
    if alpha.min() == 0:
        sel = rgb[alpha > 0].reshape(-1, 3)
    else:
        sel = rgb.reshape(-1, 3)
    q = (sel // 8).astype(np.int32)
    key = (q[:, 0] << 16) | (q[:, 1] << 8) | q[:, 2]
    vals, counts = np.unique(key, return_counts=True)
    top = vals[counts.argmax()]
    return sel[key == top].mean(axis=0)


def flood_bg(passable):
    """行扫描 flood fill：种子是画布四边的 passable 像素，返回背景布尔图。"""
    bg = np.zeros_like(passable)
    h, w = passable.shape
    stack = [(x, 0) for x in range(w)] + [(x, h - 1) for x in range(w)]
    stack += [(0, y) for y in range(h)] + [(w - 1, y) for y in range(h)]
    while stack:
        x, y = stack.pop()
        if bg[y, x] or not passable[y, x]:
            continue
        row = passable[y]
        x0 = x
        while x0 > 0 and row[x0 - 1] and not bg[y, x0 - 1]:
            x0 -= 1
        x1 = x
        while x1 < w - 1 and row[x1 + 1] and not bg[y, x1 + 1]:
            x1 += 1
        bg[y, x0:x1 + 1] = True
        for ny in (y - 1, y + 1):
            if 0 <= ny < h:
                seg = passable[ny, x0:x1 + 1] & ~bg[ny, x0:x1 + 1]
                idx = np.nonzero(seg)[0]
                if not len(idx):
                    continue
                starts = idx[np.r_[0, np.nonzero(np.diff(idx) != 1)[0] + 1]]
                for s in starts:
                    stack.append((x0 + int(s), ny))
    return bg


def foreground(im):
    """返回 (fg 布尔图, 底色, 说明字符串)。"""
    a = np.array(im.convert("RGBA"))
    rgb = a[..., :3].astype(int)
    alpha = a[..., 3]
    base = base_color(rgb, alpha)
    passable = (alpha == 0) | (np.max(np.abs(rgb - base), axis=2) <= TOL)
    fg = ~flood_bg(passable)
    how = "floodfill(TOL=%d, base=%s, alpha%s)" % (
        TOL, "/".join("%.0f" % v for v in base), "有洞" if alpha.min() == 0 else "恒255")
    return fg, base, how


def frame_canvas(rgb, fg, box, scale, bleed=2):
    """裁 bbox → 溢色 → 按**整张 sheet 统一的 scale** 缩放 → 底对齐、水平居中贴 256x384。

    为什么不是"每帧各自缩到 368 高"：那样每帧的缩放比都不一样，
    等于把"帧高不一致"这个真实毛病**拉伸掩盖掉**（farmer-side 帧高 803..944 会被抹平），
    同时把走路的上下起伏也抹平（闸门 B 判据 topSpread 直接变 0）。
    统一比例 = 保留帧间相对身高与起伏；BOTTOM_PAD 只保证脚底基线一致。
    """
    x0, y0, x1, y1 = box
    sub_rgb = rgb[y0:y1, x0:x1].astype(np.uint8).copy()
    sub_fg = fg[y0:y1, x0:x1].copy()
    for _ in range(bleed):
        holes = ~sub_fg
        if not holes.any():
            break
        acc = np.zeros(sub_rgb.shape, np.float64)
        cnt = np.zeros(sub_fg.shape, np.float64)
        for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
            src_rgb = np.roll(np.roll(sub_rgb, dy, axis=0), dx, axis=1)
            src_fg = np.roll(np.roll(sub_fg, dy, axis=0), dx, axis=1)
            acc += src_rgb * src_fg[..., None]
            cnt += src_fg
        take = holes & (cnt > 0)
        sub_rgb[take] = (acc[take] / cnt[take][:, None]).astype(np.uint8)
    alpha = (sub_fg * 255).astype(np.uint8)
    im = Image.fromarray(np.dstack([sub_rgb, alpha]), "RGBA")
    im = im.resize((max(1, int(round(im.width * scale))),
                    max(1, int(round(im.height * scale)))), Image.LANCZOS)
    canvas = Image.new("RGBA", (CANVAS_W, CANVAS_H), (0, 0, 0, 0))
    canvas.paste(im, ((CANVAS_W - im.width) // 2, CANVAS_H - BOTTOM_PAD - im.height))
    return canvas


def checker(w, h, cell=16):
    im = Image.new("RGB", (w, h), (255, 255, 255))
    px = im.load()
    for y in range(h):
        for x in range(w):
            if ((x // cell) + (y // cell)) % 2:
                px[x, y] = (206, 206, 206)
    return im


def montage(frames, cols, zoom, path):
    fw, fh = CANVAS_W, CANVAS_H
    rows = (len(frames) + cols - 1) // cols
    canvas = checker(fw * cols * zoom, fh * rows * zoom, 16)
    for i, im in enumerate(frames):
        big = im.resize((fw * zoom, fh * zoom), Image.NEAREST)
        canvas.paste(big, ((i % cols) * fw * zoom, (i // cols) * fh * zoom), big)
    canvas.save(path)
    print("  montage %s  %dx%d（%d 列 × %d 行）" % (path, canvas.width, canvas.height, cols, rows))


def run_one(kind, view, bleed=2, montage_dir=None):
    p = sheet_file(kind, view)
    tag = tag_of(kind, view)
    im = Image.open(p)
    a = np.array(im.convert("RGBA"))
    rgb = a[..., :3]
    fg, base, how = foreground(im)
    h, w = fg.shape
    cell_w = w // FRAMES
    alpha0_before = 100.0 * (a[..., 3] == 0).mean()

    # 整幅去底（保持 6528x1088 原始规格）
    out = np.dstack([rgb, np.where(fg, 255, 0).astype(np.uint8)])
    d = os.path.join(OUT, tag)
    os.makedirs(os.path.join(d, "frames"), exist_ok=True)
    Image.fromarray(out, "RGBA").save(os.path.join(d, "sheet-debg-%s.png" % tag))
    # 给闸门读的原规格平铺版：闸门 `Image.open(...).convert("RGB")` 会丢掉 alpha，
    # 直接喂 RGBA 等于把透明区的原始米色又露回去（去底白做）。铺成**键色**后再喂，
    # 闸门自己的背景遮罩会把键色抠掉 —— 抠出来的就是本脚本的 fg。
    # 键色取洋红而不是 extract-walk-sheet.py 的 PAPER(217,185,128)：后者与人物棕褐
    # (146,121,79) 只差 71，贴着 TOL=70 的边，会把鞋/木器当底色吃掉。
    keyed = Image.new("RGB", (w, h), KEY_COLOR)
    keyed.paste(Image.fromarray(out, "RGBA"), (0, 0), Image.fromarray(out, "RGBA"))
    keyed.save(os.path.join(d, "sheet-debg-flat-%s.png" % tag))

    boxes = []
    for i in range(FRAMES):
        sub = fg[:, i * cell_w:(i + 1) * cell_w]
        ys, xs = np.nonzero(sub)
        boxes.append((i * cell_w + int(xs.min()), int(ys.min()),
                      i * cell_w + int(xs.max()) + 1, int(ys.max()) + 1))
    scale = TARGET_H / float(max(b[3] - b[1] for b in boxes))   # 统一比例：以最高帧定标
    manifest = {"sheet": os.path.relpath(p, REPO).replace("\\", "/"), "tag": tag,
                "size": [w, h], "cellW": cell_w, "tol": TOL,
                "baseColor": [round(float(v), 2) for v in base], "bg": how,
                "alpha0Before": round(alpha0_before, 2), "bleed": bleed,
                "sheetScale": round(scale, 5), "scaleBasis": "TARGET_H / 最高帧高", "frames": []}
    for i, box in enumerate(boxes):
        canvas = frame_canvas(rgb, fg, box, scale, bleed)
        name = "walk-%s-%02d.png" % (tag, i)
        canvas.save(os.path.join(d, "frames", name))
        manifest["frames"].append({
            "index": i, "file": name, "cell": [i * cell_w, (i + 1) * cell_w],
            "sourceBox": list(box), "sourceW": box[2] - box[0], "sourceH": box[3] - box[1],
            "outH": int(canvas.getbbox()[3] - canvas.getbbox()[1]),
            "scale": round(scale, 4),
            "fgPx": int(fg[:, i * cell_w:(i + 1) * cell_w].sum()),
        })

    # 8 帧重拼（2048x384）+ 闸门可读的平铺版
    alpha0_after = []
    assemb = Image.new("RGBA", (CANVAS_W * FRAMES, CANVAS_H), (0, 0, 0, 0))
    for i, f in enumerate(manifest["frames"]):
        fr = Image.open(os.path.join(d, "frames", f["file"]))
        assemb.paste(fr, (i * CANVAS_W, 0), fr)
        alpha0_after.append(100.0 * (np.array(fr)[..., 3] == 0).mean())
    assemb.save(os.path.join(d, "sheet-assembled-%s.png" % tag))
    flat = Image.new("RGB", assemb.size, BCOLOR)
    flat.paste(assemb, (0, 0), assemb)
    flat.save(os.path.join(d, "sheet-assembled-flat-%s.png" % tag))
    manifest["alpha0After"] = [round(v, 2) for v in alpha0_after]
    manifest["note"] = ("frames 为 256x384 RGBA；sheet-debg 为原始 6528x1088；"
                        "sheet-assembled 为 2048x384（8x256 格，gap=0）")
    with open(os.path.join(d, "manifest.json"), "w", encoding="utf-8") as fh:
        json.dump(manifest, fh, ensure_ascii=False, indent=2)

    print("  %-18s %dx%d 底色%s 前景 %.1f%%  alpha0 %.1f%% -> %.1f%%" % (
        tag, w, h, "/".join("%.0f" % v for v in base), 100.0 * fg.mean(),
        alpha0_before, alpha0_after[0]))
    if montage_dir:
        montage([Image.open(os.path.join(d, "frames", f["file"])) for f in manifest["frames"]],
                FRAMES, 2, os.path.join(montage_dir, "montage-%s.png" % tag))
    return manifest


def main():
    args = sys.argv[1:]
    bleed = 2
    if "--bleed" in args:
        bleed = int(args[args.index("--bleed") + 1])
    only = None
    if "--only" in args:
        only = args[args.index("--only") + 1]
    os.makedirs(OUT, exist_ok=True)
    print("=== 去底：9 张走帧 sheet（输出 Tools/logs/walk-asset/）===")
    manis = []
    for kind, view in SHEETS:
        if only and kind != only:
            continue
        manis.append(run_one(kind, view, bleed, OUT))
    if not only:
        montage([Image.open(os.path.join(OUT, "farmer-%s/frames/walk-farmer-%s-%02d.png" % (v, v, i)))
                 for v in ("back", "front", "side") for i in range(FRAMES)],
                FRAMES, 1, os.path.join(OUT, "montage-farmer-walk.png"))
    with open(os.path.join(OUT, "debg-summary.json"), "w", encoding="utf-8") as fh:
        json.dump([{k: m[k] for k in ("tag", "size", "bg", "baseColor", "alpha0Before")}
                   for m in manis], fh, ensure_ascii=False, indent=2)
    print("汇总: %s" % os.path.join(OUT, "debg-summary.json"))


if __name__ == "__main__":
    main()
