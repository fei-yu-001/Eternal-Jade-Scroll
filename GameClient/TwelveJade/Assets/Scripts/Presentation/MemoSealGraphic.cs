using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // Small saved-volume imprint: a dark cinnabar slab with raised edges, behind the borrowed red stamp.
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class MemoSealGraphic : MaskableGraphic
    {
        protected override void Awake() { base.Awake(); raycastTarget = false; }
        Vector2 At(float x, float y)
        {
            var r = rectTransform.rect; return new Vector2(r.xMin + x * r.width, r.yMin + y * r.height);
        }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Plate(mesh, .10f, .02f, .87f, .85f, .10f, new Color(.18f, .025f, .016f, .66f), new Color(.32f, .055f, .03f));
            Plate(mesh, .04f, .12f, .86f, .86f, .08f, new Color(.35f, .055f, .035f), new Color(.64f, .13f, .075f));
            Edge(mesh, .10f, .19f, .74f, .72f, .04f, new Color(.90f, .55f, .28f, .62f), .7f);
        }
        void Plate(VertexHelper mesh, float x, float y, float w, float h, float cut, Color bottom, Color top)
        {
            int n = mesh.currentVertCount;
            mesh.AddVert(At(x + w * .5f, y + h * .5f), Color.Lerp(bottom, top, .5f) * color, Vector2.zero);
            Corner(mesh, x + cut, y, bottom); Corner(mesh, x + w - cut, y, bottom);
            Corner(mesh, x + w, y + cut, bottom); Corner(mesh, x + w, y + h - cut, top);
            Corner(mesh, x + w - cut, y + h, top); Corner(mesh, x + cut, y + h, top);
            Corner(mesh, x, y + h - cut, top); Corner(mesh, x, y + cut, bottom);
            for (int i = 0; i < 8; i++) mesh.AddTriangle(n, n + 1 + i, n + 1 + (i + 1) % 8);
        }
        void Corner(VertexHelper mesh, float x, float y, Color tint) { mesh.AddVert(At(x, y), tint * color, Vector2.zero); }
        void Edge(VertexHelper mesh, float x, float y, float w, float h, float cut, Color tint, float width)
        {
            Line(mesh, x + cut, y, x + w - cut, y, width, tint); Line(mesh, x + w - cut, y, x + w, y + cut, width, tint);
            Line(mesh, x + w, y + cut, x + w, y + h - cut, width, tint); Line(mesh, x + w, y + h - cut, x + w - cut, y + h, width, tint);
            Line(mesh, x + w - cut, y + h, x + cut, y + h, width, tint); Line(mesh, x + cut, y + h, x, y + h - cut, width, tint);
            Line(mesh, x, y + h - cut, x, y + cut, width, tint); Line(mesh, x, y + cut, x + cut, y, width, tint);
        }
        void Line(VertexHelper mesh, float ax, float ay, float bx, float by, float width, Color tint)
        {
            var a = At(ax, ay); var b = At(bx, by); var v = b - a;
            if (v.sqrMagnitude < .000001f) return;
            var normal = new Vector2(-v.y, v.x).normalized * width * .5f; int n = mesh.currentVertCount; var c = tint * color;
            mesh.AddVert(a - normal, c, Vector2.zero); mesh.AddVert(a + normal, c, Vector2.zero);
            mesh.AddVert(b + normal, c, Vector2.zero); mesh.AddVert(b - normal, c, Vector2.zero);
            mesh.AddTriangle(n, n + 1, n + 2); mesh.AddTriangle(n, n + 2, n + 3);
        }
    }
}
