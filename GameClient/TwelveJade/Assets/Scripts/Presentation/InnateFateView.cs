using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using TwelveJade.Core;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // Read-only presentation of the current character. No roll, reward, cultivation or save write.
    public sealed class InnateFateView : MonoBehaviour
    {
        public const float Duration = 3.8f;
        static readonly string[] Elements = { "metal", "wood", "earth", "water", "fire" };
        static readonly string[] Glyphs = { "金", "木", "土", "水", "火" };
        static readonly Color Pale = UiKit.Hex("EFE6CF"), Gold = UiKit.Hex("C5AD79"), Ash = UiKit.Hex("B7C6BA");
        readonly List<CanvasGroup> nodes = new List<CanvasGroup>();
        readonly List<CanvasGroup> cards = new List<CanvasGroup>();
        readonly List<Vector2> cardOrigins = new List<Vector2>();
        IReadOnlyList<string> traits = Array.AsReadOnly(Array.Empty<string>());
        CanvasGroup figureGroup, resultGroup;
        RectTransform design, figureRect, compassRect;
        InnateFateRingGraphic ring;
        RawImage compass;
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
            var ground = ui.Stretch(design, "Innate fate ink ground").gameObject.AddComponent<Image>();
            ground.color = UiKit.Hex("142C29"); ground.raycastTarget = false;
            var paper = AestheticUi.Paper(ui, design, "Innate fate silk grain", new Rect(0, 0, 1920, 1080));
            paper.color = new Color(.38f, .46f, .38f, .13f); paper.raycastTarget = false;
            ring = ui.Rect(design, "Innate fate orbit", 510, 142, 900, 740).gameObject.AddComponent<InnateFateRingGraphic>();
            ring.raycastTarget = false;
            Text(ui, design, "Innate fate heading", "命 禀 · 灵 根", new Rect(560, 48, 800, 70), 43, Pale);
            Text(ui, design, "Innate fate epigraph", "一身禀赋，随行旅渐明", new Rect(610, 116, 700, 32), 21, Ash);
            Command(ui, design, "Innate fate close", "收 卷", new Rect(1660, 62, 170, 64), RequestClose, click);

            var destiny = CharacterGen.FindDestiny(data.destiny);
            Text(ui, design, "Innate destiny caption", "命 格", new Rect(130, 240, 360, 38), 23, Gold);
            Text(ui, design, "Innate destiny title", destiny?.Name ?? "命星待照", new Rect(130, 286, 360, 60), 35, Pale);
            Text(ui, design, "Innate destiny description", destiny?.Description ?? "此卷尚未记下命中星象。", new Rect(148, 365, 324, 118), 24, Ash, TextAlignmentOptions.TopLeft);
            Text(ui, design, "Innate roots marginal note", HasRootVision ? "坎为水 · 离为火\n两仪相照，一身双极" : "天地五行，藏于人身\n缘起未至，灵盘未应", new Rect(1390, 270, 380, 120), 27, Pale);
            Text(ui, design, "Innate roots knowledge", HasRootVision ? "老翁几句机锋，似醉似醒。\n你只当闲话，尚不知其意。" : "眼下仍是青石镇的寻常人。\n天机藏在往后的行旅里。", new Rect(1390, 412, 380, 86), 23, Ash);

            figureRect = ui.Rect(design, "Innate fate figure", 745, 312, 430, 438);
            figureGroup = figureRect.gameObject.AddComponent<CanvasGroup>(); figureGroup.blocksRaycasts = false;
            var portrait = figureRect.gameObject.AddComponent<PosterCardView>();
            portrait.Configure(string.IsNullOrWhiteSpace(data.characterId) ? "qingshi" : data.characterId, data.gender, useFigure: true);
            var centres = new[] { new Vector2(656, 510), new Vector2(746, 302), new Vector2(960, 212), new Vector2(1174, 302), new Vector2(1264, 510) };
            for (int i = 0; i < Elements.Length; i++)
            {
                var node = ui.Rect(design, "Root node " + Elements[i], centres[i].x - 57, centres[i].y - 57, 114, 146);
                var group = node.gameObject.AddComponent<CanvasGroup>(); group.blocksRaycasts = false; nodes.Add(group);
                bool visibleRoot = HasRootVision && (Elements[i] == "water" || Elements[i] == "fire");
                var coin = ui.Rect(node, "Root seal " + Elements[i], 7, 7, 100, 100).gameObject.AddComponent<InnateFateRingGraphic>();
                coin.Node = true; coin.Accent = visibleRoot ? Elements[i] == "water" ? UiKit.Hex("95C8C9") : UiKit.Hex("D99273") : Gold;
                coin.Revealed = visibleRoot; coin.raycastTarget = false;
                Text(ui, node, "Root node " + Elements[i] + " glyph", Glyphs[i], new Rect(7, 18, 100, 78), 40, visibleRoot ? coin.Accent : Pale);
                Text(ui, node, "Root node " + Elements[i] + " status", HasRootVision ? visibleRoot ? "显影" : "未显" : "未测", new Rect(0, 113, 114, 33), 21, visibleRoot ? coin.Accent : Ash);
            }
            compassRect = ui.Rect(design, "Innate fate authored compass", 780, 760, 76, 76);
            compassRect.pivot = new Vector2(.5f, .5f); compassRect.anchoredPosition = new Vector2(818, -798);
            compass = compassRect.gameObject.AddComponent<RawImage>();
            compass.texture = ApprovedUiArt.Load("nov8/wuxing-lingpan-compass"); compass.raycastTarget = false;
            Text(ui, design, "Innate fate character name", string.IsNullOrWhiteSpace(data.characterName) ? "未定名" : data.characterName, new Rect(876, 775, 280, 45), 27, Pale);
            var result = ui.Rect(design, "Innate fate result", 480, 846, 960, 115);
            resultGroup = result.gameObject.AddComponent<CanvasGroup>(); resultGroup.blocksRaycasts = false;
            Text(ui, result, "Innate root title", RootTitle, new Rect(0, 0, 960, 54), 35, Pale);
            Text(ui, result, "Innate root boundary", HasRootVision ? "双极初显 · 凡人之身，尚未入门" : "灵盘静候 · 行旅中自有机缘", new Rect(0, 67, 960, 40), 22, Ash);
            int count = traits.Count;
            for (int i = 0; i < count; i++)
            {
                var entry = CharacterGen.FindTrait(traits[i]);
                bool right = (i & 1) != 0;
                var rect = ui.Rect(design, "Innate trait " + i, right ? 1390 : 130, 555 + (i / 2) * 174, 380, 164);
                var group = rect.gameObject.AddComponent<CanvasGroup>(); group.blocksRaycasts = false;
                cards.Add(group); cardOrigins.Add(rect.anchoredPosition);
                var sheet = rect.gameObject.AddComponent<QingshiComicPaperGraphic>(); sheet.Kind = QingshiComicPaperGraphic.Surface.Note;
                sheet.color = UiKit.Hex("E6D9B8"); sheet.raycastTarget = false;
                var seal = ui.Rect(rect, "Innate trait seal " + i, 22, 22, 42, 42).gameObject.AddComponent<QingshiComicPaperGraphic>();
                seal.Kind = QingshiComicPaperGraphic.Surface.Seal; seal.color = UiKit.Hex("813F31"); seal.raycastTarget = false;
                Text(ui, seal.transform, "Innate trait seal glyph " + i, "禀", new Rect(0, 0, 42, 42), 25, Pale);
                Text(ui, rect, "Innate trait name " + i, entry?.Name ?? traits[i], new Rect(82, 19, 270, 43), 29, UiKit.Hex("293A32"), TextAlignmentOptions.Left);
                Text(ui, rect, "Innate trait description " + i, entry?.Description ?? "此项禀赋尚待考证。", new Rect(26, 81, 328, 64), 23, UiKit.Hex("3D4739"), TextAlignmentOptions.TopLeft);
            }
            if (count == 0) Text(ui, design, "Innate traits pending", "先天禀赋 · 尚未记载", new Rect(130, 573, 380, 80), 26, Ash);
            Text(ui, design, "Innate trait left caption", "先 天 禀 赋", new Rect(130, 509, 380, 36), 22, Gold);
            if (count > 1) Text(ui, design, "Innate trait right caption", "先 天 禀 赋", new Rect(1390, 509, 380, 36), 22, Gold);
            skip = Command(ui, design, "Innate fate skip", "一 览", new Rect(724, 982, 216, 64), RevealAll, click);
            replay = Command(ui, design, "Innate fate replay", "再 观", new Rect(980, 982, 216, 64), Replay, click);
            Fit(); Sample(0, lowMotion?.Invoke() == true);
        }

        static TMP_Text Text(UiKit ui, Transform parent, string name, string value, Rect rect, float size, Color color, TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var text = AestheticUi.Text(ui, parent, name, value, rect, size, align, color);
            text.raycastTarget = false; text.enableAutoSizing = true; text.fontSizeMin = size - 3; text.fontSizeMax = size;
            return text;
        }
        static Button Command(UiKit ui, Transform parent, string name, string title, Rect bounds, Action action, Action sound)
        {
            var rect = ui.Rect(parent, name, bounds.x, bounds.y, bounds.width, bounds.height);
            var paper = rect.gameObject.AddComponent<QingshiComicPaperGraphic>(); paper.color = UiKit.Hex("E7DCBE");
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = paper;
            var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = UiKit.Hex("FFF1C8");
            colors.pressedColor = UiKit.Hex("C8B78F"); colors.selectedColor = UiKit.Hex("F8E8B9"); colors.disabledColor = UiKit.Hex("B1B0A1"); colors.fadeDuration = .09f; button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => { if (button == null || !button.isActiveAndEnabled || !button.IsInteractable()) return; sound?.Invoke(); action?.Invoke(); });
            Text(ui, rect, name + " label", title, new Rect(8, 6, bounds.width - 16, bounds.height - 12), 25, UiKit.Hex("293A32"));
            return button;
        }
        public void Sample(float seconds, bool reduced)
        {
            if (!configured) return;
            float t = reduced ? Duration : Mathf.Clamp(float.IsNaN(seconds) ? 0 : seconds, 0, Duration);
            sampled = t; IsSettled = t >= Duration;
            float arrival = Ease((t - .10f) / .85f);
            figureGroup.alpha = reduced ? 1 : arrival;
            figureRect.anchoredPosition = new Vector2(745, -312 - (1 - arrival) * 15);
            compass.color = new Color(.66f, .71f, .64f, .85f * Ease(t / .8f));
            compassRect.localRotation = Quaternion.Euler(0, 0, reduced ? 0 : (1 - Ease(t / 1.7f)) * -16);
            for (int i = 0; i < nodes.Count; i++) nodes[i].alpha = Ease((t - .45f - i * .19f) / .45f);
            for (int i = 0; i < cards.Count; i++)
            {
                float appear = Ease((t - 2.00f - i * .32f) / .60f);
                cards[i].alpha = appear;
                ((RectTransform)cards[i].transform).anchoredPosition = cardOrigins[i] + new Vector2((i % 2 == 0 ? 18 : -18) * (1 - appear), -8 * (1 - appear));
            }
            resultGroup.alpha = Ease((t - 2.85f) / .55f);
            ring.Sample(t, reduced, HasRootVision);
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
