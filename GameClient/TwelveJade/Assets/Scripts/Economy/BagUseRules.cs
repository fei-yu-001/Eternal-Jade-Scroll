using TwelveJade.Core;

namespace TwelveJade.Economy
{
    // 行囊"使用"动作的裁决层（D-09 / O6）。
    //
    // 为什么单独写出来：上一轮 BagUse 对吃食与药散是"数量减 1 + 一句用了"，
    // 而气血与精力**只存在于战斗内**（ADR-034：世界态没有 hp/stamina），
    // 也就是说玩家按下去什么也没发生，界面却替他记了一次"已经用过"——
    // 那不是便利，是替玩家把东西吃掉了还宣称有效。
    // 判据写在纯 C# 层，界面只负责念；这样"行囊不假装回血"这件事能被断言钉住。
    public static class BagUseRules
    {
        /// <summary>丹药：本轮由战斗页的"服药"动作结算（员工 N 域）。</summary>
        public const string MedicineRefusal = "「{0}」须在山野对敌时服用，行囊里动不了。";

        /// <summary>吃食：体力回复效果尚未接入，所以连数量都不扣。</summary>
        public const string FoodRefusal = "「{0}」的食用效果尚未接入，这一件没有被吃掉。";

        /// <summary>剧情物品：往后有大用。</summary>
        public const string PlotRefusal = "「{0}」往后有大用，此刻还动不得。";

        /// <summary>既不是吃食也不是丹药的东西。</summary>
        public const string NotEdible = "「{0}」不是入口的东西。";

        static string Fill(string template, string name) => template.Replace("{0}", name ?? "");

        /// <summary>返回 null 表示"这件现在真的能用、可以消耗"；
        /// 否则返回该对玩家说的那句话，**调用方不得扣数量、不得改任何数值**。</summary>
        public static string RefusalFor(ItemDef def)
        {
            if (def == null) return "这一件来历不明，还看不出用法。";
            if (PlotItemRules.IsPlotItem(def)) return Fill(PlotRefusal, def.Name);
            if (def.Category == ItemCategoryNames.Medicine) return Fill(MedicineRefusal, def.Name);
            if (def.Category == ItemCategoryNames.Food) return Fill(FoodRefusal, def.Name);
            return Fill(NotEdible, def.Name);
        }

        /// <summary>这件东西现在能不能被"使用"掉。本轮答案是恒 false（除将来接入的战斗页结算），
        /// 单独留着是为了让断言能钉住"行囊不消耗入口物"这条事实。</summary>
        public static bool Consumes(ItemDef def) => RefusalFor(def) == null;
    }

    // 类别 id 的口径（与 items.json 的 categories 逐字对齐）。
    // 放在 Core 之外、Economy 之内是因为只有生活线按类别做裁决；
    // 交易侧的"收不收"仍由 MerchantDef.buys 与 PlotItemRules 决定。
    public static class ItemCategoryNames
    {
        public const string Food = "chishi";        // 吃食
        public const string Medicine = "yaodan";    // 丹药
        public const string Material = "cailiao";   // 材料
        public const string Gear = "qiyong";        // 器用
        public const string Weapon = "bingqi";      // 兵器
    }
}
