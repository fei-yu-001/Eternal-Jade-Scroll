using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TwelveJade.Core;

namespace TwelveJade.Economy
{
    // 行囊 / 手艺 / 物价 / 气运的判据全集（85+ 项）。
    //
    // 为什么把断言放进 Economy 而不是直接写在 Editor 夹具里：
    // 这一层刻意不碰 UnityEngine，于是同一套判据能在两个地方跑——
    // Unity 的 InventoryEconomyShot.RunSuite，和没有 Unity 的 SaveChecks 控制台。
    // 判据只写一份，就不会出现"两边绿得不一样"这种事。
    public static class InventoryEconomySuite
    {
        public delegate void CheckFn(string label, bool ok, string detail = null);

        public static void Run(string configDir, CheckFn check)
        {
            // O0 的 marker：ConfigChecks.cs 把本套件关在 `if (worldPhase)` 里面，
            // 一旦哪天配置目录没找到、或阶段判据变了，这 200 多条会一行都不跑，而 SaveChecks 照样喊全过。
            // 所以第一条必须是"我确实进来了"，而且要能被数出来。
            check("判据入口被调用: InventoryEconomySuite", true, configDir);

            var items = ItemTable.Parse(Read(configDir, "items.json"));
            ItemFlags.Load(File.ReadAllText(Path.Combine(configDir, "items.json")));
            var recipes = RecipeTable.Parse(Read(configDir, RecipeTable.FileName));
            recipes.CrossCheck(items);
            var karma = KarmaTable.Parse(Read(configDir, KarmaTable.FileName));

            CheckItemDictionary(items, check);
            CheckBagSlots(items, check);
            CheckAutoSort(items, check);
            CheckFilters(items, check);
            CheckRelicProtection(items, check);
            CheckCooking(items, recipes, check);
            CheckAlchemy(items, recipes, check);
            CheckPrices(configDir, items, check);
            CheckBlackMarket(items, check);
            CheckKarma(configDir, karma, check);
            CheckTooltip(items, check);
            CheckDiscardGuard(items, check);
            CheckUseHonesty(configDir, items, check);
            CheckItemSources(configDir, items, recipes, check);
            CheckLifeLoopTables(configDir, items, check);
            CheckKarmaWiring(configDir, karma, check);
            CheckReachability(configDir, check);
            CheckProficiency(configDir, items, recipes, check);
            CheckStaleFixtureEvidence(configDir, items, check);
            CheckBidirectionalTables(configDir, items, karma, recipes, check);
            CheckQuestDriven(configDir, items, karma, check);
            CheckAssetRatchet(configDir, items, check);
            CheckCloudConflictTexts(configDir, check);
            CheckSaveCodecShape(configDir, check);
            CheckTalkBranchLayout(configDir, check);
        }

        static string Read(string dir, string file) => File.ReadAllText(Path.Combine(dir, file));

        static SaveData NewSave()
        {
            var save = new SaveData { slot = 1, characterId = "farmer", characterName = "甲" };
            save.bag = InventoryRules.NewBag();
            return save;
        }

        // ---------------- 1：items.json 2.0 ----------------

        static readonly string[] AddedItems =
        {
            "wheat-flour", "rou-fu", "licorice", "jinchuang-san",
            "wood-bow", "leather-bracer", "red-string", "black-box-relic"
        };

        // ADR-031 跨线 id 契约表：id｜中文名｜category｜基准价｜stack。
        // 架构师定名即终局文字，逐字对表——"两边各写一遍、名字悄悄不同"就是 T053 的形态，
        // 而掉落表（员工 N）与本表（员工 O）分属两条线，只有对表能拦住漂移。
        static readonly (string id, string name, string category, int price, int stack)[] ContractItems =
        {
            ("beast-gall", "野猪兽胆", "cailiao", 18, 5),
            ("wolf-fur", "冥狼皮", "cailiao", 26, 5),
            ("wolf-fang", "冥狼牙", "cailiao", 22, 5),
            ("mandrill-stone", "山魈石壳", "cailiao", 40, 3),
            ("iron-ore", "铁矿", "cailiao", 12, 10),
            ("dark-iron-armor", "玄铁甲", "qiyong", 520, 1),
        };

        static void CheckItemDictionary(ItemTable items, CheckFn check)
        {
            check("物品表 2.0 解析通过且已扩充", items.Items.Count >= 33, items.Items.Count + " 件");
            foreach (var id in AddedItems)
            {
                var def = items.Find(id);
                check("物品表含新物件 " + id, def != null);
                if (def == null) continue;
                check(id + " 名称非空", !string.IsNullOrEmpty(def.Name));
                check(id + " 品阶已在表里定义", items.FindTier(def.Tier) != null, def.Tier);
                check(id + " 类别已在表里定义", items.FindCategory(def.Category) != null, def.Category);
                check(id + " 堆叠上限合法 1–99", def.Stack >= 1 && def.Stack <= ItemTable.MaxStack,
                    def.Stack.ToString());
                check(id + " 基准价非负", def.Price >= 0, def.Price.ToString());
            }
            foreach (var (id, name, category, price, stack) in ContractItems)
            {
                var def = items.Find(id);
                check("ADR-031 契约物已落表 " + id, def != null);
                if (def == null) continue;
                check(id + " 中文名逐字照抄契约表", def.Name == name, def.Name);
                check(id + " 类别逐字照抄契约表", def.Category == category, def.Category);
                check(id + " 基准价逐字照抄契约表", def.Price == price, def.Price + " 文");
                check(id + " 堆叠上限逐字照抄契约表", def.Stack == stack, def.Stack + " 件");
            }
            check("机缘信物类别已登记", items.FindCategory(BagService.RelicCategory) != null);
            check("信物不标价（黑匣）", items.Find("black-box-relic").Price == 0);
            check("信物不标价（红绳）", items.Find("red-string").Price == 0);
            check("物品 id 无重复", items.Items.Select(i => i.Id).Distinct().Count() == items.Items.Count);
        }

        // ---------------- 2：24 格边界 ----------------

        static void CheckBagSlots(ItemTable items, CheckFn check)
        {
            var bag = InventoryRules.NewBag();
            check("行囊固定 24 格", bag.Length == InventoryRules.SlotCount, bag.Length + " 格");
            check("空包占用 0 格", InventoryRules.UsedSlots(bag) == 0);
            var leftover = InventoryRules.Add(bag, items, "ganliang", 12);
            check("干粮按 9 上限分堆", leftover == 0 && InventoryRules.Count(bag, "ganliang") == 12,
                InventoryRules.Count(bag, "ganliang") + " 件");
            check("分堆后占用 2 格", InventoryRules.UsedSlots(bag) == 2, InventoryRules.UsedSlots(bag) + " 格");
            // 溢出必须如实报余量，绝不静默吞掉。
            var full = InventoryRules.NewBag();
            var overflow = InventoryRules.Add(full, items, "wheat-flour", 24 * 9 + 5);
            check("塞满后如实报余量", overflow == 5 && InventoryRules.Count(full, "wheat-flour") == 24 * 9,
                overflow + " 件放不下");
            check("溢出后仍是 24 格", full.Length == InventoryRules.SlotCount);
            check("溢出后背包仍合法", InventoryRules.IsValid(full));
        }

        // ---------------- 3：智能理包 ----------------

        static void CheckAutoSort(ItemTable items, CheckFn check)
        {
            check("品阶权重 天 > 地", BagService.TierRank(items, "tianjie") > BagService.TierRank(items, "dijie"));
            check("品阶权重 地 > 玄", BagService.TierRank(items, "dijie") > BagService.TierRank(items, "xuanjie"));
            check("品阶权重 玄 > 黄", BagService.TierRank(items, "xuanjie") > BagService.TierRank(items, "huangjie"));
            check("品阶权重 黄 > 凡", BagService.TierRank(items, "huangjie") > BagService.TierRank(items, "fanpin"));
            check("未登记的品阶权重为 -1", BagService.TierRank(items, "not-a-tier") == -1);
            // 品阶顺序就是理包顺序的真源（tiers 声明序 = 凡→天）。它一旦被人中间插入或调换，
            // 上一轮那种"凡品排最前"的整线颠倒会静默回来——所以顺序本身要被快照钉住（T099）。
            var tierOrder = string.Join(",", items.Tiers.Select(t => t.Id));
            check("品阶声明顺序快照（要改顺序的人必须显式改这条期望）",
                tierOrder == "fanpin,huangjie,xuanjie,dijie,tianjie", tierOrder);
            check("每个物品的品阶都能在 tiers 里找到（查不到就是拼错，不许静默错排）",
                items.Items.All(d => items.FindTier(d.Tier) != null),
                string.Join(",", items.Items.Where(d => items.FindTier(d.Tier) == null).Select(d => d.Id)));
            check("每个物品的类别都能在 categories 里找到",
                items.Items.All(d => items.FindCategory(d.Category) != null));

            var messy = InventoryRules.NewBag();
            messy[0] = new ItemStack("ganliang", 9);
            messy[3] = new ItemStack("black-box-relic", 1);
            messy[5] = new ItemStack("caoyao", 5);
            messy[7] = new ItemStack("caoyao", 5);
            messy[9] = new ItemStack("jinchuang-san", 3);
            messy[11] = new ItemStack("wheat-flour", 9);
            messy[13] = new ItemStack("wheat-flour", 5);
            var before = BagService.TotalCount(messy);
            var usedBefore = InventoryRules.UsedSlots(messy);

            BagService.SortAndCompactBag(messy, items);

            check("理包不丢件", BagService.TotalCount(messy) == before,
                before + " → " + BagService.TotalCount(messy));
            check("药草两堆已按总数保住", InventoryRules.Count(messy, "caoyao") == 10,
                InventoryRules.Count(messy, "caoyao") + " 件");
            check("麦面两堆已合并计总", InventoryRules.Count(messy, "wheat-flour") == 14,
                InventoryRules.Count(messy, "wheat-flour") + " 件");
            check("理包后占用格数不增", InventoryRules.UsedSlots(messy) <= usedBefore,
                usedBefore + " → " + InventoryRules.UsedSlots(messy));
            check("最贵的排在首位（天阶黑匣）", messy[0].id == "black-box-relic", messy[0].id);

            var ranks = messy.Where(s => !s.IsEmpty)
                .Select(s => BagService.TierRank(items, items.Find(s.id)?.Tier)).ToList();
            var descending = true;
            for (var i = 1; i < ranks.Count; i++)
                if (ranks[i] > ranks[i - 1]) descending = false;
            check("品阶严格降序（天地玄黄凡）", descending, string.Join(",", ranks));

            var sawEmpty = false;
            var tidy = true;
            foreach (var stack in messy)
            {
                if (stack.IsEmpty) sawEmpty = true;
                else if (sawEmpty) tidy = false;
            }
            check("空槽已归拢到尾部", tidy);
        }

        // ---------------- 4：五档分类筛选 ----------------

        static void CheckFilters(ItemTable items, CheckFn check)
        {
            check("装备档收兵器", BagService.Allows(BagFilterKind.Equipment, "bingqi"));
            check("装备档收器用", BagService.Allows(BagFilterKind.Equipment, "qiyong"));
            check("装备档剔除材料", !BagService.Allows(BagFilterKind.Equipment, "cailiao"));
            check("装备档剔除吃食", !BagService.Allows(BagFilterKind.Equipment, "chishi"));
            check("丹药档收丹药", BagService.Allows(BagFilterKind.Consumable, "yaodan"));
            check("丹药档收吃食", BagService.Allows(BagFilterKind.Consumable, "chishi"));
            check("丹药档剔除兵器", !BagService.Allows(BagFilterKind.Consumable, "bingqi"));
            check("材料档只收材料", BagService.Allows(BagFilterKind.Material, "cailiao") &&
                !BagService.Allows(BagFilterKind.Material, "chishi"));
            check("信物档收机缘信物", BagService.Allows(BagFilterKind.Relic, BagService.RelicCategory));
            check("信物档收任务物", BagService.Allows(BagFilterKind.Relic, BagService.QuestCategory));
            check("信物档剔除材料", !BagService.Allows(BagFilterKind.Relic, "cailiao"));
            check("全部档什么都收", BagService.Allows(BagFilterKind.All, "renwu") &&
                BagService.Allows(BagFilterKind.All, "cailiao"));

            // 筛选不改变物理索引：只是"这一格显不显示"，索引永远是 0–23。
            var bag = InventoryRules.NewBag();
            bag[5] = new ItemStack("caoyao", 3);
            bag[9] = new ItemStack("black-box-relic", 1);
            var materialHits = 0;
            var relicHits = 0;
            for (var i = 0; i < bag.Length; i++)
            {
                if (bag[i].IsEmpty) continue;
                var category = items.Find(bag[i].id)?.Category;
                if (BagService.Allows(BagFilterKind.Material, category)) materialHits++;
                if (BagService.Allows(BagFilterKind.Relic, category)) relicHits++;
            }
            check("材料档只命中 1 格", materialHits == 1, materialHits + " 格");
            check("信物档只命中 1 格", relicHits == 1, relicHits + " 格");
            check("筛选不动物理索引", bag[5].id == "caoyao" && bag[9].id == "black-box-relic");
        }

        // ---------------- 5：信物保护 ----------------

        static void CheckRelicProtection(ItemTable items, CheckFn check)
        {
            check("黑匣属剧情信物", BagService.IsPlotItem(items.Find("black-box-relic").Category));
            check("红绳属剧情信物", BagService.IsPlotItem(items.Find("red-string").Category));
            check("黑匣不可丢弃", !BagService.CanDiscard(items, "black-box-relic"));
            check("红绳不可丢弃", !BagService.CanDiscard(items, "red-string"));
            check("任务物也不可丢弃", !BagService.CanDiscard(items, "yinzhang"));
            check("干粮可以丢弃", BagService.CanDiscard(items, "ganliang"));
            check("未登记之物不给丢（查不到定义）", !BagService.CanDiscard((ItemDef)null));
            check("符箓已标煞气门控", EconomyRules.IsFateGated("fulu"));
            check("干粮没有命途门控", !EconomyRules.IsFateGated("ganliang"));
            check("门控判定前必须已装表（T098）", ItemFlags.IsLoaded);

            // T094：上一轮"信物不可售卖"只写在交付说明里，代码里没有——CanSell 只挡 renwu，
            // 今天没爆纯粹因为 merchants.json 里 xinwu 出现 0 次。保护必须由代码给，不能靠数据偶然。
            check("信物属剧情物品（Core 真源口径）", PlotItemRules.IsPlotItem(items.Find("red-string").Category));
            check("任务物与信物同属剧情物品", PlotItemRules.IsPlotItem(items.Find("yupei").Category) &&
                !PlotItemRules.IsPlotItem(items.Find("ganliang").Category));
            var buysAnything = new MerchantDef("injection", "注入商人", "无", "哈",
                new List<MerchantStock> { new MerchantStock { itemId = "ganliang", count = 1 } },
                new List<MerchantStock>(),
                new List<string> { BagService.RelicCategory, BagService.QuestCategory, "chishi" },
                new List<MerchantMemoryLine>());
            check("把 xinwu 塞进收货品类后，信物仍然卖不掉",
                buysAnything.Buys.Contains(BagService.RelicCategory) &&
                !Trade.CanSell(items.Find("black-box-relic"), buysAnything));
            check("把 renwu 塞进收货品类后，任务物仍然卖不掉",
                !Trade.CanSell(items.Find("yupei"), buysAnything));
            check("同一商人照旧收寻常吃食（不是把 CanSell 整体关掉）",
                Trade.CanSell(items.Find("ganliang"), buysAnything));
        }

        // 双倍判定用固定随机源钉死：0 必定触发，0.999 必定不触发——
        // 真随机会让"这次到底双没双"变成一条有时绿的断言，那等于没断言。
        sealed class FixedRandom : Random
        {
            readonly double value;
            public FixedRandom(double value) { this.value = value; }
            public override double NextDouble() => value;
        }

        // ---------------- 6：烹饪手艺 ----------------

        static void CheckCooking(ItemTable items, RecipeTable recipes, CheckFn check)
        {
            var recipe = recipes.Find("recipe-rou-fu");
            check("烹饪配方已登记", recipe != null);
            check("肉脯属烹饪手艺", recipe.CraftType == Crafting.Cooking);
            check("肉脯要 1 份腊肉", recipe.Ingredients.Any(i => i.ItemId == "larou" && i.Count == 1));
            check("肉脯要 1 份麦面", recipe.Ingredients.Any(i => i.ItemId == "wheat-flour" && i.Count == 1));

            var never = new FixedRandom(.999);
            var always = new FixedRandom(0d);
            var save = NewSave();
            check("空手不具备材料", !Crafting.HasIngredients(save.bag, recipe));
            check("缺材料时开工被拦",
                Crafting.Craft(save, items, recipe, never, out var notice) == 0 && notice.Contains("材料不够"),
                notice);

            InventoryRules.Add(save.bag, items, "larou", 1);
            InventoryRules.Add(save.bag, items, "wheat-flour", 1);
            check("备齐材料即可开工", Crafting.HasIngredients(save.bag, recipe));
            check("不触发双倍时产出 1 份",
                Crafting.Craft(save, items, recipe, never, out notice) == 1, notice);
            check("腊肉已扣减", InventoryRules.Count(save.bag, "larou") == 0);
            check("麦面已扣减", InventoryRules.Count(save.bag, "wheat-flour") == 0);
            check("肉脯已入包", InventoryRules.Count(save.bag, "rou-fu") == 1);

            var lucky = NewSave();
            InventoryRules.Add(lucky.bag, items, "larou", 2);
            InventoryRules.Add(lucky.bag, items, "wheat-flour", 2);
            lucky.traits = new[] { Crafting.CookingTrait };
            check("手巧提高双倍几率",
                Crafting.DoubleChanceFor(recipe, lucky) > recipe.DoubleChance,
                Crafting.DoubleChanceFor(recipe, lucky).ToString("F2"));
            check("触发灵心独运时产出 2 份",
                Crafting.Craft(lucky, items, recipe, always, out notice) == 2, notice);
            check("灵心独运有话说", notice.Contains("灵心独运"), notice);

            // 包满必须安全拦截，而且是在扣材料之前拦。
            var packed = NewSave();
            InventoryRules.Add(packed.bag, items, "larou", 1);
            InventoryRules.Add(packed.bag, items, "wheat-flour", 1);
            for (var i = 2; i < InventoryRules.SlotCount; i++)
                packed.bag[i] = new ItemStack("caoyao", 9);
            check("满包时确实没有余位", !Crafting.HasRoomFor(packed.bag, items, "rou-fu", 1));
            check("包满做不了肉脯",
                Crafting.Craft(packed, items, recipe, never, out notice) == 0 &&
                notice == Crafting.BagFullNotice, notice);
            check("包满时材料一件没扣（先拦后扣）",
                InventoryRules.Count(packed.bag, "larou") == 1 &&
                InventoryRules.Count(packed.bag, "wheat-flour") == 1);
        }

        // ---------------- 7：悬壶炼药 ----------------

        static void CheckAlchemy(ItemTable items, RecipeTable recipes, CheckFn check)
        {
            var recipe = recipes.Find("recipe-jinchuang-san");
            check("炼药配方已登记", recipe != null);
            check("金疮散属炼药手艺", recipe.CraftType == Crafting.Alchemy);
            check("金疮散要 2 份止血草", recipe.Ingredients.Any(i => i.ItemId == "caoyao" && i.Count == 2));
            check("金疮散要 1 份甘草", recipe.Ingredients.Any(i => i.ItemId == "licorice" && i.Count == 1));

            var never = new FixedRandom(.999);
            var always = new FixedRandom(0d);
            var save = NewSave();
            InventoryRules.Add(save.bag, items, "caoyao", 1);
            check("止血草不够 2 份时拦住",
                Crafting.Craft(save, items, recipe, never, out var notice) == 0 && notice.Contains("材料不够"));
            InventoryRules.Add(save.bag, items, "caoyao", 1);
            InventoryRules.Add(save.bag, items, "licorice", 1);
            check("凑齐后产出 1 份金疮散",
                Crafting.Craft(save, items, recipe, never, out notice) == 1, notice);
            check("止血草扣了 2 份", InventoryRules.Count(save.bag, "caoyao") == 0);
            check("甘草扣了 1 份", InventoryRules.Count(save.bag, "licorice") == 0);
            check("金疮散已入包", InventoryRules.Count(save.bag, "jinchuang-san") == 1);

            var bright = NewSave();
            InventoryRules.Add(bright.bag, items, "caoyao", 4);
            InventoryRules.Add(bright.bag, items, "licorice", 2);
            bright.traits = new[] { Crafting.AlchemyTrait };
            check("早慧提高双倍几率",
                Crafting.DoubleChanceFor(recipe, bright) > recipe.DoubleChance,
                Crafting.DoubleChanceFor(recipe, bright).ToString("F2"));
            check("早慧命途一次出 2 份",
                Crafting.Craft(bright, items, recipe, always, out notice) == 2, notice);
            check("双倍后材料照扣不误", InventoryRules.Count(bright.bag, "caoyao") == 2 &&
                InventoryRules.Count(bright.bag, "licorice") == 1);
        }

