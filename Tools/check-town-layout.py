# -*- coding: utf-8 -*-
"""城镇布局量化门禁（C-02 · 方案 `Documents/布局与移动-验收方案-ui.md` §1 落地）。

覆盖 G-A1~A6 密度分区 / G-B1~B3 D-H 与路网 / G-C1~C5 高度阶梯 / G-D1~D3 锚点视线，共 15 条。
数据源**只读** building-plots.json（plots[]）+ town-map.json（terrainShape/landmarks/spawn）；
**不读** town-map.json 的 props / walkable——那是死契约（空数组，规格 1.6.1），读它们等于把门禁
挂在一个永远为真的输入上（本仓 "能力存在≠功能存在" 的假绿同族）。

数值口径（全部来自规格）：1 身位=170；一屏 1920×1080；跑速 470/s ⇒ 2 秒=940。
L0<340 ／ L1 340–509 ／ L2 510–594 ／ L3 595–799 ／ L4≥800；主街 half=250，横巷 half≤100，田埂 60–80。

归区（规格 2.1 的 y 带在 2200/4700 处重叠，必须有确定性优先级，否则同一件会算进两个区）：
  河带(|y−riverY|≤bankHalf+60)→区3 ｜ 农田矩形→区9 ｜ 市集圆→区6 ｜ 广场圆→区5 ｜
  其余连续分带：<1600→1 ｜ <2500→2 ｜ <4000→4 ｜ <4650→5 ｜ <5300→6 ｜ <6400→7 ｜ 其余 8
  （规格原表在 3900–4300 / 5000–5300 留了空格，这里补齐以保证每件 plot 恰好归一个区。）

纪律：入口 UTF-8 守卫；结尾必出 RESULT；WARN 不影响 rc；任一 G-* FAIL ⇒ rc=1 并列编号；
台账棘轮（仿 check-ui-layout.py）只许销账不许放宽，armed=False 为待命登记（不参与判定与棘轮）；
SKIP 一律打印原因，不计绿也不计红，也不做可销账棘轮；
**数值放行 ≠ 人眼认可**，观感归斐哥看图拍板（方案 §4）。
用法：python Tools/check-town-layout.py [--report|--selftest] [--plots F --map F]
"""
import copy
from world_geometry import main_road_half
import json
import math
import os
import sys
from collections import Counter

# GBK 控制台下 print 中文会把门禁崩在 RESULT 之前——会崩的门禁比红的门禁危险（全仓统一守卫）。
for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, 'reconfigure'):
        _stream.reconfigure(encoding='utf-8', errors='replace')

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CFG = os.path.join(ROOT, 'GameClient', 'TwelveJade', 'Assets', 'Resources', 'Config')
LOG = os.path.join(ROOT, 'Tools', 'logs', 'town-layout.log')

UNIT = 170.0            # 1 身位
L1, L2, L3, L4 = 340.0, 510.0, 595.0, 800.0   # 高度档下界（L0 = 低于 L1）
ANCHOR = 510.0                                 # "≥3 身位"大件口径（= L2 下界，规格 2.1/2.4 用的就是这个数）
SCREEN_W, SCREEN_H = 1920, 1080
RUN_SPEED = 470.0
TWO_SEC = RUN_SPEED * 2            # 940：跑速下 2 秒的行程
ZONE_NAMES = {
    1: '后山秘境带', 2: '山麓过渡带', 3: '河带·水口', 4: '北街·匠作坊', 5: '中心广场',
    6: '西市集', 7: '南街·民居', 8: '南门·官道', 9: '东西两翼·农田',
}
TOWN_CORE = (4, 5, 6, 7)           # 镇内四区（规格 2.1 的 "五区" 实列 4–7 四区，按四区实现）
L4_ALLOWED_ZONES = (1, 3, 4)       # 规格 2.6：L4 只在水口/祠堂后/河湾

# 台账（存量棘轮）。键 = 判据子键；level：'red' = 判红级存量（仍违规时 WARN 放行），
# 'warn' = WARN 级存量（升级到 FAIL 时 FAIL，不许放宽）。armed=False = 待命登记，不参与判定。
BASELINE = {
    # 祠堂–戏台三级同框：规格 2.6 的落地目标，实盘此刻只有 2 档（规格 1.7 硬伤2 的量化证据）
    'G-C4|plaza-tiers': {
        'level': 'red', 'armed': True,
        'note': '规格 2.6 目标 ≥3 档同框，实盘广场视窗只有 L0/L1 两档；等 L2/L3 落地销账',
    },
    # L2+L3 主力层 9 件 < 10 件：镇子感主力层不足
    'G-C1|main-layer': {
        'level': 'warn', 'armed': True,
        'note': '规格 2.7 目标 L2+L3 ≥10，实盘 9；加密销账',
    },
    # 2 秒视觉事件节奏：实盘事件簇拥（平均间距 171），比规格 400–600 更密——密集不致命，但记在案
    'G-A5|rhythm': {
        'level': 'warn', 'armed': True,
        'note': '事件平均间距 171 < 规格 400–600 下限；簇拥型摆件，间距链整理时销账',
    },
    # ---- 待命登记（当前达标 / 前置缺失，未 armed）----
    'G-B1|dh': {
        'level': 'warn', 'armed': True,
        'note': '实盘 D/H=1.75（沿街均高 286）偏空旷，规格 2.2 的宜人区间是 1.0–1.5；'
                '沿街加密后销账，升到 >2.0 则判红（台账不许放宽）',
    },
    'G-B3|tillage': {
        'level': 'warn', 'armed': False,
        'note': '待命：terrainShape 尚无 half 60–80 的田埂路条目（一期农田无实体），本条 SKIP；'
                '田埂进 terrainShape 当天置 armed=True',
    },
    'G-C5|median-height': {
        'level': 'warn', 'armed': False,
        'note': '待命：实盘 building/stall 中位高 440 ≥ 340 已达标；跌破时门禁直接判，届时核实后置 armed=True',
    },
}


