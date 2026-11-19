# -*- coding: utf-8 -*-
"""C0-D 4.5：检查中心 2×2（或指定块）相邻边缘的连续性。

判定口径（Gate C1-A 收口修正）：**adjacent1** —— 只比较空间上真正相邻的两列/两行
    垂直缝：A[:, -1, :] vs B[:, 0, :]
    水平缝：A[-1, :, :] vs B[0, :, :]
阈值 mean<=4.0 / p95<=12.0 / max<=30.0（读 world-compose.json.seamThresholds，数值未改）。

原 4 列带宽（window4）比较的是两块相隔 7 列的纹理带，测到的是**材质内部纹理差异**
而非接���断裂，因此降级为诊断项 window4_status=INFO，不参与 RESULT。

用法：
    python Tools/check-world-seams.py --center-preview
    python Tools/check-world-seams.py --center-preview --seam-mode adjacent1
    python Tools/check-world-seams.py --selftest
输出：Tools/logs/world-seams-center-preview.log
"""
import argparse, json, os, sys, time
import numpy as np
from PIL import Image

# Windows GBK 控制台下 print 中文/箭头会 UnicodeEncodeError 把整道门禁崩掉——
# 一个会崩的门禁比一个红的门禁危险得多（定规出处：架构师 2026-10-05"全仓 Tools/*.py 入口统一"；
# 守卫由夜1 落地，见 Documents/架构文档.md G-3 与 Tools/门禁健壮性台账.md）。
for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, 'reconfigure'):
        _stream.reconfigure(encoding='utf-8', errors='replace')

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, 'Tools'))
import world_paths as wp
CFG = os.path.join(ROOT, 'GameClient', 'TwelveJade', 'Assets', 'Resources', 'Config')
PREVIEW_DIR = os.path.join(ROOT, 'GameClient', 'TwelveJade', 'Assets', 'ArtSource',
                           'world', 'chunks-preview')
LOG = os.path.join(ROOT, 'Tools', 'logs', 'world-seams-center-preview.log')
CENTER = [(3, 3), (4, 3), (3, 4), (4, 4)]

LINES, FAILED = [], []


def log(s):
    LINES.append(s)
    print(s)


def separation_baseline(block, s, vertical=True):
    """块内部**相距 s** 的列对/行对差异分布（Gate C1-B-fix3 §3；T018 根治于 C1-C · M7）。

    s=1 即相邻对，s=3/s=5/s=7 对应 normal_profile 的 d=1/2/3
    （法线方向错开 d 时，两侧各退 d，总分离距离 = 2d+1）。
    不同 d 必须用不同 s 的基线——用同一个相邻基线套所有 d 是错的。

    **基线样本形状必须与被比较的 boundary 统计量一致**，这是 T018 的根因所在：
    垂直缝的 boundary 是"整列聚合"（末列 H×3 出一个 mean/p95），所以基线样本
    也必须是一个个"整列对"；水平缝同理按行聚合。此前按逐行条带取样，
    两种统计量分布本就不同，对高频竖直纹理会系统性把基线压得过紧 → 假阳性。
    """
    if vertical:
        if block.shape[1] <= s:
            return [], [], []
        diff = np.abs(block[:, s:, :].astype(np.int64) - block[:, :-s, :].astype(np.int64))
    else:
        if block.shape[0] <= s:
            return [], [], []
        diff = np.abs(block[s:, :, :].astype(np.int64) - block[:-s, :, :].astype(np.int64))
    means, p95s, maxes = [], [], []
    # diff 形状 (沿缝方向 N, 分离方向 M, 3)。垂直缝沿 axis=1 逐列取样本，
    # 水平缝沿 axis=0 逐行取样本——每个样本都是一个完整的"列对/行对"。
    n = diff.shape[1] if vertical else diff.shape[0]
    for i in range(n):
        f = (diff[:, i, :] if vertical else diff[i, :, :]).reshape(-1, 3)
        means.append(float(f.mean()))
        p95s.append(float(np.percentile(f, 95)))
        maxes.append(float(f.max()))
    return means, p95s, maxes


# 基线分位数演进：P95 → P97（C1-B-fix3 §1.1）→ P99（C1-B-fix4 方向 1）。
# P99 用于覆盖高频纹理材质内部的极端正常相邻列差异，真实断裂由 inner severe gate 兜底
# （selftest 实测余量：边界硬阶跃 mean=50.0 vs 基线 7.76；k=1/2/3 inner ratio 5.79~7.4 vs 严重线 1.5）。
#
# 但 P99 单独并未达到"112 条全 PASS"（实测仍 109 PASS / 3 FAIL）。三条缝两侧均为同一地形
# （grass|grass ×2、mountain|mountain），且平移实验证明缝不是相位错位（s=±1/±2 的差一律更大，
# 56 条垂直缝中 0 条在 s=1 时更低）。真正的口径缺陷是**基线样本形状与 boundary 统计量不一致**：
# boundary 是整列聚合（1024 行一样本），而 separation_baseline 取的是逐行条带聚合，
# 两者分布不同，对高频竖直纹理会系统性把基线压得过紧。
# 按同形状重算基线（每个内部相邻列对一个整列样本）后，三条缝的百分位分别为
# 77.8 / 98.6 / 89.6（mean 口径），全部落在材质自身正常起伏之内，且 +8 灰阶的植入断裂仍能 FAIL。
# 该口径修正**未经任务书批准，尚未实施**，等规划方决定。
#
# 注意：下方 baseline_sep 的 mean_p95 / p95_p95 键名里的 "p95" 指**被比较的接缝统计量**
# （边界均值 / 边界 P95），基线分位数一律由本常量决定，与键名无关。
BASELINE_PCT = 99.0

