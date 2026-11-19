using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    // Small HUD ornaments are native geometry, so their fine edges remain sharp at every canvas scale.
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class GuofengHudTrim : MaskableGraphic
    {
        public enum Shape { Rail, Pendant, PetalFocus }
        Shape shape; Color stone; float focus;
        public void Configure(Shape value,Color jade) { shape=value;stone=jade;raycastTarget=false;SetVerticesDirty(); }
        public void SetFocus(float value) { if(Mathf.Abs(focus-value)<.001f)return;focus=value;SetVerticesDirty(); }
        Vector2 P(float x,float y) { var r=rectTransform.rect;return new Vector2(r.xMin+x*r.width,r.yMin+y*r.height); }
        void Polygon(VertexHelper vh,Vector2[] points,Color tint)
        {
            int at=vh.currentVertCount;foreach(var p in points)vh.AddVert(P(p.x,p.y),tint,Vector2.zero);
            for(int i=1;i<points.Length-1;i++)vh.AddTriangle(at,at+i,at+i+1);
        }
        void Line(VertexHelper vh,float ax,float ay,float bx,float by,float width,Color tint)
        {
            var a=P(ax,ay);var b=P(bx,by);var n=new Vector2(a.y-b.y,b.x-a.x).normalized*width*.5f;int at=vh.currentVertCount;
            vh.AddVert(a-n,tint,Vector2.zero);vh.AddVert(a+n,tint,Vector2.zero);vh.AddVert(b+n,tint,Vector2.zero);vh.AddVert(b-n,tint,Vector2.zero);
            vh.AddTriangle(at,at+1,at+2);vh.AddTriangle(at,at+2,at+3);
        }
        void Outline(VertexHelper vh,Vector2[] points,float width,Color tint)
        { for(int i=0;i<points.Length;i++) {var a=points[i];var b=points[(i+1)%points.Length];Line(vh,a.x,a.y,b.x,b.y,width,tint);} }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();var gold=new Color(.84f,.76f,.49f,.9f);var light=new Color(.76f,.91f,.77f,.92f);
            if(shape==Shape.Rail)
            {
                var p=new[]{new Vector2(0,.13f),new Vector2(.93f,.13f),new Vector2(.98f,.44f),new Vector2(1,.88f),new Vector2(0,.88f)};
                Polygon(vh,p,stone);Outline(vh,p,1.2f,gold);
                Line(vh,.025f,.74f,.955f,.74f,1,new Color(1,1,.88f,.2f));
                Line(vh,.94f,.26f,.975f,.7f,1.2f,light);Line(vh,.92f,.3f,.95f,.6f,1,light);
            }
            else if(shape==Shape.Pendant)
            {
                Line(vh,.5f,1,.5f,.88f,1.8f,gold);
                var p=new[]{new Vector2(.2f,.35f),new Vector2(.22f,.79f),new Vector2(.5f,.9f),new Vector2(.78f,.79f),new Vector2(.8f,.35f),new Vector2(.65f,.27f),new Vector2(.35f,.27f)};
                Polygon(vh,p,Color.Lerp(stone,light,focus*.22f));Outline(vh,p,1.4f,light);
                Line(vh,.27f,.38f,.29f,.77f,1,new Color(1,1,.9f,.26f));
                Line(vh,.5f,.27f,.5f,.12f,2,stone);
                for(int i=0;i<5;i++)Line(vh,.5f,.14f,.4f+i*.04f,.025f,1.3f,stone);
            }
            else if(focus>0)
            {
                var p=new[]{new Vector2(.5f,.98f),new Vector2(.9f,.65f),new Vector2(.84f,.22f),new Vector2(.5f,.04f),new Vector2(.16f,.22f),new Vector2(.1f,.65f)};
                Polygon(vh,p,new Color(.58f,.93f,.65f,focus*.15f));Outline(vh,p,1.5f,new Color(1,.92f,.58f,focus*.9f));
            }
        }
    }
    public sealed class GuofengHudFeedback : MonoBehaviour,IPointerEnterHandler,IPointerExitHandler,IPointerDownHandler,IPointerUpHandler,ISelectHandler,IDeselectHandler
    {
        GuofengHudTrim trim;Func<bool> reduce;bool hovered,selected,pressed;float clock;
        public void Configure(GuofengHudTrim target,Func<bool> reduced) { trim=target;reduce=reduced; }
        void Update() { if(!hovered&&!selected&&!pressed)return;clock+=Time.unscaledDeltaTime;Apply(); }
        void Apply() { if(trim!=null)trim.SetFocus(pressed?1:hovered||selected?(reduce?.Invoke()??true)?.7f:.72f+Mathf.Sin(clock*3)*.1f:0); }
        public void OnPointerEnter(PointerEventData e){hovered=true;Apply();}
        public void OnPointerExit(PointerEventData e){hovered=false;pressed=false;Apply();}
        public void OnPointerDown(PointerEventData e){pressed=true;Apply();}
        public void OnPointerUp(PointerEventData e){pressed=false;Apply();}
        public void OnSelect(BaseEventData e){selected=true;Apply();}
        public void OnDeselect(BaseEventData e){selected=false;pressed=false;Apply();}
        void OnDisable(){hovered=selected=pressed=false;Apply();}
    }
}
