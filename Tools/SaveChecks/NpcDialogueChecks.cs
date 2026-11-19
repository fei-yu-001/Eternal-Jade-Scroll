using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TwelveJade.Core;

namespace TwelveJade.Core
{
    // 员工 J · M8（控制台版）：与 Assets/Editor/NpcDialogueShot.cs 同一套判据的
    // Core 侧断言，跑在 SaveChecks 里（dotnet run --project Tools/SaveChecks）。
    //
    // 为什么两份：NpcDialogueShot 是 Unity 编辑器的正式夹具（含立绘资源与 UI 断言），
    // 只能在 Unity 批处理里跑；这套把它**不依赖 Unity 的那部分**搬进控制台，
    // 于是在没有 Unity 的机器上，群像与对白网的判据依然有人值守。
    // 判据内容保持一致，不另立一套口径。
    public static class NpcDialogueChecks
    {
        static readonly string[] SoulNpcs =
            { "npc-shenhuai-an", "npc-shitou", "npc-zhao-da", "npc-yao-lao", "huolang" };

        public static void Run(string configDir, ItemTable items)
        {
            // O0 marker：ConfigChecks.cs 把本套判据关在 `if (worldPhase)` 里面。
            // 一旦被跳过，SaveChecks 依旧全过，而没人会察觉——所以第一条必须能被数出来。
            // 这一行由员工 O 应任务书 O2 之要求添加（本文件属员工 J，由 M 的收编笔入库）。
            Check("判据入口被调用: NpcDialogueChecks", true, configDir);

            var merchants = MerchantTable.Parse(Read(configDir, "merchants.json"));
            merchants.CrossCheck(items);
            var schedules = NpcScheduleTable.Parse(Read(configDir, "npc-schedules.json"));
            var profiles = NpcTable.Parse(Read(configDir, "npcs.json"));
            var tree = DialogueTree.Parse(Read(configDir, "dialogues.json"));
            var quests = QuestTable.Parse(Read(configDir, "quests.json"));

            CheckNpcs(merchants, schedules, profiles);
            CheckMemoryLadder();
            CheckConditions();
            CheckFortune();
            CheckQuestChains(quests, items);
            CheckDialogueNetwork(tree, profiles);
            RunDefaultRelationGateChecks(configDir);
            CheckRobustness();
        }

        static string Read(string dir, string file) => File.ReadAllText(Path.Combine(dir, file));

        static SaveData NewSave()
        {
            var save = new SaveData { slot = 1, characterId = "farmer", characterName = "A" };
            save.bag = InventoryRules.NewBag();
            return save;
        }

        static void CheckNpcs(MerchantTable merchants, NpcScheduleTable schedules, NpcTable profiles)
        {
            foreach (var id in SoulNpcs)
            {
                var merchant = merchants.Find(id);
                Check("群像: 商人表含 " + id, merchant != null);
                if (merchant != null)
                {
                    Check("群像: " + id + " 有姓名", !string.IsNullOrEmpty(merchant.Name));
                    Check("群像: " + id + " 有开场白", !string.IsNullOrEmpty(merchant.Greeting));
                    Check("群像: " + id + " 货架非空", merchant.Stock.Count + merchant.Hidden.Count > 0);
                    Check("群像: " + id + " 有收货品类", merchant.Buys.Count > 0);
                    Check("群像: " + id + " 记忆覆盖四阶", merchant.Memory.Count >= 4);
                    Check("群像: " + id + " 记忆门槛不重复",
                        merchant.Memory.Select(m => m.at).Distinct().Count() == merchant.Memory.Count);
                }
                Check("群像: 作息表含 " + id, schedules.Schedules.Any(s => s.NpcId == id));
                Check("群像: 人物表含 " + id, profiles.Find(id) != null);
            }
            // 货郎昼夜迁徙：夜间窗口必须真指双桥。
            var peddler = schedules.Schedules.FirstOrDefault(s => s.NpcId == "huolang");
            Check("群像: 货郎有夜间黑市窗口",
                peddler != null && peddler.Windows.Any(w => w.Black && w.PlaceId == "bridges"));
            Check("群像: 四个新人物都有白日可交易窗口", SoulNpcs.Where(n => n != "huolang").All(
                n => schedules.Schedules.Any(s => s.NpcId == n && s.Windows.Any(w => w.Tradeable))));
        }

