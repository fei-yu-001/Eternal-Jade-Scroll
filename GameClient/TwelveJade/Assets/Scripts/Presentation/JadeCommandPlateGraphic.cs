using UnityEngine;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class JadeCommandPlateGraphic : RawImage
    {
        // Three slices keep both carved ends at their authored aspect. Only the uninscribed jade center extends.
        protected override void OnPopulateMesh(VertexHelper v)
        {
            v.Clear();if(texture==null)return;var r=rectTransform.rect;
            float cap=Mathf.Min(r.width*.24f,texture.width*.20f*r.height/texture.height);
            float[] x={r.xMin,r.xMin+cap,r.xMax-cap,r.xMax},u={0,.20f,.80f,1};
            for(int y=0;y<2;y++)for(int i=0;i<4;i++)v.AddVert(new Vector3(x[i],y==0?r.yMin:r.yMax),color,new Vector2(u[i],y));
            for(int i=0;i<3;i++){v.AddTriangle(i,i+4,i+5);v.AddTriangle(i,i+5,i+1);}
        }
    }
}
