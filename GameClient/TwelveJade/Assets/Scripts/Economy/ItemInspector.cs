using System;
using TwelveJade.Core;

namespace TwelveJade.Economy
{
    // 行囊浮签（ItemTooltip）的文字层。
    //
    // 为什么不写在界面里：浮签上的每个数字都对玩家许了诺。早前它按 ADR-029 的平滑曲线报
    // "转手 247 文"，而货郎那边走的是 Core 的阶梯收价 218 文——界面报一个价、成交另一个价。
    // 把文案挪进纯 C# 层，这条不一致就能被不依赖 Unity 的断言当场抓住，而不是等玩家发现。
    public static class ItemInspector
    {
        public const string UnknownName = "未登记之物";
        public const string RelicNotice = "【关键信物 · 不可丢弃】";
        public const string DurabilityPending = "耐久：—（战斗损耗由战斗线维护，尚未接入）";
        public const string FateResonance = "命途共鸣：煞气重，命格压不住它的人降不住。";

        /// <summary>报价与成交同源：一律走 Core 的 Trade——那是货郎真正收付的那一套。
        /// D-05 裁决后 ADR-029 的平滑曲线已整段删除，此处只剩一个真源。</summary>
        public static int BuyPrice(ItemDef def, int reputation) => Trade.BuyPrice(def, reputation);

        public static int SellPrice(ItemDef def, int reputation) => Trade.SellPrice(def, reputation);

        public static bool IsGear(ItemDef def) =>
            def != null && (def.Category == ItemCategoryNames.Weapon || def.Category == ItemCategoryNames.Gear);

        /// <summary>食效一行（O6/D-09 + T070）：回血只在战斗内结算，行囊里吃下去不会有任何事发生。
        /// 丹药写了 combatHeal 就把那个数报出来（浮签上的数字与战斗页结算的数字必须同源），
        /// 没写的照旧是"—"——不替物品表编一个数。</summary>
        public static string EffectLine(ItemDef def)
        {
            if (def == null) return "";
            if (def.Category == ItemCategoryNames.Medicine && def.CombatHeal > 0)
                return "\n食效：战斗内回复 " + def.CombatHeal + " 点气血（行囊里服用不生效，也不会消耗）";
            if (def.Category != ItemCategoryNames.Food && def.Category != ItemCategoryNames.Medicine) return "";
            return "\n" + EffectPending;
        }

        public const string EffectPending =
            "食效：—（气血与精力只在山野对敌时结算，行囊里服用不生效，也不会消耗）";

        /// <summary>零价信物不报价：报"买入 1 文"会让人以为买得到。</summary>
        public static string PriceLine(ItemDef def, int reputation)
        {
            if (def == null || def.Price <= 0) return "";
            return "\n市价：约 " + def.Price + " 文　此处买入 " + BuyPrice(def, reputation) +
                " 文，转手 " + SellPrice(def, reputation) + " 文";
        }

        public static string RelicLine(ItemDef def) =>
            BagService.IsPlotItem(def?.Category) ? "\n" + RelicNotice : "";

        public static string GearLine(ItemDef def) => IsGear(def) ? "\n" + DurabilityPending : "";

        public static string FateLine(ItemDef def) =>
            def != null && EconomyRules.IsFateGated(def.Id) ? "\n" + FateResonance : "";

        public static string TierName(ItemTable table, ItemDef def)
        {
            var tier = def == null ? null : table.FindTier(def.Tier);
            return tier?.Name ?? "未定";
        }

        /// <summary>品阶色（#RRGGBB 原文）：界面上色，断言里核对"天阶与凡品不同色"。</summary>
        public static string TierColor(ItemTable table, ItemDef def)
        {
            var tier = def == null ? null : table.FindTier(def.Tier);
            return tier?.Color ?? "";
        }

        public static string CategoryName(ItemTable table, ItemDef def)
        {
            var category = def == null ? null : table.FindCategory(def.Category);
            return category?.Name ?? "杂物";
        }

        static string StackLine(ItemDef def, int count) =>
            "\n数量：" + Math.Max(0, count) + (def != null && def.Stack > 1 ? " / " + def.Stack : "");

        public static string Body(ItemTable table, ItemDef def, int count, int reputation) =>
            (def?.Description ?? "这一件来历不明，像是从别处流过来的。") +
            "\n\n类别：" + CategoryName(table, def) + StackLine(def, count) +
            PriceLine(def, reputation) + GearLine(def) + EffectLine(def) + FateLine(def) + RelicLine(def);
    }
}
