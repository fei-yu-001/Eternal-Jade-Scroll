# A包 · 对话半身海报、竹木选项与真实交互浮签

建立：2026-11-07T01:53:45+08:00。负责人：pF + pK；当前 pF 公益渠道不可用，首个代码切片由现有 pE 会话临时接管。

证据独占目录：`Tools/logs/nov7-dialogue-rebuild-20261107/`。当前状态：已拆包；具体执行许可见队列。

## 首批 A1（当前可派发，范围有限）

先完成自己正在进行的门楼 HANDOFF 与最终源/引用审计，再读本任务。只在 `Tools/logs/nov7-dialogue-rebuild-20261107/candidate-a1/` 编辑 `DialogueUI.cs` 的完整候选及必要新增局部视觉组件，直接交付代码候选与 diff。源产品安装由 pK 负责。无需等新立绘完成即可做界面代码。

1. `DialogueUI.cs` 的局部 Button 工厂与选项外观去掉 JadeAction/button-gold，使用独立的竹木牍底和暗金细边，hover仅轻微变化；复用当前字体、Button、导航/事件和输入门闩，不修改 UiKit。
2. 薄宣对话框、暗金包角、朱砂姓名印、TMP排版，不能把对话主面板改回重金框。已有素材能复用先复用，缺失素材列明确需求，不能用廉价RGB占位冒充终稿。
3. 不改变 request token、分页、打字/自动/跳过、一次点击一次推进、Esc恢复焦点、对话输入租约或NPC暂停/释放行为；reduceMotion可静止并保留反馈。
4. 不再修改 OcclusionValidationShot，也不启动 Unity；不写 FrontEndDialogue、NpcQuestOverhead 或任何共享文件。候选编译时替换对应源清单，不把同名类编译两份。交 A1 HANDOFF 后停在接单状态，由父方调度下一切片。

## A2/A3 完整包（排队，未授予同时写入）

- 半身海报：主角/沈怀安/石头/赵大/药老；完整人物面部、RGBA、发丝边缘、底部水墨羽化、统一青衣无背篓主角身份。`Resources/Art/Portraits`现有四NPC图均RGB；替代品用新路径与资产映射，验Alpha像素分布，不能只换文件mode。
- 正式写域：`DialogueUI.cs`、`FrontEndDialogue.cs`、`NpcQuestOverhead.cs`，新增 `Dialogue*`/`NpcIdentity*` 专属组件及meta，独立肖像映射配置与经审资产；Core任务/对话规则不改。
- `FrontEndDialogue.DialogueFrame` 当前主角 TravelerArt(0) 全身图、药老强制null必须接到新肖像。缺角色时保留可解释的安全回退但列为未验收，不作全包完成。
- NPC符签按真实 None/Available/InProgress/Completable 状态显示，None不能伪装可接任务；180px内身份牌与任务符签分层。距离与已存在的NPC交互判据保持同坐标/同投影，使用真实名字和身份来源。
- 验收：所有对话分支、长台词分页、快速连点、关闭重开、保存续话；四种任务状态；180px内外与NPC巡游时头标锚点；一般/低动态；1280/1920比例下文字与立绘不截断。

## 共用执行与交付约束

- 上位任务：`Tools/tasks/dispatch-20261107-qiqi-aesthetic-rebuild.md`（2026-11-07 01:45，制作人批准）；总调度：`me/2026-11-07-六包重构执行队列.md`。本拆包只细化实现/验证，不降低视觉目标。
- 先核对本包写域及源 SHA；已有其他人的更改必须保留。公共 `UiKit.cs`、`MemoVisualAssets.cs`、`FrontEndController.cs`、`FrontEndWorld.cs` 由 pK 合并；协作者只在本包 candidate 目录给出这些文件的窄补丁，不直接覆盖。
- pE 当前门楼批次的 post-run 源审计及 HANDOFF 完成前，所有产品文件冻结。候选、任务和独立证据可写日志。Unity 仅 pE 排队运行，主 Unity PID12028 保持。
- 涉及 C# 必须实际运行 `dotnet build Tools/UnityCompileChecks` 并记录退出码、错误/警告、源码 SHA；候选阶段可使用冻结副本编译，正式合入后仍须此门禁。离线编译不等于截图/输入/动效已通过。
- 每包交付 HANDOFF.md、changed-paths、before/after SHA、必要图证和残留项。截图预览最长边1280 JPEG；原始图证保留。所有旧失败独立保留；不改断言拼绿，不恢复旧 Dialogue02 入口。
- 不 Git add/commit/push，不改全局供应商/代理/凭据，不请求已放弃公益渠道或配额已耗尽渠道。产品图新路径输出，经 pK 审查后安装并保留旧图备份；不以RGB草稿、改名或静态平移冒充新图/走帧。
