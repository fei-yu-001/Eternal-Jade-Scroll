#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""行走 sheet 抠图门禁：残留白底 / 背景色连通块 / 白边光晕 / 帧间配色 / 身高一致。

━━ 为什么有这道闸（2026-11-01，主架构师七七派工）━━
斐哥在游戏里看到"人物行走时出现留白色块"。实测
`farmer-male-0-walksheet-side-v1.png` 第 0/1/3/4/7 帧各有
28975/19109/37809/24489/23605 个纯白像素——抠图泛洪只从四边做，
被轮廓包住的白底清不掉。

━━ 判据（6 条，各自独立报数、独立点火）━━
  C1 残留背景块    每格 alpha>200 且与纸色每通道差 <12 的像素数；任一格 >200 判红
  C2 背景色连通块  每格与纸色每通道差 <12 的连通域，面积 >800 且**不贴格边**的个数
                   >0 判红（整张卡纸走这条）
  C3 白边光晕      半透明边缘(0<alpha<255)平均亮度 - 不透明内部(alpha>240)平均亮度
                   任一格 >40 判红
  C4 帧间一致性    8 格两两调色板交集（4bit/通道，剔除纸色与半透明），
                   最小值 <0.62 判红；报最小值、中位数、最差帧对
  C5 身高一致      8 格 bbox 高变异系数 >2.0% 判红
  C6 碎白边        纸色像素的连通域面积在 8~800 px、**且必须贴剪影边界**
                   （任一像素与 alpha<=200 的像素 4 邻接）的个数 >0 判红。
                   白布/皮肤高光在人物内部，不贴边；真残留一定在轮廓上。

阈值是初值。实测后若明显不合理，报告主架构师裁定；**不许为了让现有素材变绿而放宽**。

━━ 纸色怎么定（2026-11-01 第一次实测定下来的）━━
  固定常量 `(248,240,222)` 只对得上 farmer-side / traveller-side / merchant-side；
  merchant-back 是 `(240,229,209)`、merchant-front 是 `(245,233,208)`、
  traveller-back 是 `(242,229,207)`——统一用常量 + 容差 12 会把这三张的纸底漏掉一半以上。
  现在的取法：每格找“alpha>200 且 min(RGB)>=190”的亮像素，取**最大连续块**；
  该块 ≥ 1% 格面积时，用它像素的**中位色**当本格纸色（卡纸有批次色差）；
  否则用常量 (248,240,222)。这样 farmer-side 脚边那块 28975px 白底与
  merchant/traveller 的整卡都能对上，空底格也不会凭空造出纸色。

━━ 两个口径说明（免得下一个人重踩）━━
  1. C2/C6 用同一张纸色掩膜：>800px 的算“整卡”（C2），8~800px 的算“碎白边”（C6）；
     C1 是总量。三条会同时红——这是定义决定的，不是重复计分。
  2. 纸色估不出来时（没有 ≥1% 的连续亮块）用常量；若某张图真的没有纸底，
     常量也匹配不到像素，三条都是 0。

━━ 用法 ━━
  python Tools/check-walksheet-cutout.py                       # 默认 9 张运行时 sheet
  python Tools/check-walksheet-cutout.py <sheet.png> [...]      # 指定文件
  python Tools/check-walksheet-cutout.py --root <dir>           # 扫目录
  python Tools/check-walksheet-cutout.py --ref <sheet.png>      # 可选参考锚点（C4 跨图比较）
     给了 --ref 时 C4 取"8 格内部最小值"与"每格对参考图交集"的较小者；
     格内限 0.62（实测断层 0.466 -> 0.779），跨图限 0.50
     （与 check-character-identity.py 同一套校准：同角色 0.619/0.670，阈值 0.50）。
  python Tools/check-walksheet-cutout.py --selftest             # 12 个图像夹具 + 6 个输入完整性检查

  默认从 FrontEndWorld.LoadWalkSheet 读取必需文件清单，缺一张即失败。
  显式文件列表中任何路径缺失也失败，禁止静默过滤。
  扫目录时固定跳过 Library/ Temp/ .git/ node_modules/（以及 obj/ Build/ Logs/ .batch/）。

━━ 输出 ━━
  每条判据一行 PASS/FAIL，末尾 RESULT: PASS/FAIL；明细全表写
  Tools/logs/walksheet-cutout.log（selftest 写 walksheet-cutout-selftest.log，不覆盖基线）。

