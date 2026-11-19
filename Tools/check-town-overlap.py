# -*- coding: utf-8 -*-
"""Town overlap gate, using the installed map and actual prop collision shapes.

Inputs: building-plots.json, town-map.json terrainShape, and optional adjacent
 town-circulation.json. G-E2 checks visual art rectangles. G-E3 checks runtime
 blockers: blocks:false -> none; collisionCapsule first, then collisionRects,
 then the default ground rectangle, matching WorldPropLayer.

A configured central plaza replaces the old fully empty corridor only inside
 its existing paved bounds with four rectangular circulation bands. The
 landscape bounds must equal the referenced real capsule bounds. They never
 become a collider or an object exemption. Every prop is checked against every
 band; the 400-area threshold and roads/bridges outside the plaza are unchanged.
 Core route tests separately enforce character clearance, four entrances,
 both detours and the absence of artificial walls.

G-E1 remains report-only pending an architectural threshold. G-E4 facing is
 informational. --report preserves prior evidence; --selftest includes true
 capsule corners, injected band blockers and invalid landscape/clearance.
"""
import argparse
from world_geometry import main_road_half
import json
import math
import os
import sys

# Windows GBK 控制台下 print 中文/箭头会 UnicodeEncodeError 把整道门禁崩掉——
# 一个会崩的门禁比一个红的门禁危险得多（架构师 2026-10-05 定规，全仓 Tools/*.py 入口统一）。
for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, 'reconfigure'):
        _stream.reconfigure(encoding='utf-8', errors='replace')

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CFG = os.path.join(ROOT, 'GameClient', 'TwelveJade', 'Assets', 'Resources', 'Config')
LOG = os.path.join(ROOT, 'Tools', 'logs', 'town-overlap.log')

# ---------------------------------------------------------------- 阈值（照抄方案，不许调）

INSET = 0.72          # 立绘本体占方图的比例（两侧各留 14% 透明边）——方案 E.1
GATE_CLEAR = 210.0    # blocks:false 的门洞净宽（≥1.24 身位）——方案 E.1
MIN_CONFLICT_AREA = 400.0   # 忽略细缝的像素面积下限——方案 E.2 / E.3
BANDS = (440.0, 520.0)      # G-E4 贴路立面 |dx| 区间——方案 E.2
FACING_DX_EPS = 1e-6        # facing 判据的浮点容差；0 只有算术上的意义

DEFAULT_STEP = 40.0   # 曲线走廊的离散步长（世界单位）；灵敏度分析见 --sweep-step
# WorldPropLayer.cs:70 `DefaultBaseDepth = 24f` 同值。两处不同值时，
# "运行期挡人、静态说不挡"会回来（T018 同形状原则）。
DEFAULT_BASE_DEPTH = 24.0

# 判据分级：FAIL 进退出码；REPORT 只打分布（阈值未定）；INFO 只打信息（运行期能力缺失）。
LEVEL = {'G-E1': 'REPORT', 'G-E2': 'FAIL', 'G-E3': 'FAIL', 'G-E4': 'INFO'}

OUT = []


def out(s=''):
    OUT.append(s)
    print(s)


# ---------------------------------------------------------------- 几何（与 layout 同式）

def road_x(y, ts):
    """主街中轴：x = baseX + amp*sin(y/periodY)。**无 2π**。

    口径真源是画路的生成器 make-world-mask.py:129（main_road_x），本门禁与它逐字同式，
    与 check-town-layout.py 的 road_x 也同式。terrainShape 的 period/periodY 是"角频率的倒数"，
    不是几何周期——写成 sin(2π·y/P) 会让路在画面外另起一条（2026-10-31 主架构师裁定，
    证据 Tools/logs/dispatch/road-formula-evidence.py：本式 max|Δ| 2.0，2π 式 255）。
    selftest 的 mask 对齐用例会在公式再被写错时变红。
    """
    mr = ts['mainRoad']
    return float(mr['baseX']) + mr['amp'] * math.sin(y / mr['periodY'])


def road_y(x, ts):
    """未实现：主街中轴的反函数（给定 x 求 y）。

    mainRoad 是 x = baseX + amp*sin(y/periodY)。因为正弦在 [baseX-amp, baseX+amp] 上
    **非单调**，同一个 x 有两组 y 解（一个在上升段、一个在下降段），"x→y"根本不是函数，
    任何反解都得外部指定一个参考 y 才能挑一支——那种"反解"会把歧义藏进调用点。
    本门禁的走廊判定改成"按 y 采样取包络"（见 corridor_rects），完全不需要反解。
    留此桩是为了让误用者当场炸，而不是拿到一个悄悄选错分支的 y。
    """
    raise AssertionError('road_y 不再使用：主街走廊按水平距离判定，见 in_main_road')


def in_rect(x, y, r):
    return r['x0'] <= x <= r['x1'] and r['y0'] <= y <= r['y1']


def river_y(x, ts):
    rv = ts['river']
    y = float(rv['baseY'])
    for w in rv['wave']:
        y += w['amp'] * math.sin(x / w['period'] + w['phase'])   # 同 make-world-mask.py:115
    return y


def rect_of(prop):
    """artRect：**立绘画布盒**，[footX-w/2, footY] × [footX+w/2, footY+height]。

    y 从 footY 向下延伸 height（画面 y 向下为正）。与运行期几何逐字同式：
    `UiKit.cs:73-81`（anchor(0,1)/pivot(0,1)/anchoredPosition=(x,-y)）＋
    `FrontEndWorld.cs:835-849`（pivot=(.5,1)，docstring 明写"立绘占世界 y ∈ [y, y+height]"）
    ＋ `TerrainPropShot.cs:130-135`（ArtRect 同式）。

    **这不是"房子占了街"的判据**：它只是画布盒，包含画布顶端那一条透明天空
    （[footY, footY+collisionTop]），也包含 blocks:false 的门洞件。物理判据见
    blocker_rects_of()。
    """
    w, h = float(prop['width']), float(prop['height'])
    x = float(prop['footX'])
    y = float(prop['footY'])
    return (x - w / 2.0, y, x + w / 2.0, y + h)


def body_rect_of(prop):
    """【已停用】旧版 `rect_of` 的口径：以 footY 为底边**向上**取 0.72*h 的框。

    2026-10-28 主架构师裁定：这个朝向与 ADR-018 相反，已不应被任何判据使用。
    函数保留只为让 `--gaps` 能复算那 17 对旧口径假警报，做封档对照；
    四条判据（G-E1~G-E4）现在一律用 `rect_of`（artRect，向下）。
    """
    h = float(prop['height']) * INSET
    x = float(prop['footX'])
    y = float(prop['footY'])
    return (x - h / 2.0, y - h, x + h / 2.0, y)


def intersect(a, b):
    x0, y0 = max(a[0], b[0]), max(a[1], b[1])
    x1, y1 = min(a[2], b[2]), min(a[3], b[3])
    return (x0, y0, x1, y1) if x0 < x1 and y0 < y1 else None


def area(a):
    return (a[2] - a[0]) * (a[3] - a[1]) if a else 0.0


# ---------------------------------------------------------------- 走廊（由 terrainShape 现算）

