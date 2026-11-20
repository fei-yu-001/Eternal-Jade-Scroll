using TwelveJade.Core;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // The authored three-cell sheet follows Core phases, never an independent animation clock.
    // Its pivot stays on the actor foot; opaque body/sole bounds match the standing art, also when mirrored.
    [DefaultExecutionOrder(180)]
    public sealed class ActionCombatPose : MonoBehaviour
    {
        public const int StrikeCellPixels = ActionPoseGeometry.StrikeCellPixels, StrikeFrameCount = ActionPoseGeometry.StrikeFrameCount;
        public const float StrikeBodyPixels = ActionPoseGeometry.StrikeBodyPixels, StrikeFootFromBottom = ActionPoseGeometry.StrikeFootFromBottom;
        RectTransform root;
        PuppetActor actor;
        RawImage strike;
        Texture2D strikeSheet;
        float travelRectHeight;
        ActionDodgeMesh dodgeMesh;
        bool dodging;
        float dodgeProgress, dodgeWeight;
        Vector2 foot;
        float tilt;
        int phaseFrame = -1, facing = 1;
        bool travelHidden, lowMotion;

        public bool StrikeVisible => strike != null && strike.enabled && strike.gameObject.activeInHierarchy;
        public int StrikeFrame => StrikeVisible
            ? Mathf.RoundToInt(Mathf.Min(strike.uvRect.x, strike.uvRect.x + strike.uvRect.width) * StrikeFrameCount) : -1;
        public bool StrikeMirrored => StrikeVisible && strike.uvRect.width < 0;
        public Rect StrikeUv => strike != null ? strike.uvRect : Rect.zero;
        public bool TravelRenderersHidden => StrikeVisible && actor != null && actor.RenderSuppressed;
        public bool DodgeVisible => dodging && StrikeVisible;
        public bool StrikeWhiteFlashActive
        {
            get
            {
                if (!StrikeVisible) return false;
                foreach (var image in strike.GetComponentsInChildren<RawImage>())
                    if (image != strike && image.enabled && image.gameObject.activeInHierarchy && image.texture != null && image.color.a > 0)
                        return true;
                return false;
            }
        }
        public Vector2 StrikeFootInParent()
        {
            if (strike == null || root == null || root.parent == null) return new Vector2(float.NaN, float.NaN);
            // The image pivot itself is the supplied foot, not the transparent lower cell edge.
            var point = root.parent.InverseTransformPoint(strike.rectTransform.TransformPoint(Vector3.zero));
            return new Vector2(point.x, point.y);
        }

        // Must run BEFORE ActionCombatHitFlash.Configure, so it discovers this image and caches its sheet.
        public void Configure(PuppetActor owner, Texture2D sheet, float travelRectHeight)
        {
            actor = owner; root = (RectTransform)transform;
            strikeSheet = sheet; this.travelRectHeight = travelRectHeight;
            if (sheet == null || sheet.width != StrikeCellPixels * StrikeFrameCount || sheet.height != StrikeCellPixels)
            {
                Debug.LogError("[ActionCombat] Missing or invalid registered three-phase hero strike sheet (1728x576).");
                return;
            }
            if (strike == null)
            {
                var go = new GameObject("Action combat authored strike frame", typeof(RectTransform), typeof(CanvasRenderer));
                var rect = (RectTransform)go.transform; rect.SetParent(root, false);
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0);
                rect.pivot = new Vector2(.5f, StrikeFootFromBottom / StrikeCellPixels);
                rect.anchoredPosition = Vector2.zero;
                strike = go.AddComponent<RawImage>(); strike.raycastTarget = false;
                dodgeMesh = go.AddComponent<ActionDodgeMesh>();
            }
            ApplyStrikeLayout(0);
            strike.texture = sheet; strike.enabled = false; strike.gameObject.SetActive(false); phaseFrame = -1;
        }

        public static int FrameFor(CombatPhase phase, CombatActionType action)
        {
            if (action != CombatActionType.Light && action != CombatActionType.Heavy) return -1;
            switch (phase)
            {
                case CombatPhase.Startup: return 0;
                case CombatPhase.Active: return 1;
                case CombatPhase.Recovery: return 2;
                default: return -1;
            }
        }
        public static Rect UvFor(int frame, int direction)
        {
            float width = 1f / StrikeFrameCount;
            return direction < 0 ? new Rect((frame + 1) * width, 0, -width, 1) : new Rect(frame * width, 0, width, 1);
        }
        public void Sample(Vector2 at, int direction, CombatPhase phase, CombatActionType action, float progress, bool low, float elapsed = -1f, float dodgeActiveSeconds = .22f)
        {
            root = (RectTransform)transform; foot = at; facing = direction < 0 ? -1 : 1; tilt = 0; lowMotion = low;
            phaseFrame = strike != null ? FrameFor(phase, action) : -1;
            dodging = action == CombatActionType.Dodge && actor != null && actor.WalkingSheet != null &&
                (phase == CombatPhase.Startup || phase == CombatPhase.Active || phase == CombatPhase.Recovery);
            var timing = CombatRules.Timing(CombatActionType.Dodge);
            float clock = elapsed >= 0 ? elapsed : phase == CombatPhase.Startup ? timing.startup * .5f :
                phase == CombatPhase.Active ? timing.startup + progress * dodgeActiveSeconds : timing.startup + dodgeActiveSeconds;
            dodgeProgress = Mathf.Clamp01((clock - timing.startup) / Mathf.Max(.01f, dodgeActiveSeconds));
            dodgeWeight = phase == CombatPhase.Startup ? Mathf.SmoothStep(0, .65f, clock / timing.startup) :
                phase == CombatPhase.Active ? .65f + .35f * Mathf.Sin(dodgeProgress * Mathf.PI) :
                phase == CombatPhase.Recovery ? .65f * (1 - Mathf.SmoothStep(0, 1, (clock - timing.startup - dodgeActiveSeconds) / timing.recovery)) : 0;
            if (low) dodgeWeight *= .35f;
            // Phase changes communicate gameplay even with reduced motion. No extra tilt is added
            // to authored attacks; dodge/flinch/death keep their existing non-attack restoration semantics.
            if (phaseFrame < 0 && !dodging && !low)
            {
                if (phase == CombatPhase.Startup) tilt = facing * (action == CombatActionType.Heavy ? 9 : action == CombatActionType.Dodge ? -12 : 5);
                else if (phase == CombatPhase.Active) tilt = -facing * (action == CombatActionType.Dodge ? 18 : 10) * Mathf.Sin(progress * Mathf.PI);
                else if (phase == CombatPhase.Hitstun) tilt = facing * 11;
                else if (phase == CombatPhase.Dead) tilt = facing * 65;
            }
            Apply();
        }
        void LateUpdate() { Apply(); }
        void Apply()
        {
            if (root == null) return;
            if (!isActiveAndEnabled) { RestoreTravel(); return; }
            root.anchoredPosition = foot; root.localRotation = Quaternion.Euler(0, 0, tilt);
            bool attacking = strike != null && (phaseFrame >= 0 || dodging);
            if (attacking)
            {
                actor.SetRenderSuppressed(true);
                travelHidden = true; root.localScale = actor.BaseScale;
                if (dodging)
                {
                    int count = Mathf.Max(1, actor.FrameCountForCheck);
                    int frame = Mathf.Min(count - 1, Mathf.FloorToInt(dodgeProgress * (count - 1)));
                    float width = 1f / count;
                    strike.texture = actor.WalkingSheet;
                    strike.uvRect = actor.MirrorForCheck ? new Rect((frame + 1) * width, 0, -width, 1) : new Rect(frame * width, 0, width, 1);
                    strike.rectTransform.pivot = new Vector2(.5f, 0);
                    strike.rectTransform.sizeDelta = new Vector2(travelRectHeight * strike.texture.width / (count * (float)strike.texture.height), travelRectHeight);
                    dodgeMesh.SetPose(dodgeWeight, facing);
                }
                else
                {
                    strike.texture = strikeSheet;
                    ApplyStrikeLayout(phaseFrame);
                    strike.uvRect = UvFor(phaseFrame, facing);
                    dodgeMesh.SetPose(0, facing);
                }
                strike.enabled = true; strike.gameObject.SetActive(true);
            }
            else RestoreTravel();
            if (lowMotion && actor != null) root.localScale = actor.BaseScale;
        }
        void ApplyStrikeLayout(int frame)
        {
            var layout = ActionPoseGeometry.Strike(travelRectHeight,
                actor != null && actor.View != null ? actor.View.name : null,
                strikeSheet != null ? strikeSheet.name : null, frame);
            strike.rectTransform.sizeDelta = Vector2.one * layout.Height;
            strike.rectTransform.pivot = new Vector2(.5f, layout.PivotY);
        }
        void RestoreTravel()
        {
            if (strike != null) { strike.enabled = false; strike.gameObject.SetActive(false); }
            if (!travelHidden) return;
            actor?.SetRenderSuppressed(false);
            travelHidden = false;
        }

        void OnDisable() { phaseFrame = -1; dodging = false; RestoreTravel(); if (root != null) root.localRotation = Quaternion.identity; }
        void OnDestroy() { actor?.SetRenderSuppressed(false); actor = null; strike = null; strikeSheet = null; root = null; }
    }
    // Only the action image bends: feet stay registered while the upper body settles into the step.
    public sealed class ActionDodgeMesh : BaseMeshEffect
    {
        float weight; int direction = 1;
        public void SetPose(float amount, int facing) { weight = amount; direction = facing; graphic.SetVerticesDirty(); }
        public override void ModifyMesh(VertexHelper mesh)
        {
            if (!IsActive() || weight <= 0) return;
            var image = (RawImage)graphic; var box = image.rectTransform.rect; var uv = image.uvRect;
            const int columns = 6, rows = 12;
            mesh.Clear();
            for (int y = 0; y <= rows; y++) for (int x = 0; x <= columns; x++)
            {
                float u = x / (float)columns, v = y / (float)rows;
                float upper = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.24f, .85f, v));
                float bend = Mathf.Sin(v * Mathf.PI) * .035f;
                mesh.AddVert(new Vector3(box.xMin + u * box.width + direction * (upper * .045f + bend) * box.height * weight,
                    box.yMin + v * box.height * (1 - .055f * weight)), image.color, new Vector2(uv.x + u * uv.width, uv.y + v * uv.height));
            }
            for (int y = 0; y < rows; y++) for (int x = 0; x < columns; x++)
            { int i = y * (columns + 1) + x; mesh.AddTriangle(i, i + columns + 1, i + 1); mesh.AddTriangle(i + 1, i + columns + 1, i + columns + 2); }
        }
    }

}
