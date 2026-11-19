# 布局与移动 · 验收门禁方案（C-01）

> 派单：七七架构师 · C-01（2026-10-17）。编写人：ui（前端界面与验收工程师）。
> 依据：`Documents/青石镇布局规格.md`（下称"规格"）。本文档只做方案，不改任何夹具/脚本逻辑。
> 铁律：**数值放行 ≠ 人眼认可**。本方案所有门禁只做客观门槛（机器可断言的下限/上限），
> 最终美术观感一律由斐哥看图拍板。门禁绿只是"可以拿给人看"的入场券，不是"好看"。

## 0. 现状盘点（已读源码实测）

**规格第一节已给出实测数**：世界 8192²、视口 1920×1080、镇半径 1600（1.67 屏）、
1 身位 = 170 单位、跑速 470/s（2 秒 = 940 单位）、走速 260/s。

`building-plots.json` 当前实盘（2026-10-17 实测，与规格 1.2 的"35 件"已有出入——
规格写的是审计时点，审计后物件已扩到 **99 件**：tree 31 / prop 43 / building 19 / stall 6）：

| 量 | 当前值 | 规格目标 |
|---|---|---|
| 物件总数 | 99 | 镇内 70 + 河带 15 + 山麓 15（90–110，数量上已在带内） |
| 高度 min/中位/max | 80 / 240 / 660 | 见 L0–L5 阶梯（规格 2.6） |
| ≥2 身位（340+） | 待逐件核（中位 240 ⇒ 约半数在 L0–L1） | 28–32 件 |

数据结构（门禁可用的字段）：`plots[] = {id, kind, name, footX, footY, width, height, art,
radius, collisionDepth, collisionTop}`；`town-map.json` 有 `landmarks[13]`（含 x/y）、
`terrainShape{mainRoad/crossRoads/sideRoads/plaza/market/townRadius/farmland/river/bridges/...}`。

既有门禁（五脚本四夹具已通读，后文逐条复用）：

