#!/usr/bin/env bash
# 开发者3号（经济/生活线）取证脚本 —— ADR-036.8 第②条：把重复的长命令固化成一个文件。
# 本文件是**入库正本**（架构师 2026-10-05 松绑回执点名的路径）；`.batch/` 整个目录被 gitignore，
# 下一轮拿到这套流程的唯一途径就是这里。
#
# 用法：
#   bash Tools/batch-verify-O.sh sync        # 副本 = 纯 HEAD + 本线白名单覆盖（归因用，构成自证）
#   bash Tools/batch-verify-O.sh syncmerged  # 副本 = 整个工作树镜像（合并态七关取证用）
#   bash Tools/batch-verify-O.sh gates       # 三道不依赖 Unity 的门禁（编译 / SaveChecks / 配置一致性）
#   bash Tools/batch-verify-O.sh economy     # InventoryEconomyShot.RunSuite（本线，**带 -quit**）
#   bash Tools/batch-verify-O.sh npc         # NpcDialogueShot.RunSuite（本线，**带 -quit**）
#   bash Tools/batch-verify-O.sh frontend    # FrontEndShot.Capture（只读，别人的抽帧夹具）
#   bash Tools/batch-verify-O.sh engine      # CombatEngineShot.RunSuite（只读，N 域）
#   bash Tools/batch-verify-O.sh terrain     # TerrainPropShot.RunSuite（只读，M 域）
#   bash Tools/batch-verify-O.sh all         # sync + gates + economy + npc
#
# 四条硬规矩（ADR-035 / 本轮实测）：
#   1. UI 类夹具一律**不带 -nographics**（带了出平色图，T055）
#   2. 本线两套夹具是同步的（跑完不自己 Exit）⇒ **必须带 -quit**；
#      而 FrontEndShot / CombatEngineShot / TerrainPropShot 自己调 EditorApplication.Exit ⇒ **不带 -quit**
#   3. 判据只认日志里的 "suite finished, N problem(s)" / "All save checks passed" / "RESULT: PASS"，
#      退出码不算证据（把 rc=0 当日志原文是本轮明令禁止的）
#   4. Unity 只认 Windows 形式的路径：喂 /d/... 会立刻 rc=1 且**连日志都不生成**
set -u

UNITY="D:/ruangong/unity/Editor/6000.3.25f1/Editor/Unity.exe"
# 两套路径写法各给谁用，是本轮踩出来的坑：
#   * 外部 Windows 程序（git / robocopy / Unity / dotnet）只认 D:/... —— 而且 sync() 里
#     export 了 MSYS2_ARG_CONV_EXCL="*"（robocopy 的 /XD 需要它），MSYS 就**不再自动换算路径**，
#     把 /d/... 递给 git 会得到 "fatal: not a git repository"（实测）。
#   * bash 内建与 coreutils（cd / mkdir / stat / md5sum / tee）只认 /d/...
REPO_WIN="D:/qiqi_me/Eternal Jade Scroll"
REPO_MSYS="/d/qiqi_me/Eternal Jade Scroll"
PROJ="$REPO_WIN/.batch/O/GameClient/TwelveJade"
HEADTREE="$REPO_WIN/.batch/verify-O/GameClient/TwelveJade"
LOGS="$REPO_MSYS/Tools/logs"

# 本线白名单（§4 隔离表 O 列 + Q3 本轮新开放的存档双文件）。
# 两类路径：Assets/... 相对 Unity 工程根；Tools/... 与 GameServer/... 相对仓库根。
MINE=(
  "Assets/Resources/Config/items.json"
  "Assets/Resources/Config/merchants.json"
  "Assets/Resources/Config/quests.json"
  "Assets/Resources/Config/dialogues.json"
  "Assets/Resources/Config/npcs.json"
  "Assets/Resources/Config/npc-schedules.json"
  "Assets/Resources/Config/recipes.json"
  "Assets/Resources/Config/karma.json"
  # H-3 图标族：新出的 icon-*.png 要进副本树让 Unity 生成 .meta（副本导入后收回主树提交）。
  "Assets/Resources/Art/Items"
  "Assets/Scripts/Core/DialogueTree.cs"
  "Assets/Scripts/Core/Dialogue.cs"
  "Assets/Scripts/Core/Quest.cs"
  "Assets/Scripts/Core/NpcTable.cs"
  "Assets/Scripts/Core/Merchants.cs"
  "Assets/Scripts/Core/Trade.cs"
  "Assets/Scripts/Core/ItemTable.cs"
  "Assets/Scripts/Core/SaveModels.cs"
  "Assets/Scripts/Core/SaveRepository.cs"
  "Assets/Scripts/Economy"
  "Assets/Scripts/Presentation/FrontEndTrade.cs"
  "Assets/Scripts/Presentation/FrontEndInventory.cs"
  "Assets/Editor/NpcDialogueShot.cs"
  "Assets/Editor/InventoryEconomyShot.cs"
  "Assets/Editor/NetworkShot.cs"
  # Q3 补充（客户端 40903 处置）动到的两个文件。**必须进白名单**——
  # 上一轮的教训就是"改了但没搬进副本"，那棵树上的绿根本不是我的改动。
  "Assets/Scripts/Network/CloudSaveService.cs"
  "Assets/Scripts/UI/Auth/CloudSaveDialog.cs"
  "Tools/SaveChecks/TradeChecks.cs"
  "Tools/SaveChecks/QuestChecks.cs"
  "Tools/SaveChecks/DialogueChecks.cs"
  "Tools/SaveChecks/NpcChecks.cs"
  "Tools/SaveChecks/NpcDialogueChecks.cs"
  "Tools/SaveChecks/InventoryChecks.cs"
  "Tools/SaveChecks/InventoryEconomyChecks.cs"
  # 素材棘轮的基线文件：副本树里没有它，门禁会直接判"基线读不到"而红——
  # 这是设计如此（缺基线不等于不设限），所以它必须进白名单。
  # H-4 把图标基线扩成五目录的素材基线，旧文件 art-icon-baseline.json 已删（内容并入）。
  "Tools/SaveChecks/art-ratchet-baseline.json"
  "GameServer/src/main/java/com/twelvejade/server/save/ClientSchema.java"
)

