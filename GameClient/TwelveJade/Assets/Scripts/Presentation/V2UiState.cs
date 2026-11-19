using System;
using System.Collections.Generic;
using System.Linq;
using TwelveJade.Core;
namespace TwelveJade.Presentation
{
    // Read-only presentation of the real ledgers. Opening/refreshing a panel never settles a quest.
    public sealed class V2ChapterBrief
    {
        public string Title = "", Goal = "", Progress = "", Next = "", Details = "", Footer = "";
        public ChapterSegmentDef NextDefinition;
        public bool NextReady;
        public static V2ChapterBrief Build(SaveData save, ChapterTable chapters, QuestTable quests, NpcScheduleTable schedules)
        {
            if (save == null || chapters == null || quests == null)
                return new V2ChapterBrief { Title = "寻道札记", Goal = "当前行卷尚未就绪。", Next = "请先入世，再查阅此卷。" };
            var reached = (ChapterSegment)Math.Min(Math.Max(save.chapter?.reached ?? 0, 0), (int)ChapterSegment.Departure);
            var current = chapters.Find(reached);
            var settled = save.chapter?.settled ?? Array.Empty<string>();
            var all = chapters.Segments.All(s => settled.Contains(s.Segment.ToString()));
            var owned = quests.Quests.Where(q => current != null && current.Quests.Contains(q.Id)).ToList();
            var active = owned.FirstOrDefault(q => QuestLedger.DisplayStatus(save, q) == QuestLedger.StatusActive)
                ?? owned.FirstOrDefault(q => QuestLedger.DisplayStatus(save, q) == QuestLedger.StatusAvailable);
            var result = new V2ChapterBrief { Title = "主线 · " + (current?.Title ?? "寻道"),
                Goal = all ? "本章已了结。已知线索与镇中故人仍可在卷中查阅。" : active?.Title ?? current?.Summary ?? "循当前见闻寻访。",
                Progress = "章程 " + ((int)reached + 1) + " / " + chapters.Segments.Count + " · 已结 " + settled.Distinct().Count() + " 段" };
            if (!all && active == null && (settled.Contains(reached.ToString()) ||
                (owned.Count > 0 && owned.All(q => QuestLedger.DisplayStatus(save, q) == QuestLedger.StatusCompleted))))
                result.Goal = "此段事项已了结，可查阅下一步条件。";
            var lines = new List<string> { current?.Summary ?? "", "", result.Progress };
            foreach (var quest in owned)
            {
                var status = QuestLedger.DisplayStatus(save, quest);
                lines.Add("\n" + quest.Title + " · " + StatusName(status));
                if (quest.Objectives.Count > 0)
                {
                    int done = quest.Objectives.Sum(o => Math.Min(Math.Max(1, o.Count), Math.Max(0, QuestLedger.Progress(save, quest, o.Id))));
                    int required = quest.Objectives.Sum(o => Math.Max(1, o.Count));
                    string progress = status == QuestLedger.StatusCompleted ? "目标已随任务结算" : "目标进度 " + done + " / " + required;
                    lines.Add(progress);
                    if (ReferenceEquals(quest, active)) result.Goal += "\n" + progress;
                }
            }
            var next = !all && (int)reached < (int)ChapterSegment.Departure ? chapters.Find((ChapterSegment)((int)reached + 1)) : null;
            result.NextDefinition = next;
            if (next == null) result.Next = all ? "本章已了结 · 可回看见闻" : "章程已至末段 · 依当前目标收尾";
            else
            {
                var missing = new List<string>();
                foreach (var flag in next.RequiresFlags.Where(f => !save.HasFlag(f)))
                {
                    var source = chapters.Segments.FirstOrDefault(s => s.GrantsFlag == flag);
                    var quest = quests.Quests.FirstOrDefault(q => q.Rewards.Flags.Contains(flag));
                    missing.Add(source != null ? "了结「" + source.Title + "」" : quest != null ? "完成「" + quest.Title + "」" : "完成当前段落的前置见闻");
                }
                foreach (var id in next.RequiresQuests.Where(id => QuestLedger.StatusOf(save, id) != QuestLedger.StatusCompleted))
                    missing.Add("完成「" + (quests.Find(id)?.Title ?? id) + "」");
                if (next.NightOnly > 0)
                {
                    if (!ScheduleRules.IsOnDuty(save.WorldTimeNow(), schedules, ChapterLedger.NightScheduleId)) missing.Add("等候夜间时段");
                    if (!Trade.BlackMarketFavouredDestinies.Contains(save.destiny)) missing.Add("命格尚不合夜访条件");
                }
                result.NextReady = missing.Count == 0 && !settled.Contains(next.Segment.ToString());
                string condition = result.NextReady ? "条件已齐" : string.Join("；", missing.Distinct());
                result.Next = "下一步 · " + next.Title + (next.Draft ? "（草稿）" : "") + "\n" + condition;
            }
            lines.Add("\n" + result.Next);
            result.Details = string.Join("\n", lines);
            var clues = save.clues ?? Array.Empty<ClueEntry>();
            result.Footer = "已结 " + settled.Distinct().Count() + " 段 · 线索 " + clues.Length + " 条 · 未读 " + clues.Count(c => c != null && !c.read) + " 条";
            return result;
        }
        public static string StatusName(string status) => status switch
        {
            QuestLedger.StatusCompleted => "已了结", QuestLedger.StatusActive => "在做", QuestLedger.StatusAvailable => "可接",
            QuestLedger.StatusFailed => "办砸了", _ => "尚未开启"
        };
    }
    // The draft and every committed snapshot own separate objects, including after a first save.
    public sealed class V2SettingsDraft
    {
        public UserSettings Value { get; }
        public V2SettingsDraft(UserSettings current) { Value = Copy(current); }
        public UserSettings Snapshot() => Copy(Value);
        public static UserSettings Copy(UserSettings value)
        {
            value ??= new UserSettings();
            return new UserSettings { schemaVersion = value.schemaVersion, masterVolume = value.masterVolume,
                musicVolume = value.musicVolume, effectsVolume = value.effectsVolume, width = value.width,
                height = value.height, fullscreen = value.fullscreen, reduceMotion = value.reduceMotion };
        }
    }
}
