using System;
using System.Collections.Generic;
using System.Linq;

namespace TwelveJade.Core
{
    // 作息窗口：某个 NPC 在一段时间内待在某个地点，是否可交易、是不是黑市。
    // 时间用"当天第几分钟"（0–1439）表示，跨午夜的窗口写成两条（如 19:00–05:00 拆成两条），
    // 这样重叠检测只需要比较同一条轴上的区间，不用处理环形区间。
    public sealed class ScheduleWindow
    {
        public ScheduleWindow(string id, int startMinute, int endMinute, string placeId,
            bool tradeable, bool black, string note)
        {
            Id = id; StartMinute = startMinute; EndMinute = endMinute; PlaceId = placeId;
            Tradeable = tradeable; Black = black; Note = note;
        }

        public string Id { get; }
        public int StartMinute { get; }
        public int EndMinute { get; }
        public string PlaceId { get; }
        // 可交易：货郎在集市开铺、在双桥开黑市都能做买卖。
        public bool Tradeable { get; }
        // 黑市窗口：只有这类窗口里才看得见黑市货。
        public bool Black { get; }
        public string Note { get; }

        public int Length => EndMinute - StartMinute;
        // 左闭右开：窗口首分钟算在内、末分钟算在外，相邻窗口才接得上又不重叠。
        public bool Contains(int minuteOfDay) => minuteOfDay >= StartMinute && minuteOfDay < EndMinute;
        public string Range => WorldClock.Clock(StartMinute) + "–" + WorldClock.Clock(EndMinute);
    }

    public sealed class NpcScheduleDef
    {
        public NpcScheduleDef(string id, string npcId, List<ScheduleWindow> windows)
        {
            Id = id; NpcId = npcId; Windows = windows;
        }

        public string Id { get; }
        public string NpcId { get; }
        public IReadOnlyList<ScheduleWindow> Windows { get; }

        public ScheduleWindow At(int minuteOfDay) => Windows.FirstOrDefault(w => w.Contains(minuteOfDay));
        public ScheduleWindow TradeWindowAt(int minuteOfDay) => Windows.FirstOrDefault(w => w.Contains(minuteOfDay) && w.Tradeable);
        public ScheduleWindow BlackWindowAt(int minuteOfDay) => Windows.FirstOrDefault(w => w.Contains(minuteOfDay) && w.Black);
        public bool IsOnDuty(int minuteOfDay) => TradeWindowAt(minuteOfDay) != null;
    }

    public sealed class NpcScheduleTable
    {
        readonly List<NpcScheduleDef> schedules;

        public NpcScheduleTable(List<NpcScheduleDef> schedules) { this.schedules = schedules; }

        public IReadOnlyList<NpcScheduleDef> Schedules => schedules;
        public NpcScheduleDef Find(string id) => string.IsNullOrEmpty(id) ? null : schedules.FirstOrDefault(s => s.Id == id);

        public static NpcScheduleTable Parse(string json)
        {
            if (!Json.TryParse(json, out var root, out var error))
                throw new FormatException("作息表读取失败：" + error);
            if (root.kind != JsonValue.Kind.Object) throw new FormatException("作息表应是一个对象。");

            var schedules = new List<NpcScheduleDef>();
            foreach (var entry in Elements(root, "schedules"))
            {
                var id = Required(entry, "id", "作息表");
                if (schedules.Any(s => s.Id == id)) throw new FormatException("作息 id 重复：" + id);
                var npcId = Required(entry, "npcId", "作息 " + id);
                var windows = new List<ScheduleWindow>();
                var windowIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var line in Elements(entry, "windows"))
                {
                    var windowId = Required(line, "id", "作息 " + id + " 的窗口");
                    if (!windowIds.Add(windowId))
                        throw new FormatException("作息 " + id + " 的窗口 id 重复：" + windowId);
                    var start = line["start"].AsInt(-1);
                    var end = line["end"].AsInt(-1);
                    if (start < 0 || end > WorldTime.MinutesPerDay || start >= end)
                        throw new FormatException("作息 " + id + " 的窗口 " + windowId + " 时间非法：" +
                            start + "–" + end + "（应为 0–" + WorldTime.MinutesPerDay + " 且首<末）");
                    windows.Add(new ScheduleWindow(windowId, start, end,
                        Required(line, "place", "作息 " + id + " 的窗口 " + windowId),
                        line["trade"].AsBool(false), line["black"].AsBool(false), line["note"].AsString("")));
                }
                if (windows.Count == 0) throw new FormatException("作息 " + id + " 没有任何窗口。");
                // 同一 NPC 的窗口不许重叠：重叠了"此刻他在哪"就有两个答案。
                for (var i = 0; i < windows.Count; i++)
                    for (var j = i + 1; j < windows.Count; j++)
                        if (windows[i].StartMinute < windows[j].EndMinute && windows[j].StartMinute < windows[i].EndMinute)
                            throw new FormatException("作息 " + id + " 的窗口 " + windows[i].Id + " 与 " +
                                windows[j].Id + " 时间重叠。");
                schedules.Add(new NpcScheduleDef(id, npcId, windows));
            }
            if (schedules.Count == 0) throw new FormatException("作息表里没有作息。");
            return new NpcScheduleTable(schedules);
        }

