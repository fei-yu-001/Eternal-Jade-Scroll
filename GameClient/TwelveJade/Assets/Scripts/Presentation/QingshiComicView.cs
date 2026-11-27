using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TwelveJade.Core;

namespace TwelveJade.Presentation
{
    [Serializable] public sealed class QingshiComicChoice { public string label, reply; }
    [Serializable] public sealed class QingshiComicPanel
    {
        public string speaker, text, art, dialog, balloon;
        public float focusX = .5f, focusY = .5f, zoom = .98f;
        public float balloonY = 166f;
        public QingshiComicChoice[] choices = Array.Empty<QingshiComicChoice>();
    }
    [Serializable] public sealed class QingshiComicScene { public string id; public QingshiComicPanel[] panels; }
    [Serializable] public sealed class QingshiComicBook { public QingshiComicScene[] scenes; }

    // Presentation only: selections are local to this reading; durable progression stays in QingshiStory.
    public sealed class QingshiComicView : MonoBehaviour
    {
        const string ArtDirectory = "Art/Story/Qingshi/";
        const float RevealRate = 36f, EntranceSeconds = .25f;
        UiKit ui; RectTransform root, bubble; QingshiComicScene script; QingshiStoryScene scene;
        Func<bool> reduced; Action finish, click; bool replay, completing, still;
        RawImage artwork; TMP_Text heading, speaker, body, dialogue, caption, counter, hint;
        QingshiComicInkGraphic tail;
        Button next, previous, motion;
        readonly List<Button> options = new List<Button>();
        readonly Dictionary<int, int> selections = new Dictionary<int, int>();
        int index, advancedFrame = -1, revealCount, visibleCount;
        float entered, cameraTime, revealTime;
        bool Quiet => still || (reduced?.Invoke() ?? true);
        public int PanelIndex => index;
        public int PanelCount => script?.panels?.Length ?? 0;
        public bool CanAdvance => next != null && next.interactable;
        public bool HasArtwork => artwork != null && artwork.texture != null;
        public bool IsReplay => replay;
        public bool IsRevealing => visibleCount < revealCount;
        public string SceneId => scene?.Id;
        // Full semantic text is stable while TMP reveals characters; callers never see a truncated reply.
        public string CurrentText => body != null ? body.text : "";
        public string CurrentDialog => dialogue != null ? dialogue.text : "";
        public string CurrentArtId => script == null ? "" : ArtId(script.id, script.panels[index]);
        public bool CompletedForCheck => completing;

        public static QingshiComicScene Load(string id)
        {
            var asset = Resources.Load<TextAsset>("Config/qingshi-story-panels");
            if (asset == null) throw new InvalidOperationException("青石行卷缺少篇章。");
            var book = JsonUtility.FromJson<QingshiComicBook>(asset.text);
            var result = book?.scenes?.SingleOrDefault(s => s != null && s.id == id);
            Validate(result);
            return result;
        }

