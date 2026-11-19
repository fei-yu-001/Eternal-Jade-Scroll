#!/usr/bin/env bash
# =============================================================================
# Tools/run-gates.sh —— 纯逻辑门禁汇总器（**只汇总，不判定**）
#
# 任务依据：Documents/架构文档.md `# 交接包 2026-10-05-G` → `## G-3 夜1 任务书`
#           → G-3 ·【统筹席 B 裁决 + 收口确认】（00:52 落笔，节标题原标 00:44）「续派」；
#           → G-3 ·【统筹席 B 复跑全部证实 → 已合入 fbcdd19 + 裁你三件 + 续派】（01:59）「续派（G-3 第四块）」。
#           引用按 B 席 01:22 口径写「节标题」而非行号/原始时间戳，避免重排后失效。
#
# 定位（G-3 续派 判据③）：本脚本**不许自己判定"通过"**。每一道门禁的判据仍然留在各门禁
# 自己的代码里，本脚本只做三件事：按顺序调用、打印 `脚本名 / 命令 / 退出码 / RESULT 行原文`、
# 末尾给一行"几红几绿"。它唯一的判据是：
#
#       任一被调用门禁退出码非零  ⇒  本脚本退出非零
#
# 因此本脚本不含任何阈值、期望值、白名单式判断；RESULT 行只作**展示**抓取，
# 抓取不到不等于通过，是否红仍只看退出码。
#
# 自证入口（G-3 第五块，B 席 02:39 续派；对应 2号 T114 形态的 `check` 子命令）：
#   bash Tools/run-gates.sh --self-check
#   它防的假绿：**某一道关被静默跳过时**——脚本被改名、路径写错、提前 return、被 `|| true` 吞掉，
#   它可能既没打 `RESULT:` 也没打 `SELFTEST:`，而"少了一条"这种事，只看退出码的汇总器会算进"绿"。
#   判据：25 条里每一条都必须产出至少一行 `RESULT:` 或 `SELFTEST:`，缺任何一条 ⇒ 本入口变红退出非零，
#   并且**指名**是哪一条没出字（B 席判据③：不许只数行数）。
#   反证（临时把某一条换成"必然不出字"的包装）：
#       NIGHT1_SELFCHECK_MUTE="check-world-seams.py --selftest" bash Tools/run-gates.sh
#   反证夹具只准落 `.batch/night1/`（G-3 第五块 停止条件③）。
#   注：`NIGHT1_SELFCHECK_MUTE` 一旦被设置，本脚本**强制进入自证模式**，
#       即"静默跳过一道关"这条路永远走不到绿——不许有人拿它去凑绿。
#
# 用法：
#   bash Tools/run-gates.sh
#   bash Tools/run-gates.sh --fixture-dir .batch/night1/fixture-red   # 红路自证
#   bash Tools/run-gates.sh --self-check                              # 自证：25 条条条出字
#   bash Tools/run-gates.sh --self-check-strict                       # 自证 + 缺字独立计数（可翻的例外开关）
#
# 参数 / 环境变量（都可选）：
#   --self-check                跑完 25 条后断言"每条都产出了判据字样"，缺一条即红。
#   --self-check-strict         在 --self-check 之上把"缺判据字样"从**指名**升级为**独立计数**：
#                               打印 `缺判据字样条数 M / 允许缺字条数 K` 并断言 M ≤ K。
#                               K 默认 0（一条都不许缺），只能由 NIGHT1_ALLOW_NO_EVIDENCE 改。
#                               用途：主架构师裁 (a)（`check-town-overlap.py` 该不该补判据行）
#                               之后，**只改这一个开关**即可验证"例外是否已被填上"。
#                               注意：本档只把"缺字"这一维交给开关；"根本没被调用"、
#                               "有门禁退出码非零"、"反证夹具缺失"三种情况**永远红**，不受开关影响。
#   --fixture-dir <目录>        红路自证用：临时把"接受配置路径覆盖参数"的门禁指向该目录下的
#                               坏配置。**盘上真配置零改动**；坏夹具请落在 .batch/night1/ 下。
#                               与环境变量 NIGHT1_FIXTURE_DIR 等价，命令行优先。
#                               （实测：本机 `bash` 是 WSL，PowerShell 里设的 $env: 传不进 WSL，
#                                 所以红路自证请用命令行参数。）
#   NIGHT1_FIXTURE_DIR=<目录>   同上（环境变量形式）。
#   NIGHT1_PYTHON=<解释器>      跳过解释器自动挑选，直接指定。
#   NIGHT1_ALLOW_NO_EVIDENCE=<n>   `--self-check-strict` 的允许缺字条数（默认 0）。
#                               设为 >0 时输出会显式打一条"已放宽"警告并落进证据日志，留痕。
#
# 证据日志（G-3 第六块，B 席 03:41 续派 判据②/③）：
#   `--self-check` / `--self-check-strict` 结束时把本次结论落到
#   `Tools/logs/night1-selfcheck-<短sha>-<YYYYmmdd-HHMM>.log`
#   —— 按 夜2 的 **R2 证据日志命名**（**不是** `m-live-*` 那种活日志），**首行＝R1 批次坐标**。
#   落 `.batch/` 等于没交（`.batch` 不入 git，夜2 R8），所以日志只落 `Tools/logs/`。
#
# 停止条件（抄自任务书，改动本脚本时同样适用）：
#   ① 需要改任何门禁的判据/阈值/期望值才能汇总绿 → 停下问 B；
#   ② 要跑 Unity 夹具的那几关不进这个脚本（副本归属与工程锁各线不同），只收纯逻辑关；
#   ③ Tools/ 下若已有别的线在飞的同名文件，停手报告，不要覆盖。
# 产物归属：夜1（夜间外包一号）落地，统筹席 B 复跑后 pathspec 合入。
# =============================================================================