        static void CheckMemoryLadder()
        {
            var save = NewSave();
            const string id = "npc-shitou";
            Check("记忆: 零次交易=陌路", (int)MemoryEcho.TierOf(save, id, 0) == 0);
            Check("记忆: 一次交易=陌路", (int)MemoryEcho.TierOf(save, id, 1) == 0);
            Check("记忆: 两次交易=熟络", (int)MemoryEcho.TierOf(save, id, 2) == 1);
            Check("记忆: 四次交易=熟络", (int)MemoryEcho.TierOf(save, id, 4) == 1);
            Check("记忆: 五次交易=故交", (int)MemoryEcho.TierOf(save, id, 5) == 2);
            Check("记忆: 八次交易=故交", (int)MemoryEcho.TierOf(save, id, 8) == 2);
            Check("记忆: 只做买卖封顶故交", MemoryEcho.TierOf(save, id, 999) == MemoryTier.Friend);
            save.MarkFlag(MemoryEcho.BondFlag(id));
            Check("记忆: 羁绊标记直升患难", MemoryEcho.TierOf(save, id, 0) == MemoryTier.Sworn);
            // D-17 / T057 收口：原先这里有 4–6 条"熟络 95 折、故交 9 折"的期望，
            // 断的是 MemoryEcho.Discount 一个从未接线的函数（实测定价只走 Reputation.Discount）。
            // 折扣 API 已删，"交情不进价"的护栏只在 InventoryEconomySuite 一处值守（T095：判据不两处写）。
            foreach (var npc in SoulNpcs)
            {
                var fresh = NewSave();
                fresh.MarkFlag(MemoryEcho.BondFlag(npc));
                Check("记忆: " + npc + " 可落患难阶",
                    MemoryEcho.TierOf(fresh, npc, 0) == MemoryTier.Sworn);
            }
        }

        static void CheckConditions()
        {
            var save = NewSave();
            const string id = "npc-yao-lao";
            Check("条件: 空条件视为满足", DialogueCondition.Satisfies(save, id, 0, ""));
            Check("条件: 陌路不满足 memoryTier>=1", !DialogueCondition.Satisfies(save, id, 0, "memoryTier>=1"));
            Check("条件: 故交满足 memoryTier>=2", DialogueCondition.Satisfies(save, id, 5, "memoryTier>=2"));
            Check("条件: 无羁绊不满足 memoryTier>=3", !DialogueCondition.Satisfies(save, id, 5, "memoryTier>=3"));
            save.MarkFlag(MemoryEcho.BondFlag(id));
            Check("条件: 患难满足 memoryTier>=3", DialogueCondition.Satisfies(save, id, 0, "memoryTier>=3"));
            Check("条件: trades>=5 成立", DialogueCondition.Satisfies(save, id, 9, "trades>=5"));
            Check("条件: trades>=5 在 1 次时不成立", !DialogueCondition.Satisfies(save, id, 1, "trades>=5"));
            save.localReputation = 20;
            Check("条件: reputation>=20 成立", DialogueCondition.Satisfies(save, id, 0, "reputation>=20"));
            Check("条件: reputation>=21 不成立", !DialogueCondition.Satisfies(save, id, 0, "reputation>=21"));
            save.MarkFlag("probe-flag");
            Check("条件: hasFlag 认已打标记", DialogueCondition.Satisfies(save, id, 0, "hasFlag:probe-flag"));
            Check("条件: hasFlag 不认未打标记", !DialogueCondition.Satisfies(save, id, 0, "hasFlag:never-set"));
            Check("条件: 认不出的条件判不满足", !DialogueCondition.Satisfies(save, id, 0, "瞎写>=1"));
            Check("条件: 未接取时 hasQuest 不成立",
                !DialogueCondition.Satisfies(save, id, 0, "hasQuest:quest-herbal-rescue"));
            Check("条件: & 全满足为真", DialogueCondition.Satisfies(save, id, 0, "memoryTier>=1&trades>=0"));
            Check("条件: & 差一条为假", !DialogueCondition.Satisfies(NewSave(), id, 0, "memoryTier>=1&trades>=0"));
        }

