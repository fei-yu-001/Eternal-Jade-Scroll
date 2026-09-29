using System;
using System.Collections.Generic;
using System.Linq;

namespace TwelveJade.Core
{
    // 物品表与行囊的测试：配置表能读、能挑错，堆叠与换位规则符合设计。
    public static class InventoryChecks
    {
        public static void Run(ItemTable table)
        {
            Check("item table tiers", table.Tiers.Count >= 5 && table.FindTier("fanpin")?.Name == "凡品");
            Check("item table categories", table.Categories.Count >= 5 && table.FindCategory("yaodan")?.Name == "丹药");
            Check("item table tier colors", table.Tiers.All(t => t.Color.StartsWith("#") && t.Color.Length == 7));
            Check("item table uniqueness", table.Items.Select(i => i.Id).Distinct().Count() == table.Items.Count);
            Check("item table references", table.Items.All(i =>
                table.FindTier(i.Tier) != null && table.FindCategory(i.Category) != null &&
                !string.IsNullOrWhiteSpace(i.Icon) && !string.IsNullOrWhiteSpace(i.Description)));
            Check("starting kit defined in the table", ItemTable.StartingKit.All(kit => table.Find(kit.id) != null) &&
                table.Find("yupei")?.Category == "renwu");
            Check("weapons are not stackable", table.Find("tiejian")?.Stack == 1 && table.Find("ganliang")?.Stack == 9);

            var malformed = new[]
            {
                ("{}", "缺 items"),
                ("{\"tiers\":[{\"id\":\"a\",\"name\":\"甲\"}],\"categories\":[{\"id\":\"b\",\"name\":\"乙\"}],\"items\":[]}", "空物品表"),
                ("{\"tiers\":[{\"id\":\"a\",\"name\":\"甲\"}],\"categories\":[{\"id\":\"b\",\"name\":\"乙\"}]," +
                 "\"items\":[{\"id\":\"x\",\"name\":\"甲物\",\"tier\":\"zzz\",\"category\":\"b\",\"icon\":\"i\"}]}", "未知品阶"),
                ("{\"tiers\":[{\"id\":\"a\",\"name\":\"甲\"}],\"categories\":[{\"id\":\"b\",\"name\":\"乙\"}]," +
                 "\"items\":[{\"id\":\"x\",\"name\":\"甲物\",\"tier\":\"a\",\"category\":\"b\",\"icon\":\"i\",\"stack\":0}]}", "堆叠上限为 0"),
                ("{\"tiers\":[{\"id\":\"a\",\"name\":\"甲\"}],\"categories\":[{\"id\":\"b\",\"name\":\"乙\"}]," +
                 "\"items\":[{\"id\":\"x\",\"name\":\"甲物\",\"tier\":\"a\",\"category\":\"b\",\"icon\":\"i\"}," +
                 "{\"id\":\"x\",\"name\":\"又一物\",\"tier\":\"a\",\"category\":\"b\",\"icon\":\"i\"}]}", "重复 id")
            };
            foreach (var (json, label) in malformed)
            {
                var rejected = false;
                try { ItemTable.Parse(json); }
                catch (FormatException) { rejected = true; }
                Check("malformed item table rejected: " + label, rejected);
            }

            var bag = InventoryRules.NewBag();
            Check("new bag is empty", InventoryRules.UsedSlots(bag) == 0 && InventoryRules.IsValid(bag));
            var leftover = InventoryRules.Add(bag, table, "ganliang", 12);
            Check("stacking respects the limit", leftover == 0 && InventoryRules.Count(bag, "ganliang") == 12 &&
                bag.Count(s => s.id == "ganliang") == 2 && bag[0].count == 9 && bag[1].count == 3);
            Check("tidy after sequential fills", InventoryRules.IsTidy(bag));
            Check("add rejects nonsense", InventoryRules.Add(bag, table, "ganliang", 0) == 0 &&
                InventoryRules.Add(bag, table, "ganliang", -5) == 0);

            var full = InventoryRules.NewBag();
            for (var i = 0; i < InventoryRules.SlotCount; i++) full[i] = new ItemStack("tiejian", 1);
            Check("full bag reports the leftover", InventoryRules.Add(full, table, "ganliang", 3) == 3);

            var moveBag = InventoryRules.NewBag();
            InventoryRules.Add(moveBag, table, "ganliang", 4);
            InventoryRules.Add(moveBag, table, "caoyao", 2);
            Check("swap moves the stack", InventoryRules.TryMove(moveBag, table, 0, 5) &&
                moveBag[5].id == "ganliang" && moveBag[0].IsEmpty);
            Check("swap onto another item exchanges them", InventoryRules.TryMove(moveBag, table, 5, 1) &&
                moveBag[1].id == "ganliang" && moveBag[5].id == "caoyao");
            moveBag[3] = new ItemStack("ganliang", 3);
            Check("merge merges same items", InventoryRules.TryMove(moveBag, table, 3, 1) &&
                moveBag[1].count == 7 && moveBag[3].IsEmpty);
            Check("swap refuses empty source", !InventoryRules.TryMove(moveBag, table, 7, 8) &&
                !InventoryRules.TryMove(moveBag, table, 0, 0));
            moveBag[3] = new ItemStack("ganliang", 40);
            moveBag[4] = new ItemStack("ganliang", 5);
            Check("merge respects the stack limit", InventoryRules.TryMove(moveBag, table, 3, 4) &&
                moveBag[4].count == 9 && moveBag[3].count == 36);
            moveBag[3].Clear();
            moveBag[4].Clear();

            InventoryRules.Add(moveBag, table, "caoyao", 20);
            InventoryRules.Sort(moveBag, table);
            Check("sort is tidy and lossless", InventoryRules.IsTidy(moveBag) &&
                InventoryRules.Count(moveBag, "caoyao") == 22 && moveBag[0].id == "ganliang");

            Check("remove takes the exact amount", InventoryRules.Remove(moveBag, "ganliang", 5) &&
                InventoryRules.Count(moveBag, "ganliang") == 2);
            Check("remove refuses to overdraw", !InventoryRules.Remove(moveBag, "ganliang", 3) &&
                InventoryRules.Count(moveBag, "ganliang") == 2);

            var normalized = InventoryRules.Normalize(new[]
            {
                new ItemStack("ganliang", 200), new ItemStack("", 4), null, new ItemStack("caoyao", -1)
            });
            Check("normalize trims illegal stacks", normalized.Length == InventoryRules.SlotCount &&
                normalized[0].count == ItemTable.MaxStack && normalized[1].IsEmpty && normalized[2].IsEmpty &&
                normalized[3].IsEmpty && InventoryRules.IsValid(normalized));
            Check("inventory validates", !InventoryRules.IsValid(null) &&
                !InventoryRules.IsValid(new ItemStack[4]) &&
                !InventoryRules.IsValid(new[] { new ItemStack("ganliang", 0) }.Concat(InventoryRules.NewBag().Skip(1)).ToArray()));
        }

