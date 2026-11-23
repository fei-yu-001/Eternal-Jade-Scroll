using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // Shared menu motifs: approved artwork plus a vector account silhouette.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MenuOrnament : Image
    {
        public enum Motif { Frame, Scroll, Compass, Book, Sword, Return, Account }
        Motif shape;
        Color fill = Color.clear;
        Image border;
        public float Stroke = 1.4f; // Legacy callers may still set this; nine-slice owns the border geometry.
        public Motif Shape { get => shape; set { shape = value; ApplyArtwork(); } }
        public Color Fill { get => fill; set { fill = value; ApplyArtwork(); } }
        public bool IsGoldButton { get; private set; }
        public bool IsUtilityPlate { get; private set; }

        public void UseGoldButton()
        {
            IsGoldButton = true; IsUtilityPlate = false;
            ApplyArtwork();
        }

        public void UseUtilityPlate()
        {
            IsGoldButton = false; IsUtilityPlate = true;
            // Keep the paper center close to its illuminated frame instead of a black cutout.
            fill = new Color(.95f, .98f, .94f, .78f);
            ApplyArtwork();
        }

        void ApplyArtwork()
        {
            MemoVisualAssets.KeepAlive(gameObject);
            bool icon = shape != Motif.Frame && shape != Motif.Scroll;
            if (shape == Motif.Account)
            {
                sprite = null; type = Type.Simple; preserveAspect = true; color = UiKit.Hex("343C30");
            }
            else if (icon)
            {
                string resource = shape == Motif.Compass ? "icon-compass" : shape == Motif.Book || shape == Motif.Return
                    ? "icon-document" : "icon-sword";
                sprite = MemoVisualAssets.GetSprite(resource); type = Type.Simple; preserveAspect = true; color = Color.white;
            }
            else
            {
                sprite = MemoVisualAssets.GetSprite(IsGoldButton ? "button-gold" : "paper-panel");
                type = Type.Sliced; preserveAspect = false; fillCenter = true;
                pixelsPerUnitMultiplier = IsGoldButton ? Mathf.Max(1, 160f / Mathf.Max(1, rectTransform.rect.height)) : IsUtilityPlate ? 8f : 2f;
                color = IsGoldButton ? Color.white : fill;
                if (!IsGoldButton && border == null)
                {
                    border = MemoVisualAssets.Art(transform, "Gold frame", "paper-panel", true);
                    border.transform.SetAsFirstSibling();
                }
                if (border != null) border.pixelsPerUnitMultiplier = pixelsPerUnitMultiplier;
            }
            if (border != null) border.enabled = !icon && !IsGoldButton;
            SetAllDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            if (shape != Motif.Account) { base.OnPopulateMesh(mesh); return; }
            mesh.Clear();
            var rect = GetPixelAdjustedRect();
            float size = Mathf.Min(rect.width, rect.height);
            if (size <= 0) return;
            var origin = rect.center - Vector2.one * (size * .5f);
            // A round head and broad shoulders stay recognizable at the same 43px size as the other utilities.
            AccountFan(mesh, origin + new Vector2(.5f, .73f) * size, Vector2.one * (.15f * size), Mathf.PI * 2, 32);
            AccountFan(mesh, origin + new Vector2(.5f, .14f) * size, new Vector2(.36f, .35f) * size, Mathf.PI, 24);
        }

        void AccountFan(VertexHelper mesh, Vector2 center, Vector2 radius, float sweep, int segments)
        {
            int first = mesh.currentVertCount;
            mesh.AddVert(center, color, Vector2.zero);
            for (int i = 0; i <= segments; i++)
            {
                float angle = sweep * i / segments;
                mesh.AddVert(center + new Vector2(Mathf.Cos(angle) * radius.x, Mathf.Sin(angle) * radius.y), color, Vector2.zero);
                if (i > 0) mesh.AddTriangle(first, first + i, first + i + 1);
            }
        }
    }
}
