using System;
using System.Collections.Generic;
using System.IO;
using TwelveJade.Core;
using TwelveJade.Economy;
using UnityEngine;

namespace TwelveJade.Editor
{
    // 员工 L · M9：行囊 / 手艺 / 物价 / 气运的无头夹具（85+ 断言）。
    // 批处理入口 TwelveJade.Editor.InventoryEconomyShot.RunSuite。
    //
    // 断言主体在 Economy/InventoryEconomySuite.cs（纯 C#，不依赖 UnityEngine），
    // 这里只负责三件事：找配置目录、收结果、补上两条**只有引擎里才测得到**的判据——
    // Resources 的真实加载路径、以及 JsonUtility 的落盘往返。同一套判据因此在
    // Unity 与控制台两条路上都能跑，而运行期专属的那一环不会没人值守。
    public static class InventoryEconomyShot
    {
        static readonly List<string> problems = new();
        static int ok;

        public static void RunSuite()
        {
            problems.Clear();
            ok = 0;
            var configDir = Path.Combine(Application.dataPath, "Resources", "Config");
            try { InventoryEconomySuite.Run(configDir, Check); }
            catch (Exception ex) { problems.Add("驱动异常: " + ex.Message); }
            CheckRecipesLoadAtRuntime();
            CheckKarmaTableLoadAtRuntime(configDir);
            CheckKarmaSurvivesTheEngineCodec(configDir);
            CheckGearAndCraftSurviveTheEngineCodec();

            // 成交真源的唯一一组数打进日志：上一轮这里是两套模型对撞（Core 324 / ADR 323），
            // D-05 裁决后平滑侧已删，留这一行是为了让"57.5% 封顶"这个口径可核对。
            var table = ItemTable.Parse(File.ReadAllText(Path.Combine(configDir, "items.json")));
            var probe = table.Find("tiejian");
            Debug.Log(string.Format(
                "[InventoryEconomyShot] note 单一真源：铁剑 {0} 文，买价 声望0={1} 声望100={2}；" +
                "卖价 声望0={3} 声望100={4}（{5:F1}%）；黑市={6}",
                probe.Price, Trade.BuyPrice(probe, 0), Trade.BuyPrice(probe, 100),
                Trade.SellPrice(probe, 0), Trade.SellPrice(probe, 100),
                Trade.SellPrice(probe, 100) / (float)probe.Price * 100f, Trade.BlackPrice(probe)));

            Debug.Log(string.Format(
                "[InventoryEconomyShot] suite finished, {0} problem(s), ok={1}", problems.Count, ok));
            foreach (var problem in problems)
                Debug.Log("[InventoryEconomyShot] PROBLEM " + problem);
        }

        /// <summary>配方表必须真能在 Resources 路径上被取到：文件在盘上却进不了包，
        /// 玩家面对的照样是一个没有方子的灶台。</summary>
        static void CheckRecipesLoadAtRuntime()
        {
            var asset = Resources.Load<TextAsset>(RecipeTable.ResourcePath);
            Check("运行期 Resources 取到配方表", asset != null, RecipeTable.ResourcePath);
            if (asset == null) return;
            var itemsAsset = Resources.Load<TextAsset>("Config/items");
            Check("运行期 Resources 取到物品表", itemsAsset != null);
            if (itemsAsset == null) return;
            try
            {
                var recipes = RecipeTable.Parse(asset.text);
                recipes.CrossCheck(ItemTable.Parse(itemsAsset.text));
                Check("运行期取到的配方表可解析并与物品表对表", recipes.Recipes.Count >= 2,
                    recipes.Recipes.Count + " 道");
            }
            catch (FormatException ex)
            {
                Check("运行期取到的配方表可解析", false, ex.Message);
            }
        }

        /// <summary>气运清单必须能在 Resources 路径上被取到（T096 迁出代码之后，
        /// "文件在盘上但进不了包"就会退化成"善行一律不记账"——表外 id 是被静默拒收的）。</summary>
        static void CheckKarmaTableLoadAtRuntime(string configDir)
        {
            var asset = Resources.Load<TextAsset>(KarmaTable.ResourcePath);
            Check("运行期 Resources 取到气运清单", asset != null, KarmaTable.ResourcePath);
            if (asset == null) return;
            try
            {
                var table = KarmaTable.Parse(asset.text);
                Check("运行期取到的气运清单是 25 条（17 善事 + 8 奇遇）",
                    table.Deeds.Count == 17 && table.Chances.Count == 8,
                    table.Deeds.Count + " / " + table.Chances.Count);
                Check("盘上的气运清单与 Resources 取到的是同一份",
                    File.ReadAllText(Path.Combine(configDir, KarmaTable.FileName)) == asset.text);
            }
            catch (FormatException ex)
            {
                Check("运行期取到的气运清单可解析", false, ex.Message);
            }
        }

