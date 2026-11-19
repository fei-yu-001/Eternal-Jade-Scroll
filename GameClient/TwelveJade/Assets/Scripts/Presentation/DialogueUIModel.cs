using System;
using System.Collections.Generic;
using TwelveJade.Core;
using UnityEngine;

namespace TwelveJade.Presentation
{
    public enum DialogueUISpeaker { Player, Npc }
    public enum DialogueUICommand { Continue, ToggleAuto, Skip, Close }
    public enum DialogueUICloseReason { User, Completed, Host, Disabled, Destroyed, Fault }

    /// <summary>Borrowed texture; UVs use Unity's bottom-left origin. The panel never destroys it.</summary>
    public sealed class DialogueUIPortrait
    {
        public string Name { get; }
        public Texture2D Texture { get; }
        public Rect Uv { get; }

        public DialogueUIPortrait(string name, Texture2D texture = null, Rect? uv = null)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "未署名" : name;
            Texture = texture;
            Uv = uv ?? new Rect(0, 0, 1, 1);
            if (float.IsNaN(Uv.x) || float.IsNaN(Uv.y) || float.IsNaN(Uv.width) || float.IsNaN(Uv.height) ||
                Uv.x < 0 || Uv.y < 0 || Uv.width <= 0 || Uv.height <= 0 || Uv.xMax > 1 || Uv.yMax > 1)
                throw new ArgumentOutOfRangeException(nameof(uv), "Portrait UV must be a nonempty normalized rectangle.");
        }
    }

    public sealed class DialogueUIChoice
    {
        public int SourceOptionIndex { get; }
        public string Text { get; }
        internal DialogueUIChoice(int index, string text) { SourceOptionIndex = index; Text = text ?? ""; }
    }

    /// <summary>
    /// A display snapshot of the existing Core view, not a dialogue/save model.
    /// Pass the indices left after DialogueTree.BranchOptions, in display order.
    /// A host must inspect DialogueNode.Next: DialogueView itself has no Next.
    /// </summary>
    public sealed class DialogueUIFrame
    {
        public string Token { get; }
        public string NodeId { get; }
        public string NpcId { get; }
        public string Text { get; }
        public DialogueUIPortrait Player { get; }
        public DialogueUIPortrait Npc { get; }
        public DialogueUISpeaker Speaker { get; }
        public IReadOnlyList<DialogueUIChoice> Choices { get; }
        public bool HasSourceOptions { get; }
        public bool CanAdvance { get; }
        public bool AllowAutomaticAdvance { get; }
        public bool IsTerminal => !HasSourceOptions && !CanAdvance;

        public DialogueUIFrame(string token, DialogueView view, IReadOnlyList<int> visibleOptionIndices,
            DialogueUIPortrait player, DialogueUIPortrait npc, bool canAdvance = false,
            bool allowAutomaticAdvance = false, DialogueUISpeaker speaker = DialogueUISpeaker.Npc)
        {
            if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("Use a unique token for every presentation.", nameof(token));
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (string.IsNullOrWhiteSpace(view.NodeId)) throw new ArgumentException("A real Core node is required.", nameof(view));
            if (visibleOptionIndices == null) throw new ArgumentNullException(nameof(visibleOptionIndices));
            Player = player ?? throw new ArgumentNullException(nameof(player));
            Npc = npc ?? throw new ArgumentNullException(nameof(npc));
            if (speaker != DialogueUISpeaker.Player && speaker != DialogueUISpeaker.Npc)
                throw new ArgumentOutOfRangeException(nameof(speaker));
            var source = view.Options ?? Array.Empty<DialogueOption>();
            HasSourceOptions = source.Count > 0;
            if (canAdvance && HasSourceOptions)
                throw new ArgumentException("Options, including hidden or single options, require an explicit choice.", nameof(canAdvance));
            if (allowAutomaticAdvance && !canAdvance)
                throw new ArgumentException("Auto/Skip require a host-confirmed linear successor.", nameof(allowAutomaticAdvance));
            var choices = new List<DialogueUIChoice>();
            var used = new HashSet<int>();
            foreach (var index in visibleOptionIndices)
            {
                if (index < 0 || index >= source.Count || source[index] == null || !used.Add(index))
                    throw new ArgumentException("Visible option indices must be unique indices of the original view.", nameof(visibleOptionIndices));
                choices.Add(new DialogueUIChoice(index, source[index].Text));
            }
            Token = token; NodeId = view.NodeId; NpcId = view.NpcId ?? ""; Text = view.Text ?? "";
            Choices = choices.AsReadOnly(); CanAdvance = canAdvance;
            AllowAutomaticAdvance = allowAutomaticAdvance; Speaker = speaker;
        }
    }

    /// <summary>
    /// All effects/persistence belong to the host. AcquireInputLock must cancel travel/inertia,
    /// gate world keyboard/click/hotkey input, and consume the closing input before unlocking.
    /// Choose/Advance receive a unique attempt token, distinct from the frame token. Complete with
    /// panel.Present(next, requestToken), Reject (no effects), or Close; echo that exact request token.
    /// </summary>
    public sealed class DialogueUICallbacks
    {
        public Func<IDisposable> AcquireInputLock { get; }
        public Action<string, int> Choose { get; }
        public Action<string> Advance { get; }
        public Action<DialogueUICloseReason> Closed { get; }

        public DialogueUICallbacks(Func<IDisposable> acquireInputLock, Action<string, int> choose,
            Action<string> advance, Action<DialogueUICloseReason> closed)
        {
            AcquireInputLock = acquireInputLock ?? throw new ArgumentNullException(nameof(acquireInputLock));
            Choose = choose ?? throw new ArgumentNullException(nameof(choose));
            Advance = advance ?? throw new ArgumentNullException(nameof(advance));
            Closed = closed ?? throw new ArgumentNullException(nameof(closed));
        }
    }

    // Ephemeral request gate only. No Core mutation, timers, Unity input or persistence here.
    internal sealed class DialogueUIRequestState
    {
        readonly HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        public DialogueUIFrame Frame { get; private set; }
        long requestSequence;
        public string PendingToken { get; private set; }
        public bool Closed { get; private set; }
        public bool Auto { get; private set; }
        public bool Skip { get; private set; }
        public bool CanAutomate => Frame != null && Frame.CanAdvance && Frame.AllowAutomaticAdvance && !Frame.HasSourceOptions;

        public bool Present(DialogueUIFrame next, string responseTo)
        {
            if (Closed || next == null || seen.Contains(next.Token)) return false;
            if (!string.Equals(PendingToken, responseTo, StringComparison.Ordinal)) return false;
            seen.Add(next.Token); Frame = next; PendingToken = null;
            if (!CanAutomate) { Auto = false; Skip = false; }
            return true;
        }

        public bool RequestChoice(int sourceIndex)
        {
            if (Closed || Frame == null || PendingToken != null) return false;
            foreach (var choice in Frame.Choices)
                if (choice.SourceOptionIndex == sourceIndex)
                { Auto = false; Skip = false; PendingToken = NextRequestToken(); return true; }
            return false;
        }

        public bool RequestAdvance()
        {
            if (Closed || Frame == null || PendingToken != null || !Frame.CanAdvance) return false;
            PendingToken = NextRequestToken(); return true;
        }

        string NextRequestToken() => Frame.Token + ":request:" + (++requestSequence);

        public bool Reject(string token)
        {
            if (Closed || PendingToken == null || !string.Equals(PendingToken, token, StringComparison.Ordinal)) return false;
            PendingToken = null; Auto = false; Skip = false; return true;
        }

        public void ToggleAuto() { if (Closed) return; Auto = !Auto && CanAutomate; Skip = false; }
        public void StartSkip() { if (Closed) return; Skip = CanAutomate; Auto = false; }
        public void StopAutomation() { Auto = false; Skip = false; }
        public void Close() { Closed = true; PendingToken = null; Frame = null; seen.Clear(); StopAutomation(); }
    }
}