# ---------------------------------------------------------------- 几何工具

def river_y(x, ts):
    rv = ts['river']
    y = float(rv['baseY'])
    for w in rv['wave']:
        y += w['amp'] * math.sin(x / w['period'] + w['phase'])
    return y


def road_x(y, ts):
    mr = ts['mainRoad']
    return float(mr['baseX']) + mr['amp'] * math.sin(y / mr['periodY'])


def in_rect(x, y, r):
    return r['x0'] <= x <= r['x1'] and r['y0'] <= y <= r['y1']


def zone_of(x, y, ts):
    """归区。优先级见文件头；保证每件 plot 恰好落一个区。"""
    rv = ts['river']
    if abs(y - river_y(x, ts)) <= rv['bankHalf'] + 60:
        return 3
    for f in ts.get('farmland') or []:
        if in_rect(x, y, f):
            return 9
    mk, pl = ts.get('market'), ts.get('plaza')
    if mk and math.hypot(x - mk['x'], y - mk['y']) <= mk['r']:
        return 6
    if pl and math.hypot(x - pl['x'], y - pl['y']) <= pl['r']:
        return 5
    if y < 1600:
        return 1
    if y < 2500:
        return 2
    if y < 4000:
        return 4
    if y < 4650:
        return 5
    if y < 5300:
        return 6
    if y < 6400:
        return 7
    return 8


def tier_of(h):
    """高度档编号：0=L0 <340，1=L1 340–509，2=L2 510–594，3=L3 595–799，4=L4 ≥800。"""
    if h < L1:
        return 0
    if h < L2:
        return 1
    if h < L3:
        return 2
    if h < L4:
        return 3
    return 4


def near_landmark(p, landmarks, radius=800.0):
    return any(math.hypot(p['footX'] - l['x'], p['footY'] - l['y']) <= radius for l in landmarks)


def coverage_domain(x, y, ts, spawn):
    """A3/A4/A5 的取样域：镇圆 ∪ 主路走廊（spawn→山门），域外的空窗不算"空屏"。"""
    if math.hypot(x - 4096.0, y - 4700.0) <= ts['townRadius']:
        return True
    mr = ts['mainRoad']
    if 3150 <= y <= float(spawn.get('y', 7900)) + 60 and abs(x - road_x(y, ts)) <= mr['half'] + 300:
        return True
    return False


def find_plot(plots, *needles):
    for p in plots:
        pid = str(p.get('id', '')).lower()
        if any(n in pid for n in needles):
            return p
    return None


# ---------------------------------------------------------------- 判定

class Check(object):
    def __init__(self, cid, title):
        self.id = cid
        self.title = title
        self.violations = []   # [{'id','key','level','text'}]
        self.skip = None
        self.rows = []         # 报告用明细

    def fail(self, key, text):
        self.violations.append({'id': self.id, 'key': key, 'level': 'FAIL', 'text': text})

    def warn(self, key, text):
        self.violations.append({'id': self.id, 'key': key, 'level': 'WARN', 'text': text})

    def do_skip(self, reason):
        self.skip = reason


def zone_index(plots, ts):
    """返回 {zone: [plot...]} 与 每件 plot 的区间号。"""
    zs = {z: [] for z in ZONE_NAMES}
    for p in plots:
        zs[zone_of(p['footX'], p['footY'] + p['height'], ts)].append(p)  # 按落脚点分区，不能把6380..6950的南门按图顶误分进南街。
    return zs


def check_a1(plots, ts, lands):
    c = Check('G-A1', '九区密度：每区 ≥5 件、≥1 件 ≥2 身位，镇内四区 ≥8 件')
    zs = zone_index(plots, ts)
    for z in sorted(ZONE_NAMES):
        arr = zs[z]
        tall = [p for p in arr if p['height'] >= L1]
        if len(arr) < 5:
            c.fail('G-A1|z%d-count' % z, '区%d %s 仅 %d 件（门槛 ≥5）：该区在 2.5D 视觉上不成立'
                   % (z, ZONE_NAMES[z], len(arr)))
        if not tall:
            c.fail('G-A1|z%d-tall' % z, '区%d %s 没有 ≥2 身位（340）物件：人跟屋等高，玩具感'
                   % (z, ZONE_NAMES[z]))
        if z in TOWN_CORE and len(arr) < 8:
            c.warn('G-A1|z%d-core' % z, '区%d %s 仅 %d 件（镇内建议 ≥8）'
                   % (z, ZONE_NAMES[z], len(arr)))
    return c


def check_a2(plots, ts, lands):
    c = Check('G-A2', '每区双件：≥1 可交互锚点 + ≥1 件 ≥3 身位（510）')
    zs = zone_index(plots, ts)
    for z in sorted(ZONE_NAMES):
        arr = zs[z]
        anchors = [p for p in arr if p.get('kind') in ('building', 'stall') or near_landmark(p, lands)]
        big = [p for p in arr if p['height'] >= ANCHOR]
        if not anchors:
            c.fail('G-A2|z%d-anchor' % z, '区%d %s 无可交互锚点（building/stall 或地标 800 内）'
                   % (z, ZONE_NAMES[z]))
        if not big:
            c.fail('G-A2|z%d-big' % z, '区%d %s 无 ≥3 身位（510）物件：天际线该区缺一层'
                   % (z, ZONE_NAMES[z]))
    return c


def window_counts(plots, ts, spawn):
    """1920×1080 滑窗（步长 480）件数；只取中心落在取样域内的窗口。"""
    out = []
    y0 = -SCREEN_H
    while y0 <= 8192:
        x0 = -SCREEN_W
        while x0 <= 8192:
            cx, cy = x0 + SCREEN_W / 2.0, y0 + SCREEN_H / 2.0
            if coverage_domain(cx, cy, ts, spawn):
                n = sum(1 for p in plots
                        if x0 <= p['footX'] < x0 + SCREEN_W and y0 <= p['footY'] < y0 + SCREEN_H)
                out.append((cx, cy, n))
            x0 += 480
        y0 += 480
    return out


