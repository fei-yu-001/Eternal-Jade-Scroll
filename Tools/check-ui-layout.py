#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""界面布局门禁:机械抓「面板被盖住」与「可疑的 y 符号」两类缺陷。

治的腐化:靠人眼看截图才发现"面板整块被别的面板盖住"、"纸偶飘到面板外面"。
两个已确诊的真缺陷就是本工具的验收靶子:
  - T128: FrontEndCharacters.cs 的 "Walk sample" 面板被 "Biography" 面板整块盖住
    (重叠 220816 px^2 = walk 面板面积的 97%,UGUI 中子节点序靠后者画在上层);
  - T127: 同文件 PuppetActor.Create 传 new Vector2(250f, 214f)——UiKit.Rect 的约定是
    anchoredPosition = new Vector2(x, -y)(y 向下,取负),PuppetActor.cs:220 直接
    root.anchoredPosition = basePoint,正的 y 让纸偶飘到面板上边界之上。

判据(与架构师裁决逐条一致):
  A. 同页面板两两重叠:扫 Presentation/*.cs,抓 ui.Panel( / ui.Rect( / ui.ChipPanel(
     的字面量坐标(name, x, y, w, h),按所在方法归组(同一方法 ≈ 同一页面),
     算两两相交面积:相交 >= 较小面板面积的 80% => FAIL(整块被吃掉);
     10% ~ 80% => WARN(局部遮挡常见,不判红);< 10% 或不相交 => 忽略。
     UGUI 子节点序:行号靠前 = 先挂 = 画在下层,"整块被盖住"只判面积较小且先挂的那块;
     较小者后挂(画在上层)是「大底板 + 内容面板」的正常叠印设计,不判。
     面板只跟面板比。ui.Button 不进 UI_CALL_RE:按钮落在面板里是常态,并进去会把「按钮⊂面板」算成整块遮挡。
  B. 可疑的 y 符号:抓 PuppetActor.Create( 的第 3 参与 .SetPosition( 的第 1 参,
     从中取 new Vector2(x, y),y 为正的字面量 => FAIL(其余调用点全取负,见
     FrontEndCombat.cs:1128 / FrontEndWorld.cs:398);非字面量(表达式/变量)不判。
  C. 按钮只跟按钮比(T132):独立扫 ui.Button(。与 Panel 同形,跳过 parent:
     第 1 段是按钮文字(args[1]),第 2–5 段是 x,y,w,h(args[2:6])。同一方法里两两相交,
     阈值与 A 相同(>=80% 判红,10%~80% WARN)。按钮都是命中目标,不沿用面板那条
     「较小者后挂就是叠印」豁免——小按钮压在大按钮上照样挡点击。判据不看控制流
     (互斥分支的字面量矩形仍算重叠);已有的这类判红按存量台账登记,level='red',不放宽阈值。

存量台账(与 Tools/PropChecks/check-prop-art.py 同款棘轮):
  - 不在台账的 WARN 级重叠 => WARN(提示登记);
  - 在台账且仍为 WARN => WARN(存量放行,rc=0);
  - 在台账但升到 >=80% => FAIL(台账不许放宽判据,登记只覆盖 WARN 级);
  - 判红级存量须在台账写 level:'red'(仍违规 => WARN 放行,只许销账不许放宽);
  - 在台账但已不再重叠 => 报「台账可销账」并 FAIL;
  - 台账对 B 判据同样生效(存量带 id 与理由 => WARN)。台账只许销账、不许放宽。
  - 台账对 C 判据同样生效(键前缀 C|;判红级存量写 level:'red')。

--report 模式:只打印不判定,退出码恒 0,输出全部字面量面板与两两重叠比率全表。

自检:python Tools/check-ui-layout.py --selftest(合成用例含"整块被盖住"判红、
"轻微边角重叠"判绿、y 符号正负、跨方法不判、跨行调用、ChipPanel/Rect、台账三分支、
两按钮整块重合判红、按钮在面板内判绿)。
"""
import argparse
import os
import re
import sys
import tempfile

# UTF-8 控制台与管道守卫
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
if hasattr(sys.stderr, 'reconfigure'):
    sys.stderr.reconfigure(encoding='utf-8', errors='replace')

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
DEFAULT_SCAN_DIR = os.path.join(REPO, 'GameClient', 'TwelveJade', 'Assets', 'Scripts', 'Presentation')

RED_RATIO = 0.80    # 相交 >= 较小面板面积的 80% => 判红(整块被吃掉)
WARN_RATIO = 0.10   # 相交 10% ~ 80% => WARN(局部遮挡常见)

# 判据键:台账「登记了哪些判据项」,新增判据项/升级不能被台账掩盖
CRIT_OVERLAP_WARN = 'overlap-warn'
CRIT_POSITIVE_Y = 'positive-y'
CRIT_BUTTON_OVERLAP = 'button-overlap'

NUM_RE = r'[+-]?(?:\d+\.?\d*|\.\d+)f?'
NUM_ONLY_RE = re.compile(r'^%s$' % NUM_RE)
STR_ONLY_RE = re.compile(r'^"(?:[^"\\]|\\.)*"$')
VEC2_RE = re.compile(r'new\s+Vector2\s*\(\s*(%s)\s*,\s*(%s)\s*\)' % (NUM_RE, NUM_RE))
UI_CALL_RE = re.compile(r'\bui\.(?:Panel|Rect|ChipPanel)\s*\(')
# 按钮单独扫。禁止并进 UI_CALL_RE:面板内按钮会被算成面板重叠(满屏假红,T132)。
BUTTON_CALL_RE = re.compile(r'\bui\.Button\s*\(')
NAMED_ARG_RE = re.compile(r'^[A-Za-z_]\w*\s*:')
PUPPET_CREATE_RE = re.compile(r'\bPuppetActor\.Create\s*\(')
SETPOS_RE = re.compile(r'\b(\w+)\.SetPosition\s*\(')

# 初始台账:2026-10-13 实跑现状逐条登记(id | 理由)。判据 A 的条目须写 level:
#   'red' = 判红级存量(仍违规时 WARN 放行), 缺省/'warn' = WARN 级存量(升级判红则 FAIL,不许放宽)。
# 台账只许销账、不许放宽判据;T128(Walk sample 被 Biography 整块盖住)与
# T127(PuppetActor.Create 正 y)是待修真缺陷,严禁登记进台账洗白。
BASELINE = {
    # LAY-01 全屏水墨底板先挂,氛围层后挂同矩形叠印,有意分层,非遮挡
    'A|FrontEndController.cs|BuildBackdrop|Ink base#1+Atmosphere#1':
        {'criteria': {CRIT_OVERLAP_WARN}, 'level': 'red',
         'note': 'LAY-01 水墨底板叠氛围层,有意叠印'},
    # LAY-02 败北结算面板被全屏渐黑罩住,有意压暗整屏
    'A|FrontEndCombat.cs|SettleDefeat|Combat settle#1+Defeat fade#1':
        {'criteria': {CRIT_OVERLAP_WARN}, 'level': 'red',
         'note': 'LAY-02 败北渐黑罩结算屏,有意压暗'},
    # LAY-03/04 档位卡空档按钮与已有档的继续/删除写在 if/else 互斥分支
    # (FrontEndCharacters.cs ShowSlots)。字面量矩形整块重合,运行时不同屏;判据不看控制流,
    # 按判红级存量登记,不把互斥写成豁免,也不放宽 80% 阈值。
    'C|FrontEndCharacters.cs|ShowSlots|写下新的人生#1+继续#1':
        {'criteria': {CRIT_BUTTON_OVERLAP}, 'level': 'red',
         'note': 'LAY-03 空档按钮与继续按钮互斥分支,字面量整块重合,不同时出现'},
    'C|FrontEndCharacters.cs|ShowSlots|写下新的人生#1+删除#1':
        {'criteria': {CRIT_BUTTON_OVERLAP}, 'level': 'red',
         'note': 'LAY-04 空档按钮与删除按钮互斥分支,字面量整块重合,不同时出现'},
}


def num_value(arg):
    """字面量数字 -> float;不是纯字面量返回 None。"""
    s = arg.strip()
    if not NUM_ONLY_RE.match(s):
        return None
    return float(s.rstrip('f'))


def str_value(arg):
    """字面量字符串(带引号)-> 内容;否则 None。"""
    s = arg.strip()
    if not STR_ONLY_RE.match(s):
        return None
    return s[1:-1]


def split_top_args(s):
    """按顶层逗号拆实参(忽略嵌套括号与字符串里的逗号)。"""
    parts, cur = [], []
    depth = 0
    instr = False
    i = 0
    while i < len(s):
        c = s[i]
        if instr:
            cur.append(c)
            if c == '\\' and i + 1 < len(s):
                cur.append(s[i + 1])
                i += 2
                continue
            if c == '"':
                instr = False
        elif c == '"':
            instr = True
            cur.append(c)
        elif c in '([':
            depth += 1
            cur.append(c)
        elif c in ')]':
            depth -= 1
            cur.append(c)
        elif c == ',' and depth == 0:
            parts.append(''.join(cur).strip())
            cur = []
        else:
            cur.append(c)
        i += 1
    tail = ''.join(cur).strip()
    if tail:
        parts.append(tail)
    return parts


def fold_call(text, open_idx):
    """从 '(' 下标起取到括号配平,返回 (括号内文本, 结束下标)。跨行折叠为空格。"""
    depth = 0
    instr = False
    i = open_idx
    out = []
    n = len(text)
    while i < n:
        c = text[i]
        if instr:
            out.append(c if c != '\n' else ' ')
            if c == '\\' and i + 1 < n:
                out.append(text[i + 1] if text[i + 1] != '\n' else ' ')
                i += 2
                continue
            if c == '"':
                instr = False
        elif c == '"':
            instr = True
            out.append(c)
        elif c == '(':
            depth += 1
            if depth > 1:
                out.append(c)
        elif c == ')':
            depth -= 1
            if depth == 0:
                return ''.join(out), i
            out.append(c)
        else:
            out.append(c if c != '\n' else ' ')
        i += 1
    return ''.join(out), n - 1


def method_at(lines):
    """逐行推当前方法名(启发式:带修饰符/形如 Type Name(...) 的声明行)。"""
    cur = '<top>'
    names = []
    for raw in lines:
        s = raw.strip()
        name = None
        if s and not s.startswith('//') and not s.endswith(';') and '(' in s \
                and '=' not in s.split('(')[0] and not s.startswith('['):
            m = re.match(r'(?:public|private|protected|internal|static|async|override|virtual)\s+'
                         r'[\w<>\[\],\.\?]+\s+(\w+)\s*\(', s)
            if m:
                name = m.group(1)
            else:
                m = re.match(r'(?:[\w<>\[\],\.\?]+\s+)?(\w+)\s*\([^;]*\)\s*$', s)
                if m and not re.match(r'(?:if|for|foreach|while|switch|catch|using|return|'
                                      r'else|new|case|lock|nameof)\b', s):
                    name = m.group(1)
        if name:
            cur = name
        names.append(cur)
    return names


def line_of(text, idx):
    return text.count('\n', 0, idx) + 1


def scan_panels(text, fname, method_names):
    """抓 ui.Panel/Rect/ChipPanel 的字面量坐标。返回面板列表。"""
    panels = []
    for m in UI_CALL_RE.finditer(text):
        args_text, _end = fold_call(text, m.end() - 1)
        args = split_top_args(args_text)
        if len(args) < 6:
            continue
        name = str_value(args[1])
        if name is None:
            continue
        xs = [num_value(a) for a in args[2:6]]
        if any(v is None for v in xs):
            continue  # 非字面量坐标,机械判不了,不硬猜
        x, y, w, h = xs
        if w <= 0 or h <= 0:
            continue
        ln = line_of(text, m.start())
        panels.append({
            'file': fname, 'line': ln, 'name': name,
            'x': x, 'y': y, 'w': w, 'h': h,
            'method': method_names[ln - 1] if ln - 1 < len(method_names) else '<top>',
            'kind': m.group(0).strip().split('(')[0].strip(),
        })
    return panels


def scan_buttons(text, fname, method_names):
    """抓 ui.Button 的字面量坐标(与 Panel 同形:文字在 args[1],x,y,w,h 在 args[2:6])。"""
    buttons = []
    for m in BUTTON_CALL_RE.finditer(text):
        args_text, _end = fold_call(text, m.end() - 1)
        args = split_top_args(args_text)
        if len(args) < 6:
            continue
        # parent: 之类的命名实参会打乱位置取参,机械判不了,整条跳过
        if any(NAMED_ARG_RE.match(a) for a in args[:6]):
            continue
        name = str_value(args[1])
        if name is None:
            continue
        xs = [num_value(a) for a in args[2:6]]
        if any(v is None for v in xs):
            continue
        x, y, w, h = xs
        if w <= 0 or h <= 0:
            continue
        ln = line_of(text, m.start())
        buttons.append({
            'file': fname, 'line': ln, 'name': name,
            'x': x, 'y': y, 'w': w, 'h': h,
            'method': method_names[ln - 1] if ln - 1 < len(method_names) else '<top>',
            'kind': 'ui.Button',
        })
    return buttons


def scan_position_calls(text, fname, method_names):
    """抓 PuppetActor.Create( 第 3 参 / .SetPosition( 第 1 参里的字面量 Vector2。"""
    hits = []
    # PuppetActor.Create:basePoint 是第 3 参(下标 2);.SetPosition:第 1 参(下标 0)
    spans = [(mm.start(), mm.end(), 'PuppetActor.Create', 2, None)
             for mm in PUPPET_CREATE_RE.finditer(text)]
    spans += [(mm.start(), mm.end(), mm.group(1) + '.SetPosition', 0, None)
              for mm in SETPOS_RE.finditer(text)]
    for start, end, label, arg_index, _recv in spans:
            args_text, _close = fold_call(text, text.index('(', end - 1))
            args = split_top_args(args_text)
            if len(args) <= arg_index:
                continue
            vm = VEC2_RE.search(args[arg_index])
            if not vm:
                continue  # Vector2.zero / 表达式 / 非字面量:不判(其余调用点均为负 y 或表达式)
            vx, vy = float(vm.group(1).rstrip('f')), float(vm.group(2).rstrip('f'))
            ln = line_of(text, start)
            hits.append({
                'file': fname, 'line': ln, 'call': label,
                'vx': vx, 'vy': vy,
                'method': method_names[ln - 1] if ln - 1 < len(method_names) else '<top>',
            })
    return hits


def overlap_ratio(a, b):
    """两矩形相交面积 / 较小面板面积;不相交返回 0。UiKit 坐标 y 向下,取向不影响面积。"""
    ix = max(0.0, min(a['x'] + a['w'], b['x'] + b['w']) - max(a['x'], b['x']))
    iy = max(0.0, min(a['y'] + a['h'], b['y'] + b['h']) - max(a['y'], b['y']))
    inter = ix * iy
    if inter <= 0:
        return 0.0
    smaller = min(a['w'] * a['h'], b['w'] * b['h'])
    if smaller <= 0:
        return 0.0
    return inter / smaller


def pair_key(file, method, n1, i1, n2, i2):
    return 'A|%s|%s|%s#%d+%s#%d' % (file, method, n1, i1, n2, i2)


def pos_key(hit):
    return 'B|%s|%s|%s' % (hit['file'], hit['method'], hit['call'])


def rect_str(p):
    return '(%d,%d,%d,%d)' % (p['x'], p['y'], p['w'], p['h'])


def check_dir(scan_dir, baseline=None):
    """完整判定。返回 (fails, warns, pair_rows, pos_rows, btn_rows)。

    fails/warns: FAIL 与 WARN 文本行;pair_rows/pos_rows/btn_rows: --report 与 SUMMARY 用。
    """
    if baseline is None:
        baseline = BASELINE
    fails, warns = [], []
    pair_rows, pos_rows, btn_rows = [], [], []

    cs_files = []
    for root, _dirs, files in os.walk(scan_dir):
        for f in sorted(files):
            if f.endswith('.cs'):
                cs_files.append(os.path.join(root, f))
    cs_files.sort()

    all_panels = []
    all_buttons = []
    all_pos = []
    for path in cs_files:
        with open(path, 'r', encoding='utf-8', errors='replace') as fh:
            text = fh.read()
        fname = os.path.basename(path)
        lines = text.splitlines()
        method_names = method_at(lines)
        all_panels.extend(scan_panels(text, fname, method_names))
        all_buttons.extend(scan_buttons(text, fname, method_names))
        all_pos.extend(scan_position_calls(text, fname, method_names))

    # ---- 判据 A:按 (文件, 方法) 归组两两重叠 ----
    groups = {}
    for p in all_panels:
        groups.setdefault((p['file'], p['method']), []).append(p)
    for (fname, method), panels in sorted(groups.items()):
        counter = {}
        for p in panels:
            counter[p['name']] = counter.get(p['name'], 0) + 1
            p['occ'] = counter[p['name']]
        for i in range(len(panels)):
            for j in range(i + 1, len(panels)):
                a, b = panels[i], panels[j]
                ratio = overlap_ratio(a, b)
                if ratio < WARN_RATIO:
                    continue
                # UGUI 子节点序:行号靠前 = 先挂 = 画在下层。「整块被盖住」只会发生在
                # 面积较小且先挂的那块上;若较小那块后挂(画在上层),是正常的
                # 「大底板 + 内容面板」叠印设计,不判(全屏底板/遮罩误报由此消除)。
                victim, cover = (a, b) if a['w'] * a['h'] <= b['w'] * b['h'] else (b, a)
                if victim['line'] > cover['line']:
                    continue
                row = {
                    'key': pair_key(fname, method, a['name'], a['occ'], b['name'], b['occ']),
                    'file': fname, 'method': method,
                    'under': '%s@%d%s' % (victim['name'], victim['line'], rect_str(victim)),
                    'over': '%s@%d%s' % (cover['name'], cover['line'], rect_str(cover)),
                    'ratio': ratio,
                }
                pair_rows.append(row)
                pct = ratio * 100.0
                desc = ('%s: %s 被 %s 覆盖 %.1f%%(较小面板面积占比)'
                        % (fname + ':' + method, row['under'], row['over'], pct))
                key = row['key']
                if ratio >= RED_RATIO:
                    if key in baseline:
                        # 台账三分支:判红级存量(level='red')仍违规 => WARN 放行;
                        # 台账只登记过 WARN 级却升到判红 => FAIL(不许放宽)。
                        if baseline[key].get('level') == 'red':
                            warns.append('WARN: 存量整块遮挡(台账登记: %s): %s'
                                         % (baseline[key].get('note', ''), desc))
                        else:
                            fails.append('FAIL: 台账不许放宽判据(只登记过 WARN 级,现已整块遮挡): %s | 台账注: %s'
                                         % (desc, baseline[key].get('note', '')))
                    else:
                        fails.append('FAIL: 整块被盖住(疑似 T128 同类): %s' % desc)
                else:  # WARN 级
                    if key in baseline:
                        warns.append('WARN: 存量局部遮挡(台账登记: %s): %s'
                                     % (baseline[key].get('note', ''), desc))
                    else:
                        warns.append('WARN: 未登记局部遮挡(请修正布局或在 BASELINE 登记 id+理由): %s' % desc)

    # ---- 判据 C:按钮 × 按钮(不沿用面板「较小者后挂就豁免」那条) ----
    # 按钮本身就是命中目标:小按钮压在大按钮上照样挡点击,所以不判子节点序。
    btn_groups = {}
    for p in all_buttons:
        btn_groups.setdefault((p['file'], p['method']), []).append(p)
    for (fname, method), buttons in sorted(btn_groups.items()):
        counter = {}
        for p in buttons:
            counter[p['name']] = counter.get(p['name'], 0) + 1
            p['occ'] = counter[p['name']]
        for i in range(len(buttons)):
            for j in range(i + 1, len(buttons)):
                a, b = buttons[i], buttons[j]
                ratio = overlap_ratio(a, b)
                if ratio < WARN_RATIO:
                    continue
                row = {
                    'key': 'C|%s|%s|%s#%d+%s#%d'
                           % (fname, method, a['name'], a['occ'], b['name'], b['occ']),
                    'file': fname, 'method': method,
                    'under': '%s@%d%s' % (a['name'], a['line'], rect_str(a)),
                    'over': '%s@%d%s' % (b['name'], b['line'], rect_str(b)),
                    'ratio': ratio,
                }
                btn_rows.append(row)
                pct = ratio * 100.0
                desc = ('%s: 按钮 %s 与 %s 重叠 %.1f%%(较小按钮面积占比)'
                        % (fname + ':' + method, row['under'], row['over'], pct))
                key = row['key']
                if ratio >= RED_RATIO:
                    if key in baseline:
                        if baseline[key].get('level') == 'red':
                            warns.append('WARN: 存量按钮整块重合(台账登记: %s): %s'
                                         % (baseline[key].get('note', ''), desc))
                        else:
                            fails.append('FAIL: 台账不许放宽判据(按钮重叠只登记过 WARN 级,现已整块重合): %s | 台账注: %s'
                                         % (desc, baseline[key].get('note', '')))
                    else:
                        fails.append('FAIL: 按钮整块重合(字面量矩形互相遮挡,点不到下层): %s' % desc)
                else:
                    if key in baseline:
                        warns.append('WARN: 存量按钮局部遮挡(台账登记: %s): %s'
                                     % (baseline[key].get('note', ''), desc))
                    else:
                        warns.append('WARN: 未登记按钮局部遮挡(请修正布局或在 BASELINE 登记 id+理由): %s' % desc)

    # ---- 判据 B:正的 y 字面量 ----
    for hit in all_pos:
        if hit['vy'] <= 0:
            continue
        desc = ('%s:%d %s 传 new Vector2(%g, %g),y>0 —— UiKit 约定 y 向下取负'
                '(T127 同类,PuppetActor.cs:220 直接 anchoredPosition = basePoint)'
                % (hit['file'], hit['line'], hit['call'], hit['vx'], hit['vy']))
        key = pos_key(hit)
        row = dict(hit)
        row['key'] = key
        row['desc'] = desc
        pos_rows.append(row)
        if key in baseline:
            warns.append('WARN: 存量可疑 y 符号(台账登记: %s): %s' % (baseline[key].get('note', ''), desc))
        else:
            fails.append('FAIL: 可疑的 y 符号: %s' % desc)

    # ---- 台账棘轮:登记了却不再违规 => 可销账 => FAIL(逼着清理台账) ----
    alive_a = {r['key'] for r in pair_rows}
    alive_b = {r['key'] for r in pos_rows}
    alive_c = {r['key'] for r in btn_rows}
    for key, entry in sorted(baseline.items()):
        if key.startswith('A|') and key not in alive_a:
            fails.append('FAIL: 台账可销账: %s 当前已无重叠(原登记: %s),请从 BASELINE 删除'
                         % (key, entry.get('note', '')))
        if key.startswith('B|') and key not in alive_b:
            fails.append('FAIL: 台账可销账: %s 当前已无正 y 调用(原登记: %s),请从 BASELINE 删除'
                         % (key, entry.get('note', '')))
        if key.startswith('C|') and key not in alive_c:
            fails.append('FAIL: 台账可销账: %s 当前已无按钮重叠(原登记: %s),请从 BASELINE 删除'
                         % (key, entry.get('note', '')))
    return fails, warns, pair_rows, pos_rows, btn_rows


def print_report(pair_rows, pos_rows, btn_rows=None, all_panels=None):
    """--report 模式:只打印不判定,退出码恒 0。"""
    print('=== 界面布局全表(--report;只打印不判定,rc 恒 0) ===')
    print('-- 判据 A:同方法面板两两重叠(相交 >= 较小面积 10% 才列出) --')
    if not pair_rows:
        print('(无 10% 以上的重叠对)')
    for r in pair_rows:
        tier = 'RED' if r['ratio'] >= RED_RATIO else 'WARN'
        print('%-5s %-7.2f%% | %s | %s(下层) x %s(上层) | key=%s'
              % (tier, r['ratio'] * 100.0, r['file'] + ':' + r['method'],
                 r['under'], r['over'], r['key']))
    print('-- 判据 C:同方法按钮两两重叠(相交 >= 较小面积 10% 才列出) --')
    if not btn_rows:
        print('(无 10% 以上的按钮重叠对)')
    for r in btn_rows:
        tier = 'RED' if r['ratio'] >= RED_RATIO else 'WARN'
        print('%-5s %-7.2f%% | %s | %s x %s | key=%s'
              % (tier, r['ratio'] * 100.0, r['file'] + ':' + r['method'],
                 r['under'], r['over'], r['key']))
    print('-- 判据 B:字面量正 y 的 PuppetActor.Create / .SetPosition --')
    if not pos_rows:
        print('(无)')
    for r in pos_rows:
        print('%s:%d %s new Vector2(%g, %g) | key=%s'
              % (r['file'], r['line'], r['call'], r['vx'], r['vy'], r['key']))
    print('RESULT: REPORT(只打印不判定)')
    return 0


# ---------- selftest(合成 C# 用例,全在临时目录) ----------

NS_OPEN = ('using UnityEngine;\n'
           'namespace TwelveJade.Presentation\n{\n'
           '    public sealed partial class FrontEndController\n    {\n')
NS_CLOSE = '    }\n}\n'


def wrap_cs(body):
    return NS_OPEN + body + NS_CLOSE


def run_case(td, name, files, baseline, want_rc, want_kw):
    """写合成 .cs + 跑判定,返回 (ok, got_rc, text)。"""
    root = os.path.join(td, name)
    os.makedirs(root, exist_ok=True)
    for fname, content in files.items():
        with open(os.path.join(root, fname), 'w', encoding='utf-8') as fh:
            fh.write(content)
    fails, warns, _rows, _pos, _btns = check_dir(root, baseline=baseline)
    got_rc = 1 if fails else 0
    text = ' '.join(fails + warns)
    if got_rc != want_rc:
        return False, got_rc, text
    if want_kw is not None and want_kw not in text:
        return False, got_rc, text
    return True, got_rc, text


def selftest():
    """>=10 条用例点火:整块盖住判红 / 边角重叠判绿 / y 符号 / 跨方法 / 跨行 / 台账三分支。"""
    cases = []  # (name, files, baseline, want_rc, want_kw)

    # 1 整块被盖住(walk 样例同几何) -> FAIL 判红
    cases.append(('1_整块被盖住_FAIL',
                  {'A.cs': wrap_cs(
                      '        public void ShowTest(SaveData data)\n'
                      '        {\n'
                      '            var a = ui.Panel(content, "Covered", 1054, 336, 766, 296,\n'
                      '                new Color(.06f, .14f, .12f, .9f));\n'
                      '            var b = ui.Panel(content, "Cover", 1074, 336, 746, 593,\n'
                      '                new Color(.1f, .1f, .1f, .9f));\n'
                      '        }\n')},
                  {}, 1, '整块被盖住'))

    # 2 轻微边角重叠(5%,< 10%) -> PASS 判绿
    cases.append(('2_轻微边角重叠_PASS',
                  {'A.cs': wrap_cs(
                      '        public void ShowTest(SaveData data)\n'
                      '        {\n'
                      '            ui.Panel(content, "EdgeA", 0, 0, 100, 100);\n'
                      '            ui.Panel(content, "EdgeB", 95, 0, 200, 100);\n'
                      '        }\n')},
                  {}, 0, None))

    # 3 同几何但落在不同方法 => 不归组,不判
    cases.append(('3_跨方法重叠不判_PASS',
                  {'A.cs': wrap_cs(
                      '        public void PageOne(SaveData data)\n'
                      '        {\n'
                      '            ui.Panel(content, "Solo", 1054, 336, 766, 296);\n'
                      '        }\n'
                      '        public void PageTwo(SaveData data)\n'
                      '        {\n'
                      '            ui.Panel(content, "Solo", 1074, 336, 746, 593);\n'
                      '        }\n')},
                  {}, 0, None))

    # 4 WARN 级重叠(50%)未登记 -> WARN rc0
    cases.append(('4_WARN级重叠未登记_WARN',
                  {'A.cs': wrap_cs(
                      '        public void ShowTest(SaveData data)\n'
                      '        {\n'
                      '            ui.Panel(content, "HalfA", 0, 0, 100, 100);\n'
                      '            ui.Panel(content, "HalfB", 50, 0, 200, 100);\n'
                      '        }\n')},
                  {}, 0, '未登记局部遮挡'))

    # 5 WARN 级重叠已登记台账 -> WARN rc0(存量放行)
    cases.append(('5_WARN级重叠已登记_WARN_rc0',
                  {'A.cs': wrap_cs(
                      '        public void ShowTest(SaveData data)\n'
                      '        {\n'
                      '            ui.Panel(content, "HalfA", 0, 0, 100, 100);\n'
                      '            ui.Panel(content, "HalfB", 50, 0, 200, 100);\n'
                      '        }\n')},
                  {'A|A.cs|ShowTest|HalfA#1+HalfB#1':
                   {'criteria': {CRIT_OVERLAP_WARN}, 'note': 'UI-100 高亮层压底板,历史存量'}},
                  0, '存量局部遮挡'))

    # 6 台账登记的是 WARN,实际升到 >=80% => FAIL(不许放宽)
    cases.append(('6_台账内升级到判红_FAIL',
                  {'A.cs': wrap_cs(
                      '        public void ShowTest(SaveData data)\n'
                      '        {\n'
                      '            ui.Panel(content, "HalfA", 0, 0, 100, 100);\n'
                      '            ui.Panel(content, "HalfB", 5, 0, 200, 100);\n'
                      '        }\n')},
                  {'A|A.cs|ShowTest|HalfA#1+HalfB#1':
                   {'criteria': {CRIT_OVERLAP_WARN}, 'note': '曾只是局部遮挡'}},
                  1, '不许放宽'))

    # 7 台账登记的重叠已被修复 => FAIL 可销账
    cases.append(('7_台账可销账_FAIL',
                  {'A.cs': wrap_cs(
                      '        public void ShowTest(SaveData data)\n'
                      '        {\n'
                      '            ui.Panel(content, "HalfA", 0, 0, 100, 100);\n'
                      '            ui.Panel(content, "HalfB", 200, 0, 200, 100);\n'
                      '        }\n')},
                  {'A|A.cs|ShowTest|HalfA#1+HalfB#1':
                   {'criteria': {CRIT_OVERLAP_WARN}, 'note': '曾经重叠'}},
                  1, '可销账'))

    # 8 T127 同款:PuppetActor.Create 正 y 字面量 -> FAIL
    cases.append(('8_Create正y_FAIL',
                  {'A.cs': wrap_cs(
                      '        public void ShowTest(SaveData data)\n'
                      '        {\n'
                      '            var p = PuppetActor.Create(walkSample, sampleView,\n'
                      '                new Vector2(250f, 214f), new Vector2(126f, 212f));\n'
                      '        }\n')},
                  {}, 1, '可疑的 y 符号'))

    # 9 负 y / 零向量 / 表达式 / size 向量正 y 均不判
    cases.append(('9_负y与表达式不判_PASS',
                  {'A.cs': wrap_cs(
                      '        public void ShowTest(SaveData data)\n'
                      '        {\n'
                      '            var p = PuppetActor.Create(walkSample, sampleView,\n'
                      '                new Vector2(250f, -214f), new Vector2(126f, 212f));\n'
                      '            var q = PuppetActor.Create(worldRoot, art, Vector2.zero, size);\n'
                      '            p.SetPosition(new Vector2(StageX(x) + off, -PlayerGroundY));\n'
                      '        }\n')},
                  {}, 0, None))

    # 10 SetPosition 正 y 字面量 -> FAIL
    cases.append(('10_SetPosition正y_FAIL',
                  {'A.cs': wrap_cs(
                      '        public void SyncView()\n'
                      '        {\n'
                      '            puppet.SetPosition(new Vector2(12f, 34f));\n'
                      '        }\n')},
                  {}, 1, '可疑的 y 符号'))

    # 11 ui.Rect 与 ui.ChipPanel 同样被抓
    cases.append(('11_Rect与ChipPanel也判_FAIL',
                  {'A.cs': wrap_cs(
                      '        public void ShowTest(SaveData data)\n'
                      '        {\n'
                      '            ui.Rect(content, "Inner", 10, 10, 200, 200);\n'
                      '            ui.ChipPanel(content, "Outer", 20, 20, 210, 210);\n'
                      '        }\n')},
                  {}, 1, '整块被盖住'))

    # 12 非字面量坐标(变量/表达式)不判,机械判不了不硬猜
    cases.append(('12_非字面量坐标不判_PASS',
                  {'A.cs': wrap_cs(
                      '        public void ShowTest(SaveData data)\n'
                      '        {\n'
                      '            ui.Panel(content, "VarA", x, y, w, h);\n'
                      '            ui.Panel(content, "VarB", x + 10, y, w, h);\n'
                      '        }\n')},
                  {}, 0, None))

    # 13 面板调用跨三行仍可折叠解析(先挂的小面板被跨行声明的大面板盖住 => FAIL)
    cases.append(('13_跨行调用可解析_FAIL',
                  {'A.cs': wrap_cs(
                      '        public void ShowTest(SaveData data)\n'
                      '        {\n'
                      '            ui.Rect(content, "Spread", 10, 10, 280, 280);\n'
                      '            ui.Rect(content,\n'
                      '                "Lid",\n'
                      '                0, 0, 300, 300);\n'
                      '        }\n')},
                  {}, 1, '整块被盖住'))

    # 14 B 判据存量台账:正 y 已登记 => WARN rc0
    cases.append(('14_正y存量已登记_WARN_rc0',
                  {'A.cs': wrap_cs(
                      '        public void ShowTest(SaveData data)\n'
                      '        {\n'
                      '            puppet.SetPosition(new Vector2(12f, 34f));\n'
                      '        }\n')},
                  {'B|A.cs|ShowTest|puppet.SetPosition':
                   {'criteria': {CRIT_POSITIVE_Y}, 'note': 'UI-101 特意放面板上方,历史存量'}},
                  0, '存量可疑 y 符号'))

    # 15 全屏底板先挂、内容面板后挂(较小者后挂 = 画在上层)是正常叠印,不判
    cases.append(('15_底板叠印不判_PASS',
                  {'A.cs': wrap_cs(
                      '        public void ShowTest(SaveData data)\n'
                      '        {\n'
                      '            ui.Panel(content, "Backdrop", 0, 0, 1920, 1080);\n'
                      '            ui.Panel(content, "Dialog", 525, 330, 870, 400);\n'
                      '        }\n')},
                  {}, 0, None))

    # 16 判红级存量已登记台账(level='red') => WARN rc0 放行(只许销账不许放宽)
    cases.append(('16_判红级存量已登记_WARN_rc0',
                  {'A.cs': wrap_cs(
                      '        public void ShowTest(SaveData data)\n'
                      '        {\n'
                      '            ui.Panel(content, "Covered", 1054, 336, 766, 296);\n'
                      '            ui.Panel(content, "Cover", 1074, 336, 746, 593);\n'
                      '        }\n')},
                  {'A|A.cs|ShowTest|Covered#1+Cover#1':
                   {'criteria': {CRIT_OVERLAP_WARN}, 'level': 'red', 'note': 'UI-102 有意压底,历史存量'}},
                  0, '存量整块遮挡'))

    # 17 两个 ui.Button 整块重合 -> 按钮判据 FAIL(不得被面板判据替掉)
    cases.append(('17_两按钮整块重合_FAIL',
                  {'A.cs': wrap_cs(
                      '        public void ShowTest(SaveData data)\n'
                      '        {\n'
                      '            ui.Button(content, "确认", 48, 292, 350, 62, CloseModal);\n'
                      '            ui.Button(content, "取消", 48, 292, 350, 62, CloseModal);\n'
                      '        }\n')},
                  {}, 1, '按钮整块重合'))

    # 18 ui.Button 落在 ui.Panel 内(按钮⊂面板是常态) -> PASS,不许假红
    cases.append(('18_按钮在面板内_PASS',
                  {'A.cs': wrap_cs(
                      '        public void ShowTest(SaveData data)\n'
                      '        {\n'
                      '            ui.Panel(content, "Box", 0, 0, 600, 400,\n'
                      '                new Color(.1f, .1f, .1f, .9f));\n'
                      '            ui.Button(content, "确认", 48, 292, 350, 62, CloseModal);\n'
                      '        }\n')},
                  {}, 0, None))

    bad = []
    tmp_root = os.path.join(REPO, '.batch', 'tmp')
    os.makedirs(tmp_root, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=tmp_root) as td:
        for name, files, baseline, want_rc, want_kw in cases:
            ok, got_rc, text = run_case(td, name, files, baseline, want_rc, want_kw)
            if ok:
                print('  [PASS] %-32s -> rc=%d, 断言: %s'
                      % (name, got_rc, want_kw if want_kw else 'PASS'))
            else:
                bad.append('%s: 期望 rc=%d kw=%s, 实得 rc=%d, 输出: %s'
                           % (name, want_rc, want_kw, got_rc, text[:300]))

    if bad:
        for b in bad:
            print('SELFTEST FAIL: ' + b)
        print('RESULT: FAIL(selftest %d/%d 条不点火)' % (len(bad), len(cases)))
        return 1
    print('SELFTEST: %d/%d 条用例全点火(整块盖住判红/边角重叠判绿/跨方法不判/WARN 未登记/'
          'WARN 已登记/台账升级/台账可销账/Create 正 y/负 y 与表达式/SetPosition 正 y/'
          'Rect+ChipPanel/非字面量不判/跨行折叠/正 y 存量台账/底板叠印不判/判红级存量放行/'
          '两按钮整块重合判红/按钮在面板内不假红)'
          % (len(cases), len(cases)))
    print('RESULT: PASS(selftest)')
    return 0


def main():
    ap = argparse.ArgumentParser(description='界面布局门禁:面板重叠 + 可疑 y 符号')
    ap.add_argument('--selftest', action='store_true', help='合成 C# 用例自检')
    ap.add_argument('--report', action='store_true', help='只打印全表不判定,rc 恒 0')
    ap.add_argument('--dir', default=DEFAULT_SCAN_DIR, help='扫描目录(默认 Presentation/)')
    args = ap.parse_args()

    if args.selftest:
        return selftest()

    fails, warns, pair_rows, pos_rows, btn_rows = check_dir(args.dir)

    if args.report:
        return print_report(pair_rows, pos_rows, btn_rows)

    for w in warns:
        print('WARN: ' + w)
    for f in fails:
        print('FAIL: ' + f)
    print('SUMMARY: %d 处面板重叠对(判红 %d / WARN %d),%d 处按钮重叠对(判红 %d / WARN %d),%d 处可疑 y 符号'
          % (len(pair_rows),
             sum(1 for r in pair_rows if r['ratio'] >= RED_RATIO),
             sum(1 for r in pair_rows if r['ratio'] < RED_RATIO),
             len(btn_rows),
             sum(1 for r in btn_rows if r['ratio'] >= RED_RATIO),
             sum(1 for r in btn_rows if r['ratio'] < RED_RATIO),
             len(pos_rows)))
    if fails:
        print('RESULT: FAIL(%d 项违规)' % len(fails))
        return 1
    if warns:
        print('RESULT: PASS_WITH_WARN(%d 项存量/局部遮挡警告,退出码 0 放行)' % len(warns))
        return 0
    print('RESULT: PASS')
    return 0


if __name__ == '__main__':
    sys.exit(main())
