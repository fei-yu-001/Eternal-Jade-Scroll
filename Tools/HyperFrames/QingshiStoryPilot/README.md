# 青石故里 · 一点暖意（HyperFrames 独立样片）

工程：`D:/ruangong/HyperFrames/qingshi-story-pilot`。正式游戏代码未接入，源码继续冻结。

## 播放与编辑

- 成片：`renders/qingshi-story-pilot.mp4`。
- 本机编辑预览：`http://localhost:3091/#project/qingshi-story-pilot`。3091 已保留；可编辑时间线、字幕变量与素材。
- 若预览已关闭，在本目录执行 `npm run dev`；`npm run status` 检查，`npm run stop` 只停止本工程预览。
- 改完后执行 `npm run check`，`npm run snapshot`，检查画面后执行 `npm run render`。
- `python scripts/verify_delivery.py` 对实际 MP4 做 ffprobe、完整解码、三次交接分析及关键帧导出。

CLI 固定使用本机已有 HyperFrames **0.8.202**，通过 `scripts/hf.mjs` 调用；不会安装、升级或覆盖任何工具。换机器时将 `QINGSHI_HYPERFRAMES_CLI` 设为现有 0.8.202 的 `bin/hyperframes.mjs` 路径。需已有 Node、Chrome、FFmpeg/ffprobe；Python 脚本只用标准库。脚本入口会把渲染临时目录放在本工程 `.hyperframes/tmp`。

## 源文件

- `index.html`：32.4 秒总时间线、颜色/原图/字幕变量、四个镜头挂载、音乐与淡入出。
- `compositions/home.html`、`forge.html`、`herbs.html`、`care.html`：每镜 9 秒，36 帧叠化与持续慢运镜；原画完整头手保留。
- `compositions/captions.html`：唯一字幕轨，8 条原文短句。热敷句为“热气慢慢透进爹的膝头。”。
- `index.motion.json`：当前 CLI 实际读取的 9 条运动断言，防静止、晚出现和画面越界。各镜 `.motion.json` 是镜头意图的局部记录；0.8.202 不递归读取它们。
- `sources/ledger.json`：65 页 JSON 的散列、四张原分镜所对应的正文/索引、8 条字幕时间与出处。
- `sources/asset-manifest.json`：9 个运行素材的路径、大小、SHA-256 与正式资源映射。
- `scripts/build_pilot.py`：初次由正式 JSON 提取字幕和生成 HTML 的作者脚本。**手工修改 HTML 后不要盲目重跑，它会重新生成时间线。** 预览和渲染不需要再访问游戏仓库。
- `scripts/restore_assets.py`：小源包展开后，从正式资源补齐素材；先验散列，保留已有改动，始终只往本工程写。
- `SPEC.md`：镜头、构图、安全区、音频和验收要求。`REVIEW.md`：实际检查结果与局限。
- `COPYLIST.md`：给主代理备份的最小文件清单和素材复用方式；`scripts/package_source.py` 按固定白名单打包并校验每个源文件的 SHA-256。

不打包缓存、浏览器状态和临时帧；原版大图、音乐、字体在正式游戏已有，因此最小源包只保留映射和恢复脚本。完整 pilot 目录已含这些本地副本，可直接预览。

## 原素材与声音

原图：`home-coming-of-age`、`livelihood-forge`、`livelihood-herbs`、`livelihood-father-care`。它们都来自正式 `Resources/Art/Story/Qingshi`，未生成替代人物或修改原图。

配乐：正式 `Resources/Audio/Music/hidden-stream.mp3`，截取 18.0–50.4 秒，降低 6.24dB 后保存为 48kHz PCM，时间线淡入 1.2 秒、淡出 2.4 秒。只有音乐，没有配音或额外鸟叫/行动音。字体为正式 `Resources/Fonts/NotoSerifCJKsc-Regular.otf`，附原许可。GSAP 3.15.0 来自已安装 CLI 自带的本地 vendor 文件。

## 与现有 Unity 选择节点的接入边界（建议，未实施）

这是跨两章的宣传式节选。原 `home` 第 2/4 页和 `livelihood` 第 2 页有选择，不能把 32.4 秒整片播完等同于两章完成。

生产应沿原 JSON 的选择边界拆成短片：片段末停住，显示现有纸签选项；选择仍由现有选择/回复逻辑处理，再播放对应后续片段。只有完整当前章节已达到原结尾时，才交现有 `Complete` 单次保存；重复完成拒绝、失败回滚、切档取消、回看不推进和容量判据继续由原契约负责。播放器本身不得直接写 flag、发奖励或跳过选择。

本片验证静画连续运镜与交接的观感，角色肢体仍为原画姿态；不以镜头移动代称完整人物动作表演。
