using System;
using TwelveJade.Core;

namespace TwelveJade.Combat
{
    public enum BossSkill { None, Sweep, Fissure, FallingStone }

    // One immutable spatial contract per cast. Actual HP/stamina/phase/damage remain Core-owned.
    public sealed class BossCombatBrain
    {
        public const string EnemyId = "qingshi-mountain-lord";
        public const string SweepId = "mountain-sweep", FissureId = "earth-fissure", StoneId = "seal-stonefall";
        public const float FissureLength = 3.8f, WaveRadius = .20f, StoneRadius = .48f;
        readonly EnemyProfile profile;
        float previousWaveProgress;
        bool attempted, pending;
        int sequence;
        CombatMove cast;
        public BossCombatBrain(EnemyProfile profile)
        {
            this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
            foreach (var id in new[] { SweepId, FissureId, StoneId })
                if (profile.FindMove(id) == null) throw new ArgumentException("Boss move missing: " + id);
        }
        public int Stage { get; private set; } = 1;
        public int Sequence => sequence;
        public int ResolvedCount { get; private set; }
        public BossSkill Skill { get; private set; }
        public float OriginX { get; private set; }
        public float TargetX { get; private set; }
        public int Facing { get; private set; } = -1;
        public float StartedAt { get; private set; }
        public float NextActAt { get; private set; } = 1.2f;
        public CombatMove Cast => cast;
        public bool Pending => pending;
        public void ObserveHealth(CombatActorState boss)
        { if (boss != null && boss.Hp <= boss.MaxHp / 2) Stage = 2; }
        public CombatMove Choose(CombatActorState boss, CombatActorState player, float now)
        {
            ObserveHealth(boss);
            if (boss == null || player == null || boss.IsDead || player.IsDead || boss.Phase != CombatPhase.Idle || now < NextActAt) return null;
            string[] pattern = Stage == 2 ? new[] { FissureId, StoneId, SweepId, StoneId } : new[] { SweepId, FissureId, StoneId };
            var move = profile.FindMove(pattern[sequence % pattern.Length]);
            // The close sweep really pursues first. Ranged casts still honour their Core reach.
            return boss.Stamina >= move.StaminaCost && Math.Abs(player.X - boss.X) <= move.Range ? move : null;
        }
        public CombatResult TryBegin(CombatActorState boss, CombatActorState player, CombatMove move, float now)
        {
            if (move == null || (move.Id != SweepId && move.Id != FissureId && move.Id != StoneId))
                return CombatResult.Refused(boss, "此招不属山君。 ");
            var result = CombatRules.TryAct(boss, move.Action, now, player, move);
            if (!result.Executed) return result;
            cast = move; sequence++; OriginX = boss.X; TargetX = player.X;
            Facing = player.X < boss.X ? -1 : 1; StartedAt = now;
            Skill = move.Id == SweepId ? BossSkill.Sweep : move.Id == FissureId ? BossSkill.Fissure : BossSkill.FallingStone;
            attempted = false; pending = true; previousWaveProgress = 0;
            // Keep complete advertised startup even if half-health is crossed during this cast.
            NextActAt = now + move.Startup + move.Active + move.Recovery + (Stage == 2 ? .32f : .85f);
            return result;
        }
        public float EndX => Skill == BossSkill.FallingStone ? TargetX : ActionCombatMotor.ClampX(OriginX + Facing * (Skill == BossSkill.Fissure ? FissureLength : cast?.Range ?? 0));
        public float ZoneMin => Skill == BossSkill.FallingStone ? Math.Max(ActionCombatMotor.MinX, TargetX - StoneRadius) : Math.Min(OriginX, EndX);
        public float ZoneMax => Skill == BossSkill.FallingStone ? Math.Min(ActionCombatMotor.MaxX, TargetX + StoneRadius) : Math.Max(OriginX, EndX);
        public float StartupProgress(float now) => cast == null ? 0 : Unit((now - StartedAt) / cast.Startup);
        public float ActiveProgress(float now) => cast == null ? 0 : Unit((now - StartedAt - cast.Startup) / cast.Active);
        public float WaveX(float now) => OriginX + (EndX - OriginX) * ActiveProgress(now);
        public bool TelegraphVisible(CombatActorState boss) => pending && boss != null && !boss.IsDead && boss.Phase == CombatPhase.Startup && boss.CurrentMove == cast;
        public CombatResult Resolve(CombatActorState boss, CombatActorState player, float now)
        {
            if (!pending || boss == null || player == null) return null;
            if (boss.IsDead || player.IsDead || boss.Phase == CombatPhase.Hitstun || boss.CurrentMove != cast)
            { Cancel(); return null; }
            if (boss.Phase == CombatPhase.Recovery || boss.Phase == CombatPhase.Idle)
            {
                pending = false;
                if (!attempted) { attempted = true; return Miss(boss, player, "离开了预警范围。"); }
                return null;
            }
            if (boss.Phase != CombatPhase.Active || attempted) return null;
            bool inZone;
            if (Skill == BossSkill.Fissure)
            {
                float progress = ActiveProgress(now), from = OriginX + (EndX - OriginX) * previousWaveProgress;
                float to = OriginX + (EndX - OriginX) * progress; previousWaveProgress = progress;
                // Swept interval prevents frame-step tunnelling. Clip to the advertised strip.
                float min = Math.Max(ZoneMin, Math.Min(from, to) - WaveRadius);
                float max = Math.Min(ZoneMax, Math.Max(from, to) + WaveRadius);
                inZone = player.X >= min && player.X <= max;
                if (!inZone) return null; // one wave, may meet the player later in Active
            }
            else inZone = player.X >= ZoneMin && player.X <= ZoneMax;
            attempted = true; ResolvedCount++;
            if (!inZone) return Miss(boss, player, "离开了锁定落点。");
            // Use the real boss actor, its real X and registered range. No proxy target or temporary teleport.
            return CombatRules.ResolveStrike(boss, player, now);
        }
        public void Cancel() { pending = false; attempted = true; }
        static CombatResult Miss(CombatActorState boss, CombatActorState player, string reason)
        { var result = CombatResult.Missed(boss, player, reason); result.Whiffed = true; return result; }
        static float Unit(float v) => Math.Max(0, Math.Min(1, v));
    }
}
