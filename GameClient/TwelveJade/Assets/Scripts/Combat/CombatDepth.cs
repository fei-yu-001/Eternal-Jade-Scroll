using System;
using System.Collections.Generic;
using System.Linq;
using TwelveJade.Core;

namespace TwelveJade.Combat
{
    // 任务书 K3（M2/M3）：把"部位锁敌 + 看破 + 2.0 档案"这些散落在 Core 里的机制，
    // 收成一个表现层只调一个入口的形状。Core 管算术，这里管"这一刀该砍哪儿"。
    public static class CombatDepth
    {
        // 2.0 前台档案（唯一一处 2.0 数值真源）：
        //   精力回复 25/s（任务书 K3）
        //   闪避纯无敌 .25 秒（任务书 K3；Core 基线是 .22，被 CombatChecks.cs 的出窗时刻钉死，
        //     所以做成每单位档案，默认单位仍是 .22）
        //   看破判定窗 .15 秒
        // 精力档位刻意不在这里：8/20/12 同时被 CombatChecks.cs 与 CombatShot.cs 精确断言，
        // 做成可调就是第二真源。冲突口径登记在 11_技术债务记录.md。
        public static CombatTuning Standard20 { get; } = new CombatTuning(25f, .25f, .15f);

        // 平攻与锁敌共用的入口：targetPartId 为空 = 打默认部位（躯干/正面）。
        // critBonus 来自命途与配饰（CombatDestinyInsight / CombatGear），0 = 不加。
        public static PartStrike StrikeFor(EnemyProfile profile, string targetPartId,
            float critBonus = 0f, bool counterReady = false)
        {
            var part = profile == null ? null :
                (string.IsNullOrWhiteSpace(targetPartId) ? profile.DefaultPart : profile.FindPart(targetPartId));
            // 表里没有这个部位（比如中途换了妖、或部位名打错）不能装成"倍率 1 的普通一击"，
            // 那会让"锁敌没生效"看起来像"锁敌生效但没伤害"——如实退回默认部位。
            if (part == null && profile != null) part = profile.DefaultPart;

            // 看破反击落点：守方倍率最高的弱点（Core 负责无视防御并判为暴击）。
            if (counterReady && profile != null)
                part = profile.BestWeakPoint ?? part;

            if (part == null) return new PartStrike { PartId = "", Multiplier = 1f, CritBonus = critBonus };
            return new PartStrike
            {
                PartId = part.Id,
                Multiplier = part.Multiplier,
                DodgeChance = part.DodgeChance,
                WeakPoint = part.WeakPoint,
                Bulwark = part.Bulwark,
                CritBonus = critBonus,
            };
        }

        // 表现层的锁敌提示文案："弱点：后背旧伤（×1.5，抵抗 20%）"。
        // 分隔符为什么不用中点"·"（N4-Polish 3③，实测）：这一行走手写体 ZhiMangXing，
        // 它的 cmap 里有 U+00D7「×」但**没有 U+00B7「·」**（同目录的 NotoSerifCJKsc 两个都有），
        // 缺字被 TMP 降级成一个贴地的点，画面上就成了 "(1x.抵抗 5%)" 这种读起来像病句的东西。
        // 中文 UI 里逗号本来就该用全角"，"，所以顺带把半角括号也换成全角。
        public static string PartLabel(EnemyProfile profile, string targetPartId)
        {
            var part = profile == null ? null :
                (string.IsNullOrWhiteSpace(targetPartId) ? profile.DefaultPart : profile.FindPart(targetPartId))
                ?? profile.DefaultPart;
            if (part == null) return "躯干（×1）";
            var bits = new List<string>();
            if (part.Multiplier > 1.0001f) bits.Add("×" + part.Multiplier.ToString("0.#"));
            if (part.DodgeChance > 0f) bits.Add("抵抗 " + (int)Math.Round(part.DodgeChance * 100) + "%");
            if (part.Bulwark) bits.Add("霸体");
            // 平攻部位本来就没有倍率差，别印"×1"当噪音；三样都没有才写"无特殊"。
            if (bits.Count == 0) bits.Add("无特殊");
            return (part.WeakPoint ? "弱点：" : "平攻：") + part.Name + "（" + string.Join("，", bits) + "）";
        }

        // 换锁：在部位表里循环（Tab 键语义）。空锁 = 当前打的是默认部位，
        // 所以第一次 Tab 应当从默认部位的"下一格"起步——第一下就换到弱点，而不是先换到它自己。
        public static string NextLockedPart(EnemyProfile profile, string currentId)
        {
            if (profile == null || profile.Parts.Count == 0) return "";
            var ids = profile.Parts.Select(p => p.Id).ToList();
            var index = ids.IndexOf(currentId ?? "");
            if (index < 0) index = ids.IndexOf(profile.DefaultPart.Id);
            return ids[(index + 1) % ids.Count];
        }

        // 只打弱点的那条路（"弱点锁敌"一键跳到最高倍率弱点）。
        public static string WeakPointLock(EnemyProfile profile) => profile?.BestWeakPoint?.Id ?? "";
    }
}
