# -*- coding: utf-8 -*-
"""世界配置一致性检查（任务书 §2.3）。

town-world-gen.json 是 terrain 参数与几何的唯一真源（由 make-world-greybox.py 导出）；
town-map.json 是 Unity 运行时读取的配置。两边共同字段必须逐项一致，不一致直接 FAIL。
另外做 enemy ↔ encounter 双向对表（见 encounter_contract）：光比长度看不出"新妖没有遭遇入口"。

用法：python Tools/check-town-config-consistency.py
输出：Tools/logs/town-config-consistency.log
"""
import hashlib, json, os, sys

# 门禁脚本不许依赖调用方的控制台代码页。zh-CN Windows 的 GBK 控制台下，输出 `↔` 会
# UnicodeEncodeError 直接崩在 RESULT 之前——**会崩的门禁比红的门禁危险得多**，
# 因为没人会去读堆栈，只看到"跑过了没结论"（架构师 2026-10-05 独立复验抓到；
# 3 号的 T086 与我这条 T061 是同族：验收命令的可复现性不该靠环境）。
# 仓内先例：SaveChecks.csproj 的 <CodePage>65001</CodePage> 就是为同一类问题写的。
for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, 'reconfigure'):
        _stream.reconfigure(encoding='utf-8', errors='replace')

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CFG = os.path.join(ROOT, 'GameClient', 'TwelveJade', 'Assets', 'Resources', 'Config')
LOG = os.path.join(ROOT, 'Tools', 'logs', 'town-config-consistency.log')

# 共同字段及其比较方式：'eq' 直接相等；'list' 逐项（顺序敏感）；'len' 只比长度
FIELDS = [
    ('worldWidth', 'eq'), ('worldHeight', 'eq'), ('chunkSize', 'eq'),
    ('chunks', 'len'), ('blocked', 'len'), ('landmarks', 'len'),
    ('spawn', 'json'), ('terrainShape', 'json'), ('elevation', 'json'),
]

LINES = []
FAILED = []


def log(s):
    LINES.append(s)
    print(s)


def digest(obj):
    return hashlib.sha256(json.dumps(obj, sort_keys=True, ensure_ascii=False).encode('utf-8')).hexdigest()[:12]


def same(kind, a, b):
    if kind == 'eq':
        return a == b
    if kind == 'len':
        return len(a or []) == len(b or [])
    if kind == 'json':
        return json.dumps(a, sort_keys=True, ensure_ascii=False) == \
               json.dumps(b, sort_keys=True, ensure_ascii=False)
    return a == b


def main():
    gen_path = os.path.join(CFG, 'town-world-gen.json')
    map_path = os.path.join(CFG, 'town-map.json')
    for p in (gen_path, map_path):
        if not os.path.exists(p):
            log('缺少配置：' + p)
            FAILED.append('missing-config')
            return finish()

    gen = json.load(open(gen_path, encoding='utf-8'))
    mp = json.load(open(map_path, encoding='utf-8'))

    log('=== town config consistency ===')
    log('真源 town-world-gen.json  sha256[:12]=%s' % digest(gen))
    log('运行时 town-map.json      sha256[:12]=%s' % digest(mp))
    log('')
    for name, kind in FIELDS:
        in_gen, in_map = name in gen, name in mp
        if not in_gen and not in_map:
            log('%-12s SKIP（两边都没有）' % name)
            continue
        if in_gen != in_map:
            FAILED.append(name)
            log('%-12s FAIL 只在 %s 中存在' % (name, 'town-world-gen.json' if in_gen else 'town-map.json'))
            continue
        okv = same(kind, gen[name], mp[name])
        if not okv:
            FAILED.append(name)
            detail = 'len %d vs %d' % (len(gen[name] or []), len(mp[name] or [])) \
                if kind == 'len' else '内容不同'
            log('%-12s FAIL %s' % (name, detail))
        else:
            log('%-12s OK   %s' % (name, ('len=%d' % len(gen[name])) if kind == 'len'
                                    else ('digest=%s' % digest(gen[name]))))
    encounter_contract(mp)

    # 反向检查：map 里多出来的顶层键（不算错，但要让人知道）
    extra = sorted(set(mp) - set(gen))
    if extra:
        log('')
        log('town-map.json 额外顶层字段：%s' % ', '.join(extra))
    return finish()


