#!/usr/bin/env bash
# 员工 M（开发者1号 · 世界/地形线）跑批与取证脚本 —— 入库正本。
#
# 为什么要有这个文件（ADR-036.8 + 架构师 2026-10-05 松绑回执）：
#   1. 完全访问不覆盖工作目录之外的路径。副本工程以前放 D:/tj-batch-M，每次跑都要在仓库外写文件，
#      弹框是并行会话被卡住的最常见原因；现在一律收进仓库内 .batch/（已 gitignore）。
#   2. Unity 命令行又长又带空格，每次现拼既是打错字的主要来源，也是弹框的主要来源。
#      固化成一个文件后，验收方只需要复核这一个脚本 + 日志。
#
# 用法：
#   bash Tools/batch-verify-M.sh sync       # 纯 HEAD 铺底 + 只覆盖 M 线白名单（改过代码/配置后先跑这个）
#   bash Tools/batch-verify-M.sh warmup     # sync + TerrainPropShot（亮灯：出真图 + 体积与 md5）
#   bash Tools/batch-verify-M.sh terrain    # TerrainPropShot.RunSuite（地块摆放/纵深/取景/碰撞接线/合批）
#   bash Tools/batch-verify-M.sh props      # FrontEndShotProps.Capture（P-02 交互链 + 真图两态）
#   bash Tools/batch-verify-M.sh frontend   # FrontEndShot.Capture（主线六页，含世界页纵深与货郎）
#   bash Tools/batch-verify-M.sh logic      # 五道纯逻辑门禁（编译/SaveChecks/consistency/plot-overlap/seams）
#   bash Tools/batch-verify-M.sh all        # logic + terrain + props + frontend + shots
#   bash Tools/batch-verify-M.sh shots      # 只看本批出图的体积与 md5（不跑 Unity）
#   bash Tools/batch-verify-M.sh verify     # 只建/更新归因用的纯 HEAD worktree（.batch/verify-M）
#
# 三条硬规矩（ADR-035，本轮 M4 首轮 7 红里有 4 红就是没守它）：
#   1. UI 类夹具一律不带 -nographics（带了就出一整屏平色图，T055）
#   2. 自己调 EditorApplication.Exit 的跨帧夹具不带 -quit（带了日志会从 190KB 掉到 19KB）
#   3. 判据只认日志里的 "suite finished, 0 problem(s)" / "finished, 0 problem(s)"，退出码不算
set -u

# Unity 只认 Windows 形式的路径。用 /d/... 这种 MSYS 路径喂 -projectPath/-logFile，
# 进程会立刻 rc=1 退出且连日志都不生成（看着像"没跑"，其实是路径没被接受）。
UNITY="D:/ruangong/unity/Editor/6000.3.25f1/Editor/Unity.exe"
REPO="D:/qiqi_me/Eternal Jade Scroll"
PROJ="$REPO/.batch/M/GameClient/TwelveJade"
VERIFY="$REPO/.batch/verify-M"
LOGS="$REPO/Tools/logs"
SHOTS="$REPO/.batch/M/Tools/screenshots"

