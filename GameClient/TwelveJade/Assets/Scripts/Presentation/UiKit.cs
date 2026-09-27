using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // All positions use a 1920x1080 design canvas and top-left coordinates.
    public sealed class UiKit
    {
        public static readonly Color Ink = Hex("142E2B"), Paper = Hex("EDE3CB"), Gold = Hex("C4A46A"),
            Muted = Hex("B5C3B3"), White = Hex("FAF3E4"), Jade = Hex("426F60"), Dark = Hex("102622");
        readonly TMP_FontAsset font;
        readonly Action click;
        public UiKit(TMP_FontAsset font, Action click) { this.font = font; this.click = click; }

        public static Color Hex(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out var value); return value; }

        static Sprite rounded;
        // 程序化圆角矩形（九宫格）：所有面板与按钮共用，染色由 Image.color 负责。
        public static Sprite RoundedSprite()
        {
            if (rounded != null) return rounded;
            const int size = 96, radius = 26;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var cx = Mathf.Max(radius - x, x - (size - 1 - radius), 0);
                var cy = Mathf.Max(radius - y, y - (size - 1 - radius), 0);
                var distance = Mathf.Sqrt(cx * cx + cy * cy);
                var alpha = distance <= radius - 1.5f ? 1f : Mathf.Clamp01((radius + 0.5f - distance) / 2f);
                tex.SetPixel(x, y, new Color(1, 1, 1, alpha));
            }
            tex.Apply(false, true);
            rounded = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect,
                new Vector4(radius, radius, radius, radius));
            rounded.name = "TwelveJade Rounded";
            return rounded;
        }

        static Texture2D ruleEnvelope;
        // 金线包络：中间实、两端渐隐，像卷轴留白，避免通栏直线过于生硬。
        public RawImage Rule(Transform parent, string name, float x, float y, float w, float h, Color color)
        {
            if (ruleEnvelope == null)
            {
                const int width = 256;
                ruleEnvelope = new Texture2D(width, 2, TextureFormat.RGBA32, false);
                for (var i = 0; i < width; i++)
                {
                    var t = i / (width - 1f);
                    var alpha = Mathf.Clamp01(Mathf.Min(t, 1 - t) / .14f);
                    alpha = Mathf.SmoothStep(0, 1, alpha);
                    ruleEnvelope.SetPixel(i, 0, new Color(1, 1, 1, alpha));
                    ruleEnvelope.SetPixel(i, 1, new Color(1, 1, 1, alpha));
                }
                ruleEnvelope.Apply(false, true);
            }
            var rect = Rect(parent, name, x, y, w, h);
            var image = rect.gameObject.AddComponent<RawImage>();
            image.texture = ruleEnvelope; image.color = color; image.raycastTarget = false;
            return image;
        }

        static Texture2D leftFade;
        // 左侧纱罩的横向渐变：左端浓、向右淡出，代替一条竖直硬边。
        public static Texture2D LeftFadeTexture()
        {
            if (leftFade != null) return leftFade;
            const int width = 256;
            leftFade = new Texture2D(width, 2, TextureFormat.RGBA32, false);
            for (var i = 0; i < width; i++)
            {
                var t = i / (width - 1f);
                var alpha = 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01((t - .08f) / .92f));
                alpha *= alpha;
                leftFade.SetPixel(i, 0, new Color(1, 1, 1, alpha));
                leftFade.SetPixel(i, 1, new Color(1, 1, 1, alpha));
            }
            leftFade.Apply(false, true);
            return leftFade;
        }

        public RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        public Image Panel(Transform parent, string name, float x, float y, float w, float h, Color color, bool block = false)
        {
            var image = Rect(parent, name, x, y, w, h).gameObject.AddComponent<Image>();
            image.sprite = RoundedSprite(); image.type = Image.Type.Sliced;
            image.color = color; image.raycastTarget = block;
            return image;
        }

        public TextMeshProUGUI Label(Transform parent, string text, float x, float y, float w, float h,
            float size = 26, Color? color = null, TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
        {
            var label = Rect(parent, "Text", x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font; label.text = text; label.fontSize = size;
            label.color = color ?? White; label.alignment = align;
            label.raycastTarget = false; label.textWrappingMode = TextWrappingModes.Normal;
            return label;
        }

        public Button Button(Transform parent, string text, float x, float y, float w, float h, UnityAction action,
            bool primary = false, bool enabled = true)
        {
            var image = Panel(parent, "Button " + text, x, y, w, h,
                primary ? Gold : new Color(0.12f, 0.25f, 0.22f, .92f), true);
            return StyleButton(image, text, w, h, primary ? Ink : Paper, action, enabled);
        }

        Button StyleButton(Image image, string text, float w, float h, Color textColor, UnityAction action, bool enabled)
        {
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.3f, 1.26f, 1.1f, 1);
            colors.pressedColor = new Color(.7f, .78f, .72f, 1);
            colors.selectedColor = new Color(1.22f, 1.18f, 1.08f, 1);
            colors.disabledColor = new Color(.55f, .55f, .55f, .45f);
            colors.fadeDuration = .12f;
            button.colors = colors;
            var label = Label(image.transform, text, 22, 0, w - 44, h, 25, textColor, TextAlignmentOptions.MidlineLeft);
            AttachHoverGlow(image.transform, label, textColor);
            button.interactable = enabled;
            button.onClick.AddListener(() => click?.Invoke());
            button.onClick.AddListener(action);
            return button;
        }

        // 悬停时文字与描边提亮、轻微右移，给出「活」的反馈而不只是变色。
        static void AttachHoverGlow(Transform root, TMP_Text label, Color baseColor)
        {
            var trigger = root.gameObject.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ =>
            {
                label.color = Color.Lerp(baseColor, Color.white, .65f);
                label.rectTransform.anchoredPosition += new Vector2(5, 0);
            });
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ =>
            {
                label.color = baseColor;
                label.rectTransform.anchoredPosition -= new Vector2(5, 0);
            });
            trigger.triggers.Add(enter);
            trigger.triggers.Add(exit);
        }

        // 主菜单条目：金条 + 大字 + 小注，没有填充方块；悬停时金条延展、整行右移。
        public Button MenuItem(Transform parent, string title, string caption, float x, float y, float w, float h,
            UnityAction action, bool enabled = true)
        {
            var root = Rect(parent, "MenuItem " + title, x, y, w, h);
            var bar = Rect(root, "Bar", 2, 6, 4, h - 12).gameObject.AddComponent<Image>();
            bar.color = new Color(Gold.r, Gold.g, Gold.b, .75f); bar.raycastTarget = false;
            var title_ = Label(root, title, 34, 2, w - 40, h * .58f, 33, Paper);
            var caption_ = Label(root, caption, 35, h * .55f, w - 40, h * .4f, 16, Muted);
            var hit = Rect(root, "Hit", 0, 0, w, h).gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0); hit.raycastTarget = true;
            var button = hit.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            var trigger = hit.gameObject.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ =>
            {
                if (!enabled) return;
                bar.rectTransform.sizeDelta = new Vector2(7, h - 12);
                title_.color = White; caption_.color = Gold;
                root.anchoredPosition += new Vector2(10, 0);
            });
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ =>
            {
                bar.rectTransform.sizeDelta = new Vector2(4, h - 12);
                title_.color = Paper; caption_.color = Muted;
                root.anchoredPosition -= new Vector2(10, 0);
            });
            trigger.triggers.Add(enter);
            trigger.triggers.Add(exit);
            button.interactable = enabled;
            if (!enabled)
            {
                title_.color = new Color(Paper.r, Paper.g, Paper.b, .38f);
                caption_.color = new Color(Muted.r, Muted.g, Muted.b, .45f);
                bar.color = new Color(Gold.r, Gold.g, Gold.b, .25f);
            }
            button.onClick.AddListener(() => { if (enabled) click?.Invoke(); });
            button.onClick.AddListener(() => { if (enabled) action(); });
            return button;
        }

        public TMP_InputField Input(Transform parent, string initial, float x, float y, float w)
        {
            var image = Panel(parent, "Character name", x, y, w, 62, Paper, true);
            var viewport = Rect(image.transform, "Viewport", 16, 6, w - 32, 50);
            viewport.gameObject.AddComponent<RectMask2D>();
            var label = Label(viewport, "", 0, 0, w - 32, 50, 25, Ink, TextAlignmentOptions.MidlineLeft);
            var field = image.gameObject.AddComponent<TMP_InputField>();
            field.textViewport = viewport; field.textComponent = label; field.fontAsset = font;
            field.characterLimit = 16; field.lineType = TMP_InputField.LineType.SingleLine;
            field.richText = false; field.text = initial;
            return field;
        }

        public Slider Slider(Transform parent, string title, float value, float x, float y, UnityAction<float> onChange)
        {
            Label(parent, title, x, y, 230, 40, 26);
            var percentage = Label(parent, Mathf.RoundToInt(value * 100) + "%", x + 680, y, 95, 40, 24, Gold);
            var root = Rect(parent, title + " slider", x + 260, y + 3, 390, 34);
            var background = Panel(root, "Track", 0, 13, 390, 7, new Color(.35f, .45f, .39f), true);
            var fill = Panel(root, "Fill", 0, 13, 390, 7, Gold);
            // Slider 用锚点横向拉伸填充条，宽度必须完全交给锚点，否则会叠上 sizeDelta 溢出轨道。
            fill.rectTransform.sizeDelta = new Vector2(0, fill.rectTransform.sizeDelta.y);
            var handleArea = Rect(root, "Handle area", 0, 0, 390, 34);
            var handle = Panel(handleArea, "Handle", 0, 4, 18, 26, Paper, true);
            var slider = root.gameObject.AddComponent<Slider>();
            slider.fillRect = fill.rectTransform; slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle; slider.minValue = 0; slider.maxValue = 1; slider.value = value;
            slider.onValueChanged.AddListener(v => { percentage.text = Mathf.RoundToInt(v * 100) + "%"; onChange(v); });
            return slider;
        }

        public RawImage Art(Transform parent, Texture2D texture, float x, float y, float w, float h, bool mirror = false)
        {
            var image = Rect(parent, "Artwork", x, y, w, h).gameObject.AddComponent<RawImage>();
            image.texture = texture; image.raycastTarget = false;
            if (texture != null)
            {
                var ratio = (float)texture.width / texture.height;
                var fittedW = Mathf.Min(w, h * ratio); var fittedH = fittedW / ratio;
                image.rectTransform.anchoredPosition += new Vector2((w - fittedW) / 2, -(h - fittedH) / 2);
                image.rectTransform.sizeDelta = new Vector2(fittedW, fittedH);
            }
            image.uvRect = mirror ? new Rect(1, 0, -1, 1) : new Rect(0, 0, 1, 1);
            return image;
        }
    }
}
