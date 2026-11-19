# 十二玉楼长生经 · 服务端

《十二玉楼长生经》的 SpringBoot 服务端。当前处于 **M6-03-S 阶段**：能起来、能探活，
注册登录与云存档真正落库（默认 H2 内存库，生产可切 MySQL），
并具备令牌生命周期（7 天过期 + 刷新）、改密码、登录失败锁定、payload 上限。
客户端联网与"云端比本地新"冲突比对**不在本轮**（见文末边界）。

| 项 | 内容 |
|---|---|
| 运行时 | JDK 21（LTS） |
| 框架 | Spring Boot 4.1.1 |
| 构建 | Maven 3.9.16（工程自带 Wrapper `./mvnw`） |
| 数据库 | 默认 H2 内存库（零配置）；生产用 MySQL 8.0（驱动已带） |
| 默认端口 | 18080（`TWELVEJADE_PORT` 可覆盖） |

## 快速开始

```bash
# Windows
./mvnw.cmd spring-boot:run

# Linux / macOS
./mvnw spring-boot:run
```

起服务后探活：

```bash
curl http://127.0.0.1:18080/api/health
```

打包后运行：

```bash
./mvnw -q package -DskipTests
java -jar target/game-server-0.0.1.jar
```

## 统一返回体与错误码

所有接口统一返回 `{ code, message, data }`：`code == 0` 为成功，非 0 为失败；
`message` 给人看，客户端不依赖它的文案。`data` 为 null 时整个字段不输出。

| 错误码 | 含义 |
|---|---|
| `0` | 成功 |
| `40001` | 请求不合法（缺字段、槽位越界、参数超长/含空白） |
| `40101` | 账号或密码不对（**不区分**"无此用户"与"密码错"，防撞库探测） |
| `40102` | 令牌无效或未登录 |
| `40901` | 存档版本高于本服务器支持的版本 |
| `40902` | 用户名已被占用 |
| `40903` | 槽位冲突（与另一次并发上传同槽位撞车，服务端重试一次后仍失败；客户端应提示重试） |
| `40904` | 登录失败次数过多（锁定中，M6-03-S） |
| `50001` | 服务器内部错误 |

**令牌生命周期（M6-03-S）**：TTL = 7 天，自签发（`token_at`）起算。过期与无效不区分，
一律 401 / 40102——`me` / `save/*` / `refresh` 共用同一判定（`UserService.findByToken`，
全服务唯一过期口径）。客户端对长期登录态应在过期前调 `refresh` 续期。

**登录失败锁定（M6-03-S）**：同一用户名连续失败 5 次 → 锁 10 分钟，锁定期内
正确密码也返回 **429 / 40904**（RFC 7231 Too Many Requests，M6-02-C 修订，旧版曾用 409）；成功登录清零计数；对不存在的用户名同样计数
（锁定事实不泄露用户名存在性）。**内存实现**：H2/进程重启即清零。

## 接口契约（M6-02）

### `GET /api/health`

健康探针，返回 `{"code":0,...,"data":{"status":"up",...}}`。

### `POST /api/auth/register`　body `{ username, password }`

| 规则 | 值 |
|---|---|
| username | 3–32 字符，不含空白 |
| password | 6–64 字符，不含空白 |

| 情况 | HTTP | code | data |
|---|---|---|---|
| 成功（注册即登录） | 200 | 0 | `{ userId, token }` |
| 用户名已被占用 | 409 | 40902 | — |
| 参数不合法（空/超长/含空白/不是 JSON 对象） | 400 | 40001 | — |

### `POST /api/auth/login`　body `{ username, password }`

| 情况 | HTTP | code | data |
|---|---|---|---|
| 成功 | 200 | 0 | `{ userId, token }`（**每次登录换发新 token，旧的作废**） |
| 账号或密码不对 | 401 | 40101 | — |

### `POST /api/auth/refresh`　头 `X-Auth-Token`（M6-03-S）

| 情况 | HTTP | code | data |
|---|---|---|---|
| 有效且未过期 | 200 | 0 | `{ userId, token }`（换新，旧的作废） |
| 过期 / 无效 / 缺头 | 401 | 40102 | — |

### `POST /api/auth/password`　头 `X-Auth-Token`　body `{ oldPassword, newPassword }`（M6-03-S）

