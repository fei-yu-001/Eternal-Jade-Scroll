# p2 第四项 · 水面落叶漂流（2026-11-06 09:49）

前三项已交。用户希望flash额度有效用完，接七章第6章明确缺项：动态水体的落叶浮沉。当前你独占 Presentation/TownWaterSurface.cs，可新增 Presentation/TownWaterLeaves.cs+meta；证据Tools/logs/town-water-leaves-20261106/，窄纯几何检查如需另建Tools/TownWaterLeafChecks。TownWaterGeometry.cs/QingshiWater.shader只读，World/Controller/Core不改；pE对话集成与pF名牒背景文件不碰。不开Unity，不Git，不改代理/provider或源美术，不获取密钥。

仅读上述WaterSurface/Geometry及七章水要求，做一轮有界实际实现：沿同一Definition.CenterY/FlowDirection，复用HalfWidth/BankInset与DeckClearance扩张后的Exclusions，叶子全形状不能上岸/穿桥/码头。数量上限24或更少，固定seed，缓慢沿流向平移、轻微摆动/浮沉；越界或进入桥区时先淡出回收再在合法水面重生，不能瞬间跨桥闪现。视觉严格在水面层内部，桥/人物/地物仍在上层，raycastTarget=false。

现Art/Fx只有ink-stroke，没有已审叶sprite；允许明确标为程序化小叶片，以5-7顶点叶形/中脉做低饱和枫褐/黄绿几何，别修改源画或宣称精修美术。沿既有UGUI MaskableGraphic或现水mesh适配；不要每叶建材质，不每帧Resources.Load、不每帧分配数组或全水面重建。定义/地图scale/elevation同原water一致，OnDisable/Destroy完整停动与释放。

沿原WaterSurface.SetReduceMotion与配置回调统一驱动：减弱动态冻结叶位置/摆动/相位，开启后平滑续走；不以禁用NPC巡游代替。主水层configure失败仍失败；叶层异常单列错误，不隐藏原水失败，不动现水波光规则。

优先独立LeafLayer子节点由WaterSurface.Configure创建/配置与SetReduceMotion同步，主挂钩不超过必要窄处。只做几何/生命周期真实有意义检查：整叶足迹在水带内且避真实cutouts、反向水流、长dt不越桥、reduceMotion不推进、固定数量上限/重新Configure释放；一次离线compile即可。编译冻结相关输入、前后SHA，失败保留，不跟正在改UI的全项目循环抢编译。最终≤15行READY，明确程序化视觉/GPU/实机PENDING，真实折射仍未完成，不拼历史绿。
