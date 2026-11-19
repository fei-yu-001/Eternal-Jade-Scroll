using System;
using TMPro;
using TwelveJade.Presentation;
using UnityEngine;
using UnityEngine.UI;
namespace TwelveJade.Network.UI
{
    /// <summary>网络域 UI 的共享底层：画布获取、UiKit 构造、协程宿主、输入框工厂。</summary>
    public static class NetworkUi
    {
        static UiKit kit;
        static TMP_FontAsset inputFont;
        static NetworkUiHost host;

        /// <summary>协程宿主：静态上下文里启动协程用（Spinner 动画、网络调用）。</summary>
        public class NetworkUiHost : MonoBehaviour { }

        public static MonoBehaviour Host
        {
            get
            {
                if (host == null) host = new GameObject("NetworkUiHost").AddComponent<NetworkUiHost>();
                return host;
            }
        }

        /// <summary>弹窗挂靠的画布：优先复用游戏现有 Canvas，批处理/无场景时自建一个覆盖画布。</summary>
        public static Canvas OverlayCanvas()
        {
            var existing = UnityEngine.Object.FindAnyObjectByType<Canvas>();
            if (existing != null) return existing;
            var go = new GameObject("NetworkOverlayCanvas", typeof(Canvas), typeof(CanvasScaler));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            return canvas;
        }

        /// <summary>
        /// UiKit 构造：字体用与 FrontEndController 相同的动态字资产配方
        /// （Resources/Fonts/NotoSerifCJKsc-Regular），批处理与运行时一致。
        /// </summary>
        public static UiKit Kit
        {
            get
            {
                if (kit != null) return kit;
                var source = Resources.Load<Font>("Fonts/NotoSerifCJKsc-Regular");
                if (source == null) throw new InvalidOperationException("缺少 Resources/Fonts/NotoSerifCJKsc-Regular 字体。");
                var runtime = TMP_FontAsset.CreateFontAsset(source, 48, 5,
                    UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 2048, 2048,
                    AtlasPopulationMode.Dynamic, true);
                runtime.name = "TwelveJade Network Font";
                kit = new UiKit(runtime, runtime, null);
                return kit;
            }
        }

        /// <summary>输入框字体：与 Kit 同源字体单独建一份动态字资产（密码遮罩需要显式 fontAsset），全程缓存。</summary>
        public static TMP_FontAsset InputFont
        {
            get
            {
                if (inputFont != null) return inputFont;
                var source = Resources.Load<Font>("Fonts/NotoSerifCJKsc-Regular");
                if (source == null) throw new InvalidOperationException("缺少 Resources/Fonts/NotoSerifCJKsc-Regular 字体。");
                inputFont = TMP_FontAsset.CreateFontAsset(source, 48, 5,
                    UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 2048, 2048,
                    AtlasPopulationMode.Dynamic, true);
                inputFont.name = "TwelveJade Network Input Font";
                return inputFont;
            }
        }

        /// <summary>
        /// 输入框工厂：带占位符文本与密码遮罩（UiKit.Input 不支持占位符与 ContentType）。
        /// </summary>
        public static TMP_InputField CreateInput(Transform parent, string placeholder, string name,
            float x, float y, float w, bool password, int characterLimit)
        {
            var panel = Kit.Panel(parent, name, x, y, w, 62, UiKit.Paper, true);
            var viewport = Kit.Rect(panel.transform, "Viewport", 16, 6, w - 32, 50);
            viewport.gameObject.AddComponent<RectMask2D>();
            var text = Kit.Label(viewport, "", 0, 0, w - 32, 50, 25, UiKit.Ink, TextAlignmentOptions.MidlineLeft);
            var hint = Kit.Label(viewport, placeholder, 0, 0, w - 32, 50, 22,
                new Color(.55f, .58f, .54f), TextAlignmentOptions.MidlineLeft);
            var field = panel.gameObject.AddComponent<TMP_InputField>();
            field.textViewport = viewport;
            field.textComponent = text;
            field.fontAsset = InputFont;
            field.placeholder = hint;
            field.characterLimit = characterLimit;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.richText = false;
            field.contentType = password ? TMP_InputField.ContentType.Password : TMP_InputField.ContentType.Standard;
            field.text = "";
            return field;
        }

        /// <summary>红色错误提示标签（各对话框共用样式）。</summary>
        public static TextMeshProUGUI ErrorLabel(Transform parent, string name, float x, float y, float w, float h)
        {
            var label = Kit.Label(parent, "", x, y, w, h, 22,
                new Color(.86f, .22f, .18f), TextAlignmentOptions.Midline);
            label.gameObject.name = name;
            label.textWrappingMode = TextWrappingModes.Normal;
            return label;
        }
    }
}
