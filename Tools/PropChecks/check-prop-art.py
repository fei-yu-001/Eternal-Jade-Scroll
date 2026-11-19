#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""MAP-17 门禁:立绘资产完整性棘轮门禁(画布/通道/贴边/背景残留/孤儿)。

治的腐化:building-plots.json 引用的 png 文件存在,但本身不是一张合格可入库的美术件:
画布非 960x960、RGB 无 alpha 通道、整图没抠底、ivory 底的四角残留被带进游戏、
内容贴边到画布边缘导致根基带缺失或穿模。
(MAP-16/18 的抠底与碰撞实测都依赖 alpha,RGB 会让它们全失效。)

判据(与架构师裁决逐条一致):
  1. 文件存在:配置引用必须能落到真实文件;
  2. 画布尺寸:画布宽高比与所引用地块的 plot 宽高比匹配(err ≤ 0.005)
     (MAP-22 升级：旧判据固定 960x960 已被 MAP-14 非正方形画布规格淘汰);
  3. 色彩通道:必须是 RGBA(带 alpha);
  4. alpha 非全不透明:alpha 最小值必须为 0(确实抠过底);
  5. 四角无背景残留:四角像素 alpha 必须为 0(防 ivory 底带进游戏);
  6. 内容不贴边:内容包围盒(alpha>128)必须 left>=1% / right<=99% / top>=1% / bottom>=95%
     (left/right/top 防裁切;**bottom 是落地方向:内容必须贴底**,距底越远浮空越多,
      见 MAP-25 方向修正,渲染口径 FrontEndWorld.cs:735-742 + :934 ADR-018);
  7. 无孤儿:Art/Props/ 下有但无任何地块引用的 PNG -> WARN(不 FAIL,只登记)。

棘轮三分支(与 MAP-15 同款):
  - 不在台账且违规 -> FAIL(退出码 1);
  - 在台账且仍违规(且无新增判据项) -> WARN(退出码 0);
  - 在台账但已合规 -> 报「台账可销账」并 FAIL(退出码 1)。
初始台账由实跑现状逐条填写(不凭空写)。

--report 模式:只打印不判定,退出码恒 0,输出全表:
  art路径 | 尺寸 | 通道 | alpha范围(min,max) | 内容bbox | 占宽% | 占高% | 底边% | 引用地块数

