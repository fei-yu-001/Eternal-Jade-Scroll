using System;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // Read-only HUD signal. Static mesh; only CanvasRenderer alpha changes during breathing.
    // Owns no textures/materials, event subscriptions, coroutines or save references.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class QuestGoldenMarker : MaskableGraphic
    {
        [Flags]
        public enum Notice { None=0, AvailableQuest=1, UnreadClue=2, CanAdvance=4 }
        public const float BreathSeconds = 1.5f;
        Func<bool> reduceMotion;
        Notice state;
        bool reduced = true;
        float phase, visibleAlpha = .58f;
        public Notice State => state;
        public bool HasNotice => state != Notice.None;
        public bool IsAnimating => HasNotice && !reduced && isActiveAndEnabled;
        public float PhaseSeconds => phase;
        public float VisibleAlpha => visibleAlpha;

        public void Configure(Func<bool> reducedMotion)
        {
            reduceMotion = reducedMotion; raycastTarget = false;
            reduced = reduceMotion?.Invoke() ?? true;
            phase = 0; ApplyAlpha(); SetVerticesDirty();
        }
        public void SetNotice(Notice value)
        {
            if (state == value) return;
            state = value; phase = 0;
            reduced = reduceMotion?.Invoke() ?? true;
            ApplyAlpha(); SetVerticesDirty();
        }
        protected override void Awake() { base.Awake(); raycastTarget = false; }
        protected override void OnEnable()
        {
            base.OnEnable(); raycastTarget = false; phase = 0;
            reduced = reduceMotion?.Invoke() ?? true; ApplyAlpha();
        }
        void Update()
        {
            bool nextReduced = reduceMotion?.Invoke() ?? true;
            if (nextReduced != reduced) { reduced = nextReduced; phase = 0; }
            if (HasNotice && !reduced)
                phase = Mathf.Repeat(phase + Mathf.Max(0, Time.unscaledDeltaTime), BreathSeconds);
            ApplyAlpha();
        }
        void ApplyAlpha()
        {
            float alpha = !HasNotice ? .58f : reduced ? 1f :
                .82f + .18f * (.5f - .5f * Mathf.Cos(phase * Mathf.PI * 2 / BreathSeconds));
            visibleAlpha = alpha;
            canvasRenderer.SetAlpha(alpha);
        }
        protected override void OnDisable()
        {
            phase = 0; visibleAlpha = 0; canvasRenderer.SetAlpha(0);
            base.OnDisable();
        }
        protected override void OnDestroy()
        {
            reduceMotion = null;
            base.OnDestroy();
        }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (rectTransform.rect.width <= 0 || rectTransform.rect.height <= 0) return;
            var gold = new Color(.85f,.65f,.29f,1);
            var light = new Color(1,.88f,.55f,.95f);
            var bronze = new Color(.40f,.27f,.11f,1);
            if (HasNotice) Halo(mesh);
            var rim = new[] { new Vector2(16,5),new Vector2(32,5),new Vector2(40,13),new Vector2(40,35),
                new Vector2(32,43),new Vector2(16,43),new Vector2(8,35),new Vector2(8,13) };
            Polygon(mesh,rim,new Color(.21f,.28f,.21f,.97f));
            for (int i=0;i<rim.Length;i++)
            {
                var a=rim[i];var b=rim[(i+1)%rim.Length];
                Line(mesh,a,b,2.2f,bronze);Line(mesh,a,b,1.15f,gold);
                Line(mesh,Vector2.Lerp(new Vector2(24,24),a,.89f),Vector2.Lerp(new Vector2(24,24),b,.89f),.55f,light);
            }
            // Hand-drawn question, independent of fonts: curved head, hooked stem, diamond stop.
            Question(mesh,4.5f,bronze,Vector2.zero);
            Question(mesh,2.8f,gold,Vector2.zero);
            Question(mesh,.85f,light,new Vector2(-.35f,.35f));
            Polygon(mesh,new[]{new Vector2(24,8.7f),new Vector2(26.5f,11.2f),new Vector2(24,13.7f),new Vector2(21.5f,11.2f)},gold);
            Line(mesh,new Vector2(23,11.3f),new Vector2(24,12.3f),.8f,light);
            // Fine seal-script side cuts, kept clear of the question's silhouette.
            for(int i=0;i<3;i++)
            {
                float y=20+i*4;
                Line(mesh,new Vector2(11.5f,y),new Vector2(13.5f,y+1),.7f,gold);
                Line(mesh,new Vector2(35,y+1),new Vector2(37,y),.7f,gold);
            }
            Line(mesh,new Vector2(21,40),new Vector2(27,40),.7f,light);
        }
        Vector2 Point(Vector2 p)
        {
            var r=rectTransform.rect;
            return new Vector2(r.xMin+p.x*r.width/48,r.yMin+p.y*r.height/48);
        }
        void Line(VertexHelper mesh,Vector2 a,Vector2 b,float width,Color tint)
        {
            a=Point(a);b=Point(b);
            float scale=Mathf.Min(rectTransform.rect.width,rectTransform.rect.height)/48;
            var n=new Vector2(-(b-a).y,(b-a).x).normalized*(width*scale*.5f);
            int first=mesh.currentVertCount;
            mesh.AddVert(a-n,tint,Vector2.zero);mesh.AddVert(a+n,tint,Vector2.zero);
            mesh.AddVert(b+n,tint,Vector2.zero);mesh.AddVert(b-n,tint,Vector2.zero);
            mesh.AddTriangle(first,first+1,first+2);mesh.AddTriangle(first,first+2,first+3);
        }
        void Polygon(VertexHelper mesh,Vector2[] points,Color tint)
        {
            int first=mesh.currentVertCount;
            foreach(var p in points)mesh.AddVert(Point(p),tint,Vector2.zero);
            for(int i=1;i<points.Length-1;i++)mesh.AddTriangle(first,first+i,first+i+1);
        }
        void Halo(VertexHelper mesh)
        {
            int first=mesh.currentVertCount;
            mesh.AddVert(Point(new Vector2(24,24)),new Color(.97f,.76f,.32f,.27f),Vector2.zero);
            for(int i=0;i<=40;i++)
            {
                float angle=i*Mathf.PI*2/40;
                mesh.AddVert(Point(new Vector2(24+Mathf.Cos(angle)*24,24+Mathf.Sin(angle)*24)),new Color(.97f,.76f,.32f,0),Vector2.zero);
                if(i>0)mesh.AddTriangle(first,first+i,first+i+1);
            }
        }
        void Question(VertexHelper mesh,float width,Color tint,Vector2 offset)
        {
            Curve(mesh,new Vector2(16,31)+offset,new Vector2(16,38)+offset,new Vector2(32,38)+offset,new Vector2(32,31)+offset,width,tint);
            Curve(mesh,new Vector2(32,31)+offset,new Vector2(32,25)+offset,new Vector2(24,25)+offset,new Vector2(24,18.5f)+offset,width,tint);
            Line(mesh,new Vector2(24,18.5f)+offset,new Vector2(24,17)+offset,width,tint);
        }
        void Curve(VertexHelper mesh,Vector2 a,Vector2 b,Vector2 c,Vector2 d,float width,Color tint)
        {
            var previous=a;
            for(int i=1;i<=18;i++)
            {
                float t=i/18f,u=1-t;
                var point=u*u*u*a+3*u*u*t*b+3*u*t*t*c+t*t*t*d;
                Line(mesh,previous,point,width,tint);previous=point;
            }
        }
    }
}
