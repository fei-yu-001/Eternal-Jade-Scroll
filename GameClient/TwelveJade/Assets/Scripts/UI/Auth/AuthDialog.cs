using System;
using System.Collections;
using TMPro;
using TwelveJade.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Network.UI
{
    /// <summary>
    /// 登录/注册对话框（M6-03-C · M8）：暗色磨砂遮罩 + 登录/注册/离线游玩三主按钮 +
    /// 实时错误提示标签。纯 C# UGUI 构建，样式走 UiKit；网络调用经 AuthService，
    /// 期间以 LoadingSpinner 阻断重复点击。
    /// </summary>
    public static class AuthDialog
    {
        static RectTransform openDialog;

        public static bool IsOpen => openDialog != null;

        /// <summary>打开对话框；onClosed 在对话框因任何方式关闭后回调（可为 null）。</summary>
        public static void Open(Action onClosed = null)
        {
            if (openDialog != null) Close(); // 单例
            var kit = NetworkUi.Kit;
            var canvas = NetworkUi.OverlayCanvas();

            openDialog = kit.Rect(canvas.transform, "AuthDialog", 0, 0, 1920, 1080);
            openDialog.anchorMin = Vector2.zero;
            openDialog.anchorMax = Vector2.one;
            openDialog.offsetMin = Vector2.zero;
            openDialog.offsetMax = Vector2.zero;

            var backdrop = openDialog.gameObject.AddComponent<Image>();
            backdrop.sprite = UiKit.RoundedSprite();
            backdrop.type = Image.Type.Sliced;
            backdrop.color = new Color(0f, .04f, .03f, .82f);
            backdrop.raycastTarget = true; // 磨砂遮罩：挡住下层一切点击

            kit.Label(openDialog, "山河无定数，凡尘亦长生。", 560, 300, 800, 46, 30, UiKit.Gold,
                TextAlignmentOptions.Center, true);

            var username = NetworkUi.CreateInput(openDialog, "用户名（3–32 字符）", "UsernameInput",
                660, 380, 600, password: false, characterLimit: 32);
            var password = NetworkUi.CreateInput(openDialog, "密码（6–64 字符）", "PasswordInput",
                660, 460, 600, password: true, characterLimit: 64);

            var errorLabel = NetworkUi.ErrorLabel(openDialog, "AuthErrorLabel", 560, 545, 800, 56);

            void RunAuth(Func<AuthService, Action<AuthResult>, IEnumerator> call)
            {
                errorLabel.text = "";
                LoadingSpinner.Show();
                NetworkUi.Host.StartCoroutine(RunAuthCall(call, username.text, password.text, errorLabel,
                    onSuccess: () =>
                    {
                        LoadingSpinner.Hide();
                        Close();
                        ToastNotice.Show("欢迎，" + LocalCredentialManager.Username);
                        onClosed?.Invoke();
                    }, onFail: () => LoadingSpinner.Hide()));
            }

            kit.Button(openDialog, "登 录", 560, 620, 300, 64,
                () => RunAuth((auth, done) => auth.Login(username.text, password.text, done)), primary: true);
            kit.Button(openDialog, "注 册", 910, 620, 300, 64,
                () => RunAuth((auth, done) => auth.Register(username.text, password.text, done)));
            kit.Button(openDialog, "离线游玩", 560, 706, 650, 58, () =>
            {
                LocalCredentialManager.EnterOfflineLocal();
                ToastNotice.Show("已进入离线模式：存档只写本地，随时可再联网同步");
                Close();
                onClosed?.Invoke();
            });
            kit.Button(openDialog, "告 辞", 1260, 620, 100, 64, Close);

            // 关闭钩子挂在根节点销毁上：无论从哪条路退出都能通知调用方。
            openDialog.gameObject.AddComponent<AuthDialogCloseHook>().OnClosed = onClosed;
        }

        public static void Close()
        {
            if (openDialog == null) return;
            var dying = openDialog.gameObject;
            openDialog = null;
            UnityEngine.Object.Destroy(dying);
        }

        static IEnumerator RunAuthCall(Func<AuthService, Action<AuthResult>, IEnumerator> call,
            string username, string password, TextMeshProUGUI errorLabel, Action onSuccess, Action onFail)
        {
            var runtime = NetworkRuntime.Instance;
            if (runtime == null)
            {
                errorLabel.text = "网络运行时未就绪。";
                onFail();
                yield break;
            }
            AuthResult result = null;
            yield return call(runtime.Auth, r => result = r);
            LoadingSpinner.Hide();
            if (result == null)
            {
                errorLabel.text = "无响应。";
                onFail();
                yield break;
            }
            if (result.Success)
            {
                onSuccess();
                yield break;
            }
            errorLabel.text = result.NetworkFailure
                ? "连不上服务器——可点【离线游玩】继续单机"
                : result.IsLockedOut ? result.Message : result.Message;
            onFail();
        }

        /// <summary>关闭通知钩子：Destroy 时补发 onClosed（组件销毁顺序安全）。</summary>
        sealed class AuthDialogCloseHook : MonoBehaviour
        {
            public Action OnClosed;
            void OnDestroy() => OnClosed?.Invoke();
        }
    }
}
