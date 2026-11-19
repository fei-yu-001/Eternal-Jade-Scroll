# -*- coding: utf-8 -*-
"""把「一次调用出的整张走帧网格图」切成 8 格，归一化后拼成运行期 sheet（纯本地，0 积分）。

━━ 为什么要有这一步 ━━
2026-10-31 用户下的判断：逐帧独立生成 = 8 次重画 = 人物必漂。改成一次调用出 4x2 网格后，
**身份是稳了，但同一次渲染里每格的人物位置、大小、明度仍然会漂**（模型没法数像素）。
不归一化直接切格拼 sheet，播放起来就是一耸一耸、忽明忽暗。

━━ 四件归一化（顺序固定，少一件就白做）━━
  1. 去底：**按纸色聚类**，纸色 = RGB(248,240,222) ±12（与 Tools/check-walksheet-cutout.py
     的 C2 同一口径，两条线不许各修各的）。
     为什么不是四边泛洪：生产 sheet 的四角是 (0,0,0,0) 全透明，采样只能采到黑色；
     而画面里的米色纸卡是**不透明**的整块矩形，会跟着人物一起滑进游戏
     （实测 traveller 侧向纸色占 27.5%、merchant 侧向 22.8%）。
     为什么不是"接近白色"：纸色 b=222 < 232，纯白判据根本看不见它。
     所有纸色连通域**面积 >800 一律清掉，不管贴不贴边**——纸卡一定很大，不会误伤衣料；
  2. 身高统一 + 脚底线对齐：**一个全局缩放系数**作用于全部 8 格（各帧各自缩到同一高度
     等于把走路该有的上下起伏抹平，那是自毁动画，见 assemble-walk-sheet-v2.py 的注释）；
     缩放后每格按自身脚底平移到同一地面线；
  3. 水平居中按**躯干**，不按 bbox：摆臂会让 bbox 左右各宽一截，按 bbox 居中 = 人左右横跳；
  4. 调色板对齐锚点：前景像素逐通道分位数映射到锚点直方图，治「某一格偏暗」。
     只改前景，不动 alpha；不做模糊（模糊等于换一批像素，身份闸过不去）。

输出：<out-dir>/frames/f00.png ... f07.png（816x1088 RGBA）
      <sheet>（8 格横向等距，6528x1088 RGBA）+ <report json>

用法：
    python Tools/normalize-grid-sheet.py --grid Tools/logs/walk-sheet-xindu/sheet-front-v2.png \
        --anchor Tools/logs/walk-xindu/anchor/merchant-male-1-front-anchor.png \
        --out-dir Tools/logs/walk-sheet-xindu/norm-v2 --sheet Tools/logs/walk-sheet-xindu/merchant-front-norm-v2.png
"""
import argparse
import json
import os
import sys

import numpy as np
from PIL import Image
from scipy import ndimage

for _s in (sys.stdout, sys.stderr):
    if hasattr(_s, "reconfigure"):
        _s.reconfigure(encoding="utf-8", errors="replace")

PAPER_RGB = (248, 240, 222)   # 纸色。与 check-walksheet-cutout.py 的 C2 定义一致，别改单边
PAPER_TOL = 12                # 每通道差 < 12
PAPER_WEAK = 32               # 亮晕判据：纸色有渐变，人物周围那圈能差到 +22
PAPER_MIN_AREA = 800          # 纸色连通域超过这个面积就是纸卡；小块的可能是白衣白鞋
ALPHA_FLOOR = 40         # 缩放后 alpha < 40 归零，掐掉淡淡的白边
CELL_W, CELL_H = 816, 1088
BOTTOM_MARGIN = 4        # 脚底离格底 4px：与已装机 8 张（farmer 走帧实测 4px）齐平
HEIGHT_RATIO = 0.96      # 身高占格高：已装机 1014~1054 / 1088 ≈ 0.93~0.97，取中位口径
TORSO_BAND = (0.25, 0.55)  # 躯干带（相对剪影高度），用来算水平居中


