using System;
using TMPro;
using TwelveJade.Core;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // Sibling of the task marker, owned by the real live NPC rect. It never moves the NPC or queries quests.
    public sealed class NpcIdentityBadge : MonoBehaviour
    {
        FrontEndController owner;
        MapNpc npc;
        RectTransform content;
        TMP_Text nameLabel, titleLabel;
        bool visible;
        public bool VisibleForShot => visible && content != null && content.gameObject.activeInHierarchy;
        public TMP_Text NameForShot => nameLabel;
        public TMP_Text TitleForShot => titleLabel;
        public Vector2 LiveFootForShot { get; private set; }
        public float DistanceForShot { get; private set; } = float.PositiveInfinity;
        public static NpcIdentityBadge Create(RectTransform npcRect, UiKit ui, FrontEndController owner, MapNpc npc, string name, string title)
        {
            if (npcRect == null || ui == null || owner == null) throw new ArgumentNullException("NPC identity requires a live host");
            var root = ui.Rect(npcRect, "Npc identity proximity", 0, 0, 1, 1);
            root.anchorMin = root.anchorMax = new Vector2(.5f, 1); root.pivot = new Vector2(.5f, 0); root.anchoredPosition = new Vector2(0, 72);
            var badge = root.gameObject.AddComponent<NpcIdentityBadge>(); badge.owner = owner; badge.npc = npc;
            badge.content = ui.Rect(root, "Identity plate", 0, 0, 280, string.IsNullOrEmpty(title) ? 40 : 68);
            badge.content.anchorMin = badge.content.anchorMax = new Vector2(.5f, 0); badge.content.pivot = new Vector2(.5f, 0); badge.content.anchoredPosition = Vector2.zero;
            var background = badge.content.gameObject.AddComponent<Image>(); background.sprite = UiKit.RoundedSprite(); background.type = Image.Type.Sliced;
            background.color = new Color(.08f, .15f, .13f, .88f); background.raycastTarget = false;
            var edge = ui.Panel(badge.content, "Identity fine gold rule", 12, 3, 256, 1, new Color(.69f, .58f, .35f, .52f)); edge.raycastTarget = false;
            badge.nameLabel = ui.Label(badge.content, name ?? "", 14, string.IsNullOrEmpty(title) ? 3 : 29, 252, 36, 23, UiKit.Paper, TextAlignmentOptions.Center, true);
            badge.nameLabel.name = "NPC real name"; badge.nameLabel.richText = false; badge.nameLabel.enableAutoSizing = false;
            badge.nameLabel.textWrappingMode = TextWrappingModes.NoWrap; badge.nameLabel.raycastTarget = false;
            badge.titleLabel = ui.Label(badge.content, title ?? "", 14, 4, 252, 28, 18, new Color(.79f, .73f, .57f, 1), TextAlignmentOptions.Center);
            badge.titleLabel.name = "NPC configured title"; badge.titleLabel.richText = false; badge.titleLabel.enableAutoSizing = false;
            badge.titleLabel.textWrappingMode = TextWrappingModes.NoWrap; badge.titleLabel.raycastTarget = false; badge.titleLabel.gameObject.SetActive(!string.IsNullOrEmpty(title));
            float needed = Mathf.Max(badge.nameLabel.GetPreferredValues(name ?? "").x, badge.titleLabel.GetPreferredValues(title ?? "").x) + 28;
            float width = Mathf.Max(180, needed); badge.content.sizeDelta = new Vector2(width, badge.content.sizeDelta.y);
            badge.nameLabel.rectTransform.sizeDelta = new Vector2(width - 28, 36); badge.titleLabel.rectTransform.sizeDelta = new Vector2(width - 28, 28);
            edge.rectTransform.sizeDelta = new Vector2(width - 24, 1);
            badge.content.gameObject.SetActive(false); badge.RefreshLive(); return badge;
        }
        public void RefreshLive()
        {
            bool town = owner != null && owner.CurrentPage == "town";
            Vector2 liveFoot = Vector2.zero;
            bool hasFoot = town && owner.TryNpcIdentityFoot(npc, out liveFoot);
            Vector2 player = town ? owner.PlayerWorldPosition : Vector2.zero;
            if (hasFoot) { LiveFootForShot = liveFoot; DistanceForShot = Vector2.Distance(player, liveFoot); }
            else DistanceForShot = float.PositiveInfinity;
            bool next = hasFoot && NpcIdentityRules.Visible(town, player.x, player.y, liveFoot.x, liveFoot.y);
            if (next == visible) return;
            visible = next; if (content != null) content.gameObject.SetActive(visible);
        }
        void LateUpdate() { RefreshLive(); } // Dynamic foot after NPC patrol Update; no closures, resources or text writes.
        void OnDisable() { visible = false; if (content != null) content.gameObject.SetActive(false); }
        void OnDestroy() { owner = null; npc = null; }
    }
}
