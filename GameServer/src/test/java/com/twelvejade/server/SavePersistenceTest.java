package com.twelvejade.server;

import static org.hamcrest.Matchers.matchesRegex;
import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertDoesNotThrow;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import com.twelvejade.server.common.AuthTokens;
import com.twelvejade.server.save.SaveRepository;
import java.time.OffsetDateTime;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.http.MediaType;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.test.web.servlet.MvcResult;
import tools.jackson.databind.json.JsonMapper;

/**
 * 云存档落库行为（M6-02）：令牌门禁、payload 原文往返、覆盖上传、槽位校验、list。
 *
 * <p>测试里故意放一个 {@code memo: null} 字段——服务端存 payload 必须用
 * 不带 non_null 的专用 mapper，null 字段被吞掉这条用例第一个不答应。
 */
@SpringBootTest
@AutoConfigureMockMvc
class SavePersistenceTest {

    private static final JsonMapper MAPPER = JsonMapper.builder().build();

    @Autowired
    private MockMvc mockMvc;

    @Autowired
    private SaveRepository saveRepository;

    private static String uploadBody(int slot, int schemaVersion, Map<String, Object> save) {
        var body = new LinkedHashMap<String, Object>();
        body.put("slot", slot);
        body.put("schemaVersion", schemaVersion);
        body.put("save", save);
        return MAPPER.writeValueAsString(body);
    }

    private MvcResult upload(TestSessions.Session session, int slot, int schemaVersion,
            Map<String, Object> save, int expectedStatus, int expectedCode) throws Exception {
        return mockMvc.perform(post("/api/save/upload")
                        .header(AuthTokens.HEADER, session.token())
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(uploadBody(slot, schemaVersion, save)))
                .andExpect(status().is(expectedStatus))
                .andExpect(jsonPath("$.code").value(expectedCode))
                .andReturn();
    }

    private MvcResult download(TestSessions.Session session, int slot, int expectedStatus) throws Exception {
        return mockMvc.perform(get("/api/save/download")
                        .header(AuthTokens.HEADER, session.token())
                        .param("slot", String.valueOf(slot)))
                .andExpect(status().is(expectedStatus))
                .andReturn();
    }

    @Test
    @DisplayName("无 token 上传 → 401 / 40102")
    void uploadWithoutTokenRejected() throws Exception {
        mockMvc.perform(post("/api/save/upload")
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(uploadBody(1, 10, Map.of("coins", 1))))
                .andExpect(status().isUnauthorized())
                .andExpect(jsonPath("$.code").value(40102));
    }

    @Test
    @DisplayName("无效 token 下载 → 401 / 40102")
    void downloadWithUnknownTokenRejected() throws Exception {
        mockMvc.perform(get("/api/save/download")
                        .header(AuthTokens.HEADER, "no-such-token")
                        .param("slot", "1"))
                .andExpect(status().isUnauthorized())
                .andExpect(jsonPath("$.code").value(40102));
    }

    @Test
    @DisplayName("上传后下载取回 payload 逐字一致（含 null 字段）")
    void downloadReturnsPayloadVerbatim() throws Exception {
        var session = TestSessions.registerNewUser(mockMvc);
        var save = new LinkedHashMap<String, Object>();
        save.put("coins", 300);
        save.put("bag", List.of(Map.of("id", "herb", "count", 2)));
        save.put("memo", null);
        upload(session, 1, 10, save, 200, 0);

        var data = TestSessions.dataOf(download(session, 1, 200));
        assertEquals(true, data.get("present"));
        assertEquals(10, ((Number) data.get("schemaVersion")).intValue());
        @SuppressWarnings("unchecked")
        var returned = (Map<String, Object>) data.get("save");
        assertEquals(save, returned);
    }

    @Test
    @DisplayName("覆盖上传同槽位：不产生第二条记录，取回的是最后一份")
    void overwriteSameSlotKeepsSingleRow() throws Exception {
        var session = TestSessions.registerNewUser(mockMvc);
        upload(session, 1, 10, Map.of("coins", 100), 200, 0);
        upload(session, 1, 9, Map.of("coins", 200, "from", "second"), 200, 0);

        var data = TestSessions.dataOf(download(session, 1, 200));
        assertEquals(9, ((Number) data.get("schemaVersion")).intValue());
        @SuppressWarnings("unchecked")
        var returned = (Map<String, Object>) data.get("save");
        assertEquals(Map.of("coins", 200, "from", "second"), returned);

        // (user_id, slot) 唯一：该用户在库里只应有这一条存档
        var rows = saveRepository.findAllByUser_IdOrderBySlotAsc((long) session.userId());
        assertEquals(1, rows.size());
    }

