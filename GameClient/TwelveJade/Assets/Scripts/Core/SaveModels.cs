using System;
using System.Collections.Generic;
using System.Linq;

namespace TwelveJade.Core
{
    [Serializable]
    public sealed class SaveData
    {
        public const int CurrentSchemaVersion = 11;
        public const int MaxFaceStyles = 2;
        public const int MaxCoins = 9999999;
        public const int MaxReputation = 100;
        public static readonly string[] Genders = { "male", "female" };

        public int schemaVersion = CurrentSchemaVersion;
        public int slot;
        public string characterId;
        public string characterName;
        public string createdUtc;
        public string updatedUtc;
        public string location = "CharacterPreview";
        public int facing;
        // schemaVersion 2: a person is more than an outfit. Older files load with
        // these defaults applied by the deserializer and are upgraded on next write.
        public string gender = "male";
        public int faceStyle;
        public string[] traits = Array.Empty<string>();
        public string destiny = "";
        // schemaVersion 3: 行囊、铜钱与地方声望。旧档读入时补上开局行囊（那时还没有这些字段）。
        public int coins = ItemTable.StartingCoins;
        public int localReputation;
        public ItemStack[] bag = InventoryRules.NewBag();
        // schemaVersion 4: 商人存货与记忆。首次见面时按商品表铺满，见面与买卖都写回。
        public MerchantState[] merchants = Array.Empty<MerchantState>();
        // schemaVersion 5: 一次性事件标记（如"encounter-boar-01 已发过奖励"），文本 id，最多 32 条。
        public string[] oneTimeFlags = Array.Empty<string>();
        // schemaVersion 6: 任务与章节进度。v5 及更早的档没有 quests，读入时留空——任务表
        // 第一次被用到时才由 QuestLedger 按表铺一份初始状态，历史档因此不丢东西。
        public QuestState[] quests = Array.Empty<QuestState>();
        // schemaVersion 7: 世界时间。存"第几天 + 当天第几分钟"两个整数而不是时间戳——
        // 存档因此不依赖机器时区，读写往返都不会漂。v6 及更早的档读入时落到开局时刻。
        public int worldDay = WorldTime.StartDay;
        public int worldMinuteOfDay = WorldTime.StartMinute;
        // schemaVersion 8: NPC 关系、记忆与目标进度（GDD 十·第二至第五层）。
        // 与 merchants（交易存货与次数）并存：一个是账，一个是人情。v7 及更早的档没有
        // npcs，读入时留空，第一次碰上某个人时才由 NpcLedger 建一份。
        public NpcState[] npcs = Array.Empty<NpcState>();
        // schemaVersion 9: 线索账本（哪几条、什么时候拿到的、看过没有）。
        // 与 npcs/quests 并列：一条线索是"知道的事"，不是和某个人的关系，也不是任务进度。
        // v8 及更早的档没有 clues，读入时留空，第一次打听时才落第一条。
        public ClueEntry[] clues = Array.Empty<ClueEntry>();
        // 当前对话进度（跟谁、停在哪）。Esc 中断写进存档，下次开口从这里续上。
        // 与 clues 不同：对话是"正在说的话"，线索是"已经知道的事"，两者生命周期不同。
        public DialogueState dialogue;
        // schemaVersion 10: 章进度（走到第几段、哪几段已结算）。
        // 与 quests 分开：任务是一个个目标，章进度是整条轴线走到哪儿了。
        public ChapterState chapter;
        // 气运善行簿（大纲 0.3）：十七件善事与八处奇遇的沉淀。
        // 字段是在 schemaVersion 10 期间纯增量加上的（老档读入补空数组），所以**它的校验分支
        // 从 10 起生效而不是 11**——按 11 起判会让现存那些已经带着账的 10 号档躲过检查（W-A 指出的洞）。
        public KarmaEntry[] karmaEvents = Array.Empty<KarmaEntry>();
        // schemaVersion 11: 装备与耐久落盘（T051）。此前穿戴与耐久跟着这一局走，读档/换档回到裸装。
        // 只存"哪个槽穿着哪件、还剩多少耐久"，属性换算永远由 CombatGear 表实算——存属性就是第二真源。
        public GearLine[] gear = Array.Empty<GearLine>();
        // schemaVersion 11: 每道方子做过几次（T046 熟练度的账）。
        // 存次数不存"熟练度等级"：等级是次数的函数，能算出来的东西不进存档（同 worldDay 存分钟不存时间戳）。
        public CraftLine[] crafts = Array.Empty<CraftLine>();
        // Optional additive ledger: old v11 saves remain valid; missing means no opened points.
        public CultivationState cultivation;
        // Optional v11-compatible acquired-manual ledger; missing on old saves means no learned manuals.
        public MiningEntry[] mining = Array.Empty<MiningEntry>();
        public ManualStudyEntry[] manualStudies;

