---
name: jade-scroll-gate-cycle
description: 执行《十二玉楼长生经》仓库 Gate 任务书的完整循环——开工前确认边界、验收命令速查、阈值配置化、收口产物与提交格式。当会话涉及 Gate C0/C1/C*、里程碑 M*-0*、任务书落地、验收跑图、开发日志收口、技术债务 T 编号登记时使用。
---

# Gate 循环

## Overview

这个项目是"规划 AI 出任务书 → 执行会话按 Gate 落地"的两段式协作。本技能规定一个 Gate 从确认边界到收口提交的完整动作，以及历史上真实踩过的坑。

## 开工前：先确认边界，不要反推

**任务书在仓库里**：`Documents/架构文档.md` 文末的「交接包 2026-10-05-〈字母〉」一节，
含裁决表 D-xx、ADR、三线权限隔离表与逐条里程碑。执行会话先读 `Documents/20_项目状态板.md`
（当前节点唯一入口），再读该交接包。**若最新交接包的日期明显旧于 git log 末笔，说明架构师还没
接手上一轮——向用户确认，不要从代码反推一个计划当作官方计划。**

- 一次会话 = 一个可验收的小周期。做完当前 Gate 就停下等确认，不自行推进下一个 Gate。
- 员工编号口径（2026-10-05 起用户指定）：**开发者1号=世界线、开发者2号=战斗线、开发者3号=经济生活线**，
  偶尔插入"外包短期员工"。任务书字母（I/J/K/L → M/N/O …）与开发者编号并存，投稿里两个都写。
- 历史落盘惯例：只读盘点与代码地图写在 `Tools/logs/`（如 `c1a-status-inventory.md`、`c1b-code-map.md`）。这类文件带时效声明（"上一文件行号已过期，以本文件为准"），引用前先核对行号是否仍成立。

## 两套编号

| 前缀 | 含义 | 定义处 |
|---|---|---|
| `M*` | 里程碑，提交里扩展为两位段任务号（`M3-01` / `M5-05` / `M6-01`） | `Documents/00_项目开发文档.md` 第九节 |
| `C*` | 客户端/世界管线 Gate，子级形如 `C0-A1`、`C1-B-fix3` | **仓库内无定义文件**，仅能从 `Documents/09_开发日志.md` 与 git log 反推用法 |

六阶段路线（阶段 0/一/二/三/四/五/六）定义在 `Documents/00_项目开发文档.md` 第三节，铁律原文"完成一个阶段再进入下一个，不跳阶段"。服务端属阶段四、AI NPC 对话属阶段六，客户端主线优先。

## 验收命令速查

### Unity 批处理：**UI 类夹具一律不带 `-nographics`**（本条曾经骗了三轮）

```bash
"D:/ruangong/unity/Editor/6000.3.25f1/Editor/Unity.exe" -batchmode \
  -projectPath "<副本工程>/GameClient/TwelveJade" \
  -executeMethod TwelveJade.Editor.<Suite>.<Entry> \
  -logFile "D:/Eternal Jade Scroll/Tools/logs/<name>.log"
```

- **`-nographics` 会让 UI 层截成 1920×1080 纯平色**：`k-combat-0*.png` 曾 11 张全部 38,664 字节、
  同一 md5，退出码却是 0。去掉该 flag 后同样六张变成 418 KB–535 KB、md5 6/6 互不相同。
  这条就是 T055 的根因，**旧版本技能给的模板正是带 `-nographics` 的那一条**——照它跑等于自证假材料。
- **`-quit` 要按夹具分**（跨帧夹具自己调 `EditorApplication.Exit`，加 `-quit` 会在 executeMethod
  返回时就退，相位跑不完；`TerrainPropShot.cs:28-30` 实测：日志从 100 KB 掉到 19 KB 且没有 `finished` 行）：

| 夹具入口 | 带 `-quit`？ |
|---|---|
| `TerrainPropShot.RunSuite` / `CombatEngineShot.RunSuite` / `CombatShot.Capture` / `FrontEndShot.Capture` / `FrontEndShot.CaptureTerrain` / `FrontEndShotProps.Capture` / `ChapterShot.Capture` | **不准带**（`[InitializeOnLoad]` + 自 Exit） |
| `NpcDialogueShot.RunSuite` / `InventoryEconomyShot.RunSuite` | **要带**（同步执行，不带会一直挂着） |
| `NetworkShot.RunTests` / `ProjectSetup.Prepare` | 可带 |

- **判"跑完了"只认日志原文** `[XxxShot] suite finished, N problem(s)`；**退出码 0 不算通过**
  （T044：Unity 整链没编译，进程仍返回 0）。日志体积异常小 = 相位没跑完，不是"很快通过"。
- **真图判据按"相位"不按"文件全体"**：不同相位之间 md5 不许重复；但 `p02-01-town.png` 与
  `props-01-town.png` 同 md5 是正常的（同一帧的两个夹具出口）。
- **工程锁**：判据不是"有没有 Unity 进程"（用户常挂着编辑器 + 5 个 Unity Hub），而是目标工程有没有
  `<工程>/Temp/UnityLockfile`。主库被用户的编辑器持有时，批处理**只能在副本工程跑**。
