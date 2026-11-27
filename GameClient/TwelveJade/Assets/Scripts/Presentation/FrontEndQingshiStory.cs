using System;
using System.Linq;
using TMPro;
using TwelveJade.Core;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace TwelveJade.Presentation
{
    public sealed partial class FrontEndController
    {
        public QingshiComicView QingshiComicForCheck => modal != null ? modal.GetComponentInChildren<QingshiComicView>() : null;
        public void ShowQingshiStoryPanel()
        {
            if (!ChapterModalAlive) return;
            int current = QingshiStory.CurrentIndex(activeSave);
            ResetChapterBody(current >= QingshiStory.Scenes.Count ? "青石故里 · 卷一已记" : "青石故里 · " + QingshiStory.Scenes[Math.Max(0, current)].Title);
            // Read only the opening artwork for the directory; loading every page here would turn
            // a small preview into a full-book texture load. Unread scenes never get a preview.
            var asset = Resources.Load<TextAsset>("Config/qingshi-story-panels");
            QingshiComicBook book = null;
            if (asset != null)
            {
                try { book = JsonUtility.FromJson<QingshiComicBook>(asset.text); }
                catch (ArgumentException) { /* The reader reports invalid content when opened. */ }
            }
            float y = 0, currentTop = 0; int act = -1;
            for (int i = 0; i < QingshiStory.Scenes.Count; i++)
            {
                var scene = QingshiStory.Scenes[i]; bool done = QingshiStory.CanReplay(activeSave, scene.Id);
                bool ready = QingshiStory.CanEnter(activeSave, scene.Id); string id = scene.Id;
                if (scene.Act != act)
                {
                    act = scene.Act;
                    string actName = act == 1 ? "第一幕" : act == 2 ? "第二幕" : act == 3 ? "第三幕" : "第" + act + "幕";
                    StoryDirectoryText(chapterBodyHost, "Story act " + act, actName, 16, y + 2, 900, 34, 25, ImmortalUi.Ink);
                    y += 44;
                }
                bool known = done || ready;
                float height = known ? 138 : 108;
                var plate = ui.Rect(chapterBodyHost, "Story chapter " + id, 8, y, 930, height).gameObject.AddComponent<QingshiComicPaperGraphic>();
                plate.color = Color.white; plate.raycastTarget = true;
                var button = plate.gameObject.AddComponent<Button>(); button.targetGraphic = plate;
                var colors = button.colors;
                colors.normalColor = ready ? UiKit.Hex("F2E6C9") : UiKit.Hex("E6DFCD");
                colors.highlightedColor = UiKit.Hex("FFF3D6"); colors.selectedColor = UiKit.Hex("EEE1BE");
                colors.pressedColor = UiKit.Hex("D7C5A0"); colors.disabledColor = UiKit.Hex("D8D4C8");
                colors.colorMultiplier = 1; colors.fadeDuration = 0; button.colors = colors;
                button.interactable = known;
                button.onClick.AddListener(() => { PlayClick(); CloseChapterThen(() => OpenStoryDirectoryEntry(id)); });
                // The plain accent and explicit state remain readable without motion or hover.
                if (ready)
                {
                    var accent = ui.Rect(plate.transform, "Story current accent", 8, 12, 2, height - 24).gameObject.AddComponent<Image>();
                    accent.color = UiKit.Hex("943E30"); accent.raycastTarget = false;
                }
                float textX = known ? 182 : 22;
                StoryDirectoryText(plate.transform, "Story chapter " + id + " title",
                    (i + 1).ToString("D2") + " · " + (known ? scene.Title : "尚未记下"), textX, 12, 546, 36, 27, ImmortalUi.Ink);
                StoryDirectoryText(plate.transform, "Story chapter " + id + " status",
                    done ? "已记 · 回看" : ready ? "正在续写" : "未启", 742, 14, 166, 34, 23, UiKit.Hex("514B3D"), TextAlignmentOptions.Right);
                if (known)
                {
                    var opening = book?.scenes?.FirstOrDefault(s => s != null && s.id == id)?.panels?.FirstOrDefault();
                    var preview = ui.Rect(plate.transform, "Story preview " + id, 18, 18, 144, 96).gameObject.AddComponent<RawImage>();
                    string artId = string.IsNullOrEmpty(opening?.art) ? id : opening.art;
                    preview.texture = QingshiComicView.SafeArtId(artId)
                        ? Resources.Load<Texture2D>("Art/Story/Qingshi/" + artId) : null;
                    preview.raycastTarget = false;
                    if (preview.texture != null) UiKit.Cover(preview);
                    else preview.gameObject.SetActive(false);
                    // Full scene summaries contain later events: only a finished scene may show them.
                    string excerpt = done ? scene.Summary : opening?.text;
                    if (string.IsNullOrWhiteSpace(excerpt)) excerpt = "从这一页开始，续写故里的日常。";
                    StoryDirectoryText(plate.transform, "Story chapter " + id + " summary", StoryDirectoryExcerpt(excerpt),
                        textX, 57, 726, 66, 24, UiKit.Hex("514B3D"));
                }
                else
                    StoryDirectoryText(plate.transform, "Story chapter " + id + " summary", "读完前段后，再启新页。",
                        textX, 56, 700, 40, 21, UiKit.Hex("514B3D"));
                if (ready)
                {
                    currentTop = y;
                    var lead = QingshiStoryJourney.Current(activeSave);
                    ChapterAction(string.IsNullOrEmpty(lead?.NpcId) ? "续读 · " + scene.Title : "寻访 · " + lead.NpcName,
                        () => CloseChapterThen(() => OpenStoryDirectoryEntry(id)));
                }
                y += height + 12;
            }
            chapterLog.text = "已记 " + Math.Max(0, current) + " / " + QingshiStory.Scenes.Count + " 段 · 已记可回看 · 未至不先叙";
            FitChapterBody(y);
            Canvas.ForceUpdateCanvases();
            float overflow = Mathf.Max(0, chapterBodyHost.rect.height - chapterScroll.viewport.rect.height);
            // Keep the next readable card in view; the fixed action below also survives manual scrolling.
            float offset = Mathf.Clamp(currentTop - 44, 0, overflow);
            chapterScroll.verticalNormalizedPosition = overflow > 0 ? 1 - offset / overflow : 1;
        }

        TMP_Text StoryDirectoryText(Transform parent, string name, string value, float x, float y, float width, float height,
            float size, Color ink, TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
        {
            var label = ui.Label(parent, value, x, y, width, height, size, ink, align);
            label.name = name; label.richText = false;
            label.enableAutoSizing = true; label.fontSizeMin = size - 2; label.fontSizeMax = size;
            return label;
        }

        static string StoryDirectoryExcerpt(string value)
        {
            value = value.Replace("\r", "").Replace("\n", " ").Trim();
            int stop = value.IndexOf('。');
            if (stop >= 0 && stop < 54) return value.Substring(0, stop + 1);
            return value.Length <= 54 ? value : value.Substring(0, 53) + "…";
        }

        public bool ShowQingshiComic(string id, bool journey = false)
        {
            if (activeSave == null || page != "town" || WorldDialogueInputBlocked || WorldCombatActive) return false;
            var scene = QingshiStory.Scenes.FirstOrDefault(s => s.Id == id);
            bool replay = QingshiStory.CanReplay(activeSave, id);
            if (scene == null || (!replay && !QingshiStory.CanEnter(activeSave, id))) return false;
            QingshiComicScene script;
            try { script = QingshiComicView.Load(id); }
            catch (Exception ex) { Notify(ex.Message); return false; }
            var owner = activeSave;
            CloseInventory(); CloseChapterPanel(); CloseModal(); CloseWorldCombatWheel(); StopDialogueMovement();
            var group = content.GetComponent<CanvasGroup>(); group.interactable = false; group.blocksRaycasts = false;
            modal = ui.Stretch(screenCanvas, "Qingshi story comic");
            var veil = modal.gameObject.AddComponent<Image>(); veil.color = UiKit.Hex("171A18"); veil.raycastTarget = true;
            var host = modal;
            var design = ui.Rect(host, "Story design", 0, 0, 1920, 1080);
            design.anchorMin = design.anchorMax = new Vector2(.5f, .5f);
            design.pivot = new Vector2(.5f, .5f); design.anchoredPosition = Vector2.zero;
            var view = design.gameObject.AddComponent<QingshiComicView>();
            view.Configure(ui, design, scene, script, replay, () => settings.reduceMotion, () =>
            {
                if (modal != host || page != "town" || !ReferenceEquals(owner, activeSave)) return;
                if (!replay && !QingshiStory.Complete(owner, id, repository.Write, out string reason))
                { Notify(reason); view.RetryCompletion(); return; }
                CloseModal(); FinishQingshiJourney(replay, journey);
            }, () => { if (modal == host) CloseModal(); }, PlayClick);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            return true;
        }
    }
}
