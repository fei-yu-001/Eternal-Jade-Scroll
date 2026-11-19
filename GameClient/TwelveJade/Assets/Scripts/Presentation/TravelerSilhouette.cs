using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    /// <summary>One world overlay, below page HUD. Frames only; no replacement pose for other modes.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(10000)]
    public sealed class TravelerSilhouette : MonoBehaviour
    {
        FrontEndController owner;
        PuppetActor actor;
        RectTransform layer;
        TravelerSilhouetteGraphic graphic;
        TravelerSilhouetteBindingWatcher watcher;
        RawImage current, previous;
        bool bindingsDirty = true, unsupportedReported;
        float phase;

        public bool FramesSupported { get; private set; }
        public bool Occluded { get; private set; }
        public bool ReduceMotion { get; private set; }
        public float VisibleAlpha { get; private set; }
        public float PulsePhase => phase;
        public int BindingRefreshCount { get; private set; }
        public TravelerSilhouetteGraphic Graphic => graphic;
        public RawImage CurrentFrame => current;
        public RawImage PreviousFrame => previous;

        internal static TravelerSilhouette Create(FrontEndController owner, RectTransform worldRoot, PuppetActor actor)
        {
            var go = new GameObject("Traveler spiritual silhouette", typeof(RectTransform));
            go.SetActive(false);
            go.transform.SetParent(worldRoot, false);
            var silhouette = go.AddComponent<TravelerSilhouette>();
            silhouette.owner = owner;
            silhouette.actor = actor;
            silhouette.layer = (RectTransform)go.transform;
            go.SetActive(true);
            return silhouette;
        }

        void OnEnable()
        {
            if (actor == null) return;
            watcher = actor.gameObject.AddComponent<TravelerSilhouetteBindingWatcher>();
            watcher.Owner = this;
            var go = new GameObject("Live Frames outline", typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(transform, false);
            graphic = go.AddComponent<TravelerSilhouetteGraphic>();
            graphic.Initialize();
            bindingsDirty = true;
        }

        internal void InvalidateBindings() { bindingsDirty = true; }

        void RefreshBindings()
        {
            current = previous = null;
            if (actor != null && actor.GaitModeForCheck == PuppetActor.GaitMode.Frames)
            {
                // Only on renderer rebuild; inspect direct actor children, never a whole-tree scan.
                var root = actor.Root;
                for (int i = 0; i < root.childCount; i++)
                {
                    var child = root.GetChild(i);
                    if (!child.gameObject.activeSelf || !child.TryGetComponent<RawImage>(out var image)) continue;
                    if (child.name == "Puppet frame") current = image;
                    else if (child.name == "Puppet frame fade") previous = image;
                }
            }
            FramesSupported = current != null && previous != null;
            BindingRefreshCount++;
            bindingsDirty = false;
            if (!FramesSupported && actor != null && actor.GaitModeForCheck != PuppetActor.GaitMode.Frames && !unsupportedReported)
            {
                unsupportedReported = true;
                Debug.LogWarning("[Occlusion] Silhouette supports the default live Frames pair only; MeshDeform/Slices have building fade but no silhouette. No static pose substituted.");
            }
        }

        void LateUpdate()
        {
            Profiler.BeginSample("TwelveJade.OcclusionExperience");
            try
            {
                if (owner == null || actor == null || graphic == null) { HideImmediately(); return; }
                // All PuppetActor.Update animation/UV/mirror writes have completed for this frame.
                Occluded = owner.ObserveBuildingOcclusion(this, Time.unscaledDeltaTime, out var reduced);
                ReduceMotion = reduced;
                if (bindingsDirty) RefreshBindings();
                KeepAboveWorld();
                if (!Occluded || !FramesSupported || !current.isActiveAndEnabled || !previous.isActiveAndEnabled)
                { HideImmediately(); return; }
                CopyRect(actor.Root, layer);
                CopyRect(current.rectTransform, graphic.rectTransform);
                if (!reduced) phase = (phase + Time.unscaledDeltaTime * .7f) % (Mathf.PI * 2f);
                float strength = reduced ? .65f : .59f + .06f * (.5f + .5f * Mathf.Sin(phase));
                graphic.SyncFrames(current, previous, strength);
                VisibleAlpha = graphic.Ready ? strength : 0f;
            }
            finally { Profiler.EndSample(); }
        }

        internal static void CopyRect(RectTransform source, RectTransform destination)
        {
            destination.anchorMin = source.anchorMin;
            destination.anchorMax = source.anchorMax;
            destination.pivot = source.pivot;
            destination.sizeDelta = source.sizeDelta;
            destination.localPosition = source.localPosition;
            destination.localRotation = source.localRotation;
            destination.localScale = source.localScale;
        }

        internal void KeepAboveWorld()
        {
            if (layer != null && layer.parent != null && layer.GetSiblingIndex() != layer.parent.childCount - 1)
                layer.SetAsLastSibling();
        }

        public void HideImmediately()
        {
            VisibleAlpha = 0f;
            if (graphic != null) graphic.SetStrength(0f);
        }

        void OnDisable()
        {
            HideImmediately();
            Occluded = false;
            if (owner != null) owner.ReleaseBuildingOcclusion(this);
            if (watcher != null) { watcher.Owner = null; Destroy(watcher); watcher = null; }
            if (graphic != null)
            {
                var oldGraphic = graphic;
                graphic = null;
                // Graphic releases owned resources after UGUI releases its stencil state.
                oldGraphic.gameObject.SetActive(false);
                Destroy(oldGraphic.gameObject);
            }
            current = previous = null;
            bindingsDirty = true;
        }
    }

    // Actor renderer rebuilds change direct children, including same-count front/side sheet swaps.
    public sealed class TravelerSilhouetteBindingWatcher : MonoBehaviour
    {
        internal TravelerSilhouette Owner;
        void OnTransformChildrenChanged() { if (Owner != null) Owner.InvalidateBindings(); }
    }
}
