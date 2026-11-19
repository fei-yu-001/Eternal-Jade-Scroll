using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // The approved reference uses a small bronze finial and two taut silk suspension cords.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MemoScrollHangerGraphic : MaskableGraphic
    {
        protected override void Awake() { base.Awake(); raycastTarget = false; }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();var r=rectTransform.rect;float cx=r.center.x,top=r.yMax;
            var bronze=new Color(.47f,.38f,.22f,.95f);var gold=new Color(.81f,.72f,.45f,.9f);
            var peak=new Vector2(cx,top-15);var left=new Vector2(r.xMin+17,r.yMin+3);var right=new Vector2(r.xMax-17,r.yMin+3);
            Line(mesh,peak,left,1.8f,bronze);Line(mesh,peak,right,1.8f,bronze);
            Line(mesh,peak+Vector2.up*.6f,left+Vector2.up*.6f,.65f,gold);
            Line(mesh,peak+Vector2.up*.6f,right+Vector2.up*.6f,.65f,gold);
            Arc(mesh,new Vector2(cx,top-9),3.3f,5.7f,0,360,1.7f,bronze);
            Arc(mesh,new Vector2(cx-.4f,top-8.5f),2.8f,5.0f,25,170,.7f,gold);
            Line(mesh,new Vector2(cx,top-14),new Vector2(cx,top-20),2.0f,bronze);

        }
        static void Arc(VertexHelper mesh, Vector2 c, float rx, float ry, float begin, float end, float width, Color tint)
        {
            Vector2 previous = c + new Vector2(Mathf.Cos(begin*Mathf.Deg2Rad)*rx, Mathf.Sin(begin*Mathf.Deg2Rad)*ry);
            for (int i=1;i<=40;i++)
            { float a=Mathf.Lerp(begin,end,i/40f)*Mathf.Deg2Rad; var p=c+new Vector2(Mathf.Cos(a)*rx,Mathf.Sin(a)*ry); Line(mesh,previous,p,width,tint); previous=p; }
        }
        static void Line(VertexHelper mesh, Vector2 a, Vector2 b, float width, Color tint)
        {
            Vector2 d=b-a; if(d.sqrMagnitude<.000001f)return;
            Vector2 n=new Vector2(-d.y,d.x).normalized*width*.5f; int at=mesh.currentVertCount;
            mesh.AddVert(a-n,tint,Vector2.zero);mesh.AddVert(a+n,tint,Vector2.zero);
            mesh.AddVert(b+n,tint,Vector2.zero);mesh.AddVert(b-n,tint,Vector2.zero);
            mesh.AddTriangle(at,at+1,at+2);mesh.AddTriangle(at,at+2,at+3);
        }
    }
}
