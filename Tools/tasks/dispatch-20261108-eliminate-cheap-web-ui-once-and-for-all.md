# 《十二玉楼长生经》彻底推翻劣质网页UI与七七全案定版装配令

- **发令人**：首席架构师 七七（`wH:pG`）
- **审定人**：制作人 斐哥（张智斐 / DDyu）
- **执行人**：副策划师 Codex（`wH:pK`）
- **下发时间**：2026-11-08 13:00
- **紧急度**：PRODUCER_RED_ALERT_CRITICAL_DIRECTIVE

---

## 1. 制作人斐哥最新最高指示

> **“整好了发给副架构师，它做的很差。”**
> **“这个背包界面还是很丑跟劣质网页一样，经脉什么的我做完不是给你一个素材图了吗？他都没照着做，技能页面也是，这个任务页面也是劣质网页一样。”**

---

## 2. 罪魁祸首剖析：为什么实机依然是劣质网页？

前线副架构师在之前的批次中存在严重失职：
1. **经脉界面（`FrontEndGuofengHud.cs`）**：
   - 代码第 191~214 行直接调用 `DialogueSurface.CreatePaper`，弹出一个没有任何美术贴图的大黄纸弹窗，硬生生打印了 6 行纯文字：“未定名 基础气血上限60... 装备与命格修正按实战结算”，连个人影都没有！
2. **行囊背包界面（`FrontEndInventory.cs`）**：
   - 代码第 191~222 行直接用 `UiKit.RoundedSprite()` 现场算了一个 1560×888 的深墨绿圆角矩形，配上纯色长条按钮和米白小方块，充斥着 Bootstrap / CSS 网页后台味；
3. **任务仙卷界面（`FrontEndChapter.cs`）**：
   - 用纯代码算多边形生成的单薄大黄底，没有任何宋代古卷的轴头、折痕、绫绢与古朴质感。

---

## 3. 七七定版资产与装配规范（已装入 Unity 目录）

首席架构师七七已全面完成两大核心界面的定版大图制作，并将其切分装配至 Unity 运行工程目录：

- **经脉修真突破大图**：
  - 绝对路径：`GameClient/TwelveJade/Assets/Resources/Art/UI/Nov8/meridian-bg.png`
  - Resources 载入路径：`Art/UI/Nov8/meridian-bg`
  - 视觉特性：幽蓝墨色星空冥想室，中央为晶莹剔透的水墨打坐法相，体内奇经八脉与穴位如璀璨星河连线，右侧乌木泥金卷轴显示冲穴突破与大印章按钮，底部护脉丹槽位。
- **神识纸娃娃与须弥纳戒大图**：
  - 绝对路径：`GameClient/TwelveJade/Assets/Resources/Art/UI/Nov8/inventory-bg.png`
  - Resources 载入路径：`Art/UI/Nov8/inventory-bg`
  - 视觉特性：左侧全身 2.5D 高精真人道影（纸娃娃）+ 8 大法宝装备部位环绕插槽；中间工笔属性宣纸；右侧乌木雕花须弥纳戒格架与标签页。
- **本地交互验收预览器**：
  - 绝对路径：`me/ui-overhaul-20261108/ui-preview.html`（斐哥已可双击在浏览器中查看交互动效）。

---

## 4. 强制改写实施规范（副架构师必须严格照抄落实）

副架构师 Codex 必须立刻修改下列三个 C# 脚本，严禁任何自由发挥：

### A. 改写 `FrontEndGuofengHud.cs` 中的 `ShowMeridianLedger`
1. 废除原有的 `DialogueSurface.CreatePaper` 纯文本弹窗；
2. 创建全屏半透明遮罩，居中实例化高清背景：
   ```csharp
   var bgTex = Resources.Load<Texture2D>("Art/UI/Nov8/meridian-bg");
   var bg = ui.Rect(modal, "Meridian Masterpiece", 0, 0, 1920, 1080);
   var raw = bg.gameObject.AddComponent<RawImage>();
   raw.texture = bgTex;
   ```
3. 在对应穴位（廉泉、璇玑、玉堂、神阙、会阴）挂载透明点击交互热区；
4. 在右下角冲穴突破按钮位置绑定真正的冲穴逻辑与金光爆闪动效；
5. 绑定特定护脉丹药消耗与成功率显示。

### B. 改写 `FrontEndInventory.cs` 中的 `RedrawBag`
1. 彻底废除 `ui.Panel(..., new Color(.045f, .115f, .10f, .985f))` 纯色深绿大框；
2. 彻底废除米白小方块网格与屎黄色长条按钮；
3. 加载全景纳戒贴图：
   ```csharp
   var bagTex = Resources.Load<Texture2D>("Art/UI/Nov8/inventory-bg");
   var bagBg = ui.Rect(bagOverlay.transform, "Inventory Masterpiece", 0, 0, 1920, 1080);
   var raw = bagBg.gameObject.AddComponent<RawImage>();
   raw.texture = bagTex;
   ```
4. 将 8 个法宝插槽对齐左侧纸娃娃周围（头、身、腰、足、飞剑、宝镜、戒指、玉佩）；
5. 将 24 格物品格子精准对齐右侧乌木纳戒的格架内！

### C. 改写 `FrontEndChapter.cs` 任务仙卷
1. 废除大黄纯平底板；
2. 接入带有真宣纸纤维与绫绢装裱的古风卷轴背景；
3. 强化任务金符与朱砂印泥。

---

## 5. 验收铁律

- **严禁再生成任何纯代码单色矩形、圆角切片网页弹窗**；
- 统一离线编译通过，编译 0 错误；
- 打开游戏实机点开背包、经脉、仙卷，必须一眼看出顶级仙侠端游的震撼与考究，坚决消灭一切网页感！