        // ---------------- 8：物价单一真源（D-05 / ADR-032） ----------------

        // 上一轮这里比的是"两套模型差多少"（218 vs 247）。架构师裁决平滑曲线作废之后，
        // 该断言的口径必须跟着换成"只有一套"——留着比差值的断言，等于给死代码留户口。
        static void CheckPrices(string configDir, ItemTable items, CheckFn check)
        {
            var sword = items.Find("tiejian");
            var food = items.Find("ganliang");

            check("声望 0 时原价买入", Trade.BuyPrice(sword, 0) == sword.Price,
                Trade.BuyPrice(sword, 0) + " 文");
            check("声望 0 时半价卖出", Trade.SellPrice(sword, 0) == sword.Price / 2,
                Trade.SellPrice(sword, 0) + " 文");
            // 阶梯口径：TierStep=20、每档 3%，满档只让 15%。TradeChecks:108-110 钉的是同一套数。
            check("满声望买入按 Ceiling 让到 85 折",
                Trade.BuyPrice(sword, 100) == (int)Math.Ceiling(sword.Price * .85f),
                Trade.BuyPrice(sword, 100) + " 文");
            check("满声望卖出按 Floor 收到 57.5%",
                Trade.SellPrice(sword, 100) == (int)Math.Floor(sword.Price * .5 * 1.15f),
                Trade.SellPrice(sword, 100) + " 文");
            check("声望阶梯每 20 分一跳", Reputation.Tiers(39) == 1 && Reputation.Tiers(40) == 2);
            check("满档让利封顶 15%", Math.Abs(Reputation.Discount(100) - .15f) < 1e-5f,
                Reputation.Discount(100).ToString("F4"));
            check("超出 100 的声望不再多让", Reputation.Discount(999) == Reputation.Discount(100));
            check("负声望按 0 计", Reputation.Discount(-30) == 0f);

            var monotone = true;
            for (var reputation = 10; reputation <= 100; reputation += 10)
            {
                if (Trade.BuyPrice(sword, reputation) > Trade.BuyPrice(sword, reputation - 10)) monotone = false;
                if (Trade.SellPrice(sword, reputation) < Trade.SellPrice(sword, reputation - 10)) monotone = false;
            }
            check("买价随声望不升、卖价随声望不降", monotone);
            check("便宜货折完不低于原价（4 文干粮 ×0.85 向上取整仍是 4）",
                Trade.BuyPrice(food, 100) == Trade.BuyPrice(food, 0) && Trade.BuyPrice(food, 0) > 0);

            // 黑市是"地点属性"，不是折扣：倍率只此一处口径，两侧必须逐字相等。
            check("黑市倍率两侧同值",
                Math.Abs(EconomyRules.BlackMarketRate - Trade.BlackMarketMultiplier) < 1e-6f,
                EconomyRules.BlackMarketRate + " vs " + Trade.BlackMarketMultiplier);
            check("黑市价按 140% 向上取整",
                Trade.BlackPrice(sword) == (int)Math.Ceiling(sword.Price * 1.4f), Trade.BlackPrice(sword) + " 文");
            check("黑市贵过满声望白市价", Trade.BlackPrice(sword) > Trade.BuyPrice(sword, 100));

            // 铜钱钳制（保留在 EconomyRules，因为它是"钱包"不是"价格"）。
            check("铜钱加上限封顶", EconomyRules.AddCoins(SaveData.MaxCoins, 500) == SaveData.MaxCoins);
            check("铜钱花不成负数", EconomyRules.SpendCoins(10, 999) == 0);
            check("加钱不吃负数", EconomyRules.AddCoins(10, -50) == 10);

            // 单一真源的结构性证据：定价判据里只该有 Core 那一套。
            var rulesSource = ReadAssetSource(configDir, "Scripts", "Economy", "EconomyRules.cs");
            check("EconomyRules 源码读到了", rulesSource.Length > 0);
            check("线性买卖曲线已按 D-05 删除（不再有曲线声明）",
                !rulesSource.Contains("float BuyDiscountRate") && !rulesSource.Contains("float SellRate"),
                "仍有声明就说明两套曲线又活了；注释里提名字不算");
            check("EconomyRules 里不再有买卖价函数",
                !rulesSource.Contains("static int BuyPrice") && !rulesSource.Contains("static int SellPrice"));
        }

        /// <summary>从配置目录反推 Assets 根，再按尾段拼源码路径。
        /// Unity 侧 configDir = Application.dataPath/Resources/Config，控制台侧同形，两条路都能定位。</summary>
        static string ReadAssetSource(string configDir, params string[] tail)
        {
            var parts = new List<string> { Path.GetFullPath(Path.Combine(configDir, "..", "..")) };
            parts.AddRange(tail);
            var path = Path.Combine(parts.ToArray());
            return File.Exists(path) ? File.ReadAllText(path) : "";
        }

        // ---------------- 9：黑市命途门控 ----------------

        static void CheckBlackMarket(ItemTable items, CheckFn check)
        {
            var meek = NewSave();
            meek.destiny = "anle";
            var fierce = NewSave();
            fierce.destiny = "shaxing";
            check("煞气之物对普通命途拒卖", !EconomyRules.CanBuy("fulu", meek));
            check("拒卖时货郎有话说",
                EconomyRules.RefusalFor("fulu", meek) == EconomyRules.FateGatedRefusal);
            check("压得住的命途照卖", EconomyRules.CanBuy("fulu", fierce));
            check("卖得掉时不啰嗦", EconomyRules.RefusalFor("fulu", fierce) == "");
            check("寻常货不受命途门控", EconomyRules.CanBuy("cudao", meek));
        }

        // ---------------- 10：气运善行簿 ----------------

        // 迁移对账快照（T096 要求）：这 25 行就是迁走之前 `KarmaTracker.cs:103-135` 里
        // 逐字硬编码的 id|kind|name|rule。清单一旦与它不等，当场红——
        // 迁移当天最怕的不是格式变，是"顺手少带一条"，而少带的那条永远不会有人想起来。
        static readonly string[] LegacyKarmaSnapshot =
        {
            "herb-field-fire|deed|药田失火，抢出药苗|不是放火的人点的就算，救多少不重要",
            "river-rescue|deed|河里有人喊救命，你跳下去了|会水不会水都算，淹死了也算",
            "night-fever-child|deed|邻家孩子夜热，你去了|去了，并且没先谈价钱",
            "bury-the-starved|deed|门口饿死的人，你收了|报了、埋了、或者端了一碗饭过去",
            "take-corvee|deed|替人顶了今年的徭役|顶满三十天算，替着替着跑了不算",
            "keep-a-secret|deed|替人把漏嘴的话瞒到底|一直瞒到事情过去才算；后来要挟了就不算",
            "shield-village-tax|deed|量田时替全村把数目顶回去|要付出代价才算；默默认了不算",
            "vouch-for-deserter|deed|替将被打成逃户的汉子作保|保下来算，保不住也算",
            "copy-primer|deed|替村塾先生抄完半部《千字文》|抄完算，中途换钱走不算",
            "teach-children|deed|教几个孩子认字|教到有人能替你念出第一段才算",
            "pass-on-the-craft|deed|把一门手艺教给没人要的学徒|教满一年算",
            "bury-the-unclaimed|deed|收殓无人认领的尸体|埋了算",
            "raise-a-stone|deed|给无名死者凑一块碑|立了算",
            "guard-the-grave|deed|守一夜，别让野狗刨了坟|守到天亮算",
            "carry-back-herbs|deed|把摔伤的人和药一起背回来|人没背回来、药背回来了也算",
            "night-courier|deed|半夜替人送信去被烧过的邻村|去了算",
            "find-the-lost-child|deed|替逃荒的妇人找走丢的孩子|找到了算；天亮还没找到也算",
            "cliff-herb-fall|chance|采药坠崖|下去看了，把带上来",
            "night-road-ambush|chance|荒野夜路|帮他挡了三招，不问赏钱",
            "teahouse-tale|chance|茶棚听书|听完他那段十二楼的段子",
            "river-iron-box|chance|河里摸到铁盒|摸上来，锈死了，没当场砸开",
            "after-the-beast|chance|妖兽之后|声望过门槛之后",
            "the-watchdog|chance|看门的狗|跟过去了，捡起那半块有字的碎瓷",
            "broken-carrying-pole|chance|半山腰的扁担|滚下去之后，你没先跑",
            "the-dying-beast|chance|将死的妖|你没补那一刀，还得给它收尸",
        };

        static void CheckKarma(string configDir, KarmaTable karma, CheckFn check)
        {
            // —— 清单数据化（T096）——
            var karmaFile = Path.Combine(configDir, KarmaTable.FileName);
            check("气运清单已从代码迁到 Resources/Config/karma.json", File.Exists(karmaFile), karmaFile);
            check("气运清单已被 Unity 导入（.meta 在库）", File.Exists(karmaFile + ".meta"));
            var fromConfig = karma.Entries
                .Select(d => d.Id + "|" + d.Kind + "|" + d.Name + "|" + d.Rule)
                .ToList();
            check("25 条善事奇遇与迁移前的硬编码数组逐字相同（四字段全等）",
                fromConfig.Count == LegacyKarmaSnapshot.Length &&
                !LegacyKarmaSnapshot.Where((line, i) => line != fromConfig[i]).Any(),
                fromConfig.Count + " 条 / 快照 " + LegacyKarmaSnapshot.Length + " 条");
            check("善事 17 件（大纲 0.3 实数，任务书写 16 是错的）",
                karma.Deeds.Count == 17, karma.Deeds.Count + " 件");
            check("奇遇八处", karma.Chances.Count == 8, karma.Chances.Count + " 处");
            check("每条都归 deed 或 chance 一类",
                karma.Entries.All(d => d.Kind == KarmaTracker.Deed || d.Kind == KarmaTracker.Chance));
            check("每条都有判定文案（写代码时照这条守）",
                karma.Entries.All(d => !string.IsNullOrWhiteSpace(d.Rule)));
            var badKind = "{\"karma\":[{\"id\":\"x\",\"kind\":\"karma\",\"name\":\"甲\",\"rule\":\"乙\"}]}";
            check("类型写错的气运条目解析期即拒", ThrowsFormat(() => KarmaTable.Parse(badKind)));
            var dup = "{\"karma\":[{\"id\":\"x\",\"kind\":\"deed\",\"name\":\"甲\",\"rule\":\"乙\"}," +
                      "{\"id\":\"x\",\"kind\":\"deed\",\"name\":\"丙\",\"rule\":\"丁\"}]}";
            check("气运条目 id 重复即拒", ThrowsFormat(() => KarmaTable.Parse(dup)));
            var missing = "{\"karma\":[{\"id\":\"x\",\"kind\":\"deed\",\"name\":\"甲\"}]}";
            check("缺 rule 的条目即拒（不许留空判据）", ThrowsFormat(() => KarmaTable.Parse(missing)));

            // —— 记账规则 ——
            var save = NewSave();
            var when = new WorldTime { day = 1, minuteOfDay = 600 };
            check("表外 id 不记账", !KarmaTracker.Record(save, karma, "made-up-deed", when));
            check("空 id 不记账", !KarmaTracker.Record(save, karma, "", when));
            check("没交表就记不了账（Record 要求显式表，T096）",
                !KarmaTracker.Record(save, null, "river-rescue", when));
            check("账簿开张时是空的", save.karmaEvents.Length == 0);
            check("第一次做善事记下了", KarmaTracker.Record(save, karma, "river-rescue", when));
            check("再做一次只累加次数", !KarmaTracker.Record(save, karma, "river-rescue", when));
            check("次数累到 2", KarmaTracker.TimesOf(save, "river-rescue") == 2);
            check("同一件事只占一条", save.karmaEvents.Length == 1);
            check("善事归善事类", KarmaTracker.DeedCount(save) == 1);

            check("第一次满分", Math.Abs(KarmaTracker.WeightOf(1) - 1f) < 1e-6);
            check("第二次加得少", KarmaTracker.WeightOf(2) < 1f && KarmaTracker.WeightOf(2) > 0f);
            check("收益逐次递减", KarmaTracker.WeightOf(1) > KarmaTracker.WeightOf(2) &&
                KarmaTracker.WeightOf(2) > KarmaTracker.WeightOf(3) &&
                KarmaTracker.WeightOf(3) > KarmaTracker.WeightOf(4));
            check("第五次起不再给", KarmaTracker.WeightOf(5) == 0f && KarmaTracker.WeightOf(9) == 0f);

            var boons = NewSave();
            check("一件不引机缘", !KarmaTracker.HasFortuneBoons(boons));
            KarmaTracker.Record(boons, karma, "river-rescue", when);
            KarmaTracker.Record(boons, karma, "bury-the-unclaimed", when);
            check("两件仍不引机缘", !KarmaTracker.HasFortuneBoons(boons));
            KarmaTracker.Record(boons, karma, "cliff-herb-fall", when);
            check("三件引动隐藏机缘", KarmaTracker.HasFortuneBoons(boons));
            check("奇遇另算一类", KarmaTracker.ChanceCount(boons) == 1);
            check("记账总件数按条目算（重复做同一件不推高门槛）",
                KarmaTracker.TotalCount(boons) == 3);
            check("做过的查得出来", KarmaTracker.HasDone(boons, "river-rescue"));
            check("机缘门槛是三件（对白层按 karma>= 消费）", KarmaTracker.BoonThreshold == 3);

            // 落盘：Migrate 必须可反复调用，且反复调用不能把已记的事清掉。
            SaveData.Migrate(boons);
            SaveData.Migrate(boons);
            check("迁移后账簿原样还在", boons.karmaEvents.Length == 3 &&
                KarmaTracker.HasFortuneBoons(boons), boons.karmaEvents.Length + " 条");
        }

        static bool ThrowsFormat(Func<object> action)
        {
            try { action(); return false; }
            catch (FormatException) { return true; }
        }

        // ---------------- 11：浮签文字层（Tips 数据绑定） ----------------

        // 任务书 L7.5 点名要验"装备耐久展示、信物不可丢弃属性"。早前这两条只存在于界面代码里，
        // 于是套件里一条都没覆盖——文字层挪进 Economy 之后，才有地方钉它。
        static void CheckTooltip(ItemTable items, CheckFn check)
        {
            var sword = items.Find("tiejian");
            var cake = items.Find("ganliang");
            var herb = items.Find("caoyao");
            var box = items.Find("black-box-relic");
            var fulu = items.Find("fulu");

            var swordTip = ItemInspector.Body(items, sword, 1, 0);
            var cakeTip = ItemInspector.Body(items, cake, 3, 0);
            check("浮签为兵器写出耐久行", swordTip.Contains(ItemInspector.DurabilityPending));
            check("浮签不给吃食编耐久", !cakeTip.Contains("耐久"));
            check("浮签为信物标注不可丢弃",
                ItemInspector.Body(items, box, 1, 0).Contains(ItemInspector.RelicNotice));
            check("浮签不冤枉寻常吃食为信物", !cakeTip.Contains(ItemInspector.RelicNotice));
            check("煞气门控之物有命途共鸣行",
                ItemInspector.Body(items, fulu, 1, 0).Contains(ItemInspector.FateResonance));
            check("寻常货不蹭命途共鸣", !cakeTip.Contains(ItemInspector.FateResonance));
            check("数量行带上限", ItemInspector.Body(items, herb, 3, 0).Contains("3 / " + herb.Stack));
            check("不可堆叠之物不写分母", !swordTip.Contains(" / "));
            check("零价信物不报价", ItemInspector.Body(items, box, 1, 0).IndexOf("市价", StringComparison.Ordinal) < 0);
            check("品阶色随品阶而变", ItemInspector.TierColor(items, box) != ItemInspector.TierColor(items, cake));
            check("品阶名取得到", ItemInspector.TierName(items, box) == "天阶");

            // 报价必须与成交同源（ADR-032 的两条护栏之一）。
            check("浮签买入价即货郎实收价", ItemInspector.BuyPrice(sword, 60) == Trade.BuyPrice(sword, 60));
            check("浮签转手价即货郎实付价", ItemInspector.SellPrice(sword, 60) == Trade.SellPrice(sword, 60));
            check("浮签里的数真出现在文案里",
                cakeTip.Contains(ItemInspector.SellPrice(cake, 0) + " 文"), cakeTip);
            // 护栏之二：成交价只随声望与黑市资格变化——交情、气运、当日时辰都不许动它（D-17）。
            var plain = NewSave();
            var influenced = NewSave();
            influenced.localReputation = plain.localReputation;
            influenced.destiny = "shaxing";
            influenced.karmaEvents = new[]
            {
                new KarmaEntry { eventId = "river-rescue", kind = "deed", times = 3 },
                new KarmaEntry { eventId = "take-corvee", kind = "deed", times = 1 },
                new KarmaEntry { eventId = "teahouse-tale", kind = "chance", times = 1 },
            };
            check("交情/气运/时辰都不改买入价",
                Trade.BuyPrice(sword, influenced.localReputation) == Trade.BuyPrice(sword, plain.localReputation));
            check("交情/气运/时辰都不改卖出价",
                Trade.SellPrice(sword, influenced.localReputation) == Trade.SellPrice(sword, plain.localReputation));
            check("气运够也不打折：账簿只引机缘，不参与定价",
                !KarmaTracker.HasFortuneBoons(plain) && KarmaTracker.HasFortuneBoons(influenced) &&
                ItemInspector.BuyPrice(sword, influenced.localReputation) ==
                ItemInspector.BuyPrice(sword, plain.localReputation));
        }

        // ---------------- 12：丢弃防手滑（ADR-029） ----------------

        static void CheckDiscardGuard(ItemTable items, CheckFn check)
        {
            var guard = new DiscardGuard();
            var bag = InventoryRules.NewBag();
            bag[2] = new ItemStack("ganliang", 3);
            bag[7] = new ItemStack("ganliang", 2);

            check("第一次点击只警告不撒手",
                !guard.TryDiscard(bag, items, 2, out var notice) &&
                InventoryRules.Count(bag, "ganliang") == 5);
            check("警告话术给出第二次点击的指引", notice.Contains("再点一次"), notice);
            check("记住待确认的格位", guard.ArmedSlot == 2);
            check("第二次点击才真丢掉",
                guard.TryDiscard(bag, items, 2, out notice) && InventoryRules.Count(bag, "ganliang") == 4);
            check("精确扣选中那一格，不动尾堆", bag[2].count == 2 && bag[7].count == 2);
            check("确认态一次即作废", guard.ArmedSlot == -1);

            guard.TryDiscard(bag, items, 7, out _);
            check("换了格子先警告", guard.ArmedSlot == 7 && bag[7].count == 2);
            guard.TryDiscard(bag, items, 2, out _);
            check("中途改主意就重新攒确认", guard.ArmedSlot == 2 && bag[2].count == 2);

            bag[0] = new ItemStack("black-box-relic", 1);
            check("信物永不撒手", !guard.TryDiscard(bag, items, 0, out notice) && bag[0].count == 1);
            check("信物是被直接拒的，不给\"再确认也丢不了\"的假承诺",
                notice.Contains("丢不得") && !notice.Contains("再点一次"), notice);
            check("信物不进入待确认态", guard.ArmedSlot == -1);

            bag[3] = new ItemStack("mystery-stone", 1);
            check("来历不明之物同样防手滑",
                !guard.TryDiscard(bag, items, 3, out notice) && notice.Contains("未登记之物"), notice);
            check("空格只说一句空的",
                !guard.TryDiscard(bag, items, 23, out notice) && notice == DiscardGuard.EmptySlotNotice);
            check("没选中时不越界",
                !guard.TryDiscard(bag, items, 99, out notice) && notice == DiscardGuard.NeedSelectionNotice);
            check("空背包不炸", !guard.TryDiscard(null, items, 0, out _));

            var last = InventoryRules.NewBag();
            last[5] = new ItemStack("ganliang", 1);
            guard.Reset();
            guard.TryDiscard(last, items, 5, out _);
            check("丢掉最后一件后该格归空",
                guard.TryDiscard(last, items, 5, out notice) && last[5].IsEmpty);
        }

