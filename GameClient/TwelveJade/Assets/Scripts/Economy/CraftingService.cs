using System;
using System.Collections.Generic;
using System.Linq;
using TwelveJade.Core;

namespace TwelveJade.Economy
{
    // 手艺类型与词条加成。任务书写的是"草莽 / 灵秀"，但 CharacterGen.Traits 里
    // 并没有这两个 id——照抄就永远不会命中，加成变成死代码。这里改用真实存在的词条：
    // 烹饪看「手巧 deft」，炼药看「早慧 clever」。
    public static class Crafting
    {
        public const string Cooking = "cooking";
        public const string Alchemy = "alchemy";
        public const string Forging = "forging";

        public const string CookingTrait = "deft";    // 手巧
        public const string AlchemyTrait = "clever";  // 早慧
        public const float TraitBonus = .10f;

        public const string BagFullNotice = "行囊已满，无法制作。";

        // 界面可见性与真实结算共用同一条职业/修行门槛。
        public static bool IsAvailable(SaveData save, ItemTable table, RecipeDef recipe) =>
            save != null && table != null && recipe != null &&
            (PrologueRules.CanCultivate(save) ||
             recipe.CraftType == Cooking && table.Find(recipe.ResultItemId)?.Tier == "fanpin");

        /// <summary>熟练度（T046 / schema 11）：这道方子每做过一次，双倍率加半个百分点，
        /// 最多加满十个点（做满 20 次封顶）。ADR-029 原文批评的就是"做一百次和第一次一样"。
        /// 存的是次数不是"熟练度等级"：等级能算出来，能算出来的东西进存档就是第二真源。</summary>
        public const float ProficiencyPerCraft = .005f, ProficiencyCap = .10f;

        public static float ProficiencyFor(RecipeDef recipe, SaveData save) =>
            recipe == null || save == null ? 0f
                : Math.Min(CraftLedger.TimesOf(save, recipe.Id) * ProficiencyPerCraft, ProficiencyCap);

        /// <summary>双倍产出几率：方子底数 + 对口词条加成 + 这道方子的熟练度，封顶 1。</summary>
        public static float DoubleChanceFor(RecipeDef recipe, SaveData save)
        {
            if (recipe == null) return 0f;
            var chance = recipe.DoubleChance + ProficiencyFor(recipe, save);
            if (save?.traits == null) return (float)Math.Min(Math.Max(chance, 0f), 1f);
            var affinity = recipe.CraftType == Alchemy ? AlchemyTrait : CookingTrait;
            if (save.traits.Contains(affinity)) chance += TraitBonus;
            return (float)Math.Min(Math.Max(chance, 0f), 1f);
        }

        public static bool HasIngredients(ItemStack[] bag, RecipeDef recipe)
        {
            if (bag == null || recipe == null) return false;
            return recipe.Ingredients.All(line =>
                InventoryRules.Count(bag, line.ItemId) >= line.Count);
        }

        /// <summary>这件东西还能往包里塞几件：先算已有堆叠的余量，再算空格能开几堆。</summary>
        public static int RoomFor(ItemStack[] bag, ItemTable table, string itemId)
        {
            if (bag == null) return 0;
            var limit = table?.Find(itemId)?.Stack ?? ItemTable.MaxStack;
            if (limit < 1) limit = 1;
            var room = 0;
            var empties = 0;
            foreach (var stack in bag)
            {
                if (stack == null || stack.IsEmpty) { empties++; continue; }
                if (stack.id == itemId && stack.count < limit) room += limit - stack.count;
            }
            return room + empties * limit;
        }

        public static bool HasRoomFor(ItemStack[] bag, ItemTable table, string itemId, int count) =>
            RoomFor(bag, table, itemId) >= count;

        /// <summary>开工。返回真正做出来的件数（0 = 没做成），notice 给界面一句能念的话。
        /// 材料不够或行囊放不下都**在扣材料之前**拦住——先扣后失败会把玩家的东西吞掉。</summary>
        public static int Craft(SaveData save, ItemTable table, RecipeDef recipe, Random rng, out string notice)
        {
            notice = "";
            if (save == null || table == null || recipe == null) { notice = "这道方子还做不了。"; return 0; }
            // 在初始化行囊、扣材料和记熟练度之前拒绝；旧制作记录保持原样。
            if (!IsAvailable(save, table, recipe))
            { notice = PrologueRules.Refusal; return 0; }
            var bag = save.bag ??= InventoryRules.NewBag();
            if (!HasIngredients(bag, recipe))
            {
                var shortOf = recipe.Ingredients
                    .Where(line => InventoryRules.Count(bag, line.ItemId) < line.Count)
                    .Select(line => table.Find(line.ItemId)?.Name ?? line.ItemId)
                    .FirstOrDefault();
                notice = "材料不够，还差「" + shortOf + "」。";
                return 0;
            }

            // 先定产量再验空间：双倍若塞不下，退回单份——白费一次手艺比撑破行囊好。
            var doubled = rng != null && rng.NextDouble() < DoubleChanceFor(recipe, save);
            var output = recipe.ResultCount * (doubled ? 2 : 1);
            if (!HasRoomFor(bag, table, recipe.ResultItemId, output))
            {
                if (doubled && HasRoomFor(bag, table, recipe.ResultItemId, recipe.ResultCount))
                {
                    doubled = false;
                    output = recipe.ResultCount;
                }
                else { notice = BagFullNotice; return 0; }
            }

            foreach (var line in recipe.Ingredients)
                InventoryRules.Remove(bag, line.ItemId, line.Count);
            var leftover = InventoryRules.Add(bag, table, recipe.ResultItemId, output);
            var made = output - leftover;
            var name = table.Find(recipe.ResultItemId)?.Name ?? recipe.ResultItemId;
            notice = doubled && made > recipe.ResultCount
                ? "灵心独运——同样一份材料，多出 " + (made - recipe.ResultCount) + " 份「" + name + "」。"
                : "做好了 " + made + " 份「" + name + "」。";
            // 真做出了东西才记一笔（材料不够被拦下的那次不该涨熟练度）。
            // CraftLedger.Record 只改内存，落盘责任在调用点（T097 的划分）：
            // 界面侧 `if (made > 0) PersistBag()` 就是紧跟的那一次 Write。
            if (made > 0) CraftLedger.Record(save, recipe.Id);
            return made;
        }
    }
}
