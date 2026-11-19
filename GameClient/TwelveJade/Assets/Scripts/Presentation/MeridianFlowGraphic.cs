using System;
using UnityEngine;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    [RequireComponent(typeof(RectTransform),typeof(CanvasRenderer))]
    public sealed class MeridianFlowGraphic : MaskableGraphic
    {
        public static readonly Vector2[] Points={new Vector2(1030,300),new Vector2(1110,410),new Vector2(1140,530),new Vector2(1110,650),new Vector2(1030,770)};
        Vector2[] anchors=Points;
        public int PointCount=>anchors.Length;
        public Vector2 PointAt(int index)=>anchors[index];
        Func<bool> reduced;int opened,selected;float phase,nextTick,pulseAt=-100;bool success;
        public bool Busy => Time.unscaledTime-pulseAt < .9f;
        public void Configure(Func<bool> lowMotion,Vector2[] pointAnchors=null)
        {
            if(pointAnchors!=null&&pointAnchors.Length!=Points.Length)throw new ArgumentException("Meridian sequence requires five anchors.",nameof(pointAnchors));
            anchors=pointAnchors==null?Points:(Vector2[])pointAnchors.Clone();
            reduced=lowMotion;raycastTarget=false;SetVerticesDirty();
        }
        public void State(int lit,int focus) {opened=lit;selected=focus;SetVerticesDirty();}
        public void Result(bool passed) {success=passed;pulseAt=Time.unscaledTime;SetVerticesDirty();}
        void Update()
        {
            if(Time.unscaledTime<nextTick)return;nextTick=Time.unscaledTime+1f/30f;
            if(pulseAt>-99 && Time.unscaledTime-pulseAt>=1.5f){pulseAt=-100;SetVerticesDirty();}
            float next=(reduced?.Invoke()??true)?0:Time.unscaledTime;
            if(next!=phase||Time.unscaledTime-pulseAt<1.5f){phase=next;SetVerticesDirty();}
        }
        protected override void OnDisable(){pulseAt=-100;phase=0;base.OnDisable();}
        protected override void OnDestroy(){reduced=null;base.OnDestroy();}
        Vector2 P(Vector2 p)=>new Vector2(rectTransform.rect.xMin+p.x,rectTransform.rect.yMax-p.y);
        void Line(VertexHelper vh,Vector2 a,Vector2 b,float width,Color ink)
        {
            a=P(a);b=P(b);var n=new Vector2(-(b-a).y,(b-a).x).normalized*width*.5f;int start=vh.currentVertCount;
            vh.AddVert(a-n,ink,Vector2.zero);vh.AddVert(a+n,ink,Vector2.zero);vh.AddVert(b+n,ink,Vector2.zero);vh.AddVert(b-n,ink,Vector2.zero);
            vh.AddTriangle(start,start+1,start+2);vh.AddTriangle(start,start+2,start+3);
        }
        void Glow(VertexHelper vh,Vector2 at,float radius,Color ink)
        {
            int start=vh.currentVertCount;vh.AddVert(P(at),ink,Vector2.zero);var edge=ink;edge.a=0;
            for(int i=0;i<=32;i++){float a=i*Mathf.PI/16;vh.AddVert(P(at+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius),edge,Vector2.zero);}
            for(int i=0;i<32;i++)vh.AddTriangle(start,start+i+1,start+i+2);
        }
        void Ring(VertexHelper vh,Vector2 at,float radius,Color ink)
        {
            for(int i=0;i<48;i++){float a=i*Mathf.PI/24,b=(i+1)*Mathf.PI/24;Line(vh,at+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius,at+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*radius,1.4f,ink);}
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();var gold=new Color(.80f,.53f,.16f,.8f)*color;var blue=new Color(.52f,.67f,.40f,.6f)*color;
            for(int i=0;i<anchors.Length-1;i++)
            {
                var ink=i<opened?gold:blue;ink.a*=.45f;Line(vh,anchors[i],anchors[i+1],1.1f,ink);
                if(phase>0)
                {
                    float t=Mathf.Repeat(phase*.36f+i*.23f,1);
                    Glow(vh,Vector2.Lerp(anchors[i+1],anchors[i],t),12,i<opened?gold:blue);
                }
            }
            for(int i=0;i<anchors.Length;i++)
            {
                var ink=i<opened?gold:blue;
                Glow(vh,anchors[i],i==selected?30:15,ink);
                if(i==selected)Ring(vh,anchors[i],22+(phase>0?Mathf.Sin(phase*1.5f)*2:0),gold);
            }
            float elapsed=Time.unscaledTime-pulseAt;
            if(elapsed<1.5f)
            {
                var ink=success?gold:blue;ink.a*=Mathf.Clamp01(1-elapsed/1.5f);
                bool low=reduced?.Invoke()??true;
                Glow(vh,anchors[Mathf.Clamp(selected,0,4)],low?42:80+elapsed*130,ink);
                if(!low)Ring(vh,anchors[Mathf.Clamp(selected,0,4)],30+elapsed*155,ink);
            }
        }
    }
}
