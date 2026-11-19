using UnityEngine;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    [RequireComponent(typeof(RectTransform),typeof(CanvasRenderer))]
    public sealed class CombatSkillCooldownGraphic:MaskableGraphic
    {
        float remaining;
        protected override void Awake(){base.Awake();raycastTarget=false;}
        public void SetRemaining(float seconds,float full){float next=full<=0?0:Mathf.Clamp01(seconds/full);if(Mathf.Abs(next-remaining)<.0005f)return;remaining=next;SetVerticesDirty();}
        protected override void OnPopulateMesh(VertexHelper v)
        {
            v.Clear();if(remaining<=0)return;var r=rectTransform.rect;float radius=Mathf.Min(r.width,r.height)*.46f;
            int count=Mathf.CeilToInt(72*remaining);
            for(int i=0;i<count;i++)
            {
                float a=Mathf.PI*.5f-i/72f*Mathf.PI*2,b=Mathf.PI*.5f-Mathf.Min(remaining,(i+1)/72f)*Mathf.PI*2;
                var p=new Vector2(Mathf.Cos(a),Mathf.Sin(a));var q=new Vector2(Mathf.Cos(b),Mathf.Sin(b));int n=v.currentVertCount;
                var c=new Color(.29f,.86f,.85f,.9f)*color;
                v.AddVert(r.center+p*(radius-2),c,Vector2.zero);v.AddVert(r.center+p*(radius+2),c,Vector2.zero);v.AddVert(r.center+q*(radius+2),c,Vector2.zero);v.AddVert(r.center+q*(radius-2),c,Vector2.zero);v.AddTriangle(n,n+1,n+2);v.AddTriangle(n,n+2,n+3);
            }
        }
    }
}
