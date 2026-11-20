using System;
using System.Collections.Generic;
using System.Linq;
using TwelveJade.Core;

namespace TwelveJade.Combat
{
    // Equipment is worn only by explicit choice or a saved GearLedger entry.
    // RestoreLedger binds chapter access without altering saved ownership or durability.
    // Dulled or chapter-locked gear keeps its record; its effective bonuses are zero.
    public sealed class EquippedGear
    {
        readonly Func<bool> available;
        public bool IsAvailable => available?.Invoke() ?? true;
        public EquippedGear(GearEntry entry, Func<bool> available = null)
        {
            Entry = entry ?? throw new ArgumentNullException(nameof(entry));
            Durability = entry.MaxDurability;
            this.available = available;
        }

        public GearEntry Entry { get; }
        public int Durability { get; private set; }
        public bool IsDulled => Durability <= 0;
        public float Condition => Entry.MaxDurability <= 0 ? 0f :
            (float)Durability / Entry.MaxDurability;

        // 钝损之后这些加成一律归零——"暂时失效"就是这个意思。
        public int EffectiveAttack => IsDulled || !IsAvailable ? 0 : Entry.AttackBonus;
        public int EffectiveDefense => IsDulled || !IsAvailable ? 0 : Entry.DefenseBonus;
        public float EffectiveCritBonus => IsDulled || !IsAvailable ? 0f : Entry.CritBonus;
        public int EffectiveRegenBonus => IsDulled || !IsAvailable ? 0 : Entry.RegenBonus;

        public void Wear(int points, int reduction = 0)
        {
            if (points <= 0) return;
            var cost = Math.Max(0, points - reduction);
            if (cost == 0) return;
            Durability = Math.Max(0, Durability - cost);
        }

        public void Restore() => Durability = Entry.MaxDurability;

        public int MissingDurability => Math.Max(0, Entry.MaxDurability - Durability);
    }

    public sealed class RepairResult
    {
        public bool Done;
        public int CoinsPaid;
        public string Reason = "";
        public GearSlot Slot;
        public int DurabilityRestored;
    }

    // 一个角色的三件装备 + 命途映射 = 这一场战斗的战力。
    public sealed class CombatEquipmentService
    {
        public const int LightSwingWear = 1, HeavySwingWear = 2, WoundWear = 2;
        // 修补价：每 10 点耐久 1 文，最低 4 文——"少量铜钱"，不至于劝退，也不至于免费。
        public const int RepairCoinPerTenDurability = 10, RepairMinCoins = 4;

        readonly Dictionary<GearSlot, EquippedGear> equipped = new();
        SaveData saveContext;
        bool Allowed(string itemId) => saveContext == null || PrologueRules.CanCultivate(saveContext) || Catalog.IsMundane(itemId);

        public CombatEquipmentService(GearCatalog catalog, CombatDestinyTable destinyTable, string destinyId)
        {
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            DestinyTable = destinyTable ?? CombatDestinyTable.Standard;
            DestinyId = destinyId ?? "";
        }

        public GearCatalog Catalog { get; }
        public CombatDestinyTable DestinyTable { get; }
        public string DestinyId { get; private set; }

        public EquippedGear Weapon => Get(GearSlot.Weapon);
        public EquippedGear Armor => Get(GearSlot.Armor);
        public EquippedGear Accessory => Get(GearSlot.Accessory);

        public CombatDestinyModifiers DestinyModifiers => DestinyTable.For(DestinyId);

        EquippedGear Get(GearSlot slot) => equipped.TryGetValue(slot, out var gear) ? gear : null;

        public bool IsEquipped(GearSlot slot) => equipped.ContainsKey(slot);
        public int EquippedCount => equipped.Count;
        public IEnumerable<GearSlot> OccupiedSlots => equipped.Keys.OrderBy(s => s).ToList();

        public string LastRefusal { get; private set; } = "";

        // 装上一件：物品表里没有（L 还没落名）就拒装，如实留个原因给表现层提示。
        public bool Equip(GearSlot slot, string itemId)
        {
            LastRefusal = "";
            var entry = Catalog.Find(itemId);
            if (entry == null)
            {
                LastRefusal = Catalog.IsPending(itemId)
                    ? "这件东西还没落进物品表，穿不上。"
                    : "它不是战斗装备。";
                return false;
            }
            if (entry.Slot != slot)
            {
                LastRefusal = "它该穿在" + entry.SlotName + "。";
                return false;
            }
            if (!Allowed(itemId)) { LastRefusal = PrologueRules.Refusal; return false; }
            equipped[slot] = new EquippedGear(entry, () => Allowed(entry.ItemId));
            return true;
        }

        public bool Equip(string itemId)
        {
            var entry = Catalog.Find(itemId);
            if (entry == null) return Equip(GearSlot.Weapon, itemId);
            return Equip(entry.Slot, itemId);
        }

        public EquippedGear Unequip(GearSlot slot)
        {
            if (!equipped.TryGetValue(slot, out var gear)) return null;
            equipped.Remove(slot);
            return gear;
        }

        // Read the persisted loadout without repairing it, inventing ownership or auto-equipping bag items.
        public void RestoreLedger(SaveData save)
        {
            saveContext = save;
            equipped.Clear();
            foreach (var line in save?.gear ?? Array.Empty<GearLine>())
            {
                if(line==null || InventoryRules.Count(save.bag,line.itemId)<1)continue;
                var entry=Catalog.Find(line.itemId);
                string slot=entry?.Slot==GearSlot.Weapon?GearSlots.Weapon:entry?.Slot==GearSlot.Armor?GearSlots.Armor:GearSlots.Accessory;
                if(entry==null || line.slot!=slot)continue;
                // Restore ownership and exact wear even when its effects are locked for this chapter.
                equipped[entry.Slot] = new EquippedGear(entry, () => Allowed(entry.ItemId));
                Get(entry.Slot).Wear(Math.Max(0,entry.MaxDurability-line.durability));
            }
        }

