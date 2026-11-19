package com.twelvejade.server.save;

import com.twelvejade.server.auth.UserEntity;
import com.twelvejade.server.auth.UserService;
import com.twelvejade.server.common.ApiCode;
import com.twelvejade.server.common.ApiResponse;
import com.twelvejade.server.common.AuthTokens;
import jakarta.servlet.http.HttpServletRequest;
import java.nio.charset.StandardCharsets;
import java.sql.SQLException;
import java.time.Clock;
import java.time.Instant;
import java.time.ZoneOffset;
import java.time.format.DateTimeFormatter;
import java.time.temporal.ChronoUnit;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import org.springframework.dao.DataIntegrityViolationException;
import org.springframework.http.ResponseEntity;
import org.springframework.http.converter.HttpMessageNotReadableException;
import org.springframework.web.bind.annotation.ExceptionHandler;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RequestParam;
import org.springframework.web.bind.annotation.RestController;
import tools.jackson.core.type.TypeReference;
import tools.jackson.databind.json.JsonMapper;

/**
 * 云存档端点（M6-02 起真正落库）。
 *
 * <p>M6-01 只做了契约占位；本版在既有形状上升级：两个存档端点必须带
 * {@code X-Auth-Token}（无效 40102），upload 落库（{@code (user_id, slot)} 上
 * INSERT-or-UPDATE，覆盖语义），download 从库里返回原文，新增 list 给将来的
 * "云端比本地新"冲突提示备数据（本轮不做比对逻辑）。
 *
 * <p>版本门禁行为与 M6-01 一行不改：{@code schemaVersion} 缺失 → 40001；
 * 高于 {@link ClientSchema#SUPPORTED_SCHEMA} → 409 / 40901；低于 → 收下并标
 * {@code migratable}。payload 是客户端存档 JSON 原文，服务端只存不读——
 * 客户端存档的真相在客户端，服务端不成为第二真相。
 *
 * <p>请求体用 {@code Map} 接而不是直接用 JSON 库的节点类型：Spring Boot 4 用的已经是
 * Jackson 3（包名从 {@code com.fasterxml} 变成 {@code tools.jackson}），把代码绑死在
 * 某一个库上，跨大版本时还会再踩一次包名。
 */
@RestController
@RequestMapping("/api/save")
public class SaveController {

    /** ISO8601 带 UTC 偏移：2026-10-04T01:30:00.123Z——客户端据此可跨时区比对"谁更新"。 */
    private static final DateTimeFormatter ISO_OFFSET_DATE_TIME = DateTimeFormatter.ISO_OFFSET_DATE_TIME;

    /** 存档原文（UTF-8 字节数）上限 256KB——先量再存，超限 400/40001，不入库。 */
    static final int MAX_PAYLOAD_BYTES = 256 * 1024;

    /**
     * payload 原文的读写专用 mapper，与 Spring 自动配置的那个隔离：
     * 自动配置的 mapper 带 non_null（为的是回执里 null 字段不输出），
     * 若拿它存档，会把客户端存档里的 null 字段吞掉，破坏"原文"语义。
     */
    private static final JsonMapper PAYLOAD_MAPPER = JsonMapper.builder().build();

    private static final TypeReference<Map<String, Object>> PAYLOAD_TYPE = new TypeReference<>() {
    };

    private final UserService userService;
    private final SaveRepository saveRepository;
    private final Clock clock;

    public SaveController(UserService userService, SaveRepository saveRepository, Clock clock) {
        this.userService = userService;
        this.saveRepository = saveRepository;
        this.clock = clock;
    }

