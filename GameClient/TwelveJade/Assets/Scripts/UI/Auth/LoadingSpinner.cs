using System.Collections;
using TMPro;
using TwelveJade.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Network.UI
{
    /// <summary>
    /// 全局加载遮罩（M6-03-C）：网络通信期间阻断重复点击。
    /// Show/Hide 静态入口；旋转动画由宿主协程驱动，Hide 后对象整体销毁、无残留。
    /// </summary>
    public static class LoadingSpinner
    {
        const string RootName = "LoadingSpinner";

        static RectTransform root;

        public static bool Visible => root != null;

        public static void Show(string message = "正在与云端通信…")
        {
            Hide(); // 单例：重复 Show 只刷新文案
            var canvas = NetworkUi.OverlayCanvas();
            root = NetworkUi.Kit.Rect(canvas.transform, RootName, 0, 0, 1920, 1080);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            var blocker = root.gameObject.AddComponent<Image>();
            blocker.sprite = UiKit.RoundedSprite();
            blocker.color = new Color(0f, .04f, .03f, .62f);
            blocker.raycastTarget = true; // 阻断一切穿透点击

            var spinner = NetworkUi.Kit.Rect(root, "Spinner", 900, 480, 120, 120);
            var image = spinner.gameObject.AddComponent<Image>();
            image.sprite = UiKit.RoundedSprite();
            image.color = UiKit.Gold;
            image.raycastTarget = false;
            image.preserveAspect = true;

            if (!string.IsNullOrEmpty(message))
                NetworkUi.Kit.Label(root, message, 560, 620, 800, 40, 26, UiKit.Paper,
                    TextAlignmentOptions.Center);

            NetworkUi.Host.StartCoroutine(Spin(spinner));
        }

        public static void Hide()
        {
            if (root != null) UnityEngine.Object.Destroy(root.gameObject);
            root = null;
        }

        static IEnumerator Spin(RectTransform spinner)
        {
            while (spinner != null)
            {
                spinner.Rotate(0f, 0f, 240f * Time.deltaTime);
                yield return null;
            }
        }
    }

    /// <summary>
    /// 屏幕顶部自动淡出的消息提示条（M6-03-C）：「上传成功」「已进入离线模式」等反馈。
    /// </summary>
    public static class ToastNotice
    {
        public static void Show(string message, float seconds = 2.5f)
        {
            var canvas = NetworkUi.OverlayCanvas();
            var toast = NetworkUi.Kit.Panel(canvas.transform, "ToastNotice", 560, 60, 800, 64,
                new Color(.04f, .14f, .11f, .95f), false);
            var label = NetworkUi.Kit.Label(toast.transform, message, 20, 8, 760, 48, 26, UiKit.Gold,
                TextAlignmentOptions.Center);
            var group = toast.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 1f;
            NetworkUi.Host.StartCoroutine(FadeOut(group, label, seconds));
        }

        static IEnumerator FadeOut(CanvasGroup group, TMPro.TMP_Text label, float seconds)
        {
            var shown = 0f;
            while (shown < seconds && group != null)
            {
                shown += Time.unscaledDeltaTime;
                yield return null;
            }
            while (group != null && group.alpha > 0f)
            {
                group.alpha = Mathf.Max(0f, group.alpha - Time.unscaledDeltaTime / .6f);
                yield return null;
            }
            if (group != null) UnityEngine.Object.Destroy(group.gameObject);
            // 末尾引用 label 仅为确认同生命周期销毁，无额外逻辑。
            _ = label;
        }
    }
}
