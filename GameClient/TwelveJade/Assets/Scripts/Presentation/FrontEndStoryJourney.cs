using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using TwelveJade.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    public sealed partial class FrontEndController
    {
        bool storyLeadDialogue;
        public DialogueUI StoryLeadDialogueForCheck => storyLeadDialogue ? dialoguePanel : null;
        public string StoryObjectiveForCheck => QingshiStoryJourney.Current(activeSave)?.Objective ?? "卷一已记 · 故里回望";

        // All player-facing current-story entries share the same real NPC/distance path.
        // Already recorded chapters remain independent, read-only replays.
        public void FollowQingshiStory()
        {
            if (activeSave == null || page != "town" || WorldDialogueInputBlocked || WorldCombatActive || bagOverlay != null) return;
            if (ChapterModalAlive) { CloseChapterThen(FollowQingshiStory); return; }
            if (modal != null) return;
            var lead = QingshiStoryJourney.Current(activeSave);
            if (lead == null) { ShowTownChapterPanel(); return; }
            if (!QingshiStory.CanEnter(activeSave, lead.SceneId))
            { Notify("存档暂时无法记录新的剧情，请先检查存档标记空间。"); return; }
            if (string.IsNullOrEmpty(lead.NpcId)) ShowQingshiComic(lead.SceneId, true);
            else
            {
                Notify(lead.Objective);
                RequestNpcDialogue(lead.NpcId);
            }
        }

        void OpenStoryDirectoryEntry(string id)
        {
            if (QingshiStory.CanReplay(activeSave, id)) ShowQingshiComic(id);
            else if (QingshiStoryJourney.Current(activeSave)?.SceneId == id) FollowQingshiStory();
        }

        bool TryOpenStoryLead(string npcId)
        {
            var lead = QingshiStoryJourney.Current(activeSave);
            if (lead == null || lead.NpcId != npcId || !QingshiStory.CanEnter(activeSave, lead.SceneId)) return false;
            var npc = town.Npcs.FirstOrDefault(n => n.Id == npcId);
            if (npc == null || DialogueDistance(npcId) > QuestRuntime.InteractionDistance) return false;
            CloseModal(); CloseInventory(); CloseChapterPanel();
            var owner = activeSave; var ownerContent = content;
            talkNpcId = npcId; dialogueOwnerSave = owner; storyLeadDialogue = true;
            DialogueOption Option(string caption) => new DialogueOption(caption, "", new List<string>(), new List<string>(),
                new List<string>(), NpcRelation.Min, NpcRelation.Min, false, "", "", "", 0);
            // Ephemeral bridge: never writes a legacy dialogue node, quest, reward or extra story flag.
            talkView = new DialogueView { NodeId = "qingshi-lead-" + lead.SceneId, NpcId = npcId, Text = lead.Bridge,
                Options = new[] { Option(lead.Reply), Option("先聊聊别的事"), Option("稍后再来") } };
            var frame = new DialogueUIFrame("qingshi-lead-" + (++dialogueFrameSerial), talkView, new[] { 0, 1, 2 },
                new DialogueUIPortrait(owner.characterName, DialoguePortraitCatalog.Hero(owner.gender)),
                new DialogueUIPortrait(npc.Name, DialoguePortraitCatalog.Find(npcId)));
            UpdateTravelerFacing(NpcLiveFoot(npc) - worldPos);
            try
            {
                dialoguePanel = DialogueUI.Open(canvas, ui, frame, new DialogueUICallbacks(
                    () => AcquireDialogueInput(npcId),
                    (token, option) =>
                    {
                        if (!CurrentDialogueRequest(token)) return;
                        if (option < 0 || option > 2 || DialogueDistance(npcId) > QuestRuntime.InteractionDistance ||
                            QingshiStoryJourney.Current(activeSave)?.SceneId != lead.SceneId)
                        { dialoguePanel.Reject(token, "此刻还不能接着说这件事。"); return; }
                        dialoguePanel.Close(DialogueUICloseReason.Completed);
                        if (option < 2) StartCoroutine(AfterStoryLead(ownerContent, owner, lead.SceneId, npcId, option == 0));
                    },
                    token => dialoguePanel?.Reject(token, "先选一句回应。"),
                    reason =>
                    {
                        dialoguePanel = null; dialogueOwnerSave = null; talkNpcId = ""; talkView = null;
                        storyLeadDialogue = false; applyingDialogue = false; RefreshQuestOverheads();
                    }), () => settings == null || settings.reduceMotion);
            }
            catch
            {
                storyLeadDialogue = false; SetNpcPatrolDialogue(npcId, false);
                dialogueOwnerSave = null; talkNpcId = ""; talkView = null; throw;
            }
            return true;
        }

        IEnumerator AfterStoryLead(RectTransform ownerContent, SaveData owner, string sceneId, string npcId, bool enter)
        {
            // Closing input is consumed by the dialogue lease through the next frame.
            yield return null; yield return null;
            if (content != ownerContent || page != "town" || !ReferenceEquals(activeSave, owner) ||
                modal != null || WorldDialogueInputBlocked || WorldCombatActive || bagOverlay != null) yield break;
            if (enter)
            {
                if (QingshiStoryJourney.Current(owner)?.SceneId == sceneId && DialogueDistance(npcId) <= QuestRuntime.InteractionDistance)
                    ShowQingshiComic(sceneId, true);
            }
            else OpenTownDialogue(npcId, true);
        }

        void FinishQingshiJourney(bool replay, bool journey)
        {
            RefreshQuestOverheads();
            if (replay || !journey) { ShowTownChapterPanel(); return; }
            var lead = QingshiStoryJourney.Current(activeSave);
            // The tomb, calamity and return are one uninterrupted night. No living-town errands
            // and no omniscient NPC retelling are inserted between these scenes.
            if (QingshiStoryJourney.IsContinuous(activeSave))
            {
                if (!ShowQingshiComic(lead.SceneId, true))
                {
                    ShowTownChapterPanel();
                    Notify("前段已经记下；下一段暂时无法开启，请检查存档标记空间。");
                }
                return;
            }
            if (lead != null) { Notify(lead.Objective); return; }
            ShowQingshiJourneyEnd();
        }

        void ShowQingshiJourneyEnd()
        {
            if (page != "town" || activeSave == null || modal != null) return;
            StopDialogueMovement();
            var group = content.GetComponent<CanvasGroup>(); group.interactable = group.blocksRaycasts = false;
            modal = ui.Stretch(screenCanvas, "Qingshi journey ending");
            var veil = modal.gameObject.AddComponent<Image>(); veil.color = new Color(.06f, .07f, .06f, .94f);
            var plate = ui.Rect(modal, "Journey ending paper", 450, 264, 1020, 550).gameObject.AddComponent<QingshiComicPaperGraphic>();
            plate.color = UiKit.Hex("EDE3CB");
            ui.Label(plate.transform, "卷一 · 血誓离镇", 70, 55, 880, 68, 40, UiKit.Ink, TextAlignmentOptions.Center);
            ui.Label(plate.transform, "亲人已经入土。你带着未明的血仇，走上离乡的路。\n\n此后的旅程尚待展开。此刻返回青石镇，是回望故里；已记的故事可展卷重读。",
                90, 162, 840, 196, 28, UiKit.Ink);
            StoryJourneyButton(plate.transform, "Journey ending journal", "展卷回看", new Rect(170, 408, 300, 64),
                () => { CloseModal(); ShowTownChapterPanel(); });
            StoryJourneyButton(plate.transform, "Journey ending revisit", "回望故里", new Rect(550, 408, 300, 64), CloseModal);
        }

        Button StoryJourneyButton(Transform parent, string name, string caption, Rect area, Action action)
        {
            var plate = ui.Rect(parent, name, area.x, area.y, area.width, area.height).gameObject.AddComponent<QingshiComicPaperGraphic>();
            plate.color = Color.white; plate.raycastTarget = true;
            var button = plate.gameObject.AddComponent<Button>(); button.targetGraphic = plate;
            var colors = button.colors; colors.normalColor = UiKit.Hex("F2E6C9"); colors.highlightedColor = UiKit.Hex("FFF3D6");
            colors.selectedColor = UiKit.Hex("EEE1BE"); colors.pressedColor = UiKit.Hex("D7C5A0"); colors.disabledColor = UiKit.Hex("D8D4C8");
            colors.fadeDuration = 0; button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var text = ui.Label(plate.transform, caption, 16, 8, area.width - 32, area.height - 16, 23, UiKit.Ink, TextAlignmentOptions.Center);
            text.raycastTarget = false; text.richText = false;
            button.onClick.AddListener(() => { PlayClick(); action?.Invoke(); });
            return button;
        }
    }
}