        // ---------------- 13：接线可达性 ----------------
        //
        // 这一节是本轮验收补上的：早前 166 条全绿，测的却都是"纯函数自己对不对"，
        // 没有一条问过"运行期到底有没有人来调它"。于是 recipes.json 躺在盘上没人加载、
        // 气运簿立了账却无人记账，门禁照样一声不响。判据要能抓住"配置写了、玩家碰不到"。
        static void CheckReachability(string configDir, CheckFn check)
        {
            var recipeFile = Path.Combine(configDir, RecipeTable.FileName);
            check("配方表就在 Resources/Config 下", File.Exists(recipeFile), recipeFile);
            // 没有 .meta 的 Resources 资源不会进包：Resources.Load 只会拿到 null。
            check("配方表已被 Unity 导入（.meta 在库）", File.Exists(recipeFile + ".meta"));
            check("物品表已被 Unity 导入（.meta 在库）",
                File.Exists(Path.Combine(configDir, "items.json") + ".meta"));
            check("配方表的运行期路径常量与文件位置一致",
                RecipeTable.ResourcePath == "Config/" + Path.GetFileNameWithoutExtension(RecipeTable.FileName));

            var viewPath = Path.GetFullPath(Path.Combine(configDir, "..", "..",
                "Scripts", "Presentation", "FrontEndInventory.cs"));
            var view = File.Exists(viewPath) ? File.ReadAllText(viewPath) : "";
            check("行囊界面源码在预期的树位置", view.Length > 0, viewPath);
            check("行囊界面按同一常量去加载配方表", view.Contains("RecipeTable.ResourcePath"));
            check("行囊界面真的驱动手艺层", view.Contains("Crafting.Craft("));
            check("行囊界面把丢弃交给防手滑守护", view.Contains("DiscardGuard"));
            check("行囊界面的浮签出自同一文字层", view.Contains("ItemInspector.Body("));
            // 双源回归护栏：界面一旦又开始自己算价，报出来的就是成交里不存在的数字。
            check("行囊界面不再自行算买卖价",
                !view.Contains("EconomyRules.BuyPrice") && !view.Contains("EconomyRules.SellPrice"));

            // 交易页两栏的行名必须分开：左右都叫 "Shelf row N" 时，按名字取控件的验收
            // 会点到对方那一栏去——本轮"点货架首行"其实点的是玩家行囊里一件他不收的货，
            // 于是两条真点击断言在纯 HEAD 就红着（详见 o-life-loop-report.md 的归因表）。
            var tradeView = ReadAssetSource(configDir, "Scripts", "Presentation", "FrontEndTrade.cs");
            check("交易页货架与行囊的行分属两个名字空间",
                tradeView.Contains("\"Shelf row\",") && tradeView.Contains("\"Sack row\","),
                "MakeRow 的两个调用点应分别传入 Shelf row / Sack row");
            check("行名由调用点决定，不在造行函数里写死一栏的名字",
                !tradeView.Contains("\"Shelf row \" + order"));

            // 尚未接线的部分如实留话，别让它装作已完成：
            // 大纲 0.3 的善事要在世界里被"看见"，得由对白/事件线（员工 J/E 域）在判定点
            // 调 KarmaTracker.Record——L 域无权触碰那些文件，缺口登记在 11_技术债务记录.md。
            // 气运的落盘与读取侧本轮已验证（上一节 + Unity 侧 JsonUtility 往返）。
            check("落盘责任写在明面上（KarmaPersistenceNotice 存在，T097）",
                ReadAssetSource(configDir, "Scripts", "Economy", "KarmaTracker.cs")
                    .Contains("KarmaPersistenceNotice"));
            check("没人调的公开 API 已按审计第 7 条删除（RareDropRateBonus 不再存在）",
                !ReadAssetSource(configDir, "Scripts", "Economy", "KarmaTracker.cs").Contains("RareDropRateBonus"));
            check("气运清单已不再硬编码在 C# 里（T096：源码里不该再出现善事 id 字面量）",
                !ReadAssetSource(configDir, "Scripts", "Economy", "KarmaTracker.cs").Contains("\"river-rescue\""));
            // 已知缺口（W-A 第 2 点）：SaveRepository.IsValid 目前不认 karmaEvents，架构师在 schema 11 批次补，
            // 本线全域禁写那两个文件（D-12）。这里不再对源码关键字做"必须没有"的断言——
            // 那种断言会在缺口被补上的当天把共享门禁拖红；真正的核对放在控制台侧的写入→读回往返。
            check("气运账簿的存储字段在新档里默认就位",
                new SaveData().karmaEvents != null && new SaveData().karmaEvents.Length == 0);
        }

        // ---------------- 14：行囊不假装回血（D-09 / O6） ----------------

        static void CheckUseHonesty(string configDir, ItemTable items, CheckFn check)
        {
            var food = items.Find("ganliang");
            var med = items.Find("jinchuangyao");
            var san = items.Find("jinchuang-san");
            var relic = items.Find("black-box-relic");
            var blade = items.Find("tiejian");

            check("丹药在行囊里被拒并指向归属（战斗页服药）",
                BagUseRules.RefusalFor(med) == BagUseRules.MedicineRefusal.Replace("{0}", med.Name),
                BagUseRules.RefusalFor(med));
            check("新炼的金疮散同样不在行囊里用", BagUseRules.RefusalFor(san).Contains("山野对敌时服用"));
            check("吃食被拒时如实说「效果尚未接入」", BagUseRules.RefusalFor(food).Contains("尚未接入"),
                BagUseRules.RefusalFor(food));
            check("剧情物品照旧是「往后有大用」", BagUseRules.RefusalFor(relic).Contains("往后有大用"));
            check("兵器不是入口的东西", BagUseRules.RefusalFor(blade).Contains("不是入口的东西"));
            check("来历不明之物不崩", BagUseRules.RefusalFor(null).Contains("来历不明"));
            // 本轮最硬的一条：任何入口物都不许被行囊「用掉」。
            check("本轮行囊不使用任何入口物（不扣数量、不写 buff）",
                !BagUseRules.Consumes(food) && !BagUseRules.Consumes(med) && !BagUseRules.Consumes(san));
            check("丹药浮签报出战斗回复量（T070 落字段后不再是一根横线）",
                ItemInspector.Body(items, med, 2, 0).Contains("战斗内回复 25 点气血"),
                ItemInspector.Body(items, med, 2, 0));
            check("吃食浮签仍写未接入（吃食没有战斗档，行囊里吃下去什么都不会发生）",
                ItemInspector.Body(items, food, 3, 0).Contains(ItemInspector.EffectPending));
            check("兵器浮签不蹭食效一行", !ItemInspector.Body(items, blade, 1, 0).Contains("食效"));

            // ===== T070：战斗回复量落表（2号 的战斗页服药链等的就是这一个字段）=====
            check("两件丹药的战斗回复量已落表（金疮药 25 / 凝血金疮散 45）",
                med.CombatHeal == 25 && san.CombatHeal == 45,
                med.CombatHeal + " / " + san.CombatHeal);
            // 浮签上的数字必须出自表，不是界面自带——这是"浮签数字 == 战斗结算数字"的前半段，
            // 后半段在 CombatEngineShot.SuitePills（读方在 CombatConsumables）。
            var rawItems = File.ReadAllText(Path.Combine(configDir, "items.json"));
            check("浮签上的数字出自 items.json 原文（界面不自己带一个 25）",
                rawItems.Contains("\"combatHeal\": 25") && rawItems.Contains("\"combatHeal\": 45") &&
                ItemInspector.Body(items, med, 2, 0).Contains("25 点气血") &&
                ItemInspector.Body(items, san, 1, 0).Contains("45 点气血"));
            // 读方只认丹药类（CombatConsumables.UsableCategories = { yaodan }）。
            // 别的类别上写 combatHeal 会被静默忽略——那正是"表里写了、玩家看不见"的双源形态。
            var offCategory = items.Items
                .Where(d => d.CombatHeal > 0 && d.Category != ItemCategoryNames.Medicine)
                .Select(d => d.Id + "(" + d.Category + ")").ToList();
            check("combatHeal 只允许出现在丹药类（写错类别就是静默失效）", offCategory.Count == 0,
                string.Join(" · ", offCategory));

            const string onePill = "{\"tiers\":[{\"id\":\"fanpin\",\"name\":\"凡品\",\"color\":\"#8C8C86\"}]," +
                "\"categories\":[{\"id\":\"yaodan\",\"name\":\"丹药\"}]," +
                "\"items\":[{\"id\":\"p\",\"name\":\"土方金疮药\",\"tier\":\"fanpin\",\"category\":\"yaodan\"," +
                "\"icon\":\"elixir-bottle\",\"stack\":5,\"price\":14,\"description\":\"止血生肌的土方药。\"}]}";
            var bareTable = ItemTable.Parse(onePill);
            var bare = bareTable.Find("p");
            check("表里没写数字的丹药仍报「—」（绝不替物品表编一个数）",
                bare.CombatHeal == 0 &&
                ItemInspector.Body(bareTable, bare, 1, 0).Contains(ItemInspector.EffectPending));
            check("负数战斗回复量解析期即拒", ThrowsFormat(() => ItemTable.Parse(
                onePill.Replace("\"price\":14", "\"price\":14,\"combatHeal\":-5"))));

            var view = ReadAssetSource(configDir, "Scripts", "Presentation", "FrontEndInventory.cs");
            check("界面把使用裁决交给 BagUseRules", view.Contains("BagUseRules.RefusalFor"));
            // 拒绝的话必须是个可核对的输出：只看数量不变分不清"被拒了"和"吃了但没生效"。
            check("界面把拒绝的那句话留成可断言的口子", view.Contains("LastBagUseRefusal"));
            // 消耗路径必须整段消失：留着 Remove 就是还在偷偷吃掉玩家的东西。
            check("行囊不再自行扣减入口物数量", !view.Contains("InventoryRules.Remove(userBag"),
                "仍在界面里 Remove 说明消耗没停");
            // The former two-recipe placeholder has become a portable artisan codex.
            // Guard reachable categories and overflow instead of requiring obsolete implementation copy.
            check("百工谱展示炼器炼药分类并可滚动查看完整配方", view.Contains("百工仙谱") &&
                view.Contains("recipes.OfType(craftCategory)") && view.Contains("AddComponent<ScrollRect>") &&
                view.Contains("\"forging\"") && view.Contains("\"alchemy\""));
        }

        // ---------------- 18：手艺熟练度真的会练（T046 / schema 11）----------------
        //
        // ADR-029 批评的原话是"做一百次和第一次一样"。这一节钉的就是：次数进账、
        // 双倍率随次数涨、涨到封顶就停、被拦下的那次不算练过、面板与结算同一个函数。
        // 存次数不存等级——能算出来的东西进存档就是第二真源。
        static void CheckProficiency(string configDir, ItemTable items, RecipeTable recipes, CheckFn check)
        {
            var recipe = recipes.Recipes.FirstOrDefault(r => r.Ingredients != null && r.Ingredients.Count > 0);
            if (recipe == null) { check("配方表里有可做的方子（熟练度用例的前提）", false); return; }
            var baseChance = Crafting.DoubleChanceFor(recipe, NewSave());

            var fresh = NewSave();
            check("没做过时熟练度不加成（双倍率等于底数）",
                Math.Abs(Crafting.DoubleChanceFor(recipe, fresh) - baseChance) < 1e-5, baseChance.ToString());
            for (var i = 0; i < 20; i++) CraftLedger.Record(fresh, recipe.Id);
            var practiced = Crafting.DoubleChanceFor(recipe, fresh);
            check("做满 20 次多让十个百分点（熟练度真的在练）",
                Math.Abs(practiced - baseChance - Crafting.ProficiencyCap) < 1e-5,
                baseChance + " → " + practiced);
            CraftLedger.Record(fresh, recipe.Id);
            CraftLedger.Record(fresh, recipe.Id);
            check("封顶之后不再涨（做一百次与做二十次一样，是设计不是漏账）",
                Math.Abs(Crafting.DoubleChanceFor(recipe, fresh) - practiced) < 1e-5,
                "累计 " + CraftLedger.TimesOf(fresh, recipe.Id) + " 次");
            check("熟练度只记次数不记等级（SaveData 里不许出现 proficiency/mastery 字段）",
                typeof(SaveData).GetFields()
                    .All(f => !f.Name.ToLowerInvariant().Contains("proficiency") &&
                              !f.Name.ToLowerInvariant().Contains("mastery")),
                string.Join(",", typeof(SaveData).GetFields().Select(f => f.Name)));

            string notice;
            var broke = NewSave();
            var made = Crafting.Craft(broke, items, recipe, new Random(0), out notice);
            check("材料不够被拦下：一件没做出来、熟练度也不涨",
                made == 0 && CraftLedger.TimesOf(broke, recipe.Id) == 0, notice);

            var worker = NewSave();
            foreach (var line in recipe.Ingredients)
                InventoryRules.Add(worker.bag, items, line.ItemId, line.Count * 6);
            made = Crafting.Craft(worker, items, recipe, new Random(1), out notice);
            check("真做出来一次就记一笔", made > 0 && CraftLedger.TimesOf(worker, recipe.Id) == 1, notice);
            made = Crafting.Craft(worker, items, recipe, new Random(2), out notice);
            check("再做一次是同一行加次数，不是多占一行",
                made > 0 && CraftLedger.TimesOf(worker, recipe.Id) == 2 && worker.crafts.Length == 1,
                worker.crafts.Length + " 行");

            var view = ReadAssetSource(configDir, "Scripts", "Presentation", "FrontEndInventory.cs");
            check("手艺面板的双倍率与次数都取自同一处（界面不自己写死数字）",
                view.Contains("Crafting.DoubleChanceFor(recipe, activeSave)") &&
                view.Contains("CraftLedger.TimesOf(activeSave, recipe.Id)"));
        }


        // ---------------- 19：给"夹具前提过期"留一份程序输出的证据（架构师点名要）----------------
        //
        // FrontEndShot.cs 有三条断言钉的是 O 线**按裁决改完**之前的世界（D-09 只拒不扣、
        // T042 铺来源后的货架行数）。那个文件本轮归开发者1号，我不改它；这里把"现在的世界
        // 到底是什么样"用程序算出来打进日志，改哪几行、改成什么值，由这份输出说了算。
        static void CheckStaleFixtureEvidence(string configDir, ItemTable items, CheckFn check)
        {
            var merchants = MerchantTable.Parse(File.ReadAllText(Path.Combine(configDir, "merchants.json")));
            var save = NewSave();
            var report = new List<string>();
            foreach (var m in merchants.Merchants)
            {
                var state = MerchantLedger.Of(save, m);
                var rows = m.Stock
                    .Where(l => items.Find(l.itemId) != null &&
                                MerchantLedger.Left(state, state.stock, l.itemId) > 0)
                    .Select(l => items.Find(l.itemId).Name).ToList();
                report.Add(m.Id + " 白日货架 " + rows.Count + " 行（" + string.Join("、", rows) + "）");
            }
            check("白日货架行数由商品表算出（夹具钉死 7 的前提已过期，见 FrontEndShot.cs:1202/1313）",
                report.Count > 0, string.Join(" ｜ ", report));

            var food = items.Find("ganliang");
            var med = items.Find("jinchuangyao");
            check("吃食被拒时界面说的话（FrontEndShot.cs:1114 该断言的替换值）",
                BagUseRules.RefusalFor(food).Contains("尚未接入") && !BagUseRules.Consumes(food),
                BagUseRules.RefusalFor(food));
            check("丹药被拒时界面说的话（同一条替换值的丹药半边）",
                BagUseRules.RefusalFor(med).Contains("山野对敌时服用") && !BagUseRules.Consumes(med),
                BagUseRules.RefusalFor(med));
        }

        // ---------------- 15：每件东西都得有来路（T042 / D-10 / O2） ----------------
        //
        // 「能力存在 ≠ 功能存在」的最后一块：物品表写得再全，没人卖、没人掉、没人发，
        // 玩家手里就永远不会有它——上一轮两道方子就是这么绿着的。
        // 来源口径：商人货架（含黑市）∪ 敌人掉落 ∪ 任务奖励 ∪ 开局行囊 ∪ 方子产物。
        // 红线照 O4.4：只统计本域三张表 + 只读 enemies.json；缺项进台账而不是把共享门禁拖红
        // （T040/T044 的教训：别人的半成品不该让所有人停线，但半成品本身必须被写下来）。
        static void CheckItemSources(string configDir, ItemTable items, RecipeTable recipes, CheckFn check)
        {
            var sources = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kit in ItemTable.StartingKit) sources.Add(kit.id);

            var merchantTable = MerchantTable.Parse(Read(configDir, "merchants.json"));
            foreach (var merchant in merchantTable.Merchants)
                foreach (var line in merchant.Stock.Concat(merchant.Hidden)) sources.Add(line.itemId);

            var enemyFile = Path.Combine(configDir, "enemies.json");
            var dropLines = 0;
            if (File.Exists(enemyFile))
                foreach (var enemy in EnemyTable.Parse(File.ReadAllText(enemyFile)).Enemies)
                { dropLines += enemy.Drops.Count; foreach (var drop in enemy.Drops) sources.Add(drop.ItemId); }

            var questFile = Path.Combine(configDir, "quests.json");
            if (File.Exists(questFile))
                foreach (var quest in QuestTable.Parse(File.ReadAllText(questFile)).Quests)
                    foreach (var stack in quest.Rewards?.Items ?? Array.Empty<ItemStack>()) sources.Add(stack.id);

            foreach (var recipe in recipes.Recipes) sources.Add(recipe.ResultItemId);

            var sourceless = items.Items.Where(d => !sources.Contains(d.Id))
                .Select(d => d.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();
            check("敌人掉落只读对表跑到了", dropLines > 0, dropLines + " 条掉落行");
            check("配方产物按「炼得出来」计为有来路",
                sources.Contains("rou-fu") && sources.Contains("jinchuang-san"));
            check("来路缺口台账与登记逐字相同（补一个来源就得同步缩一条）",
                sourceless.Count == KnownSourceless.Length &&
                !KnownSourceless.Where((id, i) => id != sourceless[i]).Any(),
                "实测 " + string.Join(",", sourceless));
            var nonPlot = sourceless
                .Where(id => !PlotItemRules.IsPlotItem(items.Find(id)?.Category))
                .ToList();
            check("非剧情物品的来路缺口必须逐条登记（不许有未登记的空白）",
                nonPlot.Count == PendingSources.Length &&
                !PendingSources.Where((id, i) => id != nonPlot[i]).Any(),
                "实测 " + string.Join(",", nonPlot));

            // 两道方子的材料得买得起：首日可得 = 开局铜钱 + 卷一主链奖励 + 野猪赏钱，全由配置算出。
            var materialCost = new[] { ("larou", 1), ("wheat-flour", 1), ("caoyao", 2), ("licorice", 1) }
                .Sum(pair => (items.Find(pair.Item1)?.Price ?? 0) * pair.Item2);
            var dayOne = ItemTable.StartingCoins + MainChainCoin(configDir);
            check("两道方子的材料总价不超过玩家首日可得", materialCost <= dayOne,
                "材料 " + materialCost + " 文 / 首日可得 " + dayOne + " 文");
            check("首日可得由配置算出而非手写常量", dayOne > ItemTable.StartingCoins, dayOne + " 文");

            var phantom = merchantTable.Merchants.SelectMany(m => m.Stock.Concat(m.Hidden))
                .Select(line => line.itemId).Where(id => items.Find(id) == null).Distinct().ToList();
            check("货单里没有查无此物的 id", phantom.Count == 0, string.Join(",", phantom));
            var phantomBuys = merchantTable.Merchants.SelectMany(m => m.Buys)
                .Where(c => items.FindCategory(c) == null).Distinct().ToList();
            check("收货品类都在物品表里定义", phantomBuys.Count == 0, string.Join(",", phantomBuys));
        }

        /// <summary>来路缺口全台账（id 升序，逐字比对）。O2 补完货单与任务奖励之后，
        /// 这里只剩三件任务物：它们是「用到时才由代码给」的那类（开盒、认印），归剧情线授予，
        /// 逐条说明见 Tools/logs/o-source-audit.md。
        /// 实测已确认 beast-gall / wolf-fur / wolf-fang / mandrill-stone / iron-ore 五件已有来路
        /// ——N 线的 enemies.json 先于本笔入库，这正是 ADR-031 冻结 id 拆掉顺序耦合的效果。</summary>
        static readonly string[] KnownSourceless = { "kaihetie", "yinzhang", "yupei" };

        /// <summary>台账里非剧情的部分：O2 收口后应当是空数组。留着一条就是一条红灯的理由。</summary>
        static readonly string[] PendingSources = Array.Empty<string>();

        /// <summary>卷一主链的推进顺序（链序，不是集合）。</summary>
        static readonly string[] MainChainOrder =
        {
            "tax-and-labor", "night-beast-roar", "beast-origin",
            "guide-investigation", "town-day", "fortune-teller-change", "leave-qingshi"
        };

        // ---------------- 17：气运接线与单一真源的护栏（O3 / O4） ----------------

        /// <summary>卷一主链赏钱 + 野猪赏钱，全部由配置算出，不接受手写常量。</summary>
        static int MainChainCoin(string configDir)
        {
            var coins = 0;
            foreach (var quest in QuestTable.Parse(Read(configDir, "quests.json")).Quests)
                if (MainChainOrder.Contains(quest.Id)) coins += quest.Rewards?.Coins ?? 0;
            coins += EnemyTable.Parse(Read(configDir, "enemies.json")).Enemies
                .Where(e => e.Id == "boar-demon").Select(e => e.Coins).FirstOrDefault();
            return coins;
        }

