using System;
using System.Collections.Generic;
using TwelveJade.Core;

namespace TwelveJade.Economy
{
    // 「一件事被世界看见」→「记哪一条」的映射（O3 / D-11 / T043）。
    //
    // 为什么只有这一处：判定点散在 Presentation（对白走到哪个节点、买卖是否成交）与
    // Core（任务结算），而善事清单在 karma.json。中间这层映射只准有一个，
    // 否则又变成"五处三样口径"（T095 的教训）。
    //
    // 保守原则：**宁可不记，也不虚记**。这一层每加一条，都必须能在 0.3 的判定口径里
    // 指出"玩家的哪个动作算做了这件事"；答不出来的，一律留在 o-karma-ledger.md 里标"本轮不接"。
    // 记错了比不记更糟——它会让玩家以为善行已经落账，而账上根本没有。
    public static class KarmaTriggers
    {
        /// <summary>对白节点 → 善事/奇遇。只有"走到这个节点"本身就等于那件事已被看见时才成立。</summary>
        static readonly Dictionary<string, string> FromDialogueNode = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // 沈怀安把压着的那段话说给你听，你听完就走、没往外传 —— 0.3 担责类"替他瞒着"。
            // 口径注意：0.3 要的是"一直瞒到事情过去"，本轮记的是"接下了这个秘密"这一步，
            // 完整的瞒到过去要等后续事件闭环（见 o-karma-ledger.md 与 T084）。
            ["shen-secret"] = "keep-a-secret",
            // 药老来道谢，说明后山那个摔伤的采药人和药都被背回来了 —— 0.3 难做的"把药背回来"。
            // 这条现在是**兜底证据**：正式判定点已改挂任务结算（见下面的 FromQuest，T083 已闭上）。
            // 两条都在，是因为玩家可能先被道谢、任务却还差一环没结；Record 按 eventId 幂等，
            // 谁先到算谁，不会记成两件事。
            ["yaolao-thanks"] = "carry-back-herbs",
        };

        /// <summary>成交动作 → 善事/奇遇。本轮**留空**：
        /// 0.3 里没有一件善事是"按市价付了钱"就算的（垫付、赠药、不要赏钱都要有额外动作），
        /// 而现在的世界里既没有代付也没有赠药机制。机制已经接好，等一条真的判定点。</summary>
        static readonly Dictionary<string, string> FromTrade = new Dictionary<string, string>(StringComparer.Ordinal)
        {
        };

        /// <summary>任务结算 → 善事/奇遇。T083 闭上之后才有这一张表：
        /// 以前 `QuestLedger` 运行期没人调，判定点只能借"药老来道谢"那一幕的对白节点当证据；
        /// 现在任务真会自己结算了，正式判定点改挂任务侧（去向表 O3 那行早就写了"等 T083"）。
        ///
        /// 只此一条是**有依据的**：`quest-herbal-rescue` 的目标是"打退山魈/野猪 + 把药交给药老"，
        /// 而 0.3 的 `carry-back-herbs` 写的是"把摔伤的人和药一起背回来（人没背回来、
        /// 药背回来了也算）"——任务结算是这件事在世界里唯一被证明的时刻。
        /// 其余十二条挂在任务上的善事（去向表逐条标了"等任务内容"）**不接**：
        /// 现在表里还没有对应任务，硬映射等于凭空造账。</summary>
        static readonly Dictionary<string, string> FromQuest = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["quest-herbal-rescue"] = "carry-back-herbs",
        };

        public static string ForDialogueNode(string nodeId) =>
            string.IsNullOrEmpty(nodeId) || !FromDialogueNode.TryGetValue(nodeId, out var id) ? null : id;

        public static string ForTrade(string kind) =>
            string.IsNullOrEmpty(kind) || !FromTrade.TryGetValue(kind, out var id) ? null : id;

        public static string ForQuestCompletion(string questId) =>
            string.IsNullOrEmpty(questId) || !FromQuest.TryGetValue(questId, out var id) ? null : id;

        /// <summary>映射表里出现的每个事件 id 都必须在 karma.json 里；这条由套件逐字对表。
        /// 拼错一个 id 的后果是 Record 静默拒收（表外 id 一律不计），比报错难查得多。</summary>
        public static IEnumerable<string> MappedEventIds
        {
            get
            {
                foreach (var pair in FromDialogueNode) yield return pair.Value;
                foreach (var pair in FromTrade) yield return pair.Value;
                foreach (var pair in FromQuest) yield return pair.Value;
            }
        }

        public static IEnumerable<KeyValuePair<string, string>> DialogueNodeTriggers => FromDialogueNode;
        public static IEnumerable<KeyValuePair<string, string>> TradeTriggers => FromTrade;
        public static IEnumerable<KeyValuePair<string, string>> QuestTriggers => FromQuest;
    }

    /// <summary>任务结算的落账口（T083 的运行期入口）。
    ///
    /// 为什么单独一层：`QuestRuntime` 在 Core，不能引用 Economy 的气运簿；而"结算之后
    /// 紧跟一次记账、记账之后紧跟一次落盘"这三步的顺序是**判据**（T097 的落盘责任），
    /// 不该由每个 UI 调用点各抄一遍。这里把三步钉死，UI 只负责调一次并写盘。
    ///
    /// 运行期谁调它：对白分支（FrontEndTrade.OnTalkBranch）与行囊动作
    /// （FrontEndInventory 的制作/使用之后）——两处都在自己的 `repository.Write` 之前调。</summary>
    public static class QuestSettlement
    {
        /// <summary>对账 → 结算 → 发奖 → 记气运。**Record 只改内存**，
        /// 所以本方法的每个调用点必须紧跟一次 repository.Write（T097）。</summary>
        public static QuestSyncResult Drive(SaveData save, QuestTable quests, ItemTable items,
            KarmaTable karma, QuestSignal signal)
        {
            return RecordCompletions(save,karma,QuestRuntime.Reconcile(save,quests,items,signal));
        }

        public static QuestSyncResult TurnIn(SaveData save,QuestTable quests,ItemTable items,KarmaTable karma,
            string questId,string npcId,float distance)
        {
            return RecordCompletions(save,karma,QuestRuntime.TryTurnIn(save,quests,items,questId,npcId,distance));
        }

        static QuestSyncResult RecordCompletions(SaveData save,KarmaTable karma,QuestSyncResult report)
        {
            if (save == null || karma == null) return report;
            foreach (var questId in report.Completed)
            {
                var eventId = KarmaTriggers.ForQuestCompletion(questId);
                if (string.IsNullOrEmpty(eventId)) continue;
                if (KarmaTracker.Record(save, karma, eventId, save.WorldTimeNow()))
                    report.KarmaRecorded.Add(eventId);
            }
            return report;
        }
    }
}