    @PostMapping("/upload")
    public ResponseEntity<ApiResponse<Map<String, Object>>> upload(
            HttpServletRequest request,
            @RequestBody(required = false) Map<String, Object> body) {
        var user = requireUser(request);
        if (user == null) {
            return unauthorized();
        }
        if (body == null) {
            return badRequest("请求体应是含 slot、schemaVersion、save 的存档 JSON 对象。");
        }
        var version = body.get("schemaVersion");
        if (!(version instanceof Number number)) {
            return badRequest("存档缺少可读的 schemaVersion。");
        }
        int schemaVersion = number.intValue();
        if (schemaVersion > ClientSchema.SUPPORTED_SCHEMA) {
            return ResponseEntity.status(409).body(ApiResponse.fail(ApiCode.SAVE_TOO_NEW,
                    "存档版本 " + schemaVersion + " 高于本服务器支持的 " + ClientSchema.SUPPORTED_SCHEMA + "。"));
        }
        if (!(body.get("slot") instanceof Number slotNumber)) {
            return badRequest("存档缺少可读的 slot。");
        }
        int slot = slotNumber.intValue();
        if (slot < 1 || slot > ClientSchema.SLOT_COUNT) {
            return badRequest("存档槽位需在 1–" + ClientSchema.SLOT_COUNT + " 之间，收到 " + slot + "。");
        }
        if (!(body.get("save") instanceof Map<?, ?> savePayload)) {
            return badRequest("save 字段应是存档 JSON 对象。");
        }
        String payload = PAYLOAD_MAPPER.writeValueAsString(savePayload);
        if (payload.getBytes(StandardCharsets.UTF_8).length > MAX_PAYLOAD_BYTES) {
            // 先量再存：超大 payload 不入库，也不给它写进日志的份。
            return badRequest("存档原文超过 " + (MAX_PAYLOAD_BYTES / 1024) + "KB 上限。");
        }

        // INSERT-or-UPDATE on (user_id, slot)：同一用户同一槽位永远只有一条，后传覆盖先传。
        var entity = saveRepository.findByUser_IdAndSlot(user.getId(), (byte) slot)
                .orElseGet(() -> {
                    var fresh = new SaveEntity();
                    fresh.setUser(user);
                    fresh.setSlot((byte) slot);
                    return fresh;
                });
        entity.setSchemaVersion(schemaVersion);
        entity.setPayload(payload);
        // "现在"一律走注入的 Clock（TimeConfig 纪律）；截到微秒与数据库 TIMESTAMP(6) 精度对齐，
        // 保证回执与随后 download/list 给出的字符串逐字一致。
        entity.setUpdatedAt(Instant.now(clock).truncatedTo(ChronoUnit.MICROS));
        try {
            saveRepository.save(entity);
        } catch (DataIntegrityViolationException conflict) {
            // 并发上传同槽位（W3-A）：对端请求刚 INSERT 了同一 (user_id, slot)，败者撞
            // uk_saves_user_slot。有界重试——只一次：重新查到已存在的那行走 UPDATE 分支。
            // 不写成循环：循环会把真故障也重试成静默，还会让慢查询堆积。
            // 非唯一约束类冲突（NOT NULL、外键……）是真正故障，原样上抛，绝不重试。
            if (!isUniqueSlotConflict(conflict)) {
                throw conflict;
            }
            var existing = saveRepository.findByUser_IdAndSlot(user.getId(), (byte) slot)
                    .orElseThrow(() -> conflict);
            existing.setSchemaVersion(schemaVersion);
            existing.setPayload(payload);
            existing.setUpdatedAt(Instant.now(clock).truncatedTo(ChronoUnit.MICROS));
            entity = saveRepository.save(existing);
        }

        var receipt = new LinkedHashMap<String, Object>();
        receipt.put("slot", slot);
        receipt.put("persisted", true);
        receipt.put("migratable", schemaVersion < ClientSchema.SUPPORTED_SCHEMA);
        receipt.put("updatedAt", formatUpdatedAt(entity.getUpdatedAt()));
        return ResponseEntity.ok(ApiResponse.ok(receipt));
    }

    @GetMapping("/download")
    public ResponseEntity<ApiResponse<Map<String, Object>>> download(
            HttpServletRequest request,
            @RequestParam int slot) {
        var user = requireUser(request);
        if (user == null) {
            return unauthorized();
        }
        if (slot < 1 || slot > ClientSchema.SLOT_COUNT) {
            return badRequest("存档槽位需在 1–" + ClientSchema.SLOT_COUNT + " 之间，收到 " + slot + "。");
        }
        var result = new LinkedHashMap<String, Object>();
        result.put("slot", slot);
        var stored = saveRepository.findByUser_IdAndSlot(user.getId(), (byte) slot).orElse(null);
        if (stored == null) {
            // 未存档是正常状态，不是错误：code 仍为 0，用 present=false 表示"这一档是空的"。
            result.put("present", false);
            return ResponseEntity.ok(ApiResponse.ok(result));
        }
        result.put("present", true);
        result.put("schemaVersion", stored.getSchemaVersion());
        result.put("updatedAt", formatUpdatedAt(stored.getUpdatedAt()));
        result.put("save", PAYLOAD_MAPPER.readValue(stored.getPayload(), PAYLOAD_TYPE));
        return ResponseEntity.ok(ApiResponse.ok(result));
    }

