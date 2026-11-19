# TwelveJade Server · API 契约

> 所有端点统一返回信封 `{ code, message, data }`:`code == 0` 为成功,非 0 为失败;
> `message` 给人看,客户端不依赖它的文案;`data` 为 null 时整个字段不输出。
>
> **本文档里所有 JSON 样例均取自对本地真后端的实测 curl 原文**(2026-10-05,
> W3-C 抓取,样例存档 `.batch/w3/samples/`),不是手拼的想象。
> 唯一例外是 `40903`:它只在并发竞态且服务端自动重试仍失败时出现,单条 curl 无法
> 稳定复现,其响应形状由 `SaveUploadRobustnessTest` 的集成断言实测钉死(同一信封)。
>
> 与错误码 `ApiCode.java` 的对应关系由 `ApiContractCoverageTest` 双向保证:
> 每个码有生产者、有测试覆盖、出现在本文档里,多一个少一个都红。

| 错误码 | 含义 |
|---|---|
| `0` | 成功 |
| `40001` | 请求不合法(缺字段、槽位越界、参数超长/含空白) |
| `40101` | 账号或密码不对(**不区分**"无此用户"与"密码错",防撞库探测) |
| `40102` | 令牌无效或未登录 |
| `40901` | 存档版本高于本服务器支持的版本 |
| `40902` | 用户名已被占用 |
| `40903` | 槽位冲突(并发上传撞车且服务端重试后仍失败) |
| `40904` | 登录失败次数过多(锁定中) |
| `50001` | 预留：服务器内部错误，当前没有生产者 |

---

### `GET /api/health`

鉴权:无。请求体:无。

健康探针。`data.stage` 是**有意暴露**的服务端阶段标识(联调期对表用,非敏感信息)。

成功响应(HTTP 200):

```json
{"code":0,"message":"成功","data":{"service":"twelve-jade-server","stage":"M6-02-S","status":"up"}}
```

| HTTP | code | 场景 |
|---|---|---|
| 200 | 0 | 服务在线(唯一路径) |

### `POST /api/auth/register`　body `{ username, password }`

鉴权:无。`username` 3–32 字符、不含空白;`password` 6–64 字符、不含空白。

成功响应(HTTP 200,注册即登录):

```json
{"code":0,"message":"成功","data":{"userId":1,"token":"24b16257-4311-4d0a-9d70-c6402e66f6b3"}}
```

| HTTP | code | 场景 |
|---|---|---|
| 200 | 0 | 成功 |
| 400 | 40001 | 参数不合法(空/超长/含空白)或请求体不是 JSON 对象 |
| 409 | 40902 | 用户名已被占用。实测:`{"code":40902,"message":"用户名已被占用：w3docuser"}` |

### `POST /api/auth/login`　body `{ username, password }`

鉴权:无。

成功响应(HTTP 200,**每次登录换发新 token,旧的作废**):

```json
{"code":0,"message":"成功","data":{"userId":1,"token":"f700e24e-52ae-4f6a-a788-89d2e539ada8"}}
```

| HTTP | code | 场景 |
|---|---|---|
| 200 | 0 | 成功 |
| 401 | 40101 | 账号或密码不对。实测:`{"code":40101,"message":"账号或密码不对"}` |
| 429 | 40904 | 登录失败次数过多(同一用户名连续 5 次失败 → 锁 10 分钟,锁定期内正确密码也拒)。实测:`{"code":40904,"message":"登录失败次数过多，请稍后再试"}` |

### `POST /api/auth/refresh`　头 `X-Auth-Token`

鉴权:需要(有效未过期的 token;TTL 7 天,过期与无效一律 401/40102)。

成功响应(HTTP 200,换发新 token,旧的作废):

```json
{"code":0,"message":"成功","data":{"userId":1,"token":"2e4aa238-556f-4bba-901a-c0ace969c338"}}
```

| HTTP | code | 场景 |
|---|---|---|
| 200 | 0 | 成功 |
| 401 | 40102 | 缺头 / 无效 / 已过期。实测:`{"code":40102,"message":"令牌无效或未登录"}` |

### `POST /api/auth/password`　头 `X-Auth-Token`　body `{ oldPassword, newPassword }`

鉴权:需要。`newPassword` 形状同注册(6–64、无空白)。

成功响应(HTTP 200,更新 BCrypt 哈希,**当前 token 保留有效**):

```json
{"code":0,"message":"成功"}
```

| HTTP | code | 场景 |
|---|---|---|
| 200 | 0 | 成功 |
| 400 | 40001 | 新密码形状不满足 / 缺字段 / 请求体不是 JSON 对象 |
| 401 | 40101 | 旧密码不对 |
| 401 | 40102 | 缺头 / 无效 / 已过期 |

