# 十二玉楼长生经 · 服务端

《十二玉楼长生经》的 SpringBoot 服务端。当前处于 **M6-01 骨架阶段**：能起来、能探活、
存档接口的形状已经定死，但**不连数据库、不做云存档同步**。

| 项 | 内容 |
|---|---|
| 运行时 | JDK 21（LTS） |
| 框架 | Spring Boot 4.1.1 |
| 构建 | Maven 3.9.16（工程自带 Wrapper `./mvnw`） |
| 数据库 | MySQL 8.0（驱动已带，**默认不连接**） |
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

## 接口（形状在 M6-01 定死，之后不改）

所有接口统一返回 `{ code, message, data }`：`code == 0` 为成功，非 0 为失败；
`message` 给人看，客户端不依赖它的文案。

| 错误码 | 含义 |
|---|---|
| `0` | 成功 |
| `40001` | 请求不合法（缺字段、槽位越界） |
| `40901` | 存档版本高于本服务器支持的版本 |
| `50001` | 服务器内部错误 |

### `GET /api/health`

健康探针，**不依赖数据库**。返回 `{"code":0,"message":"成功","data":{"status":"up",...}}`。

### `POST /api/save/upload`

接收 Unity 端 `SaveData` 的 JSON，**只做版本校验，不落库**（响应里 `persisted: false` 就是这个意思）。

| 情况 | HTTP | code |
|---|---|---|
| `schemaVersion` ≤ 10 | 200 | 0，`migratable` 表示是否待迁移 |
| `schemaVersion` > 10 | 409 | 40901 |
| 缺 `schemaVersion` / 请求体不是对象 | 400 | 40001 |

> `ClientSchema.SUPPORTED_SCHEMA = 10` 必须与 Unity 端 `SaveData.CurrentSchemaVersion` 一致。
> **客户端升级存档结构时，这个常量要同步改**，否则老客户端会把新结构的存档塞进来，
> 服务端照单全收却读不懂。

### `GET /api/save/download?slot=`

下载存档。**未存档是正常状态**：返回 200 / code 0，`present: false`、`save: null`。
槽位范围 1–3（与客户端三档一致），越界返回 400 / 40001。

## 数据库

默认**不连库**——骨架阶段探针与契约都要能独立起来。需要连库时激活 mysql 档：

```bash
java -jar target/game-server-0.0.1.jar --spring.profiles.active=mysql
```

该档的连接信息全部走环境变量，仓库里不留账号密码：

| 环境变量 | 说明 |
|---|---|
| `TWELVEJADE_DB_URL` | 例 `jdbc:mysql://127.0.0.1:3306/twelvejade?useSSL=false&serverTimezone=UTC` |
| `TWELVEJADE_DB_USERNAME` | 数据库账号 |
| `TWELVEJADE_DB_PASSWORD` | 数据库密码（无默认值） |

## 测试

```bash
./mvnw -q test
```

## 接下来

- **M6-02**：登录注册、云存档真正的落库与冲突处理、客户端联网（保留离线模式）
- **M6-03**：AI NPC 探针适配器

本骨架**不做**登录、云存档同步、AI 网关，也不引入 Redis / 消息队列——
加了就是还没用上的债。
