package com.twelvejade.server.auth;

import java.time.Clock;
import java.time.Duration;
import java.time.LocalDateTime;
import java.util.concurrent.ConcurrentHashMap;
import org.springframework.stereotype.Component;

/**
 * 登录失败锁定（M6-03-S），内存实现。
 *
 * <p>同一用户名连续失败 5 次 → 锁 10 分钟，锁定期内正确密码也拒绝（防在线爆破）；
 * 成功登录清零计数。键是用户名字符串本身——对不存在的用户名同样计数，
 * 这样"被锁定"这一事实不泄露用户名是否存在。
 *
 * <p>内存实现是有意为之：H2 默认档重启即清零，锁状态跟着进程走是可接受的
 * （README 已写明）；上 Redis 属于还没用上的债。
 */
@Component
public class LoginAttemptRegistry {

    static final int MAX_FAILURES = 5;
    static final Duration LOCK_DURATION = Duration.ofMinutes(10);

    private record Attempt(int failures, LocalDateTime lockedUntil) {
    }

    private final Clock clock;
    private final ConcurrentHashMap<String, Attempt> attempts = new ConcurrentHashMap<>();

    public LoginAttemptRegistry(Clock clock) {
        this.clock = clock;
    }

    /** 该用户名是否处于锁定期。锁定期自然过后视为未锁。 */
    public boolean isLocked(String username) {
        var attempt = attempts.get(username);
        if (attempt == null || attempt.lockedUntil() == null) {
            return false;
        }
        if (LocalDateTime.now(clock).isAfter(attempt.lockedUntil())) {
            attempts.remove(username, attempt);
            return false;
        }
        return true;
    }

    /** 记一次失败；连续失败达到阈值即上锁。 */
    public void recordFailure(String username) {
        attempts.compute(username, (k, attempt) -> {
            int failures = (attempt == null ? 0 : attempt.failures()) + 1;
            LocalDateTime lockedUntil =
                    failures >= MAX_FAILURES ? LocalDateTime.now(clock).plus(LOCK_DURATION) : null;
            return new Attempt(failures, lockedUntil);
        });
    }

    /** 登录成功：该用户名的失败计数清零。 */
    public void recordSuccess(String username) {
        attempts.remove(username);
    }
}
