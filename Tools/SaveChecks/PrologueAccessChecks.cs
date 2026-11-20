using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using TwelveJade.Combat;
using TwelveJade.Economy;

namespace TwelveJade.Core
{
    // Real data and persistence: locked actions must leave the player's entire ledger unchanged.
    static class PrologueAccessChecks
    {
        static int assertions;
        static readonly JsonSerializerOptions JsonOptions = new() { IncludeFields = true };
        static string Snapshot(SaveData save) => JsonSerializer.Serialize(save, JsonOptions);
        static void Check(string label, bool value)
        {
            assertions++;
            if (!value) throw new Exception("Prologue access: " + label);
        }
        static SaveData Fresh() => new() { slot = 1, characterId = "farmer", characterName = "行路人", coins = 800 };
        static void Flags(SaveData save, int mode)
        {
            save.oneTimeFlags = (mode == 0 ? Array.Empty<string>() : mode == 1 ? new[] { PrologueRules.DepartureFlag } :
                mode == 2 ? new[] { PrologueRules.InitiationFlag } : new[] { PrologueRules.DepartureFlag, PrologueRules.InitiationFlag });
        }
        static void OldRecords(SaveData save, ItemTable items)
        {
            save.cultivation = new CultivationState { opened = 3, attempts = 3 };
            save.manualStudies = ManualStudyRules.Skills.Where(s => !s.Basic).Select(s => new ManualStudyEntry {
                id = s.Id, learned = true, attempts = 1, recorded = true, progress = 100 }).ToArray();
            foreach (string id in new[] { "dark-iron-armor", "red-dew-pill", "jinchuangyao", "elixir-warm", "manual-sword-fragment", "wheat-flour", "cudao" })
                InventoryRules.Add(save.bag, items, id, 2);
            GearLedger.Record(save, GearSlots.Armor, "dark-iron-armor", 83);
        }
        public static void Run(string configDir)
        {
            assertions = 0;
            string json = File.ReadAllText(Path.Combine(configDir, "items.json"));
            var items = ItemTable.Parse(json);
            var recipes = RecipeTable.Parse(File.ReadAllText(Path.Combine(configDir, "recipes.json")));
            var basic = recipes.Find("recipe-chuibing");
            Check("null has no cultivation access", !PrologueRules.CanCultivate(null));
            Check("ordinary cooking recipe exists", basic != null && basic.CraftType == Crafting.Cooking && items.Find(basic.ResultItemId)?.Tier == "fanpin");
            for (int mode = 0; mode < 4; mode++)
            {
                var save = Fresh(); OldRecords(save, items); Flags(save, mode);
                Check("two flags are both required " + mode, PrologueRules.CanCultivate(save) == (mode == 3));
                Check("recorded meridians remain readable " + mode, CultivationRules.Opened(save) == 3);
                Check("basic combat remains learned " + mode, new[] { "light", "heavy", "dodge" }.All(id => ManualStudyRules.Learned(save, id)));
                if (mode == 3)
                {
                    Check("old abilities resume after actual initiation", ManualStudyRules.Learned(save, "sword") && ManualStudyRules.AttackBonus(save) == 2 && CultivationRules.HpBonus(save) == 5);
                    var gear = new CombatEquipmentService(GearCatalog.ForTown(items), CombatDestinyTable.Standard, "");
                    gear.RestoreLedger(save);
                    Check("old armor resumes at exact durability", gear.Armor.Durability == 83 && gear.TotalDefense(2) == 14);
                    Check("later recipes remain implemented", recipes.Recipes.All(r => Crafting.IsAvailable(save, items, r)));
                    Check("later medicines remain available", CombatConsumables.UsableIn(save, items, json).Contains("red-dew-pill"));
                    continue;
                }
                string before = Snapshot(save);
                Check("meridian attempt refuses without changes " + mode, !CultivationRules.Attempt(save, 3, true, 0).Attempted && Snapshot(save) == before);
                Check("no old cultivation benefits " + mode, CultivationRules.Chance(save, true) == 0 && CultivationRules.HpBonus(save) == 0 && CultivationRules.StaminaBonus(save) == 0);
                Check("no old manual benefits " + mode, !ManualStudyRules.Learned(save, "sword") && ManualStudyRules.HpBonus(save) == 0 && ManualStudyRules.StaminaBonus(save) == 0 && ManualStudyRules.AttackBonus(save) == 0 && StudyPathRules.Comprehension(save) == 10);
                Check("old manuals cannot bypass gate " + mode, !ManualStudyRules.StudyAndCommit(save, items, "sword", 0, _ => throw new Exception("unexpected commit")).Attempted && Snapshot(save) == before);
                foreach (StudyApproach approach in Enum.GetValues(typeof(StudyApproach)))
                    Check("study refuses without commit " + mode + "/" + approach,
                        !StudyPathRules.ActAndCommit(save, items, "sword", approach, .5, _ => throw new Exception("unexpected commit")).Changed && Snapshot(save) == before);
                foreach (var recipe in recipes.Recipes.Where(r => r.Id != basic.Id))
                {
                    Check("hidden recipe cannot be called directly " + mode + "/" + recipe.Id, !Crafting.IsAvailable(save, items, recipe));
                    Check("craft rejection preserves ledger " + mode + "/" + recipe.Id,
                        Crafting.Craft(save, items, recipe, new NoRandom(), out var reason) == 0 && reason == PrologueRules.Refusal && Snapshot(save) == before);
                }
                var player = new CombatActorState("hero", "行路人", 100, 10, 2, 60); CombatRules.RestoreHp(player, 20);
                Check("usable list contains only ordinary medicine " + mode, CombatConsumables.UsableIn(save, items, json).SequenceEqual(new[] { "jinchuangyao" }));
                var denied = CombatConsumables.Consume(save, items, json, "red-dew-pill", player, 0);
                Check("advanced medicine direct call is inert " + mode, !denied.Executed && denied.Reason == PrologueRules.Refusal && Snapshot(save) == before && player.Hp == 20 && player.Phase == CombatPhase.Idle);
                var equipment = new CombatEquipmentService(GearCatalog.ForTown(items), CombatDestinyTable.Standard, "");
                equipment.RestoreLedger(save);
                Check("old armor retained but ineffective " + mode, equipment.Armor?.Durability == 83 && !equipment.Armor.IsAvailable && equipment.TotalDefense(2) == 2);
                equipment.WearArmor();
                Check("inactive armor does not wear or repair " + mode, equipment.Armor.Durability == 83 && !equipment.RepairAtSmithy(save, GearSlot.Armor).Done && Snapshot(save) == before);
                Check("advanced equip direct call refused " + mode, !equipment.Equip("dark-iron-armor"));
                Check("ordinary weapon still works " + mode, equipment.Equip("cudao") && equipment.TotalAttack(10) == 18);
                var healed = CombatConsumables.Consume(save, items, json, "jinchuangyao", player, 0);
                Check("ordinary medicine heals 25 and consumes one " + mode, healed.Executed && healed.Healed == 25 && player.Hp == 45 && InventoryRules.Count(save.bag, "jinchuangyao") == 1);
                Check("ordinary cooking is available " + mode, Crafting.IsAvailable(save, items, basic));
                Check("ordinary cooking makes two cakes " + mode, Crafting.Craft(save, items, basic, new Random(9), out _) == 2 && InventoryRules.Count(save.bag, "wheat-flour") == 1 && InventoryRules.Count(save.bag, "chuibing") == 2 && CraftLedger.TimesOf(save, basic.Id) == 1);
            }
            var empty = Fresh(); empty.bag = null;
            Check("rejection does not initialize a missing bag", Crafting.Craft(empty, items, recipes.Find("recipe-jinchuang-san"), new NoRandom(), out _) == 0 && empty.bag == null);
            Check("no shipped content auto-grants initiation", new[] { "chapters.json", "quests.json", "dialogues.json" }.All(f => !File.ReadAllText(Path.Combine(configDir, f)).Contains(PrologueRules.InitiationFlag)));
            string dir = Path.Combine(Path.GetTempPath(), "ejs-prologue-checks-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var repository = new SaveRepository(dir, new JsonCodec());
                var old = repository.Create(1, "farmer", "旧日行者"); OldRecords(old, items);
                var bag = JsonSerializer.Serialize(old.bag, JsonOptions);
                var learned = JsonSerializer.Serialize(old.manualStudies, JsonOptions);
                repository.Write(old);
                for (int repeat = 0; repeat < 2; repeat++)
                {
                    var slot = repository.Read(1); Check("legacy save loads " + repeat, slot.State == SlotState.Ready);
                    var loaded = slot.Data;
                    Check("legacy inventory exact after reload " + repeat, JsonSerializer.Serialize(loaded.bag, JsonOptions) == bag);
                    Check("legacy records exact after reload " + repeat, JsonSerializer.Serialize(loaded.manualStudies, JsonOptions) == learned && CultivationRules.Opened(loaded) == 3 && GearLedger.Of(loaded, GearSlots.Armor)?.durability == 83);
                    Check("legacy records never grant entry " + repeat, !PrologueRules.CanCultivate(loaded) && !ManualStudyRules.Learned(loaded, "thunder"));
                    repository.Write(loaded);
                }
            }
            finally { Directory.Delete(dir, true); }
            Console.WriteLine("PASS: Prologue access " + assertions + " assertions (new, partial flags, legacy and initiated fixtures)");
        }
        sealed class NoRandom : Random
        {
            public override double NextDouble() => throw new Exception("Refused action consumed RNG");
        }
    }
}
