using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // API retained for dialogs and other JadeFrame callers. All decoration is approved art.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MenuOrnament : Image
    {
        public enum Motif { Frame, Scroll, Compass, Book, Sword, Return }
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
            if (icon)
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
    }
}
