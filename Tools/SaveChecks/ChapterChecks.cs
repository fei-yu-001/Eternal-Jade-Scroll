using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TwelveJade.Core;

namespace TwelveJade.Core
{
    // M5-05 S1 验收：章轴线能不能从第一段走到最后一段，奖励会不会重复发，夜间段门控是否严。
    public static class ChapterChecks
    {
        public static void Run(string configDir)
        {
            var chapters = ChapterTable.Parse(File.ReadAllText(Path.Combine(configDir, "chapters.json")));
            var quests = QuestTable.Parse(File.ReadAllText(Path.Combine(configDir, "quests.json")));
            chapters.CrossCheck(quests);
            var schedules = NpcScheduleTable.Parse(File.ReadAllText(Path.Combine(configDir, "npc-schedules.json")));
            ChapterLedger.Schedule = schedules;
            ChapterLedger.Quests = quests;

            Check("chapter table covers the whole axis", chapters.Segments.Count == 9, chapters.Segments.Count + " 段");
            Check("every segment depends on real quests", chapters.Segments.All(s => s.RequiresQuests.All(q => quests.Find(q) != null)));
            Check("draft segments are marked", chapters.Find(ChapterSegment.OracleTwist).Draft &&
                chapters.Find(ChapterSegment.Departure).Draft);
            Check("settled segments carry a one-shot flag", chapters.Segments
                .Where(s => !s.Draft)
                .All(s => !string.IsNullOrEmpty(s.GrantsFlag)));
            Check("flags form a chain", ChainHolds(chapters));

            var bad = new[]
            {
                ("{\"segments\":[]}", "没有段落"),
                ("{\"segments\":[{\"id\":\"Prologue\",\"title\":\"甲\",\"summary\":\"乙\"}]}", "缺段落 Arrival"),
                ("{\"segments\":[{\"id\":\"不存在的段\",\"title\":\"甲\",\"summary\":\"乙\"}]}", "未知段落"),
                ("{\"segments\":[{\"id\":\"Prologue\",\"title\":\"\",\"summary\":\"乙\"}]}", "标题为空"),
            };
            foreach (var (json, label) in bad)
            {
                var rejected = false;
                try { ChapterTable.Parse(json); }
                catch (FormatException) { rejected = true; }
                Check("malformed chapter table rejected: " + label, rejected);
            }
            var ghostQuest = new ChapterTable(new List<ChapterSegmentDef>
            {
                new ChapterSegmentDef(ChapterSegment.Prologue, "甲", "乙", new List<string>(),
                    new List<string> { "查无此任务" }, "", "", 0, false),
            });
            var questRejected = false;
            try { ghostQuest.CrossCheck(quests); }
            catch (FormatException) { questRejected = true; }
            Check("a segment depending on a ghost quest is rejected", questRejected);

            // 顺序推进：不许跳段。
            var save = NewSave();
            Check("a new save starts at the prologue", ChapterLedger.Reached(save) == ChapterSegment.Prologue);
            var jumped = ChapterLedger.Enter(save, chapters, ChapterSegment.NightRoar, Day(), out var jumpError);
            Check("skipping ahead is refused", !jumped.Entered && jumpError.Length > 0, jumpError);

            // 逐段走通：条件到位就解锁，走到就写标记。
            WalkAll(save, chapters, quests, Night());
            Check("the whole axis can be walked",
                ChapterLedger.Reached(save) == ChapterSegment.Departure &&
                ChapterLedger.SettledCount(save) == chapters.Segments.Count,
                "走到 " + ChapterLedger.Reached(save) + "，结算 " + ChapterLedger.SettledCount(save) + " 段");
            Check("every segment wrote its one-shot flag", chapters.Segments
                .All(s => string.IsNullOrEmpty(s.GrantsFlag) || save.HasFlag(s.GrantsFlag)));
            Check("ledger reconciles", ChapterLedger.Reconciles(save, chapters));

            // 重复进入不重复发：已结算的段再进一次应当被拒。
            var repeat = ChapterLedger.Enter(save, chapters, ChapterSegment.BeastFight, Day(), out var repeatError);
            Check("a settled segment cannot be replayed", !repeat.Entered && repeatError.Contains("走过"));

            // 夜间段：白天不开放，命格不够也不开放，两个条件缺一不可。
            // 白天封路：先把 NightRoar 之前的四段走完（含 FreeRoam），再问"现在能不能开"。
            var nightSave = NewSave();
            nightSave.destiny = "anle";
            ChapterLedger.Schedule = schedules;
            ChapterLedger.Enter(nightSave, chapters, ChapterSegment.Prologue, Day(), out _);
            ChapterLedger.Enter(nightSave, chapters, ChapterSegment.Arrival, Day(), out _);
            DrainQuestChain(nightSave, quests, new[] { "tax-and-labor" });
            ChapterLedger.Enter(nightSave, chapters, ChapterSegment.TaxAndLabor, Day(), out _);
            ChapterLedger.Enter(nightSave, chapters, ChapterSegment.FreeRoam, Day(), out _);
            DrainQuestChain(nightSave, quests, new[] { "night-beast-roar" });
            var nightByDay = ChapterLedger.Enter(nightSave, chapters, ChapterSegment.NightRoar, Day(), out var dayError);
            Check("the night segment is shut by daylight", !nightByDay.Entered && dayError.Contains("夜里"), dayError);

            // 命薄者：夜里也开不了（这条验的是命格门控，不是时段）。
            var fated = NewSave();
            fated.destiny = "anle";
            ChapterLedger.Enter(fated, chapters, ChapterSegment.Prologue, Night(), out _);
            ChapterLedger.Enter(fated, chapters, ChapterSegment.Arrival, Night(), out _);
            DrainQuestChain(fated, quests, new[] { "tax-and-labor" });
            ChapterLedger.Enter(fated, chapters, ChapterSegment.TaxAndLabor, Night(), out _);
            ChapterLedger.Enter(fated, chapters, ChapterSegment.FreeRoam, Night(), out _);
            DrainQuestChain(fated, quests, new[] { "night-beast-roar" });
            var nightByFate = ChapterLedger.Enter(fated, chapters, ChapterSegment.NightRoar, Day(), out var fateError);
            Check("the night segment also needs a heavy fate", !nightByFate.Entered, fateError);
            var both = ChapterLedger.Enter(fated, chapters, ChapterSegment.NightRoar, Night(), out var bothError);
            Check("night plus a light fate still stays shut", !both.Entered, bothError);
            // 换八字够重的：同样在夜里，这回才开。
            fated.destiny = "shaxing";
            var heavy = ChapterLedger.Enter(fated, chapters, ChapterSegment.NightRoar, Night(), out var heavyError);
            Check("night plus a heavy fate opens the segment", heavy.Entered, heavyError);
        }

