package com.twelvejade.server.common;

import com.twelvejade.server.auth.UserEntity;
import jakarta.servlet.http.HttpServletRequest;
import java.time.Clock;
import java.time.Duration;
import java.time.LocalDateTime;
import java.util.UUID;

/**
 * 登录令牌的唯一工具入口（M6-02-C 抽取）。
 *
 * <p>头名解析、有效期常量、有效性判定、新令牌签发——四件事只准住在这一处：
 * me / save / refresh / password 全部经由同一套判定，过期口径不存在第二份
 * （T022 同型教训：双口径 = 双真相）。时钟一律注入（{@link Clock}），
 * 测试可替换为固定/可拨时钟，禁止散落 {@code LocalDateTime.now()}。
 */
public final class AuthTokens {

    /** 登录令牌请求头。客户端登录/注册后把它原样放进每个需授权请求。 */
    public static final String HEADER = "X-Auth-Token";

    /** 令牌有效期：自签发（token_at）起算。过期与无效不区分，一律 40102。 */
    public static final Duration TOKEN_TTL = Duration.ofDays(7);

    private AuthTokens() {
    }

    /** 从请求里取令牌；没带该头时返回 null（调用方把 null 一律当"未登录"处理）。 */
    public static String from(HttpServletRequest request) {
        return request == null ? null : request.getHeader(HEADER);
    }

    /**
     * 令牌是否有效：token 非空、token_at 非空、距今不超过 {@link #TOKEN_TTL}。
     * me / save / refresh / password 共用——过期与无效一律 40102，不区分。
     */
    public static boolean isValid(UserEntity user, Clock clock) {
        if (user == null || user.getToken() == null || user.getToken().isBlank()) {
            return false;
        }
        var issuedAt = user.getTokenAt();
        return issuedAt != null && LocalDateTime.now(clock).isBefore(issuedAt.plus(TOKEN_TTL));
    }

    /**
     * 给用户签发新令牌（UUID）并把 token_at 拨到当前时间——旧令牌被覆盖即作废。
     * 调用方负责 save（本方法不碰数据库）。
     */
    public static void issue(UserEntity user, Clock clock) {
        user.setToken(UUID.randomUUID().toString());
        user.setTokenAt(LocalDateTime.now(clock));
    }
}
