using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TwelveJade.Core;

namespace TwelveJade.Core
{
    // M3 交易：商品表能读能挑错，价格、存货、记忆、黑市与存档往返都要对。
    public static class TradeChecks
    {
        static string tempRoot;

        public static void Run(ItemTable items, string configDir)
        {
            tempRoot = Path.Combine(Path.GetTempPath(), "twelve-jade-trade-" + Guid.NewGuid().ToString("N"));
            try { RunInner(items, configDir); }
            finally { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); }
        }

        static void RunInner(ItemTable items, string configDir)
        {
            var merchants = MerchantTable.Parse(File.ReadAllText(Path.Combine(configDir, "merchants.json")));
            merchants.CrossCheck(items);
            var peddler = merchants.Find("huolang");
            Check("merchant table parses", merchants.Merchants.Count >= 1, merchants.Merchants.Count + " 个商人");
            Check("merchant has both shelves", peddler.Stock.Count >= 5 && peddler.Hidden.Count >= 2);
            Check("merchant buys by category", peddler.Buys.Contains("chishi") && peddler.Buys.Contains("cailiao"));
            Check("merchant remembers", peddler.Memory.Count >= 3);
            Check("black shelf is not on the open shelf",
                peddler.Hidden.All(h => peddler.StockOf(h.itemId) == 0));

            // 坏表要被拒绝。
            var bad = new[]
            {
                ("{\"merchants\":[]}", "没有商人"),
                ("{\"merchants\":[{\"id\":\"a\",\"name\":\"甲\",\"title\":\"乙\",\"greeting\":\"丙\",\"stock\":[],\"hidden\":[]}]}", "没有货"),
                ("{\"merchants\":[{\"id\":\"a\",\"name\":\"甲\",\"title\":\"乙\",\"greeting\":\"丙\",\"stock\":[{\"itemId\":\"x\",\"count\":1}]}," +
                 "{\"id\":\"a\",\"name\":\"甲\",\"title\":\"乙\",\"greeting\":\"丙\",\"stock\":[{\"itemId\":\"x\",\"count\":1}]}]}", "id 重复"),
                ("{\"merchants\":[{\"id\":\"a\",\"name\":\"甲\",\"title\":\"乙\",\"greeting\":\"丙\",\"stock\":[{\"itemId\":\"x\",\"count\":1}]," +
                 "\"memory\":[{\"at\":1,\"line\":\"一\"},{\"at\":1,\"line\":\"二\"}]}]}", "记忆门槛重复"),
                ("{\"merchants\":[{\"id\":\"a\",\"name\":\"甲\",\"title\":\"乙\",\"greeting\":\"丙\",\"stock\":[{\"itemId\":\"x\",\"count\":-2}]}]}", "负存货"),
                ("{\"merchants\":[{\"id\":\"a\",\"name\":\"甲\",\"title\":\"乙\",\"stock\":[{\"itemId\":\"x\",\"count\":1}]}]}", "缺开场白")
            };
            foreach (var (json, label) in bad)
            {
                var rejected = false;
                try { MerchantTable.Parse(json); }
                catch (FormatException) { rejected = true; }
                Check("malformed merchant table rejected: " + label, rejected);
            }
            // 同一件货在一条货架里重复。
            var dupStock = "{\"merchants\":[{\"id\":\"a\",\"name\":\"甲\",\"title\":\"乙\",\"greeting\":\"丙\"," +
                "\"buys\":[\"chishi\"],\"stock\":[{\"itemId\":\"ganliang\",\"count\":2},{\"itemId\":\"ganliang\",\"count\":3}]," +
                "\"hidden\":[],\"memory\":[]}]}";
            var dupHidden = "{\"merchants\":[{\"id\":\"a\",\"name\":\"甲\",\"title\":\"乙\",\"greeting\":\"丙\"," +
                "\"buys\":[\"chishi\"],\"stock\":[{\"itemId\":\"ganliang\",\"count\":1}]," +
                "\"hidden\":[{\"itemId\":\"ganliang\",\"count\":1}],\"memory\":[]}]}";
            var dupRejectedBoth = 0;
            foreach (var json in new[] { dupStock, dupHidden })
            {
                try { MerchantTable.Parse(json); }
                catch (FormatException) { dupRejectedBoth++; }
            }
            Check("duplicate shelf rows are rejected", dupRejectedBoth == 2);

            // M3-02：长货架（十行）也要能读能对表——T013 的"最多 8 行"解除后，加货只改表。
            var longShelf = MerchantTable.Parse(LongShelfTable);
            longShelf.CrossCheck(items);
            Check("a ten-row shelf parses and cross-checks", longShelf.Find("huolang").Stock.Count == 10);
            // 只有黑市独门货的商人合法：白日铺会走空状态，不该被解析拒绝。
            var hiddenOnly = MerchantTable.Parse(
                "{\"merchants\":[{\"id\":\"a\",\"name\":\"甲\",\"title\":\"乙\",\"greeting\":\"丙\"," +
                "\"buys\":[\"chishi\"],\"stock\":[],\"hidden\":[{\"itemId\":\"ganliang\",\"count\":1}],\"memory\":[]}]}");
            Check("a hidden-only merchant parses", hiddenOnly.Find("a").Stock.Count == 0 &&
                hiddenOnly.Find("a").Hidden.Count == 1);

            var unknownItem = new MerchantTable(new List<MerchantDef>
            {
                new MerchantDef("x", "甲", "乙", "丙", new List<MerchantStock> { new MerchantStock { itemId = "不存在", count = 1 } },
                    new List<MerchantStock>(), new List<string> { "chishi" }, new List<MerchantMemoryLine>()),
            });
            var crossRejected = false;
            try { unknownItem.CrossCheck(items); }
            catch (FormatException) { crossRejected = true; }
            Check("merchant selling an unknown item is rejected", crossRejected);
            var unknownCategory = new MerchantTable(new List<MerchantDef>
            {
                new MerchantDef("x", "甲", "乙", "丙", new List<MerchantStock> { new MerchantStock { itemId = "ganliang", count = 1 } },
                    new List<MerchantStock>(), new List<string> { "不存在的类" }, new List<MerchantMemoryLine>()),
            });
            var categoryRejected = false;
            try { unknownCategory.CrossCheck(items); }
            catch (FormatException) { categoryRejected = true; }
            Check("merchant buying an unknown category is rejected", categoryRejected);

            // 价格。
            var food = items.Find("ganliang");
            var sword = items.Find("tiejian");
            var relic = items.Find("yupei");
            var robe = items.Find("cubupao");
            Check("buy price honours reputation", Trade.BuyPrice(sword, 0) == sword.Price &&
                Trade.BuyPrice(sword, 100) == (int)Math.Ceiling(sword.Price * .85f) &&
                Trade.BuyPrice(sword, 100) < Trade.BuyPrice(sword, 0));
            // 便宜货打完折仍是原价：向上取整后不低于原价，也不会归零（4 文干粮 ×0.85 仍是 4）。
            Check("a penny item never drops below its own price",
                Trade.BuyPrice(food, 100) == Trade.BuyPrice(food, 0) && Trade.BuyPrice(food, 0) > 0);
            Check("sell price is half and rises with reputation", Trade.SellPrice(sword, 0) == sword.Price / 2 &&
                Trade.SellPrice(sword, 100) > Trade.SellPrice(sword, 0) &&
                Trade.SellPrice(sword, 100) == (int)Math.Floor(sword.Price * .5 * 1.15f));
            Check("black market is a dearer forty percent",
                Trade.BlackPrice(sword) == (int)Math.Ceiling(sword.Price * Trade.BlackMarketMultiplier) &&
                Trade.BlackPrice(sword) > Trade.BuyPrice(sword, 0));
            Check("quest relics never sell", !Trade.CanSell(relic, peddler));
            Check("bought categories do sell", Trade.CanSell(food, peddler) && Trade.CanSell(robe, peddler));
            Check("categories the merchant ignores are refused",
                peddler.Buys.Contains("bingqi") || !Trade.CanSell(sword, peddler));
            Check("missing coin count", Trade.Missing(10, 40) == 30 && Trade.Missing(50, 40) == 0 &&
                !Trade.CanAfford(10, 40) && Trade.CanAfford(40, 40));

            // 黑市可见性按命格（id 取自 CharacterGen，拼错一个就永远看不见）。
            Check("black market is fate-gated", Trade.SeesBlackMarket(NewSave("shaxing")) &&
                Trade.SeesBlackMarket(NewSave("longshe")) && Trade.SeesBlackMarket(NewSave("chiqing")) &&
                !Trade.SeesBlackMarket(NewSave("anle")) && !Trade.SeesBlackMarket(NewSave("wanderer")));
            Check("black market ids are real destinies",
                Trade.BlackMarketFavouredDestinies.All(id => CharacterGen.FindDestiny(id) != null));

            // 记忆分阶：取不超过当前次数的最高门槛。
            Check("memory line steps with trades", peddler.MemoryLine(0) != peddler.MemoryLine(1) &&
                peddler.MemoryLine(0) == peddler.Memory.OrderBy(m => m.at).First().line &&
                peddler.MemoryLine(99) == peddler.Memory.OrderByDescending(m => m.at).First().line);

            // 见面铺货 → 买入扣钱进包减存货 → 记忆累加。
            var save = NewSave("anle");
            save.coins = 500;
            var state = MerchantLedger.Of(save, peddler);
            Check("first meeting lays out the shelves", save.merchants.Length == 1 &&
                MerchantLedger.Left(state, "ganliang") == peddler.StockOf("ganliang") &&
                MerchantLedger.Left(state, state.hidden, "ganliang") == 0);
            Check("meeting again reuses the same state", ReferenceEquals(MerchantLedger.Of(save, peddler), state) &&
                save.merchants.Length == 1);

            var stockBefore = MerchantLedger.Left(state, "ganliang");
            var unit = Trade.BuyPrice(food, save.localReputation);
            var bought = MerchantLedger.Buy(save, state, items, "ganliang", 3, false);
            Check("buying moves money, goods and stock", bought == 3 && save.coins == 500 - unit * 3 &&
                InventoryRules.Count(save.bag, "ganliang") == 3 &&
                MerchantLedger.Left(state, "ganliang") == stockBefore - 3 && state.trades == 3 &&
                state.goodwill == 3 * Trade.GoodwillPerTrade);

            var fluteStock = MerchantLedger.Left(state, "zhudi");
            Check("cannot buy more than the shelf holds",
                MerchantLedger.Buy(save, state, items, "zhudi", 99, false) == fluteStock &&
                MerchantLedger.Left(state, "zhudi") == 0);
            var cheapLeft = MerchantLedger.Left(state, "ganliang");
            save.coins = 1;
            Check("cannot buy more than the purse allows",
                MerchantLedger.Buy(save, state, items, "ganliang", 99, false) == 0 &&
                MerchantLedger.Left(state, "ganliang") == cheapLeft);

            // 卖出：出货、收钱、记忆累加；任务物不动。
            var coinsBefore = save.coins;
            var sold = MerchantLedger.Sell(save, state, items, peddler, "ganliang", 2);
            Check("selling pays out", sold == 2 &&
                save.coins == coinsBefore + Trade.SellPrice(food, save.localReputation) * 2 &&
                InventoryRules.Count(save.bag, "ganliang") == 1);
            Check("selling more than owned is refused",
                MerchantLedger.Sell(save, state, items, peddler, "ganliang", 99) == 0);
            save.bag[0] = new ItemStack("yupei", 1);
            Check("selling a relic is refused",
                MerchantLedger.Sell(save, state, items, peddler, "yupei", 1) == 0 &&
                InventoryRules.Count(save.bag, "yupei") == 1);

            // 包满时一件也装不下：钱与货都该原样留下。
            var full = NewSave("anle");
            full.coins = 5000;
            for (var i = 0; i < InventoryRules.SlotCount; i++) full.bag[i] = new ItemStack("tiejian", 1);
            var fullState = MerchantLedger.Of(full, peddler);
            var fullStock = MerchantLedger.Left(fullState, "ganliang");
            var fullCoins = full.coins;
            var fitted = MerchantLedger.Buy(full, fullState, items, "ganliang", 3, false);
            Check("a full bag takes nothing", fitted == 0 && full.coins == fullCoins &&
                MerchantLedger.Left(fullState, "ganliang") == fullStock);

            // 只剩一格空位：柴刀堆叠上限为 1，买两把只装得下一把，多收的钱要退回货架。
            var half = NewSave("anle");
            half.coins = 5000;
            for (var i = 0; i < InventoryRules.SlotCount - 1; i++) half.bag[i] = new ItemStack("tiejian", 1);
            var halfState = MerchantLedger.Of(half, peddler);
            var halfStock = MerchantLedger.Left(halfState, "cudao");
            var halfUnit = Trade.BuyPrice(items.Find("cudao"), half.localReputation);
            var partial = MerchantLedger.Buy(half, halfState, items, "cudao", 2, false);
            Check("a nearly full bag takes only what fits", partial == 1 &&
                half.coins == 5000 - halfUnit &&
                MerchantLedger.Left(halfState, "cudao") == halfStock - 1 &&
                InventoryRules.Count(half.bag, "cudao") == 1);

            // 堆叠上限之内：一格能装九件干粮，十二件分两格。
            var stacked = NewSave("anle");
            MerchantLedger.Buy(stacked, MerchantLedger.Of(stacked, peddler), items, "ganliang", 12, false);
            Check("a stack of nine holds and spills into the next slot",
                InventoryRules.UsedSlots(stacked.bag) == 2 && InventoryRules.Count(stacked.bag, "ganliang") == 12);

            // 黑市：加价成交，且只动 hidden 货架。
            var shady = NewSave("shaxing");
            shady.coins = 1000;
            var shadyState = MerchantLedger.Of(shady, peddler);
            var hiddenBefore = MerchantLedger.Left(shadyState, shadyState.hidden, "tiejian");
            var blackBought = MerchantLedger.Buy(shady, shadyState, items, "tiejian", 1, true);
            Check("black market trade is dearer and uses the hidden shelf", blackBought == 1 &&
                shady.coins == 1000 - Trade.BlackPrice(sword) &&
                MerchantLedger.Left(shadyState, shadyState.hidden, "tiejian") == hiddenBefore - 1 &&
                MerchantLedger.Left(shadyState, shadyState.stock, "tiejian") == 0);

            // 存档：迁移与校验走真实的 SaveRepository。
            var legacy = new SaveData { slot = 1, characterId = "farmer", characterName = "甲", schemaVersion = 3 };
            SaveData.Migrate(legacy);
            Check("v3 archive migrates to an empty ledger", legacy.merchants != null && legacy.merchants.Length == 0);
            legacy.merchants = new[] { state };
            SaveData.Migrate(legacy);
            Check("migration keeps a ledger that already exists", legacy.merchants.Length == 1 &&
                legacy.merchants[0].trades == state.trades);

            var repository = new SaveRepository(tempRoot, new PlainCodec()) { Items = items };
            // 兜底表：配置缺失/损坏时前端会退到它，它自己必须先过 MerchantTable.Parse。
            var fallback = MerchantTable.Parse(FallbackMerchantTable);
            fallback.CrossCheck(items);
            var fallbackMerchant = fallback.Find("none");
            Check("fallback merchant table is valid", fallbackMerchant != null &&
                fallbackMerchant.Stock.Count >= 1 && fallbackMerchant.Buys.Count >= 1 &&
                !string.IsNullOrEmpty(fallbackMerchant.MemoryLine(0)));

            var written = repository.Create(1, "farmer", "甲");
            MerchantLedger.Of(written, peddler);
            MerchantLedger.Buy(written, written.merchants[0], items, "ganliang", 2, false);
            repository.Write(written);
            var reloaded = repository.Read(1).Data;
            Check("trade state survives a save roundtrip", reloaded.schemaVersion == SaveData.CurrentSchemaVersion &&
                reloaded.merchants.Length == 1 && reloaded.merchants[0].trades == 2 &&
                reloaded.coins == ItemTable.StartingCoins - Trade.BuyPrice(food, 0) * 2 &&
                MerchantLedger.Left(reloaded.merchants[0], "ganliang") == peddler.StockOf("ganliang") - 2);
            Check("memory line is derived from the saved ledger",
                peddler.MemoryLine(reloaded.merchants[0].trades) != peddler.MemoryLine(0));

            ExpectRejected(repository, "duplicate merchant ids",
                d => { d.merchants = new[] { written.merchants[0], written.merchants[0] }; });
            ExpectRejected(repository, "negative trade count",
                d => { d.merchants = new[] { new MerchantState { id = "huolang", trades = -1 } }; });
            ExpectRejected(repository, "stock above the cap",
                d => { d.merchants = new[] { new MerchantState { id = "huolang", stock = new[] { new StockLine { itemId = "ganliang", count = Trade.MaxQuantity + 1 } } } }; });

            // P2：存档校验要按单品堆叠上限卡，不能只看全局 99。
            ExpectRejected(repository, "ganliang over its own stack of 9",
                d => { d.bag = InventoryRules.NewBag(); d.bag[0] = new ItemStack("ganliang", 10); });
            ExpectRejected(repository, "two knives on one slot (stack of 1)",
                d => { d.bag = InventoryRules.NewBag(); d.bag[0] = new ItemStack("cudao", 2); });
            var capped = new SaveData { slot = 2, characterId = "farmer", characterName = "甲", schemaVersion = 4 };
            capped.bag = InventoryRules.NewBag();
            capped.bag[0] = new ItemStack("ganliang", 40);
            capped.bag[1] = new ItemStack("早已下架的旧物", 80);
            SaveData.Migrate(capped, items);
            Check("migration trims to the item's own limit", capped.bag[0].count == 9 &&
                capped.bag[1].count == 80);
            Check("a slot limit of 1 is honoured", InventoryRules.StackLimitOf(items, "cudao") == 1 &&
                InventoryRules.StackLimitOf(items, "ganliang") == 9 &&
                InventoryRules.StackLimitOf(items, "没见过的旧物") == ItemTable.MaxStack);
        }

