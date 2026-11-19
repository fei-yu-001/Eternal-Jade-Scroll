using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TwelveJade.Core;
using UnityEngine;

namespace TwelveJade.Editor
{
    // 员工 J · M8：青石镇群像与对白网自动化全维夹具（80+ 断言）。
    // 批处理入口 TwelveJade.Editor.NpcDialogueShot.RunSuite。
    //
    // 设计取舍：本套件走**同步执行**，不进 Play Mode。
    // 被验的东西（配置表解析、记忆阶梯、条件求值、任务状态机、立绘资源）
    // 全部不依赖玩家循环；真正需要跑循环的 UI 几何断言归 FrontEndShotProps，
    // 那边已经钉死了"Talk box 唯一"与两套构图，这里不重复造一遍。
    // 好处是套件在任何批处理环境下都能出结果，不会被玩家循环节拍拖成超时。
    public static class NpcDialogueShot
    {
        // 五大灵魂人物：id、地图常驻点、作息窗口归属地标、是否商人。
        static readonly string[] SoulNpcs =
        {
            "npc-shenhuai-an", "npc-shitou", "npc-zhao-da", "npc-yao-lao", "huolang"
        };

        static readonly List<string> problems = new();
        static int ok;
        static string configDir = "";

        public static void RunSuite()
        {
            problems.Clear();
            ok = 0;
            configDir = Path.Combine(Application.dataPath, "Resources", "Config");
            try { Run(); }
            catch (Exception ex) { Fail("驱动异常: " + ex.Message); }
            Debug.Log(string.Format(
                "[NpcDialogueShot] suite finished, {0} problem(s), {1} ok", problems.Count, ok));
            foreach (var problem in problems) Debug.Log("[NpcDialogueShot] PROBLEM " + problem);
        }

        // ---------------- 断言基建 ----------------

        static void Check(bool condition, string label, string detail = "")
        {
            if (condition) { ok++; return; }
            problems.Add(label + (string.IsNullOrEmpty(detail) ? "" : "（" + detail + "）"));
        }

        static void Fail(string message)
        {
            problems.Add(message);
            Debug.Log("[NpcDialogueShot] FATAL " + message);
        }

        static string Read(string file) => File.ReadAllText(Path.Combine(configDir, file));

        // 坏配置必须"因为对的原因被拒"：只断言抛异常会让任何一处笔误都混过去。
        static bool Rejects(string keyword, Func<object> parse)
        {
            try { parse(); return false; }
            catch (FormatException ex) { return ex.Message.Contains(keyword); }
            catch (Exception) { return false; }
        }

        static void Run()
        {
            var items = ItemTable.Parse(Read("items.json"));
            var merchants = MerchantTable.Parse(Read("merchants.json"));
            merchants.CrossCheck(items);
            var schedules = NpcScheduleTable.Parse(Read("npc-schedules.json"));
            var town = TownMap.Parse(Read("town-map.json"));
            var profiles = NpcTable.Parse(Read("npcs.json"));
            var tree = DialogueTree.Parse(Read("dialogues.json"));
            var quests = QuestTable.Parse(Read("quests.json"));

            CheckSoulNpcs(merchants, schedules, town, profiles);
            CheckPositions(town);
            CheckMemoryLadder();
            CheckConditions();
            CheckFortune();
            CheckQuestChains(quests, items);
            CheckDialogueNetwork(tree, profiles);
            CheckRobustness();
        }

        static SaveData NewSave()
        {
            var save = new SaveData { slot = 1, characterId = "farmer", characterName = "A" };
            save.bag = InventoryRules.NewBag();
            return save;
        }

        // ---------------- 1/2：五人配置完整性与世界站位 ----------------