def check_a3(plots, ts, lands, spawn):
    c = Check('G-A3', '每屏有物：任一 ≥1920×1080 窗口件数 ≥1（空屏即荒野）')
    wins = window_counts(plots, ts, spawn)
    empt = [(cx, cy) for cx, cy, n in wins if n == 0]
    for cx, cy in empt[:8]:
        c.fail('G-A3|empty-window', '空屏：窗口中心 (%.0f, %.0f) 内 0 件物件' % (cx, cy))
    if len(empt) > 8:
        c.rows.append(('empty-more', len(empt) - 8))
    return c


def check_a4(plots, ts, lands, spawn):
    c = Check('G-A4', '同屏件数上限：任一窗口 ≤30 件（纵深排序开销红线）')
    wins = window_counts(plots, ts, spawn)
    worst = max(wins, key=lambda w: w[2]) if wins else (0, 0, 0)
    if worst[2] > 30:
        c.fail('G-A4|over-dense', '窗口中心 (%.0f, %.0f) 有 %d 件（上限 30）'
               % (worst[0], worst[1], worst[2]))
    return c


def check_a5(plots, ts, lands, spawn):
    c = Check('G-A5', '2 秒一视觉事件：主路上任一 940 单位行程 ≥1 事件，平均间距 400–600')
    mr = ts['mainRoad']
    lo, hi = 3280.0, float(spawn.get('y', 7900))
    events = sorted(p['footY'] for p in plots
                    if lo <= p['footY'] <= hi
                    and abs(p['footX'] - road_x(p['footY'], ts)) <= mr['half'] + 300)
    if not events:
        c.do_skip('主路走廊（y %.0f–%.0f）内 0 件物件' % (lo, hi))
        return c
    empty = []
    y = lo
    while y + TWO_SEC <= hi:
        if not any(y <= py <= y + TWO_SEC for py in events):
            empty.append(y)
        y += 188.0
    gaps = [events[i + 1] - events[i] for i in range(len(events) - 1)]
    mean_gap = sum(gaps) / len(gaps) if gaps else 0.0
    c.rows = [('events', len(events)), ('mean_gap', round(mean_gap, 1)),
              ('max_gap', max(gaps) if gaps else 0), ('empty_2s', len(empty))]
    for y in empty[:8]:
        c.fail('G-A5|gap-2s', 'y=%.0f–%.0f（2 秒行程）内 0 个视觉事件' % (y, y + TWO_SEC))
    if mean_gap and not (400 <= mean_gap <= 600):
        c.warn('G-A5|rhythm', '事件平均间距 %.0f 不在规格 400–600 区间（%s）'
               % (mean_gap, '过密' if mean_gap < 400 else '过疏'))
    return c


def check_a6(plots, ts, lands, spawn):
    c = Check('G-A6', '主街贴路：y 3280–6380 每 700 单位必有 ≥1 件 ≥2 身位贴路')
    mr = ts['mainRoad']
    bad = []
    y = 3280.0
    while y < 6380.0:
        top = min(y + 700.0, 6380.0)
        # 2026-11-01 主架构师裁定：footY 是画带**顶边**，不是落点中心。用单点判"这段有没有
        # 建筑"会把跨段的街屋判成"没有"（粮行 footY=5940 画带到 6380、布庄 6060 到 6580，
        # 两栋 >=340 的街屋明明占着 6080-6380，旧判据却报"无"）。改为画带与窗口相交。
        ok = any(min(p['footY'] + p['height'], top) - max(p['footY'], y) > 0
                 and p['height'] >= L1
                 and abs(p['footX'] - road_x(p['footY'], ts)) <= float(main_road_half(p['footY'], ts)) + 500 for p in plots)
        if not ok:
            bad.append(y)
        y += 700.0
    for y in bad:
        c.fail('G-A6|street-gap', '主街 y %.0f–%.0f 无 ≥340 贴路建筑（间隔 >700）'
               % (y, min(y + 700, 6380)))
    return c


def check_b1(plots, ts, lands):
    c = Check('G-B1', '主街 D/H 比：500 / 沿街物件均高 ∈ [1.0, 1.5]（>2.0 即荒野）')
    mr = ts['mainRoad']
    # 口径与规格 2.2 一致：镇内所有沿街物件（含摊棚杂物）都算"两侧高度"，
    # 只取 ≥340 会把矮摊算没了，D/H 会系统性偏小（旧算法实盘 1.01，等于自欺）。
    edge = [p for p in plots if 3280 <= p['footY'] <= 6380
            and abs(p['footX'] - road_x(p['footY'], ts)) <= float(main_road_half(p['footY'], ts)) + 500]
    if not edge:
        c.do_skip('主街段（3280–6380）两侧 500 内 0 件物件')
        return c
    mean_h = sum(p['height'] for p in edge) / float(len(edge))
    dh = (sum(float(main_road_half(p['footY'], ts)) * 2 for p in edge) / len(edge)) / mean_h
    c.rows = [('n', len(edge)), ('mean_h', round(mean_h, 1)), ('dh', round(dh, 3))]
    if dh > 2.0:
        c.fail('G-B1|dh', '主街 D/H=%.2f > 2.0（一眼望穿的荒野感；规格 2.2）' % dh)
    elif not (1.0 <= dh <= 1.5):
        c.warn('G-B1|dh', '主街 D/H=%.2f 不在宜人区间 [1.0, 1.5]（%s）'
               % (dh, '过窄压抑' if dh < 1.0 else '偏空旷'))
    return c


