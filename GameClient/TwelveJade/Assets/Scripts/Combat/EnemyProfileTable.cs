using System;
using System.Collections.Generic;
using System.Linq;
using TwelveJade.Core;

namespace TwelveJade.Combat
{
    // 任务书 K（M1）：妖兽生态 2.0 的数据侧。
    //
    // enemies.json 是"超集 schema"，不是任务书 K2 里那份顶层数组：顶层仍是 {"enemies":[...]}，
    // 生命/攻/防/精力/移速/赏钱/掉落仍叫 maxHp/attack/defense/stamina/moveSpeed/coins/drops，
    // 因为 EnemyTable.cs（Core，K 无写权限）与 SaveChecks/EnemyChecks.cs 认的就是这套键，
    // 照抄任务书那份 JSON 会让共享门禁当场解析失败。2.0 只在其之上"叠加"habitat/parts/attacks/ai。
    //
    // parts 与 hitProfile 是同一份部位数据的两种消费视角：hitProfile 给 Core 的倍率查询，
    // parts 给 2.0 的部位名/抵抗率/弱点/霸体。两者不一致就是双真源，所以解析期强制对表。
    public enum EnemyMoveSet { Legacy, Profile2 }

    public sealed class EnemyPart
    {
        public EnemyPart(string id, string name, float multiplier, float dodgeChance, bool weakPoint, bool bulwark)
        {
            Id = id; Name = name; Multiplier = multiplier;
            DodgeChance = dodgeChance; WeakPoint = weakPoint; Bulwark = bulwark;
        }

        public string Id { get; }
        public string Name { get; }
        public float Multiplier { get; }
        // 该部位的闪避抵抗率：Core 用它累计"抵抗计"，攒满一次就落空（确定性，无随机数）。
        public float DodgeChance { get; }
        public bool WeakPoint { get; }
        // 霸体部位（石化前胸）：打上去不但倍率折让，招式起手/判定期间挨打还不吃硬直并反震。
        public bool Bulwark { get; }
    }

    public sealed class EnemyAiProfile
    {
        public EnemyAiProfile(string behavior, EnemyMoveSet moveSet, float preferredRange, float aggressionSeconds)
        {
            Behavior = behavior; MoveSet = moveSet;
            PreferredRange = preferredRange; AggressionSeconds = aggressionSeconds;
        }

        public string Behavior { get; }
        // legacy = 沿用 Core 的轻击基线三相与基准伤害（野猪妖：三个既有夹具的期望值全押在这上面）。
        // profile2 = 消费 attacks 招式表的专属前摇/判定/后摇。
        public EnemyMoveSet MoveSet { get; }
        public float PreferredRange { get; }
        public float AggressionSeconds { get; }
    }

    public sealed class EnemyProfile
    {
        public EnemyProfile(string id, string name, string habitat, int maxHp, int attack, int defense,
            int maxStamina, float moveSpeed, int coins, IReadOnlyList<EnemyPart> parts,
            IReadOnlyList<CombatMove> moves, EnemyAiProfile ai)
        {
            Id = id; Name = name; Habitat = habitat;
            MaxHp = maxHp; Attack = attack; Defense = defense;
            MaxStamina = maxStamina; MoveSpeed = moveSpeed; Coins = coins;
            Parts = parts; Moves = moves; Ai = ai;
        }

        public string Id { get; }
        public string Name { get; }
        public string Habitat { get; }
        public int MaxHp { get; }
        public int Attack { get; }
        public int Defense { get; }
        public int MaxStamina { get; }
        public float MoveSpeed { get; }
        public int Coins { get; }
        public IReadOnlyList<EnemyPart> Parts { get; }
        public IReadOnlyList<CombatMove> Moves { get; }
        public EnemyAiProfile Ai { get; }

        public EnemyPart FindPart(string id) =>
            string.IsNullOrEmpty(id) ? null : Parts.FirstOrDefault(p => p.Id == id);

        public IEnumerable<EnemyPart> WeakPoints => Parts.Where(p => p.WeakPoint);

        // 平攻默认打的部位：优先非弱点的那个（野猪的皮糙厚肉、冥狼的精瘦狼躯），
        // 山魈只有后背旧伤是弱点，平攻就落在石化前胸（0.6 倍，正面硬）。
        public EnemyPart DefaultPart => Parts.FirstOrDefault(p => !p.WeakPoint) ?? Parts.First();

