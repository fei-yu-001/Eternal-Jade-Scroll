using System;
using TwelveJade.Core;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    /// <summary>
    /// 河面落叶层：UGUI 里的一层自有 mesh，挂在 <see cref="TownWaterSurface"/> 子节点。
    /// 位置/合法性全部来自 <see cref="TownWaterLeafField"/>（与水面同一份 Definition/Exclusions）；
    /// 这里只做投影、上色和生命周期。不建材质、不加载资源、每帧只用预分配数组。
    /// 纯程序化叶形（7 顶点 + 中脉），不代表已审美术。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class TownWaterLeaves : MaskableGraphic
    {
        // 低饱和枫褐 / 橄榄 / 黄绿；程序化几何，不是精修贴图。
        static readonly Color32[] Palette =
        {
            new Color32(122, 74, 52, 255),
            new Color32(126, 122, 74, 255),
            new Color32(140, 132, 84, 255)
        };

        const int VertsPerLeaf = 7;
        const int TrisPerLeaf = 18;

        TownWaterLeafField leafField;
        TownElevation elevation;
        Func<bool> reduceMotionProvider;
        bool reduceMotion = true;
        float worldScale = 1f;
        Vector2 meshOrigin;
        Mesh ownedMesh;
        Vector3[] vertices;
        Color32[] colors;
        int[] triangles;
        int usedVerts;
        int usedTris;

        public bool IsConfigured => leafField != null;
        public int LeafCount => leafField == null ? 0 : leafField.Count;
        public bool ReduceMotion => reduceMotion;
        /// <summary>离线/实机同源判据（运行时也读同一份，不另算）。</summary>
        public TownWaterLeafField Field => leafField;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        public override bool Raycast(Vector2 screenPoint, Camera eventCamera) => false;

        /// <summary>由 <see cref="TownWaterSurface.Configure"/> 在每次重建水面时调用。
        /// meshOrigin 是水面 rect 的 anchoredPosition（minX, maxY）：叶层与水面共用同一局部原点。</summary>
        public void Configure(TownWaterGeometry.Definition definition, TownElevation elevation, float worldScale,
            Vector2 meshOrigin, Func<bool> reducedMotion)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (elevation == null) throw new ArgumentNullException(nameof(elevation));
            if (float.IsNaN(worldScale) || float.IsInfinity(worldScale) || worldScale <= 0f)
                throw new ArgumentOutOfRangeException(nameof(worldScale));
            this.elevation = elevation;
            this.worldScale = worldScale;
            this.meshOrigin = meshOrigin;
            reduceMotionProvider = reducedMotion;
            reduceMotion = reducedMotion == null || reducedMotion();
            leafField = new TownWaterLeafField(definition, LeafSeed(definition));
            EnsureBuffers();
            AlignToParent();
            raycastTarget = false;
            if (isActiveAndEnabled) UpdateGeometry();
        }

        /// <summary>与水面同一套：直接给定状态，不再跟外部回调。</summary>
        public void SetReduceMotion(bool value)
        {
            reduceMotionProvider = null;
            reduceMotion = value;
        }

        void AlignToParent()
        {
            var rect = rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = Vector2.zero;
            if (transform.parent is RectTransform parent) rect.sizeDelta = parent.rect.size;
        }

        void EnsureBuffers()
        {
            int maxVerts = TownWaterLeafField.MaxLeaves * VertsPerLeaf;
            int maxTris = TownWaterLeafField.MaxLeaves * TrisPerLeaf;
            if (vertices == null || vertices.Length < maxVerts) vertices = new Vector3[maxVerts];
            if (colors == null || colors.Length < maxVerts) colors = new Color32[maxVerts];
            if (triangles == null || triangles.Length < maxTris) triangles = new int[maxTris];
        }

        void Update()
        {
            if (leafField == null || elevation == null) return;
            if (reduceMotionProvider != null) reduceMotion = reduceMotionProvider();
            if (reduceMotion) return;   // 冻结：位置、摆动、相位、透明度全部停住
            leafField.Advance(Time.unscaledDeltaTime, false);
            WriteMesh();
            if (ownedMesh != null) canvasRenderer.SetMesh(ownedMesh);
        }

        protected override void UpdateGeometry()
        {
            if (leafField == null || elevation == null) return;
            WriteMesh();
            canvasRenderer.SetMesh(ownedMesh);
        }

        void WriteMesh()
        {
            EnsureBuffers();
            if (ownedMesh == null)
                ownedMesh = new Mesh { name = "Qingshi water leaves (owned)", hideFlags = HideFlags.HideAndDontSave };
            usedVerts = 0;
            usedTris = 0;
            for (int i = 0; i < leafField.Count; i++)
            {
                if (!leafField.TryGetLeaf(i, out var leaf) || leaf.Visibility <= .01f) continue;
                Emit(leaf, i);
            }
            ownedMesh.Clear(false);
            ownedMesh.SetVertices(vertices, 0, usedVerts);
            ownedMesh.SetColors(colors, 0, usedVerts);
            ownedMesh.SetTriangles(triangles, 0, usedTris, 0, false);
        }

        void Emit(TownWaterLeafField.LeafState leaf, int index)
        {
            float y = RiverCenterY(leaf) + leaf.Across;
            float centerX = leaf.X * worldScale - meshOrigin.x;
            float centerY = -elevation.ProjectY(leaf.X, y) * worldScale - meshOrigin.y;
            float bob = (float)Math.Sin((index * 1.7f) + leafField.Elapsed * 1.9f) * TownWaterLeafField.BobAmplitude * worldScale;
            float length = leaf.Size * worldScale;
            float width = length * .62f;
            float cos = (float)Math.Cos(leaf.Angle), sin = (float)Math.Sin(leaf.Angle);
            var tint = Palette[index % Palette.Length];
            var alpha = (byte)Mathf.Clamp(Mathf.RoundToInt(leaf.Visibility * 255f), 0, 255);
            var baseColor = new Color32(tint.r, tint.g, tint.b, alpha);
            var veinColor = new Color32((byte)(tint.r * .74f), (byte)(tint.g * .74f), (byte)(tint.b * .74f), alpha);
            int v = usedVerts;
            AddPoint(v + 0, .50f * length, 0f, baseColor, centerX, centerY + bob, cos, sin);
            AddPoint(v + 1, .16f * length, .24f * width, baseColor, centerX, centerY + bob, cos, sin);
            AddPoint(v + 2, -.30f * length, .20f * width, baseColor, centerX, centerY + bob, cos, sin);
            AddPoint(v + 3, -.50f * length, 0f, baseColor, centerX, centerY + bob, cos, sin);
            AddPoint(v + 4, -.30f * length, -.20f * width, baseColor, centerX, centerY + bob, cos, sin);
            AddPoint(v + 5, .16f * length, -.24f * width, baseColor, centerX, centerY + bob, cos, sin);
            AddPoint(v + 6, 0f, 0f, veinColor, centerX, centerY + bob, cos, sin);
            usedVerts += VertsPerLeaf;
            int t = usedTris;
            for (int i = 0; i < 6; i++)
            {
                triangles[t++] = v + 6;
                triangles[t++] = v + i;
                triangles[t++] = v + (i + 1) % 6;
            }
            usedTris = t;
        }

        float RiverCenterY(TownWaterLeafField.LeafState leaf) => leafField.CenterYAt(leaf.X);

        void AddPoint(int index, float localX, float localY, Color32 color,
            float originX, float originY, float cos, float sin)
        {
            vertices[index] = new Vector3(originX + localX * cos - localY * sin,
                originY + localX * sin + localY * cos, 0f);
            colors[index] = color;
        }

        static int LeafSeed(TownWaterGeometry.Definition definition) =>
            20261106 ^ Mathf.RoundToInt(definition.BaseY) ^ Mathf.RoundToInt(definition.WorldWidth);

        protected override void OnDisable()
        {
            base.OnDisable();
            ReleaseMesh(); // 留住固定大小 field/arrays，重新启用续走；禁用时不保留 GPU mesh。
        }

        void ReleaseMesh()
        {
            canvasRenderer.Clear();
            if (ownedMesh != null)
            {
                if (Application.isPlaying) Destroy(ownedMesh); else DestroyImmediate(ownedMesh);
            }
            ownedMesh = null;
        }

        protected override void OnDestroy()
        {
            ReleaseMesh();
            base.OnDestroy();
            vertices = null; colors = null; triangles = null;
            leafField = null; elevation = null; reduceMotionProvider = null;
        }
    }
}