    /**
     * 三槽位清单，固定返回三条（没存过的槽位 present=false）。
     * 客户端联网后用它判断"云端比本地新"来做冲突提示；比对逻辑本轮不做。
     */
    @GetMapping("/list")
    public ResponseEntity<ApiResponse<Map<String, Object>>> list(HttpServletRequest request) {
        var user = requireUser(request);
        if (user == null) {
            return unauthorized();
        }
        var bySlot = new HashMap<Byte, SaveEntity>();
        for (SaveEntity entity : saveRepository.findAllByUser_IdOrderBySlotAsc(user.getId())) {
            bySlot.put(entity.getSlot(), entity);
        }
        var slots = new ArrayList<Map<String, Object>>(ClientSchema.SLOT_COUNT);
        for (int s = 1; s <= ClientSchema.SLOT_COUNT; s++) {
            var entry = new LinkedHashMap<String, Object>();
            entry.put("slot", s);
            var stored = bySlot.get((byte) s);
            entry.put("present", stored != null);
            entry.put("updatedAt", stored == null ? null : formatUpdatedAt(stored.getUpdatedAt()));
            slots.add(entry);
        }
        var data = new LinkedHashMap<String, Object>();
        data.put("slots", slots);
        return ResponseEntity.ok(ApiResponse.ok(data));
    }

    // ---------- 内部 ----------

    /**
     * 是否为 {@code uk_saves_user_slot} 的唯一约束冲突——只有这类冲突值得重试成 UPDATE。
     * 兼容两种方言的消息形态：H2 是 {@code UK_SAVES_USER_SLOT ...}，MySQL 8 是
     * {@code Duplicate entry ... for key 'saves.uk_saves_user_slot'}；SQLState 23505
     * 是标准唯一违例。NOT NULL（23502）、外键等其余完整性违例一律判 false——
     * 它们是真故障，重试只会把故障吞成静默。
     */
    public static boolean isUniqueSlotConflict(Throwable ex) {
        for (Throwable t = ex; t != null; t = t.getCause()) {
            String message = String.valueOf(t.getMessage());
            if (message.toLowerCase(Locale.ROOT).contains("uk_saves_user_slot")) {
                return true;
            }
            if (t instanceof SQLException sql && "23505".equals(sql.getSQLState())) {
                return true;
            }
        }
        return false;
    }

    /** Instant → 带 UTC 偏移的 ISO8601（如 2026-10-04T01:30:00.123Z），三个输出点共用同一格式。 */
    private static String formatUpdatedAt(Instant instant) {
        return ISO_OFFSET_DATE_TIME.format(instant.atOffset(ZoneOffset.UTC));
    }

    private UserEntity requireUser(HttpServletRequest request) {
        return userService.findByToken(AuthTokens.from(request)).orElse(null);
    }

    private static ResponseEntity<ApiResponse<Map<String, Object>>> unauthorized() {
        return ResponseEntity.status(401).body(ApiResponse.fail(ApiCode.INVALID_TOKEN));
    }

    private static ResponseEntity<ApiResponse<Map<String, Object>>> badRequest(String message) {
        return ResponseEntity.badRequest().body(ApiResponse.fail(ApiCode.BAD_REQUEST, message));
    }

    /** 请求体根本不是 JSON 对象（数组、字符串、坏 JSON）时，返回同一套业务码而不是 500。 */
    @ExceptionHandler(HttpMessageNotReadableException.class)
    public ResponseEntity<ApiResponse<Void>> onUnreadableBody() {
        return ResponseEntity.badRequest()
                .body(ApiResponse.fail(ApiCode.BAD_REQUEST, "请求体应是存档 JSON 对象。"));
    }

    /**
     * 唯一键冲突兜底（W3-A）：并发上传同槽位的败者在重试后仍失败（或非冲突类完整性违例
     * 直接上抛到这里）时，转 409/40903——绝不漏成 500：Spring 默认 error JSON 里没有
     * 业务 code，客户端无从按契约处置。
     */
    @ExceptionHandler(DataIntegrityViolationException.class)
    public ResponseEntity<ApiResponse<Map<String, Object>>> onDataIntegrityViolation() {
        return ResponseEntity.status(409).body(ApiResponse.fail(ApiCode.SLOT_CONFLICT,
                "该槽位刚被另一次上传占用，请重试。"));
    }
}