    @Test
    @DisplayName("槽位越界（0 与 4）→ 400 / 40001")
    void uploadRejectsOutOfRangeSlot() throws Exception {
        var session = TestSessions.registerNewUser(mockMvc);
        upload(session, 0, 10, Map.of("coins", 1), 400, 40001);
        upload(session, 4, 10, Map.of("coins", 1), 400, 40001);
    }

    @Test
    @DisplayName("schemaVersion 超限仍 409 / 40901（M6-01 门禁一行不改）")
    void uploadStillRejectsTooNewSchema() throws Exception {
        var session = TestSessions.registerNewUser(mockMvc);
        upload(session, 1, 99, Map.of("coins", 1), 409, 40901);
    }

    @Test
    @DisplayName("list 固定返回三槽位，present 与 updatedAt 正确")
    void listReturnsFixedThreeSlots() throws Exception {
        var session = TestSessions.registerNewUser(mockMvc);
        mockMvc.perform(get("/api/save/list").header(AuthTokens.HEADER, session.token()))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value(0))
                .andExpect(jsonPath("$.data.slots.length()").value(3))
                .andExpect(jsonPath("$.data.slots[0].slot").value(1))
                .andExpect(jsonPath("$.data.slots[0].present").value(false))
                .andExpect(jsonPath("$.data.slots[1].slot").value(2))
                .andExpect(jsonPath("$.data.slots[1].present").value(false))
                .andExpect(jsonPath("$.data.slots[2].slot").value(3))
                .andExpect(jsonPath("$.data.slots[2].present").value(false));

        upload(session, 2, 10, Map.of("coins", 5), 200, 0);
        mockMvc.perform(get("/api/save/list").header(AuthTokens.HEADER, session.token()))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.data.slots[1].present").value(true))
                .andExpect(jsonPath("$.data.slots[1].updatedAt")
                        // 必须是带偏移的 ISO8601：末尾 Z 或 ±HH:MM（裸墙上时间是回归）
                        .value(matchesRegex(".+?(Z|[+-]\\d{2}:\\d{2})$")))
                .andExpect(jsonPath("$.data.slots[0].present").value(false))
                .andExpect(jsonPath("$.data.slots[2].present").value(false));
    }

    @Test
    @DisplayName("payload 超 256KB → 400 / 40001，且不落库")
    void uploadOverLimitRejected() throws Exception {
        var session = TestSessions.registerNewUser(mockMvc);
        // 300KB 的 blob：超过 MAX_PAYLOAD_BYTES(256*1024)，UTF-8 单字节字符按 1:1 计
        var oversized = new LinkedHashMap<String, Object>();
        oversized.put("blob", "x".repeat(300 * 1024));
        mockMvc.perform(post("/api/save/upload")
                        .header(AuthTokens.HEADER, session.token())
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(uploadBody(1, 10, oversized)))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.code").value(40001));

        var data = TestSessions.dataOf(download(session, 1, 200));
        assertEquals(false, data.get("present"));
    }

    @Test
    @DisplayName("payload 贴着上限以内（200KB）正常收下并可取回")
    void uploadJustUnderLimitAccepted() throws Exception {
        var session = TestSessions.registerNewUser(mockMvc);
        var save = new LinkedHashMap<String, Object>();
        save.put("blob", "y".repeat(200 * 1024));
        upload(session, 2, 10, save, 200, 0);

        var data = TestSessions.dataOf(download(session, 2, 200));
        assertEquals(true, data.get("present"));
        @SuppressWarnings("unchecked")
        var returned = (Map<String, Object>) data.get("save");
        assertEquals(save, returned);
    }

    @Test
    @DisplayName("migratable 标记随落库保留：老版本存档上传与下载都带出")
    void migratableSurvivesRoundTrip() throws Exception {
        var session = TestSessions.registerNewUser(mockMvc);
        var receipt = TestSessions.dataOf(upload(session, 3, 9, Map.of("coins", 1), 200, 0));
        assertEquals(true, receipt.get("migratable"));
        assertEquals(true, receipt.get("persisted"));

        var data = TestSessions.dataOf(download(session, 3, 200));
        assertEquals(9, ((Number) data.get("schemaVersion")).intValue());
        assertEquals(true, data.get("present"));
        // updatedAt 必须能按"带偏移的时间"解析（裸墙上时间在此直接抛异常），且
        // 同一秒内 upload 回执与 download 给出同一个值。
        var receiptUpdatedAt = (String) receipt.get("updatedAt");
        var downloadUpdatedAt = (String) data.get("updatedAt");
        assertDoesNotThrow(() -> OffsetDateTime.parse(receiptUpdatedAt),
                "updatedAt 应为带偏移的 ISO8601，实际: " + receiptUpdatedAt);
        assertDoesNotThrow(() -> OffsetDateTime.parse(downloadUpdatedAt),
                "updatedAt 应为带偏移的 ISO8601，实际: " + downloadUpdatedAt);
        assertEquals(receiptUpdatedAt, downloadUpdatedAt);
    }
}
