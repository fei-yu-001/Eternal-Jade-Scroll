# C包 · 名牒卷轴与入世仙印

建立：2026-11-07T01:53:45+08:00。负责人：pF / UI；A先完成再排队。

证据独占目录：`Tools/logs/nov7-save-scroll-20261107/`。当前状态：已拆包；具体执行许可见队列。

- 写域：`FrontEndCharacters.cs`、`MemoScrollOrnament.cs`、`ScrollSelectAura.cs`、`MemoBackdrop.cs`、`PosterCardView.cs`及本包新增名牒专用组件/资产。共享UiKit/MemoVisualAssets只交窄补丁给pK。
- 双道绕行金芒、稀疏飞星；白玉佩与翠绿流苏分层微摆；朱砂立体印章；入世主动作换凌云赤金仙印。参照 `me/ui-concepts/scroll-ideal-golden-contrast.jpg`，不以加亮旧金饼代替重构。
- reduceMotion冻结飞星/摆动但选中标识清晰；切卷立即响应；动效组件与材质销毁/复用要可核查。
- 保留当前已定命名与真实数据：命格、铜钱、日期、存档损坏/未来版本提示、仅删除选中卷且二次确认、草稿不重掷、新旧档正常直达城镇。任务书“踏入仙途”为视觉定位，不能擅自替换所有已定产品文案。
- 验收：空档/旧档/损坏档/未来档/删除取消与确认；所有14个名牒信息域与键鼠导航；常规/低动态；金芒不能挡正文或遮住邻卷。

## 共用执行与交付约束

- 上位任务：`Tools/tasks/dispatch-20261107-qiqi-aesthetic-rebuild.md`（2026-11-07 01:45，制作人批准）；总调度：`me/2026-11-07-六包重构执行队列.md`。本拆包只细化实现/验证，不降低视觉目标。
- 先核对本包写域及源 SHA；已有其他人的更改必须保留。公共 `UiKit.cs`、`MemoVisualAssets.cs`、`FrontEndController.cs`、`FrontEndWorld.cs` 由 pK 合并；协作者只在本包 candidate 目录给出这些文件的窄补丁，不直接覆盖。
- pE 当前门楼批次的 post-run 源审计及 HANDOFF 完成前，所有产品文件冻结。候选、任务和独立证据可写日志。Unity 仅 pE 排队运行，主 Unity PID12028 保持。
- 涉及 C# 必须实际运行 `dotnet build Tools/UnityCompileChecks` 并记录退出码、错误/警告、源码 SHA；候选阶段可使用冻结副本编译，正式合入后仍须此门禁。离线编译不等于截图/输入/动效已通过。
- 每包交付 HANDOFF.md、changed-paths、before/after SHA、必要图证和残留项。截图预览最长边1280 JPEG；原始图证保留。所有旧失败独立保留；不改断言拼绿，不恢复旧 Dialogue02 入口。
- 不 Git add/commit/push，不改全局供应商/代理/凭据，不请求已放弃公益渠道或配额已耗尽渠道。产品图新路径输出，经 pK 审查后安装并保留旧图备份；不以RGB草稿、改名或静态平移冒充新图/走帧。
