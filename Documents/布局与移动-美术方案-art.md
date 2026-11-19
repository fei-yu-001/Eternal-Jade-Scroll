# 布局与移动 · 美术施工方案（art）

> 派单：B-01（七七架构师，2026-10-18）。本方案只施工不装机：所有候选产物落
> `Tools/logs/b01-art/` 与脚本既有的 logs 目录，**不覆盖** `Resources/Art/**` 任何已装机件，
> 不改 Tools/ 下脚本与阈值，不 git add/commit。入库一律等斐哥逐张挑图后由架构师走。
> 依据：`Documents/青石镇布局规格.md`（布局规格）、`Documents/青石镇素材工单.md`（素材工单）。
>
> **生图通道状态（如实标注，不装能出图）**：
> - 主通道 `hub.oaifree.com /v1/images/generations`（gpt-image-2.5）：**当前返回
>   `no_available_account`，不可用（斐哥口径）**。10-17 12:04 曾实测 200 恢复过一次
>   （工单 §5.6 试点），属间歇性额度问题，开工前必须先做一次探活（见 §0.2）。
> - 备用：自建 grok2api 网关（`http://127.0.0.1:8000`，grok-imagine-image，PROVENANCE.md
>   登记在册，grass/stone overlay 与早期道具 9 件均出自它）。`generate-prop-art.py`
>   直连此网关；`generate-portraits-guarded.py` / `generate-building-art.py` /
>   `generate-shop-art.py` / `generate-prop-art-v2.py` 的端点字面量写死 hub.oaifree——
>   **切备用通道 = 改端点常量，属"改脚本"，本方案不替架构师做这个裁决，只提交流程**。

## 0. 管线通读结论（一句话一张卡）

| 脚本 | 干什么 | 关键口径 |
|---|---|---|
| `generate-portraits-guarded.py` | 走帧逐帧生图（8 帧 × PHASES 相位词），逐帧过亮度≥80 / 角标闸 / 身份闸 palette≥0.50，齐 8 帧才调 assemble | **只支持 farmer-male-0 一个角色**（STYLE/SHARED_FRONT/SHARED_BACK 写死农夫人设）；`--view` 只有 `front/back/all`，**无 side**；槽位归一 816×1088；候选只落 `Tools/logs/rig-01/`，`--install` 才装机 |
| `assemble-walk-frames.py` | 逐帧拼 sheet（等距/底边对齐/gap）+ 自动跑三条闸（切帧/动作/身份） | **落盘先合成不透明白底再存 RGB**（透明底会被动作闸 convert("RGB") 填黑）；**gap 默认 0**——gap≥2 动作闸会误判"没动"，此冲突登记在 b-04-report.md 待裁 |
| `check-walk-sheet-motion.py` | 动作闸：B 起伏≥3px / C 触地变化≥10% / D 帧高偏离≤0.14（A 姿态幅度只打印） | 复用 extract-walk-sheet 的连通块提取；`--evidence` 出 8 帧剪影叠影图（人眼判据） |
| `generate-building-art.py` | 建筑立绘：gpt-image 生图 → cutout-props 抠底 → 归一（88% 占高、贴底 99%、画布宽高比=plot 宽高比 ±0.005）→ 六判据量化验收 | BUILDINGS 表写死 6 件（herbal-garden/stone-house/blacksmith/teahouse/temple-gate/paifang）；新增条目=往 BUILDINGS 表加行（**改脚本，属下一阶段工单**）；生图尺寸只有 1024×1024 / 1024×1536 两档（`nearest_size`，横幅 >1.0 走方幅——**工单 §四.2 的横幅新形状风险点**）；--from-raw 可不重新生图只重跑抠底归一 |
| `generate-shop-art.py` | 商铺立面变体，算法 importlib 复用 building-art 全套 | SHOPS 表 6 件（cloth-shop/wine-tavern/herbal-shop/general-store/bun-shop/country-inn，expect_wh 写死 300×348/340×420——**是旧尺寸档，工单 N2/N2b 的新尺寸要改表**） |
| `generate-prop-art-v2.py` | 道具立绘，与 building-art 同源，占高 96%、贴底 99% | PROPS 表 4 件（stone-lion/lantern-pole/market-stall/market-cart），极窄件（ar 0.4）按宽度适配是有意为之 |
| `generate-prop-art.py` | 旧道具管线（9 件，一次性脚本） | **直连 grok2api 本地网关**（127.0.0.1:8000），AESGCM 读 backend.db 密钥——是现成的备用通道调用样例 |
| `compose-final-material.py` | 最终材质 = 程序化基底 + overlay（opacity≤0.18，逐级降级到纯基底） | 输出 `ArtSource/world/materials/`；增量 `--names` 模式会保留其余材质结论 |
| `compose-world-chunks.py` | 按 world-mask 权重混合材质出 chunk 贴图 | `--full-grid` 64 块全量（**增量会出新旧混合代，必须全量**，日志 10-11 教训）；缺材质走 TEMP fallback 并打印 |
| 配套验收 | `PropChecks/check-prop-art.py`（画布/alpha/贴底/孤儿棘轮）、`PropChecks/check-plot-overlap.py`（判据 10 遮挡铁律+artRect/base 双矩形）、`check-world-seams.py`（112 缝）、`check-world-materials.py`（阈值读 world-material-gen.json） | 阈值一律在配置文件里，不在脚本里放宽 |

