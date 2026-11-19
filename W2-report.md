# W2 交付报告

**基线声明：所有 diff/改动的基线 = `edf843f`（W-Server-Time 合入）之后的工作树。**
三块全部完成：W2-A（T102 启动自检）、W2-B（T035 服务端侧钉测）、W2-C（T101 夹具静态更新）。
未 commit、未 push、未改 git 配置。

## W2-A：改了/新增了哪几处（文件:行 + 一句话）

| 位置 | 一句话 |
|---|---|
| `GameServer/src/main/resources/application-mysql.yml:29` | **新增 `spring.datasource.hikari.data-source-properties.connectionTimeZone: UTC`**——仓库层面默认注入该驱动属性，运维连接串漏带参数时兜底（若连接串也显式写了，以连接串为准，由自检抓住） |
| `application-mysql.yml:5-17` | 头注释重写：示例串换 `connectionTimeZone=UTC`，写明"硬性要求 + 三层兜底"（属性默认注入 → 启动自检 fail-fast → validate 只比类型看不见语义） |
| `Database/SQL/V1__user_cloud_save.sql:41-49` | 失真注释按 Instant 真实语义重写（**只动注释，未碰列类型、未新增脚本**）：明确"本表 DATETIME(6) 列存的是 UTC 墙上时间，前提是连接串带 connectionTimeZone=UTC"，Instant 与 DATETIME 的换算全部发生在驱动侧 |
| `Database/SQL/MySQL接入手册.md:47,58` | 两处示例串 `serverTimezone=UTC` → `connectionTimeZone=UTC` |
| `MySQL接入手册.md:51-53` | 加说明：`connectionTimeZone` 是 `serverTimezone` 的现名（Connector/J 8.0.23 起旧名废弃），并点明缺了它启动自检会拒绝启动 |
| `GameServer/src/main/java/com/twelvejade/server/common/MysqlUtcSemanticsCheck.java`（新增） | **启动自检核心**：mysql profile 激活时（`@Profile("mysql")`，bean 初始化期执行）在临时表上"写已知 Instant → 按字面量读回墙钟值 → 与 UTC 墙上时间逐字比对"，不符抛 `IllegalStateException` 阻止启动，错误信息指名 `TWELVEJADE_DB_URL` 加 `connectionTimeZone=UTC`；临时表自检结束即 DROP，零残留 |
| `GameServer/src/main/java/com/twelvejade/server/common/MysqlUtcSemanticsCheckTest.java`（新增，5 用例） | 覆盖"假读回值模拟不一致 → 抛异常"、核心判定单元断言、H2/默认 profile 下 bean 不加载、mysql profile 下加载且真的启动失败 |

自检的设计要点（写库读库两侧不对称才有判别力）：

- **写入侧与 Hibernate 同链路**：`ps.setTimestamp(1, Timestamp.from(written))`——与 Hibernate 把 `Instant` unwrap 成 `Timestamp` 后交给驱动的路径一致，驱动按 connectionTimeZone 把它解释成墙钟字面量落库；
- **读取侧直通字面量**：`rs.getObject(1, LocalDateTime.class)` 拿到的是数据库里**真实存下的墙上时间**，不再经过时区换算；
- **期望值**：`LocalDateTime.ofInstant(written, ZoneOffset.UTC)`。相等 ⇔ 驱动按 UTC 解释；不等 ⇔ UTC 语义已被连接时区破坏 → fail-fast。

## W2-A：启动自检在 H2 下确实跳过、在模拟不一致时确实 fail-fast（贴测试输出）

逐类 surefire 输出（全量 `./mvnw.cmd test`，2026-10-05 20:09）：

```
Test set: com.twelvejade.server.common.MysqlUtcSemanticsCheckTest
Tests run: 5, Failures: 0, Errors: 0, Skipped: 0, Time elapsed: 1.882 s -- in com.twelvejade.server.common.MysqlUtcSemanticsCheckTest
```

5 个用例分别证明：

1. `skippedWithoutMysqlProfile`——默认（H2）profile 下 `ApplicationContextRunner` 中
   `doesNotHaveBean(MysqlUtcSemanticsCheck.class)`：**自检组件根本不加载 = 自动跳过**。
   反向证据：全部 49 条既有测试（默认 profile）在本组件加入后依旧全绿，若跳过失效它们会全崩。
2. `failFastOnNonUtcWhenMysqlProfileActive`——mysql profile + 假 DataSource（读回固定
   `2000-01-01T00:00:00.000000`，结构上不可能等于任何真实写入时刻的 UTC 墙钟）：
   **context `hasFailed()`，启动失败根因链为 `IllegalStateException`，消息含 `connectionTimeZone=UTC`**。
3. `passesOnUtcWallTimeWithFixedInstant`——固定时刻写入、读回逐字等于 UTC 墙上时间 → 自检不抛。
4. `rejectsNonUtcWallTime`——+08:00 产物（17:32:19）读回 → 抛异常，消息含 `connectionTimeZone=UTC` 与 `TWELVEJADE_DB_URL`。
5. `acceptsUtcWallTime`——UTC 墙上时间读回 → 通过。

## W2-B：四个问题的实测结论逐条（现状是什么 → 我修了还是只钉了测试）

新增 `GameServer/src/test/java/com/twelvejade/server/SaveUploadRobustnessTest.java`（5 用例）。

**问题 1：并发上传同 (user, slot)，最终库里是哪一份？**
现状实测（6 轮 × 2 并发，共 12 个请求）：状态码分布 `[500, 200, 200, 500, 200, 500, 500, 200, 500, 200, 200, 500]`——**每一轮都恰好一胜一败，竞态必现**。两个并发请求都走 `findBy...orElseGet` 的"空槽"分支、各自 INSERT，败者撞 `uk_saves_user_slot` 抛 `DataIntegrityViolationException`。不变量恒成立（测试钉死）：库里恒为单行、内容恒为两份完整 payload 之一、没有半条记录。中间态不会被丢（覆盖语义下"最后提交者胜"），但**败者会收到 500**。
→ 处置：**只钉了不变量测试 + 如实暴露**；败者 500 → 友好码的改法见下节（动契约，不自改）。

