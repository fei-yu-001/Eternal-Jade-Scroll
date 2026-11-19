using System;
using System.Collections.Generic;
using System.Linq;
using TwelveJade.Core;

namespace TwelveJade.Economy
{
    // 大纲 0.3 的隐藏气运账簿。
    //
    // 三条纪律，一条都不能破：
    // 1. **不弹"功德+1"**。玩家只能从旁人一句"自打那事以后你走哪儿都顺当点儿"里感觉到。
    //    Record 因此不返回任何给玩家看的文案——返回的是"记下了没有"，不是"加了多少"。
    // 2. **不占任务位**。善事是一个动作被世界看见的结果，没有"完成"按钮。
    // 3. **允许重复但收益递减**：同一件事做第二次加得少，第五次不加——逼着玩家去做新的那件事。
    //
    // 25 条清单本身不在这里（T096）：它是内容，读 Resources/Config/karma.json。
    // Record 因此要求**显式交表**：忘了装表就编译不过，而不是运行时静默拒收一切善行。
    public static class KarmaTracker
    {
        // kind 的两个字面量真源在 Core（存档校验要用），这里只是转发同名别名：
        // 同一个字符串在两层各写一遍就是 T095 那一族双源。
        public const string Deed = KarmaKinds.Deed;      // 善事：你主动伸手
        public const string Chance = KarmaKinds.Chance;  // 奇遇：它伸过来，而你没松手

        /// <summary>攒够几件才引动隐藏机缘（沈怀安多送一卦、货郎主动提传闻）。</summary>
        public const int BoonThreshold = 3;

        // 收益递减：第一次满分，越往后越少，第五次起不再给。
        public static float WeightOf(int times)
        {
            switch (times)
            {
                case 1: return 1f;
                case 2: return .6f;
                case 3: return .35f;
                case 4: return .15f;
                default: return 0f;
            }
        }

        public static bool IsKnown(KarmaTable table, string eventId) => table.Find(eventId) != null;

        /// <summary>记一件善事或一处奇遇。返回"是否第一次记下"（重复只累加次数、不再记分）。
        /// 表外 id 一律不收：凭空造的善事不算数。落盘由调用方负责——见 KarmaPersistenceNotice。</summary>
        public static bool Record(SaveData save, KarmaTable table, string eventId, WorldTime when)
        {
            if (save == null || table == null) return false;
            if (string.IsNullOrEmpty(eventId)) return false;
            var def = table.Find(eventId);
            if (def == null) return false;
            save.karmaEvents ??= Array.Empty<KarmaEntry>();
            var entry = save.karmaEvents.FirstOrDefault(k => k != null && k.eventId == eventId);
            if (entry != null)
            {
                entry.times++;
                entry.day = when?.day ?? entry.day;
                entry.minuteOfDay = when?.minuteOfDay ?? entry.minuteOfDay;
                return false;
            }
            save.karmaEvents = save.karmaEvents.Append(new KarmaEntry
            {
                eventId = eventId,
                kind = def.Kind,
                day = when?.day ?? WorldTime.StartDay,
                minuteOfDay = when?.minuteOfDay ?? WorldTime.StartMinute,
                times = 1,
            }).ToArray();
            return true;
        }

        /// <summary>记账只改内存。写档是调用方的责任，责任必须写在明面上（T097）：
        /// 每一个 Record 调用点后面都要跟一次仓储写入，并有一条"重新读档后条目还在"的断言。</summary>
        public const string KarmaPersistenceNotice =
            "KarmaTracker.Record 只写内存；调用方必须紧接着写一次档，否则善行随退出一起蒸发。";

        public static int TimesOf(SaveData save, string eventId) =>
            save?.karmaEvents?.FirstOrDefault(k => k != null && k.eventId == eventId)?.times ?? 0;

        public static float WeightOf(SaveData save, string eventId) => WeightOf(TimesOf(save, eventId));

        public static int CountOfKind(SaveData save, string kind) =>
            save?.karmaEvents?.Count(k => k != null && k.kind == kind) ?? 0;

        public static int DeedCount(SaveData save) => CountOfKind(save, Deed);
        public static int ChanceCount(SaveData save) => CountOfKind(save, Chance);

