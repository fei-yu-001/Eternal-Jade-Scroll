package com.twelvejade.server;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNotEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import com.twelvejade.server.common.AuthTokens;
import com.twelvejade.server.save.SaveController;
import com.twelvejade.server.save.SaveRepository;
import java.time.OffsetDateTime;
import java.time.ZoneOffset;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;
import java.util.concurrent.TimeUnit;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.dao.DataIntegrityViolationException;
import org.springframework.http.MediaType;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.test.web.servlet.MvcResult;
import tools.jackson.databind.json.JsonMapper;

/**
 * 上传路径健壮性现状钉测（T035 服务端侧 / W2-B）。
 *
 * <p>目标不是"全绿表演"，而是把现状逐条钉成可断言的事实：畸形输入走到哪、
 * 错误码与 HTTP 状态怎么配对、并发覆盖语义下最终库里是什么、客户端自带的
 * 时间戳字段会不会影响服务端生成的 updatedAt。观察式结论写进 W2-report.md。
 */
@SpringBootTest
@AutoConfigureMockMvc
class SaveUploadRobustnessTest {

    private static final JsonMapper MAPPER = JsonMapper.builder().build();

    @Autowired
    private MockMvc mockMvc;

    @Autowired
    private SaveRepository saveRepository;

    private static String body(String rawJson) {
        return rawJson;
    }

    private MvcResult uploadRaw(String token, String rawJson, int expectedStatus, int expectedCode)
            throws Exception {
        return mockMvc.perform(post("/api/save/upload")
                        .header(AuthTokens.HEADER, token)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(rawJson))
                .andExpect(status().is(expectedStatus))
                .andExpect(jsonPath("$.code").value(expectedCode))
                .andReturn();
    }

