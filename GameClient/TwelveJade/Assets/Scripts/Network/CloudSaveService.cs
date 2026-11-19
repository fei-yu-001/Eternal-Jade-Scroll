using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TwelveJade.Core;

namespace TwelveJade.Network
{
    /// <summary>payload 超出 256KB 上限（上传前预检抛出，UI 显示字节数与占用百分比）。</summary>
    public class PayloadTooLargeException : Exception
    {
        public long ActualBytes { get; }
        public long LimitBytes { get; }

        public PayloadTooLargeException(long actualBytes, long limitBytes)
            : base($"存档原文 {actualBytes} 字节，超出 {limitBytes} 字节上限。")
        {
            ActualBytes = actualBytes;
            LimitBytes = limitBytes;
        }
    }

    /// <summary>云存档错误码与玩家可见文案的**唯一出处**（客户端侧）。
    ///
    /// 为什么要有这一类：服务端 `ApiCode.SLOT_CONFLICT(40903)` 说的是"同一个 (user,slot) 被两次
    /// 上传同时撞进来"。那句话玩家听不懂，得翻成一句人话——而翻法只准有一处（写在 UI 里就是第二真源，T095 同族）。
    ///
    /// **这是兜底路径，不是常见错误**（架构师 2026-10-06 O 包 D-50③，实测口径见 Tools/logs/h4-ratchet-report.md §S15 实测）：
    /// H2 实测 128 次并发上传同一 (user,slot) 零命中 40903——服务端"有界重试一次、仍失败才 409/40903"，
    /// 常态是撞车被重试吸收掉（128/128 返回 200）。真看到 40903 意味着重试也没救回来。
    /// 将来读到这段注释的人别把它当成"玩家经常撞槽位"。
    ///
    /// **9xxxx 是客户端本地判定专用段**，服务端永远不返回它。本类写之前，客户端把"另存的目标槽已有存档"
    /// 也标成 40903，与服务端的槽位冲突**同号**：W3-A 一上线，同一句提示会在两件完全不同的事上冒出来，
    /// 玩家会以为是自己并发点了上传。所以本地那条改判 `LocalSlotBusy = 90001`。</summary>
    public static class CloudErrors
    {
        /// <summary>服务端 ApiCode.SLOT_CONFLICT：并发上传撞同一槽位（生产者在 W3-A，服务端侧）。</summary>
        public const int SlotConflict = 40903;

        /// <summary>客户端本地判定"另存目标槽非空，为防误触拒绝"——不占服务端的码段。</summary>
        public const int LocalSlotBusy = 90001;

        /// <summary>玩家可见的那句（D-50②：3号 定稿，措辞不许承诺诊断）。
        /// 旧句"该槽位刚被另一次上传占用，请重试"是在**替玩家诊断原因**，而 40903 的生产者
        /// 是 `DataIntegrityViolationException` 的兜底 handler——它接到的也可能是别的完整性违例，
        /// 那句"刚被另一次上传占用"就说不成立。新句只讲两件玩家确知的事：没写进去 + 稍后再试。
        /// 服务端 SaveController 的 message 仍是旧那句，但 40903 一律走本常量，玩家读不到服务端那句
        /// （跨端一致性口径见 InventoryEconomySuite 第 23 节；服务端那句要不要同步改，已作为待裁项交给架构师）。</summary>
        public const string UploadConflictText = "这个槽位的存档没能上传，请稍后再试。";

        /// <summary>上传失败时该对玩家说的那句；返回 null 表示"不是这一类"，UI 走它自己的通用文案。
        /// **绝不允许返回空串**——空串会被 UI 当成"没什么要说的"，那就是静默失败。</summary>
        public static string TextForUpload(ApiException error) =>
            error != null && error.Code == SlotConflict ? UploadConflictText : null;
    }

