using System;
using UnityEngine;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    // The body is authored artwork. This mesh draws only qi, point lights and the committed result.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MeridianInnerGraphic : MaskableGraphic
    {
        // Pixel calibration in the 620 x 665 crop of meridian-bg; shared with the offline preview.
        public static readonly Vector2[] AuthoredNodes={new Vector2(310f/620,211f/665),new Vector2(310f/620,253f/665),
            new Vector2(310f/620,325f/665),new Vector2(310f/620,462f/665),new Vector2(315f/620,573f/665)};
        public static readonly Vector2 AuthoredDantian=new Vector2(310f/620,503f/665);
        public const float ChargeDuration=.38f,TravelDuration=1.22f,ImpactTime=ChargeDuration+TravelDuration,Duration=2.65f;
        Func<bool> low;float next,resultAt=-100;int opened,selected;bool success;
        Vector2[] nodes=new Vector2[5];Vector2 dantian;
        bool Reduced=>low?.Invoke()??true;
        float Elapsed=>Time.unscaledTime-resultAt;
        public bool Busy=>resultAt>=0&&Elapsed<(Reduced?.55f:Duration);
        public Vector2 Tremor
        {
            get
            {
                float t=Elapsed-ImpactTime;
                if(Reduced||!Busy||t<0||t>.5f)return Vector2.zero;
                float strength=(1-t/.5f)*(success?3.2f:1.3f);
                return new Vector2(Mathf.Sin(t*83),Mathf.Cos(t*61))*strength;
            }
        }
        public void Configure(Func<bool> reduced,Rect? artRect=null,Vector2[] normalizedNodes=null)
        {
            low=reduced;raycastTarget=false;var r=artRect??new Rect(0,41.129f,520,557.742f);
            var source=normalizedNodes!=null&&normalizedNodes.Length==5?normalizedNodes:AuthoredNodes;
            for(int i=0;i<nodes.Length;i++)nodes[i]=new Vector2(r.x+source[i].x*r.width,r.y+source[i].y*r.height);
            dantian=new Vector2(r.x+AuthoredDantian.x*r.width,r.y+AuthoredDantian.y*r.height);SetVerticesDirty();
        }
        public Vector2 Node(int at)=>nodes[Mathf.Clamp(at,0,4)];
        public void State(int lit,int at){opened=Mathf.Clamp(lit,0,5);selected=Mathf.Clamp(at,0,4);SetVerticesDirty();}
        public void Result(bool passed,int target){success=passed;selected=Mathf.Clamp(target,0,4);resultAt=Time.unscaledTime;SetVerticesDirty();}
        void Update()
        {
            if(Time.unscaledTime<next)return;next=Time.unscaledTime+(Reduced?.2f:1f/30);
            if(resultAt>=0&&!Busy)resultAt=-100;
            SetVerticesDirty();
        }
        Vector2 P(Vector2 p){var r=rectTransform.rect;return new Vector2(r.xMin+p.x*r.width/520,r.yMax-p.y*r.height/640);}
        void Line(VertexHelper v,Vector2 a,Vector2 b,float width,Color ink)
        {
            a=P(a);b=P(b);var normal=new Vector2(-(b-a).y,(b-a).x).normalized*width*.5f;int n=v.currentVertCount;
            v.AddVert(a-normal,ink*color,Vector2.zero);v.AddVert(a+normal,ink*color,Vector2.zero);v.AddVert(b+normal,ink*color,Vector2.zero);v.AddVert(b-normal,ink*color,Vector2.zero);v.AddTriangle(n,n+1,n+2);v.AddTriangle(n,n+2,n+3);
        }
        void Glow(VertexHelper v,Vector2 at,float radius,Color ink)
        {
            int n=v.currentVertCount;v.AddVert(P(at),ink*color,Vector2.zero);var edge=ink*color;edge.a=0;
            for(int i=0;i<=40;i++){float a=i*Mathf.PI/20;v.AddVert(P(at+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius),edge,Vector2.zero);}
            for(int i=0;i<40;i++)v.AddTriangle(n,n+i+1,n+i+2);
        }
        void Ring(VertexHelper v,Vector2 at,float radius,float width,Color ink)
        {for(int i=0;i<64;i++){float a=i*Mathf.PI/32,b=(i+1)*Mathf.PI/32;Line(v,at+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius,at+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*radius,width,ink);}}
        static Vector2 Cubic(Vector2 a,Vector2 b,Vector2 c,Vector2 d,float t)
        {float u=1-t;return u*u*u*a+3*u*u*t*b+3*u*t*t*c+t*t*t*d;}
        Vector2 Channel(float t)
        {
            var target=nodes[selected];var mid=Vector2.Lerp(dantian,target,.52f);float dy=target.y-dantian.y,side=Mathf.Min(34,Mathf.Abs(dy)*.25f);
            return t<.52f?Cubic(dantian,dantian+new Vector2(-side,dy*.13f),mid-new Vector2(side,dy*.12f),mid,t/.52f):
                Cubic(mid,mid+new Vector2(side,dy*.14f),target+new Vector2(side,-dy*.14f),target,(t-.52f)/.48f);
        }
        protected override void OnPopulateMesh(VertexHelper v)
        {
            v.Clear();var gold=new Color(1,.81f,.38f,.94f);var jade=new Color(.55f,.86f,.79f,.72f);
            float elapsed=Elapsed,pulse=Reduced?1:.88f+.12f*Mathf.Sin(Time.unscaledTime*2.4f);
            int shown=Busy&&success&&elapsed<ImpactTime?Mathf.Min(opened,selected):opened;
            for(int i=0;i<nodes.Length;i++)
            {
                var ink=i<shown?gold:jade;ink.a*=i==selected?pulse:.65f;
                Glow(v,nodes[i],i==selected?19:10,ink);Ring(v,nodes[i],i==selected?9:4,1.2f,ink);
            }
            if(!Busy)return;
            var target=nodes[selected];
            if(Reduced)
            {Glow(v,target,30,success?gold:jade);Ring(v,target,21,1.5f,success?gold:jade);return;}
            if(elapsed<ChargeDuration)
            {float t=elapsed/ChargeDuration;Glow(v,dantian,22+26*t,gold);Ring(v,dantian,35-22*t,1.4f,gold);return;}
            if(elapsed<ImpactTime)
            {
                float head=Mathf.SmoothStep(0,1,(elapsed-ChargeDuration)/TravelDuration);
                for(int i=0;i<36;i++)
                {
                    float t=head-i*.008f,back=t-.008f;if(back<0)break;
                    var ink=gold;ink.a*=1-i/36f;var a=Channel(back);var b=Channel(t);
                    var haze=ink;haze.a*=.22f;Line(v,a,b,10,haze);Line(v,a,b,2.6f,ink);
                }
                var at=Channel(head);Glow(v,at,22,gold);Glow(v,at,6,Color.white);
                for(int i=0;i<2;i++){float sign=i==0?-1:1;Line(v,at,at+new Vector2(sign*9,10),1.2f,gold);Line(v,at+new Vector2(sign*9,10),at+new Vector2(sign*6,17),.9f,gold);}
                return;
            }
            float burst=Mathf.Clamp01((elapsed-ImpactTime)/(Duration-ImpactTime));var flash=success?gold:jade;flash.a*=1-burst;
            Glow(v,target,32+burst*(success?90:35),flash);
            for(int j=0;j<3;j++){float radius=12+Mathf.Clamp01(burst+j*.13f)*(success?96:46);Ring(v,target,radius,1.5f-j*.3f,flash);}
            if(success)for(int i=0;i<18;i++)
            {float a=i*Mathf.PI/9+burst*.2f;var dir=new Vector2(Mathf.Cos(a),Mathf.Sin(a));Line(v,target+dir*(18+burst*68),target+dir*(26+burst*108),1.4f,flash);}
        }
    }
}
