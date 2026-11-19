using System;
using System.Collections.Generic;
using System.Linq;

namespace TwelveJade.Core
{
    // M4-02 妖兽配置：第一只野猪妖的数据化——属性、部位倍率、掉落与赏钱全在 JSON 里，
    // 前台不写死任何数值。校验风格与商人表一致：缺字段、负数、重复 id 都带字段名报错。
    public sealed class HitZone
    {
        public HitZone(string part, float multiplier) { Part = part; Multiplier = multiplier; }

        public string Part { get; }
        public float Multiplier { get; }
    }

    public sealed class DropLine
    {
        public DropLine(string itemId, int count) { ItemId = itemId; Count = count; }

        public string ItemId { get; }
        public int Count { get; }
    }

    public sealed class EnemyDef
    {
        public EnemyDef(string id, string name, int maxHp, int attack, int defense, int maxStamina,
            float moveSpeed, List<HitZone> hitZones, List<DropLine> drops, int coins)
        {
            Id = id; Name = name; MaxHp = maxHp; Attack = attack; Defense = defense;
            MaxStamina = maxStamina; MoveSpeed = moveSpeed;
            HitZones = hitZones; Drops = drops; Coins = coins;
        }

        public string Id { get; }
        public string Name { get; }
        public int MaxHp { get; }
        public int Attack { get; }
        public int Defense { get; }
        public int MaxStamina { get; }
        public float MoveSpeed { get; }
        public IReadOnlyList<HitZone> HitZones { get; }
        public IReadOnlyList<DropLine> Drops { get; }
        public int Coins { get; }

        // 部位倍率：没配的部位按 1 倍算，前台不必逐个查表。
        public float MultiplierOf(string part) =>
            string.IsNullOrEmpty(part) ? 1f :
            HitZones.FirstOrDefault(z => z.Part == part)?.Multiplier ?? 1f;
    }

    public sealed class EnemyTable
    {
        readonly List<EnemyDef> enemies;

        public EnemyTable(List<EnemyDef> enemies) { this.enemies = enemies; }

        public IReadOnlyList<EnemyDef> Enemies => enemies;
        public EnemyDef Find(string id) => id == null ? null : enemies.FirstOrDefault(e => e.Id == id);

        public static EnemyTable Parse(string json)
        {
            if (!Json.TryParse(json, out var root, out var error))
                throw new FormatException("妖兽表读取失败：" + error);
            if (root.kind != JsonValue.Kind.Object) throw new FormatException("妖兽表应是一个对象。");

            var enemies = new List<EnemyDef>();
            foreach (var entry in Elements(root, "enemies"))
            {
                var id = Required(entry, "id");
                if (enemies.Any(e => e.Id == id)) throw new FormatException("妖兽 id 重复：" + id);

                var maxHp = IntField(entry, "maxHp", id, min: 1);
                var attack = IntField(entry, "attack", id, min: 0);
                var defense = IntField(entry, "defense", id, min: 0);
                var maxStamina = IntField(entry, "stamina", id, min: 1);
                var coins = IntField(entry, "coins", id, min: 0);
                var moveSpeed = FloatField(entry, "moveSpeed", id, min: 0f);

                var zones = new List<HitZone>();
                foreach (var zone in Elements(entry, "hitProfile"))
                {
                    var part = Required(zone, "part");
                    if (zones.Any(z => z.Part == part))
                        throw new FormatException("妖兽 " + id + " 的部位重复：" + part);
                    var multiplier = zone["multiplier"].AsFloat(0f);
                    if (multiplier <= 0f)
                        throw new FormatException("妖兽 " + id + " 的部位 " + part + " 倍率应为正数。");
                    zones.Add(new HitZone(part, multiplier));
                }
                if (zones.Count == 0) throw new FormatException("妖兽 " + id + " 至少要有一个受击部位。");

                var drops = new List<DropLine>();
                foreach (var drop in Elements(entry, "drops"))
                {
                    var itemId = Required(drop, "itemId");
                    if (drops.Any(d => d.ItemId == itemId))
                        throw new FormatException("妖兽 " + id + " 的掉落 " + itemId + " 重复出现。");
                    var count = drop["count"].AsInt(1);
                    if (count < 1) throw new FormatException("妖兽 " + id + " 的掉落 " + itemId + " 数量应为正数。");
                    drops.Add(new DropLine(itemId, count));
                }

                enemies.Add(new EnemyDef(id, Required(entry, "name"), maxHp, attack, defense, maxStamina,
                    moveSpeed, zones, drops, coins));
            }
            if (enemies.Count == 0) throw new FormatException("妖兽表里没有妖兽。");
            return new EnemyTable(enemies);
        }

