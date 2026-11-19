using System;
using System.Collections.Generic;
using System.Linq;
using TwelveJade.Core;

namespace TwelveJade.Combat
{
    // 任务书 N（N3 / D-09）：战斗内服药。
    //
    // 为什么只在战斗里吃药：ADR-034 已裁"世界态没有 hp/stamina"（SaveData 里根本没有这两个字段），
    // 回血只能回到战斗内那张条上（Core/Combat.cs 的 CombatActorState.Hp）。
    //
    // 为什么这个文件要自己去读 items.json 原文：ItemTable/ItemDef 只暴露 id/name/tier/category/
    // icon/stack/price/description 八个字段，装不下"这瓶药回多少血"。表现层写死一个 45 是最省事的
    // 做法，也是最坏的做法——浮签上的数字与实际结算的数字会各说各话（T047 的根因就是这个）。
    // 所以回复量的真源只有两处，按优先级：
    //   1) items.json 里该物品的 `combatHeal` 字段（结构化，**目前还没有**，已挂给员工 O）；
    //   2) 物品描述里明写的"回 N 点气血"（浮签上写多少就回多少，描述即契约）；
    // 两处都取不到 → **拒用**并如实说"没写明能回多少"。宁可点不动，也不假装有效果。
    public sealed class ConsumableEffect
    {
        public ConsumableEffect(string itemId, string name, int healHp, string source)
        {
            ItemId = itemId; Name = name; HealHp = healHp; Source = source;
        }

        public string ItemId { get; }
        public string Name { get; }
        public int HealHp { get; }
        /// <summary>数值从哪来的：字段 combatHeal / 描述文案。断言要靠它证明"没写死"。</summary>
        public string Source { get; }
    }

    public sealed class ConsumeResult
    {
        public bool Executed;
        public int Healed;
        public int HpAfter;
        public float ExposureSeconds;
        public ConsumableEffect Effect;
        public string Reason = "";
    }

    public static class CombatConsumables
    {
        // 与员工 O 的跨线契约（本文件是唯一的读方；items.json 由 O 落字段）
        public const string EffectFieldKey = "combatHeal";
        public static readonly IReadOnlyList<string> UsableCategories = new[] { "yaodan" };

        static readonly Dictionary<string, ConsumableEffect> cache = new();
        // 字段表按"物品表原文"缓存一次：原文换版（读档/热重载）就整体作废，
        // 绝不出现"缓存里是上一份 items.json 的数"这种跨版本脏读（T027 的老坑）。
        static readonly Dictionary<string, int> fieldCache = new();
        static string cachedJson;

        /// <summary>换档/换表就重解析：缓存只认原文。</summary>
        public static void Invalidate()
        {
            cache.Clear();
            fieldCache.Clear();
            cachedJson = null;
        }

        static void EnsureParsed(string itemsJson)
        {
            if (itemsJson == cachedJson) return;
            cache.Clear();
            fieldCache.Clear();
            cachedJson = itemsJson ?? "";
            if (string.IsNullOrEmpty(itemsJson) || !Json.TryParse(itemsJson, out var root, out _)) return;
            var list = root["items"];
            if (list.kind != JsonValue.Kind.Array) return;
            foreach (var entry in list.Items)
            {
                if (entry == null || entry.kind != JsonValue.Kind.Object) continue;
                var id = entry["id"].AsString(null);
                var field = entry[EffectFieldKey];
                if (string.IsNullOrEmpty(id) || field.IsNull || field.kind != JsonValue.Kind.Number) continue;
                fieldCache[id] = Math.Max(0, field.AsInt(0));
            }
        }

        public static ConsumableEffect EffectFor(ItemTable items, string itemsJson, string itemId)
        {
            if (items == null || string.IsNullOrEmpty(itemId)) return null;
            EnsureParsed(itemsJson);
            if (cache.TryGetValue(itemId, out var known)) return known;

            var def = items.Find(itemId);
            if (def == null)
            {
                var missing = new ConsumableEffect(itemId, itemId, 0, "物品表里没有这一件");
                cache[itemId] = missing;
                return missing;
            }
            if (!UsableCategories.Contains(def.Category))
            {
                var wrong = new ConsumableEffect(itemId, def.Name, 0, "类别 " + def.Category + " 不能在战斗中服用");
                cache[itemId] = wrong;
                return wrong;
            }

            // 1) 结构化字段优先
            fieldCache.TryGetValue(itemId, out var heal);
            var source = heal > 0 ? "字段 " + EffectFieldKey : "";
            // 2) 描述兜底：浮签上写了多少就回多少
            if (heal <= 0 && TryReadHealFromDescription(def.Description, out var fromText))
            {
                heal = fromText;
                source = "描述文案";
            }
            var effect = new ConsumableEffect(itemId, def.Name, heal,
                heal > 0 ? source : "物品表与描述都没写明回复量");
            cache[itemId] = effect;
            return effect;
        }

