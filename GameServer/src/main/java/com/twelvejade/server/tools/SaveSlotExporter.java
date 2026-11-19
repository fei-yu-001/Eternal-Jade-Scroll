package com.twelvejade.server.tools;

import java.security.MessageDigest;
import java.sql.Connection;
import java.sql.DriverManager;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.util.HashMap;
import java.util.Map;
import java.util.Optional;

/**
 * 离线存档槽位导出工具（W3-E）：给客户端 schemaVersion 10→11 升版准备**真实历史存档**
 * 迁移夹具。独立命令行程序，**不挂任何 HTTP 端点**——一个能导出他人存档的网络接口
 * 是安全风险，这个能力只属于运维者在库机器上的本地操作。
 *
 * <p><b>只读承诺</b>：本类对数据库只发 SELECT（见 {@link #loadPayload}/{@link #resolveUserId}），
 * 没有任何 INSERT/UPDATE/DELETE/DDL 路径；测试 {@code SaveSlotExporterTest} 以
 * "导出前后库内容逐字不变"钉死这一点。
 *
 * <p>用法：
 * <pre>
 * java -cp target/game-server-0.0.1.jar com.twelvejade.server.tools.SaveSlotExporter \
 *      --url "$TWELVEJADE_DB_URL" --user "$TWELVEJADE_DB_USERNAME" --password "$TWELVEJADE_DB_PASSWORD" \
 *      --username w3docuser --slot 1          # 单槽；或 --all-slots 一次导出三槽
 * </pre>
 * 输出：每槽先打印 payload JSON 原文一行，再打印 {@code # sha256=<hex>} 校验行。
 */
public final class SaveSlotExporter {

    private SaveSlotExporter() {
    }

    public static void main(String[] args) throws Exception {
        String url = option(args, "--url");
        String user = option(args, "--user");
        String password = option(args, "--password");
        if (url == null || user == null || password == null) {
            System.err.println("用法: SaveSlotExporter --url <jdbc> --user <db用户> --password <db密码> "
                    + "(--user-id <id> | --username <名字>) (--slot <1..3> | --all-slots)");
            System.exit(2);
        }
        Long userId = containsUserId(args)
                ? Long.valueOf(requireOption(args, "--user-id"))
                : null;
        String username = option(args, "--username");
        boolean allSlots = hasFlag(args, "--all-slots");
        Integer slot = allSlots ? null : Integer.valueOf(requireOption(args, "--slot"));

        try (Connection connection = DriverManager.getConnection(url, user, password)) {
            long id = userId != null ? userId : resolveUserId(connection, username);
            int[] slots = allSlots ? new int[] {1, 2, 3} : new int[] {slot};
            int missing = 0;
            for (int s : slots) {
                Optional<String> payload = loadPayload(connection, id, s);
                if (payload.isEmpty()) {
                    System.out.println("# slot " + s + ": empty");
                    missing++;
                    continue;
                }
                System.out.println("# slot " + s);
                System.out.println(payload.get());
                System.out.println("# sha256=" + sha256Hex(payload.get()));
            }
            if (missing == slots.length) {
                System.err.println("指定范围内没有任何存档（用户 " + id + "）");
                System.exit(1);
            }
        }
    }

    /** 用户名 → user_id；只读。 */
    public static long resolveUserId(Connection connection, String username) throws SQLException {
        if (username == null) {
            throw new IllegalArgumentException("需要 --user-id 或 --username");
        }
        try (PreparedStatement ps = connection.prepareStatement(
                "SELECT id FROM users WHERE username = ?")) {
            ps.setString(1, username);
            try (ResultSet rs = ps.executeQuery()) {
                if (!rs.next()) {
                    throw new IllegalArgumentException("无此用户: " + username);
                }
                return rs.getLong(1);
            }
        }
    }

    /** 取某用户某槽位的 payload 原文；只读 SELECT，查不到返回 empty。 */
    public static Optional<String> loadPayload(Connection connection, long userId, int slot)
            throws SQLException {
        try (PreparedStatement ps = connection.prepareStatement(
                "SELECT payload FROM saves WHERE user_id = ? AND slot = ?")) {
            ps.setLong(1, userId);
            ps.setInt(2, slot);
            try (ResultSet rs = ps.executeQuery()) {
                return rs.next() ? Optional.ofNullable(rs.getString(1)) : Optional.empty();
            }
        }
    }

    public static String sha256Hex(String content) {
        try {
            MessageDigest digest = MessageDigest.getInstance("SHA-256");
            byte[] hash = digest.digest(content.getBytes(java.nio.charset.StandardCharsets.UTF_8));
            StringBuilder hex = new StringBuilder(hash.length * 2);
            for (byte b : hash) {
                hex.append(Character.forDigit((b >> 4) & 0xF, 16));
                hex.append(Character.forDigit(b & 0xF, 16));
            }
            return hex.toString();
        } catch (java.security.NoSuchAlgorithmException e) {
            throw new IllegalStateException("SHA-256 不可用", e);
        }
    }

    // ---------- 参数解析 ----------

    static String option(String[] args, String name) {
        for (int i = 0; i < args.length - 1; i++) {
            if (args[i].equals(name)) {
                return args[i + 1];
            }
        }
        return null;
    }

    static String requireOption(String[] args, String name) {
        String value = option(args, name);
        if (value == null) {
            throw new IllegalArgumentException("缺少参数 " + name);
        }
        return value;
    }

    static boolean hasFlag(String[] args, String name) {
        for (String arg : args) {
            if (arg.equals(name)) {
                return true;
            }
        }
        return false;
    }

    /** main 里用到的"是否给了 --user-id"判定（便于 --username 与之互斥使用）。 */
    static boolean containsUserId(String[] args) {
        return hasFlag(args, "--user-id");
    }
}
