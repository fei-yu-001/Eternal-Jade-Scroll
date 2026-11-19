---
name: jade-scroll-world-terrain-pipeline
description: 大世界地形底座与贴图管线的真源、脚本执行顺序、产物目录、接缝门禁判据与禁踩坑。当改动 Tools/ 下的 make-* / compose-* / check-* 地形脚本、world 材质与 chunk 产物、Assets/Resources/Config 下的 world-*/town-map、或涉及 Gate C0/C1 地形线时使用。
---

# 世界地形管线

## Overview

灰盒世界 8192²、8×8=64 chunk、每块 1024px。地面观感走"程序化基底 + AI overlay"两层合成。**程序化只能做灰盒，不能当美术**——正式观感必须过用户眼睛，指标 PASS 不构成完成。

## 真源：两条独立链，不要混

**几何 / 世界结构** — 真源是 `Tools/make-world-greybox.py` 顶部常量（`CHUNKS=8`、`CHUNK_PX=1024`、`MOUNTAIN_Y` / `RIVER_Y` / `MAIN_X`）。它导出 `Resources/Config/town-world-gen.json` 的 `terrainShape` 节点（键名故意不叫 `terrain`，原因写在脚本注释里），再由 `sync-town-map-from-world-gen.py` 白名单派生进 `town-map.json`。

> **改布局的唯一入口是改脚本重跑。手改 `town-map.json` 里的坐标 = 下次同步就被覆盖。**

**材质 / 合成参数** — 真源是 `Assets/Resources/Config/world-material-gen.json`（12 材质 × generator 参数 + overlay opacity）与 `world-compose.json`（合成与阈值）。这两个文件就是真源，对应脚本零硬编码。

**派生物（同一参数散落多处，改真源后必须同步）**：`chunkSize=1024` / `8×8` / `worldSize=8192` 同时出现在 greybox 脚本、`terrain-types.json`（自述"同步一份"）、`world-compose.json`、`town-map.json`。`town-map.json` 独有运行期表现参数（`worldScale`、`acceptance`）。

`town-map.json` 的 `acceptance.note` 里原文写着 **"Do not expand world in Gate C0"**，并记录"历史文档提过 6.4x 横向，已登记为技术债"。扩世界尺寸不是本轮该做的事。

所有路径解析必须经 `Tools/world_paths.py`，它用 `assert_canonical_output_path` 拒写仓库根的旧 `Assets/ArtSource`——那条路径是 T001 事故源。

## 脚本清单

| 脚本 | 职责 |
|---|---|
| `make-world-greybox.py` | 世界布局真源：产灰盒分块图 + blocked + `terrainShape` |
| `make-world-mask.py` / `check-world-mask.py` | 读 `terrainShape` 求值 → 语义 mask；mask 合理性 + 与 blocked 交叉一致性 |
| `make-procedural-base.py` | 11 个确定性 generator，产 `{name}.base.png` |
| `make-ai-overlay.py` | 调 grok2api（`http://127.0.0.1:8000`，模型 `grok-imagine-image`）产低结构 overlay → `overlays/originals/`。`aspect_ratio=1:1` 才出方形图，网关返回 JPEG 字节需 PIL 转真 PNG |
| `make-procedural-overlay.py` | Grok FAIL 时的程序兜底（`.fallback.png`） |
| `make-tileable-texture.py` | 原图 → 无缝可平铺（边缘羽化 crossfade，`--feather 128 --passes 2`） |
| `compose-final-material.py` | 基底 + overlay 两层合成正式材质（opacity 上限 0.18） |
| `make-material-variants.py` | grass / stone-road 各 4 视觉变体，配置覆盖、生成器零修改 |
| `compose-world-chunks.py` | mask 权重混合材质 → 合成 chunk（`--center-preview` / `--full-grid`） |
| `check-world-seams.py` | 接缝门禁 v2.1 |
| `check-world-materials.py` | 材质验收：接缝 / 尺寸 / 不透明 / 内容探针 |
| `sync-town-map-from-world-gen.py` | gen → town-map 白名单同步（先 `--dry-run` 再 `--write`） |
| `check-town-config-consistency.py` | gen ↔ map 字段一致性 |

`generate-art.py` 是通用一键生图 CLI，**不是**地形专用。`make-world-map.py`（5×5=25 块）已被 8×8 greybox 取代，属废弃路线，别接着用。

## 产物目录

正式根 = `GameClient/TwelveJade/Assets/ArtSource/`。

**输入源（该手改）**：`Resources/Config/world-material-gen.json`、`world-compose.json`、`terrain-types.json`、`Tools/make-world-greybox.py`、`ArtSource/prompts/world-materials/`。

**生成物（禁手改）**：`world/materials/procedural/*.base.png`、`overlays/{name}-overlay.png` 与 `overlays/originals/`、`materials/{name}.png`（12 张）与 `previews/*.2x2.png`、`materials/variants/{grass,stone-road}/`、`world/masks/world-mask.png`、`world/chunks-preview/`（临时）、`world/chunks-full/`（64 块 + `world-overview-thumb.png`）、`Resources/WorldGreybox/`（64+1，Unity 运行期灰盒）、`Tools/logs/*`。

## 接缝门禁 v2.1：判据与**未收口状态**