━━ 退出码 ━━
  0 = PASS；1 = FAIL；2 = 输入/环境错误（2 不是绿）
"""
import argparse
import os
import re
import sys
import tempfile
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

import world_paths as wp

for _s in (sys.stdout, sys.stderr):
    if hasattr(_s, "reconfigure"):
        _s.reconfigure(encoding="utf-8", errors="replace")

CELLS = 8
C1_MAX_PAPER = 200
PAPER = np.array([248, 240, 222], dtype=np.int32)
PAPER_TOL = 12
PAPER_MIN_FRAC = 0.01
C2_MIN_AREA = 800
C3_MAX_DELTA = 40.0
C3_MIN_SAMPLES = 50
C4_MIN_INTERSECT = 0.62
C4_REF_MIN = 0.50
C5_MAX_CV = 0.02
C6_MIN_AREA = 8
C6_MAX_AREA = 800

SKIP_DIRS = {"Library", "Temp", ".git", "node_modules", "obj", "Build", "Logs", ".batch"}
OPAQUE_ALPHA = 200
BRIGHT_MIN = 190
COLOR_BITS = 4
PALETTE_BINS = 1 << (COLOR_BITS * 3)
PAPER_RGBA = (248, 240, 222, 255)


# ────────────────────────── 基础工具 ──────────────────────────
def _components(mask):
    """4 邻接连通域（行程 + 并查集）。

    返回 [{"area": n, "border": bool, "runs": [(y, s, e), ...]}, ...]。
    带 runs 是为了能从最大块里取像素算纸色，不用再扫一遍全图。
    """
    h, w = mask.shape
    parent = []

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a

    def union(a, b):
        ra, rb = find(a), find(b)
        if ra != rb:
            parent[rb] = ra

    rows = []
    prev = []
    for y in range(h):
        idx = np.flatnonzero(mask[y])
        if idx.size == 0:
            rows.append([])
            prev = []
            continue
        cuts = np.flatnonzero(np.diff(idx) > 1)
        starts = np.concatenate(([0], cuts + 1))
        ends = np.concatenate((cuts, [idx.size - 1]))
        cur = []
        for s_i, e_i in zip(starts, ends):
            s, e = int(idx[s_i]), int(idx[e_i])
            rid = len(parent)
            parent.append(rid)
            cur.append((s, e, rid))
        j = 0
        for s, e, rid in cur:
            while j < len(prev) and prev[j][1] < s:
                j += 1
            k = j
            while k < len(prev) and prev[k][0] <= e:
                union(rid, prev[k][2])
                k += 1
        rows.append(cur)
        prev = cur

    area = {}
    border = {}
    runs_of = {}
    for y, cur in enumerate(rows):
        for s, e, rid in cur:
            r = find(rid)
            area[r] = area.get(r, 0) + (e - s + 1)
            runs_of.setdefault(r, []).append((y, s, e))
            if y == 0 or y == h - 1 or s == 0 or e == w - 1:
                border[r] = True
    return [{"area": a, "border": border.get(r, False), "runs": runs_of[r]}
            for r, a in area.items()]


def _luma(rgb):
    return 0.299 * rgb[:, :, 0] + 0.587 * rgb[:, :, 1] + 0.114 * rgb[:, :, 2]


def _palette(cell, white):
    """4bit/通道 归一化直方图；剔除近白（C1 同口径）与半透明像素。"""
    a = cell[:, :, 3]
    rgb = cell[:, :, :3].astype(np.int32)
    keep = (a > 128) & ~white
    idx = (rgb[:, :, 0][keep] >> 4) * 256 + (rgb[:, :, 1][keep] >> 4) * 16 + (rgb[:, :, 2][keep] >> 4)
    hist = np.bincount(idx, minlength=PALETTE_BINS).astype(np.float64)
    total = hist.sum()
    return hist / total if total else hist, int(total)


def _intersection(a, b):
    return float(np.minimum(a, b).sum())


def _paper_color(cell_rgb, cell_a, cell_area):
    """本格纸色：最大连续亮块（≥1% 格面积）的中位色，否则用常量 PAPER。"""
    bright = (cell_a > OPAQUE_ALPHA) & (cell_rgb.min(axis=2) >= BRIGHT_MIN)
    comps = _components(bright)
    if comps:
        big = max(comps, key=lambda c: c["area"])
        if big["area"] >= PAPER_MIN_FRAC * cell_area:
            px = np.concatenate([cell_rgb[y, s:e + 1] for (y, s, e) in big["runs"]])
            return np.median(px, axis=0).astype(np.int32)
    return PAPER.copy()


def _paper_mask(cell_rgb, cell_a, paper):
    return (cell_a > OPAQUE_ALPHA) & (np.abs(cell_rgb - paper) < PAPER_TOL).all(axis=2)


def _edge_band(cell_a):
    """剪影边界带：与 alpha<=200 的像素 4 邻接的不透明像素（格子外按透明算）。"""
    opaque = cell_a > OPAQUE_ALPHA
    pad = np.zeros((opaque.shape[0] + 2, opaque.shape[1] + 2), dtype=bool)
    pad[1:-1, 1:-1] = opaque
    inner = pad[1:-1, 1:-1]
    return inner & ~(pad[:-2, 1:-1] & pad[2:, 1:-1] & pad[1:-1, :-2] & pad[1:-1, 2:])


def read_sheet(path):
    im = Image.open(path).convert("RGBA")
    arr = np.asarray(im)
    h, w, _ = arr.shape
    if w % CELLS:
        raise ValueError("宽度 %d 不能被 %d 整除" % (w, CELLS))
    if w // CELLS < 16 or h < 16:
        raise ValueError("尺寸过小 %dx%d" % (w, h))
    return arr


# ────────────────────────── 判据 ──────────────────────────
def analyze(arr):
    h, w, _ = arr.shape
    cw = w // CELLS
    m = {
        "size": (w, h),
        "cell": (cw, h),
        "c1": [], "c1_max_block": 0, "c2": [], "c2_area": [],
        "c3": [], "c6": [], "papers": [], "heights": [], "hists": [], "fg_px": [],
    }
    for i in range(CELLS):
        cell = arr[:, i * cw:(i + 1) * cw]
        a = cell[:, :, 3]
        rgb = cell[:, :, :3].astype(np.int32)
        paper = _paper_color(rgb, a, cw * h)
        pmask = _paper_mask(rgb, a, paper)
        m["papers"].append(tuple(int(v) for v in paper))

        m["c1"].append(int(pmask.sum()))
        comps = _components(pmask)
        m["c1_max_block"] = max(m["c1_max_block"], max((c["area"] for c in comps), default=0))

        big = [c["area"] for c in comps if c["area"] > C2_MIN_AREA and not c["border"]]
        m["c2"].append(len(big))
        m["c2_area"].append(int(max(big)) if big else 0)
        band = _edge_band(a)
        m["c6"].append(sum(1 for c in comps
                            if C6_MIN_AREA <= c["area"] <= C6_MAX_AREA
                            and any(band[y, s:e + 1].any() for y, s, e in c["runs"])))

        edge = (a > 0) & (a < 255)
        inner_opaque = a > 240
        if edge.sum() < C3_MIN_SAMPLES or inner_opaque.sum() < C3_MIN_SAMPLES:
            m["c3"].append(None)
        else:
            lum = _luma(rgb.astype(np.float64))
            m["c3"].append(float(lum[edge].mean() - lum[inner_opaque].mean()))

        fg = a > 128
        ys = np.flatnonzero(fg.any(axis=1))
        m["heights"].append(int(ys[-1] - ys[0] + 1) if ys.size else 0)
        hist, n = _palette(cell, pmask)
        m["hists"].append(hist)
        m["fg_px"].append(n)

    pairs = [(float(_intersection(m["hists"][i], m["hists"][j])), i, j)
             for i in range(CELLS) for j in range(i + 1, CELLS)]
    pairs.sort()
    m["c4_pairs"] = pairs
    m["c4_min"], m["c4_worst"] = pairs[0][0], (pairs[0][1], pairs[0][2])
    m["c4_median"] = float(np.median([p[0] for p in pairs]))

    hh = np.array(m["heights"], dtype=np.float64)
    m["c5_cv"] = float(hh.std() / hh.mean()) if hh.mean() > 0 else 1.0
    return m


def verdicts(m, ref_hist=None):
    v = {}
    c1_max = max(m["c1"])
    v["C1"] = (c1_max <= C1_MAX_PAPER,
               "max=%dpx(f%d) 限 %d ／ 最大纸块 %dpx" % (
                   c1_max, m["c1"].index(c1_max), C1_MAX_PAPER, m["c1_max_block"]))
    c2_n = sum(m["c2"])
    v["C2"] = (c2_n == 0, "%d 个 >%dpx 大块纸（各格 %s）" % (c2_n, C2_MIN_AREA, m["c2"]))

    valid3 = [x for x in m["c3"] if x is not None]
    if valid3:
        c3_max = max(valid3)
        v["C3"] = (c3_max <= C3_MAX_DELTA, "Δ%+.1f 限 %+.0f" % (c3_max, C3_MAX_DELTA))
    else:
        v["C3"] = (True, "n/a（无半透明边缘像素）")

    c4_note = "min=%.3f(f%d-f%d) 中位 %.3f 限 %.2f" % (
        m["c4_min"], m["c4_worst"][0], m["c4_worst"][1], m["c4_median"], C4_MIN_INTERSECT)
    c4_ok = m["c4_min"] >= C4_MIN_INTERSECT
    if ref_hist is not None:
        ref_min = min(_intersection(h, ref_hist) for h in m["hists"])
        c4_note += " ／ ref-min=%.3f 限 %.2f" % (ref_min, C4_REF_MIN)
        c4_ok = c4_ok and ref_min >= C4_REF_MIN
    v["C4"] = (c4_ok, c4_note)

    v["C5"] = (m["c5_cv"] <= C5_MAX_CV,
               "CV=%.2f%% 限 %.2f%%（高 %d..%d）" % (
                   100 * m["c5_cv"], 100 * C5_MAX_CV, min(m["heights"]), max(m["heights"])))

    c6_n = sum(m["c6"])
    v["C6"] = (c6_n == 0, "%d 个 %d~%dpx 碎边（各格 %s）" % (
        c6_n, C6_MIN_AREA, C6_MAX_AREA, m["c6"]))
    return v


# ────────────────────────── 输出 ──────────────────────────
LABELS = {
    "C1": "残留背景块  ",
    "C2": "大块纸底    ",
    "C3": "白边光晕    ",
    "C4": "帧间一致性  ",
    "C5": "身高一致    ",
    "C6": "碎白边      ",
}


def report(path, m, v):
    name = os.path.basename(path)
    w, h = m["size"]
    print("── %s  %dx%d（%d/格）" % (name, w, h, m["cell"][0]))
    for key in ("C1", "C2", "C3", "C4", "C5", "C6"):
        ok, detail = v[key]
        print("   %s %s %s" % ("PASS" if ok else "FAIL", key, LABELS[key]), detail)
    ok_all = all(x[0] for x in v.values())
    print("   => %s" % ("PASS" if ok_all else "FAIL"))
    return ok_all


def log_sheet(fh, path, m, v):
    fh.write("── %s  %dx%d  cell=%dx%d\n" % (
        os.path.basename(path), m["size"][0], m["size"][1], m["cell"][0], m["cell"][1]))
    fh.write("纸色/格    : %s\n" % " ".join("%d,%d,%d" % c for c in m["papers"]))
    fh.write("C1 每格纸px : %s   最大块=%dpx\n" % (
        " ".join("%d" % x for x in m["c1"]), m["c1_max_block"]))
    fh.write("C2 每格大块 : %s   最大=%dpx\n" % (
        " ".join("%d" % x for x in m["c2"]), max(m["c2_area"])))
    fh.write("C6 每格碎边 : %s\n" % " ".join("%d" % x for x in m["c6"]))
    fh.write("C3 每格Δ亮度: %s\n" % (" ".join("n/a" if x is None else "%+.1f" % x for x in m["c3"])
                                    + ("  （n/a 格=半透明样本不足）" if any(x is None for x in m["c3"]) else "")))
    fh.write("C5 每格高px : %s   CV=%.2f%%\n" % (
        " ".join("%d" % x for x in m["heights"]), 100 * m["c5_cv"]))
    fh.write("C4 28 对    :\n")
    line = []
    for val, i, j in m["c4_pairs"]:
        line.append("f%d-f%d=%.3f" % (i, j, val))
        if len(line) == 4:
            fh.write("    " + "  ".join(line) + "\n")
            line = []
    if line:
        fh.write("    " + "  ".join(line) + "\n")
    for key in ("C1", "C2", "C3", "C4", "C5", "C6"):
        ok, detail = v[key]
        fh.write("%s %s %s %s\n" % ("PASS" if ok else "FAIL", key, LABELS[key], detail))
    fh.write("\n")


def iter_sheets(root):
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = sorted(d for d in dirnames if d not in SKIP_DIRS)
        for fn in sorted(filenames):
            if fn.lower().endswith(".png") and "walksheet" in fn.lower():
                yield os.path.join(dirpath, fn)


def resolve_sheets(sheets=(), root=None, project=None):
    """默认检查运行时实际引用；漏文件不能退化成只检查剩下的几张。"""
    if sheets and root is not None:
        raise ValueError("指定文件与 --root 不能同时使用")
    if sheets:
        paths = [os.fspath(p) for p in sheets]
    elif root is not None:
        if not os.path.isdir(root):
            raise ValueError("目录不存在 %s" % root)
        paths = list(iter_sheets(root))
    else:
        project = Path(project or wp.unity_project_root())
        loader = project / "Assets/Scripts/Presentation/FrontEndWorld.cs"
        refs = sorted(set(re.findall(
            r'Resources\s*\.\s*Load\s*<\s*Texture2D\s*>\s*\(\s*"(Art/Characters/Walk/[^"\n]+)"',
            loader.read_text(encoding="utf-8"))))
        if not refs:
            raise ValueError("运行时 Walk 引用清单为空：%s" % loader)
        resources = project / "Assets/Resources"
        paths = [str(resources / (ref + ".png")) for ref in refs]
        # 未接线但已放进 Walk 的图也不能逃过素材检查。
        paths = sorted(set(paths + list(iter_sheets(resources / "Art/Characters/Walk"))))
    if not paths:
        raise ValueError("没找到 walksheet")
    missing = [str(p) for p in paths if not os.path.isfile(p)]
    if missing:
        raise ValueError("走帧文件缺失（禁止跳过）：" + ", ".join(missing))
    return paths


def input_selftests():
    """仅测文件枚举；放在 .batch，不接触真实 Resources。"""
    base = Path(wp.repo_root()) / ".batch/qingyan-cutout"
    base.mkdir(parents=True, exist_ok=True)
    tmp = Path(tempfile.mkdtemp(prefix="input-", dir=base))
    assert tmp.resolve().is_relative_to(base.resolve())
    good = tmp / "one-walksheet.png"
    good.touch()
    empty = tmp / "empty"
    empty.mkdir()
    project = tmp / "project"
    loader = project / "Assets/Scripts/Presentation/FrontEndWorld.cs"
    loader.parent.mkdir(parents=True)
    refs = ["Art/Characters/Walk/test-walksheet-%d" % i for i in range(3)]
    loader.write_text("\n".join('Resources.Load<Texture2D>("%s")' % r for r in refs), encoding="utf-8")
    files = [project / "Assets/Resources" / (r + ".png") for r in refs]
    files[0].parent.mkdir(parents=True)
    for path in files[:2]:
        path.touch()
    results = []

    def rejects(name, call):
        try:
            call()
        except (ValueError, OSError):
            results.append((name, True))
        else:
            results.append((name, False))

    rejects("explicit-missing", lambda: resolve_sheets([good, tmp / "missing.png"]))
    rejects("empty-directory", lambda: resolve_sheets(root=empty))
    rejects("runtime-one-missing", lambda: resolve_sheets(project=project))
    files[2].touch()
    results.append(("runtime-complete", set(resolve_sheets(project=project)) == set(map(str, files))))
    rejects("conflicting-inputs", lambda: resolve_sheets([good], root=tmp))
    loader.write_text("// no runtime walk references", encoding="utf-8")
    rejects("runtime-no-references", lambda: resolve_sheets(project=project))
    return results


# ────────────────────────── selftest 夹具 ──────────────────────────
CW, CH = 120, 160
BODY = (90, 120, 80)
HEAD = (200, 170, 140)


def _draw_figure(d, top, height, body=BODY, fringe=None, radius=0):
    box_body = [35 - radius, top + 22 - radius, 85 + radius, top + height + radius]
    box_head = [45 - radius, top - radius, 75 + radius, top + 38 + radius]
    if fringe is not None:
        d.rectangle(box_body, fill=fringe)
        d.ellipse(box_head, fill=fringe)
    d.rectangle([35, top + 22, 85, top + height], fill=body + (255,))
    d.ellipse([45, top, 75, top + 38], fill=HEAD + (255,))


def _synth(kind):
    img = Image.new("RGBA", (CW * CELLS, CH), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    for i in range(CELLS):
        ox = i * CW
        cell = Image.new("RGBA", (CW, CH), (0, 0, 0, 0))
        cd = ImageDraw.Draw(cell)

        if kind in ("c3", "c3_green"):
            fringe = (255, 255, 255, 128) if kind == "c3" else BODY + (128,)
            _draw_figure(cd, 20, 120, fringe=fringe, radius=4)
        elif kind in ("c4", "c4_green"):
            body = ((170, 60, 60) if i < 4 else (60, 60, 170)) if kind == "c4" else BODY
            _draw_figure(cd, 20, 120, body=body)
            if kind == "c4_green" and i == 7:
                cd.rectangle([35, 42, 84, 66], fill=(60, 90, 140, 255))
        elif kind in ("c5", "c5_green"):
            hgt = (70 + i * 5) if kind == "c5" else (118 + i % 3)
            _draw_figure(cd, CH - hgt - 10, hgt)
        else:
            _draw_figure(cd, 20, 120)
            if kind == "c1":
                cd.rectangle([0, 60, 39, 99], fill=PAPER_RGBA)      # 1600px，贴左边框
            elif kind == "c2":
                cd.rectangle([10, 60, 49, 99], fill=PAPER_RGBA)     # 1600px，不贴边
            elif kind == "c6":
                cd.rectangle([5, 60, 9, 69], fill=PAPER_RGBA)       # 5x10=50px
                cd.rectangle([95, 60, 99, 69], fill=PAPER_RGBA)     # 5x10=50px
            elif kind in ("c1_green", "c6_green"):
                for k in range(4):                                  # 4 个 5px 碎点
                    cd.rectangle([5 + k * 10, 70, 9 + k * 10, 70], fill=PAPER_RGBA)

        img.alpha_composite(cell, (ox, 0))
    return img


# (名称, 夹具, 期望点红的判据集合)。C1/C2/C6 共用一张纸色掩膜：
# 大块纸会同时顶红 C1+C2 —— 这是定义决定的连带，写进期望里，不假称"只红一条"。
SELFTEST_CASES = [
    ("C1-red", "c1", ("C1",)),
    ("C1-green", "c1_green", ()),
    ("C2-red", "c2", ("C1", "C2")),
    ("C2-green", "c2_green", ()),
    ("C3-red", "c3", ("C3",)),
    ("C3-green", "c3_green", ()),
    ("C4-red", "c4", ("C4",)),
    ("C4-green", "c4_green", ()),
    ("C5-red", "c5", ("C5",)),
    ("C5-green", "c5_green", ()),
    ("C6-red", "c6", ("C6",)),
    ("C6-green", "c6_green", ()),
]


def selftest():
    tmp = os.path.join(wp.repo_root(), ".batch", "qingyan-cutout", "selftest")
    os.makedirs(tmp, exist_ok=True)
    log_path = os.path.join(wp.tools_log_root(), "walksheet-cutout-selftest.log")
    os.makedirs(wp.tools_log_root(), exist_ok=True)
    ok_n = 0
    keys = ("C1", "C2", "C3", "C4", "C5", "C6")
    print("SELFTEST（12 个合成夹具，每条判据一红一绿）")
    with open(log_path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("# walksheet-cutout selftest（合成夹具，不依赖外部素材）\n\n")
        for name, kind, expect_red in SELFTEST_CASES:
            path = os.path.join(tmp, name + ".png")
            _synth(kind).save(path)
            m = analyze(read_sheet(path))
            v = verdicts(m)
            actual = tuple(k for k in keys if not v[k][0])
            ok = actual == expect_red
            ok_n += ok
            print("  %s %-9s 点红 %-10s 期望 %-10s%s" % (
                "OK  " if ok else "FAIL", name,
                ",".join(actual) or "全绿", ",".join(expect_red) or "全绿",
                "" if ok else "  <- 不符"))
            fh.write("%s %-9s 点红 %s 期望 %s\n" % (
                "OK  " if ok else "FAIL", name,
                ",".join(actual) or "全绿", ",".join(expect_red) or "全绿"))
            fh.write("    " + " ".join("%s=%s" % (k, "PASS" if v[k][0] else "FAIL") for k in keys) + "\n")
            fh.write("    明细: " + " | ".join("%s %s" % (k, v[k][1]) for k in keys) + "\n\n")
        inputs = input_selftests()
        for name, ok in inputs:
            line = "SELFTEST: %s %s" % ("PASS" if ok else "FAIL", name)
            print(line)
            fh.write(line + "\n")
            ok_n += ok
        total = len(SELFTEST_CASES) + len(inputs)
        fh.write("RESULT: %s  （%d/%d）\n" % ("PASS" if ok_n == total else "FAIL", ok_n, total))
    print("RESULT: %s  （%d/%d）" % ("PASS" if ok_n == total else "FAIL", ok_n, total))
    print("明细 %s" % log_path)
    return 0 if ok_n == total else 1


# ────────────────────────── 主流程 ──────────────────────────
def main():
    ap = argparse.ArgumentParser(add_help=True)
    ap.add_argument("sheets", nargs="*")
    ap.add_argument("--root")
    ap.add_argument("--ref")
    ap.add_argument("--selftest", action="store_true")
    ap.add_argument("--log")
    args = ap.parse_args()

    if args.selftest:
        return selftest()

    try:
        paths = resolve_sheets(args.sheets, args.root)
    except (ValueError, OSError) as exc:
        print("RESULT: FAIL（输入错误：%s）" % exc)
        return 2

    ref_hist = None
    if args.ref:
        try:
            rarr = read_sheet(args.ref)
            rw = rarr.shape[1] // CELLS
            pool = []
            for i in range(CELLS):
                c = rarr[:, i * rw:(i + 1) * rw]
                a = c[:, :, 3]
                rgb = c[:, :, :3].astype(np.int32)
                paper = _paper_color(rgb, a, rw * rarr.shape[0])
                h_, n_ = _palette(c, _paper_mask(rgb, a, paper))
                if n_:
                    pool.append(h_)
            if not pool:
                raise ValueError("参考图没有有效前景")
            ref_hist = np.mean(pool, axis=0)
        except Exception as exc:
            print("ERROR: 参考图读不了 %s: %s" % (args.ref, exc))
            return 2

    log_path = args.log or os.path.join(wp.tools_log_root(), "walksheet-cutout.log")
    os.makedirs(os.path.dirname(os.path.abspath(log_path)), exist_ok=True)
    counts = dict((k, 0) for k in ("C1", "C2", "C3", "C4", "C5", "C6"))
    fails = 0
    with open(log_path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("# walksheet 抠图门禁明细\n")
        fh.write("# 阈值：C1>%dpx  C2>%dpx²  C3>%+.0f  C4<%.2f/ref %.2f  C5>%.2f%%  C6=%d~%dpx\n" % (
            C1_MAX_PAPER, C2_MIN_AREA, C3_MAX_DELTA, C4_MIN_INTERSECT, C4_REF_MIN,
            100 * C5_MAX_CV, C6_MIN_AREA, C6_MAX_AREA))
        fh.write("# 文件：%d 张\n\n" % len(paths))
        for path in paths:
            try:
                m = analyze(read_sheet(path))
            except Exception as exc:
                print("RESULT: FAIL（素材无法读取：%s: %s）" % (path, exc))
                return 2
            v = verdicts(m, ref_hist)
            ok = report(path, m, v)
            log_sheet(fh, path, m, v)
            if not ok:
                fails += 1
            for k in counts:
                if not v[k][0]:
                    counts[k] += 1
        summary = "；".join("%s %d 红" % (k, counts[k]) for k in ("C1", "C2", "C3", "C4", "C5", "C6"))
        fh.write("RESULT: %s  （%d 张：%d 绿 / %d 红；%s）\n" % (
            "PASS" if fails == 0 else "FAIL", len(paths), len(paths) - fails, fails, summary))

    print("")
    print("RESULT: %s  （%d 张：%d 绿 / %d 红；%s）" % (
        "PASS" if fails == 0 else "FAIL", len(paths), len(paths) - fails, fails, summary))
    print("明细 %s" % log_path)
    return 0 if fails == 0 else 1


if __name__ == "__main__":
    sys.exit(main())