**问题 2：畸形 payload 的四条路径？**
- 请求体不是合法 JSON / 是 JSON 但不是对象 → `400 / 40001`（`onUnreadableBody`，既有测试已钉，本轮复核无变化）；
- 缺 `schemaVersion` → `400 / 40001`（既有）；
- `schemaVersion` 是字符串 `"10"` → `400 / 40001`（本轮新钉 `stringSchemaVersionIsRejected`）；
- `save` 是字符串/数组 → `400 / 40001`（本轮新钉 `saveNonObjectIsRejected`）。
**没有任何一条路径能把畸形 payload 静默存进去**：`save` 必须是 JSON 对象，服务端用专用 mapper 重新序列化（产物必为合法 JSON，且保留 null 字段），每个拒绝路径后实测 `download present=false`（测试断言）。
→ 处置：现状正确，**用测试钉住**。

**问题 3：错误码与 HTTP 状态是否一致？**
常规路径全部成对出现：`200↔0`、`400↔40001`、`401↔40102`、`409↔40901`（`errorCodeAndHttpStatusArePaired` 集中钉了畸形输入的配对，不允许"200 但 code 非 0"或"4xx 但 body 没有 code"）。**唯一例外 = 问题 1 的竞态败者**：`500` + Spring Boot 默认 error JSON，**body 里没有业务 code**。
→ 处置：例外如实暴露，**未修**（见下节）。

**问题 4：客户端自带未来时间戳会怎样？**
什么都不影响。`updatedAt` 由服务端注入的 `Clock` 生成，客户端在 `save` 里塞的 `clientTs` 只是被当作数据原样存取；测试 `clientSuppliedFutureTimestampDoesNotAffectUpdatedAt` 钉死：服务端生成的 `updatedAt` 早于客户端伪造的"现在+10 年"、与回执逐字一致、`clientTs` 原样往返。
→ 处置：现状正确，**用测试钉住**。

## W2-B：需要架构师排期的跨端改动

1. **并发竞态败者的响应形态**（现状 500 + 无业务 code）：建议加
   `@ExceptionHandler(DataIntegrityViolationException.class)` → `409 / 40903`（40903"槽位冲突"
   目前是预留码，语义正好）。影响面：响应契约新增一个分支，客户端需能处理"上传撞车"的
   40903 提示（可重试）；也可以选择返回 200 + 幂等语义（把败者视为"被覆盖"），需要架构师定夺。
2. （更彻底，改动面大，仅供排期参考）给上传加乐观锁/version 字段彻底消除"后传覆盖先传"的
   丢失窗口——涉及 payload 契约与客户端配合，远超本轮边界。

## W2-C：是否做了；若做了，是否真跑过

**做了；未跑，仅静态修改。**（Unity 批处理未执行，架构师可自行复跑 `[NetworkShot] finished, N problem(s)`。）

改动文件：`GameClient/TwelveJade/Assets/Editor/NetworkShot.cs`（**仅此一个客户端文件**），共六处。
其中④号禁令遵守：S14"裸时间（无偏移）判 Unknown"的容错断言**原样保留**。

夹具常量用的是 **2026-10-05 对本地真后端实测 curl 回来的原文**（`updatedAt":"2026-10-05T12:09:33.69094Z"`，upload 与 list 同值；真实输出小数尾零被 ISO 格式截掉、只剩 5 位——手拼会拼错，这正是工单要求用原文的原因）：

1. `:256` 注释与断言（S8）：`ISO_LOCAL_DATE_TIME 无偏移` → `带 UTC 偏移的 ISO8601（Instant，Z 结尾）`；TryParse 断言的样例串同步换为 `2026-10-05T12:09:33.69094Z`。
2. `:341-346` 场景 11–14 头注释：抓包日期与后端形态说明更新（W-Server-Time 已合入）。
3. `RealListBody`：`updatedAt` → `"2026-10-05T12:09:33.69094Z"`。
4. `RealUploadReceipt`：同上。
5. S12 硬编码断言 `Check(updatedAt == ...)`（原 `:414`）：同步换新值，否则 S12 必红——工单虽未点名，但不改会与常量自相矛盾。
6. S13 `downloadBody` 局部变量（原 `:446`）：同上换新值。
7. S14：注释按新事实重写（服务端已带偏移，但裸时间防御保留，挡历史存量/第三方回放/未来回归）；**新增正向断言**：
   ```csharp
   Check(CloudSaveService.Compare(nowUtc, DateTime.UtcNow.AddHours(+5).ToString("O"))
           == ConflictSide.CloudNewer,
       "S14 云端带 Z 偏移且比本地新 → 必须判 CloudNewer");
   ```
   （`DateTime.UtcNow` 的 Kind 为 Utc，`ToString("O")` 输出 `Z` 结尾——即服务端新格式。）

## mvnw test 输出关键行（含测试总数与 BUILD SUCCESS/FAILURE）

最终全量（W2-A + W2-B 全部落盘后，2026-10-05 20:09）：

```
[INFO] Tests run: 11, Failures: 0, Errors: 0, Skipped: 0 -- in com.twelvejade.server.AuthFlowTest
[INFO] Tests run: 14, Failures: 0, Errors: 0, Skipped: 0 -- in com.twelvejade.server.AuthLifecycleTest
[INFO] Tests run: 2, Failures: 0, Errors: 0, Skipped: 0 -- in com.twelvejade.server.HealthControllerTest
[INFO] Tests run: 2, Failures: 0, Errors: 0, Skipped: 0 -- in com.twelvejade.server.LoginAttemptRegistryTest
[INFO] Tests run: 7, Failures: 0, Errors: 0, Skipped: 0 -- in com.twelvejade.server.SaveContractTest
[INFO] Tests run: 10, Failures: 0, Errors: 0, Skipped: 0 -- in com.twelvejade.server.SavePersistenceTest
[INFO] Tests run: 3, Failures: 0, Errors: 0, Skipped: 0 -- in com.twelvejade.server.SaveTimestampTest
[INFO] Tests run: 5, Failures: 0, Errors: 0, Skipped: 0 -- in com.twelvejade.server.SaveUploadRobustnessTest
[INFO] Tests run: 5, Failures: 0, Errors: 0, Skipped: 0 -- in com.twelvejade.server.common.MysqlUtcSemanticsCheckTest
[INFO] Tests run: 59, Failures: 0, Errors: 0, Skipped: 0
[INFO] BUILD SUCCESS
```

