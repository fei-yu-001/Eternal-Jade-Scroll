# 布局与移动 · 代码施工方案（code 线）

> 派单：七七架构师 A-01（2026-10-17）。依据：`Documents/青石镇布局规格.md`（下称「规格」，引用记作 §N）。
> 本方案只编排、不动业务代码；每条改动给出**涉及文件 + 验收夹具 + 风险 + 预估工作量**。
> 铁律（架构师原话，本方案逐条遵守）：
> **不许推翻**——纵深排序 T026（`FrontEndWorld.UpdateDepthOrder`）、指针拦截 T031（`IsPointerBlockedByUI`）、
> MAT-11 加减速（`SpeedAccel/Decel`）、位移驱动步态（`AdvanceGait(moved.magnitude)`）、碰撞分域 ADR-033
> （`TownMap.RegisterBlocker/ClearBlockers(source)`）；**terrainShape 单源管线不许绕过**（ADR-017：
> 布局派生项只在生成器里定义，经 sync 派生进 town-map.json，任何人不准手改）。
> 行号快照按当前工作树（FrontEndWorld.cs 1250 行 / WorldPropLayer.cs 455 行 / TownMap.cs 528 行 /
> PuppetActor.cs 690 行 / PuppetRig.cs 362 行 / building-plots.json 35 件 / town-map.json 8192²）。

---

## 0. 现状核对（与规格 §一 的差异，方案以实读为准）

- 三向走帧 sheet **三档全部已在盘上并入库**（farmer/merchant/traveller × front/side/back 共 9 张，
  `Art/Characters/*-walksheet-{front,side,back}-v1.png`），`LoadWalkSheet` switch 九条臂已写全。
  规格 §1.5「正/背无素材仍滑行」已不是真源——**真问题在 wiring 与断言口径**，不在素材缺口。
- `travelerMeshDeform` 当前**默认 true**（`FrontEndWorld.cs` WALK-FIX 段）。代码注释称 B-01「主角消失」
  根因已查清：`PuppetMeshGraphic` 的 `[RequireComponent(RectTransform)]` 顶掉了 Graphic 自带的
  `[RequireComponent(CanvasRenderer)]` ⇒ `Rebuild` 首行 return ⇒ 不画不报错；且有 `GuardTravelerMesh`
  30 帧自检兜底回退色带。但 `Documents/运行期接线矩阵.md` V 线行仍写「fixture-only / 判图 FAIL
  （2026-10-07）」——**两份记录打架**，按「仓库文件状态 > 文档」以代码为准，矩阵行属未翻，列为尾务 A-08。
- 碰撞分域只有 `world-prop` 一个 source；规格 §2.1 的九区目前只是 y 带划分 + terrainShape 的
  river/farmland/foothill 矩形，**没有「区」这个一级语义**。方案用 blocker source 分域承载（见 §2）。
- `TerrainPropShot` 断言 `plots ≥ 24`；`FrontEndShotProps` 有坏配置 / 纵深 / 立绘缺失一族断言，
  地块加密后这些是第一批会被打到的既有门禁（见 §3.1 风险）。

---

## 1. 施工总览：四个 Work Item

| WI | 一句话 | 归属规格 | 预估 |
|---|---|---|---|
| WI-1 | terrainShape 扩字段：三级路网（sideRoads 新向 + laneRoads 新类型 + farmlandPaths）与九区语义 | §2.1 / §2.2 | 3–4 h |
| WI-2 | 地块加密 35 → 90–110：生成器 + 门禁阈值 | §2.1 / §2.7 | 4–5 h |
| WI-3 | 走帧 flipbook 默认开启：接线修复 + 三向 Frames + 夹具升级 | §1.5 / B-03/B-11 | 3–4 h |
| WI-4 | 契约收口：props 死字段、接线矩阵翻行 | §1.6 | 1 h |

建议顺序：**WI-1 → WI-2 → WI-3 → WI-4**（碰撞域先立，地块再往里摆；走帧独立，可并行但别与 WI-2
同批进 Unity 批处理，避免红绿归因混在一起）。

---

## 2. WI-1：terrainShape 扩字段（三级路网 + 九区）

### 2.1 数据面改动（全部在生成器真源侧，产物经 sync 派生）

