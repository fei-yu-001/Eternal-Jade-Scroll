using System;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // Local, maskable HUD jade. Decorative light only: no current HP/SP value or resource fill is invented.
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class GuofengHudStreamer : MaskableGraphic
    {
        public enum Shape { Capacity, CaptionMist }
        const int CurveSteps = 56;
        static readonly float[] Across = { -1f, -.86f, -.60f, 0f, .60f, .86f, 1f };
        static readonly float[] Coverage = { 0f, .20f, .88f, 1f, .88f, .20f, 0f };
        Shape shape;
        Color stone;
        Func<bool> reduceMotion;
        float cloudLight, nextLightUpdate;
        public bool LightAnimating => shape == Shape.Capacity && !(reduceMotion?.Invoke() ?? true);

        // Read-only discriminator for engine evidence; actual geometry/alpha comes from CanvasRenderer.GetMesh().
        public Shape GeometryShape => shape;

        public void Configure(Shape value, Color tint, Func<bool> lowMotion = null)
        {
            shape = value;
            stone = tint; reduceMotion = lowMotion; cloudLight = 0; nextLightUpdate = 0;
            raycastTarget = false;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (rectTransform.rect.width <= 0f || rectTransform.rect.height <= 0f) return;
            if (shape == Shape.CaptionMist)
            {
                // A softly curved lens with transparent perimeter; never a rectangular quad.
                Band(vh, 0f, .53f, .28f, .45f, .72f, .57f, 1f, .45f,
                    .48f, .44f, .83f, new Color(stone.r, stone.g, stone.b, .82f));
                return;
            }

            var r = rectTransform.rect;
            // A narrow carved socket with a translucent wax-jade face, not a full identity backdrop.
            JadeLayer(vh, new Rect(r.xMin+1,r.yMin+1,r.width-12,r.height-4), r.height*.46f,
                new Color(.25f,.36f,.29f,.30f), new Color(.25f,.36f,.29f,0));
            JadeLayer(vh, new Rect(r.xMin+2,r.yMin+4,r.width-14,r.height-8), r.height*.42f,
                new Color(.74f,.86f,.74f,.82f), new Color(.39f,.55f,.43f,.72f));
            JadeLayer(vh, new Rect(r.xMin+5,r.yMin+7,r.width-20,r.height-14), r.height*.35f,
                new Color(.97f,.96f,.84f,.83f), new Color(.81f,.88f,.75f,.68f));
            // Engraving is a constant accent, not a percentage or a falsely full health bar.
            Band(vh,.06f,.84f,.30f,.87f,.72f,.85f,.94f,.64f,.028f,.014f,.87f,new Color(stone.r,stone.g,stone.b,.72f));
            Band(vh,.05f,.21f,.31f,.12f,.75f,.16f,.96f,.31f,.035f,.012f,.90f,new Color(.99f,1,.91f,.78f));
            // Feathered cloud light leaves the world visible around the moulding and at the curled tip.
            Band(vh,.18f,.15f,.47f,-.18f,.75f,.02f,.99f,.25f,.075f,.014f,.76f,new Color(.76f,.96f,.89f,.18f+cloudLight));
            Band(vh,.64f,.18f,.83f,-.20f,.83f,.33f,1f,.08f,.035f,.006f,.75f,new Color(.88f,1,.94f,.32f+cloudLight));
        }
        void Update()
        {
            if (shape != Shape.Capacity) return;
            if (reduceMotion?.Invoke() ?? true)
            { if (cloudLight != 0) { cloudLight=0; SetVerticesDirty(); } return; }
            if (Time.unscaledTime < nextLightUpdate) return;
            nextLightUpdate = Time.unscaledTime + .08f;
            float value=.018f*(1+Mathf.Sin(Time.unscaledTime*.55f));
            if(Mathf.Abs(value-cloudLight)>.0003f){cloudLight=value;SetVerticesDirty();}
        }
        protected override void OnDisable() { base.OnDisable(); cloudLight=0; nextLightUpdate=0; }
        static void JadeLayer(VertexHelper vh,Rect r,float radius,Color centre,Color edge)
        {
            float q=Mathf.Min(radius,Mathf.Min(r.width,r.height)*.5f);int n=vh.currentVertCount;
            vh.AddVert(r.center,centre,Vector2.zero);
            for(int corner=0;corner<4;corner++)for(int i=0;i<=12;i++)
            {
                float angle=(corner*90+i*7.5f)*Mathf.Deg2Rad;
                var c=new Vector2(corner==0||corner==3?r.xMax-q:r.xMin+q,corner<2?r.yMax-q:r.yMin+q);
                vh.AddVert(c+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*q,edge,Vector2.zero);
            }
            for(int i=0;i<52;i++)vh.AddTriangle(n,n+i+1,n+1+(i+1)%52);
        }

        Vector2 Point(float x, float y)
        {
            var rect = rectTransform.rect;
            return new Vector2(rect.xMin + x * rect.width, rect.yMax - y * rect.height);
        }

        void Band(VertexHelper vh, float x0, float y0, float x1, float y1,
            float x2, float y2, float x3, float y3, float headWidth, float tailWidth,
            float fadeFrom, Color tint)
        {
            var p0 = Point(x0, y0); var p1 = Point(x1, y1);
            var p2 = Point(x2, y2); var p3 = Point(x3, y3);
            int first = vh.currentVertCount;
            for (int i = 0; i <= CurveSteps; i++)
            {
                float t = i / (float)CurveSteps, u = 1f - t;
                var centre = u * u * u * p0 + 3f * u * u * t * p1 +
                    3f * u * t * t * p2 + t * t * t * p3;
                var tangent = 3f * u * u * (p1 - p0) + 6f * u * t * (p2 - p1) +
                    3f * t * t * (p3 - p2);
                var normal = new Vector2(-tangent.y, tangent.x).normalized;
                float envelope = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * t)), .28f);
                float radius = Mathf.Lerp(headWidth, tailWidth, t) * rectTransform.rect.height * envelope;
                float opacity = Mathf.SmoothStep(0f, 1f, t / .065f) *
                    (1f - Mathf.SmoothStep(0f, 1f, (t - fadeFrom) / (1f - fadeFrom)));
                for (int row = 0; row < Across.Length; row++)
                {
                    var vertexTint = tint;
                    vertexTint.a *= Coverage[row] * opacity;
                    vh.AddVert(centre + normal * (Across[row] * radius), vertexTint, Vector2.zero);
                }
                if (i == 0) continue;
                int previous = first + (i - 1) * Across.Length;
                int current = first + i * Across.Length;
                for (int row = 0; row < Across.Length - 1; row++)
                {
                    vh.AddTriangle(previous + row, previous + row + 1, current + row + 1);
                    vh.AddTriangle(previous + row, current + row + 1, current + row);
                }
            }
        }
    }
}
