using System;
using System.Collections.Generic;
using System.Linq;

namespace TwelveJade.Core
{
    // 人格：有限枚举，不是任意字符串（GDD 十·第二层）。人格不决定数值，
    // 只决定"同一件事对这个人意味着什么"——它在 NpcTemper 里变成一组系数。
    public enum NpcTemper
    {
        Honest,    // 老实：善意看得见，欺瞒最伤他
        Greedy,    // 贪：帮忙照收，好感涨得慢，钱的事记得清
        Wary,      // 慎：初见不轻信，信任要一点点换
        Bold,      // 胆：仇怨消得快，不怕结梁子
        Kind,      // 善：你对他好，他记你一辈子
        Cold,      // 冷：好话不进他耳朵
    }

    // 事件：玩家做过的事。关系与记忆只由这里产生，UI 不许自己加减好感。
    public enum NpcEvent
    {
        Helped,          // 帮了忙
        RefusedHelp,     // 拒绝了求助
        Gave,            // 送过东西
        TookBack,        // 翻脸讨回
        Lied,            // 对他撒过谎
        Confessed,       // 承认了错
        Traded,          // 做过买卖
        Cheated,         // 在他这儿耍过手段
        Fought,          // 动过手
        Defused,         // 替他解过围
    }

    // 人格系数：同一个事件在不同人身上的分量。全部 0–1，界面上不必出现小数。
    public struct NpcTemperProfile
    {
        public NpcTemperProfile(float trustFromHelp, float goodwillFromHelp, float memoryBias,
            float hostilityDecay, float trustFromLying)
        {
            TrustFromHelp = trustFromHelp; GoodwillFromHelp = goodwillFromHelp; MemoryBias = memoryBias;
            HostilityDecay = hostilityDecay; TrustFromLying = trustFromLying;
        }

        public float TrustFromHelp;     // 帮忙换来的信任
        public float GoodwillFromHelp;  // 帮忙换来的好感
        public float MemoryBias;        // 记忆强度倍率（记性好的更难忘）
        public float HostilityDecay;    // 敌意自然消解
        public float TrustFromLying;    // 撒谎扣掉的信任（老实人更受伤）
    }

    // 人生目标（GDD 十·第五层）：第一版只读表里的定义，存档只记进度，不自动生成行为。
    public sealed class NpcGoal
    {
        public NpcGoal(string id, string title, string detail, int target)
        {
            Id = id; Title = title; Detail = detail; Target = target;
        }

        public string Id { get; }
        public string Title { get; }
        public string Detail { get; }
        public int Target { get; }
    }

    public sealed class NpcDef
    {
        public NpcDef(string id, string name, string title, NpcTemper temper,
            string greeting, List<NpcGoal> goals)
        {
            Id = id; Name = name; Title = title; Temper = temper; Greeting = greeting; Goals = goals;
        }

        public string Id { get; }
        public string Name { get; }
        public string Title { get; }
        public NpcTemper Temper { get; }
        public string Greeting { get; }
        public IReadOnlyList<NpcGoal> Goals { get; }
        public NpcGoal Goal(string goalId) => Goals.FirstOrDefault(g => g.Id == goalId);
    }

    public sealed class NpcTable
    {
        readonly List<NpcDef> npcs;

        public NpcTable(List<NpcDef> npcs) { this.npcs = npcs; }

        public IReadOnlyList<NpcDef> Npcs => npcs;
        public NpcDef Find(string id) => string.IsNullOrEmpty(id) ? null : npcs.FirstOrDefault(n => n.Id == id);