| 改动 | 文件 | 说明 |
|---|---|---|
| `terrainShape.version` 1 → 2 | `Tools/make-world-greybox.py`（terrain dict，~L228） | 扩字段必须升版，下游脚本按 version 分支读旧档不炸 |
| `sideRoads` 增 4 条横巷 | 同上 | 规格 §2.2 的四条：广场↔西市、南街↔祖屋、北街↔祠堂后、市集↔河堤。现有 schema 是纵向 `{baseX,amp,periodY,half,phase}`，横巷是横向（baseY 起）——**加 `orientation:"horizontal"` 字段**，不新造数组名（复用校验与渲染分支） |
| 新增 `laneRoads`（田埂，half=70） | 同上 | 矩形/线段列表，只落在 `farmland` 四带内部；`terrain-types.json` 加 `lane` 类型（value 13，material 复用 `dirt-road.png`——不新增材质，零美术依赖） |
| `world-material-gen.json` | `Assets/Resources/Config/` | **不动**：lane 复用 dirt-road 材质，材质清单不变 |
| 重新生成链 | `make-world-greybox.py → make-world-map.py / make-world-mask.py → compose-world-chunks.py --full-grid → sync-town-map-from-world-gen.py` | 全量重跑（10-11 教训：**产物代次必须全量一致**，增量重跑会出新旧混合代的 inner SEVERE_FAIL） |
| 运行期消费 | **无** | 路/巷/埂是贴图层，运行期只认 blocked + chunk 贴图；`TownMap.CanStand` 一行不改 |

**关键约束**：`sync-town-map-from-world-gen.py` 只回写 `terrainShape/blocked/landmarks` 等派生键，
本 WI 不改 sync 脚本——新字段躺在 terrainShape 里随整块替换自然进 town-map.json。

### 2.2 九区碰撞语义（规格 §2.1 的代码承载）

规格要的是「九区环带」成为可读、可验的结构，不是注释。落法：

1. **zone 划分数据化**：`terrainShape` 新增 `zones` 数组（9 项：`{id, name, y0, y1}`，值取规格 §2.1 表）。
   纯数据，与现有 `foothill/farmland` 矩形同地位。
2. **碰撞分域沿用 ADR-033 不改模型**：`WorldPropLayer` 摆放时按 plot 的 footY 落进哪个 zone，
   把 `BlockerSource` 从单一 `"world-prop"` 细分为 `"world-prop:" + zoneId`（`PlaceAll()` ~L130 一处改动，
   `ClearBlockers` 改为清全部 `world-prop:*` 前缀——`TownMap.ClearBlockers` 加前缀清场重载，~L253）。
   收益：某区重摆/排障可以只清自己那域；`ExternalBlockerCount("world-prop:market")` 这类验收钩子直接可断言
   「西市区有 N 块根基」，分区在碰撞层第一次有了名字。
3. 镇外收束（规格 §1.3「62% 世界零碰撞」）：**不新增 tile 生成器**，用 `blocked` 既有通道——
   在 make-world-greybox 里为山脚围合/河湾/田埂外缘补 blocked（32px tile，why 用现有枚举值），
   sync 照常派生。田埂本身不挡人（田是走的地），收束靠田带外缘与山脚线。

### 2.3 验收与风险

- 夹具 / 门禁：`python Tools/check-world-seams.py --full-grid`（112 PASS 不回退）；`check-world-mask.py`
  自检；`check-town-config-consistency.py` PASS；`TerrainPropShot`（贴图重生成后首跑）；
  `FrontEndShot` 世界页截图（横巷/田埂在目视层出现，交斐哥看图）。
- **风险 R1**：横巷 orientation 字段要穿透 greybox/mask/compose 三脚本的分支，漏一处就是
  「配置里有巷、贴图上没巷」的静默丢内容。缓解：mask 脚本对未知 orientation 直接 raise。
- **风险 R2**：blocked 补收束可能把 encounter 点/地标圈压掉。缓解：sync 后跑
  `TerrainPropShot` + 既有「地标可站立」断言族；生成器里对每个 landmark 做 32px 探针盘自检
  （T065 已有同型做法，照抄）。
- 工作量：3–4 h（含一次全量 compose，机器时间占大头）。

---

