# -*- coding: utf-8 -*-
"""WALK-RIG-2 ｜ 走周期骨骼引导图（免费，纯 PIL 画线，不花积分）。

━━ 为什么要有这个 ━━
2026-10-29 实测：只用文字描述关键姿势（"contact / down / pass / up"）让模型去画，
命中率不稳——`side-pass-v1` 实际画成了"前腿前伸、脚离地"的推离相，不是"双腿并拢、
支撑腿在身下"的通过相。一次跑偏 = 1 积分，试错成本直接落在钱上。

改成业界通行的做法：**先画骨架，再让模型照着骨架把人物画出来**。
骨架由本脚本按标准走周期数学算出来，于是：
  · 8 帧的姿势是**算出来的**，不是描述出来的 —— 不存在"模型理解偏了"；
  · 支撑脚**按构造**落在地面线上（见 snap_to_ground）—— `extract-walk-sheet.py`
    的 `groundSpreadPx <= 6` 这条闸门从"祈祷模型画准"变成"结构上必然成立"；
  · 骨架图不花钱，改参数重算不心疼。

━━ 走周期模型（侧身向右走，8 帧一周期）━━
  相位 p ∈ [0,1)：两腿对称，右腿 = 左腿相位 + 0.5
  髋角   hip(p)   = A_hip * cos(2πp)           前摆为正
  膝角   knee(p)  = B_knee * (1 - cos(2πp + φ)) /2  —— 只在摆动相弯曲
  踝/脚 flat 与否：支撑相整脚贴地，摆动相脚尖下垂
  手臂   arm(p)   = -A_arm * cos(2πp)          与同侧腿反相
关键姿势自然落在 p = 0(contact) / 0.125(down) / 0.25(pass) / 0.375(up) ...
本脚本用 p = i/8 采 8 帧，正好对上 contact/down/pass/up 的四个相位再各来一次反向。

━━ 正面（2026-10-31 修）━━
旧 front 分支把侧身 FK 的前后摆幅 x 乘 0.30 后当成**左右**偏移画，正面看两腿左右交叉
（剪刀腿：phase 0 左踝 x=+35.9、髋在 −28，越过中线）。正面看迈步不产生左右位移，
它表现为三件事，现在由 draw_front_frame() 按这三件画：
  · 每条腿钉在自己那一侧（踝 x = 髋 x，永不越中线）；
  · 摆动脚离地（抬脚高度 = 侧身 FK 的同一相位算出的离地量，与侧身一致）；
  · 屈膝（膝点向外撇 0.4 px/度，让模型读出"这条腿弯了"），髋高随相位起伏。
`--selftest` 断言正面 8 相位任何一帧左右踝都不越过中线，并用旧算法做越线夹具证明它会红。

用法：
    python Tools/make-walk-pose-guide.py --view side  --out <目录>
    python Tools/make-walk-pose-guide.py --view front --out <目录> [--canvas-height 1536]
    python Tools/make-walk-pose-guide.py --view back  --out <目录>
    python Tools/make-walk-pose-guide.py --view side --contact-sheet   # 只出一张 8 帧拼版供人看
    python Tools/make-walk-pose-guide.py --selftest

`--canvas-height` 只给正面用：画布在上下对称补底色，人物与地面线随之下移，
用于 gpt-image 的 1024x1536 标准档（默认 1365 = 原 3:4 画布，不变）。
"""
import argparse
import math
import os
import sys

from PIL import Image, ImageDraw

for _s in (sys.stdout, sys.stderr):
    if hasattr(_s, "reconfigure"):
        _s.reconfigure(encoding="utf-8", errors="replace")

# 画布与锚点同规格（1024x1365 = 3:4 = 816:1088 帧格同比例）
W, H = 1024, 1365
# 与 anchor-*.png 实测对齐：头顶 49、脚底 1295 ⇒ 身高 1246
TOP, BOTTOM = 49, 1295
BODY = BOTTOM - TOP

HIP_Y = TOP + int(BODY * 0.52)
SHOULDER_Y = TOP + int(BODY * 0.21)
HEAD_R = int(BODY * 0.062)
HEAD_CY = TOP + HEAD_R + int(BODY * 0.012)