基线 49（W1 验收数）+ W2-A 自检 5 + W2-B 钉测 5 = 59，全绿。中途两次红灯如实记录：
编译错（测试类跨包访问包级私有方法，已把测试类移入 `common` 包）；自检测试首版设计 bug
（stub 读回固定值 vs `verify` 内部 `Instant.now()` 永不匹配——恰好反向证明自检真的在执行，
已改为带 `Instant` 参数的 `verify` 重载）；并发测试首版被 MockMvc 的"未处理异常直接抛出"
语义击穿（真容器等价 500），已按约束冲突根因识别竞态败者。

## 你不确定 / 没做的事

- **自检在真 MySQL 上未实测**：本机无 MySQL 实例。写入/读回两侧的驱动行为依据是
  Connector/J 8 的公开语义（`connectionTimeZone` 决定 Timestamp↔DATETIME 字面量换算；
  `getObject(LocalDateTime.class)` 直通字面量），加上了三层兜底（hikari 属性默认注入 →
  自检 fail-fast → 手册说明）与可测试的核心判定；第一次真库启动即见分晓，若语义有出入，
  自检的失败信息会直接给出两侧墙钟值，可当场定位。
- **Hikari `data-source-properties` 与 URL 同名参数的优先级**：按 Connector/J 文档连接串
  显式参数优先；因此连接串写错值时由启动自检兜住，不依赖属性层拦截。
- **W2-C 未跑 Unity 批处理**：静态修改，断言的最终合法性待架构师复跑
  （预期全部通过：新断言只用了 `Compare`/`HasExplicitOffset` 的既有公开行为，正向断言与
  S14 既有"两侧带时区判 LocalNewer"同构）。
- **W2-B 的并发测试是观察式钉测**：断言只钉不变量（单行/二选一/根因唯一），不钉时序结果
  （200/500 分布随环境浮动）；6 轮在本机全部触发竞态，但慢机器上可能全部串行化（全 200），
  这同样是"现状"的一部分，不影响断言成立。
- 未动禁令清单：`ClientSchema.SUPPORTED_SCHEMA` 保持 10；payload 结构/256KB/槽位/令牌逻辑
  未碰；V1 脚本只改注释、未动列类型、未新增迁移；除 `Editor/NetworkShot.cs` 外未碰任何
  `Assets/Scripts/**`。
- 本工单按交付方式给"整份文件 + 说明"，未生成 git diff 补丁文件；既有文件的修改 diff 已在
  下方附录（基线 = `edf843f` 之后的工作树）。

---

## 附录 1：新增文件全文

### `GameServer/src/main/java/com/twelvejade/server/common/MysqlUtcSemanticsCheck.java`

```java
package com.twelvejade.server.common;

import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.sql.Statement;
import java.sql.Timestamp;
import java.time.Instant;
import java.time.LocalDateTime;
import java.time.ZoneOffset;
import java.time.temporal.ChronoUnit;
import javax.sql.DataSource;
import org.springframework.beans.factory.InitializingBean;
import org.springframework.context.annotation.Profile;
import org.springframework.stereotype.Component;

/**
 * MySQL 档 UTC 语义启动自检（T102 / W2-A）。
 *
 * <p>{@code DATETIME(6)} 列不做时区转换，{@code Instant}（{@code SaveEntity.updatedAt} 等）
 * 的 UTC 语义完全靠驱动按 {@code connectionTimeZone=UTC} 解释墙钟字面量。
 * {@code ddl-auto: validate} 只比列类型，看不见时区语义——连接串少带/带错这个参数没人会知道，
 * 历史行会整体平移一个时区差。所以这层只能自己证明：启动时写一个已知 {@code Instant} 到
 * 临时表、按字面量读回墙钟值，与期望的 UTC 墙上时间逐字比对，不符就 fail-fast 阻止启动。
 *
 * <p>只在 mysql profile 激活时执行（{@code @Profile("mysql")}）——H2/默认开发档自动跳过。
 * 临时表随连接销毁，自检结束即 DROP，不污染业务表、不产生残留。
 */
@Component
@Profile("mysql")
public class MysqlUtcSemanticsCheck implements InitializingBean {

    private final DataSource dataSource;

    public MysqlUtcSemanticsCheck(DataSource dataSource) {
        this.dataSource = dataSource;
    }

    @Override
    public void afterPropertiesSet() {
        verify(dataSource);
    }

    /** 用真实 DataSource 走一遍"写→读回→比对"；任何失败都阻止启动。 */
    static void verify(DataSource dataSource) {
        verify(dataSource, Instant.now().truncatedTo(ChronoUnit.MICROS));
    }

    /** 写入时刻由参数给定（组件实际入口用 now；测试用固定时刻，不靠真实时间）。 */
    static void verify(DataSource dataSource, Instant written) {
        LocalDateTime readBack;
        try {
            readBack = writeThenReadBackRawWallTime(dataSource, written);
        } catch (SQLException e) {
            throw new IllegalStateException("UTC 语义自检无法执行，拒绝启动：" + e.getMessage(), e);
        }
        assertUtcWallTime(written, readBack);
    }

    /**
     * 写入 {@code written} 并按 DATETIME 字面量读回墙钟值。
     * 写入走 {@code setTimestamp(Timestamp.from(...))}——与 Hibernate 对 {@code Instant}
     * 的 unwrap 链路同款（Instant → Timestamp → 驱动按连接时区解释成墙钟字面量落库）；
     * 读取走 {@code getObject(LocalDateTime.class)} 直通字面量，不再经过时区换算，
     * 拿到的就是数据库里真实存下的墙上时间。
     */
    static LocalDateTime writeThenReadBackRawWallTime(DataSource dataSource, Instant written)
            throws SQLException {
        try (Connection connection = dataSource.getConnection()) {
            execute(connection, "DROP TEMPORARY TABLE IF EXISTS twj_utc_semantics_check");
            execute(connection, "CREATE TEMPORARY TABLE twj_utc_semantics_check (ts DATETIME(6) NOT NULL)");
            try {
                try (PreparedStatement insert = connection.prepareStatement(
                        "INSERT INTO twj_utc_semantics_check (ts) VALUES (?)")) {
                    insert.setTimestamp(1, Timestamp.from(written));
                    insert.executeUpdate();
                }
                try (PreparedStatement select = connection.prepareStatement(
                        "SELECT ts FROM twj_utc_semantics_check");
                        ResultSet rs = select.executeQuery()) {
                    rs.next();
                    return rs.getObject(1, LocalDateTime.class);
                }
            } finally {
                execute(connection, "DROP TEMPORARY TABLE IF EXISTS twj_utc_semantics_check");
            }
        }
    }

    /**
     * 核心判定：读回的墙上时间必须逐字等于该时刻的 UTC 墙上时间。
     * 不等 = 驱动按非 UTC 连接时区解释了字面量 = 存档时间的 UTC 语义已被破坏。
     */
    static void assertUtcWallTime(Instant written, LocalDateTime readBack) {
        LocalDateTime expectedUtcWallTime = LocalDateTime.ofInstant(written, ZoneOffset.UTC);
        if (!expectedUtcWallTime.equals(readBack)) {
            throw new IllegalStateException(
                    "检测到连接时区不是 UTC，请在 TWELVEJADE_DB_URL 中加 connectionTimeZone=UTC"
                            + "（期望库里存 UTC 墙上时间 " + expectedUtcWallTime
                            + "，实际读回 " + readBack + "）。DATETIME 列不做时区转换，"
                            + "连接时区错了会把 updatedAt 等时间列整体平移一个时区差。");
        }
    }

    private static void execute(Connection connection, String sql) throws SQLException {
        try (Statement statement = connection.createStatement()) {
            statement.execute(sql);
        }
    }
}
```

