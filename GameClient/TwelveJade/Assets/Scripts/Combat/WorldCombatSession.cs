using System;
using System.Collections.Generic;
using TwelveJade.Core;

namespace TwelveJade.Combat
{
    // 地图原地的 2D 战斗核心：不另开战斗场景，位置就是地图世界坐标。
    //
    // 分工照旧：生命/精力/相位/伤害只由 Core 的 CombatRules 写，这里负责“世界里的哪两点、
    // 隔多远、中间有没有墙”。Core 的射程与击退都写在 X 上（1D），因此每次结算都把两个单位
    // 的 X 临时换成本帧的 2D 距离，结算完原样还原——X/Y 只是搬运工，逻辑一律走世界点。
    // 旧地图的 1D 手感靠同一条换算保住：100 世界单位 = 1 战斗单位。
    //
    // 不用随机数；同一串输入与 dt 必然复现同一场战斗（dt 一律切成 <= 20ms 的小步，
    // 大步与多小步结果一致，掉帧不会让人穿墙或回精翻倍）。
    public struct WorldCombatPoint
    {
        public float X, Y;
        public WorldCombatPoint(float x, float y) { X = x; Y = y; }
    }

    public struct WorldCombatEvent
    {
        public string Id;
        public WorldCombatPoint Origin, Target;
    }

    public sealed class WorldCombatSession
    {
        public const float WorldUnitsPerCombatUnit = 100f;
        // 体半径按种类分开：人窄、山君宽。两者相加才是贴身的最小间距。
        public const float PlayerRadiusWorld = 18f;
        public const float FoeRadiusWorld = 30f;
        public const float BossRadiusWorld = 80f;
        public const float DodgeDistanceWorld = ActionCombatMotor.DodgeDistance * WorldUnitsPerCombatUnit;
        const float StepWorld = 12f;
        const int EventCapacity = 32;
        const float MaxTickStep = .02f;

        readonly SaveData save;
        EnemyProfile profile;
        readonly Func<float, float, float, float, bool> canTraverse;
        readonly PlayerSkillCombat skills = new PlayerSkillCombat();
        EnemyAiState foeAi;
        readonly Queue<WorldCombatEvent> events = new Queue<WorldCombatEvent>();

        WorldCombatPoint playerPoint, foePoint, dodgeFrom, dodgeTo;
        float dodgeStartsAt, dodgeUntil;
        bool dodgeMoving;
        float foeNextActAt = 1.2f;
        // 玩家招式期间两个单位被换进战斗单位的假坐标（见 Resolve2D 的说明）。
        bool skillPosed;
        float savedPlayerX, savedFoeX;
        string skillLockId = "";
        WorldCombatPoint skillOrigin, skillAim;

        bool boss;
        int bossStage = 1, bossPattern;
        string bossSkillId = "";
        WorldCombatPoint bossOrigin, bossLock, bossPrev;
        float bossDir, bossReach, bossNextAt = 1.2f;
        bool bossAttempted;

        public WorldCombatSession(CombatActorState player, CombatActorState foe, EnemyProfile profile,
            SaveData save, WorldCombatPoint playerPoint, WorldCombatPoint foePoint,
            Func<float, float, float, float, bool> canTraverse)
        {
            Player = player ?? throw new ArgumentNullException(nameof(player));
            Foe = foe;
            this.profile = profile;
            this.save = save;
            this.canTraverse = canTraverse;
            boss = profile != null && profile.Id == BossCombatBrain.EnemyId;
            playerPoint = Finite(playerPoint);
            foePoint = foe != null ? Apart(Finite(foePoint), playerPoint, Separation, FoeRadius) : playerPoint;
            this.playerPoint = playerPoint;
            this.foePoint = foePoint;
            foeAi = EnemyAi.NewState(foeNextActAt);
            if (profile?.Ai != null) foeNextActAt = Math.Max(.2f, profile.Ai.AggressionSeconds);
            // 传入的 Hp 就是当前气血：这里不碰生命，只摆位置。
            if (Player.IsDead || HasTarget && Foe.IsDead) Settle();
        }

        // 丹药表：构造签名里不带，接入了才有药可吃；退出时清空即可。
        public ItemTable Items { get; set; }
        public string ItemsJson { get; set; }

