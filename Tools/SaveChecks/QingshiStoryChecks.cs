using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using TwelveJade.Core;

static class QingshiStoryChecks
{
    static int assertions;
    static readonly JsonSerializerOptions JsonOptions = new() { IncludeFields = true };
    static readonly string[] Ids = { "home", "livelihood", "tiger", "undercurrent", "tomb", "calamity", "departure" };
    static readonly string[] JourneyNpcIds = { "npc-shitou", "npc-zhao-da", "npc-shitou", "npc-shitou", "", "", "" };
    static string[] townNpcIds;
    static string Flag(string id) => "qingshi-v25-" + id;
    static string Snapshot(SaveData save) => JsonSerializer.Serialize(save, JsonOptions);

    // Compare every saved field except the only allowed story/persistence changes.
    static string Assets(SaveData save)
    {
        var node = JsonSerializer.SerializeToNode(save, JsonOptions).AsObject();
        foreach (string key in new[] { "oneTimeFlags", "updatedUtc", "schemaVersion", "bagClipped" }) node.Remove(key);
        return node.ToJsonString();
    }

    static void Check(string label, bool condition)
    {
        assertions++;
        if (!condition) throw new Exception("Qingshi story: " + label);
    }

    public static void Run(string configDir)
    {
        assertions = 0;
        var items = ItemTable.Parse(File.ReadAllText(Path.Combine(configDir, "items.json")));
        CatalogAndQueries();
        JourneyQueries(configDir);
        RejectedInputs();
        foreach (bool legacy in new[] { false, true })
            for (int gateMode = 0; gateMode < 4; gateMode++) WalkRealSave(items, legacy, gateMode);
        FlagCapacity(items);
        PersistenceFailures(items);
        Console.WriteLine($"PASS: QingshiStory {assertions} assertions (read-only journey/NPC leads, real SaveRepository roundtrips, rollback and capacity)");
    }

    static void CatalogAndQueries()
    {
        Check("fixed seven-scene order", QingshiStory.Scenes.Select(s => s.Id).SequenceEqual(Ids));
        Check("three acts", QingshiStory.Scenes.Select(s => s.Act).SequenceEqual(new[] { 1, 1, 2, 2, 3, 3, 3 }));
        Check("scene copy is present", QingshiStory.Scenes.All(s => !string.IsNullOrWhiteSpace(s.Title) && !string.IsNullOrWhiteSpace(s.Summary)));
        bool immutable = false;
        try { ((IList<QingshiStoryScene>)QingshiStory.Scenes)[0] = QingshiStory.Scenes[1]; }
        catch (NotSupportedException) { immutable = true; }
        Check("catalog cannot be overwritten by its consumer", immutable && QingshiStory.Scenes[0].Id == "home");
        Check("null has no current scene", QingshiStory.CurrentIndex(null) == -1);
        Check("null cannot enter, replay or complete", !QingshiStory.CanEnter(null, "home") && !QingshiStory.CanReplay(null, "home") && !QingshiStory.IsCompleted(null, "home"));
        Check("null completion refused", !QingshiStory.Complete(null, "home", _ => throw new Exception("unexpected persist"), out var reason) && reason.Length > 0);

        var raw = new SaveData { oneTimeFlags = null, chapter = null, bag = null };
        string before = Snapshot(raw);
        Check("missing optional fields start at home", QingshiStory.CurrentIndex(raw) == 0 && QingshiStory.CanEnter(raw, "home"));
        Check("new character has no replays", Ids.All(id => !QingshiStory.CanReplay(raw, id) && !QingshiStory.IsCompleted(raw, id)));
        Check("queries do not normalize or mutate save", Snapshot(raw) == before);

        var sparse = new SaveData { oneTimeFlags = new[] { Flag("tomb") } };
        before = Snapshot(sparse);
        Check("isolated future flag does not advance current scene", QingshiStory.CurrentIndex(sparse) == 0 && QingshiStory.CanEnter(sparse, "home"));
        Check("isolated future flag cannot unlock replay or entry", QingshiStory.IsCompleted(sparse, "tomb") && !QingshiStory.CanReplay(sparse, "tomb") && !QingshiStory.CanEnter(sparse, "tomb"));
        Check("sparse queries keep old flags", Snapshot(sparse) == before);
    }