自检:python Tools/PropChecks/check-prop-art.py --selftest(>=10 条用例,临时目录造图)。
"""
import argparse
import io
import json
import os
import sys
import tempfile
import warnings

warnings.filterwarnings('ignore')

# UTF-8 控制台与管道守卫
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
if hasattr(sys.stderr, 'reconfigure'):
    sys.stderr.reconfigure(encoding='utf-8', errors='replace')

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
PROJ = os.path.join(REPO, 'GameClient', 'TwelveJade')
PLOTS_JSON = os.path.join(PROJ, 'Assets', 'Resources', 'Config', 'building-plots.json')
ART_ROOT = os.path.join(PROJ, 'Assets', 'Resources')
PROPS_DIR = os.path.join(ART_ROOT, 'Art', 'Props')

CANVAS_W = 960
CANVAS_H = 960
CANVAS_ASPECT_TOL = 0.005   # MAP-22：画布宽高比与 plot 宽高比匹配容差（与 MAP-14 生图规格同源）
ALPHA_THRESHOLD = 128
ALPHA_ZERO = 0

# 贴边阈值(画布分数)
# left/right/top 防的是**裁切**(内容贴到画布边缘会被切掉),方向:越界即违规;
# bottom 是**落地**方向(与前三项相反):
#   FrontEndWorld.cs:735-742 PlaceInWorld 用 pivot=(0.5,1),调用方传 foot.y - height(:934 ADR-018)
#   ⇒ 图被拉伸铺满 plot 区域、**图底边 = 地面(footY)**;
#   recalibrate-collision.py: collisionDepth = bottom_pct x plot.height ⇒ 内容的竖直位置直接决定物件落点。
#   所以**内容底边必须贴近画布底边**(bottom_pct ≈ 99%),物件才落地;
#   内容距底越远 = 浮空越多(MAP-23 反证:bottom=93.8% 致茶馆浮空 23.5px、牌坊 33px,玩家才 170px)。
# 历史自证:Art/Props 里 15 件旧资产 bottom 全在 96.1%~99.2%(贴底),是历史上正确的做法。
EDGE_LEFT = 0.01
EDGE_RIGHT = 0.99
EDGE_TOP = 0.01
EDGE_BOTTOM = 0.95   # bottom 判据:**低于此值即违规**(内容未贴底 = 浮空)

# 判据键:用于台账「登记了哪些判据项」,新增判据项不能被台账掩盖
CRIT_MISSING = 'missing'
CRIT_CANVAS = 'canvas'
CRIT_MODE = 'mode'
CRIT_ALPHA = 'alpha_min'
CRIT_CORNERS = 'corners'
CRIT_EDGE = 'edge'

# 初始台账:2026-10-13 实跑现状逐条登记的「内容贴边」违规(共 17 件)。
# 实跑当场合规的 4 件为:residence / market-stall / paifang / temple-gate;孤儿 0 件。
# 结构:art -> {'criteria': {判据键...}, 'note': 登记理由}
ART_BASELINE = {
    # 2026-10-14 MAT-04 台账清空：最后 4 件存量已按下述方式处置并通过全部判据——
    #   shop-front    内容重摆（贴底 98.96% + 四边留净边 1.46%/98.44%）
    #   lantern-pole  按 plot ar 0.400 重画（410x1024，贴底 99.02%）
    #   market-stall  按 plot ar 0.778 重画（796x1024，贴底 99.02%，气运布棚）
    #   stone-lion    按 plot ar 0.667 重画（683x1024，贴底 99.02%）
    # 另：plot-market-cart 由复用布棚图改为自有货郎木板车图（ar 1.167）。
    #
    # MAP-25 销账（历史）：以下 14 件原按旧判据登记的"内容贴底(bottom>95%)"存量，在判据
    # 方向修正后属合规行为（贴底 = 物件落地的必要条件），已全部销账：
    # ancient-locust / bamboo / drying-rack / firewood-pile / forge-anvil / haystack /
    # millstone / mountain-pine / notice-board / reeds / rubble / village-well /
    # washing-stone / willow
}


def probe_art(art_rel, assets_root=ART_ROOT):
    """读取单张立绘的全部特征。文件缺失返回 None。"""
    if not art_rel:
        return None
    path = os.path.join(assets_root, art_rel + '.png')
    if not os.path.exists(path):
        return None
    im = Image.open(path)
    mode = im.mode
    rgba = im.convert('RGBA')
    alpha = rgba.getchannel('A')
    a_min, a_max = alpha.getextrema()
    w, h = rgba.size
    corners = [alpha.getpixel((0, 0)), alpha.getpixel((w - 1, 0)),
               alpha.getpixel((0, h - 1)), alpha.getpixel((w - 1, h - 1))]
    mask = alpha.point(lambda p: 255 if p > ALPHA_THRESHOLD else 0)
    bbox = mask.getbbox()
    if bbox is None:
        bbox = (0, 0, w, h)
    l, t, r, b = bbox
    return {
        'path': path, 'mode': mode, 'size': (w, h),
        'alpha_min': a_min, 'alpha_max': a_max, 'corners': corners,
        'bbox': (l, t, r, b),
        'w_pct': (r - l) / float(w) * 100.0,
        'h_pct': (b - t) / float(h) * 100.0,
        'left_pct': l / float(w) * 100.0,
        'right_pct': r / float(w) * 100.0,
        'top_pct': t / float(h) * 100.0,
        'bottom_pct': b / float(h) * 100.0,
    }


def violations_for(probe, plot_aspects=None):
    """收集单个文件的全部违规判据项。返回 list[(crit_key, message)]。

    plot_aspects: 该 art 全部引用地块的 [(plot_id, 宽高比)] 列表。
    判据 2（MAP-22 第二轮升级，多引用任一匹配）：
    - 全部引用地块里至少一块的 plot 宽高比与画布匹配（err ≤ 0.005）即 PASS——
      这件画布至少适配它的一个用途；
    - 全部引用都不匹配才 FAIL，且消息列出每个引用地块的宽高比与 err。
    旧判据（固定 960x960、取第一块）写死于 MAP-17 时代，已被 MAP-14 非正方形画布规格淘汰。
    """
    if probe is None:
        return [(CRIT_MISSING, '文件缺失')]
    v = []
    w, h = probe['size']
    if plot_aspects:
        canvas_aspect = w / float(h)
        errs = [(pid, abs(canvas_aspect / pa - 1.0)) for pid, pa in plot_aspects]
        if all(e > CANVAS_ASPECT_TOL for _, e in errs):
            detail = '; '.join('%s plot %.4f err %.4f' % (pid, pa, e)
                               for (pid, e), (_, pa) in zip(errs, plot_aspects))
            v.append((CRIT_CANVAS, '画布宽高比与全部引用地块不匹配(实测 %.4f; %s; 阈值 %.3f)'
                      % (canvas_aspect, detail, CANVAS_ASPECT_TOL)))
    if probe['mode'] != 'RGBA':
        v.append((CRIT_MODE, '色彩通道非 RGBA(实测 %s)' % probe['mode']))
    if probe['alpha_min'] != ALPHA_ZERO:
        v.append((CRIT_ALPHA, 'alpha 最小值非 0(实测 %d,整图未抠底)' % probe['alpha_min']))
    corners = probe['corners']
    if any(a != ALPHA_ZERO for a in corners):
        v.append((CRIT_CORNERS, '四角 alpha 残留(实测 %s)' % corners))
    l, t, r, b = probe['bbox']
    edge = []
    if l < EDGE_LEFT * w:
        edge.append('left=%d(%.1f%%)' % (l, l / float(w) * 100.0))
    if r > EDGE_RIGHT * w:
        edge.append('right=%d(%.1f%%)' % (r, r / float(w) * 100.0))
    if t < EDGE_TOP * h:
        edge.append('top=%d(%.1f%%)' % (t, t / float(h) * 100.0))
    # MAP-25 方向修正:bottom 判据与前三项方向相反——**内容必须贴底**(物件才落地),
    # bottom < 95% 即违规(内容未贴底 = 浮空)。贴底(99%)是合规的,不再是违规。
    if b < EDGE_BOTTOM * h:
        edge.append('bottom=%d(%.1f%%)未贴底/浮空' % (b, b / float(h) * 100.0))
    if edge:
        v.append((CRIT_EDGE, '内容贴边: ' + ', '.join(edge)))
    return v


def load_plots(path=PLOTS_JSON):
    with io.open(path, 'r', encoding='utf-8') as f:
        doc = json.load(f)
    return doc.get('plots') or []


def collect_referenced(plots):
    """返回 {art_rel: [plot_ids]}。"""
    d = {}
    for p in plots:
        art = p.get('art')
        pid = p.get('id', '<unknown>')
        if art:
            d.setdefault(art, []).append(pid)
    return d


def find_orphans(plots, props_dir=PROPS_DIR):
    """Art/Props/ 下有但无任何地块引用的 PNG(不带 .png 后缀的相对路径)。"""
    referenced = set(collect_referenced(plots).keys())
    orphans = []
    if os.path.isdir(props_dir):
        for f in sorted(os.listdir(props_dir)):
            if f.lower().endswith('.png'):
                art = 'Art/Props/' + os.path.splitext(f)[0]
                if art not in referenced:
                    orphans.append(art)
    return orphans


def check_all(plots, assets_root=ART_ROOT, props_dir=PROPS_DIR, baseline=None):
    """完整判定。返回 (problems, warns, rows)。

    problems: FAIL 硬违规(含台账可销账、含台账外新增判据项)
    warns: 存量台账违规保持 / 孤儿 WARN
    rows: --report 用行
    """
    if baseline is None:
        baseline = ART_BASELINE
    problems = []
    warns = []
    rows = []
    referenced = collect_referenced(plots)
    plots_by_id = {p.get('id'): p for p in plots}

    for art in sorted(referenced.keys()):
        plot_ids = referenced[art]
        # MAP-22 第二轮：画布宽高比判据改为"多引用任一匹配"，传入全部引用地块的宽高比
        plot_aspects = []
        for pid in plot_ids:
            first = plots_by_id.get(pid)
            if first and first.get('width') and first.get('height'):
                plot_aspects.append((pid, float(first['width']) / float(first['height'])))
        probe = probe_art(art, assets_root)
        violations = violations_for(probe, plot_aspects)
        ref_count = len(plot_ids)

        entry = {
            'art': art, 'size': '-', 'mode': '-', 'alpha_range': '-', 'bbox': '-',
            'w_pct': 0.0, 'h_pct': 0.0, 'bottom_pct': 0.0,
            'ref_count': ref_count, 'status': 'PASS',
        }
        if probe is not None:
            entry.update({
                'size': '%dx%d' % probe['size'],
                'mode': probe['mode'],
                'alpha_range': '%d,%d' % (probe['alpha_min'], probe['alpha_max']),
                'bbox': '%d,%d,%d,%d' % probe['bbox'],
                'w_pct': round(probe['w_pct'], 1),
                'h_pct': round(probe['h_pct'], 1),
                'bottom_pct': round(probe['bottom_pct'], 1),
            })

        if violations:
            cur_keys = set(k for k, _ in violations)
            msgs = '；'.join(m for _, m in violations)
            if art in baseline:
                registered = set(baseline[art].get('criteria') or ())
                new_keys = cur_keys - registered
                if new_keys:
                    problems.append('新增违规判据项: %s(当前: %s;台账仅登记: %s)'
                                    % (art, msgs, sorted(registered)))
                    entry['status'] = 'FAIL'
                else:
                    warns.append('存量违规: %s(台账登记: %s;当前: %s)'
                                 % (art, baseline[art].get('note', ''), msgs))
                    entry['status'] = 'WARN'
            else:
                problems.append('新增违规: %s(%s)' % (art, msgs))
                entry['status'] = 'FAIL'
        else:
            if art in baseline:
                problems.append('台账可销账: %s 当前全部合规(原登记: %s),请从 ART_BASELINE 销账删除'
                                % (art, baseline[art].get('note', '')))
                entry['status'] = 'FAIL(可销账)'
            else:
                entry['status'] = 'PASS'
        rows.append(entry)

    # 判据 7:孤儿
    for art in find_orphans(plots, props_dir):
        warns.append('孤儿文件: %s(Art/Props 下无任何地块引用;只登记不 FAIL)' % art)

    return problems, warns, rows


def print_report(plots, assets_root=ART_ROOT, props_dir=PROPS_DIR):
    """--report 模式:只打印不判定,退出码恒 0。"""
    referenced = collect_referenced(plots)
    hdr = ('%-26s | %-9s | %-6s | %-11s | %-17s | %-6s | %-6s | %-6s | %s'
           % ('art路径', '尺寸', '通道', 'alpha范围', '内容bbox(l,t,r,b)',
              '占宽%', '占高%', '底边%', '引用地块数'))
    print('=== 立绘资产完整性全表(MAP-17 --report;只打印不判定,rc 恒 0) ===')
    print(hdr)
    print('-' * len(hdr))
    for art in sorted(referenced.keys()):
        probe = probe_art(art, assets_root)
        n = len(referenced[art])
        if probe is None:
            print('%-26s | %-9s | %-6s | %-11s | %-17s | %-6s | %-6s | %-6s | %d'
                  % (art, 'MISSING', '-', '-', '-', '-', '-', '-', n))
            continue
        print('%-26s | %-9s | %-6s | %-11s | %-17s | %-6.1f | %-6.1f | %-6.1f | %d'
              % (art, '%dx%d' % probe['size'], probe['mode'],
                 '%d,%d' % (probe['alpha_min'], probe['alpha_max']),
                 '%d,%d,%d,%d' % probe['bbox'],
                 probe['w_pct'], probe['h_pct'], probe['bottom_pct'], n))
    orphans = find_orphans(plots, props_dir)
    print('-' * len(hdr))
    print('被引用 art 件数: %d | 孤儿件数: %d' % (len(referenced), len(orphans)))
    if orphans:
        print('孤儿清单: %s' % ', '.join(orphans))
    print('RESULT: REPORT(只打印不判定)')
    return 0


# ---------- selftest ----------

def _mk_rgba_center(path, w=CANVAS_W, h=CANVAS_H, box=(200, 90, 760, 950)):
    # MAP-25：bottom 判据方向翻转后，默认夹具必须是"内容贴底"（底线 950/960=98.96%）；
    # 旧默认 (200,200,760,760) 底线只有 79.2%，在新口径下属"未贴底/浮空"违规。
    im = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    ImageDraw.Draw(im).rectangle(list(box), fill=(128, 64, 32, 255))
    im.save(path)


def _mk_case_dir(td, name):
    root = os.path.join(td, name)
    props = os.path.join(root, 'Art', 'Props')
    os.makedirs(props, exist_ok=True)
    return root, props


def _run_case(td, name, makers, plots, baseline, want_rc, want_kw):
    """造图 + 跑判定，返回 (ok, got_rc, text)。"""
    root, props = _mk_case_dir(td, name)
    for fname, maker in makers:
        maker(os.path.join(props, fname + '.png'))
    probs, warns, _rows = check_all(plots, assets_root=root, props_dir=props, baseline=baseline)
    got_rc = 1 if probs else 0
    text = ' '.join(probs + warns)
    if got_rc != want_rc:
        return False, got_rc, text
    if want_kw is not None and want_kw not in text:
        return False, got_rc, text
    return True, got_rc, text


def _good_maker(path):
    _mk_rgba_center(path)


def _non960_maker(path):
    # MAP-22：画布判据已升级为宽高比匹配，用例改为"正方形画布配 1.03 plot"（宽高比不匹配 -> FAIL）
    # MAP-25：同步贴底（底线 506/512=98.8%），使该用例只测画布判据、不混入 bottom 判据
    _mk_rgba_center(path, w=512, h=512, box=(60, 30, 450, 506))


def _non_square_ok_maker(path):
    # MAP-22 新增：非正方形合法画布（1059x1024，宽高比 1.034）配同比例 plot -> PASS
    # MAP-25：同步贴底（底线 1013/1024=98.9%）——合法画布 + 贴底才是完整"合格件"
    _mk_rgba_center(path, w=1059, h=1024, box=(200, 100, 860, 1013))


def _rgb_maker(path):
    im = Image.new('RGB', (CANVAS_W, CANVAS_H), (200, 100, 50))
    im.save(path)


def _opaque_maker(path):
    im = Image.new('RGBA', (CANVAS_W, CANVAS_H), (100, 50, 25, 255))
    ImageDraw.Draw(im).rectangle([200, 200, 760, 760], fill=(128, 64, 32, 255))
    im.save(path)


def _corner_maker(path):
    _mk_rgba_center(path)
    im = Image.open(path)
    im.putpixel((0, 0), (255, 255, 255, 200))
    im.save(path)


def _edge_maker(kind):
    def maker(path):
        im = Image.new('RGBA', (CANVAS_W, CANVAS_H), (0, 0, 0, 0))
        d = ImageDraw.Draw(im)
        if kind == 'top':
            d.rectangle([200, 0, 760, 100], fill=(128, 64, 32, 255))
        elif kind == 'bottom':
            # MAP-25：bottom 判据方向翻转后,"贴底"是合规的;该用例改为"内容未贴底(浮空)"——
            # 底线 700/960=72.9% < EDGE_BOTTOM(95%) -> 违规
            d.rectangle([200, 200, 760, 700], fill=(128, 64, 32, 255))
        elif kind == 'left':
            d.rectangle([0, 200, 100, 760], fill=(128, 64, 32, 255))
        else:
            d.rectangle([860, 200, CANVAS_W - 1, 760], fill=(128, 64, 32, 255))
        im.save(path)
    return maker


def _p(art_base, name):
    return 'Art/Props/' + name


def selftest():
    """>=10 条用例点火：好例/画布比例/非正方形合法/RGB/全不透明/角残留/四向贴边/文件缺失/孤儿/台账保持/台账可销账/新增判据项。"""
    cases = []  # (name, makers, plots, baseline, want_rc, want_kw)

    # 1 好例 -> PASS rc=0
    cases.append(('1_好例_合格立绘_PASS',
                  [('good', _good_maker)],
                  [{'id': 'g', 'art': _p('', 'good')}], {}, 0, None))

    # 2 画布宽高比与 plot 不匹配 -> FAIL（MAP-22：正方形图配 1.03 plot）
    cases.append(('2_画布比例不匹配_FAIL',
                  [('non960', _non960_maker)],
                  [{'id': 'g', 'art': _p('', 'non960'), 'width': 527, 'height': 512}], {}, 1, '画布宽高比'))

    # 2b 非正方形合法画布配同比例 plot -> PASS（MAP-22 新增，1059x1024 配 1.034 plot）
    cases.append(('2b_非正方形合法画布_PASS',
                  [('nonsq', _non_square_ok_maker)],
                  [{'id': 'g', 'art': _p('', 'nonsq'), 'width': 300, 'height': 290}], {}, 0, None))

    # 2c 多引用任一匹配 -> PASS（MAP-22 第二轮：两块 plot 宽高比互不相同，画布匹配其一）
    cases.append(('2c_多引用任一匹配_PASS',
                  [('nonsq', _non_square_ok_maker)],
                  [{'id': 'g1', 'art': _p('', 'nonsq'), 'width': 300, 'height': 290},
                   {'id': 'g2', 'art': _p('', 'nonsq'), 'width': 240, 'height': 386}], {}, 0, None))

    # 2d 多引用全不匹配 -> FAIL 且消息列出全部引用地块（MAP-22 第二轮）
    cases.append(('2d_多引用全不匹配_FAIL',
                  [('nonsq', _non_square_ok_maker)],
                  [{'id': 'g1', 'art': _p('', 'nonsq'), 'width': 140, 'height': 180},
                   {'id': 'g2', 'art': _p('', 'nonsq'), 'width': 60, 'height': 150}], {}, 1, '全部引用地块'))

    # 3 RGB 无通道 -> FAIL
    cases.append(('3_RGB无通道_FAIL',
                  [('rgb', _rgb_maker)],
                  [{'id': 'g', 'art': _p('', 'rgb')}], {}, 1, '通道非 RGBA'))

    # 4 alpha 全不透明 -> FAIL
    cases.append(('4_alpha全不透明_FAIL',
                  [('opaque', _opaque_maker)],
                  [{'id': 'g', 'art': _p('', 'opaque')}], {}, 1, '未抠底'))

    # 5 角残留 -> FAIL
    cases.append(('5_角残留_FAIL',
                  [('corner', _corner_maker)],
                  [{'id': 'g', 'art': _p('', 'corner')}], {}, 1, '四角'))

    # 6 四向贴边 -> FAIL x4（MAP-25：bottom 方向翻转，该项语义改为"内容未贴底/浮空"）
    for i, kind in enumerate(('top', 'bottom', 'left', 'right')):
        is_bottom = kind == 'bottom'
        cases.append(('6%s_%s_FAIL' % ('abcd'[i], '未贴底' if is_bottom else '贴' + kind),
                      [(kind, _edge_maker(kind))],
                      [{'id': 'g', 'art': _p('', kind)}], {}, 1,
                      '未贴底' if is_bottom else '内容贴边'))

    # 7 文件缺失 -> FAIL
    cases.append(('7_文件缺失_FAIL',
                  [],
                  [{'id': 'g', 'art': _p('', 'no-such-file')}], {}, 1, '缺失'))

    # 8 孤儿文件 -> WARN rc=0
    cases.append(('8_孤儿文件_WARN_rc0',
                  [('good', _good_maker), ('orphan', _good_maker)],
                  [{'id': 'g', 'art': _p('', 'good')}], {}, 0, '孤儿文件'))

    # 9 台账内违规保持(仅登记 edge 判据) -> WARN rc=0
    cases.append(('9_台账内违规保持_WARN_rc0',
                  [('bad', _edge_maker('bottom'))],
                  [{'id': 'g', 'art': _p('', 'bad')}],
                  {'Art/Props/bad': {'criteria': {CRIT_EDGE}, 'note': '历史贴底'}},
                  0, '存量违规'))

    # 10 台账可销账(已合规但台账未删) -> FAIL
    cases.append(('10_台账可销账_FAIL',
                  [('good', _good_maker)],
                  [{'id': 'g', 'art': _p('', 'good')}],
                  {'Art/Props/good': {'criteria': {CRIT_EDGE}, 'note': '曾经贴底'}},
                  1, '可销账'))

    # 11 台账内出现新增判据项(台账只登记 edge,图实际是画布违规) -> FAIL
    cases.append(('11_台账内新增判据项_FAIL',
                  [('non960', _non960_maker)],
                  [{'id': 'g', 'art': _p('', 'non960'), 'width': 527, 'height': 512}],
                  {'Art/Props/non960': {'criteria': {CRIT_EDGE}, 'note': '只登记过贴边'}},
                  1, '新增违规判据项'))

    bad = []
    tmp_root = os.path.join(REPO, '.batch', 'tmp')
    os.makedirs(tmp_root, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=tmp_root) as td:
        for name, makers, plots, baseline, want_rc, want_kw in cases:
            ok, got_rc, text = _run_case(td, name, makers, plots, baseline, want_rc, want_kw)
            if ok:
                print('  [PASS] %-30s -> rc=%d, 断言: %s'
                      % (name, got_rc, want_kw if want_kw else 'PASS'))
            else:
                bad.append('%s: 期望 rc=%d kw=%s, 实得 rc=%d, 输出: %s'
                           % (name, want_rc, want_kw, got_rc, text[:200]))

    if bad:
        for b in bad:
            print('SELFTEST FAIL: ' + b)
        print('RESULT: FAIL(selftest %d/%d 条不点火)' % (len(bad), len(cases)))
        return 1
    print('SELFTEST: %d/%d 条用例全点火(好例/画布比例/非正方形合法/RGB/全不透明/角残留/四向贴边/文件缺失/孤儿/台账保持/台账可销账/新增判据项)'
          % (len(cases), len(cases)))
    print('RESULT: PASS(selftest)')
    return 0


def main():
    ap = argparse.ArgumentParser(description='MAP-17 立绘资产完整性棘轮门禁')
    ap.add_argument('--selftest', action='store_true', help='合成图用例自检')
    ap.add_argument('--report', action='store_true', help='只打印全表不判定,rc 恒 0')
    ap.add_argument('--plots', default=PLOTS_JSON, help='地块配置路径')
    ap.add_argument('--art-root', default=ART_ROOT, help='美术根目录')
    ap.add_argument('--props-dir', default=PROPS_DIR, help='Art/Props 目录')
    args = ap.parse_args()

    if args.selftest:
        return selftest()

    plots = load_plots(args.plots)

    if args.report:
        return print_report(plots, assets_root=args.art_root, props_dir=args.props_dir)

    problems, warns, rows = check_all(plots, assets_root=args.art_root,
                                      props_dir=args.props_dir)
    for w in warns:
        print('WARN: ' + w)
    for p in problems:
        print('FAIL: ' + p)

    failed = sum(1 for r in rows if r['status'].startswith('FAIL'))
    warned = sum(1 for r in rows if r['status'] == 'WARN')
    passed = sum(1 for r in rows if r['status'] == 'PASS')
    print('SUMMARY: %d 件被引用立绘(合规 %d / 台账存量 WARN %d / FAIL %d),%d 项违规,%d 项警告'
          % (len(rows), passed, warned, failed, len(problems), len(warns)))
    if problems:
        print('RESULT: FAIL(%d 项违规)' % len(problems))
        return 1
    if warns:
        print('RESULT: PASS_WITH_WARN(%d 项存量/孤儿警告,退出码 0 放行)' % len(warns))
        return 0
    print('RESULT: PASS')
    return 0


if __name__ == '__main__':
    sys.exit(main())