using System;
using System.Collections.Generic;
using System.Linq;

namespace TwelveJade.Core
{
    // 章骨架：把地图、NPC、任务、交易、战斗、对话按剧情顺序串成一条轴线。
    // 零件各自都能跑，但"能不能从新档玩到离镇"要靠这里——段落按顺序解锁，
    // 每段有自己的进入条件与结算，奖励只发一次。
    public enum ChapterSegment
    {
        Prologue = 0,      // 投胎问命
        Arrival = 1,       // 进入青石镇
        TaxAndLabor = 2,   // 税与役
        FreeRoam = 3,      // 自由探索
        NightRoar = 4,     // 夜里的兽吼
        BeastFight = 5,    // 妖兽战
        Investigation = 6, // 调查线索
        OracleTwist = 7,   // 神算惊变（占位）
        Departure = 8,     // 离镇（占位）
    }

    [Serializable]
    public sealed class ChapterSegmentDef
    {
        public ChapterSegmentDef(ChapterSegment segment, string title, string summary,
            List<string> requiresFlags, List<string> requiresQuests, string grantsFlag,
            string encounterId, int nightOnly, bool draft)
        {
            Segment = segment; Title = title; Summary = summary; RequiresFlags = requiresFlags;
            RequiresQuests = requiresQuests; GrantsFlag = grantsFlag; EncounterId = encounterId;
            NightOnly = nightOnly; Draft = draft;
        }

        public ChapterSegment Segment { get; }
        public string Title { get; }
        public string Summary { get; }
        public IReadOnlyList<string> RequiresFlags { get; }
        // 这些任务必须已 Completed 才解锁本段。
        public IReadOnlyList<string> RequiresQuests { get; }
        // 走完本段写入的一次性标记——既是奖励去重，也是下一段的钥匙。
        public string GrantsFlag { get; }
        public string EncounterId { get; }
        // 1 = 只在夜间窗口（由 WorldClock 判定）；0 = 随时。
        public int NightOnly { get; }
        // 剧情未审：文本标草稿，不进正式轴线的结算。
        public bool Draft { get; }
    }

    public sealed class ChapterTable
    {
        readonly List<ChapterSegmentDef> segments;

        public ChapterTable(List<ChapterSegmentDef> segments) { this.segments = segments; }

        public IReadOnlyList<ChapterSegmentDef> Segments => segments;
        public ChapterSegmentDef Find(ChapterSegment segment) => segments.FirstOrDefault(s => s.Segment == segment);

        public static ChapterTable Parse(string json)
        {
            if (!Json.TryParse(json, out var root, out var error))
                throw new FormatException("章节表读取失败：" + error);
            if (root.kind != JsonValue.Kind.Object) throw new FormatException("章节表应是一个对象。");

            var segments = new List<ChapterSegmentDef>();
            foreach (var entry in Elements(root, "segments"))
            {
                var id = Required(entry, "id");
                if (!System.Enum.TryParse<ChapterSegment>(id, out var segment))
                    throw new FormatException("未知段落：" + id);
                if (segments.Any(s => s.Segment == segment))
                    throw new FormatException("段落重复：" + id);
                segments.Add(new ChapterSegmentDef(segment, Required(entry, "title"),
                    Required(entry, "summary"), Strings(entry, "requiresFlags"),
                    Strings(entry, "requiresQuests"), entry["grantsFlag"].AsString(""),
                    entry["encounterId"].AsString(""), entry["nightOnly"].AsInt(0),
                    entry["draft"].AsBool(false)));
            }
            if (segments.Count == 0) throw new FormatException("章节表里没有段落。");
            // 轴线必须从头到尾连续：少一段中间人就走不过去了。
            foreach (ChapterSegment segment in Enum.GetValues(typeof(ChapterSegment)))
                if (segments.All(s => s.Segment != segment))
                    throw new FormatException("章节缺段落：" + segment);
            return new ChapterTable(segments);
        }

        public void CrossCheck(QuestTable quests)
        {
            foreach (var segment in segments)
                foreach (var questId in segment.RequiresQuests)
                    if (quests.Find(questId) == null)
                        throw new FormatException("段落 " + segment.Segment + " 依赖不存在的任务：" + questId);
        }

        static IEnumerable<JsonValue> Elements(JsonValue parent, string key)
        {
            var array = parent[key];
            if (array.IsNull) yield break;
            if (array.kind != JsonValue.Kind.Array) throw new FormatException("字段 " + key + " 应是数组。");
            foreach (var item in array.Items)
            {
                if (item == null || item.kind != JsonValue.Kind.Object)
                    throw new FormatException("字段 " + key + " 的元素应是对象。");
                yield return item;
            }
        }

        static List<string> Strings(JsonValue entry, string key)
        {
            var result = new List<string>();
            foreach (var value in entry[key].Items)
            {
                var text = value.AsString("").Trim();
                if (string.IsNullOrEmpty(text)) throw new FormatException("字段 " + key + " 含空串。");
                result.Add(text);
            }
            return result;
        }

        static string Required(JsonValue entry, string key)
        {
            var value = entry[key].AsString(null);
            if (string.IsNullOrWhiteSpace(value)) throw new FormatException("缺少字段 " + key + "（应为非空字符串）。");
            return value;
        }
    }

    // 存档里的章进度：走到第几段 + 哪几段已结算。
    [Serializable]
    public sealed class ChapterState
    {
        public int reached = (int)ChapterSegment.Prologue;
        public string[] settled = Array.Empty<string>();
    }

