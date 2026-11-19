using UnityEngine;

namespace TwelveJade.Presentation
{
    /// <summary>Opt-in, world-foot occlusion. Owns only this building's CanvasGroup alpha.</summary>
    [DisallowMultipleComponent]
    public sealed class BuildingOcclusionFader : MonoBehaviour
    {
        public const float OccludedAlpha = .425f;
        public const float TransitionSeconds = .25f;
        CanvasGroup group;
        FrontEndController owner;
        RectTransform building;
        Rect footBounds;
        float sortBaseY, elapsed, outsideSeconds, fromAlpha = 1f, targetAlpha = 1f;
        bool configured;

        public bool IsOccluding { get; private set; }
        public float CurrentAlpha => group == null ? 1f : group.alpha;
        public Rect FootBounds => footBounds;
        public float SortBaseY => sortBaseY;
        public RectTransform Building => building;
        public CanvasGroup Group => group;

        public void Configure(FrontEndController controller, float footX, float groundFootY,
            float width, float height, float projectedGroundY)
        {
            owner = controller;
            building = (RectTransform)transform;
            // footY in building-plots is the art TOP. The caller supplies footY + groundFootOffset.
            footBounds = Rect.MinMaxRect(footX - width * .5f, groundFootY - height,
                footX + width * .5f, groundFootY);
            sortBaseY = projectedGroundY;
            group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();
            configured = true;
            RestoreImmediately();
            owner.RegisterBuildingOcclusion(this);
        }

        public bool ContainsFoot(Vector2 foot) => configured &&
            foot.x >= footBounds.xMin && foot.x <= footBounds.xMax &&
            foot.y >= footBounds.yMin && foot.y <= footBounds.yMax;

        internal void Observe(Vector2 foot, float projectedFootY, RectTransform traveler, float deltaTime)
        {
            if (!configured || !isActiveAndEnabled || group == null) return;
            bool obscured = ContainsFoot(foot) && projectedFootY < sortBaseY &&
                traveler != null && traveler.parent == building.parent &&
                traveler.GetSiblingIndex() < building.GetSiblingIndex();
            IsOccluding = obscured;
            // Enter immediately; hold only the fade target for 40ms on exit. This suppresses
            // numerical boundary chatter without claiming the foot is still inside the region.
            // The silhouette follows the strict obscured flag and disappears at the real exit.
            outsideSeconds = obscured ? 0f : outsideSeconds + Mathf.Max(0f, deltaTime);
            float next = obscured || (targetAlpha == OccludedAlpha && outsideSeconds < .04f)
                ? OccludedAlpha : 1f;
            if (next != targetAlpha)
            {
                // Reverse from the actual alpha, without snapping or relying on camera bounds.
                targetAlpha = next;
                fromAlpha = group.alpha;
                elapsed = 0f;
            }
            elapsed = Mathf.Min(TransitionSeconds, elapsed + Mathf.Max(0f, deltaTime));
            float t = Mathf.Clamp01(elapsed / TransitionSeconds);
            group.alpha = Mathf.Lerp(fromAlpha, targetAlpha, t * t * (3f - 2f * t));
        }

        public void RestoreImmediately()
        {
            IsOccluding = false;
            elapsed = TransitionSeconds;
            outsideSeconds = 0f;
            fromAlpha = targetAlpha = 1f;
            if (group != null) group.alpha = 1f;
        }

        void OnEnable()
        {
            if (configured && owner != null) owner.RegisterBuildingOcclusion(this);
        }

        void OnDisable()
        {
            RestoreImmediately();
            if (owner != null) owner.UnregisterBuildingOcclusion(this);
        }

        void OnDestroy()
        {
            RestoreImmediately();
            if (owner != null) owner.UnregisterBuildingOcclusion(this);
            owner = null;
        }
    }
}
