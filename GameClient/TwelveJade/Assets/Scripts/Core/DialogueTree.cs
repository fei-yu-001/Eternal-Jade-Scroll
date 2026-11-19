using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TwelveJade.Core
{
    // ADR-027 记忆回响四阶：陌路 / 熟络 / 故交 / 患难。
    // 阶梯只有一个真源（本文件），界面与对白都不许自己再算一遍——
    // 两处各算一份，迟早出现"嘴上叫你故交、货架还按原价"的裂缝。
    public enum MemoryTier
    {
        Stranger = 0,      // 陌路：客套防备，无深层选项
        Acquaintance = 1,  // 熟络：主动招呼，商品 95 折
        Friend = 2,        // 故交：吐露往事，商品 9 折
        Sworn = 3,         // 患难：交付信物与核心剧情
    }

    public static class MemoryEcho
    {
        // 门槛写在这里而不是散在各张表里：改一档折扣只动一行。
        public const int AcquaintanceTrades = 2;
        public const int FriendTrades = 5;

        public static readonly string[] Names = { "陌路", "熟络", "故交", "患难" };

        public static string Name(MemoryTier tier) => Names[(int)tier];

        /// <summary>患难阶的落阶标记：完成专属羁绊任务时由任务链打上（前缀固定）。</summary>
        public static string BondFlag(string npcId) => "bonded:" + npcId;

        /// <summary>记忆阶梯：交易次数是底子，患难阶只认专属羁绊标记——
        /// 光买东西买不成过命的交情，这是设计意图，不是漏判。</summary>
        public static MemoryTier TierOf(SaveData save, string npcId, int trades)
        {
            if (save == null || string.IsNullOrEmpty(npcId)) return MemoryTier.Stranger;
            if (save.HasFlag(BondFlag(npcId))) return MemoryTier.Sworn;
            if (save.HasFlag(BondFlag(Alias(npcId)))) return MemoryTier.Sworn;
            if (trades >= FriendTrades) return MemoryTier.Friend;
            if (trades >= AcquaintanceTrades) return MemoryTier.Acquaintance;
            return MemoryTier.Stranger;
        }

        // 商人的记忆阶按交易次数走，人物表里的关系按事件走；两者用同一个 id 空间。
        // 任务书把沈怀安等人写成 "npc-" 前缀，货郎是裸 id，这里容忍两种写法。
        static string Alias(string npcId) =>
            npcId.StartsWith("npc-", StringComparison.Ordinal) ? npcId.Substring(4) : "npc-" + npcId;

        /// <summary>交情（MemoryTier）只用来解锁对白分支、货单、收货与黑市资格，
        /// **绝不进价格**（D-17 / T057）：声望是唯一定价者（ADR-032），
        /// 两个折扣并存时玩家看到的价永远是骗人的。
        /// 上一轮这里留着 Discount/DiscountPercent 两个函数，实测零生产调用点。</summary>
    }

    /// <summary>气运条目数的唯一口径（T095 的同一条纪律：数法放在 Core，Economy 侧转发）。
    /// 只数"记了几件事"，不换算成分数——大纲 1.3：气运最高效的表现是被搭话，不是发光的数字。</summary>
    public static class KarmaLedger
    {
        public static int Count(SaveData save) =>
            save?.karmaEvents == null ? 0 : save.karmaEvents.Count(k => k != null);
    }

    // 沈怀安摸骨相面：十九命格断语库，与 CharacterGen.Destinies 一一对应。
    // 放在 Core 而不是表现层——判据只有一份，界面不许自己另编一套命批。
    // 隐藏超品（玉楼引 / 无名）共用同一条"摸不透"：那是卷一末尾的伏笔，不是随机台词。
    public static class FortuneReading
    {
        public const int Price = 5;
        public const string UnreadableVerdict =
            "怪哉……这骨相老夫竟摸不透……嘻嘻嘻，后生，揣好你的命，别让人夺了去。";

        static readonly Dictionary<string, string> Verdicts = new()
        {
            ["jadeloft"] = UnreadableVerdict,
            ["unreadable"] = UnreadableVerdict,
            ["fallingstar"] = "灾星过命，可你偏偏还活着——老夫只说一句：别往人多的地方站。",
            ["benefactor"] = "贵人候。贵人就在你身边，是你眼下最不当回事的那一个。",
            ["hardfate"] = "命硬。扛得住兵灾，扛不住心里那点软。",
            ["pledge"] = "守信约。你身上压着半块玉佩的债，还不清，也躲不掉。",
            ["lonely"] = "孤命。六亲缘薄，路要一个人走——好在你还走得动。",
            ["debtor"] = "旧债。讨债的人迟早会来，老夫劝你先备好一句实话。",
            ["scholar"] = "文曲偏照。见字不忘，可惜这世道不考你这一门。",
            ["away"] = "离乡命。故土留不住你。走的时候别回头，回头就走不成了。",
            ["xia"] = "侠骨。命里多刀光，也多知己。你吃亏就吃在爱管闲事上。",
            ["shaxing"] = "杀星入命。是救人的刀还是索命的刀——老夫不敢写，你自己挑。",
            ["chuandeng"] = "传灯命。你要做旁人的引路人。灯传下去，这辈子就没白走。",
            ["anle"] = "安乐命。灶头有肉、缸里有米，这在乱世是大福气，别嫌它小。",
            ["caixing"] = "财星照命。命里带财，可惜财这东西，来时热闹，去时也热闹。",
            ["renxin"] = "仁心命。见病痛就挪不动步——你这一辈子，要救很多人。",
            ["chiqing"] = "痴情种。为一人可以不要性命。是你的劫，也是你的道。",
            ["longshe"] = "龙蛇命。乱世龙蛇，是龙是蛇，看时势，也看你的手腕。",
            ["xianyun"] = "闲云命。富贵功名都留不住你。你要的是山里一亩田、半卷书。",
        };

        /// <summary>认不出的命格也给一句能念的话，不给空串——空断语等于白花五文钱。</summary>
        public static string VerdictFor(string destiny) =>
            destiny != null && Verdicts.TryGetValue(destiny, out var line)
                ? line
                : "老夫看了半晌，只看出你要走很远的路。……嘻嘻嘻，别问了，问多了不灵。";

        public static bool IsUnreadable(string destiny) =>
            destiny == "jadeloft" || destiny == "unreadable";

        public static bool Knows(string destiny) =>
            destiny != null && Verdicts.ContainsKey(destiny);
    }

    // 分支条件求值：对白表里的 condition 字符串在这里变成真/假。
    // 支持的写法（可用 & 连接，全部满足才算满足）：
    //   memoryTier>=2 / trades>=5 / reputation>=20
    //   hasFlag:标记 / hasQuest:任务id（进行中） / questDone:任务id（已完成）
    // 认不出的写法一律判不满足——宁可少开一条分支，也不能把看不懂的条件当通过。
    public static class DialogueCondition
    {
        public static bool Satisfies(SaveData save, string npcId, int trades, string condition, QuestTable quests = null)
        {
            if (string.IsNullOrWhiteSpace(condition)) return true;
            foreach (var clause in condition.Split('&'))
            {
                var text = clause.Trim();
                if (text.Length == 0) continue;
                if (!One(save, npcId, trades, text, quests)) return false;
            }
            return true;
        }

        static bool One(SaveData save, string npcId, int trades, string clause, QuestTable quests)
        {
            var atLeast = clause.IndexOf(">=", StringComparison.Ordinal);
            if (atLeast > 0)
            {
                var key = clause.Substring(0, atLeast).Trim();
                if (!int.TryParse(clause.Substring(atLeast + 2), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out var want)) return false;
                switch (key)
                {
                    case "memoryTier": return (int)MemoryEcho.TierOf(save, npcId, trades) >= want;
                    case "trades": return trades >= want;
                    case "reputation": return save != null && save.localReputation >= want;
                    // 本轮唯一新增的 DSL 键（O3 / D-11）：气运攒够几件，世界就多看你一眼。
                    // 数法走 KarmaLedger（Core 真源），与 Economy 的转发同源。
                    case "karma": return save != null && KarmaLedger.Count(save) >= want;
                    default: return false;
                }
            }
            var colon = clause.IndexOf(':');
            if (colon > 0)
            {
                switch (clause.Substring(0, colon).Trim())
                {
                    case "hasFlag": return save != null && save.HasFlag(clause.Substring(colon + 1).Trim());
                    case "hasQuest": return save != null &&
                        QuestLedger.StatusOf(save, clause.Substring(colon + 1).Trim()) == QuestLedger.StatusActive;
                    case "questAvailable": return quests!=null &&
                        QuestLedger.DisplayStatus(save,quests.Find(clause.Substring(colon+1).Trim()))==QuestLedger.StatusAvailable;
                    case "questCompletable": return quests!=null &&
                        QuestRuntime.CanTurnIn(save,quests.Find(clause.Substring(colon+1).Trim()),npcId,0);
                    case "questDone": return save != null &&
                        QuestLedger.StatusOf(save, clause.Substring(colon + 1).Trim()) == QuestLedger.StatusCompleted;
                    default: return false;
                }
            }
            return false;
        }
    }

    // 选项的附加裁定：既有 DialogueOption 装不下这些字段（它在 Dialogue.cs 里已经定型），
    // 因此按"节点 id + 声明顺序"另存一份，不改动既有结构也不复制一份状态。
    public sealed class OptionMeta
    {
        public OptionMeta(string condition, int minMemoryTier, string action, string acceptQuest, string turnInQuest = "")
        {
            Condition = condition; MinMemoryTier = minMemoryTier; Action = action;
            AcceptQuest = acceptQuest; TurnInQuest = turnInQuest;
        }

        public string Condition { get; }
        public int MinMemoryTier { get; }
        /// <summary>openShop / acceptQuest / triggerFateCheck / close，空串表示纯台词推进。</summary>
        public string Action { get; }

        /// <summary>action 是 acceptQuest 时，这一支到底接哪条任务——**写在表里，不写在 C# 里**
        /// （T096 同一条铁律：内容是数据）。空串表示这道表没指定，表现层该喊而不是静默不接。</summary>
        public string AcceptQuest { get; }
        public string TurnInQuest { get; }

        public static readonly OptionMeta Plain = new OptionMeta("", 0, "", "");
    }

    // 对白树：既有 DialogueTable 负责"节点与连线"，这里补上"这一支此刻开不开"。
    // 分工是刻意的——DialogueTable 的断链校验与说话人对表照旧生效，
    // 记忆阶与条件只做减法（把此刻不该出现的选项摘掉），绝不凭空造节点。
    public sealed class DialogueTree
    {
        readonly DialogueTable table;
        readonly Dictionary<string, List<OptionMeta>> meta;

        DialogueTree(DialogueTable table, Dictionary<string, List<OptionMeta>> meta)
        {
            this.table = table; this.meta = meta;
        }

        public DialogueTable Table => table;
        public IReadOnlyList<DialogueNode> Nodes => table.Nodes;
        public DialogueNode Find(string nodeId) => table.Find(nodeId);

        /// <summary>某人的开场节点：表里他为第一个声明的节点（与 DialogueRules.Open 同一口径）。</summary>
        public string EntryNodeFor(string npcId) => table.ForNpc(npcId).FirstOrDefault()?.Id ?? "";

        public static DialogueTree Parse(string json)
        {
            // 先交给既有解析：缺字段、节点断链、说话人不在人物表，都在那里挡住。
            var table = DialogueTable.Parse(json);
            if (!Json.TryParse(json, out var root, out var error))
                throw new FormatException("对白树读取失败：" + error);
            var meta = new Dictionary<string, List<OptionMeta>>(StringComparer.Ordinal);
            foreach (var entry in Elements(root, "nodes"))
            {
                var list = new List<OptionMeta>();
                foreach (var option in Elements(entry, "options"))
                    list.Add(new OptionMeta(option["condition"].AsString(""),
                        option["minMemoryTier"].AsInt(0), option["action"].AsString(""),
                        option["acceptQuest"].AsString(""),option["turnInQuest"].AsString("")));
                meta[entry["id"].AsString("")] = list;
            }
            return new DialogueTree(table, meta);
        }

        /// <summary>此刻真正可选的分支：Core 的可见性（标记/线索/关系）先过，
        /// 再用记忆阶与本表条件做减法。返回的是 DialogueOption 本尊，调用方拿它去 Choose。</summary>
        public List<DialogueOption> BranchOptions(SaveData save, DialogueView view, int trades, QuestTable quests = null)
        {
            var result = new List<DialogueOption>();
            if (view == null) return result;
            var node = table.Find(view.NodeId);
            if (node == null) return result;
            for (var i = 0; i < node.Options.Count; i++)
            {
                var option = node.Options[i];
                if (!view.Options.Any(o => ReferenceEquals(o, option))) continue;
                var gate = MetaAt(node.Id, i);
                if (gate.MinMemoryTier > 0 &&
                    (int)MemoryEcho.TierOf(save, node.NpcId, trades) < gate.MinMemoryTier) continue;
                if (!DialogueCondition.Satisfies(save, node.NpcId, trades, gate.Condition, quests)) continue;
                result.Add(option);
            }
            return result;
        }

        /// <summary>选项挂的动作：openShop / acceptQuest / triggerFateCheck / close。</summary>
        public string ActionOf(string nodeId, DialogueOption option) =>
            MetaAt(nodeId, IndexOf(nodeId, option)).Action;

        /// <summary>这个 acceptQuest 选项要接的任务 id（表里写的，不是代码里猜的）。</summary>
        public string AcceptQuestOf(string nodeId, DialogueOption option) =>
            MetaAt(nodeId, IndexOf(nodeId, option)).AcceptQuest;

        public OptionMeta MetaOf(string nodeId, DialogueOption option) =>
            MetaAt(nodeId, IndexOf(nodeId, option));

        OptionMeta MetaAt(string nodeId, int index)
        {
            if (index < 0 || !meta.TryGetValue(nodeId, out var list)) return OptionMeta.Plain;
            return index < list.Count ? list[index] : OptionMeta.Plain;
        }

        int IndexOf(string nodeId, DialogueOption option)
        {
            var node = table.Find(nodeId);
            if (node == null || option == null) return -1;
            for (var i = 0; i < node.Options.Count; i++)
                if (ReferenceEquals(node.Options[i], option)) return i;
            return -1;
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
    }
}
