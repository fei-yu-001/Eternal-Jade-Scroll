package com.twelvejade.server;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotEquals;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import com.twelvejade.server.common.AuthTokens;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.http.MediaType;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.test.web.servlet.MvcResult;

/**
 * 认证端点全流程（M6-02）：注册 / 登录 / 登出 / me，走真实 H2。
 *
 * <p>登录失败只给 40101、不区分"无此用户"与"密码错"——这条反枚举的纪律
 * 也在这里钉死（两种失败的 code 必须一致）。
 */
@SpringBootTest
@AutoConfigureMockMvc
class AuthFlowTest {

    @Autowired
    private MockMvc mockMvc;

    private static String body(String username, String password) {
        return "{\"username\":\"" + username + "\",\"password\":\"" + password + "\"}";
    }

    @Test
    @DisplayName("注册成功：返回 userId 与 36 字符 token")
    void registerReturnsUserIdAndToken() throws Exception {
        var session = TestSessions.registerNewUser(mockMvc);
        assertEquals(36, session.token().length());
    }

    @Test
    @DisplayName("重复注册同名用户被拒 409 / 40902")
    void duplicateUsernameRejected() throws Exception {
        String username = "dup-" + System.nanoTime();
        TestSessions.register(mockMvc, username, "pass1234");
        mockMvc.perform(post("/api/auth/register")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(body(username, "pass1234")))
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.code").value(40902));
    }

    @Test
    @DisplayName("密码错 → 401 / 40101")
    void wrongPasswordRejected() throws Exception {
        String username = "badpw-" + System.nanoTime();
        TestSessions.register(mockMvc, username, "pass1234");
        mockMvc.perform(post("/api/auth/login")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(body(username, "wrong-password")))
                .andExpect(status().isUnauthorized())
                .andExpect(jsonPath("$.code").value(40101));
    }

    @Test
    @DisplayName("不存在的用户登录同样是 401 / 40101（不泄露用户名存在性）")
    void unknownUserLoginSameCodeAsWrongPassword() throws Exception {
        mockMvc.perform(post("/api/auth/login")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(body("nobody-else-" + System.nanoTime(), "whatever123")))
                .andExpect(status().isUnauthorized())
                .andExpect(jsonPath("$.code").value(40101));
    }

    @Test
    @DisplayName("参数不合法（缺字段/超长/过短/含空白/不是对象）→ 400 / 40001")
    void registerParameterValidation() throws Exception {
        // 缺 username
        mockMvc.perform(post("/api/auth/register").contentType(MediaType.APPLICATION_JSON)
                        .content("{\"password\":\"pass1234\"}"))
                .andExpect(status().isBadRequest()).andExpect(jsonPath("$.code").value(40001));
        // username 过短（<3）
        mockMvc.perform(post("/api/auth/register").contentType(MediaType.APPLICATION_JSON)
                        .content(body("ab", "pass1234")))
                .andExpect(status().isBadRequest()).andExpect(jsonPath("$.code").value(40001));
        // username 超长（>32）
        mockMvc.perform(post("/api/auth/register").contentType(MediaType.APPLICATION_JSON)
                        .content(body("x".repeat(33), "pass1234")))
                .andExpect(status().isBadRequest()).andExpect(jsonPath("$.code").value(40001));
        // password 过短（<6）
        mockMvc.perform(post("/api/auth/register").contentType(MediaType.APPLICATION_JSON)
                        .content(body("ok-name", "p1")))
                .andExpect(status().isBadRequest()).andExpect(jsonPath("$.code").value(40001));
        // username 含空格
        mockMvc.perform(post("/api/auth/register").contentType(MediaType.APPLICATION_JSON)
                        .content(body("has space", "pass1234")))
                .andExpect(status().isBadRequest()).andExpect(jsonPath("$.code").value(40001));
        // password 含空格
        mockMvc.perform(post("/api/auth/register").contentType(MediaType.APPLICATION_JSON)
                        .content(body("ok-name", "pass 1234")))
                .andExpect(status().isBadRequest()).andExpect(jsonPath("$.code").value(40001));
        // 请求体不是 JSON 对象
        mockMvc.perform(post("/api/auth/register").contentType(MediaType.APPLICATION_JSON)
                        .content("[1,2,3]"))
                .andExpect(status().isBadRequest()).andExpect(jsonPath("$.code").value(40001));
        // 空对象（缺全部字段）
        mockMvc.perform(post("/api/auth/register").contentType(MediaType.APPLICATION_JSON)
                        .content("{}"))
                .andExpect(status().isBadRequest()).andExpect(jsonPath("$.code").value(40001));
    }

