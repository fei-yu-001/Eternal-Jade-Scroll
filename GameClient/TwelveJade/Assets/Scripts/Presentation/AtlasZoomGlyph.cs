using UnityEngine;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    // Small magnifying-glass engraving on the painted jade control, always decorative.
    [RequireComponent(typeof(RectTransform),typeof(CanvasRenderer))]
    public sealed class AtlasZoomGlyph : MaskableGraphic
    {
        bool plus;
        public void Configure(bool magnify){plus=magnify;raycastTarget=false;SetVerticesDirty();}
        protected override void OnPopulateMesh(VertexHelper v)
        {
            v.Clear();var c=rectTransform.rect.center+new Vector2(-2,2);
            for(int i=0;i<40;i++){float a=i*Mathf.PI/20,b=(i+1)*Mathf.PI/20;Line(v,c+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*10,c+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*10,1.6f);}
            Line(v,c+new Vector2(7,-7),c+new Vector2(16,-16),3);
            Line(v,c+new Vector2(-5,0),c+new Vector2(5,0),1.6f);if(plus)Line(v,c+new Vector2(0,-5),c+new Vector2(0,5),1.6f);
        }
        static void Line(VertexHelper v,Vector2 a,Vector2 b,float width)
        {
            var d=(b-a).normalized;var n=new Vector2(-d.y,d.x)*width*.5f;int start=v.currentVertCount;var ink=new Color(.18f,.30f,.25f,1);
            v.AddVert(a-n,ink,Vector2.zero);v.AddVert(a+n,ink,Vector2.zero);v.AddVert(b+n,ink,Vector2.zero);v.AddVert(b-n,ink,Vector2.zero);
            v.AddTriangle(start,start+1,start+2);v.AddTriangle(start,start+2,start+3);
        }
    }
}
