# 参考项目心得 · my_ai_town（LLM 自主生活小镇）

拉取时间：2026-10-05｜本地路径：`Reference/my_ai_town`（1.1 GB / 10,585 文件，已 gitignore）
来源：`https://github.com/mewamew/my_ai_town.git`｜Godot 4.7 + 纯 GDScript｜`VERSION` = `0.1.0-beta.6`

## 0. 授权红线（比 long-wind 更严）

`README.md:123` 自己写着：**目前还没有统一 license，不要把这个仓库当作可再分发的东西。**
⇒ 对我们：**只能读技法与结构，一行代码、一张图、一个 prompt 文件都不许搬。**
这份心得只写"它怎么想的"和"我们该落什么机制"，不复制任何实现。

## 1. 踩到的第一个坑（对我们也有用）

第一次 `git clone --depth 1` 只落了 **8,080 / 10,585** 个文件，`game/world/`（1,845 个文件，
所有世界规则与动画调用方）、`game/ui/`、`tools/guards/` **全部缺失**，而 `project.godot` 指向的
主场景不存在 → 项目根本起不来，`git status` 还显示一片 staged deletions。

原因：**Windows 长路径**。修复：`git config core.longpaths true` + `git checkout -f HEAD` → 10,585 齐了。

⇒ **这条对我们自己有效**：我们的 Unity 工程路径已经很长
（`GameClient/TwelveJade/Assets/Resources/Config/...`），三个副本工程再套一层
`.batch/M/GameClient/TwelveJade/...` 就更容易撞 260 字符。以后任何"克隆出来不对/文件莫名缺失"，
**先怀疑 longpaths，别怀疑代码**。

## 2. 它是什么（以及它明确反对什么）

**生成式 agents 小镇**：居民是 LLM 驱动的，自己决定去哪、做什么、跟谁说话，
依据是性格/职业/关系/记忆/位置/当前事件。`README.md:65` 直接拿"排期表"当靶子打：
> 自主生活……不沿固定日程或对白树机械行动。

**它没有**：作息表、需求衰减循环、行为树、效用 AI。全仓 grep
`schedule|routine|goal|utility|behavior_tree|state_machine` 在 `game/agent/` 里只命中
**prompt 文本字段**（`schedule_label`、`long_term_goals`）和世界快照投影出来的 `need_id`。

决策是**拉/唤醒模型**：世界发一个 `wake_packet`（`AgentContract.gd:186 validate_wake_packet`），
居民只能回 `{"handling":"continue_current"}` 或 `{"replace_current","action":{...}}`，
别的都不合法（`game/prompts/rules/40_decision_contract.md`）。
**动作 id 由世界侧供给**（`"待着" "搭话" "答话" "做活动" "用道具" "调整营业" "托人传话"`），
prompt 明令禁止编造能力与事实。

性格即规则集在 `game/prompts/rules/20_behavior_and_expression.md`：饿/累/冷、面子/力气/人情/时间
的权衡按每个居民自己的 `desire` 加权，最关键那句是：
> 把时间、住处、职业和工作地当作**生活节律，不是强制日程表**。

**对我们的意义**：我们现在的 6 个 NPC 是"作息表 + 记忆阶"，它这套是"节律 + 合法性契约"。
阶段六（Qwen 驱动 NPC）真正要抄的不是"用 LLM"，而是**这个契约边界**——
模型只能在世界给的法律动作集合里选，回包必须被校验。否则我们得到的是会编造剧情的 NPC。

## 3. 最值钱的一条：零引用棘轮守卫（直接治我们的老毛病）

`tools/guards/zero_reference_scan.py` 文件头我读了原文，语义是：

```
候选定义：
- game/ 下的 .tscn：其 res:// 路径与文件名在仓库其他文本文件中零出现；
- game/ 下 .gd 的 class_name X：标识符 X 在定义文件之外零出现。
两个清单：
- zero_reference_whitelist.json：动态加载/编辑器工具/手工预览等合法无引用项，
  逐项记 purpose（用途）、consumer（消费者）、owner（负责人）。
- zero_reference_baseline.json：存量候选，只减不增。
CI 判定（--check）：候选既不在白名单也不在基线 = 新增未解释候选 = 失败。
```

配套还有 `dynamic_call_scan.py`（**字符串派发如 `.call("x")` 会藏住调用点，所以它也被基线化+白名单化**）、
`required_tests_check.py`（钉住 `checks=N`，**套件不许缩水**）、`line_ratchet.py`（行数只许降）、
`preload_resource_check.py`（`res://` 目标不存在就失败），入口 `run_guards.sh`，
挂在 `.github/workflows/formal-validation.yml:22`。

### 为什么这条对我们是决定性的

我们这一周登的债里有一整族是同一个病——**"能力存在≠功能存在"**：