        // 从第一段一路推到 Departure：每段先解锁前置任务，再进入。
        static string lastWalkError;

        static void WalkAll(SaveData save, ChapterTable chapters, QuestTable quests, WorldTime night)
        {
            var walked = true;
            var walkedError = "";
            lastWalkError = "";
            foreach (var def in chapters.Segments.OrderBy(s => (int)s.Segment))
            {
                // 任务是链式的：反复刷新 + 做完"当前能做的下一件"，直到没有新的进展。
                // 不能按 id 字母序走——那会把链的顺序打乱（beast-origin 排在 night-beast-roar 之前）。
                DrainQuestChain(save, quests, def.RequiresQuests);
                var when = def.NightOnly > 0 ? night : Day();
                var entered = ChapterLedger.Enter(save, chapters, def.Segment, when, out var error);
                if (!entered.Entered) { walked = false; walkedError = "段 " + def.Segment + "：" + error; break; }
            }
        }

        // 把任务推到完成：逐个目标补进度再结算。目标推不动就带着原因报错，
        // 不做无界循环（上一版没有界，SaveChecks 起了三个进程卡死）。
        static void CompleteQuest(SaveData save, QuestTable quests, string questId)
        {
            var quest = quests.Find(questId);
            if (quest == null) throw new Exception("查不到任务：" + questId);
            QuestLedger.Of(save, quests, questId);
            // 任务状态是惰性的：前置刚完成时，这一条还是 Locked，得刷新一次。
            QuestLedger.RefreshAvailability(save, quests);
            if (!QuestLedger.TryStart(save, quest))
                throw new Exception("任务 " + questId + " 接取失败，当前 " + QuestLedger.StatusOf(save, questId));
            foreach (var objective in quest.Objectives)
                for (var i = 0; i < objective.Count + 2; i++)
                {
                    if (QuestLedger.Progress(save, quest, objective.Id) >= objective.Count) break;
                    if (!QuestLedger.Advance(save, quest, objective.Id, 1))
                        throw new Exception("任务 " + questId + " 的目标推不动：" + objective.Id);
                }
            if (!QuestLedger.IsDone(save, questId) && !QuestLedger.TryComplete(save, quest))
                throw new Exception("任务 " + questId + " 结算不了：目标可能没做完");
        }

        // 把任务链推到"本段要求的那些都完成"：每轮刷新状态、做完所有当前可接的，
        // 有界循环（推不动就带原因报错），不能按 id 排序走——那会打乱链序。
        static void DrainQuestChain(SaveData save, QuestTable quests, IReadOnlyList<string> required)
        {
            for (var round = 0; round < quests.Quests.Count + 2; round++)
            {
                QuestLedger.RefreshAvailability(save, quests);
                var progressed = false;
                foreach (var quest in quests.Quests)
                {
                    if (QuestLedger.IsDone(save, quest.Id)) continue;
                    if (QuestLedger.StatusOf(save, quest.Id) != QuestLedger.StatusAvailable) continue;
                    CompleteQuest(save, quests, quest.Id);
                    progressed = true;
                }
                if (required.All(id => QuestLedger.IsDone(save, id))) return;
                if (!progressed)
                    throw new Exception("任务链卡住了，还差：" +
                        string.Join("、", required.Where(id => !QuestLedger.IsDone(save, id))));
            }
            throw new Exception("任务链推不动");
        }

        static bool ChainHolds(ChapterTable chapters)
        {
            var settledFlags = new HashSet<string>();
            foreach (var def in chapters.Segments.OrderBy(s => (int)s.Segment))
            {
                // 本段要求的标记，要么是本段自己发的（不可能），要么是更早某段发的。
                foreach (var flag in def.RequiresFlags)
                    if (!settledFlags.Contains(flag)) return false;
                if (!string.IsNullOrEmpty(def.GrantsFlag)) settledFlags.Add(def.GrantsFlag);
            }
            return true;
        }

        static WorldTime Day() => new WorldTime { day = 2, minuteOfDay = 800 };
        // 夜间窗口 1320–1440（npc-schedules.json 的 bridges-night），必须落在窗口内。
        static WorldTime Night() => new WorldTime { day = 2, minuteOfDay = 1380 };

        static SaveData NewSave() => new SaveData
        {
            slot = 1, characterId = "farmer", characterName = "甲",
            gender = "male", faceStyle = 0, destiny = "shaxing",
        };

        static void Check(string name, bool condition, string detail = null)
        {
            if (!condition) throw new Exception("FAIL: " + name + (detail == null ? "" : "（" + detail + "）"));
            Console.WriteLine("PASS: " + name + (detail == null ? "" : "（" + detail + "）"));
        }
    }
}
