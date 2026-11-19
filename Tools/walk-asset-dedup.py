# -*- coding: utf-8 -*-
"""WALK-ASSET ｜ 走帧「8 帧到底有没有走周期」的逐帧数值判定（只读，不改 Art/）。

━━ 为什么另起一支而不是扩 check-walk-sheet-motion.py ━━
那条闸给的是一张 sheet 的**聚合**三条判据（起伏 / 触地变化 / 帧高偏离），
任务 WALK-ASSET 要的却是**逐帧 8 行**的姿势差异量：能指着 f3 说"它跟 f2 一模一样"。
聚合闸说不出这件事，且任务书禁止改已有脚本，故本脚本只做测量、不下"过/不过"的判决。

━━ 三个口径（数都能用本脚本复算）━━
1) 前景：直接把 `walk-asset-debg.py` 的 `foreground()` 借过来（同一套背景遮罩），
   不另写一份 —— 否则"去底用一套、评判用另一套"，数字对不上图。
   它做的事：passable = (alpha==0) 或 (与实测底色差<=70)，背景 = 从画布四边 flood fill 到的 passable。
   **不能用单纯 alpha**：merchant/traveller 六张的 alpha 是实心矩形（fill=1.0000），
   拿 alpha 当剪影量出来的全是矩形宽高，不是人物姿势。
2) 分帧：9 张全是 6528×1088 = 8×816，且 `--grid-check` 断言每帧前景都落在自己的 816 列格内。
   所以按**运行时 UV 的同一刀**切格（不按连通块切 —— 连通块切法会把"手臂与躯干隔 1px"的帧判成两块）。
3) 姿势差异 IoU：每帧取格内前景 bbox → 裁出 → 高度归一到 368 → 水平居中贴 256 宽画布 → 二值 IoU。
   IoU=1.000 即两帧剪影逐像素一致（= 复制姿势）；越小越不像。
   水平居中是有意的：游戏里精灵恒按角色原点居中，帧在格子里偏几像素是作画抖动，不算姿势差异。

用法：
    python Tools/walk-asset-dedup.py               # 跑 9 张，写 frames-metrics.json / .md
    python Tools/walk-asset-dedup.py --grid-check  # 只做分帧前提断言
"""
import importlib.util
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
OUT = os.path.join(REPO, "Tools/logs/walk-asset")
DEBG = os.path.join(HERE, "walk-asset-debg.py")

SHEETS = [(k, v) for k in ("farmer", "merchant", "traveller") for v in ("back", "front", "side")]
FRAMES = 8
CANVAS_W, CANVAS_H = 256, 384
TARGET_H = 368
BOTTOM_PAD = 8
LEG_BAND = 0.30
DUP_IOU = 0.9995      # "逐像素同姿势"：归一化后 IoU 到这个数就是同一张剪影
LIKE_IOU = 0.97       # "同一姿势的不同重画"：只差边缘抖动
# 下面两条只用来判"下半身根本没动"（合起来看，单看任一条都会被长袍/宽摆骗过）：
SPAN_RATIO_WEAK = 1.15   # 下30%横跨度 max/min 低于此值 = 双脚从没分开过
CX_RANGE_WEAK = 12.0     # 下30%质心横向极差（px）低于此值 = 支撑脚从没换过
HEIGHT_RATIO_BAD = 1.10  # 同一角色的 8 帧帧高 max/min 超过此值 = 比例漂移（farmer-side 1.18）
# "同一个姿势被重画 8 次"：整体轮廓几乎钉死（高度、顶点、两两 IoU 三者同时满足）。
# 这不是逐像素复制（边缘也会抖），但和走周期是两回事 —— 闸门 B 判据（起伏）会直接判死。
REDRAW_H_RATIO = 1.02
REDRAW_TOP_SPREAD = 3
REDRAW_PAIR_MIN = 0.85


