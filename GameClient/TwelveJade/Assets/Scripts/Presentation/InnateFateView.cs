using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using TwelveJade.Core;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // An authored, read-only character scroll. Sampling never rolls traits or writes progress.
    public sealed class InnateFateView : MonoBehaviour
    {
        public const float Duration = 3.8f;
        static readonly string[] Elements = { "metal", "wood", "earth", "water", "fire" };
        static readonly string[] Glyphs = { "金", "木", "土", "水", "火" };
        static readonly Color Ink = UiKit.Hex("352B22"), Muted = UiKit.Hex("685541"), Vermilion = UiKit.Hex("883E2B"), Paper = UiKit.Hex("F6E7C9");
        static readonly Vector2 FigureOrigin = new Vector2(300, -142);
        readonly List<CanvasGroup> nodes = new List<CanvasGroup>();
        readonly List<CanvasGroup> cards = new List<CanvasGroup>();
        readonly List<Vector2> cardOrigins = new List<Vector2>();
        IReadOnlyList<string> traits = Array.AsReadOnly(Array.Empty<string>());
        CanvasGroup figureGroup, resultGroup, destinyGroup;
        RectTransform design, figureRect;
        Button skip, replay;
        Func<bool> lowMotion;
        Action closing;
        float clock, sampled;
        bool configured, hold;
        public bool HasRootVision { get; private set; }
        public string RootTitle { get; private set; }
        public IReadOnlyList<string> TraitIds => traits;
        public bool IsSettled { get; private set; }
        public float SampledSeconds => sampled;
        public static bool RootsRevealed(SaveData data) => QingshiStory.CanReplay(data, "livelihood");

        public void Configure(UiKit ui, RectTransform parent, SaveData data, Func<bool> lowMotion, Action close, Action click)
        {
            if (configured) throw new InvalidOperationException("An innate-fate view cannot change its character.");
            if (data == null) throw new ArgumentNullException(nameof(data));
            configured = true; this.lowMotion = lowMotion; closing = close;
            HasRootVision = RootsRevealed(data);
            RootTitle = HasRootVision ? "坎离水火双极灵根" : "五行未测";
            traits = Array.AsReadOnly((data.traits ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray());
            design = ui.Rect(parent, "Innate fate design", 0, 0, 1920, 1080);
            design.anchorMin = design.anchorMax = design.pivot = new Vector2(.5f, .5f);
            design.anchoredPosition = Vector2.zero;
            Artwork(ui, design, "Innate fate authored landscape", "nov27/innate-scroll-stage", new Rect(0, 0, 1920, 1080), true);
            Command(ui, design, "Innate fate close", "收 卷", new Rect(1718, 52, 130, 64), RequestClose, click);

            // A full, transparent figure stands in the painted landscape, without a portrait frame.
            figureRect = ui.Rect(design, "Innate fate figure", FigureOrigin.x, -FigureOrigin.y, 540, 834);
            figureGroup = figureRect.gameObject.AddComponent<CanvasGroup>(); figureGroup.blocksRaycasts = false;
            var portrait = figureRect.gameObject.AddComponent<PosterCardView>();
            portrait.Configure(string.IsNullOrWhiteSpace(data.characterId) ? "qingshi" : data.characterId, data.gender, useFigure: true);
            string characterName = string.IsNullOrWhiteSpace(data.characterName) ? "未定名" : data.characterName;
            bool shortName = characterName.Length <= 6;
            Text(ui, design, "Innate fate character name", shortName ? string.Join("\n", characterName.Select(c => c.ToString())) : characterName,
                shortName ? new Rect(825, 225, 60, 386) : new Rect(748, 240, 244, 142), shortName ? 42 : 34, Ink, TextAlignmentOptions.Top, true);
            Text(ui, design, "Innate fate epigraph", "一\n身\n禀\n赋\n·\n随\n行\n旅\n渐\n明", new Rect(923, shortName ? 232 : 405, 30, 342), 23, Ink, TextAlignmentOptions.Top);
            var seal = ui.Rect(design, "Innate fate hometown seal", 829, 627, 52, 52).gameObject.AddComponent<QingshiComicPaperGraphic>();
            seal.Kind = QingshiComicPaperGraphic.Surface.Seal; seal.color = Vermilion; seal.raycastTarget = false;
            Text(ui, seal.transform, "Innate fate hometown glyph", "青石", new Rect(4, 4, 44, 44), 20, Paper);

            var destiny = CharacterGen.FindDestiny(data.destiny);
            var header = ui.Rect(design, "Innate fate destiny reveal", 1030, 66, 720, 241);
            destinyGroup = header.gameObject.AddComponent<CanvasGroup>(); destinyGroup.blocksRaycasts = false;
            Text(ui, header, "Innate fate heading", "观 照 命 禀", new Rect(0, 0, 470, 40), 25, Muted, TextAlignmentOptions.Left);
            Text(ui, header, "Innate destiny caption", "命\n格", new Rect(3, 66, 32, 78), 22, Vermilion);
            Text(ui, header, "Innate destiny title", destiny?.Name ?? "命星待照", new Rect(60, 47, 600, 104), 70, Ink, TextAlignmentOptions.Left, true);
            Text(ui, header, "Innate destiny description", destiny?.Description ?? "此卷尚未记下命中星象。", new Rect(2, 164, 700, 68), 25, Muted, TextAlignmentOptions.TopLeft);

            var result = ui.Rect(design, "Innate fate result", 1032, 318, 730, 92);
            resultGroup = result.gameObject.AddComponent<CanvasGroup>(); resultGroup.blocksRaycasts = false;
            Text(ui, result, "Innate root title", RootTitle, new Rect(0, 0, 730, 48), 36, Ink, TextAlignmentOptions.Left);
            Text(ui, result, "Innate root boundary", HasRootVision ? "水火初显，凡人之身尚未入门。" : "天地五行藏于人身，缘起未至，灵根未明。", new Rect(0, 54, 730, 32), 22, Muted, TextAlignmentOptions.Left);
            for (int i = 0; i < Elements.Length; i++)
            {
                var node = ui.Rect(design, "Root node " + Elements[i], 1050 + i * 136, 422, 94, 116);
                var group = node.gameObject.AddComponent<CanvasGroup>(); group.blocksRaycasts = false; nodes.Add(group);
                bool visibleRoot = HasRootVision && (Elements[i] == "water" || Elements[i] == "fire");
                Color accent = visibleRoot ? Elements[i] == "water" ? UiKit.Hex("315F5C") : Vermilion : UiKit.Hex("D5BF97");
                var stamp = ui.Rect(node, "Root seal " + Elements[i], 11, 0, 72, 72).gameObject.AddComponent<QingshiComicPaperGraphic>();
                stamp.Kind = QingshiComicPaperGraphic.Surface.Seal; stamp.color = accent; stamp.raycastTarget = false;
                Text(ui, stamp.transform, "Root node " + Elements[i] + " glyph", Glyphs[i], new Rect(8, 5, 56, 61), 42, visibleRoot ? Paper : Muted, TextAlignmentOptions.Center, true);
                Text(ui, node, "Root node " + Elements[i] + " status", HasRootVision ? visibleRoot ? "显影" : "未显" : "未测", new Rect(0, 82, 94, 32), 21, visibleRoot ? accent : Muted);
            }
            Text(ui, design, "Innate traits caption", "先 天 禀 赋", new Rect(1032, 552, 730, 34), 22, Vermilion, TextAlignmentOptions.Left);
            var cardTexture = ApprovedUiArt.Load("nov27/innate-fate-card");
            if (cardTexture == null) throw new InvalidOperationException("Missing authored innate-fate card.");
            int count = traits.Count;
            float spacing = 720f / Mathf.Max(2, count), cardHeight = Mathf.Min(440, (spacing - 24) * cardTexture.height / cardTexture.width);
            float cardWidth = cardHeight * cardTexture.width / cardTexture.height;
            for (int i = 0; i < count; i++)
            {
                var entry = CharacterGen.FindTrait(traits[i]);
                float centre = 1032 + spacing * (i + .5f);
                var rect = ui.Rect(design, "Innate trait " + i, centre - cardWidth * .5f, 601, cardWidth, cardHeight);
                var group = rect.gameObject.AddComponent<CanvasGroup>(); group.blocksRaycasts = false;
                cards.Add(group); cardOrigins.Add(rect.anchoredPosition);
                var sheet = rect.gameObject.AddComponent<RawImage>(); sheet.texture = cardTexture; sheet.raycastTarget = false;
                Text(ui, rect, "Innate trait name " + i, entry?.Name ?? traits[i], new Rect(16, 46, cardWidth - 32, 58), 37, Ink, TextAlignmentOptions.Center, true);
                Text(ui, rect, "Innate trait description " + i, entry?.Description ?? "此项禀赋尚待考证。", new Rect(25, 127, cardWidth - 50, 154), 23, Muted, TextAlignmentOptions.TopLeft);
            }
            if (count == 0) Text(ui, design, "Innate traits pending", "尚未记载", new Rect(1032, 627, 730, 80), 28, Muted, TextAlignmentOptions.Left);
            skip = Command(ui, design, "Innate fate skip", "一 览", new Rect(378, 994, 170, 64), RevealAll, click);
            replay = Command(ui, design, "Innate fate replay", "再 观", new Rect(584, 994, 170, 64), Replay, click);
            Fit(); Sample(0, lowMotion?.Invoke() == true);
        }

        static RawImage Artwork(UiKit ui, Transform parent, string name, string id, Rect bounds, bool cover)
        {
            var image = ui.Rect(parent, name, bounds.x, bounds.y, bounds.width, bounds.height).gameObject.AddComponent<RawImage>();
            image.texture = ApprovedUiArt.Load(id); image.raycastTarget = false;
            if (image.texture == null) throw new InvalidOperationException("Missing authored innate-fate artwork: " + id);
            if (cover) UiKit.Cover(image);
            return image;
        }
        static TMP_Text Text(UiKit ui, Transform parent, string name, string value, Rect rect, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.Center, bool brush = false)
        {
            var text = ui.Label(parent, value, rect.x, rect.y, rect.width, rect.height, size, color, align, brush);
            text.name = name; text.raycastTarget = false; text.enableAutoSizing = true; text.fontSizeMin = size - 3; text.fontSizeMax = size;
            return text;
        }
        static Button Command(UiKit ui, Transform parent, string name, string title, Rect bounds, Action action, Action sound)
        {
            var rect = ui.Rect(parent, name, bounds.x, bounds.y, bounds.width, bounds.height);
            var paper = rect.gameObject.AddComponent<QingshiComicPaperGraphic>(); paper.color = UiKit.Hex("E9D6AF");
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = paper;
            var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = UiKit.Hex("FFF1C8");
            colors.pressedColor = UiKit.Hex("C8B78F"); colors.selectedColor = UiKit.Hex("F8E8B9"); colors.disabledColor = UiKit.Hex("C4BAA3"); colors.fadeDuration = .09f; button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => { if (button == null || !button.isActiveAndEnabled || !button.IsInteractable()) return; sound?.Invoke(); action?.Invoke(); });
            Text(ui, rect, name + " label", title, new Rect(8, 6, bounds.width - 16, bounds.height - 12), 25, Ink);
            return button;
        }
        public void Sample(float seconds, bool reduced)
        {
            if (!configured) return;
            float t = reduced ? Duration : Mathf.Clamp(float.IsNaN(seconds) ? 0 : seconds, 0, Duration);
            sampled = t; IsSettled = t >= Duration;
            float arrival = Ease((t - .10f) / 1.05f);
            figureGroup.alpha = arrival;
            figureRect.anchoredPosition = FigureOrigin + Vector2.down * ((1 - arrival) * 22);
            destinyGroup.alpha = Ease((t - .25f) / .8f);
            resultGroup.alpha = Ease((t - .8f) / .6f);
            for (int i = 0; i < nodes.Count; i++) nodes[i].alpha = Ease((t - 1.0f - i * .16f) / .45f);
            for (int i = 0; i < cards.Count; i++)
            {
                float appear = Ease((t - 2.00f - i * .32f) / .60f);
                cards[i].alpha = appear;
                ((RectTransform)cards[i].transform).anchoredPosition = cardOrigins[i] + Vector2.up * (18 * (1 - appear));
            }
            skip.interactable = !IsSettled; replay.interactable = IsSettled;
        }
        static float Ease(float value) => Mathf.SmoothStep(0, 1, Mathf.Clamp01(value));
        public void RevealAll() { if (!configured) return; hold = true; clock = Duration; Sample(Duration, lowMotion?.Invoke() == true); }
        public void Replay() { if (!configured) return; hold = false; clock = 0; Sample(0, lowMotion?.Invoke() == true); }
        public void RequestClose() { if (configured) closing?.Invoke(); }
        void OnDestroy() { configured = false; closing = null; lowMotion = null; }
        void Update()
        {
            if (!configured) return;
            bool reduced = lowMotion?.Invoke() == true;
            if (reduced) { clock = Duration; hold = true; }
            if (!hold) clock = Mathf.Min(Duration, clock + Time.unscaledDeltaTime);
            Sample(clock, reduced); Fit();
        }
        void Fit()
        {
            if (design == null || !(design.parent is RectTransform parent)) return;
            float scale = Mathf.Min(parent.rect.width / 1920f, parent.rect.height / 1080f);
            if (scale > 0) design.localScale = Vector3.one * scale;
        }
        void OnRectTransformDimensionsChange() { Fit(); }
    }
}
