using UnityEngine;
using UnityEngine.InputSystem;

namespace TwelveJade.Network.UI
{
    /// <summary>Account/cloud buttons on the save-slot page, with guarded slots/town shortcuts.</summary>
    public sealed class NetworkDialogEntry : MonoBehaviour
    {
        TwelveJade.Presentation.FrontEndController controller;
        RectTransform slotTools;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindAnyObjectByType<NetworkDialogEntry>() != null) return;
            new GameObject("NetworkDialogEntry").AddComponent<NetworkDialogEntry>();
        }

        // Page changes and resolution-driven slot redraws finish in FrontEndController.Update.
        void LateUpdate()
        {
            RefreshSlotTools();
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.f1Key.wasPressedThisFrame) OpenDialog(false);
            else if (keyboard.f2Key.wasPressedThisFrame) OpenDialog(true);
        }

        void RefreshSlotTools()
        {
            if (controller == null) controller = FindAnyObjectByType<TwelveJade.Presentation.FrontEndController>();
            var pageRoot = controller != null ? controller.NetworkDialogEntryRoot : null;
            if (slotTools != null && slotTools.parent != pageRoot)
            {
                slotTools.gameObject.SetActive(false);
                Destroy(slotTools.gameObject);
                slotTools = null;
            }
            if (pageRoot == null || !pageRoot.gameObject.activeInHierarchy) return;
            if (slotTools == null)
            {
                var kit = NetworkUi.Kit;
                slotTools = kit.Rect(pageRoot, "Slots network tools", 0, 0, 428, 64);
                slotTools.anchorMin = slotTools.anchorMax = Vector2.one;
                slotTools.pivot = Vector2.one;
                slotTools.anchoredPosition = new Vector2(-40, -35);
                TwelveJade.Presentation.AestheticUi.Jade(kit, slotTools, "Slots account", "账号", new Rect(0, 0, 204, 64),
                    () => OpenDialog(false, true), () => true, fontSize: 28);
                TwelveJade.Presentation.AestheticUi.Jade(kit, slotTools, "Slots cloud saves", "云存档", new Rect(224, 0, 204, 64),
                    () => OpenDialog(true, true), () => true, fontSize: 28);
            }
            bool visible = EntryAllowed();
            if (slotTools.gameObject.activeSelf != visible) slotTools.gameObject.SetActive(visible);
        }

        void OpenDialog(bool cloud, bool slotsOnly = false)
        {
            if (!EntryAllowed() || slotsOnly && controller.CurrentPage != "slots") return;
            if (slotTools != null) slotTools.gameObject.SetActive(false);
            if (cloud) CloudSaveDialog.Open();
            else AuthDialog.Open();
        }

        bool EntryAllowed() => controller != null && controller.CanOpenNetworkDialog &&
            !AuthDialog.IsOpen && !CloudSaveDialog.IsOpen;

        void OnDisable() { if (slotTools != null) slotTools.gameObject.SetActive(false); }
        void OnDestroy() { if (slotTools != null) Destroy(slotTools.gameObject); }
    }
}