    static void JourneyQueries(string configDir)
    {
        var town = TownMap.Parse(File.ReadAllText(Path.Combine(configDir, "town-map.json")));
        townNpcIds = town.Npcs.Select(n => n.Id).ToArray();
        var properties = typeof(QingshiStoryLead).GetProperties();
        Check("journey lead is a sealed read-only contract", typeof(QingshiStoryLead).IsSealed &&
            typeof(QingshiStoryLead).GetFields().Length == 0 && properties.All(p => p.CanRead && p.GetSetMethod(true) == null));
        Check("journey lead exposes exactly the agreed fields", properties.Select(p => p.Name).OrderBy(n => n).SequenceEqual(
            new[] { "SceneId", "NpcId", "NpcName", "Objective", "Bridge", "Reply", "Marker" }.OrderBy(n => n)) &&
            properties.All(p => p.PropertyType == (p.Name == "Marker" ? typeof(QuestOverheadState) : typeof(string))));

        JourneyReadOnly(null, -1, false, "journey no selected save");
        JourneyReadOnly(new SaveData(), 0, true, "journey empty save");
        var raw = new SaveData { schemaVersion = 2, oneTimeFlags = null, chapter = null, quests = null,
            npcs = null, bag = null, clues = null, merchants = null, updatedUtc = "untouched-old-date" };
        JourneyReadOnly(raw, 0, true, "journey missing legacy fields");

        // All combinations include holes and orphaned future flags, not just a contiguous prefix.
        for (int mask = 0; mask < 1 << Ids.Length; mask++)
        {
            int firstMissing = 0;
            while (firstMissing < Ids.Length && (mask & (1 << firstMissing)) != 0) firstMissing++;
            var sparse = new SaveData {
                oneTimeFlags = Ids.Where((id, i) => (mask & (1 << i)) != 0).Select(Flag)
                    .Concat(new[] { PrologueRules.DepartureFlag, PrologueRules.InitiationFlag }).ToArray(),
                chapter = new ChapterState { reached = (int)ChapterSegment.Departure, settled = new[] { "Prologue", "Departure" } }
            };
            JourneyReadOnly(sparse, firstMissing, true, "journey sparse mask=" + mask);
        }

        var leads = Ids.Select((id, i) => QingshiStoryJourney.Current(new SaveData {
            oneTimeFlags = Ids.Take(i).Select(Flag).ToArray()
        })).ToArray();
        for (int i = 0; i < leads.Length; i++)
        {
            var lead = leads[i];
            string copy = lead.Objective + lead.Bridge + lead.Reply;
            Check(lead.SceneId + " lead has no premature tragic revelation",
                new[] { "夺舍", "弑亲", "殉情", "红尘仙", "血誓", "阴谋", "黑匣", "木匣", "古冢", "情血" }.All(word => !copy.Contains(word)));
            if (i < 4)
            {
                var actors = town.Npcs.Where(n => n.Id == lead.NpcId).ToArray();
                Check(lead.SceneId + " resolves one actual town-map NPC", actors.Length == 1);
                Check(lead.SceneId + " displayed name and objective match actual NPC",
                    lead.NpcName == actors[0].Name && lead.Objective.Contains(actors[0].Name));
            }
            else
                Check(lead.SceneId + " declares spectator view without a living-NPC errand",
                    lead.NpcId == "" && lead.NpcName == "" && lead.Marker == QuestOverheadState.None &&
                    QingshiStoryJourney.IsContinuous(new SaveData { oneTimeFlags = Ids.Take(i).Select(Flag).ToArray() }) && lead.Bridge.Contains("玩家") &&
                    lead.Bridge.Contains("主角") && lead.Bridge.Contains("不知"));
        }
        Check("home invitation is a coming-of-age return", leads[0].Objective.Contains("回家") && leads[0].Bridge.Contains("加冠"));
        Check("livelihood asks for medicine-money work", leads[1].Objective.Contains("药钱") && leads[1].Bridge.Contains("帮手"));
        Check("tiger invites a companion to the back mountain", leads[2].Objective.Contains("后山") &&
            leads[2].Objective.Contains("虎患") && leads[2].Bridge.Contains("同行"));
        Check("undercurrent stays with visible post-hunt concerns", leads[3].Objective.Contains("围虎") &&
            leads[3].Bridge.Contains("牲口") && leads[3].Bridge.Contains("押运") && leads[3].Bridge.Contains("点名"));

        for (int i = 0; i < Ids.Length; i++)
            foreach (int total in new[] { 30, 31, 32, 33 })
            {
                var full = new SaveData { oneTimeFlags = Ids.Take(i).Select(Flag).Concat(FillFlags(total - i)).ToArray() };
                bool fits = total + (i == Ids.Length - 1 ? 2 : 1) <= 32;
                JourneyReadOnly(full, i, fits, "journey capacity=" + total + "/" + Ids[i]);
                var lead = QingshiStoryJourney.Current(full);
                Check(Ids[i] + " full ledger keeps its entire visible lead", lead.Objective == leads[i].Objective &&
                    lead.Bridge == leads[i].Bridge && lead.Reply == leads[i].Reply && lead.NpcName == leads[i].NpcName);
                Check(Ids[i] + " journey capacity matches completion preflight", QingshiStory.CanEnter(full, Ids[i]) == fits);
                Check(Ids[i] + " blocked query never alters shared available lead", QingshiStoryJourney.Current(new SaveData {
                    oneTimeFlags = Ids.Take(i).Select(Flag).ToArray()
                }).Marker == leads[i].Marker);
            }
        foreach (string[] flags in new[] { new string[] { null }, new[] { "" }, new[] { " " }, new[] { "old", "old" }, new[] { new string('x', 49) } })
            JourneyReadOnly(new SaveData { oneTimeFlags = flags }, 0, false, "journey invalid flag ledger");
        // Completed story without the old departure flag still offers no lead and does not repair it.
        JourneyReadOnly(new SaveData { oneTimeFlags = Ids.Select(Flag).ToArray() }, Ids.Length, false, "journey completed/replay only");
    }

