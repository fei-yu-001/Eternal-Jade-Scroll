# pF · 11/6首页与名牒视觉重构（待父方唤醒后执行）

用户已明确本轮可以执行11/6任务。只读本任务及两份今日UI备忘录（首页UI按钮视觉缺陷；名牒与存档UI视觉纠错与重构），必要时看 `Tools/logs/nov6-visual-assets-20261106/ui-reference-contact.jpg`。不回看旧历史。你仍现有pF，与父方合计并发2；不新建助手、不启Unity、不派发别人。

## 独占写域

`Assets/Scripts/Presentation/{FrontEndCharacters,UiKit,MenuOrnament,MemoScrollOrnament,MenuButtonGlow,MemoBackdrop}.cs`，`FrontEndController.cs` 仅字体创建/菜单构建/背景切换窄区，可新增 `PosterCardView.cs`、`MemoVisualAssets.cs`、`ScrollSelectAura.cs` 及meta；`Assets/Editor/MenuVideoShot.cs` 只按新版视觉契约补断言。根路径均 `GameClient/TwelveJade/`。证据写 `Tools/logs/nov6-ui-rebuild-20261106/`。不写Core、World、Town验收、地图、Packages、旧美术。新素材由父方审查安装后提供路径，不调用生图服务/不处理密钥。

## 本轮产品目标

1. 首页/名牒相关 `MenuOrnament` 的 Line/Circle 拼图逻辑用实际美术贴图替换，完整9-slice金边/金玉按钮，右上乾坤/玉牒/掩卷用3张明确icon，不要再画几何假图标或悬空两短线。保留其他调用者API兼容；贴图缓存有明确销毁/复用，不每帧 Resources.Load 或建Sprite/Material。
2. 名牒不用 Stand/Facing/Puppet 图，使用独立9:16或1:2海报卡 `PosterCardView`，按preset/gender配置父方提供的海报。海报金边完整、不拉伸，AspectCover裁切有记录。当前实际是ui.Art(stand)，不必为不存在的Puppet实例绕路。
3. 卷轴用实际丝绸木轴/玉佩流苏/朱印贴图分层，选中金光1.2秒呼吸并环绕，切换立即响应；reduceMotion冻结动效但保留选中标识，不能全scroll每帧重新建mesh/贴图。整体透明部位应保留背景，不把3条扁平矩形当完成。
4. 名牒暖色纸基底Alpha .82–.88（较新备忘录覆盖早晨不透明要求）；金边和角花独立高不透明度，防多个重叠底板让有效Alpha再次变成1。去掉刻度/尺子边框。正文使用墨黑/深褐、属性金褐层级，对比与读数清晰。
5. 不再出现「尚未记载」：未知姓名「未定名」，宗门「一介散修」只用于无所属状态，灵根「天机未明」，未知年龄「年岁待考」，未启程「行卷初启」等。保留14个真实信息域及真实铜钱格式、命格与时辰。SaveData目前无age/realm/sect/spirit字段，不编造数值或新增产品系统；未知财帛类型使用「未入仙途」而非伪造0。损坏/未来存档保护文案仍准确，不把不可读当新档。
6. 主按钮/入世金玉底面、深焦褐#382314字，尺寸与现布局协调，使用TMP SDF真实字形与明确笔画对比；查当前48采样书法资产在实际缩放是否需提高采样，不能只加更大阴影把字糊掉。中文缺字需实证。右上icon深色衬底 .75–.80，图标清晰金色。
7. 保留已定名：叩弦长生、乾坤、玉牒、掩卷、回卷、承卷入世、立传开卷、归尘。备忘录中的「韶庵/卸车/进入游戏」是误读或参考，不替换命名。
8. 保留首页视频只menu、slots独立露台和.3–.5s过渡；既有选卷/草稿不重掷/新旧档直达Town/归尘二次确认及仅删选中卷/键鼠交互不可回退。

## 证据与完成边界

原MenuVideoShot完整流程、自然loop、超时和错误计数保留，按变更更新视觉预期并补海报/真实贴图存在、纸有效Alpha、无禁用文案、字形/所有14域完整、卷轴选择/reduceMotion；不能删除失败断言换通过。父方提供资产READY后，按现有离线模板一次编译91+实际新增源码；0error才交 `READY-HANDOFF.md` 和完整冻结SHA/changed-paths。不复制全Library，不运行Unity。实际截图/布局/50ms/低性能指标仍待pE实机，不能仅源码宣称完成。

完成当前可实施范围；若素材缺项，准确列出缺项而不是偷偷用旧粗线框或纸偶替代。父方统一调度真实Unity。整体NOT_APPROVED，无Git add/commit/push，不改全局配置，不读写输出凭据。