THIGH = int(BODY * 0.255)
SHIN = int(BODY * 0.245)
UPPER_ARM = int(BODY * 0.195)
FOREARM = int(BODY * 0.180)

A_HIP = math.radians(24)      # 髋前后摆幅
A_KNEE = math.radians(58)     # 摆动相最大屈膝
A_ARM = math.radians(22)      # 臂摆幅
LEG_LIFT = 0.9                # 摆动相抬脚比例（1 = 抬到膝高）

OUTLINE = (196, 64, 48)       # 引导线用暖红，和米色底、人物本色都拉得开
JOINT = (196, 64, 48)


# 8 相位关键帧表（近侧腿；远侧腿 = 相位 + 4 帧）。角度单位：度，+ 为向前。
# 抓过的两个错都在这张表上：
#   ① 用 sin 驱动屈膝 ⇒ 腿在前时屈膝，画成火烈鸟单腿站立；
#   ② 屈膝给到 58° ⇒ 摆动相脚后踢 259px、离地 158px，像驴踢。
# 手写这张表，把"支撑相几乎伸直、摆动相才屈膝、离地约 70px"钉死。
LEG_KEYS = [
    # phase, thigh, knee
    (0.000,  22,  4),   # contact：前脚跟着地，腿伸直
    (0.125,  10, 12),   # down：承重，膝微弯
    (0.250,   0,  6),   # pass：支撑腿在身下
    (0.375, -12,  8),   # up：脚跟离地，向后推
    (0.500, -22, 18),   # toe off：脚尖蹬离
    (0.625, -10, 34),   # lift：屈膝收脚
    (0.750,   4, 30),   # swing：膝仍弯，脚从身下过
    (0.875,  16, 14),   # reach：腿向前伸，准备落地
]

A_HIP_FALLBACK = None  # 保留旧常量名以免外部引用炸；本版本不再用正弦驱动


def sample_leg(phase):
    """在关键帧表上线性插值，返回 (thigh_deg, knee_deg)。"""
    phase = phase % 1.0
    n = len(LEG_KEYS)
    for i in range(n):
        p0, t0, k0 = LEG_KEYS[i]
        p1, t1, k1 = LEG_KEYS[(i + 1) % n]
        span = (p1 - p0) % 1.0 or 1.0
        rel = (phase - p0) % 1.0
        if rel <= span + 1e-9:
            f = rel / span
            return t0 + (t1 - t0) * f, k0 + (k1 - k0) * f
    return LEG_KEYS[0][1], LEG_KEYS[0][2]


def leg_joints(phase, side_sign):
    """返回 (髋, 膝, 踝, 脚尖, 屈膝量)。侧身向右走：+x 为前。"""
    thigh_deg, knee_deg = sample_leg(phase)
    hip_angle = math.radians(thigh_deg)
    knee_angle = math.radians(knee_deg)
    hip = (0.0, 0.0)
    kx = math.sin(hip_angle) * THIGH
    ky = math.cos(hip_angle) * THIGH
    knee = (hip[0] + kx, hip[1] + ky)
    shin_angle = hip_angle - knee_angle
    ax = knee[0] + math.sin(shin_angle) * SHIN
    ay = knee[1] + math.cos(shin_angle) * SHIN
    ankle = (ax, ay)
    toe = (ax + math.sin(shin_angle + math.radians(10)) * BODY * 0.045,
           ay + math.cos(shin_angle + math.radians(10)) * BODY * 0.010)
    return hip, knee, ankle, toe, knee_deg


def arm_joints(phase):
    angle = -A_ARM * math.cos(2 * math.pi * phase)
    sh = (0.0, 0.0)
    ex = math.sin(angle) * UPPER_ARM
    ey = math.cos(angle) * UPPER_ARM
    elbow = (sh[0] + ex, sh[1] + ey)
    fore_angle = angle + math.radians(14)
    wx = elbow[0] + math.sin(fore_angle) * FOREARM
    wy = elbow[1] + math.cos(fore_angle) * FOREARM
    return sh, elbow, (wx, wy)