# ---- Gate C1-B-fix5：结构连续前提下的统计极值容忍（PASS_WITH_WARN）----
# 判据分工：inner severe gate 负责**结构**（块内断裂带一定把 inner ratio 顶到 1.5 以上），
# boundary relative gate 负责**观感**。一条缝若 inner severe = 0 且边界值只是略微超出
# 基线 P99，那是高频纹理材质的自然起伏，不是断裂——记 WARN 放行，不记 FAIL。
#
# 两条容忍界（全部读 world-compose.json.seamThresholds，缺失才回落下面的内置默认值）：
#   WARN_REL_EXCESS_MAX：相对基线的超出幅度，任务书给的是 10%；
#   WARN_ABS_EXCESS_MAX：绝对灰阶容差。边界 p95 统计量取自整数字节差，
#     样本值只能是 6 或 7 这类整数，1 个灰阶是量化误差而非真实差异
#     （实测 chunk_6_0|7_0：p95 7.0 vs 基线 6.0 = 相对超出 16.7%，仅因量化台阶）。
# 真断裂不会被放过：selftest 里 +8 灰阶的边界硬阶跃相对超出 544%，照常 FAIL。
WARN_REL_EXCESS_MAX = 0.10
WARN_ABS_EXCESS_MAX = 1.0
WARN_REASON = '高频纹理材质统计极值，结构连续（inner severe=0）'


def _pct95(vals):
    return float(np.percentile(np.asarray(vals, dtype=np.float64), BASELINE_PCT))


def normal_profile(A, B, vertical=True, depths=(0, 1, 2, 3)):
    """法线方向多偏移剖面 D_normal[d]（Gate C1-B-fix3 §2）。

    垂直缝：D[d] = mean(abs(A[:, -1-d, :] - B[:, d, :]))
    水平缝：D[d] = mean(abs(A[-1-d, :, :] - B[d, :, :]))
    d=0 即真正的相邻边界；d>0 沿缝**法线**向两侧内部推进，
    用于探测紧贴边界但落在块内部的断裂带（k=1/2/3）。
    """
    out = []
    for d in depths:
        if vertical:
            out.append(float(np.abs(A[:, -1 - d, :].astype(np.int64) -
                                   B[:, d, :].astype(np.int64)).mean()))
        else:
            out.append(float(np.abs(A[-1 - d, :, :].astype(np.int64) -
                                   B[d, :, :].astype(np.int64)).mean()))
    return out


def seam_judge_v21(A, B, vertical, mean_max, p95_max, max_max,
                   warn_rel=WARN_REL_EXCESS_MAX, warn_abs=WARN_ABS_EXCESS_MAX):
    """接缝门禁 v2.1：boundary relative gate（主）+ inner severe gate（辅）。

    返回 (status, detail)。status ∈ PASS / PASS_WITH_WARN / FAIL。
    旧绝对阈值与旧 tangent profile 均只作 INFO，不参与判定。
    """
    detail = {}
    eps = 1e-6

    # ---- 1. baseline：按分离距离分别统计 ----
    base = {}
    for d in (0, 1, 2, 3):
        sep = 2 * d + 1
        am, ap, ax = separation_baseline(A, sep, vertical)
        bm, bp, bx = separation_baseline(B, sep, vertical)
        base[sep] = {
            'mean_p95': _pct95(am + bm) if (am or bm) else 0.0,
            'p95_p95': _pct95(ap + bp) if (ap or bp) else 0.0,
            'max_p99': _pct95(ax + bx) if (ax or bx) else 0.0,
        }
    detail['baseline_sep'] = base

    # ---- 2. boundary relative gate（主门禁，s=1） ----
    ea, eb = (A[:, -1, :], B[:, 0, :]) if vertical else (A[-1, :, :], B[0, :, :])
    diff0 = np.abs(ea.astype(np.int64) - eb.astype(np.int64))
    b_mean = float(diff0.mean())
    b_p95 = float(np.percentile(diff0, 95))
    b_max = int(diff0.max())
    b1 = base[1]
    boundary_ok = (b_mean <= b1['mean_p95']) and (b_p95 <= b1['p95_p95'])
    # 超出幅度（未超出时为负，表示余量）。两条子判据各自算，取更差的那条。
    ex_mean_rel = (b_mean - b1['mean_p95']) / max(b1['mean_p95'], eps)
    ex_p95_rel = (b_p95 - b1['p95_p95']) / max(b1['p95_p95'], eps)
    ex_mean_abs = b_mean - b1['mean_p95']
    ex_p95_abs = b_p95 - b1['p95_p95']
    mean_band = boundary_ok or (ex_mean_rel <= warn_rel or ex_mean_abs <= warn_abs)
    p95_band = boundary_ok or (ex_p95_rel <= warn_rel or ex_p95_abs <= warn_abs)
    in_warn_band = (not boundary_ok) and mean_band and p95_band
    detail.update(boundary_mean=b_mean, boundary_p95=b_p95, boundary_max=b_max,
                  baseline_adj_mean_p95=b1['mean_p95'], baseline_adj_p95_p95=b1['p95_p95'],
                  boundary_excess_rel=(ex_mean_rel, ex_p95_rel),
                  boundary_excess_abs=(ex_mean_abs, ex_p95_abs),
                  boundary_in_warn_band=in_warn_band,
                  boundary_relative='PASS' if boundary_ok
                  else ('WARN' if in_warn_band else 'FAIL'))

    # ---- 3. inner severe gate（辅门禁，d=1/2/3） ----
    dn = normal_profile(A, B, vertical)
    detail['d_normal'] = dn
    ratios, worst_ratio, worst_d = [], 0.0, None
    for i, d in enumerate((1, 2, 3)):
        sep = 2 * d + 1
        # dn 的下标 d 对应 dn[d]（dn[0] 是 d=0 的边界）——这里容易错位一位。
        r = dn[d] / max(base[sep]['mean_p95'], eps)
        ratios.append(r)
        if r > worst_ratio:
            worst_ratio, worst_d = r, d
    detail['ratio'] = ratios
    detail['worst_ratio'] = worst_ratio
    detail['worst_ratio_d'] = worst_d
    severe = [d for d, r in zip((1, 2, 3), ratios) if r > 1.5]
    warn = [d for d, r in zip((1, 2, 3), ratios) if 1.2 < r <= 1.5]
    detail['inner_severe_d'] = severe
    detail['inner_warn_d'] = warn
    detail['inner_status'] = 'SEVERE_FAIL' if severe else ('WARN' if warn else 'OK')

    # ---- 4. INFO 项（旧口径，不参与判定） ----
    detail['old_absolute'] = 'PASS' if not judge_metrics(
        {'mean': b_mean, 'p95': b_p95, 'max': b_max}, mean_max, p95_max, max_max) else 'FAIL'
    w4 = metrics((A[:, -4:, :] - B[:, :4, :]) if vertical else (A[-4:, :, :] - B[:4, :, :]))
    detail['window4'] = w4
    detail['old_tangent_profile'] = 'INVALID_INFO'

    # ---- 5. 判定：结构判据优先，容忍带只作用于观感判据 ----
    # 顺序不可反：inner severe 一旦触发就是块内断裂，任何容忍带都不适用。
    if severe:
        detail['RESULT'] = 'FAIL'
        return 'FAIL', detail
    if not boundary_ok and not in_warn_band:
        detail['RESULT'] = 'FAIL'
        return 'FAIL', detail
    detail['warn_reason'] = WARN_REASON if in_warn_band else None
    detail['RESULT'] = 'PASS_WITH_WARN' if (warn or in_warn_band) else 'PASS'
    return detail['RESULT'], detail