        public static NpcTable Parse(string json)
        {
            if (!Json.TryParse(json, out var root, out var error))
                throw new FormatException("人物表读取失败：" + error);
            if (root.kind != JsonValue.Kind.Object) throw new FormatException("人物表应是一个对象。");

            var npcs = new List<NpcDef>();
            foreach (var entry in Elements(root, "npcs"))
            {
                var id = Required(entry, "id");
                if (npcs.Any(n => n.Id == id)) throw new FormatException("人物 id 重复：" + id);
                var temper = Temper(entry, id);
                var goals = new List<NpcGoal>();
                var goalIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var line in Elements(entry, "goals"))
                {
                    var goalId = Required(line, "id", "人物 " + id + " 的目标");
                    if (!goalIds.Add(goalId))
                        throw new FormatException("人物 " + id + " 的目标 id 重复：" + goalId);
                    var target = line["target"].AsInt(1);
                    if (target < 1) throw new FormatException("人物 " + id + " 的目标 " + goalId + " 的完成数需至少为 1。");
                    goals.Add(new NpcGoal(goalId, Required(line, "title", "人物 " + id + " 的目标 " + goalId),
                        line["detail"].AsString(""), target));
                }
                npcs.Add(new NpcDef(id, Required(entry, "name", "人物 " + id),
                    Required(entry, "title", "人物 " + id), temper,
                    Required(entry, "greeting", "人物 " + id), goals));
            }
            if (npcs.Count == 0) throw new FormatException("人物表里没有人物。");
            return new NpcTable(npcs);
        }

        static NpcTemper Temper(JsonValue entry, string id)
        {
            var raw = Required(entry, "temper", "人物 " + id);
            if (!Enum.TryParse<NpcTemper>(raw, ignoreCase: false, out var temper))
                throw new FormatException("人物 " + id + " 的人格未定义：" + raw +
                    "（可选 " + string.Join("/", Enum.GetNames(typeof(NpcTemper))) + "）");
            return temper;
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

        static string Required(JsonValue entry, string key, string owner = null)
        {
            var value = entry[key].AsString(null);
            if (string.IsNullOrWhiteSpace(value))
                throw new FormatException((owner == null ? "" : owner + " ") + "缺少字段 " + key + "（应为非空字符串）。");
            return value;
        }

        // 档案缺失时给显示层一句能直接念出来的话，别让 UI 自己编。
        public string DescribeMissing(string npcId) =>
            "「" + (npcId ?? "") + "」的档案不在人物表里，这一段只能先这么记着。";
    }

    // 关系：好感、信任、敌意三条独立轴，各有上下限。好感高不等于信任高。
    public sealed class NpcRelation
    {
        public const int Min = -100, Max = 100;
        public int goodwill;
        public int trust;
        public int hostility;

        public NpcRelation() { }

        public NpcRelation(int goodwill, int trust, int hostility)
        {
            this.goodwill = goodwill; this.trust = trust; this.hostility = hostility;
        }

        public static NpcRelation Clamp(NpcRelation relation) => new NpcRelation(
            Math.Min(Math.Max(relation.goodwill, Min), Max),
            Math.Min(Math.Max(relation.trust, Min), Max),
            Math.Min(Math.Max(relation.hostility, Min), Max));

        public bool IsValid =>
            goodwill >= Min && goodwill <= Max && trust >= Min && trust <= Max &&
            hostility >= Min && hostility <= Max;
    }

    // 记忆：一条结构化事件（谁、何时、多强、说过了没有），不是拼起来的一句话。
    [Serializable]
    public sealed class NpcMemoryEntry
    {
        public string eventId = "";
        public int day;
        public int minuteOfDay;
        public int strength;
        public bool spoken;
    }

    // 目标的进度：目标 id + 走到哪 + 是不是被什么绊住了。
    [Serializable]
    public sealed class NpcGoalState
    {
        public string goalId = "";
        public int progress;
        public string blockedBy = "";
    }

    [Serializable]
    public sealed class NpcState
    {
        public string id = "";
        public NpcRelation relation = new NpcRelation();
        public NpcMemoryEntry[] memory = Array.Empty<NpcMemoryEntry>();
        public NpcGoalState[] goals = Array.Empty<NpcGoalState>();
    }

    // 关系的唯一写入口：事件 → 系数 → 关系变化 + 记忆条目。
    // UI 拿到 NpcState 只能读；要在界面上加好感，必须走 Apply。
    public static class NpcLedger
    {
        public const int MaxMemories = 32;
        // 同类事件再发生一次，记忆加深的幅度。
        public const int RepeatDeepen = 3;
        public const int MaxGoals = 8;

