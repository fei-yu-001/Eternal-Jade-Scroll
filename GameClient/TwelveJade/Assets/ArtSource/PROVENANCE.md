# 美术来源记录

> 本文件登记世界地形材质的来源、生成方式与验收数据。
> 判定阈值来自 `world-material-gen.json.seamThresholds`：finalPass mean≤12 / p95≤35 / max≤90。
> 素材根目录：`Assets/ArtSource/world/materials/`（美术源，非 Unity Resources 运行时贴图）。

## 一、当前管线（Gate C1-A 起）

所有地形材质统一走 **程序化基底 + Grok overlay** 两层结构，不使用 AI 直出完整材质：

```
make-procedural-base.py   程序化基底（确定性噪声，配置驱动，零硬编码）
        ↓  {name}.base.png + previews/{name}.base.png.2x2.png
compose-final-material.py  叠加 Grok overlay（soft-light-ish，仅改亮度不改色相）
        ↓  {name}.png + previews/{name}.png.2x2.png
check-world-materials.py   接缝 + 尺寸 + 不透明 + 内容探针验收
```

overlay 仅两张，均由本项目自建 grok2api 网关（`127.0.0.1:8000`，模型 `grok-imagine-image`）生成，
原图存 `materials/overlays/originals/`。程序化基底不使用任何随机源——全部噪声来自
`value_noise_fast`（`np.mod` 环绕）与 `hash2d`，重复运行逐字节可复现。

| overlay 文件 | 来源 | 报告状态 | 环绕差 |
|---|---|---|---|
| grass-overlay.png | 自建 grok2api 网关 / grok-imagine-image | PASS | 6.1 |
| stone-overlay.png | 自建 grok2api 网关 / grok-imagine-image | PASS | 9.0 |

## 二、12 张世界地形材质

| 文件 | 用途 | 生成方式 | baseGenerator | overlay 文件 | overlay 来源 | opacity | 最终尺寸 | 接缝 mean/p95/max | 状态 | 生成日期 | 备注 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| world/materials/grass.png | 草地 | procedural + overlay | turf-dirt | grass-overlay.png | Grok（自有网关） | 0.17 | 1024×1024 | 6.0 / 14.0 / 30.0 | **PASS** | 2026-10-03 | 变体 grass-richer-turf 固化；macro-06 宏观调制（macroAmount=0.06, macroChromaAmount=0.03），colStd 0.30→1.61 |
| world/materials/stone-road.png | 石板路 | procedural + overlay | torus-voronoi-flagstone | stone-overlay.png | Grok（自有网关） | 0.16 | 1024×1024 | 5.0 / 19.0 / 32.0 | **PASS** | 2026-10-03 | 变体 stone-worn 固化 |
| world/materials/water.png | 河水 | 沿用既有 | —（useExistingFinal） | — | — | 0.0 | 1024×1024 | 3.8 / 10.0 / 53.0 | **PASS** | 2026-10-02 | 沿用 C0 之前已 PASS 的水材质，未改动 |
| world/materials/dirt-road.png | 踩踏土路 | procedural + overlay | packed-earth | grass-overlay.png | Grok（自有网关） | 0.14 | 1024×1024 | 2.5 / 14.0 / 18.0 | **PASS** | 2026-10-03 | C1-A 新增 |
| world/materials/plaza.png | 广场/集市场地 | procedural + overlay | open-plaza | grass-overlay.png | Grok（自有网关） | 0.08 | 1024×1024 | 3.2 / 14.0 / 36.0 | **PASS** | 2026-10-03 | C1-A 新增。中心 2×2 失败缝根因修复对象：std 26.34 / colStd 6.17 / rowStd 9.09，非平涂。overlay 由任务书原定的 stone-overlay 改为 grass-overlay（stone-overlay 强纹理会破坏广场空旷感） |
| world/materials/bridge.png | 桥面 | procedural + overlay | bridge-deck | stone-overlay.png | Grok（自有网关） | 0.13 | 1024×1024 | 6.7 / 29.0 / 46.0 | **PASS** | 2026-10-03 | C1-A 新增。环面 Voronoi，cellMax=140 |
| world/materials/mountain.png | 山体岩面 | procedural + overlay | rock-face | stone-overlay.png | Grok（自有网关） | 0.10 | 1024×1024 | 2.9 / 10.0 / 20.0 | **PASS** | 2026-10-03 | C1-A 新增 |
| world/materials/mountain-path.png | 山径 | procedural + overlay | trail-stone-dirt | stone-overlay.png | Grok（自有网关） | 0.12 | 1024×1024 | 3.6 / 15.0 / 32.0 | **PASS** | 2026-10-03 | C1-A 新增。比 dirt-road 更石更干 |
| world/materials/farmland.png | 农田 | procedural + overlay | furrow-field | grass-overlay.png | Grok（自有网关） | 0.13 | 1024×1024 | 3.6 / 12.0 / 28.0 | **PASS** | 2026-10-03 | C1-A 新增。水平垄线用 value_noise_fast 独立 x/y scale（stripeScaleX=1024 / stripeScaleY=64），colStd 4.89 非纯横向死条纹 |
| world/materials/riverbank.png | 河岸 | procedural + overlay | wet-shore | grass-overlay.png | Grok（自有网关） | 0.14 | 1024×1024 | 3.9 / 20.0 / 57.0 | **PASS** | 2026-10-03 | C1-A 新增 |
| world/materials/road-edge.png | 路肩 | procedural + overlay | shoulder-transition | grass-overlay.png | Grok（自有网关） | 0.12 | 1024×1024 | 4.0 / 15.0 / 57.0 | **PASS** | 2026-10-03 | C1-A 新增 |
| world/materials/mountain-foot.png | 山脚碎石土 | procedural + overlay | slope-transition | grass-overlay.png | Grok（自有网关） | 0.12 | 1024×1024 | 3.1 / 18.0 / 34.0 | **PASS** | 2026-10-03 | C1-A 新增 |

