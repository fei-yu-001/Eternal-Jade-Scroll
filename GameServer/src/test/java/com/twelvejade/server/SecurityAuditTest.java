package com.twelvejade.server;

import static org.junit.jupiter.api.Assertions.assertTrue;

import com.twelvejade.server.common.PasswordConfig;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.util.List;
import java.util.regex.Pattern;
import java.util.stream.Collectors;
import java.util.stream.Stream;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;
import org.springframework.security.crypto.password.PasswordEncoder;

/**
 * W3-D 审计的可持续化：两件"现在是对的"的事钉成测试，防回归。
 *
 * <ul>
 *   <li>BCrypt 强度必须保持高位（实测单次验证耗时给出下限——强度 4 只需 1–3ms，
 *       强度 10 在现代机器上远高于 20ms，阈值留足余量不抖）；</li>
 *   <li>日志卫生：主源码里任何 log 调用的参数不得出现 password/token/payload 字样
 *       （当前全服务仅一条启动自检日志，审计确认无敏感参数；这道闸防将来引入）。</li>
 * </ul>
 */
class SecurityAuditTest {

    @Test
    @DisplayName("BCrypt 强度 10：单次验证耗时有下限（过低说明强度被调低）")
    void bcryptStrengthStaysHigh() {
        PasswordEncoder encoder = new PasswordConfig().passwordEncoder();
        String hash = encoder.encode("pass1234");
        long t0 = System.nanoTime();
        assertTrue(encoder.matches("pass1234", hash));
        long millis = (System.nanoTime() - t0) / 1_000_000;
        System.out.println("[SecurityAudit] BCrypt(10) 单次 matches 耗时 ≈ " + millis + " ms");
        assertTrue(millis >= 20,
                "BCrypt 单次验证仅 " + millis + "ms，强度疑似被调低（10 应远高于 20ms）");
    }

    @Test
    @DisplayName("日志卫生：主源码 log 调用参数不含 password/token/payload 字样")
    void logCallsDoNotMentionSecrets() throws IOException {
        Path mainSrc = Paths.get("src", "main", "java");
        Pattern logCall = Pattern.compile("\\blog\\.(info|debug|warn|error|trace)\\s*\\(");
        List<String> offenders;
        try (Stream<Path> walk = Files.walk(mainSrc)) {
            offenders = walk.filter(p -> p.toString().endsWith(".java"))
                    .flatMap(p -> {
                        try {
                            return Files.readAllLines(p, StandardCharsets.UTF_8).stream()
                                    .map(line -> p.getFileName() + ": " + line);
                        } catch (IOException e) {
                            return Stream.<String>empty();
                        }
                    })
                    .filter(line -> logCall.matcher(line).find())
                    .filter(line -> line.toLowerCase(java.util.Locale.ROOT)
                            .matches(".*\\b(password|token|payload)\\b.*"))
                    .collect(Collectors.toList());
        }
        assertTrue(offenders.isEmpty(),
                "log 调用参数疑似含敏感字段（password/token/payload），逐条人工复核：" + offenders);
    }
}
