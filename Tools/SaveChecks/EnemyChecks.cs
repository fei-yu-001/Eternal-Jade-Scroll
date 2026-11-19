using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TwelveJade.Core;

namespace TwelveJade.Core
{
    // M4-02 妖兽表测试：正式配置能读、坏表被拒、掉落与物品表对得上、奖励入包不凭空增减。
    public static class EnemyChecks
    {
        // ADR-031 冻结的跨线物品 id：掉落可以先引用，等员工 O 把 items.json 落名后自动并轨。
        // 只放行这六个——表外自造的 id 依旧当场红（这是"降级"与"放水"的分界）。
        static readonly string[] ContractDropIds =
        {
            "beast-gall", "wolf-fur", "wolf-fang", "mandrill-stone", "iron-ore", "dark-iron-armor",
        };

        // 掉落对表（ADR-031 版）：三条牙齿一条不少，只把"契约内还没落名"这一种情形降级。
        //   1) 表外未知 id → 当场红（沿用 CrossCheck 的语义，不许有人自造战利品名）
        //   2) 任务物 → 当场红（不能靠打野刷剧情物）
        //   3) 契约表内的缺项 → 记 WARN 并继续（D-10 的目的就是"谁先到都不用等谁"）
        static void CrossCheckDrops(EnemyTable enemies, ItemTable items)
        {
            var unknown = new List<string>();
            var pending = new List<string>();
            foreach (var enemy in enemies.Enemies)
            {
                foreach (var drop in enemy.Drops)
                {
                    var def = items.Find(drop.ItemId);
                    if (def == null)
                    {
                        if (ContractDropIds.Contains(drop.ItemId)) pending.Add(drop.ItemId);
                        else unknown.Add(enemy.Id + "→" + drop.ItemId);
                        continue;
                    }
                    if (def.Category == "renwu")
                        throw new FormatException("妖兽 " + enemy.Id + " 的掉落是任务物，不能重复刷出：" + drop.ItemId);
                }
            }
            if (unknown.Count > 0)
                throw new FormatException("妖兽掉落里有未登记且不在 ADR-031 契约表内的物品：" + string.Join("、", unknown));
            if (pending.Count > 0)
                Console.WriteLine("WARN: 掉落引用的 ADR-031 契约物品尚未落进 items.json（等员工 O 提交即并轨）：" +
                    string.Join("、", pending.Distinct()));
        }

