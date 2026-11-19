// V 线 W-A：单图网格形变的 **uGUI 侧**。
//
// 它做的事只有一件：把 `PuppetRig` 算出来的 697 个**形变后**顶点画成一个 UI 网格，
// 而每个顶点的 **UV 取自形变前**的位置 —— 于是"像素仍然是原图的像素"，
// 只是被搬了位置。这就是 W-A 与"再生成一套美术"的根本区别：
// 身份一致性闸门恒等通过（`farmer-male-0-front.png` 一个像素都没被重新画过）。
//
// 继承 `MaskableGraphic` 而不是自己写 shader：本仓世界全部画在 uGUI Canvas 里
// （`RawImage` 45 处 / `SpriteRenderer` 0 处），沿用它就能继续吃 Canvas 的合批、
// 遮罩、排序与 `CanvasScaler`，**不动渲染管线**（U 线还没裁决，不许先斩后奏）。
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // WALK-FIX：除了 RectTransform **必须**把 CanvasRenderer 也写在这个特性里。
    // 只写 RectTransform 时，从 Graphic 继承下来的 `[RequireComponent(typeof(CanvasRenderer))]`
    // 会被这条顶掉：AddComponent 之后对象上只有 RectTransform + 本组件，没有 CanvasRenderer，
    // 于是 `Graphic.Rebuild` 第一行的 `canvasRenderer == null` 直接 return —— OnPopulateMesh
    // 终生不被调用、不报错、不抛异常，画面就是**什么都没有**。10-06 记的"主角整块消失"即此。
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class PuppetMeshGraphic : MaskableGraphic
    {
        Texture2D source;
        PuppetRigDefinition rig = PuppetRigDefinition.Default;
        PuppetRigPose pose = PuppetRigPose.Rest;
        bool mirrored;
        bool legsSplit = true;

        readonly PuppetVec2[] rest = new PuppetVec2[PuppetRig.Count];
        readonly PuppetVec2[] deformed = new PuppetVec2[PuppetRig.Count];
        bool gridBuilt;

        public int VertexCountForCheck => PuppetRig.Count;
        public int TriangleCountForCheck => PuppetRig.TriangleCount;

        // WALK-FIX："网格模式整块消失"的取证口。三个数就够分清三种原因：
        //   PopulateCount = 0        → OnPopulateMesh 根本没被调（层级 / 启用 / 脏标记问题）
        //   VertexCount = 0          → 调了但一个顶点没进 VertexHelper
        //   Rect w/h = 0             → rect 尺寸为 0（GetPixelAdjustedRect 拿到空矩形）
        // 探针读不到就说明原因在渲染管线之外（材质 / 纹理），不必再猜。
        int populateCount;
        int lastVertexCount;
        float lastRectX, lastRectY, lastRectW, lastRectH;
        float lastFirstX, lastFirstY;
        public int PopulateCountForCheck => populateCount;
        public int LastPopulatedVertexCountForCheck => lastVertexCount;
        public Vector4 LastPopulatedRectForCheck => new Vector4(lastRectX, lastRectY, lastRectW, lastRectH);
        public Vector2 LastFirstVertexForCheck => new Vector2(lastFirstX, lastFirstY);
        public bool MaterialForCheck => materialForRendering != null;

        /// <summary>当前帧里**离静止姿态最远**的顶点位移（图像比例）。
        /// 夹具用它钉住"站着不动时一个顶点都不许动"（P2 硬判据的几何版）。</summary>
        public float MaxDisplacementForCheck
        {
            get
            {
                var worst = 0f;
                for (var i = 0; i < PuppetRig.Count; i++)
                    worst = Mathf.Max(worst, PuppetRig.Distance(rest[i], deformed[i]));
                return worst;
            }
        }

        public override Texture mainTexture => source != null ? source : s_WhiteTexture;

        public void Configure(Texture2D texture, in PuppetRigDefinition definition, bool mirror, bool splitLegs)
        {
            // 白保险：上一条 RequireComponent 若再被人改回去，这里会留下一行日志，
            // 而不是像以前那样悄悄画不出来。
            if (canvasRenderer == null)
            {
                gameObject.AddComponent<CanvasRenderer>();
                Debug.LogError("[W-A] 网格对象缺 CanvasRenderer（RequireComponent 被改写？），已现场补上。");
            }
            source = texture;
            rig = definition;
            mirrored = mirror;
            legsSplit = splitLegs;
            EnsureGrid();
            ApplyPose(PuppetRigPose.Rest, splitLegs);
            raycastTarget = false;
        }

        public void SetSource(Texture2D texture)
        {
            if (texture == null || texture == source) return;
            source = texture;
            SetMaterialDirty();
            SetVerticesDirty();
        }

        public void SetMirror(bool value)
        {
            if (mirrored == value) return;
            mirrored = value;
            SetVerticesDirty();
        }

        public void SetRig(in PuppetRigDefinition definition)
        {
            rig = definition;
            SetVerticesDirty();
        }

        public void ApplyPose(in PuppetRigPose value, bool splitLegs)
        {
            legsSplit = splitLegs;
            EnsureGrid();
            var next = value;
            if (!splitLegs) next.CollapseToSingleLeg();
            pose = next;
            PuppetRig.Skin(rest, deformed, rig, pose, splitLegs);
            SetVerticesDirty();
        }

        void EnsureGrid()
        {
            if (gridBuilt) return;
            PuppetRig.BuildGrid(rest);
            System.Array.Copy(rest, deformed, PuppetRig.Count);
            gridBuilt = true;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            EnsureGrid();
            var r = GetPixelAdjustedRect();
            lastRectX = r.x; lastRectY = r.y; lastRectW = r.width; lastRectH = r.height;
            var tint = color;
            for (var n = 0; n < PuppetRig.Count; n++)
            {
                var p = deformed[n];
                var u = rest[n];
                // 图像空间（x 右 / y 下）→ UI 局部空间（y 上）
                var position = new Vector3(r.xMin + p.X * r.width, r.yMax - p.Y * r.height, 0f);
                // UV **取自形变前**的位置：像素不变，只搬位置
                var uv = new Vector2(mirrored ? 1f - u.X : u.X, 1f - u.Y);
                vh.AddVert(position, tint, uv);
            }
            for (var j = 0; j < PuppetRig.GridY; j++)
                for (var i = 0; i < PuppetRig.GridX; i++)
                {
                    var n0 = i + j * (PuppetRig.GridX + 1);
                    var n1 = n0 + 1;
                    var n2 = n0 + PuppetRig.GridX + 1;
                    var n3 = n2 + 1;
                    vh.AddTriangle(n0, n2, n1);
                    vh.AddTriangle(n1, n2, n3);
                }
            populateCount++;
            lastVertexCount = vh.currentVertCount;
            var probe = new UIVertex();
            vh.PopulateUIVertex(ref probe, 0);
            lastFirstX = probe.position.x;
            lastFirstY = probe.position.y;
        }
    }
}