### `GameServer/src/main/resources/application-mysql.yml`（小文件，全文）

```yaml
# 《十二玉楼长生经》服务端 · MySQL 档（默认不激活）
#
# 启用方式：java -jar game-server.jar --spring.profiles.active=mysql
# 全部连接信息来自环境变量，仓库里不留账号密码：
#   TWELVEJADE_DB_URL        例 jdbc:mysql://127.0.0.1:3306/twelvejade?useSSL=false&connectionTimeZone=UTC
#   TWELVEJADE_DB_USERNAME   例 root
#   TWELVEJADE_DB_PASSWORD   （无默认值，必须由环境提供）
#
# 时区语义（T102/W2-A）：DATETIME(6) 列不做时区转换，Instant 的 UTC 语义完全靠
# 驱动按 connectionTimeZone=UTC 解释墙钟字面量——这个参数是硬性要求，分三层兜底：
#   1. 下面 data-source-properties 已默认注入 connectionTimeZone=UTC（连接串漏带参数时的保险；
#      若连接串里也显式写了该参数，以连接串为准）；
#   2. 启动期自检 MysqlUtcSemanticsCheck 写→读回→比对一次，连接时区不是 UTC 时直接
#      拒绝启动，错误信息指名 TWELVEJADE_DB_URL 要加 connectionTimeZone=UTC；
#   3. ddl-auto: validate 只比列类型，看不见时区语义——类型对不等于语义对，
#      这一层只能靠上面两条自己证明。
#
# M6-02-C：MySQL 档 ddl-auto=validate——每次启动只读校验实体与真表结构一致：
# V1 脚本未执行或实体与 DDL 失配时启动直接失败（保护机制，宁可起不来也不带病运行）。
# 建表/变更仍必须走 Database/SQL/ 版本化脚本，Hibernate 永不自动改表。
spring:
  datasource:
    url: ${TWELVEJADE_DB_URL:}
    username: ${TWELVEJADE_DB_USERNAME:}
    password: ${TWELVEJADE_DB_PASSWORD:}
    driver-class-name: com.mysql.cj.jdbc.Driver
    hikari:
      data-source-properties:
        connectionTimeZone: UTC
  jpa:
    hibernate:
      ddl-auto: validate
    open-in-view: false
  h2:
    console:
      enabled: false
```

### `GameServer/src/test/java/com/twelvejade/server/common/MysqlUtcSemanticsCheckTest.java`

