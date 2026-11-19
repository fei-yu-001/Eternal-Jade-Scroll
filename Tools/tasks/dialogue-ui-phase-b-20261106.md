# 阶段 B：双人剧情对话 UI 最小接入（2026-11-06）

- 所有者 pF；依据 dispatch 第 6 节及 quest-overhead-phase-b-20261106.md；整体 NOT_APPROVED。已收到 pE 隔离同步冻结完成通知及父方四文件实现放行；公开 UI 已落盘，现有入口/Core 接线归 pE。
- 下文 P=`GameClient/TwelveJade/Assets/Scripts/Presentation`，C=`GameClient/TwelveJade/Assets/Scripts/Core`，R=`GameClient/TwelveJade/Assets/Resources`。

## 独占范围与依赖

- pF 独占新增 `P/DialogueUI.cs`、`P/DialogueUIModel.cs` 及各自 `.meta`（本轮已创建），以及本文；不增加自动启动组件、演示场景或另一套存档状态机。
- pE 主责 Core 任务、NPC 接近/点击/E 交互及世界输入接线；FrontEndChapter.cs / FrontEndTrade.cs / C/Dialogue.cs / R/Config/dialogues.json 当前不双写，单一写入者由 pK 指定。
- 不动阶段 A 冻结产品/验收、TownMemoIntegrationShot、地图、Packages；不启动 Unity、不提交 Git。现有入口在分配接线前保持原状。
- 复用 TwelveJade.Presentation（Core、UGUI、TMP、InputSystem 已引用），UiKit.Label/JadeFrame/Button、现有字体/卷轴配色和 Overlay 层；不新增同名 DialogueNode。

## 公开接口与数据流（已实现，供 pE 接线）

- `DialogueUI.Open(RectTransform parent, UiKit kit, DialogueUIFrame frame, DialogueUICallbacks callbacks, Func<bool> reduceMotion)`：挂宿主指定层，取得输入锁后显示。
- `Present(next, responseTo)` 更新并确认一次请求：responseTo 必须是 Choose/Advance 回调收到的本次 PendingRequestToken，不是 CurrentToken；`Reject(requestToken, message)` 仅用于无效果或已完整回滚的失败；`HandleInput(command)` 接宿主单次路由；`Close(reason)` 可重复调用且只清理/通知一次。
- Frame 由既有 DialogueView 构造不可变显示快照，携带唯一帧 token、玩家/NPC 姓名及借用 Texture2D/UV、说话侧、可见选项与其原 Options 索引、宿主确认的线性后继许可；不持有 SaveRepository 或任务可变状态。
- Callbacks：`AcquireInputLock(): IDisposable`、`Choose(requestToken, sourceOptionIndex)`、`Advance(requestToken)`、`Closed(reason)`；动作结果由宿主 Present/Reject 回送，关窗后拒收旧 token。
- 两个旧入口最终共用面板；宿主沿 DialogueRules.Open/Resume、DialogueTree.BranchOptions、ActionOf/AcceptQuestOf 筛选并执行现有效果，保留原选项身份/索引。
- 宿主负责 Choose → 既有任务/善行处理 → DialogueRules.Snapshot/Save → 同一 SaveData 持久化；UI 不领奖、不自行接受任务、不另写对白存档。
- 画面为玩家左、药老右、底部卷轴正文与选项；选中玩家话语可回显为玩家侧的一句，再显示 NPC 新节点，回显不重放效果。说话侧切换 0.15 秒非缩放时间渐变，未说话侧压暗。
- 正文 TMP 逐字显示，自动换行/必要分页；正文、名字、选项与按钮不重叠，不用省略号代替正文；实际字形可见性纳入后续实机验收。

## 输入、Skip / Auto、reduceMotion 与清理

- 全屏 raycast 遮罩阻止点穿；parent 层由宿主保证在世界/HUD 上方、系统确认与提示下方，不硬编码绝对排序值。
- 输入租约必须让宿主同时屏蔽移动、地图/背包热键、NPC 重入、点地与自动寻路；当前 UpdateWorld 直接读键盘，CanvasGroup/modal 遮罩本身不足。
- 进入时由宿主 CancelTravel 并清零移动惯性；E/Space/点击唤起的同次输入不能继续推进；退出输入在释放后再恢复世界，避免同帧穿透。Esc 只由最上层消费者处理。
- 单次输入先补全文字，下一次才推进；每个 token 最多一个待处理请求，双击/键鼠同时触发不能重复 Choose；失败显式 Reject，不伪装完成。
- Auto 只在全文显示并停留后请求已确认线性后继；有分支即暂停等待人工选择，单个可见选项也不自动当作安全后继。
- Skip 补全文字后只沿宿主确认的非分支路径逐次 Advance；到分支、接取/交还/交易等需确认动作、末尾或失败立即停止；不跳过 Core 效果，不做同步无限循环。
- reduceMotion 实时生效：关闭呼吸/浮动，0.15 秒过渡直接落终态、正文可立即全文；Auto 停留/选项交互仍有效，不更改节点与任务语义。
- 关闭、换页/场景、读档、宿主销毁、OnDisable/OnDestroy 均停打字/Auto/Skip、注销回调与监听、清 pending token、Dispose 租约并恢复有效焦点；异常路径同样释放。
- 只销毁本 UI 新建对象/临时资源；Resources 借用纹理与共享字体不 Destroy；不留下事件订阅、输入锁或隐藏运行的协程。

