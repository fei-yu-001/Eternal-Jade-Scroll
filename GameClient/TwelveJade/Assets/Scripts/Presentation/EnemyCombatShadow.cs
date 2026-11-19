using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class EnemyCombatShadow : MaskableGraphic
    {
        Vector2 foot; float radius = 95;
        protected override void Awake() { base.Awake(); raycastTarget = false; }
        public void Sample(Vector2 at, bool boss) { foot = at; radius = boss ? 155 : 95; SetVerticesDirty(); }
        protected override void OnPopulateMesh(VertexHelper v)
        {
            v.Clear(); const int n = 48;
            v.AddVert(foot, new Color(.035f, .045f, .035f, .56f) * color, Vector2.zero);
            for (int i = 0; i <= n; i++)
            {
                float a = i * Mathf.PI * 2 / n;
                v.AddVert(foot + new Vector2(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius * .14f), new Color(.04f, .05f, .04f, 0), Vector2.zero);
                if (i > 0) v.AddTriangle(0, i, i + 1);
            }
        }
    }
}