def corridor_rects(ts, y0=0.0, y1=8192.0, step=None, circulation=None):
    """把 terrainShape 的走廊离散成矩形列表 [(x0,y0,x1,y1,label)...]。

    主街是曲线，取 y 步长 STEP 采样成阶梯矩形（每段用两端 road_x 的包络），
    这样判定不会因为曲线而漏掉一段。其余走廊在 terrainShape 里本来就是矩形/圆，直接展开。

    step 可覆盖（灵敏度分析用）：值越小越贴近曲线、但也越容易把窄擦边算成重叠；
    默认 DEFAULT_STEP=40。**改 step 只影响曲线走廊的离散精度，不改任何阈值。**
    """
    rects = []
    STEP = float(step if step else DEFAULT_STEP)
    mr = ts['mainRoad']
    y = y0
    while y < y1:
        ya = y
        yb = min(y + STEP, y1)
        xa = road_x(ya, ts)
        xb = road_x(yb, ts)
        ha, hb = float(main_road_half(ya, ts)), float(main_road_half(yb, ts))
        lo = min(xa-ha, xb-hb)
        hi = max(xa+ha, xb+hb)
        rects.append((lo, ya, hi, yb, 'main-road'))
        y = yb

    for i, r in enumerate(ts.get('sideRoads') or []):
        # 纵向街：x = baseX + amp*sin(y/periodY + phase)（同 make-world-mask.py:185），按 y 采样成阶梯
        half = float(r['half'])
        base, amp = float(r['baseX']), float(r.get('amp', 0.0))
        per, ph = float(r.get('periodY', 900.0)), float(r.get('phase', 0.0))
        y = y0
        while y < y1:
            ya, yb = y, min(y + STEP, y1)
            ca = base + amp * math.sin(ya / per + ph)
            cb = base + amp * math.sin(yb / per + ph)
            rects.append((min(ca, cb) - half, ya, max(ca, cb) + half, yb,
                          'side-road-%d' % (i + 1)))
            y = yb

    for i, r in enumerate(ts.get('crossRoads') or []):
        half = float(r['half'])
        base, amp = float(r['baseY']), float(r.get('amp', 0.0))
        # town-map.json 的横路键是 period（make-world-mask.py:183 读 cr['period']）；periodY 兼容旧写法
        per = float(r.get('period', r.get('periodY', 800.0)))
        x = 0.0
        while x < 8192.0:
            xa, xb = x, min(x + STEP, 8192.0)
            ca = base + amp * math.sin(xa / per)       # 同 make-world-mask.py:183
            cb = base + amp * math.sin(xb / per)
            rects.append((xa, min(ca, cb) - half, xb, max(ca, cb) + half,
                          'cross-road-%d' % (i + 1)))
            x = xb

    for a in ts.get('alleys') or []:
        half = float(a['half'])
        if a.get('axis') == 'ns':
            cx = float(a['x'])
            rects.append((cx - half, float(a['y0']), cx + half, float(a['y1']),
                          str(a.get('id', 'alley'))))
        else:
            cy = float(a['y'])
            rects.append((float(a['x0']), cy - half, float(a['x1']), cy + half,
                          str(a.get('id', 'alley'))))

    for i, b in enumerate(ts.get('bridges') or []):
        cx, half = float(b['x']), float(b['half'])
        ry = river_y(cx, ts)
        bank = float(ts['river']['bankHalf'])
        rects.append((cx - half, ry - bank, cx + half, ry + bank, 'bridge-%d' % (i + 1)))

    fy = ts.get('ferry')
    if fy:
        cx, half = float(fy['x']), float(fy['half'])
        ry = river_y(cx, ts)
        bank = float(ts['river']['bankHalf'])
        rects.append((cx - half, ry - bank, cx + half, ry + bank, 'ferry'))

    circles = []
    for i, n in enumerate(ts.get('pavedNodes') or []):
        circles.append((float(n['x']), float(n['y']), float(n['r']), 'paved-%d' % (i + 1)))
    for key, label in (('plaza', 'plaza'), ('market', 'market')):
        c = ts.get(key)
        if c:
            circles.append((float(c['x']), float(c['y']), float(c['r']), label))
    for cx, cy, r, label in circles:
        rects.append((cx - r, cy - r, cx + r, cy + r, label))

    if circulation:
        # Replace only the existing plaza paving rectangle. Nothing outside it is exempted.
        bx0, by0, bx1, by1 = circulation['bounds']
        clipped = []
        for x0, y0, x1, y1, label in rects:
            ix0, iy0, ix1, iy1 = max(x0,bx0), max(y0,by0), min(x1,bx1), min(y1,by1)
            if ix0 >= ix1 or iy0 >= iy1:
                clipped.append((x0,y0,x1,y1,label)); continue
            for r in ((x0,y0,x1,iy0), (x0,iy1,x1,y1),
                      (x0,iy0,ix0,iy1), (ix1,iy0,x1,iy1)):
                if r[0] < r[2] and r[1] < r[3]: clipped.append((*r,label))
        rects = clipped + [(*b['rect'], 'plaza-ring-'+b['id']) for b in circulation['bands']]
    return rects


def validate_circulation(c, plots, ts):
    """The landscape is the real root's bounds, never a new runtime collider or an ID exemption."""
    tree = next(p for p in plots if p['id'] == c['landscape']['plotId'])
    cap = tree['collisionCapsule']
    x0, x1, y, radius = capsule_of(tree)
    root = [x0-radius,y-radius,x1+radius,y+radius]
    if not tree.get('blocks',True) or c['landscape']['collision'] != 'collisionCapsule':
        raise ValueError('Landscape must reference a blocking runtime capsule')
    if any(abs(a-b) > .001 for a,b in zip(root,c['landscape']['rect'])):
        raise ValueError('Landscape bounds differ from the real capsule')
    plaza = ts['crossPlaza']; bx0,by0,bx1,by1 = c['bounds']
    if c['bounds'] != [plaza['x']-plaza['halfX'],plaza['y']-plaza['halfY'],plaza['x']+plaza['halfX'],plaza['y']+plaza['halfY']]:
        raise ValueError('Circulation must stay on the existing plaza paving')
    rx0,ry0,rx1,ry1 = c['landscape']['rect']
    expected = {'north':[bx0,by0,bx1,ry0], 'east':[rx1,by0,bx1,by1],
                'south':[bx0,ry1,bx1,by1], 'west':[bx0,by0,rx0,by1]}
    if len(c['bands']) != 4 or {b['id']:b['rect'] for b in c['bands']} != expected:
        raise ValueError('Four complete rectangular circulation bands are required')
    margin = float(c['characterClearance'])
    if margin <= 0 or min(rx0-bx0,bx1-rx1,ry0-by0,by1-ry1) < 2*margin:
        raise ValueError('Plaza band cannot contain the character clearance')


# ---------------------------------------------------------------- 判据

class Violation(object):
    def __init__(self, cid, text):
        self.id = cid
        self.text = text