        static void CheckFortune()
        {
            Check("摸骨: 收费 5 文", FortuneReading.Price == 5);
            Check("摸骨: 玉楼引落摸不透", FortuneReading.VerdictFor("jadeloft") == FortuneReading.UnreadableVerdict);
            Check("摸骨: 无名落摸不透", FortuneReading.VerdictFor("unreadable") == FortuneReading.UnreadableVerdict);
            Check("摸骨: 隐藏超品被认出",
                FortuneReading.IsUnreadable("jadeloft") && FortuneReading.IsUnreadable("unreadable"));
            Check("摸骨: 安乐命不算摸不透", !FortuneReading.IsUnreadable("anle"));
            Check("摸骨: 不同命格不同断语",
                new[] { "jadeloft", "anle", "renxin", "caixing" }
                    .Select(FortuneReading.VerdictFor).Distinct().Count() == 4);
            Check("摸骨: 认不出的命格也有批语", !string.IsNullOrEmpty(FortuneReading.VerdictFor("不存在")));
            foreach (var destiny in CharacterGen.Destinies)
                Check("摸骨: 覆盖命格 " + destiny.Id, FortuneReading.Knows(destiny.Id));
        }

        static void CheckQuestChains(QuestTable quests, ItemTable items)
        {
            var ids = new[] { "quest-herbal-rescue", "quest-shitou-promise", "quest-bridge-blackmarket" };
            foreach (var id in ids)
            {
                var def = quests.Find(id);
                Check("任务: 表内含 " + id, def != null);
                if (def == null) continue;
                Check("任务: " + id + " 有目标", def.Objectives.Count > 0);
                Check("任务: " + id + " 属第一章", def.Chapter == 1);
                Check("任务: " + id + " 是自然机缘（无强制前置）", def.Prerequisites.Count == 0);
            }

            var save = NewSave();
            var rescue = quests.Find("quest-herbal-rescue");
            QuestLedger.RefreshAvailability(save, quests);
            Check("任务: 药圃解围可接取", QuestLedger.TryStart(save, rescue));
            Check("任务: 接取后为进行中",
                QuestLedger.StatusOf(save, "quest-herbal-rescue") == QuestLedger.StatusActive);
            Check("任务: 击败野猪推进目标", QuestLedger.Advance(save, rescue, "slay-the-boar", 1));
            // 目标没达成时 TryComplete 是**抛**而不是返回 false（Core 的契约：静默返回 false
            // 会让"做完了却没结算"永远查不出来）。断言必须按这个契约写，不能写成取反。
            var blockedEarly = false;
            try { QuestLedger.TryComplete(save, rescue); }
            catch (InvalidOperationException) { blockedEarly = true; }
            Check("任务: 目标未达成时交差应抛异常而非静默放行", blockedEarly);
            Check("任务: 回报药老推进目标", QuestLedger.Advance(save, rescue, "report-to-yaolao", 1));
            Check("任务: 药圃解围可完成", QuestLedger.TryComplete(save, rescue));
            var spoils = QuestLedger.GrantRewards(save, rescue, items);
            Check("任务: 药圃解围发 3 帖金疮药", spoils.itemsGranted == 3, spoils.itemsGranted + " 帖");
            Check("任务: 金疮药真的进了行囊", InventoryRules.Count(save.bag, "jinchuangyao") == 3);
            Check("任务: 记下救人标记", save.HasFlag("herb-boy-saved"));
            Check("任务: 重复发奖不二次记账",
                !QuestLedger.GrantRewards(save, rescue, items).flags.Contains("herb-boy-saved"));

            var promise = quests.Find("quest-shitou-promise");
            QuestLedger.RefreshAvailability(save, quests);
            QuestLedger.TryStart(save, promise);
            QuestLedger.Advance(save, promise, "hear-of-shitou", 1);
            QuestLedger.Advance(save, promise, "buy-a-blade", 1);
            QuestLedger.Advance(save, promise, "give-the-blade", 1);
            Check("任务: 寒巷旧诺可完成", QuestLedger.TryComplete(save, promise));
            QuestLedger.GrantRewards(save, promise, items);
            Check("任务: 打上石头羁绊标记", save.HasFlag("bonded:npc-shitou"));
            Check("任务: 羁绊把石头抬到患难阶",
                MemoryEcho.TierOf(save, "npc-shitou", 0) == MemoryTier.Sworn);
            Check("任务: 给护身红绳标记", save.HasFlag("got-luck-cord"));

            var market = quests.Find("quest-bridge-blackmarket");
            QuestLedger.RefreshAvailability(save, quests);
            QuestLedger.TryStart(save, market);
            QuestLedger.Advance(save, market, "meet-at-bridges", 1);
            QuestLedger.Advance(save, market, "buy-the-scrap", 1);
            Check("任务: 双桥夜探可完成", QuestLedger.TryComplete(save, market));
            QuestLedger.GrantRewards(save, market, items);
            Check("任务: 记下黑市试炼标记", save.HasFlag("blackmarket-tried"));
            Check("任务: 三条机缘链全部完成",
                ids.All(id => QuestLedger.IsDone(save, id)));
        }

