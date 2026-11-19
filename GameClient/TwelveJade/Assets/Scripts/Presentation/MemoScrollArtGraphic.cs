using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // Native generated silk, jade rollers and silk cords; the footer atlas also supplies the articulated pendant.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MemoScrollArtGraphic : MaskableGraphic
    {
        public enum Part { Body, Header, Footer, Hanger }
        Part part;
        Texture2D artwork;
        public override Texture mainTexture => artwork != null ? artwork : Texture2D.whiteTexture;
        public void Configure(Part value)
        {
            part = value; raycastTarget = false;
            MemoScrollArtwork.Retain(gameObject); artwork = MemoScrollArtwork.Get((int)part);
            SetAllDirty();
        }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); if (artwork == null) return;
            var r = rectTransform.rect;
            // Native artwork is already a finished part. Keep the painted carved caps intact.
            // The footer atlas begins with a 44px roller in a 364px-high source.
            if (part == Part.Footer) Quad(mesh, r.xMin, r.yMin, r.width, r.height, 0, 320f / 364f, 1, 44f / 364f);
            else Quad(mesh, r.xMin, r.yMin, r.width, r.height, 0, 0, 1, 1);
        }
        void Quad(VertexHelper mesh, float x, float y, float w, float h, float u, float v, float uw, float vh)
        {
            if (w <= 0 || h <= 0) return;
            int at = mesh.currentVertCount;
            mesh.AddVert(new Vector3(x, y), color, new Vector2(u, v));
            mesh.AddVert(new Vector3(x, y + h), color, new Vector2(u, v + vh));
            mesh.AddVert(new Vector3(x + w, y + h), color, new Vector2(u + uw, v + vh));
            mesh.AddVert(new Vector3(x + w, y), color, new Vector2(u + uw, v));
            mesh.AddTriangle(at, at + 1, at + 2); mesh.AddTriangle(at, at + 2, at + 3);
        }
    }
}
