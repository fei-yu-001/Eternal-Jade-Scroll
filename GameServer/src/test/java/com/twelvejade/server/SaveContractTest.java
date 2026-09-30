package com.twelvejade.server;

import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import com.twelvejade.server.save.ClientSchema;
import com.twelvejade.server.save.SaveController;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.webmvc.test.autoconfigure.WebMvcTest;
import org.springframework.http.MediaType;
import org.springframework.test.web.servlet.MockMvc;

/**
 * 存档契约：版本校验是这一版唯一的业务规则，也是最该被钉死的一条。
 *
 * <p>断言不只看"对不对"，还看 HTTP 状态与业务 code 是否一致——
 * 两者不一致时客户端要按哪个分支走会有歧义，那属于契约没定死。
 */
@WebMvcTest(SaveController.class)
class SaveContractTest {

    @Autowired
    private MockMvc mockMvc;

    @Test
    @DisplayName("当前版本的存档被收下，但明确没有落库")
    void acceptsCurrentSchema() throws Exception {
        mockMvc.perform(post("/api/save/upload")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("{\"schemaVersion\":" + ClientSchema.SUPPORTED_SCHEMA + ",\"slot\":1}"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value(0))
                .andExpect(jsonPath("$.data.slot").value(1))
                .andExpect(jsonPath("$.data.migratable").value(false))
                .andExpect(jsonPath("$.data.persisted").value(false));
    }

    @Test
    @DisplayName("比服务端新的存档被拒，且给出 40901")
    void rejectsNewerSchema() throws Exception {
        mockMvc.perform(post("/api/save/upload")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("{\"schemaVersion\":99,\"slot\":1}"))
                .andExpect(status().isConflict())
                .andExpect(jsonPath("$.code").value(40901));
    }

    @Test
    @DisplayName("比服务端老的存档收下并标记待迁移")
    void acceptsOlderSchemaAsMigratable() throws Exception {
        mockMvc.perform(post("/api/save/upload")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("{\"schemaVersion\":1,\"slot\":2}"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value(0))
                .andExpect(jsonPath("$.data.slot").value(2))
                .andExpect(jsonPath("$.data.migratable").value(true));
    }

    @Test
    @DisplayName("没有可读的 schemaVersion 视为请求不合法")
    void rejectsMissingSchemaVersion() throws Exception {
        mockMvc.perform(post("/api/save/upload")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("{\"slot\":1}"))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value(40001));
    }

    @Test
    @DisplayName("请求体不是 JSON 对象时走业务码，不抛 500")
    void rejectsNonObjectBody() throws Exception {
        mockMvc.perform(post("/api/save/upload")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("[1,2,3]"))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value(40001));
    }

    @Test
    @DisplayName("空槽位下载是正常状态：code 0 且 present 为 false")
    void downloadEmptySlotIsNotAnError() throws Exception {
        mockMvc.perform(get("/api/save/download").param("slot", "1"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value(0))
                .andExpect(jsonPath("$.data.slot").value(1))
                .andExpect(jsonPath("$.data.present").value(false))
                .andExpect(jsonPath("$.data.save").doesNotExist());
    }

    @Test
    @DisplayName("越界的槽位被拒（客户端只有三档）")
    void downloadRejectsBadSlot() throws Exception {
        mockMvc.perform(get("/api/save/download").param("slot", "9"))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value(40001));
        mockMvc.perform(get("/api/save/download").param("slot", "0"))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value(40001));
    }
}