        // 看破反击的落点：倍率最高的弱点。
        public EnemyPart BestWeakPoint => WeakPoints.OrderByDescending(p => p.Multiplier).FirstOrDefault();

        public CombatMove FindMove(string id) =>
            string.IsNullOrEmpty(id) ? null : Moves.FirstOrDefault(m => m.Id == id);

        public string DescribeParts() => string.Join("、", Parts.Select(p =>
            p.Name + "(" + p.MultiplyText() + (p.WeakPoint ? "·弱点" : "") + (p.Bulwark ? "·霸体" : "") + ")"));
    }

    static class EnemyPartText
    {
        public static string MultiplyText(this EnemyPart part) => part.Multiplier.ToString("0.#") + "x";
    }

    // 整表：加载 + 与 Core 的 EnemyTable 对表 + 掉落/物品表对表。
    public sealed class EnemyProfileTable
    {
        readonly List<EnemyProfile> profiles;
        readonly Dictionary<string, string> pendingItemIds;

        EnemyProfileTable(List<EnemyProfile> profiles, Dictionary<string, string> pendingItemIds)
        {
            this.profiles = profiles;
            this.pendingItemIds = pendingItemIds;
        }

        public IReadOnlyList<EnemyProfile> Profiles => profiles;
        public EnemyProfile Find(string id) => id == null ? null : profiles.FirstOrDefault(p => p.Id == id);
        public EnemyProfile ByHabitat(string habitat) =>
            profiles.FirstOrDefault(p => string.Equals(p.Habitat, habitat, StringComparison.Ordinal));

        // 三级山野生态：镇郊草伏 / 北山石涧 / 后山古冢入口。
        public IReadOnlyList<EnemyProfile> InOrder =>
            profiles.OrderBy(p => HabitatRank(p.Habitat)).ToList();

        static int HabitatRank(string habitat) => habitat switch
        {
            "镇郊草伏" => 0,
            "北山石涧" => 1,
            "后山古冢入口" => 2,
            _ => 9,
        };

