using System;
using UnityEngine;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    [RequireComponent(typeof(RectTransform),typeof(CanvasRenderer))]
    public sealed class ArtsCodexGraphic : MaskableGraphic
    {
        public enum Form { Disc, Mandala, Socket, Glyph, Taiji, Progress, Ornament, CommandEdge, Orbit }
        Form form;int glyph;float progress;Color accent=ArtsCodexArt.Bronze;
        Func<bool> reduced;bool emphasized;float clock,nextTick;
        public void Configure(Form mode,Color tint,Func<bool> lowMotion=null,int symbol=0)
        {form=mode;accent=tint;reduced=lowMotion;glyph=symbol;raycastTarget=false;SetVerticesDirty();}
        public void Emphasize(bool active){if(active==emphasized)return;emphasized=active;SetVerticesDirty();}
        public void Value(float value){progress=Mathf.Clamp01(value);SetVerticesDirty();}
        void Update()
        {
            if(Time.unscaledTime<nextTick)return;nextTick=Time.unscaledTime+1f/24f;
            bool move=!(reduced?.Invoke()??true)&&(form==Form.Mandala||form==Form.Orbit||form==Form.Ornament||form==Form.CommandEdge||emphasized);
            float now=move?Time.unscaledTime:0;if(now==clock)return;clock=now;SetVerticesDirty();
        }
        Vector2 P(Vector2 p)=>rectTransform.rect.center+p*Mathf.Min(rectTransform.rect.width,rectTransform.rect.height)/200f;
        Color A(Color c,float alpha){c.a*=alpha;return c;}
        void Vert(VertexHelper vh,Vector2 p,Color c)=>vh.AddVert(P(p),c*color,Vector2.zero);
        void Quad(VertexHelper vh,Vector2 a,Vector2 b,Vector2 c,Vector2 d,Color ink)
        {int n=vh.currentVertCount;Vert(vh,a,ink);Vert(vh,b,ink);Vert(vh,c,ink);Vert(vh,d,ink);vh.AddTriangle(n,n+1,n+2);vh.AddTriangle(n,n+2,n+3);}
        void Line(VertexHelper vh,Vector2 a,Vector2 b,float width,Color ink)
        {var n=new Vector2(-(b-a).y,(b-a).x).normalized*width*.5f;Quad(vh,a-n,a+n,b+n,b-n,ink);}
        Vector2 Polar(float radius,float degrees)=>new Vector2(Mathf.Cos(degrees*Mathf.Deg2Rad),Mathf.Sin(degrees*Mathf.Deg2Rad))*radius;
        void Arc(VertexHelper vh,float radius,float start,float span,float width,Color ink,int steps=96)
        {for(int i=0;i<steps;i++)Line(vh,Polar(radius,start+span*i/steps),Polar(radius,start+span*(i+1)/steps),width,ink);}
        void Disc(VertexHelper vh,Vector2 center,float radius,Color inner,Color outer)
        {
            int n=vh.currentVertCount;Vert(vh,center,inner);
            for(int i=0;i<=64;i++)Vert(vh,center+Polar(radius,i*360f/64),outer);
            for(int i=0;i<64;i++)vh.AddTriangle(n,n+i+1,n+i+2);
        }
        void Path(VertexHelper vh,Color ink,float width,params Vector2[] points)
        {for(int i=1;i<points.Length;i++)Line(vh,points[i-1],points[i],width,ink);}
        void Sword(VertexHelper vh,float x,float y,float angle,float scale)
        {
            Func<float,float,Vector2> p=(a,b)=>new Vector2(x,y)+(Vector2)(Quaternion.Euler(0,0,angle)*new Vector3(a*scale,b*scale));
            Quad(vh,p(0,56),p(-7,34),p(0,-25),p(7,34),A(accent,.95f));Line(vh,p(0,51),p(0,-23),1.5f,ArtsCodexArt.Ivory);
            Line(vh,p(-15,-27),p(15,-27),4,accent);Line(vh,p(0,-27),p(0,-45),5,accent);Disc(vh,p(0,-47),4,accent,A(accent,.15f));
        }
        void Rune(VertexHelper vh)
        {
            Disc(vh,Vector2.zero,72,A(accent,.18f),A(accent,0));
            if(glyph==0||glyph==1){Sword(vh,0,0,glyph==0?-32:22,glyph==0?1:1.15f);Arc(vh,65,20,150,1.6f,A(accent,.7f),40);}
            else if(glyph==2)
            {
                for(int i=0;i<3;i++)
                {float y=25-i*22;for(int j=0;j<25;j++){float t=j/25f,u=(j+1)/25f;Line(vh,new Vector2(-58+t*112,y+Mathf.Sin(t*Mathf.PI*1.5f)*15),new Vector2(-58+u*112,y+Mathf.Sin(u*Mathf.PI*1.5f)*15),4-i*.8f,accent);}}
                Path(vh,ArtsCodexArt.Ivory,2,new Vector2(28,59),new Vector2(49,41),new Vector2(24,23));
            }
            else if(glyph==3){Sword(vh,0,7,0,.94f);Sword(vh,-37,-15,24,.58f);Sword(vh,37,-15,-24,.58f);Arc(vh,65,200,140,1.5f,accent,32);}
            else if(glyph==4)
            {
                for(int i=0;i<5;i++)
                {float a=i*72+18;var at=Polar(62,a);Disc(vh,at,8,A(accent,.8f),A(accent,0));Path(vh,A(accent,.7f),1.5f,at,Polar(45,a-20),Polar(32,a+12));}
                Quad(vh,new Vector2(14,58),new Vector2(-29,-2),new Vector2(4,-2),new Vector2(-12,-59),accent);
                Path(vh,ArtsCodexArt.Ivory,3,new Vector2(13,56),new Vector2(-12,7),new Vector2(21,7),new Vector2(-11,-57));
            }
            else
            {
                for(int petal=-2;petal<=2;petal++)
                {
                    float angle=petal*23;var tip=Polar(62,90+angle);var side=Polar(27,angle);
                    var origin=new Vector2(0,-36);var last=origin;
                    for(int j=1;j<=32;j++)
                    {float t=j/32f;var p=Vector2.Lerp(origin,tip,Mathf.Sin(t*Mathf.PI))+side*Mathf.Sin(t*2*Mathf.PI);Line(vh,last,p,2.2f,A(accent,.9f));last=p;}
                }
                Arc(vh,69,200,140,1.3f,A(accent,.6f),40);Disc(vh,new Vector2(0,-13),10,ArtsCodexArt.Ivory,A(accent,0));
            }
        }
        void Taiji(VertexHelper vh)
        {
            Disc(vh,Vector2.zero,68,ArtsCodexArt.Ivory,ArtsCodexArt.Ivory);
            int n=vh.currentVertCount;Vert(vh,Vector2.zero,ArtsCodexArt.DeepJade);for(int i=0;i<=64;i++)Vert(vh,Polar(68,-90+i*180f/64),ArtsCodexArt.DeepJade);
            for(int i=0;i<64;i++)vh.AddTriangle(n,n+i+1,n+i+2);
            Disc(vh,new Vector2(0,34),34,ArtsCodexArt.DeepJade,ArtsCodexArt.DeepJade);Disc(vh,new Vector2(0,-34),34,ArtsCodexArt.Ivory,ArtsCodexArt.Ivory);
            Disc(vh,new Vector2(0,34),8,ArtsCodexArt.Ivory,ArtsCodexArt.Ivory);Disc(vh,new Vector2(0,-34),8,ArtsCodexArt.DeepJade,ArtsCodexArt.DeepJade);
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();float breathe=clock==0?1:.87f+.13f*Mathf.Sin(clock*Mathf.PI*2/2.5f);
            if(form==Form.Disc){Disc(vh,Vector2.zero,100,Color.white,Color.white);return;}
            if(form==Form.Glyph){Rune(vh);return;}
            if(form==Form.Progress)
            {
                // A woven track with round end beads, not a flat UI fill image.
                float width=rectTransform.rect.width/Mathf.Max(1,rectTransform.rect.height)*100;
                Line(vh,new Vector2(-width+10,0),new Vector2(width-10,0),15,A(accent,.18f));
                Line(vh,new Vector2(-width+10,0),new Vector2(Mathf.Lerp(-width+10,width-10,progress),0),8,accent);
                for(int i=0;i<=10;i++)Disc(vh,new Vector2(Mathf.Lerp(-width+10,width-10,i/10f),0),i/10f<=progress?16:10,i/10f<=progress?ArtsCodexArt.Ivory:A(accent,.45f),A(accent,0));return;
            }
            if(form==Form.CommandEdge)
            {
                var rect=rectTransform.rect;var half=rect.size*.5f;const int n=80;
                for(int i=0;i<n;i++)
                {
                    float a=i*Mathf.PI*2/n,b=(i+1)*Mathf.PI*2/n;
                    Vector2 da=new Vector2(Mathf.Sign(Mathf.Cos(a))*Mathf.Pow(Mathf.Abs(Mathf.Cos(a)),.22f),Mathf.Sign(Mathf.Sin(a))*Mathf.Pow(Mathf.Abs(Mathf.Sin(a)),.22f));
                    Vector2 db=new Vector2(Mathf.Sign(Mathf.Cos(b))*Mathf.Pow(Mathf.Abs(Mathf.Cos(b)),.22f),Mathf.Sign(Mathf.Sin(b))*Mathf.Pow(Mathf.Abs(Mathf.Sin(b)),.22f));
                    var inner=half-new Vector2(2.2f,2.2f);int k=vh.currentVertCount;
                    var tint=A(accent,(emphasized?.40f:.19f)*breathe)*color;var clear=tint;clear.a=0;
                    vh.AddVert(rect.center+Vector2.Scale(da,inner),tint,Vector2.zero);vh.AddVert(rect.center+Vector2.Scale(da,half),clear,Vector2.zero);
                    vh.AddVert(rect.center+Vector2.Scale(db,half),clear,Vector2.zero);vh.AddVert(rect.center+Vector2.Scale(db,inner),tint,Vector2.zero);
                    vh.AddTriangle(k,k+1,k+2);vh.AddTriangle(k,k+2,k+3);
                }
                foreach(int sx in new[]{-1,1})foreach(int sy in new[]{-1,1})
                {
                    var corner=rect.center+new Vector2(sx*(half.x-10),sy*(half.y-8));
                    Vector2[] cloud={new Vector2(-24,0),Vector2.zero,new Vector2(0,-10),new Vector2(-12,-10),new Vector2(-12,-4),new Vector2(-5,-4)};
                    for(int j=1;j<cloud.Length;j++)
                    {
                        var a=corner+Vector2.Scale(cloud[j-1],new Vector2(sx,sy));var b=corner+Vector2.Scale(cloud[j],new Vector2(sx,sy));
                        var normal=new Vector2(-(b-a).y,(b-a).x).normalized*.6f;int k=vh.currentVertCount;var ink=A(accent,.62f*breathe)*color;
                        vh.AddVert(a-normal,ink,Vector2.zero);vh.AddVert(a+normal,ink,Vector2.zero);vh.AddVert(b+normal,ink,Vector2.zero);vh.AddVert(b-normal,ink,Vector2.zero);vh.AddTriangle(k,k+1,k+2);vh.AddTriangle(k,k+2,k+3);
                    }
                }return;
            }
            if(form==Form.Ornament)
            {
                float width=rectTransform.rect.width/Mathf.Max(1,rectTransform.rect.height)*95;
                Line(vh,new Vector2(-width,0),new Vector2(width,0),12,A(accent,(emphasized?.8f:.45f)*breathe));for(int i=-1;i<=1;i++)
                {var p=new Vector2(i*12,0);Path(vh,accent,.9f,p+new Vector2(-4,0),p+new Vector2(0,4),p+new Vector2(4,0),p+new Vector2(0,-4),p+new Vector2(-4,0));}return;
            }
            if(form==Form.Socket||form==Form.Taiji)
            {
                Disc(vh,Vector2.zero,95,A(accent,(emphasized?.34f:.12f)*breathe),A(accent,0));
                Disc(vh,Vector2.zero,75,ArtsCodexArt.DeepJade,new Color(.09f,.16f,.14f,1));
                Arc(vh,77,0,360,2.5f,accent);Arc(vh,72,0,360,.65f,A(ArtsCodexArt.Ivory,.65f));Arc(vh,85,0,360,.8f,A(accent,.65f));
                for(int i=0;i<8;i++){float a=i*45;Line(vh,Polar(79,a),Polar(91,a),1.7f,accent);}
                if(form==Form.Taiji)Taiji(vh);
                if(emphasized)Arc(vh,89,clock*12,65,1.8f,A(ArtsCodexArt.Ivory,.85f),25);return;
            }
            if(form==Form.Orbit)
            {
                foreach(float radius in new[]{41f,46})Arc(vh,radius,0,360,.18f,A(accent,.38f));
                for(int i=0;i<4;i++){float a=clock==0?i*90:clock*4+i*90;Disc(vh,Polar(46,a),1.8f,A(ArtsCodexArt.Ivory,.65f*breathe),A(accent,0));}
                return;
            }
            if(form==Form.Mandala)
            {
                Disc(vh,Vector2.zero,90,new Color(.14f,.32f,.29f,.34f),new Color(.06f,.14f,.13f,0));
                foreach(float radius in new[]{41f,46,70,91,96})Arc(vh,radius,0,360,.24f,A(accent,radius>90?.6f:.27f));
                for(int i=0;i<8;i++)
                {
                    float angle=22.5f+i*45;var inner=Polar(48,angle);var outer=Polar(89,angle);
                    Line(vh,inner,outer,.2f,A(accent,.22f));
                    var a=Polar(91,angle-22.5f);var b=Polar(91,angle+22.5f);Line(vh,a,b,.4f,A(accent,.6f));
                    for(int row=0;row<3;row++)
                    {
                        float r=80+row*3;var center=Polar(r,angle);var tangent=Polar(1,angle+90);
                        if((i&(1<<row))!=0)Line(vh,center-tangent*5,center+tangent*5,1,accent);
                        else{Line(vh,center-tangent*5,center-tangent*1.3f,1,accent);Line(vh,center+tangent*1.3f,center+tangent*5,1,accent);}
                    }
                }
                for(int i=0;i<64;i++){float a=i*360f/64;Line(vh,Polar(93,a),Polar(i%8==0?96:94.5f,a),.22f,A(accent,.65f));}
                for(int i=0;i<4;i++){float a=clock==0?i*90:clock*4+i*90;Disc(vh,Polar(46,a),1.7f,A(ArtsCodexArt.Ivory,.65f*breathe),A(accent,0));}
            }
        }
        protected override void OnDisable(){clock=0;base.OnDisable();}
        protected override void OnDestroy(){reduced=null;base.OnDestroy();}
    }
}