    static void JourneyReadOnly(SaveData save, int expectedIndex, bool canEnter, string label)
    {
        string before = Snapshot(save);
        var references = save == null ? null : typeof(SaveData).GetFields()
            .Where(f => !f.IsStatic && !f.FieldType.IsValueType).ToDictionary(f => f, f => f.GetValue(save));
        var lead = QingshiStoryJourney.Current(save);
        bool active = expectedIndex >= 0 && expectedIndex < Ids.Length;
        string npcId = active ? JourneyNpcIds[expectedIndex] : "";
        var marker = active && expectedIndex < 4 && canEnter
            ? (expectedIndex == 3 ? QuestOverheadState.Completable : QuestOverheadState.Available) : QuestOverheadState.None;
        Check(label + " lead exists only for an unfinished save", (lead != null) == active);
        if (active)
        {
            Check(label + " follows first missing scene", lead.SceneId == Ids[expectedIndex] && lead.NpcId == npcId);
            Check(label + " current marker respects actionability", lead.Marker == marker);
            Check(label + " visible copy remains present", !string.IsNullOrWhiteSpace(lead.Objective) &&
                !string.IsNullOrWhiteSpace(lead.Bridge) && !string.IsNullOrWhiteSpace(lead.Reply));
        }
        Check(label + " continuous only for the last three current scenes",
            QingshiStoryJourney.IsContinuous(save) == (expectedIndex >= 4 && expectedIndex <= 6));
        foreach (string candidate in townNpcIds.Concat(new[] { null, "", " ", "shitou", "zhao-da", "NPC-SHITOU", "npc-shitou ", " npc-zhao-da", "missing-npc" }))
            Check(label + " overhead for " + (candidate ?? "<null>"), QingshiStoryJourney.Overhead(save, candidate) ==
                (!string.IsNullOrEmpty(npcId) && candidate == npcId ? marker : QuestOverheadState.None));
        foreach (string replayId in Ids.Where(id => QingshiStory.CanReplay(save, id)))
        {
            var afterReplayQuery = QingshiStoryJourney.Current(save);
            Check(label + " replay query keeps current lead " + replayId,
                afterReplayQuery?.SceneId == lead?.SceneId && afterReplayQuery?.Marker == lead?.Marker);
        }
        Check(label + " every saved field stays unchanged", Snapshot(save) == before);
        Check(label + " no null-field repair or reference replacement", references == null ||
            references.All(pair => ReferenceEquals(pair.Value, pair.Key.GetValue(save))));
    }