        // ---------------- 16：卷一生活闭环反向对表（O5） ----------------
        //
        // 这四张表回答同一个问题：**任务说的每一件事，今天这个世界里有没有东西能接住它**。
        // 全部写在本线**今天真在跑**的通道里，而不是挂在 !worldPhase 上的死族（T081）——
        // 判据写在没人执行的文件里，等于没写。
        static void CheckLifeLoopTables(string configDir, ItemTable items, CheckFn check)
        {
            var quests = JsonRoot(configDir, "quests.json");
            var town = JsonRoot(configDir, "town-map.json");
            var npcs = IdSet(JsonRoot(configDir, "npcs.json"), "npcs");
            var clues = IdSet(JsonRoot(configDir, "clues.json"), "clues");
            var encounters = IdSet(town, "encounters");
            var landmarks = IdSet(town, "landmarks");
            var townNpcs = IdSet(town, "npcs");

            // 四张表全靠按 key 取值：key 念错会得到空集合。这条护栏把"我读错了"
            // 与"真的没配"区分开——本节初版就踩过一次（奖励的 itemId 念成 id）。
            check("对表用的五个 id 集合都非空（读错 key 会伪装成内容缺失）",
                npcs.Count >= 5 && clues.Count >= 1 && encounters.Count >= 1 &&
                landmarks.Count >= 1 && townNpcs.Count >= 5,
                "npcs=" + npcs.Count + " clues=" + clues.Count + " encounters=" + encounters.Count +
                " landmarks=" + landmarks.Count + " town.npcs=" + townNpcs.Count);

            // 表 1：任务 → 目标可达
            var seenTypes = new HashSet<string>(StringComparer.Ordinal);
            var dangling = new List<string>();
            var unknownType = new List<string>();
            var noTarget = new List<string>();
            foreach (var quest in quests["quests"].Items)
            {
                var qid = quest["id"].AsString("");
                foreach (var objective in quest["objectives"].Items)
                {
                    var type = objective["type"].AsString("");
                    var target = objective["target"].AsString("");
                    seenTypes.Add(type);
                    if (target.Length == 0)
                    {
                        // 没写 target 的目标只能靠代码按 objective id 点名推进（FrontEndTrade 那种）。
                        // 这不是"链断了"，是"推进没数据化"——单独记账，别和断链混成一类。
                        noTarget.Add(qid + "/" + objective["id"].AsString(""));
                        continue;
                    }
                    bool ok;
                    switch (type)
                    {
                        case "defeat": ok = encounters.Contains(target); break;
                        case "talk": ok = npcs.Contains(target) && townNpcs.Contains(target); break;
                        case "clue": ok = clues.Contains(target); break;
                        case "collect": ok = items.Find(target) != null; break;
                        case "reach": ok = landmarks.Contains(target) || encounters.Contains(target); break;
                        default: unknownType.Add(qid + " 的 objective 类型未登记：" + type); ok = false; break;
                    }
                    if (!ok) dangling.Add(qid + "→" + type + " " + target);
                }
            }
            check("objective 类型全部在登记表内（未知类型当场红，不准静默忽略）",
                unknownType.Count == 0, string.Join(" · ", unknownType));
            check("凡是写了 target 的目标都被世界接住（遭遇/NPC/线索/物品/地标逐条对表）",
                dangling.Count == 0, string.Join(" · ", dangling));
            check("没写 target 的目标逐条点名登记（它们靠代码推进，O9 要数据化的是这批）",
                noTarget.Count == 4 && string.Join(" · ", noTarget.OrderBy(x => x, StringComparer.Ordinal)) ==
                    "beast-origin/compare-the-wound · beast-origin/hear-the-hunter · " +
                    "guide-investigation/weigh-the-testimony · tax-and-labor/answer-summons",
                string.Join(" · ", noTarget.OrderBy(x => x, StringComparer.Ordinal)));
            check("五类目标都在用（clue/collect/defeat/reach/talk）", seenTypes.Count == 5,
                string.Join(",", seenTypes.OrderBy(x => x, StringComparer.Ordinal)));

            // 表 2：任务 → 奖励有效
            var badRewards = new List<string>();
            foreach (var quest in quests["quests"].Items)
            {
                var qid = quest["id"].AsString("");
                var reward = quest["rewards"];
                foreach (var line in reward["items"].Items)
                {
                    // 键是 itemId（本节初版在这里念成过 id，把"自己读错 key"误报成"内容缺失"）
                    var rid = line["itemId"].AsString("");
                    if (items.Find(rid) == null) badRewards.Add(qid + " 奖励了查无此物的 " + rid);
                    if (line["count"].AsInt(1) < 1) badRewards.Add(qid + " 的奖励 " + rid + " 数量小于 1");
                }
                if (reward["coins"].AsInt(0) < 0) badRewards.Add(qid + " 赏钱为负");
            }
            check("任务奖励的物品 id 全部真实存在", badRewards.Count == 0, string.Join(" · ", badRewards));
            var flagClash = quests["quests"].Items
                .SelectMany(q => q["rewards"]["flags"].Items.Select(f => new { q = q["id"].AsString(""), f = f.AsString("") }))
                .Where(x => x.f.StartsWith("bonded:") && !npcs.Contains(x.f.Substring(7)))
                .Select(x => x.q + " 的 bonded: 指向不存在的 NPC").ToList();
            check("配置里的 bonded: 一律指向真实 NPC", flagClash.Count == 0, string.Join(" · ", flagClash));

            // 表 3：对白网连通 + action 合法集
            var nodes = JsonRoot(configDir, "dialogues.json")["nodes"];
            var nodeIds = IdSet(JsonRoot(configDir, "dialogues.json"), "nodes");
            var broken = new List<string>();
            var illegalAction = new List<string>();
            var legal = new HashSet<string>(StringComparer.Ordinal)
                { "", "openShop", "acceptQuest", "turnInQuest", "triggerFateCheck", "close" };
            foreach (var node in nodes.Items)
            {
                var next = node["next"].AsString("");
                if (next.Length > 0 && !nodeIds.Contains(next)) broken.Add(node["id"].AsString("") + " → " + next);
                foreach (var option in node["options"].Items)
                {
                    var on = option["next"].AsString("");
                    if (on.Length > 0 && !nodeIds.Contains(on))
                        broken.Add(node["id"].AsString("") + " 的选项 → " + on);
                    var action = option["action"].AsString("");
                    if (!legal.Contains(action)) illegalAction.Add(node["id"].AsString("") + " 的 action=" + action);
                }
            }
            check("对白网没有断链", broken.Count == 0, string.Join(" · ", broken));
            check("对白 action 只允许登记动作（含显式交还，表外当场红）", illegalAction.Count == 0,
                string.Join(" · ", illegalAction));

            // 表 4：卷一全部任务从零推进（纯逻辑模拟）。注意它证的是**账本逻辑可通**；
            // 运行期还没人调 TryComplete/GrantRewards（T083），这一条在报告里分开写。
            var table = QuestTable.Parse(Read(configDir, "quests.json"));
            var sim = new SaveData { slot = 9, characterId = "farmer", characterName = "模拟" };
            sim.bag = InventoryRules.NewBag();
            var stalled = "";
            var completed = 0;
            // 先主链（按链序），再侧链（机缘链各条独立）——红绳与黑匣挂在患难链上，
            // 只走主链的模拟永远拿不到它们，那正是上一版这条断言红的真因。
            var walk = MainChainOrder.Concat(table.Quests.Select(q => q.Id)
                .Where(id => !MainChainOrder.Contains(id))).ToArray();
            foreach (var id in walk)
            {
                var quest = table.Find(id);
                if (quest == null) { stalled = "任务表里查不到 " + id; break; }
                QuestLedger.RefreshAvailability(sim, table);
                if (!QuestLedger.TryStart(sim, quest)) { stalled = id + " 起不来（前置或状态不对）"; break; }
                foreach (var objective in quest.Objectives)
                    QuestLedger.Advance(sim, quest, objective.Id, objective.Count);
                if (!QuestLedger.ObjectivesMet(sim, quest)) { stalled = id + " 目标推不满"; break; }
                if (!QuestLedger.TryComplete(sim, quest)) { stalled = id + " 结不了算"; break; }
                QuestLedger.GrantRewards(sim, quest, items);
                completed++;
            }
            check("卷一任务能从零全部结算（主链 7 条 + 侧链 3 条）",
                stalled.Length == 0 && completed == table.Quests.Count,
                stalled.Length == 0 ? completed + " / " + table.Quests.Count + " 条完成" : "卡在：" + stalled);
            check("离镇即卷一终点：leave-qingshi 已结算", QuestLedger.IsDone(sim, "leave-qingshi"));
            check("红绳与黑匣随患难链结算入包（O2 给剧情物品开的来路）",
                InventoryRules.Count(sim.bag, "red-string") == 1 &&
                InventoryRules.Count(sim.bag, "black-box-relic") == 1,
                "红绳 " + InventoryRules.Count(sim.bag, "red-string") +
                " / 黑匣 " + InventoryRules.Count(sim.bag, "black-box-relic"));
            check("信物一旦入包就丢不掉、也卖不掉（防手滑与防误售同时成立）",
                !BagService.CanDiscard(items, "red-string") &&
                !Trade.CanSell(items.Find("black-box-relic"), new MerchantDef("inj", "甲", "乙", "丙",
                    new List<MerchantStock>(), new List<MerchantStock>(),
                    new List<string> { "xinwu" }, new List<MerchantMemoryLine>())));
        }

        static void CheckKarmaWiring(string configDir, KarmaTable karma, CheckFn check)
        {
            // 映射表里拼错一个 id，Record 会静默拒收（表外 id 一律不计）——这是最难查的一类错，
            // 因为"没记上"和"这件事本来没发生"在画面上长得一模一样。
            var unmapped = KarmaTriggers.MappedEventIds.Where(id => karma.Find(id) == null).Distinct().ToList();
            check("KarmaTriggers 映射到的每个事件 id 都在 karma.json 里", unmapped.Count == 0,
                string.Join(" · ", unmapped));
            var nodeIds = IdSet(JsonRoot(configDir, "dialogues.json"), "nodes");
            var ghostNodes = KarmaTriggers.DialogueNodeTriggers
                .Where(pair => !nodeIds.Contains(pair.Key)).Select(pair => pair.Key).ToList();
            check("对白触发点挂的都是真实存在的节点", ghostNodes.Count == 0, string.Join(" · ", ghostNodes));
            check("对白触发点不为空（T043 的根因就是没人记账）",
                KarmaTriggers.MappedEventIds.Any());

            // O5.5 的落点：25 条每条要么有生产判定点，要么在去向表里被逐条点名并写原因。
            // 表是文档，但这里把它当门禁对：文档与映射表两边必须严格相等——
            // "写了表没接线"和"接了线没写表"都不许绿着过去（上一轮的虚账就是这么留下的）。
            // configDir 是 <repo>/GameClient/TwelveJade/Assets/Resources/Config：五段 .. 才回到仓库根，
            // Unity 侧（Application.dataPath/Resources/Config）同形，两条路都落在 Tools/logs。
            var ledgerPath = Path.GetFullPath(Path.Combine(configDir,
                "..", "..", "..", "..", "..", "Tools", "logs", "o-karma-ledger.md"));
            var ledger = File.Exists(ledgerPath) ? File.ReadAllText(ledgerPath) : "";
            check("气运去向表读到了（Tools/logs/o-karma-ledger.md）", ledger.Length > 0, ledgerPath);
            var unlisted = karma.Entries.Where(d => !ledger.Contains("`" + d.Id + "`"))
                .Select(d => d.Id).ToList();
            check("去向表逐条点名 25 条，一条不落", unlisted.Count == 0,
                unlisted.Count == 0 ? "25/25" : "缺：" + string.Join(" · ", unlisted));
            var claimed = karma.Entries
                .Where(d => LedgerRow(ledger, d.Id).Contains("已接线")).Select(d => d.Id).ToList();
            var mapped = KarmaTriggers.MappedEventIds.Distinct().ToList();
            check("去向表标「已接线」的集合 == 映射表的集合（两边不许各说各话）",
                claimed.OrderBy(id => id).SequenceEqual(mapped.OrderBy(id => id)),
                "表里：" + string.Join(" · ", claimed) + " || 代码：" + string.Join(" · ", mapped));

            // karma>= 条件键：本轮唯一新增的 DSL 键（O3.3）。
            var none = new SaveData { slot = 1, characterId = "farmer", characterName = "甲" };
            none.bag = InventoryRules.NewBag();
            var three = new SaveData { slot = 1, characterId = "farmer", characterName = "乙" };
            three.bag = InventoryRules.NewBag();
            foreach (var id in new[] { "river-rescue", "keep-a-secret", "teahouse-tale" })
                KarmaTracker.Record(three, karma, id, new WorldTime { day = 1, minuteOfDay = 600 });
            check(" karma>=3 在两件时不开、三件时开",
                !DialogueCondition.Satisfies(none, "npc-shenhuai-an", 0, "karma>=3") &&
                !DialogueCondition.Satisfies(three, "npc-shenhuai-an", 0, "karma>=4") &&
                DialogueCondition.Satisfies(three, "npc-shenhuai-an", 0, "karma>=3"));
            check("表外 condition key 一律判不满足（不许静默当通过）",
                !DialogueCondition.Satisfies(three, "npc-shenhuai-an", 0, "karmaXX>=1") &&
                !DialogueCondition.Satisfies(three, "npc-shenhuai-an", 0, "气运>=1"));
            check("记账条数只有一处口径：Economy 与 Core 数出的必须相同",
                KarmaTracker.TotalCount(three) == KarmaLedger.Count(three) &&
                KarmaLedger.Count(three) == 3, KarmaLedger.Count(three).ToString());

            // 两条收益节点必须在对白网里挂着，且门槛就是三件（D-11）。
            var benefit = new[] { "shen-fate-extra", "huolang-rumor" };
            check("气运收益节点已挂进对白网",
                benefit.All(id => nodeIds.Contains(id)), string.Join(" · ", benefit));
            // 大纲 1.3 与 0.3 的写作纪律：气运不当场弹窗告知。Record 的返回值是
            // "这是不是第一次记下"，不是一个可以显示给玩家看的分数——这条靠签名把住：
            // 账簿侧不许存在任何"给玩家看的文案"型 API。
            check("账簿不返回可显示的功德数值",
                typeof(KarmaTracker).GetMethods()
                    .Where(m => m.Name == "Record")
                    .All(m => m.ReturnType == typeof(bool)));

            // D-17：交情折扣这个 API 不该还存在。
            var treeSource = ReadAssetSource(configDir, "Scripts", "Core", "DialogueTree.cs");
            check("MemoryEcho 不再提供折扣（D-17 / T057）",
                treeSource.Length > 0 && !treeSource.Contains("float Discount(") &&
                !treeSource.Contains("int DiscountPercent("),
                "残留即「两个折扣并存」又回来了");
            check("karma>= 已进 DialogueCondition 的键表", treeSource.Contains("case \"karma\""));
        }

        /// <summary>取去向表里某个 id 的那一行（只认表格行，正文里的顺带提及不算点名）。</summary>
        static string LedgerRow(string ledger, string id) =>
            ledger.Split('\n').FirstOrDefault(line => line.StartsWith("| `" + id + "`")) ?? "";

        /// <summary>读一张配置表（供只读对表用）。解析失败当场抛，不静默给空集。</summary>
        static JsonValue JsonRoot(string configDir, string file)
        {
            if (!Json.TryParse(File.ReadAllText(Path.Combine(configDir, file)), out var root, out var error))
                throw new FormatException(file + " 解析失败：" + error);
            return root;
        }

        /// <summary>取一个数组里的 id 集合；根就是数组时直接扫，否则先取那一层键。</summary>

        // ---------------- 20：五张表的双向对表（§11#1/#2 第一次落地）----------------
        //
        // 单向对表只问得出"表里引用的 id 存不存在"，问不出"表里写了却没人用"。
        // T038/T042/T043/T052/T057/T074 全是后半句失守：能力存在，功能不存在。
        // 所以每张表正反各问一次；确实没有对向消费方的，进台账点名写原因——
        // 静默漏掉就是下一次的红。
        static readonly HashSet<string> AutoUnlockedQuests = new HashSet<string>(StringComparer.Ordinal)
        {
            // 这九条没有对白入口，靠 quests.json 的 prerequisites/status 自动解锁。
            // 但自动解锁的运行期驱动者（QuestLedger.RefreshAvailability）至今没人调 ——
            // 整组挂在 T083 上，不是"已经能玩到"。新增任务而不归类，这条就红。
            "tax-and-labor", "night-beast-roar", "beast-origin", "guide-investigation",
            "town-day", "fortune-teller-change", "leave-qingshi", "quest-shitou-promise",
            "quest-bridge-blackmarket",
        };

        static void CheckBidirectionalTables(string configDir, ItemTable items,
            KarmaTable karma, RecipeTable recipes, CheckFn check)
        {
            var dialogues = JsonRoot(configDir, "dialogues.json");
            var questIds = IdSet(JsonRoot(configDir, "quests.json"), "quests");
            var nodes = dialogues.kind == JsonValue.Kind.Array ? dialogues : dialogues["nodes"];

            // ---- 对白 → 任务（正向）：acceptQuest 必须指名一条存在的任务 ----
            var named = new List<string>();
            var nameless = new List<string>();
            var ghost = new List<string>();
            foreach (var node in nodes.Items)
            {
                var options = node["options"];
                if (options == null || options.IsNull) continue;
                foreach (var option in options.Items)
                {
                    if (option == null) continue;
                    var action = option["action"].AsString("");
                    var quest = option["acceptQuest"].AsString("");
                    if (action != "acceptQuest") continue;
                    if (quest.Length == 0) { nameless.Add(node["id"].AsString("")); continue; }
                    named.Add(quest);
                    if (!questIds.Contains(quest)) ghost.Add(quest);
                }
            }
            check("每个 acceptQuest 选项都指名要接的任务（点了没反应的那类静默 bug）",
                nameless.Count == 0, string.Join(" · ", nameless));
            check("对白让接的任务都在任务表里", ghost.Count == 0, string.Join(" · ", ghost));

            // ---- 任务 → 对白（反向）：每条任务都要有声明过的入口 ----
            var noEntry = questIds.Where(id => !named.Contains(id) && !AutoUnlockedQuests.Contains(id))
                .OrderBy(id => id).ToList();
            check("每条任务都有归类过的运行期入口（对白接 / 表内自动解锁并挂 T083）",
                noEntry.Count == 0, string.Join(" · ", noEntry));
            check("自动解锁那一类整体仍挂在 T083（运行期没人调 QuestLedger）",
                AutoUnlockedQuests.Count == questIds.Count - named.Distinct().Count(),
                "对白入口 " + named.Distinct().Count() + " 条 / 自动解锁 " + AutoUnlockedQuests.Count + " 条");

            // ---- 敌人掉落 → 物品（正向）：N 域的 id 必须能在物品表里查到 ----
            var itemIds = new HashSet<string>(items.Items.Select(d => d.Id), StringComparer.Ordinal);
            var drops = JsonRoot(configDir, "enemies.json");
            var unknown = new List<string>();
            foreach (var enemy in (drops.kind == JsonValue.Kind.Array ? drops : drops["enemies"]).Items)
            {
                var list = enemy["drops"];
                if (list == null || list.IsNull) continue;
                foreach (var drop in list.Items)
                {
                    var id = drop?["itemId"].AsString("");
                    if (id.Length > 0 && !itemIds.Contains(id)) unknown.Add(id);
                }
            }
            check("敌人掉的每一件都在物品表里（T074 那类英文 id 上屏从此红着）",
                unknown.Count == 0, string.Join(" · ", unknown));

            // ---- 气运门槛：dialogues 里 karma>=N 的 N 必须落在清单能凑齐的范围里 ----
            var thresholds = new List<int>();
            foreach (var node in nodes.Items)
            {
                var options = node["options"];
                if (options == null || options.IsNull) continue;
                foreach (var option in options.Items)
                {
                    var cond = option?["condition"].AsString("");
                    if (!string.IsNullOrEmpty(cond) && cond.StartsWith("karma>="))
                        thresholds.Add(ParseKarmaThreshold(cond));
                }
            }
            check("对白里的 karma>= 门槛都在清单可及范围内（1 ≤ N ≤ 条数）",
                thresholds.Count > 0 && thresholds.All(n => n >= 1 && n <= karma.Entries.Count),
                "门槛 " + string.Join(",", thresholds) + " / 清单 " + karma.Entries.Count + " 条");

            // ---- 方子产物 → 物品（正向补齐：输入侧上一轮已钉）----
            var noOutput = recipes.Recipes.Where(r => items.Find(r.ResultItemId) == null)
                .Select(r => r.Id).ToList();
            check("每道方子的产物都在物品表里（输入侧上一轮已钉，这里补产物方向）",
                noOutput.Count == 0, string.Join(" · ", noOutput));

            // ---- 防回潮：任务 id 不许再写回 C# 源码 ----
            // 只看代码、不看注释：这条护栏第一次跑就红在自己写的说明文字里
            // （注释里引用了那个旧字面量）——源码级断言不剥注释，就是在给自己埋假红。
            check("对白接哪条任务已完全由表决定（C# 代码里不再有任务 id 字面量）",
                !CodeOnly(ReadAssetSource(configDir, "Scripts", "Presentation", "FrontEndTrade.cs"))
                    .Contains("\"quest-herbal-rescue\""));
        }

