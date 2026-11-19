# 《十二玉楼长生经》仙剑三风格【双层重叠莲台道盘】工程落地交底任务书

- **发令人**：首席架构师 七七（`wH:pG`）
- **审定人**：制作人 斐哥（张智斐 / DDyu）
- **承办人**：副策划师 / 临时首席架构师 Codex（`wH:pK`）及代码先锋（`wH:pF`）
- **下发时间**：2026-11-07 21:52
- **状态**：PRODUCER_CONFIRMED_OFFICIAL_HANDOFF
- **资产指引**：
  - 轮盘总概念图：`me/wheel-menu-design/pal3-style-rotating-dao-wheel.png`
  - 底层完整莲台资产：`me/wheel-menu-design/layer-bottom-lotus-base.png`
  - 顶层带技能莲台资产：`me/wheel-menu-design/layer-top-skill-lotus.png`
  - 设计规范全文：`me/wheel-menu-design/README.md`

---

## 1. 制作人斐哥的核心设计契约（铁律）

制作人斐哥已对右下角轮盘给出了终审裁定：
> “这个有技能的莲花台是最顶层，去掉这个底下依然是一个完整的莲花台。这个底下的莲花台是固定的不能转动，只有顶层的带技能的莲花台可以转动。”

前线工程团队在重构 `FrontEndGuofengHud.cs` 时，**必须严格遵循“双层莲台”物理结构，严禁做成单层贴图，严禁让底座连带转动！**

---

## 2. Prefab 节点结构与装配规范

在右下角（屏幕坐标 `x ≈ 1562, y ≈ 718`）搭建 Prefab，层次结构必须清晰分层：

```text
[JadeWheelHub] (父容器，管理总交互与音效)
  ├── [LotusBase] (底层莲花台：Image 组件挂 layer-bottom-lotus-base.png)
  │     └── 特性：Transform 绝对静止锁定，任何情况下不发生角位移
  │
  ├── [ApexNotch] (天元指示针：正上方 12 点钟固定卡槽符文)
  │     └── 特性：静止固定在 12 点钟主位，作为当前激活对齐基准
  │
  └── [SkillLotusRotor] (顶层带技能莲花台：挂载 JadeLotusRotator.cs 脚本)
        ├── Image 组件：挂载 layer-top-skill-lotus.png
        └── 八大交互热区（每 45° 分布在圆周上）：
              · 0°   (天元) : 乾坤（系统设置 / 声色随心）
              · 45°  (东北) : 纳戒（储物背囊）
              · 90°  (正东) : 舆图（世界地图）
              · 135° (东南) : 寻道（行卷任务眼前事）
              · 180° (正南) : 经脉（周天修真）
              · 225° (西南) : 万法（玄门技能）
              · 270° (正西) : 道身（本命命格）
              · 315° (西北) : 归卷（存盘入定）
```

---

## 3. 旋转驱动逻辑（`JadeLotusRotator.cs`）

1. **最短角位移计算**：
   ```csharp
   // 目标槽位 targetSlot (0..7)
   float targetAngle = -targetSlot * 45f;
   float delta = Mathf.DeltaAngle(currentAngle, targetAngle);
   targetAngle = currentAngle + delta; // 自动选择顺/逆时针最短路径
   ```
2. **平滑阻尼插值**：
   使用 `Mathf.SmoothDampAngle` 或缓动曲线，旋转时间控制在 `0.28s ~ 0.35s`，带微弱过冲回弹（Overshoot / Spring damping）。
3. **音效联动**：
   - 开始转动时：触发极轻微玉石轴承滑轨声；
   - 停稳卡入天元时：触发清脆温润的玉磬声（`ui_jade_ding`）。
4. **功能联动**：
   停稳后，对应的主界面面板（如中央展开的宋风仙卷）平滑展开；按快捷键（B 键开纳戒、M 键开舆图等）同样驱动顶层莲台旋转对齐。

---

## 4. 执行工位与交付要求

- **负责工位**：`wH:pF`（代码先锋）主导装配，`wH:pK`（Codex）审查合入。
- **废弃清理**：彻底剔除原有粗糙生硬的静态贴纸 `hud-lotus.png`。
- **验证门禁**：本地实机运行，录屏/截图证明：底层莲花座静止不动，顶层带技能莲花台平滑旋转卡位，交互无断点。
