using System;
using System.Collections.Generic;
using System.Linq;
using TwelveJade.Core;

namespace TwelveJade.Combat
{
    // 任务书 K4（M4）：装备栏位与档位表。
    //
    // 每一条都绑一个 items.json 里真实存在的 itemId——装备不是凭空多出来的物件，行囊里得真有它。
    // items.json 归员工 L 独占，所以这里允许登记"尚未落到物品表"的档位：解析期进 pending 名单，
    // 表现层照旧显示但装不上，等 L 提交了对应物品就自动生效。硬引用不存在的 id 会让
    // EnemyTable/ItemTable 那套对表门禁当场炸掉，那不是"扩内容"，那是把别人的 Gate 拖红。
    public enum GearSlot { Weapon, Armor, Accessory }

    public sealed class GearEntry
    {
        public GearEntry(string itemId, GearSlot slot, string label, int attackBonus, int defenseBonus,
            float critBonus, int maxDurability, int regenBonus = 0)
        {
            if (maxDurability <= 0) throw new ArgumentException("装备耐久上限应为正数：" + itemId);
            if (attackBonus < 0 || defenseBonus < 0 || critBonus < 0f)
                throw new ArgumentException("装备加成为什么会是负的？" + itemId);
            ItemId = itemId; Slot = slot; Label = label;
            AttackBonus = attackBonus; DefenseBonus = defenseBonus; CritBonus = critBonus;
            MaxDurability = maxDurability; RegenBonus = regenBonus;
        }

        public string ItemId { get; }
        public GearSlot Slot { get; }
        public string Label { get; }
        public int AttackBonus { get; }
        public int DefenseBonus { get; }
        // 弱点命中的额外暴击倍率（只在锁敌/看破这条路上加，平攻不 crit）。
        public float CritBonus { get; }
        public int MaxDurability { get; }
        public int RegenBonus { get; }

        public string SlotName => Slot switch
        {
            GearSlot.Weapon => "主手兵刃",
            GearSlot.Armor => "护具",
            _ => "配饰",
        };
    }

    public sealed class GearCatalog
    {
        readonly List<GearEntry> entries;
        readonly HashSet<string> pendingItemIds;

        GearCatalog(List<GearEntry> entries, HashSet<string> pendingItemIds)
        {
            this.entries = entries;
            this.pendingItemIds = pendingItemIds;
        }

        public IReadOnlyList<GearEntry> Entries => entries;
        public IReadOnlyCollection<string> PendingItemIds => pendingItemIds;

        public GearEntry Find(string itemId) =>
            string.IsNullOrEmpty(itemId) ? null : entries.FirstOrDefault(e => e.ItemId == itemId);

        public GearEntry Find(GearSlot slot, string itemId) =>
            entries.FirstOrDefault(e => e.Slot == slot && e.ItemId == itemId);

        public IEnumerable<GearEntry> ForSlot(GearSlot slot) => entries.Where(e => e.Slot == slot);

        public bool IsPending(string itemId) => pendingItemIds.Contains(itemId);

        // 战斗域档位表：柴刀 +8 攻、铁剑 +15 攻（任务书 K4 逐字），护具与配饰同理。
        // 玄铁甲 +12 防与"皮护腕/护身红绳"依赖 items.json 里的对应条目，L 落名后一行就能接上。
        public static GearCatalog ForTown(ItemTable items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            var declared = new[]
            {
                new GearEntry("qinggang-sword", GearSlot.Weapon, "青罡剑", 22, 0, .02f, 150),
                new GearEntry("jade-lamellar", GearSlot.Armor, "玉纹轻甲", 0, 8, 0f, 130),
                new GearEntry("cloud-silk-robe", GearSlot.Armor, "流云道袍", 0, 6, 0f, 100, 1),
                new GearEntry("swift-boots", GearSlot.Accessory, "踏风履", 0, 2, .04f, 100),
                new GearEntry("cudao", GearSlot.Weapon, "柴刀", 8, 0, 0f, 60),
                new GearEntry("tiejian", GearSlot.Weapon, "铁剑", 15, 0, 0f, 120),
                new GearEntry("cubupao", GearSlot.Armor, "粗布袍", 0, 4, 0f, 80),
                new GearEntry("leather-bracer", GearSlot.Armor, "熟牛皮护腕", 0, 4, 0f, 100),
                // ADR-031 冻结的重甲档（防 +12）。items.json 目前还没有这个 id：
                // ForTown 会把它收进 pending 而不是抛错，O 落名那一刻自动生效。
                new GearEntry("dark-iron-armor", GearSlot.Armor, "玄铁甲", 0, 12, 0f, 160),
                new GearEntry("fulu", GearSlot.Accessory, "符箓", 0, 0, .03f, 40),
                new GearEntry("red-string", GearSlot.Accessory, "护身红绳", 0, 0, .05f, 60),
            };

            var active = new List<GearEntry>();
            var pending = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in declared)
            {
                var def = items.Find(entry.ItemId);
                if (def == null)
                {
                    pending.Add(entry.ItemId);
                    continue;
                }
                // 任务物不能当装备穿（与掉落对表同一条红线）。
                if (entry.Slot == GearSlot.Weapon && def.Category != "bingqi" && def.Category != "qiyong")
                    throw new FormatException("装备档位 " + entry.ItemId + " 声称是兵刃，物品表里的类别却是 " + def.Category + "。");
                if (entry.Slot == GearSlot.Armor && def.Category != "qiyong" && def.Category != "cailiao")
                    throw new FormatException("装备档位 " + entry.ItemId + " 声称是护具，物品表里的类别却是 " + def.Category + "。");
                if (def.Category == "renwu")
                    throw new FormatException("任务物不能登记成装备：" + entry.ItemId);
                active.Add(entry);
            }
            return new GearCatalog(active, pending);
        }
    }
}
