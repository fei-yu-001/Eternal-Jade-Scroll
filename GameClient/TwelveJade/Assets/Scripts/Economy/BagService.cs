using System;
using System.Collections.Generic;
using TwelveJade.Core;

namespace TwelveJade.Economy
{
    // 行囊 2.0 的纯逻辑层：五档分类筛选、智能理包、剧情信物保护。
    // 刻意不碰 UnityEngine——这样它既能被 Unity 的界面调用，也能被无 Unity 的
    // 控制台测试（SaveChecks）直接跑，判据不必分成两处写。
    public enum BagFilterKind
    {
        All = 0,        // 全部：完整 24 格
        Equipment = 1,  // 装备：兵器与器用
        Consumable = 2, // 丹药耗材：丹药与吃食
        Material = 3,   // 生活材料
        Relic = 4,      // 机缘信物
    }

    public static class BagService
    {
        // 类别口径已上移到 Core 的 PlotItemRules（T095）：Core 不能反过来引用 Economy，
        // 真源只能放在被引用的那一侧。这里保留同名转发，是为了让界面与既有断言不必改调用形。
        public const string RelicCategory = PlotItemRules.Relic;
        public const string QuestCategory = PlotItemRules.Quest;

        public static readonly string[] FilterNames = { "全部", "装备", "丹药", "材料", "信物" };

        /// <summary>五档筛选：类别是表里的数据，这里只做"哪几类归到哪一档"的映射。
        /// 类别 id 一律取 ItemCategoryNames，不在两处各写一遍字面量（T095 同一条纪律）。</summary>
        public static bool Allows(BagFilterKind kind, string category)
        {
            switch (kind)
            {
                case BagFilterKind.Equipment:
                    return category == ItemCategoryNames.Weapon || category == ItemCategoryNames.Gear;
                case BagFilterKind.Consumable:
                    return category == ItemCategoryNames.Medicine || category == ItemCategoryNames.Food;
                case BagFilterKind.Material:
                    return category == ItemCategoryNames.Material;
                case BagFilterKind.Relic:
                    return IsPlotItem(category);
                default:
                    return true;
            }
        }

        /// <summary>剧情信物：机缘信物与任务物。这类东西不可卖、不可弃——
        /// 丢了它剧情就断了，玩家还不一定能在第一时间发现。判据本身在 Core（T095）。</summary>
        public static bool IsPlotItem(string category) => PlotItemRules.IsPlotItem(category);

        public static bool CanDiscard(ItemDef def) => def != null && !IsPlotItem(def.Category);

        public static bool CanDiscard(ItemTable table, string itemId) =>
            CanDiscard(table?.Find(itemId));

        /// <summary>品阶权重：items.json 里 tiers 的声明顺序就是"凡 → 黄 → 玄 → 地 → 天"
        /// （由贱到贵），所以权重**取声明序号**——天阶最重、凡品最轻。
        /// 早前写成"反序"，结果凡品权重最高、天阶最低，理包顺序整个颠倒，
        /// 是被 InventoryEconomySuite 的"品阶权重 天 > 地"那一条当场抓住的。</summary>
        public static int TierRank(ItemTable table, string tierId)
        {
            var tiers = table?.Tiers;
            if (tiers == null || tiers.Count == 0) return 0;
            for (var i = 0; i < tiers.Count; i++)
                if (tiers[i].Id == tierId) return i;
            return -1;
        }