        static void CheckSoulNpcs(MerchantTable merchants, NpcScheduleTable schedules,
            TownMap town, NpcTable profiles)
        {
            foreach (var id in SoulNpcs)
            {
                var merchant = merchants.Find(id);
                Check(merchant != null, "商人表里应有「" + id + "」");
                if (merchant != null)
                {
                    Check(!string.IsNullOrEmpty(merchant.Name), id + " 应有姓名");
                    Check(!string.IsNullOrEmpty(merchant.Greeting), id + " 应有开场白");
                    Check(merchant.Stock.Count + merchant.Hidden.Count > 0, id + " 不该空着货架");
                    Check(merchant.Buys.Count > 0, id + " 应有收货品类");
                    Check(merchant.Memory.Count >= 4, id + " 的记忆回响应覆盖四阶",
                        merchant.Memory.Count + " 档");
                    // 记忆门槛必须互不相同：同档位会让"取最高那一档"变成取第一条。
                    Check(merchant.Memory.Select(m => m.at).Distinct().Count() == merchant.Memory.Count,
                        id + " 的记忆门槛不应重复");
                }
                var schedule = schedules.Schedules.FirstOrDefault(s => s.NpcId == id);
                Check(schedule != null, "作息表里应有「" + id + "」");
                if (schedule != null) Check(schedule.Windows.Count > 0, id + " 至少应有一个作息窗口");
                Check(town.Npcs.Any(n => n.Id == id), "世界地图上应有「" + id + "」");
                Check(profiles.Find(id) != null, "人物表里应有「" + id + "」的档案");
                // ADR-025：立绘是锦上添花，但资源必须能**按生产的加载口径**取到。
                // D-21 / T090：本仓 textureType 全局是 Default（ArtImportGuard:34），
                // Default 资源用 Load<Sprite> 必返 null——上一版这条断言就是用 Sprite 口径去问，
                // 于是"五张图都在盘上"却报五条红，而真正该被发现的其实是加载方式错了。
                var texture = Resources.Load<Texture2D>("Art/Portraits/" + id);
                Check(texture != null, id + " 的立绘槽位资源应已就位（Art/Portraits/" + id + "）");
                if (texture != null)
                {
                    // 与生产同一句话：Texture2D → Sprite.Create。
                    // 这条存在的意义是"夹具不许比生产宽松"——只查 texture 不算数，
                    // 必须证明用它能造出 Image 用得上的 Sprite。
                    var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f), 100f);
                    Check(sprite != null && sprite.rect.width == texture.width &&
                        sprite.rect.height == texture.height,
                        id + " 的立绘应能按生产口径造成 Sprite", texture.width + "×" + texture.height);
                }
            }

