# p2 第二项 · NPC 巡游宿主接线（2026-11-06 09:25）

上项52/22检查及API已收到。用户要求用当前 Acomb-ds-v41 / deepseek-v4.1-flash 多做有效开发。直接接此项，不再新会话或换模型。pE目前413恢复中、无正在写代码；父方现明确将 FrontEndWorld.cs 独占写权转给你，pE保留Quest/Dialogue及FrontEndChapter/Trade/Controller。你不要向pE连发prompt以免重复触发失败，短交接给父方即可。

只改 GameClient/TwelveJade/Assets/Scripts/Presentation/FrontEndWorld.cs；可新增同目录 FrontEndNpcPatrol.cs+meta；工具/证据仅 Tools/logs/npc-patrol-wiring-20261106/。禁止其他既有产品文件与源美术、存档/配置、Git操作、Unity启动。pM独占 NpcLocalPatrolShot.cs+meta 做实机证据，pF独占UI两文件。你现有TownMap/TownNavigator及NpcLocalPatrol已交付，不再无关扩大。

## 最小闭环

1. 复用原MapNpc ID和NpcStandingAt日程锚点，一个NPC一个临时 NpcLocalPatrol。稳定ID seed，不用每次随机GetHashCode；活动半径/停留/速度用局部显式小范围默认值（不是碰撞Radius）。先建立全部实际日程圆心，再开始规划；动态圆心同步失败要保持旧位置、停该实例，不能画面走了碰撞没走。
2. 每帧通过真实TownNavigator/CanTraverse闭包仅排除自身。Advance真实位移成功后，同一X/Y更新地图圆心、人物Rect/Placeable.BaseY、头标父位置、动态排序；默认玩家继续撞NPC。沿原世界投影/脚点语义，不另造排序坐标系。
3. BuildTownNpcs点击闭包不再用旧foot。TownNpcFootPosition、SnapTravelerNextToNpc和交互到达点读取实时位置。玩家点击接近NPC时冻结该NPC防追逐，失败/取消/点别处释放等待态；真正对话期间保持暂停，结束再停留后规划。给pE明确 Begin/End或SetNpcPatrolDialogue接口供现有对话租约调用；不要自己新造对话系统。
4. 切离Town、重建日程、PageClear等现有生命周期先Stop并ClearNpcPositions，再丢弃本次对象；返回新建。不计入存档、不后台计时。reduceMotion仅停止视觉浮动/动画，不能取消NPC真实活动。
5. NPC可以先复用现有合法静态立绘小范围移动；若已有三向资源与动画可安全复用则读现成路径。不得把无动画滑动声称完整步态/专属角色已通过。角色美术仍HOLD。
6. 只读取证API：NpcPatrolForShot(string id)->NpcLocalPatrol；TownNpcFootPosition用当前位置；给NPC实际Rect/Placeable.BaseY取证方式以便pM核同源。头顶任务实际label由pE对话/任务宿主提供，你只确保NPC节点变动时它可随父同步。

先核相关既有生命周期/真实点击中断流，直接实现；所需pE接点以≤10行给父方，避免跨写域调用不存在函数。做一次冻结相关输入的离线compile（可复用现行离线工程），实机PENDING给pE；测试不能通过直接SetDialogue/读UI推算伪造真实操作。完成精确列已接/待接，勿把接线compile等同实机绿。
