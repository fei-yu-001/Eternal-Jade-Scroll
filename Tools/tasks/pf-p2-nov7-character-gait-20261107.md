# D包 · 角色同源走帧与NPC资产独立

建立：2026-11-07T01:53:45+08:00。负责人：pF / p2；当前p2配额耗尽，暂由pK保存合同，等待可用执行名额。

证据独占目录：`Tools/logs/nov7-character-gait-20261107/`。当前状态：已拆包；具体执行许可见队列。

- 第一阶段仅在本包candidate内改候选：`PuppetActor.cs`、`FrontEndNpcPatrol.cs`、NPC专用配置/帧映射、新角色动画资产及meta。`FrontEndWorld.cs`、角色Preset公共加载只交窄补丁，父方统一整合。
- 主角唯一形象：青衣长衫、腰袋、无背篓；侧向同一人物8帧，检查轮廓/脚点/比例/周期连续，不以镜像错服饰或替换身份冒充同源。保留已有正背方向问题为明确PENDING。
- 当前 `PuppetActor.cs:650`附近归零仅在 !deform && gaitMode!=Frames 的Slices分支，注释说明同一腿带叠印保护；Frames与MeshDeform有不同路径。先实证当前真实模式及消失/撕裂风险，再修真正缺帧/错误分支，不能按行号盲删保护。
- NPC独立身份配置与主角Preset脱钩；4帧局部踱步/呼吸，巡游步速与帧节奏相合，站立停止步周期；不能使用单张RawImage平移冒充帧动画。角色/NPC碰撞、头标锚点、门楼遮挡与深度排序均不可回退。
- 验收：八帧/四帧真实切换图证、静止与正常/奔跑周期、侧向左右/前后切换、NPC对话暂停与恢复；禁背篓资产引用扫描+实际图证。未审资产继续HOLD。

## 共用执行与交付约束

- 上位任务：`Tools/tasks/dispatch-20261107-qiqi-aesthetic-rebuild.md`（2026-11-07 01:45，制作人批准）；总调度：`me/2026-11-07-六包重构执行队列.md`。本拆包只细化实现/验证，不降低视觉目标。
- 先核对本包写域及源 SHA；已有其他人的更改必须保留。公共 `UiKit.cs`、`MemoVisualAssets.cs`、`FrontEndController.cs`、`FrontEndWorld.cs` 由 pK 合并；协作者只在本包 candidate 目录给出这些文件的窄补丁，不直接覆盖。
- pE 当前门楼批次的 post-run 源审计及 HANDOFF 完成前，所有产品文件冻结。候选、任务和独立证据可写日志。Unity 仅 pE 排队运行，主 Unity PID12028 保持。
- 涉及 C# 必须实际运行 `dotnet build Tools/UnityCompileChecks` 并记录退出码、错误/警告、源码 SHA；候选阶段可使用冻结副本编译，正式合入后仍须此门禁。离线编译不等于截图/输入/动效已通过。
- 每包交付 HANDOFF.md、changed-paths、before/after SHA、必要图证和残留项。截图预览最长边1280 JPEG；原始图证保留。所有旧失败独立保留；不改断言拼绿，不恢复旧 Dialogue02 入口。
- 不 Git add/commit/push，不改全局供应商/代理/凭据，不请求已放弃公益渠道或配额已耗尽渠道。产品图新路径输出，经 pK 审查后安装并保留旧图备份；不以RGB草稿、改名或静态平移冒充新图/走帧。