| 情况 | HTTP | code | data |
|---|---|---|---|
| 旧密码正确 | 200 | 0 | 更新 BCrypt 哈希，**当前 token 保留有效**（强制下线由客户端主动 logout；M6-02-C 修订，旧版曾定为作废） |
| 旧密码错 | 401 | 40101 | — |
| 新密码形状同注册（6–64、无空白）不满足 / 缺字段 | 400 | 40001 | — |
| token 无效 / 过期 | 401 | 40102 | — |

### `POST /api/auth/logout`　头 `X-Auth-Token`

登出：令牌置 null。**幂等**——令牌已无效也返回 code 0；缺头 → 401 / 40102。

### `GET /api/me`　头 `X-Auth-Token`

| 情况 | HTTP | code | data |
|---|---|---|---|
| 有效 | 200 | 0 | `{ userId, username }` |
| 缺头/无效 | 401 | 40102 | — |

### `POST /api/save/upload`　头 `X-Auth-Token`　body `{ slot, schemaVersion, save }`

`save` 是客户端存档 JSON 对象原文，服务端**只存不读**；`slot` 1–3；
`schemaVersion` 与 Unity 端 `SaveData.CurrentSchemaVersion` 比较。
落库为 `(user_id, slot)` 上的 INSERT-or-UPDATE——覆盖语义，一槽永远只有最新一份。
**payload 上限 256KB**（M6-03-S，UTF-8 字节数，先量再存），超出 400 / 40001、不落库；
请求体上限已在 yml 同步配置（`server.tomcat.max-http-form-post-size`，JSON 体实际由应用层强制）。

| 情况 | HTTP | code | data |
|---|---|---|---|
| 成功落库 | 200 | 0 | `{ slot, persisted: true, migratable, updatedAt }`（updatedAt 为带 UTC 偏移的 ISO8601，如 `2026-10-05T09:32:19.123Z`） |
| 缺 token / token 无效 | 401 | 40102 | — |
| `schemaVersion` 缺失或不可读 | 400 | 40001 | — |
| `slot` 缺失或越界（非 1–3） | 400 | 40001 | — |
| `save` 不是 JSON 对象 | 400 | 40001 | — |
| `schemaVersion` > 10 | 409 | 40901 | — |
| 与另一次并发上传同槽位撞车（服务端自动重试一次后仍失败） | 409 | 40903 | — |

版本门禁与 M6-01 一行不改：**老版本（<10）照收**，回执里 `migratable: true` 标记待迁移。

**409/40903 的客户端处置（W3-A 起）**：服务端对并发撞车已自动重试一次（改为覆盖更新），
重试仍失败才返回此码。客户端收到它应**提示用户"该槽位刚被另一次上传占用，请重试"**——
**不许静默失败**（玩家以为存上了实际没存，比报错更糟）。

### `GET /api/save/download?slot=`　头 `X-Auth-Token`

| 情况 | HTTP | code | data |
|---|---|---|---|
| 有存档 | 200 | 0 | `{ slot, present: true, save: {…原文…}, schemaVersion, updatedAt }` |
| 空槽位（**正常状态，不是错误**） | 200 | 0 | `{ slot, present: false }` |
| 槽位越界 | 400 | 40001 | — |
| 缺 token / token 无效 | 401 | 40102 | — |

### `GET /api/save/list`　头 `X-Auth-Token`

固定返回三条槽位记录（没存过的槽位 `present: false`、`updatedAt: null`）。
这是将来"云端比本地新"冲突提示的服务端数据源；**比对逻辑本轮不做**。

```json
{"code":0,"message":"成功","data":{"slots":[
  {"slot":1,"present":true,"updatedAt":"2026-10-05T09:32:19.123456Z"},
  {"slot":2,"present":false,"updatedAt":null},
  {"slot":3,"present":false,"updatedAt":null}]}}
```

> **`updatedAt` 语义（W-Server-Time，2026-10-05 起）**：由"服务器本地墙上时间（无偏移）"
> 改为**带 UTC 偏移的 ISO8601**（`Z` 结尾），存储为 `Instant`（UTC 语义自描述），
> 客户端据此才能跨时区判断"云端与本地谁更新"。
> **变更前写入的旧行**存的是服务器本地墙上时间，在新语义下按 UTC 解读会**偏移一个时区差**；
> 本项目处于开发期、单用户，该成本已被接受，不做数据迁移。

