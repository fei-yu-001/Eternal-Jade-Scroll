# -*- coding: utf-8 -*-
"""行走序列帧的「运动」维度闸门（V 线补闸，2026-10-06 架构师看图后补）。

━━ 为什么必须补这一闸 ━━
`extract-walk-sheet.py` 的结论行是：

    verdict = "PASS" if (ok and manifest["groundSpreadPx"] <= 6) else "FAIL"

而 `groundSpreadPx = max(box[3]) - min(box[3])`，`box[3]` 是**整帧剪影的最低行**。
走周期里永远有一只支撑脚踩在地上 ⇒ 整帧最低行天然恒定 ⇒ 这条判据**结构上不可能**
发现"根本没有走周期"。它能抓的只有"某帧被画飘了 / 帧高不一致"。

实测事故（2026-10-06，farmer-male-0-walksheet-v1）：
8 帧是**同一个迈步姿势的 8 次重画**（只是逐帧重画的抖动，没有走周期），
旧判据却给 `RESULT: PASS（帧数 8/8 ／ 脚底线散布 0≤6 ／ 高度差 1）`。
这是典型假绿：判据维度缺一维，不是实现 bug。

━━ 本闸的三条判据（都直接对应"在走路"的物理事实，与朝向无关）━━
  B 整体垂直起伏 `topSpread`：8 帧剪影最高行的极差。
    走路必然上下起伏；复制帧没有起伏。
  C 触地分布变化：取两者之大 ——
      ① 触地质心横向摆动 / 帧宽（支撑脚从后脚换到前脚）
      ② 触地横向跨度的最大/最小比（双脚着地 vs 单脚支撑）
    走路时支撑脚要交替；复制帧的触地分布每帧一样。

  D 帧内实体完整性 `heightDev`：每帧剪影高度（bbox 高）相对中位数的极差比例。
    （2026-10-15 补。触发事故事见 T125：B 与 C 都是"变化量"判据，而坏图的变化量
     比好图还大 —— `farmer-male-0-walksheet-side-v1` 帧高在 709~942 之间飘 233px、
     f1 触地横向跨度只剩 1px，闸门却给 `PASS（起伏 235 >= 3 ／ 触地分布变化 10200% >= 10%）`。
     它把"坏得离谱"读成了"动得厉害"。补这一维之后同样的 sheet 变 FAIL。）

  A 姿态幅度 `poseSpread`（下半身剪影软 IoU 差异）**只打印、不进判据**：
    它对逐帧重画的边界抖动敏感（真 sheet 复制姿势也能拿到 0.246），
    而本仓**没有真实手绘走周期样本**可用于定阈值。宁可留一条不改判的参考数，
    也不许凭空定一个阈值（沿用 check-character-identity.py 的口径：要放宽/收紧必须先报告）。

━━ 校准（`--selftest` 现场合成 + 实测 sheet，数都留痕）━━
  材料                                  起伏    触地变化    姿态幅度(参考)
  合成真走周期（4 关键姿势 × 2 半周期）   6 px    59.0%      0.917
  合成"同一姿势复制 8 份"                 0 px     0.0%      0.000
  合成"复制 + ±1px 抖动"                  1 px     0.0%      0.000
  实测 farmer-male-0-walksheet-v1         1 px     5.5%      0.246
  ⇒ 阈值 TOP_MIN=3px（真周期 6 的 50%，实测 1 的 3 倍）、CONTACT_MIN=10%
    （真周期 59% 的 17%，实测 5.5% 的 1.8 倍）。两处间隔都 ≥1.8 倍。
  ⇒ `--selftest` 会现场断言"真周期必须 PASS、复制与复制+抖动必须 FAIL"。

━━ D 的校准（2026-10-15 实测，全部是跑出来的数，不是估的）━━
  材料                                        帧高偏离 heightDev
  合成·真走周期（cycle）                       0.026
  合成·复制 / 复制+抖动（clone / jitter）      0.000
  已装机素材里最"不整"的一张合格件（farmer-back） 0.063
  坏图（farmer-male-0-side，帧高 709..942）    0.254
  ⇒ 阈值 HEIGHT_DEV_MAX = 0.14：比最差的合格件高 2.22 倍，比坏图低 1.81 倍，两侧都 ≥1.8 倍。
  ⇒ 为什么不用别的量（都实测过，都被否决，写在这里免得后人再走一遍）：
     · 剪影**面积**偏离：真走周期自己就有 0.182，合格件最高 0.436（merchant-back），
       坏图 0.362 —— 区分不出来，用它只会误杀。
     · 每帧**触地像素数**下限：合格件 farmer-front 的 min 就是 0（有一帧不着地），
       坏图 farmer-side 的 min 是 2 —— 同样是反的。
     · **连通块数**（多于期望时的 WARN）：RIG-01 新产出的 farmer back/front 恰是 8 块，
       而 merchant/traveller 那 6 张是 9~18 块。看着像信号，但本仓没有"块数多就一定坏"的证据，
       故只打印不进判据，等攒够样本再定。
  ⇒ `--selftest` 另加一例"单帧下半截断"：它必须**只栽在 D 上**（B、C 都还是过），
     这才是 D 有牙齿的证明 —— 只加一维却不证明它抓得住 B/C 抓不住的东西，等于没加。

━━ 副产品 ━━
`--evidence <目录>` 会写出 8 帧剪影叠加图（全身 + 下半身），这是**人眼一眼可判**的证据：
真走周期会看到 8 条明显分开的腿影；复制帧只会看到一条轮廓线。

用法：
  python Tools/check-walk-sheet-motion.py <sheet.png> [帧数] [--evidence <目录>]
  python Tools/check-walk-sheet-motion.py --selftest
"""
import importlib.util
import os
import sys