def check_b2(plots, ts, lands):
    c = Check('G-B2', '横巷 D/H：half ≤100 的巷两侧建筑高 ⇒ D/H ≤0.67')
    alleys = [r for r in (ts.get('sideRoads') or []) if r.get('half', 999) <= 100]
    if not alleys:
        c.do_skip('terrainShape.sideRoads 无 half ≤100 的横巷条目（规格 2.2 的四条横巷尚未落地）')
        return c
    for i, r in enumerate(alleys):
        col = [p for p in plots if p['height'] >= 300
               and abs(p['footX'] - (r['baseX'] + r.get('amp', 0))) <= r['half'] + 200]
        if not col:
            c.do_skip('第 %d 条横巷两侧 200 内无 ≥300 建筑' % (i + 1))
            continue
        mean_h = sum(p['height'] for p in col) / float(len(col))
        dh = (r['half'] * 2) / mean_h
        if dh > 0.67:
            c.fail('G-B2|alley%d' % (i + 1), '横巷%d D/H=%.2f > 0.67（缺围合感）' % (i + 1, dh))
    return c


def check_b3(plots, ts, lands):
    c = Check('G-B3', '田埂路：half 60–80 的农路存在（农田带内部通路）')
    tillage = [r for r in (ts.get('sideRoads') or []) if 60 <= r.get('half', 0) <= 80]
    if not tillage:
        c.do_skip('terrainShape 无 half 60–80 的田埂路条目（一期农田无实体，规格 2.2 三期为田埂）')
        return c
    c.warn('G-B3|tillage', '田埂路 %d 条已进 terrainShape，但农田内部实体件尚未落地' % len(tillage))
    return c


def check_c1(plots, ts, lands):
    c = Check('G-C1', '高度档覆盖：L1/L2/L3 各 ≥1 件；L2+L3 主力层 ≥10 件')
    tiers = Counter(tier_of(p['height']) for p in plots)
    for t, name in ((1, 'L1'), (2, 'L2'), (3, 'L3')):
        if tiers.get(t, 0) == 0:
            c.fail('G-C1|tier%d' % t, '高度档 %s 一件都没有（规格 2.6 阶梯断档）' % name)
    main_layer = tiers.get(2, 0) + tiers.get(3, 0)
    if main_layer < 10:
        c.warn('G-C1|main-layer', 'L2+L3 主力层 %d 件 < 10（镇子感主力层不足，规格 2.7）' % main_layer)
    return c


def check_c2(plots, ts, lands):
    c = Check('G-C2', 'L3 数量封顶：镇级大件 ≤6 件')
    zs = [p for p in plots if tier_of(p['height']) == 3]
    if len(zs) > 6:
        c.fail('G-C2|l3-count', 'L3 档 %d 件 > 6（天际线会被拉平，规格 2.6）' % len(zs))
    return c


def check_c3(plots, ts, lands):
    c = Check('G-C3', 'L4 分区封顶：每区 ≤2 件，且只在区 1/3/4（水口/祠堂后/河湾）')
    zs = zone_index(plots, ts)
    for z in sorted(ZONE_NAMES):
        four = [p for p in zs[z] if p['height'] >= L4]
        if len(four) > 2:
            c.fail('G-C3|z%d-l4' % z, '区%d %s 有 %d 件 L4（≥800），上限 2'
                   % (z, ZONE_NAMES[z], len(four)))
        if four and z not in L4_ALLOWED_ZONES:
            c.fail('G-C3|z%d-l4zone' % z, '区%d %s 出现 L4 大件 %s（只允许区 1/3/4）'
                   % (z, ZONE_NAMES[z], '/'.join(p['id'] for p in four[:3])))
    return c


def check_c4(plots, ts, lands):
    c = Check('G-C4', '天际线三级同框：广场 1920×1080 视窗内 ≥3 个不同高度档')
    pl = ts['plaza']
    tiers = Counter()
    for p in plots:
        if abs(p['footX'] - pl['x']) <= SCREEN_W / 2.0 and abs(p['footY'] - pl['y']) <= SCREEN_H / 2.0:
            tiers[tier_of(p['height'])] += 1
    c.rows = [('tiers', sorted(tiers)), ('n', sum(tiers.values()))]
    if len(tiers) < 3:
        c.fail('G-C4|plaza-tiers', '广场视窗只有 %d 个高度档（%s），目标 ≥3'
               % (len(tiers), '/'.join('L%d' % t for t in sorted(tiers)) or '无'))
    return c