    static void JourneyOnDisk(Store store, SaveData save, int index, string label)
    {
        string file = store.FileFor(1), backupFile = file + ".bak";
        string disk = File.ReadAllText(file), backup = File.Exists(backupFile) ? File.ReadAllText(backupFile) : null;
        var touched = File.GetLastWriteTimeUtc(file);
        int writes = store.Codec.Writes;
        JourneyReadOnly(save, index, true, label + " persisted journey");
        Check(label + " journey never writes or touches disk", writes == store.Codec.Writes &&
            File.ReadAllText(file) == disk && File.GetLastWriteTimeUtc(file) == touched &&
            (File.Exists(backupFile) ? File.ReadAllText(backupFile) : null) == backup);
    }

    static void RejectedInputs()
    {
        var save = new SaveData();
        foreach (string id in new[] { null, "", " ", "HOME", " home", "home ", "qingshi-v25-home", "not-a-scene" })
        {
            Check("unknown scene query " + id, !QingshiStory.IsCompleted(save, id) && !QingshiStory.CanEnter(save, id) && !QingshiStory.CanReplay(save, id));
            RefusesWithoutMutation(save, id, "unknown " + id);
        }
        foreach (string id in Ids.Skip(1)) RefusesWithoutMutation(save, id, "out of order " + id);
        string before = Snapshot(save);
        Check("missing persistence refused", !QingshiStory.Complete(save, "home", null, out var reason) && reason.Length > 0 && Snapshot(save) == before);
        foreach (string[] flags in new[] { new[] { "" }, new[] { "  " }, new[] { "old", "old" }, new[] { new string('x', 49) } })
        {
            var invalid = new SaveData { oneTimeFlags = flags };
            Check("invalid flags cannot enter", !QingshiStory.CanEnter(invalid, "home"));
            RefusesWithoutMutation(invalid, "home", "invalid flags");
        }

        // A completed replay must not repair missing departure or award anything.
        save.oneTimeFlags = Ids.Select(Flag).ToArray();
        Check("all story flags finish the independent axis", QingshiStory.CurrentIndex(save) == Ids.Length);
        foreach (string id in Ids) RefusesWithoutMutation(save, id, "completed without departure " + id);
        Check("replay never synthesizes departure or initiation", !save.HasFlag(PrologueRules.DepartureFlag) && !save.HasFlag(PrologueRules.InitiationFlag));
    }

    static void RefusesWithoutMutation(SaveData save, string id, string label)
    {
        string before = Snapshot(save);
        var flags = save.oneTimeFlags;
        int writes = 0;
        Check(label + " refused with reason", !QingshiStory.Complete(save, id, _ => writes++, out var reason) && reason.Length > 0);
        Check(label + " no write or mutation", writes == 0 && ReferenceEquals(flags, save.oneTimeFlags) && Snapshot(save) == before);
    }

