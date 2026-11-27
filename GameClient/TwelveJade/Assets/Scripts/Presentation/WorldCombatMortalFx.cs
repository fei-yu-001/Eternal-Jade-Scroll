using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    public enum MortalFxKind
    {
        LightSlash,
        HeavySlash,
        StepDust,
        ImpactGrit,
        HerbBreath,
        DefeatAsh,
        BossWarnRing
    }

    /// <summary>
    /// 青石镇凡人招式的克制水墨表现：纯 UGUI 几何（无贴图加载、无悬浮光环）。
    /// 节点固定保持 localRotation = identity，以保证全方向出招时重力与抬升始终朝屏幕上方。
    /// </summary>
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class WorldCombatMortalFx : WorldCombatFxGraphic
    {
        static readonly Color ColdSteel = new Color(.74f, .76f, .73f);
        static readonly Color GravelDark = new Color(.28f, .25f, .22f);
        static readonly Color GravelLight = new Color(.56f, .51f, .43f);
        static readonly Color HerbWarm = new Color(.62f, .60f, .40f);
        static readonly Color DustPale = new Color(.68f, .64f, .55f);

        MortalFxKind kind;
        Vector2 dir = Vector2.right;
        float lift;

        public MortalFxKind Kind => kind;
        public Vector2 Direction => dir;
        public float Lift => lift;
        public bool ReducedForCheck => low;
        public float NormalizedProgressForCheck => t;

        public void BeginMortal(float now, float wait, float duration, Vector2 foot, Vector2 direction,
            float size, float verticalLift, Color color, bool heavy, MortalFxKind fxKind)
        {
            kind = fxKind;
            dir = direction.sqrMagnitude > .0001f ? direction.normalized : Vector2.right;
            lift = verticalLift;
            Begin(now, wait, duration, foot, foot + Vector2.right, size, color, heavy);
        }

        protected override void OnPopulateMesh(VertexHelper v)
        {
            v.Clear();
            float k = Ease;
            if (k <= .002f) return;
            k *= low ? .68f : 1f;
            float drift = low ? .30f : 1f;
            var perp = new Vector2(-dir.y, dir.x);

            switch (kind)
            {
                case MortalFxKind.LightSlash:
                    DrawBladeWind(v, k, drift, perp, false);
                    break;
                case MortalFxKind.HeavySlash:
                    DrawBladeWind(v, k, drift, perp, true);
                    break;
                case MortalFxKind.StepDust:
                    DrawStepDust(v, k, drift, perp);
                    break;
                case MortalFxKind.ImpactGrit:
                    DrawImpactGrit(v, k, drift, perp);
                    break;
                case MortalFxKind.HerbBreath:
                    DrawHerbBreath(v, k, drift);
                    break;
                case MortalFxKind.DefeatAsh:
                    DrawDefeatAsh(v, k, drift, perp);
                    break;
                case MortalFxKind.BossWarnRing:
                    DrawBossWarnRing(v, k, drift);
                    break;
            }
        }

        void DrawBladeWind(VertexHelper v, float k, float drift, Vector2 perp, bool heavy)
        {
            float reach = heavy ? Mathf.Clamp(radius * .88f, 46f, 114f) : Mathf.Clamp(radius * .72f, 34f, 88f);
            float advance = radius * (heavy ? .18f : .14f) * drift * Out;
            var center = new Vector2(0f, lift) + dir * advance;
            float baseAngle = Mathf.Atan2(dir.y, dir.x);
            float sweep = (heavy ? .28f : .22f) * drift * (t - .35f);
            float halfSpan = heavy ? Mathf.Lerp(.90f, .46f, t) : Mathf.Lerp(.68f, .36f, t);
            int segs = heavy ? 14 : 12;
            float baseThick = heavy ? (boss ? 16f : 13.5f) : 8.5f;

            for (int i = 0; i < segs; i++)
            {
                float a = baseAngle + Mathf.Lerp(-halfSpan, halfSpan, i / (float)segs) + sweep;
                float b = baseAngle + Mathf.Lerp(-halfSpan, halfSpan, (i + 1f) / segs) + sweep;
                float taper = Mathf.Sin((i + .5f) / segs * Mathf.PI);
                float thick = baseThick * taper * (1f - t * .40f);
                ArcBandAt(v, center, reach - thick * .65f, reach + thick * .35f, a, b,
                    Alpha(tint, (heavy ? .78f : .72f) * k * taper));
                if (heavy && taper > .30f)
                    ArcBandAt(v, center, reach - thick * .95f, reach - thick * .95f + 2.4f, a, b,
                        Alpha(tint, .42f * k * taper));
                if (taper > .35f)
                    ArcBandAt(v, center, reach + thick * .18f, reach + thick * .18f + (heavy ? 2.3f : 1.8f), a, b,
                        Alpha(ColdSteel, (heavy ? .70f : .64f) * k * taper));
            }

            int wisps = heavy ? 3 : 2;
            for (int i = 0; i < wisps; i++)
            {
                float side = (i - (wisps - 1) * .5f) * (heavy ? 18f : 14f);
                float len = reach * (.52f + .36f * drift * Out);
                var start = center + perp * side + dir * 6f;
                var end = center + perp * (side * (1f - .35f * t)) + dir * len;
                Line(v, start, end, heavy ? 2.0f : 1.5f, Alpha(tint, .36f * k * (1f - t * .6f)));
            }
        }

        void DrawStepDust(VertexHelper v, float k, float drift, Vector2 perp)
        {
            float spread = Mathf.Clamp(radius, 28f, 110f);
            bool dodgeTrail = lift >= 10f;
            int puffs = dodgeTrail ? 5 : 4;

            for (int i = 0; i < puffs; i++)
            {
                float seed = Hash(i * 31 + 7);
                float sideSeed = Hash(i * 19 + 3) - .5f;
                float push = (10f + spread * .34f * (.3f + .7f * seed)) * Out * drift;
                Vector2 offset = dodgeTrail
                    ? -dir * push + perp * (sideSeed * 22f * (.4f + .6f * drift))
                    : -dir * (push * .6f) + perp * (sideSeed * spread * .55f * (.35f + .65f * Out * drift));
                var pos = new Vector2(0f, 3f + (8f + 10f * seed) * Out * drift) + offset;
                float rx = (7f + 6f * seed) * (.65f + .55f * Out);
                DustEllipse(v, pos, rx, rx * .46f, Alpha(tint, (.34f + .16f * seed) * k * (1f - t * .45f)));
            }

            if (dodgeTrail)
            {
                for (int i = 0; i < 3; i++)
                {
                    float y = 10f + i * 7f;
                    float side = (i - 1) * 7f;
                    float len = spread * (.34f + .30f * Out * drift);
                    var from = new Vector2(0f, y) + perp * side + dir * 4f;
                    var to = from + dir * len;
                    Line(v, from, to, 1.8f, Alpha(DustPale, .38f * k * (1f - t * .5f)));
                }
            }
        }

        void DrawImpactGrit(VertexHelper v, float k, float drift, Vector2 perp)
        {
            float r = Mathf.Clamp(radius, 30f, 76f);
            var hitPos = new Vector2(0f, lift);
            var slitDir = (perp + dir * .32f).normalized;
            float slitHalf = r * .34f * (1f - t * .32f) * (low ? .75f : 1f);
            Line(v, hitPos - slitDir * slitHalf, hitPos + slitDir * slitHalf, 2.6f * (1f - t * .5f),
                Alpha(ColdSteel, .74f * k));

            float baseAngle = Mathf.Atan2(dir.y, dir.x);
            for (int i = 0; i < 4; i++)
            {
                float seed = Hash(i * 43 + 5);
                float a = baseAngle + (i - 1.5f) * .45f + (seed - .5f) * .20f;
                var shockDir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                float start = 4f + 5f * Out;
                float end = r * (.52f + .32f * seed) * (.42f + .58f * Out * drift);
                var p0 = hitPos + shockDir * start;
                var p1 = hitPos + shockDir * end;
                var n = new Vector2(-shockDir.y, shockDir.x) * (2.8f * (1f - t * .55f));
                var spikeColor = i == 1 || i == 2 ? Color.Lerp(tint, DustPale, .35f) : tint;
                Triangle(v, p0 - n, p0 + n, p1, Alpha(spikeColor, .78f * k * (1f - t * .35f)));
            }

            int chips = low ? 5 : (boss ? 10 : 7);
            for (int i = 0; i < chips; i++)
            {
                float seed = Hash(i * 53 + 11);
                float a = baseAngle + (seed - .5f) * 1.45f;
                var chipDir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                float travel = r * (.40f + .60f * seed) * Out * drift;
                float y = Mathf.Max(2f, lift + 18f * seed * Out * drift - 24f * t * t);
                var at = new Vector2(0f, y) + chipDir * travel;
                var side = new Vector2(-chipDir.y, chipDir.x) * (3.0f + 1.4f * seed);
                var fwd = chipDir * (3.8f + 1.6f * seed);
                var chipColor = i % 2 == 0 ? GravelLight : GravelDark;
                Quad(v, at - side, at + fwd * .6f + side * .4f, at + fwd, at - fwd * .4f - side * .5f,
                    Alpha(chipColor, .88f * (1f - t * .5f) * (low ? .75f : 1f)));
            }

            for (int i = 0; i < 2; i++)
            {
                float side = (i == 0 ? -1f : 1f) * r * .24f;
                var sc0 = new Vector2(0f, 3f) + dir * (r * .14f * Out * drift) + perp * side;
                var sc1 = sc0 + dir * (r * .26f * Out * drift) + perp * (side * .45f);
                Line(v, sc0, sc1, 1.8f, Alpha(DustPale, .36f * k * (1f - t * .6f)));
            }
        }

        void DrawHerbBreath(VertexHelper v, float k, float drift)
        {
            float r = Mathf.Clamp(radius, 32f, 58f);
            float ringR = r * (.40f + .32f * Out * drift);
            FlatArc(v, new Vector2(0f, 3f), ringR, .36f, 1.8f, .25f, 2.55f, Alpha(tint, .24f * k));
            FlatArc(v, new Vector2(0f, 3f), ringR, .36f, 1.8f, 3.40f, 5.75f, Alpha(tint, .24f * k));

            for (int i = 0; i < 6; i++)
            {
                float s1 = Hash(i * 41 + 13);
                float s2 = Hash(i * 23 + 5);
                float sway = low ? 0f : Mathf.Sin((t * 1.6f + s1) * Mathf.PI) * 5f;
                float x = (s1 - .5f) * r * .92f * (1f - .25f * t) + sway;
                float y = 10f + (24f + 42f * s2) * Out * (low ? .52f : 1f);
                var at = new Vector2(x, y);
                var c = i % 2 == 0 ? tint : HerbWarm;
                Line(v, at, at + new Vector2(sway * .25f, 8f + 5f * s2), 2.1f, Alpha(c, .54f * k * (1f - t * .4f)));
            }
        }

        void DrawDefeatAsh(VertexHelper v, float k, float drift, Vector2 perp)
        {
            float r = Mathf.Clamp(radius, 36f, 68f);
            for (int i = 0; i < 5; i++)
            {
                float seed = Hash(i * 37 + 9);
                float a = seed * Mathf.PI * 2f;
                float reach = r * (.28f + .62f * seed) * Out * drift;
                var at = new Vector2(Mathf.Cos(a) * reach, 3f + Mathf.Sin(a) * reach * .32f);
                float rx = (8f + 6f * seed) * (.7f + .4f * Out);
                DustEllipse(v, at, rx, rx * .42f, Alpha(tint, .36f * k * (1f - t * .5f)));
            }

            for (int i = 0; i < 6; i++)
            {
                float s1 = Hash(i * 29 + 17);
                float s2 = Hash(i * 17 + 3);
                float sway = low ? 0f : Mathf.Sin((t * 1.4f + s1) * Mathf.PI) * 6f;
                float x = (s1 - .5f) * r * .88f + sway + dir.x * 8f * Out * drift;
                float y = Mathf.Max(2f, 26f * (1f - Out * .72f) + 6f * s2);
                var at = new Vector2(x, y);
                DustEllipse(v, at, 2.6f, 2.1f, Alpha(tint, .52f * k * (1f - t * .7f)));
            }
        }

        void DrawBossWarnRing(VertexHelper v, float k, float drift)
        {
            float r = Mathf.Clamp(radius, 44f, 120f);
            float outer = r * (.78f + .22f * Out);
            for (int i = 0; i < 4; i++)
            {
                float from = i * Mathf.PI * .5f + .16f;
                float to = (i + 1) * Mathf.PI * .5f - .16f;
                FlatArc(v, new Vector2(0f, 2f), outer, .36f, 2.8f, from, to, Alpha(tint, .56f * k));
            }
            FlatArc(v, new Vector2(0f, 2f), r * .48f, .36f, 1.5f, 0f, Mathf.PI * 2f, Alpha(tint, .30f * k));
            for (int i = 0; i < 5; i++)
            {
                float a = Hash(i * 29 + 3) * Mathf.PI * 2f;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * .36f);
                Line(v, d * (r * .22f), d * (r * (.68f + .22f * drift)), 1.7f, Alpha(tint, .38f * k));
            }
        }

        static void ArcBandAt(VertexHelper v, Vector2 center, float inner, float outer, float from, float to, Color color)
        {
            const int steps = 4;
            for (int i = 0; i < steps; i++)
            {
                float a = Mathf.Lerp(from, to, i / (float)steps);
                float b = Mathf.Lerp(from, to, (i + 1f) / steps);
                var da = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                var db = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
                Triangle(v, center + da * inner, center + da * outer, center + db * outer, color);
                Triangle(v, center + da * inner, center + db * outer, center + db * inner, color);
            }
        }

        static void FlatArc(VertexHelper v, Vector2 center, float radius, float yRatio, float width, float from, float to, Color color)
        {
            const int steps = 10;
            float inner = Mathf.Max(2f, radius - width * .5f);
            float outer = radius + width * .5f;
            for (int i = 0; i < steps; i++)
            {
                float a = Mathf.Lerp(from, to, i / (float)steps);
                float b = Mathf.Lerp(from, to, (i + 1f) / steps);
                var da = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * yRatio);
                var db = new Vector2(Mathf.Cos(b), Mathf.Sin(b) * yRatio);
                Triangle(v, center + da * inner, center + da * outer, center + db * outer, color);
                Triangle(v, center + da * inner, center + db * outer, center + db * inner, color);
            }
        }

        static void DustEllipse(VertexHelper v, Vector2 c, float rx, float ry, Color color)
        {
            const int steps = 10;
            for (int i = 0; i < steps; i++)
            {
                float a = i * Mathf.PI * 2f / steps;
                float b = (i + 1) * Mathf.PI * 2f / steps;
                Triangle(v, c, c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry),
                    c + new Vector2(Mathf.Cos(b) * rx, Mathf.Sin(b) * ry), color);
            }
        }

        static Color Alpha(Color baseColor, float alpha) => new Color(baseColor.r, baseColor.g, baseColor.b, Mathf.Clamp01(alpha));
    }
}
