# W-Server-Time 交付报告

交付物:`W-Server-Time.patch`(仓库根,6 个文件,只含 `GameServer/` 内改动)+ 本报告。
未 commit、未 push、未改 `GameServer/` 之外的任何文件。

## 选了什么方案(Instant 还是 LocalDateTime+UTC),为什么

**首选方案:`java.time.Instant`。** 理由:

- `Instant` 类型本身自描述"UTC 时间点"语义,不依赖注释维系"本列其实是 UTC"的口头约定;
- Hibernate ORM(随 Spring Boot 4.1.1 自带)对 `Instant` 默认按 `TIMESTAMP_UTC` 语义映射。
  在默认 H2 profile 上实测:自动建表、存取往返无损(固定 Clock 下回执与 download/list
  逐字一致),全部测试绿——**没有撞到任何 DDL / 实体校验冲突**,无需退回;
- 序列化统一走 `DateTimeFormatter.ISO_OFFSET_DATE_TIME` + `instant.atOffset(ZoneOffset.UTC)`,
  输出 `Z` 结尾(如 `2026-10-05T09:32:19.123456Z`)。

未走退回方案(`LocalDateTime` 保持 + 语义固定 UTC),故"退回撞到的报错"一节不适用。

## 改了哪几处(文件:行 + 一句话)

行号为改动后的行号。

| 位置 | 一句话 |
|---|---|
| `SaveController.java:54` | 格式器从 `ISO_LOCAL_DATE_TIME` 改为 `ISO_OFFSET_DATE_TIME`(无偏移的根源) |
| `SaveController.java:73` | 构造注入 `TimeConfig` 提供的 `Clock` |
| `SaveController.java:127` | 裸 `LocalDateTime.now()` 改为 `Instant.now(clock).truncatedTo(ChronoUnit.MICROS)`(取值走注入 Clock;截到微秒与数据库 TIMESTAMP(6) 对齐,保证回执与后续 download/list 逐字一致) |
| `SaveController.java:135` / `:160` / `:185` | upload 回执 / download / list 三个输出点统一改走 `formatUpdatedAt(...)` |
| `SaveController.java:195-197` | 新增私有 `formatUpdatedAt(Instant)`:统一 `atOffset(UTC)` + `ISO_OFFSET_DATE_TIME` |
| `SaveEntity.java:59` | `updatedAt` 字段 `LocalDateTime` → `Instant`(getter/setter 同步,注释写明 UTC 自描述) |
| `SaveContractTest.java:70` | `isNotEmpty()` 收紧为"末尾必须 Z 或 ±HH:MM"的正则断言 |
| `SavePersistenceTest.java:164` | list 的 `updatedAt` 同样收紧为正则断言 |
| `SavePersistenceTest.java:213-221` | `containsKey` 收紧为 `OffsetDateTime.parse` + 回执与 download 逐字同值 |
| `SaveTimestampTest.java`(新增) | 3 个用例:固定 Clock 注入,三输出点逐字断言 |
| `README.md` | list 示例上方增补 `updatedAt` 语义变更与旧行偏移风险说明;upload 契约表、list 示例、用例数同步更新 |

## 新增/收紧了哪几条断言(逐条列出断言原文)

1. `SaveContractTest.java:69-71`(`acceptsCurrentSchema`,原 `isNotEmpty()`):

```java
.andExpect(jsonPath("$.data.updatedAt")
        // 必须是带偏移的 ISO8601：末尾 Z 或 ±HH:MM（裸墙上时间是回归）
        .value(matchesRegex(".+?(Z|[+-]\\d{2}:\\d{2})$")));
```

2. `SavePersistenceTest.java:163-164`(`listReturnsFixedThreeSlots`,原 `isNotEmpty()`):

```java
.andExpect(jsonPath("$.data.slots[1].updatedAt")
        // 必须是带偏移的 ISO8601：末尾 Z 或 ±HH:MM（裸墙上时间是回归）
        .value(matchesRegex(".+?(Z|[+-]\\d{2}:\\d{2})$")))
```

3. `SavePersistenceTest.java:213-221`(`migratableSurvivesRoundTrip`,原 `containsKey("updatedAt")`):

```java
// updatedAt 必须能按"带偏移的时间"解析（裸墙上时间在此直接抛异常），且
// 同一秒内 upload 回执与 download 给出同一个值。
var receiptUpdatedAt = (String) receipt.get("updatedAt");
var downloadUpdatedAt = (String) data.get("updatedAt");
assertDoesNotThrow(() -> OffsetDateTime.parse(receiptUpdatedAt),
        "updatedAt 应为带偏移的 ISO8601，实际: " + receiptUpdatedAt);
assertDoesNotThrow(() -> OffsetDateTime.parse(downloadUpdatedAt),
        "updatedAt 应为带偏移的 ISO8601，实际: " + downloadUpdatedAt);
assertEquals(receiptUpdatedAt, downloadUpdatedAt);
```

