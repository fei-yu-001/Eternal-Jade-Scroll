# -*- coding: utf-8 -*-
"""校验 world-mask.png 是否合理，并交叉核对与 town-map.json 的 blocked 是否一致。

一致性校验的意义：mask 由 terrain 形状参数求值，blocked 由生成器栅格化而来。
两者若出现系统性偏差，说明配置或公式漂移了——必须在合成正式地形之前发现。

输出：Tools/logs/world-mask-check.log
"""
import json, os, sys
import numpy as np
from PIL import Image

# Windows GBK 控制台下 print 中文/箭头会 UnicodeEncodeError 把整道门禁崩掉——
# 一个会崩的门禁比一个红的门禁危险得多（架构师 2026-10-05 定规，全仓 Tools/*.py 入口统一）。
import sys
for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, 'reconfigure'):
        _stream.reconfigure(encoding='utf-8', errors='replace')

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, 'Tools'))
import world_paths as wp

CFG_DIR = wp.config_root()
LOG_DIR = wp.tools_log_root()

LINES = []
FAILED = []
WARNS = []


def log(text):
    LINES.append(text)
    print(text)


def ok(name, condition, detail='', warn_only=False):
    if condition:
        log('%-22s OK %s' % (name + ':', detail))
        return True
    if warn_only:
        WARNS.append(name)
        log('%-22s WARN %s' % (name + ':', detail))
    else:
        FAILED.append(name)
        log('%-22s FAIL %s' % (name + ':', detail))
    return False


