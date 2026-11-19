package com.twelvejade.server;

import static org.junit.jupiter.api.Assertions.assertTrue;

import com.twelvejade.server.common.ApiCode;
import com.twelvejade.server.save.ClientSchema;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.regex.Matcher;
import java.util.regex.Pattern;
import java.util.stream.Collectors;
import java.util.stream.Stream;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;

/**
 * API 契约三方一致性（W3-C）：{@code ApiCode} 里的每个码必须有生产者（方向 A）、
 * 必须被至少一个测试断言命中（方向 B）、必须出现在 {@code docs/API.md} 且文档里
 * 不多出 ApiCode 之外的码（方向 C）。
 *
 * <p>动机：40903 预留了很久直到 W3-A 才第一次有生产者——没有这道闸，
 * 契约只活在文档里，"写了但没人调用"的错误会一再重演。
 * 扫描按字面量进行（生产者查 {@code ApiCode.XXX} 引用，测试与文档查数字字面量），
 * 宽于语义但只松不紧：宁可漏报也不误伤真实生产者。
 */
class ApiContractCoverageTest {

    private static final Path MAIN_SRC = Paths.get("src", "main", "java");
    private static final Path TEST_SRC = Paths.get("src", "test", "java");
    private static final Path API_DOC = Paths.get("docs", "API.md");

    private static String read(Path p) throws IOException {
        return Files.readString(p, StandardCharsets.UTF_8);
    }

    private static List<Path> javaFilesUnder(Path root) throws IOException {
        try (Stream<Path> walk = Files.walk(root)) {
            return walk.filter(p -> p.toString().endsWith(".java")).collect(Collectors.toList());
        }
    }

    /**
     * 显式豁免清单：扫描确认"预留但尚无生产者"的码在这里挂牌示众（测试输出可见），
     * 出现任何**清单之外**的死码仍然红。INTERNAL_ERROR 的全局兜底 handler 待架构师排期
     * （见 W3 报告"待排期"），排期落地后应把它的名字从这里删掉——豁免不是永久席。
     */
    private static final List<String> RESERVED_WITHOUT_PRODUCER = List.of("INTERNAL_ERROR(50001)");

    @Test
    @DisplayName("方向 A：每个错误码在主源码至少有一处生产者（ApiCode.XXX 引用形态）；预留死码须在豁免清单挂牌")
    void everyCodeHasAProducer() throws IOException {
        List<String> dead = new ArrayList<>();
        for (ApiCode code : ApiCode.values()) {
            if (code == ApiCode.OK) {
                continue; // 0 是成功路径，由 ApiResponse.ok 统一生产，不适用错误码生产者扫描
            }
            String needle = "ApiCode." + code.name();
            boolean produced = false;
            for (Path p : javaFilesUnder(MAIN_SRC)) {
                if (p.getFileName().toString().equals("ApiCode.java")) {
                    continue; // 常量定义处不算生产者
                }
                if (read(p).contains(needle)) {
                    produced = true;
                    break;
                }
            }
            if (!produced) {
                dead.add(code.name() + "(" + code.code() + ")");
            }
        }
        List<String> unexpectedDead = new ArrayList<>(dead);
        unexpectedDead.removeAll(RESERVED_WITHOUT_PRODUCER);
        if (!dead.isEmpty()) {
            System.out.println("[ApiContractCoverage] 无生产者的预留码（已挂牌，等待架构师排期）: " + dead);
        }
        assertTrue(unexpectedDead.isEmpty(),
                "发现豁免清单之外的无生产者死码：" + unexpectedDead
                        + " —— 请补生产者；若确属预留，先在 RESERVED_WITHOUT_PRODUCER 挂牌并在报告里写明理由");
    }

    @Test
    @DisplayName("方向 B：每个码至少被一个测试断言命中（src/test 中的数字字面量）；预留码在豁免清单挂牌")
    void everyCodeIsAssertedInTests() throws IOException {
        List<String> uncovered = new ArrayList<>();
        for (ApiCode code : ApiCode.values()) {
            String literal = String.valueOf(code.code());
            boolean hit = false;
            for (Path p : javaFilesUnder(TEST_SRC)) {
                if (read(p).contains(literal)) {
                    hit = true;
                    break;
                }
            }
            if (!hit) {
                uncovered.add(literal);
            }
        }
        List<String> unexpected = new ArrayList<>(uncovered);
        // 预留码没有生产者自然也没有测试断言它（50001，见 RESERVED_WITHOUT_PRODUCER 的说明）
        unexpected.removeAll(RESERVED_WITHOUT_PRODUCER.stream()
                .map(s -> s.substring(s.indexOf('(') + 1, s.length() - 1))
                .collect(Collectors.toList()));
        if (!uncovered.isEmpty()) {
            System.out.println("[ApiContractCoverage] 无测试断言的预留码（已挂牌）: " + uncovered);
        }
        assertTrue(unexpected.isEmpty(),
                "以下码在 src/test 无任何字面量命中（没有测试断言过它们）：" + unexpected);
    }

    @Test
    @DisplayName("方向 C：docs/API.md 含每个码，且文档中的 5 位码都在 ApiCode 里（多一个少一个都红）")
    void apiDocAndApiCodeAgreeBothWays() throws IOException {
        assertTrue(Files.exists(API_DOC), "缺少 " + API_DOC + "，API 契约文档必须存在");
        String doc = read(API_DOC);

        List<String> missingInDoc = new ArrayList<>();
        for (ApiCode code : ApiCode.values()) {
            if (!doc.contains(String.valueOf(code.code()))) {
                missingInDoc.add(String.valueOf(code.code()));
            }
        }
        assertTrue(missingInDoc.isEmpty(), "docs/API.md 缺少以下码：" + missingInDoc);

        var known = new HashSet<Integer>();
        for (ApiCode code : ApiCode.values()) {
            known.add(code.code());
        }
        List<String> unknownInDoc = new ArrayList<>();
        Matcher m = Pattern.compile("\\b\\d{5}\\b").matcher(doc);
        while (m.find()) {
            String token = m.group();
            if (!known.contains(Integer.parseInt(token)) && !unknownInDoc.contains(token)) {
                unknownInDoc.add(token);
            }
        }
        assertTrue(unknownInDoc.isEmpty(),
                "docs/API.md 出现了 ApiCode 中不存在的 5 位码：" + unknownInDoc);
    }

    @Test
    @DisplayName("方向 D：docs/API.md 的 schema 版本字样与 ClientSchema.SUPPORTED_SCHEMA 一致（文档漂移即红）")
    void apiDocSchemaVersionMatchesClientSchema() throws IOException {
        assertTrue(Files.exists(API_DOC), "缺少 " + API_DOC + "，API 契约文档必须存在");
        String doc = read(API_DOC);
        int supported = ClientSchema.SUPPORTED_SCHEMA;
        assertTrue(doc.contains("> " + supported),
                "docs/API.md 缺少 \"> " + supported + "\"（40901 触发条件里的版本上限与服务端支持的版本漂移了）"
                        + "—— 请同步文档；ClientSchema.SUPPORTED_SCHEMA 本身的值不许为了迁就文档而改");
        assertTrue(doc.contains("支持的 " + supported),
                "docs/API.md 缺少 \"支持的 " + supported + "\"（40901 实测样例文案与服务端支持的版本漂移了）"
                        + "—— 请同步文档；ClientSchema.SUPPORTED_SCHEMA 本身的值不许为了迁就文档而改");
    }
}
