using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // Focus/press changes the fine edge only. Controls and glyphs never scale or move.
    [DisallowMultipleComponent]
    public sealed class MenuMemoFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        Selectable owner;
        MemoMountGraphic mount;
        CanvasGroup opacity;
        Func<bool> reduceMotion;
        bool hovered, focused, pressed;
        float edgeEmphasis;
        public void Configure(Selectable selectable, MemoMountGraphic frame, Func<bool> reducedMotion)
        {
            owner = selectable; mount = frame; reduceMotion = reducedMotion;
            opacity = GetComponent<CanvasGroup>();
            if (opacity == null) opacity = gameObject.AddComponent<CanvasGroup>();
            Refresh(true);
        }
        void LateUpdate() => Refresh(reduceMotion?.Invoke() ?? true);
        void Refresh(bool immediate)
        {
            if (owner == null || mount == null) return;
            immediate |= reduceMotion?.Invoke() ?? true;
            bool enabled = owner.IsActive() && owner.IsInteractable();
            float target = !enabled ? 0 : pressed ? 1 : focused ? .85f : hovered ? .58f : 0;
            edgeEmphasis = immediate ? target : Mathf.MoveTowards(edgeEmphasis, target, Time.unscaledDeltaTime * 6);
            mount.Emphasis(edgeEmphasis);
            if (opacity != null) opacity.alpha = enabled ? 1 : .48f;
        }
        public void OnPointerEnter(PointerEventData data) { hovered = true; Refresh(false); }
        public void OnPointerExit(PointerEventData data) { hovered = pressed = false; Refresh(false); }
        public void OnPointerDown(PointerEventData data)
        { if (data.button == PointerEventData.InputButton.Left) { pressed = true; Refresh(true); } }
        public void OnPointerUp(PointerEventData data) { pressed = false; Refresh(true); }
        public void OnSelect(BaseEventData data) { focused = true; Refresh(true); }
        public void OnDeselect(BaseEventData data) { focused = pressed = false; Refresh(true); }
        void OnDisable() { hovered = focused = pressed = false; edgeEmphasis = 0; if (mount != null) mount.Emphasis(0); }
        void OnDestroy() { owner = null; mount = null; opacity = null; reduceMotion = null; }
    }
}