def blocker_rect(p):
    """运行期实际注册的阻挡矩形（与 WorldPropLayer.cs:192-196 同形）。blocks:false 返回 None。

    depth 的回退链照抄运行期：collisionDepth>0 → radius*2 → DEFAULT_BASE_DEPTH(24)。
    实盘 99/99 走第一支；后两支保留只为与运行期同形，不是本门禁在读 radius 做判据。
    collisionRects（MAP-27 分段阻挡）实盘 0 件使用，出现时本函数返回 None 并由调用方单列。
    """
    if not p.get('blocks', True):
        return None
    if p.get('collisionRects'):
        return None
    depth = float(p.get('collisionDepth') or 0)
    if depth <= 0:
        depth = float(p['radius']) * 2.0 if float(p.get('radius') or 0) > 0 else 24.0
    top = float(p.get('collisionTop') or 0)
    w = float(p['width'])
    x = float(p['footX'])
    y = float(p['footY'])
    return (x - w / 2.0, y + top, x + w / 2.0, y + depth)


def coverage_ratio(p):
    """G-E1 的量：(collisionDepth - collisionTop) / height。拿不到单矩形的返回 None。"""
    r = blocker_rect(p)
    if r is None:
        return None
    return (r[3] - r[1]) / float(p['height'])


def _quantile(v, f):
    v = sorted(v)
    i = (len(v) - 1) * f
    lo = int(i)
    hi = min(lo + 1, len(v) - 1)
    return v[lo] + (v[hi] - v[lo]) * (i - lo)


def e1_distribution(plots):
    """G-E1 分布（只报数）。返回 (行列表, 跳过的件)。"""
    vals, by, skipped = [], {}, []
    for p in plots:
        c = coverage_ratio(p)
        if c is None:
            skipped.append(p['id'])
            continue
        vals.append(c)
        by.setdefault(p.get('kind', '?'), []).append(c)
    lines = []

    def row(name, v):
        return ('%-9s n=%3d  min=%.3f  P25=%.3f  P50=%.3f  P75=%.3f  max=%.3f'
                % (name, len(v), min(v), _quantile(v, .25), _quantile(v, .5),
                   _quantile(v, .75), max(v)))
    if vals:
        lines.append(row('全表', vals))
        for k in sorted(by):
            lines.append(row(k, by[k]))
    return lines, skipped


def check_e1(plots):
    """G-E1 当前是 REPORT 级：阈值未定，不产出任何违规。

    返回空列表是**刻意的**，不代表通过。分布由 e1_distribution 打印；
    阈值给定后在这里补判定，并把 LEVEL['G-E1'] 改成 'FAIL'。
    """
    return []


def check_e2(plots):
    """G-E2 摆件彼此视觉盒重叠（方案 E.2）。"""
    bad = []
    rects = [(p, rect_of(p)) for p in plots]

    def same_cluster(a, b):
        if not (a.get('cluster') and b.get('cluster')):
            return False
        return str(a['id']).rsplit('-', 1)[0] == str(b['id']).rsplit('-', 1)[0]

    for i in range(len(rects)):
        pa, ra = rects[i]
        for j in range(i + 1, len(rects)):
            pb, rb = rects[j]
            hit = intersect(ra, rb)
            if not hit:
                continue
            a = area(hit)
            if a <= MIN_CONFLICT_AREA:
                continue
            if same_cluster(pa, pb):
                continue
            bad.append(Violation('G-E2', '%s × %s 视觉盒重叠 %d px²（%s / %s）'
                                 % (pa['id'], pb['id'], a, pa.get('name', ''),
                                    pb.get('name', ''))))
    return bad


def blocker_rects_of(prop):
    """运行期真正注册的阻挡矩形列表；与 `WorldPropLayer.cs:176-199` 逐字同式。

    这才是"房子占了街"的物理量：玩家撞不撞得上，由这几块决定。
      - `blocks: false`（牌坊这类门洞件）→ 运行期一个矩形都不注册 → 返回 []
      - `collisionCapsule` 优先：本函数返回包围盒，blocker_hits 用真实胶囊求交
      - `collisionRects` 非空（MAP-27 分段阻挡）→ 每条按 footX/footY 偏移展开
      - 否则单矩形：x ∈ [footX-w/2, footX+w/2]，y ∈ [footY+top, footY+depth]
          depth = collisionDepth>0 ? collisionDepth
                : radius>0 ? radius*2 : DEFAULT_BASE_DEPTH(24)
          top   = collisionTop>0 ? collisionTop : 0

    注意 footY 是**画布顶边**（= 根基带的北边界，见 `TerrainPropShot.cs:872-874`
    "脚点的 y 正好等于根基带的北边界"），不是墙脚；墙脚在 footY+height。
    """
    if not prop.get('blocks', True):
        return []
    x = float(prop['footX'])
    y = float(prop['footY'])
    cap = capsule_of(prop)
    if cap:
        x0,x1,cy,r = cap
        return [(x0-r,cy-r,x1+r,cy+r)]  # Bounds only; blocker_hits clips against the actual capsule.
    segs = prop.get('collisionRects')
    if segs:
        return [(x + float(g['x0']), y + float(g['y0']),
                 x + float(g['x1']), y + float(g['y1'])) for g in segs]
    half = float(prop['width']) / 2.0
    depth = float(prop.get('collisionDepth') or 0.0)
    if depth <= 0.0:
        radius = float(prop.get('radius') or 0.0)
        depth = radius * 2.0 if radius > 0.0 else DEFAULT_BASE_DEPTH
    top = float(prop.get('collisionTop') or 0.0)
    return [(x - half, y + top, x + half, y + depth)]


def capsule_of(prop):
    c = prop.get('collisionCapsule')
    if c is None or not prop.get('blocks',True): return None
    return (float(prop['footX'])+float(c['x0']), float(prop['footX'])+float(c['x1']),
            float(prop['footY'])+float(c['y']), float(c['radius']))


def capsule_rect_area(c, rect):
    # Horizontal capsule = centre rectangle + two clipped half-discs.
    x0,x1,cy,r = c
    def disk_area(cx, box):
        if box[0] >= box[2] or box[1] >= box[3]: return 0.0
        def quadrant(x,y):
            sign = 1 if x*y >= 0 else -1
            x,y = min(abs(x),r),min(abs(y),r)
            t = min(x, math.sqrt(max(0,r*r-y*y)))
            def primitive(v):
                return .5*(v*math.sqrt(max(0,r*r-v*v))+r*r*math.asin(v/r))
            return sign*(y*t+primitive(x)-primitive(t))
        a,b,d,e = box[0]-cx,box[1]-cy,box[2]-cx,box[3]-cy
        return max(0,quadrant(d,e)-quadrant(a,e)-quadrant(d,b)+quadrant(a,b))
    hit = intersect((x0,cy-r,x1,cy+r),rect)
    middle = area(hit) if hit else 0.0
    return middle + disk_area(x0,(rect[0],rect[1],min(rect[2],x0),rect[3])) + disk_area(x1,(max(rect[0],x1),rect[1],rect[2],rect[3]))


def blocker_hits(prop, rects):
    cap = capsule_of(prop)
    for band in blocker_rects_of(prop):
        for x0,y0,x1,y1,label in rects:
            hit = intersect(band,(x0,y0,x1,y1))
            if not hit: continue
            overlap = capsule_rect_area(cap,hit) if cap else area(hit)
            if overlap > 1e-8: yield label,hit,overlap


