using System;
using System.Collections.Generic;
using System.Linq;

namespace TwelveJade.Core
{
    // 任务（大纲十一 / 任务包 M5-01）：id、章节、目标、前置、奖励与后续全在表里，
    // 状态流转只经 QuestLedger——UI 不允许直接写 status，也就没有"完成判断散落在界面"的写法。
    // 这一版只立数据与状态骨架：分支条件等剧情审核后再定。
    // 目标文案不在此处——它当前没有任何消费者（没有界面读它），而"有解析没消费"的字段只制造假象：
    // 填了字符也没人显示。真要上目标文案时，把它与读它的那处界面一起加回来（producer 与 consumer 同笔）。
    public sealed class QuestObjective
    {
        public QuestObjective(string id, string type, string target, int count)
        {
            Id = id; Type = type; Target = target; Count = count;
        }

        public string Id { get; }
        // 目标类型（talk / collect / defeat / reach …）：由内容侧解释，Core 只保证非空且唯一。
        public string Type { get; }
        // 目标指向的对象（NPC id、物品 id、遭遇 id…），空表示不指向具体对象。
        public string Target { get; }
        // 需要达成的数量；0 与 1 都表示"做一次即可"。
        public int Count { get; }
    }

    public sealed class QuestReward
    {
        public QuestReward(int coins, ItemStack[] items, string[] flags)
        {
            Coins = coins; Items = items; Flags = flags;
        }

        public int Coins { get; }
        // 奖励物品：与 ItemTable 对表，任务物只能经剧情显式发放，不进战利品。
        public ItemStack[] Items { get; }
        // 一次性标记：发奖时按 MarkFlag 记档，重复发奖不会二次生效。
        public string[] Flags { get; }
    }

    public sealed class QuestDef
    {
        public QuestDef(string id, string title, int chapter, string initialStatus,
            List<QuestObjective> objectives, List<string> prerequisites,
            QuestReward rewards, string next, string giverNpc = "", string turnInNpc = "")
        {
            Id = id; Title = title; Chapter = chapter; InitialStatus = initialStatus;
            Objectives = objectives; Prerequisites = prerequisites; Rewards = rewards; Next = next;
            GiverNpc = giverNpc ?? ""; TurnInNpc = turnInNpc ?? "";
        }

        public string Id { get; }
        public string Title { get; }
        public int Chapter { get; }
        // 表里声明的初始状态：新玩家世界的开局状态（首个任务 Available，其余由前置推出）。
        public string InitialStatus { get; }
        public IReadOnlyList<QuestObjective> Objectives { get; }
        public IReadOnlyList<string> Prerequisites { get; }
        public QuestReward Rewards { get; }
        // 后续任务 id：空表示主线在此断（章节末或待补）。
        public string Next { get; }
        public string GiverNpc { get; }
        public string TurnInNpc { get; }
    }

    public sealed class QuestTable
    {
        readonly List<QuestDef> quests;

        public QuestTable(List<QuestDef> quests) { this.quests = quests; }

        public IReadOnlyList<QuestDef> Quests => quests;
        public QuestDef Find(string id) => string.IsNullOrEmpty(id) ? null : quests.FirstOrDefault(q => q.Id == id);

