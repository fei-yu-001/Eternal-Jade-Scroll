using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TwelveJade.Core;

namespace TwelveJade.Presentation
{
    // Visual-only task layer. Identity is a separate sibling, so None does not disable proximity updates.
    public sealed class NpcQuestOverhead : MonoBehaviour
    {
        public const float FloatPixels = 4f;
        public const float FloatSeconds = 1.2f;
        TextMeshProUGUI label;
        RectTransform rect;
        QuestMarkerGraphic graphic;
        CanvasGroup group;
        Func<bool> reduceMotion;
        Vector2 resting;
        float phase;
        QuestOverheadState state = QuestOverheadState.None;
        public TMP_Text Label => label; // Compatibility: semantic text only, deliberately not rendered as an icon.
        public RectTransform Rect => rect;
        public QuestOverheadState State => state;
        public QuestMarkerGraphic GraphicForShot => graphic;
        public float BreathAlphaForShot => group == null ? 0 : group.alpha;
        public bool FloatingForShot => rect != null && Mathf.Abs(rect.anchoredPosition.y - resting.y) > .01f;
        public static NpcQuestOverhead Create(RectTransform npcRect, UiKit ui, Func<bool> reduceMotion)
        {
            if (npcRect == null) throw new ArgumentNullException(nameof(npcRect));
            if (ui == null) throw new ArgumentNullException(nameof(ui));
            var root = ui.Rect(npcRect, "Npc quest overhead", 0, 0, 56, 62);
            root.anchorMin = root.anchorMax = new Vector2(.5f, 1f); root.pivot = new Vector2(.5f, 0f);
            var marker = root.gameObject.AddComponent<NpcQuestOverhead>();
            marker.rect = root; marker.reduceMotion = reduceMotion; marker.resting = new Vector2(0, 6);
            root.anchoredPosition = marker.resting;
            marker.group = root.gameObject.AddComponent<CanvasGroup>(); marker.group.blocksRaycasts = false; marker.group.interactable = false;
            marker.graphic = root.gameObject.AddComponent<QuestMarkerGraphic>(); marker.graphic.raycastTarget = false;
            marker.label = ui.Label(root, "", 0, 0, 1, 1, 1, Color.clear);
            marker.label.name = "Task state compatibility metadata"; marker.label.richText = false; marker.label.raycastTarget = false;
            marker.label.enabled = false; // Old !/? screenshots need a new visual assertion; no hidden fake PASS glyph.
            root.gameObject.SetActive(false); return marker;
        }
        public void Refresh(QuestOverheadState next)
        {
            bool visible = NpcIdentityRules.TaskVisible(next);
            if (state == next && gameObject.activeSelf == visible) return;
            state = next;
            if (label != null) label.text = NpcIdentityRules.TaskDescription(next);
            if (graphic != null) graphic.SetState(next);
            gameObject.SetActive(visible);
            if (visible) ApplyMotion(); else ResetMotion();
        }
        void LateUpdate()
        {
            bool reduced = reduceMotion != null && reduceMotion();
            if (!reduced) phase = (phase + Mathf.Max(0, Time.unscaledDeltaTime) * (Mathf.PI * 2 / FloatSeconds)) % (Mathf.PI * 2);
            ApplyMotion();
        }
        void ApplyMotion()
        {
            bool reduced = reduceMotion != null && reduceMotion();
            if (rect != null) rect.anchoredPosition = resting + new Vector2(0, NpcIdentityRules.MotionOffset(phase, reduced));
            if (group != null) group.alpha = NpcIdentityRules.Breath(phase, reduced);
        }
        void ResetMotion() { if (rect != null) rect.anchoredPosition = resting; if (group != null) group.alpha = 1; }
        void OnDisable() { ResetMotion(); }
    }
}
