# 当前冻结与实机顺序
更新：2026-11-06T13:56:28+08:00。父方pK维护，整体NOT_APPROVED。

1. pE宿主已冻结22项，Core1209PASS；原初编9警告，不外推最终编译。父方轻审见quest-dialogue-host-20261106/parent-freeze-audit-01.json。
2. pF只收口名牒场景/独占脚本与Controller背景窄区；pM只收口水落叶/巡游脚本。两线先交PRODUCT_FREEZE_READY，暂停产品写入，不能边修改共享编译输入边取证。
3. 父方比对当前输入；通知pF任务/Menu与pM巡游各一次冻结离线编译，失败保留、缺口如实PENDING。
4. pE是唯一Unity执行者。先新版Menu名牒新run，再DialogueQuest新run，再NPC巡游新run；各用新空证据目录与scratch。执行前再次核对应输入SHA，记录应用的当前水文件；不重跑旧Town02/四图，不关闭用户Editor。
5. Dialogue目标推进如果由脚本注入必须标INJECTED；真实NPC点击/选择/告辞/交还与奖励单次保存独立验。NPC需真实脚点/Rect/BaseY/动态圆心同源、对话暂停恢复、离场Stop；缺接口/头标不得视为通过。
6. 落叶离线几何/生命周期通过仅证明其范围；真实水面/GPU/岸边折射与树干直接重叠遮挡继续PENDING，不能混入旧通过批次。

状态按当前事实更新，不用旧READY代替本轮验收。p2余额耗尽不再发请求，pG保持原状。上游并发限制时优先产品收口与唯一Unity关键路径；不改全局配置，不重开团队。

2026-11-06T14:20:04+08:00：pF 16项PRODUCT_FREEZE_READY已父方SHA核对全符，manifest b388caf4…；尚未compile。pF/pE暂停写入；pM独占请求继续落叶三处修复及巡游脚本，已授权收口后直接一次冻结compile，无需再等许可。

2026-11-06T14:34:48+08:00：全部最终compile已到。pF两个Editor由父方直接按既有模板编译，未唤醒pF重复跑；pM独立巡游compile通过。两编译共享runtime SHA完全一致，合并当前输入202项均相符，已放行pE唯一新Unity。


2026-11-06T14:42:39+08:00 新实机首失败及限定修复：b-menu-memo-run-20261106-01在新背景meta一致性门禁失败（4PASS/40FAIL，其中38必达未达；0runtimeError/0图）。Unity现有ArtImportGuard将alphaIsTransparency 0改1并统一LF，源零漂移/隔离1项漂移。父方只对齐本轮新增memo-terrace.png.meta至ec33d49a…，PNG dc3a620b…和GUID完全不变，未改导入规则或原断言。旧失败/旧冻结完整保留；当前BATCH-FREEZE-02.json明确唯一amendment，其余201项无漂移。无C#改动，不重复已过编译，pE已获准新Menu02后Dialogue/Patrol独立空scratch。整体NOT_APPROVED。


2026-11-06T15:34:12+08:00 当前B批实机与下一窄修（pK）：整体NOT_APPROVED。
- Menu02：21PASS/34FAIL（32必达未达），0runtimeError；自然loop阶段异常切页，来源未记。父方只在MenuVideoShot隔离验收进程native输入并补phase/脚本点击轨迹，原断言与40秒期限保留；离线91输入0error/8warning/零漂移，BATCH-FREEZE-03共202项。
- Menu03：183PASS/1FAIL，18图，源/隔离零漂移，无必达未达。唯一FAIL为UnityEditor.Connect.Token Exchange请求异常（runtimeErrors=1），不滤日志、不重试取绿。558个phase0样本全menu、native键鼠禁用有记录；自然loop及名牒新建/读取直达Town、删除保护均到达，仅能说本轮未复现异常切页。pE正收JPEG目检，尚未视觉批准。
- Dialogue01：161PASS/2FAIL/1PENDING，0runtimeError；拒绝选项文字valid=false，重开intro后缺选项导致Single失败。pM独立审计见quest-dialogue-host-20261106/independent-failure-audit-01.md。自动线性路径、后续接取/交还/奖励/重读未完成；目标推进仍INJECTED，非战斗闭环。
- Patrol01：301PASS/0FAIL/32PENDING，5图；移动同源、动态阻挡、真点击/对话停走和离场清理有证；静态立绘平移不代表步态动画，头标/深度/视觉缺口仍在。
- 新写域：pM仅Core/Dialogue.cs+Tools/SaveChecks/NpcDialogueChecks.cs复现并修缺省关系门槛（未填min值误作0），保留RefusedHelp及显式门槛；pF仅证据目录staged脚本补同帧文字/Frame诊断，不能覆盖Assets；pE仅收Menu03交接；pK做集成和台账。之后单次冻结编译、新Dialogue02，未放行任何重跑。p2余额耗尽待命，pG不动。
- Menu03后进入窄修窗口，BATCH03保留为历史输入，下一运行前必须另建冻结；不得把独立批次合成当前全绿。


2026-11-06T15:48:03+08:00 调度修正：pF/pM/pE均出现上游Concurrency limit exceeded，已Esc停止重连；pE的Menu03结果交接已完整保存，未停止用户Unity。随后只恢复pF的证据目录诊断任务，pK接手Core缺省门槛，不再占用pM请求。pM停止前Dialogue.cs仍与BATCH03同SHA且无新证据文件。
Core修复已READY：Dialogue.cs仅未配minTrust/minGoodwill改NpcRelation.Min，真实拒绝goodwill=-2/重开无选项的修前FAIL保留；修后单次全量1227PASS（新增18）/0error/2个ChapterChecks未使用项warning/78输入零漂移。记录Tools/logs/dialogue-default-gates-20261106/HANDOFF.md；Unity与文字渲染仍PENDING，等待pF staged诊断后新冻结。Menu03三图轻审已落visual-review.json：独立露台、金额与命格可读；不覆盖其1个UnityEditor.Connect异常FAIL。整体NOT_APPROVED。


2026-11-06T17:34:31+08:00 最新用户范围与调度（覆盖此前未执行排期）：只推进2026-11-05七章，2026-11-06新增UI视觉/角色动作/对话扩展留新窗口。Dialogue02撤下，2639输入sync成功零差异但尚未启动Unity、无run目录；Core缺省门槛修复/诊断/最终compile-02与BATCH04均保留不回滚，旧READY不再代表可直接运行。当前只pE一名Codex助手工作，与父方合计并发2；pF/pM idle，p2不再使用也不换agy，pG原七七不动。pE已确认转独占TownMemoIntegrationShot.cs补第5/7章门体/古槐与玩家直接重叠遮挡短模式，≤4图、原倍率/真实路径碰撞，无新产品改动；尚未READY/运行，输出town-occlusion-followup-20261106。最新接续入口me/2026-11-06-傍晚接续短交接.md。整体NOT_APPROVED，七章未全部完成。