## 1. 地块加密素材清单（35 → 90–110 件）

> 口径：尺寸=世界单位 宽×高（px），身位=高/170，画布比=宽/高。落位全部「待对齐」
> （等布局规格定稿回填 footX/footY）。**每件都给生成命令**——新增条目需要往脚本清单表
> 加行（改脚本属下一阶段，本方案先把命令形状与验收闸定死）。

### 1.1 重画 3 件（主题升级，R 表）

| # | 物件 | 建议 宽×高（身位） | 画布比 | L 级 | 生成命令（示意，需先扩表） | 验收闸 |
|---|---|---|---|---|---|---|
| R1 | 茶楼·两层临街 | 430×690（4.06） | 0.623 | L3 | `python Tools/generate-building-art.py teahouse --attempts 3`（BUILDINGS 表 teahouse 条目 expect_wh 240×386→430×690、prompt 已是两层茶楼文案，改尺寸即走） | 脚本内六判据 + `check-prop-art.py` + `check-plot-overlap.py` |
| R2 | 牌坊 | 480×760（4.47） | 0.632 | L3 | `python Tools/generate-building-art.py paifang --attempts 3`（同上，expect_wh 340×541→480×760；可分两档：先只等比放地块不重画，斐哥嫌软再重画） | 同上 |
| R3 | 北街商铺·升两层 | 480×720（4.24） | 0.667 | L3 | `python Tools/generate-shop-art.py shop-north-two-storey --attempts 2`（SHOPS 表新增条目） | 同上 |

### 1.2 新增·镇级建筑（天际线主力 L2–L3，N 表建筑组）

| # | 物件 | 建议 宽×高（身位） | 画布比 | L 级 | 归属脚本 | 验收闸 |
|---|---|---|---|---|---|---|
| N1 | 镇门楼（谯楼式，blocks:false 门洞） | 520×800（4.71） | 0.650 | L3 | generate-building-art.py（新条目，FRAME_PORTRAIT_NARROW） | 六判据+prop-art+overlap |
| N2 | 沿街两层铺面 ×4–6 变体 | 340×660（3.88） | 0.515 | L2 | generate-shop-art.py（新条目组） | 同上 |
| N2b | 沿街单层铺面 ×2–3 变体 | 300×450（2.65） | 0.667 | L1 | generate-shop-art.py（MAT-12 已有 6 件 300×348 清单与 prompt 可复用风格，按新尺寸出） | 同上 |
| N3 | 钟鼓楼（全镇最高件，天际线锚点） | 300×760（4.47） | 0.395 | L3 | generate-building-art.py（FRAME_PORTRAIT_NARROW；**ar 0.395 比现有任何条目都窄，先单跑 1 张看归一化再批量**） | 同上 |
| N4 | 里正院（横幅合院） | 500×400（2.35） | 1.250 | L1 | generate-building-art.py（**横幅新形状**：nearest_size 会走 1024×1024 方幅，工单 §四.2 明说不确定——先单跑验证） | 同上 |
| N5 | 戏台（台口朝广场） | 420×520（3.06） | 0.808 | L3 | generate-building-art.py（新条目） | 同上 |
| N6 | 埠头石阶 300×150（0.88，ar 2.0）+ 可选水口亭 260×300（1.76） | 见左 | 2.0 / 0.867 | L0/L1 | generate-prop-art-v2.py（石阶 ar 2.0 超出 pick_size 三档（1.0/0.667/1.5）最近档=1.5，横构图归一需先验证） | prop-art+overlap |
| N7 | 货栈 | 380×460（2.71） | 0.826 | L1 | generate-shop-art.py（新条目） | 同上 |
| N8 | 马厩 | 320×330（1.94） | 0.970 | L1 | generate-shop-art.py（新条目） | 同上 |
| N9 | 幌子旗幡 ×4–6 变体（酒/茶/药/当） | 60×220（1.29） | 0.273 | L0 | generate-prop-art-v2.py（**ar 0.273 比 lantern-pole 0.4 还窄**，按宽度适配分支会触发——杆子会矮，先单跑 1 张看效果） | prop-art |

