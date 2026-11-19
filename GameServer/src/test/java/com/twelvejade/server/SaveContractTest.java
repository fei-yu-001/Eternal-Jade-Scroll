package com.twelvejade.server;

import static org.hamcrest.Matchers.matchesRegex;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import com.twelvejade.server.common.AuthTokens;
import com.twelvejade.server.save.ClientSchema;
import java.util.Map;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.http.MediaType;
import org.springframework.test.web.servlet.MockMvc;

/**
 * 存档契约：版本门禁与形状（M6-02 修订版）。
 *
 * <p>断言不只看"对不对"，还看 HTTP 状态与业务 code 是否一致——
 * 两者不一致时客户端要按哪个分支走会有歧义，那属于契约没定死。
 *
 * <p>相对 M6-01 的断言更新（本版起存档真正落库、必须登录）：
 * <ul>
 *   <li>upload / download 必须带 X-Auth-Token（无效 401 / 40102）——新增前置；</li>
 *   <li>upload 请求体改为 { slot, schemaVersion, save }，save 为必填 JSON 对象；</li>
 *   <li>当前版本上传的回执从 persisted=false 改为 persisted=true（落库即回执），
 *       其余字段与行为不变。</li>
 * </ul>
 * 场景一个不少：acceptsCurrentSchema / rejectsNewerSchema / acceptsOlderSchemaAsMigratable /
 * rejectsMissingSchemaVersion / rejectsNonObjectBody / downloadEmptySlotIsNotAnError /
 * downloadRejectsBadSlot。
 */
@SpringBootTest
@AutoConfigureMockMvc
class SaveContractTest {

    @Autowired
    private MockMvc mockMvc;

    private String token;
    private int userId;

    @BeforeEach
    void registerFreshUser() throws Exception {
        var session = TestSessions.registerNewUser(mockMvc);
        token = session.token();
        userId = session.userId();
    }

    @Test
    @DisplayName("当前版本的存档被收下且真正落库（persisted=true）")
    void acceptsCurrentSchema() throws Exception {
        mockMvc.perform(post("/api/save/upload")
                        .header(AuthTokens.HEADER, token)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("{\"schemaVersion\":" + ClientSchema.SUPPORTED_SCHEMA
                                + ",\"slot\":1,\"save\":{\"coins\":10}}"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value(0))
                .andExpect(jsonPath("$.data.slot").value(1))
                .andExpect(jsonPath("$.data.migratable").value(false))
                .andExpect(jsonPath("$.data.persisted").value(true))
                .andExpect(jsonPath("$.data.updatedAt")
                        // 必须是带偏移的 ISO8601：末尾 Z 或 ±HH:MM（裸墙上时间是回归）
                        .value(matchesRegex(".+?(Z|[+-]\\d{2}:\\d{2})$")));
    }

    @Test
    @DisplayName("比服务端新的存档被拒，且给出 40901")
    void rejectsNewerSchema() throws Exception {
        mockMvc.perform(post("/api/save/upload")
                        .header(AuthTokens.HEADER, token)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("{\"schemaVersion\":99,\"slot\":1,\"save\":{}}"))
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.code").value(40901));
    }

    @Test
    @DisplayName("比服务端老的存档收下并标记待迁移")
    void acceptsOlderSchemaAsMigratable() throws Exception {
        mockMvc.perform(post("/api/save/upload")
                        .header(AuthTokens.HEADER, token)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("{\"schemaVersion\":1,\"slot\":2,\"save\":{}}"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value(0))
                .andExpect(jsonPath("$.data.slot").value(2))
                .andExpect(jsonPath("$.data.migratable").value(true));
    }

    @Test
    @DisplayName("没有可读的 schemaVersion 视为请求不合法")
    void rejectsMissingSchemaVersion() throws Exception {
        mockMvc.perform(post("/api/save/upload")
                        .header(AuthTokens.HEADER, token)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("{\"slot\":1,\"save\":{}}"))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value(40001));
    }

    @Test
    @DisplayName("请求体不是 JSON 对象时走业务码，不抛 500")
    void rejectsNonObjectBody() throws Exception {
        mockMvc.perform(post("/api/save/upload")
                        .header(AuthTokens.HEADER, token)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("[1,2,3]"))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value(40001));
    }

    @Test
    @DisplayName("空槽位下载是正常状态：code 0 且 present 为 false")
    void downloadEmptySlotIsNotAnError() throws Exception {
        mockMvc.perform(get("/api/save/download")
                        .header(AuthTokens.HEADER, token)
                        .param("slot", "1"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value(0))
                .andExpect(jsonPath("$.data.slot").value(1))
                .andExpect(jsonPath("$.data.present").value(false))
                .andExpect(jsonPath("$.data.save").doesNotExist());
    }

    @Test
    @DisplayName("越界的槽位被拒（客户端只有三档）")
    void downloadRejectsBadSlot() throws Exception {
        mockMvc.perform(get("/api/save/download")
                        .header(AuthTokens.HEADER, token)
                        .param("slot", "9"))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value(40001));
        mockMvc.perform(get("/api/save/download")
                        .header(AuthTokens.HEADER, token)
                        .param("slot", "0"))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value(40001));
    }
}