        public CombatActorState Player { get; }
        public CombatActorState Foe { get; private set; }
        public bool HasTarget => Foe != null;
        WorldCombatPoint facing = new WorldCombatPoint(1, 0);
        public WorldCombatPoint Facing => facing;
        public void Aim(float x, float y)
        {
            if (!Finite(x) || !Finite(y) || x*x+y*y < .0001f || Player.Phase != CombatPhase.Idle) return;
            facing = Unit(new WorldCombatPoint(0,0), new WorldCombatPoint(x,y));
        }
        public void SetTarget(CombatActorState foe, EnemyProfile definition, WorldCombatPoint point)
        {
            if (foe == null || foe.IsDead || Player.IsDead || ReferenceEquals(Foe, foe)) return;
            RestorePose();
            Foe = foe; profile = definition;
            boss = definition?.Id == BossCombatBrain.EnemyId;
            foePoint = Apart(Finite(point), playerPoint, Separation, FoeRadius);
            foeAi = EnemyAi.NewState(Now + 1.2f);
            foeNextActAt = Now + Math.Max(.2f, profile?.Ai?.AggressionSeconds ?? 1.2f);
            bossNextAt = Now + 1.2f; bossStage = 1; bossPattern = 0; Settled = false;
        }
        public void ClearTarget()
        {
            Cancel(); Foe = null; profile = null; boss = false; Settled = Player.IsDead;
            foePoint = playerPoint;
            // Already emitted hit/death events remain available until consumed.
            // Keep the same skill clock and cooldown ledger while sheathing or changing targets.
        }
        public WorldCombatPoint PlayerPoint { get { return playerPoint; } }
        public WorldCombatPoint FoePoint { get { return foePoint; } }
        public float Now { get; private set; }
        public bool Settled { get; private set; }
        public string LastMessage { get; private set; } = "";
        public float TelegraphProgress { get; private set; }
        public float TelegraphRadius { get; private set; }
        public WorldCombatPoint TelegraphPoint { get; private set; }
        public string ActiveSkill { get; private set; } = "";
        public int EventSequence { get; private set; }
        public string LastEvent { get; private set; } = "";
        public WorldCombatPoint EventPoint { get; private set; }

        public float PlayerRadius { get { return PlayerRadiusWorld; } }
        public float FoeRadius { get { return boss ? BossRadiusWorld : FoeRadiusWorld; } }
        public float Separation { get { return PlayerRadius + FoeRadius; } }

        public bool TryDequeueEvent(out WorldCombatEvent evt)
        {
            if (events.Count == 0) { evt = default(WorldCombatEvent); return false; }
            evt = events.Dequeue();
            return true;
        }

        // 控制器走路：先整步，撞了再单轴滑，最后按两个人自己的半径推开。返回实际落点。
        public WorldCombatPoint SetPlayerPosition(float x, float y)
        {
            if (!Finite(x) || !Finite(y)) return PlayerPoint;
            if (Settled) return PlayerPoint;
            var from = playerPoint;
            var target = Finite(new WorldCombatPoint(x, y));
            if (BodyTraverse(from, target, PlayerRadius)) playerPoint = target;
            else
            {
                var slideX = new WorldCombatPoint(target.X, from.Y);
                var slideY = new WorldCombatPoint(from.X, target.Y);
                if (BodyTraverse(from, slideX, PlayerRadius)) playerPoint = slideX;
                else if (BodyTraverse(from, slideY, PlayerRadius)) playerPoint = slideY;
            }
            if (HasTarget) playerPoint = Apart(playerPoint, foePoint, Separation, PlayerRadius);
            return PlayerPoint;
        }

        public float Cooldown(string id) { return string.IsNullOrEmpty(id) ? 0f : skills.CooldownLeft(id, Now); }

        public bool Learned(string id) { return skills.Learned(save, id); }

        // 逃离/中断：只掐招式，Settled 与奖励都不动。
        // 相位只通过 Core 收招，不推进时钟，不补血，不提前结算流血。
        public void Cancel()
        {
            skills.Cancel();
            RestorePose();
            bossSkillId = "";
            TelegraphProgress = 0f;
            TelegraphRadius = 0f;
            ActiveSkill = "";
            skillLockId = "";
            dodgeMoving = false;
            CombatRules.CancelAction(Player);
            CombatRules.CancelAction(Foe);
        }

