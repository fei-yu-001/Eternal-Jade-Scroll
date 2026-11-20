using System;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // Only accepted world movement advances the gait. Camera/elevation changes never move its phase.
    public sealed class NpcFrameAnimator : MonoBehaviour
    {
        RawImage image;
        RectTransform body;
        Texture2D stand, sheet;
        Func<bool> reduce;
        float distance, cycleDistance, idleClock;
        int count, last = -1;
        bool initialized, mirrored, nativeLeft, movedThisFrame;

        public int FrameForCheck => last;
        public int FrameCountForCheck => count;
        public bool HasSheetForCheck => sheet != null;
        public float DistanceForCheck => distance;

        public void Configure(TownCharacterArt.Entry art, Func<bool> reduced)
        {
            var root = (RectTransform)transform;
            reduce = reduced;
            stand = TownCharacterArt.Texture(art.stand);
            sheet = TownCharacterArt.Texture(art.walk);
            count = Mathf.Max(1, art.frames);
            cycleDistance = art.cycleDistance > 0 ? art.cycleDistance : root.rect.height * .16f;
            nativeLeft = art.nativeFacing == "left";
            var old = GetComponent<RawImage>();
            if (old != null) old.color = Color.clear;
            body = new GameObject("Independent NPC frame", typeof(RectTransform)).GetComponent<RectTransform>();
            body.SetParent(transform, false);
            body.anchorMin = Vector2.zero; body.anchorMax = Vector2.one;
            body.pivot = new Vector2(.5f, 0); body.offsetMin = body.offsetMax = Vector2.zero;
            image = body.gameObject.AddComponent<RawImage>(); image.raycastTarget = false;
            initialized = true;
            Apply(false);
        }

        // Called after collision and world-position writeback succeed, including zero while waiting/paused.
        public void SetMovement(Vector2 worldDelta)
        {
            float moved = worldDelta.magnitude;
            movedThisFrame = initialized && moved > .00001f && moved < 40f;
            if (!movedThisFrame) return;
            distance = (distance + moved) % cycleDistance;
            idleClock = 0;
            // Ignore tiny horizontal navigation noise while walking mostly north/south.
            if (Mathf.Abs(worldDelta.x) > Mathf.Max(.00001f, Mathf.Abs(worldDelta.y) * .2f))
            {
                bool next = (worldDelta.x < 0) != nativeLeft;
                if (next != mirrored) { mirrored = next; last = -2; }
            }
        }

        void LateUpdate()
        {
            if (!initialized || image == null) return;
            bool walking = movedThisFrame;
            movedThisFrame = false;
            if (!walking) idleClock += Time.unscaledDeltaTime;
            Apply(walking);
            float breath = (reduce?.Invoke() ?? true) || walking ? 0 : Mathf.Sin(idleClock * 1.8f) * .004f;
            body.localScale = new Vector3(1, 1 + breath, 1);
        }

        void OnDisable()
        {
            movedThisFrame = false;
            if (initialized) Apply(false);
        }

        void Apply(bool moving)
        {
            int frame = moving && sheet != null ? PuppetGait.FrameIndexAt(distance, cycleDistance, count) : -1;
            if (frame == last && image.texture != null) return;
            last = frame;
            image.texture = frame < 0 ? stand : sheet;
            float width = frame < 0 ? 1 : 1f / count;
            float x = frame < 0 ? 0 : frame / (float)count;
            image.uvRect = mirrored ? new Rect(x + width, 0, -width, 1) : new Rect(x, 0, width, 1);
        }
    }
}