set -u

cd "$(dirname "$0")/.." || exit 1

FIXTURE_DIR="${NIGHT1_FIXTURE_DIR:-}"
SELF_CHECK=0
# 严格档（G-3 第六块）：把"缺判据字样"从指名升级为独立计数，断言 缺字条数 ≤ 允许例外数。
STRICT=0
# 允许缺字条数（默认 0 ＝ 一条都不许缺）。主架构师裁 (a) 之后，只改这一个开关即可验证例外是否已被填上。
ALLOW_NO_EVIDENCE="${NIGHT1_ALLOW_NO_EVIDENCE:-0}"
case "$ALLOW_NO_EVIDENCE" in
    ''|*[!0-9]*) ALLOW_NO_EVIDENCE=0 ;;
esac
# 反证：把某一条的调用临时换成"必然不出字"的包装（夹具只落 .batch/night1/）。
# 一旦设置即强制进自证模式 ⇒ 这条路永远走不到绿，杜绝被拿来凑绿。
NIGHT1_SELFCHECK_MUTE="${NIGHT1_SELFCHECK_MUTE:-}"
NIGHT1_SELFCHECK_STUB="${NIGHT1_SELFCHECK_STUB:-.batch/night1/no-evidence-stub.sh}"
[ -n "$NIGHT1_SELFCHECK_MUTE" ] && SELF_CHECK=1
while [ "$#" -gt 0 ]; do
    case "$1" in
        --self-check)
            SELF_CHECK=1
            shift
            ;;
        --self-check-strict)
            SELF_CHECK=1
            STRICT=1
            shift
            ;;
        --fixture-dir)
            FIXTURE_DIR="${2:-}"
            shift 2
            ;;
        --fixture-dir=*)
            FIXTURE_DIR="${1#--fixture-dir=}"
            shift
            ;;
        -h|--help)
            sed -n '1,40p' "$0"
            exit 0
            ;;
        *)
            echo "run-gates.sh: 不认识的参数 '$1'（本脚本只做汇总，不接受改判据的参数）" >&2
            exit 1
            ;;
    esac
