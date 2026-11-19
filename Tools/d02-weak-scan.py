#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""TD-02 / TD-13 / TD-27 弱判据体检扫描器（员工 D）。

**只扫描、不修改**：对 `GameClient/TwelveJade/Assets/Editor/*.cs` 里的 `Check(` 调用按
"弱形态"分类，产出候选清单，供人工逐条复核。

用法：
    python Tools/d02-weak-scan.py                     # 扫默认目录，打在屏幕上
    python Tools/d02-weak-scan.py --out <文件>         # 另写一份（便于进报告）
    python Tools/d02-weak-scan.py --top 40            # 每类最多列多少条

分类依据是**形态**（这条"能不能在坏情况下仍然通过"），**不是语义**：
- 形态命中只说明"值得人看一眼"，**不等于它是错的**；
- 语义由人工复核（见 `Tools/logs/d-02-report.md`）；
- 本脚本**不判定通过与否、不改任何阈值、不修改任何源码**。

为什么形态扫描有用：1087 条 `Check(` 人肉看不完；先把"恒真/只有下界/子串匹配"这类
**结构上就必然放得宽**的挑出来，人工只需复核这几百条里的一小部分。
"""
import argparse
import os
import re
import sys

for s in (sys.stdout, sys.stderr):
    if hasattr(s, 'reconfigure'):
        s.reconfigure(encoding='utf-8', errors='replace')

DEFAULT_DIR = os.path.join('GameClient', 'TwelveJade', 'Assets', 'Editor')

# (类名, 正则, 为什么这类形态值得怀疑)
PATTERNS = [
    ('NULL_ONLY', re.compile(r'!=\s*null|==\s*null'),
     '只判"存在/非空引用"，不判"值对不对"——对象存在但字段全 0 照样过'),
    ('NON_EMPTY', re.compile(r'(\.Count|\.Length|\.Any\()'),
     '只判"容器非空"；非空 ≠ 内容正确（TD-02 已知第一条 TravelerCropHash 就是这族）'),
    ('GE_ZERO', re.compile(r'>=\s*0\b|>\s*-1\b|>=\s*0f\b'),
     '对非负量（Count/Length/像素/比率）取 >= 0 常见**恒真**；'
     '真恒假要看声明类型，本脚本只标形态，语义由人工核'),
    ('GT_ZERO_ONLY', re.compile(r'>\s*0\b'),
     '只有下界没有上界：给一个荒谬的大值也能过'),
    ('CONTAINS', re.compile(r'\.Contains\('),
     '子串/成员包含是弱匹配：打错一个字就不命中，命中了也不代表语义对'),
    ('OR_JOIN', re.compile(r'\|\|'),
     '用 || 连接 ⇒ 任一分支成立即过，等于放宽了判据'),
    ('ABS_TOL', re.compile(r'Mathf\.Abs\('),
     '带容差的近似比较：容差过宽等于没判（阈值属他人判据，本脚本不改）'),
]

CHECK_RX = re.compile(r'\bCheck\(')


def scan(editor_dir):
    """返回 [(相对路径, 行号, 原文)]，按文件名字典序、行号升序。"""
    hits = []
    if not os.path.isdir(editor_dir):
        return hits
    for name in sorted(os.listdir(editor_dir)):
        if not name.endswith('.cs'):
            continue
        path = os.path.join(editor_dir, name)
        try:
            lines = open(path, encoding='utf-8', errors='replace').read().splitlines()
        except OSError:
            continue
        for i, ln in enumerate(lines, 1):
            if CHECK_RX.search(ln):
                hits.append((name, i, ln.strip()))
    return hits


def main():
    ap = argparse.ArgumentParser(description='TD-02 弱判据形态扫描（只读）')
    ap.add_argument('--dir', default=DEFAULT_DIR, help='夹具目录（默认 Assets/Editor）')
    ap.add_argument('--out', default=None, help='另写一份到文件')
    ap.add_argument('--top', type=int, default=40, help='每类最多列多少条')
    args = ap.parse_args()

    hits = scan(args.dir)
    if not hits:
        print('D02-SCAN: FAIL 在 %s 下没扫到任何 Check( —— 目录对吗？' % args.dir)
        return 1

    buckets = {name: [] for name, _, _ in PATTERNS}
    for name, no, text in hits:
        label = '%s:%d: %s' % (name, no, text)
        for cls, rx, _why in PATTERNS:
            if rx.search(text):
                buckets[cls].append(label)

    out = []
    out.append('D02-SCAN  Check( 总条数: %d   目录: %s' % (len(hits), args.dir))
    out.append('（形态命中 ≠ 判据有错；语义由人工复核，见 Tools/logs/d-02-report.md）')
    out.append('')
    for cls, _rx, why in PATTERNS:
        items = buckets[cls]
        out.append('=== %s  %d 条  —— %s' % (cls, len(items), why))
        for it in items[:args.top]:
            out.append('  ' + it)
        out.append('')

    text = '\n'.join(out) + '\n'
    sys.stdout.write(text)
    if args.out:
        d = os.path.dirname(args.out)
        if d:
            os.makedirs(d, exist_ok=True)
        open(args.out, 'w', encoding='utf-8', newline='\n').write(text)
        print('D02-SCAN: OK 另写一份 → %s' % args.out)
    return 0


if __name__ == '__main__':
    sys.exit(main())