```java
package com.twelvejade.server.common;

import static org.assertj.core.api.Assertions.assertThat;
import static org.junit.jupiter.api.Assertions.assertDoesNotThrow;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;
import static org.mockito.ArgumentMatchers.anyString;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.when;

import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.sql.Statement;
import java.time.Instant;
import java.time.LocalDateTime;
import java.time.ZoneOffset;
import javax.sql.DataSource;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.BeanCreationException;
import org.springframework.boot.test.context.runner.ApplicationContextRunner;

/**
 * UTC 语义启动自检的逻辑证明（T102 / W2-A）：不依赖真 MySQL——
 * 用假读回值模拟"驱动按非 UTC 解释"，断言它抛异常；用 ApplicationContextRunner
 * 证明 H2/默认 profile 下自检根本不加载、mysql profile 下加载且真的会 fail-fast。
 * 与被测组件同包，包级私有方法可直接触达。
 */
class MysqlUtcSemanticsCheckTest {

    private static final Instant WRITTEN = Instant.parse("2026-10-05T09:32:19.123456Z");

    private final ApplicationContextRunner runner = new ApplicationContextRunner()
            .withUserConfiguration(MysqlUtcSemanticsCheck.class);

    @Test
    @DisplayName("读回的墙上时间不是 UTC 墙上时间 → 抛异常，错误信息指名 connectionTimeZone=UTC")
    void rejectsNonUtcWallTime() {
        // +08:00 的连接时区会把同一时刻存成北京墙上时间 17:32:19（比 UTC 多 8 小时）
        var readBackFromPlus8Connection = LocalDateTime.parse("2026-10-05T17:32:19.123456");
        var ex = assertThrows(IllegalStateException.class,
                () -> MysqlUtcSemanticsCheck.assertUtcWallTime(WRITTEN, readBackFromPlus8Connection));
        assertTrue(ex.getMessage().contains("connectionTimeZone=UTC"),
                "错误信息必须指名修法: " + ex.getMessage());
        assertTrue(ex.getMessage().contains("TWELVEJADE_DB_URL"),
                "错误信息必须指名环境变量: " + ex.getMessage());
    }

    @Test
    @DisplayName("读回的墙上时间等于 UTC 墙上时间 → 通过")
    void acceptsUtcWallTime() {
        var utcWallTime = LocalDateTime.ofInstant(WRITTEN, ZoneOffset.UTC);
        assertDoesNotThrow(() -> MysqlUtcSemanticsCheck.assertUtcWallTime(WRITTEN, utcWallTime));
    }

    @Test
    @DisplayName("H2/默认 profile 下自检组件不加载（自动跳过）")
    void skippedWithoutMysqlProfile() {
        runner.run(context -> {
            assertThat(context).hasNotFailed();
            assertThat(context).doesNotHaveBean(MysqlUtcSemanticsCheck.class);
        });
    }

    @Test
    @DisplayName("mysql profile 下自检加载并执行：模拟驱动按非 UTC 解释 → 启动失败")
    void failFastOnNonUtcWhenMysqlProfileActive() throws Exception {
        // 假 DataSource：读回 2000-01-01（结构上不可能等于任何真实写入时刻的 UTC 墙钟）
        DataSource dataSource = stubDataSourceReturning(
                LocalDateTime.parse("2000-01-01T00:00:00.000000"));
        runner.withPropertyValues("spring.profiles.active=mysql")
                .withBean(DataSource.class, () -> dataSource)
                .run(context -> {
                    assertThat(context).hasFailed();
                    assertThat(context.getStartupFailure()).isInstanceOf(BeanCreationException.class);
                    Throwable rootCause = context.getStartupFailure();
                    while (rootCause.getCause() != null) {
                        rootCause = rootCause.getCause();
                    }
                    assertThat(rootCause)
                            .isInstanceOf(IllegalStateException.class)
                            .hasMessageContaining("connectionTimeZone=UTC");
                });
    }

    @Test
    @DisplayName("固定时刻写入、读回逐字等于 UTC 墙上时间 → 自检通过")
    void passesOnUtcWallTimeWithFixedInstant() throws Exception {
        DataSource dataSource = stubDataSourceReturning(
                LocalDateTime.ofInstant(WRITTEN, ZoneOffset.UTC));
        assertDoesNotThrow(() -> MysqlUtcSemanticsCheck.verify(dataSource, WRITTEN));
    }

    /** 桩 DataSource：临时表写读链路全部 mock，读回值由调用方指定。 */
    private static DataSource stubDataSourceReturning(LocalDateTime readBack) throws SQLException {
        DataSource dataSource = mock(DataSource.class);
        Connection connection = mock(Connection.class);
        Statement statement = mock(Statement.class);
        PreparedStatement insert = mock(PreparedStatement.class);
        PreparedStatement select = mock(PreparedStatement.class);
        ResultSet resultSet = mock(ResultSet.class);
        when(dataSource.getConnection()).thenReturn(connection);
        when(connection.createStatement()).thenReturn(statement);
        when(connection.prepareStatement(anyString())).thenReturn(insert, select);
        when(select.executeQuery()).thenReturn(resultSet);
        when(resultSet.next()).thenReturn(true);
        when(resultSet.getObject(1, LocalDateTime.class)).thenReturn(readBack);
        return dataSource;
    }
}
```

### `GameServer/src/test/java/com/twelvejade/server/SaveUploadRobustnessTest.java`

