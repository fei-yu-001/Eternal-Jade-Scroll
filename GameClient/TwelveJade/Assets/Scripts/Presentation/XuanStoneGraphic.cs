using UnityEngine;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    [RequireComponent(typeof(RectTransform),typeof(CanvasRenderer))]
    public sealed class XuanStoneGraphic : MaskableGraphic
    {
        public enum Form { Panel, Groove, Ruyi }
        Form form;
        public void Configure(Form value,bool hit=false){form=value;raycastTarget=hit;SetVerticesDirty();}
        void Quad(VertexHelper v,Vector2 a,Vector2 b,Vector2 c,Vector2 d,Color tint)
        {int n=v.currentVertCount;v.AddVert(a,tint*color,Vector2.zero);v.AddVert(b,tint*color,Vector2.zero);v.AddVert(c,tint*color,Vector2.zero);v.AddVert(d,tint*color,Vector2.zero);v.AddTriangle(n,n+1,n+2);v.AddTriangle(n,n+2,n+3);}
        void Line(VertexHelper v,Vector2 a,Vector2 b,float width,Color tint)
        {var d=(b-a).normalized;var normal=new Vector2(-d.y,d.x)*width*.5f;Quad(v,a-normal,a+normal,b+normal,b-normal,tint);}
        void Disc(VertexHelper v,Vector2 c,float radius,Color middle,Color rim)
        {for(int i=0;i<32;i++){float a=i*Mathf.PI/16,b=(i+1)*Mathf.PI/16;int n=v.currentVertCount;
            v.AddVert(c,middle*color,Vector2.zero);v.AddVert(c+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius,rim*color,Vector2.zero);v.AddVert(c+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*radius,rim*color,Vector2.zero);v.AddTriangle(n,n+1,n+2);}}
        protected override void OnPopulateMesh(VertexHelper v)
        {
            v.Clear();var r=rectTransform.rect;var jade=new Color(.73f,.85f,.79f,.92f);var line=new Color(.54f,.69f,.63f,.52f);
            if(form==Form.Ruyi)
            {
                float size=Mathf.Min(r.width,r.height),rad=size*.25f;var c=r.center;
                Disc(v,c+new Vector2(-rad*.9f,0),rad,jade,new Color(.36f,.56f,.49f,.85f));Disc(v,c+new Vector2(rad*.9f,0),rad,jade,new Color(.36f,.56f,.49f,.85f));
                Disc(v,c+new Vector2(0,rad*.8f),rad*1.12f,new Color(.92f,.96f,.86f,1),jade);
                Line(v,c+new Vector2(-rad,0),c-new Vector2(0,rad*1.3f),2,jade);Line(v,c+new Vector2(rad,0),c-new Vector2(0,rad*1.3f),2,jade);return;
            }
            if(form==Form.Groove)
            {
                Line(v,new Vector2(r.xMin+14,r.center.y),new Vector2(r.xMax-14,r.center.y),12,new Color(.02f,.06f,.055f,.7f));
                Line(v,new Vector2(r.xMin+14,r.center.y-4),new Vector2(r.xMax-14,r.center.y-4),1.5f,line);
                for(int i=0;i<11;i++){float x=Mathf.Lerp(r.xMin+16,r.xMax-16,i/10f);Line(v,new Vector2(x,r.center.y-9),new Vector2(x,r.center.y-13),1,line);}return;
            }
            // Fine mineral facets, not an opaque yellow slab. Boundary alpha reaches zero over 10 design pixels.
            const int cols=40,rows=24;
            for(int y=0;y<=rows;y++)for(int x=0;x<=cols;x++)
            {
                float u=x/(float)cols,t=y/(float)rows,noise=Mathf.PerlinNoise(u*38,t*25);
                float edge=Mathf.Min(Mathf.Min(u*r.width,(1-u)*r.width),Mathf.Min(t*r.height,(1-t)*r.height));
                var ink=Color.Lerp(new Color(.055f,.095f,.10f,.88f),new Color(.115f,.17f,.17f,.90f),noise);
                ink.a*=Mathf.Clamp01(edge/10);v.AddVert(new Vector3(r.xMin+r.width*u,r.yMin+r.height*t),ink*color,Vector2.zero);
            }
            for(int y=0;y<rows;y++)for(int x=0;x<cols;x++){int n=y*(cols+1)+x;v.AddTriangle(n,n+cols+1,n+cols+2);v.AddTriangle(n,n+cols+2,n+1);}
            foreach(int sx in new[]{-1,1})foreach(int sy in new[]{-1,1})
            {
                var corner=r.center+new Vector2(sx*(r.width*.5f-28),sy*(r.height*.5f-28));
                Vector2 prev=corner+new Vector2(-sx*62,0);Line(v,prev,corner,1.2f,line);Line(v,corner,corner+new Vector2(0,-sy*62),1.2f,line);
                for(int i=0;i<45;i++){float a=i*.17f,b=(i+1)*.17f;var center=corner+new Vector2(-sx*20,-sy*20);
                    var q=center+new Vector2(sx*Mathf.Cos(a),sy*Mathf.Sin(a))*(20-a*1.8f);
                    var z=center+new Vector2(sx*Mathf.Cos(b),sy*Mathf.Sin(b))*(20-b*1.8f);Line(v,q,z,1.1f,line);}
            }
        }
    }
}
