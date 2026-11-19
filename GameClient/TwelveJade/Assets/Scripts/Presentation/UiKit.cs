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
        // AB-2：对话态世界柔化遮罩——25% 黑。把世界压暗但不换色（深绿罩会"深绿压深绿"，发闷）。
        public static readonly Color WorldVeil = new Color(0f, 0f, 0f, .25f);
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

        public RectTransform Stretch(Transform parent, string name)
        {
            var rect = Rect(parent, name, 0, 0, 0, 0);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(.5f, .5f);
            return rect;
        }

        public static void Cover(RawImage image)
        {
            if (image == null || image.texture == null) return;
            var size = image.rectTransform.rect.size;
            if (size.x <= 0 || size.y <= 0 || image.texture.height <= 0) return;
            float source = (float)image.texture.width / image.texture.height, target = size.x / size.y;
            if (source < target)
            {
                float height = source / target;
                image.uvRect = new Rect(0, (1 - height) * .5f, 1, height);
            }
            else
            {
                float width = target / source;
                image.uvRect = new Rect((1 - width) * .5f, 0, width, 1);
            }
        }

        public MenuOrnament JadeFrame(Transform parent, string name, float x, float y, float w, float h,
            Color fill, bool block = false, bool scroll = false)
        {
            var frame = Rect(parent, name, x, y, w, h).gameObject.AddComponent<MenuOrnament>();
            frame.Shape = scroll ? MenuOrnament.Motif.Scroll : MenuOrnament.Motif.Frame;
            frame.Fill = fill;
            frame.raycastTarget = block;
            return frame;
        }

        public Button JadeAction(Transform parent, string name, string text, float x, float y, float w, float h,
            UnityAction action, Func<bool> reduceMotion, bool primary = false, bool enabled = true)
        {
            var frame = JadeFrame(parent, name, x, y, w, h, new Color(.055f, .06f, .055f, primary ? .88f : .78f), true);
            frame.UseGoldButton();
            var button = frame.gameObject.AddComponent<Button>();
            button.targetGraphic = frame; button.transition = Selectable.Transition.None;
            button.interactable = enabled;
            float inset = Mathf.Min(w * .22f, h * .72f);
            var label = Label(frame.transform, text, inset, 0, w - inset * 2, h, primary ? 40 : 27,
                Hex("382314"), TextAlignmentOptions.Center, true);
            TextWrapCompat.SetWrapping(label, false);
            label.enableAutoSizing = true; label.fontSizeMin = primary ? 34 : 20; label.fontSizeMax = primary ? 40 : 27;
            label.name = name + " label";
            label.richText = false;
            // The 96px SDF brush supplies the stroke edge; no dark outline/soft shadow swallows its counters.
            label.extraPadding = true;
            button.onClick.AddListener(() => click?.Invoke()); button.onClick.AddListener(action);
            frame.gameObject.AddComponent<MenuButtonGlow>().Configure(frame, button, reduceMotion, primary);
            return button;
        }

        public Button JadeCommand(Transform parent, string name, string text, float x, float y, float width, float height,
            UnityAction action, Func<bool> reduced, Action chime)
        {
            var rect = Rect(parent, name, x, y, width, height);
            var image = rect.gameObject.AddComponent<Image>(); image.raycastTarget = true;
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            button.transition = Selectable.Transition.None;
            var label = Label(rect, text, width * .18f, 0, width * .64f, height, 40,
                Hex("3E4333"), TextAlignmentOptions.Center, true);
            label.name = name + " label"; label.richText = false;
            label.enableAutoSizing = false; TextWrapCompat.SetWrapping(label, false);
            button.onClick.AddListener(() => chime?.Invoke()); button.onClick.AddListener(action);
            rect.gameObject.AddComponent<JadeCommandView>().Configure(image, button, label, reduced);
            return button;
        }

        public Button JadeUtility(Transform parent, string name, string text, float x, MenuOrnament.Motif motif,
            UnityAction action, Func<bool> reduceMotion)
        {
            var button = JadeAction(parent, name, text, x, 0, 104, 92, action, reduceMotion);
            var plate = button.GetComponent<MenuOrnament>();
            plate.UseUtilityPlate();
            button.GetComponent<MenuButtonGlow>().Configure(plate, button, reduceMotion, false);
            var label = button.GetComponentInChildren<TMP_Text>();
            // Both blocks fit inside the thin nine-slice rails at every canvas scale.
            label.rectTransform.anchoredPosition = new Vector2(14, -48);
            label.rectTransform.sizeDelta = new Vector2(76, 28); label.enableAutoSizing = false; label.fontSize = 24;
            label.color = Ink;
            var icon = Rect(button.transform, name + " ornament", 37, 14, 30, 30).gameObject.AddComponent<MenuOrnament>();
            icon.Shape = motif; icon.color = Color.white; icon.raycastTarget = false;
            return button;
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
            label.raycastTarget = false; TextWrapCompat.SetWrapping(label, true);
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

        // AB-2 面板/签条配方（TB-33/TB-34）：1px Gold 外框 + Ink 88% 底。对话页颜色只取色板——
        // 外框 Gold、底 Ink（88% 不换色相）、字 Paper，不新造色。返回外框：几何就是调用方给的
        // x/y/w/h（验收据此量构图）；1px 描边由外框与内层 fill 的差构成，fill 无交互。
        const float ChipBorder = 1f, ChipPad = 20f;

        // 签条的零件：选中/禁用态要改 fill 与文字的颜色，按名字找子节点太脆，挂个零件类持引用。
        sealed class ChipParts : MonoBehaviour { public Image Fill; public TMP_Text Label; }

        public Image ChipPanel(Transform parent, string name, float x, float y, float w, float h)
        {
            var frame = Panel(parent, name, x, y, w, h, Gold, true);
            var fill = Panel(frame.transform, name + " fill", ChipBorder, ChipBorder,
                w - ChipBorder * 2f, h - ChipBorder * 2f, new Color(Ink.r, Ink.g, Ink.b, .88f));
            frame.gameObject.AddComponent<ChipParts>().Fill = fill;
            return frame;
        }

        // 可点签条：ChipPanel + Paper 正文 + Button。悬停提亮沿用 AttachHoverGlow 同一套做法。
        // TB-36 三态：hover = AttachHoverGlow；选中 = SetChipSelected；禁用 = enabled=false。
        public Button Chip(Transform parent, string name, float x, float y, float w, float h, string text,
            UnityAction action, float fontSize = 22, bool enabled = true)
        {
            var frame = ChipPanel(parent, name, x, y, w, h);
            var parts = frame.GetComponent<ChipParts>();
            var label = Label(frame.transform, text, ChipPad, 0f, w - ChipPad * 2f, h, fontSize, Paper,
                TextAlignmentOptions.MidlineLeft);
            parts.Label = label;
            var button = frame.gameObject.AddComponent<Button>();
            button.targetGraphic = frame;
            button.transition = Selectable.Transition.None;
            AttachHoverGlow(frame.transform, label, Paper);
            button.onClick.AddListener(() => click?.Invoke());
            button.onClick.AddListener(action);
            button.interactable = enabled;
            if (!enabled) SetChipEnabled(frame, false);
            return button;
        }

        // 选中态（TB-36，颜色走 Gold 系）：整条转 Gold 底 + Ink 字——与 primary 按钮同一语言，
        // 键盘导航的当前项在深色条阵里一眼可辨（AB-2"选中态用 Gold 描边 + 底色提亮"）。
        public void SetChipSelected(Image frame, bool selected)
        {
            if (frame == null) return;
            var parts = frame.GetComponent<ChipParts>();
            if (parts == null) return;
            parts.Fill.color = selected ? Gold : new Color(Ink.r, Ink.g, Ink.b, .88f);
            if (parts.Label != null) parts.Label.color = selected ? Ink : Paper;
        }

        // 禁用态（TB-36）：Muted 字 + 底色压暗。可点性由 Button.interactable 管，这里只管长相。
        public void SetChipEnabled(Image frame, bool enabled)
        {
            if (frame == null) return;
            var parts = frame.GetComponent<ChipParts>();
            if (parts == null) return;
            parts.Fill.color = enabled
                ? new Color(Ink.r, Ink.g, Ink.b, .88f) : new Color(Ink.r, Ink.g, Ink.b, .35f);
            if (parts.Label != null) parts.Label.color = enabled ? Paper : Muted;
        }

        // TB-37 交易页取色：以下命名色从 FrontEndTrade 方法体字面量收敛而来，值与原观感一致——
        // 收敛的是真相源（今后交易页换色只改这一处），不是换色本身。对话页仍走 Ink/Paper/Gold/Muted。
        public static readonly Color TradeVeil = new Color(.91f, .88f, .79f, .98f);      // 全屏亮 veil
        public static readonly Color TradePaper = new Color(.93f, .90f, .82f, .96f);     // 双栏面板底
        public static readonly Color TradePaperDim = new Color(.88f, .86f, .79f, .6f);   // 他不收的行
        public static readonly Color TradeRow = new Color(.98f, .96f, .9f, 1f);          // 货架/行囊行底
        public static readonly Color TradeRowPicked = new Color(1f, .95f, .78f, 1f);     // 选中行
        public static readonly Color TradeSettle = new Color(.045f, .115f, .10f, .97f);  // 结算区深 panel
        public static readonly Color TradeTitleGray = new Color(.28f, .32f, .29f, .9f);  // 商人头衔
        public static readonly Color TradeNoticeBrown = new Color(.4f, .3f, .18f, .95f); // 成交提示
        public static readonly Color TradeBlackRed = new Color(.55f, .2f, .16f, .95f);   // 黑市告示
        public static readonly Color TradeEmptyGray = new Color(.35f, .38f, .34f, .85f); // 空状态文案

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
                enabled ? Ink : new Color(Ink.r, Ink.g, Ink.b, .42f), TextAlignmentOptions.Top, true);
            title_.fontStyle = FontStyles.Bold;
            var caption_ = Label(chip.transform, caption, 27, h * .54f, w - 44, h * .42f, 16,
                enabled ? Hex("4A5B51") : new Color(.37f, .43f, .39f, .4f), TextAlignmentOptions.Top);
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

        // UI-01 头像:圆形遮罩为纯几何(与立绘无关),一次烘焙、静态缓存;取景框由离线表
        // Config/portrait-frames.json 给出(运行时不读像素,守卫禁止 isReadable)。
        static Sprite circleSprite;
        public static Sprite CircleSprite()
        {
            if (circleSprite != null) return circleSprite;
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var radius = size / 2f;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var distance = Mathf.Sqrt((x + .5f - radius) * (x + .5f - radius) + (y + .5f - radius) * (y + .5f - radius));
                var alpha = distance <= radius - 1.5f ? 1f : Mathf.Clamp01((radius + .5f - distance) / 2f);
                tex.SetPixel(x, y, new Color(1, 1, 1, alpha));
            }
            tex.Apply(false, true);
            circleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100);
            circleSprite.name = "TwelveJade Circle";
            return circleSprite;
        }

        public sealed class PortraitFrame
        {
            public float X, Y, W, H;
        }

        // 取景框查表:键 = 立绘文件名去掉 "-front" 后缀(如 farmer-male-0)。缺失返回 null,由调用方回退。
        public static PortraitFrame FindPortraitFrame(string configText, string key)
        {
            if (string.IsNullOrEmpty(configText) || string.IsNullOrEmpty(key)) return null;
            var root = JsonUtility.FromJson<PortraitFrameTable>(configText);
            if (root == null || root.entries == null) return null;
            foreach (var e in root.entries)
                if (e.key == key) return new PortraitFrame { X = e.x, Y = e.y, W = e.w, H = e.h };
            return null;
        }

        [Serializable] sealed class PortraitFrameTable { public PortraitFrameEntry[] entries = Array.Empty<PortraitFrameEntry>(); }
        [Serializable] sealed class PortraitFrameEntry { public string key = ""; public float x = 0f, y = 0f, w = 0f, h = 0f; }

        // 头像:正方形 Simple 显示(不用 Sliced,九宫格会把圆拉成椭圆)。
        public RawImage PortraitBadge(Transform parent, Texture2D face, PortraitFrame frame, float x, float y, float size)
        {
            var root = Rect(parent, "Portrait", x, y, size, size);
            var ring = root.gameObject.AddComponent<Image>();
            ring.sprite = CircleSprite(); ring.type = Image.Type.Simple;
            ring.color = Gold; ring.raycastTarget = false;
            var inner = Rect(root, "Portrait face", 3, 3, size - 6, size - 6);
            var image = inner.gameObject.AddComponent<RawImage>();
            image.texture = face != null ? face : Texture2D.whiteTexture;
            image.raycastTarget = false;
            // 取景表缺失时回退整张立绘(不裁切),不再用硬编码取景框:缺失可见,不会静默裁错。
            image.uvRect = frame != null ? new Rect(frame.X, frame.Y, frame.W, frame.H) : new Rect(0, 0, 1, 1);
            return image;
        }

        // 通用条:底 + 填,按 0..1 比例设宽。无业务耦合,不读取任何 Core/存档数据。
        // 填充用锚点拉伸:左端固定在 inset,右端 = 底宽内边距 * ratio,由锚点决定宽度,不叠 sizeDelta。
        public RectTransform StatBar(Transform parent, string name, float x, float y, float w, float h, Color back, Color fill, float ratio)
        {
            var track = Panel(parent, name + " back", x, y, w, h, back).rectTransform;
            const float inset = 2f;
            var fillRect = Rect(track, name + " fill", 0, 0, 0, 0);
            fillRect.anchorMin = new Vector2(0, 0);
            fillRect.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1);
            fillRect.offsetMin = new Vector2(inset, inset);
            fillRect.offsetMax = new Vector2(-inset, -inset);
            var image = fillRect.gameObject.AddComponent<Image>();
            image.color = fill; image.raycastTarget = false;
            fillRect.gameObject.SetActive(ratio > 0f);
            return track;
        }
    }
}
