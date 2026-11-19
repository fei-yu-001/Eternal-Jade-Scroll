# 来源：2026-10-05 开发者1号 在 D 盘根留下的 solve-npc.py，架构师搬入仓库并改名（原文件已随清理删除）。
# 用途：反解 NPC 落点，使遮挡判据对所有地块成立；从仓库根运行（脚本内是相对路径）。
# -*- coding: utf-8 -*-
"""反解 NPC 落点：遮挡判据对**所有**地块生效（比校验器更严），因为 prop 豁免的是走廊判据，
不是"人被草垛挡住"这件事。"""
import sys, os, json, io, math, importlib.util
# Windows GBK 控制台下 print 中文/箭头会 UnicodeEncodeError 把整道门禁崩掉——
# 一个会崩的门禁比一个红的门禁危险得多（架构师 2026-10-05 定规，全仓 Tools/*.py 入口统一）。
for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, 'reconfigure'):
        _stream.reconfigure(encoding='utf-8', errors='replace')

sys.path.insert(0,'Tools/PropChecks'); sys.path.insert(0,'Tools')
from PIL import Image
spec=importlib.util.spec_from_file_location('cpo','Tools/PropChecks/check-plot-overlap.py')
m=importlib.util.module_from_spec(spec); spec.loader.exec_module(m)
A='GameClient/TwelveJade/Assets/Resources/Art/%s.png'
bp=json.load(io.open('GameClient/TwelveJade/Assets/Resources/Config/building-plots.json',encoding='utf-8'))
tm=json.load(io.open('GameClient/TwelveJade/Assets/Resources/Config/town-map.json',encoding='utf-8'))
plots=[m.Plot(p,i) for i,p in enumerate(bp['plots'])]
bl=[(float(b['x']),float(b['y']),float(b['width']),float(b['height'])) for b in tm['blocked'] if isinstance(b,dict)]
scale=tm.get('worldScale',1.0) or 1.0
PAIR={'huolang':'plot-market-cart','npc-shenhuai-an':'plot-fortune-stall',
      'npc-shitou':'plot-firewood','npc-zhao-da':'plot-forge-anvil','npc-yao-lao':'plot-drying-rack'}
byid={p.id:p for p in plots}
def npc_w(art,h):
    f=A%art.replace('Art/','')
    return h*(Image.open(f).width/float(Image.open(f).height)) if os.path.exists(f) else h*.55
def ok(x,y,npc):
    if m.in_blocked(x,y,bl): return False
    h=npc['height'] or 170.0; w=npc_w(npc['art'],h); base_y=y-h
    rect=(x-w/2.0, base_y, x+w/2.0, y)
    for p in plots:
        b=p.base_rect()
        if p.major and b[0]<=x<=b[2] and b[1]<=y<=b[3]: return False
        if m.overlaps(p.art_rect(scale),rect) and p.foot_y>base_y+0.01: return False
    return True
for npc in tm['npcs']:
    pid=PAIR[npc['id']]; p=byid[pid]; cands=[]
    for r10 in range(0,121,5):
        for ang in range(0,360,5):
            x=p.foot_x+r10*math.cos(math.radians(ang)); y=p.foot_y+r10*math.sin(math.radians(ang))
            if ok(x,y,npc): cands.append((0 if y>p.foot_y+30 else 1, r10, round(x), round(y)))
    cands.sort()
    d=math.hypot(npc['x']-p.foot_x,npc['y']-p.foot_y)
    print('%-16s 配对 %-18s foot=(%g,%g) 现距=%.0f%s  最优落点=%s  (可行点 %d 个)'%(
        npc['id'],pid,p.foot_x,p.foot_y,d,'' if d<=120 else ' 【超标】',
        ('(%d,%d) r=%d'%(cands[0][2],cands[0][3],cands[0][1])) if cands else '无解', len(cands)))