        public CombatResult Act(int slot)
        {
            if (Settled) return Refuse("这一战已经结束了。");
            switch (slot)
            {
                case 0: return ActMelee(CombatActionType.Light, "cast:light");
                case 1: return ActMelee(CombatActionType.Heavy, "cast:heavy");
                case 2: return ActDodge();
                case 3: return ActPill();
                case 4: return ActSkill("sword");
                case 5: return ActSkill("thunder");
                case 6: return ActSkill("clarity");
                case 7: return ActTarget();
                default: return Refuse("没有这个招式位。");
            }
        }

        // 大步切成小步：Now、回精、位移用同一个步长，掉帧不会多回精或穿墙。
        public void Tick(float dt)
        {
            if (Settled) return;
            if (!Finite(dt) || dt <= 0f) return;
            var remaining = dt;
            while (remaining > 0f && !Settled)
            {
                var step = Math.Min(remaining, MaxTickStep);
                remaining -= step;
                Now += step;
                TickOnce(step);
            }
        }

        void TickOnce(float step)
        {
            CombatRules.Tick(Player, Now);
            if (HasTarget) CombatRules.Tick(Foe, Now);
            CombatRules.Regenerate(Player, step);
            if (HasTarget) CombatRules.Regenerate(Foe, step);
            if (boss) ObserveBoss();

            ResolvePlayerMelee();
            ResolveDodge();
            ResolveSkill();
            if (HasTarget)
            {
                ResolveBossCast(); ResolveFoeStrike(); StepFoe(step);
            }
            if (!skillPosed) RestorePose();
            if (Player.IsDead || HasTarget && Foe.IsDead) Settle();
        }

        // ---------------- 玩家侧 ----------------

        CombatResult ActMelee(CombatActionType action, string eventId)
        {
            var result = CombatRules.TryAct(Player, action, Now, Foe);
            if (!result.Executed) return Refuse(result.Reason, result);
            if (HasTarget) facing = Unit(playerPoint, foePoint);
            Emit(eventId, playerPoint, Advance(playerPoint, facing,
                (Player.CurrentMove?.Range ?? CombatRules.StrikeRange) * WorldUnitsPerCombatUnit));
            return result;
        }

        // 剑/雷/清心的招式也是 Light/Heavy 相位，近战那一路必须让位，否则同一下打两次。
        void ResolvePlayerMelee()
        {
            if (!HasTarget) return;
            var action = Player.CurrentAction;
            if (Player.Phase != CombatPhase.Active || (action != CombatActionType.Light && action != CombatActionType.Heavy))
                return;
            if (Player.StruckThisAction || skills.OwnsAction(Player)) return;
            var move = Player.CurrentMove;
            if (move != null && move.Id.StartsWith("learned-", StringComparison.Ordinal)) return;
            var distance = Traverse(playerPoint, foePoint)
                ? Distance(playerPoint, foePoint) / WorldUnitsPerCombatUnit
                : float.PositiveInfinity;
            var result = Resolve2D(Player, Foe, distance, null);
            Emit(result.Hit ? "hit:foe" : "", playerPoint, foePoint);
        }

        CombatResult ActDodge()
        {
            var result = CombatRules.TryAct(Player, CombatActionType.Dodge, Now, Foe);
            if (!result.Executed) return Refuse(result.Reason, result);
            var away = HasTarget ? Unit(FoePoint, PlayerPoint) : facing;
            var to = Advance(PlayerPoint, away, DodgeDistanceWorld);
            dodgeFrom = PlayerPoint;
            dodgeTo = Advance(PlayerPoint, away, ReachAlong(PlayerPoint, to, DodgeDistanceWorld, PlayerRadius));
            var timing = CombatRules.TimingOf(Player);
            dodgeStartsAt = Now + timing.startup;
            dodgeUntil = dodgeStartsAt + timing.active;
            dodgeMoving = true;
            Emit("dodge", PlayerPoint, dodgeTo);
            return result;
        }