## 3. WI-2：地块加密 35 → 90–110

### 3.1 改动点清单

| 改动 | 文件 | 说明 |
|---|---|---|
| 新增 `Tools/make-building-plots.py` | Tools/（新） | **plots 生成器化**：九区锚点表（每区 ≥1 交互锚 + ≥1 件 ≥3 身位，规格 §2.1 铁律）+ L0–L4 高度档（§2.6）+ 主街贴路间距 ≤700（§2.5）写成生成约束，输出 building-plots.json。手排 90 件坐标必出碰撞/遮挡错，且不可重放——ADR-017 同型逻辑，内容也该有真源 |
| `building-plots.json` | Resources/Config/ | 35 → 90–110 件；kind 扩类目（ancestral-hall / stage / watergate-tree / wharf 等新 kind 字符串——`WorldPropLayer.ParsePlots` 对 kind 不做枚举校验，零代码改动即可进） |
| `WorldPropLayer.cs` | ~L130（见 WI-2.2 分域）+ 轮询收敛（见下） | 无结构性改动；**每帧 `AllPlotsAlive()` 巡检在 100 件量级仍是 O(n) 引用判空，可先不动**，等 PERF-01（MemoryAuditShot 在途）给数再决定改事件制——写进方案是为了堵「顺手重构」 |
| `TerrainPropShot.cs` | L221 `specs.Count >= 24` | 阈值提至 ≥ 90；同时加「九区每区 ≥1 件 L3+」断言（zones 从 town-map.json terrainShape 读，与 WI-1 同源） |
| `check-plot-overlap.py` | Tools/PropChecks/ | 判据不变；90–110 件下 MIN_CORRIDOR=60 的成对检查是主要守门员，生成器输出必须先过它再入库 |
| `check-prop-art.py` | Tools/ | 新 kind 的占位色分支（`AddFallback` 目前只分 tree/building 两色，新类目暂归 building 褐，TODO 注释即可，不为它扩枚举） |

### 3.2 摆放语义不变量（防回归钉子）

- footY 升序入库（文件头契约，生成器 sort 后写出）；
- 「建筑/大树与 NPC 画带相交时 footY 必须小于 NPC 的 BaseY」（文件头遮挡铁律）写进生成器硬约束，
  违反直接拒出——这比事后靠截图发现强；
- `blocks:false` 只给门洞类（牌坊先例 T067），新类目默认 true；
- art 留空 = 占位水墨几何，**不许报错**（既有契约，90 件里大量 art 为空是正常态）。

### 3.3 验收与风险

- 夹具：`FrontEndShotProps`（坏配置/纵深/缺图一族不回退）+ `TerrainPropShot`（≥90、分区覆盖）+
  `python Tools/PropChecks/check-plot-overlap.py` PASS + `FrontEndShot` 密度对照截图（每屏 20–25 件，
  规格 §2.7 的目视验收，交斐哥）。
- **风险 R3（本方案最大风险）**：纵深排序每帧 `placeables.Sort` + SiblingIndex 写回，实体从 ~50 涨到
  ~150（90 地块 + NPC + 遭遇）后每帧开销与 SetSiblingIndex 抖动都放大；规格自定的红线是每屏 ≤30
  placeable。生成器按区摆放天然稀释同屏密度，但**必须用 FrontEndShot 的帧时间或 PERF-01 实测背书**，
  不许只凭「应该没事」。
- **风险 R4**：新 plots 的根基矩形注进 CanStand 后，既有 T112 到访圈（ReachRadius=96）与
  NPC ArrivalPointNear 落点可能被新墙挤掉 ⇒ `FrontEndShot` 的搭话/到访断言红。缓解：生成器对
  13 个 landmarks 与全部 npcs/encounters 做落点可达性自检（复用 check-plot-overlap 的 base_rect 计算）。
- 工作量：4–5 h（生成器 2.5h + 锚点表数据 1h + 门禁迭代 1h）。

---

## 4. WI-3：走帧 flipbook 默认开启（B-03/B-11 收口）

目标态：`travelerUseWalksheet` **默认 true**，三向（正/侧/背）走路全是 Frames，零滑行。

### 4.1 改动点清单（按依赖序）

