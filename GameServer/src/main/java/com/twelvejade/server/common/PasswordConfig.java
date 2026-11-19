package com.twelvejade.server.common;

import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.security.crypto.bcrypt.BCryptPasswordEncoder;
import org.springframework.security.crypto.password.PasswordEncoder;

/**
 * 密码哈希配置（M6-02）。
 *
 * <p>只引 {@code spring-security-crypto} 单件：要的只是 BCrypt 算法本身，
 * 不引 Spring Security 过滤器链——我们的授权模型是自管的 X-Auth-Token 令牌，
 * 引全家桶等于背上一套没用上的安全框架。
 */
@Configuration
public class PasswordConfig {

    /**
     * BCrypt 自带盐，哈希定长 60 字符，落在 users.password_hash 的 VARCHAR(100) 里。
     * 强度 10 显式声明（W3-D 审计）：无参构造当前也恰好是 10，但显式写出防止依赖库
     * 默认值将来漂移——这不是性能参数，是安全参数，必须写死在仓库里。
     */
    @Bean
    public PasswordEncoder passwordEncoder() {
        return new BCryptPasswordEncoder(10);
    }
}
