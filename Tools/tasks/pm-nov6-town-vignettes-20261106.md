# pM · 11/6市井组团布局整改（父方唤醒后直接实施）

用户已允许本轮执行11/6任务。pE的旧遮挡基线run-01已结束，单项直接遮挡PASS_FOR_SCOPE，运行环境错误保留。你是父方以外唯一活动助手，不新建助手、不起Unity、不调用生图、不改全局配置、不Git add/commit/push。

只读 `me/2026-11-06-青石镇物件摆放杂乱与市井空间规划纠错备忘录.md`、本任务及必要配置/校验脚本，不重审整晚历史。

## 独占写域

- `GameClient/TwelveJade/Assets/Resources/Config/building-plots.json`，仅今日物件布局和必要的声明性组团字段；保留原id的含义，若改id记录映射。
- `Tools/logs/nov6-town-vignettes-20261106/`：原输入快照、候选配置、迁移清单、窄静态验收/交接。
- 不写town-map/town-circulation/WorldPropLayer/FrontEndWorld/Core、地砖生成器、原图、角色、UI或pE的Town验收。若源生成器会覆盖building-plots，仅指出准确接点交父方，别改整个生成链。

## 实施要求

1. 当前配置不止east一只：plot-well-lion-west/east都是水井区孤狮，另有plot-bridge-lion-west/east。依据真实位置与宿主一起审，去除水井和无门阀依托的孤狮摆法；优先迁入现有plot-ancestral-hall大门两侧成双对称（真实groundFoot水平，X关于门中轴对称）。没有合适宿主的多余狮子移出摆放表保留源美术，不能为了安放而凭空新增官方建筑。
2. market-stall-south-01/02及03/04、plot-market-cart核实门前外摆/横街市集依托，收敛为2–3个清楚的商贸组团，不孤放南门入城主通道；保持主街500净宽、路桥开口、既有门洞628.1981净宽及60通行余量规则不变。必须依现有道路形状/真实脚底碰撞计算，不能凭坐标猜。
3. 柴堆含clutter-firewood-tea/opera/grain及plot-firewood/-02，石磨plot-millstone、clutter-millstone-well：安置临墙后厨/院落角落有建筑依托，保持人行和门前出口。水井留生活组，不能放狮子或无依托板车。
4. 所有lantern-pole实例检查，空地独杆迁到现有商铺门头/临街墙边或真实丁字路口；不要改变图像比例或把完整灯杆缩成灯笼掩饰。当前素材限制无法表现挑檐吊灯如实列出。
5. 世界配置坐标Y向下：footY是图片顶，排序/接地点为footY+groundFootOffset；不要误用footY当脚点。移动时保留每物体原width/height、collisionCapsule/Rects相对几何、blocks、碰撞10–20%规则；不扩大或关闭阻挡取绿。
6. 优先实施这些具名缺陷，不泛改全镇主干骨架。不安装新美术、不重生64地块。地表阴影/贴花如需运行时支持列给父方，不以任意色块冒充完整画面完成。

## 验证与交付

先保存原始快照。运行现有合适布局/重叠/配置一致性窄检查，新增独立断言仅针对成对脚点/宿主距离/主通道阻挡/碰撞几何未变等实际回归风险。原有警告和失败记录不删、不降门槛。不反复跑无关Core全量或所有材质。

交 `READY-HANDOFF.md`、changed-plots.json（前后坐标/宿主/原因）、before/after验证结果、源SHA和所有未完成美术/实机项。完成后停住，由pE后续唯一Unity新冻结复验。整体NOT_APPROVED。
