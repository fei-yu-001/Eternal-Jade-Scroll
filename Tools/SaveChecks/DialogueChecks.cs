using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TwelveJade.Core;

namespace TwelveJade.Core
{
    // M5-04 验收：对话结构化分支、线索幂等、存档往返（Esc 不丢状态）、错误输入拒绝。
    public static class DialogueChecks
    {
        public static void Run(NpcTable npcs, string configDir)
        {
            var dialogue = DialogueTable.Parse(File.ReadAllText(Path.Combine(configDir, "dialogues.json")));
            dialogue.CrossCheck(npcs);
            var clues = ClueTable.Parse(File.ReadAllText(Path.Combine(configDir, "clues.json")));
            clues.CrossCheck(npcs);

            Check("dialogue table parses", dialogue.Nodes.Count >= 8, dialogue.Nodes.Count + " 个节点");
            Check("every node belongs to a real npc",
                dialogue.Nodes.All(n => npcs.Find(n.NpcId) != null));
            Check("nodes carry options or an end", dialogue.Nodes.All(n => n.Options.Count > 0 || !string.IsNullOrEmpty(n.Next) || n.Next == ""));
            Check("both test characters have a start node",
                dialogue.ForNpc("yaoshi").Any() && dialogue.ForNpc("huolang").Any());

            // 坏表必须被拒：断链、空选项文本、说话人不存在、id 重复。
            var bad = new[]
            {
                ("{\"nodes\":[{\"id\":\"a\",\"npc\":\"yaoshi\",\"text\":\"甲\",\"options\":[{\"text\":\"走\",\"next\":\"没有这个节点\"}]}]}", "指向不存在的节点"),
                ("{\"nodes\":[{\"id\":\"a\",\"npc\":\"yaoshi\",\"text\":\"甲\",\"options\":[{\"text\":\"\",\"next\":\"a\"}]}]}", "选项文本为空"),
                ("{\"nodes\":[{\"id\":\"a\",\"npc\":\"yaoshi\",\"text\":\"甲\",\"options\":[{\"next\":\"a\"}]}]}", "选项缺文本"),
                ("{\"nodes\":[]}", "没有节点")
            };
            foreach (var (json, label) in bad)
            {
                var rejected = false;
                try { DialogueTable.Parse(json); }
                catch (FormatException) { rejected = true; }
                Check("malformed dialogue table rejected: " + label, rejected);
            }
            var unknownNpc = new DialogueTable(new List<DialogueNode>
            {
                new DialogueNode("a", "查无此人", "甲", new List<DialogueOption>(), ""),
            });
            var npcRejected = false;
            try { unknownNpc.CrossCheck(npcs); }
            catch (FormatException) { npcRejected = true; }
            Check("dialogue with an unknown speaker is rejected", npcRejected);

            var badClues = new[]
            {
                ("{\"clues\":[]}", "没有线索"),
                ("{\"clues\":[{\"id\":\"a\",\"title\":\"甲\",\"text\":\"乙\",\"sourceNpc\":\"yaoshi\",\"reliability\":9}]}", "可靠度越界"),
                ("{\"clues\":[{\"id\":\"a\",\"title\":\"甲\",\"text\":\"乙\",\"sourceNpc\":\"\"}]}", "来源为空"),
                ("{\"clues\":[{\"id\":\"a\",\"title\":\"甲\",\"text\":\"乙\",\"sourceNpc\":\"查无此人\",\"reliability\":1}]}", "来源不在人物表"),
            };
            foreach (var (json, label) in badClues)
            {
                var rejected = false;
                try { ClueTable.Parse(json).CrossCheck(npcs); }
                catch (FormatException) { rejected = true; }
                Check("malformed clue table rejected: " + label, rejected);
            }

            // 正常流程：开对话 → 问草 → 拿线索 → 幂等 → 存档往返。
            var save = NewSave();
            var when = new WorldTime { day = 2, minuteOfDay = 540 };
            var view = DialogueRules.Open(save, dialogue, "yaoshi");
            Check("opening a dialogue lands on the first node", view.NodeId == "yaoshi-intro" && view.Options.Count == 2,
                view.NodeId);

            // 关系门控：林药师对陌生人信任为 0，"好奇"分支不该出现。
            Check("curious-only branch hidden without the trait",
                view.Options.All(o => o.Text != "药里会不会有问题？"));

            view = DialogueRules.Choose(save, dialogue, npcs, clues, view, 0, when, out _);
            Check("choosing the first option advances the node", view.NodeId == "yaoshi-herb");
            var herbOption = Array.FindIndex(view.Options.ToArray(), o => o.Text == "少了多少？");
            Check("herb option visible", herbOption >= 0);
            view = DialogueRules.Choose(save, dialogue, npcs, clues, view, herbOption, when, out _);
            Check("asking about the count yields the clue", view.NodeId == "yaoshi-count" && ClueLedger.Has(save, "herb-thinning"));

            // 幂等：同一线索再拿一次不会多出一条。
            var clueCount = ClueLedger.Of(save).Count();
            ClueLedger.Acquire(save, clues, "herb-thinning", when, out _);
            Check("acquiring the same clue twice is idempotent", ClueLedger.Of(save).Count() == clueCount);

            // 条件线索：没有 told-the-truth 标记就拿不到 poison-trace。
            ClueLedger.Acquire(save, clues, "poison-trace", when, out var missError);
            Check("a clue behind a flag stays out of reach when the flag is missing",
                !string.IsNullOrEmpty(missError) && !ClueLedger.Has(save, "poison-trace"), missError);

            // 打听三档：关系差 → Nothing；好感有一点 → Partial；信任高 → Success。
            var stranger = NewSave();
            Check("asking with no relation yields nothing",
                DialogueRules.Ask(stranger, dialogue, npcs, clues, "yaoshi", "herb-thinning", "Traded", when, out _) ==
                AskOutcome.Nothing);

            var friendly = NewSave();
            var yaoshi = npcs.Find("yaoshi");
            NpcLedger.Of(friendly, yaoshi);
            NpcLedger.Apply(friendly, yaoshi, NpcEvent.Gave, when, 3);
            var relationAfterOne = NpcLedger.RelationOf(friendly, "yaoshi");
            Check("a kind act raises goodwill before trust",
                relationAfterOne.goodwill > 0, "好感 +" + relationAfterOne.goodwill + "（送礼只买好感，不买信任）");
            // 刚认识就送过礼：有 goodwill 但信任还不够 → 打听只给"半个答复"，不交底。
            Check("a casual ask after a gift is only partial",
                DialogueRules.Ask(friendly, dialogue, npcs, clues, "yaoshi", "herb-thinning", "Traded", when, out _) ==
                AskOutcome.Partial && !ClueLedger.Has(friendly, "herb-thinning"));
            // 关系是渐进兑换的：日常做好事每次只加两三点信任，门槛得慢慢磨。
            // 这里直接把关系推到"他信得过你"的档位，验的是**门槛判定**而不是攒关系的速度。
            var trusted = NewSave();
            NpcLedger.Of(trusted, yaoshi);
            var trustedState = NpcLedger.Of(trusted, yaoshi);
            trustedState.relation = new NpcRelation(50, 45, 0);
            var askOutcome = DialogueRules.Ask(trusted, dialogue, npcs, clues, "yaoshi", "herb-thinning", "Traded", when, out _);
            Check("asking someone who trusts you succeeds", askOutcome == AskOutcome.Success &&
                ClueLedger.Has(trusted, "herb-thinning"));
            Check("asking again for a known clue is idempotent",
                DialogueRules.Ask(trusted, dialogue, npcs, clues, "yaoshi", "herb-thinning", "Traded", when, out _) ==
                AskOutcome.Nothing &&
                ClueLedger.Of(trusted).Count(c => c.id == "herb-thinning") == 1);

            // 存档往返：Esc 中断写档，重开从同一节点续上。
            var escaped = NewSave();
            var opened = DialogueRules.Open(escaped, dialogue, "huolang");
            DialogueRules.Save(escaped, DialogueRules.Snapshot(escaped, opened, when));
            Check("dialogue progress saved", escaped.dialogue != null && escaped.dialogue.nodeId == "huolang-intro");
            var resumed = DialogueRules.Resume(escaped, dialogue, "huolang", out var resumedFrom);
            Check("resuming restores the same node", resumedFrom == "huolang-intro" && resumed.NodeId == "huolang-intro");
            var advanced = DialogueRules.Choose(escaped, dialogue, npcs, clues, opened, 0, when, out _);
            DialogueRules.Save(escaped, DialogueRules.Snapshot(escaped, advanced, when));
            Check("a mid-conversation node also roundtrips",
                escaped.dialogue.nodeId == "huolang-night" &&
                DialogueRules.Resume(escaped, dialogue, "huolang", out _).NodeId == "huolang-night");

            // 没有存档时按新对话开，而不是崩。
            var fresh = NewSave();
            fresh.dialogue = new DialogueState { npcId = "yaoshi", nodeId = "这个节点不存在" };
            var fallback = DialogueRules.Resume(fresh, dialogue, "yaoshi", out var resumedEmpty);
            Check("a stale node falls back to a fresh opening",
                resumedEmpty == "" && fallback.NodeId == "yaoshi-intro");

            // 半截状态（说话人/节点为空）读入后应被清掉，不卡住下次开口。
            var half = NewSave();
            half.dialogue = new DialogueState { npcId = "", nodeId = "" };
            SaveData.Migrate(half);
            Check("a half-written dialogue record is cleared on load", half.dialogue == null);
        }

        static SaveData NewSave() => new SaveData
        {
            slot = 1, characterId = "farmer", characterName = "甲",
            gender = "male", faceStyle = 0, destiny = "anle",
        };

        static void Check(string name, bool condition, string detail = null)
        {
            if (!condition) throw new Exception("FAIL: " + name + (detail == null ? "" : "（" + detail + "）"));
            Console.WriteLine("PASS: " + name + (detail == null ? "" : "（" + detail + "）"));
        }
    }
}
