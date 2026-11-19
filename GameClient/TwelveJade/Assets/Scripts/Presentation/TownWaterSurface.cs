using System;
using TwelveJade.Core;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    /// <summary>
    /// Overlay-canvas river graphic. Attach after all terrain chunks, before sorted entities,
    /// and count rectTransform in the controller's static background sibling offset.
    /// The component owns one mesh and one material; neither the source config nor terrain is changed.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class TownWaterSurface : MaskableGraphic
    {
        public const string ShaderResourcePath = "Shaders/QingshiWater";
        static readonly int PhaseId = Shader.PropertyToID("_FlowPhase");
        static readonly int HalfWidthId = Shader.PropertyToID("_WaterHalfWidth");
        static readonly int ShoreFeatherId = Shader.PropertyToID("_ShoreFeather");
        static readonly int WavelengthId = Shader.PropertyToID("_Wavelength");

        [SerializeField, Min(1f)] float wavelength = 112f;
        [SerializeField, Min(0f)] float flowUnitsPerSecond = 3f;
        [SerializeField, Min(1f)] float shoreFeather = 24f;
        const float DeckFeather = 10f;
        TownWaterGeometry.Ribbon ribbon;
        TownWaterLeaves leafLayer;
        Vector3[] positions;
        Mesh ownedMesh;
        Material ownedMaterial, renderMaterial;
        Func<bool> reduceMotionProvider;
        bool reduceMotion = true;
        float phase, direction = 1;

        public bool IsConfigured => ribbon != null && ownedMaterial != null;
        public int WaterVertexCount => ribbon == null ? 0 : ribbon.Vertices.Count;
        public int WaterTriangleCount => ribbon == null ? 0 : ribbon.Indices.Count / 3;
        public float FlowPhase => phase;
        public bool ReduceMotion => reduceMotion;
        /// <summary>落叶层（子节点）；配置失败时为 null，不影响水层本身。</summary>
        public TownWaterLeaves Leaves => leafLayer;
        public int WaterLeafCount => leafLayer == null ? 0 : leafLayer.LeafCount;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        public override bool Raycast(Vector2 screenPoint, Camera eventCamera) => false;

        /// <summary>
        /// Reads the existing terrainShape at the call site. No geometry fallback on missing data.
        /// Prefer the json override when the controller retains the exact JSON used to build town.
        /// Return false and surface=null on failure; report error once at the caller.
        /// </summary>
        public static bool TryCreateFromTownMap(RectTransform worldRoot, TownMap town, float worldScale,
            Func<bool> reducedMotion, out TownWaterSurface surface, out string error, string json = null)
        {
            surface = null; error = null;
            if (worldRoot == null || town == null)
            { error = "Water requires an existing worldRoot and TownMap."; return false; }
            GameObject instance = null;
            try
            {
                if (json == null)
                {
                    var source = Resources.Load<TextAsset>("Config/town-map");
                    if (source == null) throw new FormatException("water: Config/town-map is missing");
                    json = source.text;
                }
                var definition = TownWaterGeometry.FromTownMapJson(json);
                if (Math.Abs(definition.WorldWidth - town.WorldWidth) > .01f || Math.Abs(definition.WorldHeight - town.WorldHeight) > .01f)
                    throw new FormatException("water: loaded TownMap and water dimensions differ; rebuild the town first");
                if (!town.Elevation.IsFlat)
                    for (float x = 0; x <= definition.WorldWidth; x += 32)
                        if (Math.Abs(definition.CenterY(x) - town.Elevation.RiverY(x)) > .05f)
                            throw new FormatException("water: loaded TownMap and river shape differ; rebuild the town first");
                // Keep Awake/OnEnable from drawing anything until configuration succeeds.
                instance = new GameObject("Qingshi water", typeof(RectTransform));
                instance.SetActive(false);
                instance.transform.SetParent(worldRoot, false);
                var candidate = instance.AddComponent<TownWaterSurface>();
                candidate.Configure(definition, town.Elevation, worldScale, reducedMotion);
                instance.SetActive(true);
                surface = candidate;
                return true;
            }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is InvalidOperationException)
            {
                if (instance != null) Release(instance);
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Parameterized entry point for future explicit river definitions. All dimensions and deck
        /// exclusions are world coordinates; projection uses the same TownElevation as the chunks.
        /// A null motion provider defaults to a static surface until SetReduceMotion(false).
        /// Configure only on creation/map changes, never in Update.
        /// </summary>
        public void Configure(TownWaterGeometry.Definition definition, TownElevation elevation, float worldScale,
            Func<bool> reducedMotion = null)
        {
            if (elevation == null) throw new ArgumentNullException(nameof(elevation));
            if (float.IsNaN(worldScale) || float.IsInfinity(worldScale) || worldScale <= 0)
                throw new ArgumentOutOfRangeException(nameof(worldScale));
            var shader = Resources.Load<Shader>(ShaderResourcePath);
            if (shader == null || !shader.isSupported)
                throw new InvalidOperationException("water: Resources/Shaders/QingshiWater is missing or unsupported");
            var next = TownWaterGeometry.Build(definition);
            var nextPositions = new Vector3[next.Vertices.Count];
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < next.Vertices.Count; i++)
            {
                var v = next.Vertices[i];
                float x = v.X * worldScale, y = -elevation.ProjectY(v.X, v.Y) * worldScale;
                if (float.IsNaN(y) || float.IsInfinity(y)) throw new FormatException("water: invalid height projection");
                nextPositions[i] = new Vector3(x, y, 0);
                minX = Mathf.Min(minX, x); minY = Mathf.Min(minY, y);
                maxX = Mathf.Max(maxX, x); maxY = Mathf.Max(maxY, y);
            }
            // Tight rect for UI clipping; worldRoot and this object both use a top-left origin.
            var rect = rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(minX, maxY);
            rect.sizeDelta = new Vector2(maxX - minX, maxY - minY);
            for (int i = 0; i < nextPositions.Length; i++) nextPositions[i] -= new Vector3(minX, maxY, 0);
            ribbon = next; positions = nextPositions; direction = definition.FlowDirection;
            reduceMotionProvider = reducedMotion;
            reduceMotion = reducedMotion == null || reducedMotion();
            raycastTarget = false;
            if (ownedMaterial == null)
                ownedMaterial = new Material(shader) { name = "Qingshi water (owned)", hideFlags = HideFlags.HideAndDontSave };
            ownedMaterial.SetFloat(HalfWidthId, definition.HalfWidth - definition.BankInset);
            ownedMaterial.SetFloat(ShoreFeatherId, Mathf.Max(1, shoreFeather));
            ownedMaterial.SetFloat(WavelengthId, Mathf.Max(1, wavelength));
            ownedMaterial.SetFloat(PhaseId, phase);
            material = ownedMaterial;
            // Recreate a stencil variant after reconfiguration; its properties must match the source.
            RecalculateMasking();
            SetAllDirty();
            ConfigureLeaves(definition, elevation, worldScale, reducedMotion);
        }

        /// <summary>叶层是独立子节点：它失败只报自己的错，绝不把水层判成失败，也不动水波规则。</summary>
        void ConfigureLeaves(TownWaterGeometry.Definition definition, TownElevation elevation, float worldScale,
            Func<bool> reducedMotion)
        {
            try
            {
                if (leafLayer == null)
                {
                    var go = new GameObject("Qingshi water leaves", typeof(RectTransform));
                    go.SetActive(false);
                    go.transform.SetParent(rectTransform, false);
                    leafLayer = go.AddComponent<TownWaterLeaves>();
                }
                leafLayer.Configure(definition, elevation, worldScale, rectTransform.anchoredPosition, reducedMotion);
                leafLayer.gameObject.SetActive(isActiveAndEnabled);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is FormatException)
            {
                if (leafLayer != null) { Release(leafLayer.gameObject); leafLayer = null; }
                Debug.LogError("[Water] 落叶层配置失败（水层保留）：" + ex.Message);
            }
        }

        /// <summary>Switch to direct motion control, replacing the optional settings callback.</summary>
        public void SetReduceMotion(bool value)
        {
            reduceMotionProvider = null;
            reduceMotion = value;
            if (leafLayer != null) leafLayer.SetReduceMotion(value);
        }

        void Update()
        {
            if (ownedMaterial == null || ribbon == null) return;
            if (reduceMotionProvider != null) reduceMotion = reduceMotionProvider();
            if (reduceMotion) return; // Freeze the current phase, including specular perturbation.
            float nextPhase = Mathf.Repeat(phase + Mathf.Clamp(Time.unscaledDeltaTime, 0, .1f) *
                Mathf.Max(0, flowUnitsPerSecond) / Mathf.Max(1, wavelength) * direction, 1f);
            if (nextPhase == phase) return;
            phase = nextPhase;
            ownedMaterial.SetFloat(PhaseId, phase);
            // Mask uses a derived stencil material. Update that instance too, without resolving
            // materialForRendering or calling SetMaterialDirty every frame.
            if (renderMaterial != null && renderMaterial != ownedMaterial) renderMaterial.SetFloat(PhaseId, phase);
        }

        protected override void UpdateMaterial()
        {
            base.UpdateMaterial();
            renderMaterial = IsActive() && canvasRenderer.materialCount > 0 ? canvasRenderer.GetMaterial(0) : null;
            if (renderMaterial != null) renderMaterial.SetFloat(PhaseId, phase);
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (ribbon == null || positions == null || ownedMaterial == null) return;
            for (int i = 0; i < ribbon.Vertices.Count; i++)
            {
                var v = ribbon.Vertices[i];
                var tint = color;
                tint.a *= Mathf.SmoothStep(0, 1, Mathf.Clamp01(v.DeckDistance / DeckFeather));
                vh.AddVert(positions[i], tint, new Vector2(v.Along, v.Across));
            }
            for (int i = 0; i < ribbon.Indices.Count; i += 3)
                vh.AddTriangle(ribbon.Indices[i], ribbon.Indices[i + 1], ribbon.Indices[i + 2]);
        }

        protected override void UpdateGeometry()
        {
            if (ownedMesh == null)
                ownedMesh = new Mesh { name = "Qingshi water ribbon (owned)", hideFlags = HideFlags.HideAndDontSave };
            // Only on UI rebuild/configuration, never during phase animation. Ownership is explicit;
            // do not destroy Graphic.workerMesh, which is shared by all UI graphics.
            using (var helper = new VertexHelper())
            {
                OnPopulateMesh(helper);
                helper.FillMesh(ownedMesh);
            }
            canvasRenderer.SetMesh(ownedMesh);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (leafLayer != null) leafLayer.gameObject.SetActive(true);
        }

        protected override void OnDisable()
        {
            if (leafLayer != null) leafLayer.gameObject.SetActive(false);
            renderMaterial = null;
            base.OnDisable(); // Releases the UI stencil material; keep the owned mesh for re-enable.
        }

        protected override void OnDestroy()
        {
            if (leafLayer != null) { leafLayer.gameObject.SetActive(false); Release(leafLayer.gameObject); leafLayer = null; }
            base.OnDestroy();
            if (ownedMesh != null) Release(ownedMesh);
            if (ownedMaterial != null) Release(ownedMaterial);
            ownedMesh = null; ownedMaterial = null; renderMaterial = null;
            ribbon = null; positions = null; reduceMotionProvider = null; leafLayer = null;
        }

        static void Release(UnityEngine.Object value)
        {
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
    }
}