def load_debg():
    spec = importlib.util.spec_from_file_location("walk_asset_debg", DEBG)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def cell_box(fg, i, cell_w):
    """格内前景 → (x0,y0,x1,y1) + 主体/碎屑统计。

    bbox 只取**8 连通最大块**：这几张 sheet 里有"手臂与躯干只隔 1px 米色"和"人物旁边飘着几块
    指甲盖大的碎屑"（见 frames-metrics.md 的格内连通块列），
    拿全前景 bbox 归一化会被碎屑把框撑大、把人物挤歪 —— 实测 merchant-back 的 f1 框
    比 f0 宽 150px 就是这么来的。碎屑不丢，单独记 fragPx。
    """
    sub = fg[:, i * cell_w:(i + 1) * cell_w]
    ys, xs = np.nonzero(sub)
    if not len(ys):
        return None
    if _LBL is None:
        box = (i * cell_w + int(xs.min()), int(ys.min()),
               i * cell_w + int(xs.max()) + 1, int(ys.max()) + 1)
        return {"box": box, "comps": None, "fragPx": 0, "bodyPx": int(len(ys))}
    lab, n = _LBL(sub, structure=_EIGHT)
    sizes = np.bincount(lab.ravel())
    sizes[0] = 0
    big = int(sizes.argmax())
    m = lab == big
    ys2, xs2 = np.nonzero(m)
    box = (i * cell_w + int(xs2.min()), int(ys2.min()),
           i * cell_w + int(xs2.max()) + 1, int(ys2.max()) + 1)
    return {"box": box, "comps": int(n), "fragPx": int(len(ys) - len(ys2)), "bodyPx": int(len(ys2))}


try:
    from scipy import ndimage as _NDI

    _LBL, _EIGHT = _NDI.label, np.ones((3, 3), bool)
except ImportError:      # 没 scipy 也能跑，退化成全前景 bbox、连通块列留空
    _LBL, _EIGHT = None, None


def frame_stats(fg, cell, i, cell_w):
    box = cell["box"]
    x0, y0, x1, y1 = box
    w, h = x1 - x0, y1 - y0
    cx = (x0 + x1 - 1) / 2.0
    cell_cx = i * cell_w + cell_w / 2.0
    band_y0 = y1 - max(1, int(round(h * LEG_BAND)))
    sub = fg[band_y0:y1, x0:x1]
    ys, xs = np.nonzero(sub)
    if not len(ys):
        leg = dict(legSpan=0, legLeft=0.0, legRight=0.0, legCx=0.0, legPx=0)
    else:
        gx0, gx1 = int(xs.min()) + x0, int(xs.max()) + x0
        mean_x = float(xs.mean()) + x0
        leg = dict(legSpan=gx1 - gx0 + 1, legLeft=round(mean_x - gx0, 2),
                   legRight=round(gx1 - mean_x, 2), legCx=round(mean_x - cx, 2),
                   legPx=int(len(ys)))
    return dict(
        x0c=int(x0 - i * cell_w), x1c=int(x1 - 1 - i * cell_w),
        top=int(y0), bot=int(y1 - 1), w=int(w), h=int(h),
        cxInCell=round(cx - cell_cx, 2),
        areaBody=int(fg[y0:y1, x0:x1].sum()),
        bodyPx=cell["bodyPx"], fragPx=cell["fragPx"], comps=cell["comps"],
        **leg)