| # | 改动 | 文件 / 位置 | 说明 |
|---|---|---|---|
| 1 | **静态断言钉死九条臂** | `Tools/SaveChecks`（art-ratchet，`Art/Characters` 档，rule=declared-or-literal） | 现状棘轮靠 `Resources.Load` 字面量判引用，switch 九臂刚好满足；**不许**为「消灭 switch」改成字符串拼接路径——那会让棘轮瞬间全孤儿红。本 WI 不动 LoadWalkSheet 结构，只加一条 SaveChecks 断言：九张在盘 sheet 各有字面量臂（防「素材在盘、臂漏写」的静默缺口，这正是 B-11 的历史形态）。switch 字面量保留是**特性不是债**（1.6-3 的解是钉住它，不是消灭它） |
| 2 | **MeshDeform × Frames 优先级互斥** | `PuppetActor.SetGaitMode` / `FrontEndWorld.BuildTraveler` | 当前 BuildTraveler 顺序：walksheet 开 → Frames；meshDeform 开 → 立刻又 SetGaitMode(MeshDeform)，**Frames 被无条件覆盖**。默认态 travelerMeshDeform=true + walksheet=true 时 Frames 根本不生效——这是「素材全在、玩家看到的还是色带」的 wiring 断点。改法：`ApplyTravelerWalkSheet` 移到 meshDeform 分支之后（walksheet 优先），或两开关互斥（勾 walksheet 自动关 meshDeform）。方案取前者 + 注释钉死优先级理由（Frames 是真帧素材，MeshDeform 是程序形变，素材在则素材优先） |
| 3 | **三向 Frames** | `FrontEndWorld.UpdateTravelerFacing`（~L860）+ `WalkSheetFor` | 现状已按 view 喂对应 sheet（front/side/back 各一张），缺的是「缺档回退」断言：sheet 为 null 时当前静默保持旧模式。改为：WalkSheetFor 三级回退（所选 → male → farmer-male）仍 null 时 `Debug.LogError` 并保持 Slices——正/背滑行从此只在「素材真缺」时出现，且有日志点名缺哪张 |
| 4 | **默认开启** | `FrontEndWorld.cs` L43 `[SerializeField] bool travelerUseWalksheet = false` → `true` | 场景未序列化本字段，改默认值即生效（与 travelerMeshDeform 的 MAT-10 先例同型）。同批把字段注释里的「默认 false = 行为零变化（B-03 口径）」改写为新的默认态与回退路径 |
| 5 | **镜像覆盖 frameImage** | `PuppetActor.ApplyMirror` / `SetFrameSlice` | 现状 ApplyMirror 只处理 slices 与 meshGraphic，frameImage 的 uv 不翻——左走是右走的同向帧（10-07 日志自记「ApplyMirror 未覆盖 frameImage，观感待反馈」）。默认开启前必须修：mirror 时 `uvRect` 宽取负（与 slices 同式），一行量级但玩家必看见 |
| 6 | **夹具升级** | `Editor/FrontEndShot.cs` L1278–1309 | L1280 注释自述「喂真实位移⇒索引跟着走」那半族**待开关有公开入口后补跑**——本 WI 就是那个时点：CheckFramesMode 扩到城镇页 traveler（`TravelerPuppetForShot`，B-10 已修好的钩子），断言三向各走一段后 FrameIndexForCheck 推进、FramesActiveForCheck=true、镜像后 uv 翻转。T108 模式无关断言（换帧或换相位）保持不动 |

### 4.2 与 B-01/MeshDeform 的关系（如实记录）

接线矩阵仍记 MeshDeform 判图 FAIL（2026-10-07，`w-a-mesh-deform.png` 主角不可见），代码注释称
WALK-FIX 已根治并默认 true。**本 WI 不解这个矛盾、不碰 PuppetMeshGraphic**——Frames 默认开后
MeshDeform 在城镇页被 walksheet 优先级压到不生效（第 2 条），矛盾对玩家不可见；矩阵翻行归 WI-4。
若日后要恢复 MeshDeform 档位，先按 `Tools/logs/walk-fix/` 的报告复跑一次判图再谈。

### 4.3 验收与风险

