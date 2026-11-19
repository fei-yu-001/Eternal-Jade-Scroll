using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // Assemble authored textures; all coordinates are in the 1920 x 1080 design space.
    // No generated panel sprites, flat backgrounds or per-frame material ownership.
    public static class ImmortalUi
    {
        public static readonly Color Ink = UiKit.Hex("382C24"), Pale = UiKit.Hex("E8DCBE"),
            Gold = UiKit.Hex("DCC18A"), Ash = UiKit.Hex("C1B99F"), Red = UiKit.Hex("984838");
        public static TMP_Text Text(UiKit ui, Transform parent, string name, string text,
            float x, float y, float w, float h, float size = 24, Color? ink = null,
            bool brush = false, TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
        {
            var label = ui.Label(parent, text, x, y, w, h, size, ink ?? Pale, align, brush);
            label.name = name; label.richText = false; label.extraPadding = false;
            label.enableAutoSizing = false; label.overflowMode = TextOverflowModes.Overflow;
            if (!brush) SdfInkWeight.Apply(label);
            return label;
        }
        public static RawImage Art(UiKit ui, Transform parent, string name, string id,
            float x, float y, float w, float h, bool block = false)
        {
            var tex = ApprovedUiArt.Load(id);
            if (tex == null) return null;
            var image = ui.Rect(parent, name, x, y, w, h).gameObject.AddComponent<RawImage>();
            image.texture = tex; image.raycastTarget = block;
            return image;
        }
        public static RectTransform Master(UiKit ui, Transform parent, string name, string id)
        {
            var image = Art(ui, parent, name, "nov8/" + id + "-bg", 0, 0, 1920, 1080, true);
            var overlayId = "nov8/" + id + "-live-overlay";
            if (ApprovedUiArt.Has(overlayId))
                Art(ui, image != null ? image.transform : parent, name + " live-data restoration", overlayId, 0, 0, 1920, 1080);
            return image != null ? image.rectTransform : ui.Rect(parent, name, 0, 0, 1920, 1080);
        }
        public static Button Hotspot(UiKit ui, Transform parent, string name, string text,
            float x, float y, float w, float h, UnityAction action, float size = 24, Color? ink = null)
        {
            var host = ui.Rect(parent, name, x, y, w, h);
            // This is a hit target only. Visible artwork remains the authored image underneath.
            var hit = host.gameObject.AddComponent<Image>(); hit.color = Color.clear;
            var button = host.gameObject.AddComponent<Button>(); button.targetGraphic = hit;
            button.transition = Selectable.Transition.None;
            TMP_Text label = null;
            if (!string.IsNullOrEmpty(text)) label = Text(ui, host, name + " caption", text, 6, 3, w - 12, h - 6, size, ink, false, TextAlignmentOptions.Center);
            host.gameObject.AddComponent<ImmortalControlFeedback>().Configure(label);
            if (action != null) button.onClick.AddListener(action);
            return button;
        }
        public static Button Button(UiKit ui, Transform parent, string name, string text,
            float x, float y, float w, float h, UnityAction action, bool selected = false)
        {
            var art = Art(ui, parent, name + " carved tab", "nov8/ledger-tab", x, y, w, h);
            return Hotspot(ui, art.transform, name, text, 0, 0, w, h, action, 24, selected ? Gold : Pale);
        }
        public static Image Silk(UiKit ui, Transform parent, string name, float x, float y, float w, float h)
        {
            var host = ui.Rect(parent, name, x, y, w, h);
            return MemoVisualAssets.Art(host, name + " woven silk", "scroll-body");
        }
        public static Image Paper(UiKit ui, Transform parent, string name, float x, float y, float w, float h)
        {
            var host = ui.Rect(parent, name, x, y, w, h);
            return MemoVisualAssets.Art(host, name + " aged paper", "paper-panel");
        }
        public static RawImage Texture(UiKit ui, Transform parent, string name, Texture2D texture,
            float x, float y, float w, float h, Color? tint = null)
        {
            var image = ui.Rect(parent, name, x, y, w, h).gameObject.AddComponent<RawImage>();
            image.texture = texture; image.raycastTarget = false; image.color = tint ?? Color.white;
            if (texture != null)
            {
                float scale = Mathf.Min(w / texture.width, h / texture.height);
                var fit = new Vector2(texture.width * scale, texture.height * scale);
                image.rectTransform.anchoredPosition += new Vector2((w - fit.x) * .5f, -(h - fit.y) * .5f);
                image.rectTransform.sizeDelta = fit;
            }
            return image;
        }
        public static RawImage Hero(UiKit ui, Transform parent, string gender, float x, float y, float w, float h)
        {
            var texture=TownCharacterArt.Hero(gender,"front",false);
            var image=ui.Rect(parent,"Current saved hero",x,y,w,h).gameObject.AddComponent<RawImage>();
            image.texture=texture;image.raycastTarget=false;
            // Both approved 384x512 front images have transparent side gutters (male x83..299,
            // female x93..289). Crop only x0..76 and x308..383; keep the real bottom foot row.
            // Bottom alignment seats the feet on the authored taiji pedestal, without stretching the actor.
            image.uvRect=new Rect(.2f,0,.6f,1);
            if(texture!=null)
            {
                float scale=Mathf.Min(w/(texture.width*.6f),h/texture.height);
                var fit=new Vector2(texture.width*.6f*scale,texture.height*scale);
                image.rectTransform.sizeDelta=fit;
                image.rectTransform.anchoredPosition+=new Vector2((w-fit.x)*.5f,-(h-fit.y));
            }
            return image;
        }
        public static Image Rod(UiKit ui, Transform parent, string name, float x, float y, float w, float h)
        {
            var host = ui.Rect(parent, name, x, y, w, h);
            var image = MemoVisualAssets.Art(host, name + " carved roller", "scroll-header");
            image.preserveAspect = false;
            return image;
        }
    }
}
