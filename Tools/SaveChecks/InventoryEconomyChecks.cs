using System;
using System.IO;
using TwelveJade.Economy;

namespace TwelveJade.Core
{
    // 员工 L · M9（控制台版）：与 Assets/Editor/InventoryEconomyShot.cs 共用同一套判据
    // （Editor/InventoryEconomySuite.cs），跑在 SaveChecks 里。
    // 这条控制台路线不是 Unity 批处理缺席时的替代品，而是**常态化的第二双眼睛**：
    // 它跑得快、不依赖引擎，改一件物品就能当场回验。真正只有引擎测得到的那几条
    // （Resources 加载路径与 JsonUtility 落盘往返）留在 InventoryEconomyShot 里。
    public static class InventoryEconomyChecks
    {
        public static void Run(string configDir)
        {
            CheckMaterialSourceReferences();
            InventoryEconomySuite.Run(configDir, Check);
            // T097 的落盘往返只在这一侧跑：它要真仓储 + 真编解码器，
            // 而 Economy 目录同时被 Unity 侧编译，JsonCodec 只存在于控制台这一份工程里。
            CheckPersistence(configDir);
            // schema 11 的迁移矩阵同样只在这一侧：它要读盘上的历史档、走真的 IsValid。
            DumpHistoricalFixturesIfAsked(configDir);
            CheckSchema11Matrix(configDir);
        }

