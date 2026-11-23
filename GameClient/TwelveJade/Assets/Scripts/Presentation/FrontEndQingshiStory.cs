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
            chapterBody.text = "";
            for (int i = 0; i < QingshiStory.Scenes.Count; i++)
            {
                var scene = QingshiStory.Scenes[i]; bool done = QingshiStory.CanReplay(activeSave, scene.Id);
                bool ready = QingshiStory.CanEnter(activeSave, scene.Id); string id = scene.Id;
                var button = AestheticUi.Jade(ui, chapterBodyHost, "Story chapter " + id,
                    (done ? "回看 · " : ready ? "续写 · " : "未至 · ") + scene.Title,
                    new Rect(8, i * 90, 930, 74), () => CloseChapterThen(() => ShowQingshiComic(id)), () => settings.reduceMotion, PlayClick, 26);
                button.interactable = done || ready;
            }
            chapterLog.text = "已记 " + Math.Max(0, current) + " / " + QingshiStory.Scenes.Count + " 段 · 逐段落卷 · 完成后可回看";
            FitChapterBody(QingshiStory.Scenes.Count * 90);
        }
        public bool ShowQingshiComic(string id)
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
                CloseModal(); ShowTownChapterPanel();
            }, () => { if (modal == host) CloseModal(); });
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            return true;
        }
    }
}