    static void WalkRealSave(ItemTable items, bool legacy, int gateMode)
    {
        using var store = new Store(items);
        var save = store.Repo.Create(1, "farmer", legacy ? "旧行路人" : "新行路人");
        store.Repo.Create(2, "farmer", "旁档");
        string otherSlot = File.ReadAllText(store.FileFor(2));
        if (legacy) GiveLegacyRecords(save, items);
        if ((gateMode & 1) != 0) save.MarkFlag(PrologueRules.DepartureFlag);
        if ((gateMode & 2) != 0) save.MarkFlag(PrologueRules.InitiationFlag);
        store.Repo.Write(save);
        if (legacy)
        {
            // Existing v10 records must survive migration and the new independent story axis.
            var node = JsonNode.Parse(File.ReadAllText(store.FileFor(1))).AsObject();
            node["schemaVersion"] = 10;
            File.WriteAllText(store.FileFor(1), node.ToJsonString());
        }
        var loaded = store.Repo.Read(1);
        Check($"fixture loads legacy={legacy}, flags={gateMode}", loaded.CanLoad && loaded.Data.schemaVersion == (legacy ? 10 : SaveData.CurrentSchemaVersion));
        save = loaded.Data;
        string assets = Assets(save);
        var expectedFlags = save.oneTimeFlags.ToList();
        bool initiatedBefore = (gateMode & 2) != 0;
        bool departedBefore = (gateMode & 1) != 0;
        Check("old chapters and departure do not auto-complete V2.5", QingshiStory.CurrentIndex(save) == 0);

        for (int index = 0; index < Ids.Length; index++)
        {
            string id = Ids[index];
            string label = $"legacy={legacy}, flags={gateMode}, {id}";
            var chapter = save.chapter;
            JourneyOnDisk(store, save, index, label);
            Check(label + " only next scene may enter", QingshiStory.CurrentIndex(save) == index && Ids.All(x => QingshiStory.CanEnter(save, x) == (x == id)));
            Check(label + " replays limited to completed prefix", Ids.Select((x, i) => QingshiStory.CanReplay(save, x) == (i < index)).All(x => x));
            if (index + 1 < Ids.Length) RefusesWithoutMutation(save, Ids[index + 1], label + " skip");
            int writes = store.Codec.Writes;
            Check(label + " completed and persisted", QingshiStory.Complete(save, id, store.Repo.Write, out var reason) && reason == "" && store.Codec.Writes == writes + 1);
            expectedFlags.Add(Flag(id));
            if (index == Ids.Length - 1 && !departedBefore) expectedFlags.Add(PrologueRules.DepartureFlag);
            Check(label + " exact flags, no invented rewards", save.oneTimeFlags.SequenceEqual(expectedFlags));
            Check(label + " all assets and old chapter untouched in memory", Assets(save) == assets && ReferenceEquals(chapter, save.chapter));
            Check(label + " old initiation preserved, never newly awarded", save.HasFlag(PrologueRules.InitiationFlag) == initiatedBefore);
            Check(label + " departure only after final scene", save.HasFlag(PrologueRules.DepartureFlag) == (departedBefore || index == Ids.Length - 1));
            Check(label + " cultivation still requires both actual flags", PrologueRules.CanCultivate(save) == (initiatedBefore && (departedBefore || index == Ids.Length - 1)));
            Check(label + " old learned manual never bypasses gate", ManualStudyRules.Learned(save, "sword") == (legacy && PrologueRules.CanCultivate(save)));

            loaded = store.Repo.Read(1);
            Check(label + " real disk load preserves every asset field", loaded.CanLoad && Assets(loaded.Data) == assets && loaded.Data.oneTimeFlags.SequenceEqual(expectedFlags));
            save = loaded.Data;
            Check(label + " loaded next index", QingshiStory.CurrentIndex(save) == index + 1 && QingshiStory.IsCompleted(save, id) && QingshiStory.CanReplay(save, id));
            string disk = File.ReadAllText(store.FileFor(1));
            writes = store.Codec.Writes;
            string before = Snapshot(save);
            Check(label + " repeated completion refused", !QingshiStory.Complete(save, id, store.Repo.Write, out reason) && reason.Length > 0);
            Check(label + " repeat does not write, grant or alter timestamp", store.Codec.Writes == writes && Snapshot(save) == before && File.ReadAllText(store.FileFor(1)) == disk);
            JourneyOnDisk(store, save, index + 1, label + " replay");
        }
        Check("finished axis only offers replays", Ids.All(id => !QingshiStory.CanEnter(save, id) && QingshiStory.CanReplay(save, id)));
        Check("at most eight new flags", expectedFlags.Count - (legacy ? 2 : 0) - ((gateMode & 1) != 0 ? 1 : 0) - ((gateMode & 2) != 0 ? 1 : 0) == (departedBefore ? 7 : 8));
        Check("other save slot unchanged", File.ReadAllText(store.FileFor(2)) == otherSlot);
    }

