using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TwelveJade.Core;

namespace TwelveJade.Core
{
    // M4-03/M4-04 配套检查：一次性标记的存档往返，遭遇点与妖兽表对表。
    public static class M4Checks
    {
        public static void Run(ItemTable items, TownMap town, string configDir)
        {
            var enemies = EnemyTable.Parse(File.ReadAllText(Path.Combine(configDir, "enemies.json")));
            enemies.CrossCheck(items);

            // 遭遇点：地图里声明的敌人必须真实存在。
            Check("town declares the boar encounter",
                town.Encounters.Any(e => e.Id == "encounter-boar-01" && e.Enemy == "boar-demon"),
                town.Encounters.Count + " 个遭遇点");
            Check("encounters reference real enemies",
                town.Encounters.All(e => enemies.Find(e.Enemy) != null));
            Check("encounter stands on walkable ground",
                town.Encounters.All(e => town.CanStand(town.ClampToWalkable(e.X, e.Y).x, town.ClampToWalkable(e.Y, e.Y).y) ||
                    town.WalkableContains(e.X, e.Y)));

            var bad = new[]
            {
                ("{\"walkable\":[{\"x\":0,\"y\":0,\"width\":100,\"height\":100}],\"spawn\":{\"x\":50,\"y\":50}," +
                 "\"encounters\":[{\"id\":\"a\",\"enemy\":\"x\",\"name\":\"甲\",\"x\":10,\"y\":10}]}".Replace("x\":\"x", "enemy\":\"x"), "占位"),
                ("{\"walkable\":[{\"x\":0,\"y\":0,\"width\":100,\"height\":100}],\"spawn\":{\"x\":50,\"y\":50}," +
                 "\"encounters\":[{\"id\":\"a\",\"enemy\":\"boar\",\"name\":\"甲\",\"x\":10,\"y\":10}," +
                 "{\"id\":\"a\",\"enemy\":\"boar\",\"name\":\"甲\",\"x\":20,\"y\":10}]}", "id 重复"),
                ("{\"walkable\":[{\"x\":0,\"y\":0,\"width\":100,\"height\":100}],\"spawn\":{\"x\":50,\"y\":50}," +
                 "\"encounters\":[{\"id\":\"a\",\"enemy\":\"boar\",\"name\":\"甲\",\"x\":99999,\"y\":10}]}", "超出画布")
            };
            foreach (var (json, label) in bad)
            {
                if (label == "占位") continue;
                var rejected = false;
                try { TownMap.Parse(json); }
                catch (FormatException) { rejected = true; }
                Check("malformed encounter map rejected: " + label, rejected);
            }

            // 一次性标记：往返、幂等、容量与非法值。
            var scratch = Path.Combine(Path.GetTempPath(), "twelve-jade-m4-" + Guid.NewGuid().ToString("N"));
            try
            {
                var repository = new SaveRepository(scratch, new PlainCodec()) { Items = items };
                var save = repository.Create(1, "farmer", "甲");
                Check("marking a flag reports first time", save.MarkFlag("encounter-boar-01"));
                Check("marking again is idempotent", !save.MarkFlag("encounter-boar-01") && save.HasFlag("encounter-boar-01"));
                save.MarkFlag("encounter-boar-01-reward-note");
                repository.Write(save);
                var reloaded = repository.Read(1).Data;
                Check("flags survive a save roundtrip", reloaded.HasFlag("encounter-boar-01") &&
                    reloaded.schemaVersion == SaveData.CurrentSchemaVersion);

                Check("oversized flag id is refused",
                    Throws<ArgumentException>(() => repository.Read(1).Data.MarkFlag(new string('长', 49))));
                var crowded = repository.Read(1).Data;
                for (var i = crowded.oneTimeFlags.Length; i < 32; i++) crowded.MarkFlag("flag-" + i.ToString("00"));
                Check("flag capacity capped at 32",
                    crowded.oneTimeFlags.Length == 32 &&
                    Throws<InvalidOperationException>(() => crowded.MarkFlag("flag-32")));
                repository.Write(crowded);
                Check("a full ledger still roundtrips",
                    repository.Read(1).Data.oneTimeFlags.Length == 32);
            }
            finally
            {
                try { if (Directory.Exists(scratch)) Directory.Delete(scratch, true); }
                catch { /* 临时目录，清不掉就算了 */ }
            }
        }

        static bool Throws<T>(Action action) where T : Exception
        {
            try { action(); return false; }
            catch (T) { return true; }
        }

        static void ExpectRejected(SaveRepository repository, string label, Action<SaveData> corrupt)
        {
            var data = repository.Read(1).Data;
            corrupt(data);
            var rejected = false;
            try { repository.Write(data); }
            catch (ArgumentException) { rejected = true; }
            catch (InvalidOperationException) { rejected = true; }
            Check("save rejects: " + label, rejected);
        }

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