        static void CheckDialogueNetwork(DialogueTree tree, NpcTable profiles)
        {
            tree.Table.CrossCheck(profiles);
            Check("对白: 全部说话人在人物表内", true);
            foreach (var npc in SoulNpcs)
            {
                var entry = tree.EntryNodeFor(npc);
                Check("对白: " + npc + " 有开场节点", !string.IsNullOrEmpty(entry));
                if (string.IsNullOrEmpty(entry)) continue;
                Check("对白: " + npc + " 开场挂自己名下", tree.Find(entry).NpcId == npc);
                var save = NewSave();
                var count = tree.BranchOptions(save,
                    DialogueRules.Open(save, tree.Table, npc, entry), 0).Count;
                Check("对白: " + npc + " 陌路也有话可说", count > 0, count + " 支");
            }
            var fresh = NewSave();
            var view = DialogueRules.Open(fresh, tree.Table, "npc-shenhuai-an", "shen-intro");
            var at0 = tree.BranchOptions(fresh, view, 0).Count;
            var at2 = tree.BranchOptions(fresh, view, 5).Count;
            fresh.MarkFlag(MemoryEcho.BondFlag("npc-shenhuai-an"));
            var at3 = tree.BranchOptions(fresh, view, 0).Count;
            Check("对白: 故交比陌路多分支", at0 < at2, at0 + " → " + at2);
            Check("对白: 患难比故交多分支", at2 < at3, at2 + " → " + at3);

            var intro = tree.Find("shen-intro");
            var fate = intro.Options.FirstOrDefault(o => o.Text.Contains("摸骨"));
            Check("对白: 摸骨挂 triggerFateCheck",
                fate != null && tree.ActionOf("shen-intro", fate) == "triggerFateCheck");
            var shop = intro.Options.FirstOrDefault(o => o.Text == "做买卖");
            Check("对白: 做买卖挂 openShop",
                shop != null && tree.ActionOf("shen-intro", shop) == "openShop");
            var rescue = tree.Find("yaolao-intro").Options.FirstOrDefault(o => o.NextNode == "yaolao-rescue");
            Check("对白: 药老初问不接任务", rescue != null && tree.ActionOf("yaolao-intro", rescue) == "");
            var confirm=tree.Find("yaolao-rescue").Options.First(o=>o.NextNode=="yaolao-accept");
            Check("对白: 我去看看确认才接任务",tree.ActionOf("yaolao-rescue",confirm)=="acceptQuest");
            Check("对白: 石头黑匣节点在网内", tree.Find("shitou-box") != null);
            Check("对白: 那块石头节点在网内", tree.Find("shitou-stone") != null);
            Check("对白: 沈怀安卷末伏笔节点在网内", tree.Find("shen-secret") != null);
        }


