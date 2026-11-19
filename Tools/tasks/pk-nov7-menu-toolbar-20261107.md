# B包 · 主菜单与工具盘

建立：2026-11-07T01:53:45+08:00。负责人：pK。

证据独占目录：`Tools/logs/nov7-menu-toolbar-20261107/`。当前状态：已拆包；具体执行许可见队列。

## 首批 B1：现成去黑洞补丁

- 审查候选 `Tools/logs/nov7-toolbar-candidate-20261107` 的三文件 SHA 与 diff；pE冻结释放后按各文件beforeSHA比对合入 `MenuOrnament.cs`、`MenuButtonGlow.cs`、`UiKit.cs`。
- 核查结果：当前已有 Sliced/fillCenter，问题是深色fill、边框占用和标签压在装饰边上；候选改浅宣底、轻反馈、边框倍率8、图标/标签安全内聚。保持alpha .78与真实点击逻辑。
- 合入后跑规定的 dotnet 门禁；真实截图/文字mesh/鼠标与键盘/normal-hover-pressed-disabled/reduceMotion另排 pE 验证。B1完成不代表B全包完成。

## B2：如意白玉金丝令（排队）

- 正式写域：`MenuOrnament.cs`、`MenuButtonGlow.cs`、`UiKit.cs`、`FrontEndController.cs`菜单局部、菜单专属新UI Shader/美术/音效映射；父方是共享文件唯一整合者。
- 主按钮按新玉令美术原比例和安全slice布局，保持正式文字“叩弦长生”，TMP描金清晰可读；微弱呼吸Shader遵守UI stencil/clip/CanvasGroup及reduceMotion；点击玉磬音效尊重已有音量/静音，不每帧建Material/Resources.Load。
- 保持新档/旧档去向、菜单视频生命周期、设置与退出输入，不以旧1.2秒tint存在宣称新美术已达标。

## 共用执行与交付约束

- 上位任务：`Tools/tasks/dispatch-20261107-qiqi-aesthetic-rebuild.md`（2026-11-07 01:45，制作人批准）；总调度：`me/2026-11-07-六包重构执行队列.md`。本拆包只细化实现/验证，不降低视觉目标。
- 先核对本包写域及源 SHA；已有其他人的更改必须保留。公共 `UiKit.cs`、`MemoVisualAssets.cs`、`FrontEndController.cs`、`FrontEndWorld.cs` 由 pK 合并；协作者只在本包 candidate 目录给出这些文件的窄补丁，不直接覆盖。
- pE 当前门楼批次的 post-run 源审计及 HANDOFF 完成前，所有产品文件冻结。候选、任务和独立证据可写日志。Unity 仅 pE 排队运行，主 Unity PID12028 保持。
- 涉及 C# 必须实际运行 `dotnet build Tools/UnityCompileChecks` 并记录退出码、错误/警告、源码 SHA；候选阶段可使用冻结副本编译，正式合入后仍须此门禁。离线编译不等于截图/输入/动效已通过。
- 每包交付 HANDOFF.md、changed-paths、before/after SHA、必要图证和残留项。截图预览最长边1280 JPEG；原始图证保留。所有旧失败独立保留；不改断言拼绿，不恢复旧 Dialogue02 入口。
- 不 Git add/commit/push，不改全局供应商/代理/凭据，不请求已放弃公益渠道或配额已耗尽渠道。产品图新路径输出，经 pK 审查后安装并保留旧图备份；不以RGB草稿、改名或静态平移冒充新图/走帧。
