using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // A held pointer control, not repeated click-based teleports. Deactivation always releases it.
    public sealed class ActionCombatHold : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        System.Action<bool> changed;
        bool held;
        public void Configure(System.Action<bool> callback) { changed = callback; }
        public void OnPointerDown(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) Set(true); }
        public void OnPointerUp(PointerEventData e) { Set(false); }
        public void OnPointerExit(PointerEventData e) { Set(false); }
        void Set(bool value) { if (held == value) return; held = value; changed?.Invoke(value); }
        void OnApplicationFocus(bool focused) { if (!focused) Set(false); }
        void OnDisable() { Set(false); }
        void OnDestroy() { changed = null; }
    }
}