        public static QuestTable Parse(string json)
        {
            if (!Json.TryParse(json, out var root, out var error))
                throw new FormatException("任务表读取失败：" + error);
            if (root.kind != JsonValue.Kind.Object) throw new FormatException("任务表应是一个对象。");

            var quests = new List<QuestDef>();
            foreach (var entry in Elements(root, "quests"))
            {
                var id = Required(entry, "id");
                if (quests.Any(q => q.Id == id)) throw new FormatException("任务 id 重复：" + id);
                var chapter = entry["chapter"].AsInt(1);
                if (chapter < 1 || chapter > 99) throw new FormatException("任务 " + id + " 的章节需在 1–99 之间。");
                var status = entry["status"].AsString(QuestLedger.StatusAvailable);
                if (!QuestLedger.IsKnownStatus(status))
                    throw new FormatException("任务 " + id + " 的初始状态未定义：" + status);

                var objectives = new List<QuestObjective>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var line in Elements(entry, "objectives"))
                {
                    var objectiveId = Required(line, "id", "任务 " + id + " 的目标");
                    if (!seen.Add(objectiveId))
                        throw new FormatException("任务 " + id + " 的目标 id 重复：" + objectiveId);
                    var count = line["count"].AsInt(1);
                    if (count < 0) throw new FormatException("任务 " + id + " 的目标 " + objectiveId + " 数量不能为负。");
                    objectives.Add(new QuestObjective(objectiveId, Required(line, "type", "任务 " + id + " 的目标 " + objectiveId),
                        line["target"].AsString(""), count));
                }

                var prerequisites = new List<string>();
                foreach (var value in entry["prerequisites"].Items)
                {
                    var prerequisite = value.AsString("").Trim();
                    if (prerequisite.Length == 0) throw new FormatException("任务 " + id + " 的前置任务不能为空串。");
                    if (prerequisite == id) throw new FormatException("任务 " + id + " 不能把自己列为前置。");
                    prerequisites.Add(prerequisite);
                }
                if (prerequisites.Distinct().Count() != prerequisites.Count)
                    throw new FormatException("任务 " + id + " 的前置任务重复。");

                var coins = entry["rewards"].IsNull ? 0 : entry["rewards"]["coins"].AsInt(0);
                if (coins < 0) throw new FormatException("任务 " + id + " 的奖励铜钱不能为负。");
                var rewardItems = new List<ItemStack>();
                if (!entry["rewards"].IsNull)
                    foreach (var line in Elements(entry["rewards"], "items"))
                    {
                        var itemId = Required(line, "itemId", "任务 " + id + " 的奖励");
                        var count = line["count"].AsInt(1);
                        if (count < 1) throw new FormatException("任务 " + id + " 的奖励物品 " + itemId + " 数量需至少为 1。");
                        rewardItems.Add(new ItemStack(itemId, count));
                    }
                var flags = new List<string>();
                if (!entry["rewards"].IsNull)
                    foreach (var value in entry["rewards"]["flags"].Items)
                    {
                        var flag = value.AsString("").Trim();
                        if (flag.Length == 0) throw new FormatException("任务 " + id + " 的奖励标记不能为空串。");
                        if (flags.Contains(flag)) throw new FormatException("任务 " + id + " 的奖励标记重复：" + flag);
                        flags.Add(flag);
                    }

                quests.Add(new QuestDef(id, Required(entry, "title", "任务 " + id), chapter, status, objectives,
                    prerequisites, new QuestReward(coins, rewardItems.ToArray(), flags.ToArray()),
                    entry["next"].AsString("").Trim(), OptionalNpc(entry,"giverNpc",id), OptionalNpc(entry,"turnInNpc",id)));
            }
            if (quests.Count == 0) throw new FormatException("任务表里没有任务。");

            // 前置与后续都要指向真实存在的任务：串错一条，整条主线就静默断了。
            foreach (var quest in quests)
            {
                foreach (var prerequisite in quest.Prerequisites)
                    if (quests.All(q => q.Id != prerequisite))
                        throw new FormatException("任务 " + quest.Id + " 的前置任务未定义：" + prerequisite);
                if (!string.IsNullOrEmpty(quest.Next) && quests.All(q => q.Id != quest.Next))
                    throw new FormatException("任务 " + quest.Id + " 的后续任务未定义：" + quest.Next);
            }
            RejectCycles(quests);
            return new QuestTable(quests);
        }