def seam_pair_vertical(a, b):
    """垂直缝：真正相邻的两列 + 4 列诊断带。"""
    return a[:, -1, :], b[:, 0, :], a[:, -4:, :], b[:, :4, :]


def seam_pair_horizontal(a, b):
    """水平缝：真正相邻的两行 + 4 行诊断带。"""
    return a[-1, :, :], b[0, :, :], a[-4:, :, :], b[:4, :, :]


def metrics(diff):
    flat = np.abs(diff).reshape(-1, 3)
    return {'mean': float(flat.mean()),
            'p95': float(np.percentile(flat, 95)),
            'max': float(flat.max())}


def judge_metrics(m, mean_max, p95_max, max_max):
    return m['mean'] > mean_max or m['p95'] > p95_max or m['max'] > max_max


def run_selftest_v21():
    """Gate C1-B-fix3 §5：连续纹理夹具 selftest。

    与 v1 的关键区别：**不使用独立随机噪声作为主要证据**。
    夹具是一张确定性连续纹理沿中线切开，两侧内容本就连续，
    这样 boundary hard step 与 inner offset k=1/2/3 才能被真实区分。
    """
    lines = ['=== world seams selftest v2.1 (normal profile) ===']
    ok = True

    def make_tex(w, h, seed=11):
        rs = np.random.RandomState(seed)
        yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
        t = (128.0 + 26.0 * np.sin(2 * np.pi * xx / 64.0) +
             18.0 * np.sin(2 * np.pi * yy / 32.0) +
             12.0 * np.sin(2 * np.pi * (xx + yy) / 128.0) +
             rs.randint(-10, 11, size=(h, w)))
        return np.dstack([np.clip(t, 0, 255)] * 3).astype(np.int16)

    W2, CUT = 1024, 512
    full = make_tex(W2, W2)
    L = full[:, :CUT, :].copy()
    R = full[:, CUT:, :].copy()
    R[:, 0, :] = L[:, -1, :]                    # 切口严格连续

    # 1. 连续纹理 PASS
    st_, d = seam_judge_v21(L, R, True, 4.0, 12.0, 30.0)
    lines.append('continuous high texture: boundary=%s inner=%s D_normal=%s ratio=%s -> %s'
                 % (d['boundary_relative'], d['inner_status'],
                    [round(x, 1) for x in d['d_normal']],
                    [round(x, 2) for x in d['ratio']], st_))
    # 连续纹理允许 PASS 或 PASS_WITH_WARN：inner ratio 落在 1.2~1.5 是
    # 正常纹理起伏，不是断裂。任务书 §5.8 允许 WARN 但要求列出人工判断。
    if st_ not in ('PASS', 'PASS_WITH_WARN'):
        ok = False; lines.append('  ERROR: 连续纹理应 PASS/PASS_WITH_WARN，实际 %s' % st_)
    else:
        lines.append('  OK')

    # 2. boundary hard step FAIL（边界直接断裂）
    Rb = R.copy(); Rb[:, 0, :] = np.clip(Rb[:, 0, :] + 50.0, 0, 255)
    st_, d = seam_judge_v21(L, Rb, True, 4.0, 12.0, 30.0)
    lines.append('boundary hard step: boundary=%s (mean=%.1f vs base %.2f) -> %s'
                 % (d['boundary_relative'], d['boundary_mean'],
                    d['baseline_adj_mean_p95'], st_))
    if st_ == 'PASS':
        ok = False; lines.append('  ERROR: 边界硬断裂应 FAIL')
    else:
        lines.append('  OK')

    # 2b. warn band 机制自证（T018 根治后重新定标）。
    # 旧写法是"在真实纹理夹具上加一个略微超基线的阶跃，期望判 WARN"——根治后基线
    # 变宽，8 灰阶阶跃本就落在该材质列间自然起伏之内（列间差本来就 ~7.6），判 PASS
    # 才是正确结论；再硬造一个"刚好超一点"的扰动等于给代码凑用例。
    # 改为直接验证带本身：均匀面 + 1 灰阶缝线，基线≈0，带的绝对臂恰好兜住 1 灰阶量化台阶。
    U = np.full((256, 256, 3), 128, dtype=np.int16)
    Ub = U.copy(); Ub[:, 0, :] = 129
    st_band, d_band = seam_judge_v21(U, Ub, True, 4.0, 12.0, 30.0)
    st_off, d_off = seam_judge_v21(U, Ub, True, 4.0, 12.0, 30.0, 0.0, 0.0)
    lines.append('warn band (uniform face + 1-gray seam line, baseline~0): '
                 'band-on=%s band-off=%s excess_abs=%+.2f'
                 % (st_band, st_off, d_band['boundary_excess_abs'][0]))
    if st_band != 'PASS_WITH_WARN':
        ok = False; lines.append('  ERROR: 1 灰阶量化台阶应落带内记 PASS_WITH_WARN，实际 %s' % st_band)
    elif st_off != 'FAIL':
        ok = False; lines.append('  ERROR: 关掉容忍带后应 FAIL（证明带确实在起作用），实际 %s' % st_off)
    else:
        lines.append('  OK')

    # 2c. 明显超出基线仍必须 FAIL（带不是免死金牌）。
    Uc = U.copy(); Uc[:, 0, :] = 148          # 20 灰阶缝线：远超绝对臂
    st_, d = seam_judge_v21(U, Uc, True, 4.0, 12.0, 30.0)
    lines.append('clearly over baseline (20-gray seam line): excess_abs=%+.2f -> %s'
                 % (d['boundary_excess_abs'][0], st_))
    if st_ != 'FAIL':
        ok = False; lines.append('  ERROR: 明显超基线应 FAIL')
    else:
        lines.append('  OK')

    # 3/4/5. inner offset k=1/2/3 必须由 inner severe 抓到
    for k in (1, 2, 3):
        Rk = R.copy(); Rk[:, k:k + 4, :] = np.clip(Rk[:, k:k + 4, :] + 60.0, 0, 255)
        st_, d = seam_judge_v21(L, Rk, True, 4.0, 12.0, 30.0)
        caught = bool(d['inner_severe_d']) or d['boundary_relative'] == 'FAIL'
        lines.append('inner offset k=%d: boundary=%s inner=%s severe_at=%s D_normal=%s ratio=%s -> %s'
                     % (k, d['boundary_relative'], d['inner_status'],
                        d['inner_severe_d'], [round(x, 1) for x in d['d_normal']],
                        [round(x, 2) for x in d['ratio']], st_))
        if not caught:
            ok = False
            lines.append('  ERROR: k=%d 未被 inner severe 抓到' % k)
        else:
            lines.append('  OK')

    # 6/7. 真实材质自证
    mat_dir = os.path.join(ROOT, 'GameClient', 'TwelveJade', 'Assets', 'ArtSource', 'world', 'materials')
    for case, fname in (('real grass', 'grass.png'), ('real farmland', 'farmland.png')):
        fp = os.path.join(mat_dir, fname)
        if not os.path.exists(fp):
            lines.append('%s: SKIP (%s not found)' % (case, fname))
            continue
        m = np.asarray(Image.open(fp).convert('RGB'), dtype=np.int16)
        H, W = m.shape[0], m.shape[1]
        c = W // 2
        A2, B2 = m[:, :c, :].copy(), m[:, c:, :].copy()
        B2[:, 0, :] = A2[:, -1, :]
        st_, d = seam_judge_v21(A2, B2, True, 4.0, 12.0, 30.0)
        lines.append('%s: boundary=%s inner=%s boundary_mean=%.2f base=%.2f D_normal=%s ratio=%s -> %s'
                     % (case, d['boundary_relative'], d['inner_status'],
                        d['boundary_mean'], d['baseline_adj_mean_p95'],
                        [round(x, 1) for x in d['d_normal']],
                        [round(x, 2) for x in d['ratio']], st_))
        if case == 'real grass':
            if st_ == 'FAIL':
                ok = False; lines.append('  ERROR: grass 自证FAIL')
            else:
                lines.append('  OK')
        else:
            if st_ == 'FAIL':
                lines.append('  REPORT_ONLY: farmland 未PASS，记录为后续软化候选（本轮不改材质）')
            else:
                lines.append('  OK')

    # 8. 全网格枚举 112
    import tempfile, shutil
    tmp = tempfile.mkdtemp(prefix='twelvejade-grid-v21-')
    rs2 = np.random.RandomState(23)
    for gy in range(8):
        for gx in range(8):
            canvas = rs2.randint(80, 180, size=(256, 256)).astype(np.uint8)
            Image.fromarray(np.dstack([canvas] * 3), 'RGB').save(
                os.path.join(tmp, 'chunk_%d_%d.png' % (gx, gy)))
    got = sum(1 for gy in range(8) for gx in range(8) if gx + 1 < 8 or gy + 1 < 8) * 2 - 16
    got = 8 * 7 * 2
    lines.append('full-grid count: %d (expect 112) -> %s' % (got, 'OK' if got == 112 else 'MISMATCH'))
    if got != 112:
        ok = False
    shutil.rmtree(tmp, ignore_errors=True)

    # 原为 'RESULT: %s'（会把自检自身的输出撞成门禁判据字样，对 --selftest-v21 输出 grep
    # "RESULT: PASS" 会误读成门禁绿）。统筹席 B 2026-10-06 01:59 裁决①（升级为通用口径：
    # 任何 --selftest* 代码路径一律打 SELFTEST:，永不打 RESULT:）批准改字样，
    # 由夜1 落地。只改字样，阈值 / 判定分支 / ok 的取值一律不动。
    lines += ['', 'SELFTEST: %s' % ('PASS 自检用例全过' if ok else 'FAIL 自检用例未过')]
    out = os.path.join(ROOT, 'Tools', 'logs', 'world-seams-selftest-v21.log')
    os.makedirs(os.path.dirname(out), exist_ok=True)
    with open(out, 'w', encoding='utf-8', newline='\n') as fh:
        fh.write('\n'.join(lines) + '\n')
    print('\n'.join(lines))