done

# ---- 解释器选择（健壮性：不同 shell 里 python 的名字不一样，挑一个真能跑门禁的）--------
# 说明：本仓门禁依赖 numpy / PIL。挑解释器的依据**只有"能不能 import 这两个库"**，
# 与任何门禁判据无关；挑不到就红着退出，绝不假装绿。
pick_python() {
    if [ -n "${NIGHT1_PYTHON:-}" ]; then
        echo "$NIGHT1_PYTHON"
        return 0
    fi
    local c p
    for c in python python.exe py.exe py python3; do
        p="$(command -v "$c" 2>/dev/null)" || continue
        [ -n "$p" ] || continue
        if "$c" -c 'import numpy, PIL' >/dev/null 2>&1; then
            echo "$c"
            return 0
        fi
    done
    echo ""
}

PYTHON="$(pick_python)"

RED=0
GREEN=0
FAILED_LIST=""
# T5 / D-57（B 席 08:10 追加）：WARN 只做**可见性计数**，不进退出码、不影响红绿。
WARN_TOTAL=0

# ---- 自证（--self-check）用的账：每条跑过没有、出没出判据字样 ------------------------
# 这里的 25 条与下面的调用清单**必须一一对应**（顺序无关，只看集合）；
# 万一两边漂移，自证会红着报"某条没跑"，属安全方向（报红比报绿安全）。
ALL_LABELS="check-town-config-consistency.py|check-town-overlap.py|check-world-mask.py|check-world-materials.py|check-world-seams.py --full-grid|PropChecks/check-plot-overlap.py|PropChecks/check-prop-art.py|check-ui-layout.py|check-town-config-consistency.py --selftest|check-town-overlap.py --selftest|check-world-mask.py --selftest|check-world-materials.py --selftest|check-world-seams.py --selftest|check-world-seams.py --selftest-v21|PropChecks/check-prop-art.py --selftest|check-ui-layout.py --selftest|check-walk-sheet-motion.py --selftest|art-intake-ledger.py --check|check-walksheet-cutout.py|check-walksheet-cutout.py --selftest|check-world-build.py|check-town-layout.py|PropChecks/check-building-scale.py|PropChecks/check-collision-depth.py|PropChecks/check-plot-overlap.py --selftest"
GATE_TOTAL=0
EVIDENCE_OK=0
NO_EVIDENCE_LIST=""
SEEN_LIST=""
STUB_ERR=""

# ---- 批次坐标（R1；格式照 Documents/raw/取证留存规格.md 的模板行，规格归 夜2，实现归夜1）----
# 只作展示，**不参与任何判定**（B 席 01:59 判据③）：红绿仍只认各门禁自己的退出码。
# 停止条件①：工作树清单截断到前 20 个路径并注明「另有 N 项」；
#             绝不去改 .gitignore / .git/info/exclude 让它变短。
BATCH_TIME="$(date '+%Y-%m-%d %H:%M:%S')"
# T6 / B 席 09:20（G-4 末尾 触发表修正）：这一格的 **sha 必须保持 ASCII**
# —— 它落在 `批次: … HEAD=<sha> …` 与证据日志首行里。GBK 控制台读不出中文串，
#    按 B 席口径"读不出的判据行等于没有判据行"，故回落值用 ASCII 而不是中文。
HEAD_SHA="$(git rev-parse --short HEAD 2>/dev/null || echo '(git-unavailable)')"
# 证据日志用的时间戳与"安全 sha"（HEAD 取不到时 HEAD_SHA 可能是带括号的提示串，不能直接进文件名）
LOG_TS="$(date '+%Y%m%d-%H%M')"
SAFE_SHA="$HEAD_SHA"
case "$SAFE_SHA" in
    *[!A-Za-z0-9]*) SAFE_SHA="nogit" ;;
