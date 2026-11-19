# 参考项目心得 · 《长风》long-wind

拉取时间：2026-10-05｜本地路径：`Reference/long-wind`（242 MB，已 gitignore，不入库）
来源：`https://github.com/jbang2004/long-wind.git`

## 0. 授权红线（先看这条，其它都可以慢慢看）

`LICENSE:1-26` 是 MIT，**但只覆盖源码**；`LICENSE:23-26` 明确排除：
`public/assets/models/*.glb`（Tripo AI 生成）与 `public/assets/anims/mixamo.glb`（Adobe Mixamo）
**不是 MIT**，`CREDITS.md:30-38` 写了"不要提取、不要再分发"。Poly Haven 是 CC0、字体是 OFL。

⇒ 对我们意味着：**读架构、读技法可以；任何一个 `.glb`、任何一张图、Mixamo 重定向的产物，都不许进我们的仓库。**
这条已经写进本文件，是为了防止后面某个执行会话"顺手拿个模型试试"。

## 1. 它到底是什么（先破一个误解）

**不是**修仙 RPG，**不是** 2D，**不是** Unity。它是浏览器里的 **3D 第三人称武侠动作游戏**
（类只狼：轻/重连段、弹反、无敌帧闪避、架势击破、处决），three.js r186 + WebGL2 + 纯 JS ES module + Vite + GLSL。

- README 自称 ~38,000 行 —— 我数了 `src/**` + `tools/**`：**38,802 行 / 154 个源文件**，**没吹牛**。
- 运行时依赖只有一个（three），其余是 vite/playwright 开发依赖。
- 三章内容（`src/levels/{steppe,bamboo,town}.js`），**完全没有 RPG 层**：
  全仓 grep `dialogue|inventory|cultivat|alchemy` → 0 命中。没有任务、没有物品、没有带作息的 NPC。
- 只有 1 个 commit（发布前压扁过），所以"维护性"这件事无法从历史推断。
- README 自陈约 28 小时、AI 写的。**代码质量比预期好**：全仓 0 个 `TODO/FIXME/HACK/XXX`，
  注释密度 12.5% 且注释在解释"为什么"（例：`quality.js:6-8` 解释为什么每个画质档都强制开 TAA——
  中途换 AA 会重编译最终 pass）。但也有边界：`src/world/` + `src/character/` 占了 57% 代码，
  全是渲染/几何/数学，**是一个方向的深度，不是整体深度**；无 lint、无 tsconfig，纯无类型 JS。

**结论：它是"过程与可测试性"的参考，不是"题材"的参考。** 我们不该学它的玩法，该学它怎么把
"声明了却没人用"变成测试失败。

## 2. 最值钱的一条：契约数组 + 双向覆盖测试

它的动画 clip 有一张**显式契约表** `CLIP_NAMES`，然后：

- 加载器遍历 `CLIP_NAMES ∪ Object.keys(SPECS)`，凡是"声明了但没做"的直接
  `console.warn('[anim] clip X not authored — using a placeholder')`（`src/character/clips/index.js:20-24, 38-44`）；
- 测试**双向**断言（`src/game/test/run.mjs:106-118`）：
  契约表里每个 clip 必须有 gameplay meta，**且**每条 fallback 必须对应契约表里的 clip——
  两个方向都覆盖，多一个少一个都红；
- `tools/mixamo/convert.mjs` 更狠："只发布表格里真正用到的键"（`CONTRACTS.md:141`）——
  **这条管线在物理上不可能发布未使用的动画**。

### 为什么这条对我们特别值钱

我们这一周反复在登同一类债，我给它起了个名字叫**"能力存在≠功能存在"**，已经实证 6 次：

| 债 | 现象 |
|---|---|
| T038 | `building-plots.json` 的 `radius` 写了但运行期没人读，房子全是空气 |
| T043 | `KarmaTracker.Record` 写完了，全仓零生产调用点 |
| T057 | `MemoryEcho.Discount` 折扣函数在，成交路径根本不经过它 |
| T091 | 分层纸偶 `PuppetActor` 写得很好，世界页对它零引用 |
| T100 | `RareDropRateBonus` 只有夹具在调 |
| T052 | `enemies.json` 加了两种妖，世界上没有一条遭遇入口 |

**它的 `run.mjs:117-118` 就是把这类事变成测试失败的机制。** 我们完全可以照搬概念：

> 一条编辑器测试：配置里每个 `NpcId` / `DialogueNodeId` / `ScheduleId` / `EnemyId` / `ItemId`
> 必须能解析到运行期消费者，**且**每个运行期处理器必须在配置里有对应条目；
> 任一侧缺失就 FAIL，不是 WARN。

这比我们现在"发现一个登一条 T"要主动得多。我把它列进下一轮候选（见 §5）。

## 3. 第二条：动画过渡的 inertialization（直接关系到"走路僵硬"）

它的每帧姿态管线（`src/character/animator.js:1-27`）顺序是：

>步态（程序化 locomotion 姿态）→ 动作 clip（Hermite 关键帧轨）→ 层混合（全身 / 上半身遮罩）
> → **inertialization（五次多项式偏移衰减，过渡时保留速度）** → 加性层（转向/加速倾斜、呼吸、
> 待机重心转移、look-at）→ 脚在世界里踩实（锁定、重新踏步、地形倾角）→ 骨盆 reach 修正
> → 两骨 IK → 骨骼