            CheckPortraitImportPolicy();
        }

        /// <summary>护栏（D-21 的"防止第三次来回翻"）：Portraits 的 textureType 必须与 NPC/Props 一致。
        /// 谁把某一档改成 Sprite 导入，这条就红——因为 ArtImportGuard 会在下次导入把它按回 Default，
        /// 而被按回的修复等于没修。</summary>
        static void CheckPortraitImportPolicy()
        {
            static string TypeOf(string dir, string file)
            {
                var meta = Path.Combine(Application.dataPath, "Resources", "Art", dir, file + ".meta");
                if (!File.Exists(meta)) return "<无 .meta>";
                var line = File.ReadAllLines(meta).FirstOrDefault(l => l.StartsWith("textureType:"));
                return line == null ? "<未设置（默认 0=Default）>" : line.Trim();
            }

            var portrait = TypeOf("Portraits", "npc-shenhuai-an");
            var npc = TypeOf("NPC", "boar-demon");
            var prop = TypeOf("Props", "willow");
            Check(portrait == npc && portrait == prop,
                "Portraits 的 textureType 必须与 NPC/Props 同档（全局策略 Default）",
                "Portraits=" + portrait + " NPC=" + npc + " Props=" + prop);

            // 生产端与夹具必须同一口径：界面里再出现 Load<Sprite> 取立绘，就是两套标准回来了。
            var view = Path.Combine(Application.dataPath, "Scripts", "Presentation", "FrontEndTrade.cs");
            var source = File.Exists(view) ? File.ReadAllText(view) : "";
            Check(source.Contains("Resources.Load<Texture2D>") && source.Contains("Sprite.Create"),
                "生产端立绘应走 Load<Texture2D> + Sprite.Create（D-21）");
            Check(!source.Contains("Resources.Load<Sprite>(prefix"),
                "生产端不许再用 Load<Sprite> 取 Default 档立绘（那是本轮五条红的根因）");
        }

        /// <summary>常驻点判据（T087 / D-28 裁定：删抄本，改现读）。
        ///
        /// 这里原来抄了一份 `Homes` 坐标表，真源其实是 town-map.json 的 npcs——
        /// ADR-030 把五个人重锚（药老搬去北山）之后，抄的那份就成了五条假红。
        /// 夹具里存配置的副本，等于给每次重锚埋一颗雷（开发者1号 在 86bc050 对 Props 做的是同一件事）。
        /// 所以现在只问三件**不需要副本**就能成立的事：人在不在图上、点在世界里、五个人没叠在同一点。</summary>
        static void CheckPositions(TownMap town)
        {
            var placed = new List<string>();
            foreach (var id in SoulNpcs)
            {
                var lines = town.Npcs.Where(n => n.Id == id).ToList();
                Check(lines.Count == 1,
                    id + " 在地图上应有且仅有一个常驻点（真源是 town-map.json 的 npcs）",
                    lines.Count + " 个");
                if (lines.Count != 1) continue;
                var npc = lines[0];
                Check(!float.IsNaN(npc.X) && !float.IsNaN(npc.Y) &&
                      npc.X >= 0f && npc.X <= town.WorldWidth &&
                      npc.Y >= 0f && npc.Y <= town.WorldHeight,
                    id + " 的常驻点落在世界范围内",
                    $"({npc.X},{npc.Y}) / 世界 {town.WorldWidth}×{town.WorldHeight}");
                placed.Add(npc.X + "," + npc.Y);
            }
            // 重锚最容易撞出来的事故：两个人被摆到同一个点上（玩家看到的是叠影）。
            Check(placed.Count == SoulNpcs.Length && placed.Distinct().Count() == placed.Count,
                "五个灵魂人物的常驻点两两不同（重锚不许把人叠在一起）",
                string.Join(" · ", placed));
        }

        // ---------------- 3：4 阶记忆回响状态机 ----------------

        static void CheckMemoryLadder()
        {
            var save = NewSave();
            const string id = "npc-shitou";
            Check((int)MemoryEcho.TierOf(save, id, 0) == 0, "零次交易应为陌路（Tier 0）");
            Check((int)MemoryEcho.TierOf(save, id, 1) == 0, "一次交易仍停在陌路（Tier 0）");
            Check((int)MemoryEcho.TierOf(save, id, 2) == 1, "两次交易应升熟络（Tier 1）");
            Check((int)MemoryEcho.TierOf(save, id, 4) == 1, "四次交易仍在熟络（Tier 1）");
            Check((int)MemoryEcho.TierOf(save, id, 5) == 2, "五次交易应升故交（Tier 2）");
            Check((int)MemoryEcho.TierOf(save, id, 8) == 2, "八次交易仍在故交（Tier 2）");
            Check(MemoryEcho.TierOf(save, id, 999) == MemoryTier.Friend,
                "只做买卖封顶在故交：买不成过命的交情");
            save.MarkFlag(MemoryEcho.BondFlag(id));
            Check(MemoryEcho.TierOf(save, id, 0) == MemoryTier.Sworn,
                "打上羁绊标记后应直升患难（Tier 3）");
            // D-17 / T057 收口：原先这里有 4–6 条"熟络 95 折、故交 9 折"的期望，
            // 断的是 MemoryEcho.Discount 一个从未接线的函数（实测定价只走 Reputation.Discount）。
            // 折扣 API 已删，"交情不进价"的护栏只在 InventoryEconomySuite 一处值守（T095：判据不两处写）。
            Check(MemoryEcho.Name(MemoryTier.Stranger) == "陌路" &&
                  MemoryEcho.Name(MemoryTier.Acquaintance) == "熟络" &&
                  MemoryEcho.Name(MemoryTier.Friend) == "故交" &&
                  MemoryEcho.Name(MemoryTier.Sworn) == "患难", "四阶名称应为陌路/熟络/故交/患难");
            foreach (var npc in SoulNpcs)
            {
                var fresh = NewSave();
                Check((int)MemoryEcho.TierOf(fresh, npc, 0) == 0, npc + " 初见应为陌路");
                fresh.MarkFlag(MemoryEcho.BondFlag(npc));
                Check(MemoryEcho.TierOf(fresh, npc, 0) == MemoryTier.Sworn,
                    npc + " 打上羁绊标记后应升患难");
            }
        }

        // ---------------- 4：分支条件求值 ----------------

        static void CheckConditions()
        {
            var save = NewSave();
            const string id = "npc-yao-lao";
            Check(DialogueCondition.Satisfies(save, id, 0, ""), "空条件应视为满足");
            Check(!DialogueCondition.Satisfies(save, id, 0, "memoryTier>=1"), "陌路时不该满足 memoryTier>=1");
            Check(DialogueCondition.Satisfies(save, id, 5, "memoryTier>=2"), "故交时满足 memoryTier>=2");
            Check(!DialogueCondition.Satisfies(save, id, 5, "memoryTier>=3"), "无羁绊时不满足 memoryTier>=3");
            save.MarkFlag(MemoryEcho.BondFlag(id));
            Check(DialogueCondition.Satisfies(save, id, 0, "memoryTier>=3"), "患难时满足 memoryTier>=3");
            Check(DialogueCondition.Satisfies(save, id, 9, "trades>=5"), "trades>=5 应按交易次数成立");
            Check(!DialogueCondition.Satisfies(save, id, 1, "trades>=5"), "trades>=5 在 1 次时不成立");
            save.localReputation = 20;
            Check(DialogueCondition.Satisfies(save, id, 0, "reputation>=20"), "reputation>=20 应成立");
            Check(!DialogueCondition.Satisfies(save, id, 0, "reputation>=21"), "reputation>=21 不该成立");
            save.MarkFlag("probe-flag");
            Check(DialogueCondition.Satisfies(save, id, 0, "hasFlag:probe-flag"), "hasFlag 应认已打上的标记");
            Check(!DialogueCondition.Satisfies(save, id, 0, "hasFlag:never-set"), "hasFlag 不该认没打过的标记");
            Check(!DialogueCondition.Satisfies(save, id, 0, "瞎写的条件>=1"), "认不出的条件一律判不满足");
            Check(!DialogueCondition.Satisfies(save, id, 0, "hasQuest:quest-herbal-rescue"),
                "任务未接取时 hasQuest 不该成立");
            // & 连接：全满足才算满足，差一条就不算。
            Check(DialogueCondition.Satisfies(save, id, 0, "memoryTier>=1&trades>=0"),
                "& 连接的条件全部满足时应为真");
            Check(!DialogueCondition.Satisfies(NewSave(), id, 0, "memoryTier>=1&trades>=0"),
                "& 连接的条件差一条即为假");
        }

        // ---------------- 5：沈怀安摸骨相面（十九命格断语库） ----------------

        static void CheckFortune()
        {
            Check(FortuneReading.Price == 5, "摸骨相面应收费 5 文");
            Check(FortuneReading.VerdictFor("jadeloft") == FortuneReading.UnreadableVerdict,
                "玉楼引应落「摸不透」那条断语");
            Check(FortuneReading.VerdictFor("unreadable") == FortuneReading.UnreadableVerdict,
                "无名命格应落「摸不透」那条断语");
            Check(FortuneReading.IsUnreadable("jadeloft") && FortuneReading.IsUnreadable("unreadable"),
                "隐藏超品命格应被认出");
            Check(!FortuneReading.IsUnreadable("anle"), "安乐命不该被当成摸不透");
            var distinct = new[] { "jadeloft", "anle", "renxin", "caixing" }
                .Select(FortuneReading.VerdictFor).Distinct().Count();
            Check(distinct == 4, "不同命格应给出不同断语", distinct + " 种");
            Check(!string.IsNullOrEmpty(FortuneReading.VerdictFor("不存在的命格")),
                "认不出的命格也要给一句能念的话");
            // 十九命格全覆盖：漏一条就是有人被摸了骨却没批语。
            foreach (var destiny in CharacterGen.Destinies)
                Check(FortuneReading.Knows(destiny.Id),
                    "断语库应覆盖命格 " + destiny.Id + "（" + destiny.Name + "）");
        }

        // ---------------- 6：三大机缘任务链与落盘 ----------------

        static void CheckQuestChains(QuestTable quests, ItemTable items)
        {
            foreach (var id in new[] { "quest-herbal-rescue", "quest-shitou-promise", "quest-bridge-blackmarket" })
            {
                var quest = quests.Find(id);
                Check(quest != null, "任务表里应有「" + id + "」");
                if (quest == null) continue;
                Check(quest.Objectives.Count > 0, id + " 应至少有一个目标");
                Check(quest.Chapter == 1, id + " 应属第一章");
                Check(quest.Prerequisites.Count == 0, id + " 应是自然机缘（无强制前置）");
                Check(quest.Rewards != null, id + " 应有奖励定义");
            }

            // 药圃解围：接取 → 打野猪 → 回报药老 → 完成 → 发奖入包。
            var save = NewSave();
            var rescue = quests.Find("quest-herbal-rescue");
            Check(QuestLedger.StatusOf(save, "quest-herbal-rescue") == QuestLedger.StatusAvailable ||
                  QuestLedger.StatusOf(save, "quest-herbal-rescue") == QuestLedger.StatusLocked,
                  "未接取时药圃解围不该是完成态");
            QuestLedger.RefreshAvailability(save, quests);
            Check(QuestLedger.TryStart(save, rescue), "药圃解围应能接取");
            Check(QuestLedger.StatusOf(save, "quest-herbal-rescue") == QuestLedger.StatusActive,
                "接取后应进入进行中");
            Check(QuestLedger.Advance(save, rescue, "slay-the-boar", 1), "击败野猪应推进目标");
            // 目标没达成时 TryComplete 是**抛**而不是返回 false（Core 的契约）。
            var blockedEarly = false;
            try { QuestLedger.TryComplete(save, rescue); }
            catch (InvalidOperationException) { blockedEarly = true; }
            Check(blockedEarly, "目标没做完就交差应抛异常，不许静默放行");
            Check(QuestLedger.Advance(save, rescue, "report-to-yaolao", 1), "回报药老应推进目标");
            Check(QuestLedger.TryComplete(save, rescue), "目标达成后应能完成");
            Check(QuestLedger.IsDone(save, "quest-herbal-rescue"), "完成后应记为已完成");
            var spoils = QuestLedger.GrantRewards(save, rescue, items);
            Check(spoils.itemsGranted == 3, "药圃解围应发 3 帖金疮药", spoils.itemsGranted + " 帖");
            Check(InventoryRules.Count(save.bag, "jinchuangyao") == 3, "金疮药应真的进了行囊",
                InventoryRules.Count(save.bag, "jinchuangyao") + " 帖");
            Check(save.HasFlag("herb-boy-saved"), "完成药圃解围应记下救人标记");
            Check(!QuestLedger.GrantRewards(save, rescue, items).flags.Contains("herb-boy-saved"),
                "重复发奖不该二次记账（标记幂等）");

            // 寒巷旧诺：交付后石头应升到患难阶。
            var promise = quests.Find("quest-shitou-promise");
            QuestLedger.RefreshAvailability(save, quests);
            QuestLedger.TryStart(save, promise);
            QuestLedger.Advance(save, promise, "hear-of-shitou", 1);
            QuestLedger.Advance(save, promise, "buy-a-blade", 1);
            QuestLedger.Advance(save, promise, "give-the-blade", 1);
            Check(QuestLedger.TryComplete(save, promise), "寒巷旧诺目标达成后应能完成");
            QuestLedger.GrantRewards(save, promise, items);
            Check(save.HasFlag("bonded:npc-shitou"), "完成寒巷旧诺应打上石头的羁绊标记");
            Check(MemoryEcho.TierOf(save, "npc-shitou", 0) == MemoryTier.Sworn,
                "羁绊标记应把石头抬到患难阶");
            Check(save.HasFlag("got-luck-cord"), "寒巷旧诺应给护身红绳标记");

            // 双桥夜探。
            var market = quests.Find("quest-bridge-blackmarket");
            QuestLedger.RefreshAvailability(save, quests);
            QuestLedger.TryStart(save, market);
            QuestLedger.Advance(save, market, "meet-at-bridges", 1);
            QuestLedger.Advance(save, market, "buy-the-scrap", 1);
            Check(QuestLedger.TryComplete(save, market), "双桥夜探目标达成后应能完成");
            QuestLedger.GrantRewards(save, market, items);
            Check(save.HasFlag("blackmarket-tried"), "完成双桥夜探应记下黑市试炼标记");

            // 落盘：任务状态必须经得起一次序列化往返，否则"做完回档就没了"。
            var bag = new QuestBag { quests = save.quests };
            var reloaded = JsonUtility.FromJson<QuestBag>(JsonUtility.ToJson(bag));
            var carried = reloaded.quests.FirstOrDefault(q => q.id == "quest-herbal-rescue");
            Check(carried != null && carried.status == QuestLedger.StatusCompleted,
                "任务完成状态应能经序列化往返保留", carried == null ? "丢失" : carried.status);
            Check(reloaded.quests.Count(q => q.status == QuestLedger.StatusCompleted) == 3,
                "三条任务链都应落盘为已完成",
                reloaded.quests.Count(q => q.status == QuestLedger.StatusCompleted) + " 条");
        }

        [Serializable]
        class QuestBag
        {
            public QuestState[] quests = Array.Empty<QuestState>();
        }

        // ---------------- 7：对白分支网 ----------------

        static void CheckDialogueNetwork(DialogueTree tree, NpcTable profiles)
        {
            tree.Table.CrossCheck(profiles);
            Check(true, "全部对白节点的说话人都应在人物表里");
            foreach (var npc in SoulNpcs)
            {
                var entry = tree.EntryNodeFor(npc);
                Check(!string.IsNullOrEmpty(entry), npc + " 应有一个开场节点");
                if (string.IsNullOrEmpty(entry)) continue;
                Check(tree.Find(entry).NpcId == npc, npc + " 的开场节点应挂在自己名下");
                Check(NewSave() != null && tree.BranchOptions(NewSave(),
                    DialogueRules.Open(NewSave(), tree.Table, npc, entry), 0).Count > 0,
                    npc + " 陌路时也该有话可说");
            }
            Check(tree.EntryNodeFor("npc-shenhuai-an") == "shen-intro", "沈怀安开场应是 shen-intro");
            Check(tree.EntryNodeFor("npc-shitou") == "shitou-intro", "石头开场应是 shitou-intro");

            // 记忆阶越高，能说的话越多——分支网不能是摆设。
            var save = NewSave();
            var view = DialogueRules.Open(save, tree.Table, "npc-shenhuai-an", "shen-intro");
            var at0 = tree.BranchOptions(save, view, 0).Count;
            var at2 = tree.BranchOptions(save, view, 5).Count;
            save.MarkFlag(MemoryEcho.BondFlag("npc-shenhuai-an"));
            var at3 = tree.BranchOptions(save, view, 0).Count;
            Check(at0 < at2, "故交应比陌路多出可选的分支", at0 + " → " + at2);
            Check(at2 < at3, "患难应比故交多出可选的分支", at2 + " → " + at3);

            // 动作挂载：认不出动作，分支就只是台词而不是功能入口。
            var intro = tree.Find("shen-intro");
            var fate = intro.Options.FirstOrDefault(o => o.Text.Contains("摸骨"));
            Check(fate != null && tree.ActionOf("shen-intro", fate) == "triggerFateCheck",
                "沈怀安的摸骨选项应挂 triggerFateCheck");
            var shop = intro.Options.FirstOrDefault(o => o.Text == "做买卖");
            Check(shop != null && tree.ActionOf("shen-intro", shop) == "openShop",
                "做买卖选项应挂 openShop");
            var rescue = tree.Find("yaolao-intro").Options.FirstOrDefault(o => o.NextNode == "yaolao-rescue");
            Check(rescue != null && tree.ActionOf("yaolao-intro", rescue) == "acceptQuest",
                "药老的接任务选项应挂 acceptQuest");
            // 石头的黑匣与那块石头：卷一的核心伏笔必须在网里，不能只是配置文件里的一句话。
            Check(tree.Find("shitou-box") != null, "石头应有黑匣子分支节点");
            Check(tree.Find("shitou-stone") != null, "石头应有「那块石头」的分支节点");
            Check(tree.Find("shen-secret") != null, "沈怀安应有卷末伏笔分支节点");
        }

        // ---------------- 8：异常防御 ----------------

        static void CheckRobustness()
        {
            Check(Rejects("没有节点", () => DialogueTree.Parse("{\"nodes\":[]}")), "空对白表应被拒");
            Check(Rejects("缺少字段", () => DialogueTree.Parse("{\"nodes\":[{\"id\":\"a\"}]}")),
                "缺字段的对白节点应被拒");
            Check(Rejects("指向不存在的节点", () => DialogueTree.Parse(
                "{\"nodes\":[{\"id\":\"a\",\"npc\":\"huolang\",\"text\":\"t\"," +
                "\"options\":[{\"text\":\"x\",\"next\":\"ghost\"}]}]}")), "断链对白应被拒");
            Check(Rejects("没有作息", () => NpcScheduleTable.Parse("{\"schedules\":[]}")), "空作息表应被拒");
            Check(Rejects("时间非法", () => NpcScheduleTable.Parse(
                "{\"schedules\":[{\"id\":\"x\",\"npcId\":\"huolang\",\"windows\":" +
                "[{\"id\":\"w\",\"start\":900,\"end\":100,\"place\":\"market\"}]}]}")),
                "首尾倒置的作息窗口应被拒");
            Check(MemoryEcho.TierOf(null, "npc-shitou", 0) == MemoryTier.Stranger,
                "没有存档时记忆阶应回落陌路，不抛异常");
        }
    }
}
