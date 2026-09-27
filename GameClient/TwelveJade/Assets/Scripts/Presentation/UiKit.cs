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
        readonly TMP_FontAsset font, brush;
        readonly Action click;
        public UiKit(TMP_FontAsset font, TMP_FontAsset brush, Action click) { this.font = font; this.brush = brush; this.click = click; }

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

        static Texture2D inkStroke;
        static Texture2D InkStrokeTexture()
        {
            if (inkStroke == null)
                inkStroke = Resources.Load<Texture2D>("Art/Fx/ink-stroke");
            return inkStroke;
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
            float size = 26, Color? color = null, TextAlignmentOptions align = TextAlignmentOptions.TopLeft, bool brush = false)
        {
            var label = Rect(parent, "Text", x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = brush ? this.brush : font; label.text = text; label.fontSize = size;
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

        // 主菜单条目：纸质签条 + 水墨笔触底纹 + 墨色文字，没有任何线条装饰；
        // 悬停时墨迹加深、签条轻移。中国风元素直接来自笔触本身的不规则。
        public Button MenuItem(Transform parent, string title, string caption, float x, float y, float w, float h,
            UnityAction action, bool enabled = true)
        {
            var root = Rect(parent, "MenuItem " + title, x, y, w, h);
            var chip = Rect(root, "Chip", 0, 0, w, h).gameObject.AddComponent<Image>();
            chip.sprite = RoundedSprite(); chip.type = Image.Type.Sliced;
            chip.color = enabled ? new Color(.955f, .937f, .875f, .93f) : new Color(.955f, .937f, .875f, .36f);
            chip.raycastTarget = true;
            var stroke = Rect(chip.transform, "Ink stroke", 14, 8, w - 28, h - 16).gameObject.AddComponent<RawImage>();
            stroke.texture = InkStrokeTexture(); stroke.raycastTarget = false;
            // 底纹压得很淡，只留笔触的形；签条文字必须一眼可读。
            stroke.color = enabled ? new Color(.16f, .18f, .17f, .2f) : new Color(.16f, .18f, .17f, .1f);
            var title_ = Label(chip.transform, title, 26, 4, w - 44, h * .56f, 38,
                enabled ? Ink : new Color(Ink.r, Ink.g, Ink.b, .42f), TextAlignmentOptions.TopLeft, true);
            title_.fontStyle = FontStyles.Bold;
            var caption_ = Label(chip.transform, caption, 27, h * .54f, w - 44, h * .42f, 16,
                enabled ? Hex("4A5B51") : new Color(.37f, .43f, .39f, .4f));
            var hit = chip.gameObject.GetComponent<Button>() ?? chip.gameObject.AddComponent<Button>();
            hit.targetGraphic = chip;
            hit.transition = Selectable.Transition.None;
            var trigger = hit.gameObject.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ =>
            {
                if (!enabled) return;
                stroke.color = new Color(.16f, .18f, .17f, .4f);
                title_.color = Hex("7A5A22");
                root.anchoredPosition += new Vector2(10, 0);
            });
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ =>
            {
                if (!enabled) return;
                stroke.color = new Color(.16f, .18f, .17f, .2f);
                title_.color = Ink;
                root.anchoredPosition -= new Vector2(10, 0);
            });
            trigger.triggers.Add(enter);
            trigger.triggers.Add(exit);
            hit.interactable = enabled;
            hit.onClick.AddListener(() => { if (enabled) click?.Invoke(); });
            hit.onClick.AddListener(() => { if (enabled) action(); });
            return hit;
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
