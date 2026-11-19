using System;
using System.Security.Cryptography;
using System.Text;
using TwelveJade.Core;

namespace TwelveJade.Network
{
    /// <summary>
    /// 本地多槽位全量离线备份（M6-03-C · M7 极端容灾）：
    /// 三个槽位打包为单个 .twelvejade.backup.json（带 SHA256 校验和），
    /// 导入时先验完整性再逐槽经 SaveRepository.Write 的全套校验落盘——
    /// 损坏的备份恢复不进来，脏档也混不进去。
    /// </summary>
    public static class LocalBackupUtility
    {
        public const int BundleVersion = 1;
        public const string FileExtension = ".twelvejade.backup.json";

        /// <summary>
        /// 导出本地全部槽位为单个备份 JSON。空槽位也记录（present=false），
        /// 恢复时才能忠实还原"这一档本来就没有"的事实。
        /// </summary>
        public static string ExportAllLocalSavesToBundle(SaveRepository repository, IJsonCodec codec)
        {
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            if (codec == null) throw new ArgumentNullException(nameof(codec));
            var all = repository.ReadAll();
            var builder = new StringBuilder();
            builder.Append("{\"version\":").Append(BundleVersion);
            builder.Append(",\"createdAtUtc\":\"").Append(DateTime.UtcNow.ToString("O")).Append('"');
            builder.Append(",\"slots\":[");
            var checksumSource = new StringBuilder();
            var first = true;
            foreach (var info in all)
            {
                if (!first) builder.Append(',');
                first = false;
                var present = info.CanLoad;
                var json = present ? codec.Serialize(info.Data) : "";
                builder.Append("{\"slot\":").Append(info.Slot)
                    .Append(",\"present\":").Append(present ? "true" : "false")
                    .Append(",\"json\":").Append(NetworkJson.Escape(json)).Append('}');
                checksumSource.Append(info.Slot).Append('\n').Append(json).Append('\n');
            }
            builder.Append("],\"sha256\":\"").Append(Sha256Hex(checksumSource.ToString())).Append("\"}");
            return builder.ToString();
        }

        /// <summary>
        /// 从备份 JSON 恢复本地全部槽位。流程：解析 → 校验和核对 → 逐槽反序列化 →
        /// （可选）拒绝覆盖非空槽 → SaveRepository.Write 全套校验落盘。
        /// 任何一步失败即整体失败，绝不留下半恢复状态（Write 本身是原子写）。
        /// </summary>
        public static void ImportBundleToLocalSaves(string bundleJson, SaveRepository repository,
            IJsonCodec codec, bool allowOverwriteExisting)
        {
            if (string.IsNullOrWhiteSpace(bundleJson)) throw new ArgumentException("备份内容为空。");
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            if (codec == null) throw new ArgumentNullException(nameof(codec));

            var root = Json.Parse(bundleJson);
            if (root.kind != JsonValue.Kind.Object) throw new InvalidOperationException("备份根节点不是对象。");
            if (root["version"].AsInt(0) != BundleVersion)
                throw new InvalidOperationException("备份版本不受支持：" + root["version"].AsInt(0));
            var slots = root["slots"];
            if (slots.kind != JsonValue.Kind.Array) throw new InvalidOperationException("备份缺少 slots 数组。");

            var checksumSource = new StringBuilder();
            foreach (var entry in slots.Items)
            {
                checksumSource.Append(entry["slot"].AsInt(0)).Append('\n')
                    .Append(entry["json"].AsString("")).Append('\n');
            }
            var expected = root["sha256"].AsString("");
            var actual = Sha256Hex(checksumSource.ToString());
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("备份校验和不匹配，文件已损坏或不完整，拒绝恢复。");

            foreach (var entry in slots.Items)
            {
                var slot = entry["slot"].AsInt(0);
                if (slot < 1 || slot > SaveRepository.SlotCount)
                    throw new InvalidOperationException("备份里的槽位编号越界：" + slot);
                if (!entry["present"].AsBool(false)) continue;
                var json = entry["json"].AsString("");
                var data = codec.Deserialize<SaveData>(json);
                if (data == null) throw new InvalidOperationException($"槽位 {slot} 的备份内容无法解析。");
                if (!allowOverwriteExisting && repository.Read(slot).CanLoad)
                    throw new InvalidOperationException($"槽位 {slot} 已有存档且未允许覆盖，恢复中止。");
                SaveData.Migrate(data);
                data.slot = slot; // 恢复到哪一槽由备份条目决定
                repository.Write(data); // 内部完成 IsValid 校验与原子写
            }
        }

        /// <summary>SHA256 十六进制摘要（小写），内容为逐槽 "slot\njson\n" 拼接。</summary>
        public static string Sha256Hex(string content)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(content ?? ""));
            var builder = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) builder.Append(b.ToString("x2"));
            return builder.ToString();
        }
    }
}