        public void Clear() => equipped.Clear();

        public void SetDestiny(string destinyId) => DestinyId = destinyId ?? "";

        // 战力汇总（任务书 K4-2）：TotalAttack = 基础 + 兵刃 + 命途。
        // 裸装且无命途加成时，两个 Total 与基础值逐字相等——既有夹具吃的就是这条。
        public int TotalAttack(int baseAttack) =>
            baseAttack + equipped.Values.Sum(g => g.EffectiveAttack) + DestinyModifiers.AttackBonus;

        public int TotalDefense(int baseDefense) =>
            baseDefense + equipped.Values.Sum(g => g.EffectiveDefense) + DestinyModifiers.DefenseBonus;

        public float CritBonus => equipped.Values.Sum(g => g.EffectiveCritBonus) + DestinyModifiers.CritBonus;

        public float StaminaRegenPerSecond => CombatDepth.Standard20.StaminaRegenPerSecond + DestinyModifiers.RegenBonus + equipped.Values.Sum(g => g.EffectiveRegenBonus);

        public float RiposteWindowSeconds =>
            Math.Max(.01f, CombatDepth.Standard20.RiposteWindowSeconds + DestinyModifiers.RiposteBonusSeconds);

        public int BleedBonus => DestinyModifiers.BleedDamage;

        // 耐久损耗：武器看招式，护具看受创。命途的损耗减免在这里生效。
        public void WearWeapon(CombatActionType action)
        {
            var wear = action switch
            {
                CombatActionType.Heavy => HeavySwingWear,
                CombatActionType.Light => LightSwingWear,
                _ => 0,
            };
            if (wear > 0 && Weapon?.IsAvailable == true) Weapon.Wear(wear, DestinyModifiers.WearReduction);
        }

        public void WearArmor(int hits = 1)
        {
            if (hits <= 0) return;
            if (Armor?.IsAvailable == true) Armor.Wear(WoundWear * hits, DestinyModifiers.WearReduction);
        }

        public bool WeaponDulled => Weapon != null && Weapon.IsDulled;
        public bool ArmorDulled => Armor != null && Armor.IsDulled;

        // 器刃钝损：Core 的修正量归零，不删装备、不抛异常。
        public void ApplyTo(CombatActorState actor, int baseAttack, int baseDefense)
        {
            if (actor == null) return;
            actor.AttackModifier = TotalAttack(baseAttack) - baseAttack;
            actor.DefenseModifier = TotalDefense(baseDefense) - baseDefense;
            actor.Tuning = new CombatTuning(StaminaRegenPerSecond,
                CombatDepth.Standard20.DodgeActiveSeconds, RiposteWindowSeconds);
        }

        // 铁匠铺修补接口（赵大）：扣少量铜钱，把一件装备恢复到满耐久。
        // 铜钱不够就原样退回，不扣钱、不掉耐久、不抛异常——战斗域绝不允许把钱算成负的。
        public RepairResult RepairAtSmithy(SaveData save, GearSlot slot)
        {
            var result = new RepairResult { Slot = slot };
            var gear = Get(slot);
            if (gear == null)
            {
                result.Reason = "这一件身上没穿。";
                return result;
            }
            if (!gear.IsAvailable) { result.Reason = PrologueRules.Refusal; return result; }
            if (gear.Durability >= gear.Entry.MaxDurability)
            {
                result.Reason = "兵刃还利着，不用磨。";
                return result;
            }
            var missing = gear.MissingDurability;
            var cost = Math.Max(RepairMinCoins, missing / RepairCoinPerTenDurability);
            if (save == null)
            {
                result.Reason = "没带钱，也没带档。";
                return result;
            }
            if (save.coins < cost)
            {
                result.Reason = "铜钱不够，赵大说下回来也一样。";
                return result;
            }
            save.coins = Math.Max(0, save.coins - cost);
            gear.Restore();
            result.Done = true;
            result.CoinsPaid = cost;
            result.DurabilityRestored = missing;
            result.Reason = "";
            return result;
        }

        public RepairResult RepairAllAtSmithy(SaveData save)
        {
            var total = new RepairResult();
            foreach (var slot in OccupiedSlots.ToList())
            {
                var one = RepairAtSmithy(save, slot);
                if (!one.Done) continue;
                total.Done = true;
                total.CoinsPaid += one.CoinsPaid;
                total.DurabilityRestored += one.DurabilityRestored;
            }
            total.Reason = total.Done ? "" : "没有需要修补的。";
            return total;
        }

        public string Describe()
        {
            var lines = new List<string>();
            foreach (var slot in new[] { GearSlot.Weapon, GearSlot.Armor, GearSlot.Accessory })
                lines.Add(DescribeSlot(slot));
            return string.Join("　", lines);
        }

        string DescribeSlot(GearSlot slot)
        {
            var bare = slot switch
            {
                GearSlot.Weapon => "主手：空手",
                GearSlot.Armor => "护具：布衣",
                _ => "配饰：无",
            };
            var gear = Get(slot);
            if (gear == null) return bare;
            var prefix = slot switch
            {
                GearSlot.Weapon => "主手：",
                GearSlot.Armor => "护具：",
                _ => "配饰：",
            };
            return (!gear.IsAvailable ? "（尚未入门，效用暂封）" : gear.IsDulled ? "（钝损）" : "") + prefix + gear.Entry.Label +
                " 耐久 " + gear.Durability + "/" + gear.Entry.MaxDurability;
        }
    }
}
