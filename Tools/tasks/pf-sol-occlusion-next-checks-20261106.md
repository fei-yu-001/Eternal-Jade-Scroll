# pF 窄审 — 门楼后续验证实施稿

你是现有pF，父方已为你配置gpt-6.1-sol/max独立渠道；无需读取任何密钥、全局配置或供应商目录，不调用外网生图。今天2026-11-06。主Unity保持，pE唯一Unity执行。当前源正在最终冻结审计，不写Assets/Packages/ProjectSettings，不Git提交、不启动Unity、不触发其他助手。

已完成UI Menu02=255PASS，门楼Town=130PASS/6PENDING，父方限定视觉范围通过。先只读Tools/logs/nov6-occlusion-experience-20261106/parent-audit/HANDOFF.md和batch-03/HANDOFF.md中未达项，以及BuildingOcclusionFader/TravelerSilhouette/TravelerSilhouetteGraphic这3文件必要段。不要重审整晚历史。

交付具体可执行的下一轮验证设计，重点：真实活动Mask/Stencil遮罩、>0.1s遮挡帧、CPU每帧<0.1ms的可测边界。写域仅Tools/logs/nov6-sol-occlusion-test-design-20261106/。给出测试前置、触发方式、测量点、如何区分CPU/GPU和首帧预热、必需原始证据、失败条件；若现有工具/产品钩子不足，列最小独立验收代码补丁草案但不装源。不将推测当实机PASS，不扩到非Frames分支修复。每项引用当前文件行号/现有证据名，最后HANDOFF.md≤120行。完成后idle，不擅自继续。
