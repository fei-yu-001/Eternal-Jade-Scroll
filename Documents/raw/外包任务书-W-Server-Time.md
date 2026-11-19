# 外包任务书 W-Server-Time（自包含，可直接整份复制给外部 AI 工具）

> 本文件是给**外部一次性 AI 工具**的完整工单。它读不到项目的架构文档，
> 所以所有必要信息都在下面这一份里，不要额外引用仓库外文档。
> 交付方式：**补丁制**——不许 commit、不许 push、不改共享文档。

---

你是一次性外包工程师，代号 W-Server-Time。任务：修一个 Spring Boot 后端的时区缺陷。
你只有本任务书里的信息，不要猜测项目其他部分，也不要扩大范围。

## 仓库与你的工作范围

一个 Java 17 + Spring Boot 项目，Maven wrapper 在仓库根：

```
<repo>/GameServer/            ← 你唯一允许修改的目录
<repo>/GameServer/mvnw        ← 测试入口（Windows 用 mvnw.cmd）
```

## 缺陷

云存档接口返回的时间戳**不带时区偏移**，导致客户端无法判断"本地存档与云端存档谁更新"。

现象：客户端拿到形如 `2026-10-05T09:32:19.123456` 的字符串，末尾没有任何 `Z` 或 `+08:00` 标记。
客户端拿自己本地的、带偏移的时间戳去比大小，只能按**本机时区**补齐这个裸时间——
一旦服务端换到 UTC 机房，"谁更新"的判断会整体偏一个时区差，用户点"下载"就会覆盖掉自己更新的进度。

## 要改的位置

行号是架构师 2026-10-05 实测的，动手前先自己确认一遍。

`src/main/java/com/twelvejade/server/save/SaveController.java`

| 行 | 现状 | 问题 |
|---|---|---|
| :51 | `private static final DateTimeFormatter ISO_DATE_TIME = DateTimeFormatter.ISO_LOCAL_DATE_TIME;` | 无偏移的根源 |
| :120 | `var now = LocalDateTime.now();` | **没用注入的 Clock，违反本仓自己的纪律** |
| :121 | `entity.setUpdatedAt(now);` | 入库值 |
| :128 | `receipt.put("updatedAt", ISO_DATE_TIME.format(now));` | `/api/save/upload` 回执 |
| :153 | `result.put("updatedAt", ISO_DATE_TIME.format(stored.getUpdatedAt()));` | `/api/save/download` |
| :178 | `entry.put("updatedAt", stored == null ? null : ISO_DATE_TIME.format(stored.getUpdatedAt()));` | `/api/save/list` |

`src/main/java/com/twelvejade/server/save/SaveEntity.java`
- `:56` `private LocalDateTime updatedAt;` ← 实体字段类型

`src/main/java/com/twelvejade/server/common/TimeConfig.java`
- 该类注释明文规定：「凡是"现在几点"的判断只准注入这个 Clock 取，不准散落 `LocalDateTime.now()`」。
  你的改动必须遵守：构造注入 `Clock`，取值一律 `LocalDateTime.now(clock)` / `Instant.now(clock)` 形态。

## 必做

1. 让上面三个输出点（`:128` / `:153` / `:178`）产生的字符串**自带 UTC 偏移**，
   例如 `2026-10-05T09:32:19.123Z` 或 `2026-10-05T09:32:19.123+00:00`。
2. 时间取值一律走注入的 `Clock`（修掉 `:120` 那处裸 `now()`）。
3. 存储语义必须自描述、无歧义。
   - **首选方案**：把实体字段类型改成 `java.time.Instant`
     （Hibernate 6 对 `Instant` 的映射默认按 UTC 存，H2 与 MySQL 都支持），
     序列化用 `DateTimeFormatter.ISO_INSTANT` 或 `ISO_OFFSET_DATE_TIME`。
   - 若你实测发现 `Instant` 与本工程既有 DDL / 实体校验冲突、改不动，**退回方案**：
     字段保持 `LocalDateTime` 但语义固定为 UTC（取值用 Clock 且 Zone 为 UTC），只在序列化时补偏移；
     此时必须在字段上加一行注释写明"本列存 UTC 墙上时间"，并在报告里说明你为什么退回。
   - 两个方案都算合格，但**必须在报告里写明你选了哪个、为什么**。
