using System;
using System.Collections.Generic;
using System.Linq;

namespace TwelveJade.Core
{
    // 一格行囊：id 为空即空格。存档里固定 24 格，JsonUtility 与配置表两边都能直接落盘。
    [Serializable]
    public sealed class ItemStack
    {
        public string id = "";
        public int count;

        public ItemStack() { }
        public ItemStack(string itemId, int amount) { id = itemId; count = amount; }

        public bool IsEmpty => string.IsNullOrEmpty(id) || count <= 0;
        public void Clear() { id = ""; count = 0; }
    }

    // 行囊规则：堆叠、放入、取出、换位——全部是纯逻辑，可在无 Unity 的测试里跑。
    // 物品的堆叠上限与品阶都由物品表决定，这里不写死任何一件具体物品。
    public static class InventoryRules
    {
        public const int SlotCount = 24;

        public static ItemStack[] NewBag()
        {
            var bag = new ItemStack[SlotCount];
            for (var i = 0; i < bag.Length; i++) bag[i] = new ItemStack();
            return bag;
        }

        // 旧档读入或缺字段时补齐格数、清掉非法数量；不改动物品本身。
        // 传入物品表时按单品上限截断（干粮 9、柴刀 1），不传则退回存档层全局上限。
        public static ItemStack[] Normalize(ItemStack[] bag, ItemTable table = null)
        {
            var result = NewBag();
            if (bag == null) return result;
            for (var i = 0; i < SlotCount && i < bag.Length; i++)
            {
                var source = bag[i];
                if (source == null || string.IsNullOrEmpty(source.id) || source.count <= 0) continue;
                result[i] = new ItemStack(source.id, Math.Min(source.count, StackLimitOf(table, source.id)));
            }
            return result;
        }

        // 单品堆叠上限：物品表说了算；表里没有的旧物品退回存档层上限，免得读不回来。
        public static int StackLimitOf(ItemTable table, string itemId) =>
            table?.Find(itemId)?.Stack ?? ItemTable.MaxStack;

        public static int Count(ItemStack[] bag, string id)
        {
            if (bag == null || string.IsNullOrEmpty(id)) return 0;
            return bag.Where(stack => stack != null && stack.id == id).Sum(stack => stack.count);
        }

        public static int UsedSlots(ItemStack[] bag) =>
            bag == null ? 0 : bag.Count(stack => stack != null && !stack.IsEmpty);

        // 放入：先填已有的同类堆叠，再占空格；返回放不下的余量（0 = 全部放进）。
        public static int Add(ItemStack[] bag, ItemTable table, string id, int amount)
        {
            if (bag == null) throw new ArgumentNullException(nameof(bag));
            if (amount <= 0) return 0;
            var def = table?.Find(id);
            var limit = def?.Stack ?? ItemTable.MaxStack;
            foreach (var stack in bag)
            {
                if (amount <= 0) break;
                if (stack == null || stack.IsEmpty || stack.id != id) continue;
                var room = limit - stack.count;
                if (room <= 0) continue;
                var moved = Math.Min(room, amount);
                stack.count += moved;
                amount -= moved;
            }
            foreach (var stack in bag)
            {
                if (amount <= 0) break;
                if (stack == null || !stack.IsEmpty) continue;
                var moved = Math.Min(limit, amount);
                stack.id = id;
                stack.count = moved;
                amount -= moved;
            }
            return amount;
        }

        public static bool Remove(ItemStack[] bag, string id, int amount)
        {
            if (bag == null || amount <= 0) return false;
            if (Count(bag, id) < amount) return false;
            for (var i = bag.Length - 1; i >= 0 && amount > 0; i--)
            {
                var stack = bag[i];
                if (stack == null || stack.IsEmpty || stack.id != id) continue;
                var taken = Math.Min(stack.count, amount);
                stack.count -= taken;
                amount -= taken;
                if (stack.count <= 0) stack.Clear();
            }
            return true;
        }

        // 换位：同类可堆叠物品合并，其余整体交换；返回是否发生了改动。
        public static bool TryMove(ItemStack[] bag, ItemTable table, int from, int to)
        {
            if (bag == null) return false;
            if (from == to) return false;
            if (from < 0 || to < 0 || from >= bag.Length || to >= bag.Length) return false;
            var source = bag[from];
            var target = bag[to];
            if (source == null || target == null || source.IsEmpty) return false;
            if (source.id == target.id)
            {
                var limit = table?.Find(source.id)?.Stack ?? ItemTable.MaxStack;
                var room = limit - target.count;
                if (room <= 0) return false;
                var moved = Math.Min(room, source.count);
                target.count += moved;
                source.count -= moved;
                if (source.count <= 0) source.Clear();
                return true;
            }
            bag[from] = target;
            bag[to] = source;
            return true;
        }

        // 整理：按物品表顺序左对齐、同类合并——玩家喊「整理行囊」时调用。
        public static void Sort(ItemStack[] bag, ItemTable table)
        {
            if (bag == null || table == null) return;
            var totals = new List<(int order, string id, int count)>();
            foreach (var def in table.Items)
            {
                var count = Count(bag, def.Id);
                if (count > 0) totals.Add((totals.Count, def.Id, count));
            }
            var unknown = bag.Where(s => s != null && !s.IsEmpty && table.Find(s.id) == null)
                .GroupBy(s => s.id).Select(g => (order: int.MaxValue, id: g.Key, count: g.Sum(x => x.count))).ToList();
            totals.AddRange(unknown);
            var rebuilt = NewBag();
            var index = 0;
            foreach (var (_, id, count) in totals)
            {
                var remaining = count;
                while (remaining > 0 && index < SlotCount)
                {
                    var limit = table.Find(id)?.Stack ?? ItemTable.MaxStack;
                    var put = Math.Min(limit, remaining);
                    rebuilt[index] = new ItemStack(id, put);
                    remaining -= put;
                    index++;
                }
            }
            for (var i = 0; i < SlotCount; i++) bag[i] = rebuilt[i];
        }

        // 校验每格：拿到物品表时按单品上限卡（ganliang:10、cudao:2 都要被拒）。
        public static bool IsValid(ItemStack[] bag, ItemTable table = null)
        {
            if (bag == null || bag.Length != SlotCount) return false;
            foreach (var stack in bag)
            {
                if (stack == null) return false;
                if (stack.count < 0 || stack.count > ItemTable.MaxStack) return false;
                if (string.IsNullOrEmpty(stack.id)) { if (stack.count != 0) return false; continue; }
                if (stack.id.Length > 32 || stack.count == 0) return false;
                if (stack.count > StackLimitOf(table, stack.id)) return false;
            }
            return true;
        }

        // 空格是否排在后面：只用来看「行囊是否整齐」，不参与校验。
        public static bool IsTidy(ItemStack[] bag)
        {
            var seenEmpty = false;
            foreach (var stack in bag)
            {
                var empty = stack == null || stack.IsEmpty;
                if (empty) seenEmpty = true;
                else if (seenEmpty) return false;
            }
            return true;
        }
    }
}