        CombatResult ActPill()
        {
            if (Items == null) return Refuse("药表还没接上。");
            var usable = CombatConsumables.UsableIn(save, Items, ItemsJson);
            if (usable.Count == 0) return Refuse("行囊里没有可用的丹药。");
            var before = Player.Hp;
            var consumed = CombatConsumables.Consume(save, Items, ItemsJson, usable[0], Player, Now);
            if (!consumed.Executed) return Refuse(consumed.Reason);
            Emit("heal", PlayerPoint, PlayerPoint);
            if (Player.Hp != before) LastMessage = "回了一口气（" + (Player.Hp - before) + "）。";
            return new CombatResult { Executed = true, DefenderHp = Player.Hp };
        }

        CombatResult ActTarget()
        {
            if (profile?.Parts == null || profile.Parts.Count == 0) return Refuse("这妖没有可锁的部位。");
            var index = 0;
            for (var i = 0; i < profile.Parts.Count; i++)
                if (profile.Parts[i].Id == Player.TargetPartId) index = i + 1;
            var part = profile.Parts[index % profile.Parts.Count];
            Player.TargetPartId = part.Id;
            LastMessage = "锁定 " + part.Name + "。";
            return new CombatResult { Executed = true, AttackerStamina = Player.Stamina, Reason = part.Id };
        }

        CombatResult ActSkill(string id)
        {
            var def = PlayerSkillCombat.Find(id);
            if (def == null) return Refuse("未登记此术。");
            var refusal = skills.Refusal(id, save, Player, Foe, Now, true);
            if (refusal.Length > 0) return Refuse(refusal);
            var toward = HasTarget ? Unit(PlayerPoint, FoePoint) : facing;
            facing = toward;
            var reach = def.Move.Range * WorldUnitsPerCombatUnit;
            var gap = id == "sword" || !HasTarget ? reach : Math.Min(Distance(PlayerPoint, FoePoint), reach);
            var result = skills.Request(id, save, Player, Foe, Now, toward.X < 0 ? -1 : 1, true);
            if (!result.Executed) return Refuse(result.Reason, result);
            skillLockId = id;
            skillOrigin = PlayerPoint;
            // 准星钉在出手那一刻的敌人位置（不是 x+），撞墙就落在墙面上；之后人可以走开。
            skillAim = Advance(PlayerPoint, toward, ReachAlong(PlayerPoint, Advance(PlayerPoint, toward, gap), gap));
            SkillPose();
            ActiveSkill = id;
            Emit("cast:" + id, PlayerPoint, skillAim);
            return result;
        }

        void ResolveSkill()
        {
            if (!skills.Pending || skills.Current == null) { if (skillLockId.Length > 0) FinishSkill(); return; }
            if (Player.IsDead || Player.Phase == CombatPhase.Hitstun) { FinishSkill(); return; }
            if (Player.Phase == CombatPhase.Recovery || Player.Phase == CombatPhase.Idle) { FinishSkill(); return; }

            if (skills.Current.Id == "clarity")
            {
                if (Player.Phase != CombatPhase.Active) return;
                var healed = skills.Resolve(Player, Foe, Now);
                if (healed == null) return;
                Emit("heal", PlayerPoint, PlayerPoint);
                LastMessage = "清心归元，回了 " + healed.Healed + " 点气血。";
                FinishSkill();
                return;
            }
            if (!HasTarget || Player.Phase != CombatPhase.Active || Player.StruckThisAction) return;

            if (skills.Current.Id == "thunder")
            {
                var visible = Traverse(PlayerPoint, FoePoint);
                var locked = Distance(FoePoint, skillAim) <= PlayerSkillCombat.ThunderRadius * WorldUnitsPerCombatUnit;
                var hit = Resolve2D(Player, Foe, locked && visible ? 0f : float.PositiveInfinity, null);
                if (!hit.Hit) LastMessage = !visible ? "雷印被挡住了。" : locked ? "雷印没落准。" : "雷印被躲开了。";
                Emit(hit.Hit ? "hit:foe" : "", PlayerPoint, FoePoint);
                FinishSkill();
                return;
            }
            // 剑气：本帧扫过的线段，墙先截断。
            var reach = ReachAlong(skillOrigin, skillAim, Distance(skillOrigin, skillAim));
            var swept = Advance(skillOrigin, Unit(skillOrigin, skillAim), reach * skills.Progress(Now));
            var halfWidth = PlayerSkillCombat.SwordRadius * WorldUnitsPerCombatUnit + FoeRadius * .5f;
            if (DistanceToSegment(FoePoint, skillOrigin, swept) > halfWidth) return;
            var struck = Resolve2D(Player, Foe, 0f, null);
            Emit(struck.Hit ? "hit:foe" : "", PlayerPoint, FoePoint);
        }

