using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
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
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.16f, 1);
            colors.pressedColor = new Color(.65f, .75f, .68f, 1);
            colors.selectedColor = new Color(1.2f, 1.2f, 1.12f, 1);
            colors.disabledColor = new Color(.6f, .6f, .6f, .5f);
            button.colors = colors;
            Label(image.transform, text, 20, 0, w - 40, h, 25, primary ? Ink : Paper, TextAlignmentOptions.MidlineLeft);
            button.interactable = enabled;
            button.onClick.AddListener(() => click?.Invoke());
            button.onClick.AddListener(action);
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
