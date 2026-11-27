# HyperFrames 青石故里动画样片 · 2026-11-27

斐哥反馈逐页漫画“一卡一卡”，本轮授权上方主力助手在 `D:/ruangong/HyperFrames/qingshi-story-pilot` 制作独立样片。游戏正式包 `qingshi-outline-20261127` 已单独验证与备份；此样片尚未接入游戏播放或存档结算。

## 样片内容

《青石故里 · 一点暖意》，32.4秒，1920×1080、30fps，4个镜头、8条短字幕。按加冠→铁匠铺劳动→药铺换药渣→归家热敷顺序，引用已核对的4张原画；主角不在画面内，兼容男女主。字幕从现有正文逐字摘取；最后热敷句缩为“热气慢慢透进爹的膝头。”，留足阅读时间。

每镜9秒，下一镜提前1.2秒入场，前后镜头在重叠区保持缓慢平移和缩放。原图3:2以留幅方式放入16:9，保全头手；没有重绘嘴型、手脚或骨骼动作。本样片验证连续运镜、转场和配乐能否改善阅读节奏，不能称为整本角色动画已经完成。

用户提供的hidden-stream原曲取18.0–50.4秒，降低6.24dB，片头1.2秒淡入、片尾2.4秒淡出。无配音、无合成鸟叫；没有合适本地动作录音，因此本轮只有原配乐。音乐做波形/响度验证，不宣称人工听审。

## 后续接入边界

这段样片跨越加冠和谋生两章，并省略途中选择，不能直接播放完就把两章记为完成。正式接入应按现有选择节点切分短片，到选择时停留画面并显示纸签选项；选定后接原回复，读完单章才调用现有QingshiStory.Complete，保留顺序推进、单次写盘和失败回滚。

| 章节 | 原选择所在页（从1开始） | 适合切分的演出范围 |
| --- | --- | --- |
| home | 2、4 | 1–2 / 3–4 / 5–9 |
| livelihood | 2 | 1–2 / 3–14 |
| tiger | 3 | 1–3 / 4–7 |
| undercurrent | 3 | 1–3 / 4–7 |
| tomb | 无 | 1–10 |
| calamity | 无 | 1–10 |
| departure | 2 | 1–2 / 3–8 |

回看保持只读；退出保持当前未完成段，沿用现有首镜续读约定。视频片尾和玩家点击需要共用一次完成门禁，避免结算两次。样片内自带音乐，真正接入时应由游戏统一控制音量与暂停，避免与当前背景音乐叠播。视频资源应提前准备；失败仍可回到原漫画阅读器。

## 验证与文件

成片为 `D:/ruangong/HyperFrames/qingshi-story-pilot/renders/qingshi-story-pilot.mp4`，71,915,462字节，SHA256 `b45e72f42c4aa1bdb43dd378905f188ca5042b5a5bb83c0e2c1889b8d6f37884`。本机预览为 `http://localhost:3091/#project/qingshi-story-pilot`；预览关闭后在工程执行 `npm run dev` 可重开。

- 使用本机已有HyperFrames 0.8.202，未升级或调用付费生成。check实际运行浏览器，runtime/layout/motion/contrast/craft均通过；保留字幕同轨8项的维护性提示1条，不称0警告。运动检查使用该版本实际读取的根index.motion.json，未把未被递归读取的局部motion文件当作已执行。
- delivery渲染实际成功；ffprobe为H.264/yuv420p、1920×1080、30/1fps、32.4秒，972帧；音轨仅1条AAC、48kHz双声道。
- 全部972帧解码后以256×144灰度检查，没有完全相同的连续帧；三次叠化没有检测到突降黑场。该测量不等于所有播放器性能或人物动作验收。主代理另逐张复核编码后转场条与热敷字幕，未将截图检查称为完整播放听审。
- 音频实际分析为-17.8 LUFS，真峰值-7.2dB，削波0，无静音段告警；没有声称人工听审。
- 可编辑工程保留在上述独立目录。仓库备份位于Tools/HyperFrames/QingshiStoryPilot；HTML、脚本、时间轴和素材散列入库，原画/字体/音乐从正式资源按散列恢复，不重复提交浏览器缓存、原图或渲染文件。

最小源包为39文件、75,377字节；主代理已逐文件SHA核对后展开到仓库Tools/HyperFrames/QingshiStoryPilot，另保存SOURCE-MANIFEST.json。该目录执行 `python scripts/restore_assets.py --check-only` 可验证恢复素材；完整源包不含成片和浏览器缓存。

仓库副本另做一次浏览器检查，ok=true、browserSkipped=false，runtime/layout/motion/contrast/craft均0错误，仍仅1条字幕同轨提示；本地报告为Tools/logs/qingshi-direction-20261127/pilot-backup-check.json。39个冻结源文件逐一通过SHA-256核对，9项素材检查通过。源包同时含CRLF与LF，仓库副本新增局部.gitattributes禁止换行转换，以保留SOURCE-MANIFEST.json对应的原始字节；该属性文件和清单本身属于备份附加文件。
