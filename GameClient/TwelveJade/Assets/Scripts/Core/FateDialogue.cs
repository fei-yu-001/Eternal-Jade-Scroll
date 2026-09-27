using System;
using System.Linq;

namespace TwelveJade.Core
{
    // 开局问命：说书人三相，答案只给命运加权倾向，最终结果仍由随机决定。
    public static class FateDialogue
    {
        public sealed class Option
        {
            public Option(string text, params string[] favor)
            { Text = text; Favor = favor; }
            public string Text { get; }
            public string[] Favor { get; }
        }

        public sealed class Question
        {
            public Question(string text, params Option[] options)
            { Text = text; Options = options; }
            public string Text { get; }
            public Option[] Options { get; }
        }

        public static readonly Question[] Questions =
        {
            new("老朽观你眉宇间有山川之气。敢问夜里入梦，常梦见什么？",
                new Option("高楼玉宇，有仙乐自云端飘下", "jadeloft", "benefactor"),
                new Option("大水漫过田庄，四处都是哭喊", "fallingstar", "debtor"),
                new Option("黑漆漆什么也没有，一觉到天亮", "unreadable", "lonely")),
            new Question("再问一句。若明日就要离乡背井，身上只许带一样东西，你带什么？",
                new Option("半块旧玉，是家里传下来的", "pledge", "jadeloft"),
                new Option("一把用惯的柴刀，走到哪里都饿不死", "hardfate", "away"),
                new Option("一本破书，闲时翻上两页", "scholar", "unreadable")),
            new Question("最后一问——你信命么？",
                new Option("信。命里有时，终须有。", "benefactor", "debtor"),
                new Option("不信。我命由我不由天。", "hardfate", "away"),
                new Option("不知道。走着瞧吧。", "unreadable", "lonely"))
        };

        // 加权随机：每个命运底数为 1，被答案倾向的命运再加 3。答案影响概率，不决定结果。
        public static string RollDestiny(string[] favored, Random rng)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            var weights = CharacterGen.Destinies.Select(d =>
                1 + (favored != null && favored.Contains(d.Id) ? 3 : 0)).ToArray();
            var pick = rng.Next(weights.Sum());
            for (var i = 0; i < weights.Length; i++)
            {
                pick -= weights[i];
                if (pick < 0) return CharacterGen.Destinies[i].Id;
            }
            return CharacterGen.Destinies[CharacterGen.Destinies.Count - 1].Id;
        }
    }
}
