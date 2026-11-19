# pE · 11/6 门楼半透与神识轮廓（待父方唤醒）

用户已授权本轮今日任务。只读本任务和 `me/2026-11-06-南门楼地标遮挡视野盲区与神识透视重构备忘录.md`；旧直接遮挡基线已完成，不能再跑同轮拿绿：town-occlusion-followup-20261106/HANDOFF.md及parent-audit/HANDOFF.md。今天的效果是新产品，有单独冻结和实机。

独占新增 `BuildingOcclusionFader.cs`、`TravelerSilhouette.cs`（必要时独立Graphic/Shader）及meta；`WorldPropLayer.cs` 解析/挂载窄区；新增 `FrontEndOcclusion.cs` partial；`FrontEndWorld.cs` 只必要生命周期/排序后调用窄接点；配置building-plots.json仅对gate-landmark相应plot加occlusion字段，必须等pM已释放该文件后，保留pM所有布局。不得改原排序、碰撞、道路、角色帧或UI。验收只改TownMemoIntegrationShot.cs加入新的独立模式，保留基线/full/visual主体。

实现契约：
- 坐标Y向下：真实脚点在[footX-width/2,footX+width/2]与[groundFoot-height,groundFoot]内且人在实体后侧才触发。字段显式开关，默认false；初期只南门楼打开，组件可复用但别全树都变透。实际footY是图顶，不要混淆。
- 只目标建筑CanvasGroup .25s平滑到.425，离开恢复1；地面、水、路和旁建筑alpha全保持1。不因视野/宽高误差在边界来回闪跳。禁用/切页/销毁恢复alpha且释放资源。
- 受遮挡当帧就给可见金色轮廓（#FAD02C，最高约.65），不能等.25s淡出结束才看见角色。轮廓必须来自当前真实姿态的渲染纹理/UV/mesh与镜像，而不是一张静态正面图或矩形金块；跟随Frames当前/交叉淡化、MeshDeform或Slices当前可用形态。可专注本产品默认Frames分支，但其他分支支持情况须明确。
- 独立透明顶层显示，raycastTarget=false，沿UI Mask/Clip/Stencil约束，不能影响世界点击。不要创建盖住HUD的全屏Canvas，不增加角色排序碰撞实体。保持原脚点排序；轮廓层放在物体层上方、HUD下方。
- 共享材质/mesh固定数量，缓存shader/texture/component references，无每帧Find/Resources.Load/GetComponents全树扫描/LINQ分配；只在朝向渲染子物体重建时刷新缓存。性能<.1ms是需要实际Profiler量测的门禁，没测就PENDING，不凭源码估计填通过。reduceMotion关闭脉冲但保留功能性渐变与可见轮廓。
- 参考项目已有QingshiWater.shader的UGUI/Stencil写法，但不要改水shader。

实机验收：保留旧基线输入/4图/所有环境失败。新的唯一run从自然spawn真实走城门北→南完整通道，逐帧记录角色visible/轮廓alpha、建筑alpha、路/地面alpha、排序与碰撞；测进入/离开恢复/长帧/换页清理。至少2张进入/完全遮挡时、1张穿出恢复；≤6图，JPEG≤1280。不能用把角色藏/显示或强改位置代替路径。

离线compile一次，真实失误修复再新批次但保留初错。统筹UI/布局/动作最终源freeze后才启Unity（等待父方明确统一批次，不擅自运行半集成版本）。先交CODE_READY+SHA+编译/检查/未实现门禁。你始终唯一Unity执行者，用户主Unity不动，无Git add/commit/push。整体NOT_APPROVED。
