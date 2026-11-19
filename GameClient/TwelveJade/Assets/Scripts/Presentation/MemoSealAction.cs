using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // Button owns invocation/navigation. Feedback is static in both motion modes.
    // Borrow the approved RGBA texture; own only the single FullRect sprite per dossier.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button))]
    public sealed class MemoSealAction : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        public const string ArtworkResource = "Art/UI/Nov7/cinnabar-immortal-seal";
        public const float Width = 276f;
        Button button;
        Image surface, focusRule;
        TMP_Text label;
        Sprite owned;
        bool hovered, focused, pressed;
        int lastState = -1;
        public static int OwnedSpriteCount { get; private set; }
        public Image ArtworkForShot => surface;
        public bool HasApprovedArtwork => owned != null && surface != null && surface.sprite == owned;
        public static RectTransform Record(UiKit ui, Transform parent, string name, float x, float y)
        {
            var rect = ui.Rect(parent, name, x, y, 44, 44);
            rect.gameObject.AddComponent<MemoSealGraphic>();
            var ring = MemoVisualAssets.Art(rect, "Borrowed cinnabar imprint", "seal-cinnabar");
            ring.rectTransform.offsetMin = new Vector2(3, 6); ring.rectTransform.offsetMax = new Vector2(-7, -3);
            ring.color = new Color(1, .72f, .52f, .8f); return rect;
        }
        public static Button Create(UiKit ui, Transform parent, string name, string text,
            UnityAction action, Action click, bool enabled)
        {
            var texture = ApprovedUiArt.Load("nov7/cinnabar-immortal-seal"); // One lookup at dossier construction, never in Update.
            float height = texture == null ? 126 : Width * texture.height / texture.width;
            var rect = ui.Rect(parent, name, 0, 0, Width, height);
            var image = rect.gameObject.AddComponent<Image>(); image.type = Image.Type.Simple;
            image.preserveAspect = true; image.raycastTarget = true;
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            button.transition = Selectable.Transition.None; button.interactable = enabled && texture != null;
            var label = ui.Label(rect, text, 0, 0, Width * .64f, height * .56f, 40,
                new Color(.97f, .87f, .64f), TextAlignmentOptions.Center, true);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = label.rectTransform.pivot = new Vector2(.5f, .5f);
            label.rectTransform.anchoredPosition = new Vector2(0, height * .02f);
            label.name = name + " label"; label.richText = false; label.textWrappingMode = TextWrappingModes.NoWrap;
            label.enableAutoSizing = false; label.overflowMode = TextOverflowModes.Overflow; label.extraPadding = true;
            var rule = ui.Panel(rect, "Seal keyboard focus rule", Width * .25f, height * .83f, Width * .5f, 1.4f, new Color(1, .86f, .5f));
            rule.raycastTarget = false;
            button.onClick.AddListener(() => click?.Invoke()); button.onClick.AddListener(action);
            var feedback = rect.gameObject.AddComponent<MemoSealAction>();
            feedback.button = button; feedback.surface = image; feedback.label = label; feedback.focusRule = rule;
            if (texture != null)
            {
                feedback.owned = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
                feedback.owned.name = "Memo approved cinnabar immortal seal"; feedback.owned.hideFlags = HideFlags.DontSave;
                OwnedSpriteCount++; image.sprite = feedback.owned;
            }
            else Debug.LogError("Missing approved entry-seal texture: " + ArtworkResource);
            feedback.Refresh(); return button;
        }
        public void OnPointerEnter(PointerEventData data) { hovered = true; Refresh(); }
        public void OnPointerExit(PointerEventData data) { hovered = pressed = false; Refresh(); }
        public void OnPointerDown(PointerEventData data) { if (data.button == PointerEventData.InputButton.Left) { pressed = true; Refresh(); } }
        public void OnPointerUp(PointerEventData data) { pressed = false; Refresh(); }
        public void OnSelect(BaseEventData data) { focused = true; Refresh(); }
        public void OnDeselect(BaseEventData data) { focused = pressed = false; Refresh(); }
        void OnEnable() { lastState = -1; Refresh(); }
        void OnDisable()
        {
            hovered = focused = pressed = false; lastState = -1;
            if (focusRule != null) focusRule.gameObject.SetActive(false);
        }
        void LateUpdate() { Refresh(); }
        void Refresh()
        {
            if (button == null || surface == null || label == null) return;
            bool enabled = button.IsActive() && button.IsInteractable();
            int state = !enabled ? 0 : pressed && hovered ? 3 : focused ? 2 : hovered ? 1 : 4;
            if (state == lastState) return;
            lastState = state;
            surface.color = !enabled ? new Color(.58f, .58f, .58f, .66f) : state == 3 ? new Color(.78f, .75f, .69f) : Color.white;
            focusRule.gameObject.SetActive(state == 1 || state == 2 || state == 3);
            label.color = !enabled ? new Color(.82f, .77f, .65f, .7f) : state == 1 || state == 2 ? new Color(1, .95f, .77f) : new Color(.97f, .87f, .64f);
        }
        void OnDestroy()
        {
            if (owned != null) { Destroy(owned); owned = null; OwnedSpriteCount--; }
        }
    }
}
