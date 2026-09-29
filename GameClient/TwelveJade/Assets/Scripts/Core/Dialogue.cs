using System;
using System.Collections.Generic;
using System.Linq;

namespace TwelveJade.Core
{
    // 对话节点：id、说话人、文本、选项、条件、效果、下一节点。
    // 铁律一：状态只认节点 id，绝不用 UI 文本顺序当状态——文本一改流程就散架。
    // 铁律二：节点与选项的条件都从存档现读（关系/标记/线索/词条），不预先算好存起来。
    public sealed class DialogueOption
    {
        public DialogueOption(string text, string nextNode, List<string> requiredFlags,
            List<string> blocksFlags, List<string> requiredClues, int minTrust, int minGoodwill,
            bool curiousOnly, string grantFlag, string grantClue, string socialEvent, int socialWeight)
        {
            Text = text; NextNode = nextNode; RequiredFlags = requiredFlags; BlocksFlags = blocksFlags;
            RequiredClues = requiredClues; MinTrust = minTrust; MinGoodwill = minGoodwill;
            CuriousOnly = curiousOnly; GrantFlag = grantFlag; GrantClue = grantClue;
            SocialEvent = socialEvent; SocialWeight = socialWeight;
        }

        public string Text { get; }
        public string NextNode { get; }
        public IReadOnlyList<string> RequiredFlags { get; }
        public IReadOnlyList<string> BlocksFlags { get; }
        public IReadOnlyList<string> RequiredClues { get; }
        public int MinTrust { get; }
        public int MinGoodwill { get; }
        // 只对带「好奇」词条的玩家开放（打听的高阶分支）。
        public bool CuriousOnly { get; }
        public string GrantFlag { get; }
        public string GrantClue { get; }
        public string SocialEvent { get; }
        public int SocialWeight { get; }
    }

    public sealed class DialogueNode
    {
        public DialogueNode(string id, string npcId, string text, List<DialogueOption> options, string next)
        {
            Id = id; NpcId = npcId; Text = text; Options = options; Next = next;
        }

        public string Id { get; }
        public string NpcId { get; }
        public string Text { get; }
        public IReadOnlyList<DialogueOption> Options { get; }
        // 没有选项时的去处（"" 表示对话到此为止）。
        public string Next { get; }
        public bool EndsConversation => Options.Count == 0;
    }

    public sealed class DialogueTable
    {
        readonly List<DialogueNode> nodes;

        public DialogueTable(List<DialogueNode> nodes) { this.nodes = nodes; }

        public IReadOnlyList<DialogueNode> Nodes => nodes;
        public DialogueNode Find(string id) => id == null ? null : nodes.FirstOrDefault(n => n.Id == id);

        public static DialogueTable Parse(string json)
        {
            if (!Json.TryParse(json, out var root, out var error))
                throw new FormatException("对话表读取失败：" + error);
            if (root.kind != JsonValue.Kind.Object) throw new FormatException("对话表应是一个对象。");

            var nodes = new List<DialogueNode>();
            var entryIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in Elements(root, "nodes"))
            {
                var id = Required(entry, "id");
                if (!entryIds.Add(id)) throw new FormatException("对话节点 id 重复：" + id);
                if (!entryIds.Add("entry:" + id) && entryIds.Contains("entry:" + id)) continue;
                var options = new List<DialogueOption>();
                foreach (var option in Elements(entry, "options"))
                {
                    var text = Required(option, "text");
                    var next = option["next"].AsString("");
                    if (string.IsNullOrWhiteSpace(next))
                        throw new FormatException("对话节点 " + id + " 的选项「" + text + "」缺少 next。");
                    options.Add(new DialogueOption(
                        text, next, Strings(option, "requiredFlags"), Strings(option, "blocksFlags"),
                        Strings(option, "requiredClues"),
                        option["minTrust"].AsInt(0), option["minGoodwill"].AsInt(0),
                        option["curiousOnly"].AsBool(false),
                        option["grantFlag"].AsString(""), option["grantClue"].AsString(""),
                        option["socialEvent"].AsString(""), option["socialWeight"].AsInt(1)));
                }
                var npcId = Required(entry, "npc");
                nodes.Add(new DialogueNode(id, npcId, Required(entry, "text"), options,
                    entry["next"].AsString("")));
            }
            if (nodes.Count == 0) throw new FormatException("对话表里没有节点。");
            // 指向不存在节点的 next 是断链，必须在解析期就拒。
            foreach (var node in nodes)
            {
                foreach (var target in node.Options.Select(o => o.NextNode).Concat(
                             node.EndsConversation && !string.IsNullOrEmpty(node.Next) ? new[] { node.Next } : Array.Empty<string>()))
                    if (nodes.All(n => n.Id != target))
                        throw new FormatException("对话节点 " + node.Id + " 指向不存在的节点：" + target);
            }
            return new DialogueTable(nodes);
        }