        // Also used by direct Configure callers: accepting new art IDs must not allow paths or missing resources.
        public static void Validate(QingshiComicScene content)
        {
            if (content == null || !QingshiStory.Scenes.Any(s => s.Id == content.id) ||
                content.panels == null || content.panels.Length == 0)
                throw new InvalidOperationException("青石行卷这一页暂时打不开。");
            foreach (var panel in content.panels)
            {
                if (panel == null || !Finite(panel.zoom) || panel.zoom < .5f || panel.zoom > 1f ||
                    !Finite(panel.focusX) || panel.focusX < 0 || panel.focusX > 1 ||
                    !Finite(panel.focusY) || panel.focusY < 0 || panel.focusY > 1 ||
                    !Finite(panel.balloonY) || panel.balloonY < 132 || panel.balloonY > 500 ||
                    string.IsNullOrWhiteSpace(panel.speaker) ||
                    (string.IsNullOrWhiteSpace(panel.text) && string.IsNullOrWhiteSpace(panel.dialog)) ||
                    (!string.IsNullOrEmpty(panel.balloon) && panel.balloon != "left" && panel.balloon != "right") ||
                    (panel.choices?.Length ?? 0) > 3 || (panel.choices ?? Array.Empty<QingshiComicChoice>()).Any(c =>
                        c == null || string.IsNullOrWhiteSpace(c.label) || string.IsNullOrWhiteSpace(c.reply)))
                    throw new InvalidOperationException("青石行卷这一页暂时打不开。");
                string artId = ArtId(content.id, panel);
                if (!SafeArtId(artId) || Resources.Load<Texture2D>(ArtDirectory + artId) == null)
                    throw new InvalidOperationException("这一卷的画稿尚未备齐。");
            }
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static string ArtId(string sceneId, QingshiComicPanel panel) => string.IsNullOrEmpty(panel.art) ? sceneId : panel.art;
        internal static bool SafeArtId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 96 || value[0] == '-' || value[value.Length - 1] == '-') return false;
            bool dash = false;
            foreach (char c in value)
            {
                if (c == '-') { if (dash) return false; dash = true; }
                else { if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))) return false; dash = false; }
            }
            return true;
        }

        public void Configure(UiKit kit, RectTransform host, QingshiStoryScene definition,
            QingshiComicScene content, bool isReplay, Func<bool> lowMotion, Action completed, Action closed, Action clicked = null)
        {
            Validate(content);
            if (definition == null || definition.Id != content.id) throw new InvalidOperationException("行卷与篇章不相符。");
            ui = kit; root = host; scene = definition; script = content; replay = isReplay;
            reduced = lowMotion; finish = completed; click = clicked;
            advancedFrame = Time.frameCount; // The press finishing the previous scene cannot advance this one.
            var backdrop = root.gameObject.AddComponent<Image>(); backdrop.color = UiKit.Hex("171A18"); backdrop.raycastTarget = true;
            var frame = ui.Stretch(root, "Story artwork viewport"); frame.gameObject.AddComponent<RectMask2D>();
            artwork = ui.Stretch(frame, "Story painted panel").gameObject.AddComponent<RawImage>(); artwork.raycastTarget = false;
            Ink(root, "Story top shade", 0, 0, 1920, 130, QingshiComicInkGraphic.Shape.TopShade);
            Ink(root, "Story subtitle shade", 0, 680, 1920, 400, QingshiComicInkGraphic.Shape.BottomShade);
            heading = Label(root, "Story heading", "青石故里 · " + scene.Title, 64, 26, 1010, 48, 30, UiKit.Paper);
            counter = Label(root, "Story counter", "", 1100, 34, 226, 36, 22, UiKit.Paper, TextAlignmentOptions.Right);
            motion = ReaderButton("Story motion", "静观", 1376, 24, 160, 52, () =>
            {
                still = !still;
                motion.GetComponentInChildren<TMP_Text>().text = still ? "缓动" : "静观";
                if (Quiet) RevealAll();
                ApplyFocus();
            });
            ReaderButton("Story close", "暂收此卷", 1560, 24, 296, 52, () => { if (!completing) closed?.Invoke(); });

            var note = Paper(root, "Story dialogue balloon", 96, 166, 720, 220, QingshiComicPaperGraphic.Surface.Note);
            note.color = UiKit.Paper; note.raycastTarget = false; bubble = note.rectTransform;
            tail = Ink(bubble, "Story balloon tail", 72, 219, 40, 32, QingshiComicInkGraphic.Shape.TailLeft);
            tail.color = UiKit.Hex("EDE3CB");
            speaker = Label(bubble, "Story speaker", "", 32, 20, 656, 38, 25, UiKit.Hex("784D33"));
            dialogue = Label(bubble, "Story dialogue", "", 32, 70, 656, 120, 32, UiKit.Ink);
            dialogue.enableAutoSizing = true; dialogue.fontSizeMin = 26; dialogue.fontSizeMax = 32;
            caption = Label(root, "Story caption", "", 190, 824, 1540, 32, 22, UiKit.Hex("DDC8A0"));
            body = Label(root, "Story narration", "", 190, 864, 1540, 110, 32, UiKit.Paper);
            body.enableAutoSizing = true; body.fontSizeMin = 26; body.fontSizeMax = 32;
            previous = ReaderButton("Story previous", "前一页", 64, 996, 190, 56, Back);
            next = ReaderButton("Story next", "继续", 1518, 996, 338, 56, Advance);
            hint = Label(root, "Story reading hint", "", 300, 1004, 1140, 40, 22, UiKit.Paper, TextAlignmentOptions.Center);
            for (int i = 0; i < 3; i++)
            {
                int selected = i;
                var button = ReaderButton("Story choice " + i, "", 186, 912, 516, 64, () => Choose(selected));
                var label = button.GetComponentInChildren<TMP_Text>();
                label.rectTransform.anchoredPosition = new Vector2(76, 0);
                label.rectTransform.sizeDelta = new Vector2(416, 64);
                label.fontSize = 28; label.fontSizeMin = 22; label.fontSizeMax = 28;
                var seal = Paper(button.transform, "Story choice " + i + " seal", 18, 16, 32, 32, QingshiComicPaperGraphic.Surface.Seal);
                seal.color = UiKit.Hex("943E30"); seal.raycastTarget = false;
                Label(seal.transform, "Story choice " + i + " seal number", new[] { "一", "二", "三" }[i],
                    2, 1, 28, 30, 18, UiKit.Paper, TextAlignmentOptions.Center);
                options.Add(button);
            }
            ShowPanel();
        }
        TMP_Text Label(Transform parent, string name, string text, float x, float y, float width, float height,
            float size, Color color, TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft)
        {
            var label = ui.Label(parent, text, x, y, width, height, size, color, alignment);
            label.name = name; label.richText = false; return label;
        }
        QingshiComicPaperGraphic Paper(Transform parent, string name, float x, float y, float width, float height,
            QingshiComicPaperGraphic.Surface surface)
        {
            var paper = ui.Rect(parent, name, x, y, width, height).gameObject.AddComponent<QingshiComicPaperGraphic>();
            paper.Kind = surface; paper.color = Color.white; return paper;
        }
        Button ReaderButton(string name, string text, float x, float y, float width, float height,
            UnityEngine.Events.UnityAction action)
        {
            var paper = Paper(root, name, x, y, width, height, QingshiComicPaperGraphic.Surface.Slip);
            paper.raycastTarget = true;
            var button = paper.gameObject.AddComponent<Button>(); button.targetGraphic = paper;
            var colors = button.colors;
            colors.normalColor = UiKit.Hex("EDE3CB");
            colors.highlightedColor = UiKit.Hex("FFF4D9");
            colors.selectedColor = UiKit.Hex("E2D1AD");
            colors.pressedColor = UiKit.Hex("CBB98F");
            colors.disabledColor = UiKit.Hex("D1CFC5");
            colors.colorMultiplier = 1; colors.fadeDuration = 0; button.colors = colors;
            var label = Label(paper.transform, name + " caption", text, 22, 0, width - 44, height,
                25, UiKit.Hex("28382F"), TextAlignmentOptions.Center);
            label.enableAutoSizing = true; label.fontSizeMin = 23; label.fontSizeMax = 25;
            // Space belongs to Advance; UGUI must not submit a selected button a second time.
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => { click?.Invoke(); action?.Invoke(); });
            return button;
        }
        QingshiComicInkGraphic Ink(Transform parent, string name, float x, float y, float width, float height, QingshiComicInkGraphic.Shape shape)
        {
            var graphic = ui.Rect(parent, name, x, y, width, height).gameObject.AddComponent<QingshiComicInkGraphic>();
            graphic.Kind = shape; graphic.raycastTarget = false; return graphic;
        }
        void ShowPanel(bool enter = true, bool fullyRead = false)
        {
            var panel = script.panels[index];
            if (enter)
            {
                entered = Time.unscaledTime; cameraTime = 0;
                artwork.texture = Resources.Load<Texture2D>(ArtDirectory + CurrentArtId);
                if (artwork.texture == null) throw new InvalidOperationException("这一卷的画稿尚未备齐。");
            }
            counter.text = (replay ? "回看 · " : "") + (index + 1) + " / " + script.panels.Length;
            bool chosen = selections.TryGetValue(index, out int selection);
            body.text = chosen ? panel.choices[selection].reply : panel.text ?? "";
            dialogue.text = chosen ? "" : panel.dialog ?? "";
            bool hasDialogue = !string.IsNullOrWhiteSpace(dialogue.text);
            bool choicePage = (panel.choices?.Length ?? 0) > 0;
            // Preserve these positions after selecting a reply; only the option signs disappear.
            caption.rectTransform.anchoredPosition = new Vector2(190, choicePage ? -754 : -824);
            body.rectTransform.anchoredPosition = new Vector2(190, choicePage ? -790 : -864);
            body.rectTransform.sizeDelta = new Vector2(1540, choicePage ? 100 : 110);
            caption.text = chosen ? "你的回应" : hasDialogue ? "" : panel.speaker;
            bubble.gameObject.SetActive(hasDialogue);
            if (hasDialogue)
            {
                bool right = panel.balloon == "right";
                speaker.text = panel.speaker;
                // Measure at the authored font size, not the preceding page's autosized result.
                // Keep the authored outer edge fixed so a short right-hand reply stays by its speaker.
                dialogue.enableAutoSizing = false; dialogue.fontSize = dialogue.fontSizeMax;
                float textWidth = Mathf.Max(dialogue.GetPreferredValues(dialogue.text, float.PositiveInfinity, float.PositiveInfinity).x,
                    speaker.GetPreferredValues(speaker.text, float.PositiveInfinity, float.PositiveInfinity).x);
                float balloonWidth = Mathf.Clamp(Mathf.Ceil(textWidth) + 80, 280, 720);
                float innerWidth = balloonWidth - 64;
                float height = Mathf.Clamp(dialogue.GetPreferredValues(dialogue.text, innerWidth, float.PositiveInfinity).y + 100, 154, 340);
                // Leave room for the 32px paper tail and a gap before choices or captions.
                float lowerEdge = choicePage ? 754 : 824;
                float y = Mathf.Min(panel.balloonY, lowerEdge - height - 48);
                bubble.anchoredPosition = new Vector2(right ? 1824 - balloonWidth : 96, -y);
                bubble.sizeDelta = new Vector2(balloonWidth, height);
                speaker.rectTransform.sizeDelta = new Vector2(innerWidth, 38);
                dialogue.rectTransform.sizeDelta = new Vector2(innerWidth, height - 96);
                dialogue.enableAutoSizing = true;
                tail.rectTransform.anchoredPosition = new Vector2(right ? balloonWidth - 112 : 72, -height + 1);
                tail.Kind = right ? QingshiComicInkGraphic.Shape.TailRight : QingshiComicInkGraphic.Shape.TailLeft;
                tail.SetVerticesDirty();
            }
            int count = panel.choices?.Length ?? 0;
            float width = count == 3 ? 516 : 600, left = (1920 - (width * count + 24 * Math.Max(0, count - 1))) * .5f;
            for (int i = 0; i < options.Count; i++)
            {
                bool visible = i < count && !chosen;
                options[i].gameObject.SetActive(visible);
                if (!visible) continue;
                var rect = (RectTransform)options[i].transform;
                rect.anchoredPosition = new Vector2(left + i * (width + 24), -912); rect.sizeDelta = new Vector2(width, 64);
                var label = options[i].GetComponentInChildren<TMP_Text>();
                label.rectTransform.sizeDelta = new Vector2(width - 100, 64); label.text = panel.choices[i].label;
            }
            body.maxVisibleCharacters = dialogue.maxVisibleCharacters = int.MaxValue;
            body.ForceMeshUpdate(true); dialogue.ForceMeshUpdate(true);
            revealCount = Math.Max(body.textInfo.characterCount, hasDialogue ? dialogue.textInfo.characterCount : 0);
            visibleCount = (Quiet || fullyRead) ? revealCount : 0; revealTime = 0;
            ApplyReveal(); ApplyFocus();
        }
        void ApplyReveal()
        {
            body.maxVisibleCharacters = dialogue.maxVisibleCharacters = visibleCount;
            previous.interactable = index > 0 && !completing;
            bool pendingChoice = (script.panels[index].choices?.Length ?? 0) > 0 && !selections.ContainsKey(index);
            next.interactable = !completing && (IsRevealing || !pendingChoice);
            next.GetComponentInChildren<TMP_Text>().text = IsRevealing ? "显全文" : pendingChoice ? "请选择" :
                index == script.panels.Length - 1 ? (replay ? "回到行记" : "记入行记") : "继续";
            hint.text = IsRevealing ? "空格 · 显全文" : pendingChoice ? "选一句话，接着往下读" : "空格 · 继续    ← · 前页";
            foreach (var option in options) option.interactable = !completing && !IsRevealing;
        }
        void RevealAll() { visibleCount = revealCount; ApplyReveal(); }
        public void Choose(int selection)
        {
            if (completing || IsRevealing || advancedFrame == Time.frameCount || selection < 0 ||
                selection >= (script.panels[index].choices?.Length ?? 0) || selections.ContainsKey(index)) return;
            advancedFrame = Time.frameCount;
            selections[index] = selection; ShowPanel(false);
        }
        public void Back()
        {
            if (completing || index <= 0 || advancedFrame == Time.frameCount) return;
            advancedFrame = Time.frameCount; index--; ShowPanel();
        }
        public void Advance()
        {
            if (!CanAdvance || completing || advancedFrame == Time.frameCount) return;
            advancedFrame = Time.frameCount;
            if (IsRevealing) { RevealAll(); return; }
            if (index < script.panels.Length - 1) { index++; ShowPanel(); return; }
            completing = true; next.interactable = false; previous.interactable = false; finish?.Invoke();
        }
        public void RetryCompletion() { completing = false; ShowPanel(false, true); }
        void Update()
        {
            if (script == null || completing) return;
            if (Quiet) { if (IsRevealing) RevealAll(); }
            else
            {
                cameraTime += Time.unscaledDeltaTime;
                if (IsRevealing)
                {
                    revealTime += Time.unscaledDeltaTime;
                    int count = Mathf.Min(revealCount, Mathf.FloorToInt(revealTime * RevealRate));
                    if (count != visibleCount) { visibleCount = count; ApplyReveal(); }
                }
            }
            ApplyFocus();
            var keys = Keyboard.current;
            if (keys == null) return;
            if (keys.rightArrowKey.wasPressedThisFrame || keys.spaceKey.wasPressedThisFrame) Advance();
            else if (keys.leftArrowKey.wasPressedThisFrame) Back();
        }
        void ApplyFocus()
        {
            if (artwork == null || artwork.texture == null) return;
            float t = Mathf.SmoothStep(0, 1, Mathf.Clamp01(cameraTime / 8f));
            UiKit.Cover(artwork); var crop = artwork.uvRect; var panel = script.panels[index];
            float zoom = panel.zoom - .02f * t, width = crop.width * zoom, height = crop.height * zoom;
            artwork.uvRect = new Rect(Mathf.Clamp(panel.focusX - width * .5f + .006f * t, 0, 1 - width),
                Mathf.Clamp(panel.focusY - height * .5f, 0, 1 - height), width, height);
            float opacity = Quiet ? 1 : Mathf.SmoothStep(0, 1, Mathf.Clamp01((Time.unscaledTime - entered) / EntranceSeconds));
            artwork.color = new Color(1, 1, 1, opacity);
        }
    }

    // Runtime UGUI geometry: no generated bitmap or imported material needed for a soft shade or paper tail.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class QingshiComicInkGraphic : MaskableGraphic
    {
        public enum Shape { BottomShade, TopShade, TailLeft, TailRight }
        public Shape Kind;
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); var rect = rectTransform.rect;
            if (Kind == Shape.TailLeft || Kind == Shape.TailRight)
            {
                mesh.AddVert(new Vector2(rect.xMin, rect.yMax), color, Vector2.zero);
                mesh.AddVert(new Vector2(rect.xMax, rect.yMax), color, Vector2.zero);
                mesh.AddVert(new Vector2(Kind == Shape.TailLeft ? rect.xMin + 6 : rect.xMax - 6, rect.yMin), color, Vector2.zero);
                mesh.AddTriangle(0, 1, 2); return;
            }
            var bottom = new Color(.025f, .035f, .03f, Kind == Shape.BottomShade ? .94f : 0);
            var top = new Color(.025f, .035f, .03f, Kind == Shape.TopShade ? .72f : 0);
            mesh.AddVert(new Vector2(rect.xMin, rect.yMin), bottom, Vector2.zero);
            mesh.AddVert(new Vector2(rect.xMin, rect.yMax), top, Vector2.zero);
            mesh.AddVert(new Vector2(rect.xMax, rect.yMax), top, Vector2.zero);
            mesh.AddVert(new Vector2(rect.xMax, rect.yMin), bottom, Vector2.zero);
            mesh.AddTriangle(0, 1, 2); mesh.AddTriangle(0, 2, 3);
        }
    }
}
