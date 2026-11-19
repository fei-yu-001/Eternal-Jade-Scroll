#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""MAP-11/12 门禁：collisionDepth 与 collisionTop 美术 alpha 实测一致性。

治的腐化：building-plots.json 里的 collisionDepth / collisionTop 是手抄/离线算的，
美术换图（重切立绘、换净边）后忘了改深度/上沿，阻挡带就和画面再次对不上：
  - collisionDepth 算错/过小：碰撞只装在美术上半部、下部穿模（MAP-11 之前）。
  - collisionTop 算错/缺失：画带以内内容顶以上的透明空白区被整块挡住（MAP-12 之前「很远被挡」）。

口径与 survey 脚本及 Tools/logs/map-12/apply-collision-top.py 逐字同阈值：
alpha>128 记为不透明内容；
内容顶边 = 首个非空行；pct 距图顶；期望 top = round(顶边距顶% × height)。
内容底边 = 末个非空行；pct 距图顶；期望 depth = round(底边距顶% × height)。
基准 rect：[footX-w/2, footY+collisionTop] × [footX+w/2, footY+collisionDepth]。

判据：
  1. collisionDepth / collisionTop 缺省或 0 = 回退旧口径（合法，但 FALLBACK 要计数）；
  2. 非数值 → FAIL；
  3. 为负 → FAIL；
  4. |json 值 − alpha 实测期望| > 2 → FAIL，并报出两者；
  5. collisionTop > collisionDepth（倒挂空矩形）→ FAIL；
  6. 有 collision 字段但美术缺失 → FAIL（无法自证的值不许存）；
  7. blocks:false 的块不参与（它不注册阻挡，字段无意义）。