        // 说话人必须在人物表里（线索来源同理）。
        public void CrossCheck(NpcTable npcs)
        {
            foreach (var node in nodes)
                if (npcs.Find(node.NpcId) == null)
                    throw new FormatException("对话节点 " + node.Id + " 的说话人不在人物表里：" + node.NpcId);
        }

        public IEnumerable<DialogueNode> ForNpc(string npcId) =>
            nodes.Where(n => n.NpcId == npcId);

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

    // 存档里的对话进度：停在哪个人、哪个节点。Esc 中断后回到这里，不丢状态。
    [Serializable]
    public sealed class DialogueState
    {
        public string npcId = "";
        public string nodeId = "";
        public int startedDay;
        public int startedMinuteOfDay;
    }

    public sealed class DialogueView
    {
        public string NodeId = "";
        public string NpcId = "";
        public string Text = "";
        public IReadOnlyList<DialogueOption> Options = Array.Empty<DialogueOption>();
        public bool EndsConversation;
    }

    // 打听的三种结果：普通/成功/失败。成功才给线索，且"好奇"词条影响走哪一档。
    public enum AskOutcome { Nothing, Partial, Success }

    public static class DialogueRules
    {
        public const string CuriousTrait = "curious";

        static bool IsCurious(SaveData save) =>
            save?.traits != null && save.traits.Contains(CuriousTrait);

        static bool Visible(SaveData save, string npcId, DialogueOption option)
        {
            if (save == null) return false;
            foreach (var flag in option.RequiredFlags)
                if (!save.HasFlag(flag)) return false;
            foreach (var flag in option.BlocksFlags)
                if (save.HasFlag(flag)) return false;
            foreach (var clue in option.RequiredClues)
                if (!ClueLedger.Has(save, clue)) return false;
            if (option.CuriousOnly && !IsCurious(save)) return false;
            var relation = NpcLedger.RelationOf(save, npcId);
            if (relation != null)
            {
                if (relation.trust < option.MinTrust) return false;
                if (relation.goodwill < option.MinGoodwill) return false;
            }
            return true;
        }