        static void Check(string name, bool condition, string detail = null)
        {
            if (!condition) throw new Exception("FAIL: " + name + (detail == null ? "" : "（" + detail + "）"));
            Console.WriteLine("PASS: " + name + (detail == null ? "" : "（" + detail + "）"));
        }

        static void ExpectRejected(SaveRepository repository, string label, Action<SaveData> corrupt)
        {
            var data = repository.Read(1).Data;
            corrupt(data);
            var rejected = false;
            try { repository.Write(data); }
            catch (ArgumentException) { rejected = true; }
            Check("save rejects: " + label, rejected);
        }

        static SaveData NewSave(string destiny) => new SaveData
        {
            slot = 1, characterId = "farmer", characterName = "甲",
            gender = "male", faceStyle = 0, destiny = destiny,
        };

        // 与 Presentation 层 FrontEndTrade.EmptyMerchantTable 保持一致：记忆行必须非空。
        const string FallbackMerchantTable =
            "{\"merchants\":[{\"id\":\"none\",\"name\":\"货郎\",\"title\":\"走南闯北的散商\",\"greeting\":\"今日只看货，不谈旧事。\"," +
            "\"buys\":[\"chishi\"],\"stock\":[{\"itemId\":\"ganliang\",\"count\":1}],\"hidden\":[]," +
            "\"memory\":[{\"at\":0,\"line\":\"今日只看货，不谈旧事。\"}]}]}";