| 债 | 现象 |
|---|---|
| T038 | `building-plots.json` 的 `radius` 写了没人读，房子是空气 |
| T043 | `KarmaTracker.Record` 写完了，全仓零生产调用点 |
| T052 | `enemies.json` 加了两种妖，世界上没有遭遇入口 |
| T057 | `MemoryEcho.Discount` 折扣函数在，成交路径不经过它 |
| T091 | `PuppetActor` 分层纸偶写得很好，世界页对它零引用 |
| T100 | `RareDropRateBonus` 只有夹具在调 |

我们的处置方式一直是**人肉发现 → 登 T 号**。它的处置方式是**机器发现 → CI 红**。
而且它那两个清单的设计很聪明：**存量进基线（只减不增），新增必须给理由（purpose/consumer/owner）**——
这正好解决"一次性补不完历史欠账，但不能再新增"的现实约束。

`dynamic_call_scan.py` 那一层尤其值得注意：它承认"字符串派发会让调用点看不见"，
所以把这类调用**也**基线化，而不是假装静态分析够用。我们的对应物是
`ConfigChecks.cs:57` 的 `if (worldPhase)` 门控——**判据被条件包起来时，静默不跑就是同一个盲区**
（T092 里我已经让开发者3号加 marker 断言，但那是补丁，棘轮才是制度）。

`required_tests_check.py` 的"套件不许缩水"我们也该有：我们发生过 `FrontEndShot` 的
`traveler != null` 才跑，导致深度断言**静默跳过**（T091/T092），断言数从名义值悄悄掉。

## 4. 第二条：走路相位由**距离**驱动，不是时间驱动（直接给 P 线）

`game/characters/paper_doll/PaperDoll64Sprite.gd:152-164` 我读了原文，机制是：

```gdscript
func set_motion(direction, distance_delta):
    if direction.length_squared() <= eps: 回到待机帧; return
    _direction_row = _direction_row_from_vector(direction)
    if distance_delta > 0 and cycle_distance > 0:
        _distance_progress = fposmod(_distance_progress + distance_delta, cycle_distance)
        segment_distance = cycle_distance / FRAME_SEQUENCE.size()
        _frame_sequence_index = floor(_distance_progress / segment_distance) % 帧数
```

`configure_motion_speed()` 里 `cycle_distance = world_speed * frame_duration * 4`。

**这就是"脚踩得住地、任何速度都不滑冰"的结构性原因**：一步走完对应固定像素距离，
而不是"每秒播 N 帧"。对比我们的 `PuppetActor`：`time += Time.unscaledDeltaTime`（:198）是纯时间驱动，
`stepScale`（:167-169，钳在 .55–1.9）只是把步幅缩放一下——**慢走时腿还在原地高频摆，这就是"僵硬/假"的物理来源**。
而 `FrontEndWorld.cs:44` 那个 `bobPhase` 自增后全仓没人读（T091 里我记过），
说明我们当初也想做这件事但没接上。

⇒ **P 线任务书我会直接要求：把世界页角色的相位改成"累计位移 mod cycle_distance"驱动，
`cycle_distance` 由 `WalkSpeedWorld/RunSpeedWorld`（`FrontEndWorld.cs:447`）与帧时长算出来。**

另外两条动画技法：
- **只画 3 个方向，左向由右向运行时镜像**（`leftPolicy: "exact_runtime_mirror_of_right"`，
  见 `game/assets/characters/resident_2d_rig_v1/wardrobe_v1/wardrobe_catalog.json`）；
  人类角色**完全不用 `flip_h`**（全仓只有 4 个脚本出现）；斜向按主轴吸附到 4 正向，**没有 8 向**。
  我们现在世界页连朝向翻转都没有（T091 第③条），这是最省的美术量路径。
- **分层在编译期烘，不在运行期合成**：slot `head/top_hands/bottom/shoes` × 6 变体 = 6⁴=1296 组合
  + 1 张母版 = 那 1,297 个 PNG。**这个我们不学**——6 个 NPC 用 4 个 `SpriteRenderer` 共享图集
  比 1297 张图划算得多，而且它自己 `plannedLoadouts` 显示还要涨，是二次爆炸。
  **学它的契约校验，丢掉它的烘焙策略。**

## 5. 第三条：配置在加载期**拒绝**，不是降级

`PaperDoll64Sprite._validate_catalog` 是 **147 行、失败就 `push_error` + 不给贴图 + return false** 的
加载期校验，覆盖：schema 版本不符、几何尺寸不精确相等、`columnSequence` 数组不相等、
id 重复或带空白、图集路径不在白名单目录内、路径穿越（`contains("..")`）、
`defaultLoadoutId` 指向 `previewOnly` 项、`ResourceLoader.exists()` 失败、
以及 `_has_exact_string_keys` **同时比数量和成员**（多一个键和少一个键都失败）。
1,297 份 manifest 每份都重复 `masterContractSha256`，用来发现烘焙与运行期漂移。