# 路径基准：Assets/... 在 Unity 工程下，其余相对仓库根。
resolve_src() {
  case "$1" in
    Assets/*) echo "$REPO_WIN/GameClient/TwelveJade/$1|$REPO_MSYS/GameClient/TwelveJade/$1|$PROJ" ;;
    *)        echo "$REPO_WIN/$1|$REPO_MSYS/$1|$REPO_WIN/.batch/O" ;;
  esac
}

# 只读输入：不是我改的文件，但门禁要读它（跨端契约核对）。
# 不带进副本的话，那条断言在副本树上会"读空"——读空长得像红了一次，其实护栏根本没跑。
FIXTURE_INPUTS=(
  "GameServer/src/main/java/com/twelvejade/server/save/SaveController.java"
)

# 把白名单展开成「源文件 | 副本里同一位置」的清单（目录条目递归展开、跳过 .meta）。
# 单独拆出来是为了 `check` 子命令能**只验不搬**——搬一次要几十秒，而"这棵树到底带没带我的改动"
# 是要在跑夹具前反复确认的一件事。
ENTRIES=()
collect_entries() {
  local skipped=0 dirs=0 rel
  ENTRIES=()
  for f in "${MINE[@]}"; do
    IFS='|' read -r src_win src_msys dest_root <<< "$(resolve_src "$f")"
    if [ ! -e "$src_msys" ]; then
      # 静默跳过 = "以为覆盖了其实没有"。旧版把所有条目一律拼到 GameClient/TwelveJade/ 下，
      # 于是 SaveModels.cs 与 Tools/SaveChecks/* 从来没被覆盖进副本，
      # 隔离树上跑出来的"0 个错误"验的根本不是我的改动。
      echo "[overlay] 缺文件（跳过）：$f"
      skipped=$((skipped + 1)); continue
    fi
    if [ -d "$src_msys" ]; then
      dirs=$((dirs + 1))
      while IFS= read -r rel; do
        ENTRIES+=("$src_msys/$rel|$(copy_of "$f/$rel")")
      done < <(cd "$src_msys" && find . -type f ! -name '*.meta' | sed 's|^\./||' | sort)
      continue
    fi
    ENTRIES+=("$src_msys|$(copy_of "$f")")
  done
  for f in "${FIXTURE_INPUTS[@]}"; do
    [ -e "$REPO_MSYS/$f" ] && ENTRIES+=("$REPO_MSYS/$f|$(copy_of "$f")")
  done
  echo "[白名单] ${#MINE[@]} 条（缺 $skipped 条 · 目录 $dirs 个）展开成 ${#ENTRIES[@]} 个待验文件"
}

overlay_mine() {
  local f src_win src_msys dest_root
  for f in "${MINE[@]}"; do
    IFS='|' read -r src_win src_msys dest_root <<< "$(resolve_src "$f")"
    [ -e "$src_msys" ] || continue
    if [ -d "$src_msys" ]; then
      # 目录：源与目标都写全名（robocopy 搬的是源目录的**内容**）。连栽两次都记在这儿：
      # ① `robocopy .../Economy <父目录> /E` 把 BagService.cs 倒进 Assets/Scripts/ 根 ⇒ asmdef 重名；
      # ② 改成"父目录为源 + 目录名当条目"后，robocopy 只把第三参当**文件名模式**，
      #    扫到 8 个目录、0 个文件，一行都没搬 ⇒ 副本上的绿是 HEAD 的绿（T113）。
      robocopy "$src_win" "$dest_root/$f" /E /XD Library Temp /NFL /NDL /NJH /NJS >/dev/null
      continue
    fi
    robocopy "$(dirname "$src_win")" "$dest_root/$(dirname "$f")" "$(basename "$f")"       /E /XD Library Temp /NFL /NDL /NJH /NJS >/dev/null
  done
  for f in "${FIXTURE_INPUTS[@]}"; do
    [ -e "$REPO_MSYS/$f" ] || { echo "[只读输入] 缺 $f"; continue; }
    robocopy "$(dirname "$REPO_WIN/$f")" "$REPO_WIN/.batch/O/$(dirname "$f")" "$(basename "$f")"       /E /NFL /NDL /NJH /NJS >/dev/null
  done
  collect_entries
  verify_overlay "${ENTRIES[@]}"
}

# 覆盖完必须自证"我的改动真的在这棵树上"。形态向 2号 的 T114 对齐（D-48.3 点名要一致），
# 两道都不写死探针——写死的符号会随轮次腐烂，腐烂之后它就永远"通过"：
#   ① hash 硬闸：白名单逐文件比源树与副本的 sha1，不一致就**中止**（不中止就会拿假绿去报数）；
#   ② 符号抽查：从工作树 diff 自动取每个改动文件"最长的那条新增行"，数它在副本里命中几次。
# 两道都过才允许跑夹具；`check` 子命令单独跑这道闸，`check-tamper` 证明它坏了会中止。
# 白名单路径 → 副本里的同一位置（Assets/... 在工程根下，其余在 .batch/O 根下）
copy_of() {
  case "$1" in
    Assets/*) echo "$REPO_MSYS/.batch/O/GameClient/TwelveJade/$1" ;;
    *)        echo "$REPO_MSYS/.batch/O/$1" ;;
  esac
}

verify_overlay() {
  local entries=("$@") bad=0 probes=0 missed=0 entry src_abs dst_abs inner rel f prefix token hits
  for entry in "${entries[@]}"; do
    IFS='|' read -r src_abs dst_abs <<< "$entry"
    [ -f "$src_abs" ] || continue
    if [ ! -f "$dst_abs" ]; then
      echo "[抽查] 副本里缺文件 $inner"; bad=$((bad + 1)); continue
    fi
    if [ "$(sha1sum < "$src_abs")" != "$(sha1sum < "$dst_abs")" ]; then
      echo "[抽查] **内容不一致** $inner（源树与副本不是同一份东西）"; bad=$((bad + 1))
    fi
  done
  for f in "${MINE[@]}"; do
    # 该条目在仓库里的相对前缀（Assets/... 在工程下，其余本来就在仓库根）。
    # 注意：这里必须用 REPO_WIN（D:/ 形式）喂 git -C——sync 里 export 了
    # MSYS2_ARG_CONV_EXCL="*"，那条环境变量会让 git 拒绝 /d/... 形式（fatal: cannot change to），
    # 而它失败是**静默**的：探针数出来 0 条，看着像"没有改动要验"。
    case "$f" in Assets/*) prefix="GameClient/TwelveJade/$f" ;; *) prefix="$f" ;; esac
    for rel in $(git -C "$REPO_WIN" diff --name-only -- "$prefix" 2>/dev/null); do
      case "$rel" in GameClient/TwelveJade/*) inner="${rel#GameClient/TwelveJade/}" ;; *) continue ;; esac
      token=$(git -C "$REPO_WIN" diff -- "$rel" | grep '^+' | grep -v '^+++' | sed 's/^+//' | tr -d '\r' |
        awk '{ if (length($0) > n) { n = length($0); line = $0 } } END { print substr(line, 1, 100) }')
      [ -z "$token" ] && continue
      probes=$((probes + 1))
      hits=$(grep -acF "$token" "$PROJ/$inner" 2>/dev/null || echo 0)
      if [ "${hits:-0}" -gt 0 ]; then
        echo "[抽查] 命中 $hits · ${inner##*/} 的新增符号行"
      else
        echo "[抽查] **副本里没有** ${inner##*/} 的那一行 ⇒ 这一棵树验的不是我的改动"
        missed=$((missed + 1))
      fi
    done
  done
  echo "[抽查] hash 比对 ${#entries[@]} 个文件（不一致 $bad）· 符号探针 $probes 条（落空 $missed）"
  if [ "$bad" -gt 0 ] || [ "$missed" -gt 0 ]; then
    echo "[抽查] **中止：副本没有携带本次改动，后面的 0 problem 不可信**"
    exit 1
  fi
  d53_provenance
  verify_dir_metas
  d53_retire_report
}

