using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // Used only by the meridian and chapter compositions. Every visible pixel samples authored art.
    // Feather the mesh alpha; do not create a texture, flatten the painting, or distort its UV aspect.
    public sealed class ScrollPageFeatheredArt : RawImage
    {
        public Rect SourceUv = new Rect(0, 0, 1, 1);
        public bool CloudContour;
        // Left, top, right, bottom, in design pixels.
        public Vector4 Feather = new Vector4(48, 48, 48, 48);
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var box = rectTransform.rect;
            if (texture == null || box.width <= 0 || box.height <= 0) return;
            var uv = SourceUv;
            float sourceAspect = texture.width * uv.width / (texture.height * uv.height);
            float targetAspect = box.width / box.height;
            if (sourceAspect > targetAspect)
            {
                float width = uv.width * targetAspect / sourceAspect;
                uv.x += (uv.width - width) * .5f; uv.width = width;
            }
            else
            {
                float height = uv.height * sourceAspect / targetAspect;
                uv.y += (uv.height - height) * .5f; uv.height = height;
            }
            const int columns = 40, rows = 32;
            for (int y = 0; y <= rows; y++)
                for (int x = 0; x <= columns; x++)
                {
                    float u = x / (float)columns, v = y / (float)rows;
                    var ink = color;
                    ink.a *= Fade(u * box.width, Feather.x) * Fade((1 - u) * box.width, Feather.z)
                        * Fade(v * box.height, Feather.w) * Fade((1 - v) * box.height, Feather.y);
                    if (CloudContour)
                    {
                        float radius = new Vector2((u - .5f) * 2, (v - .5f) * 2).magnitude;
                        ink.a *= 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.70f, 1.12f, radius));
                    }
                    mesh.AddVert(new Vector2(box.xMin + u * box.width, box.yMin + v * box.height),
                        ink, new Vector2(uv.x + u * uv.width, uv.y + v * uv.height));
                }
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < columns; x++)
                {
                    int a = y * (columns + 1) + x, b = a + columns + 1;
                    mesh.AddTriangle(a, b, b + 1); mesh.AddTriangle(a, b + 1, a + 1);
                }
        }
        static float Fade(float distance, float feather)
            => feather <= 0 ? 1 : Mathf.SmoothStep(0, 1, Mathf.Clamp01(distance / feather));
    }
}