```java
package com.twelvejade.server;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import com.twelvejade.server.common.AuthTokens;
import com.twelvejade.server.save.SaveRepository;
import java.time.OffsetDateTime;
import java.time.ZoneOffset;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;
import java.util.concurrent.TimeUnit;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.http.MediaType;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.test.web.servlet.MvcResult;
import tools.jackson.databind.json.JsonMapper;

/**
 * 上传路径健壮性现状钉测（T035 服务端侧 / W2-B）。
 *
 * <p>目标不是"全绿表演"，而是把现状逐条钉成可断言的事实：畸形输入走到哪、
 * 错误码与 HTTP 状态怎么配对、并发覆盖语义下最终库里是什么、客户端自带的
 * 时间戳字段会不会影响服务端生成的 updatedAt。观察式结论写进 W2-report.md。
 */
@SpringBootTest
@AutoConfigureMockMvc
class SaveUploadRobustnessTest {

    private static final JsonMapper MAPPER = JsonMapper.builder().build();

    @Autowired
    private MockMvc mockMvc;

    @Autowired
    private SaveRepository saveRepository;

    private static String body(String rawJson) {
        return rawJson;
    }

    private MvcResult uploadRaw(String token, String rawJson, int expectedStatus, int expectedCode)
            throws Exception {
        return mockMvc.perform(post("/api/save/upload")
                        .header(AuthTokens.HEADER, token)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(rawJson))
                .andExpect(status().is(expectedStatus))
                .andExpect(jsonPath("$.code").value(expectedCode))
                .andReturn();
    }

    private void assertSlotEmpty(String token, int slot) throws Exception {
        mockMvc.perform(get("/api/save/download")
                        .header(AuthTokens.HEADER, token)
                        .param("slot", String.valueOf(slot)))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value(0))
                .andExpect(jsonPath("$.data.present").value(false));
    }

    @Test
    @DisplayName("schemaVersion 是字符串（\"10\"）→ 400 / 40001，且绝不静默落库")
    void stringSchemaVersionIsRejected() throws Exception {
        var session = TestSessions.registerNewUser(mockMvc);
        uploadRaw(session.token(),
                body("{\"slot\":1,\"schemaVersion\":\"10\",\"save\":{\"coins\":1}}"), 400, 40001);
        assertSlotEmpty(session.token(), 1);
    }

    @Test
    @DisplayName("save 不是 JSON 对象（字符串/数组）→ 400 / 40001，且绝不静默落库")
    void saveNonObjectIsRejected() throws Exception {
        var session = TestSessions.registerNewUser(mockMvc);
        uploadRaw(session.token(),
                body("{\"slot\":1,\"schemaVersion\":10,\"save\":\"not-an-object\"}"), 400, 40001);
        uploadRaw(session.token(),
                body("{\"slot\":1,\"schemaVersion\":10,\"save\":[1,2,3]}"), 400, 40001);
        assertSlotEmpty(session.token(), 1);
    }

    @Test
    @DisplayName("HTTP 状态与业务 code 成对出现：200↔0、400↔40001（畸形输入逐条）")
    void errorCodeAndHttpStatusArePaired() throws Exception {
        var session = TestSessions.registerNewUser(mockMvc);
        // 成功路径：HTTP 200 且 code 恰为 0（不允许"200 但 code 非 0"）
        mockMvc.perform(post("/api/save/upload")
                        .header(AuthTokens.HEADER, session.token())
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(body("{\"slot\":1,\"schemaVersion\":10,\"save\":{\"coins\":1}}")))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value(0));
        // 失败路径：HTTP 4xx 且 body 里带着非零业务 code（不允许"4xx 但没有 code"）
        uploadRaw(session.token(), body("{\"slot\":1,\"save\":{\"coins\":1}}"), 400, 40001);
        uploadRaw(session.token(),
                body("{\"slot\":1,\"schemaVersion\":\"10\",\"save\":{}}"), 400, 40001);
        uploadRaw(session.token(),
                body("{\"slot\":1,\"schemaVersion\":10,\"save\":\"text\"}"), 400, 40001);
        uploadRaw(session.token(),
                body("{\"slot\":9,\"schemaVersion\":10,\"save\":{}}"), 400, 40001);
    }

    @Test
    @DisplayName("客户端在 save 里自带未来时间戳 → 只是被存的数据，updatedAt 仍由服务端生成")
    void clientSuppliedFutureTimestampDoesNotAffectUpdatedAt() throws Exception {
        var session = TestSessions.registerNewUser(mockMvc);
        var fakeFuture = OffsetDateTime.now(ZoneOffset.UTC).plusYears(10).toString();
        var receipt = TestSessions.dataOf(uploadRaw(session.token(),
                body("{\"slot\":1,\"schemaVersion\":10,\"save\":{\"coins\":1,\"clientTs\":\""
                        + fakeFuture + "\"}}"), 200, 0));

        // 回执 updatedAt 是服务端"现在"生成，绝无可能是客户端塞进来的那个未来值
        var receiptUpdatedAt = (String) receipt.get("updatedAt");
        assertNotEquals(fakeFuture, receiptUpdatedAt, "updatedAt 不能被客户端 payload 影响");
        assertTrue(OffsetDateTime.parse(receiptUpdatedAt)
                        .isBefore(OffsetDateTime.parse(fakeFuture)),
                "服务端生成的 updatedAt 应早于客户端伪造的未来时间");

        // 客户端字段原样往返（只存不读），而 updatedAt 与回执逐字一致
        var data = TestSessions.dataOf(mockMvc.perform(get("/api/save/download")
                        .header(AuthTokens.HEADER, session.token())
                        .param("slot", "1"))
                .andExpect(status().isOk())
                .andReturn());
        @SuppressWarnings("unchecked")
        var returned = (java.util.Map<String, Object>) data.get("save");
        assertEquals(fakeFuture, returned.get("clientTs"), "客户端字段只是数据，原样存取");
        assertEquals(receiptUpdatedAt, data.get("updatedAt"), "同一秒内回执与 download 同值");
    }

    @Test
    @DisplayName("并发上传同 (user, slot)：库里恒为单行且是完整两份之一，状态码只允许 200/500")
    void concurrentUploadsToSameSlotKeepSingleRow() throws Exception {
        var observedStatuses = new ArrayList<Integer>();
        int rounds = 6;
        for (int round = 0; round < rounds; round++) {
            var session = TestSessions.registerNewUser(mockMvc);
            var barrier = new CountDownLatch(1);
            var firstPayload = "{\"coins\":1,\"from\":\"first\"}";
            var secondPayload = "{\"coins\":2,\"from\":\"second\"}";
            ExecutorService executor = Executors.newFixedThreadPool(2);
            try {
                Future<String[]> first = executor.submit(
                        () -> uploadConcurrently(session.token(), barrier, firstPayload));
                Future<String[]> second = executor.submit(
                        () -> uploadConcurrently(session.token(), barrier, secondPayload));
                barrier.countDown();
                for (String[] r : new String[][] {first.get(30, TimeUnit.SECONDS),
                        second.get(30, TimeUnit.SECONDS)}) {
                    int httpStatus = Integer.parseInt(r[0]);
                    observedStatuses.add(httpStatus);
                    assertTrue(httpStatus == 200 || httpStatus == 500,
                            "并发上传只应出现 200（胜者）或 500（唯一约束撞车），实际: "
                                    + httpStatus + " body=" + r[1]);
                }
            } finally {
                executor.shutdownNow();
            }

            // 不变量：恒为单行（唯一约束生效），内容是两份之一且完整（没有半条记录）
            var rows = saveRepository.findAllByUser_IdOrderBySlotAsc((long) session.userId());
            assertEquals(1, rows.size(),
                    "(user_id, slot) 唯一约束：并发后该用户该槽位只能有一条");
            var data = TestSessions.dataOf(mockMvc.perform(get("/api/save/download")
                            .header(AuthTokens.HEADER, session.token())
                            .param("slot", "1"))
                    .andExpect(status().isOk())
                    .andExpect(jsonPath("$.data.present").value(true))
                    .andReturn());
            @SuppressWarnings("unchecked")
            var returned = (java.util.Map<String, Object>) data.get("save");
            boolean isOneOfThem = returned.equals(MAPPER.readValue(firstPayload, java.util.Map.class))
                    || returned.equals(MAPPER.readValue(secondPayload, java.util.Map.class));
            assertTrue(isOneOfThem, "最终落库的必须是两份完整 payload 之一，实际: " + returned);
        }
        // 观察记录进标准输出，供 W2-report 引用（断言只钉不变量，不钉时序结果）
        System.out.println("[SaveUploadRobustness] 并发上传 " + rounds + " 轮 × 2 请求，"
                + "状态码分布: " + observedStatuses);
    }

    /** 并发任务：等栅栏放行后上传，返回 [httpStatus, 响应体原文]。 */
    private String[] uploadConcurrently(String token, CountDownLatch barrier, String payload)
            throws Exception {
        barrier.await(10, TimeUnit.SECONDS);
        try {
            MvcResult result = mockMvc.perform(post("/api/save/upload")
                            .header(AuthTokens.HEADER, token)
                            .contentType(MediaType.APPLICATION_JSON)
                            .content(body("{\"slot\":1,\"schemaVersion\":10,\"save\":" + payload + "}")))
                    .andReturn();
            String responseBody = result.getResponse().getContentAsString();
            int httpStatus = result.getResponse().getStatus();
            if (httpStatus != 200) {
                System.out.println("[SaveUploadRobustness] 非成功响应 status=" + httpStatus
                        + " body=" + responseBody);
            }
            return new String[] {String.valueOf(httpStatus), responseBody};
        } catch (Exception servletEx) {
            // MockMvc 对未处理异常直接向上抛（真容器里等价于 500 /error），
            // 唯一约束竞态的败者就长这个形态。按 500 计，但只允许是唯一约束冲突这一种根因。
            assertTrue(isUniqueConstraintViolation(servletEx),
                    "竞态败者只应是唯一约束冲突（真容器 500），异常链: " + servletEx);
            System.out.println("[SaveUploadRobustness] 竞态败者（MockMvc 抛出，真容器等价 500）"
                    + "根因: " + bottomCause(servletEx).getMessage().split("\n")[0]);
            return new String[] {"500", "DataIntegrityViolationException（UK_SAVES_USER_SLOT）"};
        }
    }

    /** 异常链上任一环是约束冲突（Spring/Hibernate/H2 任意层），或消息里点名了唯一约束。 */
    private static boolean isUniqueConstraintViolation(Throwable ex) {
        for (Throwable t = ex; t != null; t = t.getCause()) {
            if (t instanceof org.springframework.dao.DataIntegrityViolationException
                    || t instanceof org.hibernate.exception.ConstraintViolationException
                    || t instanceof java.sql.SQLIntegrityConstraintViolationException
                    || String.valueOf(t.getMessage()).contains("UK_SAVES_USER_SLOT")) {
                return true;
            }
        }
        return false;
    }

    private static Throwable bottomCause(Throwable ex) {
        Throwable t = ex;
        while (t.getCause() != null) {
            t = t.getCause();
        }
        return t;
    }
}
```

