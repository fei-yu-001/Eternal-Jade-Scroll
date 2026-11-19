using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // Local menu/memo assembly; shared UiKit and the artwork catalogue remain independent.
    public static class MenuMemoArt
    {
        static readonly Color Ink = UiKit.Hex("343C30");

        public static Button Command(UiKit ui, Transform parent, string name, string text, Rect bounds,
            UnityAction action, Func<bool> reducedMotion, Action click = null, float fontSize = 27, bool brush = true)
        {
            var rect = ui.Rect(parent, name, bounds.x, bounds.y, bounds.width, bounds.height);
            var hit = rect.gameObject.AddComponent<Image>(); hit.color = Color.clear;
            string id = ApprovedUiArt.Has("nov9/immortal-command") ? "nov9/immortal-command" : "nov8/moon-jade-command";
            var texture = ApprovedUiArt.Load(id);
            RawImage halo = null;
            if (ApprovedUiArt.Has("nov9/immortal-command-glow"))
            {
                halo = ui.Rect(rect, name + " painted cloud radiance", -bounds.height * .16f, -bounds.height * .16f,
                    bounds.width + bounds.height * .32f, bounds.height * 1.32f).gameObject.AddComponent<JadeCommandPlateGraphic>();
                halo.texture = ApprovedUiArt.Load("nov9/immortal-command-glow"); halo.raycastTarget = false;
            }
            // The entire authored cloud carving is visible. Only the uninscribed jade centre extends.
            var art = ui.Rect(rect, name + " complete cloud jade artwork", 0, 0, bounds.width, bounds.height)
                .gameObject.AddComponent<JadeCommandPlateGraphic>();
            art.texture = texture; art.raycastTarget = false;
            float inset = Mathf.Max(22, bounds.height * .55f);
            var label = ui.Label(rect, text, inset, 4, bounds.width - inset * 2, bounds.height - 8,
                fontSize, UiKit.Hex("253F3B"), TextAlignmentOptions.Center, brush);
            Caption(label, name + " label");
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = hit;
            button.transition = Selectable.Transition.None; button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            button.onClick.AddListener(() => click?.Invoke()); if (action != null) button.onClick.AddListener(action);
            rect.gameObject.AddComponent<MenuCelestialButtonFeedback>().Configure(button, art, halo, label, reducedMotion);
            return button;
        }

        public static Button Choice(UiKit ui, Transform parent, string name, string text, Rect bounds,
            UnityAction action, Func<bool> reducedMotion, bool chosen, Action click = null)
            => BuildButton(ui, parent, name, text, bounds, action, reducedMotion, click, 27,
                MenuMemoSurfaceGraphic.Surface.Choice, MemoMountGraphic.Form.Input, chosen, true);

        static Button BuildButton(UiKit ui, Transform parent, string name, string text, Rect bounds,
            UnityAction action, Func<bool> reducedMotion, Action click, float fontSize,
            MenuMemoSurfaceGraphic.Surface material, MemoMountGraphic.Form edge, bool chosen, bool brush)
        {
            var rect = ui.Rect(parent, name, bounds.x, bounds.y, bounds.width, bounds.height);
            var surface = rect.gameObject.AddComponent<MenuMemoSurfaceGraphic>(); surface.Configure(material, true);
            var mount = Mount(rect, edge, chosen);
            var label = ui.Label(rect, text, 22, 4, bounds.width - 44, bounds.height - 8,
                fontSize, Ink, TextAlignmentOptions.Center, brush);
            Caption(label, name + " label");
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = surface; button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            button.onClick.AddListener(() => click?.Invoke());
            if (action != null) button.onClick.AddListener(action);
            rect.gameObject.AddComponent<MenuMemoFeedback>().Configure(button, mount, reducedMotion);
            return button;
        }

        public static Button Utility(UiKit ui, Transform parent, string name, string text, float x,
            MenuOrnament.Motif motif, UnityAction action, Func<bool> reducedMotion, Action click = null)
        {
            var rect = ui.Rect(parent, name, x, 0, 104, 92);
            var surface = rect.gameObject.AddComponent<MenuMemoSurfaceGraphic>();
            surface.Configure(MenuMemoSurfaceGraphic.Surface.Utility, true);
            string artwork = motif == MenuOrnament.Motif.Compass ? "icon-compass" :
                motif == MenuOrnament.Motif.Book ? "icon-document" : "icon-sword";
            var texture = Resources.Load<Texture2D>("Art/UI/Nov6/" + artwork);
            if (texture != null)
            {
                float scale = 43f / Mathf.Max(texture.width, texture.height);
                float width = texture.width * scale, height = texture.height * scale;
                var icon = ui.Art(rect, texture, (104 - width) * .5f, 5 + (43 - height) * .5f, width, height);
                icon.name = name + " transparent icon"; icon.raycastTarget = false;
            }
            var mount = Mount(rect, MemoMountGraphic.Form.Utility);
            var label = ui.Label(rect, text, 12, 54, 80, 34, 24, Ink, TextAlignmentOptions.Center, true);
            Caption(label, name + " label");
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = surface; button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => click?.Invoke());
            if (action != null) button.onClick.AddListener(action);
            rect.gameObject.AddComponent<MenuMemoFeedback>().Configure(button, mount, reducedMotion);
            return button;
        }

        // Width follows the real text. Keep the established bottom lane, lifetime and font size at the call site.
        public static RectTransform Notice(UiKit ui, Transform parent, string message, Rect lane, float fontSize = 22)
        {
            var rect = ui.Rect(parent, "Notification", lane.x, lane.y, lane.width, lane.height);
            var surface = rect.gameObject.AddComponent<MenuMemoSurfaceGraphic>();
            surface.Configure(MenuMemoSurfaceGraphic.Surface.Notice);
            Mount(rect, MemoMountGraphic.Form.Command);
            var label = ui.Label(rect, message, 22, 8, lane.width - 44, lane.height - 16,
                fontSize, Ink, TextAlignmentOptions.MidlineLeft);
            label.name = "Notification message"; label.richText = false; label.extraPadding = false;
            label.margin = new Vector4(2, 2, 2, 2); SdfInkWeight.Apply(label);
            float preferred = label.GetPreferredValues(message, Mathf.Infinity, Mathf.Infinity).x;
            float width = Mathf.Clamp(Mathf.Ceil(preferred) + 52, Mathf.Min(260, lane.width), lane.width);
            rect.sizeDelta = new Vector2(width, lane.height);
            label.rectTransform.sizeDelta = new Vector2(width - 44, lane.height - 16);
            return rect;
        }

        public static void StyleNameField(TMP_InputField field, Func<bool> reducedMotion)
        {
            if (field == null || field.transform.Find("Memo name silk") != null) return;
            var image = field.GetComponent<Image>();
            if (image != null) image.color = Color.clear;
            var silk = MemoVisualAssets.Rect(field.transform, "Memo name silk"); silk.SetAsFirstSibling();
            silk.gameObject.AddComponent<MenuMemoSurfaceGraphic>().Configure(MenuMemoSurfaceGraphic.Surface.Input);
            var mount = Mount(field.transform, MemoMountGraphic.Form.Input);
            field.transition = Selectable.Transition.None;
            field.gameObject.AddComponent<MenuMemoFeedback>().Configure(field, mount, reducedMotion);
            // Keep the original viewport, 25px text, caret, IME, character limit and all value listeners.
        }

        public static void FramePortrait(PosterCardView poster)
        {
            if (poster == null || poster.transform.Find("Memo fine mounting") != null) return;
            if (poster.Frame != null) poster.Frame.enabled = false;
            Mount(poster.transform, MemoMountGraphic.Form.Portrait);
        }

        static MemoMountGraphic Mount(Transform parent, MemoMountGraphic.Form form, bool chosen = false)
        {
            var mount = MemoVisualAssets.Rect(parent, "Memo fine mounting").gameObject.AddComponent<MemoMountGraphic>();
            mount.Configure(form, chosen); return mount;
        }
        static void Caption(TMP_Text label, string name)
        {
            label.name = name; label.richText = false; label.raycastTarget = false;
            label.enableAutoSizing = false; label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow; label.extraPadding = false;
            label.margin = new Vector4(2, 2, 2, 2); SdfInkWeight.Apply(label);
        }
    }
}
