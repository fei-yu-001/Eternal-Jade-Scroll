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
