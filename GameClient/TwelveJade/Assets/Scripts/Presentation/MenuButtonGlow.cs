using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    public sealed class MenuButtonGlow : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler, ISubmitHandler
    {
        Graphic ornament;
        Button button;
        Func<bool> reduced;
        Color resting;
        bool hovered, selected, pressed, breathe;
        float submitUntil;
        public void Configure(Graphic target, Button owner, Func<bool> reduceMotion, bool primary)
        {
            ornament = target; button = owner; reduced = reduceMotion; breathe = primary;
            resting = target.color;
            Apply();
        }
        void Update() => Apply();
        void Apply()
        {
            if (ornament == null || button == null) return;
            bool enabled = button.IsInteractable();
            float pulse = enabled && breathe && !(reduced?.Invoke() ?? true)
                ? .035f * (1 + Mathf.Sin(Time.unscaledTime * Mathf.PI * 2 / 1.2f)) : 0;
            bool down = pressed || Time.unscaledTime < submitUntil;
            bool utility = ornament is MenuOrnament menu && menu.IsUtilityPlate;
            Color tint;
            if (!enabled) tint = Color.Lerp(resting, new Color(.51f, .48f, .40f, resting.a), .65f);
            else if (down) tint = utility ? new Color(.80f, .86f, .78f, resting.a) : new Color(.76f, .65f, .44f, resting.a);
            else if (hovered || selected) tint = utility ? new Color(.98f, 1f, .90f, resting.a) : new Color(1, 1, .80f, resting.a);
            else tint = Color.Lerp(resting, new Color(1, .94f, .73f, resting.a), pulse);
            if (ornament.color != tint) ornament.color = tint;
        }
        public void OnPointerEnter(PointerEventData data) { hovered = true; Apply(); }
        public void OnPointerExit(PointerEventData data) { hovered = false; pressed = false; Apply(); }
        public void OnPointerDown(PointerEventData data) { if (data.button == PointerEventData.InputButton.Left) pressed = true; Apply(); }
        public void OnPointerUp(PointerEventData data) { pressed = false; Apply(); }
        public void OnSelect(BaseEventData data) { selected = true; Apply(); }
        public void OnDeselect(BaseEventData data) { selected = false; pressed = false; Apply(); }
        public void OnSubmit(BaseEventData data) { submitUntil = Time.unscaledTime + .12f; Apply(); }
        void OnDisable() { hovered = selected = pressed = false; submitUntil = 0; }
    }
}
