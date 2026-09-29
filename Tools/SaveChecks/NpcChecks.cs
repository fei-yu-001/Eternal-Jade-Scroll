using System;
using System.IO;
using System.Linq;
using TwelveJade.Core;

namespace TwelveJade.Core
{
    // M5-03 NPC 人格/关系/目标/记忆：人格是有限枚举、关系只由 Core 事件改、
    // 记忆是结构化条目（可去重、可标注说过没有）、旧档与"表里没有的人"都不丢。
    public static class NpcChecks
    {
        static string tempRoot;

        public static void Run(ItemTable items, string configDir, TownMap town)
        {
            tempRoot = Path.Combine(Path.GetTempPath(), "twelve-jade-npc-" + Guid.NewGuid().ToString("N"));
            try { RunInner(items, configDir, town); }
            finally { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); }
        }

        static void RunInner(ItemTable items, string configDir, TownMap town)
        {
            var npcs = NpcTable.Parse(File.ReadAllText(Path.Combine(configDir, "npcs.json")));
            var peddler = npcs.Find("huolang");
            Check("npc table parses", npcs.Npcs.Count >= 1, npcs.Npcs.Count + " 个人物");
            Check("the peddler has a profile with a temper and goals",
                peddler != null && peddler.Goals.Count >= 1 && !string.IsNullOrWhiteSpace(peddler.Greeting));
            Check("temper is a fixed enum, not a free string", Enum.IsDefined(typeof(NpcTemper), peddler.Temper));
            Check("every temper in the enum has a profile",
                Enum.GetValues(typeof(NpcTemper)).Cast<NpcTemper>()
                    .All(t => NpcLedger.ProfileOf(t).MemoryBias > 0));

            // 人格要真的改变结果：同样一件事，善意人格涨得多、冷漠人格涨得少。
            var kindDef = Table("huolang", "Kind");
            var coldDef = Table("huolang", "Cold");
            var kindSave = NewSave();
            var coldSave = NewSave();
            NpcLedger.Apply(kindSave, kindDef, NpcEvent.Helped, WorldClock.ReadFrom(1, 600));
            NpcLedger.Apply(coldSave, coldDef, NpcEvent.Helped, WorldClock.ReadFrom(1, 600));
            Check("temper changes how much a favour is worth",
                NpcLedger.RelationOf(kindSave, "huolang").goodwill > NpcLedger.RelationOf(coldSave, "huolang").goodwill,
                kindSave.npcs[0].relation.goodwill + " vs " + coldSave.npcs[0].relation.goodwill);
            var kindLie = NewSave();
            var coldLie = NewSave();
            NpcLedger.Apply(kindLie, kindDef, NpcEvent.Lied, WorldClock.ReadFrom(1, 600));
            NpcLedger.Apply(coldLie, coldDef, NpcEvent.Lied, WorldClock.ReadFrom(1, 600));
            Check("temper changes how much a lie costs",
                NpcLedger.RelationOf(kindLie, "huolang").trust < NpcLedger.RelationOf(coldLie, "huolang").trust,
                kindLie.npcs[0].relation.trust + " vs " + coldLie.npcs[0].relation.trust);

            // 关系只经事件改：同一个人、同一事件、第二次不改数值也不再记一条记忆。
            var save = NewSave();
            var when = WorldClock.ReadFrom(1, 600);
            Check("an event is recorded once and only once",
                NpcLedger.Apply(save, peddler, NpcEvent.Helped, when) &&
                !NpcLedger.Apply(save, peddler, NpcEvent.Helped, when));
            var afterFirst = save.npcs[0].relation.goodwill;
            NpcLedger.Apply(save, peddler, NpcEvent.Helped, when);
            Check("a repeated event does not farm goodwill", save.npcs[0].relation.goodwill == afterFirst &&
                save.npcs[0].memory.Length == 1);
            Check("memory is a structured entry, not a sentence",
                save.npcs[0].memory[0].eventId == "Helped" && save.npcs[0].memory[0].day == 1 &&
                save.npcs[0].memory[0].minuteOfDay == 600 && save.npcs[0].memory[0].strength > 0 &&
                !save.npcs[0].memory[0].spoken);

            // 同类事件再来一次：关系不重复结算，记忆条目不新增，但记得更深。
            var repeat = NewSave();
            NpcLedger.Apply(repeat, peddler, NpcEvent.Helped, when);
            var depthBefore = repeat.npcs[0].memory[0].strength;
            var goodwillBefore = repeat.npcs[0].relation.goodwill;
            NpcLedger.Apply(repeat, peddler, NpcEvent.Helped, when);
            Check("a repeated deed is remembered more deeply without farming goodwill",
                repeat.npcs[0].memory.Length == 1 && repeat.npcs[0].memory[0].strength > depthBefore &&
                repeat.npcs[0].relation.goodwill == goodwillBefore,
                depthBefore + " → " + repeat.npcs[0].memory[0].strength);

            // 记忆满了丢最弱的，留他最记得的那件：直接塞满历史条目再触发一次新增。
            var crowded = NewSave();
            var crowdedState = NpcLedger.Of(crowded, peddler);
            for (var i = 0; i < NpcLedger.MaxMemories + 8; i++)
                crowdedState.memory = crowdedState.memory.Concat(new[]
                {
                    new NpcMemoryEntry { eventId = "old-" + i, day = 1, minuteOfDay = 600, strength = 10 + i % 5 }
                }).ToArray();
            NpcLedger.Apply(crowded, peddler, NpcEvent.Fought, when, 3);
            Check("memory is capped and keeps the strongest entries",
                crowded.npcs[0].memory.Length == NpcLedger.MaxMemories &&
                crowded.npcs[0].memory.All(m => m.strength >= 1) &&
                crowded.npcs[0].memory[0].strength >= crowded.npcs[0].memory[^1].strength);
            Check("a repeated event never appears twice in memory",
                crowded.npcs[0].memory.Select(m => m.eventId).Distinct().Count() ==
                crowded.npcs[0].memory.Length);

            // 敌意会自己消解：世界不围着玩家转。
            var grudging = NewSave();
            NpcLedger.Apply(grudging, peddler, NpcEvent.Fought, when);
            var hostile = grudging.npcs[0].relation.hostility;
            NpcLedger.Apply(grudging, peddler, NpcEvent.Defused, when);
            Check("anger cools when the player makes amends", grudging.npcs[0].relation.hostility < hostile,
                hostile + " → " + grudging.npcs[0].relation.hostility);

            // 关系三轴各有上下限，超界只钳位不死循环。
            var extreme = NewSave();
            for (var i = 0; i < 40; i++) NpcLedger.Apply(extreme, peddler, NpcEvent.Fought, when, 10);
            Check("relations stay inside their bounds", extreme.npcs[0].relation.IsValid,
                extreme.npcs[0].relation.goodwill + "/" + extreme.npcs[0].relation.trust + "/" +
                extreme.npcs[0].relation.hostility);
            Check("helping after hating still moves the needle",
                NpcLedger.Apply(extreme, peddler, NpcEvent.Confessed, when));

            // 目标：只读表里的定义，进度按表封顶；表里加目标不丢旧进度。
            var goal = peddler.Goals[0];
            Check("goals come from the table with a target", goal != null && goal.Target > 0);
            NpcLedger.AdvanceGoal(save, peddler, goal.Id, 3);
            Check("goal progress advances", NpcLedger.ProgressOf(save, peddler, goal.Id) == 3);
            NpcLedger.AdvanceGoal(save, peddler, goal.Id, 999);
            Check("goal progress stops at its target",
                NpcLedger.ProgressOf(save, peddler, goal.Id) == goal.Target &&
                NpcLedger.IsGoalDone(save, peddler, goal.Id));
            Check("unknown goals are refused",
                !NpcLedger.AdvanceGoal(save, peddler, "没有这个目标", 1) &&
                !NpcLedger.AdvanceGoal(save, peddler, goal.Id, 0));

            // 坏表：人格必须来自枚举，id 不重复，目标 id 不重复。
            var bad = new[]
            {
                ("{\"npcs\":[]}", "没有人物"),
                (Npcs("{\"id\":\"a\"}"), "缺名字"),
                (Npcs("{\"id\":\"a\",\"name\":\"A\",\"title\":\"T\",\"temper\":\"Cruel\"}"), "人格未定义"),
                (Npcs("{\"id\":\"a\",\"name\":\"A\",\"title\":\"T\"}"), "缺人格"),
                (Npcs("{\"id\":\"a\",\"name\":\"A\",\"title\":\"T\",\"temper\":\"Greedy\",\"greeting\":\"G\"," +
                      "\"goals\":[{\"id\":\"g\",\"target\":0}]}"), "目标完成数为零"),
                (Npcs("{\"id\":\"a\",\"name\":\"A\",\"title\":\"T\",\"temper\":\"Greedy\",\"greeting\":\"G\"," +
                      "\"goals\":[{\"id\":\"g\"},{\"id\":\"g\"}]}"), "目标 id 重复"),
            };
            foreach (var (json, label) in bad)
            {
                var rejected = false;
                try { NpcTable.Parse(json); }
                catch (FormatException) { rejected = true; }
                Check("malformed npc table rejected: " + label, rejected);
            }
            Check("duplicate npc ids rejected", Rejects("id 重复", Npcs(
                "{\"id\":\"a\",\"name\":\"A\",\"title\":\"T\",\"temper\":\"Greedy\",\"greeting\":\"G\"}",
                "{\"id\":\"a\",\"name\":\"B\",\"title\":\"T\",\"temper\":\"Greedy\",\"greeting\":\"G\"}")));

            // 档案缺失：显示层有可读提示，不会拿到 null 就崩。
            Check("a missing profile has a readable line", npcs.DescribeMissing("幽灵").Contains("档案"));
            Check("a missing profile yields no definition", npcs.Find("幽灵") == null);

            // 存档：关系、记忆、目标往返；旧档迁移；表里没有的人不丢。
            var repository = new SaveRepository(tempRoot, new PlainCodec()) { Items = items };
            var written = repository.Create(1, "farmer", "A");
            NpcLedger.Apply(written, peddler, NpcEvent.Helped, when);
            NpcLedger.AdvanceGoal(written, peddler, goal.Id, 2);
            // 一个"表里现在没有"的人：以前版本存过，不能因为今天表里没他就判脏或抹掉。
            written.npcs = written.npcs.Concat(new[]
            {
                new NpcState { id = "说书老人", relation = new NpcRelation(3, 2, 0) },
            }).ToArray();
            repository.Write(written);
            var reloaded = repository.Read(1).Data;
            Check("npc state survives a save roundtrip",
                reloaded.schemaVersion == SaveData.CurrentSchemaVersion &&
                NpcLedger.RelationOf(reloaded, "huolang").goodwill == save.npcs[0].relation.goodwill &&
                NpcLedger.ProgressOf(reloaded, peddler, goal.Id) == 2 &&
                reloaded.npcs[0].memory[0].eventId == "Helped");
            Check("an npc missing from the table is kept, not dropped",
                reloaded.npcs.Any(n => n.id == "说书老人"));
            SaveData.Migrate(reloaded, items);
            Check("migrating twice keeps relations and memories",
                reloaded.npcs.Length == 2 && NpcLedger.HasMemory(reloaded, "huolang", "Helped"));

            var legacy = new SaveData { slot = 2, characterId = "farmer", characterName = "A", schemaVersion = 7 };
            SaveData.Migrate(legacy, items);
            Check("a v7 save gains an empty npc list", legacy.npcs != null && legacy.npcs.Length == 0);

            ExpectRejected(repository, "duplicate npc ids", d =>
                d.npcs = new[] { new NpcState { id = "huolang" }, new NpcState { id = "huolang" } });
            ExpectRejected(repository, "out-of-range relation", d =>
                d.npcs = new[] { new NpcState { id = "huolang", relation = new NpcRelation(500, 0, 0) } });
            ExpectRejected(repository, "duplicate memory events", d =>
                d.npcs = new[]
                {
                    new NpcState
                    {
                        id = "huolang",
                        memory = new[]
                        {
                            new NpcMemoryEntry { eventId = "Helped", day = 1, minuteOfDay = 600, strength = 5 },
                            new NpcMemoryEntry { eventId = "Helped", day = 1, minuteOfDay = 700, strength = 5 },
                        }
                    }
                });
            ExpectRejected(repository, "memory with an impossible minute", d =>
                d.npcs = new[]
                {
                    new NpcState
                    {
                        id = "huolang",
                        memory = new[] { new NpcMemoryEntry { eventId = "Helped", day = 1, minuteOfDay = 5000, strength = 5 } }
                    }
                });
            ExpectRejected(repository, "negative goal progress", d =>
                d.npcs = new[]
                {
                    new NpcState
                    {
                        id = "huolang",
                        goals = new[] { new NpcGoalState { goalId = "fill-the-box", progress = -1 } }
                    }
                });
        }

