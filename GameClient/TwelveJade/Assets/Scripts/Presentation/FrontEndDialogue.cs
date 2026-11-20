using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using TwelveJade.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TwelveJade.Presentation
{
    public sealed partial class FrontEndController
    {
        DialogueUI dialoguePanel;
        SaveData dialogueOwnerSave;
        int dialogueLocks;
        int dialogueConsumeThroughFrame = -1;
        long dialogueFrameSerial;
        bool applyingDialogue;
        readonly Dictionary<string, NpcQuestOverhead> questOverheads = new();

        bool WorldDialogueInputBlocked => dialogueLocks > 0 || Time.frameCount <= dialogueConsumeThroughFrame;
        public bool DialogueInputLockedForShot => WorldDialogueInputBlocked;
        public TMP_Text QuestOverheadLabelForShot(string npcId) => page == "town" && npcId != null &&
            questOverheads.TryGetValue(npcId, out var marker) && marker != null ? marker.Label : null;

        sealed class DialogueLease : IDisposable
        {
            Action release;
            public DialogueLease(Action release) { this.release = release; }
            public void Dispose() { var action = release; release = null; action?.Invoke(); }
        }

        void StopDialogueMovement()
        {
            CancelTravel(); travelerSpeedNow = 0f; travelerMoveDir = Vector2.zero;
            if (travelerPuppet != null) travelerPuppet.SetMotion(PuppetActor.Motion.Idle);
        }

        IDisposable AcquireDialogueInput(string npcId)
        {
            dialogueLocks++;
            StopDialogueMovement();
            SetNpcPatrolDialogue(npcId, true);
            if (content != null)
            {
                var group = content.GetComponent<CanvasGroup>();
                if (group != null) { group.interactable = false; group.blocksRaycasts = false; }
            }
            return new DialogueLease(() =>
            {
                StopDialogueMovement();
                SetNpcPatrolDialogue(npcId, false);
                dialogueLocks = Math.Max(0, dialogueLocks - 1);
                // The key/pointer closing the panel must not also move or reopen it this frame.
                dialogueConsumeThroughFrame = Time.frameCount + 1;
                if (content != null)
                {
                    var group = content.GetComponent<CanvasGroup>();
                    if (group != null) { group.interactable = true; group.blocksRaycasts = true; }
                }
            });
        }

        void OnDisable() => CloseTownDialogue();

        void CloseTownDialogue(DialogueUICloseReason reason = DialogueUICloseReason.Host)
        {
            var panel = dialoguePanel;
            if (panel != null) panel.Close(reason);
        }

        float DialogueDistance(string npcId)
        {
            var position = TownNpcFootPosition(npcId);
            return page == "town" && position.HasValue ? Vector2.Distance(worldPos, position.Value) : float.PositiveInfinity;
        }

        void RequestNpcDialogue(string npcId)
        {
            if (WorldDialogueInputBlocked || modal != null || bagOverlay != null || activeSave == null || page != "town") return;
            var npc = town?.Npcs.FirstOrDefault(n => n.Id == npcId);
            if (npc == null) return;
            if (DialogueDistance(npcId) <= QuestRuntime.InteractionDistance) { NpcArrived?.Invoke(npcId); return; }
            var spot = ArrivalPointNear(NpcLiveFoot(npc), npc.Radius + 36f);
            RequestTravelTo(spot, id => NpcArrived?.Invoke(id), npcId,
                () => DialogueDistance(npcId) <= QuestRuntime.InteractionDistance);
        }

        bool HandleTownDialogueInput()
        {
            var keyboard = Keyboard.current;
            if (dialoguePanel != null && dialoguePanel.IsOpen)
            {
                if (!ReferenceEquals(dialogueOwnerSave, activeSave) || page != "town")
                { CloseTownDialogue(); return true; }
                if (keyboard != null)
                {
                    if (keyboard.escapeKey.wasPressedThisFrame) dialoguePanel.HandleInput(DialogueUICommand.Close);
                    else if (keyboard.eKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame)
                        dialoguePanel.HandleInput(DialogueUICommand.Continue);
                    else if (keyboard.tabKey.wasPressedThisFrame) dialoguePanel.HandleInput(DialogueUICommand.ToggleAuto);
                    else if (keyboard.leftCtrlKey.wasPressedThisFrame) dialoguePanel.HandleInput(DialogueUICommand.Skip);
                    else if (keyboard.anyKey.wasPressedThisFrame)
                    {
                        var keys = new[] { keyboard.digit1Key, keyboard.digit2Key, keyboard.digit3Key, keyboard.digit4Key, keyboard.digit5Key, keyboard.digit6Key };
                        for (int i = 0; i < keys.Length; i++)
                        {
                            if (!keys[i].wasPressedThisFrame) continue;
                            var visible = VisibleDialogueOptions();
                            if (i < visible.Count) dialoguePanel.SelectOption(talkView.Options.ToList().IndexOf(visible[i]));
                            break;
                        }
                    }
                }
                return true;
            }
            if (WorldDialogueInputBlocked) return true;
            if (keyboard == null || page != "town" || activeSave == null || modal != null || bagOverlay != null || chapterPanel != null) return false;
            if (!keyboard.eKey.wasPressedThisFrame && !keyboard.spaceKey.wasPressedThisFrame) return false;
            var closest = town.Npcs.Where(n => DialogueDistance(n.Id) <= QuestRuntime.InteractionDistance)
                .OrderBy(n => DialogueDistance(n.Id)).FirstOrDefault();
            if (closest == null) return false;
            TalkTo(closest.Id);
            return dialoguePanel != null && dialoguePanel.IsOpen;
        }

        List<DialogueOption> VisibleDialogueOptions() =>
            Dialogues.BranchOptions(activeSave, talkView, talkTrades, Quests);

        DialogueUIFrame DialogueFrame()
        {
            var node = Dialogues.Find(talkView.NodeId);
            var visible = VisibleDialogueOptions();
            var indices = visible.Select(o => talkView.Options.ToList().IndexOf(o)).ToArray();
            var npc = town.Npcs.First(n => n.Id == talkNpcId);
            var portrait = DialoguePortraitCatalog.Find(talkNpcId);
            if (portrait == null) portrait = PortraitFor(talkNpcId)?.texture;
            bool linear = node != null && node.Options.Count == 0 && !string.IsNullOrEmpty(node.Next);
            return new DialogueUIFrame("town-dialogue-" + (++dialogueFrameSerial), talkView, indices,
                new DialogueUIPortrait(activeSave.characterName, DialoguePortraitCatalog.Hero(activeSave.gender)),
                new DialogueUIPortrait(npc.Name, portrait), linear, linear);
        }

        void OpenTownDialogue(string npcId)
        {
            if (WorldDialogueInputBlocked || activeSave == null || page != "town" || DialogueDistance(npcId) > QuestRuntime.InteractionDistance) return;
            if (!LoadTables()) return;
            if (string.IsNullOrEmpty(Dialogues.EntryNodeFor(npcId)))
            {
                OpenAmbientTownDialogue(town.Npcs.FirstOrDefault(n => n.Id == npcId));
                return;
            }
            CloseModal(); CloseInventory(); CloseChapterPanel();
            talkNpcId = npcId; talkTrades = TradesOf(npcId); fortuneVerdict = "";
            talkView = OpenEntryView(npcId); dialogueOwnerSave = activeSave;
            UpdateTravelerFacing(NpcLiveFoot(town.Npcs.First(n => n.Id == npcId)) - worldPos);
            try
            {
                dialoguePanel = DialogueUI.Open(canvas, ui, DialogueFrame(), new DialogueUICallbacks(
                    () => AcquireDialogueInput(npcId), ChooseTownDialogue, AdvanceTownDialogue,
                    reason => { dialoguePanel = null; dialogueOwnerSave = null; talkNpcId = ""; talkView = null; applyingDialogue = false; RefreshQuestOverheads(); }),
                    () => settings == null || settings.reduceMotion);
            }
            catch
            {
                SetNpcPatrolDialogue(npcId, false); dialogueOwnerSave = null; talkNpcId = ""; talkView = null;
                throw;
            }
        }

        // Map residents already carry authored lines. Display those through the same locked panel,
        // without inventing a merchant, quest, relation mutation or persistent dialogue-tree entry.
        void OpenAmbientTownDialogue(MapNpc npc)
        {
            if (npc == null || string.IsNullOrWhiteSpace(npc.Line)) return;
            CloseModal(); CloseInventory(); CloseChapterPanel();
            talkNpcId = npc.Id; dialogueOwnerSave = activeSave;
            var node = new DialogueNode("map-line-" + npc.Id, npc.Id, npc.Line, new List<DialogueOption>(), "");
            var table = new DialogueTable(new List<DialogueNode> { node });
            talkView = DialogueRules.Open(activeSave, table, npc.Id, node.Id);
            var frame = new DialogueUIFrame("town-ambient-" + (++dialogueFrameSerial), talkView, Array.Empty<int>(),
                new DialogueUIPortrait(activeSave.characterName, DialoguePortraitCatalog.Hero(activeSave.gender)),
                new DialogueUIPortrait(npc.Name, DialoguePortraitCatalog.Find(npc.Id)));
            UpdateTravelerFacing(NpcLiveFoot(npc) - worldPos);
            try
            {
                dialoguePanel = DialogueUI.Open(canvas, ui, frame, new DialogueUICallbacks(
                    () => AcquireDialogueInput(npc.Id),
                    (token, option) => dialoguePanel?.Reject(token, "这段话没有分支。"),
                    token => dialoguePanel?.Close(DialogueUICloseReason.Completed),
                    reason => { dialoguePanel = null; dialogueOwnerSave = null; talkNpcId = ""; talkView = null; applyingDialogue = false; RefreshQuestOverheads(); }),
                    () => settings == null || settings.reduceMotion);
            }
            catch
            {
                SetNpcPatrolDialogue(npc.Id, false); dialogueOwnerSave = null; talkNpcId = ""; talkView = null;
                throw;
            }
        }

        bool CurrentDialogueRequest(string token) => dialoguePanel != null && dialoguePanel.IsOpen &&
            dialoguePanel.PendingRequestToken == token && !applyingDialogue &&
            ReferenceEquals(activeSave, dialogueOwnerSave) && talkView != null;

        void ChooseTownDialogue(string requestToken, int index)
        {
            if (!CurrentDialogueRequest(requestToken)) return;
            if (index < 0 || index >= talkView.Options.Count || DialogueDistance(talkNpcId) > QuestRuntime.InteractionDistance)
            { dialoguePanel.Reject(requestToken, "请走近些再交谈。"); return; }
            var option = talkView.Options[index];
            if (!VisibleDialogueOptions().Contains(option)) { dialoguePanel.Reject(requestToken, "这句话此刻已不合适。"); return; }
            var tree = Dialogues; var meta = tree.MetaOf(talkView.NodeId, option); var quests = Quests;
            var accept = quests?.Find(meta.AcceptQuest); var turnIn = quests?.Find(meta.TurnInQuest);
            if (meta.Action == "acceptQuest" && (accept == null || QuestLedger.DisplayStatus(activeSave, accept) != QuestLedger.StatusAvailable ||
                (!string.IsNullOrEmpty(accept.GiverNpc) && accept.GiverNpc != talkNpcId)))
            { dialoguePanel.Reject(requestToken, "这件事已不待认领。"); return; }
            if (meta.Action == "turnInQuest" && !QuestRuntime.CanTurnIn(activeSave, turnIn, talkNpcId, DialogueDistance(talkNpcId)))
            { dialoguePanel.Reject(requestToken, "事情尚未办妥。"); return; }
            var npcId = talkNpcId;
            applyingDialogue = true;
            try
            {
                if (meta.Action == "acceptQuest")
                {
                    QuestLedger.RefreshAvailability(activeSave, quests);
                    if (!QuestLedger.TryStart(activeSave, accept)) throw new InvalidOperationException("Quest acceptance changed during the choice.");
                }
                if (meta.Action == "turnInQuest" && TwelveJade.Economy.QuestSettlement.TurnIn(activeSave, quests, Table, Karma, turnIn.Id, npcId, DialogueDistance(npcId)).Completed.Count != 1)
                    throw new InvalidOperationException("Quest turn-in changed during the choice.");
                var next = DialogueRules.Choose(activeSave, tree.Table, chapterNpcs, chapterClues, talkView, index, activeSave.WorldTimeNow(), out var notice);
                RecordKarmaAtNode(next?.NodeId);
                DriveQuests(new QuestSignal { TalkedNpcId = npcId });
                if (meta.Action == "triggerFateCheck")
                {
                    if (activeSave.coins >= FortunePrice) { activeSave.coins -= FortunePrice; fortuneVerdict = FortuneReading.VerdictFor(activeSave.destiny); }
                    else fortuneVerdict = "囊中羞涩，容我改日再来。";
                    next.Text = fortuneVerdict + "\n" + next.Text;
                }
                DialogueRules.Save(activeSave, DialogueRules.Snapshot(activeSave, next, activeSave.WorldTimeNow()));
                repository.Write(activeSave); // One successful choice, one save, including acceptance/turn-in/rewards.
                talkView = next; RefreshQuestOverheads();
                if (meta.Action == "openShop")
                {
                    var npc = town.Npcs.First(n => n.Id == npcId);
                    var merchant = Merchants.Find(string.IsNullOrEmpty(npc.Merchant) ? npcId : npc.Merchant);
                    var schedule = ScheduleFor(npcId);
                    bool duty = schedule == null || ScheduleRules.IsOnDuty(activeSave.WorldTimeNow(), WorldSchedules, schedule.Id);
                    if (merchant != null && duty)
                    { bool black = ScheduleRules.SeesBlackMarket(activeSave, activeSave.WorldTimeNow(), WorldSchedules, schedule?.Id); CloseTownDialogue(); ShowTrade(merchant.Id, black); return; }
                }
                if (meta.Action == "close") { CloseTownDialogue(DialogueUICloseReason.Completed); return; }
                if (!dialoguePanel.Present(DialogueFrame(), responseTo: requestToken)) throw new InvalidOperationException("Dialogue reply token rejected.");
                if (!string.IsNullOrEmpty(notice)) Notify(notice);
            }
            catch (Exception error)
            {
                // Effects may already have been applied: never turn this into an automatic retry.
                CloseTownDialogue(DialogueUICloseReason.Fault); Debug.LogException(error);
            }
            finally { applyingDialogue = false; }
        }

        void AdvanceTownDialogue(string requestToken)
        {
            if (!CurrentDialogueRequest(requestToken)) return;
            var node = Dialogues.Find(talkView.NodeId);
            if (node == null || node.Options.Count != 0 || string.IsNullOrEmpty(node.Next))
            { dialoguePanel.Reject(requestToken, "请先作出选择。"); return; }
            applyingDialogue = true;
            try
            {
                var next = DialogueRules.Open(activeSave, Dialogues.Table, talkNpcId, node.Next);
                RecordKarmaAtNode(next.NodeId); DriveQuests(new QuestSignal { TalkedNpcId = talkNpcId });
                DialogueRules.Save(activeSave, DialogueRules.Snapshot(activeSave, next, activeSave.WorldTimeNow()));
                repository.Write(activeSave); talkView = next;
                if (!dialoguePanel.Present(DialogueFrame(), responseTo: requestToken)) throw new InvalidOperationException("Dialogue reply token rejected.");
            }
            catch (Exception error) { CloseTownDialogue(DialogueUICloseReason.Fault); Debug.LogException(error); }
            finally { applyingDialogue = false; }
        }

        void BuildQuestOverheads()
        {
            questOverheads.Clear();
            Quests?.CrossCheckNpcs(town);
            var identities = TownNpcs;
            Func<bool> reduced = () => settings == null || settings.reduceMotion;
            foreach (var npc in town.Npcs)
            {
                var rect = TownNpcRectForShot(npc.Id); if (rect == null) continue;
                var marker = NpcQuestOverhead.Create(rect, ui, reduced);
                questOverheads[npc.Id] = marker; AttachNpcHeadMarker(npc.Id, marker.Rect);
                // Same live NPC parent and dynamic world-foot source as DialogueDistance; identity has its own 180px reveal.
                // Missing configured title stays empty: no invented occupation or fake quest marker.
                var definition = identities?.Find(npc.Id);
                NpcIdentityBadge.Create(rect, ui, this, npc, npc.Name, NpcIdentityRules.Title(definition));
            }
            RefreshQuestOverheads();
        }

        // Same dynamic centre used by NpcLiveFoot; hide until a real Town registration exists.
        // Avoid the public shot hook's LINQ predicate allocation in every badge LateUpdate.
        internal bool TryNpcIdentityFoot(MapNpc npc, out Vector2 foot)
        {
            foot = Vector2.zero;
            if (page != "town" || town == null || npc == null || !npcPatrolsLive) return false;
            if (!town.TryNpcPosition(npc.Id, out var x, out var y)) return false;
            foot = new Vector2(x, y); return true;
        }

        void RefreshQuestOverheads()
        {
            if (page != "town" || activeSave == null) return;
            var quests = Quests;
            foreach (var pair in questOverheads)
                if (pair.Value != null) pair.Value.Refresh(QuestRuntime.OverheadState(activeSave, quests, pair.Key));
        }
    }
}