def cut_paper(rgb, paper=PAPER_RGB):
    """清纸底，返回 bool 前景掩码。两条规则合起来用：

    1. **纸卡**：与纸色每通道差 <12 的连通域，面积 >800 的一律清掉，不看贴不贴边。
       生产 sheet 的四角是 (0,0,0,0)，纸卡是画上去的不透明矩形，只能这样抓。
    2. **亮晕**：纸色不是均匀的，人物周围常有一圈更亮的过渡（实测到 +22），
       单色判据漏它，轮廓就会鼓出来。规则是从画布边缘出发、与纸色差 <32 的连通泛洪。
       限定"从边缘连通"是因为衣料再浅也被轮廓围在里面，泛不到。
    """
    diff = np.abs(rgb.astype(np.int16) - np.array(paper, dtype=np.int16)).max(axis=2)
    labels, count = ndimage.label(diff < PAPER_TOL)             # 4 邻接
    card = np.zeros(rgb.shape[:2], dtype=bool)
    if count:
        areas = ndimage.sum(diff < PAPER_TOL, labels, range(1, count + 1))
        keep = np.concatenate([[False], areas > PAPER_MIN_AREA])
        card = keep[labels]

    # 注意这里**不能**再 & ~card：纸卡本身也是可通行区，把它排除掉，
    # 泛洪就被卡在纸上过不去，紧贴轮廓那一圈亮晕（差 13，刚好超出 ±12）永远填不到。
    passable = diff < PAPER_WEAK
    seen = np.zeros(rgb.shape[:2], dtype=bool)
    stack = [(0, x) for x in range(rgb.shape[1])] + [(rgb.shape[0] - 1, x) for x in range(rgb.shape[1])]
    stack += [(y, 0) for y in range(rgb.shape[0])] + [(y, rgb.shape[1] - 1) for y in range(rgb.shape[0])]
    while stack:
        y, x = stack.pop()
        if y < 0 or y >= rgb.shape[0] or x < 0 or x >= rgb.shape[1] or seen[y, x] or not passable[y, x]:
            continue
        seen[y, x] = True
        stack.append((y + 1, x)); stack.append((y - 1, x))
        stack.append((y, x + 1)); stack.append((y, x - 1))
    return ~(card | seen)


def feather_alpha(fg, inset=1, sigma=1.0):
    """先内缩 1px 再羽化 1px：硬切的边在游戏里是锯齿，直接软边的边是白晕，两头不讨好。"""
    core = ndimage.binary_erosion(fg, structure=np.ones((3, 3), dtype=bool), iterations=inset)         if inset else fg
    if not core.any():
        core = fg
    return np.clip(ndimage.gaussian_filter(core.astype(np.float32), sigma) * 255.0, 0, 255)


def paper_blocks(rgba, paper=PAPER_RGB):
    """残留的"纸卡级"纸色块：不透明、与纸色差 <12、面积 > PAPER_MIN_AREA 的连通域个数。

    这才是"会不会跟着人一起滑进游戏"的判据——一片 90000 像素的纸卡会，一块 90 像素的
    裤腿高光不会。零散纸色像素另有 residual_paper 报数，两个数一起看。
    """
    a = np.asarray(rgba.convert("RGBA")).astype(np.int16)
    diff = np.abs(a[:, :, :3] - np.array(paper, dtype=np.int16)).max(axis=2)
    mask = (a[:, :, 3] > 200) & (diff < PAPER_TOL)
    labels, count = ndimage.label(mask)
    if not count:
        return 0
    areas = ndimage.sum(mask, labels, range(1, count + 1))
    return int((areas > PAPER_MIN_AREA).sum())


def residual_paper(rgba, paper=PAPER_RGB):
    """残留纸色：不透明且每通道与纸色差 <12。清干净就是 0，不用"纯白"糊弄自己。"""
    a = np.asarray(rgba.convert("RGBA")).astype(np.int16)
    diff = np.abs(a[:, :, :3] - np.array(paper, dtype=np.int16)).max(axis=2)
    return int(((a[:, :, 3] > 200) & (diff < PAPER_TOL)).sum())


def decontaminate(rgb, fg, band=3):
    """边缘去污：剪影最外 band 圈被纸色混过，颜色直接取**最近的干净内部像素**。

    只换颜色，不动 alpha。band 不能只取 1：羽化会把 alpha 铺开 2~3px，
    第 2、3 圈要是还留着纸色，垫到绿底上就是一圈发光的白边。
    用最近邻填充而不是 3x3 均值：手腕、系带这种细结构用均值会被糊平。
    """
    inner = ndimage.binary_erosion(fg, structure=np.ones((3, 3), dtype=bool), iterations=band)
    if not inner.any():
        return rgb
    dist, (iy, ix) = ndimage.distance_transform_edt(~inner, return_indices=True)
    ring = fg & (dist <= band + 1)   # 腐蚀后边缘像素的 dist 会等于 band+1，差这一步最外圈改不到
    out = rgb.astype(np.float64).copy()
    out[ring] = rgb[iy[ring], ix[ring]].astype(np.float64)
    return out