        void FinishSkill()
        {
            skillLockId = "";
            ActiveSkill = "";
            skills.Cancel();
            RestorePose();
        }

        void ResolveDodge()
        {
            if (!dodgeMoving) return;
            if (Player.IsDead || Player.CurrentAction != CombatActionType.Dodge)
            { dodgeMoving = false; return; }
            if (Now < dodgeStartsAt) return;
            // Tick may have crossed into recovery: still consume the final part of the movement.
            float progress = Math.Min(1f, (Now - dodgeStartsAt) / Math.Max(.001f, dodgeUntil - dodgeStartsAt));
            var step = Advance(dodgeFrom, Unit(dodgeFrom, dodgeTo), Distance(dodgeFrom, dodgeTo) * (progress * progress * (3f - 2f * progress)));
            if (BodyTraverse(PlayerPoint, step, PlayerRadius)) playerPoint = step;
            else dodgeMoving = false;
            if (progress >= 1f) dodgeMoving = false;
        }

        // 结算时把 2D 距离临时写成 X：distanceCombat 为 0 表示“贴上了”，必过 Core 的射程检查。
        CombatResult Resolve2D(CombatActorState attacker, CombatActorState defender, float distanceCombat, PartStrike part)
        {
            var ax = attacker.X; var dx = defender.X;
            attacker.X = 0f; defender.X = distanceCombat;
            CombatResult result;
            try { result = CombatRules.ResolvePartStrike(attacker, defender, Now, part ?? PlayerPartStrike()); }
            finally { attacker.X = ax; defender.X = dx; }
            if (!string.IsNullOrEmpty(result.Reason)) LastMessage = result.Reason;
            return result;
        }

        PartStrike PlayerPartStrike()
        {
            var part = profile?.FindPart(Player.TargetPartId);
            if (part == null) return new PartStrike();
            return new PartStrike
            {
                PartId = part.Id, Multiplier = part.Multiplier, DodgeChance = part.DodgeChance,
                WeakPoint = part.WeakPoint, Bulwark = part.Bulwark,
            };
        }

        // 招式期间把 X 存档占位，真正的坐标在 PlayerPoint/FoePoint 里。
        void SkillPose()
        {
            if (skillPosed) return;
            savedPlayerX = Player.X; savedFoeX = Foe?.X ?? 0;
            skillPosed = true;
        }

        void RestorePose()
        {
            if (!skillPosed) return;
            Player.X = savedPlayerX; if (Foe != null) Foe.X = savedFoeX;
            skillPosed = false;
        }

        // ---------------- 妖兽侧 ----------------

        void ObserveBoss()
        {
            if (bossStage < 2 && Foe.Hp <= Foe.MaxHp / 2) bossStage = 2;
        }

        void StepFoe(float step)
        {
            if (Foe.IsDead || Player.IsDead) return;
            var distance = Distance(playerPoint, foePoint);
            if (Foe.Phase == CombatPhase.Idle && Now >= foeNextActAt)
            {
                var reachable = distance / WorldUnitsPerCombatUnit;
                var move = boss ? ChooseBossMove(reachable) : ChooseFoeMove(reachable);
                if (move != null) { BeginFoeCast(move); return; }
            }
            if (Foe.Phase != CombatPhase.Idle) return;
            if (distance < Separation) { PushFoeApart(); return; }
            var preferred = (profile?.Ai?.PreferredRange ?? 1.2f) * WorldUnitsPerCombatUnit;
            var stop = Math.Max(preferred, Separation);
            if (distance <= stop) return;
            var direction = Unit(foePoint, playerPoint);
            var travel = Math.Min(distance - stop, (profile?.MoveSpeed ?? 40f) * WorldUnitsPerCombatUnit / 75f * step);
            var want = Advance(foePoint, direction, travel);
            if (BodyTraverse(foePoint, want, FoeRadius)) { foePoint = want; return; }
            // 正前方是岩壁：沿墙面横向让一步，别原地抖。
            var side = Advance(foePoint, Perpendicular(direction), travel);
            if (BodyTraverse(foePoint, side, FoeRadius)) { foePoint = side; }
        }

