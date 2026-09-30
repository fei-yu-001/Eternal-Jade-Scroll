package com.twelvejade.server.save;

import com.twelvejade.server.common.ApiCode;
import com.twelvejade.server.common.ApiResponse;
import java.util.LinkedHashMap;
import java.util.Map;
import org.springframework.http.ResponseEntity;
import org.springframework.http.converter.HttpMessageNotReadableException;
import org.springframework.web.bind.annotation.ExceptionHandler;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RequestParam;
import org.springframework.web.bind.annotation.RestController;

/**
 * 存档契约占位：为 M6-02 的云存档定下形状，这一版**不落库、不做同步**。
 *
 * <p>现在就把接口形状定死，是因为 Unity 端要按它写解析代码；等真正做同步时再改形状，
 * 客户端就得跟着改一次。版本校验现在就做——它是最容易在联调时才暴露、也最难排查的一条。
 *
 * <p>请求体用 {@code Map} 接而不是直接用 JSON 库的节点类型：Spring Boot 4 用的已经是
 * Jackson 3（包名从 {@code com.fasterxml} 变成 {@code tools.jackson}），把代码绑死在
 * 某一个库上，跨大版本时还会再踩一次包名。契约层只需要"有哪些字段、值是多少"。
 *
 * <p>上传规则：
 * <ul>
 *   <li>版本高于服务端 → 拒（40901）：服务端读不懂更新的结构，不能假装收下了。</li>
 *   <li>版本低于服务端 → 收下并标记 {@code migratable}：老客户端会先在本地迁移再上传，
 *       真正的迁移逻辑属于 M6-02。</li>
 * </ul>
 */
@RestController
@RequestMapping("/api/save")
public class SaveController {

    @PostMapping("/upload")
    public ResponseEntity<ApiResponse<Map<String, Object>>> upload(
            @RequestBody(required = false) Map<String, Object> save) {
        if (save == null) {
            return ResponseEntity.badRequest()
                    .body(ApiResponse.fail(ApiCode.BAD_REQUEST, "请求体应是存档 JSON 对象。"));
        }
        var version = save.get("schemaVersion");
        if (!(version instanceof Number number)) {
            return ResponseEntity.badRequest()
                    .body(ApiResponse.fail(ApiCode.BAD_REQUEST, "存档缺少可读的 schemaVersion。"));
        }
        int schemaVersion = number.intValue();
        if (schemaVersion > ClientSchema.SUPPORTED_SCHEMA) {
            return ResponseEntity.status(409).body(ApiResponse.fail(ApiCode.SAVE_TOO_NEW,
                    "存档版本 " + schemaVersion + " 高于本服务器支持的 " + ClientSchema.SUPPORTED_SCHEMA + "。"));
        }

        int slot = save.get("slot") instanceof Number slotNumber ? slotNumber.intValue() : 1;
        // 这一版只回执不落库：写进 README 与日志，别让人以为云存档已经能用。
        var receipt = new LinkedHashMap<String, Object>();
        receipt.put("slot", slot);
        receipt.put("schemaVersion", schemaVersion);
        receipt.put("migratable", schemaVersion < ClientSchema.SUPPORTED_SCHEMA);
        receipt.put("persisted", false);
        return ResponseEntity.ok(ApiResponse.ok(receipt));
    }

    @GetMapping("/download")
    public ResponseEntity<ApiResponse<Map<String, Object>>> download(@RequestParam int slot) {
        if (slot < 1 || slot > ClientSchema.SLOT_COUNT) {
            return ResponseEntity.badRequest()
                    .body(ApiResponse.fail(ApiCode.BAD_REQUEST,
                            "存档槽位需在 1–" + ClientSchema.SLOT_COUNT + " 之间，收到 " + slot + "。"));
        }
        // 未存档是正常状态，不是错误：code 仍为 0，用 present=false 表示"这一档是空的"。
        var result = new LinkedHashMap<String, Object>();
        result.put("slot", slot);
        result.put("present", false);
        result.put("schemaVersion", null);
        result.put("save", null);
        return ResponseEntity.ok(ApiResponse.ok(result));
    }

    /** 请求体根本不是 JSON 对象（数组、字符串、坏 JSON）时，返回同一套业务码而不是 500。 */
    @ExceptionHandler(HttpMessageNotReadableException.class)
    public ResponseEntity<ApiResponse<Void>> onUnreadableBody() {
        return ResponseEntity.badRequest()
                .body(ApiResponse.fail(ApiCode.BAD_REQUEST, "请求体应是存档 JSON 对象。"));
    }
}
