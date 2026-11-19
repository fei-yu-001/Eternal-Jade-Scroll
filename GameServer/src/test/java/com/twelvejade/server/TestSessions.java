package com.twelvejade.server;

import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import java.nio.charset.StandardCharsets;
import java.util.Map;
import java.util.UUID;
import org.springframework.http.MediaType;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.test.web.servlet.MvcResult;
import tools.jackson.core.type.TypeReference;
import tools.jackson.databind.json.JsonMapper;

/**
 * 测试夹具：注册 / 登录一个真实用户（走 MockMvc + H2 内存库），取回 userId 与 token。
 *
 * <p>H2 内存库在 Spring 上下文缓存期间一直活着，所以用户名必须每次唯一，
 * 否则同名注册会撞 40902。
 */
final class TestSessions {

    /** 一次登录态：userId + token。 */
    record Session(int userId, String token) {
    }

    private static final JsonMapper MAPPER = JsonMapper.builder().build();

    private static final TypeReference<Map<String, Object>> BODY_TYPE = new TypeReference<>() {
    };

    private TestSessions() {
    }

    static Session register(MockMvc mockMvc, String username, String password) throws Exception {
        var result = mockMvc.perform(post("/api/auth/register")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(MAPPER.writeValueAsString(Map.of("username", username, "password", password))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value(0))
                .andReturn();
        var data = dataOf(result);
        return new Session(((Number) data.get("userId")).intValue(), (String) data.get("token"));
    }

    static Session registerNewUser(MockMvc mockMvc) throws Exception {
        return register(mockMvc, "player-" + UUID.randomUUID().toString().substring(0, 8), "pass1234");
    }

    static Session login(MockMvc mockMvc, String username, String password) throws Exception {
        var result = mockMvc.perform(post("/api/auth/login")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(MAPPER.writeValueAsString(Map.of("username", username, "password", password))))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value(0))
                .andReturn();
        var data = dataOf(result);
        return new Session(((Number) data.get("userId")).intValue(), (String) data.get("token"));
    }

    /** 取回执里的 data 节点（Map 形式）。 */
    @SuppressWarnings("unchecked")
    static Map<String, Object> dataOf(MvcResult result) throws Exception {
        Map<String, Object> body = MAPPER.readValue(
                result.getResponse().getContentAsString(StandardCharsets.UTF_8), BODY_TYPE);
        return (Map<String, Object>) body.get("data");
    }
}