### 1.3 新增·自然与农桑（工单 B 层，T/F/M 表）

| # | 物件 | 建议 宽×高（身位） | 画布比 | L 级 | 归属脚本 | 验收闸 |
|---|---|---|---|---|---|---|
| T1 | 枫林行道树 ×3–5 | 320×420（2.47） | 0.762 | L1–L2 | generate-building-art.py（树类新条目） | 六判据+prop-art+overlap |
| T2 | 大香樟 | 380×440（2.59） | 0.864 | L2 | 同上 | 同上 |
| T3 | 银杏（秋染色企划位） | 340×500（2.94） | 0.680 | L2 | 同上（窄竖幅） | 同上 |
| T4 | 远山林线（1 张试作） | 600×260 | 2.308 | L5 贴图层 | generate-building-art.py 横幅（**需 Scene 背景渲染位，属提案类，先出图不定装机**） | 只出候选+人眼 |
| F1 | 花圃 ×3 变体 | 240×140（0.82） | 1.714 | L0 | generate-prop-art-v2.py | prop-art |
| F2 | 野花丛 | 120×80（0.47） | 1.500 | L0 | 同上 | prop-art |
| F3 | 蒲苇/红蓼水岸丛 ×2 变体 | 160×260（1.53） | 0.615 | L0 | 同上 | prop-art |
| M1 | 稻田段（只出田底+畦沟，作物层分离） | 480×180（1.06） | 2.667 | L0 | generate-building-art.py 横幅（同 §四.2 风险） | prop-art+overlap |
| M2 | 旱地菜畦 | 420×160（0.94） | 2.625 | L0 | 同上 | 同上 |
| M3 | 田埂 | 60×120（0.71） | 0.500 | L0 | generate-prop-art-v2.py | prop-art |
| M4 | 秧苗捆 | 80×100（0.59） | 0.800 | L0 | 同上 | prop-art |
| M5 | 稻草人 | 200×340（2.00） | 0.588 | L0–L1 | 同上（竖杆件） | prop-art |
| M6 | 竹筒水车（预留旋转动画位） | 320×420（2.47） | 0.762 | L2 | generate-building-art.py | 六判据+prop-art |
| M7 | 农具架 | 260×160（0.94） | 1.625 | L0 | generate-prop-art-v2.py | prop-art |

### 1.4 布局规格分区补充件（规格 §2.1/§2.3 点名、工单未单列的）

