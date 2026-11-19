# -*- coding: utf-8 -*-
"""建筑地块（building-plots.json）合法性校验 —— 场景线的长期门禁工具。

校验对象：Resources/Config/building-plots.json 的每一块 plot，对照 town-map.json 的
blocked（不可通行光栅）与 landmarks（地标/验收探针点），以及 Resources/Art/Props/ 的贴图质量。

## 两个矩形，别混为一谈（这是本工具最容易被误解的地方）

一块道具在配置里只有一个 (footX, footY, width, height)，但它在世界里扮演两种角色：

  artRect（视觉占地）  = [footX-w/2, footX+w/2] × [footY, footY + h/scale]
      ADR-018：PlaceInWorld 传进去的点是物件**顶边中点**，画面向下（世界 Y 增大方向）延伸 height。
      所以这是一栋房子"画出来的那一整片"，用来判"两栋楼叠在一起画糊了"。
      注意：它**不是**地面上被占住的区域——460 高的楼不会在地上占 460 深。

  base（地面根基阻挡）= [footX-w/2, footX+w/2] × [footY, footY + baseDepth]
      玩家真正撞上的那一条地基。baseDepth（MAP-11 起，三处逐字同式）：
      collisionDepth（覆盖不透明内容带底边的实测深度）> 0 时取之，
      否则 radius×2（radius>0 时），再否则 DEFAULT_BASE_DEPTH。
      通行走廊、死胡同、水面压占这三条判据用的是它。
      任务书 I5.1 说的"阻挡区仅限制在底部核心根基范围"就是这个矩形。

拿 artRect 去做走廊/死胡同判定，会把"楼后面一整条街"当成墙，任何像样的布局都过不了；
拿 base 去做重叠判定，则允许两栋楼画在一起互相糊掉。两者都要。

## 判据清单

  1. foot 点不得落在 blocked 里 —— 地块不能长在河里山里。
  2. 任意两块的 artRect 互不重叠 —— 两块建筑摞在一起是配置错误不是美术风格。
  3. foot 点距任一 landmark 不小于 minLandmarkDistance（默认 60）
     —— 地标是验收探针点，被建筑压住会让后续探针读到脏状态。
  4. artRect 不得越出世界边界 —— 越界的地块在截图里永远看不见，静默浪费配置。
  5. base 不得压住 blocked —— 地基骑在水面上，玩家会看见一棵长在水里的树。
  6. 通行走廊：大型实体（building/tree/stall）两两之间的 base 间隙
     不小于 minCorridor（默认 60 世界单位）—— 挤成一条缝玩家就卡在那儿。
     prop（小型点缀）豁免：八景就是要"铁砧挨着铁匠铺、石狮对峙桥头"，
     对它们套走廊等于禁止一切成组摆放。
  7. 死胡同：把所有 base 压进可行走光栅后，从出生点泛洪，
     不得出现"被新造出来的封闭可走小室"（原本连通、摆放后与主连通域断开）。
  8. 贴图四边体检：Resources/Art/Props/ 下每张 PNG 的四条边必须全透明 ——
     T028 的防再发。抠底没抠干净（边缘残留背景板）在整屏截图里很难看出来，只能显式量。
  9. 引用的 art 路径必须真的存在（缺图是允许的，WorldPropLayer 会走占位几何；
     但"写了路径却找不到文件"是配置笔误，必须当场报，不能等到截图才发现满屏占位块）。
 10. 遮挡逆序：与 NPC/遭遇的画带有交叠的大型实体，其 BaseY（= footY）必须**小于**
     该探针的 BaseY（= y - height，见 ADR-018 与 FrontEndWorld.BuildTownNpcs）。
     判据 1 只看"脚点是否落在根基带里"，看不见这种更常见的事故：楼往人身上压过去、
     人整条被墙吃掉。八景就是"赵大站铁匠炉前、石头站柴扉门口"，所以楼必须在人后面。
     反过来（实体 BaseY 更小）是任务书 I5.1 要的"树冠与屋顶在人物后方自然遮挡"。
 11. 人摊配对（ADR-030）：每个 NPC 必须站在其"摊/道具"配对的 foot 点 ≤120 世界单位内。
     配对对象刻意选**摊或道具**而不是大建筑，这是几何逼出来的，不是偷懒：
     宽 300 的药圃/柴扉，人要站到画带之外需 dx > 半宽+人半宽 ≈ 197，
     要站到它南边（不被挡）需 dy ≥ 人身高 170——两条都超出 120。
     也就是说"人 ≤120 贴着大建筑"与"人不被大建筑挡住"不可能同时成立，
     硬凑的结果就是判据 10 转红。配对表在 NPC_PLOT_PAIR，改任何一边都要同时改这张表。

用法：
    python Tools/PropChecks/check-plot-overlap.py
    python Tools/PropChecks/check-plot-overlap.py --suggest      # 违规时给出就近空位建议
    python Tools/PropChecks/check-plot-overlap.py --plots <path> --town-map <path>
    python Tools/PropChecks/check-plot-overlap.py --no-alpha-check
    python Tools/PropChecks/check-plot-overlap.py --selftest     # 判据自证（好例必须过、坏例必须挂）

退出码：0 = PASS；1 = 存在违规项；2 = 输入缺失/解析失败。
"""
import argparse
import io
import json
import os
import sys
from collections import deque

# Windows GBK 控制台下 print 中文/箭头会 UnicodeEncodeError 把整道门禁崩掉——
# 一个会崩的门禁比一个红的门禁危险得多（架构师 2026-10-05 定规，全仓 Tools/*.py 入口统一）。
for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, 'reconfigure'):
        _stream.reconfigure(encoding='utf-8', errors='replace')

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
# 与主仓其它管线脚本一样，路径经 world_paths 解析，避免写死绝对路径。
sys.path.insert(0, os.path.join(ROOT, 'Tools'))
import world_paths as wp

