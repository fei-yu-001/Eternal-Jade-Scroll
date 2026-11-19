using System;
using TwelveJade.Core;
using TwelveJade.Combat;

namespace TwelveJade.Presentation
{
    // Distance-driven selection; pure managed so the actual production selector can be exercised offline.
    public sealed class EnemyCombatFrameClock
    {
        public const int Cells = 4, CellPixels = 512;
        public const float FootInset = 28f, CycleUnits = 1.12f;
        public float Distance { get; private set; }
        public int WalkFrame { get; private set; } = -1;
        public void Move(float actualDistance, bool walking)
        {
            if (walking && actualDistance > 0 && !float.IsNaN(actualDistance) && !float.IsInfinity(actualDistance))
                Distance += actualDistance;
            WalkFrame = walking ? (int)Math.Floor(Distance / CycleUnits * Cells) % Cells : -1;
        }
        public static int AttackFrame(CombatPhase phase, BossSkill skill, float progress)
        {
            if (phase == CombatPhase.Startup) return 0;
            if (phase == CombatPhase.Active) return skill == BossSkill.Sweep || progress < .3f ? 1 : 2;
            if (phase == CombatPhase.Recovery) return 3;
            return -1;
        }
    }
}
