package com.twelvejade.server.auth;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.GeneratedValue;
import jakarta.persistence.GenerationType;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import java.time.LocalDateTime;

/**
 * users 表实体：一个注册用户。表结构与 Database/SQL/V1__user_cloud_save.sql 一一对应。
 *
 * <p>密码只存 BCrypt 哈希（自带盐）；令牌是登录时签发的 UUID，null 表示未登录，
 * 每次登录换发新令牌、旧令牌作废——同一账号同时在多处登录时，最后登录的赢。
 */
@Entity
@Table(name = "users")
public class UserEntity {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    /** 3–32 字符，不含空白，全库唯一（区分大小写）。 */
    @Column(nullable = false, unique = true, length = 32)
    private String username;

    /** BCrypt 哈希（60 字符），明文不落库、不进日志、不进回执。 */
    @Column(name = "password_hash", nullable = false, length = 100)
    private String passwordHash;

    /** 登录令牌 UUID（定长 36 字符），null = 未登录。 */
    @Column(unique = true, columnDefinition = "CHAR(36)")
    private String token;

    @Column(name = "created_at", nullable = false)
    private LocalDateTime createdAt;

    /** 最近一次签发令牌的时间，用于将来做令牌过期。 */
    @Column(name = "token_at")
    private LocalDateTime tokenAt;

    public Long getId() {
        return id;
    }

    public String getUsername() {
        return username;
    }

    public void setUsername(String username) {
        this.username = username;
    }

    public String getPasswordHash() {
        return passwordHash;
    }

    public void setPasswordHash(String passwordHash) {
        this.passwordHash = passwordHash;
    }

    public String getToken() {
        return token;
    }

    public void setToken(String token) {
        this.token = token;
    }

    public LocalDateTime getCreatedAt() {
        return createdAt;
    }

    public void setCreatedAt(LocalDateTime createdAt) {
        this.createdAt = createdAt;
    }

    public LocalDateTime getTokenAt() {
        return tokenAt;
    }

    public void setTokenAt(LocalDateTime tokenAt) {
        this.tokenAt = tokenAt;
    }
}