| 件 | 现有职责 | 本方案怎么用 |
|---|---|---|
| `Tools/check-town-config-consistency.py` | world-gen ↔ town-map 共同字段一致性 + enemy↔encounter 双向对表 | 挂 G-B4（路网同源） |
| `Tools/check-town-overlap.py` | props 立绘矩形 × walkable 走廊遮挡 | **已死挂**（town-map `props`/`walkable` 均空数组，规格 1.6.1）——G-A 系列要求它改读 building-plots 或明确退役 |
| `Tools/check-ui-layout.py` | 面板/按钮字面量重叠 + 正 y 符号（Presentation/*.cs 静态扫描） | 复用棘轮台账模式给 G-A 系列；与布局数据门禁互不替代 |
| `Tools/check-world-seams.py` | 64 块贴图接缝（贴图层，L5） | 不动；新物件加密后会换贴图，它自然重新值守 |
| `Tools/check-walk-sheet-motion.py` | 走帧 sheet 运动三维闸门（起伏/触地/帧高偏离，B/C/D） | G-M4 把它从"离线素材闸"接成"三向全装覆盖闸" |
| `FrontEndShot`（1763 行） | 六页主线 + FacingProbes 四向走拍（东/西/北/南）+ CheckFramesMode + T108 八图 md5 | G-M1/M2/M3 挂这里 |
| `FrontEndWorld.cs`（1217 行） | traveler 钩子（TravelerPuppetForShot/TravelerViewForShot）、LoadWalkSheet switch、travelerUseWalksheet 开关 | G-M2 需要它补三个 `*ForCheck` 只读钩子（新代码，不改旧行为） |
| `TerrainPropShot`（1465 行） | 地块/贴图运行期断言 | G-A7（同屏件数上限）的运行期侧挂这里 |
| `FrontEndShotProps`（1139 行） | 物件层断言族 | G-A5（锚点贴路）若做运行期复核可挂这里（一期不做，纯数据侧） |

六关现状：FrontEndShot / FrontEndShotProps / TerrainPropShot / CombatEngineShot /
InventoryEconomyShot / NpcDialogueShot，合并态全绿（2026-10-10~11 基线）。
纯逻辑侧有 `Tools/run-gates.sh` 汇总 18 条门禁（只汇总不判定，每条必须出 `RESULT:`/`SELFTEST:` 字样）。

## 1. 规格第三节「量化对照表」补全

> 直接补进规格文档第三节的成品表。每格给出：**指标 → 数值门槛 → 断言方式**。
> 全部门槛分两级：**FAIL**（破了即红）与 **WARN**（登记台账放行，只许销账不许放宽——
> 沿用 check-ui-layout.py 的棘轮口径）。

### 1.1 密度与分区（对应规格 2.1 / 2.5 / 2.7）

| 编号 | 指标 | 门槛 | 判据说明 |
|---|---|---|---|
| G-A1 | 九区每区物件密度 | 每区 **≥5 件** FAIL；其中 ≥2 身位（h≥340）**≥1 件** FAIL；镇内五区（区4–7）每区 **≥8 件** WARN | 区界按规格 2.1 表的 y 带/x 带硬编码进脚本常量；plots 按 footX/footY 归区 |
| G-A2 | 每区"存在感双件" | 每区 **≥1 个可交互锚点 + ≥1 件 ≥3 身位（h≥510）物件**，缺即 FAIL | "可交互锚点"一期近似 = 落在 landmarks 800 半径内的 plot 或 kind∈{building,stall}；二期 QuestLedger 接线后改查真交互点 |
| G-A3 | 每屏物件密度 | 任一 1920×1080 滑动窗（步长 480）内 **≥1 件** FAIL（空屏即荒野）；镇区内窗口均值 **≥20 件** WARN | 滑动窗枚举全部 plots 计数，输出最差窗口坐标 |
| G-A4 | 同屏件数上限 | 任一窗口 **≤30 件** FAIL | 规格 2.7 的纵深排序开销红线 |
| G-A5 | 视觉事件节奏（2 秒一事件） | 沿 mainRoad 中心线从 spawn(4096,7900) 到山门(4096,3280)，任一 **940 单位（跑速 2 秒）** 行程内，路两侧 300 范围内物件数 **≥1** FAIL；均值间距 **400–600** WARN | 主路是弯的（amp 140/periodY 1500），按 terrainShape.mainRoad 参数方程取中心线点列，步长 47（0.1 秒） |
| G-A6 | 主街建筑贴路 | 主街段（y∈[3280..6380]）两侧各 500 内，沿程每 **700 单位** 必有 ≥1 件 h≥340 FAIL | 规格 2.5"主街两侧建筑间距 ≤700" |

### 1.2 D/H 比与路网（对应规格 2.2）

| 编号 | 指标 | 门槛 | 判据说明 |
|---|---|---|---|
| G-B1 | 主街 D/H | 主街两侧贴路建筑（路沿 600 内、h≥340）平均高 H̄；500/H̄ ∈ **[1.0, 1.5]** WARN（现状 H̄≈240 ⇒ 2.08，必然 WARN 起步，进台账） | D 取 terrainShape.mainRoad.half×2，不手填 |
| G-B2 | 横巷 D/H | 新增横巷（sideRoads 里 half≤100 的条目）两侧建筑高 ≥300 ⇒ D/H ≤ **0.67** WARN | 无横巷时整条 SKIP 并在报告里点名（不许静默绿） |
| G-B3 | 田埂路 | farmland 四带内部存在 half∈[60..80] 的通路条目 WARN（一期农田无实体，登记台账） | 查 terrainShape.farmland + sideRoads |
| G-B4 | 路网同源 | terrainShape 内 roads/bridges/ferry 字段在 world-gen 与 town-map 完全一致 FAIL | **已存在**——check-town-config-consistency.py 的 `('terrainShape','json')`，本方案只是把它在表里点名，不新增代码 |

### 1.3 高度阶梯 L0–L5（对应规格 2.6）

| 编号 | 指标 | 门槛 | 判据说明 |
|---|---|---|---|
| G-C1 | 高度档覆盖 | L1（340–509）、L2（510–594）、L3（595–799）三档**每档 ≥1 件** FAIL；L2+L3 合计 **≥10 件** WARN（"镇子感主力层"） | 按 height 分档计数；L0=h<340、L4=h≥800、L5=贴图层不入 plots |
| G-C2 | L3 数量封顶 | L3 档（595–799）**≤6 件** FAIL | 规格 2.6"全镇 ≤6 件"，多了天际线就平了 |
| G-C3 | L4 分区封顶 | 每区 L4（≥800）**≤2 件**，且只允许落在区 1/3/4（水口/祠堂后/河湾）FAIL | 规格 2.6 摆放规则的机器化 |
| G-C4 | 天际线三级同框 | 以 plaza 中心（terrainShape.plaza 圆心）为原点，±960×±540 视窗内至少出现 **3 个不同高度档** FAIL（现状最多 2 级，起步即红 ⇒ 先登记 red 台账，按规格落地节奏销账） | 纯数据近似：视窗内 plots 的档位数。真"视线遮挡下的同框"只能靠截图，归 §4 人眼环节 |
| G-C5 | 玩具感红线 | building/stall 类中位高度 ≥ **340（2 身位）** WARN（现状 240 ⇒ 必然 WARN 起步） | 棘轮：每次扩物件只允许中位数不降 |

### 1.4 锚点间距与视线（对应规格 2.4）

| 编号 | 指标 | 门槛 | 判据说明 |
|---|---|---|---|
| G-D1 | 每屏一锚 | 沿主路行程，相邻两件 ≥3 身位（h≥510）物件的沿路距离 **≤1200** FAIL；镇区全域同类物件对最近邻距离 ≤1200 WARN | 距离沿主路中心线弧长算，不是欧氏直线 |
| G-D2 | 回望走廊 | 以牌坊（footY≈6380, x≈4096）为起点向南 1200×500 矩形内，**0 件 h≥510** FAIL | 规格 2.4"牌坊到官道两侧不摆高层物件" |
| G-D3 | 祠堂–戏台对望 | 祠堂（区4）与戏台（区7）两件都落地后：两者连线上（缓冲带 ±250）无第三件 h≥510 FAIL；任一缺失时 SKIP 并点名 | 一期必然 SKIP；不许拿 SKIP 凑绿（run-gates 自证条款同款） |

## 2. 新门禁落地：脚本与 FAIL 判据

### 2.1 新脚本 `Tools/check-town-layout.py`（唯一新增门禁脚本）

G-A1~A6、G-B1~B3、G-C1~C5、G-D1~D3 共 **15 条**，全部落在这一个脚本里，理由：
输入只有两份 JSON（building-plots.json + town-map.json），判定全是几何/计数，
拆开只会变成 15 个 90% 相同骨架的文件。

设计要点（全部沿用仓内先例，不发明新轮子）：

- **入口 UTF-8 守卫** + `RESULT: PASS/FAIL` 行（check-town-config-consistency.py 同款，
  会崩的门禁比红的门禁危险）。
- **台账棘轮**（check-ui-layout.py BASELINE 同款）：WARN 起步项（G-B1/G-B3/G-C5）
  与"落地前必然红"项（G-C4）登记 `level/note`，只许销账不许放宽。
- **`--selftest`**：≥6 条合成用例（空屏必红、L3 超 6 必红、回望走廊塞大件必红、
  锚点间距 1300 必红、合规配置必绿、台账升级必红），夹具全落 `.batch/` 临时目录，
  真配置零改动（check-ui-layout.py selftest 同款结构）。
- **`--report`**：只打印不判定 rc=0，输出九区密度表/高度档分布/锚点间距链全表，
  供斐哥看图前拿数字。
- **SKIP 不许静默**：G-B2/G-D3 前置不满足时打印 `SKIP（原因）`，报告里点名，
  不计绿也不计红。

FAIL 判据汇总：任一 G-* 条 FAIL → 脚本 rc=1，`RESULT: FAIL` 并列出失败编号；
WARN 不影响 rc；台账可销账（登记了却不再违规）→ FAIL（棘轮收紧方向）。

### 2.2 既有脚本的小改（方案登记，不在本任务执行）

| 改动 | 文件 | 理由 |
|---|---|---|
| props 数据源改读 building-plots.json（或显式退役并补 SKIP 判据行） | `Tools/check-town-overlap.py` | 现在读 town-map 的空 `props`/`walkable`，恒绿 = 假绿（规格 1.6.1 契约裂缝）；run-gates 的 `--self-check` 条款（每条必须出判据字样）正是为这种"静默绿"准备的 |
| FIELDS 增补 sideRoads/farmland 已在 terrainShape 内，无需改 | `check-town-config-consistency.py` | 只点名复用 |

## 3. 走帧 flipbook 验收夹具（规格 1.5 + 移动维度）

### 3.1 旧类名引用：已修复，方案只做防回归

- 历史事故：`FrontEndShot.cs:1264` `CheckFramesMode(FrontEndCharacters controller)` 用了
  已被 partial 合并消灭的类名 → CS0246 卡死编辑器（09 日志 10-07 12:1x 段）。
  **现状已是 `CheckFramesMode(FrontEndController controller)`（:1290），本方案不改。**
- 防回归门禁 **G-M1**：类型名笔误会被 `dotnet build Tools/UnityCompileChecks --no-incremental`
  → 0 警告 0 错误直接拦下（编译器就是判据）。方案要求：**FrontEndShot 改动的验收流程里，
  Unity 批处理前必须先过 UnityCompileChecks**——这条写进六关操作手册即可，零新代码。

### 3.2 三向帧判定：夹具改造方案（FrontEndShot 内新增，旧断言一字不动）

现状：`CheckFramesMode`（:1290）只验"默认 Slices / Frames 不喂位移索引不动 / 帧数>0"三件，
**没有方向维度**；`FacingProbes`（:704）四向探针已带 `view`（0 正/1 侧/2 背）与 `mirror`，
侧档腿摆断言（:890）在 `!FramesActiveForCheck` 时采样，Frames 态三向覆盖是空白。
运行期侧（`FrontEndWorld.cs:380-407`）按当前朝向 suffix（front/side/back）查
`LoadWalkSheet` switch 取 sheet——**三向各自的 sheet 是否真接上，没有任何断言**。

新增门禁 **G-M2（夹具 `CheckFramesModeDirections`）**，挂在 FrontEndShot 世界页段，
复用 FacingProbes 的四向驱动，对每向断言：

| 断言 | FAIL 判据 |
|---|---|
| 朝向接线 | `controller.TravelerViewForShot == probe.view` 且 `p.MirrorForCheck == probe.mirror`（复用 :885 既有口径，Frames 态下同判） |
| 该向 sheet 已接 | `p.FramesActiveForCheck == true` 且 `p.FrameCountForCheck >= 8` |
| 该向帧在走 | 沿该向喂位移 ≥1 个 frameCycleDistance 后，`FrameIndexForCheck` 变化量 ≥1（T108 模式无关断言 :862 的 Frames 版延伸，与现有相位断言并存不冲突） |
| 镜像正确 | 西向（mirror=true）与东向同 sheet 但 MirrorForCheck 相反（复用既有钩子） |

配套运行期钩子（`PuppetActor` / `FrontEndWorld` 各加一个只读 `*ForCheck` 属性，
仓内 `GaitModeForCheck/FramesActiveForCheck` 同款命名，**新代码不改旧行为**）：
- `PuppetActor.CurrentFrameSheetNameForCheck`（当前 sheet 的 Resources 名，区分 front/side/back）；
- `FrontEndWorld.LoadedWalkSheetSuffixForCheck`（最近一次 LoadWalkSheet 命中的 suffix）。

**G-M3（素材覆盖闸，数据侧，落 check-town-layout.py 或独立小脚本段）**：
读 `LoadWalkSheet` 的 switch 字面量 × `Resources/Art/Characters/` 实盘文件，
断言：每个 `{id}-{gender}` 组合 front/side/back **三向各 ≥1 张**。当前实盘：
farmer/merchant/traveller 三套 × 三向 = 9 张齐（2026-10-17 实测），门禁从第一天就是真绿。
FAIL 判据：任一组合缺向 → FAIL；switch 字面量引用了盘上不存在的文件 → FAIL
（SaveChecks 孤儿棘轮的同族）。注意：RIG 线新生帧（back 8 过 6/front 8 过 3）属于
"已有 sheet 的帧级质量"，由 check-walk-sheet-motion 管，本闸只管"有没有接全"。

**G-M4（运动闸接进覆盖范围）**：`check-walk-sheet-motion.py` 现在按需单文件调用。
方案：run-gates 增加一条对 Characters 目录下全部 `*-walksheet-*.png` 批量跑
（9 张 × 每张三判据），阈值沿用现校准值（TOP_MIN=3 / CONTACT_MIN=10% / HEIGHT_DEV_MAX=0.14），
**不调阈值**；新 sheet 入库即自动进闸。RIG 线补帧落地时它自然重新判。

### 3.3 六关接入

| 门禁 | 接入点 | 方式 |
|---|---|---|
| G-A1~D3（15 条） | `Tools/run-gates.sh` 第 19 条 + `--selftest` 第 20 条 | `ALL_LABELS` 与两条 run_one 段各加一行；汇总器只认退出码与 RESULT 行，脚本自带 |
| G-M1 | 六关操作手册 | 流程条款：FrontEndShot 相关改动先 UnityCompileChecks 再批处理 |
| G-M2 | 六关之 FrontEndShot | 新断言进 `Capture` 世界页段（FacingProbes 循环内），红绿直接体现在 `suite finished, N problem(s)` |
| G-M3 | `Tools/run-gates.sh` | 若做成独立脚本则第 21 条；若并进 check-town-layout.py 则不新增条目 |
| G-M4 | `Tools/run-gates.sh` | 一条批量循环，沿用 walk-sheet-motion 自身 rc |
| G-C4/A3 等红起步项 | check-town-layout.py BASELINE 台账 | `level:'red'` 登记，与 check-ui-layout.py LAY-01~04 同款口径 |

## 4. 人眼验收边界（铁律的具体化）

机器门禁**不判**以下事项，全部归斐哥看图拍板，门禁报告里固定打印一行
"以下事项机器不判，等看图"：

1. plaza 亮度 195 的观感（`Tools/logs/map-02/plaza-after.png`，09 日志登记在等）；
2. 徽派马头墙轮廓与贴图美感（规格 2.6 明言归素材工单）；
3. D/H 比"围合感"是否成立——门禁只量几何比值，压迫感是心理量；
4. 天际线"三级同框"的视觉真实性（G-C4 是数据近似，遮挡关系只有截图看得见）；
5. 走帧动画"像不像在走"（motion 闸只排除"没在动"与"坏得离谱"，姿态美感归人）。

交付物约定：每次六关跑完，把 FrontEndShot 的世界页八张朝向图 + TerrainPropShot 镇景图
的路径列表打进门禁报告尾部，方便斐哥直接点图。

## 5. 实施分期建议

| 期 | 内容 | 前置 |
|---|---|---|
| 一期（本方案落地） | check-town-layout.py 15 条 + selftest + 接 run-gates；G-M1 手册条款；G-M3；G-M4 批量 | 无，纯 Tools 侧 |
| 二期 | G-M2 夹具断言 + 两个 ForCheck 钩子（动 FrontEndShot/PuppetActor/FrontEndWorld） | 需主架构师派单放行这三个文件的白名单 |
| 三期 | G-D3 取消 SKIP（祠堂/戏台落地）；G-B2/B3 台账销账（横巷/田埂进 terrainShape）；check-town-overlap 数据源修复 | 布局规格 §4–6 落地后 |

---

### 附：门禁编号索引

G-A1~A6 密度分区｜G-B1~B4 D/H 路网｜G-C1~C5 高度阶梯｜G-D1~D3 锚点视线（数据侧 18 条，其中 B4 为已存在项点名复用，**新增 17 条**）
G-M1~M4 移动/走帧（**新增 4 条**，M2 含 4 条断言）
合计新增 **21 条门禁**（15 条落新脚本 check-town-layout.py，2 条接 run-gates 流程，4 条移动侧）。
