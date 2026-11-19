# 阶段 B：任务头标与交还 NPC 最小接入

- 架构裁决日期：2026-11-06（Asia/Shanghai）。所有者 pE；当前先完成阶段 A，本文是下一件可直接执行的任务。
- 依据：dispatch-20261106-rpg-dialogue-and-quest-overhead.md 第 6 节；整体 NOT_APPROVED。
- 写域：Core/Quest.cs（必要时新增 Core/QuestOverhead.cs）、FrontEndWorld.cs / 对应独占 partial、相关 Quest 检查；表现与 pF 的 Dialogue UI 以回调衔接。
- 与 pF 共用的 FrontEndChapter.cs、FrontEndTrade.cs、Core/Dialogue.cs、dialogues.json 必须先明确单一写入者，再接线。不要两人同时修改。

## 已核现状与首个闭环

- QuestRuntime.Reconcile 现对达标 Active 任务自动 TryComplete → GrantRewards → RefreshAvailability；默认行为保留。
- QuestLedger.GrantRewards 自身只对 flag 幂等，铜钱和物品重复调用仍会重复发放。新交还入口必须先成功完成状态迁移才调用一次发奖；不要在头标查询或每帧刷新中调用。
- QuestLedger.ObjectivesMet 内部调用 Of，会创建/调整状态；展示查询不能因此悄悄改存档，须用只读状态与 progress 判断。
- 首个真实闭环优先复用 npc-yao-lao / quest-herbal-rescue / yaolao-intro、yaolao-rescue、yaolao-thanks。数据已包含野猪遭遇、报告药老、金疮药奖励。
- 为该任务显式配置可选交还 NPC；旧未配置任务依旧自动完成，旧已完成存档不倒退、不补领。接受任务的可见性与实际状态一致。
- 不另造演示存档状态机，不改既有奖励数量来迎合截图或测试。
- 接取时机裁决：acceptQuest 放到“我去看看”的确认选项；初问与拒绝不接任务。交付条件改为真实可交还态；奖励保留三帖金疮药，致谢文案改为三帖给玩家。

## 最小行为

1. 可选 giver/turnIn NPC 元数据采用现有解析风格；缺省空值兼容旧配置，NPC id 必须对应真实世界对象。
2. 新任务展示枚举：None / Available / InProgress / Completable；同 NPC 多任务优先 Completable > Available > InProgress > None。
3. 配置 turnIn 的任务目标达成后保持 Active，派生 Completable，停止自动结算；未配置仍沿原 Reconcile。
4. 单一交还入口校验 NPC、Active、全部目标达成，成功迁移后发奖、解锁后续、持久化一次。重复点击/重读存档不能再发奖励；拒绝错误 NPC/未达标时不改档。
5. 头标用现有 UGUI NPC/名字层，金 ! / 金 ? / 灰 ?；4px、1.2s浮动，reduceMotion静止。查询不领任务、不发奖。
6. 150–200 世界单位内点击或 E/Space 唤起同一个 pF 对话面板；超距不隔空交任务。实际角色朝向采用现有朝向能力，不另开角色系统。
7. pF UI 仅请求回调；任务接取/交还/选择效果由现有 Core/控制器入口执行。Auto/Skip 不代选分支、不重复消费一次输入。
8. 关闭/换页/换档释放世界输入锁；头标刷新依据实际任务状态，避免每帧全表扫描与临时材质增长。

## 必要验证（一次稳定输入批次）

- 未接金 ! → 接取灰 ? → 目标达成金 ? → 指定 NPC 交还 → 标识消失/转下一项；真实 Town 截图。
- 错误 NPC、未达标、距离不足拒绝交还且存档不变；纯查询不改序列化结果。
- 双击/重复交还、退出重读后再交还，铜钱/物品/flag都不重复增加。
- 无 turnIn 的旧任务继续自动完成；旧 Completed 存档保持完成。
- 一轮对应 Core 检查与离线编译后，pE 独占隔离 Unity 验证。七章其他红项不随阶段 B 通过而自动消失。
