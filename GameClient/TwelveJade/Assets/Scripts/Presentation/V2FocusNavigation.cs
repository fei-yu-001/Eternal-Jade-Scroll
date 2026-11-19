using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    // Scoped to this scroll/page. A confirmation outside the page owns focus while it is open.
    public sealed class V2FocusNavigation : MonoBehaviour
    {
        void Update()
        {
            var keyboard = Keyboard.current; var events = EventSystem.current;
            if (keyboard == null || events == null || !keyboard.tabKey.wasPressedThisFrame) return;
            var current = events.currentSelectedGameObject;
            if (current != null && !current.transform.IsChildOf(transform)) return;
            var items = GetComponentsInChildren<Selectable>(false).Where(s => s.IsActive() && s.IsInteractable() && s.navigation.mode != Navigation.Mode.None).ToArray();
            if (items.Length == 0) return;
            int at = System.Array.FindIndex(items, s => s.gameObject == current);
            int direction = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed ? -1 : 1;
            int next = at < 0 ? (direction > 0 ? 0 : items.Length - 1) : (at + direction + items.Length) % items.Length;
            events.SetSelectedGameObject(items[next].gameObject);
        }
    }
}
