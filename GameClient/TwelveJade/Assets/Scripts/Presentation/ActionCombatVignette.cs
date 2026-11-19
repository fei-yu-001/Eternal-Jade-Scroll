using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // Only a feathered contrast veil at the HUD edges. The whole battle remains authored artwork.
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class ActionCombatVignette : MaskableGraphic
    {
        protected override void Awake() { base.Awake(); raycastTarget = false; }
        protected override void OnPopulateMesh(VertexHelper v)
        {
            v.Clear(); var r = rectTransform.rect;
            Band(v, r, r.yMax, r.yMax - 400, .88f);
            Band(v, r, r.yMin, r.yMin + 190, .86f);
        }
        void Band(VertexHelper v, Rect r, float edge, float inner, float alpha)
        {
            int n = v.currentVertCount; var dark = new Color(.035f, .09f, .085f, alpha); var clear = new Color(.035f, .09f, .085f, 0);
            v.AddVert(new Vector2(r.xMin, edge), dark * color, Vector2.zero); v.AddVert(new Vector2(r.xMax, edge), dark * color, Vector2.zero);
            v.AddVert(new Vector2(r.xMax, inner), clear * color, Vector2.zero); v.AddVert(new Vector2(r.xMin, inner), clear * color, Vector2.zero);
            v.AddTriangle(n, n + 1, n + 2); v.AddTriangle(n, n + 2, n + 3);
        }
    }
}