from PIL import Image, ImageChops, ImageDraw

for _s in (sys.stdout, sys.stderr):
    if hasattr(_s, "reconfigure"):
        _s.reconfigure(encoding="utf-8", errors="replace")

POSE_WARN = 0.45         # A 仅作参考（缺真实手绘周期样本，不许它决定成败）
TOP_MIN = 3              # B 整体垂直起伏下限（px）
CONTACT_MIN = 0.10       # C 触地分布变化下限（质心摆动占帧宽 / 跨度比，取大者）
HEIGHT_DEV_MAX = 0.14    # D 帧内实体完整性：帧高偏离中位数上限（比例，见文件头校准表）
GRID_W, GRID_H = 24, 20
LEG_FRACTION = 0.45      # 「下半身」= 归一化画布的下 45%
CANVAS_W, CANVAS_H = 256, 384
PAD_BOTTOM = 8
CONTACT_BAND = 2         # 触地判定：最低行往上 2 行
MIN_CONTACT_PX = 12      # 一个触地簇至少这么多像素
CELL_W, CELL_H = 150, 720
PAPER = (217, 185, 128)  # 与 extract-walk-sheet.py 的键控底色一致
# 透明底交付件（RGBA）的前景就是 alpha>0。为了让闸里的「按背景色键控」逻辑等价于
# 「alpha 即前景」，这里把透明区合成到**纯品红键控色**再交给判定：
#   - 用宣纸色合成会出祸：merchant 的真实纸色是 (253,242,217)，与人物米白衣料只差十几，
#     TOL=70 会把衣料当背景吃掉，造成假 FAIL；
#   - 用品红 (255,0,255)：画里不会出现该色，键控结果与 alpha 完全一致。
KEY_MAGENTA = (255, 0, 255)