自证：python check-collision-depth.py --selftest（合成地块，全绿才算门禁活着）。
"""
import io
import json
import os
import sys
import warnings

# GBK 控制台下 print 中文会把门禁崩在 RESULT 之前——会崩的门禁比红的门禁危险（全仓统一守卫）。
for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, 'reconfigure'):
        _stream.reconfigure(encoding='utf-8', errors='replace')

warnings.filterwarnings('ignore')
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
PROJ = os.path.join(REPO, 'GameClient', 'TwelveJade')
PLOTS = os.path.join(PROJ, 'Assets', 'Resources', 'Config', 'building-plots.json')
ARTROOT = os.path.join(PROJ, 'Assets', 'Resources')
TOLERANCE = 2.0
ALPHA_T = 128


def art_alpha_bounds(art_rel):
    """art_rel 如 'Art/Props/mountain-pine'。返回 (顶边距顶%, 底边距顶%, 图高px)；缺失返回 None。"""
    path = os.path.join(ARTROOT, art_rel + '.png')
    if not os.path.exists(path):
        return None
    im = Image.open(path).convert('RGBA')
    a = im.getchannel('A')
    w, h = im.size
    px = a.load()
    top = None
    bot = None
    for y in range(h):
        for x in range(w):
            if px[x, y] > ALPHA_T:
                top = y
                break
        if top is not None:
            break
    for y in range(h - 1, -1, -1):
        for x in range(w):
            if px[x, y] > ALPHA_T:
                bot = y
                break
        if bot is not None:
            break
    if top is None or bot is None:
        return 0.0, 0.0, h
    return top * 100.0 / h, bot * 100.0 / h, h


def check_plots(plots, label):
    problems = []
    fallback_depth = 0
    fallback_top = 0
    checked_depth = 0
    checked_top = 0
    for i, p in enumerate(plots):
        pid = p.get('id') or ('#%d' % i)
        if p.get('blocks') is False:
            continue
        raw_depth = p.get('collisionDepth', None)
        raw_top = p.get('collisionTop', None)

        # 1. 深度与上沿的基础有效性检查
        has_depth = raw_depth is not None and raw_depth != 0
        has_top = raw_top is not None and raw_top != 0

        if not has_depth:
            fallback_depth += 1
        if not has_top:
            fallback_top += 1

        if not has_depth and not has_top:
            continue

        if has_depth:
            if isinstance(raw_depth, bool) or not isinstance(raw_depth, (int, float)):
                problems.append('地块 %s 的 collisionDepth 必须是数字（实际 %r）'
                                % (pid, raw_depth))
                has_depth = False
            elif raw_depth < 0:
                problems.append('地块 %s 的 collisionDepth 不能为负（实际 %g）'
                                % (pid, raw_depth))
                has_depth = False

        if has_top:
            if isinstance(raw_top, bool) or not isinstance(raw_top, (int, float)):
                problems.append('地块 %s 的 collisionTop 必须是数字（实际 %r）'
                                % (pid, raw_top))
                has_top = False
            elif raw_top < 0:
                problems.append('地块 %s 的 collisionTop 不能为负（实际 %g）'
                                % (pid, raw_top))
                has_top = False

        if has_depth and has_top:
            if float(raw_top) > float(raw_depth):
                problems.append('地块 %s 的 collisionTop(%g) > collisionDepth(%g)（倒挂空矩形）'
                                % (pid, raw_top, raw_depth))

        art = p.get('art') or ''
        if not art:
            problems.append('地块 %s 有 collision 字段但 art 为空，无法自证' % pid)
            continue
        prof = art_alpha_bounds(art)
        if prof is None:
            problems.append('地块 %s 的美术缺失（Resources/%s.png），collision 字段无法自证'
                            % (pid, art))
            continue
        tp, bp, _pxh = prof
        height = p.get('height')
        if isinstance(height, bool) or not isinstance(height, (int, float)) or height <= 0:
            problems.append('地块 %s 的 height 必须为正（实际 %r）' % (pid, height))
            continue

        if has_depth:
            expected_depth = round(bp / 100.0 * float(height))
            checked_depth += 1
            if abs(float(raw_depth) - expected_depth) > TOLERANCE:
                problems.append('地块 %s 的 collisionDepth=%g 与美术实测不符'
                                '（期望 %d = 底边%.1f%% × height %.0f，容差 %g）'
                                % (pid, raw_depth, expected_depth, bp, height, TOLERANCE))

        if has_top:
            expected_top = round(tp / 100.0 * float(height))
            checked_top += 1
            if abs(float(raw_top) - expected_top) > TOLERANCE:
                problems.append('地块 %s 的 collisionTop=%g 与美术实测不符'
                                '（期望 %d = 顶边%.1f%% × height %.0f，容差 %g）'
                                % (pid, raw_top, expected_top, tp, height, TOLERANCE))

    return problems, {
        'fallback_depth': fallback_depth, 'checked_depth': checked_depth,
        'fallback_top': fallback_top, 'checked_top': checked_top,
        'plots': len(plots), 'label': label
    }


def selftest():
    """合成地块：一条条把判据点火。全绿才算门禁活着。"""
    cases = []
    # 真美术 + 真高度：mountain-pine 顶边 2.29% × 250 = 6，底边 99.06% × 250 = 248。
    ok_plot = {'id': 'good', 'kind': 'tree', 'art': 'Art/Props/mountain-pine',
               'height': 250, 'collisionDepth': 248, 'collisionTop': 6}
    cases.append(('正常值通过', [ok_plot], 0))
    cases.append(('缺字段回退合法', [
        {'id': 'fallback', 'kind': 'tree', 'art': 'Art/Props/mountain-pine',
         'height': 250, 'radius': 22}], 0))
    cases.append(('深度非数值炸', [dict(ok_plot, collisionDepth='deep')], 1))
    cases.append(('上沿非数值炸', [dict(ok_plot, collisionTop='high')], 1))
    cases.append(('深度为负炸', [dict(ok_plot, collisionDepth=-5)], 1))
    cases.append(('上沿为负炸', [dict(ok_plot, collisionTop=-3)], 1))
    cases.append(('上沿大于深度倒挂炸', [dict(ok_plot, collisionTop=249)], 1))
    cases.append(('深度与美术不符炸', [dict(ok_plot, collisionDepth=100)], 1))
    cases.append(('上沿与美术不符炸', [dict(ok_plot, collisionTop=50)], 1))
    cases.append(('容差内通过(±2)', [dict(ok_plot, collisionDepth=246, collisionTop=4)], 0))
    cases.append(('美术缺失炸', [
        {'id': 'noart', 'kind': 'tree', 'art': 'Art/Props/nope',
         'height': 250, 'collisionDepth': 248, 'collisionTop': 6}], 1))
    cases.append(('blocks:false 不参与', [
        dict(ok_plot, id='gate', blocks=False)], 0))
    bad = []
    for name, plots, want_fail in cases:
        problems, _ = check_plots(plots, name)
        if (len(problems) > 0) != (want_fail > 0):
            bad.append('%s：期望 %s，实得 %d 条 %s'
                       % (name, 'FAIL' if want_fail else 'PASS',
                          len(problems), problems))
    if bad:
        for b in bad:
            print('SELFTEST FAIL: ' + b)
        print('RESULT: FAIL（selftest %d/%d 条不点火）' % (len(bad), len(cases)))
        return 1
    print('SELFTEST: %d/%d 条用例全点火（缺字段回退 / 深度与上沿非数值 / 为负 / 倒挂 / '
          '与美术不符 / 容差边界 / 美术缺失 / blocks:false）' % (len(cases), len(cases)))
    print('RESULT: PASS（selftest）')
    return 0


def main():
    if '--selftest' in sys.argv[1:]:
        return selftest()
    if not os.path.exists(PLOTS):
        print('RESULT: FAIL（building-plots.json 不存在：%s）' % PLOTS)
        return 1
    with io.open(PLOTS, encoding='utf-8') as f:
        doc = json.load(f)
    plots = doc.get('plots') or []
    problems, summary = check_plots(plots, 'building-plots.json')
    print('BASE-FALLBACK: depth %d/%d, top %d/%d plots 走旧回退'
          % (summary['fallback_depth'], summary['plots'],
             summary['fallback_top'], summary['plots']))
    print('checked %d 个 collisionDepth 值、%d 个 collisionTop 值，容差 %g 世界单位'
          % (summary['checked_depth'], summary['checked_top'], TOLERANCE))
    for p in problems:
        print('PROBLEM: ' + p)
    if problems:
        print('RESULT: FAIL（%d 条）' % len(problems))
        return 1
    print('RESULT: PASS（%d 个 depth / %d 个 top 与美术实测一致）'
          % (summary['checked_depth'], summary['checked_top']))
    return 0


if __name__ == '__main__':
    sys.exit(main())
