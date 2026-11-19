# -*- coding: utf-8 -*-
"""WALK-RIG-2 ｜ 走帧注册与拼 sheet（2026-10-29，纯本地，0 积分）。

━━ 为什么需要「注册」这一步 ━━
`assemble-walk-frames.py` 只做「等距 + 底边对齐」。它治不了真正的问题：
同一个人用 AI 逐帧生成时，**每帧的人物大小和水平位置都会漂**。直接底边对齐拼起来，
播放时人就一耸一耸、左右横跳。

本脚本做四件事，顺序固定：
  1. 去底：四边 flood fill，判据与 `walk-asset-debg.py` 一致（alpha==0 或 与实测底色差<=70）；
  2. **按头高归一**：头高（头顶→颈部最窄行）是**姿态无关**的稳定量；
     用剪影高度归一会把「走路该有的上下起伏」一起抹掉，那是自毁动画；
  3. 地面吸附：整帧平移，让最低点落在统一地面线；
  4. 水平居中：按**头中心**对齐（不用剪影中心，摆臂会让剪影中心左右漂）。

产出两套：
  · 4 格 sheet（正确周期：contact/down/pass/up）
  · 8 格 sheet（把 4 格按序重复一遍，可直接塞进现有 frameCount=8 的运行期，不必改代码）
    侧身走路的后半程剪影与前半程相同（左右腿互换但轮廓一致），所以重复 4 格在视觉上是
    成立的完整步态——这正是老坑「8 帧像同一姿势」的物理原因，不是素材坏了。

用法：
    python Tools/assemble-walk-sheet-v2.py --view side \
        --frames <dir>/side-f00-v1.png ... --out <sheet.png> --report <report.json>
"""
import argparse
import glob
import json
import os
import sys

import numpy as np
from PIL import Image

for _s in (sys.stdout, sys.stderr):
    if hasattr(_s, "reconfigure"):
        _s.reconfigure(encoding="utf-8", errors="replace")

TOL = 70          # 与 walk-asset-debg.py / extract-walk-sheet.py 同判据
CELL_W = 512
CELL_H = 683      # 3:4，与锚点/运行期 frameAspect 一致
GROUND = CELL_H - 24   # 格子内地面线（底部留 24px 空档，给脚趾投影）
HEAD_TARGET = int(CELL_H * 0.150)   # 归一后的头高（占格高 15% ≈ 真实人体比例）


def cut_background(path):
    """返回 (RGBA 图, alpha)。四边 flood fill：只有与画布边缘连通的底色才算背景。"""
    rgb = np.array(Image.open(path).convert("RGB")).astype(int)
    h, w, _ = rgb.shape
    corners = np.array([rgb[5, 5], rgb[5, w - 6], rgb[h - 6, 5], rgb[h - 6, w - 6]])
    base = np.median(corners, axis=0)
    diff = np.abs(rgb - base).max(axis=2)
    passable = diff <= TOL

    visited = np.zeros((h, w), dtype=bool)
    stack = []
    for x in range(w):
        stack.append((0, x)); stack.append((h - 1, x))
    for y in range(h):
        stack.append((y, 0)); stack.append((y, w - 1))
    while stack:
        y, x = stack.pop()
        if y < 0 or y >= h or x < 0 or x >= w or visited[y, x] or not passable[y, x]:
            continue
        visited[y, x] = True
        stack.append((y + 1, x)); stack.append((y - 1, x))
        stack.append((y, x + 1)); stack.append((y, x - 1))

    background = visited
    fg = ~background
    alpha = np.where(fg, 255, 0).astype(np.uint8)
    rgba = np.dstack([np.array(Image.open(path).convert("RGB")), alpha])
    return Image.fromarray(rgba.astype(np.uint8), "RGBA"), fg


def head_height(fg):
    """头顶 → 颈部最窄行（top 6%~20% 内最窄的那一行）。姿态无关。"""
    rows = np.where(fg.any(axis=1))[0]
    top, bottom = rows[0], rows[-1]
    height = bottom - top + 1
    widths = []
    for y in range(top, bottom + 1):
        xs = np.where(fg[y])[0]
        widths.append(0 if len(xs) == 0 else xs[-1] - xs[0] + 1)
    widths = np.array(widths)
    lo, hi = int(height * 0.06), int(height * 0.20)
    neck = int(np.argmin(widths[lo:hi])) + lo
    return neck, top, bottom


