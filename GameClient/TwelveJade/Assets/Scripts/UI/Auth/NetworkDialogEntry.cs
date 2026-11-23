using UnityEngine;
using UnityEngine.InputSystem;

namespace TwelveJade.Network.UI
{
    /// <summary>Title-page account entry and guarded F1 shortcut; cloud services have no menu entry.</summary>
    public sealed class NetworkDialogEntry : MonoBehaviour
    {
        TwelveJade.Presentation.FrontEndController controller;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindAnyObjectByType<NetworkDialogEntry>() != null) return;
            new GameObject("NetworkDialogEntry").AddComponent<NetworkDialogEntry>();
        }

        void OnEnable() => BindController();

        // Check input after the front end has applied page changes, dialogue locks and combat updates.
        void LateUpdate()
        {
            BindController();
            if (controller == null) return;
            controller.SetAccountEntryInteractable(EntryAllowed());
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f1Key.wasPressedThisFrame) OpenAccount();
        }

        void BindController()
        {
            if (controller != null) return;
            controller = FindAnyObjectByType<TwelveJade.Presentation.FrontEndController>();
            if (controller != null) controller.AccountDialogRequested += OpenAccount;
        }

        void OpenAccount()
        {
            if (!EntryAllowed()) return;
            controller.SetAccountEntryInteractable(false);
            AuthDialog.Open();
        }

        bool EntryAllowed() => controller != null && controller.CanOpenNetworkDialog &&
            !AuthDialog.IsOpen && !CloudSaveDialog.IsOpen;

        void OnDisable()
        {
            if (controller != null)
            {
                controller.AccountDialogRequested -= OpenAccount;
                controller.SetAccountEntryInteractable(false);
            }
            controller = null;
        }
    }
}