def check_e3(plots, rects):
    """G-E3 阻挡带 × 走廊可走带（2026-10-31 主架构师裁定后的第三版语义）。

    ## 一句话

    **这件摆件运行期注册的阻挡矩形，有没有压进人能走的走廊带。** 压了 ⇒ 玩家撞墙
    ⇒ 房子占了街。

    ## 为什么不是 artRect（旧口径测错了什么）

    旧版拿 `rect_of()`（立绘画布盒）当占地。画布盒比阻挡带多出两块**画出来的空处**：
      1. 画布顶端到首个不透明行为止的那条透明天空 `[footY, footY+collisionTop]`
         （`plot-cloth-shop` top=181/520，整条 181 高的空带压在 alley-south-east 上，
         实测画带 28000 px²、阻挡带 0 px²）；
      2. `blocks:false` 的门洞件（牌坊三门洞可穿行，运行期一个矩形都不注册，
         旧口径把它算成压主街 273900 px²）。
    两者都不是"房子占了街"。实测旧口径 144 条里，这两类共 4 条；其余 140 条在
    阻挡带口径下照旧成立 —— **这次重写不是为了把红变绿**（144→140），是把
    "画出来但走不进去"和"真的挡在路上"分开。

    ## 聚合

    曲线走廊离散成多段，同一件的多段先按 label **累加全部相交面积**，累加完再与
    MIN_CONFLICT_AREA 比。旧版是"每段先过阈值、过了的段再加总"，于是总压占 >400
    但每段都 ≤400 的件会整条漏报（实测 `plot-street-east-gate` × side-road-2：
    8 段 41/100/157/211/264/314/362/408，旧版只报最后一段 407，合计 1857）。

    一条违规 = 一件摆件 × 一条走廊。
    """
    bad = []
    for p in plots:
        agg = {}                  # label -> [x0, y0, x1, y1, 累计面积]
        for label,hit,overlap in blocker_hits(p,rects):
            g = agg.get(label)
            if g is None:
                agg[label] = [hit[0],hit[1],hit[2],hit[3],overlap]
            else:
                g[0],g[1] = min(g[0],hit[0]),min(g[1],hit[1])
                g[2],g[3] = max(g[2],hit[2]),max(g[3],hit[3])
                g[4] += overlap
        for label in sorted(agg):
            g = agg[label]
            if g[4] <= MIN_CONFLICT_AREA:
                continue
            bad.append(Violation('G-E3', '%s 阻挡带(foot %d,%d %dx%d top=%d depth=%d) '
                                         '× 走廊[%s] 压占 x %d-%d y %d-%d（%d px²）'
                                 % (p['id'], p['footX'], p['footY'],
                                    p['width'], p['height'],
                                    p.get('collisionTop') or 0, p.get('collisionDepth') or 0,
                                    label, g[0], g[2], g[1], g[3], g[4])))
    return bad


def art_occlusions(plots, rects):
    """旧口径留档：立绘画布盒压走廊的条数（只报数，不判定）。

    返回 (总数, 仅画带有而阻挡带没有的 [(id, label)])。保留它是因为"屋顶在画面上
    压住路面"仍然影响可读性，只是它不是"房子占了街"。
    """
    art_hits = set()
    for p in plots:
        art = rect_of(p)
        per = {}
        for (x0, y0, x1, y1, label) in rects:
            hit = intersect(art, (x0, y0, x1, y1))
            if not hit:
                continue
            per[label] = per.get(label, 0.0) + area(hit)
        art_hits |= {(p['id'], l) for l, a in per.items() if a > MIN_CONFLICT_AREA}
    phys = set()
    for p in plots:
        per = {}
        for label,hit,overlap in blocker_hits(p,rects):
            per[label] = per.get(label,0.0) + overlap
        phys |= {(p['id'], l) for l, a in per.items() if a > MIN_CONFLICT_AREA}
    return len(art_hits), sorted(art_hits - phys)


def check_e4(plots, ts):
    """G-E4 贴路立面朝街（方案 E.2，只判 kind=building）。"""
    bad = []
    for p in plots:
        if p.get('kind') != 'building':
            continue
        dx = float(p['footX']) - road_x(float(p['footY']), ts)
        adx = abs(dx)
        if adx < BANDS[0] or adx > BANDS[1]:
            continue
        facing = p.get('facing')
        if not facing:
            bad.append(Violation('G-E4', '%s 贴路 |dx|=%.0f 但无 facing 字段（背街摆）'
                                 % (p['id'], adx)))
            continue
        want = 'east' if dx < -FACING_DX_EPS else ('west' if dx > FACING_DX_EPS else None)
        if want is None:
            bad.append(Violation('G-E4', '%s |dx|=%.0f 落在主街中轴上，不存在"朝街"解'
                                 % (p['id'], adx)))
            continue
        if str(facing).lower() != want:
            bad.append(Violation('G-E4', '%s 贴路 |dx|=%.0f facing=%s，应朝 %s（背街摆）'
                                 % (p['id'], adx, facing, want)))
    return bad


# ---------------------------------------------------------------- 主流程

def load(plots_path, map_path):
    with open(plots_path, encoding='utf-8') as fh:
        pj = json.load(fh)
    with open(map_path, encoding='utf-8') as fh:
        mj = json.load(fh)
    plots = pj['plots'] if isinstance(pj, dict) else pj
    ts = mj['terrainShape']
    circulation_path = os.path.join(os.path.dirname(map_path), 'town-circulation.json')
    if os.path.isfile(circulation_path):
        with open(circulation_path,encoding='utf-8') as fh: mj['circulation'] = json.load(fh)
        validate_circulation(mj['circulation'],plots,ts)
    return plots, mj, ts


def run(plots, ts, mj, quiet=False, step=None):
    """返回 (violations, notes, info)。四条判据全跑，互不短路。

    violations 只含 FAIL 级（G-E2/G-E3）；info 是 G-E4 的 INFO 行；G-E1 分布由 emit 现算。

    step 只影响曲线走廊的离散精度（灵敏度分析用），不改任何阈值。默认 DEFAULT_STEP。
    """
    v = []
    notes = []
    if mj.get('props') or mj.get('walkable'):
        notes.append('town-map.json 的 props/walkable 非空，但本门禁按规格 1.6.1 **不读**'
                     '这两个字段（数据源已是 building-plots.json + terrainShape）')
    st = float(step if step else DEFAULT_STEP)
    if mj.get("circulation"): validate_circulation(mj["circulation"],plots,ts)
    rects = corridor_rects(ts, step=st, circulation=mj.get("circulation"))
    v += check_e1(plots)
    v += check_e2(plots)
    v += check_e3(plots, rects)
    info = check_e4(plots, ts)          # INFO 级，单独返回，不进 v
    if not quiet:
        out('=== 城镇布局遮挡 / 穿模门禁（G-E1~G-E4）===')
        out('摆件 %d 件 ｜ 走廊矩形 %d 段（曲线走廊按 %d 步长采样）'
            % (len(plots), len(rects), st))
        out('阈值（照抄方案 E 节）：minArea=%d  band=%s ｜ G-E1 阈值：待架构师定'
            % (int(MIN_CONFLICT_AREA), '%.0f–%.0f' % BANDS))
    return v, notes, info