# 归因树 = 纯 HEAD；副本 = 纯 HEAD + 只覆盖下面这几个 M 线白名单路径。
# 为什么不直接镜像工作树：共享工作树里随时有别的线在飞（本轮实测被 FrontEndInventory /
# KarmaTable 的半成品打断过一次，Unity 在"别人的半成品"上编译失败，我的套件连跑都跑不起来）。
# "干净检出 + 我的白名单"反而更硬：它证明的就是这一笔提交出去之后的样子。
MINE=(
  "Assets/Resources/Config/town-map.json"
  "Assets/Resources/Config/building-plots.json"
  "Assets/Scripts/Core/TownMap.cs"
  "Assets/Scripts/Presentation/WorldPropLayer.cs"
  "Assets/Scripts/Presentation/FrontEndWorld.cs"
  # Q1.5 世界精修：reach 生产者要连 Core 与套件一起覆盖（跨域写入，报告里披露）
  "Assets/Scripts/Core/Quest.cs"
  # 2026-10-06 Q1.6 把 InventoryEconomySuite.cs 从这份清单里移出，理由是一条实测：
  # 我这边的三条 Unity 夹具都不调用它（调用方是 Editor/InventoryEconomyShot.cs），所以它留在
  # MINE 里只会做一件事——把别人**未提交**的在飞版本盖进我的副本树。04:36 那次 sync 就盖进了
  # 3号 正在写的 166 行，日志里还把它标成"[我的改动]"。我的改动进 HEAD 之后，白名单条目就该跟着退场。
  # P-Character（Gate Q1）白名单：主角形象与动作线
  "Assets/Scripts/Presentation/PuppetActor.cs"
  "Assets/Scripts/Presentation/CharacterActor.cs"
  "Assets/Scripts/Presentation/CharacterPreset.cs"
  "Assets/Scripts/Presentation/FrontEndCharacters.cs"
  "Assets/Editor/TerrainPropShot.cs"
  "Assets/Editor/FrontEndShotProps.cs"
  "Assets/Editor/FrontEndShot.cs"
  # D-01②（2026-10-07，员工 D 按任务书授权补录）：V/W 线新文件——工作树版 PuppetActor.cs 引用
  # PuppetRig/PuppetGait/PuppetMeshGraphic，缺这几件副本直接编译红（CS0246 ×6，A-06 实测）；
  # FootstepSynth/FrontEndFootsteps 是 W-C 交付件，不补则副本测的是旧树（T114 教训）。
  "Assets/Scripts/Presentation/PuppetRig.cs"
  "Assets/Scripts/Presentation/PuppetGait.cs"
  "Assets/Scripts/Presentation/PuppetMeshGraphic.cs"
  "Assets/Scripts/Presentation/FootstepSynth.cs"
  "Assets/Scripts/Presentation/FrontEndFootsteps.cs"
  "Assets/Scripts/Presentation/FrontEndSettings.cs"
)

sha() { git -C "$REPO" rev-parse --short HEAD; }

# ---- D-53：副本取证的两样凭据，固定成脚本输出而不是靠人记 ----
# ①树身份：跑之前先把归因树推到主库 HEAD（sync 里做），并在这里打一行"这一路的 HEAD"；
# ②覆盖清单归属：MINE 只是"我要盖哪些路径"，**它不能自证这些路径属于本线**。
#   Q1.6 实测：一个已进 HEAD 的条目没退场，sync 就把 3号 未提交的 166 行盖进了取证树，
#   日志还称它是"[我的改动]"。OWNED 是写域清单（派单里给的路径），盖之前逐个核。
OWNED=(
  "Assets/Resources/Config/town-map.json"
  "Assets/Resources/Config/building-plots.json"
  "Assets/Scripts/Core/TownMap.cs"
  "Assets/Scripts/Core/Quest.cs"
  "Assets/Scripts/Presentation/WorldPropLayer.cs"
  "Assets/Scripts/Presentation/FrontEndWorld.cs"
  "Assets/Scripts/Presentation/PuppetActor.cs"
  "Assets/Scripts/Presentation/CharacterActor.cs"
  "Assets/Scripts/Presentation/CharacterPreset.cs"
  "Assets/Scripts/Presentation/FrontEndCharacters.cs"
  "Assets/Editor/TerrainPropShot.cs"
  "Assets/Editor/FrontEndShotProps.cs"
  "Assets/Editor/FrontEndShot.cs"
  # D-01② 同批补 OWNED（MINE ⊆ OWNED 是 overlay_guard 的硬判据，两边必须同步）：
  # 这 5 件的写域归属 = V/W 线任务书白名单（Y-2 总白名单），由 D 线按 D-01② 代录进"已交付件"名单。
  "Assets/Scripts/Presentation/PuppetRig.cs"
  "Assets/Scripts/Presentation/PuppetGait.cs"
  "Assets/Scripts/Presentation/PuppetMeshGraphic.cs"
  "Assets/Scripts/Presentation/FootstepSynth.cs"
  "Assets/Scripts/Presentation/FrontEndFootsteps.cs"
  "Assets/Scripts/Presentation/FrontEndSettings.cs"
)
owned() { local o; for o in "${OWNED[@]}"; do [ "$o" = "$1" ] && return 0; done; return 1; }