| 物件 | 数量 | 建议尺寸档 | 归属脚本 | 备注 |
|---|---|---|---|---|
| 水口林古樟/枫杨（≥4.5 身位 800+，全镇最高树） | 3 | 480×800（4.71）ar 0.600 | generate-building-art.py（树类条目，L4） | 规格 §2.3-2 水口锚点；触发判据 10 重排概率高 |
| 祠堂（4 身位正殿+门楼） | 1–2 | 门楼 450×680（4.0）/ 正殿 500×680 | generate-building-art.py | 规格 §2.1 区 4 主锚，"命硬/无名不许进祠堂"剧情位 |
| 破庙（壁画伏笔） | 1 | 420×560（3.29） | generate-building-art.py | 区 1 后山带 |
| 猎户茅棚 | 1 | 300×340（2.0） | generate-shop-art.py | 区 2 |
| 农舍 | 4 | 340×430（2.53）ar 0.791 | generate-shop-art.py / building-art | 区 9 东西两翼 |
| 官道树列（行道树复用 T1/T2 变体） | 4–6 | 复用 | — | 区 8 回望线走廊两侧 |
| 歇脚亭 | 1 | 260×300（1.76） | generate-prop-art-v2.py | 区 8 |
| 渡船 | 1 | 300×140 ar 2.14 | generate-prop-art-v2.py（横幅，先单跑） | 区 3 ferry |
| 土地庙小龛 | 1 | 140×180（1.06） | generate-prop-art-v2.py | 水口配套 |
| 巷口小件（灯杆/石敢当/碾盘，节奏点） | 4–6 | 100–180 高 | generate-prop-art-v2.py（灯笼杆条目可复用） | 规格 §2.5：每巷口 1 件 |
| 悬赏牌 / 里正案台 | 2 | 130–160 高 | generate-prop-art-v2.py（告示牌条目风格复用） | 区 5 广场 |
| 武馆擂台 | 1 | 360×240（1.41）ar 1.5 | generate-prop-art-v2.py | 区 7 |
| 祖屋群（民居变体） | 6 | 340×430（2.53） | generate-shop-art.py（residence 风格，generate-prop-art.py 旧表有现成 prompt 可迁移） | 区 7 |
| 村塾 / 药堂医馆 / 布庄 / 粮铺 | 4 | 300×450（2.65） | generate-shop-art.py（MAT-12 清单 herbal-shop/cloth-shop 直接对口，改尺寸重出） | 区 4/6 |

### 1.5 等比放大不重画的 15 件（工单 §二反查表）

只改 `building-plots.json` 的 width/height 两个数（等比，漂移 ≤0.005），**素材零生成成本**，
但改完必须重跑 `check-plot-overlap.py`（判据 10）+ `check-prop-art.py`（判据 2）：
plot-blacksmith ×1.3、plot-stone-house ×1.4、plot-herbal-garden ×1.3、plot-temple-gate ×1.5、
plot-ancient-locust ×2.0、old-willow ×1.5、plot-mountain-pine ×1.6、plot-pine-02 ×1.6、
river-willow ×2 各 ×1.4、竹丛 ×2 各 ×1.4、plot-paifang ×1.4（若不重画）、plot-teahouse ×1.8
（若不重画）。**放大 vs 重画的取舍是工单 §四.3 留给斐哥裁的，本方案不替他选**。

### 1.6 件数总账

- 需生图：R 表 3 + N 表约 14–19（含变体组）+ T/F/M 表约 16–20（含变体）+ §1.4 约 27–31
  ≈ **60–73 张候选**（变体组按中位数估）；
- 等比放大零生成：15 件地块改动；
- 合计地块 35 → 约 95–108 件，落在规格 90–110 目标带内；
- 生图调用估算：每张 1–3 次（building/shop 有横裁重试上限 attempts=3）≈ **90–180 次调用**。
  通道串行、一次一张（斐哥铁律）。

## 2. 走帧素材补全路线（B-03/B-11 + W1-b 衔接）

> 现状（工单 §五审计）：装机 9 张 **9/9 视觉 FAIL、全部责任在生成侧**；台账
> `art-intake-ledger.py --check` = PASS 4 / FAIL 5。W1-b 派单在修 FAIL 5 张，
> 本方案给的是**全量补全路线**，与 W1-b 共用产物目录纪律（候选只落 logs，不装机）。

### 2.1 缺口清单

| 缺口 | 数量 | 来源 |
|---|---|---|
| farmer-male-0 back 补帧 | 2 帧（身份闸 palette 0.47/0.49 卡边） | 10-11 日志 RIG 盘点 |
| farmer-male-0 front 补帧 | 5 帧（8 帧过 3） | 同上 |
| W1-b FAIL 5 张重抽/重切 | farmer-side 1 + merchant front/side 2 + traveller back/front 2 | art-intake-ledger |
| merchant/traveller side 两张的器侧缺口 | side 无 `--view` 支持 | W1-b 派单问题④，答案见 §2.4 |