    static void GiveLegacyRecords(SaveData save, ItemTable items)
    {
        save.coins = 4567;
        save.localReputation = 17;
        save.worldDay = 9;
        save.worldMinuteOfDay = 780;
        save.facing = 2;
        save.MarkFlag("legacy-family-spared");
        save.MarkFlag("encounter-boar-01");
        save.chapter = new ChapterState { reached = (int)ChapterSegment.Departure, settled = new[] { "Prologue", "Arrival", "FreeRoam", "Departure" } };
        foreach (string id in new[] { "dark-iron-armor", "manual-sword-fragment", "red-dew-pill", "iron-ore" })
        {
            Check("legacy item still defined " + id, items.Find(id) != null);
            InventoryRules.Add(save.bag, items, id, 1);
        }
        save.gear = new[] { new GearLine { slot = GearSlots.Armor, itemId = "dark-iron-armor", durability = 83 } };
        save.crafts = new[] { new CraftLine { recipeId = "rou-fu", times = 4 } };
        save.cultivation = new CultivationState { opened = 3, attempts = 5, failures = 2 };
        save.manualStudies = new[] { new ManualStudyEntry { id = "sword", learned = true, recorded = true, attempts = 2, insights = 1, progress = 100, notes = 3 } };
        save.merchants = new[] { new MerchantState { id = "yaolao", trades = 7, goodwill = 14, stock = new[] { new StockLine { itemId = "jinchuangyao", count = 0 } }, hidden = new[] { new StockLine { itemId = "red-dew-pill", count = 0 } } } };
        save.quests = new[] { new QuestState { id = "legacy-family-choice", status = QuestLedger.StatusCompleted, progress = new[] { 1 } } };
        save.npcs = new[] { new NpcState { id = "shitou", relation = new NpcRelation { goodwill = 30, trust = 20 }, memory = new[] { new NpcMemoryEntry { eventId = "legacy-help", day = 2, minuteOfDay = 600, strength = 3, spoken = true } } } };
        save.clues = new[] { new ClueEntry { id = "legacy-clue", day = 2, minuteOfDay = 700, read = true } };
        save.dialogue = new DialogueState { npcId = "shitou", nodeId = "legacy-resume", startedDay = 2, startedMinuteOfDay = 700 };
        save.karmaEvents = new[] { new KarmaEntry { eventId = "legacy-help", kind = KarmaKinds.Deed, day = 2, minuteOfDay = 600, times = 2 } };
        save.mining = new[] { new MiningEntry { nodeId = "legacy-ore", day = 2 } };
    }

    static string[] FillFlags(int count) => Enumerable.Range(0, count).Select(i => "existing-" + i).ToArray();

