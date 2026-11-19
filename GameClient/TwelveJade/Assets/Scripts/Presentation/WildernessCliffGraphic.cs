using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // Reuse precisely the same pixels as the ground plate, but sort cliff faces at
    // their ground contact. No second artwork or rectangular grass occlusion.
    public sealed class WildernessCliffGraphic : MaskableGraphic
    {
        Texture2D art;
        Vector2[] outline;
        readonly List<int> triangles = new List<int>();
        public override Texture mainTexture => art != null ? art : Texture2D.whiteTexture;
        public void Configure(Texture2D texture, Vector2[] points)
        {
            art = texture; outline = points; raycastTarget = false;
            Triangulate(); SetAllDirty();
        }
        void Triangulate()
        {
            triangles.Clear(); var indices = new List<int>();
            for (int i = 0; i < outline.Length; i++) indices.Add(i);
            float area = 0;
            for (int i = 0; i < outline.Length; i++) area += Cross(outline[i], outline[(i + 1) % outline.Length]);
            float sign = Mathf.Sign(area);
            int safety = outline.Length * outline.Length;
            while (indices.Count > 2 && safety-- > 0)
            {
                bool clipped = false;
                for (int i = 0; i < indices.Count; i++)
                {
                    int a = indices[(i + indices.Count - 1) % indices.Count], b = indices[i], c = indices[(i + 1) % indices.Count];
                    if (Cross(outline[b] - outline[a], outline[c] - outline[b]) * sign <= 0) continue;
                    bool inside = false;
                    foreach (int p in indices)
                    {
                        if (p == a || p == b || p == c) continue;
                        if (Cross(outline[b] - outline[a], outline[p] - outline[a]) * sign >= 0
                            && Cross(outline[c] - outline[b], outline[p] - outline[b]) * sign >= 0
                            && Cross(outline[a] - outline[c], outline[p] - outline[c]) * sign >= 0) { inside = true; break; }
                    }
                    if (inside) continue;
                    triangles.Add(a); triangles.Add(b); triangles.Add(c); indices.RemoveAt(i); clipped = true; break;
                }
                if (!clipped) break;
            }
        }
        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        protected override void OnPopulateMesh(VertexHelper v)
        {
            v.Clear(); if (outline == null) return;
            Rect box = rectTransform.rect;
            foreach (var p in outline)
                v.AddVert(new Vector3(box.xMin + p.x / 1600 * box.width, box.yMax - p.y / 1600 * box.height),
                    Color.white, new Vector2(p.x / 1600, 1 - p.y / 1600));
            for (int i = 0; i + 2 < triangles.Count; i += 3) v.AddTriangle(triangles[i], triangles[i + 1], triangles[i + 2]);
        }
    }
}
