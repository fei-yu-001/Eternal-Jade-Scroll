#!/usr/bin/env bash
# 员工 N（N-Combat-Feel）取证脚本 —— ADR-036.8 第②条：把重复的长命令固化成一个文件，
# 免得每次手敲一条又长又带空格的 Unity 命令行（也是弹框与打错字的主要来源）。
#
# 本文件是**入库正本**（架构师 2026-10-05 松绑回执里点名的路径）。`.batch/` 整个目录被 gitignore，
# 所以下一轮拿到它的唯一途径就是这里；`.batch/verify-N.sh` 已改成一行转发，两份不会各说各话。
#
# 用法：
#   bash Tools/batch-verify-N.sh sync            # 主库 → .batch/N 同步（改过代码/配置后必须先跑）
#   bash Tools/batch-verify-N.sh warmup          # 亮灯测试：出真图 + 打印体积与 md5
#   bash Tools/batch-verify-N.sh engine          # CombatEngineShot.RunSuite
#   bash Tools/batch-verify-N.sh shot            # CombatShot.Capture（野猪 profile2 之后的回归）
#   bash Tools/batch-verify-N.sh props           # FrontEndShotProps.Capture（只读，别人的夹具）
#   bash Tools/batch-verify-N.sh terrain         # TerrainPropShot.RunSuite（只读）
#   bash Tools/batch-verify-N.sh economy         # InventoryEconomyShot.RunSuite（只读，带 -quit）
#   bash Tools/batch-verify-N.sh npc             # NpcDialogueShot.RunSuite（只读，带 -quit）
#   bash Tools/batch-verify-N.sh all             # engine + shot + props
#   bash Tools/batch-verify-N.sh selftest        # 自证 robocopy 覆盖那一步（不跑 Unity，现场留 .batch/）
#
# 三条硬规矩（ADR-035）：
#   1. UI 类夹具一律不带 -nographics（带了就出纯平色图，T055）
#   2. 自己调 EditorApplication.Exit 的跨帧夹具不带 -quit（带了日志会从 100KB 掉到 19KB）
#   3. 判据只认日志里的 "suite finished, 0 problem(s)" / "finished, 0 problem(s)"，退出码不算
set -u
# 注意：Unity 只认 Windows 形式的路径。用 /d/... 这种 MSYS 路径喂 -projectPath/-logFile，
# 进程会立刻 rc=1 退出且**连日志都不生成**（看起来像"没跑"，实际是路径没被接受）。
UNITY="D:/ruangong/unity/Editor/6000.3.25f1/Editor/Unity.exe"
REPO="D:/qiqi_me/Eternal Jade Scroll"
PROJ="$REPO/.batch/N"
LOGS="$REPO/Tools/logs"
mkdir -p "/d/qiqi_me/Eternal Jade Scroll/Tools/logs"

# 副本工程的构成 = 纯 HEAD（.batch/verify-N 这个 detached worktree）+ 只覆盖 N 线白名单那几个文件。
# 为什么不直接镜像工作树：本轮工作树被别的员工改到编不过（FrontEndTrade.cs 引用还不存在的
# KarmaTable、NpcDialogueChecks.cs 引用已被拆掉的 MemoryEcho.Discount），镜像过来只会让
# Unity 在"别人的半成品"上编译失败，我的套件连跑都跑不起来（18:25 实测 rc=1、日志里没有一行断言）。
# 用"干净检出 + 我的白名单"反而更硬：它证明的就是我这一笔提交出去之后的样子。
  # 归因树：架构师 2026-10-05 点名的形式是 git worktree add --detach .batch/verify-N HEAD
  # （本轮它建在 6a128a4，用完 checkout -- + 删掉未跟踪文件还原，实测干净）
  HEADTREE="$REPO/.batch/verify-N/GameClient/TwelveJade"
MINE=(
  "Assets/Resources/Config/enemies.json"
  "Assets/Scripts/Combat"
  "Assets/Scripts/Presentation/FrontEndCombat.cs"
  "Assets/Editor/CombatEngineShot.cs"
  "Assets/Editor/CombatShot.cs"
)