        void PushFoeApart()
        {
            var gap = Distance(foePoint, playerPoint);
            if (gap >= Separation) return;
            var away = gap <= .0001f ? new WorldCombatPoint(-1f, 0f) : Unit(playerPoint, foePoint);
            var pushed = Advance(playerPoint, away, Separation);
            if (BodyTraverse(foePoint, pushed, FoeRadius)) { foePoint = pushed; }
        }

        CombatMove ChooseFoeMove(float distanceCombat)
        {
            if (profile?.Ai == null) return null;
            var px = Player.X; var fx = Foe.X;
            Player.X = distanceCombat; Foe.X = 0f;
            CombatMove move;
            try { move = EnemyAi.Choose(profile, foeAi, Foe, Player, Now); }
            finally { Player.X = px; Foe.X = fx; }
            if (move == null || move.Range < distanceCombat) return null;
            return move;
        }

        CombatMove ChooseBossMove(float distanceCombat)
        {
            if (Foe.Phase != CombatPhase.Idle || Now < bossNextAt) return null;
            string[] pattern = bossStage == 2
                ? new[] { BossCombatBrain.FissureId, BossCombatBrain.StoneId, BossCombatBrain.SweepId, BossCombatBrain.StoneId }
                : new[] { BossCombatBrain.SweepId, BossCombatBrain.FissureId, BossCombatBrain.StoneId };
            for (var i = 0; i < pattern.Length; i++)
            {
                var move = profile.FindMove(pattern[(bossPattern + i) % pattern.Length]);
                if (move == null || Foe.Stamina < move.StaminaCost || move.Range < distanceCombat) continue;
                bossPattern += i + 1;
                return move;
            }
            return null;
        }

        void BeginFoeCast(CombatMove move)
        {
            var result = CombatRules.TryAct(Foe, move.Action, Now, Player, move);
            if (!result.Executed) { foeNextActAt = Now + .2f; return; }
            if (boss)
            {

                bossOrigin = foePoint;
                bossPrev = foePoint;
                bossLock = playerPoint;
                var direction = Unit(foePoint, playerPoint);
                bossDir = (float)Math.Atan2(direction.Y, direction.X);
                var length = (move.Id == BossCombatBrain.SweepId ? move.Range : BossCombatBrain.FissureLength)
                    * WorldUnitsPerCombatUnit;
                var far = Advance(foePoint, direction, length);
                bossReach = ReachAlong(foePoint, far, length, FoeRadius);
                bossAttempted = false;
                bossSkillId = move.Id;
                // 落石也被墙截断：打不穿岩壁就等于砸在墙上。
                if (move.Id == BossCombatBrain.StoneId)
                    bossLock = Advance(bossOrigin, Unit(bossOrigin, bossLock), ReachAlong(bossOrigin, bossLock, Distance(bossOrigin, bossLock), FoeRadius));
                Emit("warn:boss", foePoint, TelegraphAt(move.Id, 0f));
                bossNextAt = Now + move.Startup + move.Active + move.Recovery + (bossStage == 2 ? .32f : .85f);
            }
            else EnemyAi.MarkActed(foeAi, Now, profile.Ai.AggressionSeconds, move.Id);
            foeNextActAt = Now + move.Startup + move.Active + move.Recovery + .12f;
        }

