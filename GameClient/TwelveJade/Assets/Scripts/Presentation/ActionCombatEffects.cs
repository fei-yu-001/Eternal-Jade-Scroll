using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // Short-lived combat effects driven by the same injected battle clock as damage.
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class ActionCombatEffects : MaskableGraphic
    {
        struct Stroke { public Vector2 at; public int facing, combo; public float born; public bool heavy, enemy, impact; }
        readonly List<Stroke> strokes = new List<Stroke>();
        float clock, warningReach, warningProgress; int warningFacing; Vector2 warningFoot;
        bool warning, low;
        public int SlashCount { get; private set; }
        public int ImpactCount { get; private set; }
        public bool WarningVisible => warning;
        protected override void Awake() { base.Awake(); raycastTarget = false; }
        public void Slash(Vector2 foot, int facing, bool heavy, int combo, bool enemy, float now)
        {
            SlashCount++; Add(new Stroke { at = foot + Vector2.up * 145, facing = facing, heavy = heavy, combo = combo, enemy = enemy, born = now });
        }
        public void Impact(Vector2 foot, float now)
        { ImpactCount++; Add(new Stroke { at = foot + Vector2.up * 155, impact = true, born = now }); }
        void Add(Stroke stroke) { if (strokes.Count >= 16) strokes.RemoveAt(0); strokes.Add(stroke); SetVerticesDirty(); }
        public void Sample(float now, bool reduced, bool windup, Vector2 foot, int facing, float reach, float progress)
        {
            clock = now; low = reduced; warning = windup; warningFoot = foot; warningFacing = facing;
            warningReach = reach; warningProgress = Mathf.Clamp01(progress);
            for (int i = strokes.Count - 1; i >= 0; i--) if (clock - strokes[i].born > .24f) strokes.RemoveAt(i);
            SetVerticesDirty();
        }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (warning)
            {
                var start = warningFoot + Vector2.up * 5;
                var end = start + Vector2.right * warningFacing * warningReach;
                Line(mesh, start, end, 15, new Color(.67f, .17f, .08f, .18f));
                Line(mesh, start, Vector2.Lerp(start, end, warningProgress), 3, new Color(1, .67f, .26f, .85f));
                Line(mesh, end + Vector2.down * 9, end + Vector2.up * 9, 3, new Color(.83f, .29f, .08f, .8f));
            }
            foreach (var s in strokes)
            {
                float t = Mathf.Clamp01((clock - s.born) / .24f), a = 1 - t;
                if (s.impact)
                {
                    float size = low ? 22 : 12 + t * 32;
                    for (int j = 0; j < 6; j++)
                    {
                        float angle = j * Mathf.PI / 3;
                        var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                        Line(mesh, s.at + dir * 5, s.at + dir * size, 3 * a + .5f, new Color(1, .98f, .76f, a));
                    }
                    continue;
                }
                float radius = s.heavy ? 225 : 190;
                float tilt = s.combo == 2 ? -28 : s.combo == 3 ? 27 : 0;
                Color outer = s.enemy ? new Color(1, .40f, .14f, .55f * a) : new Color(.64f, .92f, .95f, .45f * a);
                Color core = new Color(1, .97f, .78f, .93f * a);
                for (int j = 0; j < 30; j++)
                {
                    float p = j / 30f, q = (j + 1) / 30f;
                    float angleA = Mathf.Lerp(-66, 66, p) + tilt, angleB = Mathf.Lerp(-66, 66, q) + tilt;
                    var v1 = s.at + new Vector2(s.facing * Mathf.Cos(angleA * Mathf.Deg2Rad), Mathf.Sin(angleA * Mathf.Deg2Rad)) * radius;
                    var v2 = s.at + new Vector2(s.facing * Mathf.Cos(angleB * Mathf.Deg2Rad), Mathf.Sin(angleB * Mathf.Deg2Rad)) * radius;
                    float width = Mathf.Sin((p + .02f) * Mathf.PI) * (s.heavy ? 19 : 11);
                    Line(mesh, v1, v2, width + 9, outer); Line(mesh, v1, v2, Mathf.Max(1, width * .34f), core);
                }
            }
        }
        void Line(VertexHelper v, Vector2 a, Vector2 b, float width, Color tint)
        {
            var d = b - a; if (d.sqrMagnitude < .0001f) return;
            var n = new Vector2(-d.y, d.x).normalized * width * .5f;
            int k = v.currentVertCount;
            v.AddVert(a - n, tint * color, Vector2.zero); v.AddVert(a + n, tint * color, Vector2.zero);
            v.AddVert(b + n, tint * color, Vector2.zero); v.AddVert(b - n, tint * color, Vector2.zero);
            v.AddTriangle(k, k + 1, k + 2); v.AddTriangle(k, k + 2, k + 3);
        }
        protected override void OnDisable() { strokes.Clear(); warning = false; base.OnDisable(); }
    }
}