## 首个真实 NPC / 任务数据（与 pE 对齐药老）

- R/Config/town-map.json：`npc-yao-lao`（药老），位置 (5150,5430)；NPC 定义称“北山药圃的悬壶郎中”。使用实际 Town 对象及当前玩家身份。
- `yaolao-intro`：“抓药还是问事？山里的草药通人性，你待它善，它便救你的命。”；人工选“（药圃解围）后山是不是出事了？”→ yaolao-rescue，现有 action=acceptQuest / quest-herbal-rescue。
- `yaolao-rescue`：童子被东郊后山野猪妖困住；人工选“我去看看。”→ yaolao-accept，grantFlag=herb-rescue-accepted、Helped(2)；另一真实分支“这我管不了。”→ yaolao-bye / RefusedHelp。
- `yaolao-accept`：带刀前往、收取胆囊与草药；选“我这就去。”→ yaolao-bye。返回使用 yaolao-intro → yaolao-thanks →“药老客气。”→ yaolao-bye，Helped(3) 只执行一次。
- R/Config/quests.json：`quest-herbal-rescue`“药圃解围”，Available；目标 defeat encounter-boar-01 与 talk npc-yao-lao 各 1；奖励 jinchuangyao×3、flag herb-boy-saved。
- 已知接线差异：当前交付选项 condition=questDone:quest-herbal-rescue；阶段 B 由 pE 统一条件/入口，UI 不强开分支。父方已裁定接取移至“我去看看。”确认选项，不在初问后山接取；pE 单一修改既有数据。
- 已知文案差异：yaolao-thanks 写“两帖你拿着”，奖励表给 3；父方已裁定奖励仍为 3，接线时由 pE 把致谢改为三帖交给玩家；本轮 UI 不双写对白表。

## 可复用资源与确切缺口（已读文件并看图）

| 用途 | 现有证据 | 最小处置 |
| --- | --- | --- |
| 玩家 | R/Characters 三个 CharacterPreset；R/Art/Characters/Stand 下 12 张性别/面容正面图；已看 farmer-male-0-front，427×720、有透明通道 | 用 Facing(0, gender, faceStyle) 按当前存档身份取图，裁上半身；portrait-frames.json 是脸部裁切表，不能直接当半身框；本轮不申请玩家新图；Texture/UV 通过 SetPortrait 可替换 |
| 药老头像 | R/Art/Portraits/npc-yao-lao.png，512×768 RGB、无透明；实际只剩局部五官/衣领轮廓，脸与躯干不完整 | 不作为合格半身像；传 null Texture，只留姓名牌与空位；缺图只记证据 PENDING，不显示实现说明 |
| 药老世界图 | town-map 实指 Art/Characters/Stand/merchant-male-1-front；已看图，为青年商人通用全身图 | 保留世界现状，不能宣称它满足药老老年郎中半身像要求；Art/NPC 目录也无药老替代图 |
| 卷轴/边框/字体 | UiKit 现成代码与字体 | 直接复用，不申请素材、不新增 SpriteRenderer 层 |

- 赠金需求仅登记 1 项：药老中性半身像 1 张，完整头肩胸、老年郎中辨识、透明背景、沿现有配色；父方一次生成的候选仍 HOLD（Tools/logs/dialogue-portrait-20261106/validation/candidate-report.json），未安装；UI 不引用 raw/旧破损头像/HOLD 候选，双人立绘视觉仍 PENDING。

## 集成风险与后续验收

- Core Choose 的社交效果并非天然重复安全；租约/token 只能防 UI 重入，任务跨读档唯一结算仍归 pE。现有 DialogueView 无 Next，宿主需查既有 DialogueNode.Next 并落实 Advance；未接通时不得虚报线性推进可用。
- pE 唯一实机验真实 Town 药老多轮、接取/分支停止/返回交还/保存读回、重复输入无重复效果、关窗恢复移动、reduceMotion 与完整正文；画面或素材不足如实保留 FAIL/待评，独立面板演示不算主游戏闭环。本轮仅四个新增 UI 文件与交付记录；离线编译/模型检查结果见 dialogue-phase-b-20261106/READY.json，尚无 Unity 实机验收，整体 NOT_APPROVED。
