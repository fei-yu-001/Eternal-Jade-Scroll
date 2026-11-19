package com.twelvejade.server.auth;

import com.twelvejade.server.common.ApiCode;
import com.twelvejade.server.common.ApiResponse;
import com.twelvejade.server.common.AuthTokens;
import jakarta.servlet.http.HttpServletRequest;
import java.util.LinkedHashMap;
import java.util.Map;
import org.springframework.http.ResponseEntity;
import org.springframework.http.converter.HttpMessageNotReadableException;
import org.springframework.web.bind.annotation.ExceptionHandler;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RestController;

/**
 * 认证端点（M6-02）：注册 / 登录 / 登出 / 我是谁。
 *
 * <p>参数校验（长度、空白字符）在这里做；业务规则（占用、凭证比对、令牌签发）在
 * {@link UserService}。所有失败路径都走 ApiResponse + 业务码：
 * 参数不合法 40001、用户名占用 40902、凭证不对 40101、令牌无效 40102。
 *
 * <p>登录失败一律 40101，不区分"无此用户"与"密码错"——不给撞库者泄露用户名存在性。
 */
@RestController
public class AuthController {

    private final UserService userService;

    public AuthController(UserService userService) {
        this.userService = userService;
    }

    /** 注册成功即登录：data { userId, token }。 */
    @PostMapping("/api/auth/register")
    public ResponseEntity<ApiResponse<Map<String, Object>>> register(
            @RequestBody(required = false) Map<String, Object> body) {
        String username = requireString(body, "username");
        String password = requireString(body, "password");
        if (username.length() < 3 || username.length() > 32) {
            return badRequest("用户名需 3–32 个字符，收到 " + username.length() + " 个。");
        }
        if (password.length() < 6 || password.length() > 64) {
            return badRequest("密码需 6–64 个字符。");
        }
        if (hasWhitespace(username) || hasWhitespace(password)) {
            return badRequest("用户名与密码不允许含空白字符。");
        }
        var user = userService.register(username, password);
        return ResponseEntity.ok(ApiResponse.ok(authPayload(user)));
    }

    /** 登录成功换发新令牌（旧令牌作废）：data { userId, token }。 */
    @PostMapping("/api/auth/login")
    public ResponseEntity<ApiResponse<Map<String, Object>>> login(
            @RequestBody(required = false) Map<String, Object> body) {
        String username = requireString(body, "username");
        String password = requireString(body, "password");
        var user = userService.login(username, password);
        return ResponseEntity.ok(ApiResponse.ok(authPayload(user)));
    }

    /**
     * 令牌刷新（M6-03-S）：有效且未过期 → 换发新令牌（旧的作废）；
     * 过期 / 无效 / 缺头一律 401 / 40102，不区分（避免泄露令牌状态细节）。
     */
    @PostMapping("/api/auth/refresh")
    public ResponseEntity<ApiResponse<Map<String, Object>>> refresh(HttpServletRequest request) {
        var refreshed = userService.refresh(AuthTokens.from(request));
        if (refreshed.isEmpty()) {
            return unauthorized();
        }
        return ResponseEntity.ok(ApiResponse.ok(authPayload(refreshed.get())));
    }

    /**
     * 改密码（M6-02-C）：旧密码对 → 更新哈希，**当前令牌保留有效**
     * （客户端要强制重新登录时主动 logout）。旧密码错 → 401 / 40101；
     * 新密码形状同注册（6–64、无空白）且不得与旧密码相同 → 否则 400 / 40001。
     */
    @PostMapping("/api/auth/password")
    public ResponseEntity<ApiResponse<Void>> changePassword(
            HttpServletRequest request,
            @RequestBody(required = false) Map<String, Object> body) {
        var user = userService.findByToken(AuthTokens.from(request));
        if (user.isEmpty()) {
            return unauthorized();
        }
        String oldPassword = requireString(body, "oldPassword");
        String newPassword = requireString(body, "newPassword");
        if (newPassword.length() < 6 || newPassword.length() > 64) {
            return badRequest("新密码需 6–64 个字符。");
        }
        if (hasWhitespace(newPassword)) {
            return badRequest("新密码不允许含空白字符。");
        }
        if (newPassword.equals(oldPassword)) {
            return badRequest("新密码不得与旧密码相同。");
        }
        userService.changePassword(user.get().getId(), oldPassword, newPassword);
        return ResponseEntity.ok(ApiResponse.ok(null));
    }