        static NpcDef Table(string id, string temper) => NpcTable.Parse(
            Npcs("{\"id\":\"" + id + "\",\"name\":\"A\",\"title\":\"T\",\"temper\":\"" + temper + "\",\"greeting\":\"G\"}"))
            .Find(id);

        // 拒绝且理由点名预期关键词：只断言"抛了异常"会把解析失败误当成校验拒绝。
        static bool Rejects(string expectedReason, string json)
        {
            try { NpcTable.Parse(json); }
            catch (FormatException ex) { return ex.Message.Contains(expectedReason); }
            return false;
        }

        static string Npcs(params string[] bodies) => "{\"npcs\":[" + string.Join(",", bodies) + "]}";

        static SaveData NewSave() => new SaveData
        {
            slot = 1, characterId = "farmer", characterName = "A",
            gender = "male", faceStyle = 0, destiny = "anle",
        };

        static void Check(string name, bool condition, string detail = null)
        {
            if (!condition) throw new Exception("FAIL: " + name + (detail == null ? "" : "（" + detail + "）"));
            Console.WriteLine("PASS: " + name + (detail == null ? "" : "（" + detail + "）"));
        }

        static void ExpectRejected(SaveRepository repository, string label, Action<SaveData> corrupt)
        {
            var data = repository.Read(1).Data;
            corrupt(data);
            var rejected = false;
            try { repository.Write(data); }
            catch (ArgumentException) { rejected = true; }
            Check("save rejects: " + label, rejected);
        }

        sealed class PlainCodec : IJsonCodec
        {
            public string Serialize<T>(T value) => System.Text.Json.JsonSerializer.Serialize(value,
                new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
            public T Deserialize<T>(string json) where T : class =>
                System.Text.Json.JsonSerializer.Deserialize<T>(json,
                    new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
        }
    }
}