def norm_mask(fg, box):
    x0, y0, x1, y1 = box
    im = Image.fromarray((fg[y0:y1, x0:x1] * 255).astype(np.uint8), "L")
    scale = TARGET_H / float(im.height)
    im = im.resize((max(1, int(round(im.width * scale))), TARGET_H), Image.LANCZOS)
    canvas = Image.new("L", (CANVAS_W, CANVAS_H), 0)
    canvas.paste(im, ((CANVAS_W - im.width) // 2, CANVAS_H - BOTTOM_PAD - TARGET_H))
    return np.array(canvas) > 127


def iou(a, b):
    u = int(np.logical_or(a, b).sum())
    return float(np.logical_and(a, b).sum()) / u if u else 0.0


def analyze(kind, view, dbg):
    p = dbg.sheet_file(kind, view)
    im = Image.open(p)
    fg, base, how = dbg.foreground(im)
    h, w = fg.shape
    cell_w = w // FRAMES
    boxes = [cell_box(fg, i, cell_w) for i in range(FRAMES)]
    empty = [i for i, b in enumerate(boxes) if b is None]
    ok = [b for b in boxes if b]
    stats = [frame_stats(fg, c, i, cell_w) for i, c in enumerate(boxes) if c]
    masks = [norm_mask(fg, c["box"]) for c in ok]
    grid_ok = not empty and all(
        c["box"][0] >= i * cell_w and (c["box"][2] - 1) < (i + 1) * cell_w
        for i, c in enumerate(boxes) if c)

    keys = ("x0c", "x1c", "top", "bot", "w", "h", "cxInCell",
            "legSpan", "legLeft", "legRight", "legCx")
    adj = []
    for i in range(len(stats) - 1):
        d = {k: round(abs(stats[i + 1][k] - stats[i][k]), 2) for k in keys}
        d["pair"] = [i, i + 1]
        d["iou"] = round(iou(masks[i], masks[i + 1]), 4)
        adj.append(d)

    pair = []
    for i in range(len(masks)):
        for j in range(i + 1, len(masks)):
            pair.append(dict(pair=[i, j], iou=round(iou(masks[i], masks[j]), 4)))

    dup = sorted({tuple(x["pair"]) for x in pair if x["iou"] >= DUP_IOU})
    like = sorted({tuple(x["pair"]) for x in pair if x["iou"] >= LIKE_IOU})

    # 同姿势聚类（IoU>=LIKE_IOU 连边，连通分量 = 一组"同一个姿势"）
    parent = list(range(len(masks)))

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a

    for x in like:
        ra, rb = find(x[0]), find(x[1])
        if ra != rb:
            parent[ra] = rb
    groups = {}
    for i in range(len(masks)):
        groups.setdefault(find(i), []).append(i)
    clusters = sorted((sorted(g) for g in groups.values()), key=lambda g: g[0])

    return dict(
        sheet=os.path.relpath(p, REPO).replace("\\", "/"), tag=dbg.tag_of(kind, view),
        size=[im.width, im.height], mode=im.mode, cellW=cell_w,
        bg=how, baseColor=[round(float(v), 2) for v in base], gridOk=grid_ok,
        emptyCells=empty, frames=stats, adjacent=adj, pairIou=pair,
        dupPairs=[list(x) for x in dup], likePairs=[list(x) for x in like],
        poseClusters=clusters,
    )


def grid_check(dbg):
    ok = True
    for kind, view in SHEETS:
        p = dbg.sheet_file(kind, view)
        im = Image.open(p)
        fg, base, how = dbg.foreground(im)
        cell_w = im.width // FRAMES
        bad = []
        for i in range(FRAMES):
            c = cell_box(fg, i, cell_w)
            if c is None:
                bad.append("f%d=EMPTY" % i)
            elif c["box"][0] < i * cell_w or (c["box"][2] - 1) >= (i + 1) * cell_w:
                bad.append("f%d=CROSS(%d..%d)" % (i, c["box"][0], c["box"][2] - 1))
        ok = ok and not bad
        print("%-42s %s  格宽 %d  底色 %s  %s" % (
            os.path.basename(p), "OK" if not bad else "BAD", cell_w,
            "/".join("%.0f" % v for v in base), " ".join(bad)))
    print("GRID-CHECK: %s（8 格 × 816，每帧前景都完整落在自己格内）" % ("PASS" if ok else "FAIL"))
    return ok


def summary_of(r):
    """把一张 sheet 的逐帧数字压成一组可比量（报告里所有表都从这儿取）。"""
    f = r["frames"]
    hs = [x["h"] for x in f]
    sp = [x["legSpan"] for x in f]
    cx = [x["legCx"] for x in f]
    adj = [a["iou"] for a in r["adjacent"]]
    pair = [x["iou"] for x in r["pairIou"]]
    return dict(
        hMin=min(hs), hMax=max(hs), hRatio=max(hs) / float(min(hs)),
        spMin=min(sp), spMax=max(sp), spRatio=max(sp) / float(max(1, min(sp))),
        cxRange=max(cx) - min(cx),
        adjMin=min(adj), adjMax=max(adj),
        pairMin=min(pair), pairMax=max(pair),
        topSpread=max(x["top"] for x in f) - min(x["top"] for x in f),
        nLike=len(r["likePairs"]), nDup=len(r["dupPairs"]),
        fragTotal=sum(x["fragPx"] for x in f),
    )


def verdict(r):
    """把数字翻成一句人话。阈值全在本文件顶部，改阈值就等于改结论，报告里必须跟着改。"""
    s = summary_of(r)
    tags = []
    if s["nDup"]:
        tags.append("逐像素复制帧")
    if s["nLike"]:
        tags.append("近似复制")
    if s["spRatio"] < SPAN_RATIO_WEAK and s["cxRange"] < CX_RANGE_WEAK:
        tags.append("步态幅度不足")
    if s["hRatio"] >= HEIGHT_RATIO_BAD:
        tags.append("帧高漂移")
    if (s["hRatio"] <= REDRAW_H_RATIO and s["topSpread"] <= REDRAW_TOP_SPREAD
            and s["pairMin"] >= REDRAW_PAIR_MIN):
        tags.append("同姿势重绘(无步态)")
    if not tags:
        tags.append("非复制")
    why = [
        "帧高 %d..%d（比 %.2f）" % (s["hMin"], s["hMax"], s["hRatio"]),
        "下30%%跨度 %d..%d（比 %.2f）" % (s["spMin"], s["spMax"], s["spRatio"]),
        "下半身质心横向极差 %.1fpx" % s["cxRange"],
        "相邻帧 IoU %.3f..%.3f" % (s["adjMin"], s["adjMax"]),
        "最大帧对 IoU %.4f" % s["pairMax"],
        "最小帧对 IoU %.4f" % s["pairMin"],
        "顶点纵向极差 %dpx" % s["topSpread"],
    ]
    if r["likePairs"]:
        why.append("IoU>=%.2f 的帧对：%s" % (
            LIKE_IOU, " ".join("f%d-f%d(%.3f)" % (a, b, iou_of(r, a, b)) for a, b in r["likePairs"])))
    if r["dupPairs"]:
        why.append("IoU>=%.4f 的帧对：%s" % (
            DUP_IOU, " ".join("f%d-f%d" % tuple(x) for x in r["dupPairs"])))
    if s["fragTotal"]:
        why.append("格内主体之外的碎屑共 %dpx" % s["fragTotal"])
    return ("+".join(tags), "；".join(why))


def iou_of(r, a, b):
    for x in r["pairIou"]:
        if x["pair"] == [a, b]:
            return x["iou"]
    return None


def main():
    dbg = load_debg()
    if "--grid-check" in sys.argv:
        sys.exit(0 if grid_check(dbg) else 1)

    os.makedirs(OUT, exist_ok=True)
    results = []
    for kind, view in SHEETS:
        results.append(analyze(kind, view, dbg))

    print("%-16s %-13s %-13s %-7s %-13s %-8s %-5s %s" % (
        "sheet", "帧高", "下30%跨度", "质心极差", "相邻IoU", "最大帧对", "重复对", "判决"))
    for r in results:
        s = summary_of(r)
        v, _ = verdict(r)
        print("%-16s %4d..%-4d %.2f %4d..%-4d %.2f %7.1f %5.3f..%.3f %.4f %3d+%d %s" % (
            r["tag"], s["hMin"], s["hMax"], s["hRatio"], s["spMin"], s["spMax"], s["spRatio"],
            s["cxRange"], s["adjMin"], s["adjMax"], s["pairMax"], s["nLike"], s["nDup"], v))

    with open(os.path.join(OUT, "frames-metrics.json"), "w", encoding="utf-8") as fh:
        json.dump(results, fh, ensure_ascii=False, indent=2)
    write_md(results)
    print("写出: Tools/logs/walk-asset/frames-metrics.json")
    print("写出: Tools/logs/walk-asset/frames-metrics.md")


def md_matrix(r):
    m = {}
    for x in r["pairIou"]:
        m[tuple(x["pair"])] = x["iou"]
    out = ["| |" + "|".join(" f%d " % j for j in range(FRAMES)) + "|",
           "|---|" + "---:|" * FRAMES]
    for i in range(FRAMES):
        cells = []
        for j in range(FRAMES):
            if i == j:
                cells.append(" - ")
            else:
                v = m[(min(i, j), max(i, j))]
                cells.append("**%.3f**" % v if v >= LIKE_IOU else "%.3f" % v)
        out.append("| **f%d** |" % i + "|".join(cells) + "|")
    return out


def write_md(results):
    L = ["<!-- 由 Tools/walk-asset-dedup.py 生成，勿手改 -->", "",
         "## 口径（4 条，缺一条数字就不可复算）", "",
         "1. 前景 = `walk-asset-debg.py` 的 `foreground()`：`passable = (alpha==0) 或 (与实测底色最大通道差<=70)`，"
         "背景 = 从画布四边 flood fill 能走到的 passable。",
         "   **不用单纯 alpha** —— merchant/traveller 六张的 alpha 是实心矩形（每格 `fill=1.0000`，"
         "alpha 只有 {0,255} 两个值），拿 alpha 当剪影量出来的是矩形宽高，不是人物。",
         "2. 分帧 = 按运行时 UV 同一刀切 816 列一格（`--grid-check` 断言每帧前景都不出格）；"
         "不按连通块切，因为有多张 sheet 的手臂与躯干只隔 1px 底色。",
         "3. 每帧 bbox 取**8 连通最大块**（主体）；主体之外的像素记 `碎屑px`，不参与归一化，"
         "否则一块指甲盖大的碎屑就能把 bbox 撑宽 150px（merchant-back f1 实测）。",
         "4. IoU = 主体 bbox 裁图 → 高归一 368 → 水平居中贴 256 宽 → 二值 IoU。1.0000 即剪影逐像素一致。",
         "5. **`下30%跨度`/`质心x` 只在下摆不遮脚时才可信**：脚底下画了贴地投影（merchant/traveller 侧视明显），"
         "长袍/披风也会把这一带撑满，此时它们量的是衣服+投影而不是迈步。判死亡只看 IoU 表也能得出结论，"
         "这几个量是给人看的旁证，不是唯一依据。",
         "",
         "## 判决阈值（改这里就等于改结论）", "",
         "| 常量 | 值 | 含义 |", "|---|---:|---|",
         "| `DUP_IOU` | %.4f | 帧对 IoU ≥ 此值 = 逐像素复制帧 |" % DUP_IOU,
         "| `LIKE_IOU` | %.2f | 帧对 IoU ≥ 此值 = 同一姿势的不同重画（参与同姿势聚类） |" % LIKE_IOU,
         "| `SPAN_RATIO_WEAK` | %.2f | 下 30%% 横跨度 max/min < 此值 **且** 下一行也成立 ⇒ 步态幅度不足 |" % SPAN_RATIO_WEAK,
         "| `CX_RANGE_WEAK` | %.1f | 下 30%% 质心横向极差(px) < 此值 |" % CX_RANGE_WEAK,
         "| `HEIGHT_RATIO_BAD` | %.2f | 8 帧帧高 max/min ≥ 此值 ⇒ 比例漂移 |" % HEIGHT_RATIO_BAD,
         "| `REDRAW_H_RATIO`/`REDRAW_TOP_SPREAD`/`REDRAW_PAIR_MIN` | %.2f/%.0f/%.2f | 三条同时成立 ⇒ 同一个姿势被重画 8 次（轮廓钉死，无步态） |"
         % (REDRAW_H_RATIO, REDRAW_TOP_SPREAD, REDRAW_PAIR_MIN),
         "",
         "## 总表", "",
         "| sheet | 帧高 min..max（比） | 下30%%跨度 min..max（比） | 下半身质心横向极差 | 相邻帧 IoU | 最大帧对 IoU | ≥%.2f 对 | ≥%.4f 对 | 判决 |" % (LIKE_IOU, DUP_IOU),
         "|---|---|---|---:|---|---|---:|---:|---|"]
    for r in results:
        s = summary_of(r)
        v, _ = verdict(r)
        L.append("| %s | %d..%d (%.2f) | %d..%d (%.2f) | %.1f px | %.3f..%.3f | %.4f | %d | %d | **%s** |" % (
            r["tag"], s["hMin"], s["hMax"], s["hRatio"], s["spMin"], s["spMax"], s["spRatio"],
            s["cxRange"], s["adjMin"], s["adjMax"], s["pairMax"], s["nLike"], s["nDup"], v))
    L.append("")
    for r in results:
        v, why = verdict(r)
        L += ["## %s" % os.path.basename(r["sheet"]), "",
              "`%dx%d` mode=%s 格宽 %d 实测底色 %s 分帧断言 %s" % (
                  r["size"][0], r["size"][1], r["mode"], r["cellW"],
                  "/".join("%.0f" % x for x in r["baseColor"]),
                  "PASS" if r["gridOk"] else "FAIL"),
              "",
              "**判决：%s**" % v, "",
              "- " + why.replace("；", "\n- "),
              "",
              "同姿势聚类（IoU≥%.2f 连边，一个簇 = 一组同一个姿势）：%s" % (
                  LIKE_IOU, " / ".join("{" + ",".join("f%d" % i for i in g) + "}" for g in r["poseClusters"])),
              "",
              "8×8 帧对 IoU（粗体 = ≥%.2f）：" % LIKE_IOU, ""]
        L += md_matrix(r)
        L += ["",
              "| 帧 | x0c | x1c | top | bot | 宽 | 高 | 格中心x偏移 | 下30%跨度 | 左半宽 | 右半宽 | 质心x偏移 | 主体px | 碎屑px | 连通块 |",
              "|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|"]
        for i, x in enumerate(r["frames"]):
            L.append("| f%d | %d | %d | %d | %d | %d | %d | %.1f | %d | %.1f | %.1f | %.1f | %d | %d | %s |" % (
                i, x["x0c"], x["x1c"], x["top"], x["bot"], x["w"], x["h"], x["cxInCell"],
                x["legSpan"], x["legLeft"], x["legRight"], x["legCx"], x["bodyPx"], x["fragPx"],
                "-" if x["comps"] is None else x["comps"]))
        L += ["",
              "| 相邻对 | Δx0c | Δx1c | Δtop | Δbot | Δ宽 | Δ高 | Δ格中心x | Δ下30%跨度 | Δ左半宽 | Δ右半宽 | Δ质心x | IoU |",
              "|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|"]
        for a in r["adjacent"]:
            L.append("| f%d-f%d | %.1f | %.1f | %.1f | %.1f | %.1f | %.1f | %.2f | %.1f | %.2f | %.2f | %.2f | %.4f |" % (
                a["pair"][0], a["pair"][1], a["x0c"], a["x1c"], a["top"], a["bot"], a["w"], a["h"],
                a["cxInCell"], a["legSpan"], a["legLeft"], a["legRight"], a["legCx"], a["iou"]))
        L.append("")
    with open(os.path.join(OUT, "frames-metrics.md"), "w", encoding="utf-8") as fh:
        fh.write("\n".join(L) + "\n")


if __name__ == "__main__":
    main()
