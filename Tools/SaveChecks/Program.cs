using System.Text.Json;
using TwelveJade.Core;

// 游戏工程的正式配置表也要过一遍：改坏了必须在这里先报警。
ConfigChecks.Run();

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
        v2.traits.Length == 2 && v2.destiny == "jadeloft" && v2.schemaVersion == SaveData.CurrentSchemaVersion);
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

    // schemaVersion 3：行囊、铜钱与地方声望；新档一落地就带着开局行囊。
    Check("new save carries the starting kit", first.coins == ItemTable.StartingCoins &&
        InventoryRules.Count(first.bag, "ganliang") == 5 && InventoryRules.UsedSlots(first.bag) == 4 &&
        first.localReputation == 0);
    var kitSave = saves.Read(1).Data;
    Check("starting kit persisted", kitSave.bag.Length == InventoryRules.SlotCount &&
        InventoryRules.Count(kitSave.bag, "jinchuangyao") == 2);
    kitSave.coins = SaveData.MaxCoins + 1;
    Expect<ArgumentException>("illegal coin count rejected", () => saves.Write(kitSave));
    kitSave.coins = -1;
    Expect<ArgumentException>("negative coins rejected", () => saves.Write(kitSave));
    kitSave.coins = 42;
    kitSave.localReputation = SaveData.MaxReputation + 1;
    Expect<ArgumentException>("illegal reputation rejected", () => saves.Write(kitSave));
    kitSave.localReputation = 15;
    kitSave.bag = InventoryRules.NewBag();
    kitSave.bag[0] = new ItemStack("ganliang", 0);
    Expect<ArgumentException>("empty stack with an id rejected", () => saves.Write(kitSave));
    kitSave.bag = InventoryRules.NewBag();
    kitSave.bag[0] = new ItemStack("caoyao", ItemTable.MaxStack + 1);
    Expect<ArgumentException>("over-max stack rejected", () => saves.Write(kitSave));
    kitSave.bag = InventoryRules.NewBag();
    InventoryRules.Add(kitSave.bag, null, "caoyao", 300);
    Check("overflowing adds spread over slots", InventoryRules.IsValid(kitSave.bag) &&
        InventoryRules.Count(kitSave.bag, "caoyao") == 300);
    saves.Write(kitSave);
    var roundTripped = saves.Read(1).Data;
    Check("v3 roundtrip keeps coins, reputation and bag", roundTripped.schemaVersion == SaveData.CurrentSchemaVersion &&
        roundTripped.coins == 42 && roundTripped.localReputation == 15 &&
        InventoryRules.Count(roundTripped.bag, "caoyao") == 300);

    // 问命：答案加权但不决定结果；权重确定性可复现。
    var questions = FateDialogue.Questions;
    Check("fate dialogue shaped", questions.Length == 6 && questions.All(q => q.Options.Length is 3 or 4) &&
        questions.All(q => q.Options.All(o => o.Favor.Length >= 1 && !string.IsNullOrWhiteSpace(o.Reply))));
    Check("fate options reference real destinies", questions.SelectMany(q => q.Options)
        .SelectMany(o => o.Favor).All(id => CharacterGen.FindDestiny(id) != null));
    Check("every destiny favored by some answer", CharacterGen.Destinies.All(d =>
        questions.SelectMany(q => q.Options).Any(o => o.Favor.Contains(d.Id))));
    // 遍历全部答案组合：倾向的命运更占优，未倾向的命运也仍可能出现。
    IEnumerable<FateDialogue.Option[]> Enumerate()
    {
        IEnumerable<FateDialogue.Option[]> acc = new[] { Array.Empty<FateDialogue.Option>() };
        foreach (var question in questions)
            acc = acc.SelectMany(prefix => question.Options.Select(option => prefix.Append(option).ToArray()));
        return acc;
    }
    var combos = Enumerate().ToArray();
    Check("fate combos enumerated", combos.Length == questions.Aggregate(1, (n, q) => n * q.Options.Length));
    var favoredCounts = new System.Collections.Generic.Dictionary<string, int>();
    var unfavoredSeen = new System.Collections.Generic.HashSet<string>();
    int favoredTotal = 0, unfavoredTotal = 0;
    var simulation = new Random(20260927);
    foreach (var combo in combos)
    {
        var favored = combo.SelectMany(o => o.Favor).Distinct().ToArray();
        var unfavored = CharacterGen.Destinies.Select(d => d.Id).Where(id => !favored.Contains(id)).ToArray();
        for (var i = 0; i < 400; i++)
        {
            var id = FateDialogue.RollDestiny(favored, simulation);
            if (favored.Contains(id)) { favoredCounts[id] = favoredCounts.GetValueOrDefault(id) + 1; favoredTotal++; }
            else { unfavoredSeen.Add(id); unfavoredTotal++; }
        }
    }
    Check("every destiny reachable", favoredCounts.Keys.Union(unfavoredSeen).Count() == CharacterGen.Destinies.Count);
    Check("favored answers dominate the roll", favoredTotal > unfavoredTotal);
    // 一路平庸作答（安乐命被四个答案叠加倾向）：对应命格显著抬升但不注定——约三成。
    var mediocreFavored = FateDialogue.Questions.SelectMany(q => q.Options)
        .Where(o => o.Favor.Contains("anle")).SelectMany(o => o.Favor).ToArray();
    var mediocreRolls = Enumerable.Range(0, 8000)
        .Select(i => FateDialogue.RollDestiny(mediocreFavored, new Random(i))).ToArray();
    var anleRate = (double)mediocreRolls.Count(id => id == "anle") / mediocreRolls.Length;
    Check("consistent answers raise destiny odds to a third, not a certainty", anleRate > .2 && anleRate < .5);
    var sampleFavored = new[] { "anle", "shaxing", "anle", "shaxing", "anle", "shaxing" };
    var seeded = Enumerable.Range(0, 50).Select(i => FateDialogue.RollDestiny(sampleFavored, new Random(i))).ToArray();
    Check("weighted roll deterministic", seeded.SequenceEqual(
        Enumerable.Range(0, 50).Select(i => FateDialogue.RollDestiny(sampleFavored, new Random(i)))));
    var favoredRolls = 0;
    var unfavoredRolls = 0;
    var probabilityProbe = new Random(20260928);
    for (var i = 0; i < 6000; i++)
    {
        if (sampleFavored.Contains(FateDialogue.RollDestiny(sampleFavored, probabilityProbe))) favoredRolls++;
        else unfavoredRolls++;
    }
    Check("favored destinies receive higher probability", favoredRolls > unfavoredRolls);

    // v1 档位读取即迁移：缺省字段取默认值，写回时升级为当前版本。
    File.WriteAllText(Path.Combine(root, "slot-2.json"),
        "{\"schemaVersion\":1,\"slot\":2,\"characterId\":\"farmer\",\"characterName\":\"阿禾\"," +
        "\"createdUtc\":\"2026-09-27T00:00:00.0000000+00:00\",\"updatedUtc\":\"2026-09-27T00:00:00.0000000+00:00\"}");
    var migrated = saves.Read(2).Data;
    Check("v1 loads with v2 defaults", saves.Read(2).State == SlotState.Ready &&
        migrated.gender == "male" && migrated.faceStyle == 0 && migrated.traits.Length == 0);
    Check("v1 receives the starting kit", migrated.coins == ItemTable.StartingCoins &&
        InventoryRules.Count(migrated.bag, "ganliang") == 5 && InventoryRules.UsedSlots(migrated.bag) == 4);
    saves.Write(migrated);
    Check("v1 upgraded to current schema on write", saves.Read(2).Data.schemaVersion == SaveData.CurrentSchemaVersion &&
        saves.Read(2).Data.coins == ItemTable.StartingCoins);

    // v2 档位（有命格、无行囊）读入时补开局行囊，写回时保留 v2 已有的人物内容。
    File.WriteAllText(Path.Combine(root, "slot-2.json"),
        "{\"schemaVersion\":2,\"slot\":2,\"characterId\":\"farmer\",\"characterName\":\"阿禾\"," +
        "\"gender\":\"female\",\"faceStyle\":1,\"traits\":[\"deft\"],\"destiny\":\"anle\"," +
        "\"createdUtc\":\"2026-09-27T00:00:00.0000000+00:00\",\"updatedUtc\":\"2026-09-27T00:00:00.0000000+00:00\"}");
    var upgraded = saves.Read(2).Data;
    Check("v2 keeps its character while gaining a bag", upgraded.gender == "female" && upgraded.faceStyle == 1 &&
        upgraded.destiny == "anle" && upgraded.coins == ItemTable.StartingCoins &&
        InventoryRules.UsedSlots(upgraded.bag) == 4);

    first = saves.Read(1).Data;
    first.facing = 2;
    saves.Write(first);
    File.WriteAllText(Path.Combine(root, "slot-1.json"), "{ broken");
    Check("backup recovery", saves.Read(1).State == SlotState.Recovered);
    Check("backup contains previous valid state", saves.Read(1).Data.facing == 0);
    saves.Write(saves.Read(1).Data);
    Check("recovery rewrites primary", saves.Read(1).State == SlotState.Ready);
    Check("older backup survives recovery", File.Exists(Path.Combine(root, "slot-1.json.bak")));

    File.WriteAllText(Path.Combine(root, "slot-3.json"),
        "{\"schemaVersion\":" + (SaveData.CurrentSchemaVersion + 1) + ",\"slot\":3}");
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