    @Test
    @DisplayName("登录换发新 token，旧 token 立即失效")
    void loginRotatesToken() throws Exception {
        String username = "rot-" + System.nanoTime();
        var first = TestSessions.register(mockMvc, username, "pass1234");
        var second = TestSessions.login(mockMvc, username, "pass1234");
        assertNotEquals(first.token(), second.token());
        mockMvc.perform(get("/api/me").header(AuthTokens.HEADER, first.token()))
                .andExpect(status().isUnauthorized())
                .andExpect(jsonPath("$.code").value(40102));
        mockMvc.perform(get("/api/me").header(AuthTokens.HEADER, second.token()))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value(0));
    }

    @Test
    @DisplayName("登出后旧 token 失效，账号仍可重新登录")
    void logoutInvalidatesToken() throws Exception {
        String username = "relogin-" + System.nanoTime();
        var session = TestSessions.register(mockMvc, username, "pass1234");
        mockMvc.perform(post("/api/auth/logout").header(AuthTokens.HEADER, session.token()))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value(0));
        mockMvc.perform(get("/api/me").header(AuthTokens.HEADER, session.token()))
                .andExpect(status().isUnauthorized())
                .andExpect(jsonPath("$.code").value(40102));
        // 账号还在：重新登录成功
        mockMvc.perform(post("/api/auth/login").contentType(MediaType.APPLICATION_JSON)
                        .content(body(username, "pass1234")))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value(0));
    }

    @Test
    @DisplayName("登出是幂等的：同一个 token 登出两次都返回 code 0")
    void logoutIsIdempotent() throws Exception {
        var session = TestSessions.registerNewUser(mockMvc);
        mockMvc.perform(post("/api/auth/logout").header(AuthTokens.HEADER, session.token()))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value(0));
        mockMvc.perform(post("/api/auth/logout").header(AuthTokens.HEADER, session.token()))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value(0));
    }

    @Test
    @DisplayName("GET /api/me 返回 userId 与 username")
    void meReturnsIdentity() throws Exception {
        var session = TestSessions.registerNewUser(mockMvc);
        mockMvc.perform(get("/api/me").header(AuthTokens.HEADER, session.token()))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value(0))
                .andExpect(jsonPath("$.data.userId").value(session.userId()))
                .andExpect(jsonPath("$.data.username").isNotEmpty());
    }

    @Test
    @DisplayName("GET /api/me 无 token / 未知 token → 401 / 40102")
    void meRejectsMissingOrUnknownToken() throws Exception {
        mockMvc.perform(get("/api/me"))
                .andExpect(status().isUnauthorized())
                .andExpect(jsonPath("$.code").value(40102));
        mockMvc.perform(get("/api/me").header(AuthTokens.HEADER, "no-such-token"))
                .andExpect(status().isUnauthorized())
                .andExpect(jsonPath("$.code").value(40102));
    }

    @Test
    @DisplayName("logout 无 token 头 → 401 / 40102")
    void logoutRejectsMissingToken() throws Exception {
        mockMvc.perform(post("/api/auth/logout"))
                .andExpect(status().isUnauthorized())
                .andExpect(jsonPath("$.code").value(40102));
    }
}