        // 老实人最容易被谎言伤到，胆大的人不当回事——撒谎扣的信任因此反着人格走。
        static readonly Dictionary<NpcTemper, NpcTemperProfile> Profiles = new()
        {
            [NpcTemper.Honest] = new NpcTemperProfile(.5f, .6f, 1.1f, .5f, 2.0f),
            [NpcTemper.Greedy] = new NpcTemperProfile(.3f, .3f, 1.0f, .3f, .8f),
            [NpcTemper.Wary] = new NpcTemperProfile(.4f, .4f, 1.0f, .4f, 1.5f),
            [NpcTemper.Bold] = new NpcTemperProfile(.6f, .5f, .8f, .8f, .7f),
            [NpcTemper.Kind] = new NpcTemperProfile(.7f, .8f, 1.2f, .6f, 1.4f),
            [NpcTemper.Cold] = new NpcTemperProfile(.2f, .2f, 1.0f, .5f, .8f),
        };

        public static NpcTemperProfile ProfileOf(NpcTemper temper) => Profiles[temper];

        public static NpcState Of(SaveData save, NpcDef npc)
        {
            if (save == null || npc == null) return null;
            save.npcs ??= Array.Empty<NpcState>();
            var state = save.npcs.FirstOrDefault(n => n != null && n.id == npc.Id);
            if (state == null)
            {
                state = new NpcState
                {
                    id = npc.Id,
                    relation = new NpcRelation(),
                    memory = Array.Empty<NpcMemoryEntry>(),
                    goals = npc.Goals.Select(g => new NpcGoalState { goalId = g.Id, progress = 0, blockedBy = "" })
                        .ToArray(),
                };
                save.npcs = save.npcs.Concat(new[] { state }).ToArray();
            }
            state.relation ??= new NpcRelation();
            state.memory ??= Array.Empty<NpcMemoryEntry>();
            state.SyncGoals(npc);
            return state;
        }

        public static NpcRelation RelationOf(SaveData save, string npcId) =>
            save?.npcs?.FirstOrDefault(n => n != null && n.id == npcId)?.relation;

        // 事件 id：同一 NPC 的同一次事件只记一次（重复结算不会把好感刷上去）。
        public static bool HasMemory(SaveData save, string npcId, string eventId) =>
            save?.npcs?.FirstOrDefault(n => n != null && n.id == npcId)?.memory
                .Any(m => m != null && m.eventId == eventId) ?? false;

        // 应用一个事件。返回是否真的记下了新记忆。
        public static bool Apply(SaveData save, NpcDef npc, NpcEvent kind, WorldTime when, int weight = 1)
        {
            var state = Of(save, npc);
            if (state == null) return false;
            var eventId = kind.ToString();
            var existing = state.memory.FirstOrDefault(m => m != null && m.eventId == eventId);
            if (existing != null)
            {
                // 同一件事又发生了一次：不新增条目（存档里一条就够），但记得更深。
                // 关系不重复结算——否则玩家反复做好事就能把好感刷上去。
                existing.strength = Math.Min(existing.strength + RepeatDeepen, 100);
                return false;
            }

            var profile = ProfileOf(npc.Temper);
            var scale = Math.Max(1, Math.Min(weight, 10));
            var (goodwill, trust, hostility) = EffectOf(kind, profile, scale);
            var relation = state.relation;
            relation.goodwill += goodwill;
            relation.trust += trust;
            relation.hostility += hostility;
            // 敌意会自己消解：恨久了也得淡下去，世界不围着玩家转。
            if (hostility == 0 && relation.hostility > 0)
                relation.hostility = Math.Max(0, relation.hostility - (int)Math.Ceiling(profile.HostilityDecay * scale));
            state.relation = NpcRelation.Clamp(relation);

            var strength = (int)Math.Round(Math.Abs(goodwill) + Math.Abs(trust) * .5f +
                                            Math.Abs(hostility) * profile.MemoryBias);
            var entry = new NpcMemoryEntry
            {
                eventId = eventId,
                day = when?.day ?? WorldTime.StartDay,
                minuteOfDay = when?.minuteOfDay ?? WorldTime.StartMinute,
                strength = Math.Min(Math.Max(strength, 1), 100),
                spoken = false,
            };
            // 记忆满了丢掉最弱的那条，保留"他最记得的那件事"。
            var kept = state.memory.Where(m => m != null).ToList();
            kept.Add(entry);
            state.memory = kept.OrderByDescending(m => m.strength).ThenBy(m => m.eventId)
                .Take(MaxMemories).ToArray();
            return true;
        }

