# E包 · 牌坊宗祠戏台粮行场景治理

建立：2026-11-07T01:53:45+08:00。负责人：pM / pE；pM渠道不可用，布局候选由pK接管，pE唯一运行验收。

证据独占目录：`Tools/logs/nov7-town-rebuild-20261107/`。当前状态：已拆包；具体执行许可见队列。

- 正式配置写域：`GameClient/TwelveJade/Assets/Resources/Config/building-plots.json`、`town-map.json`；分包布局/美术候选先写本包candidate。缺额外schema能力先报pK，不能擅动公共Core/摄像机/导航算法。
- 牌坊补穿行官道、抱鼓石、迎客松，保证净空及自然寻路；宗祠封闭院墙、清门前碎石、双狮置须弥座两侧并按真实投影脚点对齐；戏台替换水泥灰模为木结构重檐朱柱围栏门幔；粮行删除 `clutter-millstone-well`，摊位收边。
- 禁用旧 `Tools/qingshi-map/build_plan.py --install` 覆盖现双狮。原100物件为历史模式断言；删除石磨要给新版有版本标识的清单/验收，旧失败和原报告不覆盖，不把预期数偷偷改小。
- 先复评 `Tools/logs/nov6-stall-npc-audit-20261107/HANDOFF.md`：阿杏/何二娘/胡三菜布皮货位置候选与新摊位耦合；干货没有可硬迁现NPC，不能篡改身份/商人数据。四摊不同货物资产须实图，重算透明脚点和footprint。
- pE统一冻结/编译/同步后隔离实机：新档自然到达三组区域、玩家与全景完整同框、出入口/台阶/岸边/摊位通行与NPC接近/巡游；场景美术截图+真实轨迹；不传送后称自然到达。
- 场景更改后保留旧布局20FAIL/4WARN与旧寻路40000展开风险，不因局部美术完成宣称全镇批准。

## 共用执行与交付约束

- 上位任务：`Tools/tasks/dispatch-20261107-qiqi-aesthetic-rebuild.md`（2026-11-07 01:45，制作人批准）；总调度：`me/2026-11-07-六包重构执行队列.md`。本拆包只细化实现/验证，不降低视觉目标。
- 先核对本包写域及源 SHA；已有其他人的更改必须保留。公共 `UiKit.cs`、`MemoVisualAssets.cs`、`FrontEndController.cs`、`FrontEndWorld.cs` 由 pK 合并；协作者只在本包 candidate 目录给出这些文件的窄补丁，不直接覆盖。
- pE 当前门楼批次的 post-run 源审计及 HANDOFF 完成前，所有产品文件冻结。候选、任务和独立证据可写日志。Unity 仅 pE 排队运行，主 Unity PID12028 保持。
- 涉及 C# 必须实际运行 `dotnet build Tools/UnityCompileChecks` 并记录退出码、错误/警告、源码 SHA；候选阶段可使用冻结副本编译，正式合入后仍须此门禁。离线编译不等于截图/输入/动效已通过。
- 每包交付 HANDOFF.md、changed-paths、before/after SHA、必要图证和残留项。截图预览最长边1280 JPEG；原始图证保留。所有旧失败独立保留；不改断言拼绿，不恢复旧 Dialogue02 入口。
- 不 Git add/commit/push，不改全局供应商/代理/凭据，不请求已放弃公益渠道或配额已耗尽渠道。产品图新路径输出，经 pK 审查后安装并保留旧图备份；不以RGB草稿、改名或静态平移冒充新图/走帧。
