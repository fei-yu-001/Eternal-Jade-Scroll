using UnityEngine;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    // Circular geometry samples the registered painted map; no stretched square inside an empty ring.
    public sealed class LivingMiniMap : RawImage
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();var r=GetPixelAdjustedRect();var c=r.center;var uv=uvRect;
            vh.AddVert(c,color,new Vector2(uv.x+uv.width*.5f,uv.y+uv.height*.5f));
            const int segments=96;
            for(int i=0;i<=segments;i++)
            {float a=i*Mathf.PI*2/segments;float x=Mathf.Cos(a),y=Mathf.Sin(a);
                vh.AddVert(new Vector3(c.x+x*r.width*.5f,c.y+y*r.height*.5f),color,new Vector2(uv.x+(.5f+x*.5f)*uv.width,uv.y+(.5f+y*.5f)*uv.height));
                if(i>0)vh.AddTriangle(0,i,i+1);}
        }
        public override bool Raycast(Vector2 screenPoint,Camera eventCamera)
        {
            if(!base.Raycast(screenPoint,eventCamera)||!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform,screenPoint,eventCamera,out var local))return false;
            var r=rectTransform.rect;return ((local-r.center)/Mathf.Max(1,r.width*.5f)).sqrMagnitude<=1;
        }
    }
}