    /// <summary>单个云槽位的元数据：云端状态 + 本地对照信息（供对话框与冲突判定）。</summary>
    public sealed class CloudSlotMetadata
    {
        public int slot;
        public bool present;             // 云端是否存在存档
        public int schemaVersion = -1;   // -1 = 未知（/api/save/list 不带版本，download 才带）
        public string updatedAt;         // 云端最近同步时间（ISO8601）
        public long localTimestamp;      // 本地存档 updatedUtc 的 Unix 毫秒（空槽为 0）
        public string localCharacterName;
        public int localCoins;
        public bool localPresent;
        /// <summary>本地那份档序列化后的 UTF-8 字节数（0 = 本地没有档）。
        /// 云存档面板要显示"多大、占 256KB 上限的百分之几"，玩家才知道离被拒还有多远。</summary>
        public int localPayloadBytes;
    }

    /// <summary>冲突方向判定（M6 防误触）。</summary>
    public enum ConflictSide
    {
        /// <summary>本地存档的更新时间晚于云端——建议上传（模式 B）。</summary>
        LocalNewer,
        /// <summary>云端不早于本地——建议下载（模式 A）。</summary>
        CloudNewer,
        /// <summary>任一侧时间缺失或无法解析——交给用户自行决策，不猜方向。</summary>
        Unknown,
    }

    /// <summary>
    /// 云存档核心同步（M5）+ 三策略防误触（M6，ADR-023"由用户确认覆盖方向，杜绝静默覆盖"）。
    /// 服务端对 payload 只存不读——结构化解析全部发生在客户端。
    /// </summary>
    public class CloudSaveService
    {
        readonly NetworkClient client;
        readonly Func<SaveRepository> repositoryProvider;
        readonly IJsonCodec codec = new TwelveJade.Presentation.UnityJsonCodec();

        public CloudSaveService(NetworkClient client, Func<SaveRepository> repositoryProvider)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.repositoryProvider = repositoryProvider ?? throw new ArgumentNullException(nameof(repositoryProvider));
        }

        SaveRepository Repo => repositoryProvider();

        // ---------- 尺寸防御（M5） ----------

        /// <summary>上传前 UTF-8 字节数精确预检；超限抛 PayloadTooLargeException（UI 显示字节与百分比）。</summary>
        public void PrecheckPayload(string saveJson)
        {
            var limit = client.Config.MaxPayloadBytes;
            var actual = Encoding.UTF8.GetByteCount(saveJson ?? "");
            if (actual > limit) throw new PayloadTooLargeException(actual, limit);
        }

        /// <summary>payload 字节占用（0–1），供 UI 显示百分比。</summary>
        public double PayloadUsageRatio(string saveJson) =>
            client.Config.MaxPayloadBytes <= 0
                ? 0
                : (double)Encoding.UTF8.GetByteCount(saveJson ?? "") / client.Config.MaxPayloadBytes;

        // ---------- 槽位状态（M5） ----------

        /// <summary>拉取三槽位云端状态并与本地对照（给对话框与冲突判定）。</summary>
        public IEnumerator ListSlots(Action<List<CloudSlotMetadata>, ApiException> done)
        {
            var repo = Repo;
            List<CloudSlotMetadata> slots = null;
            ApiException error = null;
            yield return client.SendJson(RequestSpec.Get("/api/save/list", needsAuth: true), result =>
            {
                if (result.Ok)
                {
                    // 服务端回执形状变了（或 data 干脆没有）时，宁可报"读不到槽位"，
                    // 也不要在这里抛 NRE——那会让整块云存档面板空白且没有任何原因。
                    var data = result.Response.Data;
                    var items = data == null ? default : data["slots"];
                    if (items == null || items.kind != JsonValue.Kind.Array)
                    {
                        error = new ApiException(50001, "云端槽位列表格式不认识，未做任何改动。", 200);
                    }
                    else
                    {
                        slots = new List<CloudSlotMetadata>();
                        for (var i = 0; i < items.Count; i++)
                        {
                            var node = items[i];
                            var meta = new CloudSlotMetadata
                            {
                                slot = node["slot"].AsInt(0),
                                present = node["present"].AsBool(false),
                                updatedAt = node["updatedAt"].AsString(null),
                            };
                            EnrichWithLocalState(meta, repo);
                            slots.Add(meta);
                        }
                    }
                }
                else error = result.Error;
            });
            done(slots, error);
        }

