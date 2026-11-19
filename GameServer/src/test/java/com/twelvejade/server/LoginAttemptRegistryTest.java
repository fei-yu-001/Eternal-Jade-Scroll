package com.twelvejade.server;

import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.twelvejade.server.auth.LoginAttemptRegistry;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.time.ZoneId;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;

/**
 * 登录锁定的纯单元测试：用可拨动的测试时钟覆盖"锁定期自然解除"路径——
 * 这条在 MockMvc 集成测试里无法不 sleep 地验证。
 */
class LoginAttemptRegistryTest {

    /** 可手动拨动的时钟（测试专用）。 */
    private static final class AdjustableClock extends Clock {
        private Instant instant;

        AdjustableClock(Instant start) {
            this.instant = start;
        }

        void advanceBy(Duration d) {
            instant = instant.plus(d);
        }

        @Override
        public ZoneId getZone() {
            return ZoneId.systemDefault();
        }

        @Override
        public Clock withZone(ZoneId zone) {
            return this;
        }

        @Override
        public Instant instant() {
            return instant;
        }
    }

    @Test
    @DisplayName("连续失败 5 次上锁，锁 10 分钟后自然解除")
    void lockExpiresAfterDuration() {
        var clock = new AdjustableClock(Instant.parse("2026-10-05T00:00:00Z"));
        var registry = new LoginAttemptRegistry(clock);
        String username = "unit-lock";

        for (int i = 1; i <= 5; i++) {
            assertFalse(registry.isLocked(username), "第 " + i + " 次失败前不应已锁");
            registry.recordFailure(username);
        }
        assertTrue(registry.isLocked(username));

        clock.advanceBy(Duration.ofMinutes(9));
        assertTrue(registry.isLocked(username), "9 分钟后仍在锁定期");

        clock.advanceBy(Duration.ofMinutes(2));
        assertFalse(registry.isLocked(username), "11 分钟后应自然解锁");
    }

    @Test
    @DisplayName("成功登录清零：4 次失败 + 成功 + 4 次失败不触发锁定")
    void successResetsCount() {
        var clock = new AdjustableClock(Instant.parse("2026-10-05T00:00:00Z"));
        var registry = new LoginAttemptRegistry(clock);
        String username = "unit-clear";

        for (int i = 0; i < 4; i++) {
            registry.recordFailure(username);
        }
        registry.recordSuccess(username);
        for (int i = 0; i < 4; i++) {
            registry.recordFailure(username);
        }
        assertFalse(registry.isLocked(username), "清零后 4 连败不应锁定");
    }
}
