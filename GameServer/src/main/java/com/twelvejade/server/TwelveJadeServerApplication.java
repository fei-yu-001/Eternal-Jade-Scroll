package com.twelvejade.server;

import org.springframework.boot.SpringApplication;
import org.springframework.boot.autoconfigure.SpringBootApplication;

/**
 * 《十二玉楼长生经》服务端入口。
 *
 * <p>M6-01 阶段只提供三样东西：健康探针、统一返回体、存档契约占位。
 * 这里不连数据库、不做业务——骨架先能起来、能被探活，联网与云存档留给 M6-02。
 */
@SpringBootApplication
public class TwelveJadeServerApplication {

    public static void main(String[] args) {
        SpringApplication.run(TwelveJadeServerApplication.class, args);
    }
}
