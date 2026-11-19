# p2 · NPC 动态阻挡与导航（2026-11-06）

用户已明确授权本次为 pi 新开会话、在既有 Acomb-ds-v41 选择 claude-opus-5-5 做高级研发。本任务替换你原先“仅小台账”的限制；其他全局 provider/model/代理及默认设置不动。父架构师 wH:pK；pE 是唯一 Unity 执行者，pF 对话UI，pM 巡游模块与窄审计，pG 不动。

项目：Unity UGUI 2.5D 城镇，Core 禁止 Unity 引用；NPC 身份/日程/存档既有，不重造。整体 NOT_APPROVED，连续高度场已取消。先读 Tools/logs/npc-local-patrol-20261106/HANDOFF.md 和 Core/NpcLocalPatrol.cs，再只读相关 TownMap/TownNavigator，避免历史大日志、Library及全仓diff。

## 交付与独占写域

仅改 GameClient/TwelveJade/Assets/Scripts/Core/TownMap.cs、TownNavigator.cs；可新增 Tools/NpcDynamicBlockerChecks/ 与 Tools/logs/npc-dynamic-blockers-20261106/。pE 已预留 TownMap；父方同步预留 Navigator。其余产品文件、配置、存档、美术禁止改；不要Git add/commit/push、不起Unity、不安装包或改凭据。

修复 TownMap.Blockers 固定用 NPC 原X/Y 带来的旧位空气墙和自身挡住寻路起点。按原NPC ID管理本次Town临时动态圆心，给兼容旧调用的站立/扫掠/寻路API增加明确的排除自身NPC参数。默认玩家查询仍阻挡全部NPC；排除仅匹配的一个ID，所有其他NPC、道具圆、矩形、胶囊、地面/水域/边界判定保持。不要临时删掉全部NPC碰撞或切全局模式。TownNavigator 所有起终点、邻边、直线合并均用同一查询上下文。保留既有签名兼容（优先重载或可选末参）。动态位置必须有限有效，未知ID不能伪造实体；明确失败行为。单NPC清除/全清后恢复原锚点，跨场景不残留、不写存档。不要以导航回调同步位置；宿主每次实际位移成功后更新。

先在10行内定下API给父方与pE（Herdr agent prompt wH:pE），再直接实现，不等再次批准。pM模块回调均为(sx,sy,tx,ty)，findPath返回IReadOnlyList<(float x,float y)>，canTraverse返回bool，pE将闭包捕获NPC ID。活动半径独立于MapNpc.Radius。

只做有意义的窄回归：默认静态兼容；自排除不排别人；旧位释放新位阻挡；障碍真实扫掠/窄路与长段；未知ID和NaN/Infinity；重置与两个NPC动态更新；用真实NpcLocalPatrol+TownNavigator证明可从自身圆心行走仍避别人。一次离线编译、冻结相关输入摘要；失败留原证据，不重复全套地图。报告确切文件、API、结果、SHA与Unity PENDING，≤20行HANDOFF，收口后短报父方。不要顺手拓展寻路算法。


最终执行模型：用户在Opus首次403后明确指定 Acomb-ds-v41/deepseek-v4.1-flash；已完成52检查及HANDOFF。当前下一任务见npc-patrol-wiring-p2-20261106.md。