CIDS = ('G-E1', 'G-E2', 'G-E3', 'G-E4')
TITLES = {
    'G-E1': '阻挡带纵向覆盖 (depth-top)/height ·只报数，阈值待架构师定',
    'G-E2': '摆件 × 摆件 视觉盒重叠 ≤400 px²（排除同簇装饰）',
    'G-E3': '摆件阻挡带 × 走廊可走带 ≤400 px²（运行期真注册的那块）',
    'G-E4': '贴路立面应 facing 朝街 ·INFO（运行期无朝向能力）',
}


def emit(v, notes, plots, ts, info=(), circulation=None):
    by = {c: [x for x in v if x.id == c] for c in CIDS}
    e1_lines, e1_skip = e1_distribution(plots)
    for c in CIDS:
        lv = LEVEL[c]
        if lv == 'FAIL':
            tail = 'FAIL %d' % len(by[c])
        elif lv == 'REPORT':
            tail = 'REPORT（不判定）'
        else:
            tail = 'INFO %d（不计入 FAIL）' % len(info)
        out('%-6s %-52s %s' % (c, TITLES[c], tail))
    out('')
    out('-- G-E1 分布 (collisionDepth - collisionTop) / height ·阈值待架构师定，本表不是通过 --')
    for line in e1_lines:
        out('REPORT: [G-E1] ' + line)
    if e1_skip:
        out('REPORT: [G-E1] 无单矩形阻挡、未计入分布：%s' % ', '.join(e1_skip))
    art_n, art_only = art_occlusions(plots, corridor_rects(ts, circulation=circulation))
    out('REPORT: [G-E3-art] 画带(artRect)压走廊 %d 条（旧口径，只报数，不判定）'
        % art_n)
    if art_only:
        out('REPORT: [G-E3-art] 其中 %d 条在阻挡带口径下不成立（透明顶带/门洞件）：%s'
            % (len(art_only), ', '.join('%s×%s' % x for x in art_only)))
    out('')
    for c in CIDS:
        for x in by[c]:
            out('FAIL: [%s] %s' % (x.id, x.text))
    for x in info:
        out('INFO: [%s] %s' % (x.id, x.text))
    for n in notes:
        out('NOTE: ' + n)
    total = len(v)
    out('\nSUMMARY: 判据 %d 条（判定 %d / 只报数 1 / INFO 1），摆件 %d 件，FAIL %d 项，INFO %d 项'
        % (len(CIDS), sum(1 for c in CIDS if LEVEL[c] == 'FAIL'), len(plots), total, len(info)))
    if total:
        hit = sorted({x.id for x in v})
        out('FAILED: ' + ', '.join(hit))
        out('RESULT: FAIL（%d 项违规）' % total)
        return 1
    out('RESULT: PASS（G-E2/G-E3 零违规；G-E1 未判定，G-E4 仅 INFO）')
    return 0


def main(plots_path, map_path, report=False):
    try:
        plots, mj, ts = load(plots_path, map_path)
    except (IOError, OSError, KeyError, ValueError) as e:
        print('RESULT: FAIL（输入缺失/解析失败：%s）' % e)
        return 2
    v, notes, info = run(plots, ts, mj)
    rc = emit(v, notes, plots, ts, info, mj.get("circulation"))
    if not report:
        try:
            os.makedirs(os.path.dirname(LOG), exist_ok=True)
            with open(LOG, 'w', encoding='utf-8', newline='\n') as fh:
                fh.write('\n'.join(OUT) + '\n')
            print('（全表已写：%s）' % os.path.relpath(LOG, ROOT))
        except (IOError, OSError) as e:
            print('NOTE: 日志未落盘（%s），判定结果不受影响' % e)
    return rc


# ---------------------------------------------------------------- 门禁 × 画面 对齐（selftest 用）

MASK_PATH = os.path.join(ROOT, 'GameClient', 'TwelveJade', 'Assets', 'ArtSource', 'world', 'masks',
                         'world-mask.png')
MASK_TOL = 8.0                    # 世界单位 = maskScale(4) × 2 像素：mask 量化误差上限
MASK_MAIN_Y = range(3300, 7801, 250)


def mask_alignment(road_fn, ts, mask_path=MASK_PATH, cfg_dir=CFG):
    """拿 road_fn(y, ts) 去和 world-mask.png 的主街实测中心比，返回 (可信点数, max|Δ|, 明细)。

    world-mask.png 是单通道 L 图，像素值 = terrain-types.json 的 type value（make-world-mask.py:339）。
    主街类型取 stone-road ∪ dirt-road（make-world-mask.py:205-207）。plaza(4) 是镇圆/广场的面，不是路。
    每行只认"覆盖 road_fn 预测中心、或离它最近"的那一段，且段宽必须 ≈ 2*half（±3 像素），
    段宽对不上的行（被广场/路口连成一片）不计入——只用干净的行判。
    任何读不到 mask 的情况都抛异常，**不许静默跳过**：缺 mask 等于失去这道对齐保证。
    """
    import numpy as np
    from PIL import Image
    with open(os.path.join(cfg_dir, 'terrain-types.json'), encoding='utf-8') as fh:
        tt = json.load(fh)
    values = {t['id']: int(t['value']) for t in tt['types']}
    scale = float(tt['maskScale'])
    im = Image.open(mask_path)
    if im.mode != 'L':
        raise ValueError('world-mask.png 模式 %s 不是 L（类型索引口径变了）' % im.mode)
    mask = np.array(im)
    want = [values['stone-road'], values['dirt-road']]
    half = float(ts['mainRoad']['half'])
    rows = []
    for y in MASK_MAIN_Y:
        line = np.isin(mask[int(y / scale)], want).astype(np.int8)
        d = np.diff(np.concatenate(([0], line, [0])))
        segs = list(zip(np.where(d == 1)[0] * scale, np.where(d == -1)[0] * scale))
        if not segs:
            continue
        c = road_fn(y, ts)
        a, b = min(segs, key=lambda ab: 0.0 if ab[0] <= c <= ab[1] else min(abs(c - ab[0]), abs(c - ab[1])))
        if abs((b - a) - 2 * half) > 3 * scale + 1:
            continue
        rows.append((y, (a + b) / 2.0, c))
    if len(rows) < 5:
        raise ValueError('mask 上可信的主街行只有 %d 条（<5），对齐判据不成立' % len(rows))
    return len(rows), max(abs(m_ - c) for _, m_, c in rows), rows


# ---------------------------------------------------------------- 自检