        /// <summary>善行账簿走引擎的序列化往返（产线 codec 即包在 JsonUtility 上）。
        /// 记了却存不住，等于玩家什么都没做过。</summary>
        static void CheckKarmaSurvivesTheEngineCodec(string configDir)
        {
            var table = KarmaTable.Parse(File.ReadAllText(Path.Combine(configDir, KarmaTable.FileName)));
            var save = new SaveData { slot = 1, characterId = "farmer", characterName = "甲" };
            save.bag = InventoryRules.NewBag();
            var when = new WorldTime { day = 2, minuteOfDay = 1230 };
            KarmaTracker.Record(save, table, "river-rescue", when);
            KarmaTracker.Record(save, table, "teahouse-tale", when);
            SaveData.Migrate(save);

            var back = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(save));
            SaveData.Migrate(back);

            Check("善行经引擎序列化回读后条数不变", back.karmaEvents.Length == 2,
                back.karmaEvents.Length + " 条");
            Check("回读后仍认得做过的那件善事", KarmaTracker.HasDone(back, "river-rescue"));
            Check("回读后奇遇仍归奇遇一类", KarmaTracker.ChanceCount(back) == 1);
            Check("回读后发生的日子没丢",
                back.karmaEvents[0].day == 2 && back.karmaEvents[0].minuteOfDay == 1230);
            Check("回读后未攒够三件就不引机缘", !KarmaTracker.HasFortuneBoons(back));
            Check("回读后次数照累加（同一件事再做一次）",
                KarmaTracker.Record(back, table, "river-rescue", when) == false &&
                KarmaTracker.TimesOf(back, "river-rescue") == 2);
        }

        /// <summary>穿戴台账与制作台账走引擎的序列化往返（T118，员工 N 2026-10-06 实测撞出：
        /// `台账 tiejian 耐久 117，身上 117/120` 而 `磁盘 没有这一条`）。根因不在记账那一层，
        /// 在形状那一层——`GearLine`/`CraftLine` 头上少了 [Serializable]，而产线 codec 包的是
        /// JsonUtility：**缺它整个字段静默不写，不报错、不红、读档归零**。
        /// 记了却存不住，等于玩家什么都没做过；这一节就是把"我写了"换成"读回来还在"。</summary>
        static void CheckGearAndCraftSurviveTheEngineCodec()
        {
            var save = new SaveData { slot = 3, characterId = "farmer", characterName = "铁" };
            save.bag = InventoryRules.NewBag();
            GearLedger.Record(save, GearSlots.Weapon, "tiejian", 117);
            GearLedger.Record(save, GearSlots.Armor, "yijia", 200);
            CraftLedger.Record(save, "porridge");
            CraftLedger.Record(save, "porridge");
            CraftLedger.Record(save, "detox-three");
            SaveData.Migrate(save);

            var back = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(save));
            SaveData.Migrate(back);

            var weapon = GearLedger.Of(back, GearSlots.Weapon);
            Check("穿戴经引擎序列化回读后条数不变（T118 的那一条：内存 2 条、磁盘 0 条）",
                back.gear != null && back.gear.Length == 2,
                (back.gear == null ? "null" : back.gear.Length + " 条"));
            Check("回读后武器还是那一件、耐久没归零（存属性是第二真源，这里只该存这三样）",
                weapon != null && weapon.itemId == "tiejian" && weapon.durability == 117,
                weapon == null ? "整条没回来" : weapon.itemId + "/" + weapon.durability);
            Check("回读后护甲槽也在（同一次写入的两条都要回来，不是只回第一条）",
                GearLedger.Of(back, GearSlots.Armor)?.itemId == "yijia",
                GearLedger.Of(back, GearSlots.Armor)?.itemId ?? "null");
            Check("回读后没多出凭空槽位（多出来的槽会让玩家穿上一件他没穿的）",
                GearLedger.CountOf(back) == 2, GearLedger.CountOf(back) + " 条");
            Check("方子次数回读后照旧（做过两次的那道还是两次——T046 熟练度就按这个数算）",
                CraftLedger.TimesOf(back, "porridge") == 2,
                CraftLedger.TimesOf(back, "porridge") + " 次");
            Check("回读后另一道方子也在（数组多行不是只存第一行）",
                CraftLedger.TimesOf(back, "detox-three") == 1,
                CraftLedger.TimesOf(back, "detox-three") + " 次");
            // 脱下也要存得住：Clear 走的是数组重建，往返一次后不该又长出那条。
            GearLedger.Clear(back, GearSlots.Armor);
            var after = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(back));
            SaveData.Migrate(after);
            Check("脱下之后回读仍是没有这一槽（撤销的账也是账）",
                GearLedger.Of(after, GearSlots.Armor) == null && GearLedger.CountOf(after) == 1,
                GearLedger.CountOf(after) + " 条");
        }

        static void Check(string label, bool condition, string detail = null)
        {
            if (condition) { ok++; return; }
            problems.Add(label + (string.IsNullOrEmpty(detail) ? "" : "（" + detail + "）"));
        }
    }
}
