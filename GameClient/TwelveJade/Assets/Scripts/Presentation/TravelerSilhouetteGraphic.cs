using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    /// <summary>One masked quad/material for the union of the live current/fading Frames alpha.</summary>
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class TravelerSilhouetteGraphic : MaskableGraphic
    {
        static Shader silhouetteShader;
        static readonly int FrameUvId = Shader.PropertyToID("_FrameUv");
        static readonly int PreviousUvId = Shader.PropertyToID("_PreviousUv");
        static readonly int PreviousTextureId = Shader.PropertyToID("_PreviousTex");
        static readonly int FrameAlphaId = Shader.PropertyToID("_FrameAlpha");
        static readonly int StrengthId = Shader.PropertyToID("_Strength");
        static readonly int StepId = Shader.PropertyToID("_OutlineStep");
        Texture currentTexture, previousTexture;
        Material ownedMaterial, renderMaterial;
        Vector4 frameUv, previousUv, outlineStep;
        Vector2 frameAlpha;
        float strength;
        bool configured, resourcesReleased;
        CanvasRenderer cachedRenderer;

        public bool Ready => ownedMaterial != null && currentTexture != null && isActiveAndEnabled;
        public float Strength => strength;
        public Material OwnedMaterialForCheck => ownedMaterial;
        public override Texture mainTexture => currentTexture != null ? currentTexture : s_WhiteTexture;
        public override bool Raycast(Vector2 screenPoint, Camera eventCamera) => false;

        protected override void Awake()
        {
            base.Awake();
            cachedRenderer = GetComponent<CanvasRenderer>();
            raycastTarget = false;
            maskable = true;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (configured) Initialize();
        }

        internal void Initialize()
        {
            configured = true;
            if (ownedMaterial != null) return;
            resourcesReleased = false;
            if (silhouetteShader == null) silhouetteShader = Resources.Load<Shader>("Shaders/TravelerSilhouette");
            if (silhouetteShader == null || !silhouetteShader.isSupported)
            {
                Debug.LogError("[Occlusion] Live Frames silhouette shader missing/unsupported; no rectangle or static pose fallback.");
                return;
            }
            ownedMaterial = new Material(silhouetteShader)
            {
                name = "Traveler live Frames silhouette (owned)",
                hideFlags = HideFlags.HideAndDontSave
            };
            material = ownedMaterial;
            color = new Color(250f / 255f, 208f / 255f, 44f / 255f, 1f);
            SetStrength(0f);
        }

        internal void SyncFrames(RawImage current, RawImage previous, float alpha)
        {
            bool changed = currentTexture != current.texture;
            currentTexture = current.texture;
            previousTexture = previous.texture;
            var a = current.uvRect; var b = previous.uvRect;
            frameUv = new Vector4(a.x, a.y, a.width, a.height);
            previousUv = new Vector4(b.x, b.y, b.width, b.height);
            frameAlpha = new Vector2(current.color.a * current.canvasRenderer.GetAlpha(),
                previous.color.a * previous.canvasRenderer.GetAlpha());
            var size = rectTransform.rect.size;
            outlineStep = new Vector4(2f / Mathf.Max(1f, size.x), 2f / Mathf.Max(1f, size.y), 0f, 0f);
            strength = Mathf.Clamp(alpha, 0f, .65f);
            if (changed) SetMaterialDirty();
            ApplyParameters(ownedMaterial);
            if (renderMaterial != null && renderMaterial != ownedMaterial) ApplyParameters(renderMaterial);
        }

        internal void SetStrength(float alpha)
        {
            strength = Mathf.Clamp(alpha, 0f, .65f);
            if (ownedMaterial != null) ownedMaterial.SetFloat(StrengthId, strength);
            if (renderMaterial != null && renderMaterial != ownedMaterial) renderMaterial.SetFloat(StrengthId, strength);
        }

        void ApplyParameters(Material target)
        {
            if (target == null) return;
            target.SetVector(FrameUvId, frameUv);
            target.SetVector(PreviousUvId, previousUv);
            target.SetTexture(PreviousTextureId, previousTexture);
            target.SetVector(FrameAlphaId, new Vector4(frameAlpha.x, frameAlpha.y, 0f, 0f));
            target.SetVector(StepId, outlineStep);
            target.SetFloat(StrengthId, strength);
        }

        protected override void UpdateMaterial()
        {
            base.UpdateMaterial();
            renderMaterial = IsActive() && canvasRenderer.materialCount > 0 ? canvasRenderer.GetMaterial(0) : null;
            // MaskableGraphic owns its stencil variant; update that cached variant as well.
            ApplyParameters(renderMaterial);
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (ownedMaterial == null) return;
            var r = rectTransform.rect;
            var tint = (Color32)color;
            vh.AddVert(new Vector3(r.xMin, r.yMin, 0f), tint, new Vector2(0f, 0f));
            vh.AddVert(new Vector3(r.xMin, r.yMax, 0f), tint, new Vector2(0f, 1f));
            vh.AddVert(new Vector3(r.xMax, r.yMax, 0f), tint, new Vector2(1f, 1f));
            vh.AddVert(new Vector3(r.xMax, r.yMin, 0f), tint, new Vector2(1f, 0f));
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);
        }

        protected override void OnDisable()
        {
            renderMaterial = null;
            base.OnDisable(); // Includes the derived stencil material's release.
            ReleaseOwnedResources();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            ReleaseOwnedResources();
        }

        internal void ReleaseOwnedResources()
        {
            // Parent disable, Graphic disable and destroy may all occur in one page teardown.
            if (resourcesReleased) return;
            resourcesReleased = true;
            strength = 0f;
            renderMaterial = null;
            currentTexture = previousTexture = null;
            material = null;
            if (ownedMaterial != null)
            {
                if (Application.isPlaying) Destroy(ownedMaterial); else DestroyImmediate(ownedMaterial);
                ownedMaterial = null;
            }
            if (cachedRenderer != null) cachedRenderer.Clear();
        }
    }
}