- **副本工程同步**（Git Bash 会把 `/MIR` 转成 `D:/ruangong/git/MIR`，报"无效参数"什么都没拷）：
  ```bash
  export MSYS2_ARG_CONV_EXCL="*"     # 或写 //MIR 骗过转换
  robocopy "<主库>/Assets" "<副本>/Assets" /MIR /XD ArtSource Library Temp Logs
  robocopy "<主库>/ProjectSettings" "<副本>/ProjectSettings" /MIR
  robocopy "<主库>/Packages" "<副本>/Packages" /MIR
  ```
  `/XD ArtSource` 必带（实测 655 MB，而夹具只读 `Resources` 的 148 MB）。
  **robocopy 返回码 0–7 都是成功**（1=已复制文件），别按 Unix 的"非 0 即失败"判。
  改过磁盘上的 JSON 后副本里可能读到旧缓存（T027），要么重拷要么触发重新导入。
- 输出目录不在命令行上，由配置决定（地形走 `world-compose.json` 的 `outputDir`）。

### 合并态验收是**六关**，不是"四大专属夹具"

`TerrainPropShot` / `CombatEngineShot` / `InventoryEconomyShot` / `NpcDialogueShot` 四条线各自的夹具之外，
还有 **`FrontEndShot`** 与 **`FrontEndShotProps`** 两关主线夹具。
教训实证：四线自报全绿那天，`FrontEndShot` 从没在合并态跑过，实跑 **6 problem**。

### 纯逻辑与编译

```bash
dotnet run --project Tools/SaveChecks/SaveChecks.csproj --no-restore   # 含 WorldChecks
dotnet build Tools/UnityCompileChecks --no-incremental                 # 必须 --no-incremental，否则增量假象会吞警告（T021）
python Tools/check-town-config-consistency.py                          # 配置一致性（landmarks 等）
python Tools/PropChecks/check-plot-overlap.py                          # 地块防重叠与走廊
cd GameServer && ./mvnw -q test                                        # 本机无全局 Maven
python Tools/check-world-seams.py --selftest                           # 另有 --selftest-v21 / --selftest-generators / --full-grid
```
**红灯归因纪律**：共享工作树里躺着别人的在途改动时，先用
`git worktree add --detach <tmp> <sha>` 在纯 HEAD 树上复跑同一道命令，两边同红才不是你的账（T040/T044）。

## 阈值必须配置化

历史上 T002 就是硬编码阈值误判好素材。当前三份真源：

| 配置 | 节点 | 读取方 |
|---|---|---|
| `Assets/Resources/Config/town-map.json` | `acceptance` | `Tools/SaveChecks/ConfigChecks.cs` → `WorldChecks.cs` |
| `Config/world-compose.json` | `seamThresholds` | `Tools/check-world-seams.py` |
| `Config/world-material-gen.json` | `seamThresholds.finalPass/finalWarn` | `Tools/check-world-materials.py`、`Tools/make-procedural-base.py` |

后两者在配置缺失时会"回落内置默认值并 WARNING"——**看到那条 WARNING 就是配置链断了，不要当噪音略过**。

## 收口必产物

一个 Gate 做完，仓库里通常同时更新（照最近几次 Gate 的实际模式）：

- `Documents/09_开发日志.md` — 新条目，固定小节：做了什么 / 问题与解决 / 验收结果 / 未完成·风险 / 待办 / 一句话复盘
- `Documents/11_技术债务记录.md` — 新 T 行（见下）
- `Assets/ArtSource/PROVENANCE.md` — 素材来源表
- `Tools/logs/` — 验收 `.log` 与 `*-report.json`、盘点 `.md`
- `Assets/Resources/Config/*.json`、`Tools/*.py`
- 管线产物资产**连 `.meta` 一起入库**（T001 防再发条款要求）

**截图不入库**：`.gitignore` 里 `Tools/screenshots/*` 与 `*.log` 均被忽略。往 `Tools/logs/` 放东西前先确认到底要不要 `-f`，别顺手 `git add -A` 把日志扫进去。

## 技术债务登记

`Documents/11_技术债务记录.md` 是单一 7 列表格：`| ID | 状态 | 问题 | 影响 | 当前处理 | 恢复方式 | 防再发 |`。规则原文："每条债务有唯一 T 编号（递增，不复用）。新增条目前先扫描本文已有最大编号。"

**编号分段（2026-10-05 起，ADR-036）**：并行会话按线取段，**不得跨段**——
执行线各占 10 个号（例：M=T060-069、N=T070-079、O=T080-089），架构师保留 `T090+`。
实证教训：表内曾出现 T040→T042 断号，因为日志里那句"建议架构师落表为 T041"三天没人补；
**"建议编号"不等于"已编号"**，架构师要当天补账。

**共享账本单点写入**：`09_开发日志.md` 与 `11_技术债务记录.md` 每轮只指定**一条线**提交，
其余线只追加不 add；dispatcher 文件（`Tools/SaveChecks/ConfigChecks.cs`）同理，
别人要挂新 Checks 就交"要加的那一行原文"，不自行动手。