**统计**：12 张全部 finalPass，零 finalWarn。11 张走程序化基底 + Grok overlay，1 张（water）沿用既有。

## 三、历史记录（Gate C1-A 之前，AI 直出时代，已作废）

以下记录属于早期「Grok 直出完整材质」的尝试，均因 2×2 接缝不合格而失败，
已被上文的程序化基底 + overlay 两层管线取代。保留作为踩坑记录，**不作为当前材质来源**。

| 文件 | 用途 | 生成工具 | 模型/路由 | 提示词文件 | 生成日期 | seed | 授权/来源 | 状态 |
|---|---|---|---|---|---|---|---|---|
| world/materials/stone-road.png | 世界地形材质：石板路 | grok2api / Grok | grok-imagine-image | Assets/ArtSource/prompts/world-materials/stone-road.png.txt | 2026-10-02 | 无 seed | 自有网关生成，项目内使用 | **作废** — 内容合格、风格符合，但 2×2 接缝 mean=33.9（>18），已重试 3 次 |

| 文件 | 用途 | 生成工具 | 模型/路由 | 原图尺寸 | 是否方形 | 处理脚本 | feather | passes | 最终尺寸 | 接缝 mean | p95 | max | 状态 | 生成日期 | seed | 备注 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| world/materials/grass.png | 世界地形材质：草地 | grok2api / Grok | grok-imagine-image, aspect_ratio=1:1, size=1024x1024 | 960x960 | 是 | make-tileable-texture.py（半偏移+羽化混合，未用镜像） | 128 | 2 | 1024x1024 | 18.4 | 51.0 | 88.0 | **作废** | 2026-10-02 | 无 seed | v1 曾画成树冠（内容违规）已重生成修正；接缝 mean/p95 超 WARN 线，重试 3 次用尽 |
| world/materials/stone-road.png | 世界地形材质：石板路 | grok2api / Grok | grok-imagine-image, aspect_ratio=1:1, size=1024x1024 | 960x960 | 是 | 同上 | 128 | 2 | 1024x1024 | 50.2 | 133.0 | 186.0 | **作废** | 2026-10-02 | 无 seed | 大块石板结构导致任何 feather/passes/weight 参数都无法平铺（实测 f192~f384 × p2~p6 均收敛在 28~50） |
| world/materials/water.png | 世界地形材质：河水 | grok2api / Grok | grok-imagine-image, aspect_ratio=1:1, size=1024x1024 | 960x960 | 是 | 同上 | 128 | 2 | 1024x1024 | 3.8 | 10.0 | 53.0 | **PASS（沿用）** | 2026-10-02 | 无 seed | 目检无内容违规；原图环绕差 3.7，处理后 3.8。当前仍在使用 |

## 四、约定

- **不使用 AI 直出完整地形材质**：Grok 只提供 overlay（低结构、中性偏灰的笔触层），基底由程序生成。
- **不生成镜像翻转**：所有平铺依赖 `value_noise_fast` 的环绕机制与 `hash2d` 的周期取模。
- **确定性**：基底与合成均不含 `random` / `np.random` / `time` / chunk id seed，同输入必得同输出。
- **overlay 分配依据实测**：grass-overlay（std 8.7、高频占比 0.002，极平）用于土/草向低结构材质；
  stone-overlay（std 28.8、高频占比 0.117，强纹理）用于桥面/山体/山径，不用���广场。
