#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""MAP-11/12 门禁：运行时显式脚底碰撞及旧 collisionDepth/Top 实测一致性。

显式 collisionCapsule/collisionRects 优先，与 WorldPropLayer 一致；核验来源SHA、
实际底脚带、源像素投影、矩形范围与胶囊。没有显式形状时保留下述旧版全alpha判据。

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
import hashlib
import math
import copy

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


def explicit_footprint_issues(plot):
    """Validate the shapes WorldPropLayer actually registers, before legacy fallbacks.

    November footprints use a bottom band, measured plinth or separate gate piers.
    collisionTop/Depth describe a full-art fallback only when no explicit shape exists.
    Treating a roof's first alpha row as a ground collider would block walkable doors.
    """
    errors = []
    def require(ok, message):
        if not ok: errors.append(message)
    def number(value):
        return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)
    fp = plot.get('footprint')
    if not isinstance(fp, dict) or fp.get('version') not in (1, 2, 3, 4):
        return ['显式碰撞缺少支持的 footprint 来源版本']
    path = os.path.join(ARTROOT, (plot.get('art') or '') + '.png')
    if not os.path.isfile(path): return ['显式碰撞源图不存在']
    with open(path, 'rb') as stream: digest = hashlib.file_digest(stream, 'sha256').hexdigest()
    require(fp.get('sourceSha256') == digest, 'footprint 源图 SHA256 已漂移')
    require(fp.get('alphaThreshold', ALPHA_T) == ALPHA_T, 'footprint alpha 判据不一致')
    width, height = plot.get('width'), plot.get('height')
    if not number(width) or not number(height) or min(width, height) <= 0:
        return errors + ['显式碰撞画布尺寸必须为正有限数']
    with Image.open(path) as image: alpha = image.convert('RGBA').getchannel('A')
    iw, ih = alpha.size
    opaque = alpha.point(lambda value: 255 if value > ALPHA_T else 0)
    bounds = opaque.getbbox()
    if bounds is None: return errors + ['显式碰撞源图全透明']
    sx, sy = width / iw, height / ih
    rects = plot.get('collisionRects')
    if not isinstance(rects, list) or not rects: return errors + ['显式碰撞缺少来源矩形']
    valid = []
    for rect in rects:
        if not isinstance(rect, dict) or not all(number(rect.get(k)) for k in ('x0','y0','x1','y1')):
            errors.append('碰撞矩形不是有限数'); continue
        x0,y0,x1,y1 = [rect[k] for k in ('x0','y0','x1','y1')]
        if x1 <= x0 or y1 <= y0:
            errors.append('碰撞矩形倒置或面积为零'); continue
        require(x0 >= -width / 2 - TOLERANCE and x1 <= width / 2 + TOLERANCE and
                y0 >= -TOLERANCE and y1 <= height + TOLERANCE, '碰撞矩形越出源图画布')
        crop = (max(0, math.floor(x0 / sx + iw / 2)), max(0, math.floor(y0 / sy)),
                min(iw, math.ceil(x1 / sx + iw / 2)), min(ih, math.ceil(y1 / sy)))
        if crop[2] <= crop[0] or crop[3] <= crop[1] or opaque.crop(crop).getbbox() is None:
            errors.append('碰撞矩形完全落在透明区域')
        valid.append(rect)
    if len(valid) != len(rects): return errors
    band = fp.get('sourceBand')
    if fp['version'] == 1:
        fraction = fp.get('bottomFraction')
        if not number(fraction) or not .10 <= fraction <= .20:
            return errors + ['自动脚底带比例必须为10%~20%']
        expected = [math.floor(bounds[3] - (bounds[3] - bounds[1]) * fraction), bounds[3]]
        require(isinstance(band, list) and len(band) == 2 and all(number(v) for v in band) and
                all(abs((actual - wanted) * sy) <= TOLERANCE for actual, wanted in zip(band, expected)),
                '自动脚底 sourceBand 与 alpha 实测相差超过2世界单位')
        require(all(abs(rect['y0'] - expected[0] * sy) <= TOLERANCE and
                    abs(rect['y1'] - expected[1] * sy) <= TOLERANCE for rect in valid) if len(valid) == 1 else
                all(rect['y0'] >= expected[0] * sy - TOLERANCE and rect['y1'] <= expected[1] * sy + TOLERANCE for rect in valid),
                '显式碰撞越出实测底脚带')
        require(number(plot.get('groundFootOffset')) and abs(plot['groundFootOffset'] - expected[1] * sy) <= TOLERANCE,
                '真实落脚点未绑定实测 alpha 底边')
    source_rects = fp.get('sourceCollisionRects')
    if source_rects is not None:
        crop = fp.get('sourceCrop')
        if not isinstance(crop, list) or len(crop) != 4 or not all(number(v) for v in crop):
            return errors + ['像素碰撞登记缺少源图裁切范围']
        require(crop[2] - crop[0] == iw and crop[3] - crop[1] == ih, '源图裁切范围与实际PNG尺寸不符')
        require(len(source_rects) == len(valid), '源像素碰撞与实际矩形数量不符')
        for source, rect in zip(source_rects, valid):
            if len(source) < 5 or not all(number(v) for v in source[:4]):
                errors.append('源像素碰撞登记无效'); continue
            expected = [(source[0] - crop[0] - iw / 2) * sx, (source[1] - crop[1]) * sy,
                        (source[2] - crop[0] - iw / 2) * sx, (source[3] - crop[1]) * sy]
            require(all(abs(rect[k] - value) <= .002 for k, value in zip(('x0','y0','x1','y1'), expected)) and
                    rect.get('role') == source[4], '源像素碰撞投影与运行时矩形不符')
    capsule = plot.get('collisionCapsule')
    if fp.get('shape') == 'capsule':
        if not isinstance(capsule, dict) or not all(number(capsule.get(k)) for k in ('x0','x1','y','radius')):
            return errors + ['胶囊参数必须为有限数']
        require(len(valid) == 1, '胶囊必须只有一个来源底脚矩形')
        rect = valid[0]; radius = min((rect['x1'] - rect['x0']) / 2, (rect['y1'] - rect['y0']) / 2)
        expected = {'x0':rect['x0'] + radius,'x1':rect['x1'] - radius,'y':(rect['y0'] + rect['y1']) / 2,'radius':radius}
        require(radius > 0 and all(abs(capsule[k] - v) <= .002 for k,v in expected.items()), '胶囊与底脚来源矩形不一致')
    else:
        require(fp.get('shape') == 'rectangles' and capsule is None, '矩形脚底被遗留胶囊覆盖')
    return errors


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
        if p.get('collisionCapsule') is not None or p.get('collisionRects'):
            problems.extend('地块 %s: %s' % (pid, issue) for issue in explicit_footprint_issues(p))
            checked_depth += 1; checked_top += 1
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
    with open(PLOTS, encoding='utf-8') as stream:
        installed = json.load(stream)['plots']
    measured = copy.deepcopy(next(p for p in installed if p['id'] == 'plot-mountain-pine'))
    cases.append(('已登记底脚胶囊通过', [measured], 0))
    for label, change in (
            ('源图hash漂移', lambda p: p['footprint'].update(sourceSha256='0'*64)),
            ('缺来源版本', lambda p: p.pop('footprint')),
            ('伪造底脚带', lambda p: p['footprint'].update(sourceBand=[0, 1])),
            ('伪造完整图碰撞', lambda p: p['collisionRects'][0].update(y0=0)),
            ('胶囊漂移', lambda p: p['collisionCapsule'].update(radius=999)),
            ('非有限胶囊', lambda p: p['collisionCapsule'].update(y=float('nan'))),
            ('透明区碰撞', lambda p: p['collisionRects'][0].update(x0=-125,x1=-124,y0=0,y1=1))):
        probe = copy.deepcopy(measured); change(probe); cases.append((label, [probe], 1))
    gate = copy.deepcopy(next(p for p in installed if p['id'] == 'plot-ancestral-hall'))
    cases.append(('分离门墩源像素投影', [gate], 0))
    shifted = copy.deepcopy(gate); shifted['collisionRects'][0]['x0'] += 12
    cases.append(('门墩错误投影拒绝', [shifted], 1))
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
    print('checked %d 个实际脚底/旧depth、%d 个实际脚底/旧top，容差 %g 世界单位'
          % (summary['checked_depth'], summary['checked_top'], TOLERANCE))
    for p in problems:
        print('PROBLEM: ' + p)
    if problems:
        print('RESULT: FAIL（%d 条）' % len(problems))
        return 1
    print('RESULT: PASS（%d 个脚底/旧depth · %d 个脚底/旧top 与来源实测一致）'
          % (summary['checked_depth'], summary['checked_top']))
    return 0


if __name__ == '__main__':
    sys.exit(main())
