package com.twelvejade.server.health;

import com.twelvejade.server.common.ApiResponse;
import java.util.Map;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RestController;

/**
 * 健康探针：给"服务起来了没有"一个不依赖数据库、不依赖登录的答案。
 *
 * <p>刻意保持极简——探针里任何多余依赖（数据库、Redis）都会让"服务活着"和"依赖可用"
 * 两件事混成一个信号，排查时反而更难定位。
 */
@RestController
public class HealthController {

    @GetMapping("/api/health")
    public ApiResponse<Map<String, Object>> health() {
        return ApiResponse.ok(Map.of(
                "status", "up",
                "service", "twelve-jade-server",
                "stage", "M6-01-skeleton"));
    }
}
