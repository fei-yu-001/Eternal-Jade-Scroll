using System;
using System.Collections;
using System.Collections.Generic;
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

namespace TwelveJade.Editor
{
    // Uses PrologueTownShot.RunStory's isolated saves, input hashes and scene restoration.
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
            var scripts = new Dictionary<string, QingshiComicScene>();
            var expectedArt = new HashSet<string>(StringComparer.Ordinal);
            foreach (var definition in QingshiStory.Scenes)
            {
                var script = QingshiComicView.Load(definition.Id); scripts.Add(definition.Id, script);
                Check(definition.Id + " script has pages", script.panels.Length > 0);
                foreach (var panel in script.panels)
                {
                    string artId = StoryArtId(script, panel);
                    var texture = Resources.Load<Texture2D>("Art/Story/Qingshi/" + artId);
                    Require(definition.Id + " actual panel art " + artId, texture != null); BindAsset(texture);
                    expectedArt.Add(artId);
                }
            }
            int expectedPages = scripts.Values.Sum(s => s.panels.Length);
            int expectedChoices = scripts.Values.Sum(s => s.panels.Count(p => (p.choices?.Length ?? 0) > 0));
            StoryValidationChecks(scripts[QingshiStory.Scenes[0].Id]);
            foreach (string gender in new[] { "male", "female" })
            {
                foreach (string view in new[] { "front", "side", "back" })
                {
                    var entry = TownCharacterArt.Find("hero-" + gender + "-" + view);
                    var stand = TownCharacterArt.Hero(gender, view, false); var walk = TownCharacterArt.Hero(gender, view, true);
                    Require(gender + view + " rural resource", entry != null && stand != null && walk != null && stand.name.StartsWith("qingshi-"));
                    BindAsset(stand); BindAsset(walk);
                    Check(gender + view + " configured walk cells", entry.frames > 0 && walk.width % entry.frames == 0 &&
                        walk.width / entry.frames == stand.width && walk.height == stand.height &&
                        entry.cycleDistance > 0 && !float.IsInfinity(entry.cycleDistance),
                        "configured frames=" + entry.frames + "; walk=" + walk.width + "x" + walk.height);
                    Check(gender + view + " creation and world agree", preset.Facing(view == "front" ? 0 : view == "back" ? 2 : 1, gender, 1) == stand);
                }
                var strikes = TownCharacterArt.HeroStrikes(gender); Require(gender + " rural strikes", strikes != null && strikes.width == 1728 && strikes.height == 576); BindAsset(strikes);
                var portrait = DialoguePortraitCatalog.Hero(gender); Require(gender + " rural portrait", portrait != null && portrait.name == "qingshi-" + gender + "-portrait"); BindAsset(portrait);
                var figure = Resources.Load<Texture2D>("Art/Characters/Qingshi/qingshi-" + gender + "-figure");
                Require(gender + " rural figure", figure != null); BindAsset(figure);
            }
            var shitouArt = TownCharacterArt.Find("npc-shitou");
            Require("shitou town art entry", shitouArt != null);
            var shitouStand = TownCharacterArt.Texture(shitouArt.stand);
            var shitouWalk = TownCharacterArt.Texture(shitouArt.walk);
            var shitouPortrait = DialoguePortraitCatalog.Find("npc-shitou");
            Require("shitou empty-hand stand resource", shitouStand != null && shitouStand.width == 384 && shitouStand.height == 512);
            Require("shitou four original leg poses", shitouWalk != null && shitouArt.frames == 4 && shitouWalk.width == 1536 && shitouWalk.height == 512);
            Require("shitou matching portrait resource", shitouPortrait != null && shitouPortrait.width == 1024 && shitouPortrait.height == 1536);
            BindAsset(shitouStand); BindAsset(shitouWalk); BindAsset(shitouPortrait);
            host.Repository.Create(1, preset.id, "故里行旅", "male", 0, null, "shaxing");
            host.LoadSlot(host.Repository.Read(1));
            yield return Until(() => host.CurrentPage == "town" && host.TravelerPuppetForShot != null, "story town ready", 20);
            yield return Frames(5);
            var save = host.ActiveSave; string assets = Ledger(save); string chapter = JsonUtility.ToJson(save.chapter);
            var settings = Field(host, "settings"); var reducedField = settings.GetType().GetField("reduceMotion");
            bool originalMotion = (bool)reducedField.GetValue(settings);
            try
            {
                reducedField.SetValue(settings, false);
                yield return StoryReaderProbe(scripts[QingshiStory.Scenes[0].Id]);
                Check("world wears rural clothes", host.TravelerPuppetForShot.View.name.StartsWith("qingshi-male-"));
                yield return Capture("story-male-town", "male");
                host.ShowTownChapterPanel(); yield return JournalReady();
                StoryDirectoryLayout(save, scripts);
                Check("future scene disabled", !StoryButton("Story chapter tiger").interactable);
                yield return Capture("story-journal", "fresh");
                yield return StoryJourneyDirectoryEntry();
                yield return Until(() => host.QingshiComicForCheck != null, "journal NPC bridge opens real comic");
                var viewHost = host.QingshiComicForCheck;
                var fullVeil = viewHost.transform.parent as RectTransform;
                var fullCanvas = Field(host, "screenCanvas") as RectTransform;
                Check("reader blocks entire viewport including letterbox", fullVeil != null && fullCanvas != null &&
                    fullVeil.parent == fullCanvas && Vector2.Distance(fullVeil.rect.size, fullCanvas.rect.size) < .1f &&
                    fullVeil.GetComponent<Image>()?.raycastTarget == true && fullVeil.GetComponent<Image>().color.a >= .999f);
                var content = Field(host, "content") as RectTransform;
                Check("comic shields world input", !content.GetComponent<CanvasGroup>().interactable && !content.GetComponent<CanvasGroup>().blocksRaycasts);
                Vector2 position = host.TravelerRectForShot.anchoredPosition;
                var crop = StoryArt(viewHost);
                reducedField.SetValue(settings, true); yield return Frames(2); var stillCrop = crop.uvRect;
                yield return Frames(6); Check("reduced motion stable and fully read", crop.uvRect == stillCrop && !viewHost.IsRevealing);
                reducedField.SetValue(settings, false); yield return new WaitForSecondsRealtime(.35f);
                Check("normal motion moves crop gently", crop.uvRect != stillCrop && Mathf.Abs(crop.uvRect.x - stillCrop.x) < .01f && Mathf.Abs(crop.uvRect.width - stillCrop.width) < .01f);
                Check("comic keeps player still", host.TravelerRectForShot.anchoredPosition == position);
                int firstChoice = Array.FindIndex(scripts["home"].panels, p => (p.choices?.Length ?? 0) > 0);
                if (firstChoice >= 0)
                {
                    while (viewHost.PanelIndex < firstChoice)
                    {
                        yield return StoryReveal(viewHost);
                        viewHost.Advance(); yield return null;
                    }
                    yield return StoryReveal(viewHost);
                    Require("home first authored choice reached", viewHost.PanelIndex == firstChoice && !viewHost.CanAdvance);
                }
                StoryButton("Story close").onClick.Invoke(); yield return Frames(3);
                Check("closing unfinished scene writes no flag", QingshiStory.CurrentIndex(save) == 0 && QingshiStory.CurrentIndex(host.Repository.Read(1).Data) == 0);
                Check("closing restores world input", content.GetComponent<CanvasGroup>().interactable && content.GetComponent<CanvasGroup>().blocksRaycasts);
                Check("cannot skip to future", !host.ShowQingshiComic("tiger"));
                Require("reopen for keyboard close", host.ShowQingshiComic("home"));
                viewHost = host.QingshiComicForCheck;
                Require("normal reader starts with typewriting", viewHost.IsRevealing);
                yield return StoryKey(UnityEngine.InputSystem.Key.Space);
                Check("first space reveals without turning page", viewHost.PanelIndex == 0 && !viewHost.IsRevealing);
                if (viewHost.PanelCount > 1 && viewHost.CanAdvance)
                {
                    yield return StoryKey(UnityEngine.InputSystem.Key.Space);
                    Check("second space advances exactly one page", viewHost.PanelIndex == 1);
                }
                position = host.TravelerRectForShot.anchoredPosition;
                yield return StoryKey(UnityEngine.InputSystem.Key.W);
                Check("held world movement blocked by comic", position == host.TravelerRectForShot.anchoredPosition);
                yield return StoryKey(UnityEngine.InputSystem.Key.Escape);
                Check("escape closes comic without progress", host.QingshiComicForCheck == null && QingshiStory.CurrentIndex(save) == 0 && content.GetComponent<CanvasGroup>().interactable);

                int pageCount = 0, choiceCount = 0;
                var capturedArt = new HashSet<string>(StringComparer.Ordinal);
                for (int sceneIndex = 0; sceneIndex < QingshiStory.Scenes.Count; sceneIndex++)
                {
                    var definition = QingshiStory.Scenes[sceneIndex]; host.CloseChapterPanel(); yield return Frames(3);
                    Require(definition.Id + " can open", host.ShowQingshiComic(definition.Id));
                    var comic = host.QingshiComicForCheck; var script = scripts[definition.Id];
                    Require(definition.Id + " fresh playback", comic != null && comic.HasArtwork && !comic.IsReplay && comic.PanelIndex == 0);
                    for (int page = 0; page < script.panels.Length; page++)
                    {
                        pageCount++; Require(definition.Id + " page " + page, comic.PanelIndex == page);
                        Check(definition.Id + " remains uncommitted at page " + page, QingshiStory.CurrentIndex(save) == sceneIndex && QingshiStory.CurrentIndex(host.Repository.Read(1).Data) == sceneIndex);
                        yield return StoryReveal(comic);
                        yield return new WaitForSecondsRealtime(.26f);
                        StoryLayout(comic, script.panels[page], false);
                        yield return Capture("story-" + definition.Id + "-" + (page + 1).ToString("D2"), "first-read");
                        capturedArt.Add(comic.CurrentArtId);
                        var options = script.panels[page].choices;
                        if (options != null && options.Length > 0)
                        {
                            choiceCount++; Check(definition.Id + " choice gates next", !comic.CanAdvance);
                            comic.Advance(); Check(definition.Id + " skipped choice rejected", comic.PanelIndex == page);
                            yield return new WaitForEndOfFrame();
                            for (int option = 0; option < options.Length; option++) StoryButtonHit("Story choice " + option);
                            yield return StoryChoiceFeedback(comic, "story-" + definition.Id + "-" + (page + 1).ToString("D2") + "-hover");
                            StoryButton("Story choice 0").onClick.Invoke();
                            Check(definition.Id + " chosen reply", comic.CanAdvance && comic.CurrentText == options[0].reply);
                            yield return StoryReveal(comic);
                            comic.Choose(options.Length - 1);
                            Check(definition.Id + " cannot replace chosen response", comic.CurrentText == options[0].reply);
                            StoryLayout(comic, script.panels[page], true);
                            yield return Capture("story-" + definition.Id + "-" + (page + 1).ToString("D2") + "-reply", "choice");
                            if (page > 0)
                            {
                                comic.Back(); yield return null;
                                Require(definition.Id + " back reaches preceding page", comic.PanelIndex == page - 1);
                                yield return StoryReveal(comic);
                                comic.Advance();
                                Check(definition.Id + " back retains selection", comic.PanelIndex == page && comic.CurrentText == options[0].reply);
                                yield return StoryReveal(comic);
                            }
                        }
                        if (page < script.panels.Length - 1)
                        {
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
                            Check("save failure remains in comic and allows retry", host.QingshiComicForCheck == comic && comic.CanAdvance && !comic.CompletedForCheck && !comic.IsRevealing);
                            Check("save failure no progress or file change", QingshiStory.CurrentIndex(save) == 0 && flags == string.Join("|", save.oneTimeFlags) && before == Sha(primary));
                        }
                    }
                    StoryButton("Story next").onClick.Invoke(); comic.Advance(); yield return JournalReady();
                    Check(definition.Id + " completion once", QingshiStory.CurrentIndex(save) == sceneIndex + 1 && QingshiStory.CurrentIndex(host.Repository.Read(1).Data) == sceneIndex + 1);
                    Check(definition.Id + " retains assets and old chapter", Ledger(save) == assets && JsonUtility.ToJson(save.chapter) == chapter);
                    Check(definition.Id + " no initiation", !save.HasFlag(PrologueRules.InitiationFlag));
                    StoryDirectoryLayout(save, scripts);
                    yield return Capture("story-journal-" + (sceneIndex + 1).ToString("D2"), "progress");
                    string disk = Sha(Path.Combine(state.scratch, "slot-1.json"));
                    var replayButton = StoryButton("Story chapter " + definition.Id);
                    StoryDirectoryReveal(replayButton); StoryButtonHit(replayButton.name);
                    var replayPointer = StoryPointer(replayButton);
                    ExecuteEvents.Execute(replayButton.gameObject, replayPointer, ExecuteEvents.pointerClickHandler);
                    yield return Until(() => host.QingshiComicForCheck != null, definition.Id + " directory replay opens");
                    comic = host.QingshiComicForCheck; Require(definition.Id + " readonly replay", comic.IsReplay);
                    for (int page = 0; page < script.panels.Length; page++)
                    {
                        Require(definition.Id + " replay page " + page, comic.PanelIndex == page);
                        yield return StoryReveal(comic);
                        var options = script.panels[page].choices;
                        if (options != null && options.Length > 0)
                        {
                            StoryButton("Story choice " + (options.Length - 1)).onClick.Invoke();
                            yield return StoryReveal(comic);
                            Check(definition.Id + " replay alternate reply", comic.CurrentText == options[options.Length - 1].reply);
                        }
                        Check(definition.Id + " replay page never saves", disk == Sha(Path.Combine(state.scratch, "slot-1.json")) && QingshiStory.CurrentIndex(save) == sceneIndex + 1);
                        StoryButton("Story next").onClick.Invoke(); yield return null;
                    }
                    yield return JournalReady();
                    Check(definition.Id + " replay did not write", disk == Sha(Path.Combine(state.scratch, "slot-1.json")) && QingshiStory.CurrentIndex(save) == sceneIndex + 1);
                    StoryDirectoryLayout(save, scripts);
                }
                Check("all authored pages and choices exercised", pageCount == expectedPages && choiceCount == expectedChoices,
                    "pages=" + pageCount + "/" + expectedPages + ";choices=" + choiceCount + "/" + expectedChoices);
                Check("all authored art rendered and captured", capturedArt.SetEquals(expectedArt), "unique art=" + capturedArt.Count + "/" + expectedArt.Count);
                Check("departure granted without initiation", save.HasFlag(PrologueRules.DepartureFlag) && !PrologueRules.CanCultivate(save));
                host.CloseChapterPanel(); yield return Frames(3);
                host.Repository.Create(2, preset.id, "青石旧衣", "female", 1, null, "shaxing");
                host.LoadSlot(host.Repository.Read(2)); yield return Until(() => host.ActiveSave?.slot == 2 && host.TravelerPuppetForShot != null, "female town ready");
                yield return Frames(5); Check("female world rural costume", host.TravelerPuppetForShot.View.name.StartsWith("qingshi-female-"));
                yield return Capture("story-female-town", "female");
                Invoke(host, "ShowDaoIdentityLedger"); yield return Frames(3);
                var poster = canvasRoot.GetComponentsInChildren<PosterCardView>(false).FirstOrDefault();
                var femaleFigure = Resources.Load<Texture2D>("Art/Characters/Qingshi/qingshi-female-figure");
                Check("female identity shares full figure", poster != null && poster.HasArtwork && poster.IsCutout &&
                    poster.ResourcePath == "Art/Characters/Qingshi/qingshi-female-figure" && poster.Artwork.texture == femaleFigure);
                Check("female identity has no picture frame or crop", poster != null && poster.Frame == null && poster.Crop == new Rect(0, 0, 1, 1));
                Check("female dialogue portrait still resolves independently", DialoguePortraitCatalog.Hero("female") != null &&
                    DialoguePortraitCatalog.Hero("female").name == "qingshi-female-portrait");
                yield return Capture("story-female-identity", "female"); Invoke(host, "CloseModal");
                yield return Frames(3);
                yield return StoryJourneyChecks(preset, scripts);
            }
            finally { reducedField.SetValue(settings, originalMotion); }
        }

        static string StoryArtId(QingshiComicScene script, QingshiComicPanel panel) => string.IsNullOrEmpty(panel.art) ? script.id : panel.art;
        static RawImage StoryArt(QingshiComicView comic) => comic.GetComponentsInChildren<RawImage>(true).Single(a => a.name == "Story painted panel");
        static IEnumerator StoryReveal(QingshiComicView comic)
        {
            yield return null; // Never reuse the navigation/choice frame that entered this page.
            if (comic.IsRevealing)
            {
                int page = comic.PanelIndex; string text = comic.CurrentText;
                comic.GetComponentsInChildren<Button>(true).Single(b => b.name == "Story next").onClick.Invoke();
                comic.Advance();
                Check(comic.SceneId + " reveal consumes first advance", comic.PanelIndex == page && !comic.IsRevealing && comic.CurrentText == text);
            }
            yield return null;
        }
        static void StoryValidationChecks(QingshiComicScene template)
        {
            foreach (string art in new[] { "../home", "home/side", "home\\side", "HOME", "home.png", "home ", "-home", "home-", "home--side", "home:1" })
            {
                var copy = JsonUtility.FromJson<QingshiComicScene>(JsonUtility.ToJson(template)); copy.panels[0].art = art;
                StoryRejected("unsafe art rejected " + art, copy);
            }
            var absent = JsonUtility.FromJson<QingshiComicScene>(JsonUtility.ToJson(template));
            absent.panels[0].art = "fixture-missing-" + Guid.NewGuid().ToString("N");
            StoryRejected("safe but missing art rejected", absent);
            var invalid = JsonUtility.FromJson<QingshiComicScene>(JsonUtility.ToJson(template));
            invalid.panels[0].balloon = "above"; StoryRejected("invalid balloon placement rejected", invalid);
            invalid = JsonUtility.FromJson<QingshiComicScene>(JsonUtility.ToJson(template));
            invalid.panels[0].focusX = float.NaN; StoryRejected("nonfinite framing rejected", invalid);
            var legacy = JsonUtility.FromJson<QingshiComicPanel>("{\"speaker\":\"旧页\",\"text\":\"旧页仍作叙述。\"}");
            Check("omitted balloonY keeps legacy default", legacy.balloonY == 166f);
            foreach (float y in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0f, 131f, 501f })
            {
                invalid = JsonUtility.FromJson<QingshiComicScene>(JsonUtility.ToJson(template));
                invalid.panels[0].balloonY = y; StoryRejected("invalid balloonY rejected " + y, invalid);
            }
            foreach (float y in new[] { 132f, 440f, 500f })
            {
                var valid = JsonUtility.FromJson<QingshiComicScene>(JsonUtility.ToJson(template));
                valid.panels[0].balloonY = y; bool accepted = true;
                try { QingshiComicView.Validate(valid); } catch (InvalidOperationException) { accepted = false; }
                Check("valid balloonY accepted " + y, accepted);
            }
        }
        static void StoryRejected(string name, QingshiComicScene script)
        {
            bool rejected = false;
            try { QingshiComicView.Validate(script); } catch (InvalidOperationException) { rejected = true; }
            Check(name, rejected);
        }

        // A disposable view using actual approved art covers both balloon sides and typing even before new JSON lands.
        // It cannot call the story completion core or repository; real progression/replay is exercised above.
        static IEnumerator StoryReaderProbe(QingshiComicScene source)
        {
            var ui = Field(host, "ui") as UiKit; Require("reader probe uses real UI kit", ui != null);
            var root = ui.Rect(canvasRoot.transform, "Story presentation probe", 0, 0, 1920, 1080);
            try
            {
                string art = StoryArtId(source, source.panels[0]);
                string narration = "灶火映着粗布衣上的针脚，巷口的脚步声慢慢远去。你停在门边，等阿娘把没说完的话说完，再伸手接过那件带着余温的衣裳。";
                var panels = new[] {
                    new QingshiComicPanel { art = art, speaker = "阿娘", text = narration, dialog = "慢些。", balloon = "left", balloonY = 440 },
                    new QingshiComicPanel { art = art, speaker = "你", text = narration,
                        dialog = "我记着。\n等忙过这一阵，\n就回来陪你们吃饭。\n阿爹安心养着。\n阿娘也别累着，\n路上的事回来再说。", balloon = "right", balloonY = 500,
                        choices = new[] {
                             new QingshiComicChoice { label = "记着回家吃饭", reply = "你把阿娘的话记在心里。" },
                             new QingshiComicChoice { label = "先把衣裳收好", reply = "你接过了补好的衣裳。" },
                             new QingshiComicChoice { label = "替阿娘添柴", reply = "你往灶里添了一把柴。" } } },
                    new QingshiComicPanel { art = art, speaker = "旧页", text = "旧页仍作叙述。" }
                };
                var script = new QingshiComicScene { id = source.id, panels = panels };
                var comic = root.gameObject.AddComponent<QingshiComicView>(); bool reduced = false; int completed = 0;
                comic.Configure(ui, root, QingshiStory.Scenes.First(s => s.Id == source.id), script, false, () => reduced, () => completed++, () => { });
                var label = comic.GetComponentsInChildren<TMP_Text>().Single(t => t.name == "Story narration");
                Check("typing keeps full semantic narration", comic.CurrentText == narration && comic.IsRevealing && label.maxVisibleCharacters == 0);
                Check("entrance begins within quarter second", StoryArt(comic).color.a < 1);
                yield return Until(() => label.maxVisibleCharacters > 0, "characters begin appearing", 3);
                Check("typing reveals a prefix before full text", comic.IsRevealing && label.maxVisibleCharacters < label.textInfo.characterCount);
                yield return StoryReveal(comic);
                yield return new WaitForSecondsRealtime(.26f);
                StoryLayout(comic, panels[0], false);
                var shortBalloon = comic.GetComponentsInChildren<RectTransform>().Single(t => t.name == "Story dialogue balloon");
                float shortWidth = shortBalloon.rect.width, shortHeight = shortBalloon.rect.height;
                Check("short reply uses a compact balloon", shortWidth < 420 && shortHeight < 210);
                foreach (var control in comic.GetComponentsInChildren<Button>())
                {
                    StoryButtonHit(control.name);
                    StoryControlFeedback(control, () => Check(control.name + " disabled navigation cannot advance or close",
                        comic.PanelIndex == 0 && completed == 0 && !comic.CompletedForCheck));
                }
                yield return Capture("story-reader-left-balloon", "presentation-probe");
                comic.Advance(); comic.Advance();
                Check("double advance crosses only one page", comic.PanelIndex == 1 && comic.IsRevealing && completed == 0);
                yield return null;
                var revealingChoices = comic.GetComponentsInChildren<Button>().Where(b => b.name.StartsWith("Story choice", StringComparison.Ordinal)).ToArray();
                Check("choices remain disabled while dialogue reveals", revealingChoices.Length == 3 && revealingChoices.All(b => !b.IsInteractable()));
                foreach (var choice in revealingChoices) StoryContrast(choice, "typing-disabled");
                var motion = comic.GetComponentsInChildren<Button>().Single(b => b.name == "Story motion");
                motion.onClick.Invoke(); var held = StoryArt(comic).uvRect;
                yield return Frames(4);
                Check("still mode completes text and freezes framing", !comic.IsRevealing && StoryArt(comic).uvRect == held);
                StoryLayout(comic, panels[1], false);
                yield return Capture("story-reader-right-balloon", "presentation-probe");
                var lowered = comic.GetComponentsInChildren<RectTransform>().Single(t => t.name == "Story dialogue balloon");
                Check("tall low balloon is lifted above choices", -lowered.anchoredPosition.y < panels[1].balloonY && !comic.CanAdvance);
                Check("long reply grows beyond compact balloon", lowered.rect.width > shortWidth + 32 && lowered.rect.height > shortHeight + 64);
                yield return StoryChoiceFeedback(comic, "story-reader-choice-hover");
                comic.Choose(0); yield return null;
                motion.onClick.Invoke(); reduced = true;
                comic.Back(); yield return Frames(3); held = StoryArt(comic).uvRect;
                yield return Frames(4);
                Check("reduced motion reveals instantly and freezes framing", comic.PanelIndex == 0 && !comic.IsRevealing && StoryArt(comic).uvRect == held && StoryArt(comic).color.a == 1);
                comic.Advance(); yield return null;
                comic.Advance(); comic.Advance();
                Check("quiet double advance cannot finish the next page", comic.PanelIndex == 2 && completed == 0);
                Check("old text-only page remains narration", comic.CurrentText == panels[2].text && comic.CurrentDialog == "" && !comic.IsRevealing);
                StoryLayout(comic, panels[2], false);
                yield return null;
                comic.Advance(); comic.Advance(); comic.Back();
                Check("completion fires once and locks reader", completed == 1 && comic.CompletedForCheck && comic.PanelIndex == 2);
                comic.RetryCompletion();
                Check("retry restores fully read last page", comic.PanelIndex == 2 && !comic.CompletedForCheck && !comic.IsRevealing && comic.CanAdvance);
            }
            finally { UnityEngine.Object.Destroy(root.gameObject); }
            yield return null;
        }

        static void StoryLayout(QingshiComicView comic, QingshiComicPanel panel, bool chosen)
        {
            Canvas.ForceUpdateCanvases(); var root = (RectTransform)comic.transform;
            var art = StoryArt(comic); var artRect = StoryRect(root, art.rectTransform); var bounds = root.rect;
            string prefix = comic.SceneId + "/" + comic.PanelIndex + (chosen ? "/reply" : "");
            Check(prefix + " artwork fills reader", Mathf.Abs(artRect.xMin - bounds.xMin) < .5f && Mathf.Abs(artRect.xMax - bounds.xMax) < .5f &&
                Mathf.Abs(artRect.yMin - bounds.yMin) < .5f && Mathf.Abs(artRect.yMax - bounds.yMax) < .5f &&
                Mathf.Abs(bounds.width - 1920) < .5f && Mathf.Abs(bounds.height - 1080) < .5f);
            Check(prefix + " actual panel art selected", art.texture == Resources.Load<Texture2D>("Art/Story/Qingshi/" + comic.CurrentArtId) &&
                comic.CurrentArtId == (string.IsNullOrEmpty(panel.art) ? comic.SceneId : panel.art));
            Check(prefix + " art fully entered", art.color.a >= .999f);
            Check(prefix + " crop stays inside texture", art.uvRect.xMin >= 0 && art.uvRect.yMin >= 0 && art.uvRect.xMax <= 1.0001f && art.uvRect.yMax <= 1.0001f);
            Check(prefix + " no separate right caption board", !comic.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Story caption paper"));
            foreach (var ink in comic.GetComponentsInChildren<QingshiComicInkGraphic>(false))
            {
                var renderer = ink.GetComponent<CanvasRenderer>();
                Require(prefix + " overlay has CanvasRenderer " + ink.name, renderer != null);
                ink.Rebuild(CanvasUpdate.PreRender);
                var submitted = renderer.GetMesh();
                Check(prefix + " overlay submits visible triangles " + ink.name,
                    submitted != null && submitted.vertexCount >= 3 && submitted.triangles.Length >= 3 && submitted.colors32.Any(c => c.a > 0));
            }
            foreach (var paper in comic.GetComponentsInChildren<QingshiComicPaperGraphic>(false)) StoryPaperMesh(paper, prefix);
            var labels = comic.GetComponentsInChildren<TMP_Text>(false);
            foreach (var label in labels)
            {
                label.ForceMeshUpdate();
                Check(prefix + " text fits " + label.name, !label.isTextOverflowing);
                Check(prefix + " text inside artwork " + label.name, StoryContains(artRect, StoryRect(root, label.rectTransform)));
            }
            var narration = labels.Single(t => t.name == "Story narration");
            var narrationRect = StoryRect(root, narration.rectTransform);
            var caption = labels.Single(t => t.name == "Story caption"); var captionRect = StoryRect(root, caption.rectTransform);
            bool choicePage = (panel.choices?.Length ?? 0) > 0;
            var shade = comic.GetComponentsInChildren<QingshiComicInkGraphic>().Single(g => g.name == "Story subtitle shade");
            var shadeRect = StoryRect(root, shade.rectTransform);
            Check(prefix + " narration overlays bottom gradient", StoryContains(shadeRect, narrationRect) && StoryContains(shadeRect, captionRect) &&
                narrationRect.yMax <= bounds.yMin + bounds.height * .3f);
            Check(prefix + " authored caption and narration lanes", Mathf.Abs(bounds.yMax - captionRect.yMax - (choicePage ? 754 : 824)) < .5f &&
                Mathf.Abs(captionRect.height - 32) < .5f && Mathf.Abs(bounds.yMax - narrationRect.yMax - (choicePage ? 790 : 864)) < .5f &&
                Mathf.Abs(narrationRect.height - (choicePage ? 100 : 110)) < .5f && !captionRect.Overlaps(narrationRect));
            if (!chosen) Check(prefix + " narration preserved", comic.CurrentText == (panel.text ?? ""));
            var bubble = comic.GetComponentsInChildren<RectTransform>(true).Single(t => t.name == "Story dialogue balloon");
            bool speech = !chosen && !string.IsNullOrWhiteSpace(panel.dialog);
            Check(prefix + " dialogue visibility", bubble.gameObject.activeSelf == speech && comic.CurrentDialog == (speech ? panel.dialog : ""));
            var buttons = comic.GetComponentsInChildren<Button>(false);
            if (speech)
            {
                var bubbleRect = StoryRect(root, bubble);
                Check(prefix + " dialogue is embedded in scene", StoryContains(artRect, bubbleRect) && !bubbleRect.Overlaps(narrationRect) && !bubbleRect.Overlaps(captionRect));
                Check(prefix + " configured balloon side", panel.balloon == "right" ? bubbleRect.center.x > bounds.center.x : bubbleRect.center.x < bounds.center.x);
                var tail = bubble.GetComponentInChildren<QingshiComicInkGraphic>();
                Require(prefix + " dialogue has paper tail", tail != null && !tail.raycastTarget);
                var tailRect = StoryRect(root, tail.rectTransform);
                Check(prefix + " tail inside scene and clear of subtitles", StoryContains(artRect, tailRect) && !tailRect.Overlaps(narrationRect) && !tailRect.Overlaps(captionRect));
                var choiceRects = buttons.Where(b => b.name.StartsWith("Story choice", StringComparison.Ordinal))
                    .Select(b => StoryRect(root, (RectTransform)b.transform)).ToArray();
                float lowerTop = Mathf.Max(captionRect.yMax, choiceRects.Length > 0 ? choiceRects.Max(r => r.yMax) : captionRect.yMax);
                float actualY = bounds.yMax - bubbleRect.yMax;
                Check(prefix + " balloon leaves tail and lower row clearance", bubbleRect.yMin >= lowerTop + 48 - .5f && tailRect.yMin > lowerTop);
                Check(prefix + " balloon respects authored depth or available room", actualY >= 132 - .5f && actualY <= panel.balloonY + .5f &&
                    (Mathf.Abs(actualY - panel.balloonY) < .5f || Mathf.Abs(bubbleRect.yMin - lowerTop - 48) < .5f));
                foreach (var button in buttons)
                {
                    var buttonRect = StoryRect(root, (RectTransform)button.transform);
                    Check(prefix + " balloon and tail clear control " + button.name, !bubbleRect.Overlaps(buttonRect) && !tailRect.Overlaps(buttonRect));
                }
                var speechLabel = bubble.GetComponentsInChildren<TMP_Text>().Single(t => t.name == "Story dialogue");
                Check(prefix + " dialogue inside paper", StoryContains(bubbleRect, StoryRect(root, speechLabel.rectTransform)) && !speechLabel.richText);
                var speakerLabel = bubble.GetComponentsInChildren<TMP_Text>().Single(t => t.name == "Story speaker");
                Check(prefix + " speaker inside fitted paper", StoryContains(bubbleRect, StoryRect(root, speakerLabel.rectTransform)));
                Check(prefix + " fitted balloon preserves outer side", panel.balloon == "right"
                    ? Mathf.Abs(bubbleRect.xMax - (bounds.xMax - 96)) < .5f
                    : Mathf.Abs(bubbleRect.xMin - (bounds.xMin + 96)) < .5f);
                Check(prefix + " tail stays on fitted paper", tailRect.xMin >= bubbleRect.xMin && tailRect.xMax <= bubbleRect.xMax);
                var note = bubble.GetComponent<QingshiComicPaperGraphic>();
                Check(prefix + " dialogue uses thin paper geometry", note != null && note.Kind == QingshiComicPaperGraphic.Surface.Note &&
                    bubble.GetComponent<Image>() == null && !note.raycastTarget);
            }
            foreach (var button in buttons)
            {
                var rect = StoryRect(root, (RectTransform)button.transform);
                Check(prefix + " button inside art " + button.name, StoryContains(artRect, rect));
                Check(prefix + " control uses native paper " + button.name, button.targetGraphic is QingshiComicPaperGraphic &&
                    button.GetComponent<Image>() == null && button.GetComponent<EventTrigger>() == null);
                var buttonLabel = button.GetComponentInChildren<TMP_Text>();
                Check(prefix + " control caption inside paper " + button.name, StoryContains(rect, StoryRect(root, buttonLabel.rectTransform)));
                StoryContrast(button, prefix + "/" + (button.IsInteractable() ? "ready" : "disabled"));
                if (button.name.StartsWith("Story choice", StringComparison.Ordinal))
                {
                    Check(prefix + " choice below narration " + button.name, rect.yMax < narrationRect.yMin && !rect.Overlaps(captionRect) &&
                        (!speech || !rect.Overlaps(StoryRect(root, bubble))) && button.interactable);
                    Check(prefix + " choice uses authored lower lane " + button.name,
                        Mathf.Abs(bounds.yMax - rect.yMax - 912) < .5f && Mathf.Abs(rect.height - 64) < .5f &&
                        StoryContains(shadeRect, rect) && Mathf.Abs(rect.width - (panel.choices.Length == 3 ? 516 : 600)) < .5f);
                    var seal = button.GetComponentsInChildren<QingshiComicPaperGraphic>().Single(g => g.Kind == QingshiComicPaperGraphic.Surface.Seal);
                    var sealRect = StoryRect(root, seal.rectTransform);
                    int choice = Array.FindIndex(buttons.Where(b => b.name.StartsWith("Story choice", StringComparison.Ordinal)).ToArray(), b => b == button);
                    Check(prefix + " separate vermilion ordinal " + button.name, seal.GetComponentInChildren<TMP_Text>().text == new[] { "一", "二", "三" }[choice] &&
                        buttonLabel.text == panel.choices[choice].label && !seal.raycastTarget &&
                        StoryContains(rect, sealRect) && sealRect.xMax <= rect.xMin + 50.5f &&
                        StoryRect(root, buttonLabel.rectTransform).xMin >= sealRect.xMax + 24);
                    var turn = buttons.Single(b => b.name == "Story next");
                    Check(prefix + " choice leaves twenty pixels before turning " + button.name,
                        rect.yMin - StoryRect(root, (RectTransform)turn.transform).yMax >= 19.5f);
                }
                if (button.name == "Story next" || button.name == "Story previous")
                    Check(prefix + " navigation below subtitles " + button.name, rect.yMax < narrationRect.yMin &&
                        Mathf.Abs(bounds.yMax - rect.yMax - 996) < .5f && Mathf.Abs(rect.height - 56) < .5f);
            }
            for (int i = 0; i < buttons.Length; i++)
                for (int j = i + 1; j < buttons.Length; j++)
                    Check(prefix + " controls do not overlap " + buttons[i].name + "/" + buttons[j].name,
                        !StoryRect(root, (RectTransform)buttons[i].transform).Overlaps(StoryRect(root, (RectTransform)buttons[j].transform)));
        }
        // Drive real UGUI handlers and inspect renderer tint plus TMP glyph colors. A text-color-only
        // assertion would miss the original shared hover callback and the disabled background tint.
        static IEnumerator StoryChoiceFeedback(QingshiComicView comic, string screenshot)
        {
            var choices = comic.GetComponentsInChildren<Button>().Where(b => b.name.StartsWith("Story choice", StringComparison.Ordinal)).ToArray();
            Require("contrast probe has actual choices", choices.Length > 0 && EventSystem.current != null);
            var previousSelection = EventSystem.current.currentSelectedGameObject;
            int page = comic.PanelIndex; string text = comic.CurrentText;
            try
            {
                EventSystem.current.SetSelectedGameObject(null);
                foreach (var button in choices)
                {
                    StoryControlFeedback(button, () => Check(button.name + " disabled click leaves choice pending",
                        comic.PanelIndex == page && comic.CurrentText == text && !comic.CanAdvance));
                }
                // The last option reproduces the reported pale-text hover case in a reviewable frame.
                var hovered = choices[choices.Length - 1];
                ExecuteEvents.Execute(hovered.gameObject, StoryPointer(hovered), ExecuteEvents.pointerEnterHandler);
                yield return Capture(screenshot, "choice-hover");
                StoryContrast(hovered, "captured-hover");
                Check("feedback never advances or chooses", comic.PanelIndex == page && comic.CurrentText == text && !comic.CanAdvance);
            }
            finally
            {
                foreach (var button in choices)
                    if (button != null)
                    {
                        button.interactable = true;
                        ExecuteEvents.Execute(button.gameObject, StoryPointer(button), ExecuteEvents.pointerExitHandler);
                    }
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(previousSelection != null && previousSelection.activeInHierarchy ? previousSelection : null);
            }
            yield return null;
        }

        static void StoryPaperMesh(QingshiComicPaperGraphic paper, string prefix)
        {
            paper.Rebuild(CanvasUpdate.PreRender);
            var mesh = paper.canvasRenderer.GetMesh();
            Require(prefix + " paper submits triangles " + paper.name, mesh != null && mesh.vertexCount > 4 && mesh.triangles.Length >= 6);
            var vertices = mesh.vertices; var colors = mesh.colors32; var triangles = mesh.triangles;
            float area = 0;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                if (colors[a].a < 254 || colors[b].a < 254 || colors[c].a < 254) continue;
                Vector2 ab = vertices[b] - vertices[a], ac = vertices[c] - vertices[a];
                area += Mathf.Abs(ab.x * ac.y - ab.y * ac.x) * .5f;
            }
            var bounds = paper.rectTransform.rect;
            Check(prefix + " paper is a filled sheet with a fine ink edge " + paper.name,
                area >= bounds.width * bounds.height * .94f && colors.Any(c => c.a > 0 && c.a < 200));
            Check(prefix + " paper mesh stays in its hit rectangle " + paper.name,
                vertices.All(v => v.x >= bounds.xMin - .5f && v.x <= bounds.xMax + .5f && v.y >= bounds.yMin - .5f && v.y <= bounds.yMax + .5f));
        }
        static void StoryControlFeedback(Button button, Action disabledClickCheck)
        {
            if (!button.IsInteractable()) { StoryContrast(button, "disabled"); return; }
            var previousSelection = EventSystem.current.currentSelectedGameObject;
            var label = button.GetComponentInChildren<TMP_Text>(); var start = label.rectTransform.anchoredPosition;
            var pointer = StoryPointer(button);
            try
            {
                EventSystem.current.SetSelectedGameObject(null);
                ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerExitHandler);
                StoryContrast(button, "normal"); var normal = StoryRenderedColor(button.targetGraphic);
                ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
                StoryContrast(button, "hover");
                Check(button.name + " hover has visible feedback", StoryRenderedColor(button.targetGraphic) != normal);
                EventSystem.current.SetSelectedGameObject(button.gameObject);
                StoryContrast(button, "selected");
                ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerDownHandler);
                StoryContrast(button, "pressed");
                ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerUpHandler);
                button.interactable = false; StoryContrast(button, "disabled");
                ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
                StoryContrast(button, "disabled-hover");
                ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler); disabledClickCheck?.Invoke();
                button.interactable = true; EventSystem.current.SetSelectedGameObject(null);
                for (int repeat = 0; repeat < 3; repeat++)
                {
                    ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerExitHandler);
                    ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
                }
                ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerExitHandler);
                StoryContrast(button, "restored");
                Check(button.name + " hover never drifts caption", label.rectTransform.anchoredPosition == start);
            }
            finally
            {
                button.interactable = true;
                ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerExitHandler);
                EventSystem.current.SetSelectedGameObject(previousSelection != null && previousSelection.activeInHierarchy ? previousSelection : null);
            }
        }

        static PointerEventData StoryPointer(Button button)
        {
            var rect = (RectTransform)button.transform;
            return new PointerEventData(EventSystem.current) {
                position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)),
                button = PointerEventData.InputButton.Left
            };
        }
        static Color StoryRenderedColor(Graphic graphic) => graphic.color * graphic.canvasRenderer.GetColor();
        static Color StoryComposite(Color foreground, Color background) => new Color(
            foreground.r * foreground.a + background.r * (1 - foreground.a),
            foreground.g * foreground.a + background.g * (1 - foreground.a),
            foreground.b * foreground.a + background.b * (1 - foreground.a), 1);
        static float StoryLuminance(Color color)
        {
            float Linear(float channel) => channel <= .04045f ? channel / 12.92f : Mathf.Pow((channel + .055f) / 1.055f, 2.4f);
            return .2126f * Linear(color.r) + .7152f * Linear(color.g) + .0722f * Linear(color.b);
        }
        static void StoryContrast(Button button, string status)
        {
            StoryTextContrast(button.GetComponentInChildren<TMP_Text>(), button.targetGraphic, button.name + "/" + status);
            var seal = button.GetComponentsInChildren<QingshiComicPaperGraphic>().SingleOrDefault(g => g.Kind == QingshiComicPaperGraphic.Surface.Seal);
            if (seal != null) StoryTextContrast(seal.GetComponentInChildren<TMP_Text>(), seal, button.name + "/seal/" + status);
        }
        static void StoryTextContrast(TMP_Text label, Graphic surfaceGraphic, string prefix)
        {
            label.ForceMeshUpdate();
            var glyphs = label.textInfo.meshInfo.Where(m => m.vertexCount > 0)
                .SelectMany(m => m.colors32.Take(m.vertexCount)).Where(c => c.a > 0).ToArray();
            Require(prefix + " renders glyphs", glyphs.Length > 0);
            Color glyph = glyphs[0];
            var ink = glyph * label.canvasRenderer.GetColor(); var paper = StoryRenderedColor(surfaceGraphic);
            foreach (var backdrop in new[] { Color.black, Color.white })
            {
                var surface = StoryComposite(paper, backdrop); var text = StoryComposite(ink, surface);
                float a = StoryLuminance(text), b = StoryLuminance(surface);
                float contrast = (Mathf.Max(a, b) + .05f) / (Mathf.Min(a, b) + .05f);
                Check(prefix + " readable on " + (backdrop == Color.white ? "light" : "dark") + " art",
                    contrast >= 4.5f, "contrast=" + contrast.ToString("F2") + "; ink=" + ink + "; paper=" + paper);
            }
        }

        static void StoryDirectoryLayout(SaveData save, Dictionary<string, QingshiComicScene> scripts)
        {
            Canvas.ForceUpdateCanvases();
            int current = QingshiStory.CurrentIndex(save); string prefix = "directory/" + current;
            string before = JsonUtility.ToJson(save);
            var content = Field(host, "chapterBodyHost") as RectTransform;
            var scroll = Field(host, "chapterScroll") as ScrollRect;
            Require(prefix + " has real scroll", content != null && scroll != null && scroll.viewport != null);
            var rows = content.GetComponentsInChildren<Button>().Where(b => b.name.StartsWith("Story chapter ", StringComparison.Ordinal)).ToArray();
            Check(prefix + " keeps chapter order and button names", rows.Select(b => b.name).SequenceEqual(QingshiStory.Scenes.Select(s => "Story chapter " + s.Id)));
            var acts = content.GetComponentsInChildren<TMP_Text>().Where(t => t.name.StartsWith("Story act ", StringComparison.Ordinal)).ToArray();
            Check(prefix + " groups the authored acts", acts.Select(t => t.name).SequenceEqual(QingshiStory.Scenes.Select(s => s.Act).Distinct().Select(a => "Story act " + a)));
            foreach (var label in content.GetComponentsInChildren<TMP_Text>())
            {
                label.ForceMeshUpdate();
                Check(prefix + " text fits " + label.name, !label.isTextOverflowing);
            }
            for (int i = 0; i < QingshiStory.Scenes.Count; i++)
            {
                var scene = QingshiStory.Scenes[i]; var row = rows[i];
                bool done = i < current, ready = QingshiStory.CanEnter(save, scene.Id);
                Check(prefix + "/" + scene.Id + " progression gates click", row.IsInteractable() == (done || ready));
                var labels = row.GetComponentsInChildren<TMP_Text>();
                var title = labels.Single(t => t.name.EndsWith(" title", StringComparison.Ordinal));
                var summary = labels.Single(t => t.name.EndsWith(" summary", StringComparison.Ordinal));
                var status = labels.Single(t => t.name.EndsWith(" status", StringComparison.Ordinal));
                var preview = row.GetComponentsInChildren<RawImage>(true).SingleOrDefault(r => r.name == "Story preview " + scene.Id);
                var rowRect = StoryRect(content, (RectTransform)row.transform);
                Check(prefix + "/" + scene.Id + " summary clears title", !StoryRect(content, title.rectTransform).Overlaps(StoryRect(content, summary.rectTransform)));
                foreach (var label in labels)
                    Check(prefix + "/" + scene.Id + " text inside card " + label.name, StoryContains(rowRect, StoryRect(content, label.rectTransform)));
                if (i > 0) Check(prefix + "/" + scene.Id + " cards do not overlap", !rowRect.Overlaps(StoryRect(content, (RectTransform)rows[i - 1].transform)));
                var act = acts.Single(t => t.name == "Story act " + scene.Act);
                Check(prefix + "/" + scene.Id + " follows its act heading", StoryRect(content, act.rectTransform).yMin >= rowRect.yMax);
                if (done || ready)
                {
                    Check(prefix + "/" + scene.Id + " known title and state", title.text.Contains(scene.Title) && status.text.Contains(done ? "回看" : "续写"));
                    var opening = scripts[scene.Id].panels[0]; string art = StoryArtId(scripts[scene.Id], opening);
                    Check(prefix + "/" + scene.Id + " uses only opening preview", preview != null && preview.gameObject.activeSelf &&
                        preview.texture == Resources.Load<Texture2D>("Art/Story/Qingshi/" + art) && !preview.raycastTarget);
                    Check(prefix + "/" + scene.Id + " preview fits card", preview != null && StoryContains(rowRect, StoryRect(content, preview.rectTransform)));
                    string knownText = done ? scene.Summary : opening.text;
                    if (!string.IsNullOrWhiteSpace(knownText))
                        Check(prefix + "/" + scene.Id + " summary limited to known content", knownText.Replace("\r", "").Replace("\n", " ").Trim().StartsWith(summary.text.TrimEnd('…'), StringComparison.Ordinal));
                }
                else
                {
                    Check(prefix + "/" + scene.Id + " future has no title or artwork spoiler", preview == null && !title.text.Contains(scene.Title) &&
                        labels.All(t => !t.text.Contains(scene.Summary)) && status.text == "未启");
                    ExecuteEvents.Execute(row.gameObject, StoryPointer(row), ExecuteEvents.pointerClickHandler);
                    Check(prefix + "/" + scene.Id + " locked pointer click keeps directory", host.ChapterScrollOpenForCheck && host.QingshiComicForCheck == null);
                }
                if (ready) Check(prefix + "/" + scene.Id + " current entry visible without scrolling", StoryContains(scroll.viewport.rect, StoryRect(scroll.viewport, (RectTransform)row.transform)));
            }
            var action = Field(host, "chapterAction") as Button;
            bool canContinue = current >= 0 && current < QingshiStory.Scenes.Count && QingshiStory.CanEnter(save, QingshiStory.Scenes[current].Id);
            Check(prefix + " fixed continue matches progress", action != null && action.gameObject.activeSelf == canContinue);
            if (canContinue)
            {
                var lead = QingshiStoryJourney.Current(save);
                string expectedAction = string.IsNullOrEmpty(lead?.NpcId) ? "续读 · " + QingshiStory.Scenes[current].Title : "寻访 · " + lead.NpcName;
                Check(prefix + " fixed action follows current NPC or continuous scene", action.GetComponentInChildren<TMP_Text>().text == expectedAction);
                StoryButtonHit(action.name);
            }
            float position = scroll.verticalNormalizedPosition;
            scroll.StopMovement(); scroll.verticalNormalizedPosition = 0; Canvas.ForceUpdateCanvases();
            Check(prefix + " last row reachable", StoryContains(scroll.viewport.rect, StoryRect(scroll.viewport, (RectTransform)rows[rows.Length - 1].transform)));
            if (rows[rows.Length - 1].IsInteractable()) StoryButtonHit(rows[rows.Length - 1].name);
            scroll.verticalNormalizedPosition = position; Canvas.ForceUpdateCanvases();
            Check(prefix + " browsing never changes save", before == JsonUtility.ToJson(save));
        }
        static void StoryDirectoryReveal(Button button)
        {
            var scroll = Field(host, "chapterScroll") as ScrollRect;
            Canvas.ForceUpdateCanvases(); scroll.StopMovement();
            float offset = -((RectTransform)button.transform).anchoredPosition.y;
            float overflow = Mathf.Max(0, scroll.content.rect.height - scroll.viewport.rect.height);
            scroll.verticalNormalizedPosition = overflow > 0 ? 1 - Mathf.Clamp(offset, 0, overflow) / overflow : 1;
            Canvas.ForceUpdateCanvases();
        }

        static Rect StoryRect(RectTransform root, RectTransform rect)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            Vector2 min = root.InverseTransformPoint(corners[0]), max = min;
            for (int i = 1; i < corners.Length; i++) { Vector2 point = root.InverseTransformPoint(corners[i]); min = Vector2.Min(min, point); max = Vector2.Max(max, point); }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
        static bool StoryContains(Rect outer, Rect inner) => inner.xMin >= outer.xMin - .5f && inner.yMin >= outer.yMin - .5f && inner.xMax <= outer.xMax + .5f && inner.yMax <= outer.yMax + .5f;
        static IEnumerator StoryKey(Key key)
        {
            var originalSettings = InputSystem.settings;
            var isolatedSettings = UnityEngine.Object.Instantiate(originalSettings);
            isolatedSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            isolatedSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = isolatedSettings;
            var disabled = InputSystem.devices.Where(d => d.enabled && (d is Keyboard || d is Mouse)).ToArray();
            foreach (var device in disabled) InputSystem.DisableDevice(device);
            var keyboard = InputSystem.AddDevice<Keyboard>("StoryVerificationKeyboard");
            try
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(key)); keyboard.MakeCurrent(); yield return Frames(3);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return Frames(2);
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
                foreach (var device in disabled) if (device.added) InputSystem.EnableDevice(device);
                InputSystem.settings = originalSettings;
                UnityEngine.Object.Destroy(isolatedSettings);
            }
        }
        static void StoryButtonHit(string name)
        {
            Canvas.ForceUpdateCanvases(); var button = StoryButton(name);
            var rect = (RectTransform)button.transform;
            var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)) };
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
