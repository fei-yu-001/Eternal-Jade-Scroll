# TwelveJade 客户端

首轮目标：主菜单、三档存档、设置、三套平民造型选择、行装预览与青石镇行走。项目固定使用 Unity 6000.3.25f1 LTS，安装位置 `D:\ruangong\unity\Editor\6000.3.25f1`。

## 打开与运行

1. 在 Unity Hub 登录并激活免费 Personal 许可证。
2. Editor 是单独安装的，在 Hub 的 **Installs → Locate** 中选 `D:\ruangong\unity\Editor\6000.3.25f1\Editor\Unity.exe`，再从 Hub 打开 `D:\Eternal Jade Scroll\GameClient\TwelveJade`。
3. 打开 `Assets/Scenes/FrontEnd.unity`，按 Play。入口场景、角色预设、字体资源和构建列表均已在仓库中。必要时可执行 **Twelve Jade → Prepare project** 修复缺失资源。

主菜单背景、三套角色共 36 张透明三视图和 19 张宣纸底物品图标均已接入；生成来源、授权与加工记录见 `Assets/ArtSource/PROVENANCE.md`。角色创建支持性别、面容款式、命格重掷与问命六问；预览页提供六种纸偶动作。

## 目录与配置

- `Assets/Scripts/Core`：无 Unity 依赖的纯逻辑（存档、JSON 读取、物品表、行囊规则、声望折扣），可在 `Tools/SaveChecks` 中直接跑。
- `Assets/Scripts/Presentation`：前台与地图表现层；`FrontEndMap.cs` 管青石镇行走，`FrontEndInventory.cs` 管行囊。
- `Assets/Resources/Config/items.json`：物品表（品阶、类别、堆叠上限、基准价、描述、图标）。页签由 `categories` 生成，加一件物品只改 JSON。
- `Assets/Resources/Config/town-map.json`：青石镇走廊、立绘、地标、出生点与透视上下限。**立绘的 `x`/`y` 是它站的地平线**（枢轴取底边），不是画面中心。

两张表都由 `Tools/SaveChecks` 每次读取并校验（越界、走廊连通、出生点可达、洪水填充可达面积、品阶/类别引用、堆叠范围），改坏了先在测试里报警。

## 操作与存储

- 鼠标选择按钮；键盘方向键移动焦点，Enter 确认，Esc 返回或取消。
- 城镇内：点击街面行走、WASD 移动、Shift 奔跑、`B` 开行囊。行囊内左键选中、拖拽换位、双击使用。
- 三档记录与设置保存在 `Application.persistentDataPath/Saves`。写入时保留上一份有效备份；主文件损坏会读取备份，更新版本的档位和设置不会被旧程序覆盖。旧存档读取时自动迁移到当前 schema（补上开局行囊与盘缠）。
- 全屏采用显示器原生分辨率；窗口提供常见 16:9 大小。设置保存后生效。

## 检查

在仓库根目录执行 `dotnet run --project Tools/SaveChecks/SaveChecks.csproj`，验证配置表、档位和设置文件逻辑（当前 107 项断言）。`dotnet build Tools/UnityCompileChecks/UnityCompileChecks.csproj` 使用本机 Editor 自带的 2D 模板程序集检查 Unity 脚本编译；它不等于编辑器导入或 Play 模式验收。当前版本另有 `TwelveJade.Editor.FrontEndShot.Capture` Play 验收：生成九页图形截图与序章四图，并验证存档流程、行囊规则、城镇行走断言和问命六问。