def measure(path):
    """第一遍：只量，不缩放。返回去底图与基准量。"""
    img, fg = cut_background(path)
    neck, top, bottom = head_height(fg)
    return img, neck, top, bottom


def register(path, global_scale):
    img, neck, top, bottom = measure(path)
    scale = HEAD_TARGET / max(1, neck) * global_scale
    new_size = (max(1, int(img.width * scale)), max(1, int(img.height * scale)))
    scaled = img.resize(new_size, Image.LANCZOS)
    alpha = np.array(scaled)[:, :, 3] > 8
    rows = np.where(alpha.any(axis=1))[0]
    cols = np.where(alpha.any(axis=0))[0]
    # 头中心（等比缩放后仍用前 18% 高度带的中点，等价于头中心）
    head_band = alpha[rows[0]:rows[0] + max(1, int((rows[-1] - rows[0]) * 0.18))]
    hx = np.where(head_band.any(axis=0))[0]
    head_cx = (hx[0] + hx[-1]) / 2.0
    return dict(img=scaled, bottom=rows[-1], head_cx=head_cx,
                head_px=neck * scale, silhouette_px=(rows[-1] - rows[0] + 1))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--view", required=True)
    parser.add_argument("--frames", nargs="+", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--report", default=None)
    args = parser.parse_args()

    paths = []
    for pattern in args.frames:
        paths.extend(sorted(glob.glob(pattern)) if any(c in pattern for c in "*?[") else [pattern])

    # 第一遍：按头高算各自缩放，再取一个**统一**的全局系数把最高的那帧塞进格子。
    # 关键：全局系数必须所有帧共用，否则每帧各自缩放 = 把走路的上下起伏抹平（那是自毁动画）。
    raw = [measure(p) for p in paths]
    target_scales = [HEAD_TARGET / max(1, neck) for _, neck, _, _ in raw]
    heights = []
    for (img, neck, top, bottom), scale in zip(raw, target_scales):
        heights.append((bottom - top + 1) * scale)
    available = GROUND - int(CELL_H * 0.04)
    global_scale = min(1.0, available / max(heights)) if max(heights) > available else 1.0

    cells, report = [], []
    for path in paths:
        reg = register(path, global_scale)
        cell = Image.new("RGBA", (CELL_W, CELL_H), (0, 0, 0, 0))
        x = int(round(CELL_W / 2 - reg["head_cx"]))
        y = int(round(GROUND - reg["bottom"]))
        cell.alpha_composite(reg["img"], (x, y))
        cells.append(cell)
        report.append(dict(file=os.path.basename(path),
                           headPx=round(float(reg["head_px"]), 1),
                           silhouettePx=int(reg["silhouette_px"])))
        print("registered %-18s head=%.1fpx silhouette=%dpx" %
              (os.path.basename(path), reg["head_px"], reg["silhouette_px"]))

    heads = [r["headPx"] for r in report]
    silhouettes = [r["silhouettePx"] for r in report]
    print("head spread: %.2f%%  silhouette spread: %.2f%% (后者应保留起伏，越大说明步态越明显)" %
          ((max(heads) - min(heads)) / np.mean(heads) * 100,
           (max(silhouettes) - min(silhouettes)) / np.mean(silhouettes) * 100))

    def save(cell_list, path, suffix):
        sheet = Image.new("RGBA", (CELL_W * len(cell_list), CELL_H), (0, 0, 0, 0))
        for index, cell in enumerate(cell_list):
            sheet.alpha_composite(cell, (index * CELL_W, 0))
        os.makedirs(os.path.dirname(path), exist_ok=True)
        sheet.save(path)
        print("wrote %s (%d frames, %dx%d)" % (path, len(cell_list), *sheet.size))
        return path

    four = save(cells, args.out, "4")
    eight_path = args.out.replace(".png", "-x8.png")
    eight = save(cells + cells, eight_path, "8")

    if args.report:
        with open(args.report, "w", encoding="utf-8") as handle:
            json.dump(dict(view=args.view, cell=[CELL_W, CELL_H], ground=GROUND,
                           headTarget=HEAD_TARGET, frames=report,
                           sheet4=os.path.basename(four), sheet8=os.path.basename(eight)),
                      handle, ensure_ascii=False, indent=2)
        print("wrote %s" % args.report)
    return 0


if __name__ == "__main__":
    sys.exit(main())