# 返回 0 = 白名单全部落在本线写域。EXTRA_MINE 只给 check 演练用。
overlay_guard() {
  local entry bad=0
  for entry in "${MINE[@]}" ${EXTRA_MINE:-}; do
    owned "$entry" || { echo "[拒盖] $entry 不在本线写域 OWNED 里 ⇒ 盖进去的可能是别人的版本"; bad=1; }
  done
  [ "$bad" = 0 ] && echo "[写域核过] 白名单 ${#MINE[@]} 个路径全部落在 OWNED（${#OWNED[@]} 条）里"
  return "$bad"
}

# 逐件 mtime：谁在写这一路，mtime 比名字可靠。
overlay_manifest() {
  local f dirty
  echo "[覆盖清单] 逐件（工作树 mtime / 是否与 HEAD 不同）："
  for f in "${MINE[@]}"; do
    [ -e "$REPO/GameClient/TwelveJade/$f" ] || { echo "    缺席  $f"; continue; }
    dirty=N
    [ -n "$(git -C "$REPO" status --porcelain -- "GameClient/TwelveJade/$f")" ] && dirty=Y
    echo "    $(stat -c '%y' "$REPO/GameClient/TwelveJade/$f" 2>/dev/null | cut -c1-19)  脏=$dirty  $f"
  done
}

# check：D-53 要求的"能证明多带一个别人的文件会中止"。正反两路都跑，只跑绿的那路不算。
check() {
  echo "=== check：副本取证自证（D-53）==="
  echo "[HEAD] 主库 $(sha)｜归因树 $(git -C "$VERIFY" rev-parse --short HEAD)"
  if overlay_guard; then
    echo "SELFTEST: OK 正常白名单被放行"
  else
    echo "SELFTEST: FAIL 正常白名单竟然被拒（OWNED 与 MINE 不同步）"; return 1
  fi
  EXTRA_MINE="Assets/Scripts/Economy/InventoryEconomySuite.cs"   # 3号 名下的真实文件，不是编出来的靶子
  if overlay_guard; then
    echo "SELFTEST: FAIL 多带一个别人名下的文件竟然没被拦"; return 1
  fi
  echo "SELFTEST: OK 多带一个别人名下的文件被拦下（拦的正是 Q1.6 那次盖进取证树的那一路）"
  return 0
}

# T114：内容级抽查。robocopy 的第三个参数**只认文件名模式**——传目录名会"扫到目录、搬 0 个文件"
# 且 rc 仍是成功值，于是"副本树 0 problem"验的其实是纯 HEAD，我的改动一行都没进去（3号 实测撞出）。
# 光看 rc 与"文件存在"都抓不到这件事，所以逐文件比 md5：不一致就响亮失败，不往下跑。
spotcheck() {
  local miss=0 f a b
  for f in "${MINE[@]}"; do
    [ -e "$REPO/GameClient/TwelveJade/$f" ] || continue
    a=$(md5sum "$REPO/GameClient/TwelveJade/$f" | cut -c1-12)
    if [ ! -f "$PROJ/$f" ]; then echo "[抽查 缺文件] $f"; miss=$((miss+1)); continue; fi
    b=$(md5sum "$PROJ/$f" | cut -c1-12)
    [ "$a" = "$b" ] || { echo "[抽查 不一致] $f 主库=$a 副本=$b"; miss=$((miss+1)); }
  done
  # 数量抽查：副本的 .cs 文件数不该少于归因树（少一批=整目录没搬进去）
  local ncs_c ncs_v
  ncs_c=$(find "$PROJ/Assets/Scripts" -name '*.cs' 2>/dev/null | wc -l)
  ncs_v=$(find "$VERIFY/GameClient/TwelveJade/Assets/Scripts" -name '*.cs' 2>/dev/null | wc -l)
  [ "$ncs_c" -ge "$ncs_v" ] || { echo "[抽查 数量] 副本 .cs $ncs_c < 归因树 $ncs_v —— 有目录没搬进去"; miss=$((miss+1)); }
  if [ "$miss" -gt 0 ]; then
    echo "[抽查] 副本与主库不一致 $miss 处 —— **不要相信这一轮的 0 problem**（副本验的不是你改的那棵树）"
    return 3
  fi
  echo "[抽查] 白名单 $(for f in "${MINE[@]}"; do [ -e "$REPO/GameClient/TwelveJade/$f" ] && printf x; done | wc -c) 个文件逐个 md5 一致；.cs 数量 副本 $ncs_c ≥ 归因树 $ncs_v"
}