def flatten_sheet_paper(im):
    """透明底交付件（RGBA）先合成回宣纸色再判：
    闸的前景基准取自 px[1,1]+TOL，直接 convert("RGB") 会把透明区填成黑，
    深色纸偶会连背景一起被当成前景（历史上因此把真周期判成"没动"）。
    判据与阈值一律未动，只是把"交付形态"从判定输入里剔出去。"""
    if im.mode in ("RGBA", "LA") or (im.mode == "P" and "transparency" in im.info):
        rgba = im.convert("RGBA")
        bg = Image.new("RGB", rgba.size, KEY_MAGENTA)
        bg.paste(rgba, (0, 0), rgba)
        return bg
    return im.convert("RGB")

EXTRACTOR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "extract-walk-sheet.py")


def load_extractor():
    spec = importlib.util.spec_from_file_location("extract_walk_sheet", EXTRACTOR)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


# ────────────────────────── 提取（复用现有脚本，不另发明） ──────────────────────────
def extract_boxes(ex, sheet, expected):
    seen = ex.background_mask(sheet)
    found = ex.blobs(sheet, seen)
    found.sort(key=lambda b: b["box"][0])
    if len(found) > expected:
        print("WARN 连通块 %d 个多于期望 %d，按像素数取前 %d" % (len(found), expected, expected))
        found = sorted(found, key=lambda b: -b["px"])[:expected]
        found.sort(key=lambda b: b["box"][0])
    if len(found) < expected:
        found = ex.split_until(sheet, seen, found, expected)
    return seen, found


def normalize_mask(mask, w, h):
    """把单帧剪影缩放到统一画布（同高、底对齐、水平居中），供跨帧比 IoU。"""
    im = Image.new("L", (w, h), 0)
    px = im.load()
    for y in range(h):
        row = mask[y]
        for x in range(w):
            if row[x]:
                px[x, y] = 255
    box = im.getbbox()
    if not box:
        return None
    im = im.crop(box)
    target_h = CANVAS_H - PAD_BOTTOM * 2
    scale = target_h / float(im.height)
    new_size = (max(1, int(round(im.width * scale))), target_h)
    im = im.resize(new_size, Image.NEAREST)
    canvas = Image.new("L", (CANVAS_W, CANVAS_H), 0)
    left = (CANVAS_W - new_size[0]) // 2
    canvas.paste(im, (left, CANVAS_H - PAD_BOTTOM - new_size[1]))
    return canvas


def pose_diff(a, b, y0, y1):
    """姿势差异 = 1 - 软 IoU（下半身降采样后的覆盖率）。

    为什么要降采样：AI 是**逐帧重画**的，同一姿势也会差 1~2px 边界；薄轮廓（小腿、鞋带）
    对这点抖动极敏感，直接按像素算 IoU 会把抖动当成运动（实测：真 sheet 未降采样时
    复制姿势也能拿到 0.257）。降到 24x20 网格后，1~2px 抖动被平均掉，真姿势差异保留。
    """
    box = (0, y0, CANVAS_W, y1)
    A = a.crop(box).resize((GRID_W, GRID_H), Image.BOX)
    B = b.crop(box).resize((GRID_W, GRID_H), Image.BOX)
    pa, pb = A.load(), B.load()
    num = den = 0
    for y in range(GRID_H):
        for x in range(GRID_W):
            va, vb = pa[x, y], pb[x, y]
            num += min(va, vb)
            den += max(va, vb)
    return 1.0 - (num / float(den)) if den else 0.0


def contact_stats(fg, x0, x1, y0, y1, ground):
    """本帧触地像素的（相对本帧中心的质心, 个数, 横向跨度）。

    横向跨度是关键：真走周期里双脚同时着地时跨度大（两脚分开），
    单脚支撑（摆动腿抬起）时跨度只剩一只脚。复制帧则每帧都一样。
    """
    lo = max(y0, ground - CONTACT_BAND)
    hi = min(y1, ground)
    xs = []
    for y in range(lo, hi + 1):
        row = fg[y]
        for x in range(x0, x1 + 1):
            if row[x]:
                xs.append(x)
    if not xs:
        return None, 0, 0
    return sum(xs) / float(len(xs)) - (x0 + x1) / 2.0, len(xs), max(xs) - min(xs)


