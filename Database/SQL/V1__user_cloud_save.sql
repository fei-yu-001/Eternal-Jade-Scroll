-- =====================================================================
-- 《十二玉楼长生经》服务端 · V1 用户与云存档（M6-02-S）
-- 目标库：MySQL 8.0+，库字符集 utf8mb4
-- 与 JPA 实体一一对应：
--   users  -> com.twelvejade.server.auth.UserEntity
--   saves  -> com.twelvejade.server.save.SaveEntity
-- 默认 profile（本地联调）用 H2 内存库自动建表，不执行本脚本；
-- 本脚本用于 mysql profile / 生产库（application-mysql.yml 的 ddl-auto=validate，
-- 表结构变更必须走本目录的版本化脚本，不允许 Hibernate 擅自建表）。
-- =====================================================================

CREATE TABLE IF NOT EXISTS users (
    id            BIGINT       NOT NULL AUTO_INCREMENT,
    username      VARCHAR(32)  NOT NULL COMMENT '3-32 字符，全库唯一，区分大小写',
    password_hash VARCHAR(100) NOT NULL COMMENT 'BCrypt 哈希（自带盐，60 字符）',
    token         CHAR(36)     NULL COMMENT '登录令牌 UUID；null=未登录，登录换发覆盖',
    created_at    DATETIME(6)  NOT NULL COMMENT '注册时间',
    token_at      DATETIME(6)  NULL COMMENT '最近一次签发令牌时间',
    PRIMARY KEY (id),
    UNIQUE KEY uk_users_username (username),
    UNIQUE KEY uk_users_token (token)
) ENGINE = InnoDB
  DEFAULT CHARSET = utf8mb4
  COLLATE = utf8mb4_0900_ai_ci COMMENT ='注册用户（M6-02）';

CREATE TABLE IF NOT EXISTS saves (
    id             BIGINT      NOT NULL AUTO_INCREMENT,
    user_id        BIGINT      NOT NULL COMMENT '所属用户',
    slot           TINYINT     NOT NULL COMMENT '槽位 1-3，与客户端三档一致',
    schema_version INT         NOT NULL COMMENT '客户端声明的存档结构版本，原样存档',
    payload        LONGTEXT    NOT NULL COMMENT '客户端存档 JSON 原文，服务端只存不读',
    updated_at     DATETIME(6) NOT NULL COMMENT '最近一次覆盖上传时间',
    PRIMARY KEY (id),
    UNIQUE KEY uk_saves_user_slot (user_id, slot),
    KEY idx_saves_user (user_id),
    CONSTRAINT fk_saves_user FOREIGN KEY (user_id) REFERENCES users (id)
) ENGINE = InnoDB
  DEFAULT CHARSET = utf8mb4
  COLLATE = utf8mb4_0900_ai_ci COMMENT ='云存档（每用户每槽位一份，覆盖式）';

-- 说明：
-- 1. 本表 DATETIME(6) 列（updated_at，含 users 的 created_at/token_at）存的是
--    **UTC 墙上时间**，前提是连接串带 connectionTimeZone=UTC（serverTimezone 的现名；
--    application-mysql.yml 已默认注入该驱动属性，启动自检 MysqlUtcSemanticsCheck
--    写→读回→比对，连接时区不是 UTC 时拒绝启动）。DATETIME 列不做时区转换，
--    java.time.Instant（实体 updatedAt，Hibernate 按 UTC 存取）与 DATETIME 之间的
--    换算全部发生在驱动侧——连接时区填错，历史行会整体平移一个时区差。
--    MySQL 的 TIMESTAMP 有 2038 上限并隐式做时区转换，故类型仍统一用 DATETIME(6)
--    （H2 侧自动建表为 TIMESTAMP，Instant 经 JDBC 同样按 UTC 解释，等价）。
-- 2. uk_saves_token 允许多个 NULL（SQL 标准行为），对应"未登录"状态。
-- 3. uk_saves_user_slot 是覆盖语义的落点：上传按它 INSERT-or-UPDATE，
--    同一用户同一槽位永远只有一条。
