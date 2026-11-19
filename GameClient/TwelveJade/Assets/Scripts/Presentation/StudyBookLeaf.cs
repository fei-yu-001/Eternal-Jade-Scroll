using UnityEngine;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class StudyBookLeaf : MaskableGraphic
    {
        float turn;bool visible;
        Texture2D paper;
        public override Texture mainTexture=>paper!=null?paper:Texture2D.whiteTexture;
        protected override void Awake(){base.Awake();raycastTarget=false;paper=Resources.Load<Texture2D>("Art/UI/Nov6/paper-panel");}
        public void Pose(float value,bool show)
        {value=Mathf.Clamp01(value);if(visible==show&&Mathf.Abs(turn-value)<.002f)return;turn=value;visible=show;SetVerticesDirty();}
        Vector2 P(float x,float y){var r=rectTransform.rect;return new Vector2(r.xMin+x*r.width,r.yMax-y*r.height);}
        protected override void OnPopulateMesh(VertexHelper v)
        {
            v.Clear();if(!visible)return;
            // Subdivided paper curls over the actual center crease of the authored open paper book.
            // Its two fixed spine vertices stay on the binding while the outer sheet arcs and settles.
            const int segments=18;float theta=turn*Mathf.PI;
            float opacity=Mathf.Clamp01(turn/.10f)*Mathf.Clamp01((1-turn)/.12f);
            for(int i=0;i<=segments;i++)
            {
                float t=i/(float)segments;float bend=Mathf.Sin(theta)*Mathf.Sin(t*Mathf.PI)*.42f;
                float angle=theta-bend;float spread=.168f*t*Mathf.Cos(angle);
                float lift=.080f*Mathf.Sin(theta)*Mathf.Sin(t*Mathf.PI*.5f);
                float slope=-.078f*t;float curl=.016f*Mathf.Sin(theta)*Mathf.Sin(t*Mathf.PI);
                float shade=.79f+.19f*Mathf.Abs(Mathf.Cos(angle))-.10f*Mathf.Sin(t*Mathf.PI)*Mathf.Sin(theta);
                var tint=new Color(shade,shade*.985f,shade*.90f,opacity);
                v.AddVert(P(.582f+spread,.693f+slope-lift-curl),tint,new Vector2(.22f+t*.56f,.74f));
                v.AddVert(P(.588f+spread,.723f+slope-lift+curl*.2f),tint,new Vector2(.22f+t*.56f,.32f));
                if(i==0)continue;int n=i*2;v.AddTriangle(n-2,n-1,n);v.AddTriangle(n,n-1,n+1);
            }
        }
        protected override void OnDestroy(){paper=null;base.OnDestroy();}
    }
}
