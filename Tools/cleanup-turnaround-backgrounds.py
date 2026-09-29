# 把角色三视图的浅色底抠成透明：
# 1) 逐列取顶部背景行参考色，从画面边缘做连通生长（与参考色接近的都算底）；
# 2) 第二轮局部连续生长，吞掉与底色相邻的淡影/接缝带；
# 3) 人物掩膜侵蚀 1px 去白边，3x3 盒滤波羽化，环带像素用内侧颜色回填。
# 4) 强制清空画布外沿 8px，避免贴地阴影残留在裁切边缘。
# 用法：python Tools/cleanup-turnaround-backgrounds.py [预览输出目录]
import sys
from pathlib import Path

import numpy as np
from PIL import Image

ART = Path(__file__).resolve().parents[1] / "GameClient" / "TwelveJade" / "Assets" / "Resources" / "Art"
PATTERNS = ("farmer-", "traveller-", "merchant-")
EDGE_TOLERANCE = 55      # 与参考底色的距离阈值：直接判定为底
SHADOW_TOLERANCE = 22    # 相邻底像素的色差阈值：吞掉淡影、接缝
SHADOW_LIMIT = 170       # 阴影带与参考底色的最远距离，防止钻进人物


def shift(img, dy, dx, fill_value):
    out = np.full_like(img, fill_value)
    h, w = img.shape[:2]
    src_y0, src_y1 = max(0, -dy), h - max(0, dy)
    src_x0, src_x1 = max(0, -dx), w - max(0, dx)
    dst_y0, dst_y1 = max(0, dy), h - max(0, -dy)
    dst_x0, dst_x1 = max(0, dx), w - max(0, -dx)
    out[dst_y0:dst_y1, dst_x0:dst_x1] = img[src_y0:src_y1, src_x0:src_x1]
    return out


def cleanup(path: Path):
    im = Image.open(path).convert("RGB")
    a = np.asarray(im).astype(np.float32)
    h, w, _ = a.shape

    # 逐列参考底色：三视图裁切接缝会让背景存在整列的亮暗差，
    # 每列用顶部背景行自己的中值做参考，接缝带才不会被漏掉。
    strip = a[:14]
    col_std = strip.std(axis=0).mean(-1)          # 该列顶部是否纯背景
    clean = strip[:, col_std < 14].reshape(-1, 3)
    ref_flat = np.median(clean, axis=0)           # 全局回退参考
    ref = np.median(strip, axis=0)                # 每列参考
    bad = col_std >= 14                           # 顶部被人物占据的列
    ref[bad] = ref_flat
    dist_ref = np.sqrt(((a - ref[None, :, :]) ** 2).sum(-1))
    near = dist_ref < EDGE_TOLERANCE

    # 第一轮：从边缘生长与底色相近的区域
    edge = np.zeros((h, w), bool)
    edge[0, :] = edge[-1, :] = edge[:, 0] = edge[:, -1] = True
    connected = edge & near
    while True:
        grow = shift(connected, 1, 0, False) | shift(connected, -1, 0, False) | \
            shift(connected, 0, 1, False) | shift(connected, 0, -1, False)
        nxt = connected | (grow & near)
        if (nxt == connected).all():
            break
        connected = nxt

    # 第二轮：局部连续生长吞掉淡影/接缝（颜色与相邻底像素接近且不远离参考底色）
    while True:
        grow = shift(connected, 1, 0, False) | shift(connected, -1, 0, False) | \
            shift(connected, 0, 1, False) | shift(connected, 0, -1, False)
        cand = grow & ~connected & (dist_ref < SHADOW_LIMIT)
        if not cand.any():
            break
        diff_ok = np.zeros((h, w), bool)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            neigh_img = shift(a, dy, dx, 0.0)
            neigh_is_bg = shift(connected, dy, dx, False)
            step = np.sqrt(((a - neigh_img) ** 2).sum(-1)) < SHADOW_TOLERANCE
            diff_ok |= neigh_is_bg & step
        nxt = connected | (cand & diff_ok)
        if (nxt == connected).all():
            break
        connected = nxt

    mask = ~connected                       # 人物
    inner = mask & shift(mask, 1, 0, False) & shift(mask, -1, 0, False) & \
        shift(mask, 0, 1, False) & shift(mask, 0, -1, False)   # 侵蚀 1px 去白边
    ring = mask & ~inner

    # 环带颜色回填：取 8 邻域内属于内部区域的均值，避免边缘残留米白
    acc = np.zeros_like(a)
    cnt = np.zeros((h, w), np.float32)
    for dy, dx in ((dy, dx) for dy in (-1, 0, 1) for dx in (-1, 0, 1) if (dy, dx) != (0, 0)):
        m = shift(inner, dy, dx, False).astype(np.float32)
        acc += shift(a, dy, dx, 0.0) * m[..., None]
        cnt += m
    fill = acc / np.maximum(cnt, 1)[..., None]
    rgb = np.where(ring[..., None] & (cnt[..., None] > 0), fill, a)

    soft = inner.astype(np.float32)
    blur = sum(shift(soft, dy, dx, 0.0) for dy in (-1, 0, 1) for dx in (-1, 0, 1)) / 9.0
    alpha = np.clip(blur * 1.6, 0, 1)       # 补回模糊损失的覆盖率，内部保持全不透明
    alpha[:8, :] = alpha[-8:, :] = 0
    alpha[:, :8] = alpha[:, -8:] = 0
    out = np.dstack([np.clip(rgb, 0, 255).astype(np.uint8), (alpha * 255).astype(np.uint8)])
    Image.fromarray(out, "RGBA").save(path)
    return int(connected.sum()), h * w


def preview(path: Path, out_dir: Path):
    im = Image.open(path).convert("RGBA")
    for name, bg in (("light", (199, 194, 166)), ("dark", (16, 38, 34))):
        canvas = Image.new("RGBA", im.size, bg + (255,))
        canvas.alpha_composite(im)
        canvas.convert("RGB").save(out_dir / f"{path.stem}-{name}.png")


if __name__ == "__main__":
    files = sorted(p for p in ART.glob("*.png") if p.name.startswith(PATTERNS))
    print(f"{len(files)} turnaround sheets")
    for p in files:
        cleared, total = cleanup(p)
        print(f"  {p.name}: cleared {cleared / total:.1%}")
    if len(sys.argv) > 1:
        out_dir = Path(sys.argv[1])
        out_dir.mkdir(parents=True, exist_ok=True)
        for name in ("farmer-male-0-front", "traveller-female-1-side", "merchant-male-1-back"):
            preview(ART / f"{name}.png", out_dir)
        print("previews written to", out_dir)