    static void FlagCapacity(ItemTable items)
    {
        using var store = new Store(items);
        var save = store.Repo.Create(1, "farmer", "满旗客");
        save.oneTimeFlags = FillFlags(24);
        store.Repo.Write(save);
        foreach (string id in Ids)
        {
            Check("eight free slots finish entire story " + id, QingshiStory.Complete(save, id, store.Repo.Write, out _));
            var read = store.Repo.Read(1);
            Check("capacity boundary reloads " + id, read.CanLoad && read.Data.oneTimeFlags.SequenceEqual(save.oneTimeFlags));
            save = read.Data;
        }
        Check("exactly 32 flags with original 24 retained", save.oneTimeFlags.Length == 32 && save.oneTimeFlags.Take(24).SequenceEqual(FillFlags(24)) && !PrologueRules.CanCultivate(save));
        RefusesWithoutMutation(save, "departure", "full save replay");

        foreach (int total in new[] { 30, 31, 32 })
            foreach (bool departureAlready in new[] { false, true })
            {
                save = store.Repo.Create(3, "farmer", "末段客");
                save.oneTimeFlags = Ids.Take(6).Select(Flag)
                    .Concat(departureAlready ? new[] { PrologueRules.DepartureFlag } : Array.Empty<string>())
                    .Concat(FillFlags(total - 6 - (departureAlready ? 1 : 0))).ToArray();
                store.Repo.Write(save);
                bool fits = total + (departureAlready ? 1 : 2) <= 32;
                Check($"departure preflight total={total}, old={departureAlready}", QingshiStory.CanEnter(save, "departure") == fits);
                string before = Snapshot(save), disk = File.ReadAllText(store.FileFor(3));
                int writes = store.Codec.Writes;
                Check("capacity matches completion", QingshiStory.Complete(save, "departure", store.Repo.Write, out _) == fits);
                if (fits)
                    Check("both final flags present and deduplicated", save.HasFlag(Flag("departure")) && save.HasFlag(PrologueRules.DepartureFlag) && save.oneTimeFlags.Length == total + (departureAlready ? 1 : 2) && store.Repo.Read(3).CanLoad);
                else
                    Check("insufficient space cannot partially depart", Snapshot(save) == before && File.ReadAllText(store.FileFor(3)) == disk && store.Codec.Writes == writes);
                store.Repo.Delete(3);
            }
        save = new SaveData { oneTimeFlags = FillFlags(32) };
        Check("full flags close entry", !QingshiStory.CanEnter(save, "home"));
        RefusesWithoutMutation(save, "home", "full flags");
        save.oneTimeFlags = FillFlags(33);
        RefusesWithoutMutation(save, "home", "over capacity flags");
        save.oneTimeFlags = FillFlags(31);
        Check("single-step preflight uses its actual one slot", QingshiStory.Complete(save, "home", _ => { }, out _) && save.oneTimeFlags.Length == 32);
        RefusesWithoutMutation(save, "livelihood", "next step cannot overwrite old flag");
    }

