using UnityEngine;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    // Uses the normal uGUI material: stencil and RectMask2D remain owned by MaskableGraphic.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class JadeWheelArtGraphic : RawImage
    {
        public const float BaseOpacity=.68f, SubstrateOpacity=.16f;
        bool rotor;
        public void Configure(bool rotating){rotor=rotating;raycastTarget=false;SetVerticesDirty();}
        public static float Opacity(Vector2 source,bool rotating)
        {
            float radius=Vector2.Distance(source,new Vector2(480,480))/480f;
            float edge=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.80f,1,radius));
            if(!rotating)return BaseOpacity*edge*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(800,830,source.y)));
            float art=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(150,177,Vector2.Distance(source,JadeLotusRotator.SourceJadeCentre)));
            foreach(var at in JadeLotusRotator.SourceSlotCentres)
                art=Mathf.Max(art,1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(76,102,Vector2.Distance(source,at))));
            return Mathf.Lerp(SubstrateOpacity,.96f,art)*edge*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(800,830,source.y)));
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();var r=rectTransform.rect;const int count=48;
            for(int y=0;y<=count;y++)for(int x=0;x<=count;x++)
            {
                float u=x/(float)count,v=y/(float)count;var tint=color;
                tint.a*=Opacity(new Vector2(u*960,(1-v)*960),rotor);
                vh.AddVert(new Vector3(r.xMin+r.width*u,r.yMin+r.height*v),tint,
                    new Vector2(uvRect.x+uvRect.width*u,uvRect.y+uvRect.height*v));
            }
            for(int y=0;y<count;y++)for(int x=0;x<count;x++)
            {int a=y*(count+1)+x;vh.AddTriangle(a,a+count+1,a+count+2);vh.AddTriangle(a,a+count+2,a+1);}
        }
    }
}