        void ResolveBossCast()
        {
            if (!boss || bossSkillId.Length == 0) return;
            if (Foe.IsDead || Player.IsDead) { BossTelegraphOff(); return; }
            var move = profile.FindMove(bossSkillId);
            if (move == null || Foe.CurrentMove != move || Foe.Phase == CombatPhase.Hitstun || Foe.Phase == CombatPhase.Idle)
            { BossTelegraphOff(); return; }
            if (Foe.Phase == CombatPhase.Startup)
            {
                TelegraphProgress = Clamp01(Now - (Foe.PhaseUntil - move.Startup), move.Startup);
                TelegraphRadius = RadiusOf(bossSkillId);
                TelegraphPoint = TelegraphAt(bossSkillId, TelegraphProgress);
                return;
            }
            if (Foe.Phase != CombatPhase.Active || bossAttempted) return;
            var progress = Clamp01(Now - (Foe.PhaseUntil - move.Active), move.Active);
            TelegraphProgress = progress;
            TelegraphRadius = RadiusOf(bossSkillId);
            var next = TelegraphAt(bossSkillId, progress);
            TelegraphPoint = next;
            var radius = TelegraphRadius;
            var inside = bossSkillId == BossCombatBrain.StoneId
                ? progress >= .6f && Distance(playerPoint, next) <= radius
                : DistanceToSegment(playerPoint, bossPrev, next) <= radius + PlayerRadius * .5f;
            bossPrev = next;
            if (!inside) return;
            bossAttempted = true;
            var hit = Resolve2D(Foe, Player, Distance(foePoint, playerPoint) / WorldUnitsPerCombatUnit, null);
            Emit(hit.Hit ? "hit:player" : "", foePoint, playerPoint);
            if (hit.Hit) BossTelegraphOff();
        }

        void BossTelegraphOff()
        {
            bossSkillId = "";
            TelegraphProgress = 0f;
            TelegraphRadius = 0f;
        }

        float RadiusOf(string id)
        {
            return id == BossCombatBrain.StoneId ? BossCombatBrain.StoneRadius * WorldUnitsPerCombatUnit
                : BossCombatBrain.WaveRadius * WorldUnitsPerCombatUnit;
        }

        WorldCombatPoint TelegraphAt(string id, float progress)
        {
            if (id == BossCombatBrain.StoneId) return bossLock;
            return Advance(bossOrigin, DirectionOf(bossDir), bossReach * progress);
        }

        void ResolveFoeStrike()
        {
            if (Foe.Phase != CombatPhase.Active || Foe.StruckThisAction) return;
            var action = Foe.CurrentAction;
            if (action != CombatActionType.Light && action != CombatActionType.Heavy) return;
            if (boss && bossSkillId.Length > 0) return;
            var gap = Distance(playerPoint, foePoint) - FoeRadius * .5f;
            var distance = Traverse(foePoint, playerPoint) ? Math.Max(0f, gap) / WorldUnitsPerCombatUnit : float.PositiveInfinity;
            var hit = Resolve2D(Foe, Player, distance, null);
            Emit(hit.Hit ? "hit:player" : "", foePoint, playerPoint);
        }

        // ---------------- 结算与事件 ----------------

        void Settle()
        {
            if (Settled) return;
            Settled = true;
            Emit("death", Player.IsDead ? PlayerPoint : FoePoint, Player.IsDead ? FoePoint : PlayerPoint);
            LastMessage = Player.IsDead ? "倒下了。" : (profile != null ? profile.Name : "妖兽") + "倒下了。";
        }

        CombatResult Refuse(string reason, CombatResult original = null)
        {
            LastMessage = reason;
            if (original != null) { original.Executed = false; original.Reason = reason; return original; }
            return CombatResult.Refused(Player, reason);
        }

        // Origin = 出手点，Target = 命中/落点，控制器直接拿 Target 放特效。
        void Emit(string id, WorldCombatPoint origin, WorldCombatPoint target)
        {
            if (string.IsNullOrEmpty(id)) return;
            EventSequence++;
            LastEvent = id;
            EventPoint = target;
            if (events.Count >= EventCapacity) events.Dequeue();
            events.Enqueue(new WorldCombatEvent { Id = id, Origin = origin, Target = target });
        }

        // ---------------- 几何 ----------------

        const float BodyEpsilon = 2f;

        bool Traverse(WorldCombatPoint a, WorldCombatPoint b)
        {
            return canTraverse == null || canTraverse(a.X, a.Y, b.X, b.Y);
        }

