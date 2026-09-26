using System;
using System.IO;
using System.Linq;
using System.Text;

namespace TwelveJade.Core
{
    // Storage has no Unity dependency: it can be tested without opening the editor.
    public sealed class SaveRepository
    {
        public const int SlotCount = 3;
        readonly string root;
        readonly IJsonCodec codec;

        public SaveRepository(string root, IJsonCodec codec)
        {
            this.root = root ?? throw new ArgumentNullException(nameof(root));
            this.codec = codec ?? throw new ArgumentNullException(nameof(codec));
        }

        string SlotPath(int slot)
        {
            if (slot < 1 || slot > SlotCount) throw new ArgumentOutOfRangeException(nameof(slot));
            return Path.Combine(root, $"slot-{slot}.json");
        }

        public SlotInfo[] ReadAll() => Enumerable.Range(1, SlotCount).Select(Read).ToArray();

        public SlotInfo Latest() => ReadAll().Where(x => x.CanLoad)
            .OrderByDescending(x => DateTimeOffset.Parse(x.Data.updatedUtc)).FirstOrDefault();

        public SlotInfo Read(int slot)
        {
            var path = SlotPath(slot);
            var primary = ReadFile(path, slot);
            // Never downgrade a save created by a newer version.
            if (primary.State == SlotState.Ready || primary.State == SlotState.FutureVersion) return primary;
            var backup = ReadFile(path + ".bak", slot);
            if (backup.CanLoad) return new SlotInfo(slot, SlotState.Recovered, backup.Data);
            if (backup.State == SlotState.FutureVersion) return backup;
            return primary.State == SlotState.Empty && backup.State == SlotState.Empty
                ? primary : new SlotInfo(slot, SlotState.Corrupt);
        }

        SlotInfo ReadFile(string path, int slot)
        {
            if (!File.Exists(path)) return new SlotInfo(slot, SlotState.Empty);
            try
            {
                var json = File.ReadAllText(path, Encoding.UTF8);
                // JsonUtility applies field initializers to missing fields; require a version explicitly.
                if (!json.Contains("\"schemaVersion\"")) return new SlotInfo(slot, SlotState.Corrupt);
                var data = codec.Deserialize<SaveData>(json);
                if (data == null) return new SlotInfo(slot, SlotState.Corrupt);
                if (data.schemaVersion > 1) return new SlotInfo(slot, SlotState.FutureVersion);
                return IsValid(data, slot) ? new SlotInfo(slot, SlotState.Ready, data)
                    : new SlotInfo(slot, SlotState.Corrupt);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                                        ex is ArgumentException || ex is FormatException)
            { return new SlotInfo(slot, SlotState.Corrupt); }
        }

        static bool IsValid(SaveData data, int slot) => data.schemaVersion == 1 && data.slot == slot &&
            !string.IsNullOrWhiteSpace(data.characterId) && !string.IsNullOrWhiteSpace(data.characterName) &&
            data.characterName.Length <= 16 && data.location == "CharacterPreview" &&
            data.facing >= 0 && data.facing <= 3 &&
            DateTimeOffset.TryParse(data.createdUtc, out _) && DateTimeOffset.TryParse(data.updatedUtc, out _);

        public SaveData Create(int slot, string characterId, string name)
        {
            if (Read(slot).State != SlotState.Empty) throw new InvalidOperationException("档位已有记录，请先在档位管理中删除。");
            if (string.IsNullOrWhiteSpace(characterId)) throw new ArgumentException("请选择角色外观。");
            name = (name ?? string.Empty).Trim();
            if (name.Length == 0 || name.Length > 16 || name.Any(char.IsControl))
                throw new ArgumentException("角色名字需要 1–16 个字，不能包含控制字符。");
            var now = DateTimeOffset.UtcNow.ToString("O");
            var data = new SaveData { slot = slot, characterId = characterId, characterName = name,
                createdUtc = now, updatedUtc = now };
            Write(data);
            return data;
        }

        public void Write(SaveData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var path = SlotPath(data.slot);
            if (!IsValid(data, data.slot)) throw new ArgumentException("存档内容无效。");
            if (Read(data.slot).State == SlotState.FutureVersion)
                throw new InvalidOperationException("此存档由更新版本创建，不能覆盖。");
            data.updatedUtc = DateTimeOffset.UtcNow.ToString("O");
            // If primary is corrupt, preserve the valid backup until replacement succeeds.
            AtomicWrite(path, codec.Serialize(data), ReadFile(path, data.slot).CanLoad);
        }

        public void Delete(int slot)
        {
            var path = SlotPath(slot);
            foreach (var suffix in new[] { ".bak", ".tmp", "" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }

        public UserSettings LoadSettings(out bool reset)
        {
            reset = false;
            var path = Path.Combine(root, "settings.json");
            if (!File.Exists(path)) return new UserSettings();
            try
            {
                var json = File.ReadAllText(path, Encoding.UTF8);
                var settings = codec.Deserialize<UserSettings>(json);
                if (!json.Contains("\"schemaVersion\"") || settings == null || settings.schemaVersion != 1)
                    throw new FormatException();
                settings.Normalize();
                return settings;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                                        ex is ArgumentException || ex is FormatException)
            { reset = true; return new UserSettings(); }
        }

        public void SaveSettings(UserSettings settings)
        {
            settings.Normalize();
            AtomicWrite(Path.Combine(root, "settings.json"), codec.Serialize(settings), true);
        }

        void AtomicWrite(string path, string json, bool preservePrevious)
        {
            Directory.CreateDirectory(root);
            var temp = path + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temp, path, preservePrevious ? path + ".bak" : null);
            else File.Move(temp, path);
        }
    }
}
