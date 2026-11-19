using System;
using TwelveJade.Core;

namespace TwelveJade.Combat
{
    // Spatial/input layer for the real encounter. Hp, stamina, phases, invulnerability and damage
    // are written only by CombatRules. This class owns facing, a single input buffer and displacement.
    public sealed class ActionCombatMotor
    {
        public const float MinX = -3.25f, MaxX = 3.25f, WalkSpeed = 1.45f;
        public const float DodgeDistance = 1.65f, BufferSeconds = .22f;
        public int Facing { get; private set; }
        public int ActionFacing { get; private set; }
        public int Sequence { get; private set; }
        public int ComboStep { get; private set; }
        public int DirectionMisses { get; private set; }
        public bool HasBufferedAction => buffered.HasValue;
        public float StartedAt { get; private set; }
        public float Travelled { get; private set; }
        public float LastDelta { get; private set; }
        public CombatActionType StartedAction { get; private set; }
        CombatActionType? buffered;
        float bufferUntil, comboUntil, displacementProgress;
        int bufferedDirection, dodgeDirection;
        bool attempted;

        public ActionCombatMotor(int facing) { Facing = ActionFacing = facing < 0 ? -1 : 1; }
        public void Face(float direction)
        {
            if (!Finite(direction) || Math.Abs(direction) < .001f) return;
            Facing = direction < 0 ? -1 : 1;
        }
        public void ClearInput() { buffered = null; }
        public void ResetTravel() { LastDelta = 0; }
        public static bool CanWalk(CombatActorState actor) => actor != null && !actor.IsDead && actor.Phase == CombatPhase.Idle;
        public float Walk(CombatActorState actor, CombatActorState other, float distance)
        {
            if (!CanWalk(actor) || !Finite(distance)) return 0;
            Face(distance); return Displace(actor, other, distance, false);
        }
        public static float ClampX(float x) => Math.Max(MinX, Math.Min(MaxX, x));
        public float Displace(CombatActorState actor, CombatActorState other, float distance, bool passThrough)
        {
            if (actor == null || actor.IsDead || !Finite(distance)) return 0;
            float from = actor.X, to = ClampX(from + distance);
            // Ordinary walking/attack steps cannot walk through a body. A committed dodge can.
            if (!passThrough && other != null && !other.IsDead)
            {
                const float separation = .24f;
                if (from < other.X && to > from) to = Math.Min(to, Math.Max(from, other.X - separation));
                else if (from > other.X && to < from) to = Math.Max(to, Math.Min(from, other.X + separation));
            }
            actor.X = to; LastDelta += to - from; Travelled += Math.Abs(to - from); return to - from;
        }
        public CombatResult Request(CombatActorState actor, CombatActorState opponent, CombatActionType action,
            float now, float movementDirection = 0)
        {
            if (actor == null) return new CombatResult { Reason = "没有参战者。" };
            if (actor.Phase != CombatPhase.Idle && !actor.IsDead)
            {
                // One deliberate press near the end of recovery. No automatic chain on held input;
                // neither hitstun nor a new press at the beginning of a windup can cancel an action.
                bool attack = actor.CurrentAction == CombatActionType.Light || actor.CurrentAction == CombatActionType.Heavy;
                float remaining = actor.PhaseUntil - now;
                if (actor.Phase == CombatPhase.Active) remaining += CombatRules.TimingOf(actor).recovery;
                if (attack && (actor.Phase == CombatPhase.Active || actor.Phase == CombatPhase.Recovery) &&
                    remaining <= BufferSeconds + CombatRules.PhaseEpsilon && action != CombatActionType.Wait)
                {
                    buffered = action; bufferedDirection = Sign(movementDirection);
                    bufferUntil = now + BufferSeconds + .03f;
                    return CombatResult.Refused(actor, "已衔接，收招后出手。");
                }
                return CombatRules.TryAct(actor, action, now, opponent);
            }
            return Start(actor, opponent, action, now, Sign(movementDirection));
        }
        public CombatResult Flush(CombatActorState actor, CombatActorState opponent, float now)
        {
            if (!buffered.HasValue) return null;
            if (actor == null || actor.IsDead || actor.Phase == CombatPhase.Hitstun || now > bufferUntil)
            { buffered = null; return null; }
            if (actor.Phase != CombatPhase.Idle) return null;
            var next = buffered.Value; int direction = bufferedDirection; buffered = null;
            return Start(actor, opponent, next, now, direction);
        }
        CombatResult Start(CombatActorState actor, CombatActorState opponent, CombatActionType action, float now, int movement)
        {
            int combo = action == CombatActionType.Light ? (now <= comboUntil ? ComboStep % 3 + 1 : 1) : 0;
            var move = PlayerMove(action, combo);
            var result = CombatRules.TryAct(actor, action, now, opponent, move);
            if (!result.Executed || action == CombatActionType.Wait) return result;
            ComboStep = combo;
            comboUntil = action == CombatActionType.Light
                ? now + move.Startup + move.Active + move.Recovery + .42f : -1;
            dodgeDirection = movement != 0 ? movement : opponent == null ? -Facing : -SignNonzero(opponent.X - actor.X, Facing);
            ObserveStart(actor, now);
            return result;
        }
        public static CombatMove PlayerMove(CombatActionType action, int combo = 1)
        {
            if (action != CombatActionType.Light && action != CombatActionType.Heavy) return null;
            var timing = CombatRules.Timing(action); bool heavy = action == CombatActionType.Heavy;
            return new CombatMove(heavy ? "action-heavy" : "action-light-" + Math.Max(1, Math.Min(3, combo)),
                heavy ? "破势" : combo == 2 ? "回锋" : combo == 3 ? "截云" : "起锋", action,
                heavy ? CombatRules.HeavyBaseDamage : CombatRules.LightBaseDamage,
                timing.startup, timing.active, timing.recovery, CombatRules.StrikeRange, CombatRules.StaminaCost(action))
            { Knockback = heavy ? .34f : .10f };
        }
        // Called for enemies after their own EnemyAi/CombatRules.TryAct succeeds as well.
        public void ObserveStart(CombatActorState actor, float now)
        {
            Sequence++; StartedAt = now; StartedAction = actor.CurrentAction; ActionFacing = Facing;
            displacementProgress = 0; attempted = false;
        }
        public void Step(CombatActorState actor, CombatActorState other, float now, bool player)
        {
            if (actor == null || actor.IsDead || actor.Phase == CombatPhase.Hitstun)
            { ClearInput(); ComboStep = 0; comboUntil = -1; return; }
            if (actor.Phase != CombatPhase.Startup && actor.Phase != CombatPhase.Active && actor.Phase != CombatPhase.Recovery) return;
            var timing = CombatRules.TimingOf(actor);
            if (timing.active <= 0) return;
            float progress = Math.Max(0, Math.Min(1, (now - StartedAt - timing.startup) / timing.active));
            float eased = progress * progress * (3 - 2 * progress);
            float increment = Math.Max(0, eased - displacementProgress); displacementProgress = eased;
            if (increment <= 0) return;
            if (player && StartedAction == CombatActionType.Dodge)
                Displace(actor, other, dodgeDirection * DodgeDistance * increment, true);
            else if (player && (StartedAction == CombatActionType.Light || StartedAction == CombatActionType.Heavy))
                Displace(actor, other, ActionFacing * (StartedAction == CombatActionType.Heavy ? .18f : .07f + .025f * ComboStep) * increment, false);
        }
        public bool TryStrike(CombatActorState actor)
        {
            if (actor == null || attempted || actor.StruckThisAction || actor.Phase != CombatPhase.Active ||
                (actor.CurrentAction != CombatActionType.Light && actor.CurrentAction != CombatActionType.Heavy)) return false;
            attempted = true; return true;
        }
        public bool Faces(CombatActorState actor, CombatActorState target) => actor != null && target != null &&
            (target.X - actor.X) * ActionFacing >= -.025f;
        public CombatResult DirectionMiss(CombatActorState actor, CombatActorState target)
        {
            DirectionMisses++;
            // No dummy target or invented coordinates: the existing Core state proceeds through
            // recovery unchanged; this motor already consumed this action's spatial attempt.
            var result = CombatResult.Missed(actor, target, "背向挥空。"); result.Whiffed = true; return result;
        }
        static int Sign(float value) => !Finite(value) || Math.Abs(value) < .001f ? 0 : value < 0 ? -1 : 1;
        static int SignNonzero(float value, int fallback) { int sign = Sign(value); return sign == 0 ? fallback : sign; }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