# ==== D-53（2026-10-06 升为全员口径，ADR-035）：跑夹具之前先交两样 ====
# ①副本树身份 = 主库 HEAD 的 sha；②覆盖清单逐件来历 = 本线"脏版覆盖"的每一条列出相对路径与来历标记。
# 为什么"只给数字"不够：共享工作树里 git 说不出未跟踪文件归谁，光看条数判不了
# "有没有不属于该线的文件"，所以必须逐件点名。
# 来历只准问 git status，不许拿 blob 哈希与工作树文件直接比：
# 工作树是 CRLF、blob 是 LF（core.autocrlf 提交时换行），直接比会造出一串假阳性名单
#（O 线 3cae5dd 实测：13 件明明干净的文件被判成脏版覆盖，名单一长这条闸就废了）。
# -uall 是必需的：不加则目录折叠，一个目录只出一行，等于没点名。
tree_identity() {
  local want tree
  want="$(git -C "$REPO" rev-parse HEAD)"
  tree="$(git -C "$REPO/.batch/verify-N" rev-parse HEAD 2>/dev/null || echo none)"
  echo "[树身份] 主库 HEAD=$want"
  echo "[树身份] 归因树=$tree"
  if [ "$want" != "$tree" ]; then
    echo "[树身份] **归因树没跟上主库 ⇒ 这一趟验的不是 HEAD**"
    return 1
  fi
  return 0
}

# MINE 条目退场：自己的改动进了 HEAD 之后，这条覆盖就成了 no-op（搬的本来就是要搬的那份）。
# 只报告不自动删行——白名单同时是本线的**写域声明**，删条目是每批的人为决定，不该由脚本替人做。
mine_retirement() {
  local total=0 noop=0 f st
  for f in "${MINE[@]}"; do
    total=$((total + 1))
    st=$(git -C "$REPO" status --porcelain -uall -- "GameClient/TwelveJade/$f" 2>/dev/null)
    if [ -z "$st" ]; then
      noop=$((noop + 1))
      echo "[退场候选] $f：工作树与 HEAD 无差异 ⇒ 这条覆盖现在是 no-op，本批收口后可撤"
    else
      echo "[在途] $f：$(echo "$st" | sed 's/^/[覆盖清单] /' | tr '\n' ' ')"
    fi
  done
  echo "[退场报告] $total 条里 $noop 条已与 HEAD 相同（只报告，删条目由人定）"
}