esac
# `-c core.quotepath=false` 是**单次调用**参数，不写进任何 git 配置（G-1：不改 git 配置）；
# 只为让中文路径原样显示，否则清单里全是 \345\274\200 这类八进制转义，展示不出东西。
DIRTY_RAW="$(git -c core.quotepath=false status --porcelain 2>/dev/null || true)"
if [ -z "$(printf '%s' "$DIRTY_RAW" | tr -d '[:space:]')" ]; then
    WORKTREE_STATE="clean"
else
    DIRTY_TOTAL="$(printf '%s\n' "$DIRTY_RAW" | wc -l | tr -d '[:space:]')"
    DIRTY_SHOWN=""
    _i=0
    while IFS= read -r _line; do
        [ -n "$_line" ] || continue
        _i=$((_i + 1))
        [ "$_i" -le 20 ] || continue
        _p="${_line#???}"
        if [ -z "$DIRTY_SHOWN" ]; then DIRTY_SHOWN="$_p"; else DIRTY_SHOWN="$DIRTY_SHOWN,$_p"; fi
    done <<EOF
$DIRTY_RAW
EOF
    unset _i _line _p
    if [ "$DIRTY_TOTAL" -gt 20 ]; then
        WORKTREE_STATE="dirty: $DIRTY_SHOWN（另有 $((DIRTY_TOTAL - 20)) 项；清单已截断，只作展示不参与判定）"
    else
        WORKTREE_STATE="dirty: $DIRTY_SHOWN"
    fi
fi
echo "批次: $BATCH_TIME / HEAD=$HEAD_SHA / 工作树=$WORKTREE_STATE"
echo "run-gates.sh —— 纯逻辑门禁汇总（只汇总，不判定）"
echo "工作目录: $(pwd)"
if [ -z "$PYTHON" ]; then
    echo "run-gates.sh: FATAL 找不到带 numpy/PIL 的 python 解释器（试过 python/python.exe/py.exe/py/python3）。" >&2
    echo "run-gates.sh: 请用 NIGHT1_PYTHON=<解释器> 指定后重跑。**一道门禁都没跑，这不是绿。**" >&2
    exit 1
fi
echo "解释器  : $PYTHON（$("$PYTHON" -V 2>&1 | head -1)）"
echo "基线 sha : $HEAD_SHA（与首行批次坐标同源）"
[ -n "$FIXTURE_DIR" ] && echo "坏夹具目录（红路自证）: $FIXTURE_DIR"
echo ""

