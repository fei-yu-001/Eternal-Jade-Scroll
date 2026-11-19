package com.twelvejade.server;

import static org.junit.jupiter.api.Assertions.assertNotEquals;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import com.twelvejade.server.auth.UserRepository;
import com.twelvejade.server.common.AuthTokens;
import java.time.Duration;
import java.time.LocalDateTime;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.http.MediaType;
import org.springframework.test.web.servlet.MockMvc;

/**
 * 令牌生命周期与账号加固（M6-03-S）：过期 / 刷新 / 改密 / 失败锁定。
 *
 * <p>过期测试不 sleep、不 mock 时钟——直接把库里的 token_at 拨回 8 天前
 * （Clock 是可注入 Bean，但改数据比换时钟便宜且同样确定）。
 */
@SpringBootTest
@AutoConfigureMockMvc
class AuthLifecycleTest {

    @Autowired
    private MockMvc mockMvc;

    @Autowired
    private UserRepository userRepository;

    private void ageTokenBeyondTtl(TestSessions.Session session) {
        var user = userRepository.findById((long) session.userId()).orElseThrow();
        user.setTokenAt(LocalDateTime.now().minus(Duration.ofDays(8)));
        userRepository.save(user);
    }

    @Test
    @DisplayName("refresh 换发新 token，旧 token 立即失效")
    void refreshRotatesToken() throws Exception {
        var first = TestSessions.registerNewUser(mockMvc);
        var refreshed = mockMvc.perform(post("/api/auth/refresh").header(AuthTokens.HEADER, first.token()))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value(0))
                .andExpect(jsonPath("$.data.userId").value(first.userId()))
                .andReturn();
        var data = TestSessions.dataOf(refreshed);
        String newToken = (String) data.get("token");
        assertNotEquals(first.token(), newToken);
        mockMvc.perform(get("/api/me").header(AuthTokens.HEADER, first.token()))
                .andExpect(status().isUnauthorized())
                .andExpect(jsonPath("$.code").value(40102));
        mockMvc.perform(get("/api/me").header(AuthTokens.HEADER, newToken))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value(0));
    }

    @Test
    @DisplayName("refresh 缺头 / 未知 token → 401 / 40102")
    void refreshRejectsMissingOrUnknownToken() throws Exception {
        mockMvc.perform(post("/api/auth/refresh"))
                .andExpect(status().isUnauthorized())
                .andExpect(jsonPath("$.code").value(40102));
        mockMvc.perform(post("/api/auth/refresh").header(AuthTokens.HEADER, "no-such-token"))
                .andExpect(status().isUnauthorized())
                .andExpect(jsonPath("$.code").value(40102));
    }

    @Test
    @DisplayName("过期 token 在 me 上失效（TTL=7 天，判定基准 token_at）")
    void expiredTokenRejectedOnMe() throws Exception {
        var session = TestSessions.registerNewUser(mockMvc);
        ageTokenBeyondTtl(session);
        mockMvc.perform(get("/api/me").header(AuthTokens.HEADER, session.token()))
                .andExpect(status().isUnauthorized())
                .andExpect(jsonPath("$.code").value(40102));
    }

    @Test
    @DisplayName("过期 token 不能 refresh（过期与无效不区分，都 40102）")
    void expiredTokenRejectedOnRefresh() throws Exception {
        var session = TestSessions.registerNewUser(mockMvc);
        ageTokenBeyondTtl(session);
        mockMvc.perform(post("/api/auth/refresh").header(AuthTokens.HEADER, session.token()))
                .andExpect(status().isUnauthorized())
                .andExpect(jsonPath("$.code").value(40102));
    }

    @Test
    @DisplayName("过期 token 在存档端点同样失效（me/save/refresh 共用同一判定）")
    void expiredTokenRejectedOnUpload() throws Exception {
        var session = TestSessions.registerNewUser(mockMvc);
        ageTokenBeyondTtl(session);
        mockMvc.perform(post("/api/save/upload")
                        .header(AuthTokens.HEADER, session.token())
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("{\"slot\":1,\"schemaVersion\":10,\"save\":{}}"))
                .andExpect(status().isUnauthorized())
                .andExpect(jsonPath("$.code").value(40102));
    }

    @Test
    @DisplayName("改密码成功：code 0、当前 token 保留有效、新密码可登录旧密码不行")
    void passwordChangeFlow() throws Exception {
        String username = "chpw-" + System.nanoTime();
        var session = TestSessions.register(mockMvc, username, "oldpass1");
        mockMvc.perform(post("/api/auth/password")
                        .header(AuthTokens.HEADER, session.token())
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("{\"oldPassword\":\"oldpass1\",\"newPassword\":\"newpass99\"}"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value(0));
        // M6-02-C 规格：改密后当前 token 继续有效（强制下线由客户端主动 logout）
        mockMvc.perform(get("/api/me").header(AuthTokens.HEADER, session.token()))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value(0));
        mockMvc.perform(post("/api/auth/login").contentType(MediaType.APPLICATION_JSON)
                        .content("{\"username\":\"" + username + "\",\"password\":\"newpass99\"}"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value(0));
        mockMvc.perform(post("/api/auth/login").contentType(MediaType.APPLICATION_JSON)
                        .content("{\"username\":\"" + username + "\",\"password\":\"oldpass1\"}"))
                .andExpect(status().isUnauthorized())
                .andExpect(jsonPath("$.code").value(40101));
    }

    @Test
    @DisplayName("新密码与旧密码相同 → 400 / 40001（防无意义改密）")
    void passwordChangeSameAsOldRejected() throws Exception {
        var session = TestSessions.registerNewUser(mockMvc);
        mockMvc.perform(post("/api/auth/password")
                        .header(AuthTokens.HEADER, session.token())
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("{\"oldPassword\":\"pass1234\",\"newPassword\":\"pass1234\"}"))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value(40001));
    }

    @Test
    @DisplayName("改密码旧密码错 → 401 / 40101")
    void passwordChangeWrongOldPassword() throws Exception {
        var session = TestSessions.registerNewUser(mockMvc);
        mockMvc.perform(post("/api/auth/password")
                        .header(AuthTokens.HEADER, session.token())
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("{\"oldPassword\":\"wrong-old\",\"newPassword\":\"newpass99\"}"))
                .andExpect(status().isUnauthorized())
                .andExpect(jsonPath("$.code").value(40101));
    }

    @Test
    @DisplayName("新密码形状同注册：过短 / 含空白 / 缺字段 → 400 / 40001")
    void passwordChangeValidation() throws Exception {
        var session = TestSessions.registerNewUser(mockMvc);
        mockMvc.perform(post("/api/auth/password")
                        .header(AuthTokens.HEADER, session.token())
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("{\"oldPassword\":\"pass1234\",\"newPassword\":\"p1\"}"))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value(40001));
        mockMvc.perform(post("/api/auth/password")
                        .header(AuthTokens.HEADER, session.token())
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("{\"oldPassword\":\"pass1234\",\"newPassword\":\"has space\"}"))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value(40001));
        mockMvc.perform(post("/api/auth/password")
                        .header(AuthTokens.HEADER, session.token())
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("{\"oldPassword\":\"pass1234\"}"))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value(40001));
    }

    @Test
    @DisplayName("改密码需有效 token：缺头 / 未知 → 401 / 40102")
    void passwordChangeRequiresValidToken() throws Exception {
        mockMvc.perform(post("/api/auth/password").contentType(MediaType.APPLICATION_JSON)
                        .content("{\"oldPassword\":\"a\",\"newPassword\":\"b\"}"))
                .andExpect(status().isUnauthorized())
                .andExpect(jsonPath("$.code").value(40102));
        mockMvc.perform(post("/api/auth/password").header(AuthTokens.HEADER, "no-such-token")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("{\"oldPassword\":\"a\",\"newPassword\":\"bbbbbb\"}"))
                .andExpect(status().isUnauthorized())
                .andExpect(jsonPath("$.code").value(40102));
    }

    @Test
    @DisplayName("同一用户名连续失败 5 次 → 锁定：正确密码也 429 / 40904")
    void loginLockedAfterFiveConsecutiveFailures() throws Exception {
        String username = "lock-" + System.nanoTime();
        TestSessions.register(mockMvc, username, "rightpass1");
        for (int i = 1; i <= 5; i++) {
            mockMvc.perform(post("/api/auth/login").contentType(MediaType.APPLICATION_JSON)
                            .content("{\"username\":\"" + username + "\",\"password\":\"wrong" + i + "\"}"))
                    .andExpect(status().isUnauthorized())
                    .andExpect(jsonPath("$.code").value(40101));
        }
        mockMvc.perform(post("/api/auth/login").contentType(MediaType.APPLICATION_JSON)
                        .content("{\"username\":\"" + username + "\",\"password\":\"rightpass1\"}"))
                .andExpect(status().is(429))
                .andExpect(jsonPath("$.code").value(40904));
    }

    @Test
    @DisplayName("锁定按用户名隔离：A 被锁不影响 B 登录")
    void lockIsPerUsername() throws Exception {
        String locked = "lockA-" + System.nanoTime();
        String other = "lockB-" + System.nanoTime();
        TestSessions.register(mockMvc, locked, "rightpass1");
        TestSessions.register(mockMvc, other, "rightpass1");
        for (int i = 1; i <= 5; i++) {
            mockMvc.perform(post("/api/auth/login").contentType(MediaType.APPLICATION_JSON)
                            .content("{\"username\":\"" + locked + "\",\"password\":\"wrong" + i + "\"}"))
                    .andExpect(status().isUnauthorized());
        }
        mockMvc.perform(post("/api/auth/login").contentType(MediaType.APPLICATION_JSON)
                        .content("{\"username\":\"" + other + "\",\"password\":\"rightpass1\"}"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value(0));
    }

    @Test
    @DisplayName("成功登录清零失败计数：先败 2 次、成功、再败 4 次仍未锁")
    void successClearsFailureCount() throws Exception {
        String username = "clear-" + System.nanoTime();
        TestSessions.register(mockMvc, username, "rightpass1");
        for (int i = 1; i <= 2; i++) {
            mockMvc.perform(post("/api/auth/login").contentType(MediaType.APPLICATION_JSON)
                            .content("{\"username\":\"" + username + "\",\"password\":\"wrong\"}"))
                    .andExpect(status().isUnauthorized());
        }
        TestSessions.login(mockMvc, username, "rightpass1");
        for (int i = 1; i <= 4; i++) {
            mockMvc.perform(post("/api/auth/login").contentType(MediaType.APPLICATION_JSON)
                            .content("{\"username\":\"" + username + "\",\"password\":\"wrong\"}"))
                    .andExpect(status().isUnauthorized())
                    .andExpect(jsonPath("$.code").value(40101)); // 40101 而非 40904 = 计数被清过
        }
    }

    @Test
    @DisplayName("不存在的用户名连续失败同样进入锁定（不泄露存在性）")
    void unknownUsernameAlsoLocked() throws Exception {
        String ghost = "ghost-" + System.nanoTime();
        for (int i = 1; i <= 5; i++) {
            mockMvc.perform(post("/api/auth/login").contentType(MediaType.APPLICATION_JSON)
                            .content("{\"username\":\"" + ghost + "\",\"password\":\"whatever\"}"))
                    .andExpect(status().isUnauthorized())
                    .andExpect(jsonPath("$.code").value(40101));
        }
        mockMvc.perform(post("/api/auth/login").contentType(MediaType.APPLICATION_JSON)
                        .content("{\"username\":\"" + ghost + "\",\"password\":\"whatever\"}"))
                .andExpect(status().is(429))
                .andExpect(jsonPath("$.code").value(40904));
    }
}