def build_pose(phase, view):
    """算出一帧全身关键点，并把**最低的脚**钉到地面线上。"""
    legs = []
    for k, side in enumerate((0.0, 0.5)):
        hip, knee, ankle, toe, swing = leg_joints((phase + side) % 1.0, 1)
        legs.append(dict(hip=hip, knee=knee, ankle=ankle, toe=toe, swing=swing, far=(k == 1)))

    if view == "side":
        # 侧身：两条腿在 x 上前后错开（同一条线），各自用上面的 FK 结果
        pass
    elif view == "front":
        # 正面：腿在 x 上左右分开，前后摆幅压到 30%，靠 y 抬落表达迈步
        for item in legs:
            scale = 0.30
            for key in ("knee", "ankle", "toe"):
                x, y = item[key]
                item[key] = (x * scale, y)
            item["hip"] = (0.0, 0.0)
    else:
        # 背面：同正面
        for item in legs:
            for key in ("knee", "ankle", "toe"):
                x, y = item[key]
                item[key] = (x * 0.30, y)
            item["hip"] = (0.0, 0.0)

    # 地面吸附：取两脚最低点，整体上移使最低点 = BOTTOM
    lowest = max(max(item["ankle"][1], item["toe"][1]) for item in legs)
    shift = BOTTOM - (HIP_Y + lowest)
    return legs, shift


BG = (245, 234, 212)
FAR_COLOR = (232, 150, 120)
GROUND_COLOR = (120, 150, 120)
FRONT_HIP_HALF = 34        # 正面两腿髋距的一半
FRONT_SHOULDER_HALF = 70   # 正面肩宽的一半
FRONT_KNEE_SPLAY = 0.4     # 正面屈膝外撇：px / 度


def front_pose(phase, pad_top=0, lift_gain=1.0, cross_px=0.0, bob_amp=0.0):
    """正面一帧的关键点（纯数据，selftest 与画图共用同一份）。

    取侧身 FK 的 y 方向量（抬脚高度、髋高起伏）——那部分对任何朝向都成立；
    x 方向的前后摆在正面看不到，丢掉，每条腿钉在自己那一侧。

    三个夸张参数只给「画给模型看的参考条」用（默认 1.0/0.0 等于原来的规矩骨架）：
      lift_gain  抬脚高度放大（模型只肯照着夸张的比例画，最终由归一化收回）
      cross_px   摆动腿脚尖越过身体中线多少 px（正数=越线；默认 0 = 不越线）
      bob_amp    上身垂直起伏幅度（px，脚不动、只有髋以上在动；0 = 原来的规矩骨架）
    selftest 始终用默认值，所以「不越线」这条约束仍然成立。"""
    legs, shift = build_pose(phase, "side")
    cx = W // 2
    hip_y = HIP_Y + shift + pad_top
    out = dict(cx=cx, hip_y=hip_y, shoulder_y=SHOULDER_Y + shift + pad_top,
               head_cy=HEAD_CY + shift + pad_top, ground=BOTTOM + pad_top, legs=[], phase=phase)
    for k, item in enumerate(legs):
        sign = -1 if k == 0 else 1
        x = cx + sign * FRONT_HIP_HALF
        foot_y = hip_y + max(item["ankle"][1], item["toe"][1])
        lift = BOTTOM + pad_top - foot_y
        ankle_y = hip_y + max(item["ankle"][1], 0)
        knee_y = hip_y + item["knee"][1]
        ankle_x = x
        knee_x = x + sign * int(item["swing"] * FRONT_KNEE_SPLAY)
        if lift > 0 and lift_gain != 1.0:
            extra = lift * (lift_gain - 1.0)
            ankle_y -= extra
            knee_y -= extra * 0.55
            foot_y -= extra
            lift += extra
        if lift > 0 and cross_px:
            ankle_x -= sign * cross_px
            knee_x -= sign * cross_px * 0.35
        out["legs"].append(dict(
            sign=sign, far=item["far"], swing=item["swing"],
            hip=(x, hip_y),
            knee=(int(round(knee_x)), knee_y),
            ankle=(int(round(ankle_x)), ankle_y),
            foot_y=foot_y, lift=lift))
    # 夸张之后必须重新贴地：抬高挑脚/放大起伏会把支撑脚也一起顶离地面，
    # 不补这一步，8 帧全是"吊在空中"的姿势，模型照着画就是一排浮空的人。
    lowest = max(leg["foot_y"] for leg in out["legs"])
    delta = out["ground"] - lowest
    if abs(delta) > 0.5:
        out["hip_y"] += delta
        out["shoulder_y"] += delta
        out["head_cy"] += delta
        for leg in out["legs"]:
            leg["hip"] = (leg["hip"][0], leg["hip"][1] + delta)
            leg["knee"] = (leg["knee"][0], leg["knee"][1] + delta)
            leg["ankle"] = (leg["ankle"][0], leg["ankle"][1] + delta)
            leg["foot_y"] += delta
    for leg in out["legs"]:
        leg["lift"] = out["ground"] - leg["foot_y"]
    if bob_amp:
        # 走路每周期起伏两次：相位 0 / 0.5（触地）最低，0.25 / 0.75（过腿）最高。
        # 只抬上身与膝，踝和脚掌钉在原地——不然整帧一起浮起来，支撑脚就离地了。
        bob = -bob_amp * math.cos(4.0 * math.pi * phase)
        out["hip_y"] += bob
        out["shoulder_y"] += bob
        out["head_cy"] += bob
        for leg in out["legs"]:
            leg["hip"] = (leg["hip"][0], leg["hip"][1] + bob)
            leg["knee"] = (leg["knee"][0], leg["knee"][1] + bob * 0.5)
    return out