verify() {
  # 归因 worktree：架构师点名的形式是 git worktree add --detach .batch/verify-M HEAD。
  # 注意 git 的 -C 参数在这里必须用 Windows 路径——本脚本开头 export 了 MSYS2_ARG_CONV_EXCL，
  # /d/... 不会被转换，git 会直接 fatal: cannot change to '/d/...'（本轮实测踩过）。
  if [ ! -d "$VERIFY" ]; then
    git -C "$REPO" worktree add --detach "$VERIFY" HEAD >/dev/null 2>&1 \
      || { echo "[verify] 仓库内建 worktree 被 git 拒了，退回 D:/tj-verify-M（会弹一次框）"; \
           git -C "$REPO" worktree add --detach "D:/tj-verify-M" HEAD >/dev/null 2>&1; \
           VERIFY="D:/tj-verify-M"; }
  fi
  git -C "$VERIFY" checkout --detach "$(sha)" >/dev/null 2>&1
  git -C "$VERIFY" status --porcelain | head -3 | sed 's/^/[verify-tree 脏文件] /'
  echo "[verify] 归因树 $VERIFY @ $(git -C "$VERIFY" rev-parse --short HEAD)"
}

sync() {
  export MSYS2_ARG_CONV_EXCL="*"      # 否则 MSYS 会把 /MIR、/XD 这类 robocopy 开关当路径改写
  verify
  # D-53 的第二样凭据：先核归属，再动手盖。不通过就直接中止，不"盖了再说"。
  overlay_guard || { echo "[sync] 中止（D-53）：白名单里有本线不拥有的路径。"
    echo "       要么把它从 MINE 删掉（我的改动进 HEAD 后条目就该退场），"
    echo "       要么在 OWNED 里写明本线为什么拥有它——写在报告里不算，脚本要能自己判。"; return 3; }
  mkdir -p "$PROJ"
  for d in Assets ProjectSettings Packages; do
    # 单项 /XD 就够；一次传 ArtSource Library Temp Logs obj 多项实测会 rc=16（参数被拆坏）。
    robocopy "$VERIFY/GameClient/TwelveJade/$d" "$PROJ/$d" \
      /MIR /XD ArtSource Library Temp Logs /NFL /NDL /NJH /NJS >/dev/null
  done
  # 覆盖必须放在 /MIR 之后：/MIR 会把"源里没有"的文件从目标删掉，顺序反了就把自己的改动删干净了。
  for f in "${MINE[@]}"; do
    [ -e "$REPO/GameClient/TwelveJade/$f" ] || continue
    robocopy "$(dirname "$REPO/GameClient/TwelveJade/$f")" "$(dirname "$PROJ/$f")" \
      "$(basename "$f")*" /E /NFL /NDL /NJH /NJS >/dev/null
  done
  echo "[sync] 副本 = 纯 HEAD($(git -C "$VERIFY" rev-parse --short HEAD)) + M 线白名单覆盖"
  # 逐件 mtime 固定输出（D-53 第二样凭据）。上一版这里只在"有脏版"时才打一行，
  # 而"谁的版本"这件事必须按件看 mtime 才能判，所以现在是每条白名单都打一行。
  # 判归属看 mtime，别按文件名猜——2026-10-06 那句"[我的改动]"就是把名字当成了所有权。
  overlay_manifest
  spotcheck || return 3
}