        /// <summary>
        /// 从"战斗中服用瞬间止血、回复气血"这类句子里抠出"回 N 点"的 N。
        /// 只认"回"字后面紧跟的十进制数字：没有数字就返回 false——这正是当前 items.json 的状态，
        /// 也是本轮必须挂给员工 O 的那条（不许我替物品表编一个数）。
        /// </summary>
        public static bool TryReadHealFromDescription(string description, out int heal)
        {
            heal = 0;
            if (string.IsNullOrEmpty(description)) return false;
            for (var i = 0; i < description.Length; i++)
            {
                if (description[i] != '回') continue;
                var j = i + 1;
                // 容许"回复 45 点""瞬回45点""回 45 点气血"三种写法：跳过空格，读数字
                while (j < description.Length && description[j] == ' ') j++;
                var start = j;
                while (j < description.Length && description[j] >= '0' && description[j] <= '9') j++;
                if (j == start) continue;
                var digits = description.Substring(start, j - start);
                if (!int.TryParse(digits, out var parsed) || parsed <= 0) continue;
                heal = parsed;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 服用一剂：扣行囊 1 件 → 按真源数值回血（钳到 maxHp）→ 付一个破绽窗
        /// （复用 CombatRules.ExposureSeconds，吃药不是白嫖）。
        /// 气血满 / 数量不足 / 取不到回复量 → 一律不执行，且不改行囊、不改状态机。
        /// </summary>
        public static ConsumeResult Consume(SaveData save, ItemTable items, string itemsJson,
            string itemId, CombatActorState player, float now)
        {
            var result = new ConsumeResult();
            if (player == null || player.IsDead)
            {
                result.Reason = "人已经倒了，灌不进去。";
                return result;
            }
            var effect = EffectFor(items, itemsJson, itemId);
            result.Effect = effect;
            if (effect == null || effect.HealHp <= 0)
            {
                result.Reason = (effect?.Name ?? "这味药") + "没写明能回多少气血，不敢乱用。";
                return result;
            }
            var bag = save?.bag;
            if (bag == null || InventoryRules.Count(bag, itemId) <= 0)
            {
                result.Reason = "行囊里没有" + effect.Name + "。";
                return result;
            }
            if (player.Hp >= player.MaxHp)
            {
                // 单一口径：满血就是不给用（另一处"允许浪费"的口径绝不允许出现）
                result.Reason = "气血没亏，先留着。";
                return result;
            }
            if (!InventoryRules.Remove(bag, itemId, 1))
            {
                result.Reason = "行囊没扣下这一件，药也没喝。";
                return result;
            }
            var before = player.Hp;
            // RestoreHp 是 Core 唯一的生命写入口，自带 maxHp 钳制与"至少留一口气"
            CombatRules.RestoreHp(player, player.Hp + effect.HealHp);
            result.Executed = true;
            result.Healed = player.Hp - before;
            result.HpAfter = player.Hp;
            result.ExposureSeconds = CombatRules.ExposureSeconds;
            // 服药要低头灌药，这一瞬就是破绽：Core 的拒绝路径干净，惩罚由调用方施加
            CombatRules.EnterExposure(player, now);
            result.Reason = "";
            return result;
        }

        /// <summary>行囊里能用的丹药（有真源回复量的那些），按回复量从高到低。</summary>
        public static List<string> UsableIn(SaveData save, ItemTable items, string itemsJson)
        {
            var found = new List<string>();
            if (save?.bag == null || items == null) return found;
            foreach (var def in items.Items)
            {
                if (!UsableCategories.Contains(def.Category)) continue;
                if (InventoryRules.Count(save.bag, def.Id) <= 0) continue;
                if ((EffectFor(items, itemsJson, def.Id)?.HealHp ?? 0) <= 0) continue;
                found.Add(def.Id);
            }
            found.Sort((a, b) => (EffectFor(items, itemsJson, b)?.HealHp ?? 0)
                .CompareTo(EffectFor(items, itemsJson, a)?.HealHp ?? 0));
            return found;
        }
    }
}