def back_pose(phase, pad_top=0, lift_gain=1.0, cross_px=0.0, bob_amp=0.0, shoulder_amp=0.0):
    """背面一帧的关键点：**正面镜像** + 肩线上下交替。

    背面的腿和正面是同一套：踝钉在自己那一侧，抬脚高度、越中线、起伏全都继承。
    原来背面走的是 build_pose(phase,"back") 老分支（侧视 FK 的踝 x 直接乘 0.30），
    前摆大的相位会被甩到中线另一侧，画出来就是剪刀腿（f00 左踝 x=+35.9）。

    背面没有脸，观众靠**肩背的起伏**和手臂摆动认出"他在走"，所以肩线要倾斜：
    一侧肩高一侧肩低，随相位交替，幅度 shoulder_amp。
    """
    pose = front_pose(phase, pad_top, lift_gain, cross_px, bob_amp)
    cx = pose["cx"]
    for leg in pose["legs"]:
        for key in ("hip", "knee", "ankle"):
            x, y = leg[key]
            leg[key] = (2 * cx - x, y)
        leg["sign"] = -leg["sign"]
    roll = shoulder_amp * math.cos(2 * math.pi * phase)
    pose["shoulder_l"] = pose["shoulder_y"] - roll
    pose["shoulder_r"] = pose["shoulder_y"] + roll
    pose["shoulder_roll"] = roll
    return pose


