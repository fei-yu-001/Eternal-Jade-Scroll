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
        // 物品表：由 Unity 启动时注入，校验与迁移才能按单品堆叠上限卡。
        // 为空时退回存档层全局上限（99），只影响外部写入的脏档。
        public ItemTable Items { get; set; }

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
                if (data.schemaVersion > SaveData.CurrentSchemaVersion) return new SlotInfo(slot, SlotState.FutureVersion);
                // 迁移在读取处完成：旧档在内存里就补齐 v3 的铜钱与行囊，写回时按当前 schema 落盘。
                SaveData.Migrate(data, Items);
                return IsValid(data, slot) ? new SlotInfo(slot, SlotState.Ready, data)
                    : new SlotInfo(slot, SlotState.Corrupt);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                                        ex is ArgumentException || ex is FormatException)
            { return new SlotInfo(slot, SlotState.Corrupt); }
        }

        bool IsValid(SaveData data, int slot) =>
            data.slot == slot &&
            data.schemaVersion >= 1 && data.schemaVersion <= SaveData.CurrentSchemaVersion &&
            !string.IsNullOrWhiteSpace(data.characterId) &&
            !string.IsNullOrWhiteSpace(data.characterName) &&
            data.characterName.Length <= 16 &&
            data.location == "CharacterPreview" &&
            data.facing >= 0 && data.facing <= 3 &&
            (data.schemaVersion == 1 || SaveData.IsValidGender(data.gender)) &&
            (data.schemaVersion == 1 || SaveData.IsValidFaceStyle(data.faceStyle)) &&
            (data.traits == null || data.traits.Length <= CharacterGen.TraitsPerCharacter) &&
            (data.destiny == null || data.destiny.Length <= 32) &&
            (data.schemaVersion < 3 || (SaveData.IsValidCoins(data.coins) &&
                SaveData.IsValidReputation(data.localReputation) && InventoryRules.IsValid(data.bag, Items))) &&
            (data.schemaVersion < 4 || IsValidMerchants(data.merchants)) &&
            DateTimeOffset.TryParse(data.createdUtc, out _) &&
            DateTimeOffset.TryParse(data.updatedUtc, out _);

        // 商人状态：id 不重复、不为空，条数与数量都在合理范围内。
        static bool IsValidMerchants(MerchantState[] merchants)
        {
            if (merchants == null) return false;
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (var merchant in merchants)
            {
                if (merchant == null || string.IsNullOrWhiteSpace(merchant.id) || merchant.id.Length > 32) return false;
                if (!seen.Add(merchant.id)) return false;
                if (merchant.trades < 0 || merchant.trades > 99999) return false;
                if (merchant.goodwill < 0 || merchant.goodwill > 9999) return false;
                if (!IsValidStock(merchant.stock) || !IsValidStock(merchant.hidden)) return false;
            }
            return true;
        }

        static bool IsValidStock(StockLine[] lines)
        {
            if (lines == null) return false;
            if (lines.Length > 64) return false;
            foreach (var line in lines)
            {
                if (line == null || string.IsNullOrWhiteSpace(line.itemId) || line.itemId.Length > 32) return false;
                if (line.count < 0 || line.count > Trade.MaxQuantity) return false;
            }
            return true;
        }

        public SaveData Create(int slot, string characterId, string name) =>
            Create(slot, characterId, name, SaveData.Genders[0], 0, null, null);

        public SaveData Create(int slot, string characterId, string name, string gender, int faceStyle,
            string[] traits, string destiny)
        {
            if (Read(slot).State != SlotState.Empty) throw new InvalidOperationException("档位已有记录，请先在档位管理中删除。");
            if (string.IsNullOrWhiteSpace(characterId)) throw new ArgumentException("请选择角色外观。");
            if (!SaveData.IsValidGender(gender)) throw new ArgumentException("性别无效。");
            if (!SaveData.IsValidFaceStyle(faceStyle)) throw new ArgumentException("面容款式无效。");
            traits = traits ?? Array.Empty<string>();
            if (traits.Length > CharacterGen.TraitsPerCharacter) throw new ArgumentException("词条数量过多。");
            foreach (var trait in traits)
                if (CharacterGen.FindTrait(trait) == null) throw new ArgumentException("未知词条：" + trait);
            if (!string.IsNullOrEmpty(destiny) && CharacterGen.FindDestiny(destiny) == null)
                throw new ArgumentException("未知命运：" + destiny);
            name = (name ?? string.Empty).Trim();
            if (name.Length == 0 || name.Length > 16 || name.Any(char.IsControl))
                throw new ArgumentException("角色名字需要 1–16 个字，不能包含控制字符。");
            var now = DateTimeOffset.UtcNow.ToString("O");
            var data = new SaveData { slot = slot, characterId = characterId, characterName = name,
                gender = gender, faceStyle = faceStyle, traits = traits, destiny = destiny ?? "",
                coins = ItemTable.StartingCoins, localReputation = 0, bag = SaveData.StartingBag(),
                createdUtc = now, updatedUtc = now };
            Write(data);
            return data;
        }

        public void Write(SaveData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var path = SlotPath(data.slot);
            // 存档一律以当前 schema 落盘：旧版本对象在写回时完成升级。
            data.schemaVersion = SaveData.CurrentSchemaVersion;
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
            // Remove the primary first. If it is locked, keep its valid backup intact.
            foreach (var suffix in new[] { "", ".bak", ".tmp" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }

        public UserSettings LoadSettings(out SettingsLoadState state)
        {
            var path = Path.Combine(root, "settings.json");
            var primary = ReadSettingsFile(path);
            if (primary.State == SettingsFileState.Ready)
            {
                state = SettingsLoadState.Ready;
                return primary.Data;
            }
            if (primary.State == SettingsFileState.FutureVersion)
            {
                state = SettingsLoadState.FutureVersion;
                return new UserSettings();
            }
            var backup = ReadSettingsFile(path + ".bak");
            if (backup.State == SettingsFileState.Ready)
            {
                state = SettingsLoadState.Recovered;
                return backup.Data;
            }
            if (backup.State == SettingsFileState.FutureVersion)
            {
                state = SettingsLoadState.FutureVersion;
                return new UserSettings();
            }
            state = primary.State == SettingsFileState.Missing && backup.State == SettingsFileState.Missing
                ? SettingsLoadState.Default : SettingsLoadState.Reset;
            return new UserSettings();
        }

        enum SettingsFileState { Missing, Ready, Corrupt, FutureVersion }

        (SettingsFileState State, UserSettings Data) ReadSettingsFile(string path)
        {
            if (!File.Exists(path)) return (SettingsFileState.Missing, null);
            try
            {
                var json = File.ReadAllText(path, Encoding.UTF8);
                var settings = codec.Deserialize<UserSettings>(json);
                if (!json.Contains("\"schemaVersion\"") || settings == null)
                    return (SettingsFileState.Corrupt, null);
                if (settings.schemaVersion > 1) return (SettingsFileState.FutureVersion, null);
                if (settings.schemaVersion != 1) return (SettingsFileState.Corrupt, null);
                settings.Normalize();
                return (SettingsFileState.Ready, settings);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                                        ex is ArgumentException || ex is FormatException)
            { return (SettingsFileState.Corrupt, null); }
        }

        public void SaveSettings(UserSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (settings.schemaVersion != 1) throw new InvalidOperationException("无法保存其他版本的设置文件。");
            var path = Path.Combine(root, "settings.json");
            var primary = ReadSettingsFile(path);
            if (primary.State == SettingsFileState.FutureVersion ||
                ReadSettingsFile(path + ".bak").State == SettingsFileState.FutureVersion)
                throw new InvalidOperationException("设置文件由更新版本创建，当前版本不会覆盖。");
            settings.Normalize();
            // A damaged primary must not replace the only valid backup.
            AtomicWrite(path, codec.Serialize(settings), primary.State == SettingsFileState.Ready);
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
