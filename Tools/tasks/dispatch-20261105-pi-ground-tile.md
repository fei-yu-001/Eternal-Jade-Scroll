# 首席架构师七七 任务派单：青石路面四方连续贴图加工与验证

- **派单对象**：pi（左上角窗格 wH:p2 · 已挂载 S2API GPT-6-Astra）
- **输入原图**：`GameClient/TwelveJade/Assets/ArtSource/incoming-20261104/ground-qingshi-flagstone-raw.jpg`（16:9 横版）
- **交付目标**：
  1. 从 16:9 原图中提取纹理最均匀、青石质感最好的区域，裁切为标准正方形（如 512x512 或 1024x1024）。
  2. 采用图像处理算法（水平与垂直 Cross-fade 边缘融合 / 频域无缝贴图算法），将其处理为**严格的四方连续无缝贴图（Seamless Tileable Texture）**。
  3. 生成一张 2x2（或 3x3）的拼接预览图（如 `ground-qingshi-seamless-test-2x2.png`），检验拼接处是否存在可见接缝、明暗断层或图案跳变。
  4. 将成品正方形贴图输出至：`GameClient/TwelveJade/Assets/Resources/Art/Background/town-ground-seamless.png`（并在对应 ArtSource 留存副本）。
  5. 输出执行脚本与复核数据。