表头下方有「架构师裁决索引」段，列出本轮 D-xx 对各 T 号的终局裁决——**新增/复议债务前先读它**，
不要复议已经裁过的事（例如不准再从比对键里删 `landmarks`，那条裁过两次）。

## 提交信息

实际格式**不是** conventional commits（纯文档提交才用 `docs:` 前缀）：

```
<Gate/里程碑号>: <中文动宾短句> + <并列改动>——<结果/状态尾注>
```

正文三段式：`已完成并有验收数据：` 逐条带量化数字 → `未完成，留给下一轮：` → 尾注 **"按任务书约定：仅本地提交，未 push。"**

## 并行会话提交纪律（多人共用一棵工作树时，这条最容易出事）

- **只用 pathspec 形式提交**：`git commit -- <路径1> <路径2> ...`
  它绕过共享索引，只提交指定路径。**禁 `git add -A` / `git add .` / `git commit -a`**。
  实证教训：`7a82ac0` 用 `git add` + `commit` 时，把另一条线未入库的三行 Economy 改动
  一起带进了提交信息，只能写在标题里"连带 L 线未入库的 Economy 三行"来自我披露。
- 提交前 `git status --short` 原文贴进投稿；提交后 `git show --stat HEAD` 核对
  **实际入库路径 == 该线白名单**，多一个文件就回滚重做。
- **禁新建目录**：两份编译门禁（`Tools/SaveChecks/SaveChecks.csproj`、
  `Tools/UnityCompileChecks/UnityCompileChecks.csproj`）用的是 `Scripts/Core/*.cs`、
  `Scripts/Combat/*.cs`、`Scripts/Economy/*.cs`、`Editor/*.cs` 这类**目录级通配**，
  在既有目录里加文件不需要动 csproj；新建目录会绕过门禁 → Unity 不编译而 dotnet 绿（T040/T044）。
- **禁 `git stash` / `git checkout -- <别人的文件>` / `git reset --hard`**：工作树里躺着别人未提交的
  进度（J 线曾整体未入库压在共享门禁底下），这些操作会直接毁掉它。
- 取证必须写**树身份**：HEAD sha + 哪些未跟踪文件在场。"绿在另一棵树上"是本项目最贵的一类错。

## 红线

- **开发目录只有一个：`D:\Eternal Jade Scroll`。禁止在 `D:\` 根或任何仓库外路径创建目录/文件。**
  副本工程、归因 worktree、取证产物一律走仓库内 `<repo>/.batch/<线>`（已在 `.gitignore`）。
  历史教训：D 盘根曾堆出 11 个 `twelvejade-batch-repo*` / `tj-batch-*` / `tj-verify-*`，数 GB 垃圾，
  而且完全访问模式不覆盖工作区外路径，写在仓库外每步都要权限确认。
  唯一允许在仓库外的路径是工具本体：`D:/ruangong/unity/...`、`dotnet`、`python`。
- **给 Windows 程序传路径必须写 `D:/...` 并加引号，绝不能写 `/d/...`**。
  Git Bash 的 `/d/foo bar` 会被 Unity / robocopy 等原生程序当成**相对路径**，
  于是凭空造出 `D:\d\foo bar\...` 这样的溢出目录（实测：2号 的 warmup 日志、
  一份 57 KB 的 Unity 日志被写成 `D:\Eternal`，都是因为路径里的空格没加引号被切断）。
  同一类坑还有 `robocopy /MIR` 被转成 `D:/ruangong/git/MIR`——**根因都是 MSYS 的路径转换**。
- **git push 一律先问用户。** 任务书通常明写本轮不 push。
- **只 add 白名单文件。** `Documents/00_项目开发文档.md`、`Documents/01_*`、根 `README.md` 长期有他人未提交改动，一律不碰。
- **`Documents/架构文档.md` 与 `20_项目状态板.md` 长期未入 git**（协调真源只活在盘上），
  提交它们需要架构师/用户明确点头，属"影响共享状态"的动作，不自作主张。
- **文档索引有整片悬空引用**：`README.md` 指向已删的 `Documents/14_验收审计与修复方案.md`；`Documents/00_项目开发文档.md` 的文档索引表大片指向已删文件；同文件开头的状态行仍写"阶段 0"，与实际进度矛盾。**这些位置不要当真源**，活文档只有 4 份：00 总纲 / 01 情节大纲 / 09 开发日志 / 11 技术债（+ 架构文档、状态板两份协调件）。
- 验收数值达标 ≠ 通过。报结果时同时给"数值结论"和"待人工看的图路径"；反过来判 FAIL 时先怀疑验收脚本口径本身。
- **"能力存在≠功能存在"是本仓第一大反复债**：新写一个服务/配置字段/折扣函数时，必须同时指出**谁调用它**，
  否则登记债务。已实证 5 次：T038（碰撞字段没人读）、T043（气运簿没人记账）、T057（交情折扣零调用）、
  T091（分层纸偶没接进世界页）、T050（战败扣款的 `Apply` 只有夹具在调）。