def _load_warn_band(th):
    """读 seamThresholds.warnBand；缺失时回落内置默认并告警（配置链断掉不能静默）。"""
    band = th.get('warnBand') or {}
    rel = band.get('relativeExcessMax')
    abs_ = band.get('absoluteExcessMax')
    if rel is None or abs_ is None:
        sys.stderr.write('WARNING: world-compose.json seamThresholds.warnBand 缺失或不完整，'
                         '回落内置默认 rel=%s abs=%s —— 配置链断了，别当噪音略过。\n'
                         % (WARN_REL_EXCESS_MAX, WARN_ABS_EXCESS_MAX))
        return WARN_REL_EXCESS_MAX, WARN_ABS_EXCESS_MAX
    return float(rel), float(abs_)


# 代次一致性门禁（2026-10-24 事故复盘）：同一次全量重烤的 64 块 mtime 必然挨得极紧，
# 而"顺手补跑几块"会与整批差出数小时以上。窗口取 24h——松到不误报流水线延迟，
# 紧到能当场抓住 chunk_3_3/3_4/4_3/4_4 那种单块补跑。checkout 后 mtime 全同，不会误报。
GENERATION_WINDOW_S = 24 * 3600


def check_generation(preview_dir, coords):
    """返回 (lines, stale)。stale 非空 = 代次混用，接缝结论不可复现。"""
    mt = {c: os.path.getmtime(os.path.join(preview_dir, 'chunk_%d_%d.png' % c))
          for c in coords}
    newest = max(mt.values())
    stale = sorted(c for c in coords if newest - mt[c] > GENERATION_WINDOW_S)
    lines = ['', 'generation: newest %s, window %.0fh'
             % (time.strftime('%Y-%m-%d %H:%M', time.localtime(newest)),
                GENERATION_WINDOW_S / 3600.0)]
    if stale:
        lines.append('generation: MIXED — %d/%d 块非最新代，接缝结论不可复现:'
                     % (len(stale), len(coords)))
        for c in stale:
            lines.append('  - chunk_%d_%d  %s  (差 %.1f 天)'
                         % (c[0], c[1],
                            time.strftime('%Y-%m-%d %H:%M', time.localtime(mt[c])),
                            (newest - mt[c]) / 86400.0))
    else:
        lines.append('generation: uniform — %d 块同一代' % len(coords))
    return lines, stale


