# pE 地图/Unity新会话
先读COMMON.md。本线独占map配置/生成同步/Core地图碰撞/Presentation FrontEndWorld与map相关文件，负责七章第五六七章；不得写UI源、Tools/make-world-mask.py/compose-world-chunks.py（pM）、素材源图。
已真实完成：旧布局ui-run-02 exit0/48PASS/自然loop（不能当新UI通过）；视频模块startup依赖已修，Packages不能回旧版。T01已安装64块，112chunk缝PASS，135meta字节保留；T01独立band4 FAIL。SaveChecks新1166PASS（旧门时期）。
刚写入未终验：Tools/logs/qingshi-map-20261105/asset-integration-20261105/town-memo-20261105及memo-relocation-02（44moves/unresolved[]）；底脚10–20%配置、81小件胶囊、松树15->4、古槐真实根点移中心；河流生成器阻挡改成真实水域/桥宽，护栏桥口截断。必要时检查这些目录短json及当前差异；不要从头遍历旧证据。原窗口最后ModuleNotFoundError world_geometry是临时探针import路径错，后memo-relocation-02 exit0，不要当产品失败。
最新门PNG1080x780 SHA36c9f902...，正面alpha门洞真实底部10–20%净宽302px；按4.5身位上限只有298world，无法兼容500街。不得直接沿旧960x595/620洞碰撞。门红项留真实，不拉伸/擦图伪造。可提交门体地标尺度方案，但必须区分普通二层3.5–4.5与特殊城门；先主街实际路线可达与实体脚墩对齐。
水体独立代码已在Presentation/TownWaterGeometry.cs、TownWaterSurface.cs与Resources/Shaders/QingshiWater.shader及meta；原作者已关闭。离线几何和mask通过，157410采样全水域、8桥/渡口扣洞；Unity/shader尚未验。接口TryCreateFromTownMap(worldRoot,town,worldScale,()=>settings==null||settings.reduceMotion,out surface,out error)，计划BuildWorldChunks后、BuildTraveler前。读源码确认后挂钩并清理；不要再做百万数值断言，做原生GPU与桥面不覆盖验证。
新UI产品已冻结Tools/logs/menu-ui-memo-20261105/UI_MEMO_READY.json，pF新会话只做一次MenuVideoShot编译后通知。届时同步UI+Packages+你地图输入，唯一运行MenuVideoShot.Run非batch、Hidden GameView、D3D11，沿现有unity_owner.py；别重复ui-run-01的internal native截图崩溃。脚本已换public ScreenCapture+WaitForEndOfFrame。
下一步：收口真实地图工程与水体挂钩，等pF UI_TEST_READY运行新版首页名牒，pM ROAD_READY再生成安装地块；最终当前地图批次和截图，保留全部失败，不拼历史。原始运行结果/短交付够用，避免新建泛审计工具。
