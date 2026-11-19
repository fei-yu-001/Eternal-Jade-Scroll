using System;
using System.Linq;
using TwelveJade.Core;

namespace TwelveJade.Combat
{
    // 任务书 K2/K3（M1/M3）：妖兽独立行为树。
    //
    // 与 Core 的分工照旧：这里只出"该出哪一招、该往哪儿走"的意图，扣血、精力、相位一律由
    // CombatRules 结算；Core 不用随机数，这里也一律不用——同一套站位与时钟推进必然得到
    // 同一场战斗，验收脚本才能逐帧断言。
    public sealed class EnemyAiState
    {
        public float NextActAt { get; set; }
        public int Chain { get; set; }
        public string LastMoveId { get; set; } = "";
    }

    public static class EnemyAi
    {
        // 连段周期：每 ChainCycle 下的最后一下才舍得甩重手。
        public const int ChainCycle = 3;

        public static EnemyAiState NewState(float firstActAt) => new EnemyAiState { NextActAt = firstActAt };

        public static float ReachOf(CombatMove move) => move?.Range ?? CombatRules.StrikeRange;

        // 该逼近还是要出手：返回 null 时，表现层按 ApproachDirection 挪步。
        public static CombatMove Choose(EnemyProfile profile, EnemyAiState state,
            CombatActorState foe, CombatActorState player, float now)
        {
            if (profile == null || state == null || foe == null || player == null) return null;
            if (foe.IsDead || player.IsDead) return null;
            if (foe.Phase != CombatPhase.Idle) return null;
            // legacy 招式集是留给"没登记 2.0 数据的妖"的兜底通道（三只正式妖自 N1 起全是 profile2）：
            // 声明 legacy 的敌人由表现层按 M4-03 的固定轻击出手，这里如实不接管，不越俎代庖。
            if (profile.Ai.MoveSet == EnemyMoveSet.Legacy) return null;
            if (now < state.NextActAt) return null;

            var distance = Math.Abs(player.X - foe.X);
            var reachable = profile.Moves
                .Where(m => distance <= m.Range && foe.Stamina >= m.StaminaCost)
                .ToList();
            if (reachable.Count == 0) return null;

            return PickByBehavior(profile.Ai.Behavior, reachable, state);
        }

        static CombatMove PickByBehavior(string behavior, System.Collections.Generic.IReadOnlyList<CombatMove> reachable,
            EnemyAiState state)
        {
            switch (behavior)
            {
                // 高体质：平时用尖牙挑击压着打，攒满一轮才撞一下。
                case "boar-charge":
                    return HeavyOnCycle(reachable, "rush", state) ?? LowestStartup(reachable);
                // 高移速：连咬两口就走，第三下换成前爪重击撕咬（挂流血）。
                case "wolf-pounce":
                    return HeavyOnCycle(reachable, "rend", state) ?? LowestStartup(reachable);
                // 重型精英：只会一招撼地重拳，起手长后摇长，正面霸体。
                case "mandrill-bulwark":
                    return reachable.FirstOrDefault(m => m.Id == "smash") ?? Highest(reachable);
                default:
                    return Highest(reachable);
            }
        }

        static CombatMove HeavyOnCycle(System.Collections.Generic.IReadOnlyList<CombatMove> reachable,
            string id, EnemyAiState state)
        {
            var move = reachable.FirstOrDefault(m => m.Id == id);
            if (move == null) return null;
            return state != null && state.Chain % ChainCycle == ChainCycle - 1 ? move : null;
        }

        static CombatMove Highest(System.Collections.Generic.IReadOnlyList<CombatMove> moves) =>
            moves.OrderByDescending(m => m.Power).ThenBy(m => m.Startup).FirstOrDefault();

        static CombatMove LowestStartup(System.Collections.Generic.IReadOnlyList<CombatMove> moves) =>
            moves.OrderBy(m => m.Startup).ThenByDescending(m => m.Power).FirstOrDefault();

        // 逼近方向：走到首选招式的射程里（Core 的坐标系，正数向右；0 = 已经站好了）。
        public static float ApproachDirection(EnemyProfile profile, CombatActorState foe, CombatActorState player)
        {
            if (profile == null || foe == null || player == null) return 0f;
            var dx = player.X - foe.X;
            var reach = Math.Min(ReachOf(profile.Moves.FirstOrDefault()), profile.Ai.PreferredRange);
            return Math.Abs(dx) <= reach ? 0f : Math.Sign(dx);
        }

        // 出手之后交回表现层：把冷却与连段计数记上。
        public static void MarkActed(EnemyAiState state, float now, float aggressionSeconds, string moveId)
        {
            if (state == null) return;
            state.Chain = (state.Chain + 1) % ChainCycle;
            state.LastMoveId = moveId ?? "";
            state.NextActAt = now + Math.Max(.01f, aggressionSeconds);
        }
    }
}