# 目录条目在 collect_entries 里**故意跳过 .meta**（不让别人未提交的 meta 混进副本），
# 代价是"副本里某份 .meta 没了或被 Unity 现生了一个新 GUID"这件事没人看——而 GUID 漂了引用当场就断
# （T045 那一族老病：把 427x720 重采样那轮，就是靠 .meta 里的导入设置活的）。这道闸补上那一格：
# 目录条目下每个资源，副本里的 <资源>.meta 必须**存在，且与工作树那一份逐字一致**。
verify_dir_metas() {
  local f src_win src_msys dest_root rel meta_src meta_dst bad=0 drift=0 checked=0
  for f in "${MINE[@]}"; do
    IFS='|' read -r src_win src_msys dest_root <<< "$(resolve_src "$f")"
    [ -d "$src_msys" ] || continue
    while IFS= read -r rel; do
      meta_src="$src_msys/$rel.meta"
      [ -f "$meta_src" ] || continue
      meta_dst="$(copy_of "$f/$rel").meta"
      checked=$((checked + 1))
      if [ ! -f "$meta_dst" ]; then
        echo "[meta 闸] 副本里缺 ${rel}.meta（缺 meta ⇒ Unity 现生一个新 GUID，引用当场断）"
        bad=$((bad + 1))
      elif [ "$(sha1sum < "$meta_src")" != "$(sha1sum < "$meta_dst")" ]; then
        echo "[meta 闸] ${rel}.meta 与工作树逐字不一致（GUID 或导入设置漂了）"
        drift=$((drift + 1))
      fi
    done < <(cd "$src_msys" && find . -type f ! -name '*.meta' | sed 's|^\./||' | sort)
  done
  echo "[meta 闸] 目录条目下的 .meta 核了 $checked 份：缺 $bad 份 · 漂 $drift 份"
  if [ "$bad" -gt 0 ] || [ "$drift" -gt 0 ]; then
    echo "[meta 闸] **中止：这些资源的导入身份在副本里不是工作树那一份，后面的 0 problem 验的不是我这批**"
    exit 1
  fi
}