        // 声望折扣：三档让利到上限，买卖都按同一折扣走。
        public static void RunReputation()
        {
            Check("reputation tiers step every twenty", Reputation.Tiers(0) == 0 && Reputation.Tiers(19) == 0 &&
                Reputation.Tiers(20) == 1 && Reputation.Tiers(60) == 3 && Reputation.Tiers(500) == Reputation.MaxTiers);
            Check("reputation discount caps at fifteen percent",
                Math.Abs(Reputation.Discount(100) - .15f) < .0001f && Reputation.Discount(-5) == 0f);
            Check("buy price falls with reputation", Reputation.BuyPrice(100, 0) == 100 &&
                Reputation.BuyPrice(100, 20) == 97 && Reputation.BuyPrice(100, 100) == 85);
            Check("sell price rises with reputation", Reputation.SellPrice(100, 0) == 50 &&
                Reputation.SellPrice(100, 100) == 57 && Reputation.SellPrice(1, 0) == 0);
            Check("prices never go negative", Enumerable.Range(0, 100).All(reputation =>
                Reputation.BuyPrice(3, reputation * 3) >= 0 && Reputation.SellPrice(3, reputation * 3) >= 0));
        }

        static void Check(string name, bool condition)
        {
            if (!condition) throw new Exception("FAIL: " + name);
            Console.WriteLine("PASS: " + name);
        }
    }
}