## 附录 2：修改既有文件的 diff（基线 = `edf843f` 之后的工作树）

```diff
diff --git a/Database/SQL/V1__user_cloud_save.sql b/Database/SQL/V1__user_cloud_save.sql
@@ -39,8 +39,14 @@ CREATE TABLE IF NOT EXISTS saves (
   COLLATE = utf8mb4_0900_ai_ci COMMENT ='云存档（每用户每槽位一份，覆盖式）';
 
 -- 说明：
--- 1. MySQL 的 TIMESTAMP 有 2038 上限并隐式做时区转换，这里统一用 DATETIME(6)，
---    语义与实体里的 java.time.LocalDateTime 一致（H2 侧自动建表为 TIMESTAMP，等价）。
+-- 1. 本表 DATETIME(6) 列（updated_at，含 users 的 created_at/token_at）存的是
+--    **UTC 墙上时间**，前提是连接串带 connectionTimeZone=UTC（serverTimezone 的现名；
+--    application-mysql.yml 已默认注入该驱动属性，启动自检 MysqlUtcSemanticsCheck
+--    写→读回→比对，连接时区不是 UTC 时拒绝启动）。DATETIME 列不做时区转换，
+--    java.time.Instant（实体 updatedAt，Hibernate 按 UTC 存取）与 DATETIME 之间的
+--    换算全部发生在驱动侧——连接时区填错，历史行会整体平移一个时区差。
+--    MySQL 的 TIMESTAMP 有 2038 上限并隐式做时区转换，故类型仍统一用 DATETIME(6)
+--    （H2 侧自动建表为 TIMESTAMP，Instant 经 JDBC 同样按 UTC 解释，等价）。
 -- 2. uk_saves_token 允许多个 NULL（SQL 标准行为），对应"未登录"状态。
 -- 3. uk_saves_user_slot 是覆盖语义的落点：上传按它 INSERT-or-UPDATE，
 --    同一用户同一槽位永远只有一条。

diff --git a/Database/SQL/MySQL接入手册.md b/Database/SQL/MySQL接入手册.md
@@ -44,14 +44,18 @@
 | 环境变量 | 示例 | 说明 |
 |---|---|---|
-| `TWELVEJADE_DB_URL` | `jdbc:mysql://127.0.0.1:3306/twelvejade?useSSL=false&serverTimezone=UTC` | JDBC 连接串 |
+| `TWELVEJADE_DB_URL` | `jdbc:mysql://127.0.0.1:3306/twelvejade?useSSL=false&connectionTimeZone=UTC` | JDBC 连接串 |
 | `TWELVEJADE_DB_USERNAME` | `twelvejade` | 数据库账号 |
 | `TWELVEJADE_DB_PASSWORD` | （无默认值） | 数据库密码 |
 
+> `connectionTimeZone` 是 `serverTimezone` 的现名（Connector/J 8.0.23 起旧名废弃），一律用新名。
+> 该参数是硬性要求：DATETIME(6) 列不做时区转换，`updatedAt` 的 UTC 语义全靠它；
+> 缺了它启动自检（`MysqlUtcSemanticsCheck`）会拒绝启动。
+
 Windows PowerShell 设置示例：
 
 ```powershell
-$env:TWELVEJADE_DB_URL = "jdbc:mysql://127.0.0.1:3306/twelvejade?useSSL=false&serverTimezone=UTC"
+$env:TWELVEJADE_DB_URL = "jdbc:mysql://127.0.0.1:3306/twelvejade?useSSL=false&connectionTimeZone=UTC"
```

