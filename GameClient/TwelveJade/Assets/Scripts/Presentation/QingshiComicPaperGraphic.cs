using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // Plain paper geometry, without a rounded sprite, texture allocation or animated ornament.
    // The outline stays inside the full rectangular hit area. Button tint only changes the paper.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class QingshiComicPaperGraphic : MaskableGraphic
    {
        public enum Surface { Slip, Note, Seal }
        public Surface Kind;

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var r = rectTransform.rect;
            if (r.width <= 0 || r.height <= 0) return;
            float edge = Kind == Surface.Seal ? .45f : .85f;
            // Subpixel edge variation suggests cut paper while preserving an almost straight edge.
            var outline = new[] {
                new Vector2(r.xMin + edge, r.yMin + edge),
                new Vector2(r.xMin + edge * .6f, r.yMin + r.height * .32f),
                new Vector2(r.xMin + edge * .25f, r.yMin + r.height * .70f),
                new Vector2(r.xMin + edge, r.yMax - edge * .5f),
                new Vector2(r.xMin + r.width * .36f, r.yMax - edge * .75f),
                new Vector2(r.xMin + r.width * .72f, r.yMax - edge * .35f),
                new Vector2(r.xMax - edge, r.yMax - edge),
                new Vector2(r.xMax - edge * .5f, r.yMin + r.height * .68f),
                new Vector2(r.xMax - edge * .8f, r.yMin + r.height * .30f),
                new Vector2(r.xMax - edge, r.yMin + edge * .45f),
                new Vector2(r.xMin + r.width * .68f, r.yMin + edge * .70f),
                new Vector2(r.xMin + r.width * .32f, r.yMin + edge * .20f)
            };
            mesh.AddVert(r.center, color, Vector2.zero);
            foreach (var point in outline) mesh.AddVert(point, color, Vector2.zero);
            for (int i = 0; i < outline.Length; i++) mesh.AddTriangle(0, i + 1, (i + 1) % outline.Length + 1);

            float inset = Kind == Surface.Note ? 8 : Kind == Surface.Seal ? 3 : 5;
            float stroke = Kind == Surface.Seal ? .65f : .8f;
            if (r.width <= inset * 2 || r.height <= inset * 2) return;
            var ink = new Color(.25f, .21f, .16f, Kind == Surface.Note ? .30f : .42f);
            var inside = new Rect(r.xMin + inset, r.yMin + inset, r.width - inset * 2, r.height - inset * 2);
            Quad(mesh, new Rect(inside.xMin, inside.yMin, inside.width, stroke), ink);
            Quad(mesh, new Rect(inside.xMin, inside.yMax - stroke, inside.width, stroke), ink);
            Quad(mesh, new Rect(inside.xMin, inside.yMin + stroke, stroke, inside.height - stroke * 2), ink);
            Quad(mesh, new Rect(inside.xMax - stroke, inside.yMin + stroke, stroke, inside.height - stroke * 2), ink);
        }

        static void Quad(VertexHelper mesh, Rect rect, Color tint)
        {
            if (rect.width <= 0 || rect.height <= 0) return;
            int start = mesh.currentVertCount;
            mesh.AddVert(new Vector2(rect.xMin, rect.yMin), tint, Vector2.zero);
            mesh.AddVert(new Vector2(rect.xMin, rect.yMax), tint, Vector2.zero);
            mesh.AddVert(new Vector2(rect.xMax, rect.yMax), tint, Vector2.zero);
            mesh.AddVert(new Vector2(rect.xMax, rect.yMin), tint, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2);
            mesh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
