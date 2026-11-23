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
        string townJson;
        TownWaterSurface townWater;
        WorldClickFeedback worldClickFeedback;

        // ---- 玩家（分层纸偶，P-Character）----
        RectTransform traveler;           // 纸偶根 Rect：纵深排序与验收钩子都取这一个引用
        PuppetActor travelerPuppet;
        CharacterPreset travelerPreset;
        int travelerView = 0;             // 0 正面（南）/ 1 侧身（东西共用）/ 2 背面（北）
        // B-03/B-11：flipbook 走帧开关。**默认 true（WI-3 收口）**：三向（正/侧/背）九张 sheet
        // 已全部灌装入库，走路默认就是逐帧动画，不再有「正/背滑行」。
        // 回退路径：某向 sheet 真缺失时 WalkSheetFor 三级回退仍 null → Debug.LogError 点名缺哪张，
        // 该向保持 Slices 色带（腿僵看得见，人没了看不见）；Inspector 勾掉本开关则整体回旧行为。
        [SerializeField] bool travelerUseWalksheet = true;
        Vector2 worldPos;                 // 玩家在世界坐标
        Vector2 walkTarget;               // 点击移动目标（世界坐标）
        // MAT-11（2.5D 移动手感）：起步与收步走加减速，而不是「按下即全速、松手即刹停」。
        // 依据：角色移动的「纸偶感」主要来自三处——①动画与位移不匹配（本项目已由
        // AdvanceGait 的位移驱动解决）②瞬时变速 ③无转身过渡。此处补②。
        float travelerSpeedNow;       // 当前速度（世界单位/秒）
        Vector2 travelerMoveDir;      // 最近一次移动方向（收步滑行期间仍用它）
        bool hasTarget;
        // ---- 走向目标（P-02）：点 NPC/遭遇 → RequestTravelTo → 到达后回调 ----
        // 新请求顶掉旧目标时，被顶掉的旧回调不触发（防串话：点了第二个 NPC，
        // 不能弹出第一个的搭话框）。
        System.Action<string> travelCallback;
        System.Func<bool> travelArrivalCondition;
        string travelContextId;
        const float TravelArriveRadius = 48f;   // 到达阈值：NPC radius 附近，"走到跟前开口"

        // 立绘高度与 NPC 默认身高同档（town-map npcs[].height = 170）：
        // 玩家与镇上的人站在同一条地平线上时不该是个矮人。
        const float TravelerHeight = 170f;

        // ---- 纵深实体层（二期建筑/NPC 接入点）----
        public sealed class Placeable
        {
            public RectTransform Rect;
            public float BaseY;   // 脚底世界 Y：越大越靠前
            public string Id;
            public bool FrameCamera;
        }
        readonly List<Placeable> placeables = new();

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
            townJson = asset.text;
            worldScale = Mathf.Max(.05f, town.WorldScale);
            return true;
        }

        public void ShowTown()
        {
            if (activeSave == null) { Notify("先落笔创建一位行旅人，再启程入镇。"); return; }
            if (!LoadWorld()) return;
            StopNpcPatrols(true);   // 重建前先收掉上一次的实例与动态圆心，绝不让旧圆心残留
            CloseInventory();
            CloseChapterPanel();
            BeginPage("town");

            // 底色铺满视口：摄像机被世界边界钳制时不会露出黑边。
            ui.Panel(content, "Town backdrop", 0, 0, 1920, 1080, new Color(.09f, .13f, .11f, 1f));

            BuildWorldChunks();
            BuildTownWater();
            BuildWorldClickFeedback();
            BuildTraveler();
            BuildTownNpcs();
            BuildNpcPatrols();
            BuildQuestOverheads();
            BuildMiniMap();
            BuildTownHud();
            BuildMobileControls();

            worldPos = ReferenceEquals(wildernessReturnOwner, activeSave) && wildernessReturn.HasValue && CanStand(wildernessReturn.Value)
                ? wildernessReturn.Value : new Vector2(town.SpawnX, town.SpawnY);
            wildernessReturn = null; wildernessReturnOwner = null;
            walkTarget = worldPos;
            hasTarget = false;
            SyncTraveler();
            UpdateCamera(0f, true);
            FocusFirst();
        }

        // ---- 分块：每块一个 RawImage，按世界坐标摆进 worldRoot ----
        // 贴图路径由 TownMap.ChunkArtAt 决定：配了 chunkArtTemplate 走正式产物（Gate C1-C），
        // 未配才回落 chunks[].art（一期灰盒）。缺图一律 LogError + 品红标记，
        // 绝不静默画灰块——静默回退会让"贴图没接上"看起来像"世界就长这样"。
        void BuildWorldChunks()
        {
            ResetTownOcclusion();
            if (worldRoot != null)
            {
                worldRoot.gameObject.SetActive(false);
                Destroy(worldRoot.gameObject);
            }
            townWater = null;
            worldClickFeedback = null;
            chunkRects.Clear();
            placeables.Clear();

            worldRoot = ui.Rect(content, "World root", 0, 0,
                town.WorldWidth * worldScale, town.WorldHeight * worldScale);
            worldRoot.gameObject.SetActive(true);

            var size = town.ChunkSize * worldScale;
            var missing = 0;
            for (var row = 0; row < town.WorldRows; row++)
            {
                for (var col = 0; col < town.WorldColumns; col++)
                {
                    var artPath = town.ChunkArtAt(col, row);
                    var rect = ui.Rect(worldRoot, "Chunk r" + row + "-c" + col,
                        col * size, row * size, size, size);
                    var art = string.IsNullOrEmpty(artPath) ? null : Resources.Load<Texture2D>(artPath);
                    if (art == null)
                    {
                        missing++;
                        Debug.LogError("世界分块贴图缺失：Resources/" + (artPath ?? "(路径未配置)") +
                            "（格 " + col + "," + row + "）。查 chunkArtTemplate 与 Resources/Art/World/Chunks/。");
                        var hole = rect.gameObject.AddComponent<Image>();
                        hole.color = new Color(1f, 0f, 1f, 1f);   // 品红 = 缺图，不是任何地形色，截图里一眼可辨
                        hole.raycastTarget = false;
                        chunkRects.Add(rect);
                        continue;
                    }
                    var image = rect.gameObject.AddComponent<WorldTerrainImage>();
                    image.Configure(town.Elevation, col * town.ChunkSize, row * town.ChunkSize, town.ChunkSize, worldScale);
                    image.texture = art;
                    image.raycastTarget = false;
                    chunkRects.Add(rect);
                }
            }
            if (missing > 0) Notify("世界贴图缺失 " + missing + " 块（品红格）。");

            worldOverview = Resources.Load<Texture2D>(town.OverviewArt);
            if (worldOverview == null && !string.IsNullOrEmpty(town.OverviewArt))
                Debug.LogError("世界总览图缺失：Resources/" + town.OverviewArt + " —— 小地图会没有底图。");
        }

        void BuildTownWater()
        {
            if (!TownWaterSurface.TryCreateFromTownMap(worldRoot, town, worldScale,
                () => settings == null || settings.reduceMotion, out townWater, out var error, townJson))
            {
                Debug.LogError("青石镇动态水体创建失败：" + error);
                return;
            }
            townWater.rectTransform.SetSiblingIndex(chunkRects.Count);
        }

        // ---- 主角：分层纸偶（P-Character P1）----
        // 以前这里是一颗深绿胶囊。胶囊没有关节也没有朝向，玩家怎么走都是"一块绿在滑"，
        // 于是角色页那台纸偶与镇上看到的自己完全是两个东西——预览页写着"镇上那台就是这台纸偶"，
        // 那句话在 P1 之前是假的。现在两处共用 PuppetActor。
        void BuildTraveler()
        {
            if (traveler != null) Destroy(traveler.gameObject);
            travelerPuppet = null;
            travelerPreset = presets == null || presets.Length == 0 || activeSave == null ? null
                : System.Array.Find(presets, p => p.id == activeSave.characterId) ?? presets[0];
            if (travelerPreset == null)
            {
                Debug.LogError("主角外观预设缺失：Resources/Characters 下没有 CharacterPreset（存档角色 id=" +
                    (activeSave == null ? "(无存档)" : activeSave.characterId) +
                    "）。宁可不画也不回退成胶囊——胶囊会让缺素材看起来像世界就长这样。");
                return;
            }
            // 进镇时的朝向沿用存档里角色页选的那一档（0 正 / 1 右 / 2 背 / 3 左），
            // 左侧靠运行时镜像，不额外要第四张画稿。
            travelerView = travelerViewOf(activeSave.facing);
            var art = TravelerArt(travelerView);
            if (art == null)
                Debug.LogError("主角立绘缺失：Art/" + travelerPreset.id + "-" + activeSave.gender +
                    "-" + activeSave.faceStyle + "-" + ViewName(travelerView) + "（朝向 " + travelerView + "）。");
            travelerPuppet = PuppetActor.Create(worldRoot, art, Vector2.zero,
                new Vector2(TravelerHeight * (art != null ? art.width / (float)art.height : .593f), TravelerHeight));
            // 只有侧身档需要左右镜像（正/背两向本身没有左右，镜像会把发髻、背篓翻到错的一边）。
            travelerPuppet.SetMirror(travelerView == 1 && activeSave.facing == 3);
            travelerPuppet.SetLegSplit(travelerView != 1);
            if (travelerMeshDeform) travelerPuppet.SetGaitMode(PuppetActor.GaitMode.MeshDeform);
            // B-03/B-11:三向走帧 sheet(正/侧/背各一张,同规格 6528x1088、8 格)。开关打开时三向都走 Frames。
            // **优先级钉死：walksheet 必须在 meshDeform 之后调用**——Frames 是真帧素材、MeshDeform 是
            // 程序形变，素材在则素材优先；顺序反了 Frames 会被 MeshDeform 无条件覆盖（WI-3 前的 wiring 断点）。
            if (travelerUseWalksheet)
                ApplyTravelerWalkSheet(travelerView);
            ResetFootsteps();   // 换人/换向后相位基准清零，免得多补出一串脚印声
            travelerPuppet.SetMotion(PuppetActor.Motion.Idle);
            traveler = travelerPuppet.Root;
            traveler.gameObject.name = "Traveler puppet";
            reachedPlaces.Clear();        // 换档/重进镇：到访去重清掉，赴约这件事要能再发生一次
            ApplyTravelerTier();          // 档位与接地阴影在建完人之后立刻套一次（此时才有 root 可挂）
        }

        // ---- Q1.7 第 4 条：主角在镇页的呈现档位（只交档位，不交"多大算对"）----
        // 三个数字是三个乘数，不是三个"更好的尺寸"：选哪一档要架构师、我、斐哥三个人看图定，
        // 我这边只保证"能切、切了立刻可见、并且有一张三档对照图"。
        // 默认档 = 1（乘数 1.0）= 本轮之前的实际尺寸，所以这条改动对玩家零变化。
        [Header("主角呈现档位（Q1.7）")]
        [SerializeField] float[] travelerSizeTiers = { .8f, 1f, 1.25f };
        [SerializeField] int travelerTier = 1;
        // 着地阴影默认关：妖在战斗页没有接触阴影（combatFoeImage 就是一张图），
        // 只给主角加会让两者用不同的着地语言——所以它是一档开关，不是新默认值。
        // MAT-10：默认**开**。此前默认关是「照 A/B 对照」的产物；斐哥看截图时的原话是
        // 「人物还是飘的感觉」——没有影子的立绘在草地上就是浮着的，这一条就是定档。
        // 场景未序列化本字段，所以改这里的默认值即生效（夹具仍可用 SetTravelerGroundShadow 关掉做对照）。
        [SerializeField] bool travelerGroundShadow = true;
        // W-A：主角渲染方式。**WALK-FIX 起默认开**（网格形变：膝盖能屈、脚掌能滚、侧身档腿真的摆）。
        // 此前默认关是因为 10-06 判图时「主角整块消失」，原因当时没查清；WALK-FIX 查清了：
        // `PuppetMeshGraphic` 的 `[RequireComponent(typeof(RectTransform))]` 把从 Graphic 继承的
        // `[RequireComponent(typeof(CanvasRenderer))]` 顶掉，对象上没有 CanvasRenderer ⇒
        // `Graphic.Rebuild` 第一行就 return ⇒ `OnPopulateMesh` 终生不调、不报错、画面上什么都没有。
        // 修掉之后同一台相机量到：侧身档摆腿 38px / 抬脚 11px（色带模式 3.1px / 3px = 冻住），
        // 图在 `Tools/logs/walk-fix/`（report.md 每条结论都绑着图）。
        [SerializeField] bool travelerMeshDeform = true;
        Image travelerShadow;
        int travelerMeshGuardFrames;   // WALK-FIX：网格自检用的帧计数（一次性的）
        bool travelerMeshGuardDone;
        readonly List<PuppetActor> tierBoard = new List<PuppetActor>();

        float TierScale()
        {
            if (travelerSizeTiers == null || travelerSizeTiers.Length == 0) return 1f;
            return travelerSizeTiers[Mathf.Clamp(travelerTier, 0, travelerSizeTiers.Length - 1)];
        }

        void ApplyTravelerTier()
        {
            if (travelerPuppet == null) return;
            travelerPuppet.SetScale(TierScale());
            SyncTravelerShadow();
        }

        /// <summary>W-A：主角渲染方式开关（与 `SetTravelerGroundShadow` 同一范式：给夹具与"看图定档"用）。
        /// 切换会**立刻重建**渲染对象；默认值见上面的字段声明（WALK-FIX 起 = 网格形变）。</summary>
        public void SetTravelerMeshDeform(bool on)
        {
            travelerMeshDeform = on;
            if (on) { travelerMeshGuardDone = false; travelerMeshGuardFrames = 0; }
            if (travelerPuppet != null)
                travelerPuppet.SetGaitMode(on ? PuppetActor.GaitMode.MeshDeform : PuppetActor.GaitMode.Slices);
        }

        // WALK-FIX：网格形变"画不出来"这件事不许再沉默。10-06 那次的症状是：
        // 主角整块消失、不报错、不抛异常（`[RequireComponent(typeof(RectTransform))]` 把 Graphic
        // 自带的 `[RequireComponent(typeof(CanvasRenderer))]` 顶掉 ⇒ `OnPopulateMesh` 终生不被调）。
        // 现在：第一次 populate 就打一行证据行；30 帧还一次都没有就当场报错并退回色带模式
        // ——腿僵看得见，人没了看不见。
        void GuardTravelerMesh()
        {
            if (!travelerMeshDeform || travelerMeshGuardDone) return;
            var mesh = travelerPuppet != null ? travelerPuppet.MeshForCheck : null;
            if (mesh == null) return;
            if (mesh.PopulateCountForCheck > 0)
            {
                travelerMeshGuardDone = true;
                Debug.Log("[WALK-FIX] 城镇页网格形变已 populate " + mesh.PopulateCountForCheck +
                    " 次 / 顶点 " + mesh.VertexCountForCheck + "（CanvasRenderer 在位，不是旧纸片）。");
                return;
            }
            if (++travelerMeshGuardFrames < 30) return;
            travelerMeshGuardDone = true;
            Debug.LogError("[WALK-FIX] 网格形变 30 帧内一次都没 populate（CanvasRenderer / Canvas 层级问题回来了？）" +
                "→ 自动退回色带模式，避开「主角整块消失」。");
            SetTravelerMeshDeform(false);
        }

        void SyncTravelerShadow()
        {
            if (travelerPuppet == null) return;
            if (!travelerGroundShadow)
            {
                if (travelerShadow != null) travelerShadow.enabled = false;
                return;
            }
            if (travelerShadow == null)
            {
                var go = new GameObject("Traveler ground shadow", typeof(RectTransform));
                var rect = go.GetComponent<RectTransform>();
                // 挂成纸偶的孩子：这样它自动跟着纸偶的纵深排序与档位缩放走，
                // 不需要在本轮的 UpdateDepthOrder 里再多插一个特例（那正是漏排序的常见起点）。
                rect.SetParent(travelerPuppet.Root, false);
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0f);
                rect.pivot = new Vector2(.5f, .5f);
                travelerShadow = go.AddComponent<Image>();
                travelerShadow.sprite = UiKit.RoundedSprite();   // 复用现成的圆角块，不生新图
                travelerShadow.type = Image.Type.Sliced;
                travelerShadow.color = new Color(0f, 0f, 0f, .22f);
                travelerShadow.raycastTarget = false;
            }
            travelerShadow.enabled = true;
            var w = TravelerHeight * .58f;
            var shadowRect = travelerShadow.rectTransform;
            shadowRect.sizeDelta = new Vector2(w, w * .18f);
            shadowRect.anchoredPosition = new Vector2(0f, w * .09f);
        }

        public void SetTravelerTier(int tier)
        {
            travelerTier = Mathf.Clamp(tier, 0, (travelerSizeTiers?.Length ?? 1) - 1);
            ApplyTravelerTier();
        }

        public void SetTravelerGroundShadow(bool on)
        {
            travelerGroundShadow = on;
            SyncTravelerShadow();
        }

        public int TravelerTierForCheck => Mathf.Clamp(travelerTier, 0, (travelerSizeTiers?.Length ?? 1) - 1);
        public int TravelerTierCount => travelerSizeTiers?.Length ?? 0;
        public float TravelerScaleForCheck => TierScale();
        public bool TravelerShadowShownForCheck => travelerShadow != null && travelerShadow.enabled;

        /// <summary>三档对照板：主角两侧各摆一台同立绘的纸偶，分别用第一档与第三档；
        /// 中间这台就是本人（默认档）。出完图必须调 ClearTravelerTierBoard——
        /// 这些副本不吃输入、不参与碰撞，留着会让下一张图连"镇上有几个人"都算错。</summary>
        public bool ShowTravelerTierBoard()
        {
            ClearTravelerTierBoard();
            if (travelerPuppet == null || traveler == null) return false;
            var art = TravelerArt(travelerView);
            if (art == null) return false;
            if (TravelerTierCount < 3) return false;     // 对照板要的是"最少三档"，档数不够就别画半张
            var size = new Vector2(TravelerHeight * art.width / (float)art.height, TravelerHeight);
            var side = new[] { -TravelerHeight * 1.15f, TravelerHeight * 1.15f };
            var tiers = new[] { 0, TravelerTierCount - 1 };
            for (var i = 0; i < side.Length; i++)
            {
                var copy = PuppetActor.Create(worldRoot, art, Vector2.zero, size);
                copy.SetLegSplit(travelerView != 1);
                copy.SetMotion(PuppetActor.Motion.Idle);
                copy.SetPosition(traveler.anchoredPosition + new Vector2(side[i], 0f));
                copy.SetScale(travelerSizeTiers[tiers[i]]);
                tierBoard.Add(copy);
            }
            return true;
        }

        public void ClearTravelerTierBoard()
        {
            foreach (var copy in tierBoard)
                if (copy != null) Destroy(copy.gameObject);
            tierBoard.Clear();
        }

        /// <summary>验收钩子：对照板上现在还挂着几台副本。出完图必须归零——
        /// 这几台不吃输入、不参与碰撞，留着就是"镇上有四个人"的假象。</summary>
        public int TravelerBoardCountForCheck => tierBoard.Count;

        static int travelerViewOf(int facing) => facing == 2 ? 2 : facing == 0 ? 0 : 1;

        static string ViewName(int view) => view == 0 ? "front" : view == 2 ? "back" : "side";

        Texture2D TravelerArt(int view) =>
            activeSave == null ? null : TownCharacterArt.Hero(activeSave.gender, ViewName(view), false)
                ?? (travelerPreset == null ? null : travelerPreset.Facing(view, activeSave.gender, activeSave.faceStyle));

        // 三向走帧:view 0 正面、1 侧面、2 背面(与 UpdateTravelerFacing 一致)。
        // 只在换向点调用(初始化一次 + 每次 view 变化一次),稳态每帧不调用,故不会每帧重建。
        // 顺序固定为先 SetFrameSheet 后 SetGaitMode:sheet 变化会 BuildRenderers,SetGaitMode 同模式短路。
        void ApplyTravelerWalkSheet(int view)
        {
            if (travelerPuppet == null) return;
            var sheet = WalkSheetFor(view);
            if (sheet == null)
            {
                // 三级回退都没拿到 = 素材真缺：报错点名缺哪张（同一档只报一次，换向来回走不刷屏）。
                var key = (travelerPreset != null ? travelerPreset.id : "(null)") + "-" + ViewName(view);
                if (warnedSheets.Add(key))
                    Debug.LogError("走帧 sheet 缺失（含 farmer-male 兜底）：preset=" +
                        (travelerPreset != null ? travelerPreset.id : "(null)") + " 朝向=" + ViewName(view) +
                        " —— 该向退回色带模式，查 Art/Characters/Walk/*-walksheet-*-v1。");
                // 必须显式退回 Slices：只 return 的话换向到缺帧那一档时会继续用旧朝向的 sheet
                // 播 Frames（错朝向的真动画比滑行更难发现）。
                travelerPuppet.SetGaitMode(PuppetActor.GaitMode.Slices);
                return;
            }
            var approved = TownCharacterArt.Find("hero-" + (activeSave.gender == "female" ? "female" : "male") + "-" + ViewName(view));
            travelerPuppet.SetFrameSheet(sheet, approved?.frames ?? 8, approved?.cycleDistance ?? (view == 1 ? 290f : 240f));
            travelerPuppet.SetGaitMode(PuppetActor.GaitMode.Frames);
        }

        // 已报过缺失的 "preset-朝向"（一次进程内同一档只报一次错）。
        static readonly HashSet<string> warnedSheets = new HashSet<string>();

        // 走帧按 **造型 + 性别 + 朝向** 选择,与 TravelerArt 静态立绘同口径(MAT-05)。
        // 字面量路径是硬约束:SaveChecks 的孤儿棘轮按 Resources.Load 字面量判引用,拼接的路径识别不到,
        // 所以这里必须逐条枚举而不能拼字符串。变体缺失时逐级回退,保证永远拿得到 sheet:
        //   所选造型+性别 -> 同造型 male(女性走帧未灌装时的兜底) -> farmer-male-0(起点档,已灌装)
        Texture2D WalkSheetFor(int view)
        {
            var suffix = view == 0 ? "front" : view == 2 ? "back" : "side";
            var id = travelerPreset != null ? travelerPreset.id : "farmer";
            var gender = activeSave != null ? activeSave.gender : "male";

            var sheet = TownCharacterArt.Hero(gender, suffix, true);
            if (sheet != null) return sheet;
            sheet = LoadWalkSheet(id, gender, suffix);
            if (sheet != null) return sheet;
            if (gender != "male")
            {
                sheet = LoadWalkSheet(id, "male", suffix);
                if (sheet != null) return sheet;
            }
            return LoadWalkSheet("farmer", "male", suffix);
        }

        // 已灌装的走帧表(每加一套新 sheet 就在这里补三条字面量)。
        static Texture2D LoadWalkSheet(string id, string gender, string suffix) =>
            (id + "-" + gender + "-" + suffix) switch
            {
                "farmer-male-front" => Resources.Load<Texture2D>("Art/Characters/Walk/farmer-male-0-walksheet-front-v1"),
                "farmer-male-side" => Resources.Load<Texture2D>("Art/Characters/Walk/farmer-male-0-walksheet-side-v1"),
                "farmer-male-back" => Resources.Load<Texture2D>("Art/Characters/Walk/farmer-male-0-walksheet-back-v1"),
                "merchant-male-front" => Resources.Load<Texture2D>("Art/Characters/Walk/merchant-male-1-walksheet-front-v1"),
                "merchant-male-side" => Resources.Load<Texture2D>("Art/Characters/Walk/merchant-male-1-walksheet-side-v1"),
                "merchant-male-back" => Resources.Load<Texture2D>("Art/Characters/Walk/merchant-male-1-walksheet-back-v1"),
                "traveller-male-front" => Resources.Load<Texture2D>("Art/Characters/Walk/traveller-male-1-walksheet-front-v1"),
                "traveller-male-side" => Resources.Load<Texture2D>("Art/Characters/Walk/traveller-male-1-walksheet-side-v1"),
                "traveller-male-back" => Resources.Load<Texture2D>("Art/Characters/Walk/traveller-male-1-walksheet-back-v1"),
                _ => null,
            };

        void SyncTraveler()
        {
            if (travelerPuppet == null) return;
            travelerPuppet.SetPosition(new Vector2(worldPos.x * worldScale, -town.Elevation.ProjectY(worldPos.x, worldPos.y) * worldScale));
        }

        // ---- 小地图：显示整个世界，主画面只显示局部 ----
        void BuildMiniMap()
        {
            var frame=ui.Rect(content,"Mini map",1546,24,340,340);
            miniRoot=ui.Rect(frame,"Circular painted world",39,39,262,262);
            var map=miniRoot.gameObject.AddComponent<LivingMiniMap>();
            map.texture=Resources.Load<Texture2D>("Art/UI/Nov8/qingshi-town-atlas");
            if(map.texture==null)map.texture=worldOverview;
            miniImage=map;miniImage.raycastTarget=true;
            miniRoot.gameObject.AddComponent<Button>().onClick.AddListener(ShowWorldOverlay);
            GuofengHudArt.Create(ui,frame,"Bronze trigram armillary","hud-map",0,0,340,340);
            // A compass cursor is projected into the circular crop and remains above the map art.
            miniDot=ui.Rect(miniRoot,"Mini player",0,0,14,14);miniDot.pivot=new Vector2(.5f,.5f);
            var dot=miniDot.gameObject.AddComponent<RawImage>();dot.texture=DotTexture();dot.color=new Color(1,.88f,.42f,1);dot.raycastTarget=false;
            MenuMemoArt.Command(ui,frame,"Wilderness trail route","山径",new Rect(106,318,128,50),ShowWildRoutes,()=>settings.reduceMotion,PlayClick,20,false);
            UpdateMiniMap();
        }
        void UpdateMiniMap()
        {
            if(miniDot==null||miniRoot==null)return;
            var image=miniImage as LivingMiniMap;if(image==null)return;
            // Local view fits the playable world edges; UV and marker share this one transform.
            const float span=.5f;
            float px=worldPos.x/town.WorldWidth,py=worldPos.y/town.WorldHeight;
            float left=Mathf.Clamp(px-span*.5f,0,1-span),top=Mathf.Clamp(py-span*.5f,0,1-span);
            image.uvRect=new Rect(left,1-top-span,span,span);
            var size=miniRoot.sizeDelta;
            var local=new Vector2((px-left)/span-.5f,(py-top)/span-.5f);
            if(local.magnitude>.455f)local=local.normalized*.455f;
            miniDot.anchoredPosition=new Vector2((.5f+local.x)*size.x,-(.5f+local.y)*size.y);
        }

        int atlasFootCell=-1;SaveData atlasFootOwner;float atlasNextRecord;
        void RecordAtlasFootprint()
        {
            if(activeSave==null||town==null||Time.unscaledTime<atlasNextRecord)return;
            int cell=AtlasExploration.Cell(worldPos.x,worldPos.y,town.WorldWidth,town.WorldHeight);
            if(cell<0||!town.CanStand(worldPos.x,worldPos.y))return;
            if(ReferenceEquals(atlasFootOwner,activeSave)&&cell==atlasFootCell)return;
            atlasNextRecord=Time.unscaledTime+1;
            try
            {
                AtlasExploration.Commit(activeSave,()=>AtlasExploration.Reveal(activeSave,worldPos.x,worldPos.y,town.WorldWidth,town.WorldHeight)
                    |AtlasExploration.RevealEarnedEvents(activeSave,town,Quests),repository.Write);
                atlasFootOwner=activeSave;atlasFootCell=cell;
            }
            catch(System.Exception ex){atlasNextRecord=Time.unscaledTime+10;Debug.LogError("Atlas exploration save failed: "+ex.Message);}
        }
        void ShowWorldOverlay()=>ShowInkAtlas("");
        void ShowInkAtlas(string resultNote)
        {
            if(page!="town"||town==null||WorldDialogueInputBlocked)return;
            RecordAtlasFootprint();CloseInventory();CloseChapterPanel();CloseModal();CancelTravel();
            var group=content.GetComponent<CanvasGroup>();group.interactable=false;group.blocksRaycasts=false;
            modal=ui.Rect(canvas,"World overlay",0,0,1920,1080);
            modal.gameObject.AddComponent<Image>().color=new Color(.025f,.045f,.06f,.8f);
            modal.gameObject.AddComponent<V2FocusNavigation>();
            modal.gameObject.AddComponent<WideInkAtlasView>().Build(ui,modal,town,townJson,worldPos,activeSave,()=>settings.reduceMotion,CloseModal,PlayClick);
        }

        // ---------------- 摄像机 ----------------

        // 平滑跟随 + 世界边界钳制。dt=0 且 immediate=true 时直接就位（初始化/验收置位用）。
        void UpdateCamera(float dt, bool immediate = false)
        {
            if (worldRoot == null) return;
            // 视口半宽/半高（世界单位）
            var halfW = 960f / worldScale;
            var halfH = 540f / worldScale;
            // Nearby configured gate roofs get bounded headroom; the traveler stays in frame.
            // Use the rendered landmark bounds, then keep the ordinary world-edge clamp and damping.
            var projectedY = town.Elevation.ProjectY(worldPos.x, worldPos.y);
            var targetX = 960f - town.ClampCameraX(worldPos.x, halfW) * worldScale;
            var targetY = -540f + town.ClampCameraY(projectedY - LandmarkCameraHeadroom(projectedY, halfH), halfH) * worldScale;
            if (immediate || dt <= 0f) camPos = new Vector2(targetX, targetY);
            else camPos = Vector2.Lerp(camPos, new Vector2(targetX, targetY), 1f - Mathf.Exp(-12f * dt));
            worldRoot.anchoredPosition = camPos;
        }

        float LandmarkCameraHeadroom(float projectedY, float halfViewHeight)
        {
            float shift = 0f;
            foreach (var item in placeables)
            {
                if (!item.FrameCamera || item.Rect == null || !item.Rect.gameObject.activeInHierarchy) continue;
                var rect = item.Rect;
                float width = rect.rect.width / worldScale;
                float horizontal = Mathf.Abs(worldPos.x - rect.anchoredPosition.x / worldScale);
                float distance = Mathf.Abs(projectedY - item.BaseY);
                float across = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(width * .35f, width * .65f, horizontal));
                float near = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(160f / worldScale, 360f / worldScale, distance));
                float roof = -rect.anchoredPosition.y / worldScale;
                float needed = Mathf.Clamp(projectedY - roof - halfViewHeight + 48f / worldScale, 0f, 240f / worldScale);
                shift = Mathf.Max(shift, needed * across * near);
            }
            return shift;
        }

        // ---------------- 输入与移动 ----------------

        void UpdateWorld()
        {
            if (page != "town" || worldRoot == null || town == null)
            {
                CancelMining();
                if (worldClickFeedback != null) worldClickFeedback.ClearImmediate();
                // 整页切走（商店/战斗/菜单）或世界已拆：停实例并清动态圆心，回镇时按日程重建。
                if (npcPatrolsLive || (town != null && town.NpcDynamicPositionCount > 0)) StopNpcPatrols(true);
                return;
            }
            // Loading or a paused editor must not turn one frame into a whole-route catch-up.
            // Clamp only world movement/inertia/camera; normal frame timing and collision sweeps stay intact.
            var frameDt = Time.unscaledDeltaTime;
            var dt = Mathf.Min(frameDt, .05f);
            if (WorldDialogueInputBlocked || modal != null || bagOverlay != null)
            {
                CancelMining();
                StopDialogueMovement();
                AdvanceNpcPatrols(dt); // The speaking NPC has its explicit lease; others keep their world routine.
                UpdateCamera(dt); UpdateDepthOrder();
                return;
            }
            // Move NPC blockers first. The player's actual move and end-of-frame queries now
            // see the same collision positions; NPC patrols also avoid the current traveler.
            AdvanceNpcPatrols(dt);
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
            if (mobileMovePad != null && mobileMovePad.isActiveAndEnabled && mobileMovePad.Direction != Vector2.zero)
            {
                move = new Vector2(mobileMovePad.Direction.x, -mobileMovePad.Direction.y);
                run = mobileMovePad.Running;
            }
            if (move != Vector2.zero)
            {
                // T033：键盘按下就是玩家说"我自己走"，优先级高于自动寻路。
                // 走向 NPC 途中被键盘接管 → 这一趟立刻作废，被丢弃的到达回调不触发
                // （否则人会走到一半又自己弹出对话框，比不响应更莫名其妙）。
                TakeOverFromKeyboard(ref move);
            }
            else if (hasTarget)
            {
                if (travelRoute.Count>0)
                {
                    while(travelRouteIndex<travelRoute.Count-1 && (travelRoute[travelRouteIndex]-worldPos).magnitude<.1f)
                        travelRouteIndex++;
                    walkTarget=travelRoute[travelRouteIndex];
                }
                var delta = walkTarget - worldPos;
                // 走向 NPC/遭遇的到达阈值比点地移动宽（radius 附近即算"走到跟前"）。
                var arriveRadius = travelRouteIndex<travelRoute.Count-1 ? 6f : travelCallback != null ? TravelArriveRadius : 8f;
                // Dialogue can complete as soon as its live NPC is in range, even before the
                // route's final approach point. Ground clicks and encounters keep their waypoint rule.
                bool arrivedAtTarget = travelArrivalCondition != null ? travelArrivalCondition()
                    : delta.magnitude < arriveRadius && travelRouteIndex >= travelRoute.Count - 1;
                if (arrivedAtTarget)
                {
                    var arrived = travelCallback;
                    var context = travelContextId;
                    // E4 要点 7：先回调、后清状态——回调（TalkTo/ShowCombat）里回读
                    // TravelBusy 时仍算"在途"，不会把"刚到达"和"已处理完"观测成同一瞬间。
                    arrived?.Invoke(context);
                    // 重入守卫：回调若又发起了新的走向请求，不能用旧一趟的状态把它抹掉。
                    if (ReferenceEquals(travelCallback, arrived) && travelContextId == context)
                    {
                        hasTarget = false;
                        if (worldClickFeedback != null) worldClickFeedback.End();
                        travelCallback = null;
                        travelArrivalCondition = null;
                        travelContextId = null;
                    }
                }
                else move = delta.normalized;
            }
            // P2：步态由**真实位移**驱动，不由时间驱动。
            // 撞墙、被阻挡、批处理里空转的每一帧，位移都是 0 → 腿停在原地不摆。
            var speed = run ? RunSpeedWorld : WalkSpeedWorld;
            // MAT-11：目标速度 -> 当前速度走一阶逼近（起步用 SpeedAccel、收步用 SpeedDecel）。
            // 停止时 move 归零但方向仍须保留，否则收步那一小段会朝 (0,0) 原地打转。
            var targetSpeed = move.sqrMagnitude > .0001f ? speed : 0f;
            if (!WorldCombatCanWalk) { targetSpeed = 0; travelerSpeedNow = 0; }
            if (move.sqrMagnitude > .0001f) travelerMoveDir = move.normalized;
            travelerSpeedNow = Mathf.MoveTowards(travelerSpeedNow, targetSpeed,
                (targetSpeed > travelerSpeedNow ? SpeedAccel : SpeedDecel) * dt);
            var moved = travelerSpeedNow > .01f
                ? TryMove(travelerMoveDir * (hasTarget ? Mathf.Min(travelerSpeedNow * dt, (walkTarget-worldPos).magnitude) : travelerSpeedNow * dt))
                : Vector2.zero;
            if (travelerPuppet != null)
            {
                travelerPuppet.GaitSpeedForCycle = Mathf.Max(1f, travelerSpeedNow);
                travelerPuppet.AdvanceGait(moved.magnitude);
                travelerPuppet.ReducedMotionForRest = settings.reduceMotion;
                travelerPuppet.SetMotion(moved.sqrMagnitude > .0001f
                    ? (run ? PuppetActor.Motion.Run : PuppetActor.Motion.Walk)
                    : PuppetActor.Motion.Idle);
                // W-C：脚步由**同一条真实位移**驱动的相位触发 ⇒ 站着不动一声都不响。
                TickFootsteps();
                // C-13/C-14：环境底噪随昼夜（白天鸟鸣 / 夜里虫鸣）切换。
                // **C 线只在这里加一行调用**（AA-4：`FrontEndWorld.cs` 归 C 的是"相机阻尼/音频调用"段）。
                TickAmbience();
                // 朝向按**意图**吸附（move），不按位移（moved）：面墙硬走时人该还是朝着那个方向，
                // 而不是因为没挪动就退回上一朝向。
                UpdateTravelerFacing(move);
                // WALK-FIX：网格形变到底画没画出来，每帧看一眼（一次性的，见 GuardTravelerMesh 的注释）。
                GuardTravelerMesh();
            }

            TickWilderness(dt);
            TickWorldCombat(dt);
            SyncTraveler();
            SampleWorldCombatPose();
            ReportTravelReach();
            RecordAtlasFootprint();
            UpdateCamera(dt);
            UpdateMiniMap();
            UpdateDepthOrder();
            if (page != "town") return;

            if (MobileControls.TryGroundPress(out var screen))
            {
                // T031：落地面寻路前先问真实射线，而不是"有没有指针悬在 UI 上"这种粗判。
                if (!IsPointerBlockedByUI(screen) && !IsPointerOverWorldAction(screen))
                {
                    var point = ScreenToWorld(screen);
                    if (point != null && RequestTravelTo(point.Value))
                    {
                        worldClickFeedback?.ConfirmGroundClick(point.Value, worldPos, travelRoute, travelRouteIndex);
                        PlayClick();
                    }
                }
            }
            worldClickFeedback?.Follow(worldPos, travelRoute, travelRouteIndex, hasTarget);
        }

        // ---------------- 指针拦截（T031：UI 开着时不许穿透到世界）----------------

        void BuildWorldClickFeedback()
        {
            var rect = ui.Rect(worldRoot, "World click feedback", 0, 0,
                town.WorldWidth * worldScale, town.WorldHeight * worldScale);
            worldClickFeedback = rect.gameObject.AddComponent<WorldClickFeedback>();
            worldClickFeedback.Configure(town, worldScale, () => settings == null || settings.reduceMotion);
        }

        // Ground clicks must not compete with the EventSystem's real NPC/encounter button hit.
        bool IsPointerOverWorldAction(Vector2 screenPosition)
        {
            var events = EventSystem.current;
            if (events == null) return true;
            var hits = new List<RaycastResult>();
            events.RaycastAll(new PointerEventData(events) { position = screenPosition }, hits);
            foreach (var hit in hits)
            {
                var button = hit.gameObject != null ? hit.gameObject.GetComponentInParent<Button>() : null;
                if (button != null && button.isActiveAndEnabled && worldRoot != null &&
                    button.transform.IsChildOf(worldRoot)) return true;
            }
            return false;
        }

        /// <summary>这个屏幕点是否被"世界层之外的 UI"挡住。
        /// 判据用 EventSystem 的真实射线（RaycastAll），不用"modal 是否为空"这类猜测：
        /// 只有射线知道此刻这个像素上压着几层、谁在最上面，背包/设置/全图浮层谁开着都一样拦得住。
        /// 世界层（worldRoot 子树）自己不算阻挡——点地面要能走、点实体要能选中。</summary>
        bool IsPointerBlockedByUI(Vector2 screenPosition)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null) return false;
            var data = new PointerEventData(eventSystem) { position = screenPosition };
            var results = new List<RaycastResult>();
            eventSystem.RaycastAll(data, results);
            foreach (var hit in results)
            {
                var go = hit.gameObject;
                if (go == null) continue;
                if (worldRoot != null && go.transform.IsChildOf(worldRoot)) continue;
                var graphic = go.GetComponent<Graphic>();
                if (graphic != null && graphic.raycastTarget) return true;
            }
            return false;
        }

        /// <summary>实体自己的屏幕中心点。
        /// 截图验收会把画布切到 ScreenSpaceCamera（FrontEndShotProps.SetupCapture），
        /// 那时世界坐标 ≠ 屏幕坐标，必须带上画布相机，否则射线打错位置。</summary>
        static Vector2 ScreenCenterOf(RectTransform rect)
        {
            var canvas = rect.GetComponentInParent<Canvas>();
            var world = rect.TransformPoint(rect.rect.center);
            return canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? RectTransformUtility.WorldToScreenPoint(null, world)
                : RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, world);
        }

        /// <summary>中断在途的"走向目标"：被丢弃的到达回调不触发。</summary>
        void CancelTravel()
        {
            if (worldClickFeedback != null) worldClickFeedback.End();
            hasTarget = false;
            travelRoute.Clear();
            travelRouteIndex = 0;
            travelCallback = null;
            travelArrivalCondition = null;
            travelContextId = null;
        }

        /// <summary>键盘接管：作废在途寻路、把方向向量归一化成交给手动移动。
        /// UpdateWorld 的按键分支与验收钩子走的是同一份代码，这样"验的是玩家按下的那条路"
        /// 才不是空话。返回 true = 确实打断了一次在途走向。</summary>
        bool TakeOverFromKeyboard(ref Vector2 move)
        {
            if (move == Vector2.zero) return false;
            var wasTravelling = TravelBusy;
            CancelTravel();
            move.Normalize();
            return wasTravelling;
        }

        /// <summary>验收钩子（T033）：模拟"寻路途中玩家按下了移动键"，
        /// 返回 true = 这一趟走向确实被打断且旧回调已丢弃。</summary>
        public bool TownKeyboardTakeoverForCheck()
        {
            var move = new Vector2(1f, 0f);
            if (!TakeOverFromKeyboard(ref move)) return false;
            return !TravelBusy && travelCallback == null && !hasTarget;
        }

        /// <summary>验收钩子（T031）：某个 NPC 此刻的屏幕点是否被上层 UI 挡住。</summary>
        public bool TownPointerBlockedByUiAt(string npcId) =>
            npcRects.TryGetValue(npcId, out var rect) && rect != null &&
            IsPointerBlockedByUI(ScreenCenterOf(rect));

        /// <summary>验收诊断（T031）：把射线在该 NPC 屏幕点上打到的东西原样吐出来。
        /// 拦截不生效时，"EventSystem 没建"、"画布没注册成 raycaster"、"点算错了"
        /// 这三种根因的修法完全不同，只靠布尔值分不清。</summary>
        public string TownRaycastProbeAt(string npcId)
        {
            if (!npcRects.TryGetValue(npcId, out var rect) || rect == null) return npcId + " 不在场上";
            var eventSystem = EventSystem.current;
            if (eventSystem == null) return "EventSystem.current == null";
            var point = ScreenCenterOf(rect);
            var data = new PointerEventData(eventSystem) { position = point };
            var hits = new List<RaycastResult>();
            eventSystem.RaycastAll(data, hits);
            var text = new System.Text.StringBuilder();
            text.Append("point=").Append(point)
                .Append(" raycasters=").Append(UnityEngine.Object.FindObjectsByType<BaseRaycaster>(
                    FindObjectsSortMode.None).Count(r => r.isActiveAndEnabled))
                .Append(" hits=").Append(hits.Count).Append(' ');
            for (var i = 0; i < hits.Count; i++)
            {
                var go = hits[i].gameObject;
                text.Append('[').Append(go.name)
                    .Append(" g=").Append(go.GetComponent<Graphic>() != null)
                    .Append(" rt=").Append(go.GetComponent<Graphic>() != null && go.GetComponent<Graphic>().raycastTarget)
                    .Append(" world=").Append(worldRoot != null && go.transform.IsChildOf(worldRoot))
                    .Append(']');
            }
            return text.ToString();
        }


        const float WalkSpeedWorld = 260f, RunSpeedWorld = 470f;
        // MAT-11：起步约 0.10s 到走速（260/2600）；收步略快，停得干脆不拖泥带水。
        const float SpeedAccel = 2600f, SpeedDecel = 3400f;

        // 画布坐标 → 世界坐标（点地移动用）
        Vector2? ScreenToWorld(Vector2 screenPoint)
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    content, screenPoint,
                    content.GetComponentInParent<Canvas>().renderMode == RenderMode.ScreenSpaceOverlay
                        ? null : content.GetComponentInParent<Canvas>().worldCamera, out var local))
            {
                // content 局部 → 世界（注意 y 轴翻转）
                var wx = (local.x - camPos.x) / worldScale;
                var wy = town.Elevation.UnprojectY(wx, -(local.y - camPos.y) / worldScale);
                if (wx < 0f || wy < 0f || wx > town.WorldWidth || wy > town.WorldHeight) return null;
                return new Vector2(wx, wy);
            }
            return null;
        }

        Vector2 TryMove(Vector2 delta)
        {
            if (WorldDialogueInputBlocked) return Vector2.zero;
            var next = worldPos + delta;
            if (CanTraverseWorldPlayer(worldPos, next)) { worldPos = next; return delta; }
            if (CanTraverseWorldPlayer(worldPos, new Vector2(next.x, worldPos.y)))
            {
                var slid = new Vector2(next.x - worldPos.x, 0f);
                worldPos = new Vector2(next.x, worldPos.y);
                return slid;
            }
            if (CanTraverseWorldPlayer(worldPos, new Vector2(worldPos.x, next.y)))
            {
                var slid = new Vector2(0f, next.y - worldPos.y);
                worldPos = new Vector2(worldPos.x, next.y);
                return slid;
            }
            return Vector2.zero;   // 三面都不通 = 一步没挪：步态相位不许推进
        }

        // P3：素材只有正/侧/背三向（左侧共用侧身图），斜向按主轴吸附到最近的一档。
        // 换向只换贴图不重建关节（重建会丢相位，走两步就抽一下）；左右只翻 uv，不做 180° 硬切。
        void UpdateTravelerFacing(Vector2 move)
        {
            if (move == Vector2.zero || travelerPuppet == null) return;
            // Keep the current axis in a small diagonal band, so steering near 45 degrees cannot flicker views.
            float horizontal = Mathf.Abs(move.x), vertical = Mathf.Abs(move.y);
            bool sideways = travelerView == 1 ? horizontal >= vertical * .82f : horizontal > vertical * 1.18f;
            var view = sideways ? 1 : move.y > 0f ? 0 : 2;   // 世界 y 越大越靠南（屏幕越下）= 正面朝观察者
            if (activeSave != null) activeSave.facing = view == 1 ? (move.x < 0 ? 3 : 1) : view == 2 ? 2 : 0;
            if (view != travelerView)
            {
                travelerView = view;
                travelerPuppet.SetView(TravelerArt(view));
                travelerPuppet.SetLegSplit(view != 1);
                if (travelerUseWalksheet)
                    ApplyTravelerWalkSheet(view);
            }
            // 只有侧身图需要镜像：正/背两向本身没有左右，镜像会把发髻、背篓这类不对称处翻错边。
            travelerPuppet.SetMirror(view == 1 && move.x < 0f);
        }

        bool CanStand(Vector2 point) => town.CanStand(point.x, point.y);

        // 纵深：玩家按世界 Y 与各实体排序，保证走到谁后面就被谁挡住（二期建筑接入点）。
        void UpdateDepthOrder()
        {
            if (traveler == null) return;
            // 物件层重摆/页面重建会销毁旧 placeable 的 Rect：先清掉死引用，
            // 否则它们的 BaseY 幽灵会挤歪插队位。
            placeables.RemoveAll(p => p.Rect == null);
            placeables.Sort((a, b) => a.BaseY.CompareTo(b.BaseY));
            // 独立水面紧接地块，所有脚底排序实体均排在它之后。
            var backgroundCount = chunkRects.Count + (townWater != null ? 1 : 0) + (worldClickFeedback != null ? 1 : 0);
            // T026 根治：排序结果写回兄弟序（创建顺序从此无关）。
            // 注意：写回会把夹在中间的玩家平移——所以玩家的插队位不能用
            // "上次目标没变就短路"的历史光标，必须按当前 index 自愈。
            for (var i = 0; i < placeables.Count; i++)
            {
                var expected = backgroundCount + i;
                if (placeables[i].Rect.GetSiblingIndex() != expected)
                    placeables[i].Rect.SetSiblingIndex(expected);
            }
            var slot = placeables.FindIndex(p => p.BaseY > town.Elevation.ProjectY(worldPos.x, worldPos.y));
            if (slot < 0) slot = placeables.Count;
            var desired = slot + backgroundCount;
            if (traveler.GetSiblingIndex() != desired)
                traveler.SetSiblingIndex(desired);
            UpdateOcclusionLayerOrder();
        }

        // ---------------- 纵深实体接口（二期：房屋/树/NPC 统一走这里）----------------

        /// <summary>在世界坐标处放一个"脚底锚点"的立绘对象，并纳入纵深排序。
        /// sortBaseY 可选：纵深排序键，不传 = footPoint.y + height（立绘底边，统一底边口径）。
        /// 注意传入点本身仍是**顶边中点**（pivot=(.5,1) + anchoredPosition=(x,-y)，立绘占世界 y ∈ [y, y+height]），
        /// 排序键与摆放锚点是两回事：排序键统一取立绘底边/墙脚（footPoint.y + height），
        /// 这样子站进立绘下半部时会被立绘遮住，而不是永远盖在整栋楼前面。</summary>
        public RectTransform PlaceInWorld(string id, Vector2 footPoint, float width, float height, Texture2D art = null,
            float sortBaseY = float.NaN, bool frameCamera = false)
        {
            if (worldRoot == null) return null;
            var baseY = float.IsNaN(sortBaseY) ? footPoint.y + height / worldScale : sortBaseY;
            var groundHeight = town.Elevation.HeightAt(footPoint.x, baseY);
            var rect = ui.Rect(worldRoot, "Placeable " + id,
                footPoint.x * worldScale, (footPoint.y - groundHeight) * worldScale, width, height);
            rect.pivot = new Vector2(.5f, 1f);   // Base Point 在脚底
            if (art != null)
            {
                var image = rect.gameObject.AddComponent<RawImage>();
                image.texture = art;
                image.raycastTarget = false;
            }
            placeables.Add(new Placeable {
                Rect = rect, BaseY = baseY - groundHeight, Id = id, FrameCamera = frameCamera });
            return rect;
        }

        // Ground decoration lives inside the existing ground-feedback layer: no added depth-sort entry or blocker.
        public RectTransform PlaceBuildingGroundContact(string id,Vector2 top,float width,float height,float groundY,Vector4[] supports)
        {
            if(worldClickFeedback==null||town==null)return null;
            float elevation=town.Elevation.HeightAt(top.x,groundY);
            var rect=ui.Rect(worldClickFeedback.transform,"Ground contact "+id,top.x*worldScale,(top.y-elevation)*worldScale,width,height);
            rect.pivot=new Vector2(.5f,1);rect.gameObject.AddComponent<BuildingGroundContact>().Configure(supports);return rect;
        }

        // ---------------- 走向目标（P-02：点谁走过去）----------------

        // ---- T112：reach 类目标的运行期生产者 ----
        // Core 的 QuestRuntime 只认 QuestSignal.ReachedPlaceId，而在此之前世界里没有任何地方
        // 记录"到过哪里"（save.location 只在建档时写一次），所以"桥边之约"那一条永远推不上。
        // 判据是位置本身：玩家进入地标半径就算到过。换档/换页不重复发（lastReachedPlace 去重），
        // 但**走过又走回来算第二次到访**——那正是"赴约"这件事的语义。
        const float ReachRadius = 96f;
        readonly HashSet<string> reachedPlaces = new HashSet<string>();

        static bool InReachOf(MapLandmark mark, Vector2 at)
        {
            var dx = mark.X - at.x;
            var dy = mark.Y - at.y;
            return dx * dx + dy * dy <= ReachRadius * ReachRadius;
        }

        void ReportTravelReach()
        {
            if (town == null || activeSave == null) return;
            var stillThere = new HashSet<string>();
            foreach (var mark in town.Landmarks)
            {
                if (!InReachOf(mark, worldPos)) continue;
                stillThere.Add(mark.Id);
                if (reachedPlaces.Add(mark.Id))          // 第一次进这个地标的圈才发信号
                    DriveQuests(new QuestSignal { ReachedPlaceId = mark.Id });
            }
            // 走开了就忘掉：再走回来还得算一次到访。
            // 之前只记"最后一个"，而双桥与河北岸两个地标的圈是**重叠的**——
            // 站到桥上先撞上 river-north，bridges 就永远发不出信号（首轮实跑就是这么红的）。
            reachedPlaces.IntersectWith(stillThere);
        }

        /// <summary>验收钩子：此刻玩家是否落在某个地标的到访半径内（T112 的取证用它点名地点）。</summary>
        public string ReachedPlaceForCheck
        {
            get
            {
                if (town == null) return null;
                var hit = town.Landmarks.Where(mark => InReachOf(mark, worldPos)).Select(mark => mark.Id);
                var all = string.Join(",", hit.ToArray());
                return all.Length == 0 ? null : all;
            }
        }

        /// <summary>是否还有"走向目标"在途（验收轮询用）。</summary>
        public bool TravelBusy => hasTarget && travelCallback != null;

        /// <summary>
        /// 让玩家走向目标点（世界坐标）；到达（阈值内）后回调 onArrived(contextId)。
        /// 返回 false = 目标不可达（不可站立/世界未加载）。复用点击地面的移动路径，
        /// 不另做移动逻辑；WASD 或新的请求会顶掉在途目标，被顶掉的旧回调不触发（防串话）。
        /// </summary>
        readonly List<Vector2> travelRoute = new List<Vector2>();
        int travelRouteIndex;

        public bool RequestTravelTo(Vector2 worldTarget, System.Action<string> onArrived = null, string contextId = null,
            System.Func<bool> arrivalCondition = null)
        {
            if (WorldDialogueInputBlocked || modal != null || bagOverlay != null || worldRoot == null || town == null) return false;
            if (!CanStand(worldTarget)) return false;
            var route=TownNavigator.FindPath(town,worldPos.x,worldPos.y,worldTarget.x,worldTarget.y);
            if(route==null || route.Count==0)return false;
            if (worldClickFeedback != null) worldClickFeedback.End();
            travelRoute.Clear();
            foreach(var point in route)travelRoute.Add(new Vector2(point.x,point.y));
            travelRouteIndex=0;
            walkTarget=travelRoute[0];
            hasTarget = true;
            travelCallback = onArrived;
            travelArrivalCondition = arrivalCondition;
            travelContextId = contextId;
            return true;
        }

        /// <summary>目标点旁的可站立位置（优先正南 = 面向观察者），全被占则回退目标点本身。
        /// NPC 的脚底点被它自己的阻挡半径占着，走向它要落到旁边。</summary>
        Vector2 ArrivalPointNear(Vector2 center, float clearRadius)
        {
            var candidates = new[]
            {
                center + new Vector2(0f, clearRadius),
                center + new Vector2(0f, -clearRadius),
                center + new Vector2(clearRadius, 0f),
                center + new Vector2(-clearRadius, 0f),
            };
            foreach (var point in candidates)
                if (CanStand(point)) return point;
            return center;
        }

        // ---------------- NPC 与遭遇点（P-02 摆放 / P-03 作息与讨伐闭环）----------------
        // 数据来自 town-map.json 的 npcs / encounters 段（铁律二：不写死）。
        // NPC 的"此刻在哪"来自 npc-schedules.json + Core 的 ScheduleRules：
        // 表现层不自己抄一份时间判断，两处判据迟早会打架。

        readonly List<RectTransform> encounterRings = new();
        readonly Dictionary<string, RectTransform> encounterMarkers = new();

        /// <summary>默认金环色（改之前三只妖全是这个色，就是 T069 的靶）。</summary>
        static readonly Color EncounterDefaultTint = new Color(1f, .78f, .25f, .8f);

        /// <summary>配置里的 tint 是 "r,g,b"（0~1）。写错不静默吞：报一次再用默认色。</summary>
        static Color EncounterTint(string tint)
        {
            if (string.IsNullOrEmpty(tint)) return EncounterDefaultTint;
            var parts = tint.Split(',');
            if (parts.Length != 3)
            {
                Debug.LogWarning("遭遇 tint 应是三个 0~1 的数（实际「" + tint + "」），先用默认金色。");
                return EncounterDefaultTint;
            }
            float r, g, b;
            if (!float.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out r) ||
                !float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out g) ||
                !float.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out b))
            {
                Debug.LogWarning("遭遇 tint 里有不是数的分量（实际「" + tint + "」），先用默认金色。");
                return EncounterDefaultTint;
            }
            return new Color(r, g, b, .85f);
        }

        /// <summary>验收钩子：这个遭遇点的标记物体（T069 要按它比对颜色与形状）。</summary>
        public RectTransform EncounterMarkerForShot(string encounterId) =>
            encounterId != null && encounterMarkers.TryGetValue(encounterId, out var rect) ? rect : null;
        readonly Dictionary<string, RectTransform> npcRects = new();

        /// <summary>作息表跟着章节表一起懒加载（FrontEndChapter.LoadTables 负责注入
        /// ChapterLedger.Schedule）。新档直接进镇时章节面板还没开过，表就是空的——
        /// 那时货郎永远钉在常驻点上，"作息驱动"会静默失效。读一次 TablesReady 把它拉起来，
        /// 判据仍然只有 Core 那一份，不在这里另建一条加载路径。</summary>
        NpcScheduleTable WorldSchedules
        {
            get
            {
                if (ChapterLedger.Schedule == null) _ = TablesReady;
                return ChapterLedger.Schedule;
            }
        }

        NpcScheduleDef ScheduleFor(string npcId) => string.IsNullOrEmpty(npcId) ? null
            : WorldSchedules?.Schedules.FirstOrDefault(s => s.NpcId == npcId);

        /// <summary>NPC 常驻点归属哪个地标（离它最近的那个）。用来判断作息窗口是不是
        /// 又指回了他自己的家——是的话别按地标中心重摆。</summary>
        string HomePlaceOf(MapNpc npc)
        {
            var home = new Vector2(npc.X, npc.Y);
            var nearest = "";
            var best = float.MaxValue;
            foreach (var landmark in town.Landmarks)
            {
                var d = Vector2.SqrMagnitude(new Vector2(landmark.X, landmark.Y) - home);
                if (d < best) { best = d; nearest = landmark.Id; }
            }
            return nearest;
        }

        /// <summary>NPC 此刻应站的世界坐标：在作息窗口里就落到该窗口的地标，
        /// 不在任何窗口（或没有作息表）时回退地图上的常驻点。
        /// 窗口指回他自己家时保留常驻点：town-map 里的 X/Y 是地形线标定的**精确摊位**，
        /// 而地标是广场中心（market 地标 3100,4950 与货郎 3245,4820 差 195 单位），
        /// 按地标重摆会把作者标好的点位覆盖成中心点。</summary>
        Vector2 NpcStandingAt(MapNpc npc)
        {
            var home = new Vector2(npc.X, npc.Y);
            var window = ScheduleFor(npc.Id)?.At(activeSave?.WorldTimeNow()?.minuteOfDay ?? 0);
            if (window == null || window.PlaceId == HomePlaceOf(npc)) return home;
            var landmark = town.Landmarks.FirstOrDefault(l => l.Id == window.PlaceId);
            return landmark == null ? home : new Vector2(landmark.X, landmark.Y);
        }

        void BuildTownNpcs()
        {
            encounterRings.Clear();
            encounterMarkers.Clear();
            npcRects.Clear();
            foreach (var npc in town.Npcs)
            {
                var independent = TownCharacterArt.Find(npc.Id);
                var art = independent == null ? null : TownCharacterArt.Texture(independent.stand);
                if (art == null) art = string.IsNullOrEmpty(npc.Art) ? null : Resources.Load<Texture2D>(npc.Art);
                if (art == null)
                    Debug.LogError("NPC 立绘缺失：Resources/" + npc.Art + "（" + npc.Id + "）。");
                var height = npc.Height > 0f ? npc.Height : 170f;
                // 宽按立绘比例；立绘缺失时按 0.55 的瘦高比占位。
                var width = art != null ? height * (art.width / (float)art.height) : height * .55f;
                // ADR-018：PlaceInWorld 的传入点是**顶边中点**，占地 [y, y+h]。
                // NPC 立在 (X,Y)（脚底/阻挡圆心），画带应是 [Y-h, Y] → 传 Y-h，别把人摆进地里。
                var foot = NpcStandingAt(npc);
                var rect = PlaceInWorld("npc-" + npc.Id, new Vector2(foot.x, foot.y - height), width, height, art);
                if (rect == null) continue;
                if (independent != null && TownCharacterArt.Texture(independent.walk) != null)
                    rect.gameObject.AddComponent<NpcFrameAnimator>().Configure(independent, () => settings == null || settings.reduceMotion);
                npcRects[npc.Id] = rect;
                npcPatrolHeight[npc.Id] = height;
                if (placeables.Count > 0) npcPatrolPlaceables[npc.Id] = placeables[placeables.Count - 1];
                MakeClickable(rect, "走到 " + npc.Name, () => RequestNpcDialogue(npc.Id));
            }
            BuildWilderness();
        }

        // ---------------- 验收钩子（作息）----------------

        /// <summary>验收用：把存档里的世界时间拨到指定分钟（0–1439），并让 NPC 按新时刻重新落位。
        /// 走 Core 的 SetWorldTime，不在表现层伪造时间。</summary>
        public bool SetWorldMinuteForCheck(int minuteOfDay)
        {
            if (activeSave == null) return false;
            activeSave.SetWorldTime(new WorldTime { day = activeSave.worldDay, minuteOfDay = minuteOfDay });
            if (page == "town") ShowTown();
            return true;
        }

        /// <summary>验收用：某 NPC 此刻的脚底世界坐标；地图上没有这位则返回 null。</summary>
        public Vector2? TownNpcFootPosition(string npcId)
        {
            var npc = town?.Npcs.FirstOrDefault(n => n.Id == npcId);
            return npc == null ? null : NpcLiveFoot(npc);
        }

        /// <summary>验收用：这位 NPC 此刻是否在可交易窗口内（收摊了就该给"明日请早"）。</summary>
        public bool TownNpcOnDuty(string npcId) =>
            ScheduleRules.IsOnDuty(activeSave?.WorldTimeNow(), WorldSchedules, npcId);

        /// <summary>验收用：地图上还剩几个没被打掉的遭遇点。</summary>
        public int TownLiveEncounterCount => town == null ? 0 :
            town.Encounters.Count(e => activeSave == null || !activeSave.HasFlag(e.Id));

        /// <summary>给 placeable 挂点击：raycast 命中 → 走过去 → 到达后 raise 事件。
        /// 点击落地前先过 T031 的射线门：上层 UI（对话/商店/全图/背包）盖住这个实体时，
        /// 点它等于点 UI，不许再向世界发移动指令。</summary>
        void MakeClickable(RectTransform rect, string context, System.Action onClick)
        {
            var button = rect.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() =>
            {
                // 判据用实体自己的屏幕点而不是鼠标位置："我被 UI 盖住了吗"本来就该问我的位置，
                // 而且验收直调 onClick 时并没有真实鼠标。
                if (IsPointerBlockedByUI(ScreenCenterOf(rect)))
                {
                    Debug.Log("[FrontEndWorld] 点击 " + rect.name + " 被上层 UI 拦截（" + context + "）");
                    return;
                }
                Debug.Log("[FrontEndWorld] 点击 " + rect.name + "（" + context + "）");
                onClick();
            });
            // PlaceInWorld 把图形的 raycastTarget 关了（地面点击要穿过它们），
            // 可点的实体要重新打开——点不中就没有"点谁走过去"。
            foreach (var graphic in rect.GetComponents<Graphic>())
                graphic.raycastTarget = true;
        }

        // ---------------- 验收钩子 ----------------

        /// <summary>玩家是否在视口内（摄像机边界正确性）。</summary>
        public bool PlayerOnScreen()
        {
            if (worldRoot == null) return false;
            var view = camPos + new Vector2(worldPos.x * worldScale, -town.Elevation.ProjectY(worldPos.x, worldPos.y) * worldScale);
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
        // T100：删掉 `TerrainZoneCount`。它统计的其实是 blocked 的条数（2 万个 tile 矩形），
        // 名字却写着"地形分区数"，而且全仓零调用点——一个名字撒谎、又没人用的读数，
        // 留着只会被下一个人当成"分区"来断言。
        /// <summary>验收用：小地图底图（正式世界总览）是否真加载到了。
        /// 底图缺失时小地图会静默留白，肉眼在整屏截图里看不出来，只能显式断言。</summary>
        public bool WorldOverviewArtLoaded => worldOverview != null;

        // ---- NPC 走位钩子（P-02 声明，本轮 M6.5 真实生效：以前是恒 false 的桩）----
        public bool TownWalkingToNpc => TravelBusy;

        /// <summary>把人物放到 NPC 跟前（基座下方、更近的一侧）。</summary>
        public bool SnapTravelerNextToNpc(string npcId)
        {
            var npc = FindNpc(npcId);
            if (npc == null) return false;
            // 落点必须与"点击 NPC"算的是同一个点：钩子只把人搬到这趟该到的位置，
            // 到达判定与搭话回调仍走 Update 里那条真实路径（不为验收另开一条到达逻辑）。
            var spot = ArrivalPointNear(NpcLiveFoot(npc), npc.Radius + 36f);
            return SnapTravelerTo(spot.x, spot.y);
        }

        /// <summary>把人物放到 NPC 画带顶边以北（更远＝更先画），用于验遮挡逆序。</summary>
        public bool SnapTravelerAboveNpc(string npcId)
        {
            var npc = FindNpc(npcId);
            if (npc == null) return false;
            var foot = NpcLiveFoot(npc);
            var height = npc.Height > 0f ? npc.Height : 170f;
            // ADR-018：NPC 占地 [foot.y-height, foot.y]；y 越小越靠北＝越远＝画在后面。
            // 站在顶边以北 AboveNpcGap，才是"从 NPC 背后绕过去"， sibling 应比 NPC 小。
            var aboveY = foot.y - height - AboveNpcGap;
            foreach (var offset in new[] { 0f, -40f, 40f, -80f, 80f })
                if (CanStand(new Vector2(foot.x + offset, aboveY)))
                    return SnapTravelerTo(foot.x + offset, aboveY);
            return false;
        }

        const float AboveNpcGap = 24f;

        MapNpc FindNpc(string npcId) => town?.Npcs.FirstOrDefault(n => n.Id == npcId);

        /// <summary>验收定位钩子：世界里这个 NPC 的物体（含 Button）。
        /// 夹具不该再按猜的物体名去找人——命名规则一改，它就静默找不到（T092）。</summary>
        public Button NpcButtonForShot(string npcId) =>
            npcId != null && npcRects.TryGetValue(npcId, out var rect) && rect != null
                ? rect.GetComponent<Button>() : null;

        /// <summary>验收定位钩子：世界页主角的 RectTransform（纸偶根）。
        /// 夹具一律按这个显式钩子取人，不许 FindAnyObjectByType——它会跨页误命中角色预览样张，
        /// 历史上就是这样假绿的（T092）。</summary>
        public RectTransform TravelerRectForShot => traveler;

        /// <summary>验收定位钩子：世界页主角这台纸偶本体。P1 之前城镇页只有绿胶囊、
        /// 没有 PuppetActor，所以"城镇页应有分层纸偶角色"这条断言长期红（T091）。
        /// 取不到就返回 null 让夹具报红，这里不兜底。</summary>
        public PuppetActor TravelerPuppetForShot => travelerPuppet;

        /// <summary>验收：主角此刻的朝向视图（0 正 / 1 侧 / 2 背）与是否左右镜像。</summary>
        public int TravelerViewForShot => travelerView;

        /// <summary>遭遇金环的脉动动画：sin 缩放，别让"可交互"靠玩家猜。</summary>

        /// <summary>验收钩子：把玩家放到指定世界坐标（限可走区）。</summary>
        public bool SnapTravelerTo(float x, float y)
        {
            if (!CanStand(new Vector2(x, y))) return false;
            worldPos = new Vector2(x, y);
            SyncTraveler();
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