def top_row(fg, w, y0, y1):
    for y in range(y0, y1 + 1):
        if any(fg[y]):
            return y
    return None


def mirror(im):
    return im.transpose(Image.FLIP_LEFT_RIGHT)


# ────────────────────────── 证据图 ──────────────────────────
COLORS = [(230, 25, 75), (60, 180, 75), (0, 130, 200), (245, 130, 48),
          (145, 30, 180), (0, 190, 190), (128, 128, 0), (0, 0, 128)]


def write_evidence(out_dir, masks, name_full="motion-overlay-full.png",
                   name_legs="motion-overlay-legs.png"):
    os.makedirs(out_dir, exist_ok=True)
    for y0, scale, name in ((0, 3, name_full),
                            (int(CANVAS_H * (1 - LEG_FRACTION)), 4, name_legs)):
        canvas = Image.new("RGB", (CANVAS_W, CANVAS_H), (255, 255, 255))
        pxc = canvas.load()
        for idx, m in enumerate(masks):
            if m is None:
                continue
            c = COLORS[idx % len(COLORS)]
            pm = m.load()
            for y in range(CANVAS_H):
                for x in range(CANVAS_W):
                    if pm[x, y] > 0:
                        r, g, b = pxc[x, y]
                        pxc[x, y] = ((r + c[0]) // 2 if r < 250 else c[0],
                                     (g + c[1]) // 2 if g < 250 else c[1],
                                     (b + c[2]) // 2 if b < 250 else c[2])
        crop = canvas.crop((0, y0, CANVAS_W, CANVAS_H))
        crop = crop.resize((crop.width * scale, crop.height * scale), Image.NEAREST)
        d = ImageDraw.Draw(crop)
        for i in range(len(masks)):
            d.rectangle([4 + i * 62, 4, 4 + i * 62 + 56, 22], fill=COLORS[i % len(COLORS)])
            d.text((8 + i * 62, 8), "f%d" % i, fill=(255, 255, 255))
        path = os.path.join(out_dir, name)
        crop.save(path)
        print("  证据图 %s  %dx%d" % (path, crop.width, crop.height))


# ────────────────────────── 主判定 ──────────────────────────
def judge(sheet, expected, evidence_dir=None, title=""):
    ex = load_extractor()
    seen, found = extract_boxes(ex, sheet, expected)
    w, h = sheet.size
    if len(found) != expected:
        print("RESULT: FAIL  （只检出 %d/%d 帧，后面的运动判据不成立）" % (len(found), expected))
        return False

    fg = [[not seen[y][x] for x in range(w)] for y in range(h)]
    ground = max(b["box"][3] for b in found)

    masks = []
    contact_x = []
    contact_w = []
    contact_n = []
    tops = []
    for b in found:
        x0, y0, x1, y1 = b["box"]
        sw, sh = x1 - x0 + 1, y1 - y0 + 1
        local = [[fg[y0 + y][x0 + x] for x in range(sw)] for y in range(sh)]
        masks.append(normalize_mask(local, sw, sh))
        cx_, n_, wd_ = contact_stats(fg, x0, x1, y0, y1, ground)
        contact_x.append(cx_)
        contact_w.append(wd_)
        contact_n.append(n_)
        tops.append(y0)

    y_leg0 = int(CANVAS_H * (1 - LEG_FRACTION))
    # 比姿势前先按「触地质心」水平对齐：游戏里精灵恒按角色位置居中，
    # 帧在格子里偏了 1~2px 是作画抖动，不是姿势差异；不对齐会把抖动算成运动。
    aligned = []
    for m, c in zip(masks, contact_x):
        if m is None:
            aligned.append(None)
        elif c is None:
            aligned.append(m)
        else:
            aligned.append(ImageChops.offset(m, int(round(-c)), 0))
    masks = aligned

    pair_max = 0.0
    pair_arg = None
    for i in range(expected):
        for j in range(i + 1, expected):
            if masks[i] is None or masks[j] is None:
                continue
            d = pose_diff(masks[i], masks[j], y_leg0, CANVAS_H)
            if d > pair_max:
                pair_max, pair_arg = d, (i, j)
    adj_max = 0.0
    for i in range(expected - 1):
        if masks[i] is None or masks[i + 1] is None:
            continue
        adj_max = max(adj_max, pose_diff(masks[i], masks[i + 1], y_leg0, CANVAS_H))

    frame_w = max(b["box"][2] - b["box"][0] + 1 for b in found)
    valid_cx = [c for c in contact_x if c is not None]
    valid_w = [x for x in contact_w if x]
    contact_spread = (max(valid_cx) - min(valid_cx)) if valid_cx else 0.0
    width_ratio = (max(valid_w) / float(min(valid_w)) - 1.0) if valid_w and min(valid_w) > 0 else 0.0
    contact_change = max(contact_spread / float(frame_w), width_ratio)
    top_spread = (max(tops) - min(tops)) if tops else 0

    # D 帧内实体完整性（2026-10-15 补，T125）：每帧剪影高度（bbox 高）相对中位数的极差比例。
    heights = [b["box"][3] - b["box"][1] + 1 for b in found]
    areas = [b["px"] for b in found]
    med_h = sorted(heights)[len(heights) // 2]
    med_a = sorted(areas)[len(areas) // 2]
    height_dev = (max(heights) - min(heights)) / float(med_h) if med_h else 0.0
    # 下面三个只打印不进判据：实测否决理由见文件头 D 的校准表。
    area_dev = (max(areas) - min(areas)) / float(med_a) if med_a else 0.0
    contact_min = min(contact_n) if contact_n else 0

    print("%s帧数 %d/%d ／ 帧高 %d..%d ／ 地面行 %d ／ 帧宽 %d" % (
        title, len(found), expected, min(b["box"][3] - b["box"][1] + 1 for b in found),
        max(b["box"][3] - b["box"][1] + 1 for b in found), ground, frame_w))
    print("  触地质心 x（相对本帧中心）: %s" % ", ".join(
        "f%d=%s" % (i, "%.0f" % c if c is not None else "-") for i, c in enumerate(contact_x)))
    print("  触地横向跨度 px: %s" % ", ".join("f%d=%d" % (i, x) for i, x in enumerate(contact_w)))
    print("  A 姿态幅度   poseSpread    = %.3f  (最大差异帧对 f%d-f%d)  ← 仅参考，不进判据" % (
        pair_max, pair_arg[0] if pair_arg else -1, pair_arg[1] if pair_arg else -1))
    print("     相邻帧最大差异          = %.3f" % adj_max)
    print("  B 整体垂直起伏 topSpread     = %d px  (限 %d)" % (top_spread, TOP_MIN))
    print("  C 触地分布变化               = %.1f%%  (质心摆动 %.1f%% ／ 跨度比 %.1f%%，限 %.1f%%)" % (
        100 * contact_change, 100 * contact_spread / frame_w, 100 * width_ratio, 100 * CONTACT_MIN))
    print("  D 帧内实体完整性 heightDev    = %.3f  (帧高 %d..%d，限 %.2f)" % (
        height_dev, min(heights), max(heights), HEIGHT_DEV_MAX))
    print("     参考（不进判据）: 面积偏离 %.3f ／ 最少触地像素 %d ／ 检出连通块 %d" % (
        area_dev, contact_min, len(found)))

    half = expected // 2
    if half and all(masks[i] is not None and masks[i + half] is not None for i in range(half)):
        dm = sum(pose_diff(masks[i], masks[i + half], y_leg0, CANVAS_H) for i in range(half)) / half
        mm = sum(pose_diff(masks[i], mirror(masks[i + half]), y_leg0, CANVAS_H) for i in range(half)) / half
        print("  参考 C 半周期「直接 vs 镜像」差异 = %.3f / %.3f %s" % (
            dm, mm,
            "（后半周期是前半周期的镜像 ⇒ 像正/背视交替）" if mm < dm - 0.02 else
            "（两者都接近 ⇒ 只是复制，没有左右交替）" if dm < 0.05 and mm < 0.05 else "（侧视：不适用镜像判据）"))

    if evidence_dir:
        write_evidence(evidence_dir, masks)

    ok = (top_spread >= TOP_MIN) and (contact_change >= CONTACT_MIN) and (height_dev <= HEIGHT_DEV_MAX)
    print("RESULT: %s  （起伏 %d %s %d ／ 触地分布变化 %.1f%% %s %.1f%% ／ 帧高偏离 %.3f %s %.2f ／ 参考姿态幅度 %.3f）" % (
        "PASS" if ok else "FAIL",
        top_spread, ">=" if top_spread >= TOP_MIN else "<", TOP_MIN,
        contact_change * 100,
        ">=" if contact_change >= CONTACT_MIN else "<", CONTACT_MIN * 100,
        height_dev, "<=" if height_dev <= HEIGHT_DEV_MAX else ">", HEIGHT_DEV_MAX,
        pair_max))
    return ok


# ────────────────────────── 合成自检素材 ──────────────────────────
import math


def _leg(theta_deg, knee_deg, hip, l1, l2):
    th = math.radians(theta_deg)
    kn = math.radians(knee_deg)
    kx = hip[0] + l1 * math.sin(th)
    ky = hip[1] + l1 * math.cos(th)
    ax = kx + l2 * math.sin(th + kn)
    ay = ky + l2 * math.cos(th + kn)
    return (kx, ky), (ax, ay)


def _leg_pose(t):
    """一整个周期：0~0.5 支撑相（前→后，脚踩地），0.5~1 摆动相（后→前，抬脚）。"""
    t = t % 1.0
    if t < 0.5:
        u = t / 0.5
        return 24 - 48 * u, 6 + 16 * math.sin(math.pi * u)
    u = (t - 0.5) / 0.5
    return -24 + 48 * u, 6 + 58 * math.sin(math.pi * u)


def synth_figure_layer(t, ground, l1=48, l2=48):
    """画在**单格局部坐标**里（cx 固定为格中线），由调用方按 i*CELL_W 贴到 sheet 上。"""
    layer = Image.new("RGBA", (CELL_W, CELL_H), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    ink = (60, 50, 40, 255)
    cx = CELL_W / 2
    hip_y = ground - 200

    knee_a, ankle_a = _leg(*_leg_pose(t), hip=(cx, hip_y), l1=l1, l2=l2)
    knee_b, ankle_b = _leg(*_leg_pose(t + 0.5), hip=(cx, hip_y), l1=l1, l2=l2)
    low_ankle = max(ankle_a[1], ankle_b[1])
    dy = ground - (low_ankle + 4)
    shift = lambda p: (p[0], p[1] + dy)

    shoulder = (cx, hip_y - 78 + dy)
    d.line([shoulder, (cx, hip_y + dy)], fill=ink, width=9)
    d.line([(cx, hip_y - 110 + dy), shoulder], fill=ink, width=8)   # 脖子：让头与躯干连通成一个连通块
    d.ellipse([cx - 15, hip_y - 128 + dy, cx + 15, hip_y - 98 + dy], fill=ink)
    for (th, kn), name in ((_leg_pose(t), "a"), (_leg_pose(t + 0.5), "b")):
        kn_p, an_p = _leg(th, kn, hip=(cx, hip_y), l1=l1, l2=l2)
        d.line([shift((cx, hip_y)), shift(kn_p), shift(an_p)], fill=ink, width=8, joint="curve")
        d.line([shift((an_p[0] - 14, an_p[1] + 2)), shift((an_p[0] + 12, an_p[1] + 2))], fill=ink, width=6)
    for off in (0, math.pi):
        ah = math.radians(26 * math.sin(2 * math.pi * (t + (0.5 if off else 0)) + off))
        elbow = (cx + 34 * math.sin(ah), hip_y - 62 + dy)
        hand = (elbow[0] + 30 * math.sin(ah * 1.4), elbow[1] + 34 * math.cos(ah * 0.6))
        d.line([shift((cx, hip_y - 70)), elbow, hand], fill=ink, width=7, joint="curve")
    return layer


def synth_sheet(mode, expected=8, ground=560):
    sheet = Image.new("RGB", (CELL_W * expected, CELL_H), PAPER)
    for i in range(expected):
        if mode in ("cycle", "cropped"):
            layer = synth_figure_layer(i / float(expected), ground)
            if mode == "cropped" and i == 3:
                # 只留剪影上部 70%：帧高变矮，但**顶行不变、也仍旧不着地**
                # ⇒ B（起伏）与 C（触地）都还是过，只有 D（帧内实体完整性）能抓到。
                # 这就是 2026-10-15 T125 那个真实坏图的形状（帧被截，不是没动）。
                box = layer.getbbox()
                layer = layer.crop((0, 0, CELL_W, box[1] + int((box[3] - box[1]) * 0.7)))
            sheet.paste(layer, (i * CELL_W, 0), layer)
        else:
            base = synth_figure_layer(0.0, ground)
            if mode == "clone_jitter":
                shifted = Image.new("RGBA", base.size, (0, 0, 0, 0))
                shifted.paste(base, ((i % 3) - 1, (i % 2) - 1), base)
                base = shifted
            sheet.paste(base, (i * CELL_W, 0), base)
    return sheet


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    flags = [a for a in sys.argv[1:] if a.startswith("--")]
    evidence = None
    if "--evidence" in sys.argv:
        k = sys.argv.index("--evidence")
        evidence = sys.argv[k + 1] if k + 1 < len(sys.argv) else "."
        if evidence in args:
            args.remove(evidence)

    if "--selftest" in flags:
        print("=== SELFTEST：合成素材必须被正确判定 ===")
        import tempfile
        tmp = tempfile.mkdtemp(prefix="walk-motion-selftest-")
        cycle_pass = judge(synth_sheet("cycle"), 8, os.path.join(tmp, "cycle"), "[合成·真走周期] ")
        clone_pass = judge(synth_sheet("clone"), 8, os.path.join(tmp, "clone"), "[合成·同一姿势复制8份] ")
        jitter_pass = judge(synth_sheet("clone_jitter"), 8, os.path.join(tmp, "jitter"), "[合成·复制+抖动] ")
        cropped_pass = judge(synth_sheet("cropped"), 8, os.path.join(tmp, "cropped"), "[合成·单帧下半截断] ")
        good = cycle_pass and (not clone_pass) and (not jitter_pass) and (not cropped_pass)
        print("SELFTEST: %s  （真周期=%s ／ 复制=%s ／ 复制+抖动=%s ／ 单帧截断=%s；期望 PASS/FAIL/FAIL/FAIL）" % (
            "PASS" if good else "FAIL", cycle_pass, clone_pass, jitter_pass, cropped_pass))
        print("临时证据图: " + tmp)
        sys.exit(0 if good else 1)

    if not args:
        raise SystemExit(__doc__)
    sheet_path = args[0]
    expected = int(args[1]) if len(args) > 1 else 8
    sheet = flatten_sheet_paper(Image.open(sheet_path))
    print("sheet %s  %dx%d" % (sheet_path, sheet.size[0], sheet.size[1]))
    ok = judge(sheet, expected, evidence, "")
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