### `POST /api/auth/logout`　头 `X-Auth-Token`

鉴权:需要。**幂等**——令牌已无效也返回 code 0。

成功响应(HTTP 200):

```json
{"code":0,"message":"成功"}
```

| HTTP | code | 场景 |
|---|---|---|
| 200 | 0 | 成功(含幂等重入) |
| 401 | 40102 | 缺头 |

### `GET /api/me`　头 `X-Auth-Token`

鉴权:需要。

成功响应(HTTP 200):

```json
{"code":0,"message":"成功","data":{"userId":1,"username":"w3docuser"}}
```

| HTTP | code | 场景 |
|---|---|---|
| 200 | 0 | 成功 |
| 401 | 40102 | 缺头/无效/过期。实测:`{"code":40102,"message":"令牌无效或未登录"}` |

### `POST /api/save/upload`　头 `X-Auth-Token`　body `{ slot, schemaVersion, save }`

鉴权:需要。`save` 是客户端存档 JSON 对象原文,服务端**只存不读**;`slot` 1–3;
payload 上限 256KB(服务端序列化后的实际 UTF-8 字节数,先量再存)。

成功响应(HTTP 200,`updatedAt` 为带 UTC 偏移的 ISO8601;与随后 download/list 逐字一致):

```json
{"code":0,"message":"成功","data":{"slot":1,"persisted":true,"migratable":false,"updatedAt":"2026-10-05T13:05:47.851786Z"}}
```

| HTTP | code | 场景 |
|---|---|---|
| 200 | 0 | 成功落库 |
| 400 | 40001 | `schemaVersion` 缺失/不可读(含字符串形态)、`slot` 缺失/越界、`save` 不是 JSON 对象、payload 超 256KB、请求体不是 JSON 对象。实测:`{"code":40001,"message":"存档槽位需在 1–3 之间，收到 9。"}` |
| 401 | 40102 | 缺 token / token 无效 |
| 409 | 40901 | `schemaVersion` > 11。实测:`{"code":40901,"message":"存档版本 99 高于本服务器支持的 11。"}` |
| 409 | 40903 | 与另一次并发上传同槽位撞车,服务端自动重试一次后仍失败(W3-A)。客户端应提示"该槽位刚被另一次上传占用,请重试",**不许静默失败**。信封同上:`{"code":40903,"message":"该槽位刚被另一次上传占用，请重试。"}`(形状由集成测试断言钉死) |

### `GET /api/save/download?slot=`　头 `X-Auth-Token`

鉴权:需要。空槽位是**正常状态**(code 0,present=false),不是错误。

成功响应(HTTP 200,有存档):

```json
{"code":0,"message":"成功","data":{"slot":1,"present":true,"schemaVersion":11,"updatedAt":"2026-10-05T13:05:47.851786Z","save":{"coins":300,"bag":[{"id":"herb","count":2}],"memo":null}}}
```

空槽位(实测):

```json
{"code":0,"message":"成功","data":{"slot":3,"present":false}}
```

| HTTP | code | 场景 |
|---|---|---|
| 200 | 0 | 有存档 / 空槽位 |
| 400 | 40001 | 槽位越界(非 1–3) |
| 401 | 40102 | 缺 token / token 无效 |

### `GET /api/save/list`　头 `X-Auth-Token`

鉴权:需要。固定返回三条槽位记录(没存过的槽位 `present: false`、`updatedAt: null`),
是"云端比本地新"冲突提示的数据源。

成功响应(HTTP 200):

```json
{"code":0,"message":"成功","data":{"slots":[{"slot":1,"present":true,"updatedAt":"2026-10-05T13:05:47.851786Z"},{"slot":2,"present":false,"updatedAt":null},{"slot":3,"present":false,"updatedAt":null}]}}
```

| HTTP | code | 场景 |
|---|---|---|
| 200 | 0 | 成功(唯一路径) |
| 401 | 40102 | 缺 token / token 无效 |

---

### `50001` 预留状态

`50001` 当前为预留业务码，尚未接入全局异常兜底处理器。
未捕获的服务器内部异常走 Spring Boot 默认错误响应，不保证 `ApiResponse` 信封或业务 `code` 字段；
客户端应以 HTTP 失败状态兜底处理，不依赖收到 `50001`。当前 `GlobalExceptionAdvice`
只处理上传体积超限的异常，该路径返回 `400 / 40001`。

> 本文档由 W3-C 生成;错误码与文档/测试/生产者的三方一致性由
> `ApiContractCoverageTest` 在每次 `mvnw test` 时强制校验——改了码不更文档会红。
