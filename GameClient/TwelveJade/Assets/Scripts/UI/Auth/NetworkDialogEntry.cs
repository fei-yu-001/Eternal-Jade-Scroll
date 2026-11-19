using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TwelveJade.Network.UI
{
    /// <summary>
    /// 对话框入口（M6-03-C 交互闭环）。
    ///
    /// 为什么是热键而不是主菜单按钮：主菜单在 FrontEndController.ShowMenu 里，那不是本轮
    /// 白名单文件（G9 的提交清单不含表现层）。可没有入口的话，AuthDialog / CloudSaveDialog
    /// 做得再全也没有玩家能打开——"面板存在"不等于"闭环成立"。
    /// 架构师排期在主菜单落一个正式按钮之后，这个引导对象直接删掉即可。
    ///
    /// 为什么放在 UI/Auth 而不是 NetworkRuntime：TwelveJade.Network 被本程序集引用，
    /// 反向引用会成编译环。
    /// </summary>
    public sealed class NetworkDialogEntry : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindAnyObjectByType<NetworkDialogEntry>() != null) return;
            new GameObject("NetworkDialogEntry").AddComponent<NetworkDialogEntry>();
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (!keyboard.f1Key.wasPressedThisFrame && !keyboard.f2Key.wasPressedThisFrame) return;
            if (!EntryAllowed()) return;

            if (keyboard.f1Key.wasPressedThisFrame && !AuthDialog.IsOpen) AuthDialog.Open();
            else if (keyboard.f2Key.wasPressedThisFrame && !CloudSaveDialog.IsOpen) CloudSaveDialog.Open();
        }

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
