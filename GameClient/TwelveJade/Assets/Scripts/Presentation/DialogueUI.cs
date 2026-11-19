using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    /// <summary>
    /// Host-created UGUI dialogue. No bootstrap, Core mutation or world wiring; local visuals borrow approved art.
    /// Route each keyboard command once; mouse buttons and keyboard share the same request gate.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class DialogueUI : MonoBehaviour
    {
        const float Width = 1920f, Height = 1080f, FadeSeconds = .15f;
        const float CharactersPerSecond = 32f, AutoPause = 1.2f, InputPause = .18f;
        readonly DialogueUIRequestState requests = new DialogueUIRequestState();
        readonly List<Button> choiceButtons = new List<Button>();
        readonly List<int> choiceIndices = new List<int>();
        UiKit kit;
        DialogueUICallbacks callbacks;
        Func<bool> reduceMotion;
        IDisposable inputLease;
        RectTransform root, stage, optionContent;
        CanvasGroup optionGroup;
        ScrollRect optionScroll;
        RawImage playerImage, npcImage;
        TextMeshProUGUI playerName, npcName, speakerName, body, status;
        TextMeshProUGUI autoLabel, continueLabel;
        Button continueButton, autoButton, skipButton, closeButton;
        DialogueUIPortrait playerPortrait, npcPortrait;
        GameObject previousFocus, lastFocusedChoice;
        bool open, cleaned, inputArmed, reduced;
        int bornFrame, lastInputFrame = -1, presentationFrame, page = 1, pages = 1, pageStart, pageEnd;
        float inputNotBefore, revealCount, readyAt, fadeStart;
        string notice = "";
        DialogueUISpeaker displayedSpeaker;
        Color playerFrom, npcFrom, playerTarget, npcTarget;

        public bool IsOpen => open && !cleaned;
        public bool IsAwaitingResult => requests.PendingToken != null;
        public bool IsTyping => body != null && body.maxVisibleCharacters < pageEnd;
        public bool IsShowingPlayerLine => displayedSpeaker == DialogueUISpeaker.Player;
        public bool AutoEnabled => requests.Auto;
        public bool SkipActive => requests.Skip;
        public string CurrentToken => requests.Frame?.Token ?? "";
        public string PendingRequestToken => requests.PendingToken ?? "";
        public string CurrentNodeId => requests.Frame?.NodeId ?? "";
        public int CurrentPage => page;
        public int PageCount => pages;
        public TMP_Text DialogueText => body;

        /// <summary>
        /// Parent must be an active host-owned Overlay layer above the world, below system dialogs.
        /// AcquireInputLock is mandatory: a raycast veil alone cannot stop world keyboard/travel.
        /// </summary>
        public static DialogueUI Open(RectTransform parent, UiKit kit, DialogueUIFrame first,
            DialogueUICallbacks callbacks, Func<bool> reduceMotion)
        {
            if (parent == null || !parent.gameObject.activeInHierarchy)
                throw new ArgumentException("Dialogue requires an active host layer.", nameof(parent));
            if (kit == null) throw new ArgumentNullException(nameof(kit));
            if (first == null) throw new ArgumentNullException(nameof(first));
            if (callbacks == null) throw new ArgumentNullException(nameof(callbacks));
            if (reduceMotion == null) throw new ArgumentNullException(nameof(reduceMotion));
            var lease = callbacks.AcquireInputLock();
            if (lease == null) throw new InvalidOperationException("The dialogue host must supply an input lease.");
            RectTransform created = null;
            DialogueUI panel = null;
            try
            {
                created = kit.Stretch(parent, "Dialogue UI");
                created.gameObject.SetActive(false);
                panel = created.gameObject.AddComponent<DialogueUI>();
                panel.inputLease = lease;
                panel.kit = kit; panel.callbacks = callbacks; panel.reduceMotion = reduceMotion;
                panel.root = created;
                panel.previousFocus = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
                panel.reduced = reduceMotion();
                panel.Build();
                panel.open = true;
                created.gameObject.SetActive(true);
                panel.ResetInput();
                if (!panel.Present(first)) throw new InvalidOperationException("Initial dialogue presentation was rejected.");
                return panel;
            }
            catch
            {
                if (panel != null) panel.Cleanup(DialogueUICloseReason.Fault, true);
                else
                {
                    try { lease.Dispose(); }
                    finally { if (created != null) Destroy(created.gameObject); }
                }
                throw;
            }
        }

        /// <summary>
        /// A reply must echo the callback's attempt token (not CurrentToken); each next.Token must be new.
        /// A null responseTo is only for an unsolicited refresh with no pending request.
        /// Returns false for stale/duplicate replies and never unlocks a different request.
        /// </summary>
        public bool Present(DialogueUIFrame next, string responseTo = null)
        {
            if (!IsOpen || !requests.Present(next, responseTo)) return false;
            notice = "";
            try
            {
                SetPortrait(DialogueUISpeaker.Player, next.Player);
                SetPortrait(DialogueUISpeaker.Npc, next.Npc);
                RebuildChoices();
                // Clicking a reply already commits the player line. Present the actual NPC response immediately.
                ShowLine(next.Text, next.Speaker);
                ResetInput();
                return true;
            }
            catch (Exception error) { Fail(error); return false; }
        }

        /// <summary>Only reject if the host applied no effects, or rolled the entire request back.</summary>
        public bool Reject(string token, string message)
        {
            if (!IsOpen || !requests.Reject(token)) return false;
            notice = string.IsNullOrWhiteSpace(message) ? "未能回应，请再试一次。" : message;
            ResetInput(); RefreshControls(); return true;
        }

        /// <summary>Swap a borrowed texture/crop without touching dialogue or request state.</summary>
        public void SetPortrait(DialogueUISpeaker side, DialogueUIPortrait portrait)
        {
            if (!IsOpen || portrait == null) return;
            if (side != DialogueUISpeaker.Player && side != DialogueUISpeaker.Npc)
                throw new ArgumentOutOfRangeException(nameof(side));
            var image = side == DialogueUISpeaker.Player ? playerImage : npcImage;
            var name = side == DialogueUISpeaker.Player ? playerName : npcName;
            if (side == DialogueUISpeaker.Player) playerPortrait = portrait; else npcPortrait = portrait;
            name.text = portrait.Name;
            image.texture = portrait.Texture;
            image.uvRect = portrait.Uv;
            image.gameObject.SetActive(portrait.Texture != null);
            if (portrait.Texture != null)
            {
                float aspect = portrait.Texture.width * portrait.Uv.width / (portrait.Texture.height * portrait.Uv.height);
                var box = (RectTransform)image.transform.parent;
                var size = box.rect.size;
                float width = Mathf.Min(size.x, size.y * aspect);
                image.rectTransform.sizeDelta = new Vector2(width, width / aspect);
            }
            if (speakerName != null && side == displayedSpeaker) speakerName.text = portrait.Name;
        }

        public bool HasPortrait(DialogueUISpeaker side) =>
            (side == DialogueUISpeaker.Player ? playerPortrait : npcPortrait)?.Texture != null;

        /// <summary>Call for a physical key-down, once, after the host has consumed world input.</summary>
        public bool HandleInput(DialogueUICommand command)
        {
            if (!IsOpen) return false;
            if (!ConsumeInput()) return true;
            if (command == DialogueUICommand.Close) { Close(DialogueUICloseReason.User); return true; }
            if (IsAwaitingResult) return true;
            notice = "";
            if (command == DialogueUICommand.Continue) ContinueLine();
            else if (command == DialogueUICommand.ToggleAuto)
            {
                requests.ToggleAuto(); readyAt = Time.unscaledTime;
            }
            else if (command == DialogueUICommand.Skip)
            {
                RevealPage();
                if (page < pages) SetPage(pages, true);
                RevealPage();
                requests.StartSkip();
            }
            RefreshControls(); return true;
        }

        /// <summary>Uses the original Core view index, not the position in the filtered button list.</summary>
        public bool SelectOption(int sourceOptionIndex)
        {
            if (!IsOpen || !ConsumeInput() || IsAwaitingResult) return false;
            if (IsTyping || page < pages) { RevealPage(); RefreshControls(); return false; }
            return SubmitChoice(sourceOptionIndex);
        }

        public void Close(DialogueUICloseReason reason = DialogueUICloseReason.Host) => Cleanup(reason, true);

        void Build()
        {
            var veil = root.gameObject.AddComponent<Image>();
            veil.color = UiKit.WorldVeil; veil.raycastTarget = true;
            stage = kit.Rect(root, "Dialogue stage", 0, 0, Width, Height);
            stage.anchorMin = stage.anchorMax = stage.pivot = new Vector2(.5f, .5f);
            stage.anchoredPosition = Vector2.zero;
            BuildPortrait(true, out playerImage, out playerName);
            BuildPortrait(false, out npcImage, out npcName);
            var paper = DialogueSurface.CreatePaper(kit, stage, "Dialogue thin paper", 80, 650, 1760, 350);
            speakerName = DialogueSurface.CreateNameSeal(kit, paper, 64, 30, 1012, 56);
            body = Text(paper, "Dialogue body", "", 64, 92, 1012, 174, 30, UiKit.Ink);
            body.characterSpacing = .6f;
            body.overflowMode = TextOverflowModes.Page;
            body.lineSpacing = 6;
            body.margin = new Vector4(0, 0, 0, 6);
            kit.Panel(paper, "Dialogue divider", 1110, 64, 1, 242, new Color(.36f, .30f, .19f, .24f));
            BuildOptions(paper);
            continueButton = Button(paper, "Dialogue continue", "继续", 64, 282, 150, 48,
                () => HandleInput(DialogueUICommand.Continue));
            continueLabel = continueButton.GetComponentInChildren<TextMeshProUGUI>(true);
            autoButton = Button(paper, "Dialogue auto", "自动", 230, 282, 138, 48,
                () => HandleInput(DialogueUICommand.ToggleAuto));
            autoLabel = autoButton.GetComponentInChildren<TextMeshProUGUI>(true);
            skipButton = Button(paper, "Dialogue skip", "跳过", 384, 282, 138, 48,
                () => HandleInput(DialogueUICommand.Skip));
            closeButton = Button(paper, "Dialogue close", "告辞", 1574, 282, 146, 48,
                () => HandleInput(DialogueUICommand.Close));
            status = Text(paper, "Dialogue status", "", 550, 282, 978, 54, 21, DialogueSurface.QuietInk);
            FitStage();
        }

        void BuildPortrait(bool player, out RawImage image, out TextMeshProUGUI name)
        {
            string side = player ? "Player" : "NPC";
            float x = player ? 140 : 1280;
            var box = kit.Rect(stage, "Dialogue " + side + " portrait", x, 50, 500, 540);
            var picture = kit.Rect(box, "Artwork", 0, 0, 500, 540);
            picture.anchorMin = picture.anchorMax = picture.pivot = new Vector2(.5f, 0);
            picture.anchoredPosition = Vector2.zero;
            image = picture.gameObject.AddComponent<RawImage>(); image.raycastTarget = false;
            name = Text(stage, "Dialogue " + side + " name", "", x, 584, 500, 58, 28, UiKit.Paper);
            name.alignment = TextAlignmentOptions.Center;
        }

        void BuildOptions(RectTransform paper)
        {
            var holder = kit.Rect(paper, "Dialogue options", 1140, 62, 580, 208);
            optionGroup = holder.gameObject.AddComponent<CanvasGroup>();
            optionScroll = holder.gameObject.AddComponent<ScrollRect>();
            var viewport = kit.Rect(holder, "Viewport", 0, 0, 560, 208);
            var hit = viewport.gameObject.AddComponent<Image>(); hit.color = Color.clear; hit.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();
            optionContent = kit.Rect(viewport, "Choices", 0, 0, 560, 208);
            optionScroll.viewport = viewport; optionScroll.content = optionContent;
            optionScroll.horizontal = false; optionScroll.vertical = true; optionScroll.inertia = false;
            optionScroll.movementType = ScrollRect.MovementType.Clamped; optionScroll.scrollSensitivity = 32;
            var track = kit.Panel(holder, "Scrollbar", 570, 0, 10, 208, new Color(.36f, .30f, .19f, .12f), true);
            var scrollbar = track.gameObject.AddComponent<Scrollbar>();
            var sliding = kit.Stretch(track.transform, "Sliding area");
            var handle = kit.Stretch(sliding, "Handle").gameObject.AddComponent<Image>(); handle.color = DialogueSurface.AgedGold;
            scrollbar.handleRect = handle.rectTransform; scrollbar.targetGraphic = handle;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            optionScroll.verticalScrollbar = scrollbar;
            optionScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }

        TextMeshProUGUI Text(Transform parent, string name, string value, float x, float y, float width,
            float height, float size, Color color)
        {
            var label = kit.Label(parent, value, x, y, width, height, size, color,
                TextAlignmentOptions.TopLeft, false);
            label.name = name; label.richText = false;
            TextWrapCompat.SetWrapping(label, true);
            label.overflowMode = TextOverflowModes.Overflow;
            return label;
        }

        Button Button(Transform parent, string name, string text, float x, float y, float width, float height, Action click)
        {
            // Borrow the ordinary Button factory to preserve its sound/action listeners and navigation.
            // Its label-shift EventTrigger is local to this new object; the dialogue replaces only that feedback.
            var button = kit.Button(parent, text, x, y, width, height, () => click());
            button.name = name;
            var inheritedHover = button.GetComponent<EventTrigger>();
            if (inheritedHover != null)
            {
                inheritedHover.enabled = false;
                inheritedHover.triggers.Clear();
                Destroy(inheritedHover);
            }
            var hit = button.GetComponent<Image>();
            hit.color = Color.clear; hit.raycastTarget = true;
            button.transition = Selectable.Transition.None;
            var surface = kit.Stretch(button.transform, name + " bamboo slip").gameObject.AddComponent<DialogueSurface>();
            surface.transform.SetAsFirstSibling(); surface.Kind = DialogueSurface.Style.BambooSlip;
            surface.raycastTarget = false;
            bool choice = name.StartsWith("Dialogue choice ", StringComparison.Ordinal);
            surface.ConfigurePainted(kit, choice);
            var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            float textInset = choice ? 46 : 22;
            label.rectTransform.anchoredPosition = new Vector2(textInset, 0);
            label.rectTransform.sizeDelta = new Vector2(width - textInset * 2, height);
            label.fontSize = 25; label.enableAutoSizing = false; label.characterSpacing = .6f;
            label.richText = false; TextWrapCompat.SetWrapping(label, true);
            label.overflowMode = TextOverflowModes.Overflow;
            label.alignment = TextAlignmentOptions.Center;
            button.gameObject.AddComponent<DialogueSlipFeedback>().Configure(button, surface, label);
            return button;
        }

        void RebuildChoices()
        {
            var events = EventSystem.current;
            if (events != null && events.currentSelectedGameObject != null &&
                events.currentSelectedGameObject.transform.IsChildOf(optionContent))
                events.SetSelectedGameObject(null);
            lastFocusedChoice = null;
            foreach (var button in choiceButtons) button.onClick.RemoveAllListeners();
            for (int i = optionContent.childCount - 1; i >= 0; i--)
            {
                var child = optionContent.GetChild(i).gameObject;
                child.SetActive(false); Destroy(child);
            }
            choiceButtons.Clear(); choiceIndices.Clear();
            float y = 0;
            foreach (var choice in requests.Frame.Choices)
            {
                int sourceIndex = choice.SourceOptionIndex;
                var button = Button(optionContent, "Dialogue choice " + sourceIndex, choice.Text, 0, y, 556, 48,
                    () => SelectOption(sourceIndex));
                var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
                label.alignment = TextAlignmentOptions.MidlineLeft;
                float height = Mathf.Max(48, Mathf.Ceil(label.GetPreferredValues(choice.Text, 464, Mathf.Infinity).y) + 20);
                ((RectTransform)button.transform).sizeDelta = new Vector2(556, height);
                label.rectTransform.sizeDelta = new Vector2(464, height);
                choiceButtons.Add(button); choiceIndices.Add(sourceIndex); y += height + 8;
            }
            if (requests.Frame.HasSourceOptions && choiceButtons.Count == 0)
                Text(optionContent, "No available choices", "暂时无话可问。", 8, 14, 520, 92, 25, DialogueSurface.QuietInk);
            optionContent.sizeDelta = new Vector2(560, Mathf.Max(208, y > 0 ? y - 8 : 208));
            optionContent.anchoredPosition = Vector2.zero;
            optionScroll.StopMovement(); optionScroll.verticalNormalizedPosition = 1;
        }

        void ShowLine(string text, DialogueUISpeaker speaker)
        {
            displayedSpeaker = speaker;
            speakerName.text = (speaker == DialogueUISpeaker.Player ? playerPortrait : npcPortrait).Name;
            playerFrom = playerImage.color; npcFrom = npcImage.color;
            var dim = new Color(.48f, .53f, .5f, 1);
            playerTarget = speaker == DialogueUISpeaker.Player ? Color.white : dim;
            npcTarget = speaker == DialogueUISpeaker.Npc ? Color.white : dim;
            fadeStart = Time.unscaledTime;
            ApplySpeakerTint();
            body.text = text ?? "";
            body.maxVisibleCharacters = int.MaxValue; body.pageToDisplay = 1;
            body.ForceMeshUpdate();
            pages = Mathf.Max(1, body.textInfo.pageCount);
            SetPage(1, reduced);
            presentationFrame = Time.frameCount;
        }

        void SetPage(int number, bool reveal)
        {
            page = Mathf.Clamp(number, 1, pages);
            int count = body.textInfo.characterCount;
            if (count > 0)
            {
                var info = body.textInfo.pageInfo[page - 1];
                pageStart = Mathf.Clamp(info.firstCharacterIndex, 0, count);
                pageEnd = Mathf.Clamp(info.lastCharacterIndex + 1, pageStart, count);
            }
            else pageStart = pageEnd = 0;
            body.pageToDisplay = page;
            revealCount = reveal ? pageEnd : pageStart;
            body.maxVisibleCharacters = (int)revealCount;
            readyAt = Time.unscaledTime;
            RefreshControls();
        }

        void RevealPage()
        {
            if (body == null) return;
            revealCount = pageEnd; body.maxVisibleCharacters = pageEnd; readyAt = Time.unscaledTime;
        }

        void ContinueLine()
        {
            if (IsTyping) { RevealPage(); return; }
            if (page < pages) { SetPage(page + 1, reduced); return; }
            if (requests.Frame.CanAdvance) { SubmitAdvance(); return; }
            if (requests.Frame.IsTerminal) { Close(DialogueUICloseReason.Completed); return; }
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            for (int i = 0; i < choiceButtons.Count; i++)
                if (selected == choiceButtons[i].gameObject) { SubmitChoice(choiceIndices[i]); return; }
            if (choiceButtons.Count > 0 && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(choiceButtons[0].gameObject);
        }

        bool SubmitChoice(int index)
        {
            if (!requests.RequestChoice(index)) return false;
            RefreshControls();
            try { callbacks.Choose(requests.PendingToken, index); }
            catch (Exception error) { Fail(error); }
            return true;
        }

        void SubmitAdvance()
        {
            if (!requests.RequestAdvance()) return;
            RefreshControls();
            try { callbacks.Advance(requests.PendingToken); }
            catch (Exception error) { Fail(error); }
        }

        void Update()
        {
            if (!IsOpen) return;
            try
            {
                FitStage();
                bool nextReduced = reduceMotion();
                if (nextReduced && !reduced) { RevealPage(); RefreshControls(); }
                reduced = nextReduced; ApplySpeakerTint();
                FollowChoiceFocus();
                if (!inputArmed && Time.frameCount > bornFrame && Time.unscaledTime >= inputNotBefore && !ActivationHeld())
                    inputArmed = true;
                if (IsAwaitingResult) return;
                bool wasTyping = IsTyping;
                if (wasTyping)
                {
                    revealCount = reduced ? pageEnd : Mathf.Min(pageEnd, revealCount + Time.unscaledDeltaTime * CharactersPerSecond);
                    body.maxVisibleCharacters = Mathf.FloorToInt(revealCount);
                    if (!IsTyping) { readyAt = Time.unscaledTime; RefreshControls(); }
                }
                if (Time.frameCount <= presentationFrame || Time.frameCount == lastInputFrame) return;
                if (requests.Skip)
                {
                    RevealPage();
                    if (page < pages) SetPage(pages, true);
                    RevealPage();
                    if (requests.CanAutomate) SubmitAdvance(); else requests.StopAutomation();
                    RefreshControls();
                }
                else if (requests.Auto && !IsTyping && Time.unscaledTime - readyAt >= AutoPause)
                {
                    if (page < pages) SetPage(page + 1, reduced);
                    else if (requests.CanAutomate) SubmitAdvance();
                    else requests.StopAutomation();
                    RefreshControls();
                }
            }
            catch (Exception error) { Fail(error); }
        }

        void FollowChoiceFocus()
        {
            var events = EventSystem.current;
            var selected = events != null ? events.currentSelectedGameObject : null;
            if (selected == lastFocusedChoice) return;
            lastFocusedChoice = selected;
            if (selected == null || !optionGroup.gameObject.activeInHierarchy) return;
            foreach (var button in choiceButtons)
            {
                if (button.gameObject != selected) continue;
                var rect = (RectTransform)button.transform;
                float top = -rect.anchoredPosition.y, bottom = top + rect.rect.height;
                float viewHeight = optionScroll.viewport.rect.height;
                float offset = optionContent.anchoredPosition.y;
                if (top < offset) offset = top;
                else if (bottom > offset + viewHeight) offset = bottom - viewHeight;
                optionScroll.StopMovement();
                optionContent.anchoredPosition = new Vector2(0,
                    Mathf.Clamp(offset, 0, Mathf.Max(0, optionContent.rect.height - viewHeight)));
                break;
            }
        }

        void FitStage()
        {
            var size = root.rect.size;
            float scale = Mathf.Min(size.x / Width, size.y / Height);
            stage.localScale = Vector3.one * Mathf.Max(.001f, scale);
        }

        void ApplySpeakerTint()
        {
            float t = reduced ? 1 : Mathf.Clamp01((Time.unscaledTime - fadeStart) / FadeSeconds);
            playerImage.color = Color.Lerp(playerFrom, playerTarget, t);
            npcImage.color = Color.Lerp(npcFrom, npcTarget, t);
            playerName.color = Color.Lerp(UiKit.Muted, UiKit.Gold, displayedSpeaker == DialogueUISpeaker.Player ? 1 : 0);
            npcName.color = Color.Lerp(UiKit.Muted, UiKit.Gold, displayedSpeaker == DialogueUISpeaker.Npc ? 1 : 0);
        }

        void RefreshControls()
        {
            if (!IsOpen || requests.Frame == null || body == null) return;
            bool pending = IsAwaitingResult;
            bool choicesVisible = !IsTyping && page == pages && requests.Frame.HasSourceOptions;
            optionGroup.gameObject.SetActive(choicesVisible);
            optionGroup.interactable = !pending;
            optionGroup.blocksRaycasts = true;
            foreach (var button in choiceButtons) button.interactable = !pending;
            continueButton.interactable = !pending && (IsTyping || page < pages || requests.Frame.CanAdvance || requests.Frame.IsTerminal);
            continueLabel.text = IsTyping ? "全文" : page < pages ? "下一页" : requests.Frame.IsTerminal ? "作别" : "继续";
            autoButton.interactable = !pending && requests.CanAutomate;
            autoLabel.text = requests.Auto ? "自动 · 开" : "自动";
            skipButton.interactable = !pending && (IsTyping || page < pages || requests.CanAutomate);
            closeButton.interactable = true;
            if (!string.IsNullOrEmpty(notice)) status.text = notice;
            else if (pending) status.text = "稍候……";
            else if (choicesVisible) status.text = "请选择回应" + (optionContent.rect.height > 208 ? " · 可滚动" : "");
            else status.text = (pages > 1 ? $"第 {page} / {pages} 页　" : "") + (IsTyping ? "继续可显示全文" : "");
        }

        void ResetInput()
        {
            inputArmed = false; bornFrame = Time.frameCount;
            inputNotBefore = Time.unscaledTime + InputPause;
        }

        bool ConsumeInput()
        {
            if (!inputArmed || Time.frameCount == lastInputFrame) return false;
            lastInputFrame = Time.frameCount;
            return true;
        }

        static bool ActivationHeld()
        {
            var keys = Keyboard.current;
            return (Mouse.current != null && Mouse.current.leftButton.isPressed) ||
                (keys != null && (keys.eKey.isPressed || keys.spaceKey.isPressed || keys.enterKey.isPressed || keys.numpadEnterKey.isPressed));
        }

        void Fail(Exception error)
        {
            // A callback may have already applied effects. Never turn an exception into a retry.
            Cleanup(DialogueUICloseReason.Fault, true);
            Debug.LogException(error);
        }

        void OnDisable() { if (open && !cleaned) Cleanup(DialogueUICloseReason.Disabled, true); }
        void OnDestroy() => Cleanup(DialogueUICloseReason.Destroyed, false);

        void Cleanup(DialogueUICloseReason reason, bool destroy)
        {
            if (cleaned) return;
            cleaned = true; open = false;
            requests.Close(); inputArmed = false;
            var lease = inputLease; inputLease = null;
            var closed = callbacks?.Closed; callbacks = null; reduceMotion = null;
            var events = EventSystem.current;
            try
            {
                bool hadFocus = events != null && events.currentSelectedGameObject != null &&
                    events.currentSelectedGameObject.transform.IsChildOf(transform);
                if (hadFocus) events.SetSelectedGameObject(null);
                foreach (var button in choiceButtons) if (button != null) button.onClick.RemoveAllListeners();
                if (continueButton != null) continueButton.onClick.RemoveAllListeners();
                if (autoButton != null) autoButton.onClick.RemoveAllListeners();
                if (skipButton != null) skipButton.onClick.RemoveAllListeners();
                if (closeButton != null) closeButton.onClick.RemoveAllListeners();
                if (optionScroll != null) optionScroll.StopMovement();
                if (gameObject.activeSelf) gameObject.SetActive(false);
            }
            catch (Exception error) { Debug.LogException(error); }
            finally
            {
                // Even a failing deselection/disable listener must not strand the input lease.
                try { lease?.Dispose(); }
                catch (Exception error) { Debug.LogException(error); }
                finally
                {
                    try { closed?.Invoke(reason); }
                    catch (Exception error) { Debug.LogException(error); }
                    finally
                    {
                        try
                        {
                            if (events != null && events.currentSelectedGameObject == null && previousFocus != null && previousFocus.activeInHierarchy)
                            {
                                var selectable = previousFocus.GetComponent<Selectable>();
                                if (selectable == null || selectable.IsInteractable()) events.SetSelectedGameObject(previousFocus);
                            }
                        }
                        catch (Exception error) { Debug.LogException(error); }
                        finally
                        {
                            previousFocus = lastFocusedChoice = null;
                            playerPortrait = npcPortrait = null; choiceButtons.Clear(); choiceIndices.Clear();
                            if (destroy) Destroy(gameObject);
                        }
                    }
                }
            }
        }
    }
}