4. **测试必须收紧，不能只断言"非空"**。现有这些断言即使带 bug 也会绿，正是本次要修的点：
   - `src/test/java/com/twelvejade/server/SaveContractTest.java:67` → `jsonPath("$.data.updatedAt").isNotEmpty()`
   - `src/test/java/com/twelvejade/server/SavePersistenceTest.java:142,160,209` → 同类 `isNotEmpty` / `containsKey`

   改成：断言返回的字符串**能被解析为带偏移的时间**（例如 `OffsetDateTime.parse(s)` 不抛，
   或正则要求末尾出现 `Z` 或 `±HH:MM`），并且**同一秒内 upload 与随后 list/download 给出同一个值**。
   另外加一条：用**可控制时间的 Clock**（固定时刻）注入，断言输出字符串逐字等于期望值——
   测试不许靠 sleep 或真实时间通过。
5. 修完跑 `mvnw.cmd -q test`（或 `./mvnw -q test`），必须全绿；把输出关键行贴进报告。
6. 在 `GameServer/README.md` 增补一行：`updatedAt` 语义由"服务器本地墙上时间"改为"带偏移的 UTC"，
   以及**变更前写入的旧行在新语义下可能偏移一个时区差**。

## 硬禁令（违反任何一条，交付作废）

- 禁止修改 `GameServer/` 之外的任何文件（客户端、Unity 工程、文档、脚本都不许碰）。
- 禁止修改 `ClientSchema.SUPPORTED_SCHEMA`（当前是 `10`，
  位于 `src/main/java/com/twelvejade/server/save/ClientSchema.java:13`）——
  本次改动**不涉及**存档 schema 版本，不许顺手升版。
- 禁止修改存档 payload 的字段结构、256KB 体积上限、槽位唯一性校验、令牌与登录锁定逻辑。
- 禁止 `git commit` / `git push` / 改 git 配置。你只产出补丁文件。
- 禁止改数据库 DDL 脚本或写数据迁移。旧数据的时间值在新语义下可能偏移一个时区差，
  本项目处于开发期、单用户，**这是已被接受的成本**——你只需按"必做 6"在 README 记一行，不要写迁移。
- 禁止为了让测试通过而放宽断言或删断言。
- 禁止全仓搜索式探索（你的预算有限）：只读上面列出的 4 个主文件 + 2 个测试文件 + `README.md`。

## 交付物（三样，缺一不可）

1. **`W-Server-Time.patch`**：标准 unified diff（`git diff` 或 `diff -u` 生成），只含 `GameServer/` 内改动。
2. **`W-report.md`**，按这个结构写：
   ```
   ## 选了什么方案（Instant 还是 LocalDateTime+UTC），为什么
   ## 改了哪几处（文件:行 + 一句话）
   ## 新增/收紧了哪几条断言（逐条列出断言原文）
   ## mvnw test 输出关键行（原文粘贴，含测试总数与 BUILD SUCCESS/FAILURE）
   ## 一条 curl 实测（upload 回执 JSON 原文，能看到带偏移的 updatedAt）
   ## 你不确定 / 没做的事（如实写，空着也要写"无"）
   ```
3. 如果第 3 条你走了退回方案，额外说明你实测撞到了什么报错（贴原文）。

## 什么算做完

三个输出点的字符串都带偏移 + 取值走注入 Clock + 断言能抓住"不带偏移"这种回归 + `mvnw test` 全绿。

**做不到就停下，在报告最后一段写清卡在哪，不要交一个"看起来做完了"的东西。**
本项目有一条反复验证过的规矩：如实写"没做完"比凑成绿色更值钱。
