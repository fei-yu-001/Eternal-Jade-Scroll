#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""measure-walk-cycle.py —— flipbook 周期顺序标定（交接包 2026-10-06-AA · AA-3 · B-23）。

回答一个问题：**这批行走帧的物理周期顺序是什么**——不按文件名猜（AA-1.1 的教训：
文件名里的 L/R 暗示"左右脚成对"，实测 L/R 差 56~58%，根本不是镜像；8 帧是一个朝向的完整周期）。

信号（每帧三个量，全部从像素量出来）：
1. leg_span  下肢区（图像高度 72%~96%，膝到脚）前景的水平跨度 —— pass 相两腿重叠最窄；
2. gap       下肢区 bbox 中部的背景缝隙宽 —— 双腿分开时中部有缝（contact 相最大）；
3. silhouette 整身前景 bbox 宽 —— 摆臂/重心带来的次级变化。

分组判据（AA-6 的"宽-窄-宽-窄"周期）：把 8 帧按 leg_span+gap 的联合信号分四簇
（宽=contact×2、中宽=down/up×2、窄=pass×2），簇序 = 宽→中→窄→中。
**簇内两帧的先后**（先左脚还是先右脚）单帧像素定不出时间方向 —— manifest 里如实标
"intra-cluster 需运行期/人眼定"（B-26 的过渡与 B-31 的一致性检查收口），不假装测出来了。

--selftest：程序合成一个已知周期的 8 帧（两腿按相位开合，底色同真实素材 237,230,210），
工具恢复的簇序必须与合成顺序一致；8 张同姿势复制必须被判"无周期"而失败。样本现造，不依赖仓库文件。

用法：
  python Tools/measure-walk-cycle.py --frames "GameClient/TwelveJade/Assets/Resources/Art/豆包/walk-side*.png"
  python Tools/measure-walk-cycle.py --selftest
