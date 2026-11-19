# MySQL 8 接入手册（M6-03-S · T024 关账配套）

> 服务端生产/真库接入步骤。命令级可执行，照抄即可。
> **状态：本手册在无 MySQL 实例的机器上编写，步骤未经真库实跑**——执行人照做时
> 如命令行为与本文不符，停下来对照 `Database/SQL/V1__user_cloud_save.sql` 与
> `GameServer/src/main/resources/application-mysql.yml` 核实后再继续。
> 本机联调无需 MySQL：默认 profile 用 H2 内存库（`jdbc:h2:mem:twelvejade`）。

## 0. 前置条件

- MySQL 8.0+ 已安装并可连接（本手册命令以 `mysql` 客户端为例）。
- JDK 21、服务端 jar 已构建：`cd GameServer && ./mvnw -q package -DskipTests`
- 目标库名约定 `twelvejade`（utf8mb4）。

## 1. 建库（utf8mb4）

```sql
CREATE DATABASE IF NOT EXISTS twelvejade
  DEFAULT CHARACTER SET utf8mb4
  COLLATE utf8mb4_0900_ai_ci;
-- 建议专用账号，权限只给这一个库：
CREATE USER IF NOT EXISTS 'twelvejade'@'localhost' IDENTIFIED BY '< StrongPasswordHere >';
GRANT ALL PRIVILEGES ON twelvejade.* TO 'twelvejade'@'localhost';
FLUSH PRIVILEGES;
```

## 2. 执行建表脚本 V1

```bash
mysql -u twelvejade -p twelvejade < Database/SQL/V1__user_cloud_save.sql
```

预期：`users`、`saves` 两张表建成（无报错）。核对：

```sql
SHOW CREATE TABLE users\G   -- 应含 uk_users_username / uk_users_token
SHOW CREATE TABLE saves\G   -- 应含 uk_saves_user_slot 与外键 fk_saves_user
```

脚本可重复执行（`CREATE TABLE IF NOT EXISTS`），但**表结构变更必须新增 V2+ 脚本**，
不准改历史脚本——H2 内存库靠 Hibernate 自动建表不受影响，MySQL 档的唯一结构真相在这里。

## 3. 环境变量（与应用配置同名，仓库不落账号密码）

| 环境变量 | 示例 | 说明 |
|---|---|---|
| `TWELVEJADE_DB_URL` | `jdbc:mysql://127.0.0.1:3306/twelvejade?useSSL=false&connectionTimeZone=UTC` | JDBC 连接串 |
| `TWELVEJADE_DB_USERNAME` | `twelvejade` | 数据库账号 |
| `TWELVEJADE_DB_PASSWORD` | （无默认值） | 数据库密码 |

> `connectionTimeZone` 是 `serverTimezone` 的现名（Connector/J 8.0.23 起旧名废弃），一律用新名。
> 该参数是硬性要求：DATETIME(6) 列不做时区转换，`updatedAt` 的 UTC 语义全靠它；
> 缺了它启动自检（`MysqlUtcSemanticsCheck`）会拒绝启动。

Windows PowerShell 设置示例：

```powershell
$env:TWELVEJADE_DB_URL = "jdbc:mysql://127.0.0.1:3306/twelvejade?useSSL=false&connectionTimeZone=UTC"
$env:TWELVEJADE_DB_USERNAME = "twelvejade"
$env:TWELVEJADE_DB_PASSWORD = "<StrongPasswordHere>"
```

## 4. 结构校验启动（M6-02-C 起 validate 为 mysql 档默认）

mysql 档 `ddl-auto: validate`（M6-02-C 起为默认，取代此前的 none）：每次启动 Hibernate
只读核对实体注解与真表结构一致；**V1 未执行或实体与 DDL 失配时启动直接失败（保护机制）**：

```bash
java -jar GameServer/target/game-server-0.0.1.jar --spring.profiles.active=mysql
```

- 启动成功（日志见 `Started TwelveJadeServerApplication` + health 返回 code 0）= 实体与表结构一致。
- 启动抛 `SchemaManagementException` / `Missing table` 等 → 实体与 V1 脚本失配，
  **先修 V2 脚本对齐，不准改 ddl-auto 放行**。

## 5. 正常运行与验收

```bash
java -jar GameServer/target/game-server-0.0.1.jar --spring.profiles.active=mysql
```
curl http://127.0.0.1:18080/api/health
```

功能验收：按 `GameServer/README.md` 的 curl 全链路手工执行等价链
（register → login → upload → download → list → 无 token → 超版本 → refresh →
改密码 → 锁定 → 超限）。

`GameServer/scripts/acceptance.ps1` 说明：脚本固定以**默认 H2 档**自起自停 jar
（内存库保证每轮全新数据），mysql 档不做脚本化验收——真库验收按本节手工跑等价链。
**注意**：mysql 档数据不随重启清空，重复跑 register `tester` 会得到 40902，
先换用户名或清库重跑。

## 6. 回滚步骤

1. 停服务（结束 java 进程 / `TWELVEJADE_PORT` 对应端口监听者）。
2. 回滚表结构（如需回退到无服务端状态）：

```sql
USE twelvejade;
SET FOREIGN_KEY_CHECKS = 0;
DROP TABLE IF EXISTS saves;
DROP TABLE IF EXISTS users;
SET FOREIGN_KEY_CHECKS = 1;
```

3. 如需整库回退：`DROP DATABASE IF EXISTS twelvejade;`（**含全部用户与存档数据，先备份**：

```bash
mysqldump -u twelvejade -p twelvejade > twelvejade-backup-$(date +%Y%m%d).sql
```

）。
4. 应用侧回滚 = 换回默认 H2 档重启（不带 `--spring.profiles.active=mysql`），
   此时服务回到内存库模式，线上数据不再可见——这是停机级别操作，先公告。