实现在 `Tools/check-world-seams.py`（版本号写在 `seam_judge_v21` 函数名与 docstring）。

1. **boundary relative gate（主判据）**：比较 `A[:,-1,:]` 与 `B[:,0,:]` 的 mean/p95，对侧是两侧块**内部**按分离距离 `s=2d+1` 的相邻列/行差分布分位数。基线分位常量 `BASELINE_PCT = 99.0`（演进轨迹 P95→P97→P99）。
2. **inner severe gate（辅）**：`normal_profile` 沿缝法线推进 d=1..3，ratio>1.5 → FAIL，1.2<ratio≤1.5 → WARN。
3. **绝对阈值已降为 INFO，不再参与判定**。配置里的 `seamThresholds`（mean 4.0 / p95 12.0 / max 30.0）与材质级 `finalPass`（12/35/90）、`finalWarn`（18/50/120）仍在文件里，读的时候注意它们不再是门禁。
4. **已废弃判据**：`offset_profile`（tangent 方向，属自证循环，代码里标 `INVALID_INFO`）、`window4`。不要复活它们。

> **这条线没有收口。** 全网格 112 条缝实测 109 PASS / **3 FAIL（压线）**。候选修法是"同形状重算基线"，但**未获任务书批准、尚未实施，根因也还没定论**（指向基线统计形状失配，等规划方拍板）。不要把 v2.1 当成已验证正确的口径来引用，更不要在这轮自行改判据去"凑 PASS"——改门禁口径本身就是一个需要任务书批准的决定。

## 全量合成与预检

```bash
python Tools/compose-world-chunks.py --center-preview   # 中心 2x2 预检：chunk (3,3)(4,3)(3,4)(4,4)
python Tools/check-world-seams.py --center-preview      # 收敛后再上全量
python Tools/compose-world-chunks.py --full-grid        # 64 块 → chunks-full/ + overview 缩略图
python Tools/check-world-seams.py --full-grid           # 56 水平 + 56 垂直 = 112 条，脚本内断言 expect 112
```

中心 2×2 是全量合成的**预检子集**，不是可跳过的步骤。

## 权重场必须全局统一

`Tools/compose-world-chunks.py`。早期实现是每 chunk 独立"裁 mask → resize → blur"，BILINEAR 放大采样网格在相邻 chunk 间不连续，导致接缝两侧过渡权重不同、水平缝 mean 超标。修法是**权重场按全图 8192² 1:1 算一次、blur 也在全图上**，各 chunk 从同一连续场裁剪。

配套坑：边缘 chunk 的 `rx0/ry0` 为负，直接切片会落空导致世界四角全黑，必须按偏移零填充。

## 禁踩坑

- **overlap 不是接缝杠杆**：实测 192/256/384 三个值数值一位不差，已回退 128。别再调它。
- `world_paths.resolve_repo_path(allow_missing=True)` 会返回仓库根的旧路径，`make-material-variants.py` 里显式避开，新代码同样注意。
- bridge 这类硬边材质需降采样噪声（`world-compose.json` 里 `materialNoiseAmplitude: bridge.png = 8`）。
- `check-world-materials.py` **无参才是查配置全集**（12 张）。指定 `--names` 做局部验证后，收口前必须补跑一次无参全量——这条是复核时补的洞。
- 产物先入 git 再动目录（T001/T013 的教训：路径迁移删掉过未跟踪的 overlay 原图）。动 `ArtSource` 下未跟踪文件前先 dry-run + 备份 + 哈希校验。

## 重跑顺序

改材质 overlay：

```bash
python Tools/make-ai-overlay.py --names grass stone
python Tools/make-tileable-texture.py --files grass-overlay.png stone-overlay.png
python Tools/compose-final-material.py --names grass stone-road   # 或 --all
python Tools/check-world-materials.py                             # 无参 = 全集
```

地形底座全链：

```bash
python Tools/make-world-greybox.py                    # 改布局唯一入口
python Tools/sync-town-map-from-world-gen.py --dry-run
python Tools/sync-town-map-from-world-gen.py --write
python Tools/check-town-config-consistency.py
python Tools/make-procedural-base.py --all
python Tools/compose-final-material.py --all
python Tools/make-world-mask.py && python Tools/check-world-mask.py
python Tools/compose-world-chunks.py --center-preview
python Tools/check-world-seams.py --center-preview
```

Unity 侧验收：`Tools/SaveChecks/WorldChecks.cs` + `FrontEndShot.CaptureTerrain`（批处理命令见 `jade-scroll-gate-cycle`）。

## Unity 侧现状

**没有 Unity `Terrain` 资产**（`.terrain` 文件不存在），也没有地形专用 Editor 脚本。呈现方式是 `Scripts/Presentation/FrontEndWorld.cs`：`worldRoot` 下每 chunk 一个 `RawImage`，`Resources.Load<Texture2D>(chunk.Art)`；小地图底图走 `town.OverviewArt`。解析在 `Scripts/Core/TownMap.cs` 的 `MapChunk`。

当前 `town-map.json` 的 `chunks[].art` 仍指向 `WorldGreybox/chunk-r0-cN`、`phase = "world-greybox"`——**12 张正式材质与 chunks-full 尚未进 Resources**，那是 Gate C1-C 的活，不要在本轮顺手做掉。
