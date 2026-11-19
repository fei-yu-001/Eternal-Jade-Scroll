using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    public static class MobileControls
    {
        public static bool Enabled
        {
            get
            {
#if UNITY_OPENHARMONY && !UNITY_EDITOR
                return true;
#else
                return Application.isMobilePlatform;
#endif
            }
        }

        public static bool TryGroundPress(out Vector2 screen)
        {
            var touch = Touchscreen.current?.primaryTouch;
            if (touch != null && touch.press.wasPressedThisFrame)
            {
                screen = touch.position.ReadValue();
                return true;
            }
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                screen = mouse.position.ReadValue();
                return true;
            }
            screen = default;
            return false;
        }
    }

    // The pad owns one pointer. Other fingers remain available for combat and UI.
    public sealed class MobileMovePad : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        RectTransform rect, knob;
        int pointer = int.MinValue;
        public Vector2 Direction { get; private set; }
        public bool Running => Direction.sqrMagnitude > .72f;

        public static MobileMovePad Create(UiKit ui, Transform parent)
        {
            var root = ui.Rect(parent, "Touch movement", 64, 748, 232, 232);
            var edge = root.gameObject.AddComponent<Image>();
            edge.sprite = UiKit.CircleSprite(); edge.color = new Color(.67f, .57f, .34f, .65f);
            var inset = ui.Rect(root, "Touch jade disc", 6, 6, 220, 220).gameObject.AddComponent<Image>();
            inset.sprite = UiKit.CircleSprite(); inset.color = new Color(.05f, .19f, .16f, .62f); inset.raycastTarget = false;
            GuofengHudArt.Create(ui, root, "Touch jade rim", "hud-avatar", 0, 0, 232, 232);
            var knob = ui.Rect(root, "Touch movement knob", 0, 0, 80, 80);
            knob.anchorMin = knob.anchorMax = knob.pivot = new Vector2(.5f, .5f);
            knob.anchoredPosition = Vector2.zero;
            var pearl = knob.gameObject.AddComponent<Image>();
            pearl.sprite = UiKit.CircleSprite(); pearl.color = new Color(.81f, .89f, .75f, .92f); pearl.raycastTarget = false;
            var hint = ui.Label(parent, "轻推行走 · 推满疾行", 48, 994, 280, 34, 21, UiKit.Paper, TMPro.TextAlignmentOptions.Center);
            hint.raycastTarget = false;
            var pad = root.gameObject.AddComponent<MobileMovePad>(); pad.rect = root; pad.knob = knob;
            return pad;
        }

        public void OnPointerDown(PointerEventData data)
        {
            if (pointer != int.MinValue) return;
            pointer = data.pointerId; Sample(data);
        }
        public void OnDrag(PointerEventData data) { if (data.pointerId == pointer) Sample(data); }
        public void OnPointerUp(PointerEventData data) { if (data.pointerId == pointer) ResetInput(); }
        void Sample(PointerEventData data)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, data.position, data.pressEventCamera, out var point)) return;
            var vector = Vector2.ClampMagnitude((point - rect.rect.center) / 84f, 1f);
            Direction = vector.sqrMagnitude < .025f ? Vector2.zero : vector;
            knob.anchoredPosition = vector * 68f;
        }
        public void ResetInput()
        {
            pointer = int.MinValue; Direction = Vector2.zero;
            if (knob != null) knob.anchoredPosition = Vector2.zero;
        }
        void OnDisable() => ResetInput();
        void OnApplicationFocus(bool focused) { if (!focused) ResetInput(); }
    }
}