run_one() {
    local label="$1"
    shift
    local -a cmd=("$@")
    local out rc res n warns warn_n

    # 反证入口：把这一条临时换成"必然不出字"的包装（只在自证模式下可能被触发，见脚本头）
    if [ -n "$NIGHT1_SELFCHECK_MUTE" ] && [ "$label" = "$NIGHT1_SELFCHECK_MUTE" ]; then
        if [ -f "$NIGHT1_SELFCHECK_STUB" ]; then
            # 走 `bash <夹具>` 而不是直接执行，免得跨 shell 撞可执行位（WSL/Git Bash 行为不一致）
            cmd=("bash" "$NIGHT1_SELFCHECK_STUB")
        else
            STUB_ERR="[反证夹具 $NIGHT1_SELFCHECK_STUB 不存在，反证没跑成]"
        fi
    fi

    echo "------------------------------------------------------------------------------"
    echo "脚本名: $label"
    echo "命令  : ${cmd[*]}"
    out="$("${cmd[@]}" 2>&1)"
    rc=$?
    echo "退出码: $rc"

    echo "RESULT 行原文:"
    res="$(printf '%s\n' "$out" | grep -E 'RESULT:|SELFTEST:' || true)"
    if [ -z "$res" ]; then
        echo "  | （输出里没有 RESULT / SELFTEST 行；是否红只看上面的退出码）"
    else
        printf '%s\n' "$res" | sed 's/^/  | /'
    fi

    # ---- T5 / D-57（B 席 08:10 追加）：WARN **必须可见**，但**不进退出码** ------------------
    # 由来：S-3 的 D-57「门禁 WARN 不许静默」。本汇总器原先只抓 RESULT:/SELFTEST:，
    # 于是 check-world-materials 那 2 条真 WARN 在汇总输出里是 0 ⇒ 读汇总的人根本不知道要交代。
    # 实现上**故意不并入上面的 res**：res 是第六块自证「这一条到底出没出判据字样」的记账口径，
    # 把 WARN 混进 res 会让"只打 WARN、不打 RESULT 的关"被算成"有判据字样"，反而造出假绿。
    # 阈值与任何门禁的判定文字一律未动（停止条件②：不许降阈值、不许删 WARN）。
    warns="$(printf '%s\n' "$out" | grep -E 'WARN' || true)"
    if [ -n "$warns" ]; then
        echo "WARN 原文（D-57：当轮须修或登记有主债务；**不计入红绿**）:"
        printf '%s\n' "$warns" | sed 's/^/  ! /'
        warn_n="$(printf '%s\n' "$warns" | grep -c . || true)"
        WARN_TOTAL=$((WARN_TOTAL + warn_n))
    fi

    # ---- 自证记账：这一条到底出没出判据字样（与退出码无关的第二个维度）----------------
    n="$(printf '%s' "$res" | grep -c . || true)"
    GATE_TOTAL=$((GATE_TOTAL + 1))
    SEEN_LIST="$SEEN_LIST|$label|"
    if [ "$n" -eq 0 ]; then
        NO_EVIDENCE_LIST="$NO_EVIDENCE_LIST [$label]"
    else
        EVIDENCE_OK=$((EVIDENCE_OK + 1))
    fi
    [ "$SELF_CHECK" -eq 1 ] && echo "判据字样: $n 行"

    if [ "$rc" -ne 0 ]; then
        RED=$((RED + 1))
        FAILED_LIST="$FAILED_LIST [$label rc=$rc]"
    else
        GREEN=$((GREEN + 1))
    fi
}

echo "################ 第一部分：纯逻辑门禁（真实盘上配置，零改动） ################"
# check-ui-layout 主跑是界面布局判据。--report 只打印不判定，不接；--selftest 在第二部分。
for g in \
    "Tools/check-town-config-consistency.py" \
    "Tools/check-town-overlap.py" \
    "Tools/check-world-mask.py" \
    "Tools/check-world-materials.py" \
    "Tools/check-world-build.py" \
    "Tools/check-town-layout.py" \
    "Tools/check-ui-layout.py" \
    "Tools/check-walksheet-cutout.py"
do
    run_one "$(basename "$g")" "$PYTHON" "$g"
done

# world-seams 的门禁入口是 --full-grid（--selftest / --selftest-v21 是自检，不是门禁判据）
run_one "check-world-seams.py --full-grid" "$PYTHON" Tools/check-world-seams.py --full-grid

# plot-overlap 是唯一接受配置路径覆盖参数的门禁，红路自证走这里（盘上真配置不动）
plot_cmd=("$PYTHON" "Tools/PropChecks/check-plot-overlap.py")
if [ -n "$FIXTURE_DIR" ]; then
    if [ -f "$FIXTURE_DIR/building-plots.json" ] && [ -f "$FIXTURE_DIR/town-map.json" ]; then
        plot_cmd+=("--plots" "$FIXTURE_DIR/building-plots.json" \
                   "--town-map" "$FIXTURE_DIR/town-map.json")
    else
        echo "! NIGHT1_FIXTURE_DIR 已设，但目录下缺 building-plots.json / town-map.json；" \
             "plot-overlap 仍用盘上真配置"
    fi
fi
run_one "PropChecks/check-plot-overlap.py" "${plot_cmd[@]}"

