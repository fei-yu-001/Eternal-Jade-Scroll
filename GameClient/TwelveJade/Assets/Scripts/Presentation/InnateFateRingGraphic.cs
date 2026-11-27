using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // Thin ink/gold geometry, sampled by the presentation clock. No texture generation or Time dependency.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class InnateFateRingGraphic : MaskableGraphic
    {
        public bool Node, Revealed;
        public Color Accent = UiKit.Hex("BEA56F");
        float time = 3.8f;
        bool reduced, roots;
        public void Sample(float seconds, bool lowMotion, bool hasRoots)
        {
            if (time == seconds && reduced == lowMotion && roots == hasRoots) return;
            time = seconds; reduced = lowMotion; roots = hasRoots; SetVerticesDirty();
        }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); var rect = rectTransform.rect;
            if (Node)
            {
                var c = rect.center; float radius = Mathf.Min(rect.width, rect.height) * .48f;
                Disc(mesh, c, radius, UiKit.Hex(Revealed ? "284840" : "1B332E"));
                Arc(mesh, c, radius, 1.4f, 0, 360, WithAlpha(Accent, .92f));
                Arc(mesh, c, radius - 6, .7f, 0, 360, WithAlpha(Accent, .58f));
                return;
            }
            Vector2 centre = new Vector2(rect.center.x, rect.yMax - 368);
            float arrival = reduced ? 1 : Mathf.SmoothStep(0, 1, Mathf.Clamp01(time / 1.3f));
            Arc(mesh, centre, 304, 1.1f, 0, 360 * arrival, WithAlpha(Accent, .40f));
            Arc(mesh, centre, 282, .6f, 0, 360 * arrival, WithAlpha(Accent, .24f));
            Arc(mesh, centre, 323, .6f, 0, 360 * arrival, WithAlpha(Accent, .18f));
            for (int i = 0; i < 60; i++)
            {
                float angle = i * 6 * Mathf.Deg2Rad;
                Vector2 ray = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Stroke(mesh, centre + ray * 310, centre + ray * (i % 5 == 0 ? 320 : 314), .8f, WithAlpha(Accent, .38f * arrival));
            }
            float light = roots ? .65f : .15f;
            Arc(mesh, centre, 305, 2.2f, -8, 44, WithAlpha(UiKit.Hex("91C7C8"), light * arrival));
            Arc(mesh, centre, 305, 2.2f, -44, -8, WithAlpha(UiKit.Hex("CF906F"), light * arrival));
            // Deliberate inward motes during reveal, then a still composition.
            if (!reduced && time > .2f && time < 3.1f)
            {
                float phase = Mathf.Clamp01((time - .2f) / 2.9f);
                float alpha = Mathf.Sin(phase * Mathf.PI) * .6f;
                for (int i = 0; i < 18; i++)
                {
                    float a = (i * 137.5f + phase * 12) * Mathf.Deg2Rad;
                    float radius = 390 - phase * (75 + i % 3 * 18);
                    Disc(mesh, centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, i % 3 == 0 ? 2 : 1, WithAlpha(Accent, alpha));
                }
            }
        }
        static Color WithAlpha(Color c, float alpha) { c.a = alpha; return c; }
        static void Disc(VertexHelper mesh, Vector2 c, float radius, Color tint)
        {
            int start = mesh.currentVertCount; mesh.AddVert(c, tint, Vector2.zero);
            const int count = 48;
            for (int i = 0; i <= count; i++)
            {
                float a = i * Mathf.PI * 2 / count;
                mesh.AddVert(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, tint, Vector2.zero);
                if (i > 0) mesh.AddTriangle(start, start + i, start + i + 1);
            }
        }
        static void Arc(VertexHelper mesh, Vector2 c, float radius, float width, float from, float to, Color tint)
        {
            int segments = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(to - from) / 3));
            for (int i = 0; i < segments; i++)
            {
                float a = Mathf.Lerp(from, to, (float)i / segments) * Mathf.Deg2Rad;
                float b = Mathf.Lerp(from, to, (float)(i + 1) / segments) * Mathf.Deg2Rad;
                Stroke(mesh, c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, c + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radius, width, tint);
            }
        }
        static void Stroke(VertexHelper mesh, Vector2 a, Vector2 b, float width, Color color)
        {
            Vector2 dir = (b - a).normalized; Vector2 n = new Vector2(-dir.y, dir.x) * width * .5f;
            int k = mesh.currentVertCount;
            mesh.AddVert(a - n, color, Vector2.zero); mesh.AddVert(a + n, color, Vector2.zero);
            mesh.AddVert(b + n, color, Vector2.zero); mesh.AddVert(b - n, color, Vector2.zero);
            mesh.AddTriangle(k, k + 1, k + 2); mesh.AddTriangle(k, k + 2, k + 3);
        }
    }
}
