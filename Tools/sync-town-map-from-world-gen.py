# -*- coding: utf-8 -*-
"""town-map.json 安全同步/派生（任务书 C0-B §2）。

真源：Resources/Config/town-world-gen.json（由 make-world-greybox.py 产出）。
目标：Resources/Config/town-map.json（Unity 运行时读取）。

只同步白名单派生字段；其余字段（terrain/spawn/npcs/props/encounters/walkable/phase
等）保持 town-map.json 原值，绝不被本脚本覆盖。

字段映射说明：
    town-world-gen.json 'terrain'（dict，地形形状参数）
        → town-map.json 'terrainShape'（dict，运行时/下游脚本读取）

用法：
    python Tools/sync-town-map-from-world-gen.py --dry-run
    python Tools/sync-town-map-from-world-gen.py --write
"""
import argparse, copy, hashlib, json, os, sys

# Windows GBK 控制台下 print 中文/箭头会 UnicodeEncodeError 把整道门禁崩掉——
# 一个会崩的门禁比一个红的门禁危险得多（架构师 2026-10-05 定规，全仓 Tools/*.py 入口统一）。
import sys
for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, 'reconfigure'):
        _stream.reconfigure(encoding='utf-8', errors='replace')

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CFG = os.path.join(ROOT, 'GameClient', 'TwelveJade', 'Assets', 'Resources', 'Config')
GEN = os.path.join(CFG, 'town-world-gen.json')
MAP = os.path.join(CFG, 'town-map.json')
BAK = MAP + '.bak'

# 白名单：gen 字段名 → map 字段名（同名的用 'eq' 占位）
# T020：原先列有 ('columns','columns') / ('rows','rows')，但 town-world-gen.json 从不产出
# 这两个键，每次同步都报 MISSING_SOURCE_FIELD 空转。世界网格数由 chunks 在运行期派生
# （TownMap.WorldColumns / WorldRows），配置里不再留第三处真相。
WHITELIST = [
    ('worldWidth', 'worldWidth'),
    ('worldHeight', 'worldHeight'),
    ('chunkSize', 'chunkSize'),
    ('chunks', 'chunks'),
    ('blocked', 'blocked'),
    ('landmarks', 'landmarks'),
    ('terrainShape', 'terrainShape'),   # 同名：gen.terrainShape → map.terrainShape（dict）
]

# 写入后必须核对的类型（任务书 §2.3 类型体检）
TYPE_CHECKS = {
    'blocked': list, 'chunks': list, 'landmarks': list, 'npcs': list,
    'props': list, 'encounters': list, 'walkable': list,
    'terrain': list, 'terrainShape': dict,
}


def digest(obj):
    return hashlib.sha256(json.dumps(obj, sort_keys=True, ensure_ascii=False)
                          .encode('utf-8')).hexdigest()[:12]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--dry-run', action='store_true')
    ap.add_argument('--write', action='store_true')
    args = ap.parse_args()
    if not args.dry_run and not args.write:
        ap.error('必须指定 --dry-run 或 --write 之一')

    gen = json.load(open(GEN, encoding='utf-8'))
    mp = json.load(open(MAP, encoding='utf-8'))

    print('=== sync town-map from town-world-gen ===')
    updates = {}
    for src_key, dst_key in WHITELIST:
        if src_key not in gen:
            print('MISSING_SOURCE_FIELD %s（gen 中不存在，跳过，不删除 map 已有值）' % dst_key)
            continue
        new_v = gen[src_key]
        old_v = mp.get(dst_key)
        if isinstance(new_v, (dict, list)):
            changed = json.dumps(new_v, sort_keys=True, ensure_ascii=False) != \
                      json.dumps(old_v, sort_keys=True, ensure_ascii=False) if old_v is not None else True
            detail = 'digest %s -> %s' % (digest(old_v) if old_v is not None else '-',
                                          digest(new_v)) if changed else 'same'
        else:
            changed = old_v != new_v
            detail = '%s -> %s' % (old_v, new_v) if changed else 'same'
        if changed:
            updates[dst_key] = new_v
            print('UPDATE %s: %s' % (dst_key, detail))
        else:
            print('NO CHANGE %s' % dst_key)
    skipped = [dk for sk, dk in WHITELIST if sk not in gen]
    for extra in sorted(set(mp) - {dk for _, dk in WHITELIST}):
        print('SKIP %s: not in whitelist' % extra)
    if skipped:
        print('MISSING_SOURCE_FIELD（gen 缺失，未列入更新）: %s' % ', '.join(skipped))

    if args.dry_run:
        # 这句原来写成 "dry-run / no changes：未写入。"——不管有没有检出变化都打同一句，
        # 读的人会把"dry-run 所以没写"当成"gen 与 map 本来就一致"，下一步就去手改 map 了
        # （而手改 map 会被下次同步覆盖，正是这条管线立白名单的理由）。2026-10-06 T065 撞到。
        print('DRY-RUN：检出 %d 项待写入（%s）；未写。要落盘用 --write。' % (
            len(updates), ', '.join(sorted(updates)) if updates else '无'))
        return 0
    if not updates:
        print('NO CHANGES：gen 与 map 已一致，未写。')
        return 0

    # 写前备份
    with open(BAK, 'w', encoding='utf-8', newline='\n') as fh:
        json.dump(mp, fh, ensure_ascii=False, indent=1)
    print('backup written: %s' % BAK)

    merged = copy.deepcopy(mp)
    merged.update(updates)
    with open(MAP, 'w', encoding='utf-8', newline='\n') as fh:
        json.dump(merged, fh, ensure_ascii=False, indent=1)

    # 写后重新解析 + 类型体检
    try:
        merged = json.load(open(MAP, encoding='utf-8'))
    except Exception as ex:
        copy.deepcopy  # noqa
        with open(BAK, encoding='utf-8') as fh:
            good = fh.read()
        open(MAP, 'w', encoding='utf-8', newline='\n').write(good)
        raise SystemExit('写入后 JSON 非法，已恢复备份：%s' % ex)

    bad_types = []
    for key, want in TYPE_CHECKS.items():
        v = merged.get(key)
        if v is None:
            continue
        if not isinstance(v, want):
            bad_types.append('%s 是 %s，应为 %s' % (key, type(v).__name__, want.__name__))
    if bad_types:
        with open(BAK, encoding='utf-8') as fh:
            good = fh.read()
        open(MAP, 'w', encoding='utf-8', newline='\n').write(good)
        raise SystemExit('类型体检失败，已恢复备份：' + '; '.join(bad_types))
    print('type check OK（terrain=%s, terrainShape=%s）' % (
        type(merged.get('terrain')).__name__ if 'terrain' in merged else '不存在',
        type(merged.get('terrainShape')).__name__ if 'terrainShape' in merged else '不存在'))
    print('written OK')
    return 0


if __name__ == '__main__':
    import copy
    sys.exit(main())
