using System;
using System.Collections.Generic;
using System.Linq;

namespace TwelveJade.Combat
{
    // 任务书 ADR-028 §3（M4）：19 条命途的战斗映射。
    //
    // 为什么映射里没有"加几点攻/几点防"：SaveModels.cs 的 CharacterGen 写得很明白——
    // "随机词条与命运只描述出身与性情，不给数值加成"，而且建档时命格是随机掷的，
    // FrontEndShotProps.cs 的"野猪 60→44"这类逐字伤害断言吃的就是裸装裸命那一档。
    // 所以命途只映射到"只在新机制里才碰得到"的四条维度上：
    //   暴击倍率（只有锁弱点/看破才 crit）、体力回复、流血伤害、看破判定窗与耐久损耗减免。
    // 存档里不写任何数值，映射在开战那一刻算出来——契约不被打破，命途也确实有手感。
    public sealed class CombatDestinyModifiers
    {
        public static readonly CombatDestinyModifiers None = new CombatDestinyModifiers();

        public float CritBonus { get; set; }
        public int RegenBonus { get; set; }
        public int BleedDamage { get; set; }
        public float RiposteBonusSeconds { get; set; }
        public int WearReduction { get; set; }
        public int AttackBonus { get; set; }
        public int DefenseBonus { get; set; }
        public string Note { get; set; } = "";

        public bool IsEmpty => CritBonus == 0f && RegenBonus == 0 && BleedDamage == 0 &&
            RiposteBonusSeconds == 0f && WearReduction == 0 && AttackBonus == 0 && DefenseBonus == 0;
    }

    public sealed class CombatDestinyRow
    {
        public CombatDestinyRow(string id, string name, CombatDestinyModifiers modifiers)
        {
            Id = id; Name = name; Modifiers = modifiers;
        }

        public string Id { get; }
        public string Name { get; }
        public CombatDestinyModifiers Modifiers { get; }
    }

    public sealed class CombatDestinyTable
    {
        readonly Dictionary<string, CombatDestinyRow> rows;
        readonly List<string> unresolved = new();

        CombatDestinyTable(List<CombatDestinyRow> rows)
        {
            this.rows = rows.ToDictionary(r => r.Id, r => r);
        }

        public int Count => rows.Count;
        public IReadOnlyCollection<string> Ids => rows.Keys.ToList();

        public CombatDestinyRow Find(string id) =>
            string.IsNullOrEmpty(id) ? null : (rows.TryGetValue(id, out var row) ? row : null);

        // 认不出来的命格不抛异常：给零加成 + 如实记一笔。存档里的 destiny 有可能来自
        // 更早的版本或还没登记进战斗表的新命格，战斗不该因此开不了场。
        public CombatDestinyModifiers For(string id)
        {
            var row = Find(id);
            if (row == null && !string.IsNullOrWhiteSpace(id) && !unresolved.Contains(id))
                unresolved.Add(id);
            return row?.Modifiers ?? CombatDestinyModifiers.None;
        }

        public IReadOnlyList<string> UnresolvedIds => unresolved;

        static CombatDestinyRow R(string id, string name, float crit = 0, int regen = 0, int bleed = 0,
            float riposte = 0, int wear = 0, string note = "") =>
            new CombatDestinyRow(id, name, new CombatDestinyModifiers
            {
                CritBonus = crit, RegenBonus = regen, BleedDamage = bleed,
                RiposteBonusSeconds = riposte, WearReduction = wear, Note = note,
            });

        // 与 SaveModels.cs 的 CharacterGen.Destinies 逐条对齐（19 条，顺序一致）。
        public static CombatDestinyTable Standard { get; } = new CombatDestinyTable(new List<CombatDestinyRow>
        {
            R("jadeloft", "玉楼引", riposte: .04f, note: "梦里那座楼教眼睛快半分"),
            R("fallingstar", "灾星过命", bleed: 3, note: "流星坠野那夜落下的病气，咬住就不松"),
            R("benefactor", "贵人候", wear: 1, note: "总有人在关键时刻替你挡一下"),
            R("hardfate", "命硬", wear: 2, note: "兵灾克不动，衣裳也耐穿"),
            R("pledge", "守信约", crit: .03f, note: "许过的话不会落空，出手也不落空"),
            R("lonely", "孤命", regen: 3, note: "一个人走惯了，喘口气就站得住"),
            R("debtor", "旧债", note: "债还没到还的时候——纯剧情钩子"),
            R("scholar", "文曲偏照", riposte: .06f, note: "看过别人的套路，一眼就认得"),
            R("away", "离乡命", regen: 4, note: "路远的人最会省力气"),
            R("unreadable", "无名", crit: .05f, note: "算不出来的命数，出手也料不定"),
            R("xia", "侠骨", crit: .05f, bleed: 2, note: "路见不平那一刀见血"),
            R("shaxing", "杀星入命", crit: .15f, note: "乱世出煞星，破绽处就是死处"),
            R("chuandeng", "传灯命", regen: 5, note: "替旁人掌灯的人，自己经得起熬"),
            R("anle", "安乐命", regen: 8, note: "灶头有肉缸里有米，底子比谁都稳"),
            R("caixing", "财星照命", wear: 1, note: "钱要花在刃口上，家伙事儿省着磨"),
            R("renxin", "仁心命", riposte: .03f, wear: 1, note: "见病痛就挪不动步，也最会避痛"),
            R("chiqing", "痴情种", crit: .06f, note: "心里揣着一个人，出手就格外定"),
            R("longshe", "龙蛇命", bleed: 2, riposte: .03f, note: "是龙是蛇看时势，也看这一口咬得准不准"),
            R("xianyun", "闲云命", regen: 4, note: "一亩药田半卷闲书，气匀得很"),
        });
    }
}
