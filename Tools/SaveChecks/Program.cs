using System.Text.Json;
using TwelveJade.Core;

var codec = new JsonCodec();
var root = Path.Combine(Path.GetTempPath(), "twelve-jade-save-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var saves = new SaveRepository(root, codec);
    Check("new slots empty", saves.ReadAll().All(s => s.State == SlotState.Empty));
    var first = saves.Create(1, "farmer", "阿禾");
    Check("create and read", saves.Read(1).Data.characterName == "阿禾");
    Check("latest", saves.Latest()?.Slot == 1);
    Check("slot isolation", saves.Read(2).State == SlotState.Empty);
    Expect<InvalidOperationException>("no overwrite", () => saves.Create(1, "merchant", "商旅"));
    Expect<ArgumentException>("invalid name", () => saves.Create(2, "traveller", "  "));
    Expect<ArgumentOutOfRangeException>("invalid slot", () => saves.Read(4));

    // schemaVersion 2：性别、面容款式、词条与命运。
    var v2 = saves.Create(3, "merchant", "云娘", "female", 1,
        new[] { "deft", "literate" }, "jadeloft");
    Check("v2 create roundtrip", v2.gender == "female" && v2.faceStyle == 1 &&
        v2.traits.Length == 2 && v2.destiny == "jadeloft" && v2.schemaVersion == 2);
    var loadedV2 = saves.Read(3).Data;
    Check("v2 persisted", loadedV2.gender == "female" && loadedV2.faceStyle == 1 &&
        loadedV2.traits.Contains("deft") && loadedV2.destiny == "jadeloft");
    Expect<ArgumentException>("invalid gender rejected", () => saves.Create(2, "farmer", "甲", "other", 0, null, null));
    Expect<ArgumentException>("invalid face style rejected", () => saves.Create(2, "farmer", "甲", "male", 9, null, null));
    Expect<ArgumentException>("unknown trait rejected", () => saves.Create(2, "farmer", "甲", "male", 0, new[] { "nope" }, null));
    Expect<ArgumentException>("unknown destiny rejected", () => saves.Create(2, "farmer", "甲", "male", 0, null, "nope"));
    Expect<ArgumentException>("too many traits rejected", () => saves.Create(2, "farmer", "甲", "male", 0,
        new[] { "deft", "literate", "strong" }, null));
    Check("trait pools sized", CharacterGen.Traits.Count >= 12 && CharacterGen.Destinies.Count >= 8);
    Check("rolled traits distinct", CharacterGen.RollTraits(new Random(7)).Distinct().Count() == CharacterGen.TraitsPerCharacter);

    // v1 档位读取即迁移：缺省字段取默认值，写回时升级为 v2。
    File.WriteAllText(Path.Combine(root, "slot-2.json"),
        "{\"schemaVersion\":1,\"slot\":2,\"characterId\":\"farmer\",\"characterName\":\"阿禾\"," +
        "\"createdUtc\":\"2026-09-27T00:00:00.0000000+00:00\",\"updatedUtc\":\"2026-09-27T00:00:00.0000000+00:00\"}");
    var migrated = saves.Read(2).Data;
    Check("v1 loads with v2 defaults", saves.Read(2).State == SlotState.Ready &&
        migrated.gender == "male" && migrated.faceStyle == 0 && migrated.traits.Length == 0);
    saves.Write(migrated);
    Check("v1 upgraded to v2 on write", saves.Read(2).Data.schemaVersion == 2);

    first.facing = 2;
    saves.Write(first);
    File.WriteAllText(Path.Combine(root, "slot-1.json"), "{ broken");
    Check("backup recovery", saves.Read(1).State == SlotState.Recovered);
    Check("backup contains previous valid state", saves.Read(1).Data.facing == 0);
    saves.Write(saves.Read(1).Data);
    Check("recovery rewrites primary", saves.Read(1).State == SlotState.Ready);
    Check("older backup survives recovery", File.Exists(Path.Combine(root, "slot-1.json.bak")));

    File.WriteAllText(Path.Combine(root, "slot-3.json"), "{\"schemaVersion\":3,\"slot\":3}");
    Check("future save protected", saves.Read(3).State == SlotState.FutureVersion);
    Expect<InvalidOperationException>("future save cannot be recreated", () => saves.Create(3, "farmer", "甲"));
    saves.Delete(3);
    Check("explicit delete clears future", saves.Read(3).State == SlotState.Empty);

    var initialSettings = saves.LoadSettings(out var settingsState);
    Check("new settings use defaults", settingsState == SettingsLoadState.Default && initialSettings.width == 1600);
    var settings = new UserSettings { masterVolume = 7, musicVolume = float.NaN, width = 100 };
    saves.SaveSettings(settings);
    var loaded = saves.LoadSettings(out settingsState);
    Check("settings normalization", settingsState == SettingsLoadState.Ready &&
        loaded.masterVolume == 1 && loaded.musicVolume == .5f && loaded.width == 1600);
    saves.SaveSettings(new UserSettings { masterVolume = .25f });
    File.WriteAllText(Path.Combine(root, "settings.json"), "garbage");
    loaded = saves.LoadSettings(out settingsState);
    Check("settings backup recovery", settingsState == SettingsLoadState.Recovered && loaded.masterVolume == 1);
    saves.SaveSettings(loaded);
    Check("recovery keeps valid backup", saves.LoadSettings(out settingsState).masterVolume == 1 &&
        new JsonCodec().Deserialize<UserSettings>(File.ReadAllText(Path.Combine(root, "settings.json.bak"))).masterVolume == 1);
    File.WriteAllText(Path.Combine(root, "settings.json"), "{\"schemaVersion\":2}");
    saves.LoadSettings(out settingsState);
    Check("future settings identified", settingsState == SettingsLoadState.FutureVersion);
    Expect<InvalidOperationException>("future settings protected", () => saves.SaveSettings(new UserSettings()));
    File.Delete(Path.Combine(root, "settings.json.bak"));
    File.WriteAllText(Path.Combine(root, "settings.json"), "garbage");
    loaded = saves.LoadSettings(out settingsState);
    Check("invalid settings reset", settingsState == SettingsLoadState.Reset && loaded.width == 1600);
    if (OperatingSystem.IsWindows())
    {
        var primaryPath = Path.Combine(root, "slot-1.json");
        var backupPath = primaryPath + ".bak";
        File.WriteAllText(primaryPath, "{ broken");
        using (var locked = new FileStream(primaryPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Expect<IOException>("locked primary cannot be deleted", () => saves.Delete(1));
            Check("failed delete preserves recovery", File.Exists(backupPath) &&
                saves.Read(1).State == SlotState.Recovered);
        }
    }
    saves.Delete(1);
    Check("delete removes backup", saves.Read(1).State == SlotState.Empty);
    Console.WriteLine("All save checks passed.");
}
finally { Directory.Delete(root, true); }

static void Check(string name, bool condition)
{
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
}

static void Expect<T>(string name, Action action) where T : Exception
{
    try { action(); }
    catch (T) { Console.WriteLine("PASS: " + name); return; }
    throw new Exception("FAIL: " + name);
}

sealed class JsonCodec : IJsonCodec
{
    static readonly JsonSerializerOptions Options = new() { IncludeFields = true, WriteIndented = true };
    public string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public T Deserialize<T>(string json) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(json, Options); }
        catch (JsonException ex) { throw new FormatException("Invalid JSON", ex); }
    }
}
