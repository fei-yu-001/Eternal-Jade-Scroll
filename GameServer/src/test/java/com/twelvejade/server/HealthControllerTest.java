package com.twelvejade.server;

import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import com.twelvejade.server.health.HealthController;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.webmvc.test.autoconfigure.WebMvcTest;
import org.springframework.test.web.servlet.MockMvc;

/** 探针：不连数据库就能回答"服务活着吗"。 */
@WebMvcTest(HealthController.class)
class HealthControllerTest {

    @Autowired
    private MockMvc mockMvc;

    @Test
    @DisplayName("健康探针返回 code 0 与 up")
    void healthIsUp() throws Exception {
        mockMvc.perform(get("/api/health"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value(0))
                .andExpect(jsonPath("$.data.status").value("up"))
                .andExpect(jsonPath("$.data.stage").value("M6-01-skeleton"));
    }

    @Test
    @DisplayName("探针失败时 data 不出现，而不是给出 null 字段")
    void shapeIsStable() throws Exception {
        mockMvc.perform(get("/api/health"))
                .andExpect(jsonPath("$.message").exists())
                .andExpect(jsonPath("$.code").isNumber());
    }
}