        // 前置与 next 合起来不得成环：a 依赖 b、b 依赖 a 这种"互为前置"会让两个任务
        // 永远满足不了前置条件，整条主线静默死锁。只查直接自引用是不够的，要顺链走到底。
        static void RejectCycles(List<QuestDef> quests)
        {
            var byId = quests.ToDictionary(q => q.Id);
            foreach (var quest in quests)
            {
                var prerequisites = new List<string> { quest.Id };
                var cursor = quest.Prerequisites.Count > 0 ? quest.Prerequisites[0] : null;
                while (cursor != null)
                {
                    if (cursor == quest.Id)
                        throw new FormatException("任务 " + quest.Id + " 的前置链成环：" +
                            string.Join(" → ", prerequisites) + " → " + cursor);
                    if (prerequisites.Contains(cursor))
                        throw new FormatException("任务 " + quest.Id + " 的前置链成环：" +
                            string.Join(" → ", prerequisites) + " → " + cursor);
                    prerequisites.Add(cursor);
                    if (!byId.TryGetValue(cursor, out var next)) break; // 上面已校验存在
                    cursor = next.Prerequisites.Count > 0 ? next.Prerequisites[0] : null;
                }
                if (string.IsNullOrEmpty(quest.Next)) continue;
                var nextChain = new List<string> { quest.Id };
                var follow = quest.Next;
                while (!string.IsNullOrEmpty(follow))
                {
                    if (nextChain.Contains(follow))
                        throw new FormatException("任务 " + quest.Id + " 的后续链成环：" +
                            string.Join(" → ", nextChain) + " → " + follow);
                    nextChain.Add(follow);
                    if (!byId.TryGetValue(follow, out var nextQuest)) break; // 上面已校验存在
                    follow = nextQuest.Next;
                }
            }
        }

        // 表与物品表对表：奖励物品必须是真实存在、可入包的物品。
        public void CrossCheck(ItemTable items)
        {
            foreach (var quest in quests)
                foreach (var line in quest.Rewards.Items)
                    if (items.Find(line.id) == null)
                        throw new FormatException("任务 " + quest.Id + " 的奖励物品不在物品表里：" + line.id);
        }

        public void CrossCheckNpcs(TownMap world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            foreach (var quest in quests)
                foreach (var npc in new[]{quest.GiverNpc,quest.TurnInNpc})
                    if (!string.IsNullOrEmpty(npc) && !world.Npcs.Any(n=>n.Id==npc))
                        throw new FormatException("任务 "+quest.Id+" 的 NPC 不存在："+npc);
        }
        static string OptionalNpc(JsonValue entry,string key,string id)
        {
            if (!entry.Has(key)) return "";
            var value=entry[key].AsString(null);
            if (value==null) throw new FormatException("任务 "+id+" 的 "+key+" 应为字符串。");
            return value.Trim();
        }

        static IEnumerable<JsonValue> Elements(JsonValue parent, string key)
        {
            var array = parent[key];
            if (array.IsNull) yield break;
            if (array.kind != JsonValue.Kind.Array) throw new FormatException("字段 " + key + " 应是数组。");
            foreach (var item in array.Items)
            {
                if (item == null || item.kind != JsonValue.Kind.Object)
                    throw new FormatException("字段 " + key + " 的元素应是对象。");
                yield return item;
            }
        }

