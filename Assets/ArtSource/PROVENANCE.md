# 图像来源与使用记录

| 项目 | 记录 |
| --- | --- |
| 制作目的 | 《十二玉楼长生经》首轮主菜单与平民角色展示 |
| 制作日期 | 2026-09-26 |
| 服务 | 用户指定并授权的 GeiliAPI |
| 图像接口 | `https://image-direct.geiliapi.com/v1` |
| 模型 | `gpt-image-2` |
| 工具 | Codex imagegen 技能随附 CLI，API 模式 |
| 输入素材 | 自编文字提示词；未上传第三方美术、角色或现实人物照片 |
| 密钥 | 本机环境变量；仓库无密钥 |
| 服务文档 | <https://sub.geiliapi.com/docs/gpt-image.llm.txt> |
| 授权状态 | 项目所有者已授权生成并用于本项目。服务商对输出使用与再分发的完整条款尚待归档，不能据接口文档声称素材已取得 CC0、MIT 或其他第三方许可。公开商业发行前补齐条款记录。 |

## 资产清单

| 游戏资源 | 来源 | 授权 / 备注 |
| --- | --- | --- |
| `GameClient/TwelveJade/Assets/Resources/Art/menu-background.jpg` | 大都会艺术博物馆藏叶欣《Landscape》，约 1645–1655，藏品编号 65625，原图 3280×2465 | The Met Open Access 项目公共领域图像，藏品 API `isPublicDomain=true`。原图未经改绘；Unity 运行时按 16:9 居中裁切显示。藏品页：<https://www.metmuseum.org/art/collection/search/65625>，开放获取政策：<https://www.metmuseum.org/about-the-met/policies-and-documents/open-access>。画作晚于游戏时代，仅作界面气氛图，不用于历史考据。 |

农家、行旅、商家三视图尚未生成。四份提示词已经存档；启用图像分组后再执行、检查方向一致性并补记来源。

AI 图像生成成功并验图后，在此填写文件名、尺寸、参数和实际加工步骤。原始输出保存在 `output/imagegen/`，Unity 使用的衍生图保存在客户端资源目录。

## 验收记录

2026-09-26：通过用户已授权的密钥读取模型列表，列表包含 Codex/GPT 聊天模型，但没有 `gpt-image-2`。使用随附 CLI 向指定图像端点发起一次 `gpt-image-2`、`quality=low`、`size=1536x1024` 的菜单背景请求，服务端返回：

```text
HTTP 400 MODEL_NOT_AVAILABLE
the selected groups do not declare support for this model and endpoint
```

因此没有生成成品，也没有进行其他付费图像请求或切换模型。需要在 GeiliAPI 控制台为现有密钥启用 `GPT Image 2 - 1K` 分组，或在本机 `GEILI_API_KEY` 配置拥有该分组的图像密钥。只有确认服务支持模型后才能继续验图和导入；不将提示词或概念描述算作已交付美术。
