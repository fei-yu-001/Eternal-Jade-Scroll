#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""MAP-15 门禁：建筑体量比例与占位复用棘轮门禁。

判据（架构师裁决）：
  1. 玩家基准 TravelerHeight = 170.0（世界单位）。
  2. visual_h = art_bbox_h% × plot.height，其中 art_bbox_h% 为立绘 alpha > 128 内容包围盒高占画布高之比。
  3. 比值 h_over_player = visual_h / 170.0。
  4. 用户2026-11-05备忘录：二层临街建筑3.5~4.5身位；单层/院落/牌坊沿用1.5~3.5。
     分类按真实素材，声明不得把二层改为单层；170世界单位对应1.75米。
     prop / stall / tree 不参与判定（矮物与树本来就矮，仅作登记）。
  5. 占位复用判据（两个维度，任一维命中即判复用）：
     a) 路径维度：kind == 'building' 的地块，其立绘 art 路径不得与其他 building 地块重复；
     b) 内容维度（MAP-26 补）：kind == 'building' 的立绘文件 md5 不得与其他 building 地块的立绘 md5 相同
        —— 路径不同但 md5 相同，等同于同一张图复制改名，视为同一件美术（堵"复制改名绕过路径比对"的漏洞）。
        md5 相同即报 FAIL，不进任何豁免台账；立绘文件不存在时给明确 FAIL 而不是崩栈。

棘轮三分支设计：
  - 不在台账 且 比值不在允许区间 -> FAIL（退出码 1，新增失衡）；
  - 在台账 且 比值仍不在允许区间 -> WARN（退出码 0，存量失衡保持，不阻断流水线）；
  - 在台账 且 比值已进允许区间   -> FAIL（退出码 1，台账可销账，强制人工清理台账防止掩盖后续回归）。
  - 占位复用采用同款棘轮（存量复用进豁免台账走 WARN，新增复用或新增第三处复用 FAIL，复用消除后未销账 FAIL）。
  - 台账每一条必须带 reason（理由）与 retire（销账条件），WARN 逐条打印，不许静默。
  - 内容指纹（md5）重复走独立硬判据：永远 FAIL，不认台账。
  - 美术缺失 -> FAIL。

自检入口：
  python Tools/PropChecks/check-building-scale.py --selftest