        // 缺字段时点名是哪个任务的哪个字段：任务表动辄几十条，只说"缺少字段"根本没法改。
        static string Required(JsonValue entry, string key, string owner = null)
        {
            var value = entry[key].AsString(null);
            if (string.IsNullOrWhiteSpace(value))
                throw new FormatException((owner == null ? "" : owner + " ") +
                    "缺少字段 " + key + "（应为非空字符串）。");
            return value;
        }
    }

    // 存档里的单个任务状态：与表按 id 关联，progress 与 objectives 同序。
    [Serializable]
    public sealed class QuestState
    {
        public string id = "";
        public string status = QuestLedger.StatusLocked;
        public int[] progress = Array.Empty<int>();
    }

    // 奖励发放的如实回执：装不下的余量单列，不凭空丢失也不虚报。
    public sealed class QuestSpoils
    {
        public int coins;
        public int itemsGranted;
        public int itemsLeftover;
        public string[] flags = Array.Empty<string>();
    }

    // 任务状态机与存档读写：唯一允许改任务状态的地方。
    public static class QuestLedger
    {
        public const string StatusLocked = "Locked";
        public const string StatusAvailable = "Available";
        public const string StatusActive = "Active";
        public const string StatusCompleted = "Completed";
        public const string StatusFailed = "Failed";

        public static readonly string[] StatusNames =
            { StatusLocked, StatusAvailable, StatusActive, StatusCompleted, StatusFailed };

        public static bool IsKnownStatus(string status) => status != null && StatusNames.Contains(status);

        public static string StatusOf(SaveData save, string questId)
        {
            var state = Find(save, questId);
            return state?.status ?? StatusLocked;
        }

        static QuestState Find(SaveData save, string questId)
        {
            if (save?.quests == null) return null;
            return save.quests.FirstOrDefault(q => q != null && q.id == questId);
        }

        // 取（或按表铺一份）任务状态。首次铺货时按表的初始状态开局、目标进度清零。
        public static QuestState Of(SaveData save, QuestDef quest)
        {
            if (save == null || quest == null) return null;
            save.quests ??= Array.Empty<QuestState>();
            var state = Find(save, quest.Id);
            if (state == null)
            {
                state = new QuestState
                {
                    id = quest.Id,
                    status = quest.InitialStatus,
                    progress = quest.Objectives.Select(_ => 0).ToArray(),
                };
                save.quests = save.quests.Concat(new[] { state }).ToArray();
            }
            // 目标数量以表为准：表里加了目标，旧档补一个 0，不改已记的进度。
            if (state.progress == null || state.progress.Length != quest.Objectives.Count)
            {
                var aligned = quest.Objectives.Select((_, index) =>
                    state.progress != null && index < state.progress.Length ? state.progress[index] : 0).ToArray();
                state.progress = aligned;
            }
            if (!IsKnownStatus(state.status)) state.status = StatusLocked;
            return state;
        }

        public static QuestState Of(SaveData save, QuestTable table, string questId) =>
            Of(save, table?.Find(questId));

        // 前置是否全部完成：解锁的唯一判据。
        public static bool PrerequisitesMet(SaveData save, QuestDef quest)
        {
            if (save == null || quest == null) return false;
            return quest.Prerequisites.All(id => StatusOf(save, id) == StatusCompleted);
        }

        // 刷新解锁：前置完成后把 Locked 的任务推成 Available。幂等，可随时调用。
        public static int RefreshAvailability(SaveData save, QuestTable table)
        {
            if (save == null || table == null) return 0;
            var promoted = 0;
            foreach (var quest in table.Quests)
            {
                var state = Of(save, quest);
                if (state.status != StatusLocked || !PrerequisitesMet(save, quest)) continue;
                state.status = StatusAvailable;
                promoted++;
            }
            return promoted;
        }

        // 接任务：只允许 Available → Active。已 Active/Completed 返回 false（重复接取是幂等的），
        // Locked 说明前置未完成，属于正常状态而不是错误。
        public static bool TryStart(SaveData save, QuestDef quest)
        {
            var state = Of(save, quest);
            if (state == null || state.status != StatusAvailable) return false;
            state.status = StatusActive;
            return true;
        }

        // 推进一个目标：只允许 Active 期间；进度不超过表里声明的数量。返回是否记到了这次推进。
        public static bool Advance(SaveData save, QuestDef quest, string objectiveId, int delta)
        {
            var state = Of(save, quest);
            if (state == null || state.status != StatusActive || delta <= 0) return false;
            var index = quest.Objectives.ToList().FindIndex(o => o.Id == objectiveId);
            if (index < 0 || index >= state.progress.Length) return false;
            var target = Math.Max(1, quest.Objectives[index].Count);
            if (state.progress[index] >= target) return false;
            state.progress[index] = Math.Min(state.progress[index] + delta, target);
            return true;
        }

        public static int Progress(SaveData save, QuestDef quest, string objectiveId)
        {
            var state = Find(save, quest?.Id);
            var index = quest?.Objectives.ToList().FindIndex(o => o.Id == objectiveId) ?? -1;
            if (state?.progress == null || index < 0 || index >= state.progress.Length) return 0;
            return state.progress[index];
        }

        // Presentation reads never call Of: a missing/old quest record must remain byte-for-byte unchanged.
        public static string DisplayStatus(SaveData save,QuestDef quest)
        {
            if (save==null || quest==null) return StatusLocked;
            var state=Find(save,quest.Id);
            return state==null ? (PrerequisitesMet(save,quest)?quest.InitialStatus:StatusLocked)
                : IsKnownStatus(state.status)?state.status:StatusLocked;
        }
        public static bool ObjectivesMetReadOnly(SaveData save,QuestDef quest)
        {
            if (save==null || quest==null) return false;
            var state=Find(save,quest.Id);
            if (state==null) return false;
            for(int i=0;i<quest.Objectives.Count;i++)
                if (state.progress==null || i>=state.progress.Length || state.progress[i]<Math.Max(1,quest.Objectives[i].Count))return false;
            return true;
        }

        // 目标是否全部达成：单次判定的唯一入口。
        public static bool ObjectivesMet(SaveData save, QuestDef quest)
        {
            var state = Of(save, quest);
            if (state == null) return false;
            if (quest.Objectives.Count == 0) return true;
            for (var i = 0; i < quest.Objectives.Count; i++)
                if (state.progress[i] < Math.Max(1, quest.Objectives[i].Count)) return false;
            return true;
        }

        // 完成任务：Active 且目标全达成才允许。目标没达成抛异常（那是调用方的逻辑错误，
        // 静默返回 false 会让"任务做完了却没结算"这种问题永远查不出来）；已完成或已失败
        // 返回 false 保持幂等；Locked/Available 仍抛——没接取就想完成，那是跳步。
        public static bool TryComplete(SaveData save, QuestDef quest)
        {
            var state = Of(save, quest);
            if (state == null) return false;
            if (state.status == StatusCompleted || state.status == StatusFailed) return false;
            if (state.status != StatusActive)
                throw new InvalidOperationException("任务 " + quest.Id + " 当前是 " + state.status + "，不能直接完成。");
            if (!ObjectivesMet(save, quest))
                throw new InvalidOperationException("任务 " + quest.Id + " 的目标尚未达成，不能完成。");
            state.status = StatusCompleted;
            return true;
        }

        // 任务失败：只允许 Active → Failed。重复失败是幂等的。
        public static bool TryFail(SaveData save, QuestDef quest)
        {
            var state = Of(save, quest);
            if (state == null || state.status != StatusActive) return false;
            state.status = StatusFailed;
            return true;
        }

        public static bool IsDone(SaveData save, string questId) => StatusOf(save, questId) == StatusCompleted;

        // 发奖：铜钱、物品、一次性标记统一从这里走。标记类奖励靠 MarkFlag 幂等兜底，
        // 重复调用不会二次记账。
        public static QuestSpoils GrantRewards(SaveData save, QuestDef quest, ItemTable items)
        {
            var spoils = new QuestSpoils();
            if (save == null || quest?.Rewards == null) return spoils;
            var rewards = quest.Rewards;
            if (rewards.Coins > 0)
            {
                save.coins = Math.Min(save.coins + rewards.Coins, SaveData.MaxCoins);
                spoils.coins = rewards.Coins;
            }
            foreach (var line in rewards.Items)
            {
                var leftover = InventoryRules.Add(save.bag, items, line.id, line.count);
                spoils.itemsGranted += line.count - leftover;
                spoils.itemsLeftover += leftover;
            }
            var flags = new List<string>();
            foreach (var flag in rewards.Flags)
                if (save.MarkFlag(flag)) flags.Add(flag);
            spoils.flags = flags.ToArray();
            return spoils;
        }
    }

    /// <summary>运行期对账的现场信号（T083）。
    /// 只有"这一瞬间发生了什么"是调用点才知道的，其余判据一律从存档里**已经存在的事实**
    /// 读回来：目标一旦推上去就写在 `QuestState.progress` 里，读档不需要重新证明。</summary>
    public sealed class QuestSignal
    {
        /// <summary>刚跟谁说了话（对白分支的点击点传）。空表示这一次调用没有说话事件。</summary>
        public string TalkedNpcId;

        /// <summary>刚走到哪个地点（世界页在玩家进入地标半径时传，T112）。
        /// 空表示这一次调用没有"到过某地"事件——注意这不是"没走到就算失败"，
        /// 目标没写地点时任何到达都不该推进任务（见 SignalSeen 的 reach 分支）。</summary>
        public string ReachedPlaceId;

        public static QuestSignal None() => new QuestSignal();
    }

    /// <summary>一次对账的如实回执。`Unwired` 是**必须存在**的一栏：它写的是"这一类目标
    /// 至今没有运行期生产者"，宁可让回执难看，也不把没接上的目标悄悄推满。</summary>
    public sealed class QuestSyncResult
    {
        public int ObjectivesAdvanced;
        public readonly List<string> Completed = new List<string>();
        public readonly List<string> KarmaRecorded = new List<string>();
        public readonly List<string> Unwired = new List<string>();
        public int CoinsGranted;
        public int ItemsGranted;
        public int ItemsLeftover;

        // T100：删掉 `NothingHappened`。它把三个计数器折成一个布尔，但全仓没人读过它；
        // 三个计数器本身都有各自的断言在用（推进/完成/未接线分别要报出名字），
        // 一个"三合一的是不是空转"既不够精确、又没人消费，留着只会诱导出"空转已被处理"的错觉。
    }

    /// <summary>任务状态机的运行期驱动器（T083）。
    ///
    /// 状态机本身（`QuestLedger`）早就写全了，缺的是**运行期没人调它**——数据层能把十条
    /// 任务从零模拟到全部结算，玩家在游戏里做完却什么都拿不到。这一层只回答一个问题：
    /// "每一类目标，运行期由谁、凭什么事实推上去"。答不出来的类型登记进回执，不写代码。
    ///
    /// 它**不碰气运、也不落盘**（那是调用方的责任，见 T097）；气运映射与落盘责任
    /// 由 Economy 侧的 `QuestSettlement` 与本类的调用点一起守。</summary>
    public enum QuestOverheadState { None, InProgress, Available, Completable }

    public static class QuestRuntime
    {
        /// <summary>回执里"没有运行期生产者"的原因码：`no-producer:&lt;目标类型&gt;`。</summary>
        public const string NoProducer = "no-producer";

        /// <summary>当前**有**运行期生产者的目标类型。加一类就要在这里加一类，
        /// 并在套件里给出"运行期谁调它"的那一句；给不出就不许进这张表。</summary>
        public static readonly string[] WiredObjectiveTypes =
            { "talk", "clue", "collect", "defeat", "reach" };

        public static QuestSyncResult Reconcile(SaveData save, QuestTable table, ItemTable items,
            QuestSignal signal)
        {
            var report = new QuestSyncResult();
            if (save == null || table == null) return report;
            var now = signal ?? QuestSignal.None();
            QuestLedger.RefreshAvailability(save, table);

            foreach (var quest in table.Quests)
            {
                if (QuestLedger.StatusOf(save, quest.Id) != QuestLedger.StatusActive) continue;
                foreach (var objective in quest.Objectives)
                {
                    var seen = SignalSeen(objective, save, now, out var unwired);
                    if (unwired != null)
                        report.Unwired.Add(quest.Id + "/" + objective.Id + "=" + unwired);
                    if (!seen) continue;
                    var need = Math.Max(1, objective.Count);
                    // 一次对账只把这一条目标推到"表里要的数量"：阈值型判据（collect）按
                    // 行囊里的实际件数推，事件型（talk/defeat）一次算一件。
                    var delta = need - QuestLedger.Progress(save, quest, objective.Id);
                    if (delta > 0 && QuestLedger.Advance(save, quest, objective.Id, delta))
                        report.ObjectivesAdvanced++;
                }
                if (!QuestLedger.ObjectivesMet(save, quest)) continue;
                if (!string.IsNullOrEmpty(quest.TurnInNpc)) continue;
                if (!QuestLedger.TryComplete(save, quest)) continue;
                report.Completed.Add(quest.Id);
                var spoils = QuestLedger.GrantRewards(save, quest, items);
                report.CoinsGranted += spoils.coins;
                report.ItemsGranted += spoils.itemsGranted;
                report.ItemsLeftover += spoils.itemsLeftover;
                // 结算完立刻重推一次解锁：下一环从 Locked 变 Available，
                // 玩家回到对白里就能看到那句"你替我去看看"。
                QuestLedger.RefreshAvailability(save, table);
            }
            return report;
        }

        public const float InteractionDistance = 200f;
        public static QuestOverheadState OverheadState(SaveData save,QuestTable table,string npcId)
        {
            var result=QuestOverheadState.None;
            if(save==null || table==null || string.IsNullOrEmpty(npcId))return result;
            foreach(var quest in table.Quests)
            {
                var status=QuestLedger.DisplayStatus(save,quest);
                var next=QuestOverheadState.None;
                if(status==QuestLedger.StatusAvailable && quest.GiverNpc==npcId) next=QuestOverheadState.Available;
                else if(status==QuestLedger.StatusActive && (quest.TurnInNpc==npcId || (quest.TurnInNpc.Length==0 && quest.GiverNpc==npcId)))
                    next=quest.TurnInNpc==npcId && QuestLedger.ObjectivesMetReadOnly(save,quest)?QuestOverheadState.Completable:QuestOverheadState.InProgress;
                if(next>result)result=next;
            }
            return result;
        }
        public static bool CanTurnIn(SaveData save,QuestDef quest,string npcId,float distance)
        {
            return quest!=null && quest.TurnInNpc.Length>0 && quest.TurnInNpc==npcId &&
                !float.IsNaN(distance) && !float.IsInfinity(distance) && distance>=0 && distance<=InteractionDistance &&
                QuestLedger.StatusOf(save,quest.Id)==QuestLedger.StatusActive && QuestLedger.ObjectivesMetReadOnly(save,quest);
        }
        // The status transition is the award guard. Wrong NPC/distance/state reads do not materialize records.
        // Host persists this report once; repeated clicks and reloaded Completed saves cannot award again.
        public static QuestSyncResult TryTurnIn(SaveData save,QuestTable table,ItemTable items,string questId,string npcId,float distance)
        {
            var report=new QuestSyncResult(); var quest=table?.Find(questId);
            if(items==null || !CanTurnIn(save,quest,npcId,distance))return report;
            if(!QuestLedger.TryComplete(save,quest))return report;
            var spoils=QuestLedger.GrantRewards(save,quest,items);
            report.Completed.Add(quest.Id); report.CoinsGranted=spoils.coins;
            report.ItemsGranted=spoils.itemsGranted; report.ItemsLeftover=spoils.itemsLeftover;
            QuestLedger.RefreshAvailability(save,table);
            return report;
        }

        /// <summary>这一类目标的"运行期谁调它"。`unwired` 非空＝这一类至今没有生产者，
        /// 调用点是谁都答不出来——绝不返回 true 蒙过去。</summary>
        static bool SignalSeen(QuestObjective objective, SaveData save, QuestSignal signal,
            out string unwired)
        {
            unwired = null;
            switch (objective.Type)
            {
                // talk：对白分支的点击点（FrontEndTrade.OnTalkBranch）。目标写了人就必须是那个人，
                // 没写人表示"跟谁都算"——卷一确实有一条这样写的任务。
                case "talk":
                    if (string.IsNullOrEmpty(objective.Target))
                        return !string.IsNullOrEmpty(signal.TalkedNpcId);
                    return signal.TalkedNpcId == objective.Target;

                // clue：打听与剧情给线索（Core 的 DialogueRules/ClueLedger.Acquire 在写）。
                // 写了 id 就认那一条，没写就认"手头有线索了"。
                case "clue":
                    return string.IsNullOrEmpty(objective.Target)
                        ? ClueLedger.Of(save).Any()
                        : ClueLedger.Has(save, objective.Target);

                // collect：行囊里的件数是事实本身（买、做、捡、任务奖励都汇到这一处）。
                case "collect":
                    return !string.IsNullOrEmpty(objective.Target) &&
                        InventoryRules.Count(save.bag, objective.Target) >= Math.Max(1, objective.Count);

                // defeat：讨伐过的遭遇由战斗结算落一次性标记（FrontEndCombat.SettleVictory 已在写），
                // 这里只读那个事实——不去改别人域的文件。
                case "defeat":
                    if (string.IsNullOrEmpty(objective.Target))
                    {
                        unwired = NoProducer + ":defeat-without-encounter";
                        return false;
                    }
                    return save.oneTimeFlags != null && save.oneTimeFlags.Contains(objective.Target);

                // reach：走到某个地点。运行期生产者 = FrontEndWorld.ReportTravelReach
                // （玩家进入地标半径时发信号，T112 由世界线接上）。
                // 目标必须写了地点，且信号里的地点正是它——"到了随便一个地方"不许推进任何一条
                // reach 任务；地点写了却没这个地标，SignalSeen 认不到，回执里也不许悄悄推满。
                case "reach":
                    return !string.IsNullOrEmpty(objective.Target) && signal.ReachedPlaceId == objective.Target;

                default:
                    unwired = NoProducer + ":" + objective.Type;
                    return false;
            }
        }
    }
}
