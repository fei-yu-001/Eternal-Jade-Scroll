package com.twelvejade.server;

import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import com.twelvejade.server.common.AuthTokens;
import com.twelvejade.server.save.ClientSchema;
import java.time.Clock;
import java.time.Instant;
import java.time.ZoneOffset;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.test.context.TestConfiguration;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Primary;
import org.springframework.http.MediaType;
import org.springframework.test.web.servlet.MockMvc;

/**
 * updatedAt 时区契约（W-Server-Time）：upload 回执 / download / list 三个输出点的
 * 字符串必须自带 UTC 偏移，且同一秒内三处逐字一致。
 *
 * <p>用固定 Clock 注入（{@code @Primary} 覆盖 {@code TimeConfig} 的系统时钟），
 * 期望值逐字断言——回归成"无偏移的本地墙上时间"（ISO_LOCAL_DATE_TIME）时，
 * 第一条用例就红；不靠 sleep、不靠真实时间。
 */
@SpringBootTest
@AutoConfigureMockMvc
class SaveTimestampTest {

    /** 固定时刻（微秒精度，与落库精度对齐）：所有"现在"都是它，输出逐字可期。 */
    private static final Instant FIXED_INSTANT = Instant.parse("2026-10-05T09:32:19.123456Z");

    /** 固定时钟下，三个输出点必须逐字输出这个字符串。 */
    private static final String EXPECTED = "2026-10-05T09:32:19.123456Z";

    @TestConfiguration
    static class FixedClockConfig {

        @Bean
        @Primary
        Clock fixedClock() {
            return Clock.fixed(FIXED_INSTANT, ZoneOffset.UTC);
        }
    }

    @Autowired
    private MockMvc mockMvc;

    private String token;

    @BeforeEach
    void registerFreshUser() throws Exception {
        token = TestSessions.registerNewUser(mockMvc).token();
    }

    @Test
    @DisplayName("upload 回执的 updatedAt 逐字等于固定时刻（带 Z）")
    void uploadReceiptTimestampIsVerbatim() throws Exception {
        mockMvc.perform(post("/api/save/upload")
                        .header(AuthTokens.HEADER, token)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("{\"schemaVersion\":" + ClientSchema.SUPPORTED_SCHEMA
                                + ",\"slot\":1,\"save\":{\"coins\":10}}"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.updatedAt").value(EXPECTED));
    }

    @Test
    @DisplayName("download 的 updatedAt 与 upload 回执逐字一致")
    void downloadTimestampMatchesReceipt() throws Exception {
        uploadSlot1();
        mockMvc.perform(get("/api/save/download")
                        .header(AuthTokens.HEADER, token)
                        .param("slot", "1"))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.updatedAt").value(EXPECTED));
    }

    @Test
    @DisplayName("list 的 updatedAt 与 upload 回执逐字一致，空槽位不带时间")
    void listTimestampMatchesReceipt() throws Exception {
        uploadSlot1();
        mockMvc.perform(get("/api/save/list").header(AuthTokens.HEADER, token))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.slots[0].updatedAt").value(EXPECTED))
                .andExpect(jsonPath("$.data.slots[1].updatedAt").doesNotExist())
                .andExpect(jsonPath("$.data.slots[2].updatedAt").doesNotExist());
    }

    private void uploadSlot1() throws Exception {
        mockMvc.perform(post("/api/save/upload")
                        .header(AuthTokens.HEADER, token)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content("{\"schemaVersion\":" + ClientSchema.SUPPORTED_SCHEMA
                                + ",\"slot\":1,\"save\":{\"coins\":10}}"))
                .andExpect(status().isOk());
    }
}