def _draw_pose(pose, canvas_h=H, lift_gain=1.0):
    """正面/背面共用一套画法；背面多出来的 shoulder_l/r 会让肩线自然倾斜。"""
    img = Image.new("RGB", (W, canvas_h), BG)
    d = ImageDraw.Draw(img)
    cx, hip_y, head_cy = pose["cx"], pose["hip_y"], pose["head_cy"]
    sh_l = pose.get("shoulder_l", pose["shoulder_y"])
    sh_r = pose.get("shoulder_r", pose["shoulder_y"])
    d.line([(cx, hip_y), (cx, (sh_l + sh_r) / 2.0)], fill=OUTLINE, width=9)
    d.ellipse([cx - HEAD_R, head_cy - HEAD_R, cx + HEAD_R, head_cy + HEAD_R], outline=OUTLINE, width=7)
    d.line([(cx - FRONT_SHOULDER_HALF, sh_l), (cx + FRONT_SHOULDER_HALF, sh_r)], fill=OUTLINE, width=8)
    d.line([(cx - FRONT_HIP_HALF, hip_y), (cx + FRONT_HIP_HALF, hip_y)], fill=OUTLINE, width=8)
    for k, leg in enumerate(pose["legs"]):
        color = OUTLINE if k == 0 else FAR_COLOR
        d.line([leg["hip"], leg["knee"]], fill=color, width=11)
        d.line([leg["knee"], leg["ankle"]], fill=color, width=9)
        x = leg["ankle"][0]
        sole_half = 26 if (lift_gain > 1.0 and leg["lift"] > 0) else 18
        d.line([(x - sole_half, leg["foot_y"]), (x + sole_half, leg["foot_y"])], fill=color, width=8)  # 脚掌：正面是一道横线
        for p in (leg["hip"], leg["knee"], leg["ankle"]):
            d.ellipse([p[0] - 6, p[1] - 6, p[0] + 6, p[1] + 6], fill=JOINT)
    # 手臂与同侧腿反相前后摆；正面看是前臂上下位置的变化，左右只给很小的外张
    _, elbow, wrist = arm_joints(pose["phase"])
    for sign, color, sh in ((-1, OUTLINE, sh_l), (1, FAR_COLOR, sh_r)):
        swing = wrist[0] * (1 if sign == -1 else -1)
        s = (cx + sign * FRONT_SHOULDER_HALF, sh)
        e = (s[0] + sign * 14, sh + elbow[1] - abs(swing) * 0.15)
        w = (s[0] + sign * 22, sh + wrist[1] - max(0.0, swing) * 0.5)
        d.line([s, e], fill=color, width=9)
        d.line([e, w], fill=color, width=7)
    d.line([(int(W * 0.12), pose["ground"]), (int(W * 0.88), pose["ground"])], fill=GROUND_COLOR, width=3)
    return img


def draw_front_frame(phase, canvas_h=H, lift_gain=1.0, cross_px=0.0, bob_amp=0.0):
    pad_top = (canvas_h - H) // 2
    return _draw_pose(front_pose(phase, pad_top, lift_gain, cross_px, bob_amp), canvas_h, lift_gain)


def draw_back_frame(phase, canvas_h=H, lift_gain=1.0, cross_px=0.0, bob_amp=0.0, shoulder_amp=16.0):
    pad_top = (canvas_h - H) // 2
    return _draw_pose(back_pose(phase, pad_top, lift_gain, cross_px, bob_amp, shoulder_amp),
                      canvas_h, lift_gain)


def legacy_front_ankles_x(phase):
    """旧 front 分支的踝 x（相对中线）——只给 selftest 当越线夹具用，不参与出图。"""
    legs, _ = build_pose(phase, "side")
    xs = []
    for index, item in enumerate(legs):
        off_x = -28 if index == 0 else 28
        xs.append(off_x + item["ankle"][0] * 0.30)
    return xs


def legacy_back_ankles_x(phase):
    """旧 back 分支（build_pose view="back" 直出）的踝 x，相对中线——只给 selftest 当夹具。"""
    legs, _ = build_pose(phase, "back")
    return [(-28 if index == 0 else 28) + item["ankle"][0] for index, item in enumerate(legs)]


def crossing_frames(ankles_by_frame):
    """ankles_by_frame: [(左踝x, 右踝x)]，x 相对中线。左踝必须 < 0、右踝必须 > 0。返回越线帧号。"""
    return [i for i, (lx, rx) in enumerate(ankles_by_frame) if not (lx < 0 < rx)]


def scissor_frames(ankles_by_frame):
    """只要两只脚分居中线两侧即可，不认哪只是哪只。

    背面是正面的镜像，原本在画面左边的腿会跑到右边——用 crossing_frames 判"左踝<0"
    会把每一帧都误报成越线。真正要防的是剪刀腿：两踝都落在同一侧。
    """
    return [i for i, (a, b) in enumerate(ankles_by_frame) if (a < 0) == (b < 0)]