- **变更留痕**：改任一 generator 或参数后，必须重跑 `compose-final-material.py` 与
  `check-world-materials.py`，并同步更新本表的接缝数据与备注。

## 五、P-01 物件层试点立绘（2026-10-04）

生图走自有 grok2api 网关（127.0.0.1:8000），模型 `grok-imagine-image`，`aspect_ratio=1:1`。
遵守"每张只给一次机会"：网关失败或产物不合格一律不再重试，改记技术债 + 色块占位。
归档位置：提示词 `prompts/{slug}-prop-v1.txt`，原图 `originals/props/{slug}-v1.png`，
成品 `GameClient/TwelveJade/Assets/Resources/Art/Props/{slug}.png`（抠底后）。

| 文件 | 用途 | 模型/路由 | 提示词文件 | 原图 | 是否方形 | 处理脚本 | 四边不透明检查 | 状态 | 生成日期 | 备注 |
|---|---|---|---|---|---|---|---|---|---|---|
| Props/shop-front.png | 建筑地块「街铺」 | grok-imagine-image, 1:1 | prompts/shop-front-prop-v1.txt | 960×960（网关返 JPEG，已转真 PNG） | 是 | cutout-props.py + 外沿 8px 清理 | CLEAN（清前右边缘 8 px） | **PASS（入库）** | 2026-10-04 | 保留像素 34%；已接入 building-plots.json |
| Props/bamboo.png | 树地块「竹丛」 | grok-imagine-image, 1:1 | prompts/bamboo-prop-v1.txt | 960×960（JPEG 转 PNG） | 是 | cutout-props.py | CLEAN | **PASS（入库）** | 2026-10-04 | 保留像素 44%；已接入 building-plots.json |
| Props/willow.png | 树地块「老柳」 | grok-imagine-image, 1:1 | prompts/willow-prop-v1.txt | 960×960（JPEG 转 PNG） | 是 | cutout-props.py | **不合格**：保留像素 96%，四边 上5602 / 下3000 / 左7241 / 右7410 | **未入库** | 2026-10-04 | 柳枝铺满画面、四角取色连通生长吞不掉背景；按规则不重试第二次，本轮回退色块占位（技术债 T019） |

附带发现（**非本轮引入，未处理，留给后续**）：仓库既有的 `Props/willow.png`（HEAD 版本）
四边同样不干净（上 1077 / 下 5466 / 左 14 / 右 145），`Props/stone-lion.png` 右边缘 328 px 不透明。
两者都不在 P-01 白名单职责内，本轮未改，仅记录。

工具改动：`Tools/generate-prop-art.py` 补 `import sys`（原缺失导致脚本无法运行）、新增 `--only`
参数（可只生成指定 slug）、新增 JPEG→真 PNG 转码（架构文档 §18.3：不准把 JPEG 内容存成 .png 假扩展名）。

## 六、青石镇五大灵魂人物立绘（2026-10-05，员工 J · ADR-025）

生图走自有 grok2api 网关（127.0.0.1:8000），模型 `grok-imagine-image`。
本轮按 ADR-025「程序槽位先行」落地**水墨剪影占位**：纯白底直出后按亮度抠成透明通道，
后续 Grok 出全彩高清立绘时**同名覆盖**即可，程序层与对白层零改动。
归档位置：提示词 `prompts/portrait-{slug}-v1.txt`、原图 `originals/portraits/{slug}-v1.raw`、
成品 `GameClient/TwelveJade/Assets/Resources/Art/Portraits/{npcId}.png`（512×768 RGBA）。

| 文件 | 用途 | 模型/路由 | 提示词文件 | 处理脚本 | 尺寸 | 不透明占比 | 状态 | 生成日期 | 备注 |
|---|---|---|---|---|---|---|---|---|---|
| Portraits/npc-shenhuai-an.png | 沈怀安 · 摸骨相士 | grok-imagine-image | prompts/portrait-shenhuai-an-v1.txt | generate-npc-portraits.py（亮度抠白） | 512×768 RGBA | 45% | **PASS（入库）** | 2026-10-05 | 白发道冠青衫；提示词保留 Use case/Asset type 头部 |
| Portraits/npc-shitou.png | 石头 · 拾遗少年 | grok-imagine-image | prompts/portrait-shitou-v1.txt | 同上 | 512×768 RGBA | 30% | **PASS（入库）** | 2026-10-05 | 粗布挽袖少年 |
| Portraits/huolang.png | 货郎老栓 | grok-imagine-image | prompts/portrait-huolang-v1.txt | 同上 | 512×768 RGBA | 15% | **PASS（入库）** | 2026-10-05 | 挑担圆脸行商；占比偏低（画面留白多），不影响左槽位构图 |
| Portraits/npc-zhao-da.png | 赵大 · 守渡铁匠 | grok-imagine-image | prompts/portrait-zhao-da-v1.txt | 同上 | 512×768 RGBA | 38% | **PASS（入库）** | 2026-10-05 | 袒臂持锤 |
| Portraits/npc-yao-lao.png | 药老 · 悬壶郎中 | grok-imagine-image | prompts/portrait-yao-lao-v1.txt | 同上 | 512×768 RGBA | 31% | **PASS（入库）** | 2026-10-05 | 长袍背药篓 |