def check_c5(plots, ts, lands):
    c = Check('G-C5', '玩具感红线：building/stall 中位高 ≥2 身位（340）')
    hs = sorted(p['height'] for p in plots if p.get('kind') in ('building', 'stall'))
    if not hs:
        c.do_skip('无 building/stall 类地块')
        return c
    med = hs[len(hs) // 2]
    if med < L1:
        c.warn('G-C5|median-height', 'building/stall 中位高 %d < 340（人跟屋等高，玩具感）' % med)
    return c


def check_d1(plots, ts, lands, spawn):
    c = Check('G-D1', '每屏一锚：沿主路相邻 ≥3 身位大件间距 ≤1200；全域最近邻 ≤1200')
    mr = ts['mainRoad']
    chain = sorted([p for p in plots if p['height'] >= ANCHOR
                    and abs(p['footX'] - road_x(p['footY'], ts)) <= mr['half'] + 1000],
                   key=lambda p: p['footY'])
    gaps = []
    for i in range(len(chain) - 1):
        gap = chain[i + 1]['footY'] - chain[i]['footY']
        gaps.append((chain[i]['id'], chain[i + 1]['id'], gap))
        if gap > 1200:
            c.fail('G-D1|chain-gap', '沿主路 %s → %s 间距 %.0f > 1200（走两屏没锚）'
                   % (chain[i]['id'], chain[i + 1]['id'], gap))
    c.rows = [('chain', len(chain)), ('max_gap', max([g[2] for g in gaps]) if gaps else 0)]
    tall = [p for p in plots if p['height'] >= ANCHOR]
    if len(tall) >= 2:
        worst = (0.0, None)
        for a in tall:
            d = min(math.hypot(a['footX'] - b['footX'], a['footY'] - b['footY'])
                    for b in tall if b is not a)
            if d > worst[0]:
                worst = (d, a['id'])
        c.rows.append(('worst_nn', round(worst[0], 1), worst[1]))
        if worst[0] > 1200:
            c.warn('G-D1|global-nn', '全域最近邻最大 %.0f > 1200（%s 附近缺大件）'
                   % (worst[0], worst[1]))
    else:
        c.do_skip('≥510 的物件不足 2 件，最近邻判据无从计算')
    return c


def check_d2(plots, ts, lands, spawn):
    c = Check('G-D2', '回望走廊：牌坊向南 1200×500 内 0 件 ≥3 身位（离镇视野通透）')
    pf = find_plot(plots, 'paifang')
    if pf is None:
        c.do_skip('building-plots.json 里找不到牌坊（id 含 paifang）')
        return c
    blockers = [p for p in plots if p is not pf and p['height'] >= ANCHOR
                and abs(p['footX'] - pf['footX']) <= 250
                and pf['footY'] < p['footY'] <= pf['footY'] + 1200]
    for p in blockers:
        c.fail('G-D2|corridor', '回望走廊内有 %s（h=%d，y=%.0f）遮挡离镇视线'
               % (p['id'], p['height'], p['footY']))
    return c


def check_d3(plots, ts, lands, spawn):
    c = Check('G-D3', '祠堂–戏台对望：两点连线 ±250 内无第三件 ≥3 身位')
    hall = find_plot(plots, 'ancestral')
    stage = find_plot(plots, 'opera')
    if hall is None or stage is None:
        c.do_skip('祠堂/戏台未同时落地（ancestral=%s, opera=%s）'
                  % (hall is not None, stage is not None))
        return c
    ax, ay, bx, by = hall['footX'], hall['footY'], stage['footX'], stage['footY']
    seg = math.hypot(bx - ax, by - ay)
    blockers = []
    for p in plots:
        if p is hall or p is stage or p['height'] < ANCHOR:
            continue
        # 点到线段距离
        t = ((p['footX'] - ax) * (bx - ax) + (p['footY'] - ay) * (by - ay)) / (seg * seg) if seg else 0
        t = max(0.0, min(1.0, t))
        d = math.hypot(p['footX'] - (ax + t * (bx - ax)), p['footY'] - (ay + t * (by - ay)))
        if d <= 250:
            blockers.append((p, d))
    for p, d in blockers:
        c.fail('G-D3|cross-line', '%s（h=%d）离祠堂–戏台连线 %.0f ≤250，挡住对望'
               % (p['id'], p['height'], d))
    return c


# ---------------------------------------------------------------- 汇总

def run_checks(plots, mp):
    ts = mp['terrainShape']
    lands = mp.get('landmarks') or []
    spawn = mp.get('spawn') or {'x': 4096, 'y': 7900}
    return [
        check_a1(plots, ts, lands),
        check_a2(plots, ts, lands),
        check_a3(plots, ts, lands, spawn),
        check_a4(plots, ts, lands, spawn),
        check_a5(plots, ts, lands, spawn),
        check_a6(plots, ts, lands, spawn),
        check_b1(plots, ts, lands),
        check_b2(plots, ts, lands),
        check_b3(plots, ts, lands),
        check_c1(plots, ts, lands),
        check_c2(plots, ts, lands),
        check_c3(plots, ts, lands),
        check_c4(plots, ts, lands),
        check_c5(plots, ts, lands),
        check_d1(plots, ts, lands, spawn),
        check_d2(plots, ts, lands, spawn),
        check_d3(plots, ts, lands, spawn),
    ]


def arbitrate(checks, baseline):
    """台账仲裁 + 棘轮：red 存量降 WARN 放行；warn 存量升红即 FAIL（不许放宽）；armed=False 不参与。"""
    fails, warns, notes, present = [], [], [], set()
    for c in checks:
        for v in c.violations:
            present.add(v['key'])
            entry = baseline.get(v['key'])
            text = '[%s] %s' % (v['id'], v['text'])
            if entry and entry.get('armed', True):
                if v['level'] == 'WARN':
                    warns.append('存量 WARN（台账 %s：%s）%s' % (entry['level'], entry['note'], text))
                elif entry['level'] == 'red':
                    warns.append('存量判红（台账 red：%s）%s' % (entry['note'], text))
                else:
                    fails.append('台账不许放宽判据（只登记过 WARN 级，现已判红）%s | 台账注：%s'
                                 % (text, entry['note']))
            elif entry:
                (fails if v['level'] == 'FAIL' else warns).append(
                    text + '（台账为待命登记，请核实后置 armed=True）')
            else:
                (fails if v['level'] == 'FAIL' else warns).append(
                    text + ('' if v['level'] == 'FAIL' else '（未登记存量，请修正布局或在 BASELINE 登记）'))
    # 棘轮：armed 且本轮被评估、却不再违规 ⇒ 可销账（SKIP 的判据不棘轮，否则前置缺失会永久假红）
    for key, entry in sorted(baseline.items()):
        if not entry.get('armed', True) or key in present:
            continue
        if any(c.id == key.split('|')[0] for c in checks if c.skip is None):
            fails.append('台账可销账：%s 当前已不违规（原登记：%s），请从 BASELINE 删除'
                         % (key, entry['note']))
        else:
            notes.append('台账未评估（判据 SKIP）：%s — %s' % (key, entry['note']))
    return fails, warns, notes


def load(path_plots, path_map):
    plots_doc = json.load(open(path_plots, encoding='utf-8'))
    plots = plots_doc['plots'] if isinstance(plots_doc, dict) else plots_doc
    mp = json.load(open(path_map, encoding='utf-8'))
    return plots, mp


def zone_rows(plots, ts, lands):
    zs = zone_index(plots, ts)
    return [(z, ZONE_NAMES[z], len(zs[z]),
             sum(1 for p in zs[z] if p['height'] >= L1),
             sum(1 for p in zs[z] if p['height'] >= ANCHOR),
             sum(1 for l in lands if zone_of(l['x'], l['y'], ts) == z))
            for z in sorted(ZONE_NAMES)]


OUT = []


def say(s):
    OUT.append(s)
    print(s)


def emit(checks, fails, warns, notes, report=False, plots=None, mp=None):
    """判定与报告共用的输出面：report=True 时先打三张全表，其余两路一模一样。"""
    out = say
    if report:
        ts = mp['terrainShape']
        tiers = Counter(tier_of(p['height']) for p in plots)
        out('=== 城镇布局全表（--report；只打印不判定，rc 恒 0） ===')
        out('plots=%d  landmarks=%d  townRadius=%.0f  spawn=(%.0f, %.0f)'
            % (len(plots), len(mp.get('landmarks') or []), ts['townRadius'],
               mp['spawn']['x'], mp['spawn']['y']))
        out('\n-- 一、九区密度表（件数 / ≥2 身位 / ≥3 身位 / 区内地标）--')
        for z, name, n, t1, t3, nlm in zone_rows(plots, ts, mp.get('landmarks') or []):
            out('  区%d %-12s 件数 %3d ｜ ≥340 %2d ｜ ≥510 %2d ｜ 地标 %d' % (z, name, n, t1, t3, nlm))
        out('\n-- 二、高度档分布（L0<340 L1 340-509 L2 510-594 L3 595-799 L4 ≥800）--')
        for t in range(5):
            out('  L%d  %3d 件' % (t, tiers.get(t, 0)))
        for t in (3, 4):
            ids = [(p['id'], p['height']) for p in plots if tier_of(p['height']) == t]
            if ids:
                out('  L%d 明细：%s' % (t, '，'.join('%s(%d)' % x for x in ids)))
        out('\n-- 三、锚点间距链与关键量（沿主路，≥510）--')
        for c in checks:
            if c.id in ('G-D1', 'G-A5', 'G-B1', 'G-C4'):
                for row in c.rows:
                    out('  %s %s' % (c.id, row))
        out('\n-- 四、判据现状 --')
    for c in checks:
        if c.skip:
            out('%-6s SKIP（%s）' % (c.id, c.skip))
        else:
            out('%-6s %-52s FAIL %d / WARN %d'
                % (c.id, c.title,
                   sum(1 for v in c.violations if v['level'] == 'FAIL'),
                   sum(1 for v in c.violations if v['level'] == 'WARN')))
    if report:
        out('\n-- 五、台账 --')
        for key, entry in sorted(BASELINE.items()):
            out('  %-22s level=%-4s armed=%-5s %s'
                % (key, entry['level'], entry.get('armed', True), entry['note']))
    out('')
    for w in warns:
        out('WARN: ' + w)
    for f in fails:
        out('FAIL: ' + f)
    for n in notes:
        out('NOTE: ' + n)
    out('\nSUMMARY: 判据 %d 条（SKIP %d），FAIL %d 项 / WARN %d 项'
        % (len(checks), sum(1 for c in checks if c.skip), len(fails), len(warns)))
    if report:
        out('RESULT: REPORT(只打印不判定)')
        return 0
    ids = sorted({f.split('[', 1)[1].split(']', 1)[0] for f in fails if f.startswith('[')})
    if ids:
        out('FAILED: ' + ', '.join(ids))
    out('RESULT: ' + ('FAIL' if fails else 'PASS'))
    out('（数值放行 ≠ 人眼认可：观感由斐哥看图拍板，见方案 §4）')
    return 1 if fails else 0


def main(plots_path, map_path, report=False, baseline=None):
    plots, mp = load(plots_path, map_path)
    if mp.get('props') or mp.get('walkable'):
        print('NOTE: town-map.json 的 props/walkable 非空，但本门禁按规格 1.6.1 **不读**这两个字段')
    checks = run_checks(plots, mp)
    fails, warns, notes = arbitrate(checks, baseline or BASELINE)
    if report:
        return emit(checks, fails, warns, notes, True, plots, mp)
    say('=== 城镇布局量化门禁（C-02） ===')
    say('数据源：%s ＋ %s' % (os.path.relpath(plots_path, ROOT), os.path.relpath(map_path, ROOT)))
    say('plots=%d  区域=%d  身高位=%.0f  跑速=%.0f/s\n' % (len(plots), len(ZONE_NAMES), UNIT, RUN_SPEED))
    rc = emit(checks, fails, warns, notes)
    os.makedirs(os.path.dirname(LOG), exist_ok=True)
    with open(LOG, 'w', encoding='utf-8', newline='\n') as fh:
        fh.write(chr(10).join(OUT) + chr(10))
    print('（全表已写：%s）' % os.path.relpath(LOG, ROOT))
    return rc


def fixture_map():
    """与实盘同值的 terrainShape（自足，不读真配置），保证自检与实盘同口径。"""
    wave_r = [{'amp': 170, 'period': 980, 'phase': 0.0}, {'amp': 80, 'period': 430, 'phase': 1.1}]
    return {
        'spawn': {'x': 4096.0, 'y': 7900.0},
        'landmarks': [{'id': i, 'x': x, 'y': y} for i, x, y in (
            ('south-gate', 4096, 6300), ('town-center', 4096, 4700), ('market', 3100, 4950),
            ('north-street', 4096, 3600), ('river-north', 4236, 2462), ('foothill', 4096, 2100),
            ('back-mountain', 3760, 260), ('west-street', 2400, 4200), ('farmland', 2400, 5900))],
        'terrainShape': {
            'river': {'baseY': 2700.0, 'wave': wave_r, 'waterHalf': 125, 'bankHalf': 185},
            'mainRoad': {'baseX': 4096.0, 'amp': 140, 'periodY': 1500, 'half': 250.0},
            'sideRoads': [{'baseX': 2400.0, 'amp': 40, 'periodY': 900, 'half': 150.0, 'phase': 0.0},
                          {'baseX': 5800.0, 'amp': 40, 'periodY': 900, 'half': 150.0, 'phase': 1.0}],
            'plaza': {'x': 4096.0, 'y': 4700.0, 'r': 620.0},
            'market': {'x': 3100.0, 'y': 4950.0, 'r': 340.0},
            'townRadius': 1600.0, 'foothill': {'y0': 1600, 'y1': 2500},
            'farmland': [{'y0': y0, 'y1': y1, 'x0': 0, 'x1': 3200} for y0, y1 in ((2900, 4250), (5500, 6600))] +
                        [{'y0': y0, 'y1': y1, 'x0': 5000, 'x1': 8192} for y0, y1 in ((2900, 4250), (5500, 6600))],
        },
    }


def mk_plot(pid, x, y, h, kind='prop'):
    return {'id': pid, 'kind': kind, 'name': pid, 'footX': float(x), 'footY': float(y),
            'width': float(h), 'height': float(h), 'art': 'x', 'radius': 40.0,
            'collisionDepth': 10.0, 'collisionTop': 0.0}


def good_plots(ts):
    """合规夹具：九区各有锚点与大件、覆盖域无空窗、主街每 700 有贴路建筑、间距链 ≤1200。"""
    plots = []
    for y in range(400, 7601, 600):
        for x in range(1200, 7201, 600):
            if abs(x - road_x(y, ts)) > 700:     # 主街走廊留给街铺，免得 200 高的杂物把 D/H 拉爆
                plots.append(mk_plot('grid-%d-%d' % (x, y), x, y, 200))
    for i, y in enumerate(range(3300, 7901, 600)):      # 贴路 L1，撑起 A6 与 D/H
        plots.append(mk_plot('street-%d' % i, road_x(y, ts) + (-420 if i % 2 == 0 else 420),
                             y, 400, 'building'))
    anchors = [                                   # (区, x, 地面y, 高)：每区一条 ≥510 + 一条 L1
        (1, 3800, 1200, 620), (1, 4000, 900, 400), (2, 4200, 2000, 560), (2, 4400, 2300, 400),
        (3, 4236, river_y(4236, ts), 850), (3, 4600, river_y(4600, ts), 400),
        (4, 4096, 3060, 560),  # 合成北街过渡件：补足按地面归区后的件数及河带到北街的锚点链
        (4, 4096, 3600, 560), (4, 3800, 3800, 400), (5, 4096, 4600, 560), (5, 3600, 4500, 400),
        (4, 3300, 3900, 200),     # 2026-10-31 road_x 去 2π 后，网格件 grid-4800-4000 移出区 4，补一件保区 4 ≥5（只补夹具，判据未动）
        (5, 4096, 4800, 200), (5, 4400, 4900, 200),     # 广场视窗的第三档（L0）
        (6, 3100, 4950, 560), (6, 2900, 4800, 400), (7, 3800, 5600, 620), (7, 4200, 5800, 400),
        (7, 4400, 6000, 400), (8, 5000, 6700, 560), (8, 4800, 6800, 400),
        (9, 2400, 5900, 560), (9, 2600, 5700, 400),
    ]
    for z, x, y, h in anchors:
        plots.append(mk_plot('z%d-%d-%d' % (z, x, y), x, y - h, h, 'building' if h >= 340 else 'stall'))
    return plots


def run_case(name, plots, mp, baseline, want_rc, want_kw):
    checks = run_checks(plots, mp)
    fails, warns, _notes = arbitrate(checks, baseline)
    rc = 1 if fails else 0
    text = ' '.join(fails + warns)
    if rc != want_rc:
        return False, '期望 rc=%d 实得 rc=%d；输出：%s' % (want_rc, rc, (text or '(无违规)')[:400])
    if want_kw and want_kw not in text:
        return False, '期望命中关键字 %r；实际输出：%s' % (want_kw, text[:400])
    return True, name


def selftest():
    """≥6 条合成用例。夹具全部在内存构造，落盘的只有 .batch/tmp/town-layout-selftest/ 的副本。"""
    mp = fixture_map()
    ts = mp['terrainShape']
    D = copy.deepcopy
    good = good_plots(ts)
    few = [mk_plot('lonely-1', 3800, 1200, 620, 'building'),
           mk_plot('lonely-2', 4096, 3600, 560, 'building')]                 # 1 空屏
    l3 = D(good) + [mk_plot('l3-%d' % i, 4300 + i * 60, 4700 + i * 60, 650, 'building')
                    for i in range(7)]                                    # 2 L3 超 6
    corridor = D(good) + [mk_plot('plot-paifang', 4096, 6380, 660, 'building'),
                          mk_plot('blocker-hall', 4200, 6800, 600, 'building')]  # 3 回望走廊
    chain = [p for p in D(good) if p['height'] < ANCHOR] + [
        mk_plot('chain-a', 4096, 3500, 560, 'building'),
        mk_plot('chain-b', 4096, 4800, 560, 'building')]                  # 4 锚点间距 1300
    dh = D(good) + [mk_plot('dh-%d' % i, road_x(3900 + i * 60, ts) + 300, 3900 + i * 60,
                            120, 'stall') for i in range(40)]             # 6 台账升级
    armed_warn = {'G-B1|dh': {'level': 'warn', 'armed': True, 'note': 'selftest：曾只登记 WARN 级'}}
    cases = [('1_空屏必红_G-A3', few, BASELINE, 1, 'G-A3'),
             ('2_L3超6必红_G-C2', l3, BASELINE, 1, 'G-C2'),
             ('3_回望走廊塞大件必红_G-D2', corridor, BASELINE, 1, 'G-D2'),
             ('4_锚点间距1300必红_G-D1', chain, BASELINE, 1, 'G-D1'),
             ('5_合规配置必绿', D(good), {}, 0, None),     # 空台账：合规配置本就不该有违规项
             ('6_台账升级必红_G-B1', dh, armed_warn, 1, '不许放宽')]
    bad = []
    for name, plots, base, want_rc, kw in cases:
        ok, msg = run_case(name, plots, mp, base, want_rc, kw)
        if ok:
            print('  [PASS] %-28s rc=%d 断言=%s' % (name, want_rc, kw or 'PASS'))
        else:
            bad.append(name)
            print('  [FAIL] %s: %s' % (name, msg))
    # 7 门禁 × 画面 对齐（2026-10-31 主架构师批准追加，只增不改既有用例/判据）：
    #   本门禁的 road_x 与 world-mask.png 实测主街中心偏差 ≤ 8，并反证 2π 公式必被抓。
    #   测量逻辑复用 check-town-overlap.py 的 mask_alignment（同一份实现，避免两份测量口径）。
    # 8/9 G-A6 画带相交回归（2026-11-01 主架构师裁定）：
    #   8 启点落在窗口外、画带跨进窗口 -> 必须算"有"（旧判据在这里会误报）
    #   9 同一位置压到 300 高（<L1 340）-> 必须照旧报红，证明判据没被放宽成"永不报"
    _span_ok = [mk_plot('grain-span', road_x(6100, ts) - 500, 5940, 440, 'building')]
    _span_bad = [mk_plot('grain-span', road_x(6100, ts) - 500, 5940, 300, 'building')]
    _hit_ok = [v for v in check_a6(_span_ok, ts, [], {'x': 4096, 'y': 7900}).violations
               if '6080' in v['text']]
    _hit_bad = [v for v in check_a6(_span_bad, ts, [], {'x': 4096, 'y': 7900}).violations
                if '6080' in v['text']]
    if not _hit_ok:
        print('  [PASS] %-28s footY=5940 画带 5940-6380 跨窗 -> 末窗不报' % '8_G-A6跨窗画带算有')
    else:
        bad.append('8_G-A6跨窗画带算有')
        print('  [FAIL] 8_G-A6跨窗画带算有: 仍报 %s' % _hit_ok[0]['text'])
    if _hit_bad:
        print('  [PASS] %-28s 压到 300 高 -> 末窗照旧报红' % '9_G-A6矮件仍报红')
    else:
        bad.append('9_G-A6矮件仍报红')
        print('  [FAIL] 9_G-A6矮件仍报红: 判据被放宽成不报')

    cases_total = len(cases) + 3
    name7 = '7_road_x与mask对齐'
    try:
        import importlib.util as _ilu
        _spec = _ilu.spec_from_file_location('_overlap', os.path.join(ROOT, 'Tools', 'check-town-overlap.py'))
        _ov = _ilu.module_from_spec(_spec)
        _spec.loader.exec_module(_ov)
        with open(os.path.join(CFG, 'town-map.json'), encoding='utf-8') as _fh:
            _real_ts = json.load(_fh)['terrainShape']
        _n, _d, _ = _ov.mask_alignment(road_x, _real_ts)

        def _road_x_2pi(y, t):
            mr_ = t['mainRoad']
            return float(mr_['baseX']) + mr_['amp'] * math.sin(2 * math.pi * y / mr_['periodY'])
        try:
            _n2, _d2, _ = _ov.mask_alignment(_road_x_2pi, _real_ts)
            _caught = _d2 > _ov.MASK_TOL
        except ValueError:
            _d2, _caught = float('nan'), True
        if _d <= _ov.MASK_TOL and _caught:
            print('  [PASS] %-28s 可信行 %d，max|Δ|=%.1f ≤ %.0f；注入 2π → %.1f 被抓'
                  % (name7, _n, _d, _ov.MASK_TOL, _d2))
        else:
            bad.append(name7)
            print('  [FAIL] %s: road_x max|Δ|=%.1f（限 %.0f），2π 反证被抓=%s'
                  % (name7, _d, _ov.MASK_TOL, _caught))
    except Exception as _e:              # 缺 mask/缺依赖/读失败：显式红，不许跳过
        bad.append(name7)
        print('  [FAIL] %s 无法执行（%s: %s）——缺这道保证即判红' % (name7, type(_e).__name__, _e))
    tmp = os.environ.get('TOWN_LAYOUT_SELFTEST_DIR') or os.path.join(ROOT, '.batch', 'tmp', 'town-layout-selftest')   # 仅改变证据位置，真配置零改动
    os.makedirs(tmp, exist_ok=True)
    for tag, plots in (('good', good), ('few', few)):
        with open(os.path.join(tmp, '%s-plots.json' % tag), 'w', encoding='utf-8', newline='\n') as fh:
            json.dump({'plots': plots}, fh, ensure_ascii=False, indent=1)
    with open(os.path.join(tmp, 'fixture-map.json'), 'w', encoding='utf-8', newline='\n') as fh:
        json.dump(mp, fh, ensure_ascii=False, indent=1)
    print('  [INFO] 夹具副本：%s（真配置零改动）' % os.path.relpath(tmp, ROOT))
    if bad:
        print('RESULT: FAIL(selftest %d/%d 条不点火)' % (len(bad), cases_total))
        return 1
    print('SELFTEST: %d/%d 条用例全点火（空屏/L3 封顶/回望走廊/锚点间距/合规必绿/台账升级/G-A6 跨窗算有+矮件仍报/mask 对齐）'
          % (cases_total, cases_total))
    print('RESULT: PASS(selftest)')
    return 0


def parse_args(argv):
    report = '--report' in argv
    selft = '--selftest' in argv
    plots = os.path.join(CFG, 'building-plots.json')
    mp = os.path.join(CFG, 'town-map.json')
    for i, a in enumerate(argv):
        if a == '--plots' and i + 1 < len(argv):
            plots = argv[i + 1]
        if a == '--map' and i + 1 < len(argv):
            mp = argv[i + 1]
    return plots, mp, report, selft


if __name__ == '__main__':
    _plots, _map, _report, _selft = parse_args(sys.argv[1:])
    sys.exit(selftest() if _selft else main(_plots, _map, report=_report))