        public static EnemyProfileTable Load(string json)
        {
            if (!Json.TryParse(json, out var root, out var error))
                throw new FormatException("妖兽 2.0 表读取失败：" + error);
            if (root.kind != JsonValue.Kind.Object) throw new FormatException("妖兽 2.0 表应是一个对象。");

            var list = new List<EnemyProfile>();
            foreach (var entry in Array(root, "enemies"))
            {
                var id = Text(entry, "id");
                if (string.IsNullOrWhiteSpace(id)) throw new FormatException("妖兽 2.0 表有条目缺 id。");
                if (list.Any(p => p.Id == id)) throw new FormatException("妖兽 2.0 表 id 重复：" + id);

                var name = Text(entry, "name");
                var habitat = Text(entry, "habitat");
                if (string.IsNullOrWhiteSpace(habitat))
                    throw new FormatException("妖兽 " + id + " 缺少生态位 habitat（三级山野：镇郊草伏/北山石涧/后山古冢入口）。");

                var parts = new List<EnemyPart>();
                foreach (var part in Array(entry, "parts"))
                {
                    var partId = Text(part, "id");
                    if (string.IsNullOrWhiteSpace(partId)) throw new FormatException("妖兽 " + id + " 有部位缺 id。");
                    if (parts.Any(p => p.Id == partId)) throw new FormatException("妖兽 " + id + " 的 2.0 部位重复：" + partId);
                    var multiplier = Number(part, "multiplier", id, min: .01f);
                    var dodge = Number(part, "dodgeChance", id, min: 0f, required: false, fallback: 0f);
                    if (dodge > 1f) throw new FormatException("妖兽 " + id + " 的部位 " + partId + " 抵抗率不应大于 1。");
                    var weak = part["weakPoint"].AsBool(false);
                    var bulwark = part["bulwark"].AsBool(false);
                    var partName = Text(part, "name");
                    parts.Add(new EnemyPart(partId, string.IsNullOrWhiteSpace(partName) ? partId : partName,
                        multiplier, dodge, weak, bulwark));
                }
                if (parts.Count < 2)
                    throw new FormatException("妖兽 " + id + " 的 2.0 部位至少要两个（弱点 + 平攻面）。");

                var moves = new List<CombatMove>();
                foreach (var move in Array(entry, "attacks"))
                {
                    var moveId = Text(move, "id");
                    if (moves.Any(m => m.Id == moveId)) throw new FormatException("妖兽 " + id + " 的招式重复：" + moveId);
                    var actionText = Text(move, "action");
                    var action = actionText switch
                    {
                        "light" => CombatActionType.Light,
                        "heavy" => CombatActionType.Heavy,
                        _ => throw new FormatException("妖兽 " + id + " 的招式 " + moveId +
                            " 的 action 只能是 light 或 heavy（当前：" + actionText + "）。"),
                    };
                    var startup = Number(move, "startup", id, min: .01f, field: moveId + ".startup");
                    var active = Number(move, "active", id, min: .01f, field: moveId + ".active");
                    var recovery = Number(move, "recovery", id, min: .01f, field: moveId + ".recovery");
                    var built = new CombatMove(moveId, Text(move, "name"), action,
                        (int)Number(move, "power", id, min: 0, asInt: true),
                        startup, active, recovery,
                        Number(move, "range", id, min: .01f),
                        (int)Number(move, "stamina", id, min: 0, asInt: true, required: false))
                    {
                        BleedSeconds = Number(move, "bleedSeconds", id, min: 0f, required: false),
                        Knockback = Number(move, "knockback", id, min: 0f, required: false),
                        WhiffPenalty = Number(move, "whiffPenalty", id, min: 0f, required: false),
                        Bulwark = move["bulwark"].AsBool(false),
                    };
                    moves.Add(built);
                }
                if (moves.Count == 0) throw new FormatException("妖兽 " + id + " 至少要登记一招（attacks）。");

                var ai = Object(entry, "ai");
                var behavior = Text(ai, "behavior");
                if (string.IsNullOrWhiteSpace(behavior))
                    throw new FormatException("妖兽 " + id + " 缺少 ai.behavior（独立行为树标识）。");
                var moveSetText = Text(ai, "moveset");
                var moveSet = moveSetText switch
                {
                    "legacy" => EnemyMoveSet.Legacy,
                    "profile2" => EnemyMoveSet.Profile2,
                    "" => throw new FormatException("妖兽 " + id + " 缺少 ai.moveset（legacy 或 profile2）。"),
                    _ => throw new FormatException("妖兽 " + id + " 的 ai.moveset 无法识别：" + moveSetText),
                };
                var aiProfile = new EnemyAiProfile(behavior, moveSet,
                    Number(ai, "preferredRange", id, min: .01f),
                    Number(ai, "aggressionSeconds", id, min: .01f));

                list.Add(new EnemyProfile(id, name, habitat,
                    (int)Number(entry, "maxHp", id, min: 1, asInt: true),
                    (int)Number(entry, "attack", id, min: 0, asInt: true),
                    (int)Number(entry, "defense", id, min: 0, asInt: true),
                    (int)Number(entry, "stamina", id, min: 1, asInt: true),
                    Number(entry, "moveSpeed", id, min: 0f),
                    (int)Number(entry, "coins", id, min: 0, asInt: true),
                    parts, moves, aiProfile));
            }
            if (list.Count == 0) throw new FormatException("妖兽 2.0 表里没有妖兽。");
            return new EnemyProfileTable(list, new Dictionary<string, string>());
        }

