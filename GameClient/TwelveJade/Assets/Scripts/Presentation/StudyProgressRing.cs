using UnityEngine;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    [RequireComponent(typeof(RectTransform),typeof(CanvasRenderer))]
    public sealed class StudyProgressRing : MaskableGraphic
    {
        int progress,strain;
        public void State(int knowledge,int burden){progress=Mathf.Clamp(knowledge,0,100);strain=Mathf.Clamp(burden,0,100);raycastTarget=false;SetVerticesDirty();}
        void Arc(VertexHelper vh,float radius,int segments,Color ink,float width)
        {
            var center=rectTransform.rect.center;
            for(int i=0;i<segments;i++)
            {
                float a=Mathf.PI*.5f-i*Mathf.PI/60,b=Mathf.PI*.5f-(i+1)*Mathf.PI/60;
                var va=new Vector2(Mathf.Cos(a),Mathf.Sin(a));var vb=new Vector2(Mathf.Cos(b),Mathf.Sin(b));int start=vh.currentVertCount;
                vh.AddVert(center+va*(radius-width*.5f),ink*color,Vector2.zero);vh.AddVert(center+va*(radius+width*.5f),ink*color,Vector2.zero);
                vh.AddVert(center+vb*(radius+width*.5f),ink*color,Vector2.zero);vh.AddVert(center+vb*(radius-width*.5f),ink*color,Vector2.zero);
                vh.AddTriangle(start,start+1,start+2);vh.AddTriangle(start,start+2,start+3);
            }
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();float r=Mathf.Min(rectTransform.rect.width,rectTransform.rect.height)*.5f-9;
            Arc(vh,r,120,new Color(.59f,.50f,.29f,.22f),1);
            Arc(vh,r,Mathf.RoundToInt(progress*1.2f),new Color(.70f,.43f,.12f,.76f),3);
            if(strain>0)Arc(vh,r-7,Mathf.RoundToInt(strain*1.2f),new Color(.60f,.27f,.20f,.4f),1.5f);
        }
    }
}