        // Optional v11 exploration ledger. Missing on older saves means unknown terrain, never an unlocked map.
        public AtlasExplorationState atlasExploration;

        // 瞬态标记（不进 JSON）：行囊是否已按物品表整理过。
        // 读档整理一次；写档路径绝不再整理，否则越界堆叠会被悄悄截到上限、脏档再也拒不掉。
        [NonSerialized] public bool bagClipped;

        public static bool IsValidGender(string value) => Genders.Contains(value);
        public static bool IsValidFaceStyle(int value) => value >= 0 && value < MaxFaceStyles;
        public static bool IsValidCoins(int value) => value >= 0 && value <= MaxCoins;
        public static bool IsValidReputation(int value) => value >= 0 && value <= MaxReputation;

        // 旧档迁移：只补缺的字段，不覆盖已写入的内容；可重复调用。
        public static void Migrate(SaveData data, ItemTable table = null)
        {
            if (data == null) return;
            if (data.schemaVersion >= 3)
            {
                // 只在**读档**路径用 Normalize（老档可能带着越界值，读进来要收拾干净）。
                // 写档路径若也 Normalize，越界堆叠会被悄悄截到上限——脏档就再也拒不掉了。
                if (table != null && !data.bagClipped)
                {
                    data.bag = InventoryRules.Normalize(data.bag, table);
                    data.bagClipped = true;
                }
                data.coins = Math.Min(Math.Max(data.coins, 0), MaxCoins);
                data.localReputation = Math.Min(Math.Max(data.localReputation, 0), MaxReputation);
            }
            else
            {
                data.coins = ItemTable.StartingCoins;
                data.localReputation = 0;
                data.bag = StartingBag();
            }
            // v3 及更早的档没见过货郎，merchants 留空，第一次搭话时由 MerchantLedger 按商品表铺货。
            // 这里只做归一化、不按版本清空——Migrate 必须可以反复调用而不丢东西。
            data.merchants ??= Array.Empty<MerchantState>();
            // 气运账簿：老档没有这个字段，读入时补空——Migrate 可反复调用，不会清空已记的事。
            data.karmaEvents ??= Array.Empty<KarmaEntry>();
            // 10 号期间**没有任何校验**守过这张账（W-A 指出的洞），所以老档里完全可能躺着
            // 越界时刻、times=0、同 id 两行这类新校验会当场拒掉的行。
            // 迁移负责把它们修回合法形状——把玩家真做过的善事判成脏档，比脏档本身更糟。
            data.karmaEvents = data.karmaEvents
                .Where(k => k != null && !string.IsNullOrWhiteSpace(k.eventId) &&
                            (k.kind == KarmaKinds.Deed || k.kind == KarmaKinds.Chance))
                .GroupBy(k => k.eventId).Select(g => g.Last())
                .Select(k =>
                {
                    var when = WorldClock.ReadFrom(k.day, k.minuteOfDay);   // 越界的日子与分钟拉回来
                    k.day = when.day;
                    k.minuteOfDay = when.minuteOfDay;
                    if (k.times < 1) k.times = 1;                           // 做过的事至少算一次
                    return k;
                })
                .ToArray();
            data.merchants = data.merchants
                .Where(m => m != null && !string.IsNullOrWhiteSpace(m.id))
                .ToArray();
            data.oneTimeFlags ??= Array.Empty<string>();
            data.oneTimeFlags = data.oneTimeFlags
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Distinct()
                .ToArray();
            // 任务状态只做归一化、不按版本清空：迁移必须能反复调用而不丢已推进的进度。
            // v5 及更早的档没有 quests，保持为空，由 QuestLedger 首次用到时按表铺初始状态。
            data.quests ??= Array.Empty<QuestState>();
            data.quests = data.quests
                .Where(q => q != null && !string.IsNullOrWhiteSpace(q.id))
                .ToArray();
            // 世界时间同样只做归一化：越界的日子与分钟拉回合法范围，迁移可反复调用。
            var world = WorldClock.ReadFrom(data.worldDay, data.worldMinuteOfDay);
            data.worldDay = world.day;
            data.worldMinuteOfDay = world.minuteOfDay;
            // NPC 状态只归一化不去重不裁剪：表里暂时没有的人（以后才加进人物表）也要留着，
            // 不能因为"现在的表里找不到"就把玩家跟他打过的交道抹掉。
            data.npcs ??= Array.Empty<NpcState>();
            data.npcs = data.npcs.Where(n => n != null && !string.IsNullOrWhiteSpace(n.id)).ToArray();
            // 线索同理：归一化但不按表去重（表会变，玩家已经知道的事不该被"表里没了"抹掉）。
            data.clues ??= Array.Empty<ClueEntry>();
            data.clues = data.clues.Where(c => c != null && !string.IsNullOrWhiteSpace(c.id)).ToArray();
            // 对话进度：说话人/节点都空就当没在对话里，别留半截状态卡住下次开口。
            if (data.dialogue != null &&
                (string.IsNullOrWhiteSpace(data.dialogue.npcId) || string.IsNullOrWhiteSpace(data.dialogue.nodeId)))
                data.dialogue = null;
            // 章进度：v9 及更早的档没有 chapter，落回第一段。
            data.chapter ??= new ChapterState();
            data.chapter.settled ??= Array.Empty<string>();
            // 装备与耐久（v11）：只归一化，不按表裁剪、不清空。
            // 表里暂时没有的那件装备也要留着（玩家穿着它打过架，"后来表改了"不该把它抹掉），
            // 但同一个槽只能有一条——两条同槽读回来谁覆盖谁是不确定的。
            data.gear ??= Array.Empty<GearLine>();
            data.gear = data.gear
                .Where(g => g != null && GearSlots.IsKnown(g.slot) && !string.IsNullOrWhiteSpace(g.itemId) &&
                            g.durability >= 0)
                .GroupBy(g => g.slot).Select(g => g.Last())
                .ToArray();
            // 每道方子的制作次数（v11）：同样只归一化。次数是历史事实，删了就是把玩家做过的事抹掉。
            data.crafts ??= Array.Empty<CraftLine>();
            data.crafts = data.crafts
                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.recipeId) && c.times > 0)
                .GroupBy(c => c.recipeId).Select(c => c.Last())
                .ToArray();
        }

        // 读出存档里的世界时间（结构体），供 WorldClock 推进。
        public WorldTime WorldTimeNow() => WorldClock.ReadFrom(worldDay, worldMinuteOfDay);

        // 把世界时间写回存档字段。
        public void SetWorldTime(WorldTime time) =>
            WorldClock.WriteTo(time, (day, minute) => { worldDay = day; worldMinuteOfDay = minute; });

        // 一次性标记：没记过返回 true 并记档；重复调用是幂等的。
        public bool MarkFlag(string flagId)
        {
            if (string.IsNullOrWhiteSpace(flagId) || flagId.Length > 48)
                throw new ArgumentException("标记 id 需要 1–48 个字符。", nameof(flagId));
            if (oneTimeFlags != null && oneTimeFlags.Contains(flagId)) return false;
            if (oneTimeFlags != null && oneTimeFlags.Length >= 32)
                throw new InvalidOperationException("一次性标记已满（32 条），请清理过期标记。");
            oneTimeFlags = (oneTimeFlags ?? Array.Empty<string>()).Append(flagId).ToArray();
            return true;
        }

        public bool HasFlag(string flagId) =>
            !string.IsNullOrWhiteSpace(flagId) && oneTimeFlags != null && oneTimeFlags.Contains(flagId);

        // 开局行囊：新档与旧档迁移共用同一份，kits 表在 ItemTable.StartingKit。
        public static ItemStack[] StartingBag()
        {
            var bag = InventoryRules.NewBag();
            foreach (var (id, count) in ItemTable.StartingKit) InventoryRules.Add(bag, null, id, count);
            return bag;
        }
    }

    // 气运账簿里的一条：哪件事、善事还是奇遇、什么时候、做过几回。
    // 只记事实，不记分数——气运不是一个能显示的数字（大纲：善行不自夸，天地自知晓）。
    [Serializable]
    public sealed class KarmaEntry
    {
        public string eventId = "";
        public string kind = "";
        public int day;
        public int minuteOfDay;
        public int times;
    }

    /// <summary>存档里的一件穿戴：槽位 + 物品 id + 剩余耐久（T051）。
    /// 只存这三样：攻防暴击与耐久上限永远由 `CombatGear` 表实算——存属性就是第二真源，
    /// 表一改，老档里那个数会永远错着。
    /// **[Serializable] 不是可选装饰**：产线 codec 包的是 Unity 的 JsonUtility，缺它这整个字段
    /// 静默不写（不报错、读档归零）。T118 就是栽在这一行上，控制台侧第 25 节现在逐族守着它。</summary>
    [Serializable]
    public sealed class GearLine
    {
        public string slot = "";
        public string itemId = "";
        public int durability;
    }

    /// <summary>装备槽的字符串口径。Core 不能引用 Combat（依赖方向是 Combat→Core），
    /// 所以这里存小写字符串而不是 `GearSlot` 枚举——枚举改顺序也不会把老档串档。
    /// "这三个名字必须与 `Combat.GearSlot` 的枚举名逐字对应"由 SaveChecks 那侧钉住（它两边都看得到）。</summary>
    public static class GearSlots
    {
        public const string Weapon = "weapon", Armor = "armor", Accessory = "accessory";
        public static readonly string[] All = { Weapon, Armor, Accessory };
        public static bool IsKnown(string slot) => !string.IsNullOrEmpty(slot) && Array.IndexOf(All, slot) >= 0;
    }

    /// <summary>气运条目的两类 kind。真源放 Core（存档校验要用），Economy 的 `KarmaTracker`
    /// 同名常量转发到这里——同一个字符串不许在两层各写一遍（T095 同族）。</summary>
    public static class KarmaKinds
    {
        public const string Deed = "deed";      // 善事：你主动伸手
        public const string Chance = "chance";  // 奇遇：它伸过来，而你没松手
    }

    /// <summary>某道方子做过几次（T046 熟练度的账）。次数是事实，"熟练度"是它的函数，不进存档。
    /// 与 `GearLine` 同一课：缺 [Serializable] 就等于没这个字段（T118）。</summary>
    [Serializable]
    public sealed class CraftLine
    {
        public string recipeId = "";
        public int times;
    }

    /// <summary>制作台账：记一次、查做过几次。与气运账簿同一套纪律——
    /// 记完只改内存，落盘责任在调用点（T097）。</summary>
    public static class CraftLedger
    {
        public const int MaxRecipes = 64;

        public static int TimesOf(SaveData save, string recipeId)
        {
            if (save?.crafts == null || string.IsNullOrWhiteSpace(recipeId)) return 0;
            var line = save.crafts.FirstOrDefault(c => c != null && c.recipeId == recipeId);
            return line?.times ?? 0;
        }

        /// <summary>记一次制作，返回累计次数。表满了（64 道方子）如实返回既有值，不静默丢账。</summary>
        public static int Record(SaveData save, string recipeId)
        {
            if (save == null || string.IsNullOrWhiteSpace(recipeId)) return 0;
            save.crafts ??= Array.Empty<CraftLine>();
            var line = save.crafts.FirstOrDefault(c => c != null && c.recipeId == recipeId);
            if (line != null) return ++line.times;
            if (save.crafts.Length >= MaxRecipes) return 0;
            System.Array.Resize(ref save.crafts, save.crafts.Length + 1);
            save.crafts[save.crafts.Length - 1] = new CraftLine { recipeId = recipeId, times = 1 };
            return 1;
        }
    }

    /// <summary>穿戴台账：把 `CombatEquipmentService` 的会话态落成存档行、再读回来。
    /// 这里只管数据形状，耐久怎么扣、器刃何时钝归 Combat 线。</summary>
    public static class GearLedger
    {
        public static int CountOf(SaveData save) => save?.gear?.Length ?? 0;

        public static GearLine Of(SaveData save, string slot) =>
            save?.gear == null ? null : save.gear.FirstOrDefault(g => g != null && g.slot == slot);

        /// <summary>记一件穿戴/耐久变化：同槽覆盖，返回写进去的那条。</summary>
        public static GearLine Record(SaveData save, string slot, string itemId, int durability)
        {
            if (save == null || !GearSlots.IsKnown(slot) || string.IsNullOrWhiteSpace(itemId)) return null;
            save.gear ??= Array.Empty<GearLine>();
            var line = Of(save, slot);
            if (line == null)
            {
                line = new GearLine { slot = slot };
                System.Array.Resize(ref save.gear, save.gear.Length + 1);
                save.gear[save.gear.Length - 1] = line;
            }
            line.itemId = itemId;
            line.durability = Math.Max(0, durability);
            return line;
        }

        /// <summary>脱下来：槽位整条撤掉，不留"itemId 为空"的半截记录。</summary>
        public static void Clear(SaveData save, string slot)
        {
            if (save?.gear == null) return;
            save.gear = save.gear.Where(g => g != null && g.slot != slot).ToArray();
        }
    }

    // 声望折扣：地方声望每满一档，买卖价让三分，最多让到一成半（GDD 八节善名线）。
    public static class Reputation
    {
        public const int TierStep = 20, MaxTiers = 5;
        public const float DiscountPerTier = .03f;

        public static int Tiers(int reputation) =>
            Math.Min(Math.Max(reputation, 0) / TierStep, MaxTiers);

        public static float Discount(int reputation) => Tiers(reputation) * DiscountPerTier;

        // 买入价向上取整、卖出价向下取整，避免四舍五入把价格算到 0。
        public static int BuyPrice(int basePrice, int reputation) =>
            (int)Math.Ceiling(basePrice * (1 - Discount(reputation)));

        public static int SellPrice(int basePrice, int reputation) =>
            (int)Math.Floor(basePrice * .5 * (1 + Discount(reputation)));
    }

    // 随机词条与命运只描述出身与性情，不给数值加成；命运为后续剧情埋的钩子。
    public static class CharacterGen
    {
        public const int TraitsPerCharacter = 2;

        public sealed class Entry
        {
            public Entry(string id, string name, string description)
            { Id = id; Name = name; Description = description; }
            public string Id { get; }
            public string Name { get; }
            public string Description { get; }
        }

        public static readonly IReadOnlyList<Entry> Traits =
            new[]
            {
                new Entry("clever", "早慧", "听老人讲过的古，记得比同龄人都牢。"),
                new Entry("accent", "乡音重", "一开口就是本乡土话，到了外地总要被笑。"),
                new Entry("darkward", "不怕黑", "田窖山路摸得惯，夜里也走得稳。"),
                new Entry("deft", "手巧", "竹篾草绳到了手里，总能编出个物件。"),
                new Entry("stubborn", "认死理", "认定的事，十头牛也拉不回。"),
                new Entry("curious", "爱打听", "渡口茶棚里的闲话，一件都落不下。"),
                new Entry("slow", "慢性子", "天塌下来，也要先把这碗饭吃完。"),
                new Entry("soft", "心软", "见不得乞儿挨饿，自己饿着也想分一口。"),
                new Entry("hardy", "好脾胃", "粗粮野菜都吃得香，底子结实。"),
                new Entry("cough", "夜咳", "阴雨天喉咙发痒，是幼年落下的病根。"),
                new Entry("fearwater", "怕水", "幼年落过水，从此见了深潭就绕道。"),
                new Entry("grudge", "记仇", "吃过的亏都记在心里，不报不快。"),
                new Entry("pride", "好面子", "宁可饿肚子，也不肯穿补丁衣裳出门。"),
                new Entry("lostway", "路痴", "出了村就认不得路，靠一张嘴问着走。"),
                new Entry("readface", "会看脸色", "市井里长大，察言观色是本能。"),
                new Entry("lightdrink", "酒量浅", "一口薄酒就上脸，躲不过席面。"),
                new Entry("literate", "识字", "跟村塾先生认过几百字，能看懂告示。"),
                new Entry("strong", "力气大", "一担水一口气挑到家，面不改色。")
            };

        public static readonly IReadOnlyList<Entry> Destinies =
            new[]
            {
                new Entry("jadeloft", "玉楼引", "梦里常有一座玉楼，醒来枕边似有微香。"),
                new Entry("fallingstar", "灾星过命", "生那夜恰逢流星坠野，村里老人念念不忘。"),
                new Entry("benefactor", "贵人候", "命里必有贵人相助，只是不知在何处。"),
                new Entry("hardfate", "命硬", "算命先生说你能扛兵灾，乱世里反倒活得长。"),
                new Entry("pledge", "守信约", "父辈留下半块玉佩与一句誓言，对面是谁已无人知晓。"),
                new Entry("lonely", "孤命", "六亲缘薄，注定要一个人走很远的路。"),
                new Entry("debtor", "旧债", "家里欠着一笔说不清的旧债，讨债的人迟早会来。"),
                new Entry("scholar", "文曲偏照", "见字不忘，先生说不去赶考可惜了这颗脑袋。"),
                new Entry("away", "离乡命", "故土留不住你，你的路在远方。"),
                new Entry("unreadable", "无名", "命格古怪，寻常卜算一概算不出你。"),
                new Entry("xia", "侠骨", "路见不平便要管，命里多刀光，也多知己。"),
                new Entry("shaxing", "杀星入命", "批语说乱世出煞星。是救人的刀还是索命的刀，批语没敢写。"),
                new Entry("chuandeng", "传灯命", "命中注定要做旁人的引路人。灯传下去，这辈子的路就没白走。"),
                new Entry("anle", "安乐命", "不求闻达，但求灶头有肉、缸里有米——乱世里这也是大福气。"),
                new Entry("caixing", "财星照命", "见钱眼开不算褒贬。命里带财，做买卖能起三铺两库。"),
                new Entry("renxin", "仁心命", "见病痛就挪不动步，命中注定要悬壶。救一人，便积一分德。"),
                new Entry("chiqing", "痴情种", "为一人可以不要性命。情之一字，是你的劫，也是你的道。"),
                new Entry("longshe", "龙蛇命", "批语只四个字：乱世龙蛇。是龙是蛇，看时势，也看你的手腕。"),
                new Entry("xianyun", "闲云命", "富贵功名都留不住你。山里一亩药田、半卷闲书，才是归宿。")
            };

        public static Entry FindTrait(string id) => Traits.FirstOrDefault(x => x.Id == id);
        public static Entry FindDestiny(string id) => Destinies.FirstOrDefault(x => x.Id == id);

        public static string[] RollTraits(Random rng)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            return Traits.OrderBy(_ => rng.Next()).Take(TraitsPerCharacter)
                .Select(x => x.Id).ToArray();
        }

        public static string RollDestiny(Random rng)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            return Destinies[rng.Next(Destinies.Count)].Id;
        }
    }

    [Serializable]
    public sealed class UserSettings
    {
        public int schemaVersion = 1;
        public float masterVolume = 0.8f;
        public float musicVolume = 0.5f;
        public float effectsVolume = 0.7f;
        public int width = 1600;
        public int height = 900;
        public bool fullscreen;
        public bool reduceMotion;

        public void Normalize()
        {
            masterVolume = Clamp(masterVolume);
            musicVolume = Clamp(musicVolume);
            effectsVolume = Clamp(effectsVolume);
            if (width < 960 || width > 7680 || height < 540 || height > 4320)
            {
                width = 1600;
                height = 900;
            }
        }

        static float Clamp(float value) => float.IsNaN(value) || float.IsInfinity(value)
            ? 0.5f : Math.Max(0, Math.Min(1, value));
    }

    public enum SlotState { Empty, Ready, Recovered, Corrupt, FutureVersion }

    public enum SettingsLoadState { Default, Ready, Recovered, Reset, FutureVersion }

    public sealed class SlotInfo
    {
        public int Slot { get; }
        public SlotState State { get; }
        public SaveData Data { get; }
        public bool CanLoad => State == SlotState.Ready || State == SlotState.Recovered;

        public SlotInfo(int slot, SlotState state, SaveData data = null)
        { Slot = slot; State = state; Data = data; }
    }

    public interface IJsonCodec
    {
        string Serialize<T>(T value);
        T Deserialize<T>(string json) where T : class;
    }
}