⇒ 对照我们：`Economy/ItemFlags.cs:21` 的 `Current` 默认 `Empty`（"没装过就是什么都不门控"，T098）、
`BagService.TierRank` 找不到 tier 返回 -1（T099）、`building-plots` 的 radius 靠 24px 兜底（T058）——
**我们三处都是"缺失静默降级"，它三处都是"缺失当场拒绝"。**
这是同一条设计立场的两种选择，而我们的选择正在持续产生债务。

## 6. 第四条：存档版本化——我们 schemaVersion 11 那一批该长的样子

`docs/持久化模块清单.md` + `docs/` 里的迁移手册描述的做法：

- 世界日志**内容寻址分段**：`user://town_session_saves/world_log_segments/{sha256}.json`，每段 ≤256 条；
- World 与 Agent 各自带 revision，通过 `slots/{slot}/manifests/{revision}.json` 成对发布；
- `sessions/{session}/revisions/{n}/` + `intents/{save|restore}/` 日志 + `_claims/`；
- **迁移台账**（`docs/persistence-changes/*.json`）由 `persistence_change_check.py` 强制：
  改了持久化结构，必须要么登记一条迁移，**要么显式声明"不需要迁移"**；
- 8 份历史存档夹具（`game/tests/fixtures/historical_saves/beta1..beta5-*`）+ 一条 CI job
  专门跑"历史存档迁移矩阵"。
- 最诚实的一句：它承认 **beta3–beta5 的存档无法可靠区分**，所以 beta6 才补了 `saveRelease` 字段。

⇒ 对照我们：客户端 `CurrentSchemaVersion=10`（`SaveModels.cs:10`）与服务端
`SUPPORTED_SCHEMA=10`（`ClientSchema.java:13`）**手工对齐**，改存档必须两侧同步；
`karmaEvents` 字段进了 schema 10 但 `SaveRepository.IsValid`（:69-91）**没有它的校验分支**（我在 W-A 里刚指出）；
schema 11 要一次带上装备耐久（T051）、手艺熟练度（T046）、食 effects。
**"迁移台账 + 历史存档夹具矩阵"正是我们缺的制度**——否则每次升版都是赌。

## 7. 阶段六可直接借的两个小机制

- **`MemoryContinuityGuard.gd:73`**：对比新旧 `short_term_goals` 差异，专门抓 **LLM 悄悄改写自己的目标**。
  这是用 LLM 做 NPC 之后必然出现的失效模式，我们迟早会撞上，现在知道有这一招。
- **说话→义务**：`AgentContract.gd:630 _dialogue_promises_follow_up_action` 匹配约 34 条中文承诺习语
  （"交给我""我去一趟""跟我来"）+ 两条带**否定守卫**的正则（"不能带""带不了"），
  把说出口的承诺转成可追踪的后续义务。我们的对白现在只有 `grantFlag`/`acceptQuest` 这种显式挂载，
  玩家说"我帮你看着"什么都不发生。
- 顺带一条 token 纪律值得学：**图片输入转成文字一次，之后只持久化文字**
  （`prompt/ModelImageContent.gd`、`AgentSystem.gd:872`）。我们将来给物品/卷轴做识图时照这个做。

## 8. 明确不学

- 1296 组合预编译烘焙（二次爆炸，见 §4）；
- 两套并行的角色系统（paper_doll 5 引用 vs resident_2d_rig_v1 20 引用，
  中间摆着一个 `direction_branches/identity_unification_v1_pending/` 待统一目录）；
- 规模本身：它自己 `docs/architecture/TownWorldRuntime拆分基线.md:14` 承认
  **23,145 行 / 704 函数 / 89 个顶层状态 / 655 处子模块访问 `world._xxx` 私有成员**——
  一份自我记录的大泥球。651 个脚本、15 个 LLM provider 不是单人开发的形状，
  **我们只移植 1 个本地 Qwen provider + 1 个 FakeModelProvider**；
- Godot 专有表面：`frame_coords`、`push_error`、`@export_file`、`.import`、`res://`、`.tscn`——
  没有 1:1 对应，**移植数据契约而不是引擎 API**；
- 用中文文件名当唯一设计记录（README 的结构声明还和实际检出对不上）。

## 9. 一句话

long-wind 教我们**"声明了就必须被用到"**（双向契约测试），
my_ai_town 教我们**"用不到的东西根本不该存在"**（零引用棘轮 + 加载期拒绝）。
这两条合起来，正好是我们这一周用十几条 T 号换来的那件事的机器版。