        // ---------------- 21：任务的运行期驱动（T083 / D-35） ----------------
        //
        // 这一节验的不是"QuestLedger 能不能结算"——数据层上一轮就能把十条任务从零模拟到全部结算，
        // 那句话当时是假象：**能力存在，功能不存在**，玩家在游戏里做完什么都拿不到。
        // 所以这里验三件事：①运行期真有调用点（源码级，不靠自述）；②结算走的就是调用点用的
        // 那个函数（QuestSettlement.Drive，不是另写一份测试专用逻辑）；③没接上的那一类
        // 目标必须被如实登记，绝不被悄悄推满。
        static void CheckQuestDriven(string configDir, ItemTable items, KarmaTable karma, CheckFn check)
        {
            var quests = QuestTable.Parse(Read(configDir, "quests.json"));
            const string herbal = "quest-herbal-rescue";
            const string stone = "quest-shitou-promise";
            const string bridge = "quest-bridge-blackmarket";

            // ---- ① 调用点：两处生产路径 + "Drive 之后紧跟 Write"的落盘责任（T097） ----
            var host = Flatten(CodeOnly(ReadAssetSource(configDir, "Scripts", "Presentation", "FrontEndDialogue.cs")));
            var bag = Flatten(CodeOnly(ReadAssetSource(configDir, "Scripts", "Presentation", "FrontEndInventory.cs")));
            var choiceStart = host.IndexOf("voidChooseTownDialogue(", StringComparison.Ordinal);
            var choiceEnd = host.IndexOf("voidAdvanceTownDialogue(", StringComparison.Ordinal);
            var choice = choiceStart >= 0 && choiceEnd > choiceStart ? host.Substring(choiceStart, choiceEnd-choiceStart) : "";
            var driveAt = choice.IndexOf("DriveQuests(newQuestSignal{TalkedNpcId=npcId});", StringComparison.Ordinal);
            var writeAt = choice.IndexOf("repository.Write(activeSave);", StringComparison.Ordinal);
            check("对白页推进真实分支时驱动任务（运行期调用点一）",
                host.Contains("ChooseTownDialogue,AdvanceTownDialogue") && choice.Contains("CurrentDialogueRequest(requestToken)") &&
                choice.Contains("DialogueRules.Choose(") && driveAt >= 0, "FrontEndDialogue.ChooseTownDialogue");
            check("行囊页每次落盘前驱动任务（运行期调用点二）",
                bag.Contains("DriveQuests(QuestSignal.None());"), "FrontEndInventory.cs");
            check("Drive 只改内存，同一真实分支在它之后统一写盘一次",
                writeAt > driveAt && driveAt >= 0 && choice.IndexOf("repository.Write(activeSave);", writeAt + 1, StringComparison.Ordinal) < 0 &&
                choice.Contains("DialogueRules.Save(activeSave,DialogueRules.Snapshot(") &&
                bag.Contains("DriveQuests(QuestSignal.None());Guard(()=>repository.Write(activeSave));"));

            // ---- ② 采药救援：打过妖兽≠做完，说过话才结算；奖励与气运都从生产路径走 ----
            var save = NewSave();
            save.MarkFlag("encounter-boar-01");     // 战斗结算就是这么记这一场的
            QuestLedger.RefreshAvailability(save, quests);
            var herb = quests.Find(herbal);
            check("采药救援在新档上是可接的",
                QuestLedger.StatusOf(save, herb.Id) == QuestLedger.StatusAvailable);
            QuestLedger.TryStart(save, herb);
            var halfDone = QuestSettlement.Drive(save, quests, items, karma, QuestSignal.None());
            check("只打过妖兽、还没回话时不结算（一次对账不许把没发生的事推满）",
                QuestLedger.StatusOf(save, herb.Id) == QuestLedger.StatusActive && halfDone.Completed.Count == 0,
                "推进 " + halfDone.ObjectivesAdvanced + " 条 · 结算 " + halfDone.Completed.Count + " 条");
            check("打过的那一场确实被认下来了",
                QuestLedger.Progress(save, herb, "slay-the-boar") == 1);
            var ready = QuestSettlement.Drive(save, quests, items, karma,
                new QuestSignal { TalkedNpcId = "npc-yao-lao" });
            check("交还任务说过话后仍Active且不自动发奖",ready.Completed.Count==0 &&
                QuestLedger.StatusOf(save,herbal)==QuestLedger.StatusActive);
            var settled = QuestSettlement.TurnIn(save,quests,items,karma,herbal,"npc-yao-lao",120);
            check("指定NPC近距确认交还才结算", settled.Completed.Contains(herbal),
                "Completed=" + string.Join(",", settled.Completed));
            check("结算的奖励真进了行囊（三剂金疮药）",
                InventoryRules.Count(save.bag, "jinchuangyao") >= 3,
                "包里 " + InventoryRules.Count(save.bag, "jinchuangyao") + " 剂");
            check("结算的一次性标记落了档", save.oneTimeFlags.Contains("herb-boy-saved"));
            check("任务结算顺带记下善事：走的就是 UI 那句 Record",
                settled.KarmaRecorded.Contains("carry-back-herbs") && KarmaTracker.HasDone(save, "carry-back-herbs"),
                "KarmaRecorded=" + string.Join(",", settled.KarmaRecorded));
            var bagAfter = InventoryRules.Count(save.bag, "jinchuangyao");
            var karmaAfter = KarmaTracker.TotalCount(save);
            var again = QuestSettlement.Drive(save, quests, items, karma,
                new QuestSignal { TalkedNpcId = "npc-yao-lao" });
            check("重复对账既不再发奖也不再记账（幂等，跑图多点几下不会刷药）",
                again.Completed.Count == 0 &&
                InventoryRules.Count(save.bag, "jinchuangyao") == bagAfter &&
                KarmaTracker.TotalCount(save) == karmaAfter);

            // ---- ③ 石头的应承：三条目标分三步、每步只认自己那一步的事实 ----
            var s2 = NewSave();
            QuestLedger.RefreshAvailability(s2, quests);
            var promise = quests.Find(stone);
            QuestLedger.TryStart(s2, promise);
            QuestSettlement.Drive(s2, quests, items, karma, new QuestSignal { TalkedNpcId = "npc-shenhuai-an" });
            check("只听沈怀安说过石头的事：三条里推上一条",
                QuestLedger.Progress(s2, promise, "hear-of-shitou") == 1 &&
                QuestLedger.Progress(s2, promise, "buy-a-blade") == 0 &&
                QuestLedger.StatusOf(s2, stone) == QuestLedger.StatusActive);
            InventoryRules.Add(s2.bag, items, "cudao", 1);
            QuestSettlement.Drive(s2, quests, items, karma, QuestSignal.None());
            check("菜刀进了包，collect 自己对上账（不用谁再点一次）",
                QuestLedger.Progress(s2, promise, "buy-a-blade") == 1);
            var s2Settled = QuestSettlement.Drive(s2, quests, items, karma,
                new QuestSignal { TalkedNpcId = "npc-shitou" });
            check("三条凑齐才结算，红绳与黑匣同时入包",
                s2Settled.Completed.Contains(stone) &&
                InventoryRules.Count(s2.bag, "red-string") >= 1 &&
                InventoryRules.Count(s2.bag, "black-box-relic") >= 1);
            var s4 = NewSave();
            QuestLedger.RefreshAvailability(s4, quests);
            var tax = quests.Find("tax-and-labor");
            check("缴税在新档上可接、夜兽吼还锁着（解锁的唯一判据是前置完成）",
                QuestLedger.StatusOf(s4, tax.Id) == QuestLedger.StatusAvailable &&
                QuestLedger.StatusOf(s4, "night-beast-roar") == QuestLedger.StatusLocked);
            QuestLedger.TryStart(s4, tax);
            QuestSettlement.Drive(s4, quests, items, karma, new QuestSignal { TalkedNpcId = "npc-zhao-da" });
            check("替全村顶回数目之后，夜里才有兽吼：前置结完，下一环自动 Available",
                QuestLedger.StatusOf(s4, "night-beast-roar") == QuestLedger.StatusAvailable,
                "tax=" + QuestLedger.StatusOf(s4, tax.Id));

            // ---- ④ reach：这一类现在没有运行期生产者，必须登记而不是推满 ----
            var s3 = NewSave();
            QuestLedger.RefreshAvailability(s3, quests);
            var blackmarket = quests.Find(bridge);
            QuestLedger.TryStart(s3, blackmarket);
            InventoryRules.Add(s3.bag, items, "fulu", 1);
            var s3Report = QuestSettlement.Drive(s3, quests, items, karma, QuestSignal.None());
            check("桥边之约：买到的符推得上，走没走到桥**不硬推**",
                QuestLedger.Progress(s3, blackmarket, "buy-the-scrap") == 1 &&
                QuestLedger.Progress(s3, blackmarket, "meet-at-bridges") == 0 &&
                QuestLedger.StatusOf(s3, bridge) == QuestLedger.StatusActive,
                "reach 进度 " + QuestLedger.Progress(s3, blackmarket, "meet-at-bridges"));
            // T112 之后 reach 有了运行期生产者（FrontEndWorld.ReportTravelReach），
            // 所以"回执里点名 no-producer"这条前提作废，改成两态：
            // 走错地方不许推、走对地方必须推。只留"推不上"那一半就是没验。
            var wrongPlace = QuestSettlement.Drive(s3, quests, items, karma,
                new QuestSignal { ReachedPlaceId = "market" });
            check("reach 走错地点不算数（信号里的地点必须就是目标写的那个）",
                QuestLedger.Progress(s3, blackmarket, "meet-at-bridges") == 0,
                "进度 " + QuestLedger.Progress(s3, blackmarket, "meet-at-bridges"));
            var rightPlace = QuestSettlement.Drive(s3, quests, items, karma,
                new QuestSignal { ReachedPlaceId = "bridges" });
            check("reach 走到桥边应推上那一条目标",
                QuestLedger.Progress(s3, blackmarket, "meet-at-bridges") == 1,
                "进度 " + QuestLedger.Progress(s3, blackmarket, "meet-at-bridges"));
            check("桥边之约两条都齐后应能结算",
                QuestLedger.StatusOf(s3, bridge) == QuestLedger.StatusCompleted,
                rightPlace.Completed.Count + " 条结算");
            var unwiredKinds = quests.Quests
                .SelectMany(q => q.Objectives)
                .Select(o => o.Type)
                .Distinct()
                .Where(t => !QuestRuntime.WiredObjectiveTypes.Contains(t))
                .OrderBy(t => t)
                .ToList();
            check("任务表里每一类目标都有运行期生产者为它作证（T112 之后不该再有 no-producer）",
                unwiredKinds.Count == 0,
                "还差 " + string.Join(",", unwiredKinds));


            // ---- ⑤ 任务→气运的映射两侧都要有出处 ----
            var orphanQuests = KarmaTriggers.QuestTriggers
                .Where(p => quests.Find(p.Key) == null)
                .Select(p => p.Key + "→" + p.Value).ToList();
            check("映射到任务结算的每一条，任务表里都真有这条任务",
                orphanQuests.Count == 0, string.Join(" · ", orphanQuests));

            // ---- ⑥ T084：口径写进表里，rule 原文一个字不改 ----
            var secret = karma.Find("keep-a-secret");
            check("keep-a-secret 的判定口径仍是 0.3 原文（没有为了让它变绿而放宽）",
                secret != null && secret.Rule == "一直瞒到事情过去才算；后来要挟了就不算",
                secret?.Rule);
            check("keep-a-secret 在表里写明本轮只记哪一步（T084）",
                secret != null && !string.IsNullOrWhiteSpace(secret.Note) &&
                secret.Note.Contains("本轮只记") && secret.Note.Contains("泄密即撤销"),
                secret == null ? "表里没有这一条" : "note=" + secret.Note);
        }

        // ---------------- 22：素材棘轮（H-4 两条断言 + 自证会红，按 D-45 字样独占） ----------------
        //
        // H-4 原文两条：
        //   ①正向——五表里声明了资源就必须解析得到；没声明的必须落在**显式豁免清单**里并打印可见
        //     （豁免不是永久席：哪张表哪天加了 art 字段，它那条豁免就必须删掉）；
        //   ②反向——Resources/Art 下没人引用的素材只准降不准升，基线取实现当天实测值。
        // 演练式自证一律打 `SELFTEST:` 前缀——D-45 把 `RESULT: PASS` / `RESULT: FAIL` /
        // `suite finished, N problem(s)` 划成"真门禁在真判定之后"独占，一个假 RESULT 就是一次可被误读的绿。
        //
        // 实现期学到的一条口径，写在这里防下一个人重踩：**解析规则不确定的目录宁可不纳入**。
        // 若按"代码里出现过这个前缀就算引用"去算，Art/World 65 张、Art/Props 21 张会全部判成活的——
        // **把死文件判成活文件比不判更糟**（之后真出现的孤儿也会绿着过去）。
        //
        // D-49 之后这一节改成**按目录分档**：基线只剩一张 `directories`（旧的 orphanRules / orphanBaseline /
        // orphanDetail / notCovered 四张平行表合成一张——四份 key 各自漂移过一轮，拼错一个目录名就能把
        // "读不到目录"糊成"0 个孤儿"）。每一档要么 measured（带规则、带当天实测数、带孤儿名单），
        // 要么 awaiting-convention（带原因、带"在等谁"，**基线必须空着**）；盘上有图没档、档在盘上没图，
        // 两类都红。未纳入的档每次跑都打印它当前套两条候选规则各判出多少孤儿——约定一到，那行数字当天就变成基线。
        static void CheckAssetRatchet(string configDir, ItemTable items, CheckFn check)
        {
            // configDir 是 Assets/Resources/Config ⇒ 上跳一级是 Resources（Art 在这儿），
            // 上跳两级才是 Assets（Scripts 在这儿）。两个根别混——混了就是"目录读不到"。
            var resourcesRoot = Path.GetFullPath(Path.Combine(configDir, ".."));
            var assetsRoot = Path.GetFullPath(Path.Combine(configDir, "..", ".."));
            var artRoot = Path.Combine(resourcesRoot, "Art");
            var repoRoot = Path.GetFullPath(Path.Combine(configDir,
                "..", "..", "..", "..", ".."));
            var baselinePath = Path.Combine(repoRoot, "Tools", "SaveChecks", "art-ratchet-baseline.json");
            var baselineText = File.Exists(baselinePath) ? File.ReadAllText(baselinePath) : "";
            check("素材棘轮基线读到了（Tools/SaveChecks/art-ratchet-baseline.json）",
                baselineText.Length > 0, baselinePath);
            if (baselineText.Length == 0) return;
            if (!Json.TryParse(baselineText, out var baseline, out _) ||
                baseline.kind != JsonValue.Kind.Object)
            {
                check("素材棘轮基线是合法 JSON 对象", false, "解析失败 ⇒ 棘轮等于没装");
                return;
            }

            // ---- 引用面：表里声明的资源值 ∪ 代码里的 Resources.Load 字面量 ----
            var declared = items.Items
                .Select(d => d.Icon)
                .Where(name => !string.IsNullOrEmpty(name))
                .ToList();
            var literals = new HashSet<string>(StringComparer.Ordinal);
            // 字面量既收整段路径（"Art/Items/coins"），也收它的文件名——
            // 反向棘轮按文件名比对，只认整段路径会把真在用的图判成孤儿（本轮实测：coins）。
            var literalNames = new HashSet<string>(StringComparer.Ordinal);
            var scriptsDir = Path.Combine(assetsRoot, "Scripts");
            foreach (var source in Directory.GetFiles(scriptsDir, "*.cs", SearchOption.AllDirectories))
                foreach (var literal in ResourcesLoadLiterals(File.ReadAllText(source)))
                {
                    literals.Add(literal);
                    literalNames.Add(Path.GetFileNameWithoutExtension(literal));
                }

            // ---- 正向一：items 声明的每个 icon 都要在盘上，且带 .meta ----
            var itemsDir = Path.Combine(artRoot, "Items");
            var absent = declared
                .Where(icon => !File.Exists(Path.Combine(itemsDir, icon + ".png")))
                .ToList();
            check("正向：表里声明的资源全部解析得到（items 的 icon 逐个查文件）",
                absent.Count == 0, string.Join(" · ", absent));
            var itemFiles = PngNames(itemsDir);
            var noMeta = itemFiles
                .Where(name => !File.Exists(Path.Combine(itemsDir, name + ".png.meta")))
                .ToList();
            check("正向：Art/Items 每张图都带 .meta（没 meta＝不导入＝运行时空白格）",
                noMeta.Count == 0, string.Join(" · ", noMeta));
            var shared = declared.GroupBy(icon => icon).Where(g => g.Count() > 1)
                .Select(g => g.Key + "×" + g.Count()).ToList();
            check("正向：每件物品用自己的图标（共用一张就是分不清）",
                shared.Count == 0, string.Join(" ｜ ", shared));

            // ---- 正向二：没声明资源字段的表，必须逐张被豁免并写明原因 ----
            var fields = baseline["declaredResourceFields"];
            var exemptions = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in baseline["exemptions"].Items)
                if (row.kind == JsonValue.Kind.Object)
                    exemptions[row["table"].AsString("")] = row["reason"].AsString("");
            var unexempted = new List<string>();
            var reasonless = new List<string>();
            var stale = new List<string>();
            foreach (var table in new[] { "items", "npcs", "enemies", "encounters", "quests", "clues", "karma" })
            {
                var declaredField = fields[table].kind == JsonValue.Kind.Null
                    ? null : fields[table].AsString(null);
                var exempted = exemptions.ContainsKey(table);
                if (declaredField == null && !exempted) unexempted.Add(table);
                if (declaredField == null && exempted && string.IsNullOrWhiteSpace(exemptions[table]))
                    reasonless.Add(table);
                if (declaredField != null && exempted) stale.Add(table + "（已有 " + declaredField + " 字段，豁免该删）");
            }
            check("正向：不声明资源的表必须逐张被显式豁免（漏一张就红，不许静默跳过）",
                unexempted.Count == 0, "缺豁免：" + string.Join(" · ", unexempted));
            check("正向：每条豁免都要写明原因（空原因的豁免＝永久席的雏形）",
                reasonless.Count == 0, "没写原因：" + string.Join(" · ", reasonless));
            check("正向：已经声明了资源字段的表，那条豁免必须删掉（豁免会腐烂）",
                stale.Count == 0, string.Join(" · ", stale));
            check("豁免清单打印可见（跑一次门禁就能看见现在豁免了哪几张表）",
                exemptions.Count > 0, string.Join(" ｜ ",
                    exemptions.Select(p => p.Key + "＝" + Shorten(p.Value, 26))));

