# F包 · 国风HUD四区落地

建立：2026-11-07T01:53:45+08:00。负责人：pK统筹UI；先完成A/B第一批再接入。

证据独占目录：`Tools/logs/nov7-guofeng-hud-20261107/`。当前状态：已拆包；具体执行许可见队列。

- 唯一视觉定版：`me/hud-design/guofeng-hud-concept-grok-style.jpg`，1376×768。总案第八章旧的 `me/ui-concepts/` 链接不作当前取图入口；不修改首席总案。
- 正式写域：`FrontEndMap.cs`、新增HUD专属组件/配置/经审资产；`FrontEndWorld.cs`仅小地图构建/位置更新/布局锚点由pK串行合入；任务追踪来自真实QuestLedger/Chapter状态。
- 四区：白玉命盘与朱砂/碧玉条；青铜八卦星盘且玩家点坐标真实；左侧窄丝帛任务追踪；右下九瓣莲轮，装饰瓣非假按钮，功能瓣只绑定已有真实入口。
- 已核实的数据依赖：SaveModels.SaveData无realm/灵力字段，城镇HP目前只有PlayerStatsFor上限，SP现状为静态展示且与战斗stamina不同，不能静默改成“当前灵力”。不硬编码筑基期/满HP满SP；境界无数据时采用诚实未定文案或不显示等级，真实境界/灵力完整实现仍PENDING。若要建立新修炼/跨场景生命系统，需另作数据契约与迁移设计，不能作为UI重构顺手伪造。
- 已确认入口：行囊ShowInventory、全图ShowWorldOverlay；属性/技能必须先查真实功能能力，缺失项不能绑定空回调、错误页面或只弹“敬请期待”。可保留无点击装饰瓣但完整四功能交付仍PENDING。
- 验收：坐标与比例、任务变更刷新、背包/地图输入及返回、世界点击不穿透、对话/模态时关闭导航；常规/低动态与宽高比例；不得让华丽HUD遮住当前对话与必要任务信息。

## 共用执行与交付约束

- 上位任务：`Tools/tasks/dispatch-20261107-qiqi-aesthetic-rebuild.md`（2026-11-07 01:45，制作人批准）；总调度：`me/2026-11-07-六包重构执行队列.md`。本拆包只细化实现/验证，不降低视觉目标。
- 先核对本包写域及源 SHA；已有其他人的更改必须保留。公共 `UiKit.cs`、`MemoVisualAssets.cs`、`FrontEndController.cs`、`FrontEndWorld.cs` 由 pK 合并；协作者只在本包 candidate 目录给出这些文件的窄补丁，不直接覆盖。
- pE 当前门楼批次的 post-run 源审计及 HANDOFF 完成前，所有产品文件冻结。候选、任务和独立证据可写日志。Unity 仅 pE 排队运行，主 Unity PID12028 保持。
- 涉及 C# 必须实际运行 `dotnet build Tools/UnityCompileChecks` 并记录退出码、错误/警告、源码 SHA；候选阶段可使用冻结副本编译，正式合入后仍须此门禁。离线编译不等于截图/输入/动效已通过。
- 每包交付 HANDOFF.md、changed-paths、before/after SHA、必要图证和残留项。截图预览最长边1280 JPEG；原始图证保留。所有旧失败独立保留；不改断言拼绿，不恢复旧 Dialogue02 入口。
- 不 Git add/commit/push，不改全局供应商/代理/凭据，不请求已放弃公益渠道或配额已耗尽渠道。产品图新路径输出，经 pK 审查后安装并保留旧图备份；不以RGB草稿、改名或静态平移冒充新图/走帧。
