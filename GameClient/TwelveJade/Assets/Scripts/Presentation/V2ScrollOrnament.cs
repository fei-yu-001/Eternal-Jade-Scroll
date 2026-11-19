using UnityEngine;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    // Code-native, individually maskable artwork; no text or controls baked into a reference image.
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class V2ScrollOrnament : MaskableGraphic
    {
        public enum Shape { Paper, Brief, Header, Pendant, WoodPill, Groove, Pearl, SoundLotus, SoundStrings, SoundBell, Veil }
        Shape shape; float emphasis;
        public Shape GeometryShape => shape;
        public void Configure(Shape value, bool receivesPointer = false) { shape = value; raycastTarget = receivesPointer; SetVerticesDirty(); }
        public void Emphasize(float value) { if (Mathf.Abs(emphasis - value) < .001f) return; emphasis = value; SetVerticesDirty(); }
        static readonly Color Ink = new Color(.13f,.24f,.19f), Gold = new Color(.57f,.43f,.21f), Jade = new Color(.31f,.49f,.37f);
        static Color Alpha(Color c,float a) { c.a=a;return c; }
        protected override void OnPopulateMesh(VertexHelper v)
        {
            v.Clear(); var r=rectTransform.rect; if(r.width<=0||r.height<=0)return;
            if(shape==Shape.Veil)
            {
                int n=v.currentVertCount;v.AddVert(r.center,new Color(.025f,.04f,.035f,.40f),Vector2.zero);
                foreach(var p in new[]{new Vector2(r.xMin,r.yMin),new Vector2(r.xMin,r.yMax),new Vector2(r.xMax,r.yMax),new Vector2(r.xMax,r.yMin)})v.AddVert(p,new Color(.025f,.04f,.035f,.22f),Vector2.zero);
                for(int i=0;i<4;i++)v.AddTriangle(n,n+1+i,n+1+(i+1)%4);return;
            }
            if(shape==Shape.Brief)
            {
                // Folded Song-style xuan leaf. The Paper/settings rendering below is unchanged.
                var leaf=Inset(r,6);
                Rounded(v,new Rect(leaf.x+1.5f,leaf.y-1.5f,leaf.width,leaf.height),4,new Color(.22f,.17f,.10f,.18f));
                Rounded(v,leaf,4,new Color(.89f,.83f,.67f,.97f));
                Rounded(v,Inset(leaf,2),3,new Color(.97f,.94f,.83f,.97f));
                Border(v,Inset(leaf,5),.85f,new Color(.61f,.47f,.25f,.78f));
                Border(v,Inset(leaf,7),.55f,new Color(.80f,.67f,.40f,.46f));
                // Fibres and fold shadows stay in the gutters, outside the text's 2px clear area.
                for(int i=0;i<42;i++)
                {
                    float y=leaf.yMin+16+Mathf.Repeat(i*19.71f,Mathf.Max(1,leaf.height-32));
                    float x=i%2==0?leaf.xMin+5:leaf.xMax-10;
                    Stroke(v,new Vector2(x,y),new Vector2(x+3+i%3,y+1.5f+i%4),.5f,new Color(.42f,.32f,.18f,.12f));
                }
                Stroke(v,new Vector2(leaf.xMin+11,leaf.yMin+15),new Vector2(leaf.xMin+11,leaf.yMax-15),2.4f,new Color(.46f,.34f,.18f,.10f));
                Stroke(v,new Vector2(leaf.xMin+13,leaf.yMin+15),new Vector2(leaf.xMin+13,leaf.yMax-15),.8f,new Color(1,.99f,.91f,.72f));
                foreach(float x in new[]{leaf.xMin+11,leaf.xMax-11})foreach(float y in new[]{leaf.yMin+11,leaf.yMax-11})
                {
                    float dx=x<r.center.x?1:-1,dy=y<r.center.y?1:-1;
                    Stroke(v,new Vector2(x,y+dy*9),new Vector2(x,y),1.1f,Alpha(Gold,.85f));
                    Stroke(v,new Vector2(x,y),new Vector2(x+dx*14,y),1.1f,Alpha(Gold,.85f));
                    Arc(v,new Vector2(x+dx*5,y+dy*5),3,dx>0?180:0,dx>0?450:270,.8f,Alpha(Gold,.68f));
                }
                // Small square cinnabar seal: fixed decoration, never a red-dot notification.
                var seal=new Rect(r.xMax-48,r.yMin+27,22,24);
                var cinnabar=new Color(.55f,.19f,.13f,.78f);
                Rounded(v,seal,1,new Color(.66f,.27f,.19f,.10f));Border(v,seal,1.5f,cinnabar);
                Border(v,Inset(seal,3),.7f,Alpha(cinnabar,.55f));
                Stroke(v,new Vector2(seal.x+6,seal.y+17),new Vector2(seal.x+16,seal.y+17),1.5f,cinnabar);
                Stroke(v,new Vector2(seal.x+11,seal.y+6),new Vector2(seal.x+11,seal.y+20),1.4f,cinnabar);
                Stroke(v,new Vector2(seal.x+6,seal.y+12),new Vector2(seal.x+16,seal.y+12),1.4f,cinnabar);
                Stroke(v,new Vector2(seal.x+6,seal.y+12),new Vector2(seal.x+6,seal.y+6),1.4f,cinnabar);
                Stroke(v,new Vector2(seal.x+16,seal.y+12),new Vector2(seal.x+16,seal.y+6),1.4f,cinnabar);
                if(emphasis>.01f)Border(v,Inset(leaf,4),1.25f,new Color(.78f,.62f,.30f,emphasis));
                return;
            }
            if(shape==Shape.Paper||shape==Shape.Brief){Paper(v,r,shape==Shape.Brief);if(emphasis>.01f)Border(v,Inset(r,9),1.4f,new Color(.78f,.62f,.30f,emphasis));return;}
            if(shape==Shape.WoodPill||shape==Shape.Header)
            {
                Rounded(v,r,shape==Shape.Header?20:r.height*.47f,new Color(.23f,.16f,.085f));
                Rounded(v,Inset(r,2),shape==Shape.Header?18:r.height*.45f,Gold);
                Rounded(v,Inset(r,4),shape==Shape.Header?17:r.height*.43f,Color.Lerp(new Color(.27f,.19f,.105f),new Color(.41f,.31f,.16f),emphasis*.55f));
                for(int i=0;i<5;i++){float y=r.yMin+9+(r.height-18)*i/4;Stroke(v,new Vector2(r.xMin+17,y),new Vector2(r.xMax-17,y+1.3f),.6f,new Color(.85f,.66f,.36f,.12f));}
                Border(v,Inset(r,7),1,Alpha(Gold,.7f));Cloud(v,new Vector2(r.xMin+17,r.center.y),7,Gold);Cloud(v,new Vector2(r.xMax-17,r.center.y),-7,Gold);
                if(emphasis>.01f)Border(v,Inset(r,2),1.5f,new Color(.94f,.83f,.51f,emphasis));return;
            }
            if(shape==Shape.Groove)
            {
                var track=new Rect(r.xMin,r.center.y-6,r.width,12);
                Rounded(v,new Rect(track.x,track.y-1,track.width,track.height+2),7,new Color(.34f,.34f,.24f,.65f));
                Rounded(v,track,6,new Color(.90f,.89f,.75f));
                Rounded(v,Inset(track,2),4,new Color(.58f,.67f,.53f));
                Stroke(v,new Vector2(track.xMin+6,track.yMax-2),new Vector2(track.xMax-6,track.yMax-2),1.2f,new Color(1,.98f,.85f,.9f));return;
            }
            if(shape==Shape.Pearl)
            {
                float q=Mathf.Min(r.width,r.height)*.46f;var c=r.center;
                Disc(v,c+new Vector2(0,-1.7f),q+1.5f,new Color(.21f,.25f,.17f,.45f));Disc(v,c,q,Gold);
                Disc(v,c,q-1.5f,new Color(.78f,.83f,.67f));Disc(v,c+new Vector2(-q*.12f,q*.12f),q*.79f,new Color(.94f,.95f,.85f));
                Disc(v,c+new Vector2(-q*.28f,q*.31f),q*.22f,new Color(1,1,.95f,.86f));
                if(emphasis>.01f)Ring(v,c,q+2,1.5f,new Color(.91f,.77f,.44f,emphasis));return;
            }
            if(shape==Shape.Pendant){Pendant(v,r);return;}
            float radius=Mathf.Min(r.width,r.height)*.46f;
            Disc(v,r.center,radius,Gold);Disc(v,r.center,radius-2,new Color(.39f,.54f,.40f));Ring(v,r.center,radius-4,1,new Color(.88f,.91f,.74f));
            var pale=new Color(.93f,.93f,.75f);var c0=r.center;
            if(shape==Shape.SoundStrings)
            {
                for(int i=-2;i<=2;i++)Stroke(v,c0+new Vector2(-radius*.48f,i*3-7),c0+new Vector2(radius*.48f,i*3+7),1,pale);
                Disc(v,c0+new Vector2(-radius*.48f,-7),radius*.18f,pale);
            }
            else if(shape==Shape.SoundBell)
            {
                Arc(v,c0-new Vector2(0,3),radius*.45f,0,180,1.8f,pale);
                Stroke(v,c0+new Vector2(-radius*.45f,-3),c0+new Vector2(-radius*.55f,-radius*.35f),1.8f,pale);
                Stroke(v,c0+new Vector2(radius*.45f,-3),c0+new Vector2(radius*.55f,-radius*.35f),1.8f,pale);
                Stroke(v,c0+new Vector2(-radius*.6f,-radius*.35f),c0+new Vector2(radius*.6f,-radius*.35f),2,pale);Disc(v,c0-new Vector2(0,radius*.48f),2,pale);
            }
            else for(int i=0;i<5;i++){float a=(30+i*30)*Mathf.Deg2Rad;var p=c0+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius*.29f;Ellipse(v,new Rect(p.x-5,p.y-8,10,16),pale);}
        }
        static Rect Inset(Rect r,float a)=>new Rect(r.x+a,r.y+a,Mathf.Max(0,r.width-2*a),Mathf.Max(0,r.height-2*a));
        static void Paper(VertexHelper v,Rect r,bool brief)
        {
            float inset=brief?6:18;var p=Inset(r,inset);
            Rounded(v,p,brief?7:9,new Color(.91f,.86f,.70f,brief?.73f:.98f));
            Rounded(v,Inset(p,3),6,new Color(.97f,.93f,.80f,brief?.19f:.96f));
            for(int i=0;i<43;i++)
            {float t=(i+.5f)/43;float y=p.yMin+p.height*t;float x=p.xMin+9+Mathf.Repeat(i*31.7f,Mathf.Max(1,p.width-40));Stroke(v,new Vector2(x,y),new Vector2(Mathf.Min(p.xMax-8,x+13+i%7*9),y+.8f),.55f,new Color(.40f,.31f,.17f,.075f));}
            Border(v,Inset(p,8),.8f,Alpha(Gold,brief?.4f:.76f));
            if(!brief){Border(v,Inset(p,12),.65f,Alpha(Gold,.43f));Stroke(v,new Vector2(r.center.x,p.yMin+15),new Vector2(r.center.x,p.yMax-15),2,new Color(.40f,.28f,.13f,.1f));}
            foreach(float y in new[]{r.yMin+inset*.7f,r.yMax-inset*.7f})
            {
                float h=brief?9:28;var wood=new Rect(r.xMin+2,y-h/2,r.width-4,h);
                Rounded(v,wood,h*.44f,new Color(.22f,.15f,.085f,.97f));
                Stroke(v,new Vector2(wood.xMin+7,y+h*.22f),new Vector2(wood.xMax-7,y+h*.22f),brief?1:3,new Color(.59f,.43f,.24f,.85f));
                foreach(float x in new[]{wood.xMin+9,wood.xMin+19,wood.xMax-19,wood.xMax-9})Stroke(v,new Vector2(x,wood.yMin),new Vector2(x,wood.yMax),brief?1:2.2f,Gold);
            }
            if(!brief)foreach(float x in new[]{p.xMin+24,p.xMax-24})foreach(float y in new[]{p.yMin+24,p.yMax-24})Cloud(v,new Vector2(x,y),10,Alpha(Gold,.8f));
        }
        static void Pendant(VertexHelper v,Rect r)
        {
            var c=new Vector2(r.center.x,r.yMax-r.height*.32f);float q=Mathf.Min(r.width*.35f,r.height*.14f);
            Stroke(v,new Vector2(c.x,r.yMax),c,1.5f,Gold);Disc(v,c,q,Gold);Disc(v,c,q-1.2f,new Color(.51f,.68f,.52f));Ring(v,c,q*.52f,3,new Color(.87f,.92f,.73f));Disc(v,c,q*.18f,Ink);
            Stroke(v,c-new Vector2(0,q),new Vector2(c.x,r.yMin+r.height*.3f),2,Gold);
            for(int i=-6;i<=6;i++)Stroke(v,new Vector2(c.x+i*.6f,r.yMin+r.height*.32f),new Vector2(c.x+i*1.1f,r.yMin+3+Mathf.Abs(i)*.2f),1,new Color(.20f,.40f,.32f,.9f));
        }
        static void Cloud(VertexHelper v,Vector2 c,float s,Color tint)
        {Arc(v,c,Mathf.Abs(s),10,265,1.15f,tint);Arc(v,c+new Vector2(s*.75f,-s*.35f),Mathf.Abs(s)*.62f,160,420,1,tint);}
        static void Border(VertexHelper v,Rect r,float w,Color c)
        {Stroke(v,new Vector2(r.xMin,r.yMin),new Vector2(r.xMax,r.yMin),w,c);Stroke(v,new Vector2(r.xMin,r.yMax),new Vector2(r.xMax,r.yMax),w,c);Stroke(v,new Vector2(r.xMin,r.yMin),new Vector2(r.xMin,r.yMax),w,c);Stroke(v,new Vector2(r.xMax,r.yMin),new Vector2(r.xMax,r.yMax),w,c);}
        static void Disc(VertexHelper v,Vector2 c,float radius,Color tint)=>Ellipse(v,new Rect(c.x-radius,c.y-radius,radius*2,radius*2),tint);
        static void Ellipse(VertexHelper v,Rect r,Color tint)
        {int n=v.currentVertCount;v.AddVert(r.center,tint,Vector2.zero);for(int i=0;i<=40;i++){float a=i*Mathf.PI/20;v.AddVert(r.center+new Vector2(Mathf.Cos(a)*r.width*.5f,Mathf.Sin(a)*r.height*.5f),tint,Vector2.zero);if(i>0)v.AddTriangle(n,n+i,n+i+1);}}
        static void Ring(VertexHelper v,Vector2 c,float r,float w,Color tint)=>Arc(v,c,r,0,360,w,tint);
        static void Arc(VertexHelper v,Vector2 c,float r,float from,float to,float w,Color tint)
        {Vector2 p=c+new Vector2(Mathf.Cos(from*Mathf.Deg2Rad),Mathf.Sin(from*Mathf.Deg2Rad))*r;for(int i=1;i<=36;i++){float a=Mathf.Lerp(from,to,i/36f)*Mathf.Deg2Rad;Vector2 q=c+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*r;Stroke(v,p,q,w,tint);p=q;}}
        static void Rounded(VertexHelper v,Rect r,float radius,Color tint)
        {
            float d=Mathf.Min(radius,Mathf.Min(r.width,r.height)/2);int first=v.currentVertCount;v.AddVert(r.center,tint,Vector2.zero);
            for(int corner=0;corner<4;corner++)for(int i=0;i<=10;i++)
            {float angle=(corner*90+i*9)*Mathf.Deg2Rad;Vector2 c=new Vector2(corner==0||corner==3?r.xMax-d:r.xMin+d,corner<2?r.yMax-d:r.yMin+d);v.AddVert(c+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*d,tint,Vector2.zero);}
            for(int i=0;i<44;i++)v.AddTriangle(first,first+1+i,first+1+(i+1)%44);
        }
        static void Stroke(VertexHelper v,Vector2 a,Vector2 b,float width,Color c)
        {var n=new Vector2(-(b-a).y,(b-a).x).normalized*(width*.5f);int i=v.currentVertCount;v.AddVert(a-n,c,Vector2.zero);v.AddVert(a+n,c,Vector2.zero);v.AddVert(b+n,c,Vector2.zero);v.AddVert(b-n,c,Vector2.zero);v.AddTriangle(i,i+1,i+2);v.AddTriangle(i,i+2,i+3);}
    }
}
