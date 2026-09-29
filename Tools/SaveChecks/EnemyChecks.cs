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
        public static void Run(ItemTable items, string configDir)
        {
            var enemies = EnemyTable.Parse(File.ReadAllText(Path.Combine(configDir, "enemies.json")));
            enemies.CrossCheck(items);
            var boar = enemies.Find("boar-demon");
            Check("enemy table parses", enemies.Enemies.Count >= 1, enemies.Enemies.Count + " 只妖兽");
            Check("boar demon is the first entry", boar != null && boar.MaxHp >= 30 &&
                boar.HitZones.Count >= 2 && boar.Drops.Count >= 1 && boar.Coins > 0);
            Check("head is the weak part", Math.Abs(boar.MultiplierOf("head") - 1.5f) < .001f &&
                Math.Abs(boar.MultiplierOf("body") - 1f) < .001f &&
                Math.Abs(boar.MultiplierOf("尾巴") - 1f) < .001f);

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
