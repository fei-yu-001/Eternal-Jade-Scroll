using System;
using TwelveJade.Core;

namespace TwelveJade.Presentation
{
    // Pure presentation rules shared by the live components and offline boundary checks.
    public static class NpcIdentityRules
    {
        public const float RevealDistance = 180f;
        public static bool Visible(bool townActive, float playerX, float playerY, float npcX, float npcY)
        {
            if (!townActive || !Finite(playerX) || !Finite(playerY) || !Finite(npcX) || !Finite(npcY)) return false;
            double dx = (double)playerX - npcX, dy = (double)playerY - npcY;
            return dx * dx + dy * dy <= RevealDistance * RevealDistance;
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static bool TaskVisible(QuestOverheadState state) => state == QuestOverheadState.Available ||
            state == QuestOverheadState.InProgress || state == QuestOverheadState.Completable;
        public static string TaskDescription(QuestOverheadState state) => state == QuestOverheadState.Available ? "可接任务" :
            state == QuestOverheadState.Completable ? "可交任务" : state == QuestOverheadState.InProgress ? "任务进行中" : "";
        public static string Title(NpcDef definition) => definition?.Title ?? "";
        public static float MotionOffset(float phase, bool reduced) => reduced ? 0f : (float)Math.Sin(phase) * 2f;
        public static float Breath(float phase, bool reduced) => reduced ? 1f : .91f + .09f * (.5f + .5f * (float)Math.Sin(phase));
    }
}