        public static void Run(ItemTable items, string configDir)
        {
            var enemies = EnemyTable.Parse(File.ReadAllText(Path.Combine(configDir, "enemies.json")));
            CrossCheckDrops(enemies, items);
            var boar = enemies.Find("boar-demon");
            Check("enemy table parses", enemies.Enemies.Count >= 1, enemies.Enemies.Count + " 只妖兽");
            Check("boar demon is the first entry", boar != null && boar.MaxHp >= 30 &&
                boar.HitZones.Count >= 2 && boar.Drops.Count >= 1 && boar.Coins > 0);
            Check("head is the weak part", Math.Abs(boar.MultiplierOf("head") - 1.5f) < .001f &&
                Math.Abs(boar.MultiplierOf("body") - 1f) < .001f &&
                Math.Abs(boar.MultiplierOf("尾巴") - 1f) < .001f);
            Check("野猪掉落仍含 larou×2/caoyao×3（兽胆只能追加）",
                boar.Drops.Count(d => d.ItemId == "larou" && d.Count == 2) == 1 &&
                boar.Drops.Count(d => d.ItemId == "caoyao" && d.Count == 3) == 1);

            // 坏表要被拒绝，报错带字段名。
            var bad = new[]
            {
                ("{\"enemies\":[]}", "没有妖兽"),
                ("{\"enemies\":[{\"id\":\"a\",\"name\":\"甲\"}]}", "缺属性字段"),
                ("{\"enemies\":[{\"id\":\"a\",\"name\":\"甲\",\"maxHp\":0,\"attack\":1,\"defense\":0,\"stamina\":10,\"moveSpeed\":1," +
                 "\"hitProfile\":[{\"part\":\"body\",\"multiplier\":1}],\"drops\":[],\"coins\":0}]}", "生命为零"),
                ("{\"enemies\":[{\"id\":\"a\",\"name\":\"甲\",\"maxHp\":10,\"attack\":1,\"defense\":0,\"stamina\":10,\"moveSpeed\":1," +
                 "\"hitProfile\":[],\"drops\":[],\"coins\":0}]}", "没有部位"),
                ("{\"enemies\":[{\"id\":\"a\",\"name\":\"甲\",\"maxHp\":10,\"attack\":1,\"defense\":0,\"stamina\":10,\"moveSpeed\":1," +
                 "\"hitProfile\":[{\"part\":\"body\",\"multiplier\":0}],\"drops\":[],\"coins\":0}]}", "倍率为零"),
                ("{\"enemies\":[{\"id\":\"a\",\"name\":\"甲\",\"maxHp\":10,\"attack\":1,\"defense\":0,\"stamina\":10,\"moveSpeed\":1," +
                 "\"hitProfile\":[{\"part\":\"body\",\"multiplier\":1}],\"drops\":[{\"itemId\":\"x\",\"count\":-1}],\"coins\":0}]}", "掉落数量为负"),
                ("{\"enemies\":[{\"id\":\"a\",\"name\":\"甲\",\"maxHp\":10,\"attack\":1,\"defense\":0,\"stamina\":10,\"moveSpeed\":1," +
                 "\"hitProfile\":[{\"part\":\"body\",\"multiplier\":1}],\"drops\":[{\"itemId\":\"x\",\"count\":1}],\"coins\":-5}]}", "赏钱为负"),
                ("{\"enemies\":[{\"id\":\"a\",\"name\":\"甲\",\"maxHp\":10,\"attack\":1,\"defense\":0,\"stamina\":10,\"moveSpeed\":1," +
                 "\"hitProfile\":[{\"part\":\"body\",\"multiplier\":1},{\"part\":\"body\",\"multiplier\":2}],\"drops\":[],\"coins\":0}]}", "部位重复"),
                ("not json", "不是 JSON")
            };
            foreach (var (json, label) in bad)
            {
                var rejected = false;
                try { EnemyTable.Parse(json); }
                catch (FormatException) { rejected = true; }
                Check("malformed enemy table rejected: " + label, rejected);
            }

            var duplicated = "{\n  \"enemies\": [" +
                BoarJson("a") + "," + BoarJson("a") + "]\n}";
            var duplicateRejected = false;
            try { EnemyTable.Parse(duplicated); }
            catch (FormatException) { duplicateRejected = true; }
            Check("malformed enemy table rejected: id 重复", duplicateRejected);

            // 掉落与物品表对表：未知物品、任务物都不能刷。
            var unknownDrop = new EnemyTable(new List<EnemyDef> { WithDrops(("不存在的物品", 1)) });
            var unknownRejected = false;
            try { unknownDrop.CrossCheck(items); }
            catch (FormatException) { unknownRejected = true; }
            Check("unknown drop item is rejected", unknownRejected);

            var relicDrop = new EnemyTable(new List<EnemyDef> { WithDrops(("yupei", 1)) });
            var relicRejected = false;
            try { relicDrop.CrossCheck(items); }
            catch (FormatException) { relicRejected = true; }
            Check("quest relic cannot be farmed", relicRejected);

            // 奖励发放：铜钱与掉落都进档，数量分毫不差。
            var winner = new SaveData { slot = 1, characterId = "farmer", characterName = "甲", schemaVersion = SaveData.CurrentSchemaVersion };
            SaveData.Migrate(winner, items);
            var coinsBefore = winner.coins;
            var spoils = CombatSpoils.Grant(winner, boar, items);
            Check("spoils grant coins and drops", spoils.CoinsGranted == boar.Coins &&
                winner.coins == coinsBefore + boar.Coins &&
                spoils.Drops.All(d => d.leftover == 0) &&
                InventoryRules.Count(winner.bag, "larou") == 2 &&
                InventoryRules.Count(winner.bag, "caoyao") == 3,
                spoils.Describe());

            // 行囊快满：装不下的部分如实报余量，钱照发，货不凭空消失在桌上。
            var packed = new SaveData { slot = 1, characterId = "farmer", characterName = "甲", schemaVersion = SaveData.CurrentSchemaVersion };
            packed.bag = InventoryRules.NewBag();
            for (var i = 0; i < InventoryRules.SlotCount - 1; i++) packed.bag[i] = new ItemStack("tiejian", 1);
            var packedCoins = packed.coins;
            var tight = CombatSpoils.Grant(packed, boar, items);
            var tightLine = tight.Drops.First(d => d.leftover > 0);
            Check("a nearly full bag reports the leftover", !tight.EverythingFits &&
                packed.coins == packedCoins + boar.Coins &&
                tight.Drops.Sum(d => d.granted) + tight.Drops.Sum(d => d.leftover) == boar.Drops.Sum(d => d.Count),
                tight.Describe());

            // 奖励入包后写档再读，数字原样回来。
            var scratch = Path.Combine(Path.GetTempPath(), "twelve-jade-enemy-" + Guid.NewGuid().ToString("N"));
            try
            {
                var repository = new SaveRepository(scratch, new PlainCodec()) { Items = items };
                repository.Create(1, "farmer", "甲");
                var save = repository.Read(1).Data;
                var beforeCoins = save.coins;
                CombatSpoils.Grant(save, boar, items);
                repository.Write(save);
                var reloaded = repository.Read(1).Data;
                Check("spoils survive a save roundtrip", reloaded.coins == beforeCoins + boar.Coins &&
                    InventoryRules.Count(reloaded.bag, "larou") == 2 &&
                    InventoryRules.Count(reloaded.bag, "caoyao") == 3);
            }
            finally
            {
                try { if (Directory.Exists(scratch)) Directory.Delete(scratch, true); }
                catch { /* 临时目录，清不掉就算了 */ }
            }
        }

        static EnemyDef WithDrops((string itemId, int count) drop) => new EnemyDef("x", "甲", 10, 1, 0, 10, 1f,
            new List<HitZone> { new HitZone("body", 1f) },
            new List<DropLine> { new DropLine(drop.itemId, drop.count) }, 0);

        static string BoarJson(string id) =>
            "{\"id\":\"" + id + "\",\"name\":\"甲\",\"maxHp\":10,\"attack\":1,\"defense\":0,\"stamina\":10,\"moveSpeed\":1," +
            "\"hitProfile\":[{\"part\":\"body\",\"multiplier\":1}],\"drops\":[{\"itemId\":\"ganliang\",\"count\":1}],\"coins\":0}";

        static void Check(string name, bool condition, string detail = null)
        {
            if (!condition) throw new Exception("FAIL: " + name + (detail == null ? "" : "（" + detail + "）"));
            Console.WriteLine("PASS: " + name + (detail == null ? "" : "（" + detail + "）"));
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
