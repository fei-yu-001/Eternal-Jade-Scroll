using System;
using UnityEngine;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    // Warm mist and lotus halo. Maskable mesh, no private shader/material/texture lifetime.
    [RequireComponent(typeof(RectTransform),typeof(CanvasRenderer))]
    public sealed class WarmSanctumGraphic : MaskableGraphic
    {
        Func<bool> reduced;float phase,next;
        public void Configure(Func<bool> low){reduced=low;raycastTarget=false;SetVerticesDirty();}
        void Update(){if(Time.unscaledTime<next)return;next=Time.unscaledTime+1f/24;float p=(reduced?.Invoke()??true)?0:Time.unscaledTime;if(p!=phase){phase=p;SetVerticesDirty();}}
        Vector2 P(Vector2 p)=>new Vector2(rectTransform.rect.xMin+p.x,rectTransform.rect.yMax-p.y);
        void Wash(VertexHelper vh,Vector2 center,float rx,float ry,Color ink)
        {
            int first=vh.currentVertCount;vh.AddVert(P(center),ink*color,Vector2.zero);var edge=ink;edge.a=0;
            for(int i=0;i<=64;i++){float a=i*Mathf.PI/32;vh.AddVert(P(center+new Vector2(Mathf.Cos(a)*rx,Mathf.Sin(a)*ry)),edge*color,Vector2.zero);}
            for(int i=0;i<64;i++)vh.AddTriangle(first,first+i+1,first+i+2);
        }
        void Line(VertexHelper vh,Vector2 a,Vector2 b,float width,Color ink)
        {
            var n=new Vector2(-(b-a).y,(b-a).x).normalized*width*.5f;int v=vh.currentVertCount;
            vh.AddVert(P(a-n),ink*color,Vector2.zero);vh.AddVert(P(a+n),ink*color,Vector2.zero);vh.AddVert(P(b+n),ink*color,Vector2.zero);vh.AddVert(P(b-n),ink*color,Vector2.zero);
            vh.AddTriangle(v,v+1,v+2);vh.AddTriangle(v,v+2,v+3);
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();float breath=phase==0?1:1+Mathf.Sin(phase*.65f)*.025f;
            Wash(vh,new Vector2(680,450),440*breath,430*breath,new Color(1,.81f,.35f,.34f));
            Wash(vh,new Vector2(680,450),245,270,new Color(1,.97f,.76f,.5f));
            for(int ring=0;ring<2;ring++)for(int i=0;i<128;i++)
            {
                float a=i*Mathf.PI/64,b=(i+1)*Mathf.PI/64,r=332+ring*16;
                Line(vh,new Vector2(680+Mathf.Cos(a)*r,495+Mathf.Sin(a)*r),new Vector2(680+Mathf.Cos(b)*r,495+Mathf.Sin(b)*r),1,new Color(.68f,.48f,.18f,.32f));
            }
            for(int i=0;i<7;i++)
            {
                float x=230+i*225,y=850+Mathf.Sin(i*1.8f)*38+(phase==0?0:Mathf.Sin(phase*.3f+i)*5);
                Wash(vh,new Vector2(x,y),265,70,new Color(.98f,.97f,.84f,.9f));
                Wash(vh,new Vector2(x,y+24),200,40,new Color(.62f,.75f,.61f,.14f));
            }
            for(int i=0;i<11;i++)
            {
                float a=(i-5)*.2f;var center=new Vector2(680+Mathf.Sin(a)*210,838-Mathf.Cos(a)*38);
                Wash(vh,center,40,30,new Color(.89f,.71f,.35f,.16f));
            }
        }
        protected override void OnDisable(){phase=0;SetVerticesDirty();base.OnDisable();}
        protected override void OnDestroy(){reduced=null;base.OnDestroy();}
    }
}