        void EnrichWithLocalState(CloudSlotMetadata meta, SaveRepository repo)
        {
            var info = repo.Read(meta.slot);
            meta.localPresent = info.CanLoad;
            if (!info.CanLoad) return;
            meta.localCharacterName = info.Data.characterName;
            meta.localCoins = info.Data.coins;
            meta.localPayloadBytes = Encoding.UTF8.GetByteCount(codec.Serialize(info.Data));
            if (DateTimeOffset.TryParse(info.Data.updatedUtc, out var localTime))
                meta.localTimestamp = localTime.ToUnixTimeMilliseconds();
        }

        /// <summary>给 UI 的一句话体积描述：多少 KB、占 256KB 上限的百分之几。
        /// 文案算在这里而不是对话框里——上限值只有本类知道，散到 UI 就成了第二份真源。</summary>
        public string PayloadSizeText(int payloadBytes)
        {
            if (payloadBytes <= 0) return "存档 0 KB";
            var limit = Math.Max(1, client.Config.MaxPayloadBytes);
            var percent = (int)Math.Round(payloadBytes * 100.0 / limit);
            return $"存档 {payloadBytes / 1024.0:0.0} KB（占 {limit / 1024}KB 上限的 {percent}%）";
        }

        // ---------- 三种解决模式（M6 防误触） ----------

        /// <summary>
        /// 模式 A（云端覆盖本地）/ 模式 C（云端另存到别的槽位）：
        /// sourceSlot 取云端 payload；targetSlot == sourceSlot 即覆盖本地，
        /// targetSlot 不同则写入目标槽——目标槽非空时拒绝（两份进度互不覆盖）。
        /// </summary>
        public IEnumerator DownloadToSlot(int sourceSlot, int targetSlot, Action<ApiException> done)
        {
            if (targetSlot != sourceSlot && Repo.Read(targetSlot).State != SlotState.Empty)
            {
                done(new ApiException(CloudErrors.LocalSlotBusy,
                    $"另存目标槽位 {targetSlot} 已有存档，为防误触已拒绝（两份进度互不覆盖）。", 0));
                yield break;
            }
            SaveData downloaded = null;
            ApiException error = null;
            yield return client.SendJson(
                RequestSpec.Get($"/api/save/download?slot={sourceSlot}", needsAuth: true), result =>
            {
                if (result.Ok)
                {
                    var data = result.Response.Data;
                    if (!data.Has("save") || data["save"].kind != JsonValue.Kind.Object)
                    { error = new ApiException(40001, $"云端槽位 {sourceSlot} 为空，没有可下载的存档。", 200); return; }
                    var payloadText = NetworkJson.Write(data["save"]);
                    try
                    {
                        downloaded = codec.Deserialize<SaveData>(payloadText);
                        if (downloaded == null)
                            error = new ApiException(50001, "云端存档内容无法解析为本地存档结构。", 200);
                    }
                    catch (Exception ex)
                    {
                        error = new ApiException(50001, "云端存档解析失败：" + ex.Message, 200);
                    }
                }
                else error = result.Error;
            });
            if (error != null || downloaded == null) { done(error); yield break; }

            try
            {
                SaveData.Migrate(downloaded); // 老版本云端 payload 在落盘前补齐缺省字段
                downloaded.slot = targetSlot;
                // Write 内部完成 IsValid 校验、迁移与原子落盘：云端来的脏档在这里被当场拒绝。
                Repo.Write(downloaded);
                LocalCredentialManager.UpdateLastSyncTime();
                done(null);
            }
            catch (Exception ex)
            {
                done(new ApiException(50001, "云端存档写入本地失败：" + ex.Message, 0));
            }
        }

