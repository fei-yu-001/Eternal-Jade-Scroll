using UnityEngine;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    // One existing, authored town tile. Its softly irregular shore of mist leaves room for future regions.
    [RequireComponent(typeof(RectTransform),typeof(CanvasRenderer))]
    public sealed class AtlasRegionGraphic : MaskableGraphic
    {
        Texture2D art;
        public override Texture mainTexture=>art!=null?art:Texture2D.whiteTexture;
        public void Configure(Texture2D texture){art=texture;raycastTarget=false;SetAllDirty();}
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();var r=rectTransform.rect;const int steps=48;
            for(int y=0;y<=steps;y++)for(int x=0;x<=steps;x++)
            {
                float u=x/(float)steps,v=y/(float)steps;
                float distance=Mathf.Min(Mathf.Min(u,1-u),Mathf.Min(v,1-v));
                float edge=.032f+.014f*Mathf.Sin(u*25+v*19)+.008f*Mathf.Sin(u*49-v*31);
                float alpha=Mathf.SmoothStep(0,1,Mathf.Clamp01(distance/Mathf.Max(.012f,edge)));
                float contour=Mathf.Pow(Mathf.Abs(u-.5f)*2,4)+Mathf.Pow(Mathf.Abs(v-.5f)*2,4);
                float wave=.045f*Mathf.Sin(u*27+v*21)+.028f*Mathf.Sin(u*39-v*29);
                alpha*=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.72f,1.25f,contour+wave));
                mesh.AddVert(new Vector2(r.xMin+u*r.width,r.yMax-v*r.height),new Color(1,1,1,alpha)*color,new Vector2(u,1-v));
            }
            for(int y=0;y<steps;y++)for(int x=0;x<steps;x++)
            {int a=y*(steps+1)+x;mesh.AddTriangle(a,a+steps+1,a+1);mesh.AddTriangle(a+1,a+steps+1,a+steps+2);}
        }
        protected override void OnDestroy(){art=null;base.OnDestroy();}
    }
}
