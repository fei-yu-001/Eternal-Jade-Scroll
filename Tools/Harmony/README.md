# 鸿蒙独立迁移工具

正式客户端仍使用 Unity 6000.3.25f1。这里的工具把源码同步到仓库内被忽略的 `.batch/HarmonyClient`，由团结引擎构建；不要直接用团结打开正式工程。

- `prepare-project.py`：校验目标目录和标记，增量复制源码与 GUID；仅在副本调整 TMP/UGUI 和引擎版本。Input System 1.17 使用校验过的官方包，缓存于工作区 `tools/Downloads`。
- `HarmonyClientBuild.cs`：以 FrontEnd 场景导出 ARM64/IL2CPP/OpenGLES3 开发工程，配置手机纹理、图标与横屏，并注册 `JadeHttp`。
- `JadeHttp.ets`：异步系统 HTTPS 桥，保持系统证书验证；注册位置是导出的 `tuanjieLib/src/main/ets/gen/TuanjieJSScriptRegister.ets`。
- `HarmonyInputChecks.cs`：独立副本的 PlayMode 输入检查，7 项断言；执行 `HarmonyInputChecks.Run` 时不要加 `-quit`，由检查自行退出。
- `Runtime/HarmonyDevelopmentProbe.cs`：只复制到独立副本的开发诊断，记录页面、坐标、输入设备和性能状态，不记录凭据或存档内容。

## 构建步骤

1. 从 HarmonyOS 工作区 `tools/Use-Tools.ps1` 加载 D 盘工具及缓存环境。在仓库根目录运行 `python Tools/Harmony/prepare-project.py`。
2. 设置 `HARMONY_TOOLS_ROOT`、`HARMONY_EXPORT_ROOT`、`HARMONY_BUNDLE_ID`、`HARMONY_VERSION_CODE`；导出路径使用本仓库 `.batch/HarmonyExport`，包名必须匹配本机已有调试签名。当前版本码 1000003，下次递增。
3. 使用团结 1.11.1 执行 `-batchmode -nographics -quit -projectPath <独立副本> -executeMethod HarmonyClientBuild.Export -logFile <本地日志>`，确认 `ETERNAL_JADE_GAME_EXPORT_SUCCESS`。
4. 按工作区私有签名记录为导出工程配置 `.sign`。在导出目录运行 `ohpm install`，再使用 DevEco 自带 Node 执行 `hvigorw.js --mode module -p product=default -p module=entry@default -p buildMode=debug assembleHap --no-daemon`。
5. 产物为导出目录的 `entry/build/default/outputs/default/entry-default-signed.hap`。经 USB 安装后验证本次变化；编译成功不能替代真机体验验收。

此流程依赖工作区既有工具安装及本机调试签名。所有签名、设备标识、HAP、截图和原始日志留在仓库外或被忽略的 `.batch` 中。阶段结论见 `Documents/鸿蒙开发版进度-2026-11-20.md`。
