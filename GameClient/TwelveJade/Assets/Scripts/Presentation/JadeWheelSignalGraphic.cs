using UnityEngine;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class JadeWheelSignalGraphic : MaskableGraphic
    {
        public enum Shape { Pointer, Focus, Halo, Caption, BakedTextCover, ClipCircle }
        Shape shape; float strength;
        public void Configure(Shape value, float alpha) { shape=value; strength=alpha; raycastTarget=false; SetVerticesDirty(); }
        public void SetStrength(float value) { value=Mathf.Clamp01(value);if(Mathf.Abs(value-strength)<.001f)return;strength=value;SetVerticesDirty(); }
        protected override void OnPopulateMesh(VertexHelper v)
        {
            v.Clear();var r=rectTransform.rect;if(r.width<=0||r.height<=0||strength<=0)return;
            var gold=new Color(.94f,.78f,.38f,strength);
            if(shape==Shape.ClipCircle){Disc(v,r.center,Mathf.Min(r.width,r.height)*.5f,Color.white);return;}
            if(shape==Shape.Caption||shape==Shape.BakedTextCover)
            {
                bool caption=shape==Shape.Caption;
                SoftJadePlate(v,r,new Color(.065f,.18f,.145f,strength*(caption?.91f:1f)),caption);
                return;
            }
            if(shape==Shape.Focus)
            {
                // The selected jade needs a visible aureole beyond the caption, not a one-pixel hidden rim.
                float radius=Mathf.Min(r.width,r.height)*.45f;
                for(int band=4;band>=0;band--)
                    Ring(v,r.center,radius+band*.7f,2.8f,new Color(1,.74f,.24f,strength*(.08f-.012f*band)));
                Ring(v,r.center,radius,2.3f,new Color(1,.86f,.45f,strength*.96f));
                Ring(v,r.center,radius-3.3f,.9f,new Color(1,.96f,.75f,strength*.72f));
                return;
            }
            if(shape==Shape.Pointer)
            {
                var c=new Vector2(r.center.x,r.yMax-r.height*.27f);float radius=r.width*.20f;
                Disc(v,c,radius*1.55f,new Color(1,.87f,.42f,.12f*strength));Disc(v,c,radius,gold);Disc(v,c+new Vector2(-radius*.18f,radius*.20f),radius*.55f,new Color(1,.98f,.78f,strength));
                Triangle(v,new Vector2(c.x-r.width*.18f,c.y-radius*1.7f),new Vector2(c.x+r.width*.18f,c.y-radius*1.7f),new Vector2(c.x,r.yMin+r.height*.08f),gold);
            }
            else
            {
                float radius=Mathf.Min(r.width,r.height)*.46f;
                for(int band=0;band<(shape==Shape.Halo?5:2);band++)
                {
                    float ring=radius-band*(shape==Shape.Halo?3.5f:2.5f);var tint=gold;tint.a*=shape==Shape.Halo?.10f:(band==0?.72f:.25f);
                    Ring(v,r.center,ring,shape==Shape.Halo?3.8f:1.2f,tint);
                }
            }
        }
        static Vector2 RoundedPoint(float angle,Vector2 radius,float exponent)
        {
            float x=Mathf.Cos(angle),y=Mathf.Sin(angle),power=2f/exponent;
            return new Vector2(Mathf.Sign(x)*Mathf.Pow(Mathf.Abs(x),power)*radius.x,Mathf.Sign(y)*Mathf.Pow(Mathf.Abs(y),power)*radius.y);
        }
        static void SoftJadePlate(VertexHelper v,Rect r,Color ink,bool caption)
        {
            const int segments=64;float feather=caption?2.2f:2.8f;
            var outer=r.size*.5f;var inner=new Vector2(Mathf.Max(.1f,outer.x-feather),Mathf.Max(.1f,outer.y-feather));
            float exponent=caption?2.7f:4f;var clear=ink;clear.a=0;
            for(int i=0;i<segments;i++)
            {
                float a=i*Mathf.PI*2/segments,b=(i+1)*Mathf.PI*2/segments;
                var p=r.center+RoundedPoint(a,inner,exponent);var q=r.center+RoundedPoint(b,inner,exponent);
                var x=r.center+RoundedPoint(a,outer,exponent);var y=r.center+RoundedPoint(b,outer,exponent);
                Triangle(v,r.center,p,q,ink);
                int n=v.currentVertCount;v.AddVert(p,ink,Vector2.zero);v.AddVert(x,clear,Vector2.zero);v.AddVert(y,clear,Vector2.zero);v.AddVert(q,ink,Vector2.zero);v.AddTriangle(n,n+1,n+2);v.AddTriangle(n,n+2,n+3);
                if(caption)
                {
                    var rim=new Color(.77f,.79f,.52f,ink.a*.36f);var rimSize=inner-new Vector2(.7f,.7f);
                    var u=r.center+RoundedPoint(a,rimSize,exponent);var z=r.center+RoundedPoint(b,rimSize,exponent);
                    Triangle(v,p,q,z,rim);Triangle(v,p,z,u,rim);
                }
            }
        }
        static void Triangle(VertexHelper v,Vector2 a,Vector2 b,Vector2 c,Color color)
        {int n=v.currentVertCount;v.AddVert(a,color,Vector2.zero);v.AddVert(b,color,Vector2.zero);v.AddVert(c,color,Vector2.zero);v.AddTriangle(n,n+1,n+2);}
        static void Disc(VertexHelper v,Vector2 c,float r,Color color)
        {for(int i=0;i<48;i++){float a=i*Mathf.PI/24,b=(i+1)*Mathf.PI/24;Triangle(v,c,c+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*r,c+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*r,color);}}
        static void Ring(VertexHelper v,Vector2 c,float radius,float width,Color color)
        {
            for(int i=0;i<72;i++){float a=i*Mathf.PI/36,b=(i+1)*Mathf.PI/36;var da=new Vector2(Mathf.Cos(a),Mathf.Sin(a));var db=new Vector2(Mathf.Cos(b),Mathf.Sin(b));
                var p=c+da*(radius-width*.5f);var q=c+da*(radius+width*.5f);var r=c+db*(radius+width*.5f);var s=c+db*(radius-width*.5f);Triangle(v,p,q,r,color);Triangle(v,p,r,s,color);}
        }
    }
}