        // 控制器只给点校验，宽体（山君 80）拿点走会蹭进岩壁：脚底再按半径偏置两条一起验。
        bool BodyTraverse(WorldCombatPoint a, WorldCombatPoint b, float radius)
        {
            if (!Traverse(a, b)) return false;
            if (radius <= BodyEpsilon) return true;
            return Traverse(new WorldCombatPoint(a.X + radius, a.Y), new WorldCombatPoint(b.X + radius, b.Y))
                && Traverse(new WorldCombatPoint(a.X - radius, a.Y), new WorldCombatPoint(b.X - radius, b.Y))
                && Traverse(new WorldCombatPoint(a.X, a.Y + radius * .5f), new WorldCombatPoint(b.X, b.Y + radius * .5f))
                && Traverse(new WorldCombatPoint(a.X, a.Y - radius * .5f), new WorldCombatPoint(b.X, b.Y - radius * .5f));
        }

        float ReachAlong(WorldCombatPoint from, WorldCombatPoint to, float want, float radius = 0f)
        {
            if (want <= 0f) return 0f;
            var direction = Unit(from, to);
            for (var travelled = StepWorld; travelled < want; travelled += StepWorld)
                if (!BodyTraverse(from, Advance(from, direction, travelled), radius))
                    return Math.Max(0f, travelled - StepWorld);
            return BodyTraverse(from, Advance(from, direction, want), radius) ? want : Math.Max(0, want - StepWorld);
        }

        static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }

        static WorldCombatPoint Finite(WorldCombatPoint point)
        {
            return new WorldCombatPoint(Finite(point.X) ? point.X : 0f, Finite(point.Y) ? point.Y : 0f);
        }

        // 只沿连线推到最小间距；推不进去时保持原样，绝不硬塞。
        WorldCombatPoint Apart(WorldCombatPoint moved, WorldCombatPoint anchor, float minimum, float radius)
        {
            var gap = Distance(moved, anchor);
            if (gap >= minimum) return moved;
            var away = gap <= .0001f ? new WorldCombatPoint(1f, 0f) : Unit(anchor, moved);
            var pushed = Advance(anchor, away, minimum);
            return BodyTraverse(moved, pushed, radius) ? pushed : moved;
        }

        static WorldCombatPoint Advance(WorldCombatPoint from, WorldCombatPoint direction, float distance)
        {
            return new WorldCombatPoint(from.X + direction.X * distance, from.Y + direction.Y * distance);
        }

        static WorldCombatPoint DirectionOf(float radians)
        {
            return new WorldCombatPoint((float)Math.Cos(radians), (float)Math.Sin(radians));
        }

        static WorldCombatPoint Unit(WorldCombatPoint from, WorldCombatPoint to)
        {
            var gap = Distance(from, to);
            return gap <= .0001f ? new WorldCombatPoint(1f, 0f)
                : new WorldCombatPoint((to.X - from.X) / gap, (to.Y - from.Y) / gap);
        }

        static WorldCombatPoint AwayVector(WorldCombatPoint from, WorldCombatPoint target)
        {
            var away = Unit(from, target);
            return new WorldCombatPoint(-away.X, -away.Y);
        }

        static WorldCombatPoint Perpendicular(WorldCombatPoint direction) { return new WorldCombatPoint(-direction.Y, direction.X); }

        static float Distance(WorldCombatPoint a, WorldCombatPoint b)
        {
            var dx = a.X - b.X; var dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        static float DistanceToSegment(WorldCombatPoint point, WorldCombatPoint a, WorldCombatPoint b)
        {
            var length = Distance(a, b);
            if (length <= .0001f) return Distance(point, a);
            var t = ((point.X - a.X) * (b.X - a.X) + (point.Y - a.Y) * (b.Y - a.Y)) / (length * length);
            return Distance(point, Advance(a, Unit(a, b), length * Clamp01(t, 1f)));
        }

        static float Clamp01(float value) { return value < 0f ? 0f : value > 1f ? 1f : value; }
        static float Clamp01(float value, float span) { return Clamp01(span <= .0001f ? 0f : value / span); }
    }
}
