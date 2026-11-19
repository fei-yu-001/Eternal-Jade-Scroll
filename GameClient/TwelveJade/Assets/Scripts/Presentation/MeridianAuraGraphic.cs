using System;
using UnityEngine;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MeridianAuraGraphic : MaskableGraphic
    {
        Func<bool> low;float clock,next;
        public void Configure(Func<bool> reduced){low=reduced;raycastTarget=false;}
        void Update(){float t=(low?.Invoke()??true)?0:Time.unscaledTime;if(t==clock||Time.unscaledTime<next)return;clock=t;next=Time.unscaledTime+1f/24;SetVerticesDirty();}
        protected override void OnPopulateMesh(VertexHelper v)
        {
            v.Clear();var r=rectTransform.rect;var center=r.center+new Vector2(0,r.height*.08f);float breath=clock==0?.6f:.58f+.17f*Mathf.Sin(clock*Mathf.PI*2/2.5f);
            for(int band=0;band<3;band++)for(int i=0;i<120;i++)
            {
                float a=i*Mathf.PI/60,b=(i+1)*Mathf.PI/60,rx=r.width*(.28f+band*.055f),ry=r.height*(.36f+band*.025f);
                float flow=clock==0?.6f:.35f+.65f*Mathf.Pow(Mathf.Max(0,Mathf.Cos(a-clock*.42f-band)),6);
                var ink=new Color(.96f,.81f,.47f,breath*(.09f+flow*.17f)/(band+1));var clear=ink;clear.a=0;int n=v.currentVertCount;
                v.AddVert(center+new Vector2(Mathf.Cos(a)*rx,Mathf.Sin(a)*ry),ink*color,Vector2.zero);
                v.AddVert(center+new Vector2(Mathf.Cos(a)*(rx+18),Mathf.Sin(a)*(ry+18)),clear,Vector2.zero);
                v.AddVert(center+new Vector2(Mathf.Cos(b)*(rx+18),Mathf.Sin(b)*(ry+18)),clear,Vector2.zero);
                v.AddVert(center+new Vector2(Mathf.Cos(b)*rx,Mathf.Sin(b)*ry),ink*color,Vector2.zero);v.AddTriangle(n,n+1,n+2);v.AddTriangle(n,n+2,n+3);
            }
        }
        protected override void OnDestroy(){low=null;base.OnDestroy();}
    }
}