            // ---- 反向棘轮：D-49 之后**按目录分档**（一个目录一条数，不做全局一个数）----
            // 为什么不能全局一个数：Art/World/Chunks 是程序化产物，重新合成一次地形数量就变，
            // 全局棘轮会把"1号 正常重跑了地形"判成违规。所以每一档各带自己的规则、自己的基线。
            var dirs = baseline["directories"];
            var npcIds = ReadIds(Path.Combine(configDir, "npcs.json"), "npcs");
            var enemyIds = ReadIds(Path.Combine(configDir, "enemies.json"), "enemies");
            var idPool = new HashSet<string>(npcIds.Concat(enemyIds), StringComparer.Ordinal);
            check("基线带 directories 这一族（D-49 的按目录分组；缺它＝棘轮还在跑旧的平行表）",
                dirs.kind == JsonValue.Kind.Object &&
                dirs.Keys.Any(k => !k.StartsWith("_", StringComparison.Ordinal)), "kind=" + dirs.kind);
            if (dirs.kind == JsonValue.Kind.Object)
            {
                var onDisk = ArtDirs(artRoot);
                ValidateArtDirs(dirs, onDisk,
                    out var structural, out var phantom, out var missing, out var ghost);
                check("D-49 结构：每一档的 status / rule / measuredAt 都合格（含糊的档等于没有门）",
                    structural.Count == 0, string.Join(" ｜ ", structural));
                check("D-49 结构：没实测的档不许写基线数（架构师原话『写预计值等于没写』，这条钉成机器判据）",
                    phantom.Count == 0, string.Join(" ｜ ", phantom));
                check("D-49 覆盖：Resources/Art 下每个有图的目录都有一档（新素材目录不许悄悄没人管）",
                    missing.Count == 0, "缺档：" + string.Join(" · ", missing));
                check("D-49 覆盖：每一档指的目录真在盘上（幽灵档会让判据跑在不存在的路径上）",
                    ghost.Count == 0, "幽灵档：" + string.Join(" · ", ghost));

                var pending = new List<string>();
                foreach (var dir in dirs.Keys)
                {
                    if (dir.StartsWith("_", StringComparison.Ordinal)) continue;
                    var entry = dirs[dir];
                    var files = PngNames(ArtSub(artRoot, dir));
                    if (entry["status"].AsString("") == "measured")
                    {
                        var rule = entry["rule"].AsString("");
                        var expected = entry["baseline"].AsInt(-1);
                        var listed = entry["orphans"].Items.Select(v => v.AsString("")).ToList();
                        // 目录读不到 ⇒ files 为空 ⇒ 孤儿"0 个"，看着比基线还好。这是**假绿**，
                        // 本轮实测就踩过一次（路径拼成 Art/Art/Items）。所以先断言"目录真的读到了"。
                        check("反向棘轮：" + dir + " 目录真的读到了（读不到目录不等于 0 孤儿）",
                            files.Count > 0, files.Count + " 个 png");
                        List<string> orphans;
                        List<string> declaredButAbsent = null;
                        if (rule == "preset-grid")
                        {
                            // S-2 批准的第四种规则：预设 id × 性别 × 面容款式 × 三朝向 展开成网格。
                            // 正向只对**网格**判（那四个维度全是数据：*.asset 文件名 / SaveData.Genders /
                            // SaveData.MaxFaceStyles / 基线里声明的朝向），代码里的整段字面量那一族
                            // 只算"有主"不判红——它们指向的图缺不缺是别人的活（本轮就抓到 2 个，见字面量缺口账）。
                            //
                            // 【半成品修复】上一轮只写到这里：PresetGridNames 只有调用没有方法体，
                            // 六条编译错（CS0103 + 连锁的 method group / CS0136）。基线里目前也还没有
                            // 任何 preset-grid 档——resolver 的字段约定（朝向怎么声明、预设 asset 从
                            // 哪个目录读）尚未落地。按本文件 §22 的实现期口径「解析规则不确定的目录
                            // 宁可不纳入」，先如实降级：这一档本轮不判正向，盘上图全部按"没有展开器
                            // 证明有主"进反向棘轮比对；解析器落地时把这一段换成真展开，并同步补基线。
                            orphans = files.ToList();
                            declaredButAbsent = new List<string>();
                            check("正向：" + dir + " preset-grid 解析器尚未实现（PresetGridNames 只有调用没有方法体），本轮不判正向",
                                true, "降级：盘上 " + files.Count + " 张全按可疑进反向比对；字段约定落地后这里换真展开");
                        }
                        else if (rule == "config-field" || rule == "template")
                        {
                            var want = ExpectedPathsForDir(entry["resolver"], configDir, dir, out var source);
                            check("正向：" + dir + " 的期望清单现读得到（读不到就判不了，绝不拿空清单当\"全是孤儿\"）",
                                want != null && want.Count > 0, source);
                            want = want ?? new List<string>();
                            orphans = files.Where(f => !want.Contains(f))
                                .OrderBy(n => n, StringComparer.Ordinal).ToList();
                            declaredButAbsent = want.Where(w => !files.Contains(w)).ToList();
                        }
                        else orphans = OrphansBy(files, rule, declared, literalNames, idPool);
                        check("反向棘轮：" + dir + " 孤儿数与基线相等（升＝堆垃圾；降＝把基线一起改小）",
                            expected >= 0 && orphans.Count == expected,
                            dir + " 现在 " + orphans.Count + " / 基线 " + expected +
                            "（规则 " + rule + "）：" + string.Join(" · ", orphans));
                        check("反向棘轮：" + dir + " 的孤儿名单与基线逐条一致（不许换一张顶数）",
                            orphans.OrderBy(n => n, StringComparer.Ordinal)
                                .SequenceEqual(listed.OrderBy(n => n, StringComparer.Ordinal)),
                            "实际 " + string.Join(" · ", orphans) + " || 基线 " + string.Join(" · ", listed));
                        if (declaredButAbsent != null)
                            check("正向：" + dir + " 声明/展开出来的每一条都真在盘上（配置写了、文件没有＝运行期 null）",
                                declaredButAbsent.Count == 0,
                                "缺文件：" + string.Join(" · ", declaredButAbsent));
                    }
                    else
                    {
                        // 未纳入的档：只量文件数、只打印，绝不判绿红——解析规则还不在我手里。
                        var byDeclared = OrphansBy(files, "declared-or-literal",
                            declared, literalNames, idPool).Count;
                        var byId = OrphansBy(files, "id-equality", declared, literalNames, idPool).Count;
                        check("未纳入目录 " + dir + " 的档读得到目录（读不到＝档写在一条不存在的路径上）",
                            files.Count > 0, files.Count + " 个 png ｜ 等谁：" +
                            Shorten(entry["awaiting"].AsString(""), 40));
                        pending.Add(dir + "：" + files.Count + " 张 · 套 declared-or-literal 判 " +
                            byDeclared + " 孤儿 · 套 id-equality 判 " + byId + " 孤儿");
                    }
                }
                var pendingTotal = dirs.Keys.Count(k => !k.StartsWith("_", StringComparison.Ordinal) &&
                    dirs[k]["status"].AsString("") == "awaiting-convention");
                check("未纳入的目录每条都出了现场数字（有档却没打印＝悄悄跳过）",
                    pending.Count == pendingTotal,
                    string.Join(" ｜ ", pending) +
                    " ⇒ 同一批图套两条候选规则能差出几十张，所以规则必须由加载方给；约定一到，上面这些数字当天就变成基线");
            }

            // ---- 约定缺口：表里有 id、盘上没图（今天 3 条，红了也修不到本域，所以钉成"只准降"）----
            var gaps = new List<string>();
            foreach (var id in enemyIds)
                if (!PngNames(Path.Combine(artRoot, "NPC")).Contains(id)) gaps.Add("enemies/" + id);
            foreach (var id in npcIds)
                if (!PngNames(Path.Combine(artRoot, "Portraits")).Contains(id) &&
                    !PngNames(Path.Combine(artRoot, "Portraits")).Contains("portrait-" + id))
                    gaps.Add("npcs/" + id);
            var gapBase = baseline["conventionGapBaselineCount"].AsInt(-1);
            check("约定加载族的素材缺口只准降不准升（每条都要有人去出图，别让它长）",
                gapBase >= 0 && gaps.Count == gapBase,
                "现在 " + gaps.Count + " / 基线 " + gapBase + "：" + string.Join(" · ", gaps));

            // ---- encounters 的 art 字段：1号 Q1.6 §2 交来的语义，接进棘轮（D-52③ 要我这一侧的代码）----
            CheckEncounterArt(configDir, baseline, check);

            // ---- SELFTEST：真删一张 / 真加一张，证明这两记门禁真的会红（D-45：前缀 SELFTEST:）----
            SelfProveRatchet(itemsDir, declared, literalNames, check);
        }