# MAP-19 接入：check-prop-art 主跑（立绘资产完整性）。--report 是展示模式（rc 恒 0），
# 只打印不判定，**不进判定段**，故这里只接主跑。
prop_art_cmd=("$PYTHON" "Tools/PropChecks/check-prop-art.py")
run_one "PropChecks/check-prop-art.py" "${prop_art_cmd[@]}"
run_one "PropChecks/check-building-scale.py" "$PYTHON" Tools/PropChecks/check-building-scale.py
run_one "PropChecks/check-collision-depth.py" "$PYTHON" Tools/PropChecks/check-collision-depth.py

echo ""
echo "################ 第二部分：自检关（4 支夜1 新增 + 2 支既有 seams 自检 + 1 支 MAP-19 prop-art 自检 + 3 支本轮自检 + 1 支台账守卫；坏输入 → 门禁必须变红） ################"
for s in \
    "Tools/check-town-config-consistency.py --selftest" \
    "Tools/check-town-overlap.py --selftest" \
    "Tools/check-world-mask.py --selftest" \
    "Tools/check-world-materials.py --selftest" \
    "Tools/check-ui-layout.py --selftest" \
    "Tools/check-walk-sheet-motion.py --selftest" \
    "Tools/check-walksheet-cutout.py --selftest"
do
    run_one "$(basename "${s%% *}") --selftest" $PYTHON $s
done

# 既有两支 seams 自检（B 席 01:59 裁决③：收进来，否则汇总覆盖 4 支新自检却漏 2 支既有自检是个空洞）
# 它们是员工线既有的判据资产：**只沿用命名风格、不重写、不扩判据**（G-3 停止条件②）。
run_one "check-world-seams.py --selftest"     "$PYTHON" Tools/check-world-seams.py --selftest
run_one "check-world-seams.py --selftest-v21" "$PYTHON" Tools/check-world-seams.py --selftest-v21

# MAP-19 接入：check-prop-art 自检（14 条用例，坏输入必须真变红）。入自检段，不入判定段。
run_one "PropChecks/check-prop-art.py --selftest" "$PYTHON" Tools/PropChecks/check-prop-art.py --selftest
run_one "PropChecks/check-plot-overlap.py --selftest" "$PYTHON" Tools/PropChecks/check-plot-overlap.py --selftest

# 走帧运动闸门主入口要一张 sheet，没有目录批处理，汇总器只接 --selftest（已在上面的自检循环里）。
# 抠图主跑独立读运行时必需文件，不能用自测或台账一致性代替质量判定。
# 台账 --check 不写盘，只守存量 9 张是否仍与闸门口径一致。不接默认写盘入口。
run_one "art-intake-ledger.py --check" "$PYTHON" Tools/art-intake-ledger.py --check

echo ""
echo "=============================================================================="
# ---- 覆盖面缺口（2026-10-16 补）------------------------------------------------
# ALL_LABELS 只自证「清单内部齐」，查不出「盘上有门禁压根没被收进来」。
# 这一族是最贵的假绿：门禁自己红着，汇总却报全绿，读汇总的人以为没事。
# 只做**可见性**：不参与 rc（红线仍只认各门禁自己的退出码），与 WARN 同性质。
# 零参主跑不成立的子门禁必须写进 UNCOVERED_SKIP 并注明理由，不许静默。
# 2026-10-31（青岩，主架构师七七令「给绿加盖标记」）：本段位置放在汇总行之前，
# 让「覆盖缺口」与「几红几绿」出现在同一屏。扫描范围、SKIP 名单、匹配方式一字未动，
# 本段仍只做可见性，不进 RED/GREEN、不进退出码。
UNCOVERED_SKIP=" check-character-identity.py "   # 需 <参考.png> <候选...> 两段入参，由 assemble-walk-frames.py 内部调
UNCOVERED=""
for _g in Tools/check-*.py Tools/PropChecks/check-*.py; do
    [ -f "$_g" ] || continue
    case "$_g" in *.before.py) continue ;; esac
    _b="$(basename "$_g")"
    case "$UNCOVERED_SKIP" in *" $_b "*) continue ;; esac
    case "$ALL_LABELS" in
        *"$_b"*) ;;
        *) UNCOVERED="$UNCOVERED $_b" ;;
    esac
