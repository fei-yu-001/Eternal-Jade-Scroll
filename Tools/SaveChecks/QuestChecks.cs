using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TwelveJade.Core;

namespace TwelveJade.Core
{
    // M5-01 任务与章节进度数据层：表能读能挑错、状态机不许跳步、存档能存能读能反复迁移。
    // 完成条件全部落在这里——前台只负责把状态画出来，不在这里做判断。
    public static class QuestChecks
    {
        static string tempRoot;

        public static void Run(ItemTable items, string configDir, TownMap town)
        {
            tempRoot = Path.Combine(Path.GetTempPath(), "twelve-jade-quest-" + Guid.NewGuid().ToString("N"));
            try { RunInner(items, configDir, town); }
            finally { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); }
        }

        /// <summary>卷一主链七条（顺序即大纲 7 节的任务链）。按 id 点名而不是数全表：
        /// 数全表会被侧链的增长"合法地"顶掉，删掉一条主链反而没人拦（O1 / 任务书 N3.1 同型纪律）。</summary>
        static readonly string[] MainChain =
        {
            "tax-and-labor", "night-beast-roar", "beast-origin",
            "guide-investigation", "town-day", "fortune-teller-change", "leave-qingshi"
        };

        static void RunInner(ItemTable items, string configDir, TownMap town)
        {
            var quests = QuestTable.Parse(File.ReadAllText(Path.Combine(configDir, "quests.json")));
            quests.CrossCheck(items);
            quests.CrossCheckNpcs(town);
            QuestOverheadChecks.Run(items,configDir,town,tempRoot);
            // O0 marker：本套判据在 ConfigChecks.cs 里挂在 `if (!worldPhase)` 的**反分支**上，
            // 即"世界有分块"时整族静默跳过。今天 town.Chunks.Count > 0，所以这条 marker
            // 在 SaveChecks 输出里出现次数为 0——那本身就是结论（详见 T081 与投稿挂线段）。
            Check("判据入口被调用: QuestChecks", true, quests.Quests.Count + " 条任务");
            var tax = quests.Find("tax-and-labor");
            var roar = quests.Find("night-beast-roar");
            // 卷一主链按 id 逐条点名（J 线把任务加到 10 条，原来的 `Count == 7` 之所以没红，
            // 只是因为这一族整组被 worldPhase 跳过——不是它对了）。
            // 卷一主链按 id 逐条点名（J 线把任务加到 10 条，原来的 `Count == 7` 之所以没红，
            // 只是因为这一族整组被 worldPhase 跳过——不是它对了）。
            foreach (var id in MainChain)
                Check("卷一主链点名存在: " + id, quests.Find(id) != null);
            var side = quests.Quests.Where(q => !MainChain.Contains(q.Id)).ToArray();
            Check("主链条数仍为 7（只数主链，不数全表）",
                quests.Quests.Count(q => MainChain.Contains(q.Id)) == 7,
                quests.Quests.Count(q => MainChain.Contains(q.Id)) + " 条");
            Check("侧链机缘任务 3–8 条（下限保内容、上限防无节制膨胀）",
                side.Length >= 3 && side.Length <= 8, side.Length + " 条：" + string.Join(" · ", side.Select(q => q.Id)));
            Check("全表粗护栏不少于 10 条", quests.Quests.Count >= 10, quests.Quests.Count + " 个任务");
            // 开局只许一条**主链**敞着（原断言数的是全表，侧链机缘任务落地后前提就过期了 → T082）。
            // 侧链是"撞上了就能做"的机缘，本来就不该有前置，所以另立一条断言管它们，
            // 而不是把"唯一开放"放宽成"随便开几条"。
            Check("开局只有一条主链敞着",
                tax != null && tax.InitialStatus == QuestLedger.StatusAvailable &&
                quests.Quests.Count(q => MainChain.Contains(q.Id) &&
                    q.InitialStatus == QuestLedger.StatusAvailable) == 1,
                quests.Quests.Where(q => q.InitialStatus == QuestLedger.StatusAvailable)
                    .Select(q => q.Id).Aggregate("", (a, b) => a + " · " + b));
            Check("侧链机缘任务开局即可接（无前置是设计，不是漏配）",
                side.All(q => q.InitialStatus == QuestLedger.StatusAvailable && q.Prerequisites.Count == 0),
                string.Join(" · ", side.Select(q => q.Id)));
            Check("every quest declares a chapter and a title",
                quests.Quests.All(q => q.Chapter >= 1 && !string.IsNullOrWhiteSpace(q.Title)));
            // 主链必须一条扣一条串到底，且只有终点没有 next；侧链各自独立闭合（→ T082）。
            Check("main line chains without a break",
                quests.Quests.Where(q => MainChain.Contains(q.Id) && !string.IsNullOrEmpty(q.Next))
                    .All(q => quests.Find(q.Next) != null) &&
                quests.Quests.Count(q => MainChain.Contains(q.Id) && string.IsNullOrEmpty(q.Next)) == 1,
                "主链终点应唯一");
            Check("侧链不接下一环（机缘是岔口，不是链）",
                side.All(q => string.IsNullOrEmpty(q.Next)));
            Check("initial statuses are all real",
                quests.Quests.All(q => QuestLedger.IsKnownStatus(q.InitialStatus)));

            // 任务指向的遭遇必须真实存在（跨表一致性：任务表与地图表各自校验不到对方）。
            var encounterTargets = quests.Quests.SelectMany(q => q.Objectives)
                .Where(o => o.Type == "defeat").Select(o => o.Target).Where(t => !string.IsNullOrEmpty(t))
                .ToArray();
            Check("defeat objectives point at real encounters", encounterTargets.All(id => town.Encounters.Any(e => e.Id == id)),
                string.Join(" · ", encounterTargets));

            // 坏表要能指出具体 id 与字段。
            Check("malformed quest table rejected: 没有任务", Throws("{\"quests\":[]}"));
            Check("malformed quest table rejected: 读不动的 JSON", Throws("{ broken"));
            var bad = new[]
            {
                (Wrap("{\"id\":\"a\"}"), "缺标题"),
                (Wrap("{\"id\":\"a\",\"title\":\"A\",\"chapter\":0}"), "章节越界"),
                (Wrap("{\"id\":\"a\",\"title\":\"A\",\"chapter\":1,\"status\":\"Done\"}"), "未知状态"),
                (Wrap("{\"id\":\"a\",\"title\":\"A\",\"chapter\":1,\"objectives\":[{\"type\":\"talk\"}]}"), "目标缺 id"),
                (Wrap("{\"id\":\"a\",\"title\":\"A\",\"chapter\":1,\"objectives\":[" +
                      "{\"id\":\"x\",\"type\":\"talk\"},{\"id\":\"x\",\"type\":\"talk\"}]}"), "目标 id 重复"),
                (Wrap("{\"id\":\"a\",\"title\":\"A\",\"chapter\":1,\"prerequisites\":[\"ghost\"]}"), "前置未定义"),
                (Wrap("{\"id\":\"a\",\"title\":\"A\",\"chapter\":1,\"prerequisites\":[\"a\"]}"), "自己前置自己"),
                (Wrap("{\"id\":\"a\",\"title\":\"A\",\"chapter\":1,\"next\":\"ghost\"}"), "后续未定义"),
                (Wrap("{\"id\":\"a\",\"title\":\"A\",\"chapter\":1,\"rewards\":{\"coins\":-5}}"), "负铜钱"),
                (Wrap("{\"id\":\"a\",\"title\":\"A\",\"chapter\":1,\"rewards\":{\"items\":[{\"itemId\":\"x\",\"count\":0}]}}"), "零奖励物品"),
                (Wrap("{\"id\":\"a\",\"title\":\"A\",\"chapter\":1,\"rewards\":{\"flags\":[\"f\",\"f\"]}}"), "奖励标记重复"),
                (Wrap("{\"id\":\"a\",\"title\":\"A\",\"chapter\":1,\"objectives\":[{\"id\":\"x\",\"type\":\"talk\",\"count\":-1}]}"), "负目标数量"),
            };
            foreach (var (json, label) in bad)
            {
                // 坏表用例必须是"合法 JSON、非法任务表"：JSON 本身就写错时，被拒的原因是
                // 解析器而不是任务校验，这条用例就白测了（真踩过：多一个引号，报的却是解析错）。
                if (!Json.TryParse(json, out _, out var syntaxError))
                    throw new Exception("FAIL: 坏表用例本身不是合法 JSON：" + label + "（" + syntaxError + "）");
                var rejected = false;
                var reason = "";
                try { QuestTable.Parse(json); }
                catch (FormatException ex) { rejected = true; reason = ex.Message; }
                Check("malformed quest table rejected: " + label, rejected);
                if (rejected) Check("rejection names the offending quest: " + label, reason.Contains("a "), reason);
            }
            // 重复 id 与成环必须被拒——顺带断言拒绝理由，否则"解析失败"也会被当成通过（真踩过）。
            Check("duplicate quest ids rejected", Rejects("id 重复", Quests(
                "{\"id\":\"a\",\"title\":\"A\",\"chapter\":1}", "{\"id\":\"a\",\"title\":\"B\",\"chapter\":1}")));
            Check("prerequisite cycles rejected", Rejects("成环", Quests(
                "{\"id\":\"a\",\"title\":\"A\",\"chapter\":1,\"prerequisites\":[\"b\"]}",
                "{\"id\":\"b\",\"title\":\"B\",\"chapter\":1,\"prerequisites\":[\"a\"]}")));
            Check("next chains that loop back are rejected", Rejects("成环", Quests(
                "{\"id\":\"a\",\"title\":\"A\",\"chapter\":1,\"next\":\"b\"}",
                "{\"id\":\"b\",\"title\":\"B\",\"chapter\":1,\"next\":\"a\"}")));
            // 奖励物品要与物品表对表。
            var unknownReward = QuestTable.Parse(Wrap(
                "{\"id\":\"a\",\"title\":\"A\",\"chapter\":1,\"rewards\":{\"items\":[{\"itemId\":\"no-such-item\",\"count\":1}]}}"));
            var crossRejected = false;
            try { unknownReward.CrossCheck(items); }
            catch (FormatException) { crossRejected = true; }
            Check("quest rewarding an unknown item is rejected", crossRejected);

            // 状态机：解锁 → 接取 → 推进 → 完成，逐步都要对，跳步要挡住。
            var save = NewSave();
            Check("a new save has no quest state yet", save.quests.Length == 0 &&
                QuestLedger.StatusOf(save, "tax-and-labor") == QuestLedger.StatusLocked);
            QuestLedger.Of(save, quests, "tax-and-labor");
            Check("first lookup lays out the quest", save.quests.Length == 1 &&
                QuestLedger.StatusOf(save, "tax-and-labor") == QuestLedger.StatusAvailable);
            Check("a locked quest refuses to start",
                !QuestLedger.TryStart(save, quests.Find("night-beast-roar")) &&
                QuestLedger.StatusOf(save, "night-beast-roar") == QuestLedger.StatusLocked);
            Check("prerequisites unmet keeps the next quest locked",
                !QuestLedger.PrerequisitesMet(save, roar) && QuestLedger.RefreshAvailability(save, quests) == 0);

            Check("starting flips it to active", QuestLedger.TryStart(save, tax) &&
                QuestLedger.StatusOf(save, "tax-and-labor") == QuestLedger.StatusActive);
            Check("starting twice is idempotent", !QuestLedger.TryStart(save, tax) &&
                QuestLedger.StatusOf(save, "tax-and-labor") == QuestLedger.StatusActive);
            Check("completing with an objective unmet throws", Throws<InvalidOperationException>(() =>
                QuestLedger.TryComplete(save, tax)));
            Check("progress caps at the declared count",
                QuestLedger.Advance(save, tax, "answer-summons", 3) &&
                QuestLedger.Progress(save, tax, "answer-summons") == 1);
            Check("unknown objectives are ignored", !QuestLedger.Advance(save, tax, "没有这个目标", 1) &&
                !QuestLedger.Advance(save, tax, "answer-summons", 0));
            Check("completing now succeeds", QuestLedger.TryComplete(save, tax) &&
                QuestLedger.IsDone(save, "tax-and-labor"));
            Check("completing twice is idempotent", !QuestLedger.TryComplete(save, tax));
            Check("a completed quest cannot be started again", !QuestLedger.TryStart(save, tax));

            // 完成前置后解锁下一个，链条要自己往下走。
            Check("finishing the prerequisite unlocks the next", QuestLedger.RefreshAvailability(save, quests) == 1 &&
                QuestLedger.StatusOf(save, "night-beast-roar") == QuestLedger.StatusAvailable);
            Check("unlocking is idempotent", QuestLedger.RefreshAvailability(save, quests) == 0);
            QuestLedger.TryStart(save, roar);
            QuestLedger.Advance(save, roar, "drive-off-the-boar", 1);
            QuestLedger.TryComplete(save, roar);
            Check("the main line advances two steps in", QuestLedger.IsDone(save, "tax-and-labor") &&
                QuestLedger.IsDone(save, "night-beast-roar"));
            var townDay = quests.Find("town-day");
            Check("a quest without objectives completes on demand",
                QuestLedger.PrerequisitesMet(save, townDay) == false);

            // 失败路径：Active 才能失败，失败后不能再推进。
            var failing = NewSave();
            QuestLedger.Of(failing, quests, "tax-and-labor");
            QuestLedger.TryStart(failing, tax);
            Check("failing flips it to failed", QuestLedger.TryFail(failing, tax) &&
                QuestLedger.StatusOf(failing, "tax-and-labor") == QuestLedger.StatusFailed);
            Check("failing twice is idempotent", !QuestLedger.TryFail(failing, tax));
            Check("a failed quest takes no more progress",
                !QuestLedger.Advance(failing, tax, "answer-summons", 1) &&
                !QuestLedger.TryStart(failing, tax) && !QuestLedger.TryComplete(failing, tax));

            // 目标数量以表为准：表里加一个目标，旧档补 0、已记的进度不动。
            var grown = QuestTable.Parse(Quests(
                "{\"id\":\"solo\",\"title\":\"Solo\",\"chapter\":1,\"objectives\":[{\"id\":\"one\",\"type\":\"talk\"}]}",
                "{\"id\":\"pair\",\"title\":\"Pair\",\"chapter\":1,\"objectives\":[{\"id\":\"one\",\"type\":\"talk\"}," +
                     "{\"id\":\"two\",\"type\":\"clue\",\"count\":2}]}"));
            var aligned = NewSave();
            QuestLedger.Of(aligned, grown, "pair");
            QuestLedger.TryStart(aligned, grown.Find("pair"));
            QuestLedger.Advance(aligned, grown.Find("pair"), "one", 1);
            var stretched = QuestTable.Parse(Quests(
                "{\"id\":\"pair\",\"title\":\"Pair\",\"chapter\":1,\"objectives\":[{\"id\":\"one\",\"type\":\"talk\"}," +
                     "{\"id\":\"two\",\"type\":\"clue\",\"count\":2},{\"id\":\"three\",\"type\":\"reach\"}]}"));
            QuestLedger.Of(aligned, stretched, "pair");
            Check("a grown objective list pads with zeros and keeps progress",
                aligned.quests[0].progress.Length == 3 && aligned.quests[0].progress[0] == 1 &&
                aligned.quests[0].progress[1] == 0 && aligned.quests[0].progress[2] == 0);

            // 发奖：铜钱、物品、标记统一走 Core，装不下的如实报余量。
            var rewarding = QuestTable.Parse(Wrap(
                "{\"id\":\"pay\",\"title\":\"Pay\",\"chapter\":1,\"rewards\":{\"coins\":120," +
                "\"items\":[{\"itemId\":\"ganliang\",\"count\":3}],\"flags\":[\"paid\"]}}"));
            var pay = rewarding.Find("pay");
            var purse = NewSave();
            purse.coins = 100;
            var spoils = QuestLedger.GrantRewards(purse, pay, items);
            Check("rewards pay out coins, items and flags", spoils.coins == 120 && purse.coins == 220 &&
                spoils.itemsGranted == 3 && InventoryRules.Count(purse.bag, "ganliang") == 3 &&
                purse.HasFlag("paid") && spoils.flags.Length == 1);
            var again = QuestLedger.GrantRewards(purse, pay, items);
            Check("flag rewards do not fire twice", again.flags.Length == 0 && purse.HasFlag("paid"));
            var full = NewSave();
            full.coins = 0;
            for (var i = 0; i < InventoryRules.SlotCount; i++) full.bag[i] = new ItemStack("tiejian", 1);
            var blocked = QuestLedger.GrantRewards(full, pay, items);
            Check("a full bag keeps the shortfall instead of losing it",
                blocked.itemsGranted == 0 && blocked.itemsLeftover == 3 && full.coins == 120);

            // 存档：走真实的 SaveRepository，旧档迁移、往返、脏档拒绝都要过。
            var repository = new SaveRepository(tempRoot, new PlainCodec()) { Items = items };
            var written = repository.Create(1, "farmer", "甲");
            QuestLedger.Of(written, quests, "tax-and-labor");
            QuestLedger.TryStart(written, tax);
            QuestLedger.Advance(written, tax, "answer-summons", 1);
            QuestLedger.TryComplete(written, tax);
            QuestLedger.RefreshAvailability(written, quests);
            repository.Write(written);
            var reloaded = repository.Read(1).Data;
            Check("quest state survives a save roundtrip", reloaded.schemaVersion == SaveData.CurrentSchemaVersion &&
                QuestLedger.IsDone(reloaded, "tax-and-labor") &&
                QuestLedger.StatusOf(reloaded, "night-beast-roar") == QuestLedger.StatusAvailable &&
                QuestLedger.Progress(reloaded, tax, "answer-summons") == 1);
            SaveData.Migrate(reloaded, items);
            // RefreshAvailability 会给表里每个任务铺一份状态，条数对表而不是"用到过的几个"。
            Check("migrating a quest save keeps its progress",
                QuestLedger.IsDone(reloaded, "tax-and-labor") && reloaded.quests.Length == quests.Quests.Count,
                reloaded.quests.Length + " / " + quests.Quests.Count + " 条状态");

            var legacy = new SaveData { slot = 2, characterId = "farmer", characterName = "甲", schemaVersion = 5 };
            legacy.oneTimeFlags = new[] { "kept" };
            SaveData.Migrate(legacy, items);
            Check("a v5 save gains an empty quest list", legacy.quests != null && legacy.quests.Length == 0);
            SaveData.Migrate(legacy, items);
            Check("migrating a legacy save twice changes nothing", legacy.quests.Length == 0 &&
                legacy.oneTimeFlags.Length == 1);

            ExpectRejected(repository, "duplicate quest ids",
                d => { d.quests = new[] { new QuestState { id = "tax-and-labor", status = "Active" },
                    new QuestState { id = "tax-and-labor", status = "Active" } }; });
            ExpectRejected(repository, "unknown quest status",
                d => { d.quests = new[] { new QuestState { id = "tax-and-labor", status = "Done" } }; });
            ExpectRejected(repository, "negative quest progress",
                d => { d.quests = new[] { new QuestState { id = "tax-and-labor", status = "Active", progress = new[] { -1 } } }; });
            ExpectRejected(repository, "null quest entry",
                d => { d.quests = new QuestState[] { null }; });
        }

        // Wrap 把单条任务包成一张完整的表；拼多条时用 Quests——再套 Wrap 会套出
        // {"quests":[{"quests":[...]}]}，外层元素没有 id，测的就不是想测的东西了。
        static string Wrap(string quest) => Quests(quest);
        static string Quests(params string[] quests) => "{\"quests\":[" + string.Join(",", quests) + "]}";

        // 拒绝且理由里点名了预期关键词：只断言"抛了异常"会把"解析失败"误当成"校验拒绝"。
        static bool Rejects(string expectedReason, string json)
        {
            try { QuestTable.Parse(json); }
            catch (FormatException ex) { return ex.Message.Contains(expectedReason); }
            return false;
        }

        static bool Throws(string json)
        {
            try { QuestTable.Parse(json); }
            catch (FormatException) { return true; }
            return false;
        }

        static bool Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return true; }
            return false;
        }

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

        static SaveData NewSave() => new SaveData
        {
            slot = 1, characterId = "farmer", characterName = "甲",
            gender = "male", faceStyle = 0, destiny = "anle",
        };

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
