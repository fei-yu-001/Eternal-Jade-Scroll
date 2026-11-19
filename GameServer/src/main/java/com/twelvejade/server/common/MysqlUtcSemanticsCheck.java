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
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
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

    private static final Logger log = LoggerFactory.getLogger(MysqlUtcSemanticsCheck.class);

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
        // 成功也要留下痕迹：verify-mysql.ps1 靠这一行确认自检在真库上真的跑过而不是被跳过。
        log.info("UTC semantics check PASSED: wrote {} (UTC wall time), DB literal read back {}",
                written, readBack);
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
