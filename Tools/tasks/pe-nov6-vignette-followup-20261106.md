# pE · 今日组团布局三图实机收口

前轮已交HANDOFF并idle；父方现唤醒你进行独立新任务，仍唯一Unity助手，不新建队伍。父方本地并行做审计和交接，只写本任务指定验收与证据。用户已授权11/6工作；无需另问启动权限。

源布局已安装并在batch03冻结过，详见Tools/logs/nov6-town-vignettes-20261106/READY-HANDOFF.md。仅新增TownMemoIntegrationShot.cs独立mode `vignette-followup`，保留其他所有mode/断言/错误处理主体与产品不变。证据独立根 `Tools/logs/nov6-town-vignette-runtime-20261106/`。不能写任何产品代码/配置、美术或UI，不能替换角色素材。

3组恰好3图，必须实际出生→现有真实寻路到目标，可自然走中途节点但无teleport/碰撞关停/移动物体/改世界比例/全图相机。若自然视角无法看全，记录PENDING，不把局部截图说成全组通过。

1. 祠堂：plot-ancestral-hall (3420,1710)高640，双狮X3245/3595真实ground2470；两狮对称于3420且脚点水平，桥头/水井无孤狮。图需显示实际建筑依托和双狮，不只画配置框。选可站点应考虑楼体底脚2258.75..2343.75与双狮碰撞；不要拿footY当底脚。玩家固定相机视野约1704x940，可据自然视角择一个可站点让整组入画；路径和可达判据完整保留。
2. 粮行：plot-grain-shop(2080,6810)高440，干货/菜摊X1960/2200真实ground7440，柴堆/磨盘在西侧院角；附近西纵街可达，如X2400/Y7260作为初查点（须实际CanStand/CanTraverse验证）。检查门前/街面畅通和摊位有宿主，不能仅遥远同框。
3. 布庄：plot-cloth-shop(4950,6970)高520，布头/皮货X4830/5070真实ground7680，灯杆5250/7440；可初查X5400/Y7420自然路径（不是强制位置）。检查两摊依铺、未压主街/农田通行。

每组记录主建筑与成员artRect/真实ground/屏幕rect、源art、碰撞、同框、相关NPC关系，截图≤1280JPEG供父方。100件配置稳定、门净宽628.1981与500主街保持、源SHA与三方漂移均要审；layout20FAIL/4WARN保留，不扩域治理。131/130等旧成绩不拼为新全绿。

一次新完整冻结→离线compile(所有runtime+Editor)→全sync→单次Unity新mode/空run/scratch。此前首错和batch03不覆盖。全日志Curl/licensing/D3D12依旧按环境FAIL保留，不改全局代理、不为取绿重跑。若出现真实实现错误保留首轮并做最小验收修复才可新批；产品问题交父方，不私自改配置。

完成READY-HANDOFF/HANDOFF及SHA、真实轨迹、三个小JPEG和未达项后idle。主Unity保持，无Git add/commit/push，不启动Dialogue02，不调用生图。