        // 与物品表对表：掉落必须是登记过的普通物品，任务物不能当战利品刷出来。
        public void CrossCheck(ItemTable items)
        {
            foreach (var enemy in enemies)
            {
                foreach (var drop in enemy.Drops)
                {
                    var def = items.Find(drop.ItemId);
                    if (def == null)
                        throw new FormatException("妖兽 " + enemy.Id + " 的掉落不在物品表里：" + drop.ItemId);
                    if (def.Category == "renwu")
                        throw new FormatException("妖兽 " + enemy.Id + " 的掉落是任务物，不能重复刷出：" + drop.ItemId);
                }
            }
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

        static int IntField(JsonValue entry, string key, string enemyId, int min)
        {
            var value = entry[key];
            if (value.IsNull || value.kind != JsonValue.Kind.Number)
                throw new FormatException("妖兽 " + enemyId + " 缺少字段 " + key + "（应为数字）。");
            var number = value.AsInt(min - 1);
            if (number < min)
                throw new FormatException("妖兽 " + enemyId + " 的 " + key + " 不应小于 " + min + "。");
            return number;
        }

        static float FloatField(JsonValue entry, string key, string enemyId, float min)
        {
            var value = entry[key];
            if (value.IsNull || value.kind != JsonValue.Kind.Number)
                throw new FormatException("妖兽 " + enemyId + " 缺少字段 " + key + "（应为数字）。");
            var number = value.AsFloat(min - 1f);
            if (number < min)
                throw new FormatException("妖兽 " + enemyId + " 的 " + key + " 不应小于 " + min + "。");
            return number;
        }

        static string Required(JsonValue entry, string key)
        {
            var value = entry[key].AsString(null);
            if (string.IsNullOrWhiteSpace(value)) throw new FormatException("缺少字段 " + key + "（应为非空字符串）。");
            return value;
        }
    }

    public sealed class SpoilsResult
    {
        public int CoinsGranted;
        public readonly List<(string itemId, int granted, int leftover)> Drops = new();

        public bool EverythingFits => Drops.All(d => d.leftover == 0);
        public string Describe(ItemTable items = null) => "铜钱 " + CoinsGranted + " 文　" + string.Join("、",
            Drops.Select(d =>
            {
                var name = items?.Find(d.itemId)?.Name ?? d.itemId;
                return d.leftover > 0
                    ? name + "×" + d.granted + "（装不下 " + d.leftover + "）"
                    : name + "×" + d.granted;
            }));
    }

    // 战斗胜利奖励：由 Core 统一发——铜钱直接进档（带存档上限护栏），
    // 掉落走行囊规则的实际放入数量；装不下的如实报余量，不凭空增减。
    public static class CombatSpoils
    {
        public static SpoilsResult Grant(SaveData save, EnemyDef enemy, ItemTable items)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));
            var result = new SpoilsResult();
            if (enemy == null) return result;
            result.CoinsGranted = Math.Min(enemy.Coins, SaveData.MaxCoins - save.coins);
            save.coins += result.CoinsGranted;
            foreach (var drop in enemy.Drops)
            {
                var leftover = InventoryRules.Add(save.bag, items, drop.ItemId, drop.Count);
                result.Drops.Add((drop.ItemId, drop.Count - leftover, leftover));
            }
            return result;
        }
    }
}
