# -*- coding: utf-8 -*-
"""把地形真源 town-world-gen.json 的派生项同步进运行时配置 town-map.json（MAT-07）。

ADR-017：landmarks / terrainShape / blocked 都是"世界布局派生项"，只能在
make-world-greybox.py 里定义、经本脚本派生进 town-map.json（**不准手改**）。
check-town-config-consistency.py 会逐项比对两边，本脚本就是让它们一致的工具。

定点替换（不用 json.dump 整体重写，避免把无关部分的格式搅乱）。
用法：python Tools/sync-town-world-gen.py
"""
import io
import json
import os
import sys

for _s in (sys.stdout, sys.stderr):
    if hasattr(_s, 'reconfigure'):
        _s.reconfigure(encoding='utf-8', errors='replace')

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CFG = os.path.join(ROOT, 'GameClient', 'TwelveJade', 'Assets', 'Resources', 'Config')
GEN = os.path.join(CFG, 'town-world-gen.json')
TM = os.path.join(CFG, 'town-map.json')


def replace_value(s, key, value, depth_open, depth_close):
    """把 `"key": <...>` 的值整体替换为 value 的 JSON 文本（含缩进）。"""
    marker = '"%s": ' % key
    i = s.find(marker)
    if i < 0:
        return s, False
    j = i + len(marker)
    depth = 0
    while True:
        c = s[j]
        if c == depth_open:
            depth += 1
        elif c == depth_close:
            depth -= 1
            if depth == 0:
                break
        j += 1
    txt = json.dumps(value, ensure_ascii=False, indent=1).replace('\n', '\n ')
    return s[:i] + marker + txt + s[j + 1:], True


def main():
    gen = json.load(io.open(GEN, encoding='utf-8'))
    s = io.open(TM, encoding='utf-8').read()
    report = []

    # 1) terrainShape / blocked（大对象，整块替换）
    for key in ('terrainShape', 'blocked', 'elevation'):
        s, ok = replace_value(s, key, gen[key], '{' if key in ('terrainShape', 'elevation') else '[',
                              '}' if key in ('terrainShape', 'elevation') else ']')
        if not ok and key == 'elevation':
            end = s.rfind('}')
            s = s[:end].rstrip() + ',\n "elevation": ' + json.dumps(gen[key], ensure_ascii=False, indent=1) + '\n}'
            ok = True
        report.append((key, 'synced' if ok else 'MISSING'))

    # 2) landmarks：逐条比坐标，仅替换变化的（数组整体替换代价大且无必要）
    mp = json.loads(s)
    ml = {l['id']: l for l in mp['landmarks']}
    changed = []
    for l in gen['landmarks']:
        m = ml.get(l['id'])
        if m and (m.get('x') != l.get('x') or m.get('y') != l.get('y')):
            changed.append((l['id'], (m.get('x'), m.get('y')), (l.get('x'), l.get('y'))))
    if changed:
        block_i = s.find('"landmarks": [')
        block_j = s.find(']', block_i)
        head = s[:block_i]
        tail = s[block_j + 1:]
        arr = json.dumps(gen['landmarks'], ensure_ascii=False, indent=1).replace('\n', '\n ')
        s = head + '"landmarks": ' + arr + tail
    report.append(('landmarks', 'changed: %s' % changed if changed else 'already in sync'))

    io.open(TM, 'w', encoding='utf-8', newline='\n').write(s)
    json.load(io.open(TM, encoding='utf-8'))          # 校验
    for k, v in report:
        print('%-14s %s' % (k, v))
    print('town-map.json 已同步并校验 OK')


if __name__ == '__main__':
    main()