    static void PersistenceFailures(ItemTable items)
    {
        foreach (bool final in new[] { false, true })
        {
            using var store = new Store(items);
            var save = store.Repo.Create(1, "farmer", "回滚客");
            GiveLegacyRecords(save, items);
            save.oneTimeFlags = save.oneTimeFlags.Concat(final ? Ids.Take(6).Select(Flag) : Array.Empty<string>()).ToArray();
            store.Repo.Write(save);
            string id = final ? "departure" : "home";
            var flags = save.oneTimeFlags;
            string before = Snapshot(save), disk = File.ReadAllText(store.FileFor(1)), backup = File.ReadAllText(store.FileFor(1) + ".bak");
            store.Codec.FailWrites = true;
            Check("serialization failure returns refusal " + id, !QingshiStory.Complete(save, id, store.Repo.Write, out var reason) && reason.Length > 0);
            Check("serialization failure fully rolls back memory " + id, Snapshot(save) == before && ReferenceEquals(flags, save.oneTimeFlags));
            Check("failed save preserves primary and backup " + id, File.ReadAllText(store.FileFor(1)) == disk && File.ReadAllText(store.FileFor(1) + ".bak") == backup && store.Repo.Read(1).CanLoad);
            store.Codec.FailWrites = false;
            Check("same scene can retry after failure " + id, QingshiStory.CanEnter(save, id) && QingshiStory.Complete(save, id, store.Repo.Write, out _) && store.Repo.Read(1).Data.HasFlag(Flag(id)));
        }

        using (var store = new Store(items))
        {
            store.Repo.Create(1, "farmer", "旧空档");
            var raw = new SaveData { slot = 1, characterId = "farmer", characterName = "旧空档", schemaVersion = 10,
                createdUtc = "2026-11-20T00:00:00Z", updatedUtc = "2026-11-20T00:00:00Z",
                oneTimeFlags = null, chapter = null, clues = null, merchants = null, bag = null, karmaEvents = null, gear = null, crafts = null };
            string before = Snapshot(raw), disk = File.ReadAllText(store.FileFor(1));
            bool normalizedBeforeFault = false;
            store.Codec.BeforeWrite = saved => normalizedBeforeFault = saved.schemaVersion == SaveData.CurrentSchemaVersion && saved.updatedUtc != raw.createdUtc && saved.chapter != null && saved.bag != null && saved.HasFlag(Flag("home"));
            store.Codec.FailWrites = true;
            Check("repository normalization really ran before failure", !QingshiStory.Complete(raw, "home", store.Repo.Write, out _) && normalizedBeforeFault);
            Check("null fields, schema, flags and timestamp all restored", Snapshot(raw) == before && File.ReadAllText(store.FileFor(1)) == disk);
            store.Codec.FailWrites = false;
            store.Codec.BeforeWrite = null;
            Check("old null-state save retries through repository", QingshiStory.Complete(raw, "home", store.Repo.Write, out _) && store.Repo.Read(1).CanLoad);
        }

        using (var store = new Store(items))
        {
            var save = store.Repo.Create(1, "farmer", "磁盘回滚");
            save.oneTimeFlags = Ids.Take(6).Select(Flag).ToArray();
            store.Repo.Write(save);
            string before = Snapshot(save), disk = File.ReadAllText(store.FileFor(1)), backup = File.ReadAllText(store.FileFor(1) + ".bak");
            int writes = store.Codec.Writes;
            // Deny the actual AtomicWrite temp file, after serialization and timestamp update.
            using (var held = new FileStream(store.FileFor(1) + ".tmp", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                Check("real filesystem failure refuses final scene", !QingshiStory.Complete(save, "departure", store.Repo.Write, out _) && store.Codec.Writes == writes + 1);
            Check("filesystem failure rolls back both final flags and every field", Snapshot(save) == before && !save.HasFlag(PrologueRules.DepartureFlag));
            Check("filesystem failure leaves primary and backup intact", File.ReadAllText(store.FileFor(1)) == disk && File.ReadAllText(store.FileFor(1) + ".bak") == backup && store.Repo.Read(1).CanLoad);
            Check("filesystem retry writes both final flags", QingshiStory.Complete(save, "departure", store.Repo.Write, out _) && store.Repo.Read(1).Data.HasFlag(PrologueRules.DepartureFlag));
        }
    }

    sealed class Codec : IJsonCodec
    {
        public int Writes;
        public bool FailWrites;
        public Action<SaveData> BeforeWrite;
        public string Serialize<T>(T value)
        {
            Writes++;
            if (value is SaveData save) BeforeWrite?.Invoke(save);
            if (FailWrites) throw new IOException("Injected story save failure");
            return JsonSerializer.Serialize(value, JsonOptions);
        }
        public T Deserialize<T>(string json) where T : class => JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    sealed class Store : IDisposable
    {
        readonly string tempBase = Path.GetFullPath(Path.GetTempPath());
        readonly string root;
        public readonly Codec Codec = new();
        public readonly SaveRepository Repo;
        public Store(ItemTable items)
        {
            root = Path.GetFullPath(Path.Combine(tempBase, "twelve-jade-story-checks-" + Guid.NewGuid().ToString("N")));
            Repo = new SaveRepository(root, Codec) { Items = items };
        }
        public string FileFor(int slot) => Path.Combine(root, $"slot-{slot}.json");
        public void Dispose()
        {
            // Only remove this fixture's resolved unique directory, never a user save root.
            if (!string.Equals(Path.GetDirectoryName(root), tempBase.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(root).StartsWith("twelve-jade-story-checks-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unsafe story fixture cleanup path.");
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