        /// <summary>隐藏机缘是否已引动：不显示、不提示，只在此处被世界读到。
        /// 消费方是对白层（沈怀安多送一卦、货郎主动提传闻），条件键是 DialogueCondition 的 karma>=。</summary>
        public static bool HasFortuneBoons(SaveData save) =>
            DeedCount(save) + ChanceCount(save) >= BoonThreshold;

        /// <summary>记账总件数：deed 与 chance 各算一件（不乘 times，重复做同一件事不抬高门槛判定）。</summary>
        public static int TotalCount(SaveData save) =>
            save?.karmaEvents?.Count(k => k != null) ?? 0;

        public static bool HasDone(SaveData save, string eventId) => TimesOf(save, eventId) > 0;
    }

    // 善事与奇遇的清单（读 karma.json）。名字/判定文案都在表里，改它 = 改内容，不碰代码。
    public sealed class KarmaTable
    {
        /// <summary>运行期加载路径（Resources 下相对路径，不带扩展名）。</summary>
        public const string ResourcePath = "Config/karma";
        public const string FileName = "karma.json";

        public sealed class Def
        {
            public Def(string id, string kind, string name, string rule, string note = "")
            { Id = id; Kind = kind; Name = name; Rule = rule; Note = note; }
            public string Id { get; }
            public string Kind { get; }
            public string Name { get; }
            /// <summary>怎么判定算"做了"——写代码时照这条守。</summary>
            public string Rule { get; }
            /// <summary>本轮实际接到哪一步（可空）。**判定口径比原文宽松时必须写**：
            /// 表里 `rule` 一个字不改（那是大纲 0.3 的原文），把"这一轮记的是哪一步"
            /// 单独写在这里，读表的人就不会把"接下了秘密"当成"瞒到了事情过去"（T084）。</summary>
            public string Note { get; }
        }

        readonly List<Def> entries;

        KarmaTable(List<Def> entries) { this.entries = entries; }

        public static KarmaTable Empty { get; } = new KarmaTable(new List<Def>());

        public IReadOnlyList<Def> Entries => entries;
        public IReadOnlyList<Def> Deeds => entries.Where(d => d.Kind == KarmaTracker.Deed).ToList();
        public IReadOnlyList<Def> Chances => entries.Where(d => d.Kind == KarmaTracker.Chance).ToList();

        public Def Find(string id) => string.IsNullOrEmpty(id) ? null
            : entries.FirstOrDefault(d => d.Id == id);

        public static KarmaTable Parse(string json)
        {
            if (!Json.TryParse(json, out var root, out var error))
                throw new FormatException("气运清单读取失败：" + error);
            // 兼容两种写法：裸数组（本仓配置的样子）与 {"karma":[...]}。
            var array = root.kind == JsonValue.Kind.Array ? root : root["karma"];
            if (array.IsNull) throw new FormatException("气运清单应是数组，或含 karma 数组。");
            if (array.kind != JsonValue.Kind.Array) throw new FormatException("气运清单的 karma 应是数组。");

            var entries = new List<Def>();
            foreach (var entry in array.Items)
            {
                if (entry == null || entry.kind != JsonValue.Kind.Object)
                    throw new FormatException("气运清单的元素应是对象。");
                var id = Required(entry, "id");
                if (entries.Any(d => d.Id == id)) throw new FormatException("气运条目 id 重复：" + id);
                var kind = Required(entry, "kind");
                if (kind != KarmaTracker.Deed && kind != KarmaTracker.Chance)
                    throw new FormatException("气运条目 " + id + " 的类型未定义：" + kind +
                        "（可选 " + KarmaTracker.Deed + " / " + KarmaTracker.Chance + "）");
                entries.Add(new Def(id, kind, Required(entry, "name"), Required(entry, "rule"),
                    entry["note"].AsString("")));
            }
            if (entries.Count == 0) throw new FormatException("气运清单里没有条目。");
            return new KarmaTable(entries);
        }

        static string Required(JsonValue entry, string key)
        {
            var value = entry[key].AsString(null);
            if (string.IsNullOrWhiteSpace(value))
                throw new FormatException("气运条目缺少字段 " + key + "（应为非空字符串）。");
            return value;
        }
    }
}
