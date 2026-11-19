package com.twelvejade.server.common;

/**
 * 业务错误码。数值分段：0 成功、4xxxx 请求问题、5xxxx 服务端问题。
 *
 * <p>存档版本高于服务端单列一个码（{@link #SAVE_TOO_NEW}）——它不是"请求写错了"，
 * 而是"客户端比服务端新"，将来真出现时运维一眼能分清是哪种问题。
 *
 * <p>M6-02 追加认证与云存档落库的错误码（只追加不重排，旧编号含义不变）。
 */
public enum ApiCode {

    OK(0, "成功"),
    BAD_REQUEST(40001, "请求不合法"),
    BAD_CREDENTIALS(40101, "账号或密码不对"),
    INVALID_TOKEN(40102, "令牌无效或未登录"),
    SAVE_TOO_NEW(40901, "存档版本高于本服务器支持的版本"),
    USERNAME_TAKEN(40902, "用户名已被占用"),
    SLOT_CONFLICT(40903, "槽位冲突"),
    LOGIN_LOCKED(40904, "登录失败次数过多（锁定中）"),
    // 预留（W3-C 扫描确认当前无生产者）：未捕获异常目前走 Spring Boot 默认 error JSON（无业务 code），
    // 等"全局异常兜底 handler"场景把它变成 500/50001 信封——由架构师排期，不静默保留。
    INTERNAL_ERROR(50001, "服务器内部错误");

    private final int code;
    private final String message;

    ApiCode(int code, String message) {
        this.code = code;
        this.message = message;
    }

    public int code() {
        return code;
    }

    public String message() {
        return message;
    }
}
