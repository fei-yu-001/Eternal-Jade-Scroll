package com.twelvejade.server.auth;

import com.twelvejade.server.common.AuthTokens;
import java.time.Clock;
import java.time.LocalDateTime;
import java.util.Optional;
import org.springframework.dao.DataIntegrityViolationException;
import org.springframework.security.crypto.password.PasswordEncoder;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

/**
 * 注册 / 登录 / 令牌的业务逻辑（M6-02；M6-03-S 加固；M6-02-C 令牌逻辑收口到
 * {@link AuthTokens}）。
 *
 * <p>参数形状（长度、空白字符）由 Controller 校验；这里只管业务规则：
 * 用户名占用、凭证比对、令牌签发作废、过期判定、失败锁定。密码只存 BCrypt 哈希——
 * 明文不落库、不进日志、不进回执。
 */
@Service
public class UserService {

    /** 用户名已被占用。并发同名注册由 users.username 唯一约束兜底。 */
    public static final class UsernameTakenException extends RuntimeException {
        public UsernameTakenException(String username) {
            super("用户名已被占用：" + username);
        }
    }

    /** 账号或密码不对。不区分"无此用户"与"密码错"，避免泄露用户名存在性。 */
    public static final class BadCredentialsException extends RuntimeException {
        public BadCredentialsException() {
            super("账号或密码不对");
        }
    }

    /** 连续失败达到阈值，账号处于锁定期。 */
    public static final class LoginLockedException extends RuntimeException {
        public LoginLockedException() {
            super("登录失败次数过多，请稍后再试");
        }
    }

    private final UserRepository userRepository;
    private final PasswordEncoder passwordEncoder;
    private final LoginAttemptRegistry loginAttempts;
    private final Clock clock;

    public UserService(UserRepository userRepository, PasswordEncoder passwordEncoder,
            LoginAttemptRegistry loginAttempts, Clock clock) {
        this.userRepository = userRepository;
        this.passwordEncoder = passwordEncoder;
        this.loginAttempts = loginAttempts;
        this.clock = clock;
    }

    @Transactional
    public UserEntity register(String username, String password) {
        if (userRepository.findByUsername(username).isPresent()) {
            throw new UsernameTakenException(username);
        }
        var user = new UserEntity();
        user.setUsername(username);
        user.setPasswordHash(passwordEncoder.encode(password));
        user.setCreatedAt(now());
        AuthTokens.issue(user, clock); // 注册即登录：回执带令牌
        try {
            return userRepository.save(user);
        } catch (DataIntegrityViolationException e) {
            // 撞唯一约束 = 并发同名注册，对调用方而言就是"被占用"；事务随之回滚。
            throw new UsernameTakenException(username);
        }
    }

    @Transactional
    public UserEntity login(String username, String password) {
        if (loginAttempts.isLocked(username)) {
            throw new LoginLockedException(); // 锁定期内正确密码也拒绝，先于凭证比对
        }
        var user = userRepository.findByUsername(username).orElse(null);
        boolean passwordOk = user != null && passwordEncoder.matches(password, user.getPasswordHash());
        if (!passwordOk) {
            // 对不存在的用户名同样计数：锁定事实本身不泄露用户名是否存在。
            loginAttempts.recordFailure(username);
            throw new BadCredentialsException();
        }
        loginAttempts.recordSuccess(username);
        AuthTokens.issue(user, clock); // 每次登录换发新令牌，旧令牌作废
        return userRepository.save(user);
    }

    /**
     * 令牌刷新：有效且未过期 → 换发新令牌（旧的作废）；过期/无效 → empty。
     * 过期判定走 {@link #findByToken}，与 me/save 端点共用同一口径。
     */
    @Transactional
    public Optional<UserEntity> refresh(String token) {
        var found = findByToken(token);
        found.ifPresent(user -> {
            AuthTokens.issue(user, clock);
            userRepository.save(user);
        });
        return found;
    }

    /**
     * 改密码：旧密码比对通过 → 更新 BCrypt 哈希。当前令牌保留有效——
     * 客户端要强制重新登录时主动 logout（M6-02-C 规格修订；
     * M6-03-S 旧规格曾定为"作废当前令牌"，两轮任务书结论相反，以新为准并留痕）。
     * 旧密码错按凭证错误处理，不区分提示。
     */
    @Transactional
    public void changePassword(Long userId, String oldPassword, String newPassword) {
        var user = userRepository.findById(userId).orElseThrow(BadCredentialsException::new);
        if (!passwordEncoder.matches(oldPassword, user.getPasswordHash())) {
            throw new BadCredentialsException();
        }
        user.setPasswordHash(passwordEncoder.encode(newPassword));
        userRepository.save(user);
    }

    /**
     * 登出：把该令牌置 null。幂等——令牌已无效（查不到）也照样成功，
     * 因为"这个令牌不再可用"的目标状态已经成立。
     */
    @Transactional
    public void logout(String token) {
        userRepository.findByToken(token).ifPresent(user -> {
            user.setToken(null);
            user.setTokenAt(null);
            userRepository.save(user);
        });
    }

    /**
     * 按令牌找用户；null / 空白 / 未知 / 已过期一律 empty——调用方统一回 40102。
     * 有效性判定委托 {@link AuthTokens#isValid}（全服务唯一口径，
     * me / save / refresh / password 共用）。
     */
    @Transactional(readOnly = true)
    public Optional<UserEntity> findByToken(String token) {
        if (token == null || token.isBlank()) {
            return Optional.empty();
        }
        return userRepository.findByToken(token).filter(user -> AuthTokens.isValid(user, clock));
    }

    private LocalDateTime now() {
        return LocalDateTime.now(clock);
    }
}