MIN_LANDMARK_DISTANCE = 60.0      # 判据 3
MIN_CORRIDOR = 60.0               # 判据 6：大型实体之间的通行走廊
DEFAULT_BASE_DEPTH = 24.0         # 没写 radius 时，地面根基默认占多深
DEFAULT_PROBE_HEIGHT = 170.0      # 探针缺 height 时的默认身高（与 BuildTownNpcs 的 170f 一致）
ENCOUNTER_RING = 64.0             # 遭遇金环边长（与 FrontEndWorld 第 638 行的 ringSize 同值）
MAJOR_KINDS = ('building', 'tree', 'stall')   # 受走廊判据约束的 kind
# 判据 11 的配对表（ADR-030 人摊契约）。id 逐字来自 town-map.json 与 building-plots.json；
# 两边任一改名都要同步改这里——改名不通知配对表，判据会静默漏检（查不到 id 时显式报 FAIL）。
NPC_PLOT_PAIR = {
    'huolang': 'plot-market-cart',
    'npc-shenhuai-an': 'plot-fortune-stall',
    'npc-shitou': 'plot-firewood',
    'npc-zhao-da': 'plot-forge-anvil',
    'npc-yao-lao': 'plot-drying-rack',
}
MAX_NPC_PLOT_DISTANCE = 120.0     # 判据 11：人离自己的摊/道具不得超过这个距离
CELL = 32                         # blocked 是 32×32 一格的可行走光栅
SUGGEST_STEP = 60.0               # --suggest 的搜索步长（任务书指定 60）
SUGGEST_RINGS = 12                # 最多向外搜 12 环（=720 单位），超出就报"放不下"
ALPHA_EDGE_TOLERANCE = 0          # 四边允许的半透明像素数（0 = 一个都不许有）


def load_json(path, what):
    if not os.path.exists(path):
        sys.exit('RESULT: FAIL（%s 不存在：%s）' % (what, path))
    try:
        return json.load(io.open(path, encoding='utf-8'))
    except ValueError as ex:
        sys.exit('RESULT: FAIL（%s JSON 解析失败：%s）' % (what, ex))


def num(value, field, plot_id):
    """配置里的数值必须真是数值——字符串坐标会一路静默变成 0。"""
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return None, '地块 %s 的 %s 不是数值（实际 %r）' % (plot_id, field, value)
    return float(value), None


class Plot(object):
    def __init__(self, raw, index):
        self.index = index
        self.id = raw.get('id') or ('#%d' % index)
        self.kind = raw.get('kind') or ''
        self.foot_x = raw.get('footX')
        self.foot_y = raw.get('footY')
        self.width = raw.get('width')
        self.height = raw.get('height')
        self.art = raw.get('art') or ''
        self.radius = raw.get('radius', 0) or 0
        # MAP-11：collisionDepth（覆盖不透明内容带的实测深度）优先；缺省 0 走旧回退。
        self.collision_depth = raw.get('collisionDepth', 0) or 0
        # MAP-12：collisionTop（首个不透明行的实测距离）优先；缺省 0 走图顶旧口径。
        self.collision_top = raw.get('collisionTop', 0) or 0
        # blocks:false = 这块地不挡人（牌坊这类门洞可穿行的物件）。
        # 与 WorldPropLayer 读同一个字段：那边不注册根基，这边就不参与根基判据，两边同形。
        self.blocks = raw.get('blocks', True) is not False
        self.collision_rects = raw.get('collisionRects')

    def art_rect(self, world_scale):
        """视觉占地（画出来的那一整片），见文件头。"""
        half = self.width / 2.0
        return (self.foot_x - half, self.foot_y,
                self.foot_x + half, self.foot_y + self.height / world_scale)

    def base_depth(self):
        """根基深度（MAP-11，与 WorldPropLayer / TerrainPropShot 逐字同式）：
        collisionDepth（覆盖不透明内容带）优先，否则 radius×2，再否则 24 薄皮。"""
        if self.collision_depth > 0:
            return self.collision_depth
        return self.radius * 2.0 if self.radius > 0 else DEFAULT_BASE_DEPTH

    def base_top(self):
        """根基上沿（MAP-12，与 WorldPropLayer / TerrainPropShot 逐字同式）：
        collisionTop（首个不透明行距顶距离）优先，否则 0（图顶）。"""
        return self.collision_top if self.collision_top > 0 else 0.0

    def base_rect(self):
        """地面根基阻挡带：玩家真正会撞上的那一条。
        MAP-12 基准 rect：[footX-w/2, footY+collisionTop] × [footX+w/2, footY+collisionDepth]"""
        half = self.width / 2.0
        return (self.foot_x - half, self.foot_y + self.base_top(),
                self.foot_x + half, self.foot_y + self.base_depth())

    def base_rects(self):
        if self.collision_rects is not None:
            return [(self.foot_x+r['x0'],self.foot_y+r['y0'],self.foot_x+r['x1'],self.foot_y+r['y1']) for r in self.collision_rects]
        return [self.base_rect()]

    @property
    def major(self):
        return self.kind in MAJOR_KINDS


def overlaps(a, b):
    return not (a[2] <= b[0] or b[2] <= a[0] or a[3] <= b[1] or b[3] <= a[1])


def inflate(rect, amount):
    return (rect[0] - amount, rect[1] - amount, rect[2] + amount, rect[3] + amount)


def in_blocked(x, y, blocked):
    for rect in blocked:
        rx, ry, rw, rh = rect
        if rx <= x <= rx + rw and ry <= y <= ry + rh:
            return True
    return False


def rect_hits_blocked(rect, blocked):
    """返回第一个与该矩形相交的 blocked 矩形；没有则 None（真值判断照样能用）。"""
    for rx, ry, rw, rh in blocked:
        if not (rect[2] <= rx or rx + rw <= rect[0] or rect[3] <= ry or ry + rh <= rect[1]):
            return (rx, ry, rw, rh)
    return None


