package com.twelvejade.server.common;

import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.ControllerAdvice;
import org.springframework.web.bind.annotation.ExceptionHandler;
import org.springframework.web.multipart.MaxUploadSizeExceededException;

/**
 * 全局异常映射（M6-02-C）：把不经过任何 Controller 的容器层异常并入统一返回体。
 *
 * <p>当前唯一成员：multipart 请求体超限。存档端点是纯 JSON（大小上限在
 * SaveController 应用层按 256KB 先量再存），multipart 上限属前置防御——
 * 将来若出现文件上传端点，超限会在这里落到 400 / 40001，而不是裸 500。
 */
@ControllerAdvice
public class GlobalExceptionAdvice {

    @ExceptionHandler(MaxUploadSizeExceededException.class)
    public ResponseEntity<ApiResponse<Void>> onMaxUploadSize(MaxUploadSizeExceededException e) {
        return ResponseEntity.badRequest().body(ApiResponse.fail(ApiCode.BAD_REQUEST, "请求体超出大小限制。"));
    }
}