done
unset _g _b
if [ -n "$UNCOVERED" ]; then
    echo "覆盖缺口：盘上有这些 check-*.py 但 ALL_LABELS 没有（不计红绿，处置见 Tools/门禁健壮性台账.md）:$UNCOVERED"
fi
# ---- 口径声明（2026-10-31 青岩，主架构师七七令「给绿加盖标记」）----------------
# 只加标记，不加判定。上面「几红几绿」只数本汇总器清单内的条数；Unity 六关按停止条件②
# 一律不进本脚本。不盖这个章，「N 绿 / M 红」复印出去会被读成「门禁体系全绿」。
# 不改任何门禁判据、不改 RED/GREEN 计数、不改退出码语义，本段也不参与 rc
# （与 WARN / 覆盖缺口同性质）。不许扩成「六关自动跑」。
echo "口径：本汇总只覆盖纯逻辑门禁（$GATE_TOTAL 条），Unity 六关**不在内**——"
echo "      TerrainPropShot / CombatEngineShot / InventoryEconomyShot /"
echo "      NpcDialogueShot / FrontEndShotProps / FrontEndShot"
echo "      六关须单独以日志里的 'suite finished, 0 problem(s)' 认证，退出码 0 不算通过（T044）。"
echo "      本行以上不给 Unity 侧作任何背书；'全绿' 只指本汇总器清单内。"
echo "-------------------------------------------------------------------------------"
echo "汇总：$GREEN 绿 / $RED 红"
if [ -n "$FAILED_LIST" ]; then
    echo "红的有：$FAILED_LIST"
fi
echo "WARN 合计 $WARN_TOTAL 条（D-57：当轮须修或登记有主债务）"
echo "（判据只认各门禁自己的退出码；「几红几绿」是计数值，不是本脚本的判定；"
echo "  WARN / 覆盖缺口 / 口径声明三者只做可见性，均不参与判定）"
echo "=============================================================================="

