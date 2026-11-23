using System;
using System.Collections;
using System.IO;
using System.Linq;
using TMPro;
using TwelveJade.Core;
using TwelveJade.Presentation;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using System.Collections.Generic;

namespace TwelveJade.Editor
{
    // Uses PrologueTownShot's isolated save root, input hashes and scene restoration.
    public static partial class PrologueTownShot
    {
        static IEnumerator StoryChecks()
        {
            Require("story scratch repository", SamePath(Field(host.Repository, "root") as string, state.scratch));
            Require("story scratch empty", host.Repository.ReadAll().All(s => s.State == SlotState.Empty));
            canvasRoot = Field(host, "canvasObject") as GameObject;
            Require("story real canvas", canvasRoot != null);
            var preset = Resources.LoadAll<CharacterPreset>("Characters").First(); BindAsset(preset);
            ResourceText("Config/qingshi-story-panels"); ResourceText("Config/town-character-art"); ResourceText("Config/dialogue-portraits");
            foreach (var definition in QingshiStory.Scenes)
            {
                var script = QingshiComicView.Load(definition.Id);
                Check(definition.Id + " script has pages", script.panels.Length >= 4);
                var texture = Resources.Load<Texture2D>("Art/Story/Qingshi/" + definition.Id);
                Require(definition.Id + " real story art", texture != null); BindAsset(texture);
            }
            foreach (string gender in new[] { "male", "female" })
            {
                foreach (string view in new[] { "front", "side", "back" })
                {
                    var entry = TownCharacterArt.Find("hero-" + gender + "-" + view);
                    var stand = TownCharacterArt.Hero(gender, view, false); var walk = TownCharacterArt.Hero(gender, view, true);
                    Require(gender + view + " rural resource", stand != null && walk != null && stand.name.StartsWith("qingshi-"));
                    BindAsset(stand); BindAsset(walk);
                    Check(gender + view + " four frame walk", entry.frames == 4 && walk.width == stand.width * 4 && walk.height == stand.height);
                    Check(gender + view + " creation and world agree", preset.Facing(view == "front" ? 0 : view == "back" ? 2 : 1, gender, 1) == stand);
                }
                var strikes = TownCharacterArt.HeroStrikes(gender); Require(gender + " rural strikes", strikes != null && strikes.width == 1728 && strikes.height == 576); BindAsset(strikes);
                var portrait = DialoguePortraitCatalog.Hero(gender); Require(gender + " rural portrait", portrait != null && portrait.name == "qingshi-" + gender + "-portrait"); BindAsset(portrait);
            }
            var fresh = host.Repository.Create(1, preset.id, "故里行旅", "male", 0, null, "shaxing");
            host.LoadSlot(host.Repository.Read(1));
            yield return Until(() => host.CurrentPage == "town" && host.TravelerPuppetForShot != null, "story town ready", 20);
            yield return Frames(5);
            var save = host.ActiveSave; string assets = Ledger(save); string chapter = JsonUtility.ToJson(save.chapter);
            Check("world wears rural clothes", host.TravelerPuppetForShot.View.name.StartsWith("qingshi-male-"));
            yield return Capture("story-male-town", "male");
            host.ShowTownChapterPanel(); yield return JournalReady();
            Check("future scene disabled", !StoryButton("Story chapter tiger").interactable);
            yield return Capture("story-journal", "fresh");
            StoryButton("Story chapter home").onClick.Invoke();
            yield return Until(() => host.QingshiComicForCheck != null, "journal opens real comic");
            var viewHost = host.QingshiComicForCheck;
            var content = Field(host, "content") as RectTransform;
            Check("comic shields world input", !content.GetComponent<CanvasGroup>().interactable && !content.GetComponent<CanvasGroup>().blocksRaycasts);
            Vector2 position = host.TravelerRectForShot.anchoredPosition;
            var crop = viewHost.GetComponentsInChildren<RawImage>().Single(a => a.name == "Story painted panel");
            var settings = Field(host, "settings"); var reducedField = settings.GetType().GetField("reduceMotion");
            bool originalMotion = (bool)reducedField.GetValue(settings);
            reducedField.SetValue(settings, true); yield return Frames(2); Rect still = crop.uvRect;
            yield return Frames(6); Check("reduced motion stable", crop.uvRect == still);
            reducedField.SetValue(settings, false); yield return new WaitForSecondsRealtime(.35f);
            Check("normal motion moves crop", crop.uvRect != still);
            Check("comic keeps player still", host.TravelerRectForShot.anchoredPosition == position);
            reducedField.SetValue(settings, originalMotion);
            yield return StoryKey(UnityEngine.InputSystem.Key.Space);
            Require("home first choice reached", viewHost.PanelIndex == 1 && !viewHost.CanAdvance);
            StoryButton("Story close").onClick.Invoke(); yield return Frames(3);
            Check("closing unfinished scene writes no flag", QingshiStory.CurrentIndex(save) == 0 && QingshiStory.CurrentIndex(host.Repository.Read(1).Data) == 0);
            Check("closing restores world input", content.GetComponent<CanvasGroup>().interactable && content.GetComponent<CanvasGroup>().blocksRaycasts);
            Check("cannot skip to future", !host.ShowQingshiComic("tiger"));
            Require("reopen for keyboard close", host.ShowQingshiComic("home")); yield return Frames(3);
            position = host.TravelerRectForShot.anchoredPosition;
            yield return StoryKey(UnityEngine.InputSystem.Key.W);
            Check("held world movement blocked by comic", position == host.TravelerRectForShot.anchoredPosition);
            yield return StoryKey(UnityEngine.InputSystem.Key.Escape);
            Check("escape closes comic without progress", host.QingshiComicForCheck == null && QingshiStory.CurrentIndex(save) == 0 && content.GetComponent<CanvasGroup>().interactable);
            int pageCount = 0, choiceCount = 0;
            for (int sceneIndex = 0; sceneIndex < QingshiStory.Scenes.Count; sceneIndex++)
            {
                var definition = QingshiStory.Scenes[sceneIndex]; host.CloseChapterPanel(); yield return Frames(3);
                Require(definition.Id + " can open", host.ShowQingshiComic(definition.Id)); yield return Frames(3);
                var comic = host.QingshiComicForCheck; var script = QingshiComicView.Load(definition.Id);
                Require(definition.Id + " fresh playback", comic != null && comic.HasArtwork && !comic.IsReplay && comic.PanelIndex == 0);
                yield return Capture("story-" + definition.Id, "first-read");
                for (int page = 0; page < script.panels.Length; page++)
                {
                    pageCount++; Require(definition.Id + " page " + page, comic.PanelIndex == page);
                    Check(definition.Id + " remains uncommitted at page " + page, QingshiStory.CurrentIndex(save) == sceneIndex);
                    foreach (var label in comic.GetComponentsInChildren<TMP_Text>(false))
                    { label.ForceMeshUpdate(); Check(definition.Id + " text fits " + page + "/" + label.name, !label.isTextOverflowing); }
                    var options = script.panels[page].choices;
                    if (options != null && options.Length > 0)
                    {
                        choiceCount++; Check(definition.Id + " choice gates next", !comic.CanAdvance);
                        comic.Advance(); Check(definition.Id + " skipped choice rejected", comic.PanelIndex == page);
                        // GraphicRaycaster depth is assigned when the newly active options render.
                        yield return new WaitForEndOfFrame();
                        for (int option = 0; option < options.Length; option++) StoryButtonHit("Story choice " + option);
                        if (definition.Id == "livelihood") yield return Capture("story-three-choices", "choice");
                        if (definition.Id == "home" && page == 1) yield return Capture("story-home-choice", "choice");
                        StoryButton("Story choice 0").onClick.Invoke();
                        Check(definition.Id + " chosen reply", comic.CanAdvance && comic.CurrentText == options[0].reply);
                        if (page > 0) { comic.Back(); comic.Advance(); Check(definition.Id + " back retains selection", comic.PanelIndex == page && comic.CurrentText == options[0].reply); yield return null; }
                    }
                    if (page < script.panels.Length - 1) {
                        StoryButton("Story next").onClick.Invoke(); comic.Advance();
                        Check(definition.Id + " same frame advances once " + page, comic.PanelIndex == page + 1);
                        yield return null;
                    }
                }
                if (sceneIndex == 0)
                {
                    string primary = Path.Combine(state.scratch, "slot-1.json"); string before = Sha(primary); string flags = string.Join("|", save.oneTimeFlags);
                    using (var locked = new FileStream(primary + ".tmp", FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                    {
                        StoryButton("Story next").onClick.Invoke(); yield return Frames(2);
                        Check("save failure remains in comic and allows retry", host.QingshiComicForCheck == comic && comic.CanAdvance && !comic.CompletedForCheck);
                        Check("save failure no progress or file change", QingshiStory.CurrentIndex(save) == 0 && flags == string.Join("|", save.oneTimeFlags) && before == Sha(primary));
                    }
                }
                StoryButton("Story next").onClick.Invoke(); yield return JournalReady();
                Check(definition.Id + " completion once", QingshiStory.CurrentIndex(save) == sceneIndex + 1 && QingshiStory.CurrentIndex(host.Repository.Read(1).Data) == sceneIndex + 1);
                Check(definition.Id + " retains assets and old chapter", Ledger(save) == assets && JsonUtility.ToJson(save.chapter) == chapter);
                Check(definition.Id + " no initiation", !save.HasFlag(PrologueRules.InitiationFlag));
                host.CloseChapterPanel(); yield return Frames(3);
                string disk = Sha(Path.Combine(state.scratch, "slot-1.json"));
                Require(definition.Id + " replay opens", host.ShowQingshiComic(definition.Id));
                comic = host.QingshiComicForCheck; Require(definition.Id + " readonly replay", comic.IsReplay);
                for (int page = 0; page < script.panels.Length; page++)
                {
                    var options = script.panels[page].choices;
                    if (options != null && options.Length > 0) StoryButton("Story choice " + (options.Length - 1)).onClick.Invoke();
                    StoryButton("Story next").onClick.Invoke(); yield return null;
                }
                yield return JournalReady();
                Check(definition.Id + " replay did not write", disk == Sha(Path.Combine(state.scratch, "slot-1.json")) && QingshiStory.CurrentIndex(save) == sceneIndex + 1);
            }
            Check("all authored pages and choices exercised", pageCount == 36 && choiceCount == 6, "pages=" + pageCount + ";choices=" + choiceCount);
            Check("departure granted without initiation", save.HasFlag(PrologueRules.DepartureFlag) && !PrologueRules.CanCultivate(save));
            host.CloseChapterPanel(); yield return Frames(3);
            var female = host.Repository.Create(2, preset.id, "青石旧衣", "female", 1, null, "shaxing");
            host.LoadSlot(host.Repository.Read(2)); yield return Until(() => host.ActiveSave?.slot == 2 && host.TravelerPuppetForShot != null, "female town ready");
            yield return Frames(5); Check("female world rural costume", host.TravelerPuppetForShot.View.name.StartsWith("qingshi-female-"));
            yield return Capture("story-female-town", "female");
            Invoke(host, "ShowDaoIdentityLedger"); yield return Frames(3);
            var poster = canvasRoot.GetComponentsInChildren<PosterCardView>(false).FirstOrDefault();
            Check("female identity shares portrait", poster != null && poster.HasArtwork && poster.Artwork.texture == DialoguePortraitCatalog.Hero("female"));
            yield return Capture("story-female-identity", "female"); Invoke(host, "CloseModal");
        }
        static IEnumerator StoryKey(Key key)
        {
            var keyboard = Keyboard.current; Require("real keyboard available", keyboard != null);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key)); yield return Frames(3);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return Frames(2);
        }
        static void StoryButtonHit(string name)
        {
            Canvas.ForceUpdateCanvases(); var button = StoryButton(name);
            var rect = (RectTransform)button.transform;
            var pointer = new PointerEventData(EventSystem.current) {
                position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center))
            };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            Check(name + " owns pointer target", hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == button,
                "point=" + pointer.position + "; hits=" + string.Join(",", hits.Take(4).Select(h => h.gameObject.name)) + "; depth=" + button.targetGraphic.depth);
        }
        static Button StoryButton(string name)
        {
            var button = canvasRoot.GetComponentsInChildren<Button>(false).FirstOrDefault(b => b.name == name);
            Require("live button " + name, button != null && button.isActiveAndEnabled); return button;
        }
        static IEnumerator JournalReady()
        {
            yield return Until(() => {
                var panel = Field(host, "chapterPanel") as RectTransform;
                var modal = Field(host, "chapterModal") as RectTransform;
                var clip = panel != null ? panel.Find("Chapter unrolling mask") as RectTransform : null;
                return host.ChapterScrollOpenForCheck && modal != null && modal.GetComponent<CanvasGroup>()?.interactable == true &&
                    clip != null && Mathf.Abs(clip.rect.height - panel.rect.height) <= .5f;
            }, "journal fully open");
            yield return Frames(2);
        }
    }
}
