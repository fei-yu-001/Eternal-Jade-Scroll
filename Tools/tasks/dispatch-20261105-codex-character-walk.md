# 首席架构师七七 任务派单：角色走帧正侧向一致性排查与文生图解决方案

- **派单对象**：Codex（右下角窗格 wH:pF · 角色表现与走帧专项）
- **核心问题（斐哥指出）**：当前主角（农家子弟 farmer-male-0）在行走时，“正向和侧向好像不是一个人物”。
- **目标**：彻底打磨人物行走，定位正侧向不一致根因，分析通过文生图（配合现有生图网关与后处理）能否解决，让人物在正视、侧视、背视时保持完全统一的样貌与服装风格。

---

## 调查与审查线索

1. **查阅现有走帧历史证据**：
   - 重点精读：`Tools/logs/v-01-walk-judgement-report.md`（报告中明确指出：旧提示词把侧面描述成了“布头巾/本白短褐/卷袖/灰内袍”，而正面原型却是“橄榄绿长袍/发髻/绑腿/背篓”，提示词本身承载了错误的身份）。
   - 查看现有资源：
     - 正面走帧/立绘：`GameClient/TwelveJade/Assets/Resources/Art/Characters/Stand/farmer-male-0-front.png` 与 `Walk/farmer-male-0-walksheet-front-v1.png`
     - 侧面走帧/立绘：`GameClient/TwelveJade/Assets/Resources/Art/Characters/Stand/farmer-male-0-side.png` 与 `Walk/farmer-male-0-walksheet-side-v1.png`
     - 背面走帧/立绘：`farmer-male-0-back.png` 与 `walksheet-back-v1.png`

2. **锁定主角“农家子弟”身份特征契约（Identity Contract）**：
   - 提取正向立绘的决定性视觉锚点（如：橄榄青绿麻布长袍、腰间麻绳/暗色腰带、高束发髻、绑腿草鞋、背篓或简朴背带）。
   - 确立统一的 Prompt 特征锁词（英文），严禁出现短褐、头巾等与正面冲突的描述。

3. **评估与设计“文生图”解决方案**：
   - **现有可用生图基础设施**：
     - 本地网关：`http://127.0.0.1:8000/v1/images/generations`（模型 `grok-imagine-image`，兼容 OpenAI 标准，Clash 代理在 7890 端口已就绪）。
     - 调用脚本参考：`python D:/qiqi_me/DDyu/scripts/check_web_image.py`。
   - **文生图核心挑战**：
     - Grok 是文生图无固定 seed，每次发散独立，单张生成侧向很难保证与正面完全相同。
     - 请分析可行策略：
       - 方案 A（全周期 8 帧雪碧图 / 3 视角同时出图）：Prompt 显式要求生成“3-view character turn-around or 8-frame walk cycle sprite sheet of the EXACT SAME character on plain pure white background”。
       - 方案 B（基准侧向立绘 + 骨骼/切帧变形）：先文生图抽卡获得 1 张与正面风格高度一致的侧立绘（或三视图），再通过现有 Tools/TB 或网格变形生成动作。
       - 方案 C（批量提示词微调 + 客观闸门筛选）：利用 `Tools/check-walk-sheet-motion.py` 跑客观打分，选出起伏与触地分布达标的候选。

4. **产出要求**：
   - 给出《角色走帧一致性排查与文生图解决方案报告》（包含正侧向差异对比、根因分析、推荐的成套 Prompt 设计、缺失的美术资产缺口列表）。
   - 如果可以通过本地网关调脚本验证，可编写测试脚本进行抽样验证。