        // 与地图对表：NPC 与地点都得是地图上真实存在的，否则窗口指向空气。
        public void CrossCheck(TownMap town)
        {
            if (town == null) throw new ArgumentNullException(nameof(town));
            foreach (var schedule in schedules)
            {
                if (town.Npcs.All(n => n.Id != schedule.NpcId))
                    throw new FormatException("作息 " + schedule.Id + " 指向地图上没有的 NPC：" + schedule.NpcId);
                foreach (var window in schedule.Windows)
                    if (town.Landmarks.All(l => l.Id != window.PlaceId))
                        throw new FormatException("作息 " + schedule.Id + " 的窗口 " + window.Id +
                            " 指向地图上没有的地点：" + window.PlaceId);
            }
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

        static string Required(JsonValue entry, string key, string owner)
        {
            var value = entry[key].AsString(null);
            if (string.IsNullOrWhiteSpace(value))
                throw new FormatException(owner + " 缺少字段 " + key + "（应为非空字符串）。");
            return value;
        }
    }

    // 作息与命格的组合判定：黑市两个条件缺一不可——
    // 命格看得见（Trade 里那份白名单）与此刻在黑市窗口内，缺一个都开不了。
    // 单独读某个条件没用，所以判定只从这里出。
    public static class ScheduleRules
    {
        public static bool SeesBlackMarket(SaveData save, WorldTime time, NpcScheduleTable table, string scheduleId)
        {
            if (time == null || table == null) return false;
            var schedule = table.Find(scheduleId);
            if (schedule == null) return false;
            return Trade.SeesBlackMarket(save) && schedule.BlackWindowAt(time.minuteOfDay) != null;
        }

        // NPC 此刻在不在可交易窗口：不在场时旧按钮不该还能下单。
        public static bool IsOnDuty(WorldTime time, NpcScheduleTable table, string scheduleId)
        {
            var schedule = table?.Find(scheduleId);
            return schedule != null && schedule.IsOnDuty(time?.minuteOfDay ?? 0);
        }

        // NPC 此刻在哪：不在任何窗口里就返回空串（"不在镇上"）。
        public static string PlaceAt(WorldTime time, NpcScheduleTable table, string npcId)
        {
            var minute = time?.minuteOfDay ?? 0;
            foreach (var schedule in table?.Schedules ?? Array.Empty<NpcScheduleDef>())
                if (schedule.NpcId == npcId)
                    return schedule.At(minute)?.PlaceId ?? "";
            return "";
        }

        // 下一个可交易窗口的起点（当天分钟），没有就返回 -1——给"他今晚再来"这类提示用。
        public static int NextTradeMinute(WorldTime time, NpcScheduleTable table, string scheduleId)
        {
            var schedule = table?.Find(scheduleId);
            if (schedule == null || time == null) return -1;
            var current = time.minuteOfDay;
            var today = schedule.Windows.Where(w => w.Tradeable && w.StartMinute > current)
                .OrderBy(w => w.StartMinute).FirstOrDefault();
            if (today != null) return today.StartMinute;
            var tomorrow = schedule.Windows.Where(w => w.Tradeable).OrderBy(w => w.StartMinute).FirstOrDefault();
            return tomorrow == null ? -1 : tomorrow.StartMinute;
        }
    }
}