"""

import argparse
import hashlib
import io
import json
import os
import shutil
import sys
import tempfile
import math
import warnings

warnings.filterwarnings('ignore')

# UTF-8 控制台与管道守卫
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
if hasattr(sys.stderr, 'reconfigure'):
    sys.stderr.reconfigure(encoding='utf-8', errors='replace')

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
PROJ = os.path.join(REPO, 'GameClient', 'TwelveJade')
PLOTS_JSON = os.path.join(PROJ, 'Assets', 'Resources', 'Config', 'building-plots.json')
ART_ROOT = os.path.join(PROJ, 'Assets', 'Resources')

TRAVELER_HEIGHT = 170.0
MIN_BUILDING_SCALE = 1.5
MAX_BUILDING_SCALE = 3.5
TWO_STOREY_MIN = 3.5
TWO_STOREY_MAX = 4.5
# 素材的实际楼层，不按可随意改变的plot名称判断。
BUILDING_CLASSES = {
    "Art/Buildings/town-gate-south-wide": "gate-landmark",
    "Art/Props/teahouse-v2": "two-storey",
    "Art/Props/stone-house-v2": "two-storey",
    "Art/Props/shop-front": "single-storey",
    "Art/Props/shop-front-v2": "single-storey",
    "Art/Props/residence": "courtyard",
    "Art/Props/temple-gate": "gateway",
    "Art/Props/paifang": "gateway",
}

def building_scale_range(art):
    if BUILDING_CLASSES.get(art) == "gate-landmark":
        return None  # 城门按真实门洞单列验收，普通二层区间不变。
    return (TWO_STOREY_MIN, TWO_STOREY_MAX) if BUILDING_CLASSES.get(art) == "two-storey" else (MIN_BUILDING_SCALE, MAX_BUILDING_SCALE)
ALPHA_THRESHOLD = 128

def gate_landmark_problems(plot, assets_root):
    """Approved G01 alone: original aspect, fixed real foot, full per-row alpha piers."""
    issues = []
    if plot.get('id') != 'plot-town-gate-south' or plot.get('buildingClass') != 'gate-landmark':
        issues.append('城门地标身份错误')
    with Image.open(os.path.join(assets_root, plot['art'] + '.png')) as image:
        alpha = image.convert('RGBA').getchannel('A')
    width, height = alpha.size
    bounds = alpha.point(lambda v: 255 if v > ALPHA_THRESHOLD else 0).getbbox()
    sx, sy = plot['width'] / width, plot['height'] / height
    if abs(sx - sy) > .000001:
        issues.append('城门原比例被改变')
    fraction = plot.get('footprint', {}).get('bottomFraction', 0)
    if not .10 <= fraction <= .20:
        return issues + ['城门碰撞底脚必须覆盖完整10%~20%带']
    y0, y1 = math.floor(bounds[3] - (bounds[3] - bounds[1]) * fraction), bounds[3]
    if plot.get('footprint', {}).get('sourceBand') != [y0, y1]:
        issues.append('城门源图底脚带不一致')
    if abs(plot['footY'] + plot.get('groundFootOffset', 0) - 6953.2474) > .001 or abs(plot['footX'] - 3960) > .001:
        issues.append('城门既有真实落脚基准被改变')
    rects = plot.get('collisionRects', [])
    continuous_left, continuous_right = -float('inf'), float('inf')
    exact_rows = True
    for y in range(y0, y1):
        left, right = width // 2, width // 2 + 1
        while left > 0 and alpha.getpixel((left - 1, y)) == 0: left -= 1
        while right < width and alpha.getpixel((right, y)) == 0: right += 1
        solid = [x for x in range(width) if alpha.getpixel((x, y)) > ALPHA_THRESHOLD]
        row_low, row_high = round(y * sy, 4), round((y + 1) * sy, 4)
        active = sorted([r for r in rects if r['y0'] <= (row_low + row_high) / 2 < r['y1']], key=lambda r: r['x0'])
        expected = [(round((solid[0] - width / 2) * sx, 4), round((left - width / 2) * sx, 4)),
                    (round((right - width / 2) * sx, 4), round((solid[-1] + 1 - width / 2) * sx, 4))]
        if len(active) != 2:
            exact_rows = False; continue
        for rect, (x0, x1) in zip(active, expected):
            exact_rows &= abs(rect['x0'] - x0) < .001 and abs(rect['x1'] - x1) < .001 and rect['y0'] <= row_low + .001 and rect['y1'] >= row_high - .001
        continuous_left = max(continuous_left, active[0]['x1'])
        continuous_right = min(continuous_right, active[1]['x0'])
    if not exact_rows: issues.append('城门双墩未逐行贴合真实透明门洞')
    if continuous_right - continuous_left < 620: issues.append('城门离散后连续门洞不足620world')
    if any(r['y0'] < round(y0 * sy, 4) - .001 or r['y1'] > round(y1 * sy, 4) + .001 for r in rects):
        issues.append('城门碰撞越出底脚带')
    return ['城门地标: ' + issue for issue in issues]


# 棘轮存量台账：MAP-15 初始冻结的 6 个存量失衡建筑地块及比值
SCALE_BASELINE = {}   # MAP-22：4 件存量失衡已随新立绘落地进区间销账（herbal 1.50/stone 1.60/blacksmith 1.80/teahouse 2.00）

# 占位复用存量豁免台账（仅针对 kind == 'building' 之间、且 art 路径相同的复用）
# 键 = art 路径；每条必须写清 tier/class/reason/retire 四样，WARN 逐条打印，不许静默。
# tier: 'permanent'（合理的同型共用，不销账） / 'placeholder'（真实占位，带销账条件）。
# 内容指纹 md5 相同（复制改名）**不入台账**，一律判 FAIL。
REUSE_BASELINE = {
    # ---- 永久豁免：A 类同型建筑共用一件通用美术，属正常做法，不是占位 ----
    'Art/Props/stone-house-v2': {
        'plots': ['plot-stone-house', 'plot-street-east-home'],
        'class': 'A',
        'tier': 'permanent',
        'reason': ('plot-stone-house(石门柴扉) 与 plot-street-east-home(东街石屋) 都是石砌底层+木构二层的'
                   '两层民居，同一建筑类型共用一件通用民居立绘属正常做法；两处相距 1616 单位不同屏'),
        'retire': '永久豁免，不销账（同型共用不是占位；若将来其中一处改用专属美术致使用数<=1，棘轮会 FAIL 要求销账）',
    },
    'Art/Props/shop-front': {
        'plots': ['shop-north-street-01', 'plot-cloth-shop'],
        'class': 'A',
        'tier': 'permanent',
        'reason': ('shop-north-street-01(北街街铺) 与 plot-cloth-shop(布庄) 都是一开间柜台小铺+空白布幡，'
                   '空白布幡落到布庄正好，属通用铺面共用，不是占位'),
        'retire': '永久豁免，不销账（同型共用不是占位；若将来其中一处改用专属美术致使用数<=1，棘轮会 FAIL 要求销账）',
    },
    # ---- 带销账条件登记：真实占位，等生图通道恢复后重画专属美术 ----
    'Art/Props/residence': {
        'plots': ['plot-grain-shop', 'plot-herbal-garden'],
        'class': 'B',
        'tier': 'placeholder',
        'reason': ('plot-grain-shop(粮行) 是 A 类本命留用；plot-herbal-garden(梯田药圃) 是 B 类占位——'
                   '该件画的是院墙+门楼+晾满干椒干菜的晒院，落到药圃上是"药圃的晾晒院"，形制是院落不是田垄'),
        'retire': '等生图通道恢复后重画专属美术，重画一件即销一条',
    },
    'Art/Props/temple-gate': {
        'plots': ['plot-temple-gate', 'plot-ancestral-hall'],
        'class': 'B',
        'tier': 'placeholder',
        'reason': ('plot-temple-gate(山门) 是 A 类本命；plot-ancestral-hall(张氏祠堂) 是 B 类占位——'
                   '祠堂与山门是同一座朱红庙门（462x640 vs 448x620，只差 3%），相隔 968 单位形成两座一模一样的大门，'
                   '祠堂缺享堂/檐廊/匾额/牌位'),
        'retire': '等生图通道恢复后重画专属美术，重画一件即销一条',
    },
    'Art/Props/shop-front-v2': {
        'plots': ['plot-blacksmith', 'plot-school', 'plot-street-east-gate', 'plot-street-east-store'],
        'class': 'C',
        'tier': 'placeholder',
        'reason': ('plot-blacksmith(守渡铁匠铺) 是 A 类——这张图本来就是铁匠铺（砧台/淬火木桶/炉膛明火/斧锤墙/石烟囱），'
                   '文件名反而是命名错误；plot-school(北街村塾) 与 plot-street-east-store(东沿杂货铺) 是 C 类——'
                   '学堂与杂货铺里摆着炉火砧台；plot-street-east-gate(东口铺面) 是 B 类——通用铺位带炉火'),
        'retire': '等生图通道恢复后重画专属美术，重画一件即销一条',
    },
    'Art/Props/teahouse-v2': {
        'plots': ['plot-teahouse', 'plot-street-east-wine', 'plot-street-locust-tea',
                  'plot-street-square-tea', 'plot-street-opera-west', 'plot-opera-stage'],
        'class': 'C',
        'tier': 'placeholder',
        'reason': ('6 个用户里只有 plot-teahouse(草亭茶棚) 勉强留用；plot-opera-stage(万年台戏台) 是 C 类——'
                   '封闭两层茶馆冒充开敞戏台（无台口/藻井/上下场门）；plot-street-east-wine(东沿临街酒楼) 与 '
                   'plot-street-opera-west(戏台西邻民居) 是 B 类——挂"茶"字幡的茶馆；plot-street-square-tea(广场北沿茶寮) '
                   '与 plot-street-locust-tea(古槐荫下茶摊) 是 D 类（本质 stall）；高度跨度 1.93x，同屏可见一大一小两座同样茶馆'),
        'retire': '等生图通道恢复后重画专属美术，重画一件即销一条',
    },
}


def get_art_bbox_h_pct(art_rel, assets_root=ART_ROOT):
    """计算立绘 alpha > 128 包围盒高占画布高百分比。若不存在返回 None。"""
    if not art_rel:
        return None
    path = os.path.join(assets_root, art_rel + '.png')
    if not os.path.exists(path):
        return None
    im = Image.open(path).convert('RGBA')
    alpha = im.getchannel('A')
    mask = alpha.point(lambda p: 255 if p > ALPHA_THRESHOLD else 0)
    bbox = mask.getbbox()
    if bbox is None:
        return 0.0
    bh = bbox[3] - bbox[1]
    return bh / float(im.size[1])


def get_art_md5(art_rel, assets_root=ART_ROOT):
    """计算立绘文件内容 md5。文件不存在或不可读返回 None（不抛异常）。"""
    if not art_rel:
        return None
    path = os.path.join(assets_root, art_rel + '.png')
    if not os.path.isfile(path):
        return None
    h = hashlib.md5()
    try:
        with open(path, 'rb') as f:
            for chunk in iter(lambda: f.read(1 << 20), b''):
                h.update(chunk)
    except (IOError, OSError):
        return None
    return h.hexdigest()


def _format_reuse_entry(art, digest, users, info):
    """把台账里的一条复用登记格式化成一行 WARN（理由 + 销账条件必须可见）。"""
    md5_short = digest[:8] if digest else '(md5 不可用)'
    cls = info.get('class', '?')
    reason = info.get('reason', '(台账未写理由)')
    retire = info.get('retire', '(台账未写销账条件)')
    if info.get('tier') == 'permanent':
        return ('永久豁免(合规同型共用): 美术 %s (md5=%s) 被 %s 共用 [%s 类]｜理由: %s｜销账: %s'
                % (art, md5_short, sorted(list(users)), cls, reason, retire))
    return ('存量占位复用: 美术 %s (md5=%s) 被 %s 共用 [%s 类]｜理由: %s｜销账条件: %s'
            % (art, md5_short, sorted(list(users)), cls, reason, retire))


def check_plots(plots, assets_root=ART_ROOT,
                scale_baseline=None, reuse_baseline=None,
                verbose=False):
    """核心判定函数。

    返回: (problems, warnings, rows)
      problems: list[str]，每条代表一个硬 FAIL
      warnings: list[str]，每条代表一个存量合规警告（不阻断退出码）
      rows: list[dict]，用于 verbose 输出的统计行
    """
    if scale_baseline is None:
        scale_baseline = SCALE_BASELINE
    if reuse_baseline is None:
        reuse_baseline = REUSE_BASELINE

    problems = []
    warns = []
    rows = []

    # 1. 逐地块比例判定
    building_plots = []
    for p in plots:
        pid = p.get('id', '<unknown>')
        kind = p.get('kind', 'prop')
        art = p.get('art', '')
        ph = float(p.get('height') or 0.0)
        pw = float(p.get('width') or 0.0)

        if kind == 'building':
            building_plots.append(p)

        art_path = os.path.join(assets_root, art + '.png') if art else ''
        if not art or not os.path.exists(art_path):
            problems.append('美术缺失: 地块 %s (kind=%s) 引用的立绘不存在: %s' % (pid, kind, art_path))
            rows.append({
                'id': pid, 'kind': kind, 'art': art,
                'plot_w': pw, 'plot_h': ph,
                'bbox_h_pct': 0.0, 'visual_h': 0.0,
                'h_over_player': 0.0, 'status': 'FAIL(美术缺失)'
            })
            continue

        bh_pct = get_art_bbox_h_pct(art, assets_root)
        visual_h = bh_pct * ph
        h_over_player = visual_h / TRAVELER_HEIGHT
        ratio = round(h_over_player, 2)

        row_status = 'PASS'
        if kind == 'building':
            if BUILDING_CLASSES.get(art) == 'gate-landmark':
                gate_issues = gate_landmark_problems(p, assets_root)
                problems.extend(gate_issues)
                row_status = 'FAIL(城门地标)' if gate_issues else 'PASS(城门地标)'
            else:
                scale_min, scale_max = building_scale_range(art)
                declared = p.get('buildingClass')
                actual = BUILDING_CLASSES.get(art)
                if declared is not None and declared != actual:
                    problems.append('房型与实际素材不符: %s declared=%s actual=%s' % (pid, declared, actual))
                in_range = (scale_min <= h_over_player <= scale_max)
                in_baseline = (pid in scale_baseline)
    
                if not in_baseline and not in_range:
                    # 分支 1: 不在台账 且 超出区间 -> 新增失衡 FAIL
                    msg = ('新增失衡: 建筑地块 %s h_over_player=%.2f ∉ [%.1f, %.1f]'
                           % (pid, ratio, scale_min, scale_max))
                    problems.append(msg)
                    row_status = 'FAIL(新增失衡)'
                elif in_baseline and not in_range:
                    # 分支 2: 在台账 且 仍超出区间 -> 存量失衡保持 WARN
                    b_val = scale_baseline[pid]
                    msg = ('存量失衡: 建筑地块 %s=%.2f（台账登记 %.2f，允许区间 [%.1f, %.1f]）'
                           % (pid, ratio, b_val, scale_min, scale_max))
                    warns.append(msg)
                    row_status = 'WARN(存量失衡)'
                elif in_baseline and in_range:
                    # 分支 3: 在台账 且 已进区间 -> 台账可销账 FAIL（必须人工删除台账）
                    msg = ('台账可销账: 建筑地块 %s=%.2f 已进入允许区间 [%.1f, %.1f]，请从 SCALE_BASELINE 中销账'
                           % (pid, ratio, scale_min, scale_max))
                    problems.append(msg)
                    row_status = 'FAIL(台账可销账)'
                else:
                    # 不在台账 且 在区间内 -> 正常合规
                    row_status = 'PASS'
        else:
            # prop / stall / tree 不参与体量判定
            row_status = 'SKIP(非building)'

        rows.append({
            'id': pid, 'kind': kind, 'art': art,
            'plot_w': pw, 'plot_h': ph,
            'bbox_h_pct': bh_pct, 'visual_h': visual_h,
            'h_over_player': h_over_player, 'status': row_status,
            'building_class': BUILDING_CLASSES.get(art) if kind == 'building' else None,
            'allowed_range': building_scale_range(art) if kind == 'building' else None
        })

    # 2. 占位复用判据（仅限 kind == 'building' 之间）
    #    维度 a：art 路径字符串相同（原判据，口径未动）
    #    维度 b：md5 内容指纹相同（MAP-26 补；路径不同但内容相同 = 复制改名）
    art_to_buildings = {}
    for bp in building_plots:
        art = bp.get('art')
        if art:
            art_to_buildings.setdefault(art, []).append(bp.get('id'))

    # 2.1 内容指纹（每个 building 立绘算一次 md5）
    art_md5 = {}
    for art in sorted(art_to_buildings):
        digest = get_art_md5(art, assets_root)
        art_md5[art] = digest
        if digest is None:
            path = os.path.join(assets_root, art + '.png')
            problems.append('内容指纹不可用: 建筑美术 %s 的 md5 无法计算（文件不存在或不可读: %s）'
                            % (art, path))

    # 2.2 维度 a：同路径复用
    for art, users in art_to_buildings.items():
        if len(users) > 1:
            digest = art_md5.get(art)
            if art in reuse_baseline:
                allowed_users = set(reuse_baseline[art].get('plots', []))
                cur_users = set(users)
                new_users = cur_users - allowed_users
                if new_users:
                    msg = ('新增占位复用: 美术 %s (md5=%s) 被新增建筑地块复用: %s（当前共用: %s）'
                           % (art, digest[:8] if digest else '(md5 不可用)',
                              sorted(list(new_users)), sorted(list(cur_users))))
                    problems.append(msg)
                else:
                    warns.append(_format_reuse_entry(art, digest, cur_users, reuse_baseline[art]))
            else:
                msg = ('新增占位复用: 美术 %s (md5=%s) 被多个建筑地块复用: %s'
                       % (art, digest[:8] if digest else '(md5 不可用)', sorted(list(users))))
                problems.append(msg)

    # 2.3 维度 b：md5 相同但路径不同（复制改名）——不入台账，一律硬 FAIL
    md5_to_arts = {}
    for art in sorted(art_md5):
        digest = art_md5[art]
        if digest:
            md5_to_arts.setdefault(digest, []).append(art)
    for digest in sorted(md5_to_arts):
        arts = md5_to_arts[digest]
        if len(arts) > 1:
            detail = '; '.join('%s (md5=%s) <- %s'
                               % (a, digest[:8], sorted(art_to_buildings.get(a, [])))
                               for a in arts)
            msg = ('新增占位复用(同图改名·内容指纹相同): %d 条不同路径的立绘 md5 全为 %s（同一件美术复制改名）: %s'
                   % (len(arts), digest[:8], detail))
            problems.append(msg)

    # 检查复用台账是否可销账
    for art, info in reuse_baseline.items():
        actual_users = art_to_buildings.get(art, [])
        if len(actual_users) <= 1:
            msg = ('占位复用台账可销账: 美术 %s 当前建筑使用数=%d (%s)，复用已消除，请从 REUSE_BASELINE 中销账（销账条件: %s）'
                   % (art, len(actual_users), actual_users, info.get('retire', '(未写)')))
            problems.append(msg)

    return problems, warns, rows


def selftest():
    """覆盖 9 类用例的棘轮自检测试。全部点火才 PASS。"""
    cases = []

    # 真立绘样本
    # shop-front: bh_pct = 0.6354. 若 ph = 500, visual_h = 317.7, h_over_player = 1.87 ∈ [1.5, 3.5]
    # residence:  bh_pct = 0.8799. 若 ph = 200, visual_h = 176.0, h_over_player = 1.03 ∉ [1.5, 3.5]
    # washing-stone: bh_pct = 0.2021. 若 ph = 50, visual_h = 10.1, h_over_player = 0.06
    # 用例元组： (名称, 地块, scale_baseline, reuse_baseline, assets_root(None=仓内真图根), 期望 rc, 期望关键字)

    # 1. 好例（比值在区间 + 无复用） -> PASS (rc=0)
    cases.append((
        '1_好例_合规建筑',
        [{'id': 'good-bldg', 'kind': 'building', 'art': 'Art/Props/shop-front', 'width': 400, 'height': 500}],
        {}, {},
        None, 0, 'PASS'
    ))

    # 2. 新增失衡（不在台账的矮建筑） -> FAIL (rc=1, 报出 id)
    cases.append((
        '2_新增失衡_超矮建筑报FAIL',
        [{'id': 'new-short-bldg', 'kind': 'building', 'art': 'Art/Props/residence', 'width': 200, 'height': 200}],
        {}, {},
        None, 1, '新增失衡'
    ))

    # 3. 存量失衡保持 -> WARN (rc=0, 存量失衡告警)
    cases.append((
        '3_存量失衡保持_台账内告警且rc=0',
        [{'id': 'legacy-short-bldg', 'kind': 'building', 'art': 'Art/Props/residence', 'width': 220, 'height': 220}],
        {'legacy-short-bldg': 0.69}, {},
        None, 0, '存量失衡'
    ))

    # 4. 台账可销账（已修好但台账没删） -> FAIL (rc=1, 报台账可销账)
    cases.append((
        '4_台账可销账_已进区间但未删台账报FAIL',
        [{'id': 'legacy-fixed-bldg', 'kind': 'building', 'art': 'Art/Props/shop-front', 'width': 400, 'height': 500}],
        {'legacy-fixed-bldg': 0.69}, {},
        None, 1, '台账可销账'
    ))

    # 5. 占位复用新增第三处 -> FAIL (rc=1, 报新增复用)
    cases.append((
        '5_占位复用新增第三处_超出豁免报FAIL',
        [
            {'id': 'bldg-1', 'kind': 'building', 'art': 'Art/Props/residence', 'width': 400, 'height': 500},
            {'id': 'bldg-2', 'kind': 'building', 'art': 'Art/Props/residence', 'width': 400, 'height': 500},
            {'id': 'bldg-3-extra', 'kind': 'building', 'art': 'Art/Props/residence', 'width': 400, 'height': 500},
        ],
        {},
        {'Art/Props/residence': {'plots': ['bldg-1', 'bldg-2'], 'reason': '测试',
                                 'retire': '测试销账条件', 'class': 'B', 'tier': 'placeholder'}},
        None, 1, '新增占位复用'
    ))

    # 6. prop/tree 超矮不判 -> PASS (rc=0)
    cases.append((
        '6_prop超矮不判_跳过体量检查',
        [{'id': 'tiny-prop', 'kind': 'prop', 'art': 'Art/Props/washing-stone', 'width': 50, 'height': 50}],
        {}, {},
        None, 0, 'PASS'
    ))

    # 7. 美术缺失 -> FAIL (rc=1)
    cases.append((
        '7_美术缺失_文件不存在报FAIL',
        [{'id': 'missing-art-bldg', 'kind': 'building', 'art': 'Art/Props/no-such-image-xyz', 'width': 300, 'height': 300}],
        {}, {},
        None, 1, '美术缺失'
    ))

    # 8/9. md5 内容指纹夹具：同一张真立绘拷成两个不同文件名（内容完全相同）；另取两张内容不同的真图
    tmp_root = tempfile.mkdtemp(prefix='map26-md5-fp-')
    dup_dir = os.path.join(tmp_root, 'Art', 'Props')
    os.makedirs(dup_dir)
    src_png = os.path.join(ART_ROOT, 'Art', 'Props', 'shop-front.png')
    shutil.copyfile(src_png, os.path.join(dup_dir, 'dup-a.png'))
    shutil.copyfile(src_png, os.path.join(dup_dir, 'dup-b.png'))

    # 8. 复制改名（两个不同文件名、内容 md5 相同） -> FAIL（同图改名）
    cases.append((
        '8_复制改名_两名同图报FAIL',
        [
            {'id': 'renamed-a', 'kind': 'building', 'art': 'Art/Props/dup-a', 'width': 400, 'height': 500},
            {'id': 'renamed-b', 'kind': 'building', 'art': 'Art/Props/dup-b', 'width': 400, 'height': 500},
        ],
        {}, {},
        tmp_root, 1, '同图改名'
    ))

    # 9. 内容不同（两张不同真图各用一次） -> PASS（不误报）
    cases.append((
        '9_内容不同不误报_各用一次PASS',
        [
            {'id': 'distinct-a', 'kind': 'building', 'art': 'Art/Props/shop-front', 'width': 400, 'height': 500},
            {'id': 'distinct-b', 'kind': 'building', 'art': 'Art/Props/residence', 'width': 400, 'height': 500},
        ],
        {}, {},
        None, 0, 'PASS'
    ))

    bad = []
    print('=== 开始执行 --selftest 9 大用例点火验证 ===')
    try:
        for (name, test_plots, test_scale_b, test_reuse_b,
             test_assets_root, want_rc, want_kw) in cases:
            probs, warns, _ = check_plots(
                test_plots, assets_root=(test_assets_root or ART_ROOT),
                scale_baseline=test_scale_b, reuse_baseline=test_reuse_b,
                verbose=False
            )
            got_rc = 1 if probs else 0
            all_text = ' '.join(probs + warns)

            if got_rc != want_rc:
                bad.append('%s: 退出码期望 %d，实得 %d（输出: %s）' % (name, want_rc, got_rc, all_text))
                continue
            if want_kw != 'PASS' and want_kw not in all_text:
                bad.append('%s: 关键字期望包含 "%s"，实得: %s' % (name, want_kw, all_text))
                continue
            print('  [PASS] %-30s -> rc=%d, 触发断言: %s' % (name, got_rc, want_kw))
    finally:
        shutil.rmtree(tmp_root, ignore_errors=True)

    if bad:
        for b in bad:
            print('SELFTEST FAIL: ' + b)
        print('RESULT: FAIL（selftest %d/%d 条不点火）' % (len(bad), len(cases)))
        return 1

    print('SELFTEST: %d/%d 条用例全点火（好例 / 新增失衡 / 存量保持 / 台账可销账 / 占位第三处复用 / '
          'prop超矮不判 / 美术缺失 / 复制改名同图 / 内容不同不误报）'
          % (len(cases), len(cases)))
    print('RESULT: PASS（selftest）')
    return 0


def main():
    parser = argparse.ArgumentParser(description='MAP-15 建筑体量比例与占位复用棘轮门禁')
    parser.add_argument('--selftest', action='store_true', help='运行 9 类合成用例自检')
    parser.add_argument('--verbose', action='store_true', help='输出全部 31 个地块详细比值表')
    args = parser.parse_args()

    if args.selftest:
        return selftest()

    if not os.path.exists(PLOTS_JSON):
        print('RESULT: FAIL（building-plots.json 不存在：%s）' % PLOTS_JSON)
        return 1

    with io.open(PLOTS_JSON, 'r', encoding='utf-8') as f:
        doc = json.load(f)
    plots = doc.get('plots') or []

    problems, warns, rows = check_plots(plots, verbose=args.verbose)

    # 打印警告（D-57：门禁 WARN 必须可见）
    for w in warns:
        print('WARN: ' + w)

    # 打印硬错误
    for p in problems:
        print('FAIL: ' + p)

    if args.verbose:
        print('\n=== 全 31 地块视觉体量比例表（按 h_over_player 升序） ===')
        sorted_rows = sorted(rows, key=lambda r: (r['h_over_player'], r['id']))
        print('%-4s | %-24s | %-8s | %-22s | %-9s | %-12s | %-8s | %-6s | %s'
              % ('序号', '地块 ID', '类别', '立绘 Art', '地块尺寸', '内容高比例', '视觉高', '比值', '状态'))
        print('-' * 115)
        for i, r in enumerate(sorted_rows, 1):
            dim_str = '%gx%g' % (r['plot_w'], r['plot_h'])
            pct_str = '%.2f%%' % (r['bbox_h_pct'] * 100.0)
            print('%-4d | %-24s | %-8s | %-22s | %-9s | %-12s | %-8.1f | %-6.2f | %s'
                  % (i, r['id'], r['kind'], r['art'].replace('Art/Props/', ''),
                     dim_str, pct_str, r['visual_h'], r['h_over_player'], r['status']))

    total_plots = len(plots)
    bldg_count = sum(1 for p in plots if p.get('kind') == 'building')

    if problems:
        print('SUMMARY: %d 地块（%d 建筑），发现 %d 项违规，%d 项存量警告'
              % (total_plots, bldg_count, len(problems), len(warns)))
        print('RESULT: FAIL（%d 项违规）' % len(problems))
        return 1
    elif warns:
        print('SUMMARY: %d 地块（%d 建筑），0 违规，%d 项存量棘轮警告（退出码 0 放行）'
              % (total_plots, bldg_count, len(warns)))
        print('RESULT: PASS_WITH_WARN（%d 项存量警告）' % len(warns))
        return 0
    else:
        print('SUMMARY: %d 地块（%d 建筑），0 违规，0 警告' % (total_plots, bldg_count))
        print('RESULT: PASS')
        return 0


if __name__ == '__main__':
    sys.exit(main())
