using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TwelveJade.Network.UI
{
    /// <summary>Shared account/cloud entry: touchable main-menu buttons plus desktop shortcuts.</summary>
    public sealed class NetworkDialogEntry : MonoBehaviour
    {
        TwelveJade.Presentation.FrontEndController controller;
        RectTransform menuTools;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindAnyObjectByType<NetworkDialogEntry>() != null) return;
            new GameObject("NetworkDialogEntry").AddComponent<NetworkDialogEntry>();
        }

        void Update()
        {
            RefreshMenuTools();
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (!keyboard.f1Key.wasPressedThisFrame && !keyboard.f2Key.wasPressedThisFrame) return;
            if (!EntryAllowed()) return;

            if (keyboard.f1Key.wasPressedThisFrame && !AuthDialog.IsOpen) AuthDialog.Open();
            else if (keyboard.f2Key.wasPressedThisFrame && !CloudSaveDialog.IsOpen) CloudSaveDialog.Open();
        }

        void RefreshMenuTools()
        {
            if (controller == null) controller = FindAnyObjectByType<TwelveJade.Presentation.FrontEndController>();
            if (controller == null) return;
            if (menuTools == null && controller.CurrentPage == "menu")
            {
                var kit = NetworkUi.Kit;
                menuTools = kit.Rect(NetworkUi.OverlayCanvas().transform, "Account and cloud entry", 0, 0, 428, 72);
                menuTools.anchorMin = menuTools.anchorMax = new Vector2(.5f, 0);
                menuTools.pivot = new Vector2(.5f, 0); menuTools.anchoredPosition = new Vector2(0, 28);
                TwelveJade.Presentation.AestheticUi.Jade(kit, menuTools, "Menu account", "账号", new Rect(0, 0, 204, 72),
                    () => { if (controller.CurrentPage == "menu") AuthDialog.Open(); }, () => true, fontSize: 28);
                TwelveJade.Presentation.AestheticUi.Jade(kit, menuTools, "Menu cloud saves", "云存档", new Rect(224, 0, 204, 72),
                    () => { if (controller.CurrentPage == "menu") CloudSaveDialog.Open(); }, () => true, fontSize: 28);
            }
            if (menuTools != null)
            {
                bool visible = controller.CurrentPage == "menu" && !AuthDialog.IsOpen && !CloudSaveDialog.IsOpen;
                if (menuTools.gameObject.activeSelf != visible) menuTools.gameObject.SetActive(visible);
            }
        }

        void OnDestroy() { if (menuTools != null) Destroy(menuTools.gameObject); }

        /// <summary>
        /// 只在主菜单与青石镇页接管：战斗/交易/角色创建中途弹账号面板会打断操作，
        /// 而对话框要挂在已有的画布下——一个画布都没有时（例如还没进任何场景）静默不响应，
        /// 比抛一条"找不到 Canvas"的红色报错更有用。
        /// </summary>
        static bool EntryAllowed()
        {
            if (FindAnyObjectByType<Canvas>() == null) return false;
            var controller = FindAnyObjectByType<TwelveJade.Presentation.FrontEndController>();
            if (controller == null) return true;   // 主菜单还没建：允许，登录面板正是给开局用的
            var page = controller.CurrentPage;
            return page == "menu" || page == "town";
        }
    }
}