# D-53（架构师 2026-10-06 Q 包，全员口径）：交 0 problem 之前必须回答两件事——
#   ①这棵副本树的身份是哪个 sha；②覆盖清单里每一件文件的**来历**。
# 来历用内容 hash 三分法，不用 mtime：mtime 只说"什么时候写的"，不说"是谁的那一版"。
#   与 HEAD 同内容              ⇒ 纯 HEAD（镜像带的，本来就是已验收状态）
#   与本线工作树同、与 HEAD 异  ⇒ 本线脏版覆盖（这一棵树验的是我的改动）
#   两边都对不上                ⇒ 来历不明：副本多带了别人的在途改动 ⇒ **中止**
# 它是 T114 的镜像：T114 管"没带上被测改动 ⇒ 0 problem 是假的"，这条管"多带上别人的东西 ⇒ 0 problem 也是假的"。
d53_provenance() {
  local entry src_abs dst_abs rel tree pure=0 mine=0 absent=0 mine_list="" absent_list="" dirty_blob
  tree="$(git -C "$REPO_WIN" rev-parse --short HEAD 2>/dev/null)"
  # "这一件工作树里改了没有"只问 git，不拿 blob 哈希自己比：这些 .cs/.json 在工作树里是 CRLF，
  # 而 blob 是 LF（core.autocrlf 提交时换行），直接 sha1sum 两份会把 13 件明明干净的判成"脏版覆盖"
  # ——假阳性名单一长就没人看了，第一版就是这么红的。
  # 不加 pathspec：MINE 里的 Assets/... 是**相对 Unity 工程根**的写法，而 git 只认仓库根相对路径，
  # 拿它当 pathspec 会得到"这些文件都干净"——本线脏版覆盖因此长期数出来 0，
  # 而工作树里明明有两件是脏的（本轮实测抓到，见 d49-d50/o3 报告）。整仓一次问，再按 rel 对名。
  dirty_blob="$(git -C "$REPO_WIN" status --porcelain -uall 2>/dev/null |
    awk '{ sub(/^.../, ""); gsub(/^"|"$/, ""); print }')"
  for entry in "${ENTRIES[@]}"; do
    IFS='|' read -r src_abs dst_abs <<< "$entry"
    [ -f "$src_abs" ] || continue
    rel="${src_abs#"$REPO_MSYS"/}"
    # 缺件先判、不看 git 状态：工作树里有而副本没有，就是"该盖上没盖上"（T113 的老病——
    # 目录条目当年扫到 8 个目录、0 个文件，那棵树上的绿根本不是我的改动）。
    if [ ! -f "$dst_abs" ]; then
      absent=$((absent + 1)); absent_list="$absent_list $rel"; continue
    fi
    if ! printf '%s\n' "$dirty_blob" | grep -qxF "$rel"; then
      pure=$((pure + 1))      # 工作树与 HEAD 相同 ⇒ 覆盖是 no-op，副本里那份就是 HEAD 那份
      continue
    fi
    mine=$((mine + 1)); mine_list="$mine_list $rel"
  done
  echo "[D-53 身份] 主库 HEAD=${tree} · 覆盖清单 ${#ENTRIES[@]} 件：与 HEAD 相同的 no-op ${pure} 件 · 本线脏版覆盖 ${mine} 件 · 该覆盖却没盖上 ${absent} 件"
  # 名单必须点名：D-53 验收第②条问的是"覆盖清单里有没有不属于该线的文件"，只给数字没法判"不属于"。
  # 共享工作树里 git 说不出未跟踪文件归谁，所以这里交名字、不装属性——名字要么本线认领，要么当场撤条目。
  [ "$mine" -gt 0 ] && echo "[D-53 覆盖名单] $mine_list"
  if [ "$absent" -gt 0 ]; then
    echo "[D-53] **中止：这 ${absent} 件工作树里有、副本里却没有（$absent_list）⇒ 这一棵树验的不是我的改动**"
    exit 1
  fi
}

# D-53 的另一半：MINE 条目退场。改动进了 HEAD 之后条目还留着，覆盖的就是与 HEAD 相同的内容——
# 不报错，但"这棵树到底验的是谁"被稀释了。这里**只报告候选、不自动删行**：白名单同时是本线写域的
# 声明（下一批还要改这些文件），删行是每批的人为决定，不该由脚本替人做。
d53_retire_report() {
  local f prefix clean=0 total=0 list=""
  for f in "${MINE[@]}"; do
    case "$f" in Assets/*) prefix="GameClient/TwelveJade/$f" ;; *) prefix="$f" ;; esac
    total=$((total + 1))
    [ -e "$REPO_MSYS/$prefix" ] || continue
    # 未跟踪的新文件不算退场：它压根不在 HEAD 里，"status 干净"这种判据对它不成立
    git -C "$REPO_WIN" ls-files --error-unmatch "$prefix" >/dev/null 2>&1 || continue
    if [ -z "$(git -C "$REPO_WIN" status --porcelain -- "$prefix" 2>/dev/null)" ]; then
      clean=$((clean + 1)); list="$list ${f##*/}"
    fi
  done
  echo "[D-53 退场] 白名单 $total 条里有 $clean 条与 HEAD 已相同（覆盖等于 no-op，可撤）：$list"
}

