using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // Thin mounting, carved corner threads and suspension cord. No bitmap allocations.
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class MemoMountGraphic : MaskableGraphic
    {
        public enum Form { Dossier, Portrait, Command, Input, Utility, Hanger }
        Form form;float emphasis;bool chosen;
        static readonly Color Ink=new Color(.19f,.29f,.26f,.82f),Gold=new Color(.64f,.51f,.29f,.84f);
        public void Configure(Form value,bool selected=false){form=value;chosen=selected;raycastTarget=false;color=Color.white;SetVerticesDirty();}
        public void Emphasis(float value){value=Mathf.Clamp01(value);if(Mathf.Abs(emphasis-value)<.002f)return;emphasis=value;SetVerticesDirty();}
        protected override void Awake(){base.Awake();raycastTarget=false;}
        protected override void OnPopulateMesh(VertexHelper v)
        {
            v.Clear();var r=rectTransform.rect;if(r.width<10||r.height<10)return;
            if(form==Form.Hanger){Hanger(v,r);return;}
            if(form==Form.Utility)
            {
                var slip=new Rect(r.xMin+4,r.yMin+4,r.width-8,34);
                Ring(v,slip,5,.8f,Gold);Line(v,new Vector2(slip.xMin+10,slip.yMin+2),new Vector2(slip.xMax-10,slip.yMin+2),.6f,Ink);
                if(emphasis>.01f)Ring(v,Inset(slip,2),3,1,new Color(.91f,.75f,.43f,emphasis*.9f));return;
            }
            float cut=form==Form.Command?9:form==Form.Input?3:8;
            // A narrow shadow below the solid fine edge supplies depth without a thick white slab.
            Ring(v,Inset(r,1),cut,2.3f,new Color(.10f,.15f,.12f,.13f));
            Ring(v,Inset(r,3),cut,1.15f,Ink);Ring(v,Inset(r,5),Mathf.Max(1,cut-2),.75f,Gold);
            if(form==Form.Dossier||form==Form.Portrait)
            {
                Ring(v,Inset(r,8),4,.55f,new Color(.94f,.83f,.58f,.42f));
                for(int side=0;side<4;side++)CloudCorner(v,r,side,form==Form.Portrait?25:32);
            }
            else
            {
                float y=r.center.y;
                if(form==Form.Command||form==Form.Input)
                    for(int side=0;side<4;side++)CloudCorner(v,r,side,form==Form.Command?18:15);
                if(form==Form.Command)
                {
                    Diamond(v,new Vector2(r.xMin+13,y),2.6f,Gold);Diamond(v,new Vector2(r.xMax-13,y),2.6f,Gold);
                    Line(v,new Vector2(r.xMin+14,r.yMax-8),new Vector2(r.xMax-14,r.yMax-8),.75f,new Color(.98f,.96f,.79f,.66f));
                    Line(v,new Vector2(r.xMin+14,r.yMin+7),new Vector2(r.xMax-14,r.yMin+7),1.4f,new Color(.15f,.25f,.20f,.29f));
                }
                if(chosen)Diamond(v,new Vector2(r.xMin+14,y),3.4f,new Color(.50f,.53f,.31f,.97f));
            }
            if(emphasis>.01f||chosen)Ring(v,Inset(r,1),cut,1.15f,new Color(.91f,.75f,.42f,Mathf.Max(chosen ? .65f : 0, emphasis * .95f)));
        }
        void Hanger(VertexHelper v,Rect r)
        {
            var top=new Vector2(r.center.x,r.yMax-5);var left=new Vector2(r.xMin+18,r.yMin+3);var right=new Vector2(r.xMax-18,r.yMin+3);
            Line(v,top,left,2.4f,new Color(.12f,.18f,.15f,.65f));Line(v,top,right,2.4f,new Color(.12f,.18f,.15f,.65f));
            Line(v,top,left,.8f,new Color(.79f,.68f,.43f,.94f));Line(v,top,right,.8f,new Color(.79f,.68f,.43f,.94f));
            Diamond(v,top,4,new Color(.69f,.65f,.40f,1));Diamond(v,top+Vector2.up*4,2.7f,new Color(.87f,.90f,.70f,1));
        }
        void CloudCorner(VertexHelper v,Rect r,int corner,float length)
        {
            float sx=corner%2==0?1:-1,sy=corner<2?-1:1;
            var origin=new Vector2(sx>0?r.xMin+6:r.xMax-6,sy<0?r.yMax-6:r.yMin+6);
            float scale=Mathf.Min(1,length/25f);
            Vector2 P(float x,float y)=>origin+new Vector2(sx*x,sy*y)*scale;
            Line(v,P(0,length),P(0,9),1.1f,Gold);Line(v,P(9,0),P(length,0),1.1f,Gold);
            var prev=P(0,9);
            for(int i=1;i<=16;i++)
            {
                float a=Mathf.PI*.5f*(i/16f);var at=P(9-9*Mathf.Cos(a),9-9*Mathf.Sin(a));Line(v,prev,at,.9f,Gold);prev=at;
            }
            Line(v,P(4,length*.72f),P(4,13),.7f,Gold);Line(v,P(13,4),P(length*.72f,4),.7f,Gold);
            Diamond(v,P(12,12),2.2f,Gold);Line(v,P(15,15),P(23,23),.6f,new Color(.75f,.62f,.35f,.60f));
        }
        static Rect Inset(Rect r,float d)=>Rect.MinMaxRect(r.xMin+d,r.yMin+d,r.xMax-d,r.yMax-d);
        public static Vector2 Corner(Rect r,float cut,int i)
        {
            cut=Mathf.Min(cut,Mathf.Min(r.width,r.height)*.25f);
            switch(i&7){case 0:return new Vector2(r.xMin+cut,r.yMin);case 1:return new Vector2(r.xMax-cut,r.yMin);case 2:return new Vector2(r.xMax,r.yMin+cut);case 3:return new Vector2(r.xMax,r.yMax-cut);case 4:return new Vector2(r.xMax-cut,r.yMax);case 5:return new Vector2(r.xMin+cut,r.yMax);case 6:return new Vector2(r.xMin,r.yMax-cut);default:return new Vector2(r.xMin,r.yMin+cut);}
        }
        void Ring(VertexHelper v,Rect r,float cut,float width,Color tint)
        {for(int i=0;i<8;i++)Line(v,Corner(r,cut,i),Corner(r,cut,i+1),width,tint);}
        void Line(VertexHelper v,Vector2 a,Vector2 b,float width,Color tint)
        {
            if((b-a).sqrMagnitude<.00001f)return;var n=new Vector2(a.y-b.y,b.x-a.x).normalized*width*.5f;int first=v.currentVertCount;
            v.AddVert(a-n,tint*color,Vector2.zero);v.AddVert(a+n,tint*color,Vector2.zero);v.AddVert(b+n,tint*color,Vector2.zero);v.AddVert(b-n,tint*color,Vector2.zero);v.AddTriangle(first,first+1,first+2);v.AddTriangle(first,first+2,first+3);
        }
        void Diamond(VertexHelper v,Vector2 at,float size,Color tint)
        {int n=v.currentVertCount;v.AddVert(at+Vector2.up*size,tint*color,Vector2.zero);v.AddVert(at+Vector2.right*size,tint*color,Vector2.zero);v.AddVert(at+Vector2.down*size,tint*color,Vector2.zero);v.AddVert(at+Vector2.left*size,tint*color,Vector2.zero);v.AddTriangle(n,n+1,n+2);v.AddTriangle(n,n+2,n+3);}
    }
}
