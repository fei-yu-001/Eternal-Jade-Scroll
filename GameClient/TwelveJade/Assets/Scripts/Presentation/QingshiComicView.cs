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
        public string speaker, text, art;
        public float focusX = .5f, focusY = .5f, zoom = .98f;
        public QingshiComicChoice[] choices = Array.Empty<QingshiComicChoice>();
    }
    [Serializable] public sealed class QingshiComicScene { public string id; public QingshiComicPanel[] panels; }
    [Serializable] public sealed class QingshiComicBook { public QingshiComicScene[] scenes; }

    // A presentation-only reader. All durable progression belongs to QingshiStory.
    public sealed class QingshiComicView : MonoBehaviour
    {
        UiKit ui; RectTransform root; QingshiComicScene script; QingshiStoryScene scene;
        Func<bool> reduced; Action finish; bool replay, completing, still;
        RawImage artwork; TMP_Text heading, speaker, body, counter;
        Button next, previous, motion;
        readonly List<Button> options = new List<Button>();
        readonly Dictionary<int, int> selections = new Dictionary<int, int>();
        int index, advancedFrame = -1; float entered;
        public int PanelIndex => index;
        public int PanelCount => script?.panels?.Length ?? 0;
        public bool CanAdvance => next != null && next.interactable;
        public bool HasArtwork => artwork != null && artwork.texture != null;
        public bool IsReplay => replay;
        public string SceneId => scene?.Id;
        public string CurrentText => body != null ? body.text : "";
        public bool CompletedForCheck => completing;

        public static QingshiComicScene Load(string id)
        {
            var asset = Resources.Load<TextAsset>("Config/qingshi-story-panels");
            if (asset == null) throw new InvalidOperationException("青石行卷缺少篇章。");
            var book = JsonUtility.FromJson<QingshiComicBook>(asset.text);
            var result = book?.scenes?.SingleOrDefault(s => s.id == id);
            if (result?.panels == null || result.panels.Length == 0 || result.panels.Any(p =>
                p == null || p.zoom < .5f || p.zoom > 1f || p.focusX < 0 || p.focusX > 1 || p.focusY < 0 || p.focusY > 1 ||
                string.IsNullOrWhiteSpace(p.speaker) || string.IsNullOrWhiteSpace(p.text) ||
                (p.choices?.Length ?? 0) > 3 || (p.choices ?? Array.Empty<QingshiComicChoice>()).Any(c =>
                    c == null || string.IsNullOrWhiteSpace(c.label) || string.IsNullOrWhiteSpace(c.reply))))
                throw new InvalidOperationException("青石行卷这一页暂时打不开。");
            foreach (var panel in result.panels)
            {
                string artId = string.IsNullOrEmpty(panel.art) ? id : panel.art;
                if (!QingshiStory.Scenes.Any(s => s.Id == artId) || Resources.Load<Texture2D>("Art/Story/Qingshi/" + artId) == null)
                    throw new InvalidOperationException("这一卷的画稿尚未备齐。");
            }
            return result;
        }
        public void Configure(UiKit kit, RectTransform host, QingshiStoryScene definition,
            QingshiComicScene content, bool isReplay, Func<bool> lowMotion, Action completed, Action closed)
        {
            ui = kit; root = host; scene = definition; script = content; replay = isReplay;
            reduced = lowMotion; finish = completed;
            var backdrop = root.gameObject.AddComponent<Image>(); backdrop.color = UiKit.Hex("171A18"); backdrop.raycastTarget = true;
            heading = ui.Label(root, "青石故里 · " + scene.Title, 64, 24, 1120, 54, 33, UiKit.Paper);
            counter = ui.Label(root, "", 1180, 34, 180, 40, 24, UiKit.Muted, TextAlignmentOptions.Right);
            motion = ui.Button(root, "静观", 1390, 25, 160, 55, () => { still = !still; motion.GetComponentInChildren<TMP_Text>().text = still ? "缓动" : "静观"; });
            var close = ui.Button(root, "暂收此卷", 1580, 25, 270, 55, () => closed?.Invoke()); close.name = "Story close";
            var frame = ui.Rect(root, "Story artwork viewport", 52, 130, 1184, 789);
            frame.gameObject.AddComponent<RectMask2D>();
            artwork = ui.Stretch(frame, "Story painted panel").gameObject.AddComponent<RawImage>();
            artwork.texture = Resources.Load<Texture2D>("Art/Story/Qingshi/" + scene.Id);
            artwork.raycastTarget = false;
            if (artwork.texture == null) throw new InvalidOperationException("青石行卷画稿尚未备齐。");
            var paper = ui.Panel(root, "Story caption paper", 1260, 100, 608, 851, UiKit.Hex("E7DDC4"));
            speaker = ui.Label(paper.transform, "", 34, 38, 540, 80, 29, UiKit.Hex("714631"));
            body = ui.Label(paper.transform, "", 34, 150, 540, 630, 32, UiKit.Ink);
            body.richText = false; speaker.richText = false; body.enableAutoSizing = true; body.fontSizeMin = 28; body.fontSizeMax = 32;
            previous = ui.Button(root, "前一页", 52, 978, 190, 66, Back); previous.name = "Story previous";
            next = ui.Button(root, "继续", 1530, 978, 338, 66, Advance); next.name = "Story next";
            for (int i = 0; i < 3; i++)
            {
                int selected = i;
                var button = ui.Button(root, "", 270 + i * 410, 978, 390, 66, () => Choose(selected));
                button.name = "Story choice " + i;
                var label = button.GetComponentInChildren<TMP_Text>(); label.richText = false; label.enableAutoSizing = true;
                label.fontSizeMin = 20; label.fontSizeMax = 26; options.Add(button);
            }
            ShowPanel();
        }
        void ShowPanel()
        {
            entered = Time.unscaledTime; var panel = script.panels[index];
            artwork.texture = Resources.Load<Texture2D>("Art/Story/Qingshi/" + (string.IsNullOrEmpty(panel.art) ? scene.Id : panel.art));
            counter.text = (replay ? "回看 · " : "") + (index + 1) + " / " + script.panels.Length;
            speaker.text = panel.speaker;
            bool chosen = selections.TryGetValue(index, out int selection);
            body.text = chosen ? panel.choices[selection].reply : panel.text;
            previous.interactable = index > 0 && !completing;
            next.interactable = !completing && ((panel.choices?.Length ?? 0) == 0 || chosen);
            next.GetComponentInChildren<TMP_Text>().text = index == script.panels.Length - 1 ? (replay ? "回到行记" : "记入行记") : "继续";
            for (int i = 0; i < options.Count; i++)
            {
                bool visible = i < (panel.choices?.Length ?? 0) && !chosen;
                options[i].gameObject.SetActive(visible);
                if (visible) options[i].GetComponentInChildren<TMP_Text>().text = panel.choices[i].label;
            }
            ApplyFocus();
        }
        public void Choose(int selection)
        {
            if (completing || selection < 0 || selection >= (script.panels[index].choices?.Length ?? 0) || selections.ContainsKey(index)) return;
            selections[index] = selection; ShowPanel();
        }
        public void Back() { if (!completing && index > 0) { index--; ShowPanel(); } }
        public void Advance()
        {
            if (!CanAdvance || completing || advancedFrame == Time.frameCount) return;
            advancedFrame = Time.frameCount;
            if (index < script.panels.Length - 1) { index++; ShowPanel(); return; }
            completing = true; next.interactable = false; previous.interactable = false; finish?.Invoke();
        }
        public void RetryCompletion() { completing = false; ShowPanel(); }
        void Update()
        {
            if (script == null || completing) return;
            ApplyFocus();
            var keys = Keyboard.current;
            if (keys == null) return;
            if (keys.rightArrowKey.wasPressedThisFrame || keys.spaceKey.wasPressedThisFrame) Advance();
            else if (keys.leftArrowKey.wasPressedThisFrame) Back();
        }
        void ApplyFocus()
        {
            if (artwork == null || artwork.texture == null) return;
            float t = (still || (reduced?.Invoke() ?? true)) ? 0 : Mathf.SmoothStep(0, 1, Mathf.Clamp01((Time.unscaledTime - entered) / 8f));
            UiKit.Cover(artwork); var crop = artwork.uvRect;
            var panel = script.panels[index];
            float zoom = panel.zoom - .025f * t;
            float w = crop.width * zoom, h = crop.height * zoom;
            artwork.uvRect = new Rect(Mathf.Clamp(panel.focusX - w * .5f + .006f * t, 0, 1 - w),
                Mathf.Clamp(panel.focusY - h * .5f, 0, 1 - h), w, h);
        }
    }
}
