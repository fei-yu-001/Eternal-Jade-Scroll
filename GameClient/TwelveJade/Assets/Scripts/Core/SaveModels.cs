using System;
using System.Collections.Generic;
using System.Linq;

namespace TwelveJade.Core
{
    [Serializable]
    public sealed class SaveData
    {
        public const int CurrentSchemaVersion = 8;
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
                data.bag = InventoryRules.Normalize(data.bag, table);
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