4. 新增 `SaveTimestampTest.java`(`@TestConfiguration` 内 `@Bean @Primary Clock.fixed(...)`,
   固定时刻 `2026-10-05T09:32:19.123456Z`,UTC,不靠 sleep、不靠真实时间):

- `uploadReceiptTimestampIsVerbatim`:`.andExpect(jsonPath("$.data.updatedAt").value(EXPECTED));`
- `downloadTimestampMatchesReceipt`:download 后同上,`EXPECTED = "2026-10-05T09:32:19.123456Z"`
- `listTimestampMatchesReceipt`:`$.data.slots[0].updatedAt` 逐字等于 `EXPECTED`,空槽位 `doesNotExist()`

这三条同时覆盖任务书要求的两点:断言输出字符串逐字等于期望值;同一秒内 upload 与
随后 list/download 给出同一个值(固定时钟下逐字相等)。

## mvnw test 输出关键行(原文粘贴)

改动前基线(先跑基线确认不是带病开工):

```
[INFO] Tests run: 46, Failures: 0, Errors: 0, Skipped: 0
[INFO] BUILD SUCCESS
```

改动后全量:

```
[INFO] Tests run: 11, Failures: 0, Errors: 0, Skipped: 0 -- in com.twelvejade.server.AuthFlowTest
[INFO] Tests run: 14, Failures: 0, Errors: 0, Skipped: 0 -- in com.twelvejade.server.AuthLifecycleTest
[INFO] Tests run: 2, Failures: 0, Errors: 0, Skipped: 0 -- in com.twelvejade.server.HealthControllerTest
[INFO] Tests run: 2, Failures: 0, Errors: 0, Skipped: 0 -- in com.twelvejade.server.LoginAttemptRegistryTest
[INFO] Tests run: 7, Failures: 0, Errors: 0, Skipped: 0 -- in com.twelvejade.server.SaveContractTest
[INFO] Tests run: 10, Failures: 0, Errors: 0, Skipped: 0 -- in com.twelvejade.server.SavePersistenceTest
[INFO] Tests run: 3, Failures: 0, Errors: 0, Skipped: 0 -- in com.twelvejade.server.SaveTimestampTest
[INFO] Tests run: 49, Failures: 0, Errors: 0, Skipped: 0
[INFO] BUILD SUCCESS
```

## 一条 curl 实测

`TWELVEJADE_PORT=18091 java -jar target/game-server-0.0.1.jar` 起服务(本机时区 +08:00),
注册用户 `wservertime` 后实测 upload,回执 JSON 原文:

```json
{"code":0,"message":"成功","data":{"slot":1,"persisted":true,"migratable":false,"updatedAt":"2026-10-05T10:07:05.923695Z"}}
```

同一次会话顺带的 download / list(三处逐字一致,均为 UTC + `Z` 偏移):

```json
{"code":0,"message":"成功","data":{"slot":1,"present":true,"schemaVersion":10,"updatedAt":"2026-10-05T10:07:05.923695Z","save":{"coins":300}}}
{"code":0,"message":"成功","data":{"slots":[{"slot":1,"present":true,"updatedAt":"2026-10-05T10:07:05.923695Z"},{"slot":2,"present":false,"updatedAt":null},{"slot":3,"present":false,"updatedAt":null}]}}
```

服务器跑在 +08:00 本机而输出仍是 UTC——输出不再依赖机房时区。

## 你不确定 / 没做的事

- **MySQL 档未实测**:生产 mysql 档(`ddl-auto: validate` + `V1__user_cloud_save.sql`)需要真库
  与环境变量,本环境没有 MySQL 实例,任务书预算也禁止全仓探索 DDL 脚本。Hibernate 对
  `Instant` 的默认 `TIMESTAMP_UTC` 语义(JDBC `TIMESTAMP` 类型)与 `DATETIME`/`TIMESTAMP` 列
  的 validate 预期兼容,但这一点**没有实测证据**。若真库启动 validate 报列类型不匹配,
  即任务书"必做 3"约定的退回方案触发条件——如实交代,未改 DDL、未写迁移。
- **新增了微秒截断**:`Instant.now(clock).truncatedTo(ChronoUnit.MICROS)`——纳秒精度被截到
  微秒,目的是与数据库 `TIMESTAMP(6)` 精度对齐,保证同一笔上传的回执与后续 download/list
  字符串逐字一致。对"谁更新"比对无影响,如实记录。
- **list 空槽位形态**:JSON 里是 `"updatedAt":null`(key 存在、值为 null),新增测试对它用
  `doesNotExist()`(MockMvc 对 null 值路径视为不存在),与既有 list 测试口径一致。
- **git index 痕迹**:生成补丁时对新测试文件用过 `git add -N`(intent-to-add),补丁生成后
  已 `git reset` 还原;未 commit、未 push、未改 git 配置。
- 其余按禁令未动:`ClientSchema.SUPPORTED_SCHEMA` 保持 `10`,payload 结构/256KB 上限/
  槽位唯一性/令牌与登录锁定逻辑一行未碰。
