# A3 实施令 · 真实任务符签与180px身份牌

2026-11-07，用户明确要求六包持续做到完成。你沿用pE原会话，A1已交父方审查。现在直接实现A3产品代码，持续做到编译通过并交付，不新建助手、不启Unity。

写域：GameClient/TwelveJade/Assets/Scripts/Presentation/NpcQuestOverhead.cs；FrontEndDialogue.cs仅BuildQuestOverheads/RefreshQuestOverheads及必要独立更新方法；可新增NpcIdentity/QuestMarker专属组件与meta。父方正在改FrontEndDialogue.DialogueFrame肖像映射，编辑必须按方法定点合并，不覆盖完整文件；两者最终共享构建时先读最新源。所有其他共享文件改动只交窄diff。

按正式A包实施：ASCII !/?换为有辨识度的金色朱砂符签(可接)、碧翠云纹玉佩(可交)、低亮进行中状态；可复用已审核RGBA美术或精细矢量UGUI图形，不能用文本字符充当图标。None任务层隐藏。身份牌独立，玩家到真实NPC交互距离<=180px时显示真实名字和来自TownNPC角色/身份配置的称谓；NPC移动时保持锚点与距离更新。不得臆造职业，不把所有NPC标成有任务。保留State/Label/Rect/FloatingForShot等兼容接口或给明确迁移补丁；旧验收原报告不改。reduceMotion冻结位置/呼吸，按真实设置恢复。无每帧分配/Resources.Load或无条件文字重建。

验证实际dotnet build Tools/UnityCompileChecks 0error；有意义地覆盖None/Available/InProgress/Completable状态、180px边界、身份显示与NPC巡游/对话暂停生命周期。尽量沿用既有检查工具，若新夹具必要只新增独立文件。证据Tools/logs/nov7-dialogue-rebuild-20261107/a3-overheads/，交HANDOFF及changed paths/SHA/compile/未验项。不得把编译当Unity截图。

父方负责A1合入、新肖像与菜单/HUD；你完成A3后可继续在本包证据目录准备A+B联合验收独立Editor候选（先读当前实现、只候选，不启动Unity），交付时说明下一步所需输入。原门楼失败保留，不重跑原批。
