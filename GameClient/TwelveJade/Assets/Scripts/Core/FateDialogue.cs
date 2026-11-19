using System;
using System.Linq;

namespace TwelveJade.Core
{
    // 序章·星夜问命：投胎之前，守渡老者代天六问。
    // 答案只给命运加权倾向，最终结果仍由随机决定——问命是算命，不是捏人。
    // 脚本出处：02_游戏设计文档GDD.md 六、出生系统（倾向矩阵保证每条命格至少被一个答案加权）。
    public static class FateDialogue
    {
        public const string Opening =
            "魂魄无前尘，心性却有痕。\n老朽代天问你几句话——答完，便送你入世。";

        public sealed class Option
        {
            public Option(string text, string reply, params string[] favor)
            { Text = text; Reply = reply; Favor = favor; }
            public string Text { get; }
            public string Reply { get; }
            public string[] Favor { get; }
        }

        public sealed class Question
        {
            public Question(string title, string text, params Option[] options)
            { Title = title; Text = text; Options = options; }
            public string Title { get; }
            public string Text { get; }
            public Option[] Options { get; }
        }

        public static readonly Question[] Questions =
        {
            new("问一 · 梦象", "魂入轮回前，心头浮出的最后一幅景象是——",
                new Option("高楼玉宇，仙乐自云端飘下",
                    "老者抬眼多看了一眼：“玉宇……这一缕魂，有点意思。”", "jadeloft", "benefactor"),
                new Option("大水漫过田庄，四处都是哭喊",
                    "老者皱眉：“生逢乱世，这是入世的谶。”", "fallingstar", "debtor"),
                new Option("白茫茫什么也没有",
                    "老者轻笑：“干净。干净得连卜算都照不出你。”", "unreadable", "lonely")),
            new("问二 · 路见不平", "人间路窄。若见强梁夺人食、饿殍卧道旁，你会——",
                new Option("拔刀就上，管这一趟闲事",
                    "老者抚掌：“好一副侠骨。只盼这刀，别太快折。”", "xia", "renxin"),
                new Option("低头快走，人各有命",
                    "老者微叹：“乱世里活得久的，多半是低头的。”", "anle", "lonely"),
                new Option("记下强梁相貌，回头报官请赏",
                    "老者眯眼：“借势而行，是个聪明人。”", "longshe", "scholar"),
                new Option("趁乱从死者身上摸走碎银",
                    "老者沉默半晌，长长叹息：“乱世磨人，也磨刀啊。”", "shaxing", "caixing")),
            new("问三 · 渡口念想", "轮回渡口，只许你带一样念想入世。你带——",
                new Option("半块旧玉，不知是谁传给谁的",
                    "老者点头：“信物入世，必有归处。”", "pledge", "chiqing"),
                new Option("一把用惯的柴刀，走到哪里都饿不死",
                    "老者笑：“实用。”", "hardfate", "away"),
                new Option("一本破书，闲时翻上两页",
                    "老者整了整衣冠：“书，是要传的。”", "scholar", "chuandeng")),
            new("问四 · 信命", "临行前，老朽多问一句——你信命么？",
                new Option("信。命里有时，终须有。",
                    "老者捋须：“认命的人，命反而待他宽厚。”", "benefactor", "anle"),
                new Option("不信。我命由我不由天。",
                    "老者大笑：“好！这话，多少人临死才咽回去。”", "hardfate", "shaxing"),
                new Option("不知道。走着瞧吧。",
                    "老者拊掌：“着。命，就是走着瞧出来的。”", "unreadable", "xianyun")),
            new("问五 · 若得势", "若有一日，你得了泼天的富贵权势——",
                new Option("买田置宅，关起门过安稳日子",
                    "老者点头：“守成也是本事，乱世里尤其。”", "anle", "caixing"),
                new Option("开仓放粮，护一乡周全",
                    "老者含笑：“积德之家，必有余庆。”", "renxin", "benefactor"),
                new Option("设帐授徒，教书育人",
                    "老者起身一揖：“为往圣继绝学，善。”", "chuandeng", "scholar"),
                new Option("豢养爪牙，要四方都怕我",
                    "老者摇头叹息：“这条路走到头，连个收尸的人都没有。”", "longshe", "shaxing")),
            new("问六 · 长生", "最后一问。长生摆在面前——你取不取？",
                new Option("取。听说仙家有十二座玉楼，我想亲眼看看",
                    "老者目光一凝：“十二楼……呵，有缘。”", "jadeloft", "away"),
                new Option("不取。生死有命，把眼前的日子过好就行",
                    "老者拊掌：“多少人求不来的，是你不要的。”", "xianyun", "anle"),
                new Option("若长生要负了身边人，宁可不要",
                    "老者动容：“情之一字，最难，也最难得。”", "chiqing", "renxin"),
                new Option("踏着尸骨也要取",
                    "老者久久无言，末了长叹：“长生误人……罢了，罢了。”", "shaxing", "fallingstar"))
        };

        // 加权随机：每个命运底数为 1，答案每倾向一次再加 3（可叠加）。
        // 答案影响概率，不决定结果——同一路径连答，对应命格概率约三至四成。
        public static string RollDestiny(string[] favored, Random rng)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            var weights = CharacterGen.Destinies.Select(d =>
                1 + 3 * (favored == null ? 0 : favored.Count(id => id == d.Id))).ToArray();
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
