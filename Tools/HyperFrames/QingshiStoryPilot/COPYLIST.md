# 最小源文件复制清单

可以直接复制 `source-package/qingshi-story-pilot-source.zip`，它不含已在正式资源中的大图、音乐、完整字体，也不含 MP4 和渲染缓存。需完整独立播放则另复制已验收的 MP4。

## 源包包含

- `index.html`
- `compositions/home.html`、`forge.html`、`herbs.html`、`care.html`、`captions.html`
- `index.motion.json` 和四个 `compositions/*.motion.json`
- `package.json`、`hyperframes.json`、`meta.json`、`AGENTS.md`、`.gitignore`
- `scripts/hf.mjs`、`scripts/build_pilot.py`、`scripts/restore_assets.py`、`scripts/verify_delivery.py`、`scripts/package_source.py`
- `sources/ledger.json`（正文/字幕账本）、`sources/asset-manifest.json`（素材散列映射）、`sources/choice-boundaries.json`（原选择边界）、`sources/registry/transitions-dissolve.html`（已搜索并取用的 registry 参考）
- `assets/vendor/gsap.min.js`、`assets/fonts/LICENSE.txt`
- `README.md`、`SPEC.md`、`STORYLINES.md`、`REVIEW.md`、`COPYLIST.md`
- 最终 QA JSON：check、snapshot 字幕、ffprobe、音频、delivery-qa，以及来源包文件散列清单。

## 不重复备份的正式素材

| pilot 内路径 | 从正式仓库读取 |
|---|---|
| `assets/art/home-coming-of-age.png` | `GameClient/TwelveJade/Assets/Resources/Art/Story/Qingshi/home-coming-of-age.png` |
| `assets/art/livelihood-forge.png` | 同目录 `livelihood-forge.png` |
| `assets/art/livelihood-herbs.png` | 同目录 `livelihood-herbs.png` |
| `assets/art/livelihood-father-care.png` | 同目录 `livelihood-father-care.png` |
| `assets/audio/hidden-stream.mp3` | `GameClient/TwelveJade/Assets/Resources/Audio/Music/hidden-stream.mp3` |
| `assets/fonts/NotoSerifCJKsc-Regular.otf` | `GameClient/TwelveJade/Assets/Resources/Fonts/NotoSerifCJKsc-Regular.otf` |
| `assets/audio/hidden-stream-pilot.wav` | 由原 MP3 本地截取/降音量生成；参数和 SHA-256 在 asset-manifest |

新位置展开源包后，运行 `python scripts/restore_assets.py --game-root "D:/qiqi_me/HarmonyOS/apps/EternalJadeScroll"`，只向展开后的 pilot 写素材；正式资源若与记录散列不同，脚本报错并保留现状。`--check-only` 仅检查现有素材。

## 留本地，无需源码备份

`.hyperframes/`、`review/snapshots/`、`review/final-caption/`、大 PNG、初轮报告、完整 CLI help。三张压缩交付关键帧与成片按主代理交付需要另存。3091 预览属于本机进程，不属于源包。