def world_to_mask(x, y, scale):
    return int(x // scale), int(y // scale)


def main():
    types_cfg = json.load(open(os.path.join(CFG_DIR, 'terrain-types.json'), encoding='utf-8'))
    world_cfg = json.load(open(os.path.join(CFG_DIR, 'town-map.json'), encoding='utf-8'))
    compose_path = os.path.join(CFG_DIR, 'world-compose.json')
    compose = json.load(open(compose_path, encoding='utf-8')) if os.path.exists(compose_path) else {}
    types = {t['id']: t for t in types_cfg['types']}
    values = {t['id']: t['value'] for t in types_cfg['types']}
    scale = int(types_cfg['maskScale'])
    mask_path = wp.resolve_repo_path(compose.get(
        'maskPath', 'GameClient/TwelveJade/Assets/ArtSource/world/masks/world-mask.png'))

    log('=== world-mask check ===')
    if not ok('mask file', os.path.exists(mask_path), mask_path):
        return finish()
    img = Image.open(mask_path)
    mask = np.array(img.convert('L'))
    log('mask size: %dx%d %s' % (mask.shape[1], mask.shape[0], 'OK' if
        (mask.shape[0], mask.shape[1]) == (world_cfg['worldWidth'] // scale, world_cfg['worldHeight'] // scale) else 'FAIL'))
    expected = (world_cfg['worldHeight'] // scale, world_cfg['worldWidth'] // scale)
    ok('mask size', mask.shape == expected, '%dx%d (期望 %dx%d)' % (mask.shape[1], mask.shape[0], expected[1], expected[0]))
    if img.mode not in ('L', 'P', 'RGB'):
        ok('mask mode', False, img.mode)
    else:
        ok('mask mode', True, img.mode)

    uniq = sorted(int(v) for v in np.unique(mask))
    legal = sorted(values.values())
    ok('unique values', all(v in legal for v in uniq), str(uniq))
    if not all(v in legal for v in uniq):
        return finish()

    total = mask.size
    ratio = {t['id']: float((mask == values[t['id']]).sum()) / total for t in types_cfg['types']}
    log('ratios: ' + '  '.join('%s=%.2f%%' % (k, v * 100) for k, v in ratio.items()))

    ok('void ratio', ratio['void'] <= .05, '%.2f%%' % (ratio['void'] * 100))
    if ratio['grass'] < .10:
        ok('grass ratio', False, '%.2f%%' % (ratio['grass'] * 100))
    else:
        ok('grass ratio', ratio['grass'] >= .20, '%.2f%%' % (ratio['grass'] * 100),
           warn_only=ratio['grass'] < .20)
    ok('water exists', ratio['water'] > 0, '%.2f%%' % (ratio['water'] * 100))
    ok('mountain exists', ratio['mountain'] > 0, '%.2f%%' % (ratio['mountain'] * 100))
    bridges = world_cfg.get('landmarks') and any('bridge' in m['id'] for m in world_cfg['landmarks'])
    if bridges:
        ok('bridge exists', ratio['bridge'] > 0, '%.3f%%' % (ratio['bridge'] * 100))
    else:
        log('bridge exists:        SKIP（配置无桥地标）')
    ok('riverbank exists', ratio['riverbank'] > 0, '%.2f%%' % (ratio['riverbank'] * 100))
    ok('road-edge exists', ratio['road-edge'] > 0, '%.2f%%' % (ratio['road-edge'] * 100))
    ok('mountain-foot exists', ratio['mountain-foot'] > 0, '%.2f%%' % (ratio['mountain-foot'] * 100))
    ok('dirt-road exists', ratio['dirt-road'] > 0, '%.2f%%' % (ratio['dirt-road'] * 100))
    ok('stone-road exists', ratio['stone-road'] > 0, '%.2f%%' % (ratio['stone-road'] * 100))
    ok('plaza exists', ratio['plaza'] > 0, '%.2f%%' % (ratio['plaza'] * 100))
    ok('mountain-path exists', ratio['mountain-path'] > 0, '%.2f%%' % (ratio['mountain-path'] * 100))
    ok('farmland exists', ratio['farmland'] > 0, '%.2f%%' % (ratio['farmland'] * 100))

    # ---- 关键点位（坐标全部来自配置，不硬编码）----
    spawn = world_cfg.get('spawn')
    if not ok('spawn in config', bool(spawn), str(spawn)):
        return finish()
    sx, sy = world_to_mask(spawn['x'], spawn['y'], scale)
    sv = int(mask[sy, sx])
    ok('spawn', types_cfg['types'][sv]['walkable'] and types_cfg['types'][sv]['id'] != 'water',
       'value=%d (%s)' % (sv, types_cfg['types'][sv]['id']))

    terrain = world_cfg.get('terrainShape') or world_cfg.get('terrain') or {}   # 兼容旧键名
    if terrain:
        import math
        # 河心：取 river 参数在某列的河心位置
        # 河心探针必须避开所有桥位：主桥就在世界正中（曾因此把桥面当成河心误报）。
        bridge_xs = [b['x'] for b in terrain.get('bridges', [])]
        bridge_xs.append(terrain.get('ferry', {}).get('x'))
        rx = int(world_cfg['worldWidth'] * .25)
        for bx in bridge_xs:
            if bx is not None and abs(rx - bx) < 400:
                rx = int(bx + 500)
                break
        cy = terrain['river']['baseY'] + sum(w['amp'] * math.sin(rx / w['period'] + w.get('phase', 0.0))
                                             for w in terrain['river']['wave'])
        mv_x, mv_y = world_to_mask(rx, cy, scale)
        mv = int(mask[mv_y, mv_x])
        ok('river-center', mv == values['water'], 'value=%d (%s)' % (mv, types_cfg['types'][mv]['id']))
        # 桥：取 bridges[0] 的中心
        if terrain.get('bridges'):
            br = terrain['bridges'][0]
            bcy = terrain['river']['baseY'] + sum(w['amp'] * math.sin(br['x'] / w['period'] + w.get('phase', 0.0))
                                                 for w in terrain['river']['wave'])
            bx, by = world_to_mask(br['x'], bcy, scale)
            bv = int(mask[by, bx])
            ok('bridge-center', bv == values['bridge'], 'value=%d (%s)' % (bv, types_cfg['types'][bv]['id']))
        # 山体中心
        mx, my = world_to_mask(int(world_cfg['worldWidth'] * .5), int(terrain['mountain']['baseY'] * .3), scale)
        mvv = int(mask[my, mx])
        ok('mountain-center', mvv in (values['mountain'], values['mountain-foot']),
           'value=%d (%s)' % (mvv, types_cfg['types'][mvv]['id']))
        # 主街：主街是摆动的，必须按 mainRoad 的解析式求该 y 处的路心，
        # 直接取 baseX 会落在路外（曾因此误报 road-center FAIL）。
        mr = terrain['mainRoad']
        probe_y = int(world_cfg['worldHeight'] * .8)
        road_x = mr['baseX'] + mr['amp'] * math.sin(probe_y / mr['periodY'])
        px, py = world_to_mask(road_x, probe_y, scale)
        pv = int(mask[py, px])
        ok('road-center', pv in (values['stone-road'], values['road-edge']),
           'value=%d (%s)' % (pv, types_cfg['types'][pv]['id']))

    # ---- 与 blocked 的交叉一致性（防止配置/公式漂移）----
    blocked = world_cfg.get('blocked') or []
    if blocked:
        sample = blocked[::max(1, len(blocked) // 4000)]      # 抽样 4000 条，够快也够准
        hit = miss = 0
        for item in sample:
            mx, my = world_to_mask(item['x'] + item['width'] / 2.0,
                                   item['y'] + item['height'] / 2.0, scale)
            if not (0 <= my < mask.shape[0] and 0 <= mx < mask.shape[1]):
                continue
            v = int(mask[my, mx])
            why = str(item.get('why') or item.get('type') or '')
            want = values['water'] if 'river' in why else values['mountain']
            if v in (want, values['riverbank'], values['mountain-foot']):   # 过渡带可接受
                hit += 1
            else:
                miss += 1
        rate = hit / max(1, hit + miss)
        ok('blocked consistency', rate >= .97, '%.2f%% 一致（%d/%d，桥/渡口缺口与过渡带不计入）'
           % (rate * 100, hit, hit + miss))

    # ---- T065：探针锚点自己的盘不许压不可通行区（地标 + 遭遇点一起管）----
    # 立这条的理由写在台账里：back-mountain 曾落在山体凹口里（离最近一块山石只有 8px），
    # 于是"任何按地标摆东西的逻辑"都必须额外退让，而这种退让是隐形的——红会报在别人身上。
    # 判据是几何真算（锚点到 tile 矩形的最短距离 < 盘半径 就算压住），不是抽样。
    # 盘半径与运行期夹具的探针环同尺寸（TerrainPropShot 的 64px 直径 = 32 半径）：
    # 静态与运行期不同形就是 T018 那一族，所以两边各有一条、互相咬住。
    PROBE_R = 32.0
    cell_why = {}
    for item in blocked:
        x0 = int(item['x']) // 32
        y0 = int(item['y']) // 32
        x1 = (int(item['x']) + int(item['width']) - 1) // 32
        y1 = (int(item['y']) + int(item['height']) - 1) // 32
        for cx in range(x0, x1 + 1):
            for cy in range(y0, y1 + 1):
                cell_why[(cx, cy)] = str(item.get('why') or item.get('type') or 'blocked')

    def disk_pressure(px, py, radius=PROBE_R):
        hit = []
        for cx in range(int((px - radius) // 32), int((px + radius) // 32) + 1):
            for cy in range(int((py - radius) // 32), int((py + radius) // 32) + 1):
                why = cell_why.get((cx, cy))
                if why is None:
                    continue
                rx, ry = cx * 32.0, cy * 32.0
                dx = max(rx - px, 0.0, px - (rx + 32.0))
                dy = max(ry - py, 0.0, py - (ry + 32.0))
                if dx * dx + dy * dy < radius * radius:
                    hit.append('%d,%d(%s)' % (cx, cy, why))
        return hit

    landmark_list = world_cfg.get('landmarks') or []
    encounter_list = world_cfg.get('encounters') or []
    probes = ([('地标', m.get('id'), m['x'], m['y']) for m in landmark_list] +
              [('遭遇', e.get('id'), e['x'], e['y']) for e in encounter_list])
    pressed = ['%s/%s 盘内压 %d 块：%s' % (kind, pid, len(h), ', '.join(h[:3]))
               for kind, pid, px, py in probes for h in [disk_pressure(float(px), float(py))] if h]
    ok('anchor clearance', not pressed,
       ('%d 个锚点（地标 %d + 遭遇 %d）盘内全在可行走地上'
        % (len(probes), len(landmark_list), len(encounter_list))
        if not pressed else '；'.join(pressed[:5])))

    return finish()


def finish():
    log('')
    if FAILED:
        log('RESULT: FAIL')
        log('FAILED:')
        for f in FAILED:
            log('- ' + f)
    else:
        log('RESULT: PASS')
        if WARNS:
            log('warnings: ' + ', '.join(WARNS))
    os.makedirs(LOG_DIR, exist_ok=True)
    with open(os.path.join(LOG_DIR, 'world-mask-check.log'), 'w', encoding='utf-8', newline='\n') as fh:
        fh.write('\n'.join(LINES) + '\n')
    return 1 if FAILED else 0


def selftest():
    """自检（夜1 G-3 第 3 条，2026-10-06）：改坏一份**配置副本**，断言本门禁变红且退出码非零。

    坏法：terrain-types.json 的 maskScale +1 —— mask 尺寸期望随之改变，'mask size' 判据必红。
    副本与日志落 .batch/night1/selftest/，真配置不动；判据 / 阈值 / 期望值一律未改。
    """
    import shutil
    global CFG_DIR, LOG_DIR
    tmp = os.path.join(ROOT, '.batch', 'night1', 'selftest', 'world-mask')
    tmp_cfg = os.path.join(tmp, 'config')
    if os.path.exists(tmp):
        shutil.rmtree(tmp)
    os.makedirs(tmp_cfg, exist_ok=True)
    copied = []
    for name in ('terrain-types.json', 'town-map.json', 'world-compose.json'):
        src = os.path.join(CFG_DIR, name)
        if os.path.exists(src):
            shutil.copy2(src, os.path.join(tmp_cfg, name))
            copied.append(name)
    if 'terrain-types.json' not in copied:
        print('SELFTEST: FAIL（terrain-types.json 没复制到副本目录，自检前提不成立）')
        return 1
    broken = os.path.join(tmp_cfg, 'terrain-types.json')
    tt = json.load(open(broken, encoding='utf-8'))
    tt['maskScale'] = int(tt.get('maskScale', 1)) + 1
    with open(broken, 'w', encoding='utf-8', newline='\n') as fh:
        json.dump(tt, fh, ensure_ascii=False, indent=2)
    CFG_DIR, LOG_DIR = tmp_cfg, tmp
    rc = main()
    print('SELFTEST: %s rc=%d（改坏项：terrain-types.json maskScale +1）'
          % ('PASS 门禁确实变红' if rc != 0 else 'FAIL 坏配置竟然没让它红', rc))
    return 0 if rc != 0 else 1


if __name__ == '__main__':
    sys.exit(selftest() if '--selftest' in sys.argv else main())
