using System;
using System.Collections.Generic;

namespace TwelveJade.Core
{
    public sealed class QingshiStoryScene
    {
        public string Id { get; }
        public string Title { get; }
        public int Act { get; }
        public string Summary { get; }

        internal QingshiStoryScene(string id, string title, int act, string summary)
        {
            Id = id;
            Title = title;
            Act = act;
            Summary = summary;
        }
    }

    // V2.5 漫画进度独立于旧 ChapterSegment；不结算物品、修行或旧任务奖励。
    public static class QingshiStory
    {
        public const string FlagPrefix = "qingshi-v25-";
        // 与 SaveData.MarkFlag / SaveRepository 的现有容量一致，不新增存档字段。
        const int FlagLimit = 32;

        public static IReadOnlyList<QingshiStoryScene> Scenes { get; } = Array.AsReadOnly(new[]
        {
            new QingshiStoryScene("home", "十八加冠", 1,
                "十八岁加冠日，阿娘用热鸡蛋滚额驱邪，把旧棉絮缝进冬衣；瘫卧的阿爹削着柴刀木柄，一家在灯下推让红薯心。"),
            new QingshiStoryScene("livelihood", "市井深处", 1,
                "为赎银药钱在铁匠铺、药铺和猎户处谋生，与石头分担辛劳，见证陆无疾与阿婉相惜。破庙老翁的机锋只让玩家看见灵根显影，主角仍是凡人。"),
            new QingshiStoryScene("tiger", "山中虎患", 2,
                "主角与石头、老猎户合力挖坑设伏，以竹签、火把和长矛围剿猛虎。虎皮被充作贡品，还要加征押解费；带伤归家，父母心疼照料。"),
            new QingshiStoryScene("undercurrent", "暗流汹涌", 2,
                "水井泛起黑沫，牲畜遭窃、流民失踪。主角查探镇中异状，暗处的眼线彼此盯防，小人物被卷入将要爆发的争斗。"),
            new QingshiStoryScene("tomb", "绝情引信", 3,
                "陆无疾与阿婉被各自势力逼至古冢石门前，相拥殉情。两滴情血相融，叩开封印，红尘仙的终极叹息引出四方混战。"),
            new QingshiStoryScene("calamity", "善念成殇", 3,
                "石头携破木匣奔回救人，却重伤遭黑衣人夺舍，身躯被操控弑亲。残存意识挣扎着把匣子甩入屋角泥水。此为玩家所见，主角此时并不知情。"),
            new QingshiStoryScene("departure", "血誓离镇", 3,
                "主角重伤归来，面对亲人罹难与街坊关于石头成魔的证言，拾起泥水中的破木匣，怀着误解与血仇离镇，尚未踏入修行之门。")
        });

        // 首个未完成段；全部完成时为 Scenes.Count，空存档为 -1。
        public static int CurrentIndex(SaveData save)
        {
            if (save == null) return -1;
            for (int i = 0; i < Scenes.Count; i++)
                if (!save.HasFlag(FlagPrefix + Scenes[i].Id)) return i;
            return Scenes.Count;
        }

        public static bool IsCompleted(SaveData save, string id) =>
            save != null && IndexOf(id) >= 0 && save.HasFlag(FlagPrefix + id);

        public static bool CanEnter(SaveData save, string id) =>
            CanComplete(save, IndexOf(id), out _);

        // 回看只开放已经连续完成的段落，不因孤立的后段标记跳过前文。
        public static bool CanReplay(SaveData save, string id)
        {
            int index = IndexOf(id);
            return save != null && index >= 0 && index < CurrentIndex(save);
        }

        // persist 应同步保存传入对象（如 SaveRepository.Write），失败则抛异常。
        // true 只表示一次新进度已保存；回放/重复调用不会再次调用 persist。
        public static bool Complete(SaveData save, string id, Action<SaveData> persist, out string reason)
        {
            int index = IndexOf(id);
            if (!CanComplete(save, index, out reason)) return false;
            if (persist == null)
            {
                reason = "无法保存剧情进度。";
                return false;
            }

            var flags = save.oneTimeFlags;
            // Write 在真正落盘前会补空、升级版本并改时间戳；失败时一并恢复。
            var chapter = save.chapter;
            var clues = save.clues;
            var merchants = save.merchants;
            var bag = save.bag;
            var karma = save.karmaEvents;
            var gear = save.gear;
            var crafts = save.crafts;
            int schemaVersion = save.schemaVersion;
            string updatedUtc = save.updatedUtc;
            try
            {
                save.MarkFlag(FlagPrefix + id);
                if (index == Scenes.Count - 1) save.MarkFlag(PrologueRules.DepartureFlag);
                persist(save);
                reason = "";
                return true;
            }
            catch (Exception)
            {
                save.oneTimeFlags = flags;
                save.chapter = chapter;
                save.clues = clues;
                save.merchants = merchants;
                save.bag = bag;
                save.karmaEvents = karma;
                save.gear = gear;
                save.crafts = crafts;
                save.schemaVersion = schemaVersion;
                save.updatedUtc = updatedUtc;
                reason = "剧情进度保存失败，请重试。";
                return false;
            }
        }

        static bool CanComplete(SaveData save, int index, out string reason)
        {
            reason = "";
            if (save == null) reason = "尚未选择角色。";
            else if (index < 0) reason = "没有这段剧情。";
            else if (save.HasFlag(FlagPrefix + Scenes[index].Id)) reason = "此段剧情已经完成，可回看。";
            else if (index != CurrentIndex(save)) reason = "请先完成前一段剧情。";
            if (reason.Length > 0) return false;

            var flags = save.oneTimeFlags;
            int needed = 1 + (index == Scenes.Count - 1 && !save.HasFlag(PrologueRules.DepartureFlag) ? 1 : 0);
            if ((flags?.Length ?? 0) > FlagLimit - needed)
            {
                reason = "存档标记空间不足，无法记录剧情进度。";
                return false;
            }
            // 不清理、不截断旧标记；坏数据交由调用方处理，不能借剧情结算洗白。
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string flag in flags ?? Array.Empty<string>())
                if (string.IsNullOrWhiteSpace(flag) || flag.Length > 48 || !seen.Add(flag))
                {
                    reason = "存档标记无效，无法记录剧情进度。";
                    return false;
                }
            return true;
        }

        static int IndexOf(string id)
        {
            for (int i = 0; i < Scenes.Count; i++)
                if (string.Equals(Scenes[i].Id, id, StringComparison.Ordinal)) return i;
            return -1;
        }
    }
}