        // 开一段对话：记下起点，返回当前节点视图。
        public static DialogueView Open(SaveData save, DialogueTable table, string npcId,
            string startNodeId = null, string socialEvent = "", int socialWeight = 0)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));
            if (table == null) throw new ArgumentNullException(nameof(table));
            var starts = table.ForNpc(npcId).Select(n => n.Id).ToList();
            if (starts.Count == 0) throw new InvalidOperationException("这个人没有对话：" + npcId);
            var start = startNodeId != null && table.Find(startNodeId) != null ? startNodeId : starts[0];
            return Enter(save, table, start);
        }

        static DialogueView Enter(SaveData save, DialogueTable table, string nodeId)
        {
            var node = table.Find(nodeId);
            if (node == null) throw new InvalidOperationException("对话节点不存在：" + nodeId);
            var options = node.Options.Where(o => Visible(save, node.NpcId, o)).ToList();
            return new DialogueView
            {
                NodeId = node.Id,
                NpcId = node.NpcId,
                Text = node.Text,
                Options = options,
                EndsConversation = options.Count == 0,
            };
        }

        // 选一项：按选项的 next 前进，落标记/线索/关系，然后返回新视图。
        public static DialogueView Choose(SaveData save, DialogueTable table, NpcTable npcs,
            ClueTable clues, DialogueView current, int optionIndex, WorldTime when, out string notice)
        {
            notice = "";
            if (save == null || current == null) throw new ArgumentNullException();
            if (optionIndex < 0 || optionIndex >= current.Options.Count)
                throw new ArgumentOutOfRangeException(nameof(optionIndex), "没有这个选项。");
            var option = current.Options[optionIndex];

            if (!string.IsNullOrEmpty(option.GrantFlag)) save.MarkFlag(option.GrantFlag);
            if (!string.IsNullOrEmpty(option.GrantClue))
            {
                if (!ClueLedger.Acquire(save, clues, option.GrantClue, when, out var clueError) && !ClueLedger.Has(save, option.GrantClue))
                    notice = clueError;
            }
            if (!string.IsNullOrEmpty(option.SocialEvent) && npcs != null)
            {
                var npc = npcs.Find(current.NpcId);
                if (npc != null)
                    NpcLedger.Apply(save, npc, ParseEvent(option.SocialEvent), when,
                        option.SocialWeight <= 0 ? 1 : option.SocialWeight);
            }
            return Enter(save, table, option.NextNode);
        }

        // 事件名对不上时不猜：默认按"做过买卖"记（关系微涨），并在解析期用 CrossCheck 挡住真错。
        static NpcEvent ParseEvent(string id) =>
            System.Enum.TryParse<NpcEvent>(id, out var value) ? value : NpcEvent.Traded;

        // 打听：不给正确答案，按关系与"好奇"词条分三档。线索已经在手上就不重复给。
        public static AskOutcome Ask(SaveData save, DialogueTable table, NpcTable npcs,
            ClueTable clues, string npcId, string clueId, string socialEvent, WorldTime when, out string clueError)
        {
            clueError = "";
            if (save == null || table == null || clues == null) throw new ArgumentNullException();
            var def = clues.Find(clueId);
            if (def == null) throw new InvalidOperationException("线索表里没有：" + clueId);
            if (ClueLedger.Has(save, clueId)) { clueError = "这条你已经知道��"; return AskOutcome.Nothing; }
            var relation = NpcLedger.RelationOf(save, npcId);
            var trust = relation?.trust ?? 0;
            var goodwill = relation?.goodwill ?? 0;
            var curious = IsCurious(save);
            AskOutcome outcome;
            if (trust >= 40 || (curious && goodwill >= 20)) outcome = AskOutcome.Success;
            else if (trust > 0 || goodwill > 0 || curious) outcome = AskOutcome.Partial;
            else outcome = AskOutcome.Nothing;
            if (outcome == AskOutcome.Success)
            {
                if (!ClueLedger.Acquire(save, clues, clueId, when, out clueError) && !ClueLedger.Has(save, clueId))
                    return AskOutcome.Nothing;
            }
            if (npcs != null && !string.IsNullOrEmpty(socialEvent))
            {
                var npc = npcs.Find(npcId);
                if (npc != null)
                    NpcLedger.Apply(save, npc, ParseEvent(socialEvent), when, outcome == AskOutcome.Success ? 2 : 1);
            }
            return outcome;
        }

        // 存档往返：写起点与节点，读回来能续上。
        // 只在"没有对话可快照"时返回 null —— 之前写成 save.dialogue == null，
        // 等于"第一次打断就存不进去"，Esc 续聊永远从头开始。
        public static DialogueState Snapshot(SaveData save, DialogueView view, WorldTime when) =>
            save == null || view == null || string.IsNullOrEmpty(view.NodeId) ? null : new DialogueState
            {
                npcId = view.NpcId,
                nodeId = view.NodeId,
                startedDay = when?.day ?? WorldTime.StartDay,
                startedMinuteOfDay = when?.minuteOfDay ?? WorldTime.StartMinute,
            };

        public static void Save(SaveData save, DialogueState state) => save.dialogue = state;

        // Esc 之后按存档续上：没有存档就当新对话开。
        public static DialogueView Resume(SaveData save, DialogueTable table, string npcId, out string resumedFrom)
        {
            resumedFrom = "";
            var state = save?.dialogue;
            if (state != null && !string.IsNullOrEmpty(state.nodeId) && table.Find(state.nodeId) != null)
            {
                resumedFrom = state.nodeId;
                return Enter(save, table, state.nodeId);
            }
            return Open(save, table, npcId);
        }
    }
}