def silhouette(mask):
    ys, xs = np.where(mask)
    return ys.min(), ys.max(), xs.min(), xs.max()


def torso_cx(mask, top, bottom):
    """躯干带的逐行中点取中位数。摆臂只影响个别行，中位数对它是稳的。"""
    h = bottom - top + 1
    y0 = top + int(h * TORSO_BAND[0])
    y1 = top + int(h * TORSO_BAND[1])
    mids = []
    for y in range(y0, y1 + 1):
        xs = np.where(mask[y])[0]
        if len(xs):
            mids.append((xs[0] + xs[-1]) / 2.0)
    return float(np.median(mids)) if mids else (np.where(mask)[1].min() + np.where(mask)[1].max()) / 2.0


def match_palette(rgb, mask, ref_pixels):
    """前景逐通道分位数映射到锚点。ref_pixels: (N,3) 锚点前景像素。"""
    out = rgb.astype(np.float64).copy()
    src = rgb[mask].astype(np.float64)
    for ch in range(3):
        sx = np.sort(src[:, ch])
        sy = np.sort(ref_pixels[:, ch].astype(np.float64))
        if len(sx) < 2:
            continue
        # 两边像素数不同：先把锚点排序值重采样到源的分位点上，再做分位数映射
        q = np.linspace(0.0, 1.0, len(sx))
        dst_q = np.interp(q, np.linspace(0.0, 1.0, len(sy)), sy)
        out[:, :, ch] = np.where(mask, np.interp(out[:, :, ch], sx, dst_q), out[:, :, ch])
    return np.clip(out, 0, 255)


def pure_white(im):
    """残留纯白：alpha>200 且 RGB 全 >232。机关在这条上——底色没抠干净就是几千个。"""
    a = np.array(im.convert("RGBA"))
    return int(((a[:, :, 3] > 200) & (a[:, :, :3].min(axis=2) > 232)).sum())


