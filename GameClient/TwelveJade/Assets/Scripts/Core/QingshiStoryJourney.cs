using System;

namespace TwelveJade.Core
{
    public sealed class QingshiStoryLead
    {
        public string SceneId { get; }
        public string NpcId { get; }
        public string NpcName { get; }
        public string Objective { get; }
        public string Bridge { get; }
        public string Reply { get; }
        public QuestOverheadState Marker { get; }

        internal QingshiStoryLead(string sceneId, string npcId, string npcName, string objective,
            string bridge, string reply, QuestOverheadState marker)
        {
            SceneId = sceneId; NpcId = npcId; NpcName = npcName;
            Objective = objective; Bridge = bridge; Reply = reply; Marker = marker;
        }
    }

    // A read-only view of the first missing story scene, independent of old chapter/quest progress.
    // It never creates a flag, settles a quest, grants a reward or normalizes the supplied save.
    public static class QingshiStoryJourney
    {
        static readonly QingshiStoryLead[] Leads =
        {
            new QingshiStoryLead("home", "npc-shitou", "石头", "去找石头，一同回家加冠。",
                "今日是你加冠的日子，婶子在家等着呢。走，我陪你回去。", "一道回家。", QuestOverheadState.Available),
            new QingshiStoryLead("livelihood", "npc-zhao-da", "赵大", "去铁匠铺找赵大，问问挣药钱的活计。",
                "家里要添药钱？铺子里正缺个帮手，先来搭把手。", "我想找些活做。", QuestOverheadState.Available),
            new QingshiStoryLead("tiger", "npc-shitou", "石头", "去找石头，商量后山的虎患。",
                "后山又闹虎患了。乡亲们正商量着上山，你可愿与我同行？", "我与你一道去。", QuestOverheadState.Available),
            new QingshiStoryLead("undercurrent", "npc-shitou", "石头", "围虎归来，去找石头续谈镇上的近况。",
                "这两日又有人丢了牲口，押运也在点名。你听见什么了？咱们再说说。", "说说这几日的事。", QuestOverheadState.Completable),
            new QingshiStoryLead("tomb", "", "", "押运队已动身。接着看山雨中的这一夜。",
                "故事转向另一处。以下是玩家所见，主角此时尚不知情。", "接着看。", QuestOverheadState.None),
            new QingshiStoryLead("calamity", "", "", "归途被截，镇上的钟声乱了。续上这一夜。",
                "这一段接着前事展开，是玩家所见；主角尚不知此处发生的事。", "继续观看。", QuestOverheadState.None),
            new QingshiStoryLead("departure", "", "", "天快亮了。你得赶回家去。",
                "接下来随主角的脚步继续。前段是玩家所见，主角仍不知其中原委。", "继续观看。", QuestOverheadState.None)
        };

        public static QingshiStoryLead Current(SaveData save)
        {
            int index = QingshiStory.CurrentIndex(save);
            if (index < 0 || index >= Leads.Length) return null;
            var lead = Leads[index];
            // A full/invalid flag ledger suppresses the actionable marker, not the visible objective.
            if (lead.Marker != QuestOverheadState.None && !QingshiStory.CanEnter(save, lead.SceneId))
                return new QingshiStoryLead(lead.SceneId, lead.NpcId, lead.NpcName, lead.Objective,
                    lead.Bridge, lead.Reply, QuestOverheadState.None);
            return lead;
        }

        public static bool IsContinuous(SaveData save)
        {
            int index = QingshiStory.CurrentIndex(save);
            return index >= 4 && index < Leads.Length;
        }

        public static QuestOverheadState Overhead(SaveData save, string npcId)
        {
            if (string.IsNullOrEmpty(npcId)) return QuestOverheadState.None;
            var lead = Current(save);
            return lead != null && string.Equals(lead.NpcId, npcId, StringComparison.Ordinal)
                ? lead.Marker : QuestOverheadState.None;
        }
    }
}