sync() {
  export MSYS2_ARG_CONV_EXCL="*"
  mkdir -p "/d/qiqi_me/Eternal Jade Scroll/.batch/N"
  # 归因树必须跟着主树走：它建的时候是某个 sha，别人（架构师/1号/3号）一直在往 main 上落笔，
  # 停在旧 sha 就等于拿"上辈子的干净检出"当基线。worktree 之间共享 object 库，直接 checkout 即可。
  local want; want="$(git -C "$REPO" rev-parse HEAD)"
  if [ "$(git -C "$REPO/.batch/verify-N" rev-parse HEAD)" != "$want" ]; then
    git -C "$REPO/.batch/verify-N" checkout --quiet --detach "$want" &&
      echo "[sync] 归因树已推进到 $want"
  fi
  for d in Assets ProjectSettings Packages; do
    robocopy "$HEADTREE/$d" "$PROJ/$d" /MIR /XD ArtSource Library Temp Logs /NFL /NDL /NJH /NJS >/dev/null
  done
  # 覆盖必须在镜像之后：/MIR 会把"源里没有"的文件从目标删掉，顺序反了就把自己的改动删干净了。
  for f in "${MINE[@]}"; do
    robocopy "$(dirname "$REPO/GameClient/TwelveJade/$f")" "$(dirname "$PROJ/$f")" \
      "$(basename "$f")*" /E /NFL /NDL /NJH /NJS >/dev/null
  done
  echo "[sync] 副本 = 纯 HEAD($(git -C "$REPO/.batch/verify-N" rev-parse --short HEAD)) + N 线白名单覆盖"
  git -C "$REPO" status --short -- ${MINE[@]/#/"GameClient/TwelveJade/"} | sed 's/^/[我的改动] /'
  tree_identity || exit 1
  mine_retirement
  verify_overlay
}

# T114（交接包 L-3）：robocopy 的第三个参数只认**文件名模式**，目录条目可能扫到 N 个目录、
# 0 个文件而静默空转 ⇒ 副本上的"0 problem"其实是 HEAD 的绿。3号 在 O 线上实测抓到。
# 两道检查，都不写死探针（写死的符号会随轮次腐烂）：
#   ① hash 硬闸：白名单逐文件比源树与副本的 sha1，不一致就**中止**（不中止就会拿假绿去报数）；
#   ② 符号抽查：从工作树 diff 里自动取每个改动文件"最长的那条新增行"，数它在副本里命中几次。
# 两道结果都打到 stdout，判据行旁边看得见——"0 problem" 只有在副本确实携带了被测改动时才有意义。
verify_overlay() {
  local src_root="$REPO/GameClient/TwelveJade" rel files=() f a b bad=0 probes=0 missed=0
  for f in "${MINE[@]}"; do
    if [ -d "$src_root/$f" ]; then
      while IFS= read -r rel; do files+=("$rel"); done \
        < <(cd "$src_root" && find "$f" -type f ! -name "*.meta" | sort)
    else
      files+=("$f")
    fi
  done
  for rel in "${files[@]}"; do
    a="$src_root/$rel"; b="$PROJ/$rel"
    [ -f "$a" ] || continue
    if [ ! -f "$b" ]; then echo "[抽查] 副本里缺文件 $rel"; bad=$((bad + 1)); continue; fi
    if [ "$(sha1sum < "$a")" != "$(sha1sum < "$b")" ]; then
      echo "[抽查] **内容不一致** $rel（源树与副本不是同一份东西）"; bad=$((bad + 1))
    fi
  done
  for f in "${MINE[@]}"; do
    for rel in $(git -C "$REPO" diff --name-only -- "GameClient/TwelveJade/$f" 2>/dev/null); do
      local inner="${rel#GameClient/TwelveJade/}" token hits
      token=$(git -C "$REPO" diff -- "$rel" | grep '^+' | grep -v '^+++' | sed 's/^+//' | tr -d '\r' |
        awk '{ if (length($0) > n) { n = length($0); line = $0 } } END { print substr(line, 1, 100) }')
      [ -z "$token" ] && continue
      probes=$((probes + 1))
      hits=$(grep -acF "$token" "$PROJ/$inner" 2>/dev/null || echo 0)
      if [ "${hits:-0}" -gt 0 ]; then
        echo "[抽查] 命中 $hits · ${inner##*/} 的新增符号行"
      else
        echo "[抽查] **副本里没有** ${inner##*/} 的那一行 ⇒ 这一棵树验的不是我的改动"; missed=$((missed + 1))
      fi
    done
  done
  echo "[抽查] hash 比对 ${#files[@]} 个文件（不一致 $bad）· 符号探针 $probes 条（落空 $missed）"
  if [ "$bad" -gt 0 ] || [ "$missed" -gt 0 ]; then
    echo "[SELFTEST] **中止：副本没有携带本次改动，判据行不可信（本行是自证，不是判据）**"
    exit 1
  fi
}

# run <日志名> <是否带 -quit(quit|none)> <executeMethod 全限定名>
run() {
  local log="$1" quit="$2" method="$3"
  # D-53 + 5f55d57 的教训：sync 只在开头核一次不够——主库每几分钟动一次，
  # sync 完 HEAD 就可能已经走掉，那一趟夹具实际跑在旧树上，而日志上的 0 problem(s)
  # 与"验的是 HEAD"长得一模一样。所以**每次起 Unity 之前再核一次身份**，不符就直接停，连 Unity 都不起。
  if ! tree_identity; then
    echo "[run] 停：归因树 sha ≠ 主库 HEAD ⇒ 这一趟验的不是 HEAD（先跑 sync 复位再跑）"
    return 1
  fi
  # T114：engine/shot 这些目标单独跑时不会先 sync，副本可能是上一轮的陈旧状态。
  # 起 Unity 之前先做一遍内容级抽查（13 个 sha1 + 几条 grep，秒级），不一致就直接中止——
  # 让假绿死在起跑线上，而不是死在"别人复跑时才发现副本里根本没有那行"。
  verify_overlay
  local out="$LOGS/$log.log"
  echo "[run] $method → $out"
  if [ "$quit" = "quit" ]; then
    "$UNITY" -batchmode -projectPath "$PROJ" -executeMethod "$method" -quit -logFile "$out" >/dev/null 2>&1
  else
    "$UNITY" -batchmode -projectPath "$PROJ" -executeMethod "$method" -logFile "$out" >/dev/null 2>&1
  fi
  echo "[exit] rc=$? （只作参考，判据看下面两行）"
  # D-45 字样独占：判据行只能由真门禁在真判定之后打印，**日志回放必须换前缀**。
  # 这里逐字回放是为了让跑批的人不用再去开日志，但前缀写清楚"这是回放"，
  # 免得有人 grep stdout 时把一次旧回放当成刚跑出来的判据。
  grep -E "suite finished|finished, [0-9]+ problem|断言计数|error CS" "$out" | tail -6 |
    sed "s|^|[回放←$log.log] |"
  grep -cE "^\[.*\] ok: |Props\] ok: |Shot\] ok: " "$out" | sed 's/^/[ok 行数] /'
}

shots() {
  echo "[shots] 战斗页出图体积与 md5（同批必须互不相同，且 >100KB）"
  for f in "/d/qiqi_me/Eternal Jade Scroll/Tools/screenshots"/k-combat-*.png; do
    [ -f "$f" ] || continue
    printf "%9s  %s  %s\n" "$(stat -c %s "$f")" "$(md5sum "$f" | cut -c1-12)" "$(basename "$f")"
  done
  uniq=$(for f in "/d/qiqi_me/Eternal Jade Scroll/Tools/screenshots"/k-combat-*.png; do
    [ -f "$f" ] && md5sum "$f" | cut -c1-32; done | sort -u | wc -l)
  total=$(ls "/d/qiqi_me/Eternal Jade Scroll/Tools/screenshots"/k-combat-*.png 2>/dev/null | wc -l)
  echo "[唯一 md5 数] $uniq / $total 张"
}

# T114 自检（语义与 batch-verify-M.sh 的 selftest 一致）：不依赖 git 状态，直接验"robocopy
# 第三参照文件名模式搬得到东西"这件事本身。3号 撞的坑是传目录名 → 扫到目录、搬 0 个文件、
# rc 仍是成功值。两种都试一次，用**实际文件数**而不是 rc 判定，所以哪天有人把 MINE 写成
# 目录条目，这条会先红。现场留在 .batch/ 下（已 gitignore），不删树。
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
  [ "$n" = "1" ] || {
    echo "[selftest] **FAIL** ①文件模式：期望搬到 1 个 one.txt，实测 $n 个 ⇒ sync 的覆盖逻辑不可信（副本会静默停在纯 HEAD）"
    return 3
  }
  # ② 目录名当第三参（3号 的错法）：必须搬不到，用来证明"为什么 MINE 里只能列文件"。
  # 这一条曾经只打印不断言——那等于把要防的事放过去了：哪天 robocopy 真按目录名搬了文件，
  # n>0 而 selftest 依旧 rc=0。所以现在与①同规格：n 必须 = 0，否则红。
  dst="$root/$run/dst2"; mkdir -p "$dst"
  robocopy "$src" "$dst" "sub" /E /NFL /NDL /NJH /NJS >/dev/null
  n=$(find "$dst" -name 'two.txt' 2>/dev/null | wc -l)
  [ "$n" = "0" ] || {
    echo "[selftest] **FAIL** ②目录名当第三参：期望搬到 0 个 two.txt，实测 $n 个 ⇒ 目录条目被当成模式生效了，"
    echo "[selftest]         MINE 里列目录将不再静默（会真的搬东西），结论的前提变了，先查清再放行"
    return 3
  }
  echo "[selftest] OK ①文件模式搬到 1 个；②目录名当模式搬到 $n 个（0 = 3号 撞的那堵墙）——两条都是硬断言"
  echo "[selftest] 现场留在 $root/$run（.batch 已 gitignore，攒几次手工清一回即可）"
}

case "${1:-all}" in
  sync) sync ;;
  selftest) selftest ;;
  check) verify_overlay ;;
  warmup) sync; run n-warmup none TwelveJade.Editor.CombatEngineShot.RunSuite; shots ;;
  engine) run n-engine none TwelveJade.Editor.CombatEngineShot.RunSuite; shots ;;
  shot) run n-combatshot none TwelveJade.Editor.CombatShot.Capture ;;
  props) run n-props none TwelveJade.Editor.FrontEndShotProps.Capture ;;
  terrain) run n-terrain none TwelveJade.Editor.TerrainPropShot.RunSuite ;;
  economy) run n-economy quit TwelveJade.Editor.InventoryEconomyShot.RunSuite ;;
  npc) run n-npc quit TwelveJade.Editor.NpcDialogueShot.RunSuite ;;
  frontend) run n-frontend none TwelveJade.Editor.FrontEndShot.Capture ;;
  all) sync
       run n-engine none TwelveJade.Editor.CombatEngineShot.RunSuite; shots
       run n-combatshot none TwelveJade.Editor.CombatShot.Capture
       run n-props none TwelveJade.Editor.FrontEndShotProps.Capture ;;
  *) echo "unknown target: ${1:-}"; exit 2 ;;
esac