        /// <summary>模式 B（本地覆盖云端）：序列化本地槽位 → 256KB 预检 → 上传。</summary>
        public IEnumerator UploadSlot(int slot, Action<string, ApiException> done)
        {
            var info = Repo.Read(slot);
            if (!info.CanLoad)
            {
                done(null, new ApiException(40001, $"本地槽位 {slot} 为空或已损坏，没有可上传的存档。", 0));
                yield break;
            }
            var saveJson = codec.Serialize(info.Data);
            try { PrecheckPayload(saveJson); }
            catch (PayloadTooLargeException ex)
            {
                done(null, new ApiException(40001, ex.Message, 0));
                yield break;
            }
            var body = "{\"slot\":" + slot +
                       ",\"schemaVersion\":" + info.Data.schemaVersion +
                       ",\"save\":" + saveJson + "}";
            yield return client.SendJson(RequestSpec.Post("/api/save/upload", body, needsAuth: true), result =>
            {
                if (result.Ok)
                {
                    LocalCredentialManager.UpdateLastSyncTime();
                    done(result.Response.Data["updatedAt"].AsString(null), null);
                }
                else done(null, result.Error);
            });
        }

        // ---------- 冲突判定（M6） ----------

        /// <summary>
        /// 冲突方向判定：两侧时间都**带显式时区**才比；任一侧缺失、无法解析、或是无时区的裸时间
        /// 一律 Unknown（不猜方向）。
        ///
        /// 为什么把"裸时间"也归进 Unknown：服务端 /api/save/* 的 updatedAt 是
        /// LocalDateTime + ISO_LOCAL_DATE_TIME，形如 "2026-10-04T20:32:37.167741"——
        /// 不带任何偏移量。而本地 updatedUtc 是 DateTimeOffset.UtcNow.ToString("O")（带 +00:00）。
        /// DateTimeOffset.TryParse 会把裸时间按**本机时区**补齐：开发机上客户端与服务端同区，
        /// 于是"看起来是对的"；一旦服务端换到 UTC 机房，判定结果会整体偏一个时区差，
        /// 把"本地更新"误判成"云端更新"——用户点下载就覆盖掉自己的进度。
        /// 静默猜时区 = 静默覆盖，正是 ADR-023 要杜绝的那件事。
        /// （服务端改吐带偏移的 UTC 后，这里自动恢复判定，见 09 日志技术债。）
        /// </summary>
        public static ConflictSide Compare(string localUpdatedUtc, string cloudUpdatedAtIso)
        {
            if (!HasExplicitOffset(localUpdatedUtc) || !HasExplicitOffset(cloudUpdatedAtIso))
                return ConflictSide.Unknown;
            if (!DateTimeOffset.TryParse(localUpdatedUtc, out var local) ||
                !DateTimeOffset.TryParse(cloudUpdatedAtIso, out var cloud)) return ConflictSide.Unknown;
            if (local > cloud) return ConflictSide.LocalNewer;
            if (cloud > local) return ConflictSide.CloudNewer;
            return ConflictSide.Unknown; // 同一刻：无从谈"更新"，交用户决策
        }

        /// <summary>这个 ISO8601 字符串是否自带时区（Z / +hh:mm / -hh:mm）。
        /// 只看 'T' 之后的时间部分：日期里的 '-' 不是偏移量。</summary>
        public static bool HasExplicitOffset(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return false;
            var t = iso.IndexOf('T');
            if (t < 0 || t + 1 >= iso.Length) return false;
            var tail = iso.Substring(t + 1);
            return tail.IndexOf('Z') >= 0 || tail.IndexOf('z') >= 0 ||
                   tail.IndexOf('+') >= 0 || tail.IndexOf('-') >= 0;
        }

        /// <summary>面向 UI 的一句话判定文案。</summary>
        public static string VerdictText(ConflictSide side) => side switch
        {
            ConflictSide.LocalNewer => "本地较新（建议上传）",
            ConflictSide.CloudNewer => "云端较新（建议下载）",
            _ => "无法自动判定新旧（时间缺失、无法解析或不带时区）——请人工对比后决定",
        };
    }
}
