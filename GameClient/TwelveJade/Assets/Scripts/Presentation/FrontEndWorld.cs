using System.Collections.Generic;
using System.Linq;
using TMPro;
using TwelveJade.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // 青石镇大世界（第一阶段·灰盒）：世界远大于一屏，摄像机是移动的窗口。
    //
    // 架构分层（后续阶段只换其中一层，不动其余）：
    //   世界层  worldRoot：尺寸 = WorldSize × worldScale，摄像机平移它，天然不露黑边。
    //   分块层  chunk：每块一块贴图，按世界坐标摆放（chunk 编号只是工程组织，玩家无感）。
    //   实体层  placeable：房屋/树/NPC/角色统一"脚底 Base Point + Y 轴纵深排序"，
    //            本阶段只有占位玩家，接口已就位（二期起直接加对象即可）。
    //   灰盒/正式美术可无痛替换：块的 art 路径来自配置，换成 Grok 母图切分产物即可，
    //            坐标与碰撞配置完全复用。
    //
    // 数据全部来自 Resources/Config/town-map.json（WorldSize/Chunk/Blocked/Camera 都在配置里）。
    public sealed partial class FrontEndController
    {
        // ---- 世界与摄像机 ----
        RectTransform worldRoot;          // 世界根：摄像机平移它
        readonly List<RectTransform> chunkRects = new();
        Texture2D worldOverview;          // 小地图底图（整个世界的缩略）
        float worldScale = 1f;            // 世界单位 → 画布像素（配置项）
        Vector2 camPos;                   // 摄像机（内容画布局部坐标）
        TownMap town;

        // ---- 玩家（占位）----
        RectTransform placeholder;        // 占位玩家的脚底锚点（pivot 0.5,1 = 脚底）
        Vector2 worldPos;                 // 玩家在世界坐标
        Vector2 walkTarget;               // 点击移动目标（世界坐标）
        bool hasTarget;
        float bobPhase;

        // ---- 纵深实体层（二期建筑/NPC 接入点）----
        public sealed class Placeable
        {
            public RectTransform Rect;
            public float BaseY;   // 脚底世界 Y：越大越靠前
            public string Id;
        }
        readonly List<Placeable> placeables = new();
        int depthCursor = -1;              // 当前置位玩家的兄弟索引缓存

        // ---- 小地图 ----
        RectTransform miniRoot, miniDot;
        RawImage miniImage;

        // 到达事件：第一阶段灰盒没有 NPC/遭遇点，事件保留接口但无人触发。
        // 后续阶段把 NPC/遭遇点按"脚底 Base Point"接入 placeables 后，走到这里即可复用
        // 已有的搭话与战斗流程（FrontEndController 里已订阅）。
        public event System.Action<string> NpcArrived;
        public event System.Action<string> EncounterArrived;
        static Texture2D dotTexture;

        public TownMap World => town;
        public float WorldScale => worldScale;
        /// <summary>一屏能看到多少世界单位（宽、高）——验收"视野远小于世界"用。</summary>
        public Vector2 ViewWorldSize => new(1920f / worldScale, 1080f / worldScale);

        // ---------------- 加载 ----------------

        bool LoadWorld()
        {
            if (town != null) return true;
            var asset = Resources.Load<TextAsset>("Config/town-map");
            if (asset == null) { Notify("世界配置缺失：Resources/Config/town-map.json。"); return false; }
            try { town = TownMap.Parse(asset.text); }
            catch (System.FormatException ex) { Notify("世界配置有误：" + ex.Message); return false; }
            worldScale = Mathf.Max(.05f, town.WorldScale);
            return true;
        }

        public void ShowTown()
        {
            if (activeSave == null) { Notify("先落笔创建一位行旅人，再启程入镇。"); return; }
            if (!LoadWorld()) return;
            CloseInventory();
            CloseChapterPanel();
            BeginPage("town");

            // 底色铺满视口：摄像机被世界边界钳制时不会露出黑边。
            ui.Panel(content, "Town backdrop", 0, 0, 1920, 1080, new Color(.09f, .13f, .11f, 1f));

            BuildWorldChunks();
            BuildPlaceholder();
            BuildMiniMap();
            BuildTownHud();

            worldPos = new Vector2(town.SpawnX, town.SpawnY);
            walkTarget = worldPos;
            hasTarget = false;
            SyncPlaceholder();
            UpdateCamera(0f, true);
            FocusFirst();
        }

        // ---- 分块：每块一个 RawImage，按世界坐标摆进 worldRoot ----
        void BuildWorldChunks()
        {
            if (worldRoot != null) { worldRoot.gameObject.SetActive(false); }
            chunkRects.Clear();
            placeables.Clear();

            worldRoot = ui.Rect(content, "World root", 0, 0,
                town.WorldWidth * worldScale, town.WorldHeight * worldScale);
            worldRoot.gameObject.SetActive(true);

            foreach (var chunk in town.Chunks)
            {
                var art = Resources.Load<Texture2D>(chunk.Art);
                var rect = ui.Rect(worldRoot, "Chunk " + chunk.Id,
                    chunk.X * worldScale, chunk.Y * worldScale,
                    chunk.Width * worldScale, chunk.Height * worldScale);
                if (art == null)
                {
                    // 缺图也要占位：否则会露出空洞，误判成"世界边缘"。
                    var missing = rect.gameObject.AddComponent<Image>();
                    missing.color = new Color(.5f, .35f, .3f, 1f);
                    chunkRects.Add(rect);
                    continue;
                }
                var image = rect.gameObject.AddComponent<RawImage>();
                image.texture = art;
                image.raycastTarget = false;
                chunkRects.Add(rect);
            }
            worldOverview = Resources.Load<Texture2D>(town.OverviewArt);
        }

        // ---- 占位玩家：一个胶囊（第二/六阶段换成正式角色，接的是同一个脚底锚点）----
        void BuildPlaceholder()
        {
            if (placeholder != null) Destroy(placeholder.gameObject);
            placeholder = ui.Rect(worldRoot, "Player placeholder", 0, 0, 54 * worldScale, 92 * worldScale);
            placeholder.pivot = new Vector2(.5f, 1f);   // 锚在脚底 = Base Point
            var body = placeholder.gameObject.AddComponent<Image>();
            body.sprite = UiKit.RoundedSprite();
            body.type = Image.Type.Sliced;
            body.color = new Color(.16f, .38f, .33f, 1f);
            body.raycastTarget = false;
        }

        void SyncPlaceholder()
        {
            if (placeholder == null) return;
            placeholder.anchoredPosition = new Vector2(worldPos.x * worldScale, -worldPos.y * worldScale);
        }

        // ---- 小地图：显示整个世界，主画面只显示局部 ----
        void BuildMiniMap()
        {
            const int size = 224;
            var frame = ui.Panel(content, "Mini map", 1608, 96, size + 16, size + 40, UiKit.Dark, true);
            ui.Label(frame.transform, "青石镇 · 全图", 10, 6, size, 26, 16, UiKit.Gold);
            miniRoot = ui.Rect(frame.transform, "Mini world", 8, 32, size, size);
            if (worldOverview != null)
            {
                miniImage = miniRoot.gameObject.AddComponent<RawImage>();
                miniImage.texture = worldOverview;
                miniImage.raycastTarget = false;
            }
            else
            {
                var flat = miniRoot.gameObject.AddComponent<Image>();
                flat.color = new Color(.2f, .26f, .22f, 1f);
            }
            miniRoot.gameObject.AddComponent<Button>().onClick.AddListener(ShowWorldOverlay);

            miniDot = ui.Rect(miniRoot, "Mini player", 0, 0, 14, 14);
            miniDot.pivot = new Vector2(.5f, .5f);
            var dot = miniDot.gameObject.AddComponent<RawImage>();
            dot.texture = DotTexture();
            dot.color = new Color(1f, .84f, .36f, 1f);
            dot.raycastTarget = false;
            UpdateMiniMap();
        }

        void UpdateMiniMap()
        {
            if (miniDot == null || miniRoot == null) return;
            var size = miniRoot.sizeDelta;
            miniDot.anchoredPosition = new Vector2(
                worldPos.x / town.WorldWidth * size.x,
                -worldPos.y / town.WorldHeight * size.y);
        }

        // 全图浮层：点小地图展开，标出所有地标与玩家位置。
        void ShowWorldOverlay()
        {
            CloseModal();
            content.GetComponent<CanvasGroup>().interactable = false;
            modal = ui.Panel(canvas, "World overlay", 0, 0, 1920, 1080, new Color(0f, .04f, .03f, .9f), true).rectTransform;
            const int size = 820;
            var box = ui.Panel(modal, "World box", (1920 - size) / 2f, (1080 - size) / 2f - 20, size, size + 70, UiKit.Dark, true).rectTransform;
            ui.Label(box, "青石镇 · 全图", 20, 10, 400, 34, 24, UiKit.Paper, TextAlignmentOptions.TopLeft, true);
            var art = ui.Rect(box, "World art", 20, 48, size - 40, size - 40);
            if (worldOverview != null)
            {
                var image = art.gameObject.AddComponent<RawImage>();
                image.texture = worldOverview;
                image.raycastTarget = false;
            }
            foreach (var mark in town.Landmarks)
            {
                var dot = ui.Rect(art, "Mark " + mark.Id, 0, 0, 10, 10);
                dot.pivot = new Vector2(.5f, .5f);
                dot.anchoredPosition = new Vector2(
                    mark.X / town.WorldWidth * art.sizeDelta.x,
                    -mark.Y / town.WorldHeight * art.sizeDelta.y);
                var image = dot.gameObject.AddComponent<RawImage>();
                image.texture = DotTexture();
                image.color = new Color(.7f, .24f, .18f, 1f);
                image.raycastTarget = false;
                ui.Label(dot, mark.Name, -46, 8, 110, 22, 16, UiKit.Paper, TextAlignmentOptions.Top);
            }
            var me = ui.Rect(art, "Mark player", 0, 0, 16, 16);
            me.pivot = new Vector2(.5f, .5f);
            me.anchoredPosition = new Vector2(
                worldPos.x / town.WorldWidth * art.sizeDelta.x,
                -worldPos.y / town.WorldHeight * art.sizeDelta.y);
            var meImage = me.gameObject.AddComponent<RawImage>();
            meImage.texture = DotTexture();
            meImage.color = new Color(1f, .84f, .36f, 1f);
            meImage.raycastTarget = false;
            ui.Label(art, "金点 = 你　朱点 = 地标", 20, art.sizeDelta.y - 30, 400, 26, 16, UiKit.Muted, TextAlignmentOptions.BottomLeft);
            var close = modal.gameObject.AddComponent<Button>();
            close.targetGraphic = modal.GetComponent<Image>();
            close.transition = Selectable.Transition.None;
            close.onClick.AddListener(CloseModal);
        }

        // ---------------- 摄像机 ----------------

        // 平滑跟随 + 世界边界钳制。dt=0 且 immediate=true 时直接就位（初始化/验收置位用）。
        void UpdateCamera(float dt, bool immediate = false)
        {
            if (worldRoot == null) return;
            // 视口半宽/半高（世界单位）
            var halfW = 960f / worldScale;
            var halfH = 540f / worldScale;
            // 目标：让玩家位于屏幕中心，再把视口中心夹回世界内
            var targetX = 960f - town.ClampCameraX(worldPos.x, halfW) * worldScale;
            var targetY = -540f + town.ClampCameraY(worldPos.y, halfH) * worldScale;
            if (immediate || dt <= 0f) camPos = new Vector2(targetX, targetY);
            else camPos = Vector2.Lerp(camPos, new Vector2(targetX, targetY), 1f - Mathf.Exp(-12f * dt));
            worldRoot.anchoredPosition = camPos;
        }

        // ---------------- 输入与移动 ----------------

        void UpdateWorld()
        {
            if (page != "town" || worldRoot == null || town == null) return;
            var dt = Time.unscaledDeltaTime;
            var keyboard = Keyboard.current;
            var move = Vector2.zero;
            var run = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
            if (keyboard != null)
            {
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) move.y -= 1f;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) move.y += 1f;
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) move.x -= 1f;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) move.x += 1f;
            }
            if (move != Vector2.zero) { hasTarget = false; move.Normalize(); }
            else if (hasTarget)
            {
                var delta = walkTarget - worldPos;
                if (delta.magnitude < 8f) hasTarget = false;
                else move = delta.normalized;
            }
            if (move != Vector2.zero)
            {
                // 速度按世界单位：跑图距离 = 速度 × 时间，灰盒阶段用"世界单位/秒"。
                TryMove(move * (run ? RunSpeedWorld : WalkSpeedWorld) * dt);
                bobPhase += dt * (run ? 11f : 7f);
            }

            SyncPlaceholder();
            UpdateCamera(dt);
            UpdateMiniMap();
            UpdateDepthOrder();

            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame
                && !EventSystem.current.IsPointerOverGameObject())
            {
                var point = ScreenToWorld(mouse.position.ReadValue());
                if (point != null) { walkTarget = point.Value; hasTarget = true; }
            }
        }

        const float WalkSpeedWorld = 260f, RunSpeedWorld = 470f;

        // 画布坐标 → 世界坐标（点地移动用）
        Vector2? ScreenToWorld(Vector2 screenPoint)
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    content, screenPoint, null, out var local))
            {
                // content 局部 → 世界（注意 y 轴翻转）
                var wx = (local.x - camPos.x) / worldScale;
                var wy = -(local.y - camPos.y) / worldScale;
                if (wx < 0f || wy < 0f || wx > town.WorldWidth || wy > town.WorldHeight) return null;
                return new Vector2(wx, wy);
            }
            return null;
        }

        void TryMove(Vector2 delta)
        {
            var next = worldPos + delta;
            if (CanStand(next)) { worldPos = next; return; }
            if (CanStand(new Vector2(next.x, worldPos.y))) { worldPos = new Vector2(next.x, worldPos.y); return; }
            if (CanStand(new Vector2(worldPos.x, next.y))) worldPos = new Vector2(worldPos.x, next.y);
        }

        bool CanStand(Vector2 point) => town.CanStand(point.x, point.y);

        // 纵深：玩家按世界 Y 与各实体排序，保证走到谁后面就被谁挡住（二期建筑接入点）。
        void UpdateDepthOrder()
        {
            if (placeholder == null) return;
            placeables.Sort((a, b) => a.BaseY.CompareTo(b.BaseY));
            var slot = placeables.FindIndex(p => p.BaseY > worldPos.y);
            if (slot < 0) slot = placeables.Count;
            var target = slot;   // 0 号是分块层，占位人物排在其上
            if (depthCursor == target) return;
            depthCursor = target;
            placeholder.SetSiblingIndex(target + chunkRects.Count);
        }

        // ---------------- 纵深实体接口（二期：房屋/树/NPC 统一走这里）----------------

        /// <summary>在世界坐标处放一个"脚底锚点"的立绘对象，并纳入纵深排序。</summary>
        public RectTransform PlaceInWorld(string id, Vector2 footPoint, float width, float height, Texture2D art = null)
        {
            if (worldRoot == null) return null;
            var rect = ui.Rect(worldRoot, "Placeable " + id,
                footPoint.x * worldScale, footPoint.y * worldScale, width, height);
            rect.pivot = new Vector2(.5f, 1f);   // Base Point 在脚底
            if (art != null)
            {
                var image = rect.gameObject.AddComponent<RawImage>();
                image.texture = art;
                image.raycastTarget = false;
            }
            placeables.Add(new Placeable { Rect = rect, BaseY = footPoint.y, Id = id });
            return rect;
        }

        // ---------------- 验收钩子 ----------------

        /// <summary>玩家是否在视口内（摄像机边界正确性）。</summary>
        public bool PlayerOnScreen()
        {
            if (worldRoot == null) return false;
            var view = camPos + new Vector2(worldPos.x * worldScale, -worldPos.y * worldScale);
            return view.x >= -1f && view.x <= 1921f && view.y <= 1f && view.y >= -1081f;
        }

        /// <summary>玩家离世界边界的距离（跑图距离验收：离边界远 = 世界够大）。</summary>
        public float PlayerEdgeDistance()
        {
            var dx = Mathf.Min(worldPos.x, town.WorldWidth - worldPos.x);
            var dy = Mathf.Min(worldPos.y, town.WorldHeight - worldPos.y);
            return Mathf.Min(dx, dy);
        }

        public Vector2 PlayerWorldPosition => worldPos;
        public bool TownTravelerOnScreen => PlayerOnScreen();
        public bool TerrainStandable(float x, float y) => town != null && CanStand(new Vector2(x, y));
        public int TerrainZoneCount => town?.Blocked.Count ?? 0;

        // ---- NPC 走位钩子（完整阶段接 NPC 时用；第一阶段无 NPC，恒为 false）----
        public bool TownWalkingToNpc => false;
        public bool SnapTravelerNextToNpc(string npcId) => false;
        public bool SnapTravelerAboveNpc(string npcId) => false;

        /// <summary>验收钩子：把玩家放到指定世界坐标（限可走区）。</summary>
        public bool SnapTravelerTo(float x, float y)
        {
            if (!CanStand(new Vector2(x, y))) return false;
            worldPos = new Vector2(x, y);
            SyncPlaceholder();
            UpdateCamera(0f, true);
            UpdateMiniMap();
            UpdateDepthOrder();
            return true;
        }

        // 小地图底图（整个世界缩略）
        static Texture2D DotTexture()
        {
            if (dotTexture != null) return dotTexture;
            const int s = 48;
            dotTexture = new Texture2D(s, s, TextureFormat.RGBA32, false);
            for (var y = 0; y < s; y++)
            for (var x = 0; x < s; x++)
            {
                var dx = (x + .5f) / s - .5f;
                var dy = (y + .5f) / s - .5f;
                var d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                var alpha = Mathf.Clamp01((1f - d) / .12f);
                dotTexture.SetPixel(x, y, new Color(1, 1, 1, alpha * alpha * (3f - 2f * alpha)));
            }
            dotTexture.Apply(false, true);
            return dotTexture;
        }
    }
}