# 自证入口（D-45 字样独占：演练一律不打 RESULT: / suite finished，只打 SELFTEST:）。
# 故意把副本里的一个文件改脏 ⇒ check 必须中止（rc≠0）；复原后 ⇒ 必须放行（rc=0）。
# 这一条防的是"抽查本身坏掉"——不响的抽查比没有抽查更危险，因为它会让人以为有人值守。
check_tamper() {
  local victim="$REPO_MSYS/.batch/O/GameClient/TwelveJade/Assets/Scripts/Core/SaveModels.cs"
  [ -f "$victim" ] || { echo "SELFTEST: 副本里没有 SaveModels.cs，先跑 sync"; exit 2; }
  cp "$victim" "$victim.selftest.bak"
  # 这一改脏模拟的是 D-53 那一格：副本里那一版**既不是 HEAD、也不是本线工作树**
  # （等价于把别人的在途改动搬了进来）。两道闸都会看到它：hash 硬闸报"内容不一致"，
  # 来历三分报"来历不明"——这里要的是"坏了会中止"这个行为，不是哪一条先喊。
  printf '\n// SELFTEST 故意改脏的一行：既不是 HEAD 也不是本线工作树的那一版\n' >> "$victim"
  ( bash "$0" check >/dev/null 2>&1 ); local dirty_rc=$?
  mv "$victim.selftest.bak" "$victim"
  ( bash "$0" check >/dev/null 2>&1 ); local clean_rc=$?
  echo "SELFTEST: 混进「别人的那一版」后 check rc=$dirty_rc（应为非 0）· 复原后 rc=$clean_rc（应为 0）"
  if [ "$dirty_rc" = 0 ] || [ "$clean_rc" != 0 ]; then
    echo "SELFTEST: **自证失败——这道抽查坏了，它不会中止**"
    exit 1
  fi

  # 第二记自证（D-53 的另一头 + T113 的老病）：**该盖上却没盖上**。
  # 做法：把副本里那一件挪开（不是删，是能挪回来），工作树里这一件仍是我的改动版本，
  # 于是"工作树改了 / 副本没有"这个形态成立，check 必须中止。
  local copy_victim="$REPO_MSYS/.batch/O/GameClient/TwelveJade/Assets/Editor/NpcDialogueShot.cs"
  local missing_rc=2 restored=0
  if [ -f "$copy_victim" ]; then
    mv "$copy_victim" "$copy_victim.gone"
    ( bash "$0" check >/dev/null 2>&1 ); missing_rc=$?
    mv "$copy_victim.gone" "$copy_victim"
    ( bash "$0" check >/dev/null 2>&1 ); restored=$?
    echo "SELFTEST: 副本少盖一件（工作树有改动、副本里没有）后 check rc=$missing_rc（应为非 0）· 挪回来后 rc=$restored（应为 0）"
    if [ "$missing_rc" = 0 ] || [ "$restored" != 0 ]; then
      echo "SELFTEST: **自证失败——漏盖一件不会被拦，这一棵树会拿别人的状态冒充我的改动**"
      exit 1
    fi
  else
    echo "SELFTEST: 副本里没有 NpcDialogueShot.cs，漏盖那记自证没跑（先 sync 再自证）"
    exit 1
  fi
  # 第三、四记自证（架构师 R 包 §9.4 排的更狠形态）：**篡改目录条目**与**动 .meta**。
  # 目录条目这条要单独造：它当年（T113）的失败形态是"扫到 8 个目录、0 个文件"，
  # 所以判它不能只看"覆盖清单里那件在不在"，还要看**目录条目本该搬来的每一件是否真在**。
  local dir_victim="$REPO_MSYS/.batch/O/GameClient/TwelveJade/Assets/Scripts/Economy/CraftingService.cs"
  local dir_rc=2 dir_back=0
  if [ -f "$dir_victim" ]; then
    mv "$dir_victim" "$dir_victim.gone"
    ( bash "$0" check >/dev/null 2>&1 ); dir_rc=$?
    mv "$dir_victim.gone" "$dir_victim"
    ( bash "$0" check >/dev/null 2>&1 ); dir_back=$?
    echo "SELFTEST: 目录条目少搬一件（Economy/CraftingService.cs 从副本里消失）后 check rc=$dir_rc（应为非 0）· 挪回来后 rc=$dir_back（应为 0）"
    if [ "$dir_rc" = 0 ] || [ "$dir_back" != 0 ]; then
      echo "SELFTEST: **自证失败——目录条目少搬文件不会被拦，T113 那个形态还活着**"
      exit 1
    fi
  else
    echo "SELFTEST: 副本里没有 Economy/CraftingService.cs，目录条目那记自证没跑（先 sync）"
    exit 1
  fi

  # 第四记：**删掉一份 .png.meta**。这道之前根本不存在（collect_entries 故意跳过 .meta），
  # 而缺 meta 的后果不是"少一个文件"，是 Unity 当场再生成一个**新 GUID** ⇒ 引用断、运行期空白。
  # 名字不写死（写死的样本会随图标族改名腐烂，腐烂之后它永远"通过"）：拿 Items 目录里第一份 .png.meta。
  local meta_victim meta_rc=2 meta_back=0
  meta_victim="$(ls "$REPO_MSYS/.batch/O/GameClient/TwelveJade/Assets/Resources/Art/Items/"*.png.meta 2>/dev/null | sort | head -1)"

  if [ -f "$meta_victim" ]; then
    mv "$meta_victim" "$meta_victim.gone"
    ( bash "$0" check >/dev/null 2>&1 ); meta_rc=$?
    mv "$meta_victim.gone" "$meta_victim"
    ( bash "$0" check >/dev/null 2>&1 ); meta_back=$?
    echo "SELFTEST: 删掉一份 .png.meta（$(basename "$meta_victim")）后 check rc=$meta_rc（应为非 0）· 复原后 rc=$meta_back（应为 0）"
    if [ "$meta_rc" = 0 ] || [ "$meta_back" != 0 ]; then
      echo "SELFTEST: **自证失败——meta 没了不拦，副本里的 GUID 漂了没人知道**"
      exit 1
    fi
  else
    echo "SELFTEST: 副本的 Art/Items 里找不到任何 .png.meta（先 sync 再自证；没 meta 的图根本不导入）"
    exit 1
  fi

  echo "SELFTEST: 自证通过（混进别人的版本、漏盖自己的、目录条目少搬一件、动 .meta——四记都会中止）"
}

