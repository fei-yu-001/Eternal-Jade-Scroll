using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // Small focus brackets for an unselected folio. Selected light comes from the painted source asset.
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class MemoScrollAuraGraphic : MaskableGraphic
    {
        public void ConfigureFocus() { raycastTarget = false; SetVerticesDirty(); }
        protected override void Awake() { base.Awake(); raycastTarget = false; }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); FocusCorners(mesh);
        }
        void FocusCorners(VertexHelper mesh)
        {
            // Curved copper-gold brackets distinguish keyboard focus without another rectangular border.
            var r = rectTransform.rect; var c = new Color(1, .87f, .53f, .90f);
            for (int lane = 0; lane < 2; lane++)
            {
                float start = lane == 0 ? .20f : .70f;
                MemoScrollGeometry.Orbit(start, r.width, r.height, 0, out float px, out float py, out _);
                for (int i = 1; i <= 24; i++)
                {
                    MemoScrollGeometry.Orbit(start + i / 240f, r.width, r.height, 0, out float x, out float y, out _);
                    Line(mesh, r.center + new Vector2(px, py), r.center + new Vector2(x, y), 1.1f, c);
                    px = x; py = y;
                }
            }
        }
        static void Line(VertexHelper mesh, Vector2 a, Vector2 b, float width, Color tint)
        {
            Vector2 v = b - a; if (v.sqrMagnitude < .000001f) return;
            var normal = new Vector2(-v.y, v.x).normalized * (width * .5f);
            int n = mesh.currentVertCount;
            mesh.AddVert(a - normal, tint, Vector2.zero); mesh.AddVert(a + normal, tint, Vector2.zero);
            mesh.AddVert(b + normal, tint, Vector2.zero); mesh.AddVert(b - normal, tint, Vector2.zero);
            mesh.AddTriangle(n, n + 1, n + 2); mesh.AddTriangle(n, n + 2, n + 3);
        }
    }
}