- 验收路径（默认开启的完整链）：`dotnet build Tools/UnityCompileChecks --no-incremental` 0/0 →
  SaveChecks 棘轮全绿（第 1 条断言在内）→ Unity 批处理 `FrontEndShot`（T108 族 + 新 Frames 断言，
  0 problem）→ 三向截图各两张相位图（md5 互异 + 腿位像素差，沿用 T108 判据）→ 斐哥 Play 实走：
  A/D 侧向迈步、W/S 正背迈步，无滑行。
- **风险 R5**：三向 Frames 后每次换向 `SetFrameSheet → BuildRenderers` 重建渲染物（10-07 实测 74 problems
  的那次正是换向重建 + 断言口径失配）。现状换向只发生在 view 变化帧（低频），可接受；但新夹具必须按
  「换向点之后相位清零属预期」写，别重蹈「按瞬时模式分支断言被寻路朝向抖动打穿」。
- **风险 R6**：9 张 6528×1088 sheet 的内存占用（RGBA32 单张 ~28MB 解压后，九张峰值可观）。
  Resources 常驻，PERF-01（MemoryAuditShot 夹具已在途未跑）应把「三 sheet 同驻」纳入四态审计；
  超标再谈按需加载/AssetBundle，不在本 WI 预优化。
- 工作量：3–4 h（wiring 1h + 镜像 0.5h + 夹具 1.5h + 批处理复跑）。

---

## 5. WI-4：契约收口

| 改动 | 文件 | 说明 |
|---|---|---|
| 删 `props: []` 死字段 | town-map.json **不改**（ADR-017 手改禁令）；改 `Tools/make-world-greybox.py` 不输出 props 键 + `TownMap.Parse` 的 props 解析段删除 | 规格 §1.6-1。两步一个提交，删字段与删解析同批，避免「配置没了、解析还在等它」的半态 |
| 接线矩阵翻行 | `Documents/运行期接线矩阵.md` V 线 | MeshDeform 行按 WALK-FIX 后的真态重写（默认开、Guard 兜底、B-01 旧判图作废待复跑）；flipbook 行从「默认关」改「默认开」（WI-3 落地后） |
| 规格第四节回填 | `Documents/青石镇布局规格.md` | 本方案的 WI 表压缩回填到「四、代码架构与编排」，规格文件保持单一入口 |

夹具：SaveChecks 全绿 + `python Tools/check-town-config-consistency.py` PASS。工作量 1 h。
风险低；唯一注意 props 删除要过 `TownMap.Parse` 的兼容路径（旧存档/旧配置无 props 键已可跑，
`Elements()` 对缺键 yield break，验证过）。

---

## 6. 三条最大风险（汇总，按伤害排序）

1. **R3 性能悬崖**：90–110 件 + 每帧 Sort/SiblingIndex 写回 + RawImage 实体翻倍，每屏 placeable 红线
   （≤30）没有实测背书前，「镇变密了」和「镇变卡了」是同一次提交的两种结局。PERF-01 必须先跑。
2. **R1 管线静默丢内容**：orientation/zones/laneRoads 穿四个脚本（greybox/mask/compose/sync），
   任何一环静默忽略新字段，产出物看起来「就是没巷」而不是「坏了」。每个脚本对未知字段必须 raise，
   全量 compose 后 seams/mask/consistency 三门禁连跑。
3. **R5+R2 复合红**：WI-3 换向重建的历史事故（74 problems）与 WI-2 新墙挤占到访圈/NPC 落点，
   都会以「既有夹具莫名红」的形态出现，归因成本高。两个 WI 不许同批进批处理；各自的夹具先把
   「预期内的红」（换向相位清零 / 落点漂移）写成显式断言，再谈新断言。

---

## 7. 不做清单（本方案明确不碰）

- 纵深排序 / 指针拦截 / MAT-11 / 位移驱动步态 / ADR-033 矩形模型本身（铁律，逐字遵守）；
- WorldPropLayer 轮询改事件制（等 PERF 数据，不预防性重构）；
- MeshDeform 的 PuppetMeshGraphic 本体（B-01 矛盾归矩阵翻行，不归本方案修代码）；
- 美术素材生成（徽派马头墙/祠堂戏台贴图归素材工单线，本方案只锁地块层的高度档与贴路连续性）；
- 任何 `git commit`（本方案纯文档）。