### 2.2 路线 A：逐帧管线（farmer front/back 补帧，走 generate-portraits-guarded）

命令形状（候选只落 `Tools/logs/rig-01/`，不加 `--install`）：

```
python Tools/generate-portraits-guarded.py --walk --view front --attempts 2
python Tools/generate-portraits-guarded.py --walk --view back  --attempts 2
```

- 每帧链：生图（1024×1536）→ fit_slot 归一 816×1088 → 逐帧三闸（luma≥80 /
  corner-defect / identity palette≥0.50，参考图 `Resources/Art/farmer-male-0-front.png`）
  → 8 帧齐 → assemble 拼 sheet 自动带三闸（切帧/动作/身份）→ 补充切帧门
  （帧高差 ≤8%、脚底散布 ≤6px）。
- 预算：2 view × 8 帧 × attempts 2 = **最多 32 次调用**（通道串行）。
- 已知坑（工单 §5.6 + b-04-report 实测，方案级规避，不改脚本）：
  1. **白底吃白衣**：assemble 落盘是白底 RGB，浅色衣被泛洪吃掉 → 候选先过
     米黄复验流程（a2-walk-pilot-side 的 reflatten 手法）再进闸；
  2. **prompt 服饰段必须对着立绘写**：farmer 是黑头巾/深灰灯笼裤/无绑腿——
     试点 0.315 就是 prompt 抄错人设；guard 脚本里 SHARED_FRONT/BACK 已含色号锚点，
     补帧批次沿用即可；
  3. **换腿可辨**：第二半周期相位词已带 L/R 侧标记，若两轮仍"4 帧复制成 8 帧"，
     备选 4 帧循环方案（报架构师裁决，不擅自降规格）。

### 2.3 路线 B：整条 sheet 一次生成（merchant/traveller 重抽，MAP-21 手法）

工单 §5.5 已定稿参数块（8 帧单行 + 逐格相位词 + margin≥12% + 统一米黄底 +
反褪色词），链路：

```
① 生图：一条 sheet 一次调用（1536×1024 横条，prompt=§5.5 参数块实例化）
② 切帧：python Tools/extract-walk-sheet.py <sheet-raw.png> <frames_dir> 8
        （期望：found==8、脚底线散布 ≤5px）
③ 装配：python Tools/assemble-walk-frames.py --frames <frames> --out <sheet.png>
        --manifest <m.json> --frame-count 8 --ref <turnaround 立绘> --evidence <dir>
        （自动跑切帧/动作/身份三闸；身份闸 ref 用对应角色正视立绘）
④ 复验：python Tools/check-walk-sheet-motion.py <sheet.png> 8 --evidence <dir>
```

- merchant/traveller **没有人设写死在脚本里**，prompt 的 `{subject}` 必须对着
  各自 turnaround/立绘逐条抄服饰色号（merchant 灰白底交替、traveller 褪色都是
  生成侧病，参数块的"uniform flat warm ivory background / NO washed-out"就是杀它们的）。
- 预算：W1-b 给定每张 ≤4 次、整单 ≤16 次；超出停下报告。

### 2.4 side 侧身 sheet 的器侧答案（W1-b 问题④，本方案正面回答）

`generate-portraits-guarded.py --view` 只有 front/back/all（choices 写死），**side
从来不是它出的**。溯源结论（工单 §5.3，commit 级证据）：

| 批次 | commit / 日期 | 覆盖 | 方式 | 通道 |
|---|---|---|---|---|
| RIG-02 | bf5a34c / 2026-10-11 | farmer ×3 向（含 side） | **逐帧**出图，8 个独立 prompt 落 `ArtSource/prompts/character-rig/farmer-male-0-walk-side-*`（自带 contact-L/R 相位词），assemble 拼 6528×1088 | grok-imagine-image-2.0 |
| MAP-21 | f919f89 / 2026-10-14 | merchant/traveller ×3 向共 6 张 | **整条 sheet** 一次生成，`Tools/logs/map-21/extract-map21.py` 切帧 + `install-map21.py` 装机 | gpt-image-2.5 @ hub.oaifree |

