package com.twelvejade.server.common;

import java.time.Clock;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

/**
 * 全服务统一时钟（M6-03-S）。
 *
 * <p>凡是"现在几点"的判断（令牌过期、锁定倒计时……）只准注入这个 Clock 取，
 * 不准散落 {@code LocalDateTime.now()}——否则过期口径会出现第二个实现
 * （T022 同型教训：双口径 = 双真相），测试也无法在不 sleep 的情况下控制时间。
 */
@Configuration
public class TimeConfig {

    @Bean
    public Clock clock() {
        return Clock.systemDefaultZone();
    }
}