退出码：正常分簇 0；无周期/素材缺失 1。
"""
import argparse
import glob
import io
import json
import os
import sys

import numpy as np
from PIL import Image

LOWER_BAND = (0.72, 0.96)   # 膝到脚：两腿分离度的测量带


def background_color(img):
    """实测底色：四角 24px 块的像素中位数（不写死，AA-6.2 的教训——键控底色写死会判错新素材）。"""
    w, h = img.size
    corners = np.concatenate([
        np.asarray(img.crop((0, 0, 24, 24))).reshape(-1, 3),
        np.asarray(img.crop((w - 24, 0, w, 24))).reshape(-1, 3),
        np.asarray(img.crop((0, h - 24, 24, h))).reshape(-1, 3),
        np.asarray(img.crop((w - 24, h - 24, w, h))).reshape(-1, 3),
    ])
    return np.median(corners, axis=0)


def measure(path):
    img = Image.open(path).convert("RGB")
    arr = np.asarray(img).astype(np.int16)
    bg = background_color(img)
    # 前景 = 与实测底色的距离 > 40（欧氏），容忍轻度压缩噪点
    dist = np.sqrt(((arr - bg) ** 2).sum(axis=2))
    fg = dist > 40.0
    if fg.sum() < 50:
        return None  # 空图/全背景

    H, W = fg.shape
    cols = np.where(fg.any(axis=0))[0]
    rows = np.where(fg.any(axis=1))[0]
    sil_w = int(cols.max() - cols.min() + 1)

    y0, y1 = int(H * LOWER_BAND[0]), int(H * LOWER_BAND[1])
    band = fg[y0:y1, :]
    bcols = np.where(band.any(axis=0))[0]
    leg_span = int(bcols.max() - bcols.min() + 1) if len(bcols) else 0

    # 中缝：下肢 bbox 中部 30% 区域内，最长一段连续背景列
    if len(bcols):
        lo, hi = bcols.min(), bcols.max()
        mid0, mid1 = lo + int((hi - lo) * 0.35), lo + int((hi - lo) * 0.65)
        col_has_fg = band[:, mid0:mid1 + 1].any(axis=0)
        gap = longest_false_run(col_has_fg)
        gap = int(gap * (mid1 - mid0 + 1) / max(len(col_has_fg), 1))  # 按比例还原到像素
    else:
        gap = 0

    return {
        "file": os.path.basename(path),
        "silhouette_w": sil_w,
        "leg_span": leg_span,
        "gap": gap,
        "signal": leg_span + gap * 2.0,   # 联合信号：跨度为主、缝为辅（缝是"分开"的最直接证据）
    }


def longest_false_run(mask):
    best = run = 0
    for v in mask:
        run = 0 if v else run + 1
        best = max(best, run)
    return best


def cluster_order(frames):
    """按联合信号把帧排成 宽→中→窄→中 的四簇；返回 (ordered, clusters, periodic)。"""
    sig = np.array([f["signal"] for f in frames], dtype=float)
    order_by_sig = np.argsort(-sig)              # 从宽到窄
    n = len(frames)
    if n < 4:
        return None, [], False

    # 四簇切分：宽(最大 1/4) / 中宽 / 窄(最小 1/4) / 中——用"窄簇=信号最小的 n/4"与
    # "宽簇=最大的 n/4"锚定，中段两簇按剩余信号二分。
    k = max(1, n // 4)
    wide = sorted(order_by_sig[:k].tolist())
    narrow = sorted(order_by_sig[n - k:].tolist())
    middle = [i for i in order_by_sig[k:n - k]]
    mid_sorted = sorted(middle, key=lambda i: -sig[i])
    # 中段切成"中宽"(靠宽端)与"中窄"(靠窄端)
    half = len(mid_sorted) // 2
    mid_wide, mid_narrow = sorted(mid_sorted[:half]), sorted(mid_sorted[half:])

    periodic = (sig[wide].min() > sig[mid_wide].max()) and (sig[mid_wide].min() > sig[mid_narrow].max()) \
        if mid_wide and mid_narrow else (sig[wide].min() > sig[narrow].max())
    ordered = wide + mid_wide + narrow + mid_narrow   # 宽 → 中宽 → 窄 → 中窄 = contact→down→pass→up 族的周期形态
    clusters = {
        "contact(宽)": wide,
        "down/中宽": mid_wide,
        "pass(窄)": narrow,
        "up/中窄": mid_narrow,
    }
    return ordered, clusters, bool(periodic)


def selftest():
    """合成已知周期：腿缝按 contact60→down40→pass0→up20 ×2；顺序必须被恢复；同帧复制必须判无周期。"""
    bgc, fgc = (237, 230, 210), (90, 70, 50)
    W, H = 256, 512
    body_box = (108, 60, 148, 300)                      # 躯干（固定）
    phases = [60, 40, 0, 20, 60, 40, 0, 20]             # 两腿中缝宽：宽→中→窄→中→宽→中→窄→中
    truth = ["c1", "d1", "p1", "u1", "c2", "d2", "p2", "u2"]
    frames = []
    for i, gap in enumerate(phases):
        img = Image.new("RGB", (W, H), bgc)
        px = img.load()
        for x in range(body_box[0], body_box[2]):
            for y in range(body_box[1], body_box[3]):
                px[x, y] = fgc
        leg_w, leg_h = 26, int(H * 0.24)
        ly = int(H * 0.72)
        center = W // 2
        # 两腿：缝 = gap（分开时中缝可见）；gap=0 时两腿并拢（中缝为 0）
        half_gap = gap // 2
        for side in (-1, 1):
            lx = center + side * half_gap - leg_w // 2 + (side > 0) * half_gap
            lx = center - leg_w // 2 + side * half_gap - (half_gap if side < 0 else 0)
            for x in range(max(0, lx), min(W, lx + leg_w)):
                for y in range(ly, ly + leg_h):
                    px[x, y] = fgc
        p = os.path.join(_tmpdir(), "syn-%02d-%s.png" % (i, truth[i]))
        img.save(p)
        frames.append(p)

    measured = [m for m in (measure(f) for f in frames) if m]
    ordered, clusters, periodic = cluster_order(measured)
    ok = True
    print("[selftest] 合成周期相位 %s" % phases)
    print("[selftest] 工具分簇 %s" % {k: [truth[i] for i in v] for k, v in clusters.items()})
    if not periodic:
        print("[selftest] FAIL 真周期被 judged 无周期")
        ok = False
    # 宽簇必须是 contact（两帧）、窄簇必须是 pass（两帧）
    wide_names = {truth[i] for i in clusters.get("contact(宽)", [])}
    narrow_names = {truth[i] for i in clusters.get("pass(窄)", [])}
    if wide_names != {"c1", "c2"}:
        print("[selftest] FAIL 宽簇应为两次 contact，实测 %s" % wide_names)
        ok = False
    if narrow_names != {"p1", "p2"}:
        print("[selftest] FAIL 窄簇应为两次 pass，实测 %s" % narrow_names)
        ok = False

    # 反例：8 张同姿势复制 → 必须判无周期
    same = []
    for i in range(8):
        p = os.path.join(_tmpdir(), "same-%02d.png" % i)
        Image.open(frames[0]).save(p)
        same.append(p)
    measured_same = [m for m in (measure(f) for f in same) if m]
    _, _, periodic_same = cluster_order(measured_same)
    if periodic_same:
        print("[selftest] FAIL 同姿势复制竟被判有周期")
        ok = False
    else:
        print("[selftest] ok: 同姿势复制被判无周期（工具拒绝假周期）")
    print("[selftest] %s" % ("PASS" if ok else "FAIL"))
    return ok


_tmp = None


def _tmpdir():
    global _tmp
    if _tmp is None:
        import tempfile
        _tmp = tempfile.mkdtemp(prefix="b23-")
    return _tmp


def main():
    if sys.stdout.encoding and sys.stdout.encoding.lower() != "utf-8":
        try:
            sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        except Exception:
            pass
    ap = argparse.ArgumentParser()
    ap.add_argument("--frames", help="行走帧 glob（真实素材）")
    ap.add_argument("--out", help="manifest 输出路径（JSON）")
    ap.add_argument("--selftest", action="store_true")
    args = ap.parse_args()

    if args.selftest:
        sys.exit(0 if selftest() else 1)

    if not args.frames:
        print("需要 --frames 或 --selftest")
        sys.exit(1)
    paths = sorted(glob.glob(args.frames))
    if len(paths) < 4:
        print("RESULT: FAIL —— 帧数 %d < 4，凑不成周期" % len(paths))
        sys.exit(1)
    measured = [m for m in (measure(p) for p in paths) if m]
    if len(measured) < 4:
        print("RESULT: FAIL —— 有效帧 %d < 4（其余全背景/不可读）" % len(measured))
        sys.exit(1)
    ordered, clusters, periodic = cluster_order(measured)
    manifest = {
        "generated": "measure-walk-cycle.py (B-23)",
        "note": "顺序由像素信号（leg_span+gap）分簇得出，不是文件名顺序；簇内先后需运行期/人眼定（AA-6：L/R 非镜像）",
        "periodic": periodic,
        "ordered_cycle": [measured[i]["file"] for i in ordered] if ordered else None,
        "clusters": {k: [measured[i]["file"] for i in v] for k, v in clusters.items()},
        "measurements": measured,
    }
    text = json.dumps(manifest, ensure_ascii=False, indent=2)
    print(text)
    if args.out:
        io.open(args.out, "w", encoding="utf-8").write(text + "\n")
        print("[manifest] 已写 " + args.out)
    print("RESULT: %s —— %d 帧分四簇，周期性=%s" % ("PASS" if periodic else "FAIL", len(measured), periodic))
    sys.exit(0 if periodic else 1)


if __name__ == "__main__":
    main()