        // Regression for refusing Yao Lao and reopening his real entry node.
        public static void RunDefaultRelationGateChecks(string configDir)
        {
            const string npc = "npc-yao-lao", quest = "quest-herbal-rescue";
            var tree = DialogueTree.Parse(Read(configDir, "dialogues.json"));
            var profiles = NpcTable.Parse(Read(configDir, "npcs.json"));
            var clues = ClueTable.Parse(Read(configDir, "clues.json"));
            var quests = QuestTable.Parse(Read(configDir, "quests.json"));
            var save = NewSave();
            QuestLedger.RefreshAvailability(save, quests);
            var when = new WorldTime { day = 1, minuteOfDay = 600 };
            var view = DialogueRules.Open(save, tree.Table, npc, tree.EntryNodeFor(npc));
            view = DialogueRules.Choose(save, tree.Table, profiles, clues, view,
                view.Options.ToList().FindIndex(o => o.NextNode == "yaolao-rescue"), when, out _);
            view = DialogueRules.Choose(save, tree.Table, profiles, clues, view,
                view.Options.ToList().FindIndex(o => o.SocialEvent == "RefusedHelp"), when, out _);
            var relation = NpcLedger.RelationOf(save, npc);
            Check("对白缺省门槛: 真实拒绝保留负好感与记忆", view.NodeId == "yaolao-bye" &&
                relation.goodwill < 0 && NpcLedger.HasMemory(save, npc, "RefusedHelp"),
                "goodwill=" + relation.goodwill + ", trust=" + relation.trust);
            Check("对白缺省门槛: 拒绝不接任务", QuestLedger.StatusOf(save, quest) == QuestLedger.StatusAvailable);
            var before = RelationGateSnapshot(save);
            view = DialogueRules.Open(save, tree.Table, npc, tree.EntryNodeFor(npc));
            var options = tree.BranchOptions(save, view, 0, quests);
            Check("对白缺省门槛: 拒绝后仍可询问买卖告辞", new[] { "yaolao-rescue", "yaolao-shop", "yaolao-bye" }
                .All(next => options.Any(o => o.NextNode == next)), "visible=" + string.Join(",", options.Select(o => o.NextNode)));
            Check("对白缺省门槛: 未完成任务仍不可交付", options.All(o => o.NextNode != "yaolao-thanks"));
            Check("对白缺省门槛: 重开及查询无副作用", RelationGateSnapshot(save) == before);
            view = DialogueRules.Choose(save, tree.Table, profiles, clues, view,
                view.Options.ToList().FindIndex(o => o.NextNode == "yaolao-rescue"), when, out _);
            var confirm = tree.BranchOptions(save, view, 0, quests).Single(o => o.NextNode == "yaolao-accept");
            Check("对白缺省门槛: 重新询问仍待真实确认", QuestLedger.StatusOf(save, quest) == QuestLedger.StatusAvailable &&
                tree.ActionOf(view.NodeId, confirm) == "acceptQuest");
            view = DialogueRules.Choose(save, tree.Table, profiles, clues, view, view.Options.ToList().IndexOf(confirm), when, out _);
            Check("对白缺省门槛: 拒绝后仍可确认接取", view.NodeId == "yaolao-accept" &&
                QuestLedger.TryStart(save, quests.Find(quest)) && QuestLedger.StatusOf(save, quest) == QuestLedger.StatusActive);

            var gated = DialogueTable.Parse("{\"nodes\":[{\"id\":\"entry\",\"npc\":\"npc-yao-lao\",\"text\":\"t\",\"options\":[" +
                "{\"text\":\"free\",\"next\":\"entry\"}," +
                "{\"text\":\"trust-zero\",\"next\":\"entry\",\"minTrust\":0}," +
                "{\"text\":\"goodwill-zero\",\"next\":\"entry\",\"minGoodwill\":0}," +
                "{\"text\":\"positive\",\"next\":\"entry\",\"minTrust\":5,\"minGoodwill\":7}," +
                "{\"text\":\"negative\",\"next\":\"entry\",\"minTrust\":-10,\"minGoodwill\":-10}," +
                "{\"text\":\"flag\",\"next\":\"entry\",\"requiredFlags\":[\"open-gate\"]}," +
                "{\"text\":\"curious\",\"next\":\"entry\",\"curiousOnly\":true}]}]}");
            var fixture = NewSave();
            var state = NpcLedger.Of(fixture, profiles.Find(npc));
            Func<string[]> visible = () => DialogueRules.Open(fixture, gated, npc, "entry").Options.Select(o => o.Text).ToArray();
            state.relation = new NpcRelation(-100, -100, 0);
            Check("对白缺省门槛: 最低关系只保留无条件分支", visible().SequenceEqual(new[] { "free" }));
            state.relation = new NpcRelation(-11, -10, 0);
            Check("对白缺省门槛: 显式负数门槛仍拒绝低一格", !visible().Contains("negative"));
            state.relation = new NpcRelation(-10, -10, 0);
            Check("对白缺省门槛: 显式负数边界通过", visible().Contains("negative"));
            Check("对白缺省门槛: 显式零不能当未配置", !visible().Contains("trust-zero") && !visible().Contains("goodwill-zero"));
            state.relation = new NpcRelation(-10, 0, 0);
            Check("对白缺省门槛: 信任零只约束信任", visible().Contains("trust-zero") && !visible().Contains("goodwill-zero"));
            state.relation = new NpcRelation(0, -10, 0);
            Check("对白缺省门槛: 好感零只约束好感", !visible().Contains("trust-zero") && visible().Contains("goodwill-zero"));
            state.relation = new NpcRelation(6, 5, 0);
            Check("对白缺省门槛: 显式正数仍要求两个轴", !visible().Contains("positive"));
            state.relation = new NpcRelation(7, 5, 0);
            Check("对白缺省门槛: 显式正数边界通过", visible().Contains("positive"));
            Check("对白缺省门槛: 标记与词条门控保留", !visible().Contains("flag") && !visible().Contains("curious"));
            fixture.MarkFlag("open-gate"); fixture.traits = new[] { DialogueRules.CuriousTrait };
            Check("对白缺省门槛: 满足标记与词条后可见", visible().Contains("flag") && visible().Contains("curious"));
            before = RelationGateSnapshot(fixture); visible(); visible();
            Check("对白缺省门槛: 关系查询无副作用", RelationGateSnapshot(fixture) == before);
        }

