# TwelveJade 客户端

首轮目标：主菜单、三档存档、设置、三套平民造型选择和行装预览。项目固定使用 Unity 6000.3.25f1 LTS，安装位置 `D:\ruangong\unity\Editor\6000.3.25f1`。

## 打开与运行

1. 在 Unity Hub 登录并激活免费 Personal 许可证。
2. Editor 是单独安装的，在 Hub 的 **Installs → Locate** 中选 `D:\ruangong\unity\Editor\6000.3.25f1\Editor\Unity.exe`，再从 Hub 打开 `D:\Eternal Jade Scroll\GameClient\TwelveJade`。
3. 打开 `Assets/Scenes/FrontEnd.unity`，按 Play。入口场景、角色预设、字体资源和构建列表均已在仓库中。必要时可执行 **Twelve Jade → Prepare project** 修复缺失资源。

菜单背景来自公共领域馆藏画作，三套角色画稿仍待图像服务分组开通。角色缺图时页面会明确显示占位文字，角色选择、命名、档位与朝向保存仍可操作。

## 操作与存储

- 鼠标选择按钮；键盘方向键移动焦点，Enter 确认，Esc 返回或取消。
- 三档记录与设置保存在 `Application.persistentDataPath/Saves`。档位写入时保留上一份有效备份；主记录损坏会读取备份，更新版本存档不会被旧程序覆盖。
- 全屏采用显示器原生分辨率；窗口提供常见 16:9 大小。设置保存后生效。

## 检查

在仓库根目录执行 `dotnet run --project Tools/SaveChecks/SaveChecks.csproj`，验证档位和设置文件逻辑。`dotnet build Tools/UnityCompileChecks/UnityCompileChecks.csproj` 使用本机 Editor 自带的 2D 模板程序集检查 Unity 脚本编译；它不等于编辑器导入或 Play 模式验收。运行编辑器批处理和界面手工验收后，才能将首轮状态记为完成。