warm() {
  # 一次性迁移：老副本 D:/tj-batch-M 的 Library 是热的（615MB，首次导入要几分钟）。
  # .batch/M 没有 Library 时搬过来，省掉一次全量 reimport；没有老副本就直接让 Unity 自己导。
  if [ -d "$PROJ/Library" ]; then echo "[warm] .batch/M/Library 已存在，跳过"; return; fi
  if [ -d "D:/tj-batch-M/GameClient/TwelveJade/Library" ]; then
    export MSYS2_ARG_CONV_EXCL="*"
    robocopy "D:/tj-batch-M/GameClient/TwelveJade/Library" "$PROJ/Library" \
      /E /NFL /NDL /NJH /NJS >/dev/null
    echo "[warm] 已从 D:/tj-batch-M 搬来 Library（rc<8 即成功）"
  else
    echo "[warm] 没有可搬的 Library，Unity 首次运行会自己全量导入（耐心等）"
  fi
}

# run <日志名> <quit|none> <executeMethod 全限定名>
run() {
  local log="$1" quit="$2" method="$3"
  local out="$LOGS/$log.log"
  mkdir -p "$LOGS"
  # D-01③：看守进程留痕——启动前记录现存 Unity PID，收工后对比打印。
  # W-14 的教训：同机并发会话的 UnityAutoQuitter 会杀掉正在跑的批处理（三次"退出码 0 但无判决行"），
  # 谁杀的没有痕迹。这里把"跑之前有谁"钉进日志，收工时多了少了谁都能对出来。
  local pids_before
  pids_before=$(powershell -NoProfile -Command "Get-CimInstance Win32_Process -Filter \"Name='Unity.exe'\" | ForEach-Object { \$_.ProcessId }" 2>/dev/null | tr '\n' ' ')
  echo "[run] $method → $out"
  echo "[watch] 启动前现存 Unity PID: ${pids_before:-无}"
  if [ "$quit" = "quit" ]; then
    "$UNITY" -batchmode -projectPath "$PROJ" -executeMethod "$method" -quit -logFile "$out" >/dev/null 2>&1
  else
    "$UNITY" -batchmode -projectPath "$PROJ" -executeMethod "$method" -logFile "$out" >/dev/null 2>&1
  fi
  local rc=$?
  local pids_after
  pids_after=$(powershell -NoProfile -Command "Get-CimInstance Win32_Process -Filter \"Name='Unity.exe'\" | ForEach-Object { \$_.ProcessId }" 2>/dev/null | tr '\n' ' ')
  echo "[watch] 收工时现存 Unity PID: ${pids_after:-无}（启动前: ${pids_before:-无}）"
  echo "[exit] rc=$rc （只作参考，判据看下面几行）"
  grep -E "suite finished|finished, [0-9]+ problem|error CS" "$out" | tail -6
  grep "断言失败" "$out" | head -8
  grep -cE "\] ok: " "$out" | sed 's/^/[ok 行数] /'
}

logic() {
  echo "=== 五道纯逻辑门禁（在主库跑；它们不碰工程锁）==="
  dotnet build Tools/UnityCompileChecks --no-incremental -v q --nologo 2>&1 | grep -E "警告|错误|error"
  dotnet run --project Tools/SaveChecks/SaveChecks.csproj --no-restore 2>&1 | tail -2
  python -X utf8 Tools/check-town-config-consistency.py 2>&1 | tail -2
  python -X utf8 Tools/PropChecks/check-plot-overlap.py 2>&1 | tail -2
  python -X utf8 Tools/check-world-seams.py --full-grid 2>&1 | tail -2
  # T061：--no-restore 在从未还原过的干净 worktree 里必报 NETSDK1004，文案像"缺源文件"。
  echo "[note] 若上面 SaveChecks 报 NETSDK1004，先去掉 --no-restore 跑一次建立 obj/ 再复跑（T061）"
}