        // 与 Core 的 legacy 表对表：同一份 JSON 解析出的两套数据必须逐字同意。
        // 这不是形式主义——它挡的是"改了 hitProfile 忘了改 parts"这种一半迁移。
        public void CrossCheck(EnemyTable legacy)
        {
            foreach (var profile in profiles)
            {
                var def = legacy.Find(profile.Id);
                if (def == null) throw new FormatException("妖兽 " + profile.Id + " 在 Core 妖兽表里不存在。");
                if (def.MaxHp != profile.MaxHp || def.Attack != profile.Attack || def.Defense != profile.Defense ||
                    def.MaxStamina != profile.MaxStamina || def.Coins != profile.Coins)
                    throw new FormatException("妖兽 " + profile.Id + " 的 2.0 数值与 Core 妖兽表不一致。");
                if (Math.Abs(def.MoveSpeed - profile.MoveSpeed) > .001f)
                    throw new FormatException("妖兽 " + profile.Id + " 的移速与 Core 妖兽表不一致。");
                foreach (var zone in def.HitZones)
                {
                    var part = profile.FindPart(zone.Part);
                    if (part == null)
                        throw new FormatException("妖兽 " + profile.Id + " 的 hitProfile 部位 " + zone.Part +
                            " 没有登记进 parts——两份部位数据必须一一对应。");
                    if (Math.Abs(part.Multiplier - zone.Multiplier) > .001f)
                        throw new FormatException("妖兽 " + profile.Id + " 的部位 " + zone.Part +
                            " 倍率对不上：hitProfile=" + zone.Multiplier + "，parts=" + part.Multiplier + "。");
                }
                foreach (var part in profile.Parts)
                    if (def.HitZones.All(z => z.Part != part.Id))
                        throw new FormatException("妖兽 " + profile.Id + " 的 parts 部位 " + part.Id +
                            " 没登记进 hitProfile——Core 查不到它的倍率。");
                if (profile.Ai.MoveSet == EnemyMoveSet.Legacy)
                {
                    // legacy 招式集：必须留一招与 Core 基线轻击逐字同参，否则前台切不到旧手感。
                    var mirror = profile.Moves.FirstOrDefault(m => m.Action == CombatActionType.Light &&
                        m.Power == CombatRules.LightBaseDamage && Math.Abs(m.Startup - .12f) < .0001f &&
                        Math.Abs(m.Active - .08f) < .0001f && Math.Abs(m.Recovery - .18f) < .0001f &&
                        Math.Abs(m.Range - CombatRules.StrikeRange) < .0001f);
                    if (mirror == null)
                        throw new FormatException("妖兽 " + profile.Id + " 声明 moveset=legacy，但 attacks 里没有" +
                            "与 Core 轻击同参的那一招（power=10 / .12-.08-.18 / range=1.25）。");
                }
            }
        }

        // 掉落物品对表：未登记的 id 不当错误抛，而是进"待登记"名单——
        // 战利品名（狼皮/兽胆之类）归员工 L 的 items.json，跨域不能硬拦，但也不能装死。
        public void CrossCheckDrops(ItemTable items, EnemyTable legacy)
        {
            foreach (var profile in profiles)
            {
                var def = legacy.Find(profile.Id);
                foreach (var drop in def.Drops)
                    if (items.Find(drop.ItemId) == null)
                        pendingItemIds[drop.ItemId] = profile.Id;
            }
        }

        public IReadOnlyDictionary<string, string> PendingDropItems => pendingItemIds;

        static IEnumerable<JsonValue> Array(JsonValue parent, string key)
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

        static JsonValue Object(JsonValue parent, string key)
        {
            var value = parent[key];
            if (value.IsNull) return JsonValue.Object();
            if (value.kind != JsonValue.Kind.Object) throw new FormatException("字段 " + key + " 应是对象。");
            return value;
        }

        static string Text(JsonValue entry, string key) => entry[key].AsString(null);

        static float Number(JsonValue entry, string key, string enemyId, float min,
            bool required = true, float fallback = 0f, string field = null)
        {
            var value = entry[key];
            if (value.IsNull || value.kind != JsonValue.Kind.Number)
            {
                if (!required) return fallback;
                throw new FormatException("妖兽 " + enemyId + " 缺少字段 " + (field ?? key) + "（应为数字）。");
            }
            var number = value.AsFloat(min - 1f);
            if (number < min)
                throw new FormatException("妖兽 " + enemyId + " 的 " + (field ?? key) + " 不应小于 " + min + "。");
            return number;
        }

        // 整数口径的重载：调用方按 min:1 / min:0 传，编译器会选到这条而不是 float 那条。
        static float Number(JsonValue entry, string key, string enemyId, int min, bool asInt,
            bool required = true)
        {
            var value = entry[key];
            if (value.IsNull || value.kind != JsonValue.Kind.Number)
            {
                if (!required) return 0f;
                throw new FormatException("妖兽 " + enemyId + " 缺少字段 " + key + "（应为数字）。");
            }
            var number = value.AsInt(min - 1);
            if (number < min)
                throw new FormatException("妖兽 " + enemyId + " 的 " + key + " 不应小于 " + min + "。");
            return number;
        }
    }
}
