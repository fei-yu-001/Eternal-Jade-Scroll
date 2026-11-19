package com.twelvejade.server;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.twelvejade.server.auth.UserEntity;
import com.twelvejade.server.auth.UserRepository;
import com.twelvejade.server.save.SaveEntity;
import com.twelvejade.server.save.SaveRepository;
import com.twelvejade.server.tools.SaveSlotExporter;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.List;
import java.util.Optional;
import javax.sql.DataSource;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.security.crypto.password.PasswordEncoder;

/**
 * W3-E 导出工具的两条硬要求：
 * 1. 导出的 payload 与 sha256 校验行自洽（校验行可用独立 MessageDigest 重算复现）；
 * 2. 工具运行前后数据库内容逐字不变——"只读"不是注释里说说，是断言钉死的。
 */
@SpringBootTest
class SaveSlotExporterTest {

    @Autowired
    private DataSource dataSource;

    @Autowired
    private SaveRepository saveRepository;

    @Autowired
    private UserRepository userRepository;

    @Autowired
    private PasswordEncoder passwordEncoder;

    private long userId;
    private List<SaveEntity> before;

    @BeforeEach
    void seedOneSave() {
        var user = new UserEntity();
        user.setUsername("w3exporter-" + System.nanoTime());
        user.setPasswordHash(passwordEncoder.encode("pass1234"));
        user.setCreatedAt(java.time.LocalDateTime.now());
        user = userRepository.save(user);
        userId = user.getId();

        var entity = new SaveEntity();
        entity.setUser(user);
        entity.setSlot((byte) 2);
        entity.setSchemaVersion(10);
        entity.setPayload("{\"coins\":777,\"from\":\"w3-exporter\"}");
        entity.setUpdatedAt(java.time.Instant.now().truncatedTo(java.time.temporal.ChronoUnit.MICROS));
        saveRepository.save(entity);

        before = saveRepository.findAllByUser_IdOrderBySlotAsc(userId);
        assertEquals(1, before.size());
    }

    @Test
    @DisplayName("导出 payload 与独立重算的 sha256 一致，且库内容逐字未变（只读证明）")
    void exportIsConsistentAndReadOnly() throws Exception {
        try (var connection = dataSource.getConnection()) {
            long resolvedId = SaveSlotExporter.resolveUserId(connection,
                    userRepository.findById(userId).orElseThrow().getUsername());
            assertEquals(userId, resolvedId, "用户名 → id 解析正确");

            Optional<String> payload = SaveSlotExporter.loadPayload(connection, userId, 2);
            assertTrue(payload.isPresent(), "槽位 2 的存档应可导出");
            assertEquals("{\"coins\":777,\"from\":\"w3-exporter\"}", payload.get(),
                    "导出内容必须与库中 payload 逐字一致");

            // 独立重算 sha256（不复用工具实现），验证输出行可被第三方复核
            byte[] hash = MessageDigest.getInstance("SHA-256")
                    .digest(payload.get().getBytes(StandardCharsets.UTF_8));
            StringBuilder expectedHex = new StringBuilder();
            for (byte b : hash) {
                expectedHex.append(String.format("%02x", b));
            }
            assertEquals(expectedHex.toString(), SaveSlotExporter.sha256Hex(payload.get()),
                    "工具输出的 sha256 行必须等于 payload 内容的独立重算值");

            assertTrue(SaveSlotExporter.loadPayload(connection, userId, 1).isEmpty(),
                    "空槽位导出为 empty（present 语义与 API 一致）");
        }

        // 只读证明：导出动作之后，库中该用户全部存档逐字未变
        var after = saveRepository.findAllByUser_IdOrderBySlotAsc(userId);
        assertEquals(before.size(), after.size(), "行数不变");
        for (int i = 0; i < before.size(); i++) {
            assertEquals(before.get(i).getPayload(), after.get(i).getPayload(), "payload 逐字不变");
            assertEquals(before.get(i).getUpdatedAt(), after.get(i).getUpdatedAt(), "updatedAt 不变");
        }
    }
}