        static (int goodwill, int trust, int hostility) EffectOf(NpcEvent kind, NpcTemperProfile p, int scale)
        {
            return kind switch
            {
                NpcEvent.Helped => (Round(p.GoodwillFromHelp * 4 * scale), Round(p.TrustFromHelp * 5 * scale), 0),
                NpcEvent.Defused => (Round(p.GoodwillFromHelp * 3 * scale), Round(p.TrustFromHelp * 4 * scale), -2 * scale),
                NpcEvent.Gave => (Round(p.GoodwillFromHelp * 5 * scale), Round(p.TrustFromHelp * 2 * scale), 0),
                NpcEvent.RefusedHelp => (-Round(p.GoodwillFromHelp * 2 * scale), 0, Round(p.HostilityDecay * 3 * scale)),
                NpcEvent.TookBack => (-Round(p.GoodwillFromHelp * 6 * scale), -Round(p.TrustFromHelp * 4 * scale), Round(p.HostilityDecay * 5 * scale)),
                NpcEvent.Lied => (0, -Round(p.TrustFromLying * 5 * scale), Round(p.HostilityDecay * 2 * scale)),
                NpcEvent.Confessed => (Round(p.GoodwillFromHelp * 2 * scale), Round(p.TrustFromHelp * 3 * scale), -Round(p.HostilityDecay * 2 * scale)),
                NpcEvent.Traded => (1 * scale, 0, 0),
                NpcEvent.Cheated => (-Round(p.GoodwillFromHelp * 4 * scale), -Round(p.TrustFromLying * 3 * scale), Round(p.HostilityDecay * 4 * scale)),
                NpcEvent.Fought => (-Round(p.GoodwillFromHelp * 5 * scale), -Round(p.TrustFromHelp * 3 * scale), 8 * scale),
                _ => (0, 0, 0),
            };
        }

        static int Round(double value) => (int)Math.Round(value);

        // 目标进度：只允许推进到表里声明的上限。
        public static bool AdvanceGoal(SaveData save, NpcDef npc, string goalId, int delta)
        {
            var state = Of(save, npc);
            var goal = state?.goals?.FirstOrDefault(g => g != null && g.goalId == goalId);
            var def = npc?.Goal(goalId);
            if (goal == null || def == null || delta <= 0) return false;
            goal.progress = Math.Min(goal.progress + delta, def.Target);
            return true;
        }

        public static int ProgressOf(SaveData save, NpcDef npc, string goalId) =>
            save?.npcs?.FirstOrDefault(n => n != null && n.id == npc?.Id)?.goals?
                .FirstOrDefault(g => g != null && g.goalId == goalId)?.progress ?? 0;

        public static bool IsGoalDone(SaveData save, NpcDef npc, string goalId)
        {
            var state = save?.npcs?.FirstOrDefault(n => n != null && n.id == npc?.Id);
            var progress = state?.goals?.FirstOrDefault(g => g != null && g.goalId == goalId)?.progress ?? 0;
            var def = npc?.Goal(goalId);
            return def != null && progress >= def.Target;
        }
    }

    static class NpcStateExtensions
    {
        // 表里新增的目标补一条 0 进度，已有的进度不动——迁移必须可反复调用。
        public static void SyncGoals(this NpcState state, NpcDef npc)
        {
            if (state?.goals == null) return;
            foreach (var goal in npc.Goals)
                if (state.goals.All(g => g == null || g.goalId != goal.Id))
                    state.goals = state.goals.Concat(new[] { new NpcGoalState { goalId = goal.Id } }).ToArray();
        }
    }
}