⇒ side 的正路是**路线 B（整条 sheet 生成 + 相位词写进 prompt）**，不需要给
guard 脚本加 `--view side`（加 = 改脚本，且 farmer-side 要走 W1-b 的重切/重抽诊断）。
farmer-side 若诊断为重抽，同样走路线 B。

### 2.5 产物路径与台账纪律

- 全部候选/证据落 `Tools/logs/` 下各 run 目录（rig-01/、a2-walk-pilot-side/ 同型），
  **绝不写 `Resources/Art/Characters/`**；
- 台账只许 `python Tools/art-intake-ledger.py` 重生成，`--check` rc=0 才算账平；
- 装机 = 斐哥挑图后架构师 `--install` / install 脚本走，本方案不碰。

## 3. 世界 chunk / 材质侧（布局加密的地基，零新生图）

横巷 4 条、田埂路、farmland 实体化都是 **terrainShape 数据 + 同源管线**的活，
美术侧只需：

```
python Tools/make-world-greybox.py            # terrainShape 改动后重出灰盒
python Tools/compose-final-material.py --all  # 12 材质已在册 PASS，增量模式只重算变动的
python Tools/compose-world-chunks.py --full-grid   # 必须全量 64 块（增量=混合代事故）
python Tools/check-world-seams.py --full-grid      # 验收：112 PASS / 0 WARN / 0 FAIL
python Tools/check-world-materials.py              # 阈值读 world-material-gen.json
```

- farmland 作物层（M1/M2 田底之上叠稻/麦/菜/药）是"需要新增系统"类（工单 §九）：
  数据字段与后端对齐前，**只出田底素材**，作物层贴图不出。
- plaza 亮度（T121/T122）：compose 后 mean 195 无 WARN，观感等斐哥看
  `Tools/logs/map-02/plaza-after.png` 拍板——**这是当前唯一挂在美术侧等用户裁决的项**。

## 4. 总验收序列（任何一批候选出完后按此跑）

```
python Tools/PropChecks/check-prop-art.py        # 画布/alpha/贴底/孤儿棘轮
python Tools/PropChecks/check-plot-overlap.py    # 判据 10 遮挡铁律（放大/新增后必跑）
python Tools/check-world-seams.py --full-grid    # 地形动过才需要
python Tools/art-intake-ledger.py --check        # 走帧动过才需要，rc=0
```

另：落位回填后由布局侧跑 FrontEndShot / TerrainPropShot 六关（不属本方案执行范围）。

## 5. 风险登记（3 条，按伤害排序）

1. **生图通道不可用是硬阻断**：hub.oaifree 当前 `no_available_account`；备用 grok2api
   可用但端点字面量写死在 4 个脚本里，切通道=改脚本=超本方案边界。**全部生图批次
   （60–73 张布局件 + 走帧 5 张重抽 + 7 帧补齐）都排在通道恢复之后**；建议斐哥定夺：
   等 oaifree 恢复，还是由架构师裁决端点切换（grok2api 出的图风格与 gpt-image-2.5
   有差异，混批次装机有人眼不一致风险）。
2. **横幅/极窄新形状未验证**：里正院（1.25）、埠头石阶（2.0）、稻田段（2.667）、
   幌子（0.273）超出建筑/道具管线的既有尺寸档习惯，`nearest_size`/`pick_size` 的归一化
   行为没有先验样本。纪律：**每类新形状先单跑 1 张看归一化结果再批量**，失败就停在
   报告层，不硬试烧额度。
3. **大树放大与新增的遮挡连锁**：古槐 ×2.0、水口古樟 800+ 这类 L4 件底边大幅下推，
   判据 10（footY < NPC BaseY）大概率触发周边 NPC 落位重排——这不是素材侧能单方面
   定的，必须和布局规格同批过门禁；且每屏 placeable ≤30 的红线（规格 §2.7）在
   90–110 件落地后要实跑验证，超了要分期减密。
