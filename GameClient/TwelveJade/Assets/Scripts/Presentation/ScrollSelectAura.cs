using System;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // The winding silk light is extracted from the approved painting, including its soft brush edges.
    // Only opacity breathes; no wire ellipses, moving line meshes, per-frame textures or materials.
    public sealed class ScrollSelectAura : MonoBehaviour
    {
        public const float BreathSeconds = 2.8f;
        CanvasGroup group;
        RawImage painted;
        Func<bool> reduceMotion;
        bool selected, reduced;
        float phase;
        public bool Selected => selected;
        public bool IsAnimating => selected && !reduced && isActiveAndEnabled;
        public float Phase => phase;
        public float VisibleAlpha => group == null ? 0 : group.alpha;
        public int OrbitCountForShot => 0;
        public int StarCountForShot => 0;

        public void Configure(Func<bool> reducedMotion)
        {
            reduceMotion = reducedMotion;
            if (group != null) { Apply(); return; }
            group = gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false; group.interactable = false;
            painted = MemoVisualAssets.Rect(transform, "Painted gold silk selection")
                .gameObject.AddComponent<RawImage>();
            painted.texture = ApprovedUiArt.Load("nov10/memo-painted-selection");
            painted.raycastTarget = false;
            SetSelected(false);
        }
        public void SetSelected(bool value)
        {
            if (selected != value) phase = 0;
            selected = value; reduced = reduceMotion?.Invoke() ?? true;
            Apply();
        }
        void Update()
        {
            if (group == null || !selected) return;
            bool next = reduceMotion?.Invoke() ?? true;
            if (next != reduced) { reduced = next; if (reduced) phase = 0; Apply(); }
            if (reduced) return;
            phase = MemoScrollGeometry.Step(phase, Time.unscaledDeltaTime, BreathSeconds, false);
            Apply();
        }
        void Apply()
        {
            if (group != null)
                group.alpha = !selected ? 0 : reduced ? .94f : .89f + .08f * Mathf.Sin(phase * Mathf.PI * 2);
        }
        void OnEnable() { reduced = reduceMotion?.Invoke() ?? true; Apply(); }
        void OnDisable() { if (group != null) group.alpha = 0; }
        void OnDestroy() { reduceMotion = null; if (painted != null) painted.texture = null; }
    }
}