        /// <summary>D-49 的分档结构校验：拿基线里的 directories 与盘上"真的有 png 的目录"对表，
        /// 分四类返回不合格：结构含糊 / 未实测却写了数 / 盘上有图却没档 / 档指向不存在的目录。
        /// 单独做成纯函数是为了让 SelfProveRatchet 能用**故意做坏的数据**喂它一遍——
        /// 判据是纸糊的，在这一步就会现形（"断言今天为绿"证明不了任何事）。</summary>
        static void ValidateArtDirs(JsonValue directories, List<string> onDisk,
            out List<string> structural, out List<string> phantom,
            out List<string> missing, out List<string> ghost)
        {
            structural = new List<string>();
            phantom = new List<string>();
            missing = new List<string>();
            ghost = new List<string>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var dir in directories.Keys)
            {
                if (dir.StartsWith("_", StringComparison.Ordinal)) continue;   // 下划线开头的是注释位
                keys.Add(dir);
                var entry = directories[dir];
                var status = entry["status"].AsString("");
                var rule = entry["rule"].AsString("");
                if (string.IsNullOrWhiteSpace(entry["measuredAt"].AsString("")))
                    structural.Add(dir + "（没写 measuredAt＝不知道这条数哪天量的，出事没法判断它旧没旧）");
                if (status == "measured")
                {
                    var needsResolver = rule == "config-field" || rule == "template";
                    if (!needsResolver && rule != "id-equality" && rule != "declared-or-literal")
                        structural.Add(dir + "（measured 却没有可用规则，现为「" + rule +
                            "」；只准那四种，第五条得先有人给它下定义）");
                    if (needsResolver &&
                        (string.IsNullOrWhiteSpace(entry["resolver"]["file"].AsString("")) ||
                         (string.IsNullOrWhiteSpace(entry["resolver"]["field"].AsString("")) &&
                          string.IsNullOrWhiteSpace(entry["resolver"]["templateField"].AsString("")))))
                        structural.Add(dir + "（规则是 " + rule + " 却没写 resolver.file ＋ field/templateField：" +
                            "期望清单现读不出来，这一档就是空的）");
                    if (entry["orphans"].kind != JsonValue.Kind.Array)
                        structural.Add(dir + "（measured 却没给出孤儿名单：数字对上了也不知道是谁）");
                }
                else if (status == "awaiting-convention")
                {
                    if (entry.Has("baseline") && entry["baseline"].kind == JsonValue.Kind.Number)
                        phantom.Add(dir + "（未实测却写了基线数 " + entry["baseline"].AsInt(-1) +
                            "）——预计值等于没写");
                    if (string.IsNullOrWhiteSpace(entry["reason"].AsString("")))
                        structural.Add(dir + "（没写为什么不纳入）");
                    if (string.IsNullOrWhiteSpace(entry["awaiting"].AsString("")))
                        structural.Add(dir + "（没写在等谁给解析规则）");
                    if (!string.IsNullOrEmpty(rule))
                        structural.Add(dir + "（规则都已经写进来了，却还是 awaiting-convention：" +
                            "当天实测当天把它转成 measured，别让它挂着）");
                }
                else
                {
                    structural.Add(dir + "（status「" + status + "」不是 measured 也不是 awaiting-convention）");
                }
                if (!onDisk.Contains(dir)) ghost.Add(dir + "（盘上没有带 png 的这个目录）");
            }
            missing.AddRange(onDisk.Where(d => !keys.Contains(d)));
        }

        /// <summary>Resources/Art 下每一个"自己直接带 png"的目录，key 用相对 Resources 的正斜杠路径
        /// （Art/World/Chunks 这种嵌套目录单独成档）。D-49 的分档覆盖就靠它和基线对表。</summary>
        static List<string> ArtDirs(string artRoot)
        {
            var found = new List<string>();
            if (!Directory.Exists(artRoot)) return found;
            void Walk(string path, string key)
            {
                if (Directory.GetFiles(path, "*.png").Length > 0) found.Add(key);
                foreach (var sub in Directory.GetDirectories(path).OrderBy(n => n, StringComparer.Ordinal))
                    Walk(sub, key + "/" + Path.GetFileName(sub));
            }
            Walk(artRoot, "Art");
            return found.OrderBy(k => k, StringComparer.Ordinal).ToList();
        }

        /// <summary>按档里写的 resolver **现读**"这个目录应当有哪些图"，返回文件名清单。
        /// 两种读法都来自 1号 D-52① 的原文（q1-6-artwire-report §1），不是我推断的：
        /// field＝某个配置字段里的完整 Resources 相对路径（如 building-plots.json 的 plots[].art）；
        /// template＝路径模板 × 网格展开（town-map.json 的 chunkArtTemplate × chunks[] 的 col/row——
        /// 用 chunks[].x/y 展开会得到 chunk_1024_0 一族，那是世界坐标不是下标，实测对不上任何文件）。
        /// **只收父目录等于本档的那些值**：把跨目录的声明算进这一档就会造出一记假红。
        /// 配置文件读不到时返回 null（不是空清单）——空清单会把整个目录判成孤儿，
        /// 那是"护栏没跑却报了红"，比不跑更糟（T081 那一族）。</summary>
        static List<string> ExpectedPathsForDir(JsonValue resolver, string configDir, string dir,
            out string source)
        {
            source = "这一档没写 resolver";
            var fileName = resolver["file"].AsString("");
            if (fileName.Length == 0) return null;
            var path = Path.Combine(configDir, fileName);
            if (!File.Exists(path)) { source = fileName + " 读不到"; return null; }
            if (!Json.TryParse(File.ReadAllText(path), out var root, out _))
            { source = fileName + " 不是合法 JSON"; return null; }
            var values = new List<string>();
            var templateKey = resolver["templateField"].AsString("");
            if (templateKey.Length > 0)
            {
                // templateField 写的是**键名**（chunkArtTemplate），模板值要从配置里再取一次——
                // 直接把键名当模板用，展开出来的全是那个键名，父目录一段对不上，期望清单就成了空的。
                var template = root[templateKey].AsString("");
                if (template.Length == 0)
                { source = fileName + " 里的 " + templateKey + " 读不到值"; return null; }
                var gridKey = resolver["gridField"].AsString("chunks");
                var xField = resolver["xField"].AsString("col");
                var yField = resolver["yField"].AsString("row");
                var grid = root[gridKey];
                if (grid.kind != JsonValue.Kind.Array)
                { source = fileName + " 里没有数组 " + gridKey; return null; }
                foreach (var cell in grid.Items)
                {
                    var x = cell[xField].AsInt(-1);
                    var y = cell[yField].AsInt(-1);
                    if (x < 0 || y < 0)
                    { source = fileName + " 的 " + gridKey + " 里有格子缺 " + xField + "/" + yField; return null; }
                    values.Add(template.Replace("{x}", x.ToString()).Replace("{y}", y.ToString()));
                }
                source = fileName + " 的 " + templateKey + "＝「" + template + "」 × " + gridKey + "[]（" +
                    xField + "=「{x}」/ " + yField + "=「{y}」，展开 " + values.Count + " 条）";
            }
            else
            {
                var field = resolver["field"].AsString("");
                if (field.Length == 0)
                { source = fileName + " 的解析器既没写 field 也没写 templateField"; return null; }
                values.AddRange(ConfigArtValues(root, field, fileName));
                source = fileName + " 的 " + field;
            }
            return values.Where(v => ParentOf(v) == dir).Select(LeafName)
                .Where(n => n.Length > 0).Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal).ToList();
        }

        /// <summary>读取既有顶层/数组字段，以及材质配置中有对应源文件路径的 sourceAsset.resource。</summary>
        static IEnumerable<string> ConfigArtValues(JsonValue root, string field, string fileName)
        {
            // 只展开这个消费合同；注释、任意嵌套 resource 和同名字面量键都不能替图片消掉孤儿记录。
            if (field == "materials[*].sourceAsset.resource")
            {
                if (fileName != "world-material-gen.json" || root["materials"].kind != JsonValue.Kind.Object)
                    yield break;
                foreach (var material in root["materials"].Keys)
                {
                    if (!material.EndsWith(".png", StringComparison.Ordinal) ||
                        material.IndexOfAny(new[] { '/', '\\', ':' }) >= 0) continue;
                    var source = root["materials"][material]["sourceAsset"];
                    var resource = source["resource"].AsString("");
                    if (!resource.StartsWith("Art/", StringComparison.Ordinal) ||
                        resource.IndexOfAny(new[] { '\\', ':' }) >= 0 ||
                        resource.EndsWith(".png", StringComparison.Ordinal) ||
                        resource.Split('/').Any(part => part.Length == 0 || part == "." || part == ".."))
                        continue;
                    // 实际生成器消费 sourceAsset.path；只认与 Resources 名称逐字对应的那一张 PNG。
                    if (source["path"].AsString("") !=
                        "GameClient/TwelveJade/Assets/Resources/" + resource + ".png") continue;
                    yield return resource;
                }
                yield break;
            }
            var at = field.IndexOf("[]", StringComparison.Ordinal);
            if (at < 0)
            {
                var single = root[field].AsString("");
                if (single.Length > 0) yield return single;
                yield break;
            }
            var key = field.Substring(0, at);
            var leaf = field.Substring(at + 2).TrimStart('.');
            foreach (var row in root[key].Items)
            {
                var value = row[leaf].AsString("");
                if (value.Length > 0) yield return value;
            }
        }

        // ---------------- 26：对话态分层排版参数（T107 的"排版那一半"） ----------------
        //
        // T107 的根因是 Destroy 要等帧末才生效（修法见 FrontEndTrade.HideStaleBranchPanels）。
        // 派单点名的还有另一半：**分层排版参数要写清楚，目视那一半断言代不了**。
        // 这一节只做能被机器做的两件事：
        //   ① 那些数字必须**有名字**——以前散在方法体里写死（770/200/44/26/24/6），改了就改了，没人知道；
        //   ② 拿到名字之后**验算式**：装不装得下、咬不咬、越不越屏、占屏比超不超。
        // 目视在报告里做（真跑真看，逐张带 md5 与树身份），这一节不假装替它签了字。
        //
        // 【2026-10-11 架构师复议·随 TF-01 重构重写】94cc6bd 把分支面板从"全宽长条＋标题"
        // 重构为"居中短条"（TB-33/AB-2），旧常量名（BranchPanelY/Height/TitleY/TitleHeight/
        // RowFirst/RowHeight）已不存在 ⇒ 本节按新结构重写。**语义不变**（数字有名字、算式可验、
        // 不许裸数字），算式全部换成 AB-2 的硬规格数字化，比旧版更严：旧版只管"面板容器装得下
        // 六行"，新版连占屏比（≤42%）、距框 44、距名牌 6、居中不越屏、名牌跨框顶全算。
        // 派生常量（BranchRowPitch = BranchStripHeight + BranchStripGap 等）的数值**按定义重算**，
        // 不从源码文本读——读文本会把"定义关系本身"漏掉（改错加数时文本照样读得出旧值）。
        static void CheckTalkBranchLayout(string configDir, CheckFn check)
        {
            var trade = ReadAssetSource(configDir, "Scripts", "Presentation", "FrontEndTrade.cs");
            var controller = ReadAssetSource(configDir, "Scripts", "Presentation", "FrontEndController.cs");
            check("排版核对要读的两个文件都在（FrontEndTrade ＋ FrontEndController 的设计宽高）",
                trade.Length > 0 && controller.Length > 0,
                "FrontEndTrade " + trade.Length + " · FrontEndController " + controller.Length + " 字节");
            if (trade.Length == 0 || controller.Length == 0) return;

            var names = new[] { "BoxY", "BoxHeight", "BoxPadX", "BodyPadTop", "BodyLinePitch", "BodyMaxLines",
                "NamePlateWidth", "NamePlateHeight", "NamePlateY", "BranchStripWidth", "BranchStripHeight",
                "BranchStripGap", "BranchBlockBottom", "BranchMaxRows",
                "PortraitBoxWidth", "FallbackBoxWidth" };
            var missingNames = names.Where(n => FloatAfter(trade, n + " =") == null).ToList();
            check("对话态每个排版数字都有名字（还有裸数字写在方法体里＝这一节读不到，等于没守）",
                missingNames.Count == 0, "没读到：" + string.Join(" · ", missingNames));
            if (missingNames.Count > 0) return;
            var derived = new[] { "BranchRowPitch", "BodyHeight", "BranchStripX" };
            var missingDerived = derived.Where(n => !trade.Contains(n + " =")).ToList();
            check("派生常量也必须有名字（行距/正文高/居中 X 由基础常量算，不许把算式写回方法体）",
                missingDerived.Count == 0, "没读到：" + string.Join(" · ", missingDerived));
            if (missingDerived.Count > 0) return;

            var boxY = FloatAfter(trade, "BoxY =").Value;
            var boxH = FloatAfter(trade, "BoxHeight =").Value;
            var bodyPadTop = FloatAfter(trade, "BodyPadTop =").Value;
            var bodyPitch = FloatAfter(trade, "BodyLinePitch =").Value;
            var bodyMaxLines = (int)FloatAfter(trade, "BodyMaxLines =").Value;
            var plateW = FloatAfter(trade, "NamePlateWidth =").Value;
            var plateH = FloatAfter(trade, "NamePlateHeight =").Value;
            var plateY = FloatAfter(trade, "NamePlateY =").Value;
            var stripW = FloatAfter(trade, "BranchStripWidth =").Value;
            var stripH = FloatAfter(trade, "BranchStripHeight =").Value;
            var stripGap = FloatAfter(trade, "BranchStripGap =").Value;
            var blockBottom = FloatAfter(trade, "BranchBlockBottom =").Value;
            var maxRows = (int)FloatAfter(trade, "BranchMaxRows =").Value;
            var portraitBoxW = FloatAfter(trade, "PortraitBoxWidth =").Value;
            var fallbackBoxW = FloatAfter(trade, "FallbackBoxWidth =").Value;
            var rowPitch = stripH + stripGap;                  // 按定义重算（BranchRowPitch）
            var bodyHeight = bodyPitch * bodyMaxLines;        // 按定义重算（BodyHeight）
            var memoryH = FloatAfter(trade, "MemoryHeight =") ?? 24f;    // 与正文/动作条同族的基础常量
            var actionH = FloatAfter(trade, "ActionHeight =") ?? 60f;
            DesignSize(controller, out var designW, out var designH);
            check("设计宽高读到了（读不到就没法判这一层越不越屏）", designW > 0 && designH > 0,
                designW + "×" + designH);
            if (designW <= 0) return;
            var stripX = (designW - stripW) * .5f;             // 按定义重算（BranchStripX：水平居中）

            // ① 条与条不互叠（行距 ≥ 条高）：改小 Gap 会直接红。
            check("分支条与条不互叠（行距 " + rowPitch + " 必须 ≥ 条高 " + stripH + "）",
                rowPitch >= stripH, "行间隙 " + (rowPitch - stripH) + "px；负数＝条互叠");
            // ② 条宽合规（AB-2：居中短条宽 ≤560）。
            check("分支条宽 ≤ 560（AB-2 规格；超出就不是「居中短条」了）", stripW <= 560f,
                "现值 " + stripW);
            // ③ 条块不压 Talk box（块底距框顶 ≥ 44）：736 vs 780 ⇒ 44。
            check("分支条块不吃进 Talk box（块底 " + blockBottom + " 距框顶 " + boxY + " 必须 ≥ 44）",
                boxY - blockBottom >= 44f, "缝 " + (boxY - blockBottom) + "px；负数＝压框");
            // ④ 条块不碰名牌（名牌叠框左上角外侧，块底距牌顶 ≥ 6）：736 vs 742 ⇒ 6。
            check("分支条块不咬进名牌（块底 " + blockBottom + " 距牌顶 " + plateY + " 必须 ≥ 6）",
                plateY - blockBottom >= 6f, "缝 " + (plateY - blockBottom) + "px；负数＝咬名牌");
            // ⑤ 名牌确实跨在框顶外侧（设计：牌底伸进框内约 10px，不全在框外也不全在框内）。
            check("名牌跨在 Talk box 顶沿（牌顶 " + plateY + " < 框顶 " + boxY + " < 牌底 " + (plateY + plateH) + "）",
                plateY < boxY && plateY + plateH > boxY, "跨入 " + (plateY + plateH - boxY) + "px");
            // ⑥ 占屏上界（AB-2：780 起向上不超 460）：满条块顶 = 块底 -（条数×行距-间隔）。
            var blockHeight = maxRows * rowPitch - stripGap;
            var blockTop = blockBottom - blockHeight;
            check("满条时块顶不越过占屏上界（块顶 " + blockTop + " 必须 ≥ 框顶 " + boxY + " - 460）",
                blockTop >= boxY - 460f, "超出 " + (boxY - 460f - blockTop) + "px；负数＝合规");
            // ⑦ 居中不越屏（条块左右都必须在设计宽内）。
            check("分支条块水平居中且不越屏（" + stripX + " + " + stripW + " ≤ " + designW + "）",
                designW > 0 && stripX >= 0f && stripX + stripW <= designW,
                "右边剩 " + (designW - stripX - stripW) + "px");
            // ⑧ 框内容得下正文＋记忆行＋动作条（不许把框撑高——TB-33 的"最多 3 行"就在守这条）。
            var boxNeed = bodyPadTop + bodyHeight + memoryH + actionH;
            check("框内容得下正文/记忆行/动作条（需要 " + boxNeed + "px，框高 " + boxH + "）",
                boxNeed <= boxH, "还差 " + (boxNeed - boxH) + "px；负数＝被裁");
            // ⑨ 对话态合计占屏比 ≤42%（AB-2 硬规格；rect 口径毛算，名牌与框的 4000px 重叠不扣——
            //    扣了只会更小，毛算是从严）。框宽取两种构图里大的那个（有立绘 1200 / 无立绘 980）。
            var boxW = Math.Max(portraitBoxW, fallbackBoxW);
            var share = (boxW * boxH + stripW * blockHeight + plateW * plateH) / (designW * designH);
            check("对话态合计占屏 ≤ 42%（AB-2；框 " + boxW + "×" + boxH + " + 条块 " + stripW + "×" +
                blockHeight + " + 名牌 " + plateW + "×" + plateH + " ⇒ " + (share * 100f).ToString("0.00") + "%）",
                share <= .42f, "超 " + ((share - .42f) * 100f).ToString("0.00") + "个百分点");

            // 根因守卫也要有机器形状：先摘旧层再建新层，两处建层点都得调（定义 1 ＋ 调用 2）。
            var guards = CountOccurrences(trade, "HideStaleBranchPanels(");
            check("HideStaleBranchPanels 是「一处定义 ＋ 两处建层前调用」（BuildTalkBranches / RenderBranchResult）",
                guards == 3, "源码命中 " + guards + " 次（应为 3；删掉任一处调用这条就红）");
            var panelLine = trade.Split('\n').FirstOrDefault(l => l.Contains("\"Talk branches\""));
            check("建层那一行只准用命名参数（行内再出现 770/200/330/420 这种裸数字＝绕过上面那些算式）",
                panelLine != null && !panelLine.Contains("770") && !panelLine.Contains("200")
                && !panelLine.Contains("330") && !panelLine.Contains("420"),
                panelLine == null ? "没找到建层那一行" : panelLine.Trim());
            SelfProveTalkBranchLayout(trade, check);
        }

        /// <summary>从 `ui.Rect(canvas, "Design canvas", 0, 0, 1920, 1080)` 读设计宽与高
        /// （按逗号切，取最后两个数——前两个是 x/y = 0）。</summary>
        static void DesignSize(string source, out float width, out float height)
        {
            width = 0f; height = 0f;
            foreach (var line in source.Split('\n'))
            {
                if (!line.Contains("Design canvas")) continue;
                var parts = line.Split(',');
                if (parts.Length < 4) continue;
                float.TryParse(parts[parts.Length - 2].Trim(), out width);
                float.TryParse(parts[parts.Length - 1].Trim().TrimEnd(';', ')'), out height);
                return;
            }
        }

        /// <summary>取 `const float Name = 数字f` 里那个数（排版参数与 BoxY/BoxHeight 同一形状）。</summary>
        static float? FloatAfter(string source, string marker)
        {
            var at = source.IndexOf(marker, StringComparison.Ordinal);
            if (at < 0) return null;
            var i = at + marker.Length;
            while (i < source.Length && (source[i] == ' ' || source[i] == '\t')) i++;
            var start = i;
            while (i < source.Length && (char.IsDigit(source[i]) || source[i] == '.' || source[i] == '-')) i++;
            if (i == start) return null;
            return float.TryParse(source.Substring(start, i - start),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
                out var v) ? v : (float?)null;
        }

        /// <summary>取含某个标记那一行的最后一个正整数（设计高就写在那行的 ui.Rect(...) 参数尾巴上）。</summary>
        static int LastIntOnMarkerLine(string source, string marker)
        {
            var line = source.Split('\n').FirstOrDefault(l => l.Contains(marker));
            if (line == null) return -1;
            foreach (var token in line.Split(new[] { ',', ' ', '(', ')', ';' },
                         StringSplitOptions.RemoveEmptyEntries).Reverse())
                if (int.TryParse(token, out var n) && n > 0) return n;
            return -1;
        }

        static int CountOccurrences(string source, string needle)
        {
            var count = 0;
            for (var at = source.IndexOf(needle, StringComparison.Ordinal); at >= 0;
                 at = source.IndexOf(needle, at + needle.Length, StringComparison.Ordinal)) count++;
            return count;
        }

        /// <summary>自证：把源码文本在**内存里**改坏三处（面板压矮、面板上移咬进框、行距小于行高），
        /// 那三条算式必须各自抓到；仓库文件一个字节不动。抓不到就说明这几条是纸糊的。</summary>
        static void SelfProveTalkBranchLayout(string trade, CheckFn check)
        {
            var probes = new[]
            {
                (name: "条块压进 Talk box（块底 736→800）", from: "BranchBlockBottom = 736f", to: "BranchBlockBottom = 800f"),
                (name: "条数超出占屏上界（6→9）", from: "BranchMaxRows = 6", to: "BranchMaxRows = 9"),
                (name: "间隔改负导致条互叠（Gap 16→-10）", from: "BranchStripGap = 16f", to: "BranchStripGap = -10f"),
                (name: "框高压不下正文/记忆行/动作条（280→100）", from: "BoxHeight = 280f", to: "BoxHeight = 100f"),
            };
            foreach (var probe in probes)
            {
                if (!trade.Contains(probe.from))
                {
                    check("SELFTEST: 排版算式的探针文本取不到样本（自证入口本身失效，这条必须红）",
                        false, probe.from);
                    continue;
                }
                var doctored = trade.Replace(probe.from, probe.to);
                var dBoxY = FloatAfter(doctored, "BoxY =").Value;
                var dBoxH = FloatAfter(doctored, "BoxHeight =").Value;
                var dBodyPadTop = FloatAfter(doctored, "BodyPadTop =").Value;
                var dBodyPitch = FloatAfter(doctored, "BodyLinePitch =").Value;
                var dBodyMax = (int)FloatAfter(doctored, "BodyMaxLines =").Value;
                var dStripH = FloatAfter(doctored, "BranchStripHeight =").Value;
                var dStripGap = FloatAfter(doctored, "BranchStripGap =").Value;
                var dBlockBottom = FloatAfter(doctored, "BranchBlockBottom =").Value;
                var dMaxRows = (int)FloatAfter(doctored, "BranchMaxRows =").Value;
                var dRowPitch = dStripH + dStripGap;
                var dBlockHeight = dMaxRows * dRowPitch - dStripGap;
                var dBlockTop = dBlockBottom - dBlockHeight;
                var dBoxNeed = dBodyPadTop + dBodyPitch * dBodyMax + 24f + 60f;
                var caught = dBoxY - dBlockBottom < 44f ||
                             dBlockTop < dBoxY - 460f ||
                             dRowPitch < dStripH ||
                             dBoxNeed > dBoxH;
                check("SELFTEST: " + probe.name + " — 排版算式抓到", caught,
                    "框 " + dBoxY + "起/高" + dBoxH + " · 条块底 " + dBlockBottom + " 高 " + dBlockHeight +
                    " 顶 " + dBlockTop + " · 行距 " + dRowPitch + "/" + dStripH + " · 框需 " + dBoxNeed);
            }
            check("SELFTEST: 完好的一份不该被自己的排版算式判红（否则它在真数据上会冤枉人）",
                FloatAfter(trade, "BoxY =").Value - FloatAfter(trade, "BranchBlockBottom =").Value >= 44f &&
                FloatAfter(trade, "BranchBlockBottom =").Value -
                    ((int)FloatAfter(trade, "BranchMaxRows =").Value *
                     (FloatAfter(trade, "BranchStripHeight =").Value + FloatAfter(trade, "BranchStripGap =").Value) -
                     FloatAfter(trade, "BranchStripGap =").Value) >=
                    FloatAfter(trade, "BoxY =").Value - 460f &&
                FloatAfter(trade, "BranchStripHeight =").Value + FloatAfter(trade, "BranchStripGap =").Value >=
                    FloatAfter(trade, "BranchStripHeight =").Value &&
                FloatAfter(trade, "BodyPadTop =").Value +
                    FloatAfter(trade, "BodyLinePitch =").Value * (int)FloatAfter(trade, "BodyMaxLines =").Value + 84f <=
                    FloatAfter(trade, "BoxHeight =").Value,
                "现值：框 780 起/高 280 · 条块底 736 · 条 560×56/隔 16/上限 6 · 名牌 742 起/高 48");
        }

        static string ParentOf(string path)
        {
            var at = path.LastIndexOf('/');
            return at < 0 ? "" : path.Substring(0, at);
        }

        static string LeafName(string path)
        {
            var at = path.LastIndexOf('/');
            return at < 0 ? path : path.Substring(at + 1);
        }

        /// <summary>encounters 的 art 字段接进棘轮，语义按 1号 Q1.6 §2 原文：
        /// **非空**＝声明了资源，必须解析得到（正向，缺文件或缺 .meta 都红——没 .meta 运行期 Load 出 null）；
        /// **空**＝"图还没灌装"，运行期画 tint+shape 的彩色标记，计入基线打印，不许当红、也不许悄悄变多。
        /// 这一族单独一档（不并进 conventionGaps）：那边按 id 拼名、这边按声明路径，混在一起就分不清谁欠图。</summary>
        static void CheckEncounterArt(string configDir, JsonValue baseline, CheckFn check)
        {
            var path = Path.Combine(configDir, "town-map.json");
            var resourcesRoot = Path.GetFullPath(Path.Combine(configDir, ".."));
            if (!File.Exists(path) || !Json.TryParse(File.ReadAllText(path), out var root, out _))
            {
                check("encounters 这一族读得到（town-map.json 在且解析得过）", false, path);
                return;
            }
            var rows = root["encounters"].Items.Where(r => r.kind == JsonValue.Kind.Object).ToList();
            check("encounters 数组读到了（读空不等于这一族没人管）",
                rows.Count > 0, rows.Count + " 条遭遇点");
            var absent = new List<string>();
            var unwired = new List<string>();
            foreach (var row in rows)
            {
                var id = row["id"].AsString("?");
                var art = row["art"].AsString("");
                if (art.Length == 0) { unwired.Add(id); continue; }
                var png = Path.Combine(resourcesRoot,
                    art.Replace('/', Path.DirectorySeparatorChar) + ".png");
                if (!File.Exists(png)) absent.Add(id + " → " + art + "（png 不在）");
                else if (!File.Exists(png + ".meta")) absent.Add(id + " → " + art + "（.meta 不在＝不导入＝运行期 null）");
            }
            check("正向：encounters 里每一条非空 art 声明都解析得到（文件 ＋ .meta）",
                absent.Count == 0, string.Join(" · ", absent));
            var ledger = baseline["encounterArtGaps"];
            var want = ledger["count"].AsInt(-1);
            var detail = ledger["detail"].Items.Select(v => v.AsString("")).ToList();
            check("art 还没灌装的遭遇点条数与基线相等（升＝有人清了声明又不补图；降＝出图到了，同笔把基线改小）",
                want >= 0 && unwired.Count == want,
                "现在 " + unwired.Count + " / 基线 " + want + "：" + string.Join(" · ", unwired));
            check("空 art 的遭遇点名单逐条对得上（数字对了也得是这几条，不许换一条顶数）",
                unwired.OrderBy(n => n, StringComparer.Ordinal)
                    .SequenceEqual(detail.OrderBy(n => n, StringComparer.Ordinal)),
                "实际 " + string.Join(" · ", unwired) + " || 基线 " + string.Join(" · ", detail));
        }

        /// <summary>自证入口：在临时目录里**真的**删一张被引用的图、**真的**加一张没人引用的图，
        /// 看检测函数抓不抓到。抓不到就说明这记门禁是纸糊的——它比"断言今天为绿"更值得信。
        /// 全程只碰 Path.GetTempPath() 下的副本，仓库里的素材一个字节都不动。</summary>
        static void SelfProveRatchet(string itemsDir, List<string> declared,
            HashSet<string> literals, CheckFn check)
        {
            var sandbox = Path.Combine(Path.GetTempPath(),
                "asset-ratchet-selftest-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(sandbox);
                // 连 .meta 一起抄：自证里有一记是"图还在、meta 没了"，不抄 meta 就永远测不到它
                // （第一版就栽在这里：只抄 png，于是"找不到第二个样本"直接红）。
                foreach (var pattern in new[] { "*.png", "*.png.meta" })
                    foreach (var file in Directory.GetFiles(itemsDir, pattern, SearchOption.AllDirectories))
                    {
                        var target=Path.Combine(sandbox,Path.GetRelativePath(itemsDir,file));
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        File.Copy(file,target);
                    }
                var victim = declared.FirstOrDefault(
                    icon => File.Exists(Path.Combine(sandbox, icon + ".png")));
                if (victim == null)
                {
                    check("SELFTEST: 沙箱里找不到可删的样本（自证入口本身失效）", false, sandbox);
                    return;
                }
                File.Delete(Path.Combine(sandbox, victim + ".png"));
                var missing = declared
                    .Where(icon => !File.Exists(Path.Combine(sandbox, icon + ".png")))
                    .ToList();
                check("SELFTEST: 真删一张被引用的图，正向断言立刻抓到",
                    missing.Count == 1 && missing[0] == victim,
                    "抓到 " + missing.Count + " 张：" + string.Join(" · ", missing));

                // .meta 那一记换一张样本删：上一张的 png 已经没了，再删它的 meta 就测不到"图在、meta 没了"这个形态。
                var metaVictim = declared.FirstOrDefault(icon =>
                    icon != victim &&
                    File.Exists(Path.Combine(sandbox, icon + ".png")) &&
                    File.Exists(Path.Combine(sandbox, icon + ".png.meta")));
                if (metaVictim == null)
                {
                    check("SELFTEST: 找不到第二个样本，meta 那记自证没跑（这本身就该红）", false, sandbox);
                }
                else
                {
                    File.Delete(Path.Combine(sandbox, metaVictim + ".png.meta"));
                    var noMeta = declared
                        .Where(name => File.Exists(Path.Combine(sandbox,name+".png")) && !File.Exists(Path.Combine(sandbox, name + ".png.meta")))
                        .ToList();
                    check("SELFTEST: 图还在、.meta 被删，meta 断言立刻抓到",
                        noMeta.Count == 1 && noMeta[0] == metaVictim, string.Join(" · ", noMeta));
                }

                // 先扫一遍沙箱（未加料），再真加一张没人引用的图，比对"多出来的恰好是它"。
                var before = OrphansBy(PngNames(sandbox), "declared-or-literal", declared, literals,
                    new HashSet<string>(StringComparer.Ordinal));
                var ghost = Path.Combine(sandbox, "selftest-ghost.png");
                File.WriteAllBytes(ghost, File.ReadAllBytes(
                    Path.Combine(itemsDir, declared[0] + ".png")));
                var after = OrphansBy(PngNames(sandbox), "declared-or-literal", declared, literals,
                    new HashSet<string>(StringComparer.Ordinal));
                check("SELFTEST: 真加一张没人引用的图，反向棘轮数到 +1 且点得出名字",
                    after.Count == before.Count + 1 && after.Contains("selftest-ghost") &&
                    !before.Contains("selftest-ghost"),
                    "加料前 " + before.Count + " 张 → 加料后 " + after.Count + " 张：" +
                    string.Join(" · ", after));
            }
            finally
            {
                // 沙箱是自证入口自己造的，删干净；只删自己刚建的那个目录。
                if (Directory.Exists(sandbox)) Directory.Delete(sandbox, true);
            }

            SelfProveArtDirs(check);
        }

        /// <summary>D-49 分档结构判据的自证。ValidateArtDirs 是纯函数，所以这里不需要沙箱：
        /// 拿一份**完好**的拷贝（不该报任何东西，否则判据在真数据上会冤枉人）和七份**故意做坏**的拷贝
        /// 依次喂进去，看它抓不抓到。抓不到＝这记门是纸糊的——它比"今天绿着"值得信。</summary>
        static void SelfProveArtDirs(CheckFn check)
        {
            const string intact =
                "{\"Art/Items\":{\"status\":\"measured\",\"rule\":\"declared-or-literal\",\"baseline\":2," +
                "\"orphans\":[\"porridge-bowl\"],\"measuredAt\":\"2026-10-06\"}," +
                "\"Art/Props\":{\"status\":\"awaiting-convention\",\"rule\":null,\"baseline\":null," +
                "\"reason\":\"规则属世界线\",\"awaiting\":\"1号 D-52①\",\"measuredAt\":\"2026-10-06\"}}";
            var both = new List<string> { "Art/Items", "Art/Props" };
            var probes = new[]
            {
                // 完好的一份：四类桶都必须是空的。它要是自己就红了，说明判据在真数据上会冤枉人。
                (name: "完好的一份不该被自己的结构判据判红", json: intact,
                 dirs: both, bucket: "clean"),
                // 架构师原话"写预计值等于没写"——把未实测的档偷偷填个数，必须抓到。
                (name: "没实测的档里写了基线数", json: intact.Replace(
                        "\"Art/Props\":{\"status\":\"awaiting-convention\",\"rule\":null,\"baseline\":null",
                        "\"Art/Props\":{\"status\":\"awaiting-convention\",\"rule\":null,\"baseline\":7"),
                 dirs: both, bucket: "phantom"),
                // 档指向一条盘上不存在的.path（本轮实测踩过 Art/Art/Items 那一类）
                (name: "档指向盘上没有的目录", json: intact.Replace("\"Art/Items\"", "\"Art/Nope\""),
                 dirs: both, bucket: "ghost"),
                // 盘上新增了一个素材目录却没登记（注意方向：多一条"盘上有、档里没有"才是缺档；
                // 反过来"档里有、盘上没有"落在幽灵档那一桶，上面单独测过）
                (name: "盘上有图却没有档", json: intact,
                 dirs: new List<string> { "Art/Items", "Art/Props", "Art/New" }, bucket: "missing"),
                (name: "status 写错（拼错的档会悄悄不参与判定）", json: intact.Replace(
                        "\"Art/Items\":{\"status\":\"measured\"", "\"Art/Items\":{\"status\":\"mesured\""),
                 dirs: both, bucket: "structural"),
                // 约定到了、规则也写进来了，基线却还空着挂着——D-49 要求当天实测当天钉
                (name: "规则已写入却还是 awaiting-convention", json: intact.Replace(
                        "\"Art/Props\":{\"status\":\"awaiting-convention\",\"rule\":null",
                        "\"Art/Props\":{\"status\":\"awaiting-convention\",\"rule\":\"id-equality\""),
                 dirs: both, bucket: "structural"),
                (name: "measured 却不给孤儿名单（数字对上了也不知道是谁）", json: intact.Replace(
                        "\"orphans\":[\"porridge-bowl\"],", ""),
                 dirs: both, bucket: "structural"),
                // 1号 的约定到手后我加了 config-field / template 两种规则：写了规则却没写从哪读，
                // 期望清单就是空的，整目录会被判成孤儿——这一记必须自己抓住。
                (name: "规则写成 config-field 却没写 resolver", json: intact.Replace(
                        "\"Art/Items\":{\"status\":\"measured\",\"rule\":\"declared-or-literal\"",
                        "\"Art/Items\":{\"status\":\"measured\",\"rule\":\"config-field\""),
                 dirs: both, bucket: "structural"),
            };
            foreach (var probe in probes)
            {
                if (!Json.TryParse(probe.json, out var node, out _) ||
                    node.kind != JsonValue.Kind.Object)
                {
                    check("SELFTEST: 探针 JSON 解析不了（自证入口本身失效，这条必须红）", false, probe.name);
                    continue;
                }
                ValidateArtDirs(node, probe.dirs,
                    out var structural, out var phantom, out var missing, out var ghost);
                var caught = probe.bucket switch
                {
                    "phantom" => phantom.Count > 0,
                    "ghost" => ghost.Count > 0,
                    "missing" => missing.Count > 0,
                    "structural" => structural.Count > 0,
                    _ => structural.Count + phantom.Count + missing.Count + ghost.Count == 0,
                };
                check("SELFTEST: " + probe.name, caught,
                    "结构 " + structural.Count + " · 预计值 " + phantom.Count + " · 缺档 " + missing.Count +
                    " · 幽灵档 " + ghost.Count + " ｜ " +
                    string.Join(" ｜ ", structural.Concat(phantom).Concat(missing).Concat(ghost)));
            }
        }

        /// <summary>按目录的解析规则数孤儿。规则不确定就不纳入（见本节开头），
        /// 所以这里只允许两种明确的规则，别的直接判红而不是"猜一个"。</summary>
        static List<string> OrphansBy(List<string> files, string rule, List<string> declared,
            HashSet<string> literals, HashSet<string> idPool)
        {
            switch (rule)
            {
                case "declared-or-literal":
                    return files.Where(name =>
                            !declared.Contains(name) && !literals.Contains(name))
                        .OrderBy(name => name, StringComparer.Ordinal).ToList();
                case "id-equality":
                    return files.Where(name => !idPool.Contains(name))
                        .OrderBy(name => name, StringComparer.Ordinal).ToList();
                default:
                    return files.Select(name => name + "（未知解析规则 " + rule + "）").ToList();
            }
        }

        /// <summary>把配置里写的 "Art/Items" 这种目录名拼到 Assets/Resources/Art 根下。
        /// artRoot 本身已经以 Art 结尾，所以这里必须剥掉重复的那一段——
        /// 拼成 Art/Art/Items 时目录读不到，孤儿会算成 0，是一记假绿（本轮实测踩过）。
        /// key 恰好是 "Art" 时（素材就直接放在 Art 根上，farmer-* 一族实测如此）剥完是空的，
        /// 返回 artRoot 本身——不能拼成 Art/Art，那是这条覆盖断言第一次跑就撞到的形态。</summary>
        static string ArtSub(string artRoot, string dir)
        {
            var parts = dir.Split('/');
            if (parts.Length > 0 && string.Equals(parts[0], "Art", StringComparison.OrdinalIgnoreCase))
                parts = parts.Skip(1).ToArray();
            var path = artRoot;
            foreach (var part in parts) path = Path.Combine(path, part);
            return path;
        }

        /// <summary>扫一个目录里的 png 文件名（不含扩展名）。目录不存在就返回空——
        /// 但调用方会把"空"当成断言证据，不会当成"跳过"。</summary>
        static List<string> PngNames(string dir) =>
            Directory.Exists(dir)
                ? Directory.GetFiles(dir, "*.png").Select(Path.GetFileNameWithoutExtension)
                    .OrderBy(n => n, StringComparer.Ordinal).ToList()
                : new List<string>();

        /// <summary>从一张表里读 id 列表（表结构都是 { "key": [ { "id": ... } ] } 或裸数组）。</summary>
        static List<string> ReadIds(string path, string key)
        {
            if (!File.Exists(path)) return new List<string>();
            if (!Json.TryParse(File.ReadAllText(path), out var root, out _)) return new List<string>();
            var array = root.kind == JsonValue.Kind.Array ? root : root[key];
            return array.kind == JsonValue.Kind.Array
                ? array.Items.Where(v => v != null && v.kind == JsonValue.Kind.Object)
                    .Select(v => v["id"].AsString("")).Where(id => id.Length > 0).ToList()
                : new List<string>();
        }

        /// <summary>扫源码里 <c>Resources.Load&lt;T&gt;("…")</c> 的字面量参数。
        /// 只认字面量：以 / 结尾的前缀不算引用（那会把整个目录判成活，见本节开头）。
        /// 也不许跨行找——"Resources.Load" 出现在注释里时，往后抓到的字符串会跟这次调用无关，
        /// 那等于凭空给某个死文件发一张通行证。</summary>
        static IEnumerable<string> ResourcesLoadLiterals(string source)
        {
            for (var at = 0; (at = source.IndexOf("Resources.Load", at, StringComparison.Ordinal)) >= 0;)
            {
                var eol = source.IndexOf('\n', at);
                var quote = source.IndexOf('"', at);
                var close = quote < 0 || (eol >= 0 && quote > eol) ? -1 : source.IndexOf('"', quote + 1);
                if (close < 0 || (eol >= 0 && close > eol))
                {
                    at += "Resources.Load".Length;   // 这一处不成对（注释、跨行换行的调用），跳过去看下一处
                    continue;
                }
                var value = source.Substring(quote + 1, close - quote - 1);
                if (value.Length > 0 && !value.EndsWith("/", StringComparison.Ordinal))
                    yield return value;
                at = close;
            }
        }

        // ---------------- 23：云存档 40903 的玩家可见口径（S16 的控制台那一半） ----------------
        //
        // 为什么不放在夹具里读：夹具跑在副本工程，副本只带本线白名单，GameServer 整棵树不在里面
        // ⇒ 读它是"读空"，而读空会伪装成"护栏红了一次"，其实护栏根本没跑（T081 那一族）。
        // 控制台这一侧走仓库根（与气运去向表同一口径：configDir 上跳五段），读得到就是真读到了。
        //
        // D-50② 之后这一节的**不变量换了**，写清楚防下一个人把它"改回逐字一致"：
        // 旧判据是"服务端与客户端那句逐字相同"，可那句本身在替玩家诊断原因（"刚被另一次上传占用"），
        // 而 40903 的生产者是服务端 DataIntegrityViolationException 的兜底 handler——它接到的未必是撞槽位。
        // 架构师把客户端那句的定稿权给了我，服务端那句不在本轮裁定范围内 ⇒ 两侧现在**故意不同**。
        // 玩家真正的保护不是"两侧同句"，而是"同一次失败只可能冒出一句话"，所以下面钉的是它。
        static void CheckCloudConflictTexts(string configDir, CheckFn check)
        {
            var repoRoot = Path.GetFullPath(Path.Combine(configDir,
                "..", "..", "..", "..", ".."));
            var assetsRoot = Path.GetFullPath(Path.Combine(configDir, "..", ".."));
            var clientPath = Path.Combine(assetsRoot, "Scripts", "Network", "CloudSaveService.cs");
            var dialogPath = Path.Combine(assetsRoot, "Scripts", "UI", "Auth", "CloudSaveDialog.cs");
            var serverPath = Path.Combine(repoRoot, "GameServer", "src", "main", "java", "com",
                "twelvejade", "server", "save", "SaveController.java");
            var client = File.Exists(clientPath) ? File.ReadAllText(clientPath) : "";
            var dialog = File.Exists(dialogPath) ? File.ReadAllText(dialogPath) : "";
            var server = File.Exists(serverPath) ? File.ReadAllText(serverPath) : "";
            check("跨端核对要读的三个文件都在（客户端 CloudErrors ＋ UI ＋ 服务端 SaveController）",
                client.Length > 0 && dialog.Length > 0 && server.Length > 0,
                "客户端 " + client.Length + " · UI " + dialog.Length + " · 服务端 " + server.Length +
                " 字节 · 服务端路径 " + serverPath);
            if (client.Length == 0 || dialog.Length == 0 || server.Length == 0) return;

            var clientText = LiteralAfter(client, "UploadConflictText =");
            var serverText = LiteralAfter(server, "ApiCode.SLOT_CONFLICT,");
            check("两侧都取到了 40903 的那句 message（取不到＝形状变了，护栏失效）",
                clientText.Length > 0 && serverText.Length > 0,
                "客户端「" + clientText + "」 服务端「" + serverText + "」");
            // 玩家只可能看到一句：UI 的通用失败分支拿到的是 error.Message，撞槽位必须先被 TextForUpload 截住。
            var guardAt = dialog.IndexOf("CloudErrors.TextForUpload", StringComparison.Ordinal);
            var genericAt = dialog.IndexOf("上传失败：", StringComparison.Ordinal);
            check("玩家看到的 40903 只有一句：UI 先问 CloudErrors，再走它自己的通用文案",
                guardAt >= 0 && genericAt >= 0 && guardAt < genericAt &&
                !dialog.Contains(clientText),
                "TextForUpload 位置 " + guardAt + " · 「上传失败：」位置 " + genericAt +
                "（要求前者存在且更早）· UI 里抄了那句没有：" + dialog.Contains(clientText));
            check("客户端按错误码处置，不按文案匹配（改文案不许悄悄改掉处置逻辑）",
                client.Contains("error.Code == SlotConflict") && !client.Contains("Message.Contains"),
                "TextForUpload 的判据形状变了");
            // D-50② 的"不许承诺诊断"要有机器判据，否则下一句归因话会悄悄长回来。
            check("客户端那句不含归因（40903 是兜底路径，替玩家断定原因就是假承诺）",
                !clientText.Contains("占用") && !clientText.Contains("另一次") &&
                clientText.Contains("稍后再试"),
                "现为「" + clientText + "」");
            check("客户端与服务端那句现在故意不同（D-50② 只改客户端）——谁改了哪句，这条要红着提醒对表",
                clientText != serverText,
                "两侧又写成同一句了：「" + clientText + "」");
            // 9xxxx 是客户端本地段（D-42）：服务端一旦开始返回它，两侧就又撞回同一个号段了。
            check("服务端不占用客户端本地段 9xxxx（D-42 的段划分不许被反向侵蚀）",
                !server.Contains("90001"), "服务端源码里出现了 90001");
        }

        /// <summary>取某个标记之后的第一个字符串字面量（跨端文案比对专用）。</summary>
        static string LiteralAfter(string source, string marker)
        {
            var at = source.IndexOf(marker, StringComparison.Ordinal);
            if (at < 0) return "";
            var open = source.IndexOf('"', at);
            if (open < 0) return "";
            var close = source.IndexOf('"', open + 1);
            return close < 0 ? "" : source.Substring(open + 1, close - open - 1);
        }

        static string Shorten(string text, int max) =>
            string.IsNullOrEmpty(text) || text.Length <= max
                ? text : text.Substring(0, max) + "…";

        /// <summary>去掉全部空白，用于源码级顺序断言：排版改了不该把护栏弄红，
        /// 但调用顺序（Drive 在 Write 之前）改了必须弄红。</summary>
        static string Flatten(string source) =>
            new string((source ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray());

        /// <summary>剥掉行注释与块注释，只留可执行代码（源码级断言专用）。</summary>
        static string CodeOnly(string source)
        {
            if (string.IsNullOrEmpty(source)) return "";
            var lines = source.Split('\n').Select(line =>
            {
                var cut = line.IndexOf("//", StringComparison.Ordinal);
                return cut < 0 ? line : line.Substring(0, cut);
            });
            return string.Join("\n", lines);
        }

        static int ParseKarmaThreshold(string condition)
        {
            int.TryParse(condition.Substring("karma>=".Length), out var n);
            return n;
        }

        static HashSet<string> IdSet(JsonValue root, string key)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var array = root.kind == JsonValue.Kind.Array ? root : root[key];
            if (array.IsNull) return ids;
            foreach (var entry in array.Items)
            {
                var id = entry?["id"].AsString("");
                if (!string.IsNullOrEmpty(id)) ids.Add(id);
            }
            return ids;
        }

        // ---------------- 25：存档落盘形状的静态守卫（T118 那一课，schema 11 的自我补账） ----------------
        //
        // T118 是员工 N 实测撞出来的：`GearLedger.Record` 有人调、内存里查得到、UI 也显示，
        // 重新读档却是空的。根因不在记账那一层，在**形状**这一层——`GearLine`/`CraftLine` 头上
        // 少了 `[Serializable]`，而产线的 codec 包的是 Unity 的 JsonUtility：少了这个 attribute，
        // 那一个字段**静默不写**，不报错、不红、读档归零。
        // 我这一族的 karma 往返早有夹具钉着，gear/crafts 却只钉了"内存里成立"，
        // 于是 schema 11 带着两个存不下去的字段绿了一整轮——这就是"只断内存态＝把静默失败的层留白"。
        //
        // 这一节不指望"有人记得加 attribute"：拿 SaveData 的数组字段清单去**现查**每个元素类型的
        // 声明处，上面没有 [Serializable] 就红。下一笔加数组字段的人会当场撞上它。
        // 声明处找不到的类型单独红一条（找不到＝这一族没人守，不能算过）。
        static void CheckSaveCodecShape(string configDir, CheckFn check)
        {
            var models = ReadAssetSource(configDir, "Scripts", "Core", "SaveModels.cs");
            check("SaveModels.cs 读到了（读不到＝这一节根本没跑，而它看起来像一条 PASS）",
                models.Length > 0, "字节 " + models.Length);
            if (models.Length == 0) return;
            var scriptsRoot = Path.GetFullPath(Path.Combine(configDir, "..", "..", "Scripts"));
            var sources = Directory.GetFiles(scriptsRoot, "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText).ToList();
            sources.Add(models);   // SaveModels.cs 自己也是候选声明处

            var elementTypes = SaveDataArrayElementTypes(models);
            check("SaveData 的数组字段解析得到（一个都没解析出来＝形状换了，守卫失效）",
                elementTypes.Count > 0, string.Join(" · ", elementTypes));
            var undeclared = elementTypes
                .Where(t => sources.All(s => DeclarationPrelude(s, t) == null)).ToList();
            check("每个元素类型都找得到声明处（找不到＝这一族没人守，不许当成通过）",
                undeclared.Count == 0, "找不到声明：" + string.Join(" · ", undeclared));
            var unmarked = elementTypes.Where(t => !TypeIsSerializable(t, sources)).ToList();
            check("存档里每个数组字段的元素类型都带 [Serializable]（JsonUtility 缺它就静默不写整个字段）",
                unmarked.Count == 0, "缺 attribute：" + string.Join(" · ", unmarked));
            check("落盘形状守卫覆盖到的字段清单打印可见（现在守着哪几族，跑一次就看得见）",
                elementTypes.Count > 0, string.Join(" · ", elementTypes));
            SelfProveSaveCodecShape(elementTypes, sources, check);
        }

        /// <summary>取 SaveData 里所有数组字段的元素类型（`string` 与不高首字母的内置类型跳过）。
        /// 手写扫描而不是正则：只认 "public X[] name" 这一种形状，认不出就靠上面那条"一个都没解析出来"红。</summary>
        static List<string> SaveDataArrayElementTypes(string source)
        {
            var result = new List<string>();
            foreach (var raw in source.Split('\n'))
            {
                var line = raw.Trim();
                var cut = line.IndexOf("//", StringComparison.Ordinal);
                if (cut >= 0) line = line.Substring(0, cut).Trim();
                if (!line.StartsWith("public ") || !line.Contains("[]")) continue;
                var at = line.IndexOf("[]", StringComparison.Ordinal);
                var before = line.Substring(0, at);
                var type = before.Substring(before.LastIndexOfAny(new[] { ' ', '\t' }) + 1).Trim();
                if (type.Length == 0 || !char.IsUpper(type[0]) || type == "string") continue;
                if (!result.Contains(type)) result.Add(type);
            }
            return result;
        }

        /// <summary>找某个类型的声明行，返回它**上面那一段**（剥掉空行、/// 文档行与属性行，
        /// 到上一个 { 或 } 为止）。名字后面必须紧跟空白或标点，`class GearLine2` 不算命中 GearLine。
        /// 找不到声明行返回 null。</summary>
        static List<string> DeclarationPrelude(string source, string type)
        {
            var lines = source.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var text = lines[i].Trim();
                if (!text.StartsWith("public ")) continue;
                var found = false;
                foreach (var keyword in new[] { "class ", "struct ", "enum " })
                {
                    var at = text.IndexOf(keyword + type, StringComparison.Ordinal);
                    if (at < 0) continue;
                    var after = at + keyword.Length + type.Length;
                    var next = after < text.Length ? text[after] : ' ';
                    if (!(char.IsWhiteSpace(next) || next == '{' || next == ':' || next == '\r')) continue;
                    found = true;
                    break;
                }
                if (!found) continue;
                var prelude = new List<string>();
                for (var j = i - 1; j >= 0; j--)
                {
                    var up = lines[j].Trim();
                    if (up.Length == 0 || up.StartsWith("///", StringComparison.Ordinal) ||
                        up.StartsWith("[", StringComparison.Ordinal))
                    {
                        prelude.Add(up);
                        continue;
                    }
                    break;
                }
                return prelude;
            }
            return null;
        }

        static bool TypeIsSerializable(string type, List<string> sources)
        {
            foreach (var source in sources)
            {
                var prelude = DeclarationPrelude(source, type);
                if (prelude == null) continue;
                return prelude.Any(l => l.StartsWith("[Serializable", StringComparison.Ordinal));
            }
            return true;   // 找不到声明处由上面那条单独判红，这里不重复报
        }

        /// <summary>自证：在**内存里**把某个真带着 attribute 的类型的 attribute 行剥掉，
        /// 守卫必须当场点出它的名字，且剥之前它是过的。剥都抓不到，这一节就是纸糊的。</summary>
        static void SelfProveSaveCodecShape(List<string> elementTypes, List<string> sources, CheckFn check)
        {
            string sampleSource = null, sampleType = null;
            foreach (var type in elementTypes)
            {
                var hit = sources.FirstOrDefault(s =>
                    DeclarationPrelude(s, type) != null && TypeIsSerializable(type, new List<string> { s }));
                if (hit == null) continue;
                sampleSource = hit; sampleType = type; break;
            }
            if (sampleSource == null)
            {
                check("SELFTEST: 找不到一个带 attribute 的样本，形状守卫的自证没跑（这本身就该红）",
                    false, string.Join(" · ", elementTypes));
                return;
            }
            var doctored = RemoveSerializableLine(sampleSource, sampleType);
            check("SELFTEST: 内存里剥掉 " + sampleType + " 的 [Serializable]，守卫当场点出它（仓库一个字节没动）",
                TypeIsSerializable(sampleType, new List<string> { sampleSource }) &&
                !TypeIsSerializable(sampleType, new List<string> { doctored }) &&
                doctored.Length < sampleSource.Length,
                "剥掉的行数：" + (sampleSource.Length - doctored.Length) + " 字符（attribute 那一行）");
        }

        static string RemoveSerializableLine(string source, string type)
        {
            var lines = source.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var text = lines[i].Trim();
                if (!text.StartsWith("public ") || !text.Contains("class " + type)) continue;
                for (var j = i - 1; j >= 0; j--)
                {
                    var up = lines[j].Trim();
                    if (up.StartsWith("[Serializable", StringComparison.Ordinal))
                    {
                        var kept = lines.ToList();
                        kept.RemoveAt(j);
                        return string.Join("\n", kept);
                    }
                    if (up.Length == 0 || up.StartsWith("///", StringComparison.Ordinal) ||
                        up.StartsWith("[", StringComparison.Ordinal)) continue;
                    break;
                }
            }
            return source;
        }
    }
}