        // M3-02：十行白日货架的临时长表——七件正式常进货 + 青灵草、符箓、洞箫，全部指向真实物品。
        const string LongShelfTable =
            "{\"merchants\":[{\"id\":\"huolang\",\"name\":\"货郎\",\"title\":\"走南闯北的散商\",\"greeting\":\"客官来点啥？\"," +
            "\"buys\":[\"chishi\",\"cailiao\",\"qiyong\"]," +
            "\"stock\":[{\"itemId\":\"ganliang\",\"count\":12},{\"itemId\":\"chuibing\",\"count\":12},{\"itemId\":\"larou\",\"count\":8}," +
            "{\"itemId\":\"jinchuangyao\",\"count\":6},{\"itemId\":\"caoyao\",\"count\":20},{\"itemId\":\"cudao\",\"count\":2}," +
            "{\"itemId\":\"zhudi\",\"count\":1},{\"itemId\":\"qinglingcao\",\"count\":10},{\"itemId\":\"fulu\",\"count\":4},{\"itemId\":\"dongxiao\",\"count\":1}]," +
            "\"hidden\":[{\"itemId\":\"tiejian\",\"count\":1}],\"memory\":[{\"at\":0,\"line\":\"头回见面。\"}]}]}";

        sealed class PlainCodec : IJsonCodec
        {
            public string Serialize<T>(T value) => System.Text.Json.JsonSerializer.Serialize(value,
                new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
            public T Deserialize<T>(string json) where T : class =>
                System.Text.Json.JsonSerializer.Deserialize<T>(json,
                    new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
        }
    }
}
