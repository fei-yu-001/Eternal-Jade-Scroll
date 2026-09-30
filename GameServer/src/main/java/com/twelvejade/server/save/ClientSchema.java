package com.twelvejade.server.save;

/**
 * 客户端存档的契约常量。
 *
 * <p>{@link #SUPPORTED_SCHEMA} 必须与 Unity 端 {@code SaveData.CurrentSchemaVersion} 一致
 * （当前为 10，由 M5-05 章节轴引入）。升级客户端存档结构时，这里要跟着改，
 * 否则老客户端会把新结构的存档塞进来，服务端照单全收却读不懂。
 */
public final class ClientSchema {

    /** 本服务端能理解的最高存档版本。 */
    public static final int SUPPORTED_SCHEMA = 10;

    /** 客户端三档存档的槽位数量，与 Unity 端 SaveRepository.SlotCount 一致。 */
    public static final int SLOT_COUNT = 3;

    private ClientSchema() {
    }
}
