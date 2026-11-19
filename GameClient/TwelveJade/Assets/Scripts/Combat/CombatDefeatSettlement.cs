using System;
using TwelveJade.Core;

namespace TwelveJade.Combat
{
    /// <summary>战败结算：算清楚"扣多少药资、醒来剩多少气血"，如实报告，不动手改存档。</summary>
    public sealed class DefeatSettlement
    {
        public int CoinsBefore;
        public int MedicalFee;
        public int CoinsAfter;
        public int MaxHp;
        public int AwakeHp;
        public string WakeAt = "青石镇药老药圃";
        public string ElderLine = "后生，山野险恶，莫要强撑。";
        public string FadeLine = "眼前一黑，不省人事……";
        public bool FeeWaived;
        public string Reason = "";
    }

    // 任务书 K5（M7）：战败保护。
    //
    // 死锁是这一节唯一不可协商的红线：气血归零、铜钱为 0、存档为 null、招式把人打死在
    // 硬直里——每一种都要走得到"回镇醒来"，绝不能抛未捕获异常把游戏关掉。
    //
    // 药资（10% 铜钱）走 Plan/Apply 两个口径：Plan 只算不扣（给面板与夹具当"该付多少"），
    // Apply 才是真落账。任务书 K5 当时只接到 Plan，是因为 CombatShot.cs 有一条
    // "失败不发奖励"的断言比对的是战败前后 TradeCoins 完全相等——那条夹具把两件事混在了一句里
    // （T049/T050 登记的死路径）。N2/D-08 已把它拆成两条独立断言（赏钱与掉落为 0 / 药资真扣并落盘），
    // 于是运行期 FrontEndCombat.SettleDefeat 从这里起改调 Apply。
    public static class CombatDefeatProtection
    {
        public const float AwakeHpRatio = .30f;
        public const float MedicalFeeRatio = .10f;
        // 醒来至少留一口气：气血上限哪怕只有 1 点，也不能算出 0。
        public const int MinAwakeHp = 1;

        public static int AwakeHpFor(int maxHp)
        {
            var clamped = Math.Max(1, maxHp);
            return Math.Max(MinAwakeHp, (int)Math.Round(clamped * AwakeHpRatio));
        }

        public static int MedicalFeeFor(int coins)
        {
            if (coins <= 0) return 0;
            return (int)Math.Floor(coins * MedicalFeeRatio);
        }

        // 只读结算：save 为 null 也要给出一份能显示的账单（战败面板不许空引用）。
        public static DefeatSettlement Plan(SaveData save, int maxHp)
        {
            var coins = save?.coins ?? 0;
            var fee = MedicalFeeFor(coins);
            return new DefeatSettlement
            {
                CoinsBefore = coins,
                MedicalFee = fee,
                CoinsAfter = Math.Max(0, coins - fee),
                MaxHp = Math.Max(1, maxHp),
                AwakeHp = AwakeHpFor(maxHp),
                FeeWaived = fee == 0,
                Reason = fee == 0 ? "身无分文，药老摆摆手没收钱。" : "",
            };
        }

        // 真正落账的那一条：够付就付，不够付一分不动，铜钱永远不为负。
        // 运行期由 FrontEndCombat.SettleDefeat 调用（N2/D-08 接通），不再是只有夹具在踩的死路径。
        public static DefeatSettlement Apply(SaveData save, int maxHp)
        {
            var plan = Plan(save, maxHp);
            if (save == null) return plan;
            if (plan.MedicalFee > 0 && save.coins >= plan.MedicalFee)
                save.coins = Math.Max(0, save.coins - plan.MedicalFee);
            else
            {
                plan.MedicalFee = 0;
                plan.CoinsAfter = save.coins;
                plan.FeeWaived = true;
                plan.Reason = "铜钱不够这一副药，药老说下回来也一样。";
            }
            return plan;
        }
    }
}
