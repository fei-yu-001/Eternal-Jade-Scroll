package com.twelvejade.server.common;

/**
 * 业务错误码。数值分段：0 成功、4xxxx 请求问题、5xxxx 服务端问题。
 *
 * <p>存档版本高于服务端单列一个码（{@link #SAVE_TOO_NEW}）——它不是"请求写错了"，
 * 而是"客户端比服务端新"，将来真出现时运维一眼能分清是哪种问题。
 */
public enum ApiCode {

    OK(0, "成功"),
    BAD_REQUEST(40001, "请求不合法"),
    SAVE_TOO_NEW(40901, "存档版本高于本服务器支持的版本"),
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