> `ClientSchema.SUPPORTED_SCHEMA = 10` 必须与 Unity 端 `SaveData.CurrentSchemaVersion` 一致。
> **客户端升级存档结构时，这个常量要同步改**，否则老客户端会把新结构的存档塞进来，
> 服务端照单全收却读不懂。

## curl 全链路示例

```bash
TOKEN=$(curl -s -X POST http://127.0.0.1:18080/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"tester","password":"pass1234"}' | sed 's/.*"token":"\([^"]*\)".*/\1/')

curl -s http://127.0.0.1:18080/api/me -H "X-Auth-Token: $TOKEN"
curl -s -X POST http://127.0.0.1:18080/api/save/upload \
  -H "X-Auth-Token: $TOKEN" -H "Content-Type: application/json" \
  -d '{"slot":1,"schemaVersion":10,"save":{"coins":300}}'
curl -s "http://127.0.0.1:18080/api/save/download?slot=1" -H "X-Auth-Token: $TOKEN"
curl -s "http://127.0.0.1:18080/api/save/list" -H "X-Auth-Token: $TOKEN"
```

一键冒烟（自动起 jar、跑九连、逐条打印 code、全过退出码 0）：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/acceptance.ps1
```

## 数据库

**默认 profile：H2 内存库**（M6-02 受控修订，取代 M6-01"默认不连库"）——
`jdbc:h2:mem:twelvejade`，用户 `sa` 空密码，零配置，**重启即清空**（用户、令牌、存档都不保留）。
调试可用 h2-console：<http://127.0.0.1:18080/h2-console>（JDBC URL `jdbc:h2:mem:twelvejade`）。

生产/真库用 mysql 档：

```bash
java -jar target/game-server-0.0.1.jar --spring.profiles.active=mysql
```

该档连接信息全部走环境变量，仓库里不留账号密码；**表结构不由 Hibernate 自动创建**
（ddl-auto: **validate**，每次启动只读校验实体与表结构一致，V1 未执行会启动失败=保护机制），建表走版本化脚本 `Database/SQL/V1__user_cloud_save.sql`
（与 JPA 实体注解一一对应，说明见 Database/ER/ 与 Database/DataDictionary/）：

| 环境变量 | 说明 |
|---|---|
| `TWELVEJADE_DB_URL` | 例 `jdbc:mysql://127.0.0.1:3306/twelvejade?useSSL=false&serverTimezone=UTC` |
| `TWELVEJADE_DB_USERNAME` | 数据库账号 |
| `TWELVEJADE_DB_PASSWORD` | 数据库密码（无默认值） |

## 测试

```bash
./mvnw -q test
```

49 个用例：健康探针 2、存档契约 7、存档时间戳 3（固定 Clock 逐字断言 updatedAt 带偏移）、认证流程 11、落库行为 10、令牌生命周期 14、锁定单元 2。

一键冒烟（自动起 jar、跑 13 步、逐条打印 code、全过退出码 0）：
`powershell -ExecutionPolicy Bypass -File scripts/acceptance.ps1`
（13 步 = 原 9 步 + refresh + payload 超限 + 改密码 + 登录失败锁定；锁定步用独立用户名放最后，
锁定是进程内存态，不能污染前面步骤。）

## 边界（本轮明确不做）

- **客户端联网未做**：Unity 端调用这些端点属于后续里程碑；本地单机离线存档不受影响。
- **冲突比对未做**：`/api/save/list` 已备好"云端各槽位 present/updatedAt"数据，
  "云端比本地新"的提示逻辑留给下一轮。
- 账号找回未做（改密码已有）；令牌过期已做（7 天 TTL + refresh），
  多设备并发策略未做（同账号后登录挤掉先登录）。
- 登录锁定是**内存实现**：进程/H2 重启即清零（有意为之，上 Redis 属于还没用上的债）。
- 不引入 Redis / 消息队列 / JWT / Spring Security 全家桶——加了就是还没用上的债。
- MySQL 档接入步骤与回滚见 `Database/SQL/MySQL接入手册.md`（T024）。

## 接下来

- **M6-02-C**：客户端联网（保留离线模式）、云端/本地冲突提示
- **M6-03（服务端已就绪部分）**：AI NPC 探针适配器