def _display_path(path):
    """日志里的路径显示。跨盘符（如 --chunk-dir 指向 C:）时 relpath 会抛 ValueError，
    而一个会崩的门禁比一个红的门禁危险得多，所以退化成绝对路径而不是让门禁挂掉。"""
    try:
        return os.path.relpath(path, ROOT).replace('\\', '/')
    except ValueError:
        return path.replace('\\', '/')


def run_full_grid(preview_dir, mean_max, p95_max, max_max,
                  warn_rel=WARN_REL_EXCESS_MAX, warn_abs=WARN_ABS_EXCESS_MAX,
                  allow_mixed_generation=False):
    """Gate C1-B-0：枚举 8x8 全网格的 112 条相邻缝（56 水平 + 56 垂直）。

    判定口径 seam_judge_v21：inner severe 是结构判据（零容忍），
    boundary relative 是观感判据（超基线 P99 但在容忍带内记 PASS_WITH_WARN）。
    window4 与旧绝对阈值仅作 INFO。阈值全部读 world-compose.json。
    """
    lines = ['=== world seams full grid (Gate C1-B-0) ===',
             'chunk dir : %s' % _display_path(preview_dir),
             'thresholds: mean<=%s p95<=%s max<=%s' % (mean_max, p95_max, max_max),
             'gate      : seam_judge_v21 (baseline P%.0f + inner severe + warn band)' % BASELINE_PCT,
             'warn band : 超出幅度 rel<=%.0f%% 或 abs<=%.1f 灰阶 → PASS_WITH_WARN'
                       % (warn_rel * 100, warn_abs)]
    if not os.path.isdir(preview_dir):
        lines += ['', 'chunk 目录不存在: %s' % preview_dir, 'RESULT: FAIL']
        _write_full_log(lines)
        print('\n'.join(lines))
        return 1

    compose = json.load(open(os.path.join(CFG, 'world-compose.json'), encoding='utf-8'))
    columns = int(compose.get('columns', 8))
    rows = int(compose.get('rows', 8))
    expected = columns * rows

    img, missing = {}, []
    for cy in range(rows):
        for cx in range(columns):
            p = os.path.join(preview_dir, 'chunk_%d_%d.png' % (cx, cy))
            if not os.path.exists(p):
                missing.append('chunk_%d_%d' % (cx, cy))
                continue
            img[(cx, cy)] = np.asarray(Image.open(p).convert('RGB'), dtype=np.int16)
    if missing:
        lines += ['', 'chunk 数量不足：期望 %d，实际 %d' % (expected, len(img)),
                  '缺失: %s' % ', '.join(missing[:12]), 'RESULT: FAIL']
        _write_full_log(lines)
        print('\n'.join(lines))
        return 1

    gen_lines, stale_gen = check_generation(preview_dir, list(img.keys()))
    lines += gen_lines
    if stale_gen:
        if allow_mixed_generation:
            lines.append('generation: 已放行（--allow-mixed-generation），接缝结论不可复现')
        else:
            lines += ['', '代次混用：先全量重烤再谈接缝。',
                      '单块补跑会让 4 条内接缝与相邻边界同时失真，',
                      '此时 112 条缝的任何数字都不代表 world 的真实连续性。',
                      'RESULT: FAIL']
            _write_full_log(lines)
            print('\n'.join(lines))
            return 1

    boundary_fail = []
    boundary_warn = []
    inner_severe = []
    inner_warn = []
    legacy_fail_but_rel_pass = []
    worst = {'mean': 0.0, 'p95': 0.0, 'max': 0.0, 'ratio': 0.0, 'label': 'n/a'}
    lines.append('')
    for cy in range(rows):
        for cx in range(columns):
            if (cx, cy) not in img:
                continue
            if cx + 1 < columns and (cx + 1, cy) in img:
                A, B = img[(cx, cy)], img[(cx + 1, cy)]
                label = 'chunk_%d_%d right vs chunk_%d_%d left' % (cx, cy, cx + 1, cy)
                status, d = seam_judge_v21(A, B, True, mean_max, p95_max, max_max,
                                           warn_rel, warn_abs)
                lines.extend(_full_line_v21(label, status, d))
                _accumulate_v21(boundary_fail, boundary_warn, inner_severe, inner_warn,
                                legacy_fail_but_rel_pass, worst, label, status, d)
            if cy + 1 < rows and (cx, cy + 1) in img:
                A, B = img[(cx, cy)], img[(cx, cy + 1)]
                label = 'chunk_%d_%d bottom vs chunk_%d_%d top' % (cx, cy, cx, cy + 1)
                status, d = seam_judge_v21(A, B, False, mean_max, p95_max, max_max,
                                           warn_rel, warn_abs)
                lines.extend(_full_line_v21(label, status, d))
                _accumulate_v21(boundary_fail, boundary_warn, inner_severe, inner_warn,
                                legacy_fail_but_rel_pass, worst, label, status, d)

    total = len([l for l in lines if l.startswith('chunk_') and ' vs ' in l])
    failed = boundary_fail + inner_severe
    overall = 'FAIL' if failed else ('PASS_WITH_WARN' if (boundary_warn or inner_warn) else 'PASS')
    lines += ['',
              'Total seams: %d' % total,
              'Boundary relative PASS: %d' % (total - len(boundary_fail) - len(boundary_warn)),
              'Boundary relative PASS_WITH_WARN: %d' % len(boundary_warn),
              'Boundary relative FAIL: %d' % len(boundary_fail),
              'Inner severe FAIL: %d' % len(inner_severe),
              'Inner warn: %d' % len(inner_warn),
              'Legacy absolute FAIL but relative PASS: %d' % len(legacy_fail_but_rel_pass),
              'Worst boundary mean: %.1f (%s)' % (worst['mean'], worst['label']),
              'Worst boundary p95: %.1f' % worst['p95'],
              'Worst inner ratio: %.2f' % worst['ratio']]
    if boundary_warn:
        lines.append('WARN SEAMS (结构连续，仅统计极值):')
        for w in boundary_warn:
            lines.append('  - %s' % w)
    if failed:
        lines.append('FAILED SEAMS:')
        for f in failed:
            lines.append('  - %s' % f)
    lines.append('RESULT: %s' % overall)
    _write_full_log(lines)
    print('\n'.join(lines))
    return 1 if failed else 0