def fixture_ts():
    """自足夹具：与实盘同值的 terrainShape（不读真配置）。"""
    return {
        'river': {'baseY': 2700.0,
                  'wave': [{'amp': 170, 'period': 980, 'phase': 0.0},
                           {'amp': 80, 'period': 430, 'phase': 1.1}],
                  'waterHalf': 125, 'bankHalf': 185},
        'mainRoad': {'baseX': 4096.0, 'amp': 140, 'periodY': 1500, 'half': 250.0},
        'crossRoads': [{'baseY': 4300.0, 'amp': 45, 'periodY': 800, 'half': 100.0}],
        'sideRoads': [{'baseX': 2400.0, 'amp': 40, 'periodY': 900, 'half': 150.0, 'phase': 0.0}],
        'alleys': [{'id': 'alley-selftest', 'axis': 'ns', 'x': 3100.0, 'half': 100.0,
                    'y0': 5350.0, 'y1': 6200.0}],
        'bridges': [], 'ferry': None,
        'plaza': {'x': 4096.0, 'y': 4700.0, 'r': 620.0},
        'market': {'x': 3100.0, 'y': 4950.0, 'r': 340.0},
        'pavedNodes': [],
        'townRadius': 1600.0,
    }


def mk(pid, x, y, w, h, kind='building', radius=None, **kw):
    """造一件 plot。radius 缺省给 ceil(0.36*W)（合规值），便于只烧一个变量。"""
    p = {'id': pid, 'kind': kind, 'name': pid, 'footX': float(x), 'footY': float(y),
         'width': float(w), 'height': float(h), 'art': 'Art/Props/x',
         'radius': float(radius if radius is not None else math.ceil(INSET * float(w))),
         'collisionDepth': round(0.99 * float(h)), 'collisionTop': 0.0}
    p.update(kw)
    return p


def good_fixture(ts):
    """合规夹具：全部件碰撞半径按公式、彼此不相交、不压任何走廊、|dx| 不落 E4 带。

    夹具位置是逐个手核过的（不是拍脑袋）：主街 |dx| > 520（避开 E4 带）、
    避开 market(r340@3100,4950) / plaza(r620@4096,4700) / alley(x3100,y5350–6200) /
    crossRoad(y4300±145) / sideRoad(x2400±190)。改坐标前先看 corridor_rects 展开的走廊集。
    """
    ps = []
    for i, (x, y) in enumerate(((300, 600), (1800, 600), (3300, 600), (4800, 600), (6300, 600))):
        ps.append(mk('ok-house-%d' % i, x, y, 400, 400))
    for i, (x, y) in enumerate(((7200, 600), (300, 1500), (1200, 1500))):
        ps.append(mk('ok-prop-%d' % i, x, y, 200, 200, 'prop'))
    return ps


def fired(v, cid):
    return [x for x in v if x.id == cid]


