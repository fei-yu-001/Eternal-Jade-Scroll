using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    /// <summary>Visual-only dialogue feedback. Button alone owns submit/click/navigation.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button))]
    public sealed class DialogueSlipFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        Button button;
        DialogueSurface surface;
        TMP_Text label;
        bool hovered, focused, pressed;
        int lastState = -1;

        public void Configure(Button owner, DialogueSurface background, TMP_Text caption)
        {
            button = owner; surface = background; label = caption; lastState = -1; Refresh();
        }
        public void OnPointerEnter(PointerEventData data) { hovered = true; Refresh(); }
        public void OnPointerExit(PointerEventData data) { hovered = pressed = false; Refresh(); }
        public void OnPointerDown(PointerEventData data)
        {
            if (data.button != PointerEventData.InputButton.Left) return;
            pressed = true; Refresh();
        }
        public void OnPointerUp(PointerEventData data) { pressed = false; Refresh(); }
        public void OnSelect(BaseEventData data) { focused = true; Refresh(); }
        public void OnDeselect(BaseEventData data) { focused = pressed = false; Refresh(); }
        void OnEnable() { lastState = -1; Refresh(); }
        void OnDisable()
        {
            hovered = focused = pressed = false; lastState = -1;
            if (surface != null) surface.SetFeedback(0, true, false);
        }
        void LateUpdate() { Refresh(); }
        void Refresh()
        {
            if (button == null || surface == null || label == null) return;
            bool enabled = button.IsActive() && button.IsInteractable();
            // Immediate contrast cues intentionally have no pulse, transform offset or time-based motion.
            // Both normal and reduceMotion retain the same pointer/keyboard/disabled feedback.
            int state = !enabled ? 0 : pressed && hovered ? 3 : focused ? 2 : hovered ? 1 : 4;
            if (state == lastState) return;
            lastState = state;
            surface.SetFeedback(state == 1 ? .65f : state == 2 || state == 3 ? 1 : 0, !enabled, state == 3);
            label.color = !enabled ? new Color(.26f,.31f,.27f,.68f)
                : state == 3 ? new Color(.12f,.24f,.20f,1)
                : new Color(.16f,.23f,.20f,1);
        }
    }
}
