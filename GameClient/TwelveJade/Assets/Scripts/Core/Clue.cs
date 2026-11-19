using System;
using System.Collections.Generic;
using System.Linq;

namespace TwelveJade.Core
{
    // 线索：一条结构化事实，不是拼起来的一句话。
    // 来源 NPC、可靠度（亲见/听说/道听途说）、可见条件、关联事件。
    // 同一线索重复获得必须幂等——打听十次也还是一条。
    public sealed class ClueDef
    {
        public ClueDef(string id, string title, string text, string sourceNpc, int reliability,
            List<string> requiredFlags, List<string> blocksFlags, string eventId)
        {
            Id = id; Title = title; Text = text; SourceNpc = sourceNpc; Reliability = reliability;
            RequiredFlags = requiredFlags; BlocksFlags = blocksFlags; EventId = eventId;
        }

        public string Id { get; }
        public string Title { get; }
        public string Text { get; }
        public string SourceNpc { get; }
        // 0 = 道听途说，1 = 听说，2 = 亲见。可靠度越高，文本里越接近事实。
        public int Reliability { get; }
        public IReadOnlyList<string> RequiredFlags { get; }
        public IReadOnlyList<string> BlocksFlags { get; }
        // 获得这条线索会在 NPC 记忆里留的事件 id（供"他已经说过了"判定）。
        public string EventId { get; }
    }

    // 存档里的一条线索：id + 何时获得 + 是否已看过。
    [Serializable]
    public sealed class ClueEntry
    {
        public string id = "";
        public int day;
        public int minuteOfDay;
        public bool read;
    }

    public sealed class ClueTable
    {
        readonly List<ClueDef> clues;

        ClueTable(List<ClueDef> clues) { this.clues = clues; }

        public IReadOnlyList<ClueDef> Clues => clues;
        public ClueDef Find(string id) => id == null ? null : clues.FirstOrDefault(c => c.Id == id);

        public static ClueTable Parse(string json)
        {
            if (!Json.TryParse(json, out var root, out var error))
                throw new FormatException("线索表读取失败：" + error);
            if (root.kind != JsonValue.Kind.Object) throw new FormatException("线索表应是一个对象。");

            var clues = new List<ClueDef>();
            foreach (var entry in Elements(root, "clues"))
            {
                var id = Required(entry, "id");
                if (clues.Any(c => c.Id == id)) throw new FormatException("线索 id 重复：" + id);
                var reliability = entry["reliability"].AsInt(-1);
                if (reliability < 0 || reliability > 2)
                    throw new FormatException("线索 " + id + " 的可靠度应在 0–2 之间。");
                var source = Required(entry, "sourceNpc");
                if (string.IsNullOrWhiteSpace(source))
                    throw new FormatException("线索 " + id + " 缺少来源。");
                clues.Add(new ClueDef(id, Required(entry, "title"), Required(entry, "text"), source,
                    reliability, Strings(entry, "requiredFlags"), Strings(entry, "blocksFlags"),
                    entry["eventId"].AsString("")));
            }
            if (clues.Count == 0) throw new FormatException("线索表里没有线索。");
            return new ClueTable(clues);
        }

        // 与人物表对表：来源 NPC 必须是表里真实存在的人。
        public void CrossCheck(NpcTable npcs)
        {
            foreach (var clue in clues)
                if (npcs.Find(clue.SourceNpc) == null)
                    throw new FormatException("线索 " + clue.Id + " 的来源不在人物表里：" + clue.SourceNpc);
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

    // 线索账本：存档里那份的唯一写入口。
    public static class ClueLedger
    {
        public const int MaxClues = 64;

        public static IEnumerable<ClueEntry> Of(SaveData save) =>
            save?.clues ?? Enumerable.Empty<ClueEntry>();

        public static ClueEntry Find(SaveData save, string id) =>
            Of(save).FirstOrDefault(c => c != null && c.id == id);

        public static bool Has(SaveData save, string id) => Find(save, id) != null;

        // 条件：requiredFlags 全在存档标记里、blocksFlags 一个都不在，才看得见。
        public static bool IsVisible(SaveData save, ClueDef clue)
        {
            if (clue == null) return false;
            foreach (var flag in clue.RequiredFlags)
                if (!save.HasFlag(flag)) return false;
            foreach (var flag in clue.BlocksFlags)
                if (save.HasFlag(flag)) return false;
            return true;
        }

        // 获得线索：幂等——已有就返回 false，不改存档。满了也不静默丢，给调用方明确结果。
        public static bool Acquire(SaveData save, ClueTable table, string id, WorldTime when, out string error)
        {
            error = "";
            if (save == null) { error = "存档为空。"; return false; }
            if (Has(save, id)) return false;
            var def = table?.Find(id);
            if (def == null) { error = "线索表里没有：" + id; return false; }
            if (!IsVisible(save, def)) { error = "条件不足，拿不到线索：" + id; return false; }
            if (Of(save).Count() >= MaxClues) { error = "线索已满（" + MaxClues + " 条）。"; return false; }
            save.clues = Of(save).Append(new ClueEntry
            {
                id = id,
                day = when?.day ?? WorldTime.StartDay,
                minuteOfDay = when?.minuteOfDay ?? WorldTime.StartMinute,
            }).ToArray();
            return true;
        }

        // 已看过的线索不再计入"待读"角标。
        public static void MarkRead(SaveData save, string id)
        {
            var entry = Find(save, id);
            if (entry != null) entry.read = true;
        }

        public static int UnreadCount(SaveData save) => Of(save).Count(c => !c.read);
    }
}