两点要学：

1. **inertialization**（它注明出处 Bollo, GDC 2018，`animator.js:44-48`）是"手感"最大的单一来源，
   它解决的是**过渡瞬间速度被清零**的顿挫感。我们 P 线（主角形象与动作）现在的问题正是这个：
   `PuppetActor` 的 `SetMotion` 上一轮就被抓到"每帧重置相位导致走路腿不摆"（日志 :1085），
   而且切换 Idle/Walk/Run 是硬切。**在 Unity 2D 里这可以表现为：切换动作时对旋转/位移偏移做
   衰减插值，而不是直接换一套常量。**
2. **加性层的思路**：待机重心转移、呼吸、转向倾斜是**叠加**在基础姿态上的，不是另做一套动画。
   我们 idle 现在只有颈 .9° + bob 1.6°，几乎等于静止——按这个思路，idle 应该是"走路姿态的衰减版 + 呼吸 + 重心漂移"。

还有一条设计决定值得抄：`player.js:9-11` 说攻击位移由**玩法数据** `meta.lunge` 驱动，
**故意与 root motion 解耦**，"这样不管动画怎么改，战斗手感一致"。这对我们同样适用——
我们的战斗是数值结算 + 表现层，表现层绝不该反向决定位移。

## 4. 其它三条值得记的技法

- **`src/world/citizens.js`（741 行）的"按距离分频 tick"**：10 米内每帧驱动动画器，22 米内每 2 帧，
  36 米内每 3 帧，更远每 4 帧，46 米外隐藏；布料模拟只给最近的 2 个人。
  我们只有 6 个 NPC，用不上；但**它的 `setPanic(k)` 恐慌传播**（`citizens.js:198` 每个市民有各自的
  `fear` 反应延迟，由总线事件驱动）正是"NPC 对玩家行为有反应"的原语——我们缺的就是这个，
  而且它比我们的"作息表 + 记忆阶"更即时。
- **`src/game/test/fakes.mjs`**：`FakeAnimator` / `fakeCharacter` / `fakeApp` 满足美术层的契约接口，
  于是**玩法逻辑测试完全不需要渲染**，文件头写着"美术区重做期间测试保持通过"。
  我们其实已经有一半（`Tools/SaveChecks` 是纯 C# 无 Unity 门禁，L 线还专门把判据下沉过去），
  缺的是另一半：**表现层契约的假实现**，让 `CombatEngineShot` 那种"必须开真页面"的断言
  也能有纯逻辑版本（T052 的根因之一就是纯逻辑层测不到世界入口）。
- **`tools/shot.mjs`（62 行）**：无头 GPU 截图，等 `window.__ready`、可注入 JS 设相机/状态、
  dump fps/drawcall，**console error 或 page error 就 exit 2**。
  这恰好是我们 T055 的反面教材：我们的 Unity 批处理**退出码恒 0，哪怕整条链没编译**（T044），
  也哪怕截出来是纯色纸。我们已经用"只认日志里 `suite finished, 0 problem(s)`"补了，
  但它这个"**出错就非零退出**"的思路更根本——值得在我们的 `Tools/batch-verify-*.sh` 里实现：
  截完图自己算 md5、比体积、不一致就返回非零。

## 5. 我作为架构师的决定（不抄代码，只落机制）

| 采纳 | 落到我们哪里 | 排期 |
|---|---|---|
| 契约数组 + 双向覆盖测试 | 新建一条编辑器/控制台判据：NPC/对白/作息/敌人/物品 id 双向对表 | **下一轮 P 线之后第一批**，能一次性灭掉 T038/T043/T052/T057/T100 这一族 |
| inertialization + 加性层 | `PuppetActor`（T091，P 线核心） | 下一轮 P 线，直接写进任务书 |
| 出错即非零退出的截图 harness | `Tools/batch-verify-<线>.sh`（ADR-036.8 已要求脚本化） | 本轮巡检之后我来定 |
| 表现层假实现（fakes） | 战斗/世界入口的纯逻辑断言层 | 下一轮 |
| 文档二分法：一本"观感/技法/数值"（BIBLE）+ 一本"路径/所有权/API"（CONTRACTS，且**自带漂移对账段** `CONTRACTS.md:39-51`） | 我们的 `架构文档.md`（技法+裁决）与 `20_项目状态板.md`（当前节点+所有权）已经是这个形状 —— **它验证了我们的做法**，但它那份"契约文件自己承认哪里已经和代码不一致"的对账段我们没有，值得加 | 下一份交接包我加一节"已知漂移对账" |

**明确不学**：所有渲染/后处理/地形/草地/WebAudio 合成（3D 与 WebGL 专用，Unity 2D 用不上，
而且素材本来就不许碰）；把 `citizens.js` 当 NPC 设计模型（它是**即时氛围反应**，没有记忆、
没有作息、没有关系网，恰好不回答我们的核心承诺）；实时动作战斗的数值（`combat.js:27-33` 的
`FEEL` 表，我们路线是回合制，只有 hitstop/震屏这套**反馈词汇**能借）。

## 6. 一句话

它 38,802 行里没有一行能直接搬进我们的 Unity 2D 修仙项目，
但 `run.mjs:117-118` 那两行断言，恰好是我们这一周用六条技术债换来的那个教训的机器版。
**学它的不是代码，是"不允许任何东西只声明不使用"这个立场。**