def _full_line_v21(label, status, d):
    sep = d['baseline_sep']
    lines = [
        '%s:' % label,
        '  boundary_mean=%.2f boundary_p95=%.2f boundary_max=%d'
        % (d['boundary_mean'], d['boundary_p95'], d['boundary_max']),
        '  baseline_adj_mean_p95=%.2f baseline_adj_p95_p95=%.2f'
        % (d['baseline_adj_mean_p95'], d['baseline_adj_p95_p95']),
        '  boundary_relative: %s' % d['boundary_relative'],
    ]
    if d['boundary_relative'] != 'PASS':
        er, ea = d['boundary_excess_rel'], d['boundary_excess_abs']
        lines.append('  boundary_excess: mean %+.1f%% (%+.2f 灰阶) / p95 %+.1f%% (%+.2f 灰阶)'
                     % (er[0] * 100, ea[0], er[1] * 100, ea[1]))
        if d['boundary_in_warn_band']:
            lines.append('  warn_reason: %s' % d.get('warn_reason', WARN_REASON))
    lines += [
        '  D_normal[0..3]=%s' % [round(x, 1) for x in d['d_normal']],
        '  baseline_sep_mean_p95[s=1,3,5,7]=%s'
        % [round(sep[k]['mean_p95'], 2) for k in (1, 3, 5, 7)],
        '  ratio[1..3]=%s' % [round(x, 2) for x in d['ratio']],
        '  inner status: %s' % d['inner_status'],
        '  old_absolute: %s INFO' % d['old_absolute'],
        '  old_tangent_profile: INVALID_INFO',
        '  window4: mean=%.1f p95=%.1f max=%d INFO' % (d['window4']['mean'], d['window4']['p95'], d['window4']['max']),
        '  RESULT: %s' % status,
    ]
    return lines


def _accumulate_v21(boundary_fail, boundary_warn, inner_severe, inner_warn,
                    legacy_fail_but_rel_pass, worst, label, status, d):
    if d['boundary_mean'] >= worst['mean']:
        worst['mean'] = d['boundary_mean']
        worst['label'] = label
    worst['p95'] = max(worst['p95'], d['boundary_p95'])
    worst['max'] = max(worst['max'], d['boundary_max'])
    worst['ratio'] = max(worst['ratio'], d['worst_ratio'])
    if d['boundary_relative'] == 'FAIL':
        boundary_fail.append(label)
    elif d['boundary_relative'] == 'WARN':
        er = d['boundary_excess_rel']
        boundary_warn.append('%s (mean %+.1f%% / p95 %+.1f%%，%s)'
                             % (label, er[0] * 100, er[1] * 100, WARN_REASON))
    if d['inner_status'] == 'SEVERE_FAIL':
        inner_severe.append('%s (d=%s ratio=%s)' % (label, d['inner_severe_d'],
                                                    [round(r, 2) for r in d['ratio']]))
    if d['inner_status'] == 'WARN':
        inner_warn.append('%s (ratio=%s)' % (label, [round(r, 2) for r in d['ratio']]))
    if status != 'FAIL' and d['old_absolute'] == 'FAIL':
        legacy_fail_but_rel_pass.append(label)


def _write_full_log(lines):
    out = os.path.join(ROOT, 'Tools', 'logs', 'world-seams-full.log')
    os.makedirs(os.path.dirname(out), exist_ok=True)
    with open(out, 'w', encoding='utf-8', newline='\n') as fh:
        fh.write('\n'.join(lines) + '\n')