    public sealed class ChapterStepResult
    {
        public bool Entered;
        public string Reason = "";
        public ChapterSegment Segment;
        public SpoilsResult Spoils;
        public bool EverythingSettled = true;
    }

    // 章账本：轴线推进的唯一入口。奖励只发一次（靠 settled 数组），夜间段按时钟门控。
    public static class ChapterLedger
    {
        public static ChapterState Of(SaveData save)
        {
            save.chapter ??= new ChapterState();
            save.chapter.settled ??= Array.Empty<string>();
            return save.chapter;
        }

        public static ChapterSegment Reached(SaveData save) =>
            (ChapterSegment)Math.Min(Math.Max(Of(save).reached, (int)ChapterSegment.Prologue),
                (int)ChapterSegment.Departure);

        public static bool IsSettled(SaveData save, ChapterSegment segment) =>
            Of(save).settled.Contains(segment.ToString());

        public static int SettledCount(SaveData save) => Of(save).settled.Length;

        // 能否进入某段：必须顺序推进（不许跳段），且前置标记与任务都到位。
        // 进入前先把任务表刷新一遍：任务状态是惰性的，前置刚完成时下游任务可能还挂着 Locked。
        public static bool CanEnter(SaveData save, ChapterTable table, ChapterSegment segment,
            QuestTable quests = null, ItemTable items = null, NpcTable npcs = null)
        {
            var def = table?.Find(segment);
            if (def == null) return false;
            if ((int)segment > (int)Reached(save) + 1) return false;
            if (IsSettled(save, segment)) return false;
            foreach (var flag in def.RequiresFlags)
                if (!save.HasFlag(flag)) return false;
            if (quests != null)
            {
                QuestLedger.RefreshAvailability(save, quests);
                foreach (var questId in def.RequiresQuests)
                    if (QuestLedger.StatusOf(save, questId) != QuestLedger.StatusCompleted) return false;
            }
            return true;
        }

        // 进入并结算：发标记、推进 reached、按段发奖。重复调用不会再发一次。
        // 日程表由调用方注入（轴线自己不持有表，避免两处真相）；为空时夜间段一律不过——保守。
        public static NpcScheduleTable Schedule;
        public static QuestTable Quests;
        public const string NightScheduleId = "huolang";
        // 与黑市同一套命格白名单：命薄的人夜里进了后山也是耳背。
        static readonly HashSet<string> NightFates =
            new HashSet<string>(Trade.BlackMarketFavouredDestinies, StringComparer.Ordinal);

        public static ChapterStepResult Enter(SaveData save, ChapterTable table, ChapterSegment segment,
            WorldTime when, out string error)
        {
            error = "";
            var result = new ChapterStepResult { Segment = segment };
            var def = table?.Find(segment);
            if (def == null) { error = "章节表里没有这一段：" + segment; return result; }
            if (IsSettled(save, segment)) { error = "这一段已经走过了。"; return result; }
            if ((int)segment > (int)Reached(save) + 1)
            {
                error = "还走不到这儿——先把上一段走完。";
                return result;
            }
            foreach (var flag in def.RequiresFlags)
                if (!save.HasFlag(flag)) { error = "条件不足：缺 " + flag; return result; }
            if (Quests != null)
            {
                QuestLedger.RefreshAvailability(save, Quests);
                foreach (var questId in def.RequiresQuests)
                    if (QuestLedger.StatusOf(save, questId) != QuestLedger.StatusCompleted)
                    {
                        error = "任务还没了结：" + questId;
                        return result;
                    }
            }
            // 夜间段：时段与命格两条都得满足（与黑市同一套门槛），缺一不可。
            if (def.NightOnly > 0)
            {
                var nightOk = ScheduleRules.IsOnDuty(when, Schedule, NightScheduleId);
                // 命格门控：夜里听得见动静的人，得八字够重（与黑市同一套白名单）。
                var fateOk = save != null && NightFates.Contains(save.destiny);
                if (!nightOk || !fateOk)
                {
                    error = !nightOk
                        ? "现在还没动静——得等夜里。"
                        : "夜里是有动静，可你八字不够重，听不真切。";
                    return result;
                }
            }

            // 结算：标记 + 推进 + 一次性奖励（战斗段走 CombatSpoils，其余段只写标记）。
            if (!string.IsNullOrEmpty(def.GrantsFlag)) save.MarkFlag(def.GrantsFlag);
            var state = Of(save);
            state.settled = state.settled.Contains(def.Segment.ToString())
                ? state.settled
                : state.settled.Concat(new[] { def.Segment.ToString() }).ToArray();
            state.reached = Math.Max(state.reached, (int)def.Segment);
            result.Entered = true;
            return result;
        }

        // 全轴对账：每段至多结算一次、标记不重、reached 与 settled 自洽。
        public static bool Reconciles(SaveData save, ChapterTable table)
        {
            if (save == null || table == null) return false;
            var state = Of(save);
            if (state.settled.Distinct().Count() != state.settled.Length) return false;
            foreach (var name in state.settled)
                if (!System.Enum.TryParse<ChapterSegment>(name, out var segment)) return false;
                else if (table.Find(segment) == null) return false;
            foreach (var name in state.settled)
            {
                var segment = System.Enum.Parse<ChapterSegment>(name);
                if ((int)segment > state.reached) return false;
            }
            return true;
        }
    }
}