工具新增：`Tools/generate-npc-portraits.py`（走 `world_paths` 解析路径，输出前过
`assert_canonical_output_path`；网关不可用时回退程序化几何剪影，保证槽位永远有图且有透明通道）。

## 2026-11-04 · 九张运行时走帧纸底清理

本轮为本地后处理，无新增外部生图。7 张源自既有运行时 Walk 图；farmer front/back 源自 `Tools/logs/walk-sheet-xindu/farmer-male-0-{front,back}-norm-v1.png`。`Tools/clean-walksheets.py` 完成纸底、封闭缝隙与轮廓净化，农户侧面另清理人工逐帧复核的脚边地面。保留 6528×1088、8 格布局与既有 GUID。源与输出 SHA256、遮罩、前后图和 Unity 指纹报告见 `Tools/logs/walksheet-cleanup-20261104/`；独立抠图门禁 9/9 通过。

## 2026-11-07 · V2 条形青石、自然路缘与立体门楼

制作人审定设计来源为 `Tools/tasks/dispatch-20261107-qiqi-aesthetic-rebuild-v2.md`。本轮生成接口为用户授权的赠金路由，模型 `gpt-image-2.5-flare`；已授权 CLI fallback。先尝试的本地 Grok 返回502，失败证据保留，未冒称本地生成成功。

4份原图与提示词在 `Tools/logs/nov7-v2-20261107/world/raw/` 和 `world/prompts/`。原图、处理后图、真实像素尺寸、裁切与碰撞映射、SHA见 `world/installed-world-assets.json`。条砖/路缘先降至512再作12px余弦端点校正与重复，保留砖缝结构；门楼仅洋红抠图、去色边及紧致裁切，保留原比例。由 `world/prepare-world-v2.py` 安装。现有资源同名覆盖并保留meta/GUID；路缘为新增独立资源。

道路过渡由全局连续alpha场叠加真实砂石材质，保留语义路面与中心覆盖；牌坊/山门前新增正式巷道，旧两块窄路贴片退役。新门楼地面碰撞由原图接地点重绑，启用既有建筑淡出与人物轮廓。状态：已安装、静态审图完成，V2实机视觉/通行/遮挡待验。此处没有追认旧素材批准，也没有把V1实机证据用于V2。

V2第二次材质安装：首轮band4接缝判据失败，完整原64块与材质留在`world/generation-01/`；不改判据。条砖改为按砌筑周期裁切原图[181,89,1083,1055]后降至512，砂石加入环绕采样sigma1抗锯齿。门前院坪扩至完整院墙宽度。最终来源/变换以`world/installed-world-assets.json`和生成manifest为准。

## 2026-11-07 · 道盘最终双完整莲台

最终使用首席交付 `me/wheel-menu-design/layer-bottom-lotus-base.png` 与 `layer-top-skill-lotus.png`，原字节分别安装到 `Art/UI/Nov7V2/Wheel/base`、`rotor`，同960画布与1:1叠放。程序必须为LotusBase固定、SkillLotusRotor整体旋转（含中央太极）。两源/实装SHA及来源文档SHA见 `Tools/logs/nov7-v2-20261107/wheel/final-two-lotus-contract.json`。这两张由首席归档，父方未重新生成或改画。

此前父方根据早期定案用授权赠金gpt-image-2.5-flare编辑参考生成的无字基座/齿环已经作废，完整候选与提示词保留在wheel/raw、prompts及superseded目录，运行时没有继续使用它们。独立taiji已退出Resources与目录清单，PNG及GUID保留在wheel/superseded-runtime。另有8张256透明图腾从原概念图逐图裁取、除去背景/错误字，供真实功能覆盖层使用；原图及处理区域可追溯。

源素材窄审记录 `me/2026-11-07-道盘正式切图窄审.md`：最终底座四角仍alpha255、底部题字与顶层重复名称须由实机明确核验，不能把源路径合规等同视觉验收。真实8功能以README顺序与产品调用为准。