shots() {
  echo "[shots] 本批出图体积与 md5（同批必须互不相同；同帧两出口同名重复是正常的，按相位判）"
  [ -d "$SHOTS" ] || { echo "[shots] 没有 $SHOTS，先跑 warmup/terrain"; return; }
  local uniq total
  for f in "$SHOTS"/i-*.png; do
    [ -f "$f" ] || continue
    printf "%10s  %s  %s\n" "$(stat -c %s "$f")" "$(md5sum "$f" | cut -c1-12)" "$(basename "$f")"
  done
  uniq=$(md5sum "$SHOTS"/i-*.png 2>/dev/null | cut -c1-32 | sort -u | wc -l)
  total=$(ls -1 "$SHOTS"/i-*.png 2>/dev/null | wc -l)
  echo "[唯一 md5 数] $uniq / $total 张"
}

# T114 的自检：不依赖 git 状态，直接验"robocopy 第三参照文件名模式搬得到东西"这件事本身。
# 3号 撞的坑是传目录名 → 扫到目录、搬 0 个文件、rc 仍是成功值。这里两种都试一次，
# 用**实际文件数**而不是 rc 判定，所以哪天有人把 MINE 写成目录条目，这条会先红。
selftest() {
  local root="$REPO/.batch/selftest-sync" run src dst n
  run="run-$(date +%s)-$$"          # 每次一个新 run 目录：不用删树（rm -rf 是三条底线之一）
  src="$root/$run/src"; dst="$root/$run/dst1"
  mkdir -p "$src/sub" "$dst"
  echo "sentinel" > "$src/one.txt"
  echo "sentinel" > "$src/sub/two.txt"
  export MSYS2_ARG_CONV_EXCL="*"
  # ① 文件模式（正确用法）：必须搬到
  robocopy "$src" "$dst" "one.txt*" /E /NFL /NDL /NJH /NJS >/dev/null
  n=$(find "$dst" -name 'one.txt' | wc -l)
  [ "$n" = "1" ] || { echo "[selftest] 文件模式没搬到货（$n）——sync 的覆盖逻辑不可信"; return 3; }
  # ② 目录名当第三参（3号 的错法）：应当搬不到，用来证明"为什么 MINE 里只能列文件"
  dst="$root/$run/dst2"; mkdir -p "$dst"
  robocopy "$src" "$dst" "sub" /E /NFL /NDL /NJH /NJS >/dev/null
  n=$(find "$dst" -name 'two.txt' 2>/dev/null | wc -l)
  echo "[selftest] 文件模式搬到 1 个 ✔；目录名当模式搬到 $n 个（0 就是 3号 撞的那堵墙）"
  echo "[selftest] 现场留在 $root/$run（.batch 已 gitignore，攒几次手工清一回即可）"
}

case "${1:-all}" in
  selftest) selftest ;;
  check) check ;;
  sync) sync ;;
  verify) verify ;;
  warm) warm ;;
  # 日志一律写 m-live-<夹具>.log：本轮归档过的证据（m-collider-round1-7red.log /
  # m-props.log / m-h7b-props.log …）不能被下一轮的复跑就地覆盖——
  # 我自己就踩过一次：terrain 的默认名与首轮 7 红的归档名相同，复跑把原件吃掉了。
  # 要留某一轮：cp Tools/logs/m-live-terrain.log Tools/logs/m-<说明>-<短 sha>.log
  warmup) sync; warm; run m-live-warmup none TwelveJade.Editor.TerrainPropShot.RunSuite; shots ;;
  terrain) run m-live-terrain none TwelveJade.Editor.TerrainPropShot.RunSuite ;;
  props) run m-live-props none TwelveJade.Editor.FrontEndShotProps.Capture ;;
  frontend) run m-live-frontend none TwelveJade.Editor.FrontEndShot.Capture ;;
  logic) logic ;;
  shots) shots ;;
  all) sync; warm; logic
       run m-live-terrain none TwelveJade.Editor.TerrainPropShot.RunSuite
       run m-live-props none TwelveJade.Editor.FrontEndShotProps.Capture
       run m-live-frontend none TwelveJade.Editor.FrontEndShot.Capture
       shots ;;
  *) echo "unknown target: ${1:-}"; exit 2 ;;
esac
