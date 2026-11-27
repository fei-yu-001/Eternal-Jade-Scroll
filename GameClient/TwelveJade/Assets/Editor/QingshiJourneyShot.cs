using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using TwelveJade.Core;
using TwelveJade.Presentation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TwelveJade.Editor
{
    public static partial class PrologueTownShot
    {
        // Called from the existing fresh journal case. Only fixture placement is snapped;
        // the approach after clicking the directory must use the real travel/arrival path.
        static IEnumerator StoryJourneyDirectoryEntry()
        {
            var save = host.ActiveSave; var lead = QingshiStoryJourney.Current(save);
            Require("journey fresh home lead", lead != null && lead.SceneId == "home");
            string progress = JourneyProgress(save);
            JourneyPlaceAway(lead.NpcId);
            Invoke(host, "OpenTownDialogue", lead.NpcId, false);
            Check("journey remote direct talk is refused", host.StoryLeadDialogueForCheck == null &&
                Field(host, "dialoguePanel") == null && host.QingshiComicForCheck == null);
            var start = host.PlayerWorldPosition;
            JourneyClick(StoryButton("Chapter action"));
            yield return JourneyArrival(lead.NpcId, start, true);
            yield return JourneyBridgeReady();
            yield return Capture("story-journey-home-bridge", "journey");
            yield return JourneyChooseBridge(2);
            Check("journey later closes without progress", JourneyProgress(save) == progress &&
                host.QingshiComicForCheck == null && !host.ChapterScrollOpenForCheck);
            JourneyMarkers("journey initial home");
            JourneyClick(host.NpcButtonForShot(lead.NpcId));
            yield return JourneyArrival(lead.NpcId, host.PlayerWorldPosition, false);
            yield return JourneyChooseBridge(0);
            Check("journey bridge opens home without committing", host.QingshiComicForCheck?.SceneId == "home" &&
                JourneyProgress(save) == progress);
        }

        static IEnumerator StoryJourneyChecks(CharacterPreset preset, Dictionary<string, QingshiComicScene> scripts)
        {
            var settings = Field(host, "settings"); var motion = settings.GetType().GetField("reduceMotion");
            bool oldMotion = (bool)motion.GetValue(settings);
            try
            {
                motion.SetValue(settings, true);
                host.Repository.Create(3, preset.id, "故里寻访", "male", 0, null, "shaxing");
                host.LoadSlot(host.Repository.Read(3));
                yield return Until(() => host.ActiveSave?.slot == 3 && host.TravelerPuppetForShot != null &&
                    host.NpcPatrolsLiveForShot, "journey isolated town ready", 20);
                yield return Frames(4);
                var save = host.ActiveSave;
                string assets = Ledger(save), chapter = JsonUtility.ToJson(save.chapter);
                Require("journey home NPC fixture position", host.SnapTravelerNextToNpc("npc-shitou"));
                yield return Frames(3); JourneyMarkers("journey home");
                int exclamationVertices = JourneySign("npc-shitou", "journey exclamation");
                yield return Capture("story-journey-exclamation", "journey");
                yield return JourneyHudFollow();
                yield return JourneyArrival("npc-shitou", host.PlayerWorldPosition, false);
                string progress = JourneyProgress(save);
                yield return JourneyChooseBridge(1);
                var ordinary = Field(host, "dialoguePanel") as DialogueUI;
                Require("journey ordinary-chat escape reaches legacy dialogue", ordinary != null && ordinary.IsOpen &&
                    !ordinary.CurrentNodeId.StartsWith("qingshi-lead-", StringComparison.Ordinal) && host.StoryLeadDialogueForCheck == null);
                Check("journey ordinary chat does not complete story", JourneyProgress(save) == progress && host.QingshiComicForCheck == null);
                // Opening the legacy panel has its own short anti-click-through guard.
                // Wait for real input readiness before testing the user's close button.
                yield return Until(() => Field(ordinary, "inputArmed") is bool armed && armed,
                    "journey ordinary chat input armed");
                JourneyClick(ordinary.GetComponentsInChildren<Button>().Single(b => b.name == "Dialogue close"));
                yield return Until(() => !host.DialogueInputLockedForShot && Field(host, "dialoguePanel") == null, "journey ordinary chat releases input");

                // The first three completions return to the town and advertise the next real person.
                for (int i = 0; i < 4; i++)
                {
                    var lead = QingshiStoryJourney.Current(save);
                    Require("journey ordered NPC lead " + i, lead != null && lead.SceneId == QingshiStory.Scenes[i].Id && !string.IsNullOrEmpty(lead.NpcId));
                    Require("journey NPC fixture position " + i, host.SnapTravelerNextToNpc(lead.NpcId));
                    yield return Frames(3); JourneyMarkers("journey before " + lead.SceneId);
                    int vertices = JourneySign(lead.NpcId, "journey " + lead.SceneId);
                    if (i == 3)
                    {
                        Check("journey question mark has its own curved mesh", vertices > exclamationVertices && lead.Marker == QuestOverheadState.Completable);
                        yield return Capture("story-journey-question", "journey");
                    }
                    JourneyClick(host.NpcButtonForShot(lead.NpcId));
                    yield return JourneyArrival(lead.NpcId, host.PlayerWorldPosition, false);
                    yield return JourneyChooseBridge(0);
                    yield return JourneyReadScene(lead.SceneId, scripts);
                    Check("journey completion persisted " + lead.SceneId, QingshiStory.CurrentIndex(save) == i + 1 &&
                        QingshiStory.CurrentIndex(host.Repository.Read(3).Data) == i + 1);
                    if (i < 3)
                    {
                        Check("journey early completion returns to town " + lead.SceneId, host.QingshiComicForCheck == null &&
                            !host.ChapterScrollOpenForCheck && Field(host, "modal") == null && !host.DialogueInputLockedForShot);
                        JourneyMarkers("journey after " + lead.SceneId);
                    }
                    if (i == 0)
                    {
                        Check("journey home switches to Zhao Da", QingshiStoryJourney.Current(save)?.NpcId == "npc-zhao-da");
                        string beforeReplay = JourneyProgress(save), disk = Sha(Path.Combine(state.scratch, "slot-3.json"));
                        host.ShowTownChapterPanel(); yield return JournalReady(); StoryDirectoryLayout(save, scripts);
                        JourneyClick(StoryButton("Story chapter home"));
                        yield return Until(() => host.QingshiComicForCheck != null, "journey directory replay opens");
                        Require("journey old entry remains a replay", host.QingshiComicForCheck.IsReplay);
                        yield return JourneyReadScene("home", scripts); yield return JournalReady();
                        Check("journey replay does not move lead or write", JourneyProgress(save) == beforeReplay &&
                            Sha(Path.Combine(state.scratch, "slot-3.json")) == disk && QingshiStoryJourney.Current(save)?.NpcId == "npc-zhao-da");
                        JourneyMarkers("journey after replay"); host.CloseChapterPanel(); yield return Frames(3);
                    }
                }

                // These scenes are contiguous spectator storytelling: no NPC bridge or journal gap.
                for (int i = 4; i < QingshiStory.Scenes.Count; i++)
                {
                    string id = QingshiStory.Scenes[i].Id;
                    Require("journey continuous scene opens directly " + id, host.QingshiComicForCheck?.SceneId == id &&
                        host.QingshiComicForCheck.PanelIndex == 0 && !host.ChapterScrollOpenForCheck && host.StoryLeadDialogueForCheck == null);
                    JourneyMarkers("journey continuous " + id);
                    string beforeClose = JourneyProgress(save), disk = Sha(Path.Combine(state.scratch, "slot-3.json"));
                    JourneyClick(StoryButton("Story close")); yield return Frames(3);
                    Check("journey interruption retains current scene " + id, host.QingshiComicForCheck == null &&
                        !host.ChapterScrollOpenForCheck && QingshiStoryJourney.Current(save)?.SceneId == id &&
                        JourneyProgress(save) == beforeClose && Sha(Path.Combine(state.scratch, "slot-3.json")) == disk);
                    yield return JourneyHudFollow();
                    yield return Until(() => host.QingshiComicForCheck?.SceneId == id, "journey resumes same segment " + id);
                    Check("journey continuation needs no living NPC " + id, host.StoryLeadDialogueForCheck == null &&
                        host.QingshiComicForCheck.PanelIndex == 0 && QingshiStoryJourney.IsContinuous(save));
                    yield return JourneyReadScene(id, scripts);
                }
                Require("journey has a distinct ending", Live("Qingshi journey ending") && host.QingshiComicForCheck == null && !host.ChapterScrollOpenForCheck);
                Check("journey completed without old chapter or asset rewards", QingshiStoryJourney.Current(save) == null &&
                    !QingshiStoryJourney.IsContinuous(save) && Ledger(save) == assets && JsonUtility.ToJson(save.chapter) == chapter &&
                    save.HasFlag(PrologueRules.DepartureFlag) && !save.HasFlag(PrologueRules.InitiationFlag));
                JourneyMarkers("journey ending");
                yield return Capture("story-journey-ending", "journey");
                JourneyClick(StoryButton("Journey ending journal")); yield return JournalReady(); StoryDirectoryLayout(save, scripts);
                host.CloseChapterPanel(); yield return Frames(3);

                // A real legacy-shaped slot with orphaned later flags must still direct to the first hole.
                var legacy = host.Repository.Read(3).Data;
                Require("journey reuses only its isolated slot", legacy != null && legacy.slot == 3);
                legacy.oneTimeFlags = new[] { QingshiStory.FlagPrefix + "home", QingshiStory.FlagPrefix + "tiger",
                    QingshiStory.FlagPrefix + "tomb", PrologueRules.DepartureFlag, PrologueRules.InitiationFlag };
                legacy.chapter = new ChapterState { reached = (int)ChapterSegment.Departure, settled = new[] { "Prologue", "Departure" } };
                host.Repository.Write(legacy); host.LoadSlot(host.Repository.Read(3));
                yield return Until(() => host.ActiveSave != save && host.ActiveSave?.slot == 3 && host.NpcPatrolsLiveForShot,
                    "journey legacy sparse town reload", 20);
                yield return Frames(4); save = host.ActiveSave;
                Check("journey legacy and sparse flags do not skip livelihood", QingshiStory.CurrentIndex(save) == 1 &&
                    QingshiStoryJourney.Current(save)?.NpcId == "npc-zhao-da");
                Require("journey legacy NPC fixture position", host.SnapTravelerNextToNpc("npc-zhao-da"));
                yield return Frames(3); JourneyMarkers("journey legacy sparse"); JourneySign("npc-zhao-da", "journey legacy sparse");
                host.ShowTownChapterPanel(); yield return JournalReady(); StoryDirectoryLayout(save, scripts);
                progress = JourneyProgress(save);
                JourneyClick(StoryButton("Story chapter livelihood"));
                yield return JourneyArrival("npc-zhao-da", host.PlayerWorldPosition, false);
                yield return JourneyChooseBridge(2);
                Check("journey legacy bridge close preserves sparse flags", JourneyProgress(save) == progress);
            }
            finally { motion.SetValue(settings, oldMotion); }
        }

        static string JourneyProgress(SaveData save) => string.Join("|", (save.oneTimeFlags ?? Array.Empty<string>())
            .Where(f => f != null && (f.StartsWith(QingshiStory.FlagPrefix, StringComparison.Ordinal) ||
                f == PrologueRules.DepartureFlag || f == PrologueRules.InitiationFlag)));

        static float JourneyDistance(string npcId)
        {
            var foot = host.TownNpcFootPosition(npcId);
            return foot.HasValue ? Vector2.Distance(host.PlayerWorldPosition, foot.Value) : float.PositiveInfinity;
        }

        static void JourneyPlaceAway(string npcId)
        {
            var foot = host.TownNpcFootPosition(npcId);
            Require("journey actual NPC foot", foot.HasValue);
            var npc = host.World.Npcs.Single(n => n.Id == npcId);
            var destination = (Vector2)Invoke(host, "ArrivalPointNear", foot.Value, npc.Radius + 36f);
            foreach (var offset in new[] { new Vector2(340, 0), new Vector2(-340, 0), new Vector2(0, 340), new Vector2(0, -340),
                new Vector2(300, 180), new Vector2(-300, 180) })
            {
                var point = foot.Value + offset;
                if (!host.TerrainStandable(point.x, point.y)) continue;
                var route = TownNavigator.FindPath(host.World, point.x, point.y, destination.x, destination.y);
                if (route == null || route.Count == 0) continue;
                if (host.SnapTravelerTo(point.x, point.y)) { Require("journey starts outside talk reach", JourneyDistance(npcId) > QuestRuntime.InteractionDistance + 80); return; }
            }
            Require("journey reachable remote fixture", false, npcId);
        }

        static IEnumerator JourneyArrival(string npcId, Vector2 start, bool mustWalk)
        {
            bool premature = false, walked = false, routed = host.TravelBusy;
            yield return Until(() => {
                walked |= Vector2.Distance(start, host.PlayerWorldPosition) > 1;
                routed |= host.TravelBusy;
                if (JourneyDistance(npcId) > QuestRuntime.InteractionDistance + .5f)
                    premature |= host.StoryLeadDialogueForCheck != null || host.QingshiComicForCheck != null;
                return host.StoryLeadDialogueForCheck != null && host.StoryLeadDialogueForCheck.IsOpen;
            }, "journey reaches actual bridge " + npcId, 20);
            Check("journey arrival respects live NPC distance " + npcId, !premature && JourneyDistance(npcId) <= QuestRuntime.InteractionDistance + .5f &&
                !host.TravelBusy && host.DialogueInputLockedForShot && host.QingshiComicForCheck == null);
            if (mustWalk) Check("journey directory actually walks a route", routed && walked);
        }

        static IEnumerator JourneyBridgeReady()
        {
            var panel = host.StoryLeadDialogueForCheck;
            Require("journey real shared dialogue", panel != null && panel.IsOpen);
            yield return Until(() => Field(panel, "inputArmed") is bool armed && armed, "journey bridge input armed");
            float deadline = Time.realtimeSinceStartup + 8;
            while ((panel.IsTyping || panel.CurrentPage < panel.PageCount) && Time.realtimeSinceStartup < deadline)
            { JourneyClick(panel.GetComponentsInChildren<Button>().Single(b => b.name == "Dialogue continue")); yield return Frames(2); }
            var lead = QingshiStoryJourney.Current(host.ActiveSave);
            Require("journey bridge presents current NPC and words", lead != null && !panel.IsTyping && panel.CurrentPage == panel.PageCount &&
                panel.CurrentNodeId == "qingshi-lead-" + lead.SceneId && Compact(panel.DialogueText.text) == Compact(lead.Bridge));
            var choices = panel.GetComponentsInChildren<Button>().Where(b => b.name.StartsWith("Dialogue choice ", StringComparison.Ordinal)).ToArray();
            Check("journey bridge has all three escape choices", choices.Select(b => b.name).SequenceEqual(new[] { "Dialogue choice 0", "Dialogue choice 1", "Dialogue choice 2" }) &&
                choices[0].GetComponentInChildren<TMP_Text>().text == lead.Reply &&
                choices[1].GetComponentInChildren<TMP_Text>().text == "先聊聊别的事" && choices[2].GetComponentInChildren<TMP_Text>().text == "稍后再来");
            yield return Frames(2);
        }

        static IEnumerator JourneyChooseBridge(int option)
        {
            yield return JourneyBridgeReady();
            var panel = host.StoryLeadDialogueForCheck;
            JourneyClick(panel.GetComponentsInChildren<Button>().Single(b => b.name == "Dialogue choice " + option));
            Check("journey closes bridge before opening destination " + option, host.StoryLeadDialogueForCheck == null &&
                host.QingshiComicForCheck == null && host.DialogueInputLockedForShot);
            yield return Until(() => option == 0 ? host.QingshiComicForCheck != null : option == 1
                ? (Field(host, "dialoguePanel") as DialogueUI)?.IsOpen == true && host.StoryLeadDialogueForCheck == null
                : !host.DialogueInputLockedForShot && Field(host, "dialoguePanel") == null, "journey bridge destination " + option);
            yield return Frames(2);
        }

        static IEnumerator JourneyHudFollow()
        {
            var button = StoryButton("Hud story follow"); var roll = button.GetComponentInParent<QuestBriefRoll>();
            Require("journey HUD has a real quest roll", roll != null);
            if (!roll.Expanded) JourneyClick(StoryButton("Hud quest toggle"));
            yield return Until(() => button.IsInteractable(), "journey HUD follow visible");
            var lead = QingshiStoryJourney.Current(host.ActiveSave);
            Check("journey HUD shows current objective", TextAt("Hud story objective") == Compact(lead?.Objective));
            JourneyClick(button); yield return null;
        }

        static IEnumerator JourneyReadScene(string id, Dictionary<string, QingshiComicScene> scripts)
        {
            var comic = host.QingshiComicForCheck; var pages = scripts[id].panels;
            Require("journey reads actual scene " + id, comic != null && comic.SceneId == id && comic.PanelCount == pages.Length);
            for (int page = 0; page < pages.Length; page++)
            {
                Require("journey actual page " + id + "/" + page, host.QingshiComicForCheck == comic && comic.PanelIndex == page && comic.HasArtwork);
                yield return StoryReveal(comic);
                if ((pages[page].choices?.Length ?? 0) > 0)
                { JourneyClick(StoryButton("Story choice 0")); yield return StoryReveal(comic); }
                Require("journey page ready " + id + "/" + page, comic.CanAdvance);
                JourneyClick(StoryButton("Story next"));
                var next = host.QingshiComicForCheck;
                if (page == pages.Length - 1 && next != null && next != comic)
                {
                    Check("journey next segment starts on first frame " + next.SceneId, next.PanelIndex == 0 && !host.ChapterScrollOpenForCheck);
                    next.Advance();
                    Check("journey same-frame advance cannot skip new first panel " + next.SceneId, next.PanelIndex == 0);
                }
                yield return Frames(2);
            }
        }

        static void JourneyMarkers(string prefix)
        {
            var lead = QingshiStoryJourney.Current(host.ActiveSave); int mainCount = 0;
            foreach (var npc in host.World.Npcs)
            {
                var root = host.TownNpcRectForShot(npc.Id);
                var marker = root != null ? root.GetComponentInChildren<NpcQuestOverhead>(true) : null;
                Require(prefix + " actual NPC marker " + npc.Id, marker != null);
                bool expected = lead != null && lead.NpcId == npc.Id && lead.Marker != QuestOverheadState.None;
                if (marker.IsStory) mainCount++;
                Check(prefix + " only current NPC is a story lead " + npc.Id, marker.IsStory == expected && marker.GraphicForShot.IsStory == expected);
                if (expected) Check(prefix + " main marker state and visibility " + npc.Id, marker.State == lead.Marker && marker.gameObject.activeInHierarchy);
            }
            Check(prefix + " unique actionable main marker", mainCount == (lead != null && !string.IsNullOrEmpty(lead.NpcId) && lead.Marker != QuestOverheadState.None ? 1 : 0));
        }

        static int JourneySign(string npcId, string prefix)
        {
            Canvas.ForceUpdateCanvases();
            var marker = host.TownNpcRectForShot(npcId).GetComponentInChildren<NpcQuestOverhead>();
            Require(prefix + " visible story paper", marker != null && marker.IsStory && marker.GraphicForShot.IsStory);
            var graphic = marker.GraphicForShot; graphic.Rebuild(CanvasUpdate.PreRender);
            var mesh = graphic.canvasRenderer.GetMesh();
            Require(prefix + " actual submitted paper and ink triangles", mesh != null && mesh.vertexCount > 12 && mesh.triangles.Length > 18);
            Check(prefix + " visible paper and red ink", mesh.colors32.Any(c => c.a > 200 && c.r > 220 && c.g > 180) &&
                mesh.colors32.Any(c => c.a > 200 && c.r > c.g * 2 && c.r > c.b * 2));
            Check(prefix + " marker never intercepts NPC input or substitutes a glyph", !graphic.raycastTarget &&
                !marker.GetComponent<CanvasGroup>().blocksRaycasts && !marker.Label.enabled && graphic.GetComponent<Image>() == null);
            return mesh.vertexCount;
        }

        static void JourneyClick(Button button)
        {
            Require("journey clickable " + button?.name, button != null && button.isActiveAndEnabled && button.IsInteractable());
            foreach (var scroll in button.GetComponentsInParent<ScrollRect>())
            {
                if (scroll.content == null || scroll.viewport == null || !button.transform.IsChildOf(scroll.content)) continue;
                var item = StoryRect(scroll.content, (RectTransform)button.transform);
                float overflow = Mathf.Max(0, scroll.content.rect.height - scroll.viewport.rect.height);
                scroll.StopMovement(); scroll.verticalNormalizedPosition = overflow > 0
                    ? 1 - Mathf.Clamp(scroll.content.rect.yMax - item.yMax - 8, 0, overflow) / overflow : 1;
            }
            Canvas.ForceUpdateCanvases(); var rect = (RectTransform)button.transform;
            PointerEventData pointer = null; string hitNames = "";
            foreach (float y in new[] { .5f, .25f, .75f })
            {
                var point = new Vector2(rect.rect.center.x, Mathf.Lerp(rect.rect.yMin, rect.rect.yMax, y));
                var candidate = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(point)), button = PointerEventData.InputButton.Left };
                var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(candidate, hits);
                hitNames = string.Join(",", hits.Take(4).Select(h => h.gameObject.name));
                if (hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == button) { pointer = candidate; break; }
            }
            Require("journey real pointer target " + button.name, pointer != null, hitNames);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }
    }
}