    /** 登出：X-Auth-Token 置 null。幂等——令牌已无效也返回 code 0。 */
    @PostMapping("/api/auth/logout")
    public ResponseEntity<ApiResponse<Void>> logout(HttpServletRequest request) {
        String token = AuthTokens.from(request);
        if (token == null || token.isBlank()) {
            return unauthorized();
        }
        userService.logout(token);
        return ResponseEntity.ok(ApiResponse.ok(null));
    }

    /** 用令牌换取身份：data { userId, username }。 */
    @GetMapping("/api/me")
    public ResponseEntity<ApiResponse<Map<String, Object>>> me(HttpServletRequest request) {
        var user = userService.findByToken(AuthTokens.from(request));
        if (user.isEmpty()) {
            return unauthorized();
        }
        var data = new LinkedHashMap<String, Object>();
        data.put("userId", user.get().getId());
        data.put("username", user.get().getUsername());
        return ResponseEntity.ok(ApiResponse.ok(data));
    }

    // ---------- 内部 ----------

    private static Map<String, Object> authPayload(UserEntity user) {
        var data = new LinkedHashMap<String, Object>();
        data.put("userId", user.getId());
        data.put("token", user.getToken());
        return data;
    }

    private static String requireString(Map<String, Object> body, String field) {
        if (body == null || !(body.get(field) instanceof String s) || s.isEmpty()) {
            throw new MissingFieldException(field);
        }
        return s;
    }

    private static boolean hasWhitespace(String s) {
        return s.chars().anyMatch(Character::isWhitespace);
    }

    private static <T> ResponseEntity<ApiResponse<T>> badRequest(String message) {
        return ResponseEntity.badRequest().body(ApiResponse.fail(ApiCode.BAD_REQUEST, message));
    }

    private static <T> ResponseEntity<ApiResponse<T>> unauthorized() {
        return ResponseEntity.status(401).body(ApiResponse.fail(ApiCode.INVALID_TOKEN));
    }

    /** 请求体缺字段、字段为空或类型不对。 */
    public static final class MissingFieldException extends RuntimeException {
        public MissingFieldException(String field) {
            super("请求体缺少可读的 " + field + " 字段。");
        }
    }

    @ExceptionHandler(MissingFieldException.class)
    public ResponseEntity<ApiResponse<Void>> onMissingField(MissingFieldException e) {
        return ResponseEntity.badRequest().body(ApiResponse.fail(ApiCode.BAD_REQUEST, e.getMessage()));
    }

    @ExceptionHandler(UserService.UsernameTakenException.class)
    public ResponseEntity<ApiResponse<Void>> onUsernameTaken(UserService.UsernameTakenException e) {
        return ResponseEntity.status(409).body(ApiResponse.fail(ApiCode.USERNAME_TAKEN, e.getMessage()));
    }

    @ExceptionHandler(UserService.BadCredentialsException.class)
    public ResponseEntity<ApiResponse<Void>> onBadCredentials(UserService.BadCredentialsException e) {
        return ResponseEntity.status(401).body(ApiResponse.fail(ApiCode.BAD_CREDENTIALS, e.getMessage()));
    }

    @ExceptionHandler(UserService.LoginLockedException.class)
    public ResponseEntity<ApiResponse<Void>> onLoginLocked(UserService.LoginLockedException e) {
        // RFC 7231：限流类拒绝用 429 Too Many Requests（M6-02-C 修订，旧版曾用 409）。
        return ResponseEntity.status(429).body(ApiResponse.fail(ApiCode.LOGIN_LOCKED, e.getMessage()));
    }

    /** 请求体根本不是 JSON 对象（数组、字符串、坏 JSON）时，返回同一套业务码而不是 500。 */
    @ExceptionHandler(HttpMessageNotReadableException.class)
    public ResponseEntity<ApiResponse<Void>> onUnreadableBody() {
        return ResponseEntity.badRequest()
                .body(ApiResponse.fail(ApiCode.BAD_REQUEST, "请求体应是 JSON 对象。"));
    }
}