def encounter_contract(mp):
    """enemy ↔ encounter 双向对表（M5 / T052）。

    只比长度永远看不出"enemies.json 加了新妖、town-map 里却一条遭遇都没有"——
    玩家从头到尾碰不到它，配置却一路绿灯。这条把 T052 变成机器值守，
    缺失一律 FAIL，不留 WARN（WARN 在这套门禁里等于没有）。
    """
    log('')
    log('=== enemy ↔ encounter 对表 ===')
    path = os.path.join(CFG, 'enemies.json')
    if not os.path.exists(path):
        FAILED.append('missing-enemies')
        log('enemies.json  FAIL 文件不存在：%s' % path)
        return
    raw = json.load(open(path, encoding='utf-8'))
    enemies = raw if isinstance(raw, list) else \
        next((v for v in raw.values() if isinstance(v, list)), [])
    enemy_ids = [e['id'] for e in enemies if isinstance(e, dict) and e.get('id')]
    encounters = mp.get('encounters') or []
    used = [e.get('enemy') for e in encounters if isinstance(e, dict)]
    enc_ids = [e.get('id') for e in encounters if isinstance(e, dict)]

    log('enemies=%d  encounters=%d' % (len(enemy_ids), len(enc_ids)))
    for eid in enemy_ids:
        n = used.count(eid)
        if n == 0:
            FAILED.append('enemy-no-encounter:' + str(eid))
            log('%-18s FAIL 没有任何 encounters 引用它（玩家遇不到＝内容白做）' % eid)
        else:
            log('%-18s OK   encounters 引用 %d 处' % (eid, n))
    for enc in encounters:
        ref = enc.get('enemy')
        if ref not in enemy_ids:
            FAILED.append('encounter-ghost-enemy:' + str(enc.get('id')))
            log('%-18s FAIL 引用 %r，但 enemies.json 里没有' % (enc.get('id'), ref))
    dupes = sorted({i for i in enc_ids if enc_ids.count(i) > 1})
    for d in dupes:
        FAILED.append('encounter-dup-id:' + str(d))
        log('%-18s FAIL 遭遇 id 重复（存档按 id 记讨伐 flag，重复会互相顶掉）' % d)


def finish():
    log('')
    log('RESULT: ' + ('FAIL' if FAILED else 'PASS'))
    if FAILED:
        log('FAILED: ' + ', '.join(FAILED))
    os.makedirs(os.path.dirname(LOG), exist_ok=True)
    with open(LOG, 'w', encoding='utf-8', newline='\n') as fh:
        fh.write('\n'.join(LINES) + '\n')
    return 1 if FAILED else 0


def selftest():
    """自检（夜1 G-3 第 3 条，2026-10-06）：故意改坏一份**配置副本**，断言本门禁变红且退出码非零。

    坏法：把 town-map.json 的 worldWidth +1（'eq' 类字段，两边必须逐项一致）。
    副本与日志都落在 .batch/night1/selftest/ —— 真配置一个字节都不动。
    判据 / 阈值 / 期望值一律未改，本函数只制造输入，不碰任何判定逻辑。
    """
    import shutil
    global CFG, LOG
    tmp = os.path.join(ROOT, '.batch', 'night1', 'selftest', 'town-config')
    tmp_cfg = os.path.join(tmp, 'config')
    if os.path.exists(tmp):
        shutil.rmtree(tmp)
    os.makedirs(tmp_cfg, exist_ok=True)
    copied = []
    for name in ('town-world-gen.json', 'town-map.json', 'enemies.json'):
        src = os.path.join(CFG, name)
        if os.path.exists(src):
            shutil.copy2(src, os.path.join(tmp_cfg, name))
            copied.append(name)
    if 'town-map.json' not in copied:
        print('SELFTEST: FAIL（连 town-map.json 都没复制到副本目录，自检前提不成立）')
        return 1
    broken = os.path.join(tmp_cfg, 'town-map.json')
    mp = json.load(open(broken, encoding='utf-8'))
    mp['worldWidth'] = int(mp.get('worldWidth', 0)) + 1
    with open(broken, 'w', encoding='utf-8', newline='\n') as fh:
        json.dump(mp, fh, ensure_ascii=False, indent=2)
    CFG, LOG = tmp_cfg, os.path.join(tmp, 'town-config-consistency.log')
    rc = main()
    print('SELFTEST: %s rc=%d（改坏项：town-map.json worldWidth +1）'
          % ('PASS 门禁确实变红' if rc != 0 else 'FAIL 坏配置竟然没让它红', rc))
    return 0 if rc != 0 else 1


if __name__ == '__main__':
    sys.exit(selftest() if '--selftest' in sys.argv else main())
