# -*- coding: utf-8 -*-
"""把豆包 8 张侧身走路单帧归一到 farmer walksheet 的格子口径。

问题：豆包单帧 1632x2752，walksheet 格子 816x1088；且 8 张帧高 2272..2568 不齐。
做法：同一缩放系数（不许逐帧独立缩放，否则人物会呼吸）+ 每帧底边对齐地面线。

用法：python Tools/_prep-side-frames.py <输出目录>
"""
import os
import sys

import numpy as np
from PIL import Image

for _s in (sys.stdout, sys.stderr):
    if hasattr(_s, "reconfigure"):
        _s.reconfigure(encoding="utf-8", errors="replace")

SRC = os.path.join("GameClient", "TwelveJade", "Assets", "Resources", "Art", "豆包")
# 走周期顺序：左腿四相 → 右腿四相。文件名原序是 L/R 交替，直接拼会相位乱跳。
ORDER = [
    "walk-sidecontact-L-v1.png",
    "walk-sidedown-L-v1.png",
    "walk-sidepass-L-v1.png",
    "walk-sideup-L-v1.png",
    "walk-sidecontact-R-v1.png",
    "walk-sidedown-R-v1.png",
    "walk-sidepass-R-v1.png",
    "walk-sideup-R-v1.png",
]
CELL_W, CELL_H = 816, 1088
GROUND_Y = 1083          # 与现役 walksheet 一致：内容最低行贴在 1083，底下留 5px
ALPHA_SOLID = 200        # 豆包图脚下有 alpha=128 的淡阴影，不能算进“实体底边”
TARGET_MEAN_H = 1020     # 现役 front 平均 1034 / back 平均 1016，侧身取中间


def solid_bbox(im):
    a = np.array(im)[:, :, 3]
    ys, xs = np.nonzero(a >= ALPHA_SOLID)
    return xs.min(), ys.min(), xs.max(), ys.max()


# 走周期里 L/R 是同相位的镜像，人物应当一样大。生成器对个别帧画小了
# （实测 sideup-R 源高 2270 vs sideup-L 2564，差 13%），逐帧独立缩放会把
# 真实的身体起伏一起抹平（topSpread 归零 ⇒ 运动闸反而判“没动”）。
# 所以只按相位把一对腿校到同一高度，相位之间的高差原样保留。
PHASE_PAIRS = [(0, 4), (1, 5), (2, 6), (3, 7)]


def main():
    out_dir = sys.argv[1] if len(sys.argv) > 1 else "Tools/logs/side-rebuild/frames"
    frames = []
    for name in ORDER:
        p = os.path.join(SRC, name)
        if not os.path.exists(p):
            raise SystemExit("缺帧：%s" % p)
        im = Image.open(p).convert("RGBA")
        x0, y0, x1, y1 = solid_bbox(im)
        frames.append((name, im.crop((x0, y0, x1 + 1, y1 + 1))))

    mean_h = sum(f.height for _, f in frames) / float(len(frames))
    base_scale = TARGET_MEAN_H / mean_h
    print("源帧均高 %.1f → 目标 %d → 基准缩放 %.4f" % (mean_h, TARGET_MEAN_H, base_scale))

    # 每帧的缩放系数：基准缩放 × 相位校准。校准时以同相位较高那帧为真值，
    # 只放大偏小的那一帧，绝不缩小本来就对的帧。
    scales = [base_scale] * len(frames)
    for a, b in PHASE_PAIRS:
        ha, hb = frames[a][1].height, frames[b][1].height
        taller = max(ha, hb)
        for idx in (a, b):
            scales[idx] = base_scale * (taller / float(frames[idx][1].height))
        if abs(ha - hb) / float(taller) > 0.05:
            print("  相位校准 (%d,%d)：%d vs %d，差 %.1f%% → 以 %d 为真值"
                  % (a, b, ha, hb, abs(ha - hb) * 100.0 / taller, taller))

    os.makedirs(out_dir, exist_ok=True)
    for i, (name, f) in enumerate(frames):
        scale = scales[i]
        w = max(1, int(round(f.width * scale)))
        h = max(1, int(round(f.height * scale)))
        r = f.resize((w, h), Image.LANCZOS)
        if w > CELL_W:
            raise SystemExit("缩放后宽度 %d 超格子 %d（%s）" % (w, CELL_W, name))
        canvas = Image.new("RGBA", (CELL_W, CELL_H), (0, 0, 0, 0))
        canvas.alpha_composite(r, ((CELL_W - w) // 2, GROUND_Y - h + 1))
        dst = os.path.join(out_dir, "%02d-%s" % (i, name.replace("walk-side", "")))
        canvas.save(dst)
        print("  %d  %-28s %dx%d → %dx%d  bbox 高 %d" % (i, name, f.width, f.height, w, h, h))
    print("输出目录：%s" % out_dir)
    return 0


if __name__ == "__main__":
    sys.exit(main())