print_identity() {
  echo "[我的改动]（工作树里本线白名单的实际状态，逐字进日志；空＝本线已全部提交）"
  # 只给 Assets 下的条目加工程前缀：Tools/ 那几条本来就是仓库根相对路径，
  # 一律前缀会拼出不存在的路径，git status 静默返回空——看着像"干净"，其实是没查。
  local paths=()
  for f in "${MINE[@]}"; do
    case "$f" in
      Assets/*) paths+=("GameClient/TwelveJade/$f") ;;
      *) paths+=("$f") ;;
    esac
  done
  git -C "$REPO_WIN" status --short -- "${paths[@]}" 2>/dev/null | sed 's/^/  /'
}

sync() {
  export MSYS2_ARG_CONV_EXCL="*"
  # 归因树必须跟着主库 HEAD 走。上一版这里只打印一句"提醒"就继续跑，结果这一趟的副本停在
  # 9cf0a76，而 1号 的 D-51 改名（96d3d80，shanxiao→stone-mandrill）已经进了主库 HEAD ⇒
  # Art/NPC 在旧树上还长着 shanxiao，我的基线红 3 条。红本身是对的（棘轮灵敏），
  # 但那三记红验的不是 HEAD——**取证基建自己把证据留在了旧时刻**，这比没跑更坏。
  # 归因树是 --detach 的只读影子树，切过去不会踩到任何人的工作，所以这里直接切，不留人工。
  local want now
  want="$(git -C "$REPO_WIN" rev-parse HEAD)"
  now="$(git -C "$REPO_WIN/.batch/verify-O" rev-parse HEAD 2>/dev/null)"
  if [ -z "$now" ]; then
    ( cd "$REPO_WIN" && git worktree add --detach .batch/verify-O "$want" >/dev/null 2>&1 )
    echo "[sync] 归因树不在，按 HEAD 新建：${want:0:7}"
  elif [ "$now" != "$want" ]; then
    # 影子树上"不该切过去"的脏内容才停手。两类是编辑器自己写的、不是谁的工作：
    #   ① Packages/packages-lock.json —— Unity 一开工程就重写它（主库工作树里那条 M 就是它）；
    #   ② 未跟踪的 *.meta —— Unity 给影子树里的 .txt 补的导入元数据。
    # 除此之外任何脏行都说明有人在归因树里动过手：那就不是一棵影子树，切它会吃掉别人的活。
    local unexpected
    unexpected="$(git -C "$REPO_WIN/.batch/verify-O" status --porcelain 2>/dev/null |
      grep -vE '^( M |M  )GameClient/TwelveJade/Packages/packages-lock\.json$' |
      grep -vE '^\?\? .*\.meta$' | grep -v '^[[:space:]]*$' || true)"
    if [ -n "$unexpected" ]; then
      echo "[sync] 停手：归因树里有不属于编辑器噪音的脏内容，先查明是谁在这里动过手再跑"
      printf '%s\n' "$unexpected" | sed 's/^/[sync]   /'
      return 1
    fi
    echo "[sync] 归因树 ${now:0:7} → 主库 HEAD ${want:0:7}（脏的只有 Unity 自己写的两份，切之前已核过）"
    git -C "$REPO_WIN/.batch/verify-O" checkout --detach "$want" >/dev/null 2>&1 || {
      echo "[sync] 切不过去（多半是 packages-lock.json 在两个时刻之间变了）。"
      echo "[sync]   人工一步：git -C \"$REPO_WIN/.batch/verify-O\" checkout --detach $want"
      echo "[sync]   不自动 force——force 会丢本地改动，这里宁可停手也不替别人决定"
      return 1
    }
  fi
  mkdir -p "$REPO_MSYS/.batch/O/GameClient/TwelveJade"
  for d in Assets ProjectSettings Packages; do
    robocopy "$HEADTREE/$d" "$PROJ/$d" /MIR /XD ArtSource Library Temp Logs /NFL /NDL /NJH /NJS >/dev/null
  done
  # 覆盖必须在 /MIR 之后：镜像会把"源里没有"的文件从目标删掉，顺序反了就把自己的改动删干净了。
  overlay_mine
  echo "[sync] 副本 = 纯 HEAD($(git -C "$REPO_WIN/.batch/verify-O" rev-parse --short HEAD 2>/dev/null)) + 本线白名单覆盖"
  echo "[sync] 主库 HEAD = $(git -C "$REPO_WIN" rev-parse --short HEAD)"
  # 上面已经切过树，这里只做兜底核对：不一致就是没切成功，别把旧时刻的证据当 HEAD 的证据。
  if [ "$(git -C "$REPO_WIN/.batch/verify-O" rev-parse HEAD 2>/dev/null)" != \
     "$(git -C "$REPO_WIN" rev-parse HEAD)" ]; then
    echo "[sync] 停手：归因树与主库 HEAD 仍不一致，这棵树不能用作验收证据"
    return 1
  fi
  print_identity
}

# robocopy 对"目标在源之内"没有任何提示：它会把源一路镜像进自己，递归到 MAX_PATH 才停。
# 2026-10-06 00:07 就是这么炸的——syncmerged 一次跑出 20 层自复制、29.185 GB / 243,306 文件
# （见 Tools/logs/perf-05/disk-audit-v2.md）。当时 /XD 里写的是正斜杠绝对路径 D:/.../.batch，
# robocopy 按反斜杠全路径比对，匹配不上 ⇒ 静默失效。
# 两道防线：(1) /XD 一律用裸目录名；(2) 起 robocopy 之前先过 assert_dst_safe。
XD_EXCLUDES=(.batch .git .qoder .codebuddy .qoder-credits ArtSource Library Temp Logs)

_win_norm() {
  local p="${1//\\//}"
  while [ "${p%/}" != "$p" ]; do p="${p%/}"; done
  printf '%s' "$p" | tr '[:upper:]' '[:lower:]'
}

# assert_dst_safe <src> <dst> <空格分隔的排除裸名>
#   目标在源之外   -> 放行
#   目标 == 源     -> 拒绝
#   目标在源之内   -> 相对路径的首段必须命中排除名单，否则拒绝
assert_dst_safe() {
  local src="$1" dst="$2" excl="${3:-}"
  local s d rel first ex
  s="$(_win_norm "$src")"; d="$(_win_norm "$dst")"
  case "$s" in [a-z]:/*) ;; *) echo "[guard] 停手：源不是绝对 Windows 路径：$src"; return 1 ;; esac
  case "$d" in [a-z]:/*) ;; *) echo "[guard] 停手：目标不是绝对 Windows 路径：$dst"; return 1 ;; esac
  if [ "$d" = "$s" ]; then
    echo "[guard] 停手：目标是源本身（src=$src）"; return 1
  fi
  if [ "${d#"$s"/}" = "$d" ]; then
    return 0
  fi
  rel="${d#"$s"/}"
  first="${rel%%/*}"
  for ex in $excl; do
    if [ "$first" = "$(_win_norm "$ex")" ]; then
      return 0
    fi
  done
  echo "[guard] 停手：目标在源之内，且首段 '$first' 不在 /XD 排除名单里 [${excl:-空}]"
  echo "[guard]   src = $src"
  echo "[guard]   dst = $dst"
  return 1
}

selftest() {
  local fail=0 want got src dst c
  # 期望(1=拒绝,0=放行)|src|dst|排除裸名（逗号分隔）
  local cases=(
    "1|D:/qiqi_me/Eternal Jade Scroll|D:/qiqi_me/Eternal Jade Scroll/.batch/O|"
    "1|D:/qiqi_me/Eternal Jade Scroll|D:/qiqi_me/Eternal Jade Scroll/.batch/O|.git"
    "0|D:/qiqi_me/Eternal Jade Scroll|D:/qiqi_me/Eternal Jade Scroll/.batch/O|.batch"
    "1|D:/qiqi_me/Eternal Jade Scroll|D:/qiqi_me/Eternal Jade Scroll|.batch"
    "0|D:/qiqi_me/Eternal Jade Scroll|D:/qiqi_me/Eternal Jade Scroll-other/O|"
    "0|D:/qiqi_me/Eternal Jade Scroll|D:/tj-batch-O|"
    "0|D:\qiqi_me\Eternal Jade Scroll|D:/qiqi_me/Eternal Jade Scroll/.batch/O|.batch"
  )
  for c in "${cases[@]}"; do
    want="${c%%|*}"; c="${c#*|}"
    src="${c%%|*}"; c="${c#*|}"
    dst="${c%%|*}"; c="${c#*|}"
    if assert_dst_safe "$src" "$dst" "${c//,/ }" >/dev/null 2>&1; then got=0; else got=1; fi
    if [ "$got" = "$want" ]; then
      echo "SELFTEST: ok   $([ "$want" = 1 ] && echo 拒绝 || echo 放行)  src=$src  dst=$dst  ex=${c:-无}"
    else
      echo "SELFTEST: FAIL 期望 $([ "$want" = 1 ] && echo 拒绝 || echo 放行)，实得 $([ "$got" = 1 ] && echo 拒绝 || echo 放行)  src=$src  dst=$dst  ex=${c:-无}"
      fail=1
    fi
  done
  [ "$fail" -eq 0 ] && echo "SELFTEST: PASS（${#cases[@]} 记）" || echo "SELFTEST: FAIL"
  return "$fail"
}

syncmerged() {
  export MSYS2_ARG_CONV_EXCL="*"
  assert_dst_safe "$REPO_WIN" "$REPO_WIN/.batch/O" "${XD_EXCLUDES[*]}" || return 1
  mkdir -p "$REPO_MSYS/.batch/O"
  local rc=0
  robocopy "$REPO_WIN" "$REPO_WIN/.batch/O" /E \
    /XD "${XD_EXCLUDES[@]}" \
    /R:1 /W:1 /NFL /NDL /NJH /NJS >/dev/null || rc=$?
  # robocopy 的退出码是位掩码：0-7 是"成功/有拷贝"，>=8 才是真错误。
  if [ "$rc" -ge 8 ]; then
    echo "[syncmerged] 停手：robocopy rc=$rc（>=8 是真错误，对照 robocopy /? 的退出码表）"
    return 1
  fi
  echo "[syncmerged] robocopy rc=$rc / 排除 ${XD_EXCLUDES[*]}"
  echo "[syncmerged] 副本 = 整个工作树镜像（含 M/N 在制品），主库 HEAD = $(git -C "$REPO_WIN" rev-parse --short HEAD)"
  echo "[syncmerged] 注意：这一棵树上的红与绿**不全是本线的**，报数前先看归因（见 o-regress.log 关 5/关 7）"
  print_identity
}

# run <日志名> <quit|none> <executeMethod 全限定名>
run() {
  local log="$1" quit="$2" method="$3"
  local out="$REPO_WIN/.batch/O/Tools/logs/$log.log"
  # D-53 的①（树身份）不只在 sync 时核一次就完：主库每几分钟就动一次，sync 之后 HEAD 再往前走，
  # 这棵副本就旧了一格，而日志上的 "0 problem(s)" 长得和"验的是 HEAD"一模一样。
  # 所以每次启动 Unity 之前再核一遍；不一致就停，让我重新 sync——今天中午这条自己抓到过一次
  # （sync 完 HEAD 从 3cae5dd 走到 7973b47，那一趟的 economy 是在旧树上跑的）。
  local want now
  want="$(git -C "$REPO_WIN" rev-parse HEAD)"
  now="$(git -C "$REPO_WIN/.batch/verify-O" rev-parse HEAD 2>/dev/null)"
  if [ "$now" != "$want" ]; then
    echo "[run] 停：归因树 ${now:0:7} 落后/偏离主库 HEAD ${want:0:7} ⇒ 先重新 sync，再跑 $method"
    return 1
  fi
  mkdir -p "$REPO_MSYS/.batch/O/Tools/logs"
  echo "[run] $method → $out"
  if [ "$quit" = "quit" ]; then
    "$UNITY" -batchmode -quit -projectPath "$PROJ" -executeMethod "$method" -logFile "$out" >/dev/null 2>&1
  else
    "$UNITY" -batchmode -projectPath "$PROJ" -executeMethod "$method" -logFile "$out" >/dev/null 2>&1
  fi
  echo "[exit] rc=$? （只作参考，判据看下面几行）"
  # "Scripts have compiler errors." 必须单独抓：副本树没编译过时，日志里既没有 suite finished
  # 也没有 error CS（本轮实测：robocopy 把整个 Economy 目录倒进了 Assets/Scripts/ 根，
  # asmdef 重名 ⇒ rc=1、ok 行数 0、日志里一行断言都没有）。这种"静默的红"最容易被当成没跑。
  grep -a -E "suite finished|finished, [0-9]+ problem|error CS|Scripts have compiler errors|Assembly with name" "$out" | tail -8
  grep -a -E "PROBLEM|断言失败" "$out" | tail -8
  # 本线两套夹具不打逐条 "ok:"（那是 FrontEndShot/TerrainPropShot 的风格），
  # 它们把通过数写在收尾行的 ok= 里 ⇒ 这里数红条数，别再打印一个骗人的 0。
  grep -a -cE "PROBLEM|断言失败" "$out" | sed 's/^/[红条数] /'
}

gates() {
  echo "[gate 1] dotnet build Tools/UnityCompileChecks --no-incremental"
  ( cd "$REPO_MSYS" && dotnet build Tools/UnityCompileChecks --no-incremental 2>&1 \
      | grep -aE "警告|错误|error CS|warning CS" | tail -4 )
  echo "[gate 2] dotnet run --project Tools/SaveChecks/SaveChecks.csproj --no-restore"
  ( cd "$REPO_MSYS" && dotnet run --project Tools/SaveChecks/SaveChecks.csproj --no-restore 2>&1 \
      | tee "$LOGS/o-savechecks-raw.log" | tail -1
    grep -ac "^PASS:" "$LOGS/o-savechecks-raw.log" | sed 's/^/[PASS 条数] /' )
  echo "[gate 3] town config consistency（GBK 控制台下会崩在 ↔ 上，见 T086，故显式钉 utf-8）"
  ( cd "$REPO_MSYS" && PYTHONIOENCODING=utf-8 python Tools/check-town-config-consistency.py 2>&1 | tail -3 )
}

shots() {
  echo "[shots] 本线出图：体积 + md5 + 同批唯一数（T073 的口径，体积只作参考列）"
  local dir="$REPO_MSYS/.batch/O/Tools/screenshots"
  local total=0 uniq=0
  for f in "$dir"/play-07-inventory.png "$dir"/play-08-trade.png "$dir"/play-06-town.png; do
    [ -f "$f" ] || continue
    printf "%9s  %s  %s\n" "$(stat -c %s "$f")" "$(md5sum "$f" | cut -c1-12)" "$(basename "$f")"
  done
  total=$(ls "$dir"/play-*.png 2>/dev/null | wc -l)
  uniq=$(for f in "$dir"/play-*.png; do [ -f "$f" ] && md5sum "$f" | cut -c1-32; done | sort -u | wc -l)
  echo "[唯一 md5 数] $uniq / $total 张"
}

case "${1:-all}" in
  sync) sync ;;
  check) collect_entries; verify_overlay "${ENTRIES[@]}" ;;
  check-tamper) check_tamper ;;
  selftest) selftest ;;
  syncmerged) syncmerged ;;
  gates) gates ;;
  shots) shots ;;
  economy) run o-economy quit TwelveJade.Editor.InventoryEconomyShot.RunSuite ;;
  npc) run o-npc quit TwelveJade.Editor.NpcDialogueShot.RunSuite ;;
  frontend) run o-frontendshot none TwelveJade.Editor.FrontEndShot.Capture; shots ;;
  engine) run o-combat none TwelveJade.Editor.CombatEngineShot.RunSuite ;;
  terrain) run o-terrain none TwelveJade.Editor.TerrainPropShot.RunSuite ;;
  # 云存档三槽回归：schema 升版这一批**必须**跑它，因为它把客户端 CurrentSchemaVersion
  # 与服务端 SUPPORTED_SCHEMA 钉在同一处比对；只跑 SaveChecks 验不出跨端口径。
  net) run o-net quit TwelveJade.Editor.NetworkShot.RunTests ;;
  # O2-Schema11 的四关连跑：副本树 = 纯 HEAD + 本线白名单（31 条），
  # 不带 M/N 在制品——上一轮手工把 1号 在写的 FrontEndShot.cs 搬进来，
  # 那一版引用了尚未入库的 PuppetActor 检查钩子 ⇒ 四关全部"静默的红"（rc=1、零行断言）。
  schema11) sync
       run o-economy quit TwelveJade.Editor.InventoryEconomyShot.RunSuite
       run o-npc quit TwelveJade.Editor.NpcDialogueShot.RunSuite
       run o-net quit TwelveJade.Editor.NetworkShot.RunTests
       run o-frontendshot none TwelveJade.Editor.FrontEndShot.Capture; shots ;;
  all) sync; gates
       run o-economy quit TwelveJade.Editor.InventoryEconomyShot.RunSuite
       run o-npc quit TwelveJade.Editor.NpcDialogueShot.RunSuite ;;
  *) echo "unknown target: ${1:-}"; exit 2 ;;
esac