# ---- 自证（--self-check）：防"某一道关被静默跳过"这一族假绿 ------------------------
# 只看退出码抓不到"少了一条"：一道关若被改名/路径写错/提前 return/被 `|| true` 吞掉，
# 它可能既没打 RESULT: 也没打 SELFTEST:，而汇总器会把"没跑"当成"没红"。
if [ "$SELF_CHECK" -eq 1 ]; then
    # 证据日志（G-3 第六块 判据②/③；R2 证据日志命名，不是 m-live-* 活日志，首行＝R1 批次坐标）
    mkdir -p Tools/logs 2>/dev/null || true
    SELFCHECK_LOG="Tools/logs/night1-selfcheck-$SAFE_SHA-$LOG_TS.log"
    printf '%s\n' "批次: $BATCH_TIME / HEAD=$HEAD_SHA / 工作树=$WORKTREE_STATE" > "$SELFCHECK_LOG"
    # 自证整段包成函数，便于把同一份输出**同时**送 stdout 与证据日志（用 PIPESTATUS 取真正的退出码）
    run_selfcheck() {
    MISSING_RUN=""
    _oldifs="$IFS"
    IFS='|'
    for _l in $ALL_LABELS; do
        [ -n "$_l" ] || continue
        case "$SEEN_LIST" in
            *"|$_l|"*) ;;
            *) MISSING_RUN="$MISSING_RUN [$_l]" ;;
        esac
    done
    IFS="$_oldifs"
    unset _oldifs _l

    echo ""
    echo "################ 自证（--self-check）：25 条每一条都必须自己产出判据字样 ################"
    echo "已跑条数: $GATE_TOTAL / 有判据字样: $EVIDENCE_OK / 无判据字样: $((GATE_TOTAL - EVIDENCE_OK))"
    if [ -n "$MISSING_RUN" ]; then
        echo "SELFCHECK: FAIL 以下关**根本没被调用**（少了一条，第二真源就是这么长出来的）：$MISSING_RUN"
    fi
    if [ -n "$NO_EVIDENCE_LIST" ]; then
        echo "SELFCHECK: FAIL 以下关跑了却**没产出任何 RESULT:/SELFTEST: 行**（可能被改名/提前 return/被 || true 吞掉）：$NO_EVIDENCE_LIST"
    fi
    if [ -n "$STUB_ERR" ]; then
        echo "SELFCHECK: FAIL $STUB_ERR"
    fi
    if [ "$RED" -ne 0 ]; then
        echo "SELFCHECK: FAIL 有 $RED 道关退出码非零（红清单：$FAILED_LIST）——自证不替它们翻案"
    fi
    # ---- 独立计数档（G-3 第六块）：把"缺判据字样"从指名升级为一条可单独翻的开关 --------
    # 只有"缺字"这一维交给开关；"根本没被调用"/"有门禁退出码非零"/"反证夹具缺失"永远红。
    if [ "$STRICT" -eq 1 ]; then
        NO_EV_COUNT=$((GATE_TOTAL - EVIDENCE_OK))
        echo ""
        echo "################ 独立计数（--self-check-strict）：缺判据字样条数 vs 允许例外数 ################"
        echo "STRICT-COUNTS: 已跑条数 $GATE_TOTAL / 缺判据字样条数 $NO_EV_COUNT / 允许缺字条数 $ALLOW_NO_EVIDENCE"
        if [ "$ALLOW_NO_EVIDENCE" -ne 0 ]; then
            echo "警告：允许缺字条数已被 NIGHT1_ALLOW_NO_EVIDENCE 放宽为 $ALLOW_NO_EVIDENCE（默认 0）——非默认口径，已落证据日志留痕"
        fi
        STRICT_BAD=0
        if [ -n "$MISSING_RUN" ]; then STRICT_BAD=1; fi
        if [ -n "$STUB_ERR" ]; then STRICT_BAD=1; fi
        if [ "$RED" -ne 0 ]; then STRICT_BAD=1; fi
        if [ "$NO_EV_COUNT" -gt "$ALLOW_NO_EVIDENCE" ]; then
            echo "STRICT: FAIL 缺判据字样 $NO_EV_COUNT 条 > 允许 $ALLOW_NO_EVIDENCE 条（缺字清单：$NO_EVIDENCE_LIST）"
            STRICT_BAD=1
        fi
        if [ "$STRICT_BAD" -ne 0 ]; then
            echo "STRICT: FAIL 自证（严格档）未过：缺字计数 $NO_EV_COUNT / 允许 $ALLOW_NO_EVIDENCE；未调用=${MISSING_RUN:-无} / 红门=$RED"
            exit 1
        fi
        echo "STRICT: PASS 缺判据字样 $NO_EV_COUNT 条 ≤ 允许 $ALLOW_NO_EVIDENCE 条，且无未调用、无红门"
        exit 0
    fi
    if [ -n "$MISSING_RUN" ] || [ -n "$NO_EVIDENCE_LIST" ] || [ -n "$STUB_ERR" ] || [ "$RED" -ne 0 ]; then
        echo "SELFCHECK: FAIL 自证未过（缺字的关已在上文指名）"
        exit 1
    fi
    echo "SELFCHECK: PASS 25 条全部被调用且每条都产出了判据字样（有字样 $EVIDENCE_OK / 无字样 0）"
    echo "（本行是**自证自己的结论**，不是门禁判据：按 D-45 不打 RESULT: 字样）"
    exit 0
    }
    run_selfcheck | tee -a "$SELFCHECK_LOG"
    SELFCHECK_RC="${PIPESTATUS[0]}"
    echo ""
    echo "自证证据日志: $SELFCHECK_LOG（首行＝R1 批次坐标；R2 证据日志命名，不是 m-live-* 活日志）"
    exit "$SELFCHECK_RC"
fi

if [ "$RED" -ne 0 ]; then
    exit 1
fi
exit 0
