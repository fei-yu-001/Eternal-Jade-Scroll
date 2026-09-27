# 图像来源与使用记录

| 项目 | 记录 |
| --- | --- |
| 制作目的 | 《十二玉楼长生经》首轮主菜单与平民角色展示 |
| 制作日期 | 2026-09-26（菜单背景）；2026-09-27（角色三视图） |
| 服务（角色三视图） | 项目所有者本人署运维的 grok2api 网关（本机 `D:\work\amaze\DDyu`，监听 `127.0.0.1:8000`），账号池为所有者自有的 Grok 网页账号；所有者于 2026-09-27 明确授权使用该网关生图 |
| 图像接口 | OpenAI 兼容 `POST /v1/images/generations`（本机回环，未出公网） |
| 模型 | `grok-imagine-image`（即路由 `Web/grok-imagine-image`，上游 `grok-imagine-image-quality`） |
| 输入素材 | 自编文字提示词（`prompts/` 内 v1 版原文）；未上传第三方美术、角色或现实人物照片 |
| 密钥 | 网关客户端密钥从其本地数据库加密字段解密取得，用后即删临时文件；仓库无密钥 |
| 服务文档 | 网关仓库自带文档（`D:\work\amaze\DDyu\README.zh-CN.md`、swagger 注解） |
| 授权状态 | 图像由所有者自有账号在自己控制的网关内生成，输出归所有者使用，可用于本项目。GeiliAPI 渠道未启用（见下方验收记录），与本次交付无关。 |

## 资产清单

| 游戏资源 | 来源 | 授权 / 备注 |
| --- | --- | --- |
| `GameClient/TwelveJade/Assets/Resources/Art/menu-background.jpg` | 大都会艺术博物馆藏叶欣《Landscape》，约 1645–1655，藏品编号 65625，原图 3280×2465 | The Met Open Access 项目公共领域图像，藏品 API `isPublicDomain=true`。原图未经改绘；Unity 运行时按 16:9 居中裁切显示。藏品页：<https://www.metmuseum.org/art/collection/search/65625>，开放获取政策：<https://www.metmuseum.org/about-the-met/policies-and-documents/open-access>。画作晚于游戏时代，仅作界面气氛图，不用于历史考据。 |
| `GameClient/TwelveJade/Assets/Resources/Art/{farmer,traveller,merchant}-{front,side,back}.png`（9 张，各约 427×720） | `grok-imagine-image`，2026-09-27 生成；提示词见 `prompts/{farmer,traveller,merchant}-turnaround-v1.txt`；参数 `n=1, aspect_ratio=16:9, response_format=b64_json`；选定原图存于 `Assets/ArtSource/originals/{farmer,traveller,merchant}-turnaround-v1.png`（1280×720） | 所有者自有 Grok 账号经自有网关生成，输出可用。加工步骤：整图按三等分横列裁切为正面/右视/背面三张竖版视图，未做其他修改；游戏内左视图由右视图镜像。生成后经人工验图：三视图同人同装、朝向正确、头脚完整、无文字水印；农家、行旅、商家三张整体画风一致。已知瑕疵：商家背面图右侧带轻微底色渐变、地面有淡影（提示词本要求无影），幅度不影响展示卡用途，保留不重生成。 |

## 验收记录

2026-09-26：通过用户已授权的密钥读取模型列表，列表包含 Codex/GPT 聊天模型，但没有 `gpt-image-2`。使用随附 CLI 向指定图像端点发起一次 `gpt-image-2`、`quality=low`、`size=1536x1024` 的菜单背景请求，服务端返回：

```text
HTTP 400 MODEL_NOT_AVAILABLE
the selected groups do not declare support for this model and endpoint
```

因此当日没有生成成品。GeiliAPI 渠道自即日起搁置：该密钥分组不含生图模型，且 2026-09-27 当日额度已尽；后续是否启用取决于所有者是否为其开通图像分组。

2026-09-27：所有者指引自有 grok2api 网关（`D:\work\amaze\DDyu`）取用生图能力。经网关 `/v1/models` 确认存在 `grok-imagine-image` 等图像路由；先用 lite 模型冒烟测试发现其输出与提示词无关（一次返回无关图像、一次 502 `upstream_unavailable`），弃用 lite；改用 `grok-imagine-image` 后，农家、行旅、商家三次请求全部一次成功（5–7 秒/张），验图通过后裁切导入。工程接入方式：`CharacterPreset.Facing()` 按 `Resources/Art/{id}-front|side|back` 自动加载，左视图运行时镜像；原先「造型画稿待接入」占位随之消失。