`application-mysql.yml` 全文见附录 1（小文件按工单要求给全文）。
`Editor/NetworkShot.cs` 为 git 未跟踪文件（无 diff 基线），六处改动的前后对照见
"W2-C"一节的逐条描述，关键片段：

```csharp
// 改前（S8，原 :256）
// 服务端 updatedAt 是无时区的 ISO 本地时间字符串（ISO_LOCAL_DATE_TIME），必须可解析
Check(DateTimeOffset.TryParse("2026-10-04T01:30:57.908464", out _),
    "S8 服务端 ISO_LOCAL_DATE_TIME 格式可被 TryParse 接受");
// 改后
// W-Server-Time 起服务端 updatedAt 是带 UTC 偏移的 ISO8601（Instant，Z 结尾），必须可解析
Check(DateTimeOffset.TryParse("2026-10-05T12:09:33.69094Z", out _),
    "S8 服务端带偏移的 ISO8601 格式可被 TryParse 接受");

// 改前（RealListBody / RealUploadReceipt 常量，原 :341-346）
"updatedAt\":\"2026-10-04T20:32:37.167741\""   /   "updatedAt\":\"2026-10-04T20:32:37.1677405\""
// 改后（W2 真跑 curl 原文）
"updatedAt\":\"2026-10-05T12:09:33.69094Z\""   （两处常量 + S12 硬编码断言 + S13 downloadBody 同值）

// 新增（S14 正向断言；裸时间判 Unknown 的容错断言原样保留）
Check(CloudSaveService.Compare(nowUtc, DateTime.UtcNow.AddHours(+5).ToString("O"))
        == ConflictSide.CloudNewer,
    "S14 云端带 Z 偏移且比本地新 → 必须判 CloudNewer");
```

`Editor/NetworkShot.cs` 在本工单开工前已被架构师合入仓库，因此也有真 diff 可用
（`git diff -- GameClient/TwelveJade/Assets/Editor/NetworkShot.cs`，19 insertions / 13 deletions，
恰好上述六处；S14 的两条裸时间容错断言原样保留在 diff 上下文里可见）：

```diff
@@ -253,9 +253,9 @@
-            // 服务端 updatedAt 是无时区的 ISO 本地时间字符串（ISO_LOCAL_DATE_TIME），必须可解析
-            Check(DateTimeOffset.TryParse("2026-10-04T01:30:57.908464", out _),
-                "S8 服务端 ISO_LOCAL_DATE_TIME 格式可被 TryParse 接受");
+            // W-Server-Time 起服务端 updatedAt 是带 UTC 偏移的 ISO8601（Instant，Z 结尾），必须可解析
+            Check(DateTimeOffset.TryParse("2026-10-05T12:09:33.69094Z", out _),
+                "S8 服务端带偏移的 ISO8601 格式可被 TryParse 接受");
@@ -335,15 +335,16 @@
-        // 下面四个场景的响应体全部取自 2026-10-04 对本地真后端（stage=M6-02-S）
-        // 实测 curl 回来的原文，不是照想象编的——假传输最大的风险就是客户端和自己的想象对表。
+        // 下面场景的响应体取自 2026-10-05 对本地真后端（W-Server-Time 已合入，
+        // updatedAt 为带 UTC 偏移的 ISO8601）实测 curl 回来的原文，不是照想象编的——
+        // 假传输最大的风险就是客户端和自己的想象对表。
 
         const string RealListBody = "{\"code\":0,\"message\":\"成功\",\"data\":{\"slots\":[" +
-            "{\"slot\":1,\"present\":true,\"updatedAt\":\"2026-10-04T20:32:37.167741\"}," +
+            "{\"slot\":1,\"present\":true,\"updatedAt\":\"2026-10-05T12:09:33.69094Z\"}," +
@@ -411,7 +412,7 @@
-            Check(updatedAt == "2026-10-04T20:32:37.1677405", "S12 回执里的 updatedAt 应回传给 UI", updatedAt);
+            Check(updatedAt == "2026-10-05T12:09:33.69094Z", "S12 回执里的 updatedAt 应回传给 UI", updatedAt);
@@ -443,7 +444,7 @@
-                "\"schemaVersion\":10,\"updatedAt\":\"2026-10-04T20:32:37.167741\",\"save\":" + cloudPayload + "}}";
+                "\"schemaVersion\":10,\"updatedAt\":\"2026-10-05T12:09:33.69094Z\",\"save\":" + cloudPayload + "}}";
@@ -488,10 +489,10 @@
-            // 真后端的 updatedAt 是 LocalDateTime + ISO_LOCAL_DATE_TIME：不带任何偏移量。
-            // 本地 updatedUtc 是 DateTimeOffset.UtcNow.ToString("O")：带偏移。
-            // 拿不带偏移的那一侧按本机时区补齐，开发机上"看起来对"，服务端一换机房就差一个时区，
-            // 判定会整体翻向——用户点下载就覆盖掉自己更新的进度。所以裸时间必须判 Unknown。
+            // W-Server-Time 之后，真后端的 updatedAt 已带 UTC 偏移（Instant，Z 结尾）。
+            // 但"裸时间（无偏移）判 Unknown"的防御必须保留：它挡的是一切不带时区的来源——
+            // 历史存量数据、第三方回放、将来某处的格式回归。裸时间按本机时区补齐会在
+            // 服务端换机房时整体翻向（用户点下载覆盖掉自己更新的进度），宁可不猜方向。
@@ -506,6 +507,11 @@
+            // 正向契约（W-Server-Time 后的服务端新格式）：云端带 Z 偏移且确实比本地新时，
+            // 比对必须给出 CloudNewer，而不是退回 Unknown——否则服务端修好了客户端照样不敢判。
+            Check(CloudSaveService.Compare(nowUtc, DateTime.UtcNow.AddHours(+5).ToString("O"))
+                    == ConflictSide.CloudNewer,
+                "S14 云端带 Z 偏移且比本地新 → 必须判 CloudNewer");
```