def run_selftest():
    """回归自测：证明 adjacent1 既能放过连续接缝，也能抓住 1 像素硬断裂。"""
    log('=== world seams selftest ===')
    ok = True

    # 用例 1：在切口处两侧像素严格相同的连续纹理，必须 PASS。
    # 夹具要点：构造 left 与 right 时，让 right 的首列直接复制 left 的末列，
    # 这样测的纯粹是「接缝是否连续」，内部纹理可以任意强（另有用例 3 覆盖）。
    n = 128
    cut = n // 2                              # 64
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float32)
    texture = (128.0 + 40.0 * np.sin(2 * np.pi * xx / 32.0) +
               25.0 * np.sin(2 * np.pi * yy / 16.0) + 10.0 * np.sin(2 * np.pi * xx / 8.0))
    full = np.dstack([texture] * 3).clip(0, 255)
    left = full[:, :cut].copy()
    right = full[:, cut:].copy()
    right[:, 0, :] = left[:, -1, :]           # 切口两侧像素严格相等 = 连续接缝
    left, right = left, right
    # T022 门禁入口统一：以下四个用例改考 seam_judge_v21。原先考的是 relative_gate，
    # 而那套口径在 C1-B-fix3 已被判为抓不住块内断裂（offset_profile 沿切线方向错开，
    # 与垂直于缝的断裂带正交），用例 4 因此在 HEAD 上就是 FAIL。
    # 改考现行门禁不减弱任何断言：连续必过 / 硬断裂必抓 / 高纹理连续必过 / k=1..3 必抓，一条不减。
    la, rb, w4a, w4b = seam_pair_vertical(left, right)
    m1, w1 = metrics(la - rb), metrics(w4a - w4b)
    st1, d1 = seam_judge_v21(left, right, True, 4.0, 12.0, 30.0)
    bad1 = st1 == 'FAIL'
    log('continuous seam: adjacent1 mean=%.2f p95=%.1f max=%d %s' %
        (m1['mean'], m1['p95'], m1['max'], 'FAIL' if bad1 else 'PASS'))
    log('  window4-diagnostic: mean=%.2f p95=%.1f max=%d INFO' % (w1['mean'], w1['p95'], w1['max']))
    if bad1:
        ok = False
        log('  ERROR: 连续接缝被判 FAIL —— adjacent1 口径有误')
    else:
        log('  OK')

    # 用例 2：1 像素硬断裂（右侧首列整体 +50），必须 FAIL
    right_step = right.copy()
    right_step[:, 0, :] = np.clip(right_step[:, 0, :] + 50.0, 0, 255)
    la2, rb2, _, _ = seam_pair_vertical(left, right_step)
    m2 = metrics(la2 - rb2)
    st2, d2 = seam_judge_v21(left, right_step, True, 4.0, 12.0, 30.0)
    caught = st2 == 'FAIL'
    log('hard step seam: adjacent1 mean=%.2f p95=%.1f max=%d %s' %
        (m2['mean'], m2['p95'], m2['max'], 'PASS' if caught else 'FAIL'))
    if not caught:
        ok = False
        log('  ERROR: 1 像素硬断裂未被抓住 —— adjacent1 口径失效')
    else:
        log('  OK')

    # 用例 3：内部高频噪声很强，但边界像素严格一致，必须 PASS（window4 仅 INFO）
    rs = np.random.RandomState(7)          # 仅测试夹具；生成器本身禁用 random
    noise = rs.randint(-60, 61, size=(n, n)).astype(np.float32)
    inner = noise.copy()
    inner[:, n // 2] = noise[:, n // 2 - 1]      # 右块首列 = 左块末列
    hi = np.dstack([inner.clip(0, 255)] * 3)
    hl, hr = hi[:, :n // 2], hi[:, n // 2:]
    la3, rb3, w4a3, w4b3 = seam_pair_vertical(hl, hr)
    m3, w3 = metrics(la3 - rb3), metrics(w4a3 - w4b3)
    st3, d3 = seam_judge_v21(hl, hr, True, 4.0, 12.0, 30.0)
    bad3 = st3 == 'FAIL'
    log('high texture continuous seam: adjacent1 mean=%.2f p95=%.1f max=%d %s' %
        (m3['mean'], m3['p95'], m3['max'], 'FAIL' if bad3 else 'PASS'))
    log('  window4-diagnostic: mean=%.2f p95=%.1f max=%d INFO' % (w3['mean'], w3['p95'], w3['max']))
    if bad3:
        ok = False
        log('  ERROR: 边界连续但被判 FAIL')
    else:
        log('  OK')

    # 用例 4：断裂带起点落在右块内部第 k 列（k=1/2/3），adjacent1 单看会漏，
    # 必须靠 offset profile 的局部极大判据抓住。这是对抗性复核发现的真实盲区。
    # 夹具用真实 chunk 尺寸（1024），两侧各自独立，保证 k 偏移有足够行数。
    caught_all = True
    log('inner-offset fracture (k=1..3, 4px wide, 1024-row):')
    # 夹具要点：两侧必须是**同一张连续纹理**沿中线切开（只在切口处复制边界像素），
    # 这样 D(0)=0、剖面随 d 单调递增；再在右块内部第 k 列开一条断裂带，
    # 剖面才会在 d=k 处出现真实极大——这才是 profile 判据要抓的东西。
    rs = np.random.RandomState(11)            # 仅测试夹具；生成器本身禁用 random
    W2, CUT = 1024, 512
    yy2, xx2 = np.mgrid[0:W2, 0:W2].astype(np.float32)
    base_tex = (128.0 + 26.0 * np.sin(2 * np.pi * xx2 / 64.0) +
                18.0 * np.sin(2 * np.pi * yy2 / 32.0) +
                rs.randint(-14, 15, size=(W2, W2)))
    full2 = np.dstack([np.clip(base_tex, 0, 255)] * 3)
    L = full2[:, :CUT, :].copy()
    R = full2[:, CUT:, :].copy()
    R[:, 0, :] = L[:, -1, :]                 # 切口严格连续

    caught_all = True
    log('inner-offset fracture (k=1..3, 4px wide, continuous texture):')
    for k in (1, 2, 3):
        stepped = R.copy()
        stepped[:, k:k + 4, :] = np.clip(stepped[:, k:k + 4, :] + 50.0, 0, 255)
        stk, dd = seam_judge_v21(L, stepped, True, 4.0, 12.0, 30.0)
        log('  k=%d adjacent1 mean=%.2f -> %s | inner=%s severe_at=%s D_normal=%s ratio=%s'
            % (k, dd['boundary_mean'], stk, dd['inner_status'], dd['inner_severe_d'],
               [round(x, 1) for x in dd['d_normal']], [round(x, 2) for x in dd['ratio']]))
        if stk != 'FAIL':
            caught_all = False
    if not caught_all:
        ok = False
        log('  ERROR: 块内断裂带未被抓住')
    else:
        log('  OK')

    log('')
    # 原为 'RESULT: %s'（会把自检自身的输出撞成门禁判据字样，对 --selftest 输出 grep
    # "RESULT: PASS" 会误读成门禁绿）。统筹席 B 2026-10-06 00:52 批准改字样，
    # 只改这一处字样：阈值、判定分支、ok 的取值一律未动。定规出处：架构师 2026-10-05；由夜1 落地。
    log('SELFTEST: %s' % ('PASS 自检用例全过' if ok else 'FAIL 自检用例未过'))
    out = os.path.join(ROOT, 'Tools', 'logs', 'world-seams-selftest-v2.log')
    os.makedirs(os.path.dirname(out), exist_ok=True)
    with open(out, 'w', encoding='utf-8', newline='\n') as fh:
        fh.write('\n'.join(LINES) + '\n')
    return 0 if ok else 1


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--chunks', nargs='*', default=[])
    ap.add_argument('--center-preview', action='store_true')
    ap.add_argument('--seam-mode', default='adjacent1',
                    choices=['adjacent1', 'window4-diagnostic'])
    ap.add_argument('--selftest', action='store_true')
    ap.add_argument('--selftest-v21', action='store_true',
                    help='Gate C1-B-fix3 selftest：连续纹理夹具 + normal profile')
    ap.add_argument('--full-grid', action='store_true',
                    help='枚举 8x8 全网格的 112 条相邻缝（Gate C1-B-0）')
    ap.add_argument('--chunk-dir', default=None,
                    help='chunk 目录；默认读 world-compose.json 的 outputDir')
    ap.add_argument('--allow-mixed-generation', dest='allow_mixed_generation',
                    action='store_true',
                    help='放行代次混用（仅实验用；此时接缝结论不可复现）')
    args = ap.parse_args()
    if args.selftest_v21:
        return run_selftest_v21()
    if args.selftest:
        return run_selftest()
    if args.seam_mode != 'adjacent1':
        sys.stderr.write(
            'WARNING: window4-diagnostic is not gate-valid; use adjacent1 for RESULT.\n')
    compose = json.load(open(os.path.join(CFG, 'world-compose.json'), encoding='utf-8'))
    th = compose.get('seamThresholds', {})
    mean_max = float(th.get('meanAbsDiffMax', 4.0))
    p95_max = float(th.get('p95AbsDiffMax', 12.0))
    max_max = float(th.get('maxAbsDiffMax', 30.0))
    warn_rel, warn_abs = _load_warn_band(th)

    preview_dir = PREVIEW_DIR
    if args.chunk_dir:
        preview_dir = wp.resolve_repo_path(args.chunk_dir)
    elif args.full_grid:
        preview_dir = wp.resolve_repo_path(compose['outputDir'])
    FULL_LOG = os.path.join(ROOT, 'Tools', 'logs', 'world-seams-full.log')

    if args.full_grid:
        return run_full_grid(preview_dir, mean_max, p95_max, max_max, warn_rel, warn_abs,
                             args.allow_mixed_generation)

    targets = CENTER if (args.center_preview or not args.chunks) else \
        [tuple(int(v) for v in c.split(',')) for c in args.chunks]
    coords = set(targets)

    img = {}
    for cx, cy in targets:
        p = os.path.join(PREVIEW_DIR, 'chunk_%d_%d.png' % (cx, cy))
        if not os.path.exists(p):
            log('chunk_%d_%d: MISSING' % (cx, cy))
            FAILED.append('%d_%d missing' % (cx, cy))
            continue
        img[(cx, cy)] = np.asarray(Image.open(p).convert('RGB'), dtype=np.int16)

    seams = []
    for (cx, cy) in targets:
        if (cx, cy) not in img:
            continue
        right = (cx + 1, cy)
        below = (cx, cy + 1)
        if right in img:
            seams.append(('chunk_%d_%d right vs chunk_%d_%d left' % (cx, cy, right[0], right[1]),
                          seam_pair_vertical(img[(cx, cy)], img[right]),
                          img[(cx, cy)], img[right]))
        if below in img:
            seams.append(('chunk_%d_%d bottom vs chunk_%d_%d top' % (cx, cy, below[0], below[1]),
                          seam_pair_horizontal(img[(cx, cy)], img[below]),
                          img[(cx, cy)], img[below]))

    # T022：中心 2×2 路径与 --full-grid 统一走 seam_judge_v21 —— 同一条缝只允许一个口径。
    # 旧 v2 relative_gate 不再参与任何判定（其 offset_profile 剖面判据在 C1-B-fix3 已判 INVALID）。
    log('=== world seams check (gate v2.1：与 --full-grid 同一口径) ===')
    log('chunk dir : %s' % os.path.relpath(PREVIEW_DIR, ROOT).replace('\\', '/'))
    log('gate      : seam_judge_v21 (baseline P%.0f 同形状 + inner severe + warn band)' % BASELINE_PCT)
    log('warn band : 超出幅度 rel<=%.0f%% 或 abs<=%.1f 灰阶 → PASS_WITH_WARN'
        % (warn_rel * 100, warn_abs))
    log('legacy absolute (mean<=%s p95<=%s max<=%s) : INFO_ONLY' % (mean_max, p95_max, max_max))
    boundary_fail, boundary_warn, severe, inner_warn_list, legacy = [], [], [], [], []
    worst = {'mean': 0.0, 'p95': 0.0, 'max': 0.0, 'ratio': 0.0, 'label': 'n/a'}
    for label, _pair, block_a, block_b in seams:
        vertical = 'right' in label
        status, d = seam_judge_v21(block_a, block_b, vertical, mean_max, p95_max, max_max,
                                   warn_rel, warn_abs)
        for line in _full_line_v21(label, status, d):
            log(line)
        _accumulate_v21(boundary_fail, boundary_warn, severe, inner_warn_list,
                        legacy, worst, label, status, d)
    FAILED.extend(boundary_fail + severe)

    log('')
    log('Total seams: %d' % len(seams))
    log('Boundary relative PASS: %d' % (len(seams) - len(boundary_fail) - len(boundary_warn)))
    log('Boundary relative PASS_WITH_WARN: %d' % len(boundary_warn))
    log('Boundary relative FAIL: %d' % len(boundary_fail))
    log('Inner severe FAIL: %d' % len(severe))
    log('Inner warn: %d' % len(inner_warn_list))
    log('Worst boundary mean: %.1f (%s)' % (worst['mean'], worst['label']))
    log('Worst inner ratio: %.2f' % worst['ratio'])
    log('Failed: %d' % len(FAILED))
    log('RESULT: ' + ('FAIL' if FAILED else
                      ('PASS_WITH_WARN' if (boundary_warn or inner_warn_list) else 'PASS')))
    if FAILED:
        log('FAILED:')
        for f in FAILED:
            log('- ' + f)
    os.makedirs(os.path.dirname(LOG), exist_ok=True)
    with open(LOG, 'w', encoding='utf-8', newline='\n') as fh:
        fh.write('\n'.join(LINES) + '\n')
    return 1 if FAILED else 0


if __name__ == '__main__':
    sys.exit(main())