def selftest():
    ts = fixture_ts()
    good = good_fixture(ts)
    cases = []   # (name, plots, ts, 期望 rc, 期望点火的判据 or None, 期望命中关键字 or None)

    # 0 基线：合规夹具必须绿（证明判据不是"永远红"）
    cases.append(('0_合规夹具必绿', good, ts, 0, None, None, {}))

    # ---- G-E1：REPORT 级（阈值待架构师定）----
    # 1 阻挡带极薄（top=depth-1）：分布里必须看得见这件，但**不许**让 rc 变红
    thin = good + [mk('thin-blocker', 1800, 1500, 400, 400, collisionTop=395.0, collisionDepth=396.0)]
    cases.append(('1_G-E1薄阻挡只报数不判红', thin, ts, 0, None, None,
                  {'report_has': 'min=0.003'}))
    # 2 分布真被算出来：合规夹具上 (depth-top)/h = 0.99（mk 默认 depth=round(0.99h), top=0）
    cases.append(('2_G-E1分布非空且量对', good, ts, 0, None, None,
                  {'report_has': 'P50=0.990'}))
    # 3 blocks:false 不进分布（运行期不注册阻挡矩形），且列名点出
    gate = good + [mk('plot-paifang', 1800, 1500, 415, 660, blocks=False)]
    cases.append(('3_G-E1门洞件不计入分布', gate, ts, 0, None, None,
                  {'report_has': '未计入分布：plot-paifang'}))

    # 4 G-E2 两件视觉盒重叠（同位置同尺寸 → 160000 px²）
    bad_e2 = good + [mk('overlap-a', 1600, 3900, 400, 400), mk('overlap-b', 1620, 3910, 400, 400)]
    cases.append(('4_G-E2摆件互叠必红', bad_e2, ts, 1, 'G-E2', '视觉盒重叠', {}))

    # 5 G-E2 同簇豁免（cluster:true 且 id 前缀相同 → 不许报）
    #    这里刻意做成"视觉盒重叠但簇内豁免"，用例断言整份绿
    ok_cluster = good + [mk('grove-tree-1', 1800, 1500, 400, 400, 'tree', cluster=True),
                         mk('grove-tree-2', 1820, 1510, 400, 400, 'tree', cluster=True)]
    cases.append(('5_G-E2同簇豁免必绿', ok_cluster, ts, 0, None, None, {}))

    # ---- G-E3 新语义（artRect 向下，无 "y < footY" 条件）----
    # 6 件把 artRect 整个罩在主街中轴上：foot 在中轴、宽 400 高 400
    #   artRect = [cx-200, cx+200] × [y, y+400]，主街半宽 250 → 相交必然 400*400=160000 px²
    y3 = 5000
    bad_e3 = good + [mk('blocking-house', road_x(y3, ts), y3, 400, 400)]
    cases.append(('6_G-E3压主街必红', bad_e3, ts, 1, 'G-E3', '× 走廊[main-road]', {}))

    # 7 压小巷（alley-selftest x3100 / y5350–6200）：件放在巷内，artRect 与巷矩形相交
    #   artRect = [2950,3250] × [5600,5900]；巷矩形 = [3000,3200] × [5350,6200]
    #   相交 = [3000,3200] × [5600,5900] = 200*300 = 60000 px² > 400
    bad_alley = good + [mk('blocking-alley', 3100, 5600, 300, 300)]
    cases.append(('7_G-E3压小巷必红', bad_alley, ts, 1, 'G-E3', '× 走廊[alley-selftest]', {}))

    # 8 反向校准：件"贴着"主街但 artRect 刚好不进路。
    #   y=6800 段只被 main-road 与 side-road-1 覆盖（实测 labels_near(6800)），无广场/横路/巷。
    #   road_x(6800) = 4096+140*sin(6800/1500) ≈ 3958.2，半宽 250 → 路东缘 ≈ 4208.2。
    #   件 footX = 4408.2、宽 400 → artRect 左缘恰在 4208.2；y∈[6800,7200] 段路中轴单调西移，
    #   逐 y 最小间隙实测 0.0（2026-10-31 按无 2π 公式重校），相交面积 0 → 必须绿。
    #   坐标由 road_x 现算，公式改了夹具跟着走；这里的数字只是说明，不是写死的输入。
    #   件在路**东侧** → facing 必须朝 west（朝路）。同时避开 G-E4 的 440–520 带告警。
    rx6 = road_x(6800, ts)
    ok_adjacent = good + [mk('adjacent-house', rx6 + 250 + 200, 6800, 400, 400, facing='west')]
    cases.append(('8_G-E3贴路相切必绿', ok_adjacent, ts, 0, None, None, {}))

    # 9 反向校准：件放到路侧远处（artRect 向下延 400 也没碰到路）→ 必须绿。
    #   同一段 y=6800，footX 取路东缘再往外 800，宽 400。
    ok_far = good + [mk('far-east-house', rx6 + 250 + 800, 6800, 400, 400, facing='west')]
    cases.append(('9_G-E3路侧远处必绿', ok_far, ts, 0, None, None, {}))

    # 8 G-E4 贴路但无 facing（y=2500 段主街不压其它走廊）
    y4 = 2500.0
    bad_e4 = good + [mk('nofacing-house', road_x(y4, ts) - 470, y4, 340, 400)]
    cases.append(('10_G-E4缺facing只出INFO', bad_e4, ts, 0, None, None,
                  {'info_has': '无 facing 字段'}))

    # 9 G-E4 facing 背街（在路西却朝 west）
    bad_e4b = good + [mk('backfacing-house', road_x(y4, ts) - 470, y4, 340, 400, facing='west')]
    cases.append(('11_G-E4背街只出INFO', bad_e4b, ts, 0, None, None,
                  {'info_has': '应朝 east'}))

    # 10 G-E4 正解必须绿（路西朝 east）
    ok_e4 = good + [mk('facing-house', road_x(y4, ts) - 470, y4, 340, 400, facing='east')]
    cases.append(('12_G-E4正解无INFO', ok_e4, ts, 0, None, None, {'info_empty': True}))
    # 13 降级不许吞掉 FAIL：同一份夹具里 G-E4 出 INFO、G-E3 压路照样判红
    mixed = bad_e4 + [mk('blocking-house-2', road_x(5000, ts), 5000, 400, 400)]
    cases.append(('13_G-E4降级不吞G-E3的红', mixed, ts, 1, 'G-E3', '× 走廊[main-road]',
                  {'info_has': '无 facing 字段'}))

    # 16 透明顶带压路：画带压巷、阻挡带不压 —— 新口径必须绿，且旧口径只报数里点名
    #   alley-selftest x3100 / y5350–6200；件画带 y=[6190,6590]，阻挡带=[6580,6590]
    sky = good + [mk('sky-band-only', 3100, 6190, 400, 400,
                     collisionTop=390.0, collisionDepth=400.0)]
    cases.append(('16_透明顶带压路不判红', sky, ts, 0, None, None,
                  {'art_report_has': 'sky-band-only×alley-selftest'}))

    # 17 门洞件（blocks:false）：运行期一个矩形都不注册 —— 压在主街上也必须绿
    gate_on_road = good + [mk('plot-paifang', road_x(5000, ts), 5000, 415, 660, blocks=False)]
    cases.append(('17_门洞件压主街不判红', gate_on_road, ts, 0, None, None,
                  {'art_report_has': 'plot-paifang×main-road'}))

    bad = []
    print('=== selftest：每条新判据都要能点火，好例必须过 ===')
    for name, plots, t, want_rc, want_cid, want_kw, extra in cases:
        v, _notes, info = run(plots, t, {}, quiet=True)
        rc = 1 if v else 0
        text = ' ｜ '.join('%s %s' % (x.id, x.text) for x in v)
        info_text = ' ｜ '.join(x.text for x in info)
        report_text = ' ｜ '.join(e1_distribution(plots)[0])
        skip = e1_distribution(plots)[1]
        if skip:
            report_text += ' ｜ 未计入分布：' + ', '.join(skip)
        ok, why = True, []
        if rc != want_rc:
            ok = False
            why.append('期望 rc=%d 实得 rc=%d' % (want_rc, rc))
        if want_cid and not fired(v, want_cid):
            ok = False
            why.append('期望 %s 点火但没点' % want_cid)
        if want_kw and want_kw not in text:
            ok = False
            why.append('期望命中 %r' % want_kw)
        if any(x.id in ('G-E1', 'G-E4') for x in v):
            ok = False
            why.append('G-E1/G-E4 混进了 FAIL 列表（降级失效）')
        if 'info_has' in extra and extra['info_has'] not in info_text:
            ok = False
            why.append('期望 INFO 命中 %r，实际 INFO：%s' % (extra['info_has'], info_text or '(无)'))
        if extra.get('info_empty') and info:
            ok = False
            why.append('期望无 INFO，实际：%s' % info_text)
        if 'art_report_has' in extra:
            _n, _only = art_occlusions(plots, corridor_rects(t))
            art_text = ', '.join('%s×%s' % x for x in _only)
            if extra['art_report_has'] not in art_text:
                ok = False
                why.append('期望旧口径只报数命中 %r，实际：%s'
                           % (extra['art_report_has'], art_text or '(空)'))
        if 'report_has' in extra and extra['report_has'] not in report_text:
            ok = False
            why.append('期望分布命中 %r，实际：%s' % (extra['report_has'], report_text[:200]))
        if ok:
            print('  [PASS] %-26s rc=%d 断言=%s' % (name, rc,
                  want_cid or next(iter(extra), 'PASS')))
        else:
            bad.append(name)
            print('  [FAIL] %-26s %s；FAIL 输出：%s' % (name, '；'.join(why), text[:200] or '(无)'))

    # ---- 14/15 门禁 × 画面 对齐（2026-10-31，本轮最重要的一条）----
    # 14 现行 road_x 与 world-mask.png 实测主街中心偏差 ≤ MASK_TOL（读真 mask、真 terrainShape）
    # 15 反证：把 2π 公式注入同一测量，必须超出 MASK_TOL——证明 14 不是"什么都能过"
    try:
        with open(os.path.join(CFG, 'town-map.json'), encoding='utf-8') as fh:
            real_ts = json.load(fh)['terrainShape']
        n, dmax, _rows = mask_alignment(road_x, real_ts)
        if dmax <= MASK_TOL:
            print('  [PASS] %-26s 可信行 %d，max|Δ|=%.1f ≤ %.0f' % ('14_road_x与mask对齐', n, dmax, MASK_TOL))
        else:
            bad.append('14_road_x与mask对齐')
            print('  [FAIL] %-26s 可信行 %d，max|Δ|=%.1f > %.0f：门禁的路不在画面上的路上'
                  % ('14_road_x与mask对齐', n, dmax, MASK_TOL))

        def _road_x_2pi(y, t):
            mr = t['mainRoad']
            return float(mr['baseX']) + mr['amp'] * math.sin(2 * math.pi * y / mr['periodY'])
        try:
            n2, d2, _ = mask_alignment(_road_x_2pi, real_ts)
            fired_2pi = d2 > MASK_TOL
            detail = '可信行 %d，max|Δ|=%.1f' % (n2, d2)
        except ValueError as e:          # 错公式连 5 条可信行都对不上，同样算"被抓住"
            fired_2pi, detail = True, '对齐判据拒绝：%s' % e
        if fired_2pi:
            print('  [PASS] %-26s 注入 sin(2πy/P)：%s → 判红' % ('15_2π公式必被抓', detail))
        else:
            bad.append('15_2π公式必被抓')
            print('  [FAIL] %-26s 注入 sin(2πy/P) 竟然过了：%s' % ('15_2π公式必被抓', detail))
    except Exception as e:                # 缺 mask/缺依赖/读失败：显式红，不许跳过
        bad.extend(['14_road_x与mask对齐', '15_2π公式必被抓'])
        print('  [FAIL] 14/15 mask 对齐无法执行（%s: %s）——缺这道保证即判红' % (type(e).__name__, e))
    # 18 聚合缺陷回归（合成走廊，不走 terrainShape）：每段 5×40=200 ≤400，10 段合计 2000 >400
    sliver = [mk('agg-sliver', 100.0, 0.0, 400.0, 400.0,
                 collisionTop=0.0, collisionDepth=400.0)]
    seam = [(90.0, float(y), 95.0, float(y) + 40.0, 'seam') for y in range(0, 400, 40)]
    hits = fired(check_e3(sliver, seam), 'G-E3')
    if len(hits) == 1 and '2000 px' in hits[0].text:
        print('  [PASS] %-26s 逐段 200 ≤400，合计 2000 px² 抓得住' % '18_逐段过阈聚合必红')
    else:
        bad.append('18_逐段过阈聚合必红')
        print('  [FAIL] 18_逐段过阈聚合必红: 命中 %d 条，文本 %s'
              % (len(hits), hits[0].text if hits else '（无）'))

    # Profile width must be consumed by the checker, not merely saved in the config.
    import copy
    neck=copy.deepcopy(ts)
    neck['mainRoad']['widthZones']=[dict(y0=6300,y1=6900,half=65,blend=240)]
    neck_corr=[r for r in corridor_rects(neck) if r[4]=='main-road' and r[1]>=6500 and r[3]<=6700]
    neck_ok=bool(neck_corr) and all(r[2]-r[0]<140 for r in neck_corr)
    neck_bad=mk('neck-obstruction',road_x(6600,neck),6600,100,100,collisionTop=0,collisionDepth=100)
    neck_ok=neck_ok and bool(fired(check_e3([neck_bad],corridor_rects(neck)),'G-E3'))
    if not neck_ok:bad.append('19_收窄仍拦截占路')
    print('  [%s] 19_收窄消费配置且道路中障碍仍判红' % ('PASS' if neck_ok else 'FAIL'))
    # Runtime capsule priority, round corners and a blocker on each declared plaza band.
    capsule = mk('capsule-priority',100,0,200,200,collisionTop=0,collisionDepth=200)
    capsule['collisionCapsule'] = dict(x0=0,x1=0,y=100,radius=100)
    capsule['collisionRects'] = [dict(x0=-100,y0=0,x1=100,y1=200)]
    capsule_ok = not check_e3([capsule],[(0,0,25,25,'outside-round-corner')])
    capsule_ok &= bool(check_e3([capsule],[(80,80,120,120,'capsule-centre')]))
    capsule_ok &= abs(capsule_rect_area(capsule_of(capsule),(0,0,200,200))-math.pi*10000)<1e-6
    capsule_ok &= abs(capsule_rect_area((80,120,100,30),(0,0,200,200))-(2400+math.pi*900))<1e-6
    if not capsule_ok: bad.append('20_capsule-priority-and-corners')
    print('  [%s] 20_capsule-priority-and-corners' % ('PASS' if capsule_ok else 'FAIL'))
    plaza_ok = True
    try:
        real_plots,real_map,real_ts = load(os.path.join(CFG,'building-plots.json'),os.path.join(CFG,'town-map.json'))
        c = real_map['circulation']
        rects = corridor_rects(real_ts,circulation=c)
        for band in c['bands']:
            x0,y0,x1,y1 = band['rect']; x,y = (x0+x1)/2,(y0+y1)/2
            obstruction = mk('ring-blocker-'+band['id'],x,y-15,30,30,collisionTop=0,collisionDepth=30)
            plaza_ok &= any('plaza-ring-'+band['id'] in v.text for v in check_e3([obstruction],rects))
        for changed in ('wrong-root','narrow-clearance'):
            invalid = copy.deepcopy(c)
            if changed == 'wrong-root': invalid['landscape']['rect'][0] -= 10
            else: invalid['characterClearance'] = 100
            try: validate_circulation(invalid,real_plots,real_ts); plaza_ok = False
            except ValueError: pass
        outside = mk('main-road-outside-plaza',road_x(5200,real_ts),5200,30,30,collisionTop=0,collisionDepth=30)
        plaza_ok &= any('main-road' in v.text for v in check_e3([outside],rects))
    except (OSError,KeyError,ValueError) as e:
        plaza_ok = False; print('  plaza fixture error: '+str(e))
    if not plaza_ok: bad.append('21_plaza-bands-still-reject-blockers')
    print('  [%s] 21_plaza-bands-reject-all-blockers-and-invalid-clearance' % ('PASS' if plaza_ok else 'FAIL'))
    total_cases = len(cases) + 6

    # 落盘夹具副本供复现（真配置零改动，按 run-gates 的约定落 .batch/）
    tmp = os.path.join(ROOT, '.batch', 'night1', 'selftest', 'town-overlap')
    try:
        os.makedirs(tmp, exist_ok=True)
        with open(os.path.join(tmp, 'fixture-good-plots.json'), 'w',
                  encoding='utf-8', newline='\n') as fh:
            json.dump({'plots': good}, fh, ensure_ascii=False, indent=1)
        with open(os.path.join(tmp, 'fixture-ts.json'), 'w',
                  encoding='utf-8', newline='\n') as fh:
            json.dump({'terrainShape': ts}, fh, ensure_ascii=False, indent=1)
        print('  [INFO] 夹具副本：%s（真配置零改动）'
              % os.path.relpath(tmp, ROOT))
    except (IOError, OSError) as e:
        print('  [INFO] 夹具未落盘（%s），不影响判定' % e)

    if bad:
        print('RESULT: FAIL(selftest %d/%d 条不点火)' % (len(bad), total_cases))
        return 1
    print('SELFTEST: %d/%d 条用例全点火（G-E1 只报数×3：薄阻挡不判红/量对/门洞不计入，'
          'G-E2 互叠/同簇豁免，G-E3 主街/小巷/贴路相切/路侧远处/透明顶带不算/门洞件不算/逐段过阈必红，'
          'G-E4 INFO×4：缺字段/背街/正解无INFO/降级不吞红，合规基线必绿，'
          'mask 对齐×2：road_x≤8/2π 必被抓）'
          % (total_cases, total_cases))
    print('RESULT: PASS(selftest)')
    return 0


def parse_args(argv):
    report = '--report' in argv
    selft = '--selftest' in argv
    plots = os.path.join(CFG, 'building-plots.json')
    map_ = os.path.join(CFG, 'town-map.json')
    pos = []
    i = 0
    while i < len(argv):
        a = argv[i]
        if a == '--plots' and i + 1 < len(argv):
            plots = argv[i + 1]
            i += 2
            continue
        if a == '--map' and i + 1 < len(argv):
            map_ = argv[i + 1]
            i += 2
            continue
        if not a.startswith('-'):
            pos.append(a)
        i += 1
    if pos:
        # 兼容旧版位置参数：旧版第一个位置参数是 town-map.json；新数据源两者都需要，
        # 若是 .json 就按 map 覆盖，plots 保持默认真源（旧 selftest 夹具就是这么用的）。
        map_ = pos[-1]
    return plots, map_, report, selft


if __name__ == '__main__':
    _plots, _map, _report, _selft = parse_args(sys.argv[1:])
    sys.exit(selftest() if _selft else main(_plots, _map, report=_report))
