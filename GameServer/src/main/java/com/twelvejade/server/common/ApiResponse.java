package com.twelvejade.server.common;

/**
 * 统一返回体：code / message / data 三个字段，形状在 M6-01 定死，之后不再改。
 *
 * <p>定死的原因：Unity 端要按这个形状解析，改字段名的成本远高于现在多写一句注释。
 * 约定——
 * <ul>
 *   <li>{@code code == 0} 表示成功，非 0 一律是失败；客户端只需判断一个数。</li>
 *   <li>{@code message} 给人看（排查问题时日志里能直接读），客户端不依赖它的文案。</li>
 *   <li>{@code data} 成功时带业务数据，失败时为 {@code null}。</li>
 * </ul>
 *
 * @param <T> 业务数据类型
 */
public record ApiResponse<T>(int code, String message, T data) {

    public static <T> ApiResponse<T> ok(T data) {
        return new ApiResponse<>(ApiCode.OK.code(), ApiCode.OK.message(), data);
    }

    public static <T> ApiResponse<T> fail(ApiCode code) {
        return new ApiResponse<>(code.code(), code.message(), null);
    }

    public static <T> ApiResponse<T> fail(ApiCode code, String message) {
        return new ApiResponse<>(code.code(), message, null);
    }
}
