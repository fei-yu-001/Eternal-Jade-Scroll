using System;
using System.Linq;
using TMPro;
using TwelveJade.Combat;
using TwelveJade.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    public sealed partial class FrontEndController
    {
        WorldCombatSession worldBattle;
        WildActor worldBattleTarget;
        CombatActorState worldPlayerState;
        SaveData worldPlayerOwner;
        CombatEquipmentService worldBattleGear;
        CombatSkillWheel worldSkillWheel;
        WorldCombatEffects worldBattleEffects;
        ActionCombatPose worldBattlePose;
        RectTransform worldTargetHud;
        TMP_Text worldTargetName, worldTargetHealth, worldBattleHint;
        Image worldTargetFill;
        bool worldWheelOpen, worldSettlementHandled;
        float worldIdleClock;

        public bool WorldCombatWheelOpen => worldWheelOpen;
        public bool WorldCombatActive => worldBattle != null && worldBattle.HasTarget;
        public CombatActorState WorldCombatPlayer => worldPlayerState;
        public CombatActorState WorldCombatEnemy => worldBattle?.Foe;
        public string WorldCombatTargetId => worldBattleTarget?.Encounter.Id ?? "";
        public CombatSkillWheel WorldSkillWheelForCheck => worldSkillWheel;
        public RectTransform WorldRootForCheck => worldRoot;
        bool WorldCombatCanWalk => worldBattle == null || ActionCombatMotor.CanWalk(worldPlayerState);

        static Vector2 Point(WorldCombatPoint point) => new Vector2(point.X, point.Y);
        Vector2 ProjectCombatPoint(Vector2 point) => new Vector2(point.x * worldScale,
            -town.Elevation.ProjectY(point.x, point.y) * worldScale);
        float WildBodyRadius(WildActor actor) => actor.Encounter.Enemy == BossCombatBrain.EnemyId ? WorldCombatSession.BossRadiusWorld : WorldCombatSession.FoeRadiusWorld;

        bool CanTraverseWild(string enemyId, float x, float y, float nextX, float nextY)
        {
            float radius = enemyId == BossCombatBrain.EnemyId ? 65 : 20;
            if (!town.CanTraverse(x, y, nextX, nextY)) return false;
            foreach (var offset in new[] { new Vector2(radius, 0), new Vector2(-radius, 0), new Vector2(0, radius * .5f), new Vector2(0, -radius * .5f) })
                if (!town.CanTraverse(x + offset.x, y + offset.y, nextX + offset.x, nextY + offset.y)) return false;
            return true;
        }

        bool CanTraverseWorldPlayer(Vector2 start, Vector2 end)
        {
            const float radius = WorldCombatSession.PlayerRadiusWorld;
            if (!town.CanTraverse(start.x, start.y, end.x, end.y)
                || !town.CanTraverse(start.x + radius, start.y, end.x + radius, end.y)
                || !town.CanTraverse(start.x - radius, start.y, end.x - radius, end.y)
                || !town.CanTraverse(start.x, start.y + radius * .5f, end.x, end.y + radius * .5f)
                || !town.CanTraverse(start.x, start.y - radius * .5f, end.x, end.y - radius * .5f)) return false;
            foreach (var actor in wildActors.Values)
            {
                if (actor.Rect == null || actor.State != null && actor.State.IsDead) continue;
                float clearance = WildBodyRadius(actor) + radius;
                float oldDistance = Vector2.Distance(start, actor.Position);
                if (Vector2.Distance(end, actor.Position) < clearance && Vector2.Distance(end, actor.Position) < oldDistance) return false;
            }
            return true;
        }

        void EnsureWorldPlayer()
        {
            if (worldPlayerState != null && ReferenceEquals(worldPlayerOwner, activeSave)) return;
            var stats = PlayerStatsFor(activeSave.characterId);
            stats.hp += CultivationRules.HpBonus(activeSave) + ManualStudyRules.HpBonus(activeSave);
            stats.stamina += CultivationRules.StaminaBonus(activeSave) + ManualStudyRules.StaminaBonus(activeSave);
            stats.attack += ManualStudyRules.AttackBonus(activeSave);
            worldPlayerState = new CombatActorState("player", activeSave.characterName, stats.hp, stats.attack, stats.defense, stats.stamina);
            worldPlayerOwner = activeSave;
            worldBattleGear = new CombatEquipmentService(CombatGearCatalog, CombatDestinyTable.Standard, activeSave.destiny);
            worldBattleGear.RestoreLedger(activeSave);
            worldBattleGear.ApplyTo(worldPlayerState, stats.attack, stats.defense);
            if (wakeHpNext.HasValue) { CombatRules.RestoreHp(worldPlayerState, wakeHpNext.Value); wakeHpNext = null; }
        }

        void EnsureWorldActions()
        {
            EnsureWorldPlayer();
            if (worldBattle == null)
            {
                worldBattleEffects?.Clear();
                worldIdleClock = 0;
                worldSettlementHandled = false;
                worldBattle = new WorldCombatSession(worldPlayerState, null, null, activeSave,
                    new WorldCombatPoint(worldPos.x, worldPos.y), new WorldCombatPoint(worldPos.x, worldPos.y),
                    (x, y, nextX, nextY) => town.CanTraverse(x, y, nextX, nextY));
            }
            if (worldBattleEffects == null)
                worldBattleEffects = WorldCombatEffects.Create(ui, worldRoot, ProjectCombatPoint, () => settings.reduceMotion);
            if (worldBattlePose == null && travelerPuppet != null)
            {
                worldBattlePose = travelerPuppet.gameObject.AddComponent<ActionCombatPose>();
                worldBattlePose.Configure(travelerPuppet,
                    ApprovedUiArt.Load("nov10/combat-hero-" + (activeSave.gender == "female" ? "female" : "male") + "-strikes"), TravelerHeight);
            }
            if (worldBattlePose != null) worldBattlePose.enabled = true;
        }

        void AimWorldAction()
        {
            int direction = activeSave.facing;
            worldBattle.Aim(direction == 1 ? 1 : direction == 3 ? -1 : 0,
                direction == 0 ? 1 : direction == 2 ? -1 : 0);
        }

        void OpenWorldCombatWheel()
        {
            if (page != "town" || activeSave == null || modal != null || bagOverlay != null) return;
            ReleaseHudWheelInput();
            CancelMining();
            CancelTravel();
            EnsureWorldActions();
            worldWheelOpen = true;
            if (hudDaoWheel != null) hudDaoWheel.gameObject.SetActive(false);
            if (worldSkillWheel == null)
            {
                worldSkillWheel = CombatSkillWheel.Create(ui, content, ReadWorldCombatSlot, slot => ActivateWorldCombatSlot(slot),
                    () => settings.reduceMotion, PlayClick);
                worldSkillWheel.SetCloseAction(CloseWorldCombatWheel);
            }
            worldSkillWheel.gameObject.SetActive(true);
            if (worldBattleEffects == null)
                worldBattleEffects = WorldCombatEffects.Create(ui, worldRoot, ProjectCombatPoint, () => settings.reduceMotion);
            if (worldBattleHint == null)
            {
                var backing = ui.Panel(content, "World combat instruction shade", 616, 984, 830, 56, new Color(.04f, .09f, .07f, .85f));
                worldBattleHint = ui.Label(backing.transform, "", 18, 10, 794, 36, 20, UiKit.Paper, TextAlignmentOptions.Center, false);
                worldBattleHint.raycastTarget = false;
            }
            worldBattleHint.transform.parent.gameObject.SetActive(true);
            RefreshWorldCombatHud();
        }

        public void CloseWorldCombatWheel()
        {
            worldWheelOpen = false;
            if (worldSkillWheel != null) worldSkillWheel.gameObject.SetActive(false);
            if (hudDaoWheel != null) hudDaoWheel.gameObject.SetActive(true);
            if (worldBattleHint != null) worldBattleHint.transform.parent.gameObject.SetActive(false);
            if (WorldCombatActive) Notify("已收剑；远离敌人可脱战。");
            dialogueConsumeThroughFrame = Math.Max(dialogueConsumeThroughFrame, Time.frameCount + 1);
        }

        void ShowWorldCombat(string encounterId)
        {
            if (activeSave == null) { Notify("先落笔创建行旅人。"); return; }
            if (page != "town" || worldRoot == null) ShowTown();
            if (!wildActors.TryGetValue(encounterId, out var actor)) { Notify("此处已无可战的妖兽。"); return; }
            if (Vector2.Distance(worldPos, actor.Position) > 720) { OpenWorldCombatWheel(); TrackWildEncounter(encounterId); return; }
            BeginWorldCombat(actor);
        }

        void BeginWorldCombat(WildActor actor)
        {
            if (WorldCombatActive || page != "town" || modal != null || bagOverlay != null || actor.State != null && actor.State.IsDead) return;
            EnsureWorldActions();
            var definition = CombatEnemies.Find(actor.Encounter.Enemy);
            if (definition == null) { Notify("这头妖兽的资料尚未齐备。"); return; }
            var profile = LoadProfileFor(definition.Id);
            if (actor.State == null)
            {
                actor.State = new CombatActorState(definition.Id, definition.Name, definition.MaxHp, definition.Attack, definition.Defense, definition.MaxStamina);
                actor.State.Tuning = TuningForFoe(profile);
            }
            CancelMining(); CancelTravel();
            worldBattleTarget = actor;
            worldSettlementHandled = false;
            worldBattle.SetPlayerPosition(worldPos.x, worldPos.y);
            worldBattle.SetTarget(actor.State, profile, new WorldCombatPoint(actor.Position.x, actor.Position.y));
            OpenWorldCombatWheel();
            BuildWorldTargetHud();
            Notify(definition.Id == BossCombatBrain.EnemyId ? "玄岩山君震怒——避开地上的落石印！" : "已锁定 " + definition.Name);
        }

        void BuildWorldTargetHud()
        {
            if (worldTargetHud != null) Destroy(worldTargetHud.gameObject);
            bool boss = worldBattleTarget.Encounter.Enemy == BossCombatBrain.EnemyId;
            worldTargetHud = ui.Rect(content, "World combat target", 730, 28, 650, 108);
            ui.Panel(worldTargetHud, "Target ink", 0, 0, 650, 108, new Color(.025f, .045f, .04f, .88f));
            worldTargetName = ui.Label(worldTargetHud, worldBattle.Foe.Name, 20, 8, 610, 40, boss ? 29 : 25,
                UiKit.Gold, TextAlignmentOptions.Center, false);
            ui.Panel(worldTargetHud, "Target health bed", 26, 54, 598, 10, new Color(.18f, .12f, .09f, 1));
            worldTargetFill = ui.Panel(worldTargetHud, "Target real health", 26, 54, 598, 10, UiKit.Hex("A74332"));
            worldTargetHealth = ui.Label(worldTargetHud, "", 20, 70, 610, 28, 18, UiKit.Paper, TextAlignmentOptions.Center, false);
            worldTargetName.raycastTarget = worldTargetHealth.raycastTarget = false;
        }

        WildActor NearestWorldTarget(string except = "")
        {
            WildActor nearest = null; float best = 650;
            foreach (var actor in wildActors.Values)
            {
                if (actor.Encounter.Id == except || actor.Rect == null || activeSave.HasFlag(actor.Encounter.Id) || actor.State != null && actor.State.IsDead) continue;
                float distance = Vector2.Distance(worldPos, actor.Position);
                if (distance < best && town.CanTraverse(worldPos.x, worldPos.y, actor.Position.x, actor.Position.y)) { nearest = actor; best = distance; }
            }
            return nearest;
        }

        CombatSkillSlotState ReadWorldCombatSlot(int slot)
        {
            bool ready = worldWheelOpen && page == "town" && modal == null && bagOverlay == null && !WorldDialogueInputBlocked;
            var state = new CombatSkillSlotState { Learned = true, Available = ready, Status = "" };
            if (slot >= 4 && slot <= 6)
            {
                string id = CombatSkillWheel.Ids[slot]; var definition = PlayerSkillCombat.Find(id);
                state.Learned = ManualStudyRules.Learned(activeSave, id);
                state.Cooldown = worldBattle?.Cooldown(id) ?? 0; state.Duration = definition.Cooldown;
                state.Available &= state.Learned && state.Cooldown <= 0 && worldBattle != null &&
                    worldPlayerState.Phase == CombatPhase.Idle && worldPlayerState.Stamina >= definition.Move.StaminaCost;
                if (id == "clarity") state.Available &= worldPlayerState != null && worldPlayerState.Hp < worldPlayerState.MaxHp;
                state.Status = !state.Learned ? "未习得" : state.Cooldown > 0 ? state.Cooldown.ToString("0.0") + "秒" : definition.Move.StaminaCost + "精";
            }
            else if (slot == 3)
            {
                string pill = FirstUsablePill(); int count = string.IsNullOrEmpty(pill) ? 0 : InventoryRules.Count(activeSave.bag, pill);
                state.Available &= count > 0 && worldPlayerState != null && worldPlayerState.Hp < worldPlayerState.MaxHp;
                state.Status = count > 0 ? count + "枚" : "无丹药";
            }
            else if (slot == 7) state.Status = "切换目标";
            else state.Available &= worldPlayerState != null && !worldPlayerState.IsDead;
            return state;
        }

        public CombatResult WorldCombatActForCheck(int slot) => ActivateWorldCombatSlot(slot);
        CombatResult ActivateWorldCombatSlot(int slot)
        {
            if (page != "town" || modal != null || bagOverlay != null || WorldDialogueInputBlocked)
                return new CombatResult { Reason = "当前不能出手。" };
            EnsureWorldActions();
            if (slot == 7)
            {
                if (WorldCombatActive) { Notify("当前锁定 " + worldBattle.Foe.Name + "，先脱战再换目标。"); return new CombatResult(); }
                var next = NearestWorldTarget();
                if (next != null) BeginWorldCombat(next); else Notify("附近没有可锁定的妖兽。");
                return new CombatResult();
            }
            if (slot == 3)
            {
                var result = CombatConsumables.Consume(activeSave, Table, CombatItemsJson, FirstUsablePill(), worldPlayerState, worldBattle?.Now ?? worldIdleClock);
                if (result.Executed) { PersistBag(); worldBattleEffects?.Play("heal", worldPos, worldPos, worldBattle?.Now ?? worldIdleClock, false); }
                else Notify(result.Reason);
                return new CombatResult { Executed = result.Executed, Reason = result.Reason };
            }
            worldBattle.SetPlayerPosition(worldPos.x, worldPos.y);
            AimWorldAction();
            var action = worldBattle.Act(slot);
            if (action.Executed) { CancelTravel(); travelerSpeedNow = 0; worldSkillWheel?.MarkUsed(slot); }
            else Notify(action.Reason);
            DrainWorldCombatEvents();
            return action;
        }

        void TickWorldCombat(float dt)
        {
            worldIdleClock += dt;
            if (worldBattle == null)
            {
                worldBattleEffects?.Sample(worldIdleClock);
                if (worldWheelOpen && worldSkillWheel != null) worldSkillWheel.Sample(worldIdleClock, dt);
                if (worldWheelOpen) ReadWorldCombatKeys();
                return;
            }
            if (worldWheelOpen || WorldCombatActive) ReadWorldCombatKeys();
            var oldFoe = worldBattleTarget?.Position ?? worldPos;
            worldBattle.SetPlayerPosition(worldPos.x, worldPos.y);
            worldBattle.Tick(dt);
            var playerPoint = Point(worldBattle.PlayerPoint);
            if (CanTraverseWorldPlayer(worldPos, playerPoint)) worldPos = playerPoint;
            if (WorldCombatActive && worldBattleTarget != null)
            {
                var foePoint = Point(worldBattle.FoePoint);
                worldBattleTarget.Position = foePoint;
                float projected = town.Elevation.ProjectY(foePoint.x, foePoint.y);
                worldBattleTarget.Rect.anchoredPosition = new Vector2(foePoint.x * worldScale,
                    -(projected - worldBattleTarget.Height * worldBattleTarget.FootRatio) * worldScale);
                worldBattleTarget.Place.BaseY = projected;
                worldBattleTarget.Pose.Step(Vector2.Distance(oldFoe, foePoint), worldPos.x < foePoint.x ? -1 : 1);
                worldBattleTarget.Pose.Combat(worldBattle.Foe.Phase,
                    CombatRules.ActiveProgress(worldBattle.Foe, worldBattle.Now), settings.reduceMotion);
                worldBattleEffects?.Telegraph(Point(worldBattle.TelegraphPoint), worldBattle.TelegraphRadius,
                    worldBattle.TelegraphProgress, worldBattle.Foe.Phase == CombatPhase.Startup, worldBattle.Foe.Id == BossCombatBrain.EnemyId);
            }
            DrainWorldCombatEvents();
            worldBattleEffects?.Sample(worldBattle.Now);
            worldSkillWheel?.Sample(worldBattle.Now, dt);
            RefreshWorldCombatHud();
            if (worldBattle.Settled && !worldSettlementHandled) SettleWorldCombat();
            else if (WorldCombatActive && worldBattleTarget != null)
            {
                if (!worldBattle.Settled && (Vector2.Distance(worldPos, worldBattleTarget.Position) > 700 ||
                    Vector2.Distance(worldPos, new Vector2(worldBattleTarget.Encounter.X, worldBattleTarget.Encounter.Y)) > worldBattleTarget.Spec.Leash + 400))
                    DisengageWorldCombat();
            }
        }

        void ReadWorldCombatKeys()
        {
            var keyboard = Keyboard.current; if (keyboard == null) return;
            if (keyboard.jKey.wasPressedThisFrame || keyboard.digit1Key.wasPressedThisFrame) ActivateWorldCombatSlot(0);
            else if (keyboard.kKey.wasPressedThisFrame || keyboard.digit2Key.wasPressedThisFrame) ActivateWorldCombatSlot(1);
            else if (keyboard.spaceKey.wasPressedThisFrame || keyboard.lKey.wasPressedThisFrame) ActivateWorldCombatSlot(2);
            else if (keyboard.qKey.wasPressedThisFrame || keyboard.digit3Key.wasPressedThisFrame) ActivateWorldCombatSlot(3);
            else if (keyboard.uKey.wasPressedThisFrame || keyboard.digit4Key.wasPressedThisFrame) ActivateWorldCombatSlot(4);
            else if (keyboard.iKey.wasPressedThisFrame || keyboard.digit5Key.wasPressedThisFrame) ActivateWorldCombatSlot(5);
            else if (keyboard.oKey.wasPressedThisFrame || keyboard.digit6Key.wasPressedThisFrame) ActivateWorldCombatSlot(6);
            else if (keyboard.tabKey.wasPressedThisFrame) ActivateWorldCombatSlot(7);
        }

        void DrainWorldCombatEvents()
        {
            if (worldBattle == null) return;
            while (worldBattle.TryDequeueEvent(out var effect))
            {
                worldBattleEffects?.Play(effect.Id, Point(effect.Origin), Point(effect.Target), worldBattle.Now, worldBattle.Foe?.Id == BossCombatBrain.EnemyId);
                if (effect.Id == "hit:foe") worldBattleGear?.WearWeapon(worldPlayerState.CurrentAction);
                if (effect.Id == "hit:player") worldBattleGear?.WearArmor();
                if ((effect.Id == "hit:foe" || effect.Id == "hit:player") && worldBattleGear != null)
                {
                    var stats = PlayerStatsFor(activeSave.characterId);
                    worldBattleGear.ApplyTo(worldPlayerState, stats.attack + ManualStudyRules.AttackBonus(activeSave), stats.defense);
                }
            }
        }

        void SampleWorldCombatPose()
        {
            if (worldBattle == null || worldBattlePose == null) return;
            worldBattlePose.Sample(ProjectCombatPoint(worldPos), worldBattle.Facing.X < 0 ? -1 : 1,
                worldPlayerState.Phase, worldPlayerState.CurrentAction, CombatRules.ActiveProgress(worldPlayerState, worldBattle.Now), settings.reduceMotion,
                CombatRules.ActionElapsed(worldPlayerState, worldBattle.Now), CombatRules.TimingOf(worldPlayerState).active);
        }

        void RefreshWorldCombatHud()
        {
            if (worldPlayerState != null)
            {
                if (worldHealthCaption != null) worldHealthCaption.text = "气血  " + worldPlayerState.Hp + " / " + worldPlayerState.MaxHp;
                if (worldStaminaCaption != null) worldStaminaCaption.text = "精力  " + Mathf.CeilToInt(worldPlayerState.Stamina) + " / " + worldPlayerState.MaxStamina;
            }
            if (worldBattleHint != null) worldBattleHint.text = !WorldCombatActive ? "面向前方出招 · J 轻击 K 重击 · U 御剑 I 雷法 O 清心" : "WASD 走位 · J 轻击  K 重击 · 空格闪身 · 避开地面预警";
            if (!WorldCombatActive || worldTargetFill == null) return;
            worldTargetFill.rectTransform.sizeDelta = new Vector2(598f * worldBattle.Foe.Hp / worldBattle.Foe.MaxHp, 10);
            worldTargetHealth.text = worldBattle.Foe.Hp + " / " + worldBattle.Foe.MaxHp +
                (worldBattle.Foe.Phase == CombatPhase.Startup ? "   蓄势 · 留意地面" : "");
        }

        void RecordWorldGear()
        {
            if (activeSave == null || worldBattleGear == null) return;
            RecordGearSlot(GearSlots.Weapon, worldBattleGear.Weapon);
            RecordGearSlot(GearSlots.Armor, worldBattleGear.Armor);
            RecordGearSlot(GearSlots.Accessory, worldBattleGear.Accessory);
        }

        void SettleWorldCombat()
        {
            worldSettlementHandled = true;
            RecordWorldGear();
            bool defeated = worldPlayerState.IsDead || worldBattle.Foe == null;
            if (!defeated)
            {
                string encounterId = worldBattleTarget.Encounter.Id;
                bool first = activeSave.MarkFlag(encounterId);
                if (chapterTable != null)
                    foreach (var segment in chapterTable.Segments.Where(segment => segment.EncounterId == encounterId))
                        ChapterLedger.Enter(activeSave, chapterTable, segment.Segment, activeSave.WorldTimeNow(), out _);
                var reward = first ? Core.CombatSpoils.Grant(activeSave, CombatEnemies.Find(worldBattle.Foe.Id), Table) : new SpoilsResult();
                PersistBag();
                Notify(worldBattle.Foe.Name + "已败 · " + reward.Describe(Table));
                worldBattleTarget.Rect.gameObject.SetActive(false);
                wildActors.Remove(encounterId); encounterMarkers.Remove(encounterId);
            }
            else
            {
                LastDefeatSettlement = CombatDefeatProtection.Apply(activeSave, worldPlayerState.MaxHp);
                CombatRules.RestoreHp(worldPlayerState, LastDefeatSettlement.AwakeHp);
                PersistBag();
                var home = new Vector2(town.SpawnX, town.SpawnY);
                if (CanStand(home)) worldPos = home;
                Notify("你在镇口醒来。" + LastDefeatSettlement.ElderLine);
            }
            float effectClock = worldBattle.Now;
            DisengageWorldCombat(false, false);
            if (defeated) { worldBattle = null; worldIdleClock = effectClock; }
            else worldSettlementHandled = false;
            CloseWorldCombatWheel();
        }

        void DisengageWorldCombat(bool notify = true, bool clearEffects = true)
        {
            if (!WorldCombatActive) return;
            RecordWorldGear(); PersistBag();
            if (worldBattleTarget != null && !worldBattle.Foe.IsDead) worldBattleTarget.Motion.ResumeAt(worldBattleTarget.Position.x, worldBattleTarget.Position.y);
            worldBattleTarget?.Pose.Combat(CombatPhase.Idle, 0, settings.reduceMotion);
            worldBattle.ClearTarget(); worldBattleTarget = null;
            if (worldTargetHud != null) Destroy(worldTargetHud.gameObject);
            worldTargetHud = null;
            worldBattleEffects?.Telegraph(Vector2.zero, 0, 0, false, false);
            if (clearEffects) worldBattleEffects?.Clear();
            wildReentryGrace = 3;
            if (notify) Notify("已脱离战斗。");
        }

        void ReleaseWorldCombat()
        {
            if (WorldCombatActive) DisengageWorldCombat(false);
            worldBattle?.Cancel(); worldBattle = null; worldIdleClock = 0;
            if (worldBattlePose != null) worldBattlePose.enabled = false;
            worldWheelOpen = false;
            worldBattleEffects?.Clear();
            worldBattleEffects = null; worldSkillWheel = null; worldBattlePose = null;
            worldTargetHud = null; worldBattleHint = null;
        }

        public void AdvanceWorldCombatForCheck(float seconds)
        {
            if (seconds <= 0 || float.IsNaN(seconds) || float.IsInfinity(seconds) || page != "town" || modal != null || bagOverlay != null) return;
            float remaining = Mathf.Min(seconds, 30);
            while (remaining > 0 && worldBattle != null)
            {
                float step = Mathf.Min(remaining, .02f);
                TickWorldCombat(step);
                remaining -= step;
            }
            SyncTraveler(); SampleWorldCombatPose(); UpdateDepthOrder();
        }
    }
}