        static string RelationGateSnapshot(SaveData save) => System.Text.Json.JsonSerializer.Serialize(save,
            new System.Text.Json.JsonSerializerOptions { IncludeFields = true });

        static bool Rejects(string keyword, Func<object> parse)
        {
            try { parse(); return false; }
            catch (FormatException ex) { return ex.Message.Contains(keyword); }
            catch (Exception) { return false; }
        }

        static void CheckRobustness()
        {
            Check("防御: 空对白表被拒", Rejects("没有节点", () => DialogueTree.Parse("{\"nodes\":[]}")));
            Check("防御: 缺字段对白节点被拒",
                Rejects("缺少字段", () => DialogueTree.Parse("{\"nodes\":[{\"id\":\"a\"}]}")));
            Check("防御: 断链对白被拒", Rejects("指向不存在的节点", () => DialogueTree.Parse(
                "{\"nodes\":[{\"id\":\"a\",\"npc\":\"huolang\",\"text\":\"t\"," +
                "\"options\":[{\"text\":\"x\",\"next\":\"ghost\"}]}]}")));
            Check("防御: 空作息表被拒", Rejects("没有作息", () => NpcScheduleTable.Parse("{\"schedules\":[]}")));
            Check("防御: 首尾倒置窗口被拒", Rejects("时间非法", () => NpcScheduleTable.Parse(
                "{\"schedules\":[{\"id\":\"x\",\"npcId\":\"huolang\",\"windows\":" +
                "[{\"id\":\"w\",\"start\":900,\"end\":100,\"place\":\"market\"}]}]}")));
            Check("防御: 无存档时记忆阶回落陌路",
                MemoryEcho.TierOf(null, "npc-shitou", 0) == MemoryTier.Stranger);
        }

        // 与本项目其余 *Checks 同一套约定：每条断言自己打印 PASS，失败即抛。
        static void Check(string name, bool condition, string detail = null)
        {
            if (!condition) throw new Exception("FAIL: " + name + (detail == null ? "" : "（" + detail + "）"));
            Console.WriteLine("PASS: " + name + (detail == null ? "" : "（" + detail + "）"));
        }
    }
}
