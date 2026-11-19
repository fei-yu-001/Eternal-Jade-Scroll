package com.twelvejade.server.save;

/**
 * 客户端存档的契约常量。
 *
 * <p>{@link #SUPPORTED_SCHEMA} 必须与 Unity 端 {@code SaveData.CurrentSchemaVersion} 一致
 * （当前为 11，由 Gate O2-Schema11 引入：装备与耐久落盘 T051、每道方子的制作次数 T046，
 * 并补上 karmaEvents 的存档校验）。升级客户端存档结构时，这里要跟着改，
 * 否则老客户端会把新结构的存档塞进来，服务端照单全收却读不懂。
 *
 * <p>两侧必须**同批**改：客户端升 11 而这里留 10，玩家每一次上传都会撞
 * 409 / 40901（高于本服务器支持的版本）——那是"先上一半"的交付，不是安全的一半。
 */
public final class ClientSchema {

    /** 本服务端能理解的最高存档版本。 */
    public static final int SUPPORTED_SCHEMA = 11;

    /** 客户端三档存档的槽位数量，与 Unity 端 SaveRepository.SlotCount 一致。 */
    public static final int SLOT_COUNT = 3;

    private ClientSchema() {
    }
}