        // 针对 D-49 新材质引用规则：直接调用生产解析器，合成数据只用于反例，不写产品配置。
        static void CheckMaterialSourceReferences()
        {
            var parser = typeof(InventoryEconomySuite).GetMethod("ConfigArtValues",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Check("[D49 material-ref] 生产解析器可用于回归", parser != null);
            string[] Resolve(string raw, string file = "world-material-gen.json",
                string field = "materials[*].sourceAsset.resource") =>
                ((System.Collections.Generic.IEnumerable<string>)parser.Invoke(null,
                    new object[] { Json.Parse(raw), field, file })).ToArray();
            const string resource = "Art/Textures/selftest-stone";
            const string valid = "{\"materials\":{\"stone-road.png\":{\"sourceAsset\":{" +
                "\"resource\":\"Art/Textures/selftest-stone\"," +
                "\"path\":\"GameClient/TwelveJade/Assets/Resources/Art/Textures/selftest-stone.png\"}}," +
                "\"note.png\":{\"resource\":\"Art/Textures/ghost\",\"sourceAsset\":{\"comment\":\"Art/Textures/ghost\"}}}," +
                "\"comment\":\"Art/Textures/ghost\"}";
            Check("[D49 material-ref] 只展开真实 sourceAsset，注释与旁支 resource 不算引用",
                Resolve(valid).SequenceEqual(new[] { resource }));
            Check("[D49 material-ref] 同名字面量键不能冒充消费结构",
                Resolve("{\"materials[*].sourceAsset.resource\":\"Art/Textures/ghost\"}").Length == 0);
            Check("[D49 material-ref] 其他配置文件不能借用材质规则放行",
                Resolve(valid, "items.json").Length == 0);
            Check("[D49 material-ref] materials 必须是对象，数组不冒充材质字典",
                Resolve("{\"materials\":[{\"sourceAsset\":{\"resource\":\"Art/Textures/ghost\"}}]}").Length == 0);
            Check("[D49 material-ref] 非材质键下的 sourceAsset 不算消费",
                Resolve(valid.Replace("\"stone-road.png\":", "\"comment\":")).Length == 0);
            Check("[D49 material-ref] 缺少 sourceAsset 不会用其他字符串补引用",
                Resolve(valid.Replace("\"sourceAsset\":", "\"notes\":")).Length == 0);
            Check("[D49 material-ref] 非字符串 resource 必须拒绝",
                Resolve(valid.Replace("\"resource\":\"Art/Textures/selftest-stone\"", "\"resource\":7")).Length == 0);
            Check("[D49 material-ref] 资源名与实际 source path 不一致必须拒绝",
                Resolve(valid.Replace("selftest-stone.png", "other-stone.png")).Length == 0);
            Check("[D49 material-ref] 路径穿越不能成为有效资源引用",
                Resolve(valid.Replace("Art/Textures/selftest-stone", "Art/Textures/../selftest-stone")).Length == 0);
            Check("[D49 material-ref] 既有 plots 数组引用保持有效",
                Resolve("{\"plots\":[{\"art\":\"Art/Buildings/selftest-gate\"}]}",
                    "building-plots.json", "plots[].art").SequenceEqual(new[] { "Art/Buildings/selftest-gate" }));
            Check("[D49 material-ref] 既有顶层 overview 引用保持有效",
                Resolve("{\"overviewArt\":\"Art/World/world-overview\"}",
                    "town-map.json", "overviewArt").SequenceEqual(new[] { "Art/World/world-overview" }));
        }

        /// <summary>善行记账之后重新读档，条目必须还在。Record 只改内存，落盘责任在调用方——
        /// 这是上一轮审计第 4 条（"我写了 Persist，可仓库里没这个方法"）的补账。</summary>
        static void CheckPersistence(string configDir)
        {
            var karma = KarmaTable.Parse(File.ReadAllText(Path.Combine(configDir, KarmaTable.FileName)));
            var root = Path.Combine(Path.GetTempPath(), "twelve-jade-karma-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var saves = new SaveRepository(root, new JsonCodec());
                saves.Create(3, "farmer", "甲");
                var save = saves.Read(3).Data;
                save.bag = InventoryRules.NewBag();
                KarmaTracker.Record(save, karma, "carry-back-herbs",
                    new WorldTime { day = 2, minuteOfDay = 800 });
                saves.Write(save);

                var readBack = saves.Read(3).Data;
                Check("善行落盘后重新读档，条目还在",
                    readBack?.karmaEvents != null && readBack.karmaEvents.Length == 1,
                    readBack?.karmaEvents?.Length.ToString() ?? "读不到档");
                Check("读回来的条目认得是哪件事",
                    readBack != null && KarmaTracker.HasDone(readBack, "carry-back-herbs"));
                Check("读回来的件数与内存里一致（Core 与 Economy 同一口径）",
                    KarmaLedger.Count(readBack) == KarmaTracker.TotalCount(save));
                Check("读档后再做同一件：只加次数、不增条目",
                    !KarmaTracker.Record(readBack, karma, "carry-back-herbs",
                        new WorldTime { day = 3, minuteOfDay = 900 }) &&
                    readBack.karmaEvents.Length == 1 &&
                    KarmaTracker.TimesOf(readBack, "carry-back-herbs") == 2);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        // ===== schema 11 迁移矩阵（O2-Schema11 / §11#7）=====
        //
        // 夹具不是手搓的 JSON：三份 schema-10 档是用**当时那一版的真序列化器**打出来的
        // （DumpHistoricalFixturesIfAsked 在纯 HEAD 树上跑一次），所以"老档能不能读进来"
        // 问的是文件，不是我脑子里的字段表。
        const string FixtureEnv = "TWELVEJADE_DUMP_SCHEMA_FIXTURES";
        static readonly string[] FixtureNames =
            { "schema10-empty-bag", "schema10-full-bag", "schema10-karma" };

        /// <summary>只在设了环境变量时干活：把当前代码写出来的档复制成历史夹具。</summary>
        public static void DumpHistoricalFixturesIfAsked(string configDir)
        {
            var target = Environment.GetEnvironmentVariable(FixtureEnv);
            if (string.IsNullOrEmpty(target)) return;
            Directory.CreateDirectory(target);
            var items = ItemTable.Parse(File.ReadAllText(Path.Combine(configDir, "items.json")));
            var karma = KarmaTable.Parse(File.ReadAllText(Path.Combine(configDir, KarmaTable.FileName)));
            var root = Path.Combine(Path.GetTempPath(), "twelve-jade-fixture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var saves = new SaveRepository(root, new JsonCodec());
                for (var slot = 1; slot <= FixtureNames.Length; slot++)
                {
                    saves.Create(slot, "farmer", "夹具" + slot);
                    var data = saves.Read(slot).Data;
                    switch (slot)
                    {
                        case 1: data.bag = InventoryRules.NewBag(); break;      // 空行囊
                        case 2: FillEverySlot(data, items); break;              // 满行囊
                        default: RecordDeeds(data, karma); break;               // 带气运账
                    }
                    saves.Write(data);
                    var dest = Path.Combine(target, FixtureNames[slot - 1] + ".json");
                    File.Copy(Path.Combine(root, "slot-" + slot + ".json"), dest, true);
                    Console.WriteLine("[fixture] wrote " + dest + " · " + new FileInfo(dest).Length + " 字节 · " +
                        FixtureNames[slot - 1]);
                }
            }
            finally { try { Directory.Delete(root, true); } catch (IOException) { } }
        }

        static void FillEverySlot(SaveData data, ItemTable items)
        {
            var bag = InventoryRules.NewBag();
            var ids = items.Items.Where(d => d.Stack > 0).Select(d => d.Id).ToArray();
            for (var i = 0; i < bag.Length && ids.Length > 0; i++)
                bag[i] = new ItemStack(ids[i % ids.Length], Math.Min(items.Find(ids[i % ids.Length]).Stack, 99));
            data.bag = bag;
        }

        static void RecordDeeds(SaveData data, KarmaTable karma)
        {
            KarmaTracker.Record(data, karma, "carry-back-herbs", new WorldTime { day = 2, minuteOfDay = 800 });
            KarmaTracker.Record(data, karma, "keep-a-secret", new WorldTime { day = 3, minuteOfDay = 1200 });
            KarmaTracker.Record(data, karma, "teahouse-tale", new WorldTime { day = 4, minuteOfDay = 1500 });
            KarmaTracker.Record(data, karma, "carry-back-herbs", new WorldTime { day = 5, minuteOfDay = 600 });
        }

        /// <summary>迁移矩阵：每份历史档都要"读得进、不吃账、写得回、升得到版"，
        /// 外加四条"新校验分支真的会咬人"的反证。</summary>
        static void CheckSchema11Matrix(string configDir)
        {
            var repoRoot = Path.GetFullPath(Path.Combine(configDir, "..", "..", "..", "..", ".."));
            var items = ItemTable.Parse(File.ReadAllText(Path.Combine(configDir, "items.json")));
            var codec = new JsonCodec();
            Check("客户端与服务端手工对齐的那条线已升版（CurrentSchemaVersion）",
                SaveData.CurrentSchemaVersion == 11, "当前 " + SaveData.CurrentSchemaVersion);

            foreach (var name in FixtureNames)
            {
                var path = Path.Combine(repoRoot, "Tools", "SaveChecks", name + ".json");
                Check("历史夹具在库：" + name, File.Exists(path), path);
                if (!File.Exists(path)) continue;
                var raw = File.ReadAllText(path);
                Check(name + " 钉的是升版**之前**的档（文件里写的是 schemaVersion 10）",
                    raw.Contains("\"schemaVersion\":10") || raw.Contains("\"schemaVersion\": 10"),
                    VersionIn(raw));
                var loaded = codec.Deserialize<SaveData>(raw);
                Check(name + " 反序列化后仍报 10 号（读档不偷偷升级）",
                    loaded != null && loaded.schemaVersion == 10,
                    loaded == null ? "读不出对象" : loaded.schemaVersion.ToString());
                if (loaded == null) continue;

                var coinsBefore = loaded.coins;
                var repBefore = loaded.localReputation;
                var piecesBefore = loaded.bag.Sum(s => s == null ? 0 : s.count);
                var karmaBefore = loaded.karmaEvents?.Length ?? 0;
                SaveData.Migrate(loaded, items);
                Check(name + " 迁移后新字段就位（gear/crafts/karmaEvents 不为 null）",
                    loaded.gear != null && loaded.crafts != null && loaded.karmaEvents != null);
                Check(name + " 迁移不吃老账：铜钱、声望、件数、善线条数一项不少",
                    loaded.coins == coinsBefore && loaded.localReputation == repBefore &&
                    loaded.bag.Sum(s => s == null ? 0 : s.count) == piecesBefore &&
                    loaded.karmaEvents.Length == karmaBefore,
                    $"铜钱 {coinsBefore}→{loaded.coins} · 件数 {piecesBefore}→{loaded.bag.Sum(s => s == null ? 0 : s.count)} · 善事 {karmaBefore}→{loaded.karmaEvents.Length}");
                // 10 号期间没有任何校验守过这张账，所以老档可能带着越界时刻/0 次/同 id 两行进来；
                // 迁移必须把它们修回合法形状——"新校验更严"不该等于"玩家的档变脏"。
                Check(name + " 迁移把老代码漏过的越界账修回合法（而不是把档判脏）",
                    loaded.karmaEvents.All(k => k.day >= WorldTime.StartDay && k.day <= WorldTime.MaxDays &&
                                                k.minuteOfDay >= 0 && k.minuteOfDay < WorldTime.MinutesPerDay &&
                                                k.times >= 1),
                    loaded.karmaEvents.Length + " 条");
                if (name == "schema10-karma")
                    Check("那份档原文里确实写着越界的 1500 分（修的是真问题，不是自问自答）",
                        raw.Contains("1500") && karmaBefore == 3, VersionIn(raw));

                // 真走一遍仓储：读档判脏不脏、写回升不升版，都由生产路径说了算。
                var root = Path.Combine(Path.GetTempPath(), "twelve-jade-matrix-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);
                try
                {
                    var saves = new SaveRepository(root, codec);
                    File.Copy(path, Path.Combine(root, "slot-" + loaded.slot + ".json"));
                    var info = saves.Read(loaded.slot);
                    Check(name + " 经仓储读入判为可继续（不是 Corrupt）",
                        info.State == SlotState.Ready, info.State.ToString());
                    if (info.State != SlotState.Ready) continue;
                    saves.Write(info.Data);
                    var back = saves.Read(loaded.slot).Data;
                    Check(name + " 写回后升到当前版，且账还在",
                        back != null && back.schemaVersion == SaveData.CurrentSchemaVersion &&
                        back.karmaEvents.Length == karmaBefore &&
                        back.bag.Sum(s => s == null ? 0 : s.count) == piecesBefore);
                }
                finally { try { Directory.Delete(root, true); } catch (IOException) { } }
            }

            // 反证：新校验分支必须真的会咬，不然"补了洞"只是话术。
            var probe = Path.Combine(Path.GetTempPath(), "twelve-jade-probe-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(probe);
            try
            {
                var saves = new SaveRepository(probe, codec);
                saves.Create(1, "farmer", "甲");
                var baseSave = saves.Read(1).Data;

                var dup = saves.Read(1).Data;
                dup.karmaEvents = new[]
                {
                    new KarmaEntry { eventId = "river-rescue", kind = "deed", day = 2, minuteOfDay = 600, times = 1 },
                    new KarmaEntry { eventId = "river-rescue", kind = "deed", day = 3, minuteOfDay = 600, times = 1 }
                };
                Check("重复的善事 id 被存档校验拒掉（W-A 指出的洞已补）",
                    Writes(dup, saves), "写档没抛＝校验没咬");

                var badKind = saves.Read(1).Data;
                badKind.karmaEvents = new[]
                {
                    new KarmaEntry { eventId = "river-rescue", kind = "善事", day = 2, minuteOfDay = 600, times = 1 }
                };
                Check("kind 不是 deed/chance 的账被拒（字面量口径与 karma.json 同源）",
                    Writes(badKind, saves));

                var badGear = saves.Read(1).Data;
                badGear.gear = new[]
                {
                    new GearLine { slot = "weapon", itemId = "cudao", durability = -3 }
                };
                Check("负数耐久被存档校验拒掉", Writes(badGear, saves));

                var twoWeapon = saves.Read(1).Data;
                twoWeapon.gear = new[]
                {
                    new GearLine { slot = "weapon", itemId = "cudao", durability = 10 },
                    new GearLine { slot = "weapon", itemId = "wood-bow", durability = 20 },
                    new GearLine { slot = "ring", itemId = "yupei", durability = 5 }
                };
                SaveData.Migrate(twoWeapon, items);
                Check("同槽两条折叠成一条、未知槽位整条丢掉",
                    twoWeapon.gear.Length == 1 && twoWeapon.gear[0].itemId == "wood-bow",
                    twoWeapon.gear.Length + " 条");

                var zeroCraft = saves.Read(1).Data;
                zeroCraft.crafts = new[] { new CraftLine { recipeId = "r-1", times = 0 } };
                SaveData.Migrate(zeroCraft, items);
                Check("做过 0 次不占一行（次数是事实，不是占位符）", zeroCraft.crafts.Length == 0);

                var good = saves.Read(1).Data;
                good.karmaEvents = new[]
                {
                    new KarmaEntry { eventId = "river-rescue", kind = KarmaKinds.Deed, day = 2, minuteOfDay = 600, times = 2 }
                };
                good.gear = new[] { new GearLine { slot = GearSlots.Weapon, itemId = "cudao", durability = 40 } };
                good.crafts = new[] { new CraftLine { recipeId = "jinchuang-san", times = 7 } };
                saves.Write(good);
                var readGood = saves.Read(1).Data;
                Check("合法的新字段档写得进读得出，三样都原样回来",
                    readGood.karmaEvents.Length == 1 && readGood.karmaEvents[0].times == 2 &&
                    readGood.gear.Length == 1 && readGood.gear[0].durability == 40 &&
                    readGood.crafts.Length == 1 && readGood.crafts[0].times == 7);

                Check("Core 的槽名字面量与 Combat 的 GearSlot 枚举逐字对应（跨程序集契约）",
                    Enum.GetNames(typeof(TwelveJade.Combat.GearSlot))
                        .Select(n => n.ToLowerInvariant()).OrderBy(n => n).SequenceEqual(
                            GearSlots.All.OrderBy(n => n)),
                    string.Join(",", Enum.GetNames(typeof(TwelveJade.Combat.GearSlot))));
                Check("kind 字面量只有一个真源（Economy 转发 Core）",
                    KarmaTracker.Deed == KarmaKinds.Deed && KarmaTracker.Chance == KarmaKinds.Chance);
                Check("存档基线版本与服务端 SUPPORTED_SCHEMA 同批（客户端 11）",
                    SaveData.CurrentSchemaVersion == 11 && baseSave.schemaVersion == 11);
            }
            finally { try { Directory.Delete(probe, true); } catch (IOException) { } }
        }

        /// <summary>写一次档，返回"是否被 IsValid 挡下"（挡下=true）。</summary>
        static bool Writes(SaveData data, SaveRepository saves)
        {
            try { saves.Write(data); return false; }
            catch (ArgumentException) { return true; }
        }

        static string VersionIn(string raw)
        {
            var i = raw.IndexOf("schemaVersion", StringComparison.Ordinal);
            return i < 0 ? "文件里没有 schemaVersion" : raw.Substring(i, Math.Min(24, raw.Length - i));
        }
        // 与本项目其余 *Checks 同一套约定：每条断言自己打印 PASS，失败即抛。
        static void Check(string name, bool condition, string detail = null)
        {
            if (!condition) throw new Exception("FAIL: " + name + (detail == null ? "" : "（" + detail + "）"));
            Console.WriteLine("PASS: " + name + (detail == null ? "" : "（" + detail + "）"));
        }
    }
}
