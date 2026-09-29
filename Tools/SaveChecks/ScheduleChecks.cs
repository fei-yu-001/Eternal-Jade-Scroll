using System;
using System.IO;
using System.Linq;
using TwelveJade.Core;

namespace TwelveJade.Core
{
    // M5-02 世界时钟与 NPC 作息：时间只由传入增量推进、窗口边界不被掉帧吞掉、
    // 作息表能读能挑错、命格与时段两个条件缺一不可、存档往返不漂。
    public static class ScheduleChecks
    {
        static string tempRoot;

        public static void Run(SaveData prototype, ItemTable items, string configDir, TownMap town)
        {
            tempRoot = Path.Combine(Path.GetTempPath(), "twelve-jade-schedule-" + Guid.NewGuid().ToString("N"));
            try { RunInner(prototype, items, configDir, town); }
            finally { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); }
        }

        static void RunInner(SaveData prototype, ItemTable items, string configDir, TownMap town)
        {
            // 时钟：不读系统时间，推进只认传入的增量（单位是秒）。
            var time = WorldClock.NewGame();
            Check("a new world starts at day one, six in the morning",
                time.day == 1 && time.minuteOfDay == 360 && WorldClock.Format(time) == "第 1 天 06:00");
            WorldClock.Advance(time, 60);
            Check("one real minute moves the clock one minute",
                time.minuteOfDay == 361 && WorldClock.Clock(time.minuteOfDay) == "06:01",
                WorldClock.Format(time));
            WorldClock.Advance(time, 60 * 60 * 23);
            Check("twenty-three hours later it is the next day",
                time.day == 2 && time.minuteOfDay == 301, WorldClock.Format(time));
            WorldClock.Advance(time, 10, 0);
            Check("a paused clock does not move", time.minuteOfDay == 301);
            WorldClock.Advance(time, -5);
            Check("a negative delta does not rewind", time.minuteOfDay == 301);

            // 作息表先读出来：下面几段都要用"此刻在不在窗口"来验。
            var schedules = NpcScheduleTable.Parse(File.ReadAllText(Path.Combine(configDir, "npc-schedules.json")));
            schedules.CrossCheck(town);

            // 跨窗口边界：黑市 22:00 开，21:50 出发走十分钟应当正好开市。
            var beforeOpen = WorldClock.ReadFrom(1, 1310);
            var afterOpen = WorldClock.ReadFrom(1, 1310);
            var closedThen = ScheduleRules.SeesBlackMarket(NewSave("shaxing"), beforeOpen, schedules, "huolang");
            WorldClock.Advance(afterOpen, 600);
            var openNow = ScheduleRules.SeesBlackMarket(NewSave("shaxing"), afterOpen, schedules, "huolang");
            Check("stepping over the window edge opens the night market", !closedThen && openNow,
                WorldClock.Format(afterOpen));
            // 一口气跨过午夜：窗口是左闭右开，跨天之后就该关。
            var overnight = WorldClock.ReadFrom(1, 1320);
            WorldClock.Advance(overnight, 60 * 60 * 20);
            Check("twenty hours later the night market is shut again",
                overnight.day == 2 && overnight.minuteOfDay == 1080 &&
                !ScheduleRules.SeesBlackMarket(NewSave("shaxing"), overnight, schedules, "huolang"),
                WorldClock.Format(overnight));
            // 手滑的极端增量：钳制后仍必须是合法时刻，不能变成负数或越过上限。
            var clamped = WorldClock.ReadFrom(1, 60);
            WorldClock.Advance(clamped, 1e9);
            Check("an absurd delta is clamped to a valid clock", WorldClock.IsValid(clamped), WorldClock.Format(clamped));
            var tinyStep = WorldClock.ReadFrom(1, 600);
            WorldClock.Advance(tinyStep, 1, 1e-9);
            Check("a tiny speed still lands on a legal minute", WorldClock.IsValid(tinyStep), WorldClock.Format(tinyStep));

            // 快进与归一化。
            var skip = WorldClock.ReadFrom(1, 360);
            WorldClock.SkipTo(skip, 1 * 1440 + 1380);
            Check("skipping ahead lands on the requested minute", skip.day == 2 && skip.minuteOfDay == 1380);
            var dirty = WorldClock.ReadFrom(99999, -50);
            Check("a dirty clock is pulled back into range", dirty.day == WorldTime.MaxDays && dirty.minuteOfDay == 1390,
                WorldClock.Format(dirty));
            Check("minute and day round-trip through absolute minutes",
                WorldClock.ToMinutes(WorldClock.ReadFrom(3, 700)) == 2 * 1440 + 700);

            // 作息表：能读能对表。
            var peddler = schedules.Find("huolang");
            Check("schedule table parses and cross-checks", schedules.Schedules.Count >= 1 && peddler != null);
            Check("the peddler keeps two windows a day", peddler.Windows.Count == 2);
            Check("windows do not overlap and cover the stated spans",
                peddler.Windows.All(w => w.EndMinute > w.StartMinute) &&
                peddler.Windows.Sum(w => w.Length) <= WorldTime.MinutesPerDay);
            Check("a day window is tradeable and a night window is black",
                peddler.Windows.Any(w => w.Tradeable && !w.Black) && peddler.Windows.Any(w => w.Black && w.Tradeable));
            Check("on duty in the market, off duty in the evening gap",
                peddler.IsOnDuty(600) && !peddler.IsOnDuty(1200) && !peddler.IsOnDuty(30));
            Check("the peddler is somewhere in the small hours",
                ScheduleRules.PlaceAt(WorldClock.ReadFrom(1, 1380), schedules, "huolang") == "bridges" &&
                ScheduleRules.PlaceAt(WorldClock.ReadFrom(1, 90), schedules, "huolang") == "");

            // 命格 × 时段：两个条件缺一不可，四种组合逐一验。
            var day = WorldClock.ReadFrom(1, 600);
            var night = WorldClock.ReadFrom(1, 1380);
            Check("black market needs both a black-market fate and the night window",
                !ScheduleRules.SeesBlackMarket(NewSave("anle"), day, schedules, "huolang") &&
                !ScheduleRules.SeesBlackMarket(NewSave("anle"), night, schedules, "huolang") &&
                !ScheduleRules.SeesBlackMarket(NewSave("shaxing"), day, schedules, "huolang") &&
                ScheduleRules.SeesBlackMarket(NewSave("shaxing"), night, schedules, "huolang"));
            Check("all three black-market fates pass once it is night",
                Trade.BlackMarketFavouredDestinies.All(id =>
                    ScheduleRules.SeesBlackMarket(NewSave(id), night, schedules, "huolang")));
            Check("an unknown schedule never opens the black market",
                !ScheduleRules.SeesBlackMarket(NewSave("shaxing"), night, schedules, "没有这个作息"));
            Check("a missing clock never opens the black market",
                !ScheduleRules.SeesBlackMarket(NewSave("shaxing"), null, schedules, "huolang"));

            // 坏表要指出具体字段。
            var bad = new[]
            {
                (Schedules("{\"id\":\"h\",\"npcId\":\"huolang\",\"windows\":[]}"), "没有窗口"),
                (Schedules("{\"id\":\"h\",\"windows\":[{\"id\":\"w\",\"start\":0,\"end\":10,\"place\":\"market\"}]}"), "缺 npcId"),
                (Schedules("{\"id\":\"h\",\"npcId\":\"huolang\",\"windows\":[{\"start\":0,\"end\":10,\"place\":\"market\"}]}"), "窗口缺 id"),
                (Schedules("{\"id\":\"h\",\"npcId\":\"huolang\",\"windows\":[{\"id\":\"w\",\"start\":600,\"end\":600,\"place\":\"market\"}]}"), "首末相等"),
                (Schedules("{\"id\":\"h\",\"npcId\":\"huolang\",\"windows\":[{\"id\":\"w\",\"start\":600,\"end\":6000,\"place\":\"market\"}]}"), "越界"),
                (Schedules("{\"id\":\"h\",\"npcId\":\"huolang\",\"windows\":[{\"id\":\"w\",\"start\":0,\"end\":10,\"place\":\"market\"}," +
                           "{\"id\":\"w\",\"start\":5,\"end\":20,\"place\":\"market\"}]}"), "窗口 id 重复"),
                (Schedules("{\"id\":\"h\",\"npcId\":\"huolang\",\"windows\":[{\"id\":\"a\",\"start\":0,\"end\":600,\"place\":\"market\"}," +
                           "{\"id\":\"b\",\"start\":300,\"end\":900,\"place\":\"market\"}]}"), "时间重叠"),
            };
            foreach (var (json, label) in bad)
            {
                var rejected = false;
                try { NpcScheduleTable.Parse(json); }
                catch (FormatException) { rejected = true; }
                Check("malformed schedule rejected: " + label, rejected);
            }
            Check("duplicate schedule ids rejected", Rejects("id 重复", Schedules(
                "{\"id\":\"h\",\"npcId\":\"huolang\",\"windows\":[{\"id\":\"a\",\"start\":0,\"end\":10,\"place\":\"market\"}]}",
                "{\"id\":\"h\",\"npcId\":\"huolang\",\"windows\":[{\"id\":\"b\",\"start\":20,\"end\":30,\"place\":\"market\"}]}")));
            // 与地图对表：未知 NPC 与未知地点都要被拒。
            var unknownNpc = NpcScheduleTable.Parse(Schedules(
                "{\"id\":\"h\",\"npcId\":\"幽灵\",\"windows\":[{\"id\":\"a\",\"start\":0,\"end\":10,\"place\":\"market\"}]}"));
            var npcRejected = false;
            try { unknownNpc.CrossCheck(town); } catch (FormatException) { npcRejected = true; }
            Check("a schedule for an unknown npc is rejected", npcRejected);
            var unknownPlace = NpcScheduleTable.Parse(Schedules(
                "{\"id\":\"h\",\"npcId\":\"huolang\",\"windows\":[{\"id\":\"a\",\"start\":0,\"end\":10,\"place\":\"天上\"}]}"));
            var placeRejected = false;
            try { unknownPlace.CrossCheck(town); } catch (FormatException) { placeRejected = true; }
            Check("a schedule pointing at an unknown place is rejected", placeRejected);

            // 存档：世界时间写回、往返不漂、v6 旧档落到开局时刻。
            var repository = new SaveRepository(tempRoot, new PlainCodec()) { Items = items };
            var save = repository.Create(1, "farmer", "A");
            var world = save.WorldTimeNow();
            WorldClock.Advance(world, 60 * 60 * 9); // 九小时后是 15:00
            save.SetWorldTime(world);
            repository.Write(save);
            var reloaded = repository.Read(1).Data;
            Check("world time survives a save roundtrip",
                reloaded.schemaVersion == SaveData.CurrentSchemaVersion &&
                reloaded.WorldTimeNow().day == 1 && reloaded.WorldTimeNow().minuteOfDay == 900,
                WorldClock.Format(reloaded.WorldTimeNow()));

            var legacy = new SaveData { slot = 2, characterId = "farmer", characterName = "A", schemaVersion = 6 };
            SaveData.Migrate(legacy, items);
            Check("a v6 save falls back to the opening hour", legacy.worldDay == 1 && legacy.worldMinuteOfDay == 360);
            legacy.worldDay = 400;
            SaveData.Migrate(legacy, items);
            Check("migrating a world-time save keeps it", legacy.worldDay == 400);
            legacy.worldMinuteOfDay = 99999;
            SaveData.Migrate(legacy, items);
            Check("a dirty minute is normalised", legacy.worldMinuteOfDay == 99999 % 1440);

            var corrupt = repository.Read(1).Data;
            corrupt.worldMinuteOfDay = 1440;
            var timeRejected = false;
            try { repository.Write(corrupt); } catch (ArgumentException) { timeRejected = true; }
            Check("save rejects a minute past the day", timeRejected);
            corrupt.worldMinuteOfDay = 600;
            corrupt.worldDay = 0;
            timeRejected = false;
            try { repository.Write(corrupt); } catch (ArgumentException) { timeRejected = true; }
            Check("save rejects day zero", timeRejected);
        }

        // 拒绝且理由点名预期关键词：只断言"抛了异常"会把解析失败误当成校验拒绝。
        static bool Rejects(string expectedReason, string json)
        {
            try { NpcScheduleTable.Parse(json); }
            catch (FormatException ex) { return ex.Message.Contains(expectedReason); }
            return false;
        }

        static string Schedules(params string[] windows) =>
            "{\"schedules\":[" + string.Join(",", windows) + "]}";

        static SaveData NewSave(string destiny) => new SaveData
        {
            slot = 1, characterId = "farmer", characterName = "A",
            gender = "male", faceStyle = 0, destiny = destiny,
        };

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