        /// <summary>智能理包：先同类合并到堆叠上限，再按品阶（降序）→ 类别 → 物品 id 重排，
        /// 空槽一律归拢到尾部。绝不丢件——合并前后每样东西的总数必须一致。</summary>
        public static void SortAndCompactBag(ItemStack[] bag, ItemTable table)
        {
            if (bag == null) return;
            var totals = new Dictionary<string, int>(StringComparer.Ordinal);
            var order = new List<string>();
            for (var i = 0; i < bag.Length; i++)
            {
                var stack = bag[i];
                if (stack == null || stack.IsEmpty) continue;
                if (!totals.ContainsKey(stack.id)) { totals[stack.id] = 0; order.Add(stack.id); }
                totals[stack.id] += stack.count;
            }
            order.Sort((a, b) =>
            {
                var left = table?.Find(a);
                var right = table?.Find(b);
                var rankLeft = TierRank(table, left?.Tier);
                var rankRight = TierRank(table, right?.Tier);
                if (rankLeft != rankRight) return rankRight.CompareTo(rankLeft);
                var byCategory = string.CompareOrdinal(left?.Category ?? "", right?.Category ?? "");
                return byCategory != 0 ? byCategory : string.CompareOrdinal(a, b);
            });

            var rebuilt = InventoryRules.NewBag();
            var index = 0;
            foreach (var id in order)
            {
                var remaining = totals[id];
                var limit = table?.Find(id)?.Stack ?? ItemTable.MaxStack;
                if (limit < 1) limit = 1;
                while (remaining > 0 && index < InventoryRules.SlotCount)
                {
                    var put = Math.Min(limit, remaining);
                    rebuilt[index] = new ItemStack(id, put);
                    remaining -= put;
                    index++;
                }
            }
            var slots = Math.Min(bag.Length, InventoryRules.SlotCount);
            for (var i = 0; i < slots; i++) bag[i] = rebuilt[i];
        }

        /// <summary>理包前后对照：任一件东西的总数变了就是丢件，必须能被断言抓到。</summary>
        public static int TotalCount(ItemStack[] bag)
        {
            if (bag == null) return 0;
            var total = 0;
            foreach (var stack in bag)
                if (stack != null && !stack.IsEmpty) total += stack.count;
            return total;
        }
    }

    // 丢弃防手滑（ADR-029 第 1 条第 4 点）：同一格连点两次才真撒手，换了格子作废。
    //
    // 做成有状态的小类而不是界面上一个 int，是因为这条规则要能被断言跑：
    // "第一次点击绝不能减少件数"这种事，一旦只写在界面的 if 里，就没有人替玩家守着。
    // 剧情信物不参与这套——它在第一次点击就被拒掉，不该给出"再确认也丢不了"的假承诺。
    public sealed class DiscardGuard
    {
        public const string NeedSelectionNotice = "先点一件要丢的东西。";
        public const string EmptySlotNotice = "这一格是空的。";
        public const string PlotRefusal = "「{0}」是要紧的信物，丢不得。";
        public const string ArmNotice = "「{0}」丢了就捡不回来了。再点一次【丢弃】才真撒手。";
        public const string DoneNotice = "丢了 1 件「{0}」。";

        static string Fill(string template, string name) => template.Replace("{0}", name ?? "");

        int armed = -1;

        /// <summary>当前"已警告过一次"的格位；-1 表示没有。</summary>
        public int ArmedSlot => armed;

        public void Reset() { armed = -1; }

        /// <summary>返回"这一次点击是否真的丢掉了 1 件"。没丢时 notice 说明为什么。</summary>
        public bool TryDiscard(ItemStack[] bag, ItemTable table, int slot, out string notice)
        {
            notice = "";
            if (bag == null || slot < 0 || slot >= bag.Length)
            {
                armed = -1;
                notice = NeedSelectionNotice;
                return false;
            }
            var stack = bag[slot];
            if (stack == null || stack.IsEmpty)
            {
                armed = -1;
                notice = EmptySlotNotice;
                return false;
            }
            var def = table?.Find(stack.id);
            var name = def?.Name ?? ItemInspector.UnknownName;
            if (!BagService.CanDiscard(def))
            {
                armed = -1;
                notice = Fill(PlotRefusal, name);
                return false;
            }
            if (armed != slot)
            {
                armed = slot;
                notice = Fill(ArmNotice, name);
                return false;
            }
            armed = -1;
            // 精确扣这一格：InventoryRules.Remove 会从尾部同 id 的堆里拿，
            // 玩家指着哪一堆就该少哪一堆，否则"丢的是眼前这件"成了假话。
            stack.count--;
            if (stack.count <= 0) stack.Clear();
            notice = Fill(DoneNotice, name);
            return true;
        }
    }
}