def slice_grid(img, cols, rows):
    w, h = img.size
    cw, ch = w // cols, h // rows
    return [img.crop(((i % cols) * cw, (i // cols) * ch,
                      (i % cols + 1) * cw, (i // cols + 1) * ch))
            for i in range(cols * rows)]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--grid", required=True)
    ap.add_argument("--anchor", required=True)
    ap.add_argument("--out-dir", required=True)
    ap.add_argument("--sheet", required=True)
    ap.add_argument("--cols", type=int, default=4)
    ap.add_argument("--rows", type=int, default=2)
    ap.add_argument("--prefix", default="f")
    ap.add_argument("--height-ratio", type=float, default=HEIGHT_RATIO)
    ap.add_argument("--no-palette", action="store_true", help="跳过调色板对齐（只用于对照实验）")
    args = ap.parse_args()

    grid = Image.open(args.grid).convert("RGB")
    cells = slice_grid(grid, args.cols, args.rows)

    anchor_rgb = np.array(Image.open(args.anchor).convert("RGB")).astype(int)
    anchor_mask = cut_paper(anchor_rgb)
    ref_pixels = anchor_rgb[anchor_mask]
    print("锚点 %s 前景 %d 像素" % (os.path.basename(args.anchor), len(ref_pixels)))

    # 第一遍：只量剪影，算一个全局缩放系数（所有帧共用）
    measured = []
    for i, cell in enumerate(cells):
        rgb = np.array(cell).astype(int)
        mask = cut_paper(rgb)
        top, bottom, left, right = silhouette(mask)
        measured.append(dict(i=i, rgb=rgb, mask=mask, top=int(top), bottom=int(bottom),
                             height=int(bottom - top + 1), torso_cx=torso_cx(mask, top, bottom)))
        print("  %s%02d 剪影 高=%d 宽=%d 躯干中线x=%.1f" %
              (args.prefix, i, measured[-1]["height"], right - left + 1, measured[-1]["torso_cx"]))

    target_h = int(round(CELL_H * args.height_ratio))
    median_h = float(np.median([m["height"] for m in measured]))
    scale = target_h / median_h
    print("身高：目标 %dpx（格高 %d x %.2f）／中位剪影 %.0fpx ⇒ 全局缩放 %.4f"
          % (target_h, CELL_H, args.height_ratio, median_h, scale))

    ground_y = CELL_H - 1 - BOTTOM_MARGIN
    os.makedirs(os.path.join(args.out_dir, "frames"), exist_ok=True)
    report, framed = [], []
    for m in measured:
        rgb = m["rgb"]
        mask = m["mask"]
        rgb = decontaminate(rgb, mask)
        if not args.no_palette:
            rgb = match_palette(rgb, mask, ref_pixels)
        alpha = feather_alpha(mask)
        rgba = np.dstack([rgb, alpha]).astype(np.uint8)
        im = Image.fromarray(rgba, "RGBA")
        new_size = (max(1, int(round(im.width * scale))), max(1, int(round(im.height * scale))))
        im = im.resize(new_size, Image.LANCZOS)
        arr = np.array(im)
        arr[:, :, 3] = np.where(arr[:, :, 3] < ALPHA_FLOOR, 0, arr[:, :, 3])   # 收敛白边
        im = Image.fromarray(arr, "RGBA")
        a = arr[:, :, 3] > 8
        if not a.any():
            raise SystemExit("f%02d 缩放后没有前景，出手的图有问题" % m["i"])
        ys = np.where(a.any(axis=1))[0]
        feet_y, head_y = ys[-1], ys[0]
        head_band = a[head_y:head_y + max(1, int((feet_y - head_y) * 0.18))]
        hx = np.where(head_band.any(axis=0))[0]
        im_cx = (hx[0] + hx[-1]) / 2.0

        cell = Image.new("RGBA", (CELL_W, CELL_H), (0, 0, 0, 0))
        dx = int(round(CELL_W / 2.0 - im_cx))
        dy = int(round(ground_y - feet_y))
        cell.paste(im, (dx, dy), im)
        white = residual_paper(cell)
        blocks = paper_blocks(cell)
        path = os.path.join(args.out_dir, "frames", "%s%02d.png" % (args.prefix, m["i"]))
        cell.save(path)
        framed.append(cell)
        report.append(dict(frame=m["i"], file=os.path.basename(path),
                           scale=round(scale, 4), height_px=int(feet_y - head_y + 1),
                           ground_y=ground_y, dx=dx, dy=dy,
                           torso_cx=round(m["torso_cx"], 1), im_cx=round(im_cx, 1),
                           residual_paper=int(white), paper_blocks=int(blocks),
                           paper_removed=int((~mask).sum())))

    sheet = Image.new("RGBA", (CELL_W * len(framed), CELL_H), (0, 0, 0, 0))
    for i, cell in enumerate(framed):
        sheet.paste(cell, (i * CELL_W, 0))
    os.makedirs(os.path.dirname(os.path.abspath(args.sheet)), exist_ok=True)
    sheet.save(args.sheet)

    heights = [r["height_px"] for r in report]
    whites = [r["residual_paper"] for r in report]
    print("清除纸底面积：%s px（合计 %d）"
          % ("/".join(str(r["paper_removed"]) for r in report), sum(r["paper_removed"] for r in report)))
    print("残留纸卡块（>800px 纸色连通域）：%s（合计 %d，必须 0）"
          % ("/".join(str(r["paper_blocks"]) for r in report), sum(r["paper_blocks"] for r in report)))
    print("残留纸色像素（±12 且不透明）：%s（合计 %d；衣料浅色也落在纸色范围内，看连通域大小）"
          % ("/".join(str(v) for v in whites), sum(whites)))
    print("落格：身高 %d..%d px ／脚底线 y=%d ／横向平移 %d..%d px"
          % (min(heights), max(heights), ground_y,
             min(r["dx"] for r in report), max(r["dx"] for r in report)))
    print("sheet %s  %dx%d" % (args.sheet, sheet.width, sheet.height))
    rep_path = os.path.join(args.out_dir, "normalize-report.json")
    with open(rep_path, "w", encoding="utf-8") as fh:
        json.dump(dict(grid=args.grid, anchor=args.anchor, scale=round(scale, 4),
                       target_h=target_h, ground_y=ground_y, frames=report),
                  fh, ensure_ascii=False, indent=2)
    print("report %s" % rep_path)
    return 0


if __name__ == "__main__":
    sys.exit(main())