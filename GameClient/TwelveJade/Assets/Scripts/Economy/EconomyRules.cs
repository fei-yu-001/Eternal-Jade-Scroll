using System;
using TwelveJade.Core;

namespace TwelveJade.Economy
{
    // 市井经济里的**非价格**部分：黑市倍率、命途门控、铜钱钳制。
    //
    // 上一轮这个文件里还有一套按 ADR-029 写的线性买卖价曲线（BuyDiscountRate / SellRate）。
    // 架构师裁决 D-05 把它判为死代码并删除：成交唯一真源是 Core 的 Reputation 阶梯
    // （TierStep=20、每档 3%、满声望卖价 57.5%），界面与商人今天都走它。
    // 留两套曲线在册子的后果我上一轮已经实测过——铁剑 380 文满声望，
    // Core 算出 324、曲线算出 323，卖价更是 218 对 247：玩家看到的价就成了骗人的价。
    // 详见 ADR-032 与 T045。要恢复平滑曲线属于"重新定价"，必须走架构师批 + 改 TradeChecks 期望。
    public static class EconomyRules
    {
        /// <summary>黑市全线溢价 140%（地点属性，不是折扣）。与 Core 的 Trade.BlackMarketMultiplier 同值。</summary>
        public const float BlackMarketRate = 1.40f;

        public const string FateGatedRefusal = "此物煞气重，你降不住它。";

        /// <summary>命途门控：煞气重的货不对命途就不卖。判据与黑市可见性共用同一份白名单，
        /// 不在这里另抄一份"哪些命格算狠人"。
        /// 判定前必须先装表（T098）：没装表时 fateGated 一律读成 false，
        /// 于是符箓会像寻常货物一样对任何人放行——那种"静默降级"比报错贵得多。</summary>
        public static bool IsFateGated(string itemId)
        {
            if (!ItemFlags.IsLoaded)
                throw new InvalidOperationException(
                    "命途门控在装表之前被调用：items.json 的 fateGated 还没读进来，" +
                    "此时任何煞气货都会被当成寻常货放行。请先 ItemFlags.Load(items.json)（T098）。");
            return ItemFlags.Current.IsFateGated(itemId);
        }

        public static bool CanBuy(string itemId, SaveData save)
        {
            if (!IsFateGated(itemId)) return true;
            return save != null && Trade.SeesBlackMarket(save);
        }

        /// <summary>拒卖时给货郎一句能念出来的话；不该拒时返回空串。</summary>
        public static string RefusalFor(string itemId, SaveData save) =>
            CanBuy(itemId, save) ? "" : FateGatedRefusal;

        /// <summary>铜钱溢出保护：加钱封顶，绝不让 coins 翻过上限变负数。</summary>
        public static int AddCoins(int coins, int gain) =>
            Math.Min(Math.Max(coins, 0) + Math.Max(gain, 0), SaveData.MaxCoins);

        public static int SpendCoins(int coins, int cost) =>
            Math.Max(0, Math.Max(coins, 0) - Math.Max(cost, 0));
    }
}