    private void assertSlotEmpty(String token, int slot) throws Exception {
        mockMvc.perform(get("/api/save/download")
                        .header(AuthTokens.HEADER, token)
                        .param("slot", String.valueOf(slot)))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value(0))
                .andExpect(jsonPath("$.data.present").value(false));
    }

    @Test
    @DisplayName("schemaVersion 是字符串（\"10\"）→ 400 / 40001，且绝不静默落库")
    void stringSchemaVersionIsRejected() throws Exception {
        var session = TestSessions.registerNewUser(mockMvc);
        uploadRaw(session.token(),
                body("{\"slot\":1,\"schemaVersion\":\"10\",\"save\":{\"coins\":1}}"), 400, 40001);
        assertSlotEmpty(session.token(), 1);
    }

    @Test
    @DisplayName("save 不是 JSON 对象（字符串/数组）→ 400 / 40001，且绝不静默落库")
    void saveNonObjectIsRejected() throws Exception {
        var session = TestSessions.registerNewUser(mockMvc);
        uploadRaw(session.token(),
                body("{\"slot\":1,\"schemaVersion\":10,\"save\":\"not-an-object\"}"), 400, 40001);
        uploadRaw(session.token(),
                body("{\"slot\":1,\"schemaVersion\":10,\"save\":[1,2,3]}"), 400, 40001);
        assertSlotEmpty(session.token(), 1);
    }

    @Test
    @DisplayName("HTTP 状态与业务 code 成对出现：200↔0、400↔40001（畸形输入逐条）")
    void errorCodeAndHttpStatusArePaired() throws Exception {
        var session = TestSessions.registerNewUser(mockMvc);
        // 成功路径：HTTP 200 且 code 恰为 0（不允许"200 但 code 非 0"）
        mockMvc.perform(post("/api/save/upload")
                        .header(AuthTokens.HEADER, session.token())
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(body("{\"slot\":1,\"schemaVersion\":10,\"save\":{\"coins\":1}}")))
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.code").value(0));
        // 失败路径：HTTP 4xx 且 body 里带着非零业务 code（不允许"4xx 但没有 code"）
        uploadRaw(session.token(), body("{\"slot\":1,\"save\":{\"coins\":1}}"), 400, 40001);
        uploadRaw(session.token(),
                body("{\"slot\":1,\"schemaVersion\":\"10\",\"save\":{}}"), 400, 40001);
        uploadRaw(session.token(),
                body("{\"slot\":1,\"schemaVersion\":10,\"save\":\"text\"}"), 400, 40001);
        uploadRaw(session.token(),
                body("{\"slot\":9,\"schemaVersion\":10,\"save\":{}}"), 400, 40001);
    }

    @Test
    @DisplayName("客户端在 save 里自带未来时间戳 → 只是被存的数据，updatedAt 仍由服务端生成")
    void clientSuppliedFutureTimestampDoesNotAffectUpdatedAt() throws Exception {
        var session = TestSessions.registerNewUser(mockMvc);
        var fakeFuture = OffsetDateTime.now(ZoneOffset.UTC).plusYears(10).toString();
        var receipt = TestSessions.dataOf(uploadRaw(session.token(),
                body("{\"slot\":1,\"schemaVersion\":10,\"save\":{\"coins\":1,\"clientTs\":\""
                        + fakeFuture + "\"}}"), 200, 0));

        // 回执 updatedAt 是服务端"现在"生成，绝无可能是客户端塞进来的那个未来值
        var receiptUpdatedAt = (String) receipt.get("updatedAt");
        assertNotEquals(fakeFuture, receiptUpdatedAt, "updatedAt 不能被客户端 payload 影响");
        assertTrue(OffsetDateTime.parse(receiptUpdatedAt)
                        .isBefore(OffsetDateTime.parse(fakeFuture)),
                "服务端生成的 updatedAt 应早于客户端伪造的未来时间");

        // 客户端字段原样往返（只存不读），而 updatedAt 与回执逐字一致
        var data = TestSessions.dataOf(mockMvc.perform(get("/api/save/download")
                        .header(AuthTokens.HEADER, session.token())
                        .param("slot", "1"))
                .andExpect(status().isOk())
                .andReturn());
        @SuppressWarnings("unchecked")
        var returned = (java.util.Map<String, Object>) data.get("save");
        assertEquals(fakeFuture, returned.get("clientTs"), "客户端字段只是数据，原样存取");
        assertEquals(receiptUpdatedAt, data.get("updatedAt"), "同一秒内回执与 download 同值");
    }

    @Test
    @DisplayName("并发上传同 (user, slot)：败者拿 200（重试成功）或 409/40903，绝不再拿 500；库里恒单行")
    void concurrentUploadsToSameSlotKeepSingleRow() throws Exception {
        var observedStatuses = new ArrayList<Integer>();
        int rounds = 6;
        for (int round = 0; round < rounds; round++) {
            var session = TestSessions.registerNewUser(mockMvc);
            var barrier = new CountDownLatch(1);
            var firstPayload = "{\"coins\":1,\"from\":\"first\"}";
            var secondPayload = "{\"coins\":2,\"from\":\"second\"}";
            ExecutorService executor = Executors.newFixedThreadPool(2);
            try {
                Future<String[]> first = executor.submit(
                        () -> uploadConcurrently(session.token(), barrier, firstPayload));
                Future<String[]> second = executor.submit(
                        () -> uploadConcurrently(session.token(), barrier, secondPayload));
                barrier.countDown();
                for (String[] r : new String[][] {first.get(30, TimeUnit.SECONDS),
                        second.get(30, TimeUnit.SECONDS)}) {
                    int httpStatus = Integer.parseInt(r[0]);
                    observedStatuses.add(httpStatus);
                    assertTrue(httpStatus == 200 || httpStatus == 409,
                            "败者绝不允许拿到 500（W3-A）：只允许 200（重试成功）或 409/40903，实际: "
                                    + httpStatus + " body=" + r[1]);
                }
            } finally {
                executor.shutdownNow();
            }

            // 不变量：恒为单行（唯一约束生效），内容是两份之一且完整（没有半条记录）
            var rows = saveRepository.findAllByUser_IdOrderBySlotAsc((long) session.userId());
            assertEquals(1, rows.size(),
                    "(user_id, slot) 唯一约束：并发后该用户该槽位只能有一条");
            var data = TestSessions.dataOf(mockMvc.perform(get("/api/save/download")
                            .header(AuthTokens.HEADER, session.token())
                            .param("slot", "1"))
                    .andExpect(status().isOk())
                    .andExpect(jsonPath("$.data.present").value(true))
                    .andReturn());
            @SuppressWarnings("unchecked")
            var returned = (java.util.Map<String, Object>) data.get("save");
            boolean isOneOfThem = returned.equals(MAPPER.readValue(firstPayload, java.util.Map.class))
                    || returned.equals(MAPPER.readValue(secondPayload, java.util.Map.class));
            assertTrue(isOneOfThem, "最终落库的必须是两份完整 payload 之一，实际: " + returned);
        }
        // 观察记录进标准输出，供 W3-report 引用（断言只钉不变量，不钉时序结果）
        System.out.println("[SaveUploadRobustness] 并发上传 " + rounds + " 轮 × 2 请求，"
                + "状态码分布: " + observedStatuses);
    }

    @Test
    @DisplayName("非唯一约束的完整性违例（payload NOT NULL）不被重试逻辑吞掉：照抛、不算槽位冲突、库不被静默改写")
    void nonUniqueIntegrityViolationIsNotSwallowedByRetry() throws Exception {
        var session = TestSessions.registerNewUser(mockMvc);
        uploadRaw(session.token(),
                body("{\"slot\":2,\"schemaVersion\":10,\"save\":{\"coins\":5}}"), 200, 0);

        var rows = saveRepository.findAllByUser_IdOrderBySlotAsc((long) session.userId());
        assertEquals(1, rows.size());
        var row = rows.get(0);
        row.setPayload(null); // 触发 payload NOT NULL（H2 SQLState 23502），与唯一键冲突（23505）是两回事

        var thrown = assertThrows(DataIntegrityViolationException.class,
                () -> saveRepository.save(row),
                "真故障必须照常抛出，绝不能被重试或兜底逻辑吞成静默");
        assertFalse(SaveController.isUniqueSlotConflict(thrown),
                "NOT NULL 违例不算唯一槽位冲突——upload 路径上它会原样上抛重试分支，而不是被当冲突重试");

        // 库中内容未被静默改写：payload 原文完好（失败的 save 已回滚）
        var after = saveRepository.findAllByUser_IdOrderBySlotAsc((long) session.userId());
        assertEquals(1, after.size());
        assertEquals("{\"coins\":5}", after.get(0).getPayload());
    }

    /** 并发任务：等栅栏放行后上传，返回 [httpStatus, 响应体原文]。 */
    private String[] uploadConcurrently(String token, CountDownLatch barrier, String payload)
            throws Exception {
        barrier.await(10, TimeUnit.SECONDS);
        MvcResult result = mockMvc.perform(post("/api/save/upload")
                        .header(AuthTokens.HEADER, token)
                        .contentType(MediaType.APPLICATION_JSON)
                        .content(body("{\"slot\":1,\"schemaVersion\":10,\"save\":" + payload + "}")))
                .andReturn();
        String responseBody = result.getResponse().getContentAsString();
        int httpStatus = result.getResponse().getStatus();
        // 409 的响应体必须沿用业务信封、带 code 40903——不许出现"4xx 但 body 没有 code"
        if (httpStatus == 409) {
            assertTrue(responseBody.contains("\"code\":40903"),
                    "409 响应必须带业务 code 40903，实际: " + responseBody);
            assertTrue(responseBody.contains("重试"),
                    "409 的 message 要给客户端可提示的处置建议，实际: " + responseBody);
        }
        return new String[] {String.valueOf(httpStatus), responseBody};
    }
}