def selftest(frames=8):
    ok = True
    new = []
    for i in range(frames):
        pose = front_pose(i / float(frames))
        lx = pose["legs"][0]["ankle"][0] - pose["cx"]
        rx = pose["legs"][1]["ankle"][0] - pose["cx"]
        new.append((lx, rx))
        lifts = [round(leg["lift"]) for leg in pose["legs"]]
        print("  front f%02d  左踝 x=%+6.1f  右踝 x=%+6.1f  离地 %s  髋 y=%d" % (i, lx, rx, lifts, pose["hip_y"]))
        if min(lifts) != 0:
            print("  FAIL f%02d 两脚都离地（支撑脚必须踩在地面线上）" % i)
            ok = False
    bad = crossing_frames(new)
    print("[1] 正面 8 相位踝不越中线：%s（越线帧 %s）" % ("PASS" if not bad else "FAIL", bad or "无"))
    ok = ok and not bad
    lifted = sum(1 for i in range(frames) if max(l["lift"] for l in front_pose(i / float(frames))["legs"]) >= 10)
    hips = [front_pose(i / float(frames))["hip_y"] for i in range(frames)]
    print("[2] 有摆动脚离地 >=10px 的帧 %d/%d、髋高极差 %dpx：%s" % (
        lifted, frames, max(hips) - min(hips), "PASS" if lifted >= frames // 2 and max(hips) > min(hips) else "FAIL"))
    ok = ok and lifted >= frames // 2 and max(hips) > min(hips)
    # 夹具：旧算法必须被同一判据抓红，否则判据没有牙齿
    legacy = [tuple(legacy_front_ankles_x(i / float(frames))) for i in range(frames)]
    legacy_bad = crossing_frames(legacy)
    fired = bool(legacy_bad)
    print("[3] 越线夹具（旧 front 算法）必须点火：%s（越线帧 %s）" % ("PASS" if fired else "FAIL", legacy_bad))
    synthetic_bad = crossing_frames([(-30.0, 30.0), (12.0, 40.0)])
    print("[4] 合成夹具（第 1 帧左踝 x=+12 越线）必须点火：%s" % ("PASS" if synthetic_bad == [1] else "FAIL"))
    ok = ok and fired and synthetic_bad == [1]

    # 背面：同一套判据。背面 = 正面镜像，踝同样必须待在自己那一侧
    back_pairs, rolls = [], []
    for i in range(frames):
        pose = back_pose(i / float(frames), shoulder_amp=16.0)
        back_pairs.append((pose["legs"][0]["ankle"][0] - pose["cx"],
                           pose["legs"][1]["ankle"][0] - pose["cx"]))
        rolls.append(pose["shoulder_l"] - pose["shoulder_y"])
    back_bad = scissor_frames(back_pairs)
    print("[5] 背面 8 相位两踝分居中线两侧（剪刀腿判据）：%s（越线帧 %s）"
          % ("PASS" if not back_bad else "FAIL", back_bad or "无"))
    legacy_back_bad = scissor_frames([legacy_back_ankles_x(i / float(frames)) for i in range(frames)])
    print("[6] 剪刀腿夹具（旧 back 算法）必须点火：%s（越线帧 %s）"
          % ("PASS" if legacy_back_bad else "FAIL", legacy_back_bad or "无"))
    roll_span = max(rolls) - min(rolls)
    print("[7] 背面肩线起伏 极差 %.1fpx（>8 才看得出肩在动）：%s"
          % (roll_span, "PASS" if roll_span > 8 else "FAIL"))
    ok = ok and not back_bad and bool(legacy_back_bad) and roll_span > 8
    print("SELFTEST: %s" % ("PASS" if ok else "FAIL"))
    return 0 if ok else 1


def draw_frame(phase, view, spread):
    """spread<1 时两腿在 x 上对称分开（正/背面用）。"""
    img = Image.new("RGB", (W, H), (245, 234, 212))
    draw = ImageDraw.Draw(img)
    legs, shift = build_pose(phase, view)

    cx = W // 2
    hip_y = HIP_Y + shift
    shoulder_y = SHOULDER_Y + shift
    head_cy = HEAD_CY + shift

    # 躯干
    draw.line([(cx, hip_y), (cx, shoulder_y)], fill=OUTLINE, width=9)
    # 头
    draw.ellipse([cx - HEAD_R, head_cy - HEAD_R, cx + HEAD_R, head_cy + HEAD_R],
                 outline=OUTLINE, width=7)
    # 肩线
    draw.line([(cx - 26, shoulder_y), (cx + 26, shoulder_y)], fill=OUTLINE, width=8)

    for index, item in enumerate(legs):
        off_x = 0 if view == "side" else (-int(28 * spread) if index == 0 else int(28 * spread))
        color = OUTLINE if not item["far"] else (232, 150, 120)   # 远侧腿淡一档，模型才知道前后
        hip = (cx + off_x, hip_y)
        knee = (cx + off_x + item["knee"][0], hip_y + item["knee"][1])
        ankle = (cx + off_x + item["ankle"][0], hip_y + item["ankle"][1])
        toe = (cx + off_x + item["toe"][0], hip_y + item["toe"][1])
        draw.line([hip, knee], fill=color, width=11)
        draw.line([knee, ankle], fill=color, width=9)
        draw.line([ankle, toe], fill=color, width=7)
        for point in (hip, knee, ankle):
            draw.ellipse([point[0] - 6, point[1] - 6, point[0] + 6, point[1] + 6], fill=JOINT)

    sh, elbow, wrist = arm_joints(phase)
    arm_scale = 1.0 if view == "side" else 0.45
    for sign in (-1, 1):
        if view == "side" and sign == -1:
            pass   # 侧身两条都画（远的淡一档），否则模型会少一条胳膊
        s = (cx, shoulder_y)
        e = (cx + sh[0] + elbow[0] * arm_scale * sign, shoulder_y + elbow[1])
        w = (cx + sh[0] + wrist[0] * arm_scale * sign, shoulder_y + wrist[1])
        color = OUTLINE if sign == 1 else (232, 150, 120)
        draw.line([s, e], fill=color, width=9)
        draw.line([e, w], fill=color, width=7)

    # 地面线（引导模型把支撑脚放这里）
    draw.line([(int(W * 0.12), BOTTOM), (int(W * 0.88), BOTTOM)], fill=(120, 150, 120), width=3)
    return img


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--view", default="side", choices=["front", "side", "back"])
    parser.add_argument("--out", default=None)
    parser.add_argument("--frames", type=int, default=8)
    parser.add_argument("--contact-sheet", action="store_true")
    parser.add_argument("--canvas-height", type=int, default=H,
                        help="只对 front 生效：画布高（>=%d，上下对称补底色）" % H)
    parser.add_argument("--exaggerate", action="store_true",
                        help="只给「画给模型看的参考条」：抬脚 x2、摆动脚尖越过中线约 12px、上身起伏 18px、背面另加肩线交替 16px。"
                             "默认关闭，selftest 与 batch.json 用的骨架不受影响")
    parser.add_argument("--selftest", action="store_true")
    args = parser.parse_args()
    if args.selftest:
        return selftest(args.frames)
    if args.canvas_height < H:
        parser.error("--canvas-height must be >= %d" % H)
    if args.view == "side" and (args.canvas_height != H or args.exaggerate):
        parser.error("--canvas-height / --exaggerate 不适用于 --view side")

    out = args.out or os.path.join(
        "GameClient", "TwelveJade", "Assets", "ArtSource", "candidates",
        "walk-rig-20261029", "guides-" + args.view)
    os.makedirs(out, exist_ok=True)

    frames = []
    for i in range(args.frames):
        phase = i / float(args.frames)
        spread = 1.0 if args.view != "side" else 0.0
        if args.view == "front":
            gain = (2.0, 46.0, 18.0) if args.exaggerate else (1.0, 0.0, 0.0)
            img = draw_front_frame(phase, args.canvas_height, *gain)
        elif args.view == "back":
            gain = (2.0, 46.0, 18.0, 16.0) if args.exaggerate else (1.0, 0.0, 0.0, 0.0)
            img = draw_back_frame(phase, args.canvas_height, *gain)
        else:
            img = draw_frame(phase, args.view, spread)
        path = os.path.join(out, "guide-%02d.png" % i)
        img.save(path)
        frames.append(img)
        print("wrote %s (phase %.3f)" % (path, phase))

    if args.contact_sheet:
        ch = frames[0].height
        sheet = Image.new("RGB", (W // 2 * args.frames, ch // 2), (245, 234, 212))
        for i, img in enumerate(frames):
            sheet.paste(img.resize((W // 2, ch // 2), Image.LANCZOS), (i * W // 2, 0))
        path = os.path.join(out, "contact-sheet.png")
        sheet.save(path)
        print("wrote %s" % path)
    return 0


if __name__ == "__main__":
    sys.exit(main())