def gap_between(a, b):
    """两个矩形之间的最小间隙（轴对齐）；任一轴已经交叠则该轴间隙为 0。"""
    dx = max(b[0] - a[2], a[0] - b[2], 0.0)
    dy = max(b[1] - a[3], a[1] - b[3], 0.0)
    return (dx * dx + dy * dy) ** 0.5


def build_walkable(blocked, world_w, world_h):
    """把 blocked 矩形栅格化成 32 单位一格的可行走表。"""
    cols = int(world_w // CELL)
    rows = int(world_h // CELL)
    grid = [[False] * cols for _ in range(rows)]
    for rx, ry, rw, rh in blocked:
        y0 = max(0, int(ry) // CELL)
        y1 = min(rows, (int(ry + rh) + CELL - 1) // CELL)
        x0 = max(0, int(rx) // CELL)
        x1 = min(cols, (int(rx + rw) + CELL - 1) // CELL)
        for cy in range(y0, y1):
            row = grid[cy]
            for cx in range(x0, x1):
                row[cx] = True
    return grid


def cells_of(rect):
    x0 = max(0, int(rect[0]) // CELL)
    x1 = int(rect[2] + CELL - 1) // CELL
    y0 = max(0, int(rect[1]) // CELL)
    y1 = int(rect[3] + CELL - 1) // CELL
    return x0, x1, y0, y1


def suggest_spot(plot, landmarks, blocked, world_scale, others):
    """在离该地块最近的 landmark 周围按 60 单位步长找一块放得下的空地。

    只给建议，不写文件——自动改配置等于让工具替架构师做设计决定。
    """
    if not landmarks:
        return None
    near = min(landmarks, key=lambda lm: (lm['x'] - plot.foot_x) ** 2 + (lm['y'] - plot.foot_y) ** 2)
    for ring in range(1, SUGGEST_RINGS + 1):
        for dx in range(-ring, ring + 1):
            for dy in range(-ring, ring + 1):
                if max(abs(dx), abs(dy)) != ring:
                    continue
                cand_x = near['x'] + dx * SUGGEST_STEP
                cand_y = near['y'] + dy * SUGGEST_STEP
                trial = Plot({'id': plot.id, 'kind': plot.kind, 'footX': cand_x,
                              'footY': cand_y, 'width': plot.width,
                              'height': plot.height, 'art': plot.art,
                              'radius': plot.radius}, plot.index)
                rect = trial.art_rect(world_scale)
                if in_blocked(cand_x, cand_y, blocked):
                    continue
                if rect_hits_blocked(rect, blocked):
                    continue
                if rect[0] < 0 or rect[1] < 0:
                    continue
                if any(overlaps(rect, o) for o in others):
                    continue
                if any((cand_x - lm['x']) ** 2 + (cand_y - lm['y']) ** 2
                       < MIN_LANDMARK_DISTANCE ** 2 for lm in landmarks):
                    continue
                return cand_x, cand_y
    return None


def alpha_border_check(assets_root, referenced):
    """判据 8：Props 贴图四边必须全透明。返回 (FAIL 列表, WARN 列表)。

    分级而不是"全目录一刀切 FAIL"，理由要说清楚：
      * 被 building-plots.json 引用的图 → 硬 FAIL。玩家真的会看见它，边缘残留背景板
        就是画面事故。
      * 没被任何地块引用的图 → WARN（照样打印出来，不静默）。
        willow.png 就是这一类：T025 实测它原图四边非底色 386/936/363/747、画面铺满整张，
        任何抠图都救不回来，只能重画；在重画落地之前它只是一张躺在库里的废图。
        如果让它把整个门禁钉死，别的线就没法验收了——那只会逼下一个人偷偷把判据删掉。
    判据本身没有被放松：凡是游戏会画的东西，一律必须干净。
    """
    try:
        from PIL import Image
    except ImportError:
        return [], None
    props_dir = os.path.join(assets_root, 'Resources', 'Art', 'Props')
    if not os.path.isdir(props_dir):
        return ['Props 目录不存在：%s' % wp.rel_repo(props_dir)], []
    hard, warn = [], []
    for name in sorted(os.listdir(props_dir)):
        if not name.endswith('.png'):
            continue
        path = os.path.join(props_dir, name)
        image = Image.open(path).convert('RGBA')
        alpha = image.load()
        width, height = image.size
        opaque = []
        for x in range(width):
            if alpha[x, 0][3] > ALPHA_EDGE_TOLERANCE:
                opaque.append(('上', x, 0))
            if alpha[x, height - 1][3] > ALPHA_EDGE_TOLERANCE:
                opaque.append(('下', x, height - 1))
        for y in range(height):
            if alpha[0, y][3] > ALPHA_EDGE_TOLERANCE:
                opaque.append(('左', 0, y))
            if alpha[width - 1, y][3] > ALPHA_EDGE_TOLERANCE:
                opaque.append(('右', width - 1, y))
        if not opaque:
            continue
        sample = '、'.join('%s边(%d,%d)' % (side, x, y) for side, x, y in opaque[:4])
        message = ('贴图 %s 四边残留不透明像素 %d 个（%s 等）——抠底没抠干净，'
                   '摆进世界就是一圈背景板' % (name, len(opaque), sample))
        stem = 'Art/Props/' + name[:-4]
        (hard if stem in referenced else warn).append(message)
    return hard, warn


def parse_plots(raw_list, problems):
    parsed = []
    for i, raw in enumerate(raw_list):
        if not isinstance(raw, dict):
            problems.append('地块 #%d 不是对象' % i)
            continue
        plot = Plot(raw, i)
        if not raw.get('id'):
            problems.append('地块 #%d 缺 id' % i)
        if not raw.get('kind'):
            problems.append('地块 %s 缺 kind（building / tree / stall / prop）' % plot.id)
        ok = True
        for field in ('footX', 'footY', 'width', 'height'):
            value, err = num(raw.get(field), field, plot.id)
            if err:
                problems.append(err)
                ok = False
            else:
                setattr(plot, {'footX': 'foot_x', 'footY': 'foot_y',
                               'width': 'width', 'height': 'height'}[field], value)
        if not ok:
            continue
        if plot.width <= 0 or plot.height <= 0:
            problems.append('地块 %s 的 width/height 必须为正（实际 %g x %g）'
                            % (plot.id, plot.width, plot.height))
            continue
        if raw.get('radius') is None:
            plot.radius = 0
        else:
            value, err = num(raw.get('radius'), 'radius', plot.id)
            if err:
                problems.append(err)     # 字符串半径以前会直接 TypeError 崩栈，现在报笔误
                continue
            plot.radius = value
        if raw.get('blocks') is not None and not isinstance(raw.get('blocks'), bool):
            problems.append('地块 %s 的 blocks 必须是布尔（实际 %r）' % (plot.id, raw.get('blocks')))
        if plot.collision_depth < 0:
            problems.append('地块 %s 的 collisionDepth 不能为负（实际 %g）' % (plot.id, plot.collision_depth))
            continue
        if raw.get('collisionDepth') is not None:
            value, err = num(raw.get('collisionDepth'), 'collisionDepth', plot.id)
            if err:
                problems.append(err)     # 字符串深度以前会静默变 0 走薄皮兜底，现在报笔误
                continue
            plot.collision_depth = value
        if plot.collision_top < 0:
            problems.append('地块 %s 的 collisionTop 不能为负（实际 %g）' % (plot.id, plot.collision_top))
            continue
        if raw.get('collisionTop') is not None:
            value, err = num(raw.get('collisionTop'), 'collisionTop', plot.id)
            if err:
                problems.append(err)
                continue
            plot.collision_top = value
        if plot.radius < 0:
            problems.append('地块 %s 的 radius 不能为负（实际 %g）' % (plot.id, plot.radius))
            continue
        rects = raw.get('collisionRects')
        if rects is not None:
            import math
            if not isinstance(rects,list) or not rects:
                problems.append('地块 %s 的 collisionRects 必须是非空数组' % plot.id)
                continue
            valid = True
            for r in rects:
                if not isinstance(r,dict) or any(isinstance(r.get(k),bool) or not isinstance(r.get(k),(int,float)) or not math.isfinite(r[k]) for k in ('x0','y0','x1','y1')):
                    valid = False;break
                if r['x0']>=r['x1'] or r['y0']>=r['y1']:valid=False;break
            if not valid:
                problems.append('地块 %s 的 collisionRects 存在无效矩形' % plot.id)
                continue
        parsed.append(plot)
    return parsed


def verify(plots_raw, town, assets_root, do_alpha=True, min_corridor=MIN_CORRIDOR,
           min_landmark_distance=MIN_LANDMARK_DISTANCE, pairing=None):
    """跑全部判据，返回 (问题列表, 摘要 dict)。

    pairing 只有自证会传：selftest 用的是合成地块（id 叫 good/good2），
    拿真实的 NPC_PLOT_PAIR 去对表必然报"id 找不到"，那会把 14 条老自证全淹掉。
    生产路径不传 → 用模块级那张真表，一条都不放松。
    """
    pairing = NPC_PLOT_PAIR if pairing is None else pairing
    problems = []
    world_scale = town.get('worldScale', 1.0) or 1.0
    world_w = float(town.get('worldWidth', 0))
    world_h = float(town.get('worldHeight', 0))

    blocked = []
    for item in town.get('blocked', []):
        if isinstance(item, dict):
            blocked.append((float(item.get('x', 0)), float(item.get('y', 0)),
                            float(item.get('width', 0)), float(item.get('height', 0))))
        elif isinstance(item, (list, tuple)) and len(item) >= 4:
            blocked.append((float(item[0]), float(item[1]), float(item[2]), float(item[3])))

    landmarks = []
    for lm in town.get('landmarks', []):
        if isinstance(lm, dict) and 'x' in lm and 'y' in lm:
            landmarks.append({'id': lm.get('id', '?'), 'x': float(lm['x']), 'y': float(lm['y'])})

    # 探针点（NPC / 遭遇）：地块压住它们等于把验收探针埋进墙里。
    # 画带几何与 FrontEndWorld 同口径，两种探针的画法完全不同，别一套公式套到底：
    #   NPC   —— BuildTownNpcs：传顶点 (x, y-h)，占地 [y-h, y]，BaseY = y-h，宽按立绘比例。
    #   遭遇   —— 第 639 行：64×64 金环，顶点 (x, y-32)，占地 [y-32, y+32]，BaseY = y-32。
    #             拿 170 的身高去量一个 64 的环，会把"牌坊压住金环"误报成"楼吃掉一个人"。
    probes = []
    for entry in town.get('npcs', []):
        px, py = float(entry['x']), float(entry['y'])
        h = float(entry.get('height') or DEFAULT_PROBE_HEIGHT)
        art = entry.get('art') or ''
        w = h * .55
        if art:
            path = os.path.join(assets_root, 'Resources', art + '.png')
            if os.path.exists(path):
                try:
                    from PIL import Image
                    with Image.open(path) as pic:
                        if pic.height:
                            w = h * pic.width / float(pic.height)
                except ImportError:
                    pass
        probes.append({'name': 'npc:' + str(entry.get('id', '?')), 'x': px, 'y': py,
                       'base_y': py - h,
                       'rect': (px - w / 2.0, py - h, px + w / 2.0, py)})
    for entry in town.get('encounters', []):
        px, py = float(entry['x']), float(entry['y'])
        half = ENCOUNTER_RING / 2.0
        probes.append({'name': 'enc:' + str(entry.get('id', '?')), 'x': px, 'y': py,
                       'base_y': py - half,
                       'rect': (px - half, py - half, px + half, py + half)})

    plots = parse_plots(plots_raw, problems)
    art_rects = [(p, p.art_rect(world_scale)) for p in plots]
    # 不挡人的地块（blocks:false）整条退出根基判据：它在地上不占任何东西，
    # 拿它的矩形去算走廊/死胡同会把"能穿过的门楼"当成一堵墙。
    base_rects = [(p, r) for p in plots if p.blocks for r in p.base_rects()]

    # 判据 1：foot 点不得落在不可通行区
    for plot, _ in art_rects:
        if in_blocked(plot.foot_x, plot.foot_y, blocked):
            problems.append('地块 %s 的 foot 点 (%g,%g) 落在 blocked 不可通行区内'
                            % (plot.id, plot.foot_x, plot.foot_y))

    # 判据 4：视觉占地不得越出世界
    if world_w > 0 and world_h > 0:
        for plot, rect in art_rects:
            if rect[0] < 0 or rect[1] < 0 or rect[2] > world_w or rect[3] > world_h:
                problems.append('地块 %s 的视觉占地越出世界边界（世界 %gx%g，矩形 %s）'
                                % (plot.id, world_w, world_h,
                                   '(%g,%g)-(%g,%g)' % rect))

    # 判据 2：视觉占地两两不重叠
    for i in range(len(art_rects)):
        for j in range(i + 1, len(art_rects)):
            if overlaps(art_rects[i][1], art_rects[j][1]):
                problems.append('地块 %s 与 %s 的视觉占地重叠：%s vs %s'
                                % (art_rects[i][0].id, art_rects[j][0].id,
                                   '(%g,%g)-(%g,%g)' % art_rects[i][1],
                                   '(%g,%g)-(%g,%g)' % art_rects[j][1]))

    # 判据 3：foot 点距地标
    for plot, _ in art_rects:
        for lm in landmarks:
            d2 = (plot.foot_x - lm['x']) ** 2 + (plot.foot_y - lm['y']) ** 2
            if d2 < min_landmark_distance ** 2:
                problems.append('地块 %s 的 foot 点距地标 %s 仅 %.1f 单位（要求 >= %g）'
                                % (plot.id, lm['id'], d2 ** 0.5, min_landmark_distance))

    # 判据 5：地面根基不得压住不可通行区（水面/山体）
    for plot, base in base_rects:
        hit = rect_hits_blocked(base, blocked)
        if hit:
            problems.append('地块 %s 的地面根基 (%g,%g)-(%g,%g) 压住不可通行区 (%g,%g,%gx%g)'
                            '——地基骑在水里/山里' % (plot.id, base[0], base[1], base[2], base[3],
                                                    hit[0], hit[1], hit[2], hit[3]))

    # 判据 6：大型实体之间的通行走廊
    majors = [(p, b) for p, b in base_rects if p.major]
    for i in range(len(majors)):
        for j in range(i + 1, len(majors)):
            a, b = majors[i], majors[j]
            if a[0].id == b[0].id: continue  # 同一座门的门墩间隙由资产/通行门禁实测。
            if overlaps(a[1], b[1]):
                continue      # 重叠已由判据 2 报过，这里只补"没叠但太挤"
            gap = gap_between(a[1], b[1])
            if gap < min_corridor:
                problems.append('大型地块 %s 与 %s 之间的通行走廊仅 %.1f 单位（要求 >= %g）'
                                '——玩家会卡在那条缝里' % (a[0].id, b[0].id, gap, min_corridor))

    # 判据 7：死胡同（摆放后把可走区分割出封闭小室）
    if world_w > 0 and world_h > 0:
        problems.extend(dead_end_check(blocked, base_rects, world_w, world_h, town))

    # 判据 9：写了 art 路径就必须真有这个文件
    for plot, _ in art_rects:
        if not plot.art:
            continue
        path = os.path.join(assets_root, 'Resources', plot.art + '.png')
        if not os.path.exists(path):
            problems.append('地块 %s 引用 %s，但文件不存在（缺图应留空走占位，'
                            '写了路径却找不到是配置笔误）' % (plot.id, plot.art))

    # 判据 1 的延伸：探针点不得被大型地块的地基压住
    for probe in probes:
        for plot, base in base_rects:
            if plot.major and base[0] <= probe['x'] <= base[2] and base[1] <= probe['y'] <= base[3]:
                problems.append('地块 %s 的地面根基压住探针点 %s (%g,%g)'
                                % (plot.id, probe['name'], probe['x'], probe['y']))

    # 判据 10：遮挡逆序——画带压到人身上却排在人后面画，人就被墙吃了
    for probe in probes:
        for plot, rect in art_rects:
            if not plot.major:
                continue
            if not overlaps(rect, probe['rect']):
                continue
            if plot.foot_y > probe['base_y']:
                problems.append('地块 %s 遮挡逆序：画带 (%g,%g)-(%g,%g) 压住探针 %s，'
                                '却因 BaseY=%g > %g 被画在人前面——人会被建筑/树整条遮挡。'
                                '要么把 %s 往北挪到 footY <= %g，要么挪开人。'
                                % (plot.id, rect[0], rect[1], rect[2], rect[3], probe['name'],
                                   plot.foot_y, probe['base_y'], plot.id, probe['base_y']))

    # 判据 11：人摊配对（ADR-030）。配对表里的 id 查不到就是配置改名没同步，必须报不能静默跳过。
    pair_rows = []
    by_id = {p.id: p for p in plots}
    npc_pos = {str(n.get('id')): (float(n['x']), float(n['y'])) for n in town.get('npcs', [])}
    for npc_id, plot_id in pairing.items():
        if npc_id not in npc_pos:
            problems.append('人摊配对表里的 NPC %s 在 town-map.npcs 里找不到（改名没同步配对表）' % npc_id)
            continue
        plot = by_id.get(plot_id)
        if plot is None:
            problems.append('人摊配对表里的地块 %s（%s 的摊/道具）在 building-plots 里找不到'
                            '（改名没同步配对表）' % (plot_id, npc_id))
            continue
        nx, ny = npc_pos[npc_id]
        d = ((nx - plot.foot_x) ** 2 + (ny - plot.foot_y) ** 2) ** 0.5
        pair_rows.append((npc_id, plot_id, d))
        if d > MAX_NPC_PLOT_DISTANCE:
            problems.append('NPC %s 离它的配对地块 %s 有 %.0f 世界单位（要求 <= %g）'
                            '——人不在自己摊前，景名与画面对不上' % (npc_id, plot_id, d, MAX_NPC_PLOT_DISTANCE))

    # 判据 8：贴图四边体检（被引用的硬失败，躺在库里的警告）
    referenced = {p.art for p in plots if p.art}
    alpha_checked = 0
    warnings = []
    if do_alpha:
        found = alpha_border_check(assets_root, referenced)
        if found == ([], None):
            print('  (跳过四边体检：本机没有 PIL)')
        else:
            hard, warn = found
            problems.extend(hard)
            warnings.extend(warn or [])
            props_dir = os.path.join(assets_root, 'Resources', 'Art', 'Props')
            alpha_checked = len([n for n in os.listdir(props_dir) if n.endswith('.png')]) \
                if os.path.isdir(props_dir) else 0

    summary = {'plots': len(plots), 'majors': len(majors), 'landmarks': len(landmarks),
               'blocked': len(blocked), 'alpha_checked': alpha_checked,
               'warnings': warnings, 'pairs': pair_rows,
               # D-18 / T058：collisionDepth 与 radius 都没填时根基退化成 24px 薄皮，
               # "全绿"可能只是"没人填"。这个数必须出现在结果里，否则静态层的空洞永远看不见。
               'fallback': len([p for p in plots if p.collision_depth <= 0 and p.radius <= 0]),
               'kinds': sorted({p.kind for p in plots})}
    return problems, summary, art_rects


def dead_end_check(blocked, base_rects, world_w, world_h, town):
    """把所有地面根基压进可行走光栅，比较"摆放前/后"的连通域。

    只报**由本次摆放新造成**的孤立可走区——本来就存在的封闭水面/山体不算地块的账。
    """
    rows = int(world_h // CELL)
    cols = int(world_w // CELL)
    grid = build_walkable(blocked, world_w, world_h)
    covered = set()
    for plot, base in base_rects:
        if not plot.major:
            continue
        x0, x1, y0, y1 = cells_of(base)
        for cy in range(max(0, y0), min(rows, y1)):
            for cx in range(max(0, x0), min(cols, x1)):
                covered.add((cy, cx))
    before = walkable_components(grid, set())
    after = walkable_components(grid, covered)
    spawn_cell = (int((town.get('spawnY') or 0)) // CELL, int((town.get('spawnX') or 0)) // CELL)
    main = after.get(spawn_cell)
    if main is None and after:
        main = max(after.values(), key=lambda s: s[0])[0]
    problems = []
    for key, (size, _) in after.items():
        if main is not None and size == main:
            continue
        # 这块可走区在摆放前是不是本来就该跟主域连着？
        if any(before.get(k, 0) and before[k] > size for k in [key]):
            continue
        if before.get(key, 0) >= size:
            continue      # 摆放前就这么大：本来就封闭，不怪地块
        if size < 4:
            continue      # 小于 4 格（128×128 单位）的碎缝不算房间，报出来只会淹掉日志
        problems.append('地块摆放后在此处围出孤立可走区（格数 %d，起点格 %s）——疑似死胡同'
                        % (size, key))
    return problems


def walkable_components(grid, covered):
    """返回 {最小格坐标: (格数, 所有格)} —— 覆盖后的可走连通域。"""
    rows, cols = len(grid), len(grid[0])
    seen = set()
    comps = {}
    for r0 in range(rows):
        for c0 in range(cols):
            start = (r0, c0)
            if start in seen or grid[r0][c0] or start in covered:
                continue
            queue = deque([start])
            seen.add(start)
            cells = []
            while queue:
                r, c = queue.popleft()
                cells.append((r, c))
                for dr, dc in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    nr, nc = r + dr, c + dc
                    nxt = (nr, nc)
                    if 0 <= nr < rows and 0 <= nc < cols and nxt not in seen \
                            and not grid[nr][nc] and nxt not in covered:
                        seen.add(nxt)
                        queue.append(nxt)
            comps[min(cells)] = (len(cells), frozenset(cells))
    return comps


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--plots', default=None, help='building-plots.json 路径（默认 Config 目录下）')
    ap.add_argument('--town-map', default=None, help='town-map.json 路径（默认 Config 目录下）')
    ap.add_argument('--suggest', action='store_true', help='违规时给出就近空位建议')
    ap.add_argument('--no-alpha-check', action='store_true', help='跳过贴图四边体检（调试用）')
    ap.add_argument('--min-landmark-distance', type=float, default=MIN_LANDMARK_DISTANCE)
    ap.add_argument('--min-corridor', type=float, default=MIN_CORRIDOR)
    ap.add_argument('--selftest', action='store_true',
                    help='判据自证：好例必须全过、每条坏例必须挂，防止判据被改成永真')
    args = ap.parse_args()

    if args.selftest:
        return selftest()

    config = wp.config_root()
    plots_path = args.plots or os.path.join(config, 'building-plots.json')
    town_path = args.town_map or os.path.join(config, 'town-map.json')

    plots_raw = load_json(plots_path, 'building-plots.json')
    town = load_json(town_path, 'town-map.json')

    raw_list = plots_raw.get('plots')
    if not isinstance(raw_list, list) or not raw_list:
        sys.exit('RESULT: FAIL（building-plots.json 的 plots 必须是非空数组）')

    # config 目录是 Assets/Resources/Config —— 往上两层才是 Assets。
    assets_root = os.path.dirname(os.path.dirname(config))
    problems, summary, art_rects = verify(
        raw_list, town, assets_root, do_alpha=not args.no_alpha_check,
        min_corridor=args.min_corridor, min_landmark_distance=args.min_landmark_distance)

    print('building-plots: %s' % wp.rel_repo(plots_path))
    print('town-map:       %s' % wp.rel_repo(town_path))
    print('plots=%d（大型实体 %d）  blocked=%d  landmarks=%d  贴图四边体检=%d 张'
          % (summary['plots'], summary['majors'], summary['blocked'],
             summary['landmarks'], summary['alpha_checked']))
    print('kind 分布: %s' % ', '.join(summary['kinds']))
    # 判据 11 的对表行：ADR-030 要求"人站自己摊前"必须可核对，不能只有一句"已收紧"。
    for npc_id, plot_id, d in summary.get('pairs', []):
        print('  人摊 %-16s -> %-20s %.0f 单位%s' % (
            npc_id, plot_id, d, '' if d <= MAX_NPC_PLOT_DISTANCE else '  【超标】'))
    for message in summary.get('warnings', []):
        print('WARN: ' + message)

    if problems:
        print('')
        for p in problems:
            print('FAIL: ' + p)
        if args.suggest:
            print('')
            print('--suggest 建议坐标（只给建议，未写入任何文件）：')
            world_scale = town.get('worldScale', 1.0) or 1.0
            landmarks = [{'id': lm.get('id', '?'), 'x': float(lm['x']), 'y': float(lm['y'])}
                         for lm in town.get('landmarks', []) if isinstance(lm, dict)]
            blocked = [(float(b.get('x', 0)), float(b.get('y', 0)),
                        float(b.get('width', 0)), float(b.get('height', 0)))
                       for b in town.get('blocked', []) if isinstance(b, dict)]
            for plot, rect in art_rects:
                others = [r for q, r in art_rects if q is not plot]
                spot = suggest_spot(plot, landmarks, blocked, world_scale, others)
                if spot is None:
                    print('  %-28s 放不下（%d 环内无空位）' % (plot.id, SUGGEST_RINGS))
                else:
                    print('  %-28s footX=%g footY=%g' % (plot.id, spot[0], spot[1]))
        print('')
        print('RESULT: FAIL（%d 项违规）' % len(problems))
        return 1

    for plot, rect in art_rects:
        print('  OK %-28s %-9s foot=(%g,%g) 视觉=(%g,%g)-(%g,%g) 根基=%s'
              % (plot.id, plot.kind, plot.foot_x, plot.foot_y,
                 rect[0], rect[1], rect[2], rect[3],
                 str(plot.base_rects())))
    # D-18：兜底命中率单独一行，且 n>0 时必须挂进 RESULT 行——不许"全绿其实没人填"。
    fallback = summary.get('fallback', 0)
    print('BASE-FALLBACK: %d/%d plots used DEFAULT_BASE_DEPTH' % (fallback, summary['plots']))
    print('')
    if fallback:
        print('RESULT: PASS (All %d plots verified without overlap) '
              '【但 %d 块 radius 未填，根基退化为 %g 薄皮——见 T058】'
              % (summary['plots'], fallback, DEFAULT_BASE_DEPTH))
    else:
        print('RESULT: PASS (All %d plots verified without overlap)' % summary['plots'])
    return 0


# ---------------------------------------------------------------- 自证

GOOD_PLOT = {'id': 'good', 'kind': 'tree', 'footX': 3000, 'footY': 4800,
             'width': 120, 'height': 200, 'art': '', 'radius': 0}


def selftest():
    """判据自证：好例必须过，每条坏例必须挂到它该挂的那条判据上。

    为什么要有这个：本工具是"地块摆得对不对"的唯一防线。判据一旦被改成永真（或被人
    为了让新布局通过而调松），布局会一路烂到截图里才发现。自证用内存里的假数据跑，
    不碰任何真实配置。
    """
    town = {'worldScale': 1.0, 'worldWidth': 8192, 'worldHeight': 8192,
            'spawnX': 3100, 'spawnY': 5000,
            'landmarks': [{'id': 'probe', 'x': 1000, 'y': 1000}],
            # 一条假河：横向 0..800、纵向 4700..4900。这样「foot 在岸上但地基骑进河里」
            # 与「foot 直接落在河里」是两种不同违规，必须分别命中，不能互相顶替。
            'blocked': [{'x': 0, 'y': 4700, 'width': 800, 'height': 200}],
            'npcs': [{'id': 'someone', 'x': 7000, 'y': 7000}],
            'encounters': [{'id': 'boar', 'x': 6000, 'y': 6000}]}
    assets = os.path.join(ROOT, 'GameClient', 'TwelveJade', 'Assets')
    cases = []

    def run(plots, pairing=None):
        problems, _, _ = verify(plots, town, assets, do_alpha=False,
                                pairing={} if pairing is None else pairing)
        return problems

    # 好例：两块离得足够远、都在可走区、离地标够远
    good_a = dict(GOOD_PLOT)
    good_b = dict(GOOD_PLOT, id='good2', footX=3600, footY=4800)
    cases.append(('好例必须全过', run([good_a, good_b]), lambda p: not p))

    # 坏例逐条：每条只踩一个判据，确认它确实被抓住
    cases.append(('foot 落进 blocked', run([dict(GOOD_PLOT, footX=400, footY=4800)]),
                  lambda p: any('blocked' in x for x in p)))
    cases.append(('视觉占地重叠', run([good_a, dict(GOOD_PLOT, id='b', footX=3010, footY=4800)]),
                  lambda p: any('视觉占地重叠' in x for x in p)))
    cases.append(('贴住地标', run([dict(GOOD_PLOT, footX=1010, footY=1010)]),
                  lambda p: any('地标' in x for x in p)))
    cases.append(('越出世界边界', run([dict(GOOD_PLOT, footX=8150, footY=4800)]),
                  lambda p: any('世界边界' in x for x in p)))
    # foot 落在 x=810（岸上，不在河里），但地基宽 120 → 横跨 750..870 压进河里
    cases.append(('根基压住水面', run([dict(GOOD_PLOT, footX=810, footY=4800)]),
                  lambda p: any('压住不可通行区' in x for x in p)))
    cases.append(('走廊过窄', run([good_a, dict(GOOD_PLOT, id='b', footX=3120, footY=4800)]),
                  lambda p: any('通行走廊' in x for x in p)))
    cases.append(('art 路径指向不存在的文件',
                  run([dict(GOOD_PLOT, art='Art/Props/no-such-prop-xyz')]),
                  lambda p: any('文件不存在' in x for x in p)))
    cases.append(('width 为负', run([dict(GOOD_PLOT, width=-10)]),
                  lambda p: any('必须为正' in x for x in p)))
    cases.append(('footX 写成字符串', run([dict(GOOD_PLOT, footX='3000')]),
                  lambda p: any('不是数值' in x for x in p)))
    cases.append(('radius 为负', run([dict(GOOD_PLOT, radius=-5)]),
                  lambda p: any('radius' in x for x in p)))
    # radius 写成字符串以前会 TypeError 崩栈：现在必须报成配置笔误
    cases.append(('radius 写成字符串', run([dict(GOOD_PLOT, radius='24')]),
                  lambda p: any('radius' in x and '不是数值' in x for x in p)))
    cases.append(('压住 NPC 探针点', run([dict(GOOD_PLOT, footX=7000, footY=7000)]),
                  lambda p: any('探针点' in x for x in p)))
    # 判据 10：楼画带压到人身上、却因 BaseY 更大而画在人前面 = 人被墙整条吃掉。
    # 探针 BaseY = 7000 - 170 = 6830；footY=6900 > 6830 即逆序。
    cases.append(('遮挡逆序（楼画在人前面）',
                  run([dict(GOOD_PLOT, footX=7000, footY=6900, width=200, height=200)]),
                  lambda p: any('遮挡逆序' in x for x in p)))
    # 同样压着画带，但楼挪到人后面（footY=6800 < 6830）就是任务书 I5.1 要的正确遮挡
    cases.append(('好例：人在楼前为正确遮挡',
                  run([dict(GOOD_PLOT, footX=7000, footY=6800, width=200, height=200)]),
                  lambda p: not any('遮挡逆序' in x for x in p)))
    # 判据 11：人摊配对。用 prop（非 major）当"摊"，这样它只考配对距离，
    # 不会被判据 10 的遮挡结论顶掉——两条判据各自单独验，才能分清是谁失效。
    near = dict(GOOD_PLOT, id='stall', kind='prop', footX=7000, footY=6950)
    far = dict(near, footY=6800)
    cases.append(('好例：人站自己摊前',
                  run([near], pairing={'someone': 'stall'}),
                  lambda p: not any('人摊' in x or '配对地块' in x for x in p)))
    cases.append(('人离摊超过 120',
                  run([far], pairing={'someone': 'stall'}),
                  lambda p: any('人摊配对表' in x or ('someone' in x and '配对地块' in x) for x in p)))
    cases.append(('配对表指向不存在的摊',
                  run([near], pairing={'someone': 'no-such-stall'}),
                  lambda p: any('找不到' in x and 'no-such-stall' in x for x in p)))
    # 遭遇金环是 64×64、中心在 (x,y)，BaseY = y-32 = 5968；把它当 170 高的人量会误报。
    # 这条同时钉住"环的几何口径"和"环也不能被楼吃掉"两件事。
    cases.append(('遮挡逆序（楼压住遭遇金环）',
                  run([dict(GOOD_PLOT, footX=6000, footY=5990, width=120, height=200)]),
                  lambda p: any('遮挡逆序' in x and 'enc:boar' in x for x in p)))
    cases.append(('好例：金环在楼前为正确遮挡',
                  run([dict(GOOD_PLOT, footX=6000, footY=5960, width=120, height=200)]),
                  lambda p: not any('遮挡逆序' in x for x in p)))
    # prop（小型点缀）必须豁免走廊，否则"铁砧挨着铁匠铺"这种成组摆放永远过不了
    cases.append(('prop 豁免走廊判据', run([good_a, dict(GOOD_PLOT, id='anvil', kind='prop',
                                                     footX=3060, footY=4800, width=60, height=60)]),
                  lambda p: not any('通行走廊' in x for x in p)))

    for invalid in ([],"bad",[dict(x0=1,y0=0,x1=0,y1=10)],[dict(x0=0,y0=0,x1=True,y1=10)]):
        cases.append(('分段阻挡坏配置必须拒绝',run([dict(GOOD_PLOT,collisionRects=invalid)]),lambda p:any('collisionRects' in x for x in p)))
    split=dict(GOOD_PLOT,collisionRects=[dict(x0=-50,y0=10,x1=-30,y1=90),dict(x0=30,y0=10,x1=50,y1=90)])
    cases.append(('好例：同座门的分段阻挡保持洞口',run([split]),lambda p:not p))
    # Single legacy bbox misses this river intrusion; only the explicit runtime segment catches it.
    cases.append(('分段阻挡压河仍需判红',run([dict(GOOD_PLOT,footX=850,collisionRects=[dict(x0=-100,y0=0,x1=-60,y1=80)])]),lambda p:any('压住不可通行区' in x for x in p)))

    print('check-plot-overlap 判据自证')
    failures = 0
    for name, problems, judge in cases:
        if name.startswith('好例') or '豁免' in name:
            ok = judge(problems)
        else:
            ok = judge(problems) and problems
        print('  %-24s %s' % (name, 'OK' if ok else 'FAIL ' + str(problems[:2])))
        if not ok:
            failures += 1
    print('')
    if failures:
        print('RESULT: FAIL（%d 条判据自证未过——判据可能失效了）' % failures)
        return 1
    print('RESULT: PASS（%d 条判据自证全过）' % len(cases))
    return 0


if __name__ == '__main__':
    sys.exit(main())
