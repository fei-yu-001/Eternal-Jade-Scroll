using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TwelveJade.Core;
using TwelveJade.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Editor
{
    /// <summary>
    /// 青石镇场景与物件全维视觉夹具（员工 I · I-World-2.0）。
    ///
    /// 与 FrontEndShotProps 的分工：那个夹具管"交互与玩法链路走不走得通"，
    /// 这个管"场景摆得对不对、画出来成不成样子、三种分辨率下会不会露底、
    /// 物件数量会不会把渲染批次顶爆"。两边各自演进，互不替换。
    ///
    /// 执行：
    ///   Unity.exe -batchmode -projectPath &lt;项目&gt;
    ///             -executeMethod TwelveJade.Editor.TerrainPropShot.RunSuite
    ///             -logFile Tools/logs/terrain-props.log
    /// 收尾输出：[TerrainPropShot] suite finished, N problem(s)，0 才算过。
    ///
    /// 为什么不用 -quit：批量模式下本夹具自己调 EditorApplication.Exit，
    /// 命令行加 -quit 会在 executeMethod 返回时就退出，跨帧相位根本跑不完
    /// （P-02 实测踩过：日志体积从 100KB 掉到 19KB 且没有 finished 行）。
    /// </summary>
    [InitializeOnLoad]
    public static class TerrainPropShot
    {
        const string SessionKey = "TerrainPropShot.Active";

        // 任务书 I6.1 要求的三个视口。注意三者都是 16:9，这不是偷懒：CanvasScaler 用
        // referenceResolution 1920×1080 + Expand，16:9 下逻辑画布恒为 1920×1080 画布单位，
        // 分辨率之间只差像素密度。所以"每个尺寸各自不露底"三条其实是同一个几何问题，
        // 真正能抓住分辨率适配回归的是第四条：高分辨率图缩回基准尺寸后必须与基准图一致。
        static readonly (string name, int width, int height)[] Viewports =
        {
            ("1080p", 1920, 1080),
            ("2K", 2560, 1440),
            ("4K", 3840, 2160),
        };

        // ADR-026 性能条款：合批后渲染批次峰值 ≤ 45。
        const int DrawCallBudget = 45;
        // 画面边缘一圈（各边 4px）里"仍是相机清屏底色"的像素占比上限——
        // 非零即说明世界没有铺满这块画面（镜头越界 / 地块摆到世界外）。
        const float EdgeUncoveredTolerance = 0.02f;
        // 判"还是底色"用的逐通道容差。取小值：世界贴图本身也可能偏暗，
        // 容差一大就会把深色水面误判成"没画"。
        const int ClearColorEpsilon = 4;
        // 4K/2K 缩回 1080p 后与 1080p 原图的平均亮度差上限。纯重采样应当很小，
        // 布局在某分辨率下跑偏会在边缘产生大差值。
        const float DownsampleDiffTolerance = 24f;

        // 与 Tools/PropChecks/check-plot-overlap.py 同口径（判据 6）：
        // 大型 kind 之间的地面根基间隙不得小于这个数，prop 豁免。
        const float MinCorridor = 60f;
        const float DefaultBaseDepth = 24f;
        static readonly HashSet<string> MajorKinds = new HashSet<string>(StringComparer.Ordinal)
        { "building", "tree", "stall" };

        static FrontEndController controller;
        static WorldPropLayer propLayer;
        static Camera camera;
        static Canvas rootCanvas;
        static CanvasScaler canvasScaler;
        static string shotsDir;
        static readonly List<string> problems = new List<string>();
        static readonly List<string> notes = new List<string>();
        static int okCount;
        static float runStart;
        static float phaseStart;
        static int frames;
        static Phase phase;
        static int slot;
        static int tickBase;
        static int leakBaseline = -1;
        static int sweepIndex;
        static int sceneShots;
        static int anchorIndex;
        static int anchorShots;

        /// <summary>M2（ADR-030 八景重锚）专用取景：按**景名**取景，而不是按地标。</summary>
        static readonly (string label, string plotId)[] SceneAnchors =
        {
            ("m-景-南门牌坊", "plot-paifang"),
            ("m-景-北山药圃", "plot-herbal-garden"),
            ("m-景-后山苍松", "plot-mountain-pine"),
        };
        static bool missingArtInjected;

        /// <summary>building-plots.json 的本地解析结果（按 footY 升序，与文件约定一致）。
        /// WorldPropLayer 的 Plot 是私有嵌套类、而且不读 radius，所以夹具自己解析：
        /// 断言要打在"配置写了什么"上，而不是"运行时的物件层恰好读了什么"。</summary>
        sealed class PlotSpec
        {
            public string Id;
            public string Kind;
            public string Art;
            public float FootX, FootY, Width, Height, Radius;
            /// <summary>MAP-11：覆盖不透明内容带的实测深度（与 WorldPropLayer /
            /// check-plot-overlap 读同一个字段）。0 = 未填，走 radius 回退。</summary>
            public float CollisionDepth;
            /// <summary>MAP-12：首个不透明行的实测距离（与 WorldPropLayer /
            /// check-plot-overlap 读同一个字段）。0 = 未填，走图顶旧口径。</summary>
            public float CollisionTop;
            /// <summary>这块地挡不挡人（与 WorldPropLayer / check-plot-overlap 读同一个字段，T067）。</summary>
            public bool Blocks = true;

            public bool Major => MajorKinds.Contains(Kind);

            /// <summary>地面根基带（玩家真正会撞上的那一条）＝校验器 base_rect 同式：
            /// [footX-w/2, footX+w/2] × [footY+baseTop, footY+baseDepth]；baseDepth =
            /// collisionDepth>0 ? collisionDepth : radius>0 ? radius×2 : 24，baseTop =
            /// collisionTop>0 ? collisionTop : 0，世界单位。</summary>
            public Rect BaseRect()
            {
                var half = Width / 2f;
                var depth = CollisionDepth > 0f ? CollisionDepth
                    : Radius > 0f ? Radius * 2f : DefaultBaseDepth;
                var top = CollisionTop > 0f ? CollisionTop : 0f;
                return RectMake(FootX - half, FootY + top, half * 2f, depth - top);
            }

            /// <summary>画带（PlaceInWorld 的实际几何：传入的点是顶边中点，画面向下延伸 height）。
            /// ＝校验器 art_rect 同式：height 是画布像素，除以 worldScale 才换算成世界单位。</summary>
            public Rect ArtRect(float worldScale)
            {
                var half = Width / 2f;
                return RectMake(FootX - half, FootY, half * 2f, Height / worldScale);
            }
        }

        static readonly List<PlotSpec> specs = new List<PlotSpec>();

        enum Phase
        {
            Idle, EnterTown, Instantiate, DepthOrder, DepthMove, Corridors,
            Viewports, SceneSweep, SceneAnchors, Polish, ColliderWire, ColliderRestore, Batching, LeakWait, BadConfig, Recover, MissingArt, Done
        }

        static TerrainPropShot()
        {
            if (!SessionState.GetBool(SessionKey, false)) return;
            shotsDir = SessionState.GetString(SessionKey + ".Dir", "");
            runStart = Time.realtimeSinceStartup;
            Attach();
        }

        public static void RunSuite()
        {
            SessionState.SetBool(SessionKey, true);
            var project = Directory.GetParent(Application.dataPath).FullName;
            var repoRoot = Directory.GetParent(Directory.GetParent(project).FullName).FullName;
            shotsDir = Path.Combine(repoRoot, "Tools", "screenshots");
            Directory.CreateDirectory(shotsDir);
            // 验收绝不写进玩家真实存档：与 FrontEndShot / CombatShot 同一套临时目录约定。
            var scratch = Path.Combine(Path.GetTempPath(), "terrain-prop-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            Environment.SetEnvironmentVariable("TWELVEJADE_SAVEDIR", scratch);
            SessionState.SetString(SessionKey + ".Dir", shotsDir);
            Debug.Log("[TerrainPropShot] scratch saves at " + scratch);
            Attach();
            EditorSceneManager.OpenScene("Assets/Scenes/FrontEnd.unity", OpenSceneMode.Single);
            runStart = Time.realtimeSinceStartup;
            EditorApplication.isPlaying = true;
        }

        static void Attach()
        {
            Application.logMessageReceived -= OnLog;
            EditorApplication.update -= Pump;
            Application.logMessageReceived += OnLog;
            EditorApplication.update += Pump;
        }

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (!EditorApplication.isPlaying) return;
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            if (condition.StartsWith("[Licensing::Module]")) return;
            if (stackTrace != null && stackTrace.Contains("UnityEditor.Search")) return;
            // 坏配置相位里这一条是**被断言的预期行为**：物件层必须喊出来，喊声不算事故。
            if (condition.Contains("[WorldPropLayer]")) return;
            problems.Add(type + ": " + condition);
        }

        static void Pump()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            try { Step(); }
            catch (Exception ex) { Fail("夹具异常: " + ex); }
        }

        static void Step()
        {
            // 4K 一帧就是三千多万字节的 ReadPixels，批量模式下比常规截图慢得多；看门狗给到 900 秒。
            if (Time.realtimeSinceStartup - runStart > 900f) { Fail("超时：900 秒未跑完"); return; }
            if (!EditorApplication.isPlaying) return;
            if (controller == null)
            {
                controller = UnityEngine.Object.FindAnyObjectByType<FrontEndController>();
                if (controller == null) return;
                SetupCapture();
                Debug.Log("[TerrainPropShot] play mode ready");
            }
            if (propLayer == null) propLayer = UnityEngine.Object.FindAnyObjectByType<WorldPropLayer>();

            frames++;
            switch (phase)
            {
                case Phase.Idle:
                    Check(propLayer != null, "物件层 WorldPropLayer 应已自举挂载");
                    specs.Clear();
                    specs.AddRange(ParseSpecs());
                    Check(specs.Count >= 24, "building-plots.json 应至少有 24 块地块（任务书 I4）",
                        "实际 " + specs.Count);
                    slot = FreeSlot();
                    Check(slot > 0, "应有空档位供验收使用");
                    controller.ShowCharacterCreation(slot);
                    Check(controller.CreateFromDraft(slot) != null, "建档成功");
                    controller.ShowTown();
                    Check(controller.CurrentPage == "town", "城镇页应已打开");
                    Next(Phase.EnterTown);
                    return;

                case Phase.EnterTown:
                    // 物件层是每帧轮询摆放的（世界根建好才摆），等它的心跳落地。
                    if (!WaitedTicks(12))
                    {
                        if (TimedOut(120f)) { Check(false, "物件层 120 秒未完成摆放"); Next(Phase.Done); }
                        return;
                    }
                    Check(propLayer != null && !propLayer.ConfigFailedForCheck,
                        "正式配置不应被判定为坏配置",
                        "gate=" + (propLayer == null ? "物件层缺失" : propLayer.LastGateForCheck));
                    Check(propLayer != null && propLayer.PlacedForCheck, "物件层应已完成一轮摆放");
                    Next(Phase.Instantiate);
                    return;

                case Phase.Instantiate:
                    CheckInstantiate();
                    Next(Phase.DepthOrder);
                    return;

                case Phase.DepthOrder:
                    // CheckDepthOrder 自己会置位到第二观测点；返回 true 表示还要再验一轮。
                    Next(CheckDepthOrder() ? Phase.DepthMove : Phase.Corridors);
                    return;

                case Phase.DepthMove:
                    // 置位后要等排序真正落到 sibling 上（UpdateDepthOrder 在玩家循环里跑）。
                    if (!WaitedTicks(4))
                    {
                        if (TimedOut(60f)) { Check(false, "移动置位后 60 秒未见排序心跳推进"); Next(Phase.Corridors); }
                        return;
                    }
                    Debug.Log("[TerrainPropShot] 第二观测点置位到 " + moveTarget);
                    CheckPlayerInsertion("移动后");
                    Next(Phase.Corridors);
                    return;

                case Phase.Corridors:
                    CheckCorridorsAndWater();
                    Next(Phase.Viewports);
                    return;

                case Phase.Viewports:
                    CheckViewports();
                    Next(Phase.SceneSweep);
                    return;

                case Phase.Polish:
                    CheckWorldPolish();
                    return;

                case Phase.ColliderWire:
                    CheckColliderWire();
                    return;

                case Phase.ColliderRestore:
                    CheckColliderRestore();
                    return;

                case Phase.SceneSweep:
                    CheckSceneSweep();
                    return;

                case Phase.SceneAnchors:
                    CheckSceneAnchors();
                    return;

                case Phase.Batching:
                    CheckBatching();       // 内部切到 LeakWait（重建要等心跳落地才能比数）
                    return;

                case Phase.LeakWait:
                    CheckLeak();
                    return;

                case Phase.BadConfig:
                    CheckBadConfig();
                    Next(Phase.Recover);
                    return;

                case Phase.Recover:
                    if (!WaitedTicks(12))
                    {
                        if (TimedOut(120f)) { Check(false, "坏配置取证后 120 秒仍未恢复正式摆放"); Next(Phase.Done); }
                        return;
                    }
                    CheckRecovery();
                    Next(Phase.MissingArt);
                    return;

                case Phase.MissingArt:
                    CheckMissingArt();
                    return;

                default:
                    Finish();
                    return;
            }
        }

        static void Next(Phase next)
        {
            phase = next;
            frames = 0;
            phaseStart = Time.realtimeSinceStartup;
            tickBase = propLayer == null ? 0 : propLayer.UpdateTicksForCheck;
        }

        /// <summary>等够 n 次物件层 Update（玩家循环心跳）——摆放/销毁都真落地了。</summary>
        static bool WaitedTicks(int n) =>
            propLayer != null && propLayer.UpdateTicksForCheck >= tickBase + n;

        static bool TimedOut(float seconds) => Time.realtimeSinceStartup - phaseStart > seconds;

        // ---------------- 配置解析 ----------------

        static List<PlotSpec> ParseSpecs()
        {
            var list = new List<PlotSpec>();
            var asset = Resources.Load<TextAsset>("Config/building-plots");
            if (asset == null)
            {
                notes.Add("building-plots.json 读不到，配置类断言会全部落空");
                return list;
            }
            var plots = Json.Parse(asset.text)["plots"];
            for (var i = 0; i < plots.Count; i++)
            {
                var item = plots[i];
                if (item.kind != JsonValue.Kind.Object) continue;
                var spec = new PlotSpec
                {
                    Id = item["id"].AsString(""),
                    Kind = item["kind"].AsString(""),
                    Art = item["art"].AsString(""),
                    FootX = item["footX"].AsFloat(0f),
                    FootY = item["footY"].AsFloat(0f),
                    Width = item["width"].AsFloat(0f),
                    Height = item["height"].AsFloat(0f),
                    Radius = item["radius"].AsFloat(0f),
                    CollisionDepth = item["collisionDepth"].AsFloat(0f),
                    CollisionTop = item["collisionTop"].AsFloat(0f),
                    Blocks = !item.Has("blocks") || item["blocks"].AsBool(true),
                };
                if (!string.IsNullOrEmpty(spec.Id)) list.Add(spec);
            }
            list.Sort((a, b) => a.FootY.CompareTo(b.FootY));
            return list;
        }

        // ---------------- 断言 ----------------

        static void CheckInstantiate()
        {
            var missing = new List<string>();
            foreach (var spec in specs)
                if (FindRect("Placeable " + spec.Id) == null) missing.Add(spec.Id);
            Check(missing.Count == 0,
                "配置里每一块地块都应在场景里有对应对象（PlaceInWorld 的 \"Placeable {id}\" 命名约定）",
                missing.Count == 0 ? "" : "缺失 " + missing.Count + "：" + string.Join(",", missing.Take(6)));

            Check(propLayer != null && propLayer.PlotCount == specs.Count,
                "物件层记录的地块数应与配置一致",
                (propLayer == null ? "物件层缺失" : propLayer.PlotCount + "") + " vs " + specs.Count);

            // 有图的必须真挂上贴图，缺图的必须走占位图形而不是空对象——
            // "缺图 = 什么都不画"和"缺图 = 占位块"在截图里是两种完全不同的事故。
            var withArt = 0;
            var placeholder = 0;
            var bare = new List<string>();
            var misplaced = new List<string>();
            var scale = Mathf.Max(0.01f, controller.WorldScale);
            foreach (var spec in specs)
            {
                var rect = FindRect("Placeable " + spec.Id);
                if (rect == null) continue;
                if (rect.GetComponent<RawImage>() != null) withArt++;
                else if (rect.GetComponent<Image>() != null) placeholder++;
                else bare.Add(spec.Id);

                // 落点：anchoredPosition = (footX, -footY) × worldScale（UiKit 的 y 轴向下）。
                if (Mathf.Abs(rect.anchoredPosition.x - spec.FootX * scale) > 2f ||
                    Mathf.Abs(rect.anchoredPosition.y + spec.FootY * scale) > 2f)
                    misplaced.Add(spec.Id + "@" + rect.anchoredPosition);
            }
            Check(bare.Count == 0, "每块地块要么挂真贴图（RawImage）要么挂占位图形（Image）",
                bare.Count == 0 ? "" : string.Join(",", bare.Take(6)));
            Check(withArt + placeholder == specs.Count,
                "渲染分支覆盖数应等于地块数（不许静默不画）",
                "有图 " + withArt + " 占位 " + placeholder + " 共 " + specs.Count);
            Check(misplaced.Count == 0, "地块实际落点应与配置的 footX/footY 一致",
                misplaced.Count == 0 ? "" : string.Join(",", misplaced.Take(6)));

            // 声明了 art 的地块到底加载上没有——这是"素材接上没有"的实况，
            // 与静态校验器的"文件存不存在"是两个不同问题，两边都要有数。
            var declared = specs.Where(s => !string.IsNullOrEmpty(s.Art)).ToList();
            var loadable = declared.Where(s => Resources.Load<Texture2D>(s.Art) != null).ToList();
            Debug.Log("[TerrainPropShot] 渲染分支: 真贴图 " + withArt + " 块，占位几何 " + placeholder +
                      " 块；配置声明 art " + declared.Count + " 块，其中 Resources 可加载 " + loadable.Count + " 块");
            Check(withArt == loadable.Count,
                "挂 RawImage 的地块数应等于 art 可加载的地块数（贴图缺失不许静默降级）",
                "RawImage " + withArt + " vs 可加载 " + loadable.Count);

            Capture("town", 1920, 1080);
        }

        /// <summary>
        /// 纵深单调性：sibling index 顺序必须与 BaseY 升序一致，一处逆序都不许有。
        /// BaseY 取立绘底边 footY + height（与 WorldPropLayer 统一底边口径一致）。
        /// </summary>
        static bool CheckDepthOrder()
        {
            var rows = new List<(string id, float baseY, int sibling)>();
            foreach (var spec in specs)
            {
                var rect = FindRect("Placeable " + spec.Id);
                if (rect == null) continue;
                rows.Add((spec.Id, spec.FootY + spec.Height, rect.GetSiblingIndex()));
            }
            Check(rows.Count == specs.Count, "深度断言需要全部地块在场",
                rows.Count + " vs " + specs.Count);
            if (rows.Count == 0) return false;

            var bySibling = rows.OrderBy(r => r.sibling).ToList();
            var inversions = new List<string>();
            for (var i = 1; i < bySibling.Count; i++)
                if (bySibling[i - 1].baseY > bySibling[i].baseY + 0.01f)
                    inversions.Add(bySibling[i - 1].id + "(" + bySibling[i - 1].baseY + ") 画在 " +
                                   bySibling[i].id + "(" + bySibling[i].baseY + ") 之前");
            Check(inversions.Count == 0,
                "绘制顺序必须严格按 BaseY 升序（T026 的根治契约：越近越后画）",
                inversions.Count == 0 ? "" : inversions.Count + " 处逆序：" + string.Join(" | ", inversions.Take(4)));

            // 玩家必须插在"比他近"的第一个实体之前、"比他远"的最后一个之后，
            // 否则走到树后不会被挡住（world y 大 = 离观察者近）。
            CheckPlayerInsertion("原位");

            // 再换个位置验一次：只站在一个地方碰巧对了，说明不了排序真是按 BaseY 走的。
            townHeight = controller.World == null ? 0f : controller.World.WorldHeight;
            moveTarget = specs.Select(s => new Vector2(s.FootX, s.FootY + 320f))
                .FirstOrDefault(p => p.y <= townHeight && controller.SnapTravelerTo(p.x, p.y));
            if (moveTarget != Vector2.zero) return true;
            notes.Add("找不到可置位的第二观测点，玩家插序只验了一个位置");
            Debug.Log("[TerrainPropShot] 地块兄弟位区间=[" + bySibling[0].sibling + "," +
                      bySibling.Last().sibling + "]");
            return false;
        }

        static Vector2 moveTarget;
        static float townHeight;

        /// <summary>玩家是否按 BaseY 插在"更近"的第一个地块之前、"更远"的最后一个之后。</summary>
        static void CheckPlayerInsertion(string where)
        {
            var player = controller.TravelerRectForShot;   // 显式钩子：城镇页主角现在是纸偶，不再是占位块
            Check(player != null, "主角纸偶应在场（" + where + "）");
            if (player == null) return;
            var scale = Mathf.Max(0.01f, controller.WorldScale);
            var playerY = -player.anchoredPosition.y / scale;
            var playerSibling = player.GetSiblingIndex();
            (string id, int sibling) ahead = (null, 0);
            (string id, int sibling) behind = (null, 0);
            var bestAhead = float.MaxValue;
            var nearestBehind = float.MaxValue;
            foreach (var spec in specs)
            {
                var rect = FindRect("Placeable " + spec.Id);
                if (rect == null) continue;
                var sibling = rect.GetSiblingIndex();
                var baseY = spec.FootY + spec.Height;
                if (baseY > playerY + 0.01f && baseY < bestAhead)
                {
                    bestAhead = baseY;
                    ahead = (spec.Id, sibling);
                }
                if (baseY <= playerY + 0.01f && playerY - baseY < nearestBehind)
                {
                    nearestBehind = playerY - baseY;
                    behind = (spec.Id, sibling);
                }
            }
            Check((ahead.id == null || playerSibling < ahead.sibling) &&
                  (behind.id == null || playerSibling > behind.sibling),
                "玩家应按 BaseY 插在地块序列中间（" + where + "，不许永远最前或最后）",
                "playerY=" + playerY + " sibling=" + playerSibling +
                " 前方=" + (ahead.id ?? "(无)") + "(" + ahead.sibling + ")" +
                " 后方=" + (behind.id ?? "(无)") + "(" + behind.sibling + ")");
        }

        static void CheckCorridorsAndWater()
        {
            var town = controller.World;
            Check(town != null, "世界配置应已加载");
            if (town == null) return;
            var scale = Mathf.Max(0.01f, controller.WorldScale);

            // 脚底压水/压山：地块落点不得落在 blocked 矩形里。
            // 口径与静态校验器判据 1 一致，只查 blocked：CanStand 还会把 NPC/prop 的
            // radius 算成不可站，而"摊子前站着摊主"是设计不是布局错误（本轮 4 个 NPC
            // 与地块同点即属此类），拿 CanStand 当判据会把正确配置判成违规。
            var inBlocked = specs.Where(s => town.Blocked.Any(r => r.Contains(s.FootX, s.FootY)))
                .Select(s => s.Id).ToList();
            Check(inBlocked.Count == 0, "地块脚底不得落在 blocked 不可通行区（水里/山里/世界外）",
                inBlocked.Count == 0 ? "" : string.Join(",", inBlocked.Take(6)));

            // 同点 NPC 只取证不判负：它会让玩家站不进地块正中心，属于另一件事。
            var npcCovered = specs
                .Where(s => !inBlocked.Contains(s.Id) && !town.CanStand(s.FootX, s.FootY))
                .Select(s => s.Id).ToList();
            if (npcCovered.Count > 0)
                Debug.Log("[TerrainPropShot] note: " + npcCovered.Count + " 块地块脚底因 NPC/prop radius 不可站" +
                          "（人站在自己摊子前，不作违规）：" + string.Join(",", npcCovered.Take(8)));

            // 地标探针：验收探针点被建筑压住，会让后续探针断言读到脏状态。
            var onLandmark = new List<string>();
            foreach (var landmark in town.Landmarks)
                foreach (var spec in specs)
                    if (Mathf.Abs(spec.FootX - landmark.X) < 8f && Mathf.Abs(spec.FootY - landmark.Y) < 8f)
                        onLandmark.Add(spec.Id + "@" + landmark.Id);
            Check(onLandmark.Count == 0, "地块脚底不得压在验收地标探针上",
                onLandmark.Count == 0 ? "" : string.Join(",", onLandmark.Take(6)));

            // 大型实体之间的通行走廊（与静态校验器判据 6 同口径，prop 豁免）。
            var majors = specs.Where(s => s.Major).ToList();
            var narrow = new List<string>();
            for (var i = 0; i < majors.Count; i++)
                for (var j = i + 1; j < majors.Count; j++)
                {
                    var a = majors[i].BaseRect();
                    var b = majors[j].BaseRect();
                    if (a.Overlaps(b)) continue;      // 叠在一起由画带判据管，这里只看"没叠但太挤"
                    if (Gap(a, b) < MinCorridor) narrow.Add(majors[i].Id + "~" + majors[j].Id);
                }
            Check(narrow.Count == 0,
                "大型实体之间应留出 >= " + MinCorridor + " 世界单位的通行走廊（玩家会卡死在窄缝里）",
                narrow.Count == 0 ? "" : narrow.Count + " 处过窄：" + string.Join(",", narrow.Take(6)));

            // 画带互不重叠：两块楼摞着画必然互相糊掉。
            var stacked = new List<string>();
            for (var i = 0; i < specs.Count; i++)
                for (var j = i + 1; j < specs.Count; j++)
                    if (specs[i].ArtRect(scale).Overlaps(specs[j].ArtRect(scale)))
                        stacked.Add(specs[i].Id + "~" + specs[j].Id);
            Check(stacked.Count == 0, "任意两块地块的画带互不重叠",
                stacked.Count == 0 ? "" : stacked.Count + " 处：" + string.Join(",", stacked.Take(6)));

            // 世界边界：越界的地块在截图里永远看不见，静默浪费配置。
            var outOfWorld = specs.Where(s =>
            {
                var r = s.ArtRect(scale);
                return r.xMin < 0f || r.yMin < 0f || r.xMax > town.WorldWidth || r.yMax > town.WorldHeight;
            }).Select(s => s.Id).ToList();
            Check(outOfWorld.Count == 0, "地块画带不得越出世界边界",
                outOfWorld.Count == 0 ? "" : string.Join(",", outOfWorld.Take(6)));

            Debug.Log("[TerrainPropShot] 走廊检查覆盖大型实体 " + majors.Count + " 块（共 " + specs.Count +
                      " 块，prop " + (specs.Count - majors.Count) + " 块按口径豁免）");
        }

        static void CheckViewports()
        {
            // 先取证：批量模式的 Screen 尺寸、CanvasScaler 实测算出的 scaleFactor、相机正交尺寸。
            // 这三个量决定了"视口"在本环境下到底等价于什么，不写进日志就是空口断言。
            Debug.Log("[TerrainPropShot] viewport probe: Screen=" + Screen.width + "x" + Screen.height +
                      " canvas.scaleFactor=" + (rootCanvas == null ? "null" : rootCanvas.scaleFactor.ToString("0.####")) +
                      " scaler=" + (canvasScaler == null ? "null"
                          : canvasScaler.uiScaleMode + "/" + canvasScaler.screenMatchMode +
                            " ref=" + canvasScaler.referenceResolution) +
                      " camera.ortho=" + (camera == null ? "null" : camera.orthographicSize.ToString("0.####")));

            Color32[] baseline = null;
            var baselineWidth = 0;
            foreach (var viewport in Viewports)
            {
                var path = Capture(viewport.name, viewport.width, viewport.height);
                Check(File.Exists(path), viewport.name + " 视口应产出截图文件 " + path);
                if (!File.Exists(path)) continue;

                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                var decoded = false;
                try { decoded = ImageConversion.LoadImage(texture, File.ReadAllBytes(path)); }
                catch (Exception ex) { notes.Add(viewport.name + " 解码异常：" + ex.Message); }
                Check(decoded, viewport.name + " 截图应可被解码读回");
                if (!decoded) { UnityEngine.Object.Destroy(texture); continue; }

                Check(texture.width == viewport.width && texture.height == viewport.height,
                    viewport.name + " 截图尺寸应正好是请求的视口",
                    texture.width + "x" + texture.height + " vs " + viewport.width + "x" + viewport.height);
                if (texture.width != viewport.width || texture.height != viewport.height)
                {
                    UnityEngine.Object.Destroy(texture);
                    continue;
                }

                var pixels = texture.GetPixels32();
                var edge = UncoveredRatio(pixels, texture.width, texture.height, 4);
                var center = UncoveredRatio(pixels, texture.width, texture.height, 0,
                    texture.width / 2 - 20, texture.width / 2 + 20, texture.height / 2 - 20, texture.height / 2 + 20);
                Debug.Log("[TerrainPropShot] " + viewport.name + " 边缘未覆盖=" +
                          (edge * 100f).ToString("0.00") + "% 中心未覆盖=" +
                          (center * 100f).ToString("0.00") + "% aspect=" + camera.aspect.ToString("0.####") +
                          " ortho=" + camera.orthographicSize.ToString("0.##"));
                Check(edge <= EdgeUncoveredTolerance,
                    viewport.name + " 视口边缘不得露出相机清屏底色（世界应铺满画面）",
                    "边缘未覆盖占比 " + (edge * 100f).ToString("0.00") + "%，上限 " +
                    (EdgeUncoveredTolerance * 100f).ToString("0") + "%");
                Check(center < 0.5f, viewport.name + " 画面中心不应大半是空底色（镜头没对上世界）",
                    "中心未覆盖占比 " + (center * 100f).ToString("0.00") + "%");

                if (viewport.name == "1080p")
                {
                    baseline = pixels;
                    baselineWidth = texture.width;
                }
                else if (baseline != null && texture.width % baselineWidth == 0 &&
                         texture.height % (baseline.Length / baselineWidth) == 0)
                {
                    var factor = texture.width / baselineWidth;
                    var diff = BoxDownsampleMeanDiff(pixels, texture.width, factor, baseline, baselineWidth);
                    Debug.Log("[TerrainPropShot] " + viewport.name + " 按 " + factor + "× 缩回 " + baselineWidth +
                              " 宽后与 1080p 的平均亮度差=" + diff.ToString("0.00"));
                    Check(diff <= DownsampleDiffTolerance,
                        viewport.name + " 缩回基准尺寸后应与 1080p 构图一致（分辨率适配无几何漂移）",
                        "平均亮度差 " + diff.ToString("0.00") + "，上限 " + DownsampleDiffTolerance);
                }
                else
                {
                    notes.Add(viewport.name + " 与基准尺寸不成整数倍，跳过缩放一致性比较（该视口只验尺寸/露底）");
                }
                UnityEngine.Object.Destroy(texture);
            }
        }

        /// <summary>任务书 I6.1 的"多角度截图"：逐个地标取景，覆盖青石镇八景。
        /// 一次 Pump 只推进一个地标（置位→等排序心跳→拍图），拍完再切下一相位。</summary>
        static void CheckSceneSweep()
        {
            var landmarks = controller.World == null ? null : controller.World.Landmarks;
            if (landmarks == null || landmarks.Count == 0)
            {
                notes.Add("世界配置里没有地标，八景取景无法执行");
                Next(Phase.Batching);
                return;
            }
            if (sweepIndex >= landmarks.Count)
            {
                Check(sceneShots >= 8, "八景取景应至少产出 8 张场景图（多角度覆盖）",
                    "实际 " + sceneShots + " 张，地标共 " + landmarks.Count + " 处");
                sweepIndex = 0;
                Next(Phase.SceneAnchors);
                return;
            }
            var landmark = landmarks[sweepIndex];
            if (!controller.SnapTravelerTo(landmark.X, landmark.Y))
            {
                notes.Add("地标 " + landmark.Id + " (" + landmark.X + "," + landmark.Y + ") 不可站立，跳过取景");
                sweepIndex++;
                return;
            }
            if (!WaitedTicks(2))
            {
                if (TimedOut(30f))
                {
                    notes.Add("地标 " + landmark.Id + " 置位后 30 秒未见排序心跳，跳过取景");
                    sweepIndex++;
                    Next(Phase.SceneSweep);
                }
                return;
            }
            Capture("scene-" + landmark.Id, 1920, 1080);
            sceneShots++;
            sweepIndex++;
            Next(Phase.SceneSweep);
        }

        /// <summary>
        /// M2（ADR-030 八景重锚）专用取景。为什么地标那一遍不够：
        /// 相机是摆在地标锚点上的，而 ADR-030 允许地块离锚点 320~900 单位——
        /// 地标帧里可能根本看不见那块地，"南门牌坊站到了北山上"这种错照样绿。
        /// 所以必须把相机摆到**地块自己脚下**再各拍一张，让人对着画面判景名对不对。
        /// </summary>
        // ---- Q1.5 世界精修：T067 牌坊压回大道 + T069 三只妖的标记两两可辨 ----
        // 这一节既要断言也要出图："改完必须出那张图"是这批的硬要求，
        // 而"断言全绿但画面没变"正是 T055/T108 那一族的形态。
        static void CheckWorldPolish()
        {
            var scale = Mathf.Max(0.01f, controller.WorldScale);
            Check(scale > 0f, "世界尺度应可读（取景与可走判定都要它）", scale.ToString("0.000"));

            // ---------- T067 ----------
            var paifang = specs == null ? null : specs.FirstOrDefault(p => p.Id == "plot-paifang");
            Check(paifang != null, "牌坊地块应在 building-plots.json 里（取景与判据都按 id 取，改名要同步这里）");
            if (paifang != null)
            {
                const float roadX = 4096f;      // terrainShape.mainRoad.baseX：南北大道中线
                Check(Mathf.Abs(paifang.FootX - roadX) < 1f,
                    "牌坊应压在南北大道中线上（原来偏东 244，孤在路东侧空草地上）",
                    "footX=" + paifang.FootX + " vs 大道 " + roadX);
                // 门洞可穿行 = 沿大道一路北上必须一直站得住。这条只认运行期真判据
                // （TownMap.CanStand 已接 ADR-033 同形根基），配置里写 blocks:false 不算数。
                var stuck = new List<string>();
                for (var y = 6780f; y >= 5900f; y -= 20f)
                    if (!controller.TerrainStandable(roadX, y)) stuck.Add(y.ToString("0"));
                Check(stuck.Count == 0,
                    "沿大道穿过牌坊应一路可走（牌坊是门，不是墙）",
                    stuck.Count == 0 ? "9 格采样全可站" : "不可站点 y=" + string.Join(",", stuck.Take(8)));
                Check(Resources.Load<Texture2D>("Art/Props/paifang") != null,
                    "牌坊立绘应真在 Resources 下（缺图会退成色块，画面就不算证到）");
                var placed = controller.SnapTravelerTo(roadX, paifang.FootY + 300f);
                Check(placed, "应能把人放到牌坊正南取景位（视线正对牌坊）",
                    placed ? "" : "正南 300 不可站，检查牌坊根基是否还在挡路");
                if (placed) Capture("q15-south-gate-paifang", 1280, 720);
            }

            // ---------- T065：地标与遭遇点的探针盘在运行期也必须全站得住 ----------
            // 静态侧 check-world-mask.py 有一条同尺寸（半径 32 的盘，几何真算不是抽样）的判据，
            // 这一条是它的运行期镜像：两边不同形就是 T018 那一族（配置说能站、实际走不过）。
            // 采样取圆心 + 半径 28 的八个方向，留 4px 给 tile 边界，免得"压线"造出假红或假绿。
            bool DiskStandable(float x, float y)
            {
                if (!controller.TerrainStandable(x, y)) return false;
                for (var i = 0; i < 8; i++)
                {
                    var angle = i * Mathf.PI * 0.25f;
                    if (!controller.TerrainStandable(x + Mathf.Cos(angle) * 28f,
                                                     y + Mathf.Sin(angle) * 28f)) return false;
                }
                return true;
            }
            var anchors = controller.World.Landmarks.Count + controller.World.Encounters.Count;
            var dirty = new List<string>();
            foreach (var mark in controller.World.Landmarks)
                if (!DiskStandable(mark.X, mark.Y)) dirty.Add("地标/" + mark.Id);
            foreach (var enc in controller.World.Encounters)
                if (!DiskStandable(enc.X, enc.Y)) dirty.Add("遭遇/" + enc.Id);
            Check(dirty.Count == 0,
                "每个地标与遭遇点的探针盘都应完全落在可站地面上（压住不可通行区=别人得替它退让）",
                dirty.Count == 0 ? anchors + " 个锚点全清" : string.Join(",", dirty.Take(6)));

            // 对照图：修复后的北山。修复前那张是 i-scene-back-mountain.png（锚点 (3800,300)，
            // 盘内压 3 块山石），md5 与两张图的差异写在报告里。
            var bm = controller.World.Landmarks.FirstOrDefault(l => l.Id == "back-mountain");
            Check(bm != null, "后山地标应在 town-map 里（T065 的验收对象就是它）");
            if (bm != null)
            {
                var afterPlaced = controller.SnapTravelerTo(bm.X, bm.Y + 60f);
                Check(afterPlaced, "后山锚点南边应能站人并取景（这张图是 T065 的验收凭据）",
                    afterPlaced ? "" : "锚点南 60 不可站，地标仍然落不进可走地面");
                if (afterPlaced) Capture("q17-after-back-mountain", 1280, 720);
            }

            // ---------- T069 ----------
            var marks = new List<(string id, string look, string shape, bool art)>();
            foreach (var id in new[] { "encounter-boar-01", "encounter-wolf-01", "encounter-mandrill-01" })
            {
                var rect = controller.EncounterMarkerForShot(id);
                Check(rect != null, "遭遇标记应在场：" + id);
                if (rect == null) continue;
                var graphic = rect.GetComponent<UnityEngine.UI.Graphic>();
                Check(graphic != null, "遭遇标记应真的画出一个图形：" + id);
                var face = graphic as UnityEngine.UI.RawImage;
                var hasArt = face != null && face.texture != null;
                // 声明了 art 就必须 Load 得出来（H-4 正向棘轮的遭遇族部分，先在这里钉住）
                var enc = controller.World.Encounters.FirstOrDefault(e => e.Id == id);
                if (enc != null && !string.IsNullOrEmpty(enc.Art))
                {
                    Check(hasArt, "配置声明了 art 的遭遇点必须真的画出立绘：" + id, enc.Art);
                    // D-51 之后钉这条命名契约：遭遇页读的是配置里声明的路径，战斗页按 "Art/NPC/" + id 拼。
                    // 两条口径一旦分叉，同一只妖会在两页长成两个样子，而两边各自都还是"绿"的。
                    Check(enc.Art == "Art/NPC/" + enc.Enemy,
                        "遭遇点声明的 art 应等于战斗页的约定路径（两页不许各读各的）",
                        enc.Art + " vs Art/NPC/" + enc.Enemy);
                }
                marks.Add((id, hasArt ? "立绘" : Quantize(graphic.color), ShapeOf(rect), hasArt));
            }
            Check(marks.Count == 3, "三只妖的遭遇标记都该在场", marks.Count + " 只");
            for (var i = 0; i < marks.Count; i++)
                for (var j = i + 1; j < marks.Count; j++)
                    Check(marks[i].look != marks[j].look || marks[i].shape != marks[j].shape,
                        "两只妖的遭遇标记必须形状或配色至少一项不同（同形同色 = 玩家点下去之前不知道面对的是什么）",
                        marks[i].id + " " + marks[i].look + "/" + marks[i].shape + " vs " +
                        marks[j].id + " " + marks[j].look + "/" + marks[j].shape);
            Debug.Log("[TerrainPropShot] 遭遇标记: " + string.Join(" | ",
                marks.Select(m => m.id + "=" + m.look + "/" + m.shape).ToArray()));

            // ---------- T112：到访信号的生产者要在**世界里**证一次 ----------
            // 套件那边已经验过"信号 → 目标推进"（含走错地点不推），这里验的是
            // "玩家真的站到桥边时，世界线认得出他到了 bridges"——两半合起来才是功能存在。
            var bridges = controller.World.Landmarks.FirstOrDefault(l => l.Id == "bridges");
            Check(bridges != null, "双桥地标应在 town-map 里（reach 目标写着它）");
            if (bridges != null)
            {
                var atBridge = controller.SnapTravelerTo(bridges.X, bridges.Y);
                Check(atBridge, "双桥应可站立并置位（否则那条 reach 目标永远走不到）",
                    atBridge ? "" : "(" + bridges.X + "," + bridges.Y + ") 不可站");
                if (atBridge)
                {
                    // 钩子返回的是"此刻落在半径内的全部地标"（逗号分隔）——双桥与河北岸两个圈重叠，
                    // 只报第一个就会漏掉任务真正要的那个（首轮实跑就是这么红的）。
                    var reached = controller.ReachedPlaceForCheck ?? "";
                    Check(reached.Split(',').Contains("bridges"),
                        "站到桥边时世界线应报出到访地点含 bridges（T112 生产者的判据）",
                        "实际 " + (reached.Length == 0 ? "(null)" : reached));
                }
            }
            Next(Phase.ColliderWire);
        }

        /// <summary>颜色量化成可比较的字符串：浮点色值直接比会因贴图/材质微差误判。</summary>
        static string Quantize(Color c) =>
            ((int)(c.r * 20)) + "/" + ((int)(c.g * 20)) + "/" + ((int)(c.b * 20));

        /// <summary>形状按 z 轴转角与宽高比归类（ring 正放、diamond 转 45°、plate 压扁）。</summary>
        static string ShapeOf(RectTransform rect)
        {
            var z = Mathf.RoundToInt(Mathf.Repeat(rect.localEulerAngles.z, 360f));
            if (z > 20 && z < 70) return "菱形";
            var size = rect.sizeDelta;
            return size.y < size.x * .7f ? "扁牌" : "圆角方块";
        }


        static void CheckSceneAnchors()
        {
            if (anchorIndex >= SceneAnchors.Length)
            {
                Check(anchorShots >= SceneAnchors.Length,
                    "三张重锚景名取景必须齐（南门牌坊 / 北山药圃 / 后山苍松）",
                    "实际 " + anchorShots + "/" + SceneAnchors.Length);
                anchorIndex = 0;
                Next(Phase.Polish);
                return;
            }
            var (label, plotId) = SceneAnchors[anchorIndex];
            var spec = specs == null ? null : specs.FirstOrDefault(p => p.Id == plotId);
            if (spec == null)
            {
                Check(false, "重锚取景要求的地块应在 building-plots.json 里存在",
                    label + " → " + plotId + " 找不到（改名没同步夹具）");
                anchorIndex++;
                Next(Phase.SceneAnchors);
                return;
            }
            // 取景位**不能**取地块自己的脚点：脚点的 y 正好等于根基带的北边界，
            // 而带是闭区间——"站在自己家的地基线上"永远 CanStand==false，这条前提本身不成立
            // （第一版就是这么写的，三张景名取景全红，红的不是画面是夹具）。
            // 改成站在景的正前方（画带以南 40，人物背后铺开整个景），前方站不住再退到正北。
            var scale = Mathf.Max(0.01f, controller.WorldScale);
            var frontY = spec.FootY + spec.Height / scale + 40f;
            var behindY = spec.FootY - 40f;
            string where;
            var placed = controller.SnapTravelerTo(spec.FootX, frontY);
            where = placed ? "景前 40" : "";
            if (!placed)
            {
                placed = controller.SnapTravelerTo(spec.FootX, behindY);
                where = placed ? "景后 40" : "";
            }
            if (!placed)
            {
                Check(false, "重锚景的景前/景后 40 至少要有一处可站立（否则这景玩家根本走不到跟前）",
                    label + " @ " + plotId + " foot=(" + spec.FootX + "," + spec.FootY + ")");
                anchorIndex++;
                Next(Phase.SceneAnchors);
                return;
            }
            Debug.Log("[TerrainPropShot] 取景 " + label + "：人物置位到 " + where + " 的 " + plotId);
            if (!WaitedTicks(2))
            {
                if (TimedOut(30f))
                {
                    Check(false, "重锚取景置位后 30 秒内未见排序心跳", label);
                    anchorIndex++;
                    Next(Phase.SceneAnchors);
                }
                return;
            }
            Capture(label, 1920, 1080);
            anchorShots++;
            anchorIndex++;
            Next(Phase.SceneAnchors);
        }

        /// <summary>
        /// M4（ADR-033）运行期碰撞接线。核心不是"有没有墙"，而是**墙的形状与静态门禁是否同形**：
        /// 校验器按轴对齐 base 矩形验走廊与死胡同，运行期若退化成圆、漏注入或多注入，
        /// "配置说能过、玩家走不过"就会回来——T018 同形状原则的碰撞版，也是 T038 的正面回答。
        /// </summary>
        static void CheckColliderWire()
        {
            var town = controller.World;
            Check(town != null, "碰撞接线断言需要世界配置已加载");
            if (town == null) { Next(Phase.Batching); return; }

            const string Source = "world-prop";
            var wired = town.ExternalBlockerCount(Source);
            var blocking = specs.Count(x => x.Blocks);
            Check(wired == blocking,
                "每块**会挡人**的地块都应注入恰好一个外部阻挡（数量不等 = 漏注入或重复注入）",
                "阻挡 " + wired + " vs 挡人的地块 " + blocking + "（共 " + specs.Count + " 块，" +
                (specs.Count - blocking) + " 块声明了 blocks:false）");

            // 根基带中心必须站不住：房子与树干是实体，不是空气。
            var airy = new List<string>();
            foreach (var s in specs.Where(x => x.Blocks))
            {
                var b = s.BaseRect();
                if (town.CanStand(b.x + b.width * .5f, b.y + b.height * .5f)) airy.Add(s.Id);
            }
            Check(airy.Count == 0, "根基带中心不应可站立（房子与树不许是空气）",
                airy.Count == 0 ? "" : "仍可站 " + airy.Count + " 块：" + string.Join(",", airy.Take(6)));
            // 反向一条（T067 的运行期牙齿）：声明 blocks:false 的地块，根基带中心**必须**站得住。
            // 只改上面那条为"跳过"就是悄悄放水；这里改成断言相反的那一面。
            var sealedGate = new List<string>();
            foreach (var s in specs.Where(x => !x.Blocks))
            {
                var b = s.BaseRect();
                if (!town.CanStand(b.x + b.width * .5f, b.y + b.height * .5f)) sealedGate.Add(s.Id);
            }
            Check(sealedGate.Count == 0,
                "声明 blocks:false 的地块必须真的能穿过去（牌坊是门，不是墙）",
                sealedGate.Count == 0 ? "" : "仍被堵住：" + string.Join(",", sealedGate));

            // 关键通道不许被地基吃掉：出生点、5 个 NPC 的**搭话位**、全部遭遇点。
            // NPC 自己的脚下是他那个圆形阻挡的圆心（TownMap.Blockers 直接取 npc.X/Y/Radius），
            // 玩家本来就不该站进人身体里——所以这条验"走到跟前"那个点（正南 radius+36，
            // 与 FrontEndWorld.ArrivalPointNear 的首选项同一点），不是验圆心。
            var sealedIn = new List<string>();
            if (!town.CanStand(town.SpawnX, town.SpawnY)) sealedIn.Add("spawn");
            foreach (var npc in town.Npcs)
            {
                var talk = new Vector2(npc.X, npc.Y + npc.Radius + 36f);
                if (!town.CanStand(talk.x, talk.y))
                    sealedIn.Add("npc:" + npc.Id + " 搭话位(" + talk.x + "," + talk.y + ")");
            }
            foreach (var enc in town.Encounters)
                if (!town.CanStand(enc.X, enc.Y)) sealedIn.Add("enc:" + enc.Id);
            Check(sealedIn.Count == 0,
                "出生点 / NPC 搭话位 / 遭遇点都必须可站立（接了碰撞不许把人封在墙里）",
                sealedIn.Count == 0 ? "" : string.Join(",", sealedIn.Take(8)));

            // 同形核对（本里程碑的核心判据）：先量"带阻挡"的结论，清掉阻挡再量基线，
            // 两者之差必须**恰好等于**静态 base 矩形的覆盖集，差一个点就是形状不一致。
            var samples = new List<Vector2>();
            foreach (var s in specs)
            {
                var b = s.BaseRect();
                for (var i = 0; i <= 4; i++)
                    for (var j = 0; j <= 2; j++)
                        samples.Add(new Vector2(b.xMin + b.width * i / 4f, b.yMin + b.height * j / 2f));
            }
            var withWire = samples.Select(p => town.CanStand(p.x, p.y)).ToList();
            town.ClearBlockers(Source);
            var baseline = samples.Select(p => town.CanStand(p.x, p.y)).ToList();
            var mismatch = new List<string>();
            for (var i = 0; i < samples.Count; i++)
            {
                var inside = specs.Any(x => x.Blocks && Covers(x.BaseRect(), samples[i]));
                // 基线本来可站 且 不在任何根基带内 → 接了碰撞也必须可站；
                // 基线可站 且 在带内 → 接了碰撞必须不可站。别的组合是 blocked/NPC 圆的账，不算这条。
                var expected = baseline[i] && !inside;
                if (withWire[i] != expected)
                    mismatch.Add("采样(" + samples[i].x + "," + samples[i].y + ") 带内=" + inside +
                                 " 基线可站=" + baseline[i] + " 接线后可站=" + withWire[i]);
            }
            Check(mismatch.Count == 0,
                "静态 base 矩形与运行期 CanStand 必须同形（0 偏差）",
                mismatch.Count == 0 ? "采样 " + samples.Count + " 点全等"
                                    : mismatch.Count + " 处偏差：" + string.Join(" | ", mismatch.Take(4)));
            Debug.Log("[TerrainPropShot] 碰撞接线：阻挡 " + wired + " 个，采样 " + samples.Count +
                      " 点，同形偏差 " + mismatch.Count + " 处");

            // 复原：清完阻挡必须有人重新注入。走一次世界页重建让物件层自己"先清后注"，
            // 然后到 ColliderRestore 等注回 —— 不能直接跳 Batching：重建会把地块对象先销毁再逐帧摆回，
            // 那一瞬间的图形数只有 73，Batching 拿它当"重建前"基线，摆回来变 104 就成了假泄漏。
            controller.ShowTown();
            Next(Phase.ColliderRestore);
        }

        /// <summary>闭区间覆盖判定——必须与 TownMap.MapRect.Contains 同口径。
        /// Unity 的 Rect.Contains 上界是开区间（x &lt; xMax），拿它当"在带内"就会在每条带的
        /// 南边界与东边界上错判：实测 31 块的边界采样报出 211 处"假不同形"，而运行期是对的。</summary>
        static bool Covers(Rect band, Vector2 p) =>
            p.x >= band.xMin && p.x <= band.xMax && p.y >= band.yMin && p.y <= band.yMax;

        /// <summary>等物件层把清掉的阻挡重新注回（这本身就是"重摆会先清后注"的正面证据）。</summary>
        static void CheckColliderRestore()
        {
            var town = controller.World;
            var now = town == null ? -1 : town.ExternalBlockerCount("world-prop");
            if (now == specs.Count(x => x.Blocks))
            {
                Check(true, "清掉的外部阻挡必须由物件层重摆自动注回",
                    "恢复后 " + now + " == 挡人的地块 " + specs.Count(x => x.Blocks));
                Next(Phase.Batching);
                return;
            }
            if (TimedOut(120f))
            {
                Check(false, "清掉的外部阻挡 120 秒内没有被物件层注回（碰撞接线会随重摆丢失）",
                    "恢复后 " + now + " vs 地块 " + specs.Count);
                Next(Phase.Batching);
            }
            // 没到齐就原地下一次心跳再看（Pump 每帧回来，物件层摆满为止）。
        }

        static void CheckBatching()
        {
            var world = Resources.FindObjectsOfTypeAll<Graphic>()
                .Where(g => g != null && g.gameObject.scene.IsValid() && g.gameObject.activeInHierarchy &&
                            IsUnderWorld(g.transform))
                .ToList();
            Check(world.Count >= specs.Count,
                "世界层图形数不应少于地块数（有图/占位都算画出来了）",
                world.Count + " vs " + specs.Count);

            // 合批的决定量：同一时间在场、被不同贴图/材质打断的 Graphic 越多，批次越多。
            // 口径必须是"镜头看得见的那些"：世界根是 8192×8192、64 块 chunk 全部激活，
            // 但离屏元素被渲染器剔除、一个批次也不产生。拿全场景的贴图分散度去比 45，
            // 量的就不是 ADR-026 说的那个东西了（第一版就是这么算的，得出 80 的假超标）。
            var visible = world.Where(g => VisibleInViewport(g.rectTransform)).ToList();
            var sceneDistinct = Dispersion(world);
            var visibleDistinct = Dispersion(visible);
            Debug.Log("[TerrainPropShot] 世界层 Graphic=" + world.Count + "（视野内 " + visible.Count + "）" +
                      " 全场景贴图/材质分散度=" + sceneDistinct + " 视野内=" + visibleDistinct);

            // 先试真批次计数（UnityStats 是引擎内部类型，批量模式下不一定取得到值），
            // 取不到就退回"视野内批次决定量"，并把用的是哪个口径如实写进日志——
            // 绝不允许"取不到数"被当成"预算通过"。
            var measured = TryReadDrawCalls(out var drawCalls, out var batches, out var setPass);
            if (measured)
            {
                Debug.Log("[TerrainPropShot] 实测批次 drawCalls=" + drawCalls +
                          " batches=" + batches + " setPass=" + setPass);
                Check(Mathf.Max(drawCalls, batches) <= DrawCallBudget,
                    "渲染批次应在 ADR-026 预算内（<= " + DrawCallBudget + "）",
                    "实测 " + Mathf.Max(drawCalls, batches));
            }
            else
            {
                notes.Add("UnityStats 批次计数在本环境不可读，改用视野内贴图/材质分散度判定");
                Check(visibleDistinct <= DrawCallBudget,
                    "视野内贴图/材质分散度（批次数上界估计）应在预算内（<= " + DrawCallBudget + "）",
                    "视野内 " + visible.Count + " 个 Graphic，不同贴图+材质 = " + visibleDistinct);
            }

            // 泄漏：重建世界页之后，场景里的世界层图形不该只增不减。
            leakBaseline = world.Count;
            controller.ShowTown();
            Next(Phase.LeakWait);
        }

        static void CheckLeak()
        {
            if (!WaitedTicks(12))
            {
                if (TimedOut(120f)) { Check(false, "重建世界页后 120 秒未见物件层重摆"); Next(Phase.BadConfig); }
                return;
            }
            var after = Resources.FindObjectsOfTypeAll<Graphic>()
                .Count(g => g != null && g.gameObject.scene.IsValid() && g.gameObject.activeInHierarchy &&
                            IsUnderWorld(g.transform));
            Check(after <= leakBaseline,
                "重建世界页不应让场景图形只增不减（对象泄漏）",
                "重建前 " + leakBaseline + " → 重建后 " + after);
            // M4 泄漏守卫：物件层每次重摆都必须"先 ClearBlockers 再整体注入"。
            // 图形数不翻倍不代表阻挡也不翻倍——那是两套容器，漏清的那套在画面上完全看不见。
            var townMap = controller.World;
            if (townMap != null)
            {
                var now = townMap.ExternalBlockerCount("world-prop");
                Check(now == specs.Count(x => x.Blocks),
                    "连进两次世界页后外部阻挡计数必须仍是每块一个（不清就累积成看不见的墙）",
                    "重摆后 " + now + " vs 挡人的地块 " + specs.Count(x => x.Blocks));
            }
            leakBaseline = -1;
            Capture("after-rebuild", 1920, 1080);
            Next(Phase.BadConfig);
        }

        static void CheckBadConfig()
        {
            Check(propLayer != null, "物件层应在场（坏配置鲁棒性前提）");
            if (propLayer == null) return;

            // 注入三种非法配置：缺 kind、footY 为负、width 为 0。
            // 要求：喊出可读浮签、产出 0 块地块、不抛未捕获异常（相位没炸本身就是证据）。
            propLayer.LoadPlotsFromTextForCheck(
                "{\"plots\":[" +
                "{\"id\":\"bad-no-kind\",\"footX\":4260,\"footY\":3720,\"width\":100,\"height\":100,\"art\":\"\"}," +
                "{\"id\":\"bad-neg-foot\",\"kind\":\"tree\",\"footX\":4260,\"footY\":-500,\"width\":100,\"height\":100,\"art\":\"\"}," +
                "{\"id\":\"bad-zero-w\",\"kind\":\"tree\",\"footX\":4260,\"footY\":3720,\"width\":0,\"height\":100,\"art\":\"\"}]}");
            Check(propLayer.PlotCount == 0, "坏配置不应产出任何地块", "实际 " + propLayer.PlotCount);
            Check(propLayer.ConfigFailedForCheck, "坏配置应被物件层标记为失败");
            Check(ActiveObjects("PropLayer error") >= 1, "坏配置应显示可读浮签而不是静默");
            Check(propLayer.ErrorBannerDrawsCjkForCheck(),
                "报错浮签的字体必须画得出汉字（T032：不许豆腐块）");
            Capture("badconfig", 1920, 1080);
            // 浮签靠 Destroy(go, 5f) 自动消失，那 5 秒是游戏时间；批量模式玩家循环极稀疏，
            // 计时器往往不触发，红字会漏进之后的截图（P-02 实测污染过）。取证后显式清干净。
            foreach (var leftover in Resources.FindObjectsOfTypeAll<RectTransform>()
                         .Where(t => t.name == "PropLayer error" && t.gameObject.scene.IsValid()))
                UnityEngine.Object.DestroyImmediate(leftover.gameObject);
            Check(ActiveObjects("PropLayer error") == 0, "浮签取证后应清理干净，不污染后续截图");

            // 坏配置之后世界页必须还能重建，不能一次坏数据就把整层打死。
            propLayer.ReloadForCheck();
        }

        static void CheckRecovery()
        {            Check(propLayer != null && !propLayer.ConfigFailedForCheck,
                "恢复正式配置后不应仍标记为坏配置",
                propLayer == null ? "物件层缺失" : "gate=" + propLayer.LastGateForCheck);
            var stillMissing = specs.Where(s => !propLayer.HasPlot(s.Id)).Select(s => s.Id).ToList();
            Check(stillMissing.Count == 0, "恢复正式配置后全部地块应重新摆出",
                stillMissing.Count == 0 ? "" : "仍缺 " + stillMissing.Count + "：" + string.Join(",", stillMissing.Take(6)));
        }

        /// <summary>任务书 I4 配置约束："艺术资源若当前缺失，WorldPropLayer 必须自动调用占位水墨几何
        /// 渲染，绝不报错崩盘"。正式配置 31 块全部接上真图之后，这条分支在真实配置里已经没有样本，
        /// 只能靠注入取证。它原先由 FrontEndShotProps 的"老柳无合格立绘故走色块占位"守着，
        /// 那条断言的前提（老柳没有合格立绘）随 T025 修复一起失效——覆盖不能跟着前提一起丢。</summary>
        static void CheckMissingArt()
        {
            if (propLayer == null) { Check(false, "缺图回退断言需要物件层在场"); Next(Phase.Done); return; }
            if (!missingArtInjected)
            {
                // 配置本身合法，只有贴图文件不存在：不许判成坏配置，必须画占位。
                propLayer.LoadPlotsFromTextForCheck(
                    "{\"plots\":[{\"id\":\"i-missing-art\",\"kind\":\"building\",\"footX\":4260,\"footY\":4020," +
                    "\"width\":120,\"height\":160,\"art\":\"Art/Props/definitely-not-imported\"}]}");
                Check(propLayer.PlotCount == 1 && !propLayer.ConfigFailedForCheck,
                    "只缺贴图文件（配置合法）不应被当成坏配置",
                    "PlotCount=" + propLayer.PlotCount + " configFailed=" + propLayer.ConfigFailedForCheck);
                missingArtInjected = true;
                Next(Phase.MissingArt);       // 重置心跳基准，等摆放真正落地
                return;
            }
            if (!WaitedTicks(12))
            {
                if (!TimedOut(120f)) return;
                Check(false, "注入缺图地块后 120 秒仍未摆放");
            }
            var rect = FindRect("Placeable i-missing-art");
            Check(rect != null, "缺图地块仍应被摆出来（缺素材不许少一栋房子）");
            if (rect != null)
            {
                Check(rect.GetComponent<Image>() != null, "缺图地块应挂占位图形（Image）");
                Check(rect.GetComponent<RawImage>() == null, "缺图地块不许挂 RawImage（那会画成白块）");
            }
            missingArtInjected = false;
            propLayer.ReloadForCheck();
            Next(Phase.Done);
        }

        // ---------------- 基础设施 ----------------

        static void SetupCapture()
        {
            camera = Camera.main;
            Check(camera != null, "主相机存在");
            if (camera == null) return;
            camera.clearFlags = CameraClearFlags.SolidColor;
            rootCanvas = UnityEngine.Object.FindAnyObjectByType<Canvas>();
            Check(rootCanvas != null, "Canvas 存在");
            if (rootCanvas == null) return;
            canvasScaler = rootCanvas.GetComponent<CanvasScaler>();
            rootCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            rootCanvas.worldCamera = camera;
            rootCanvas.planeDistance = 10;
            // 显式定 aspect：Unity 在赋值 targetTexture 时是否自动改 aspect 跨版本不一致，
            // 而视口测试的整个前提就是"相机看到多大一块画布"。前提钉死在夹具里，不赌默认。
            camera.aspect = 1920f / 1080f;
            camera.Render();
            Canvas.ForceUpdateCanvases();
            Debug.Log("[TerrainPropShot] capture ready: camera ortho=" + camera.orthographicSize +
                      " aspect=" + camera.aspect + " clearColor=" + camera.backgroundColor +
                      " canvas.scaleFactor=" + rootCanvas.scaleFactor);
        }

        static int FreeSlot()
        {
            foreach (var info in controller.Repository.ReadAll())
                if (info.State == SlotState.Empty) return info.Slot;
            return 0;
        }

        static bool IsUnderWorld(Transform t)
        {
            for (var node = t; node != null; node = node.parent)
                if (node.name == "World root") return true;
            return false;
        }

        /// <summary>这批 UI 元素会打断多少次合批（不同贴图 + 不同材质的数量）。</summary>
        static int Dispersion(IEnumerable<Graphic> graphics)
        {
            var textures = new HashSet<UnityEngine.Object>();
            var materials = new HashSet<UnityEngine.Object>();
            foreach (var graphic in graphics)
            {
                if (graphic.material != null) materials.Add(graphic.material);
                if (graphic is RawImage raw && raw.texture != null) textures.Add(raw.texture);
                else if (graphic is Image img && img.sprite != null) textures.Add(img.sprite);
            }
            return textures.Count + materials.Count;
        }

        /// <summary>这个 RectTransform 是否落在相机视野内（视口坐标 [0,1] 区间有交集）。
        /// 用视口坐标而不是像素：三种分辨率下视口坐标恒等，判据才不会随尺寸漂移。</summary>
        static bool VisibleInViewport(RectTransform rect)
        {
            if (camera == null || rect == null) return true;
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            var minX = float.MaxValue; var maxX = float.MinValue;
            var minY = float.MaxValue; var maxY = float.MinValue;
            var any = false;
            foreach (var corner in corners)
            {
                var v = camera.WorldToViewportPoint(corner);
                if (v.z <= 0f) continue;         // 相机背后，不参与本帧渲染
                any = true;
                minX = Mathf.Min(minX, v.x); maxX = Mathf.Max(maxX, v.x);
                minY = Mathf.Min(minY, v.y); maxY = Mathf.Max(maxY, v.y);
            }
            if (!any) return false;
            return maxX >= 0f && minX <= 1f && maxY >= 0f && minY <= 1f;
        }

        static Rect RectMake(float x, float y, float width, float height)
        {
            // Unity 的 Rect 允许负宽高，但 Overlaps 在负宽高上会失真；一律规范成正宽高。
            if (width < 0f) return RectMake(x + width, y, -width, height);
            if (height < 0f) return RectMake(x, y + height, width, -height);
            return new Rect(x, y, width, height);
        }

        /// <summary>两个轴对齐矩形之间的最小间隙；交叠时为 0（与校验器 gap_between 同式）。</summary>
        static float Gap(Rect a, Rect b)
        {
            var dx = Mathf.Max(0f, Mathf.Max(b.xMin - a.xMax, a.xMin - b.xMax));
            var dy = Mathf.Max(0f, Mathf.Max(b.yMin - a.yMax, a.yMin - b.yMax));
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>读 UnityStats 的批次计数。它是引擎内部类型，反射拿，拿不到返回 false。</summary>
        static bool TryReadDrawCalls(out int drawCalls, out int batches, out int setPass)
        {
            drawCalls = batches = setPass = 0;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType("UnityEngine.Rendering.UnityStats");
                if (type == null) continue;
                drawCalls = ReadInt(type, "drawCalls");
                batches = ReadInt(type, "batches");
                setPass = ReadInt(type, "setPassCalls");
                if (drawCalls > 0 || batches > 0) return true;
            }
            return false;
        }

        static int ReadInt(Type type, string name)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            var property = type.GetProperty(name, flags);
            if (property != null)
            {
                try { return Convert.ToInt32(property.GetValue(null, null)); }
                catch (Exception) { return 0; }
            }
            var field = type.GetField(name, flags);
            if (field == null) return 0;
            try { return Convert.ToInt32(field.GetValue(null)); }
            catch (Exception) { return 0; }
        }

        static void ShowPageContent()
        {
            foreach (var group in Resources.FindObjectsOfTypeAll<CanvasGroup>())
                if (group.gameObject.scene.IsValid() && group.gameObject.name.StartsWith("Page "))
                    group.alpha = 1f;
        }

        static string Capture(string label, int width, int height)
        {
            ShowPageContent();
            Canvas.ForceUpdateCanvases();
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            var previousTarget = camera.targetTexture;
            var previousAspect = camera.aspect;
            camera.targetTexture = rt;
            camera.aspect = (float)width / height;
            camera.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            var path = Path.Combine(shotsDir, "i-" + label + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            camera.targetTexture = previousTarget;
            camera.aspect = previousAspect;
            RenderTexture.active = previous;
            UnityEngine.Object.Destroy(tex);
            rt.Release();
            UnityEngine.Object.Destroy(rt);
            Debug.Log("[TerrainPropShot] captured " + path);
            return path;
        }

        /// <summary>有多少像素还是相机的清屏底色——即"这一块画面什么都没画"。
        /// 判据以相机自己的 backgroundColor 为准而不是写死一个绝对黑阈值：本作的底色是
        /// 深墨绿（UiKit.Dark #102622），拿"接近纯黑"去量会一条都测不到。</summary>
        static float UncoveredRatio(Color32[] pixels, int width, int height, int band)
        {
            return UncoveredRatio(pixels, width, height, band, 0, width, 0, height);
        }

        static float UncoveredRatio(Color32[] pixels, int width, int height, int band,
            int x0, int x1, int y0, int y1)
        {
            var clear = camera != null ? camera.backgroundColor : Color.black;
            var cr = (int)(clear.r * 255f);
            var cg = (int)(clear.g * 255f);
            var cb = (int)(clear.b * 255f);
            if (band == 0)
            {
                var hit = 0;
                var total = 0;
                for (var y = y0; y < y1; y++)
                    for (var x = x0; x < x1; x++)
                    {
                        total++;
                        if (NearClearColor(pixels[y * width + x], cr, cg, cb)) hit++;
                    }
                return total == 0 ? 0f : (float)hit / total;
            }
            var edgeHit = 0;
            var edgeTotal = 0;
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var onEdge = x < band || y < band || x >= width - band || y >= height - band;
                    if (!onEdge) continue;
                    edgeTotal++;
                    if (NearClearColor(pixels[y * width + x], cr, cg, cb)) edgeHit++;
                }
            return edgeTotal == 0 ? 0f : (float)edgeHit / edgeTotal;
        }

        static bool NearClearColor(Color32 p, int cr, int cg, int cb) =>
            Math.Abs(p.r - cr) <= ClearColorEpsilon && Math.Abs(p.g - cg) <= ClearColorEpsilon &&
            Math.Abs(p.b - cb) <= ClearColorEpsilon;

        /// <summary>把高分辨率图按整数倍 box 降采样后，与基准图逐像素比亮度的平均值差。
        /// 这是本夹具里唯一一条"只有多分辨率之间构图不一致才会响"的判据——
        /// 单张图的尺寸/露底断言在各分辨率下可以各自为政地通过。</summary>
        static float BoxDownsampleMeanDiff(Color32[] source, int sourceWidth, int factor,
            Color32[] baseline, int baselineWidth)
        {
            var targetWidth = sourceWidth / factor;
            var targetHeight = source.Length / (sourceWidth * factor);
            if (targetWidth != baselineWidth || targetHeight != baseline.Length / baselineWidth)
                return float.MaxValue;
            if (targetWidth * factor != sourceWidth || source.Length % sourceWidth != 0)
                return float.MaxValue;

            double sum = 0d;
            for (var y = 0; y < targetHeight; y++)
                for (var x = 0; x < targetWidth; x++)
                {
                    var acc = 0f;
                    for (var dy = 0; dy < factor; dy++)
                        for (var dx = 0; dx < factor; dx++)
                        {
                            var p = source[(y * factor + dy) * sourceWidth + x * factor + dx];
                            acc += (p.r + p.g + p.b) / 3f;
                        }
                    var b = baseline[y * baselineWidth + x];
                    sum += Math.Abs(acc / (factor * factor) - (b.r + b.g + b.b) / 3f);
                }
            return (float)(sum / (targetWidth * targetHeight));
        }

        static RectTransform FindRect(string name)
        {
            return Resources.FindObjectsOfTypeAll<RectTransform>()
                .FirstOrDefault(r => r.gameObject.scene.IsValid() && r.gameObject.activeInHierarchy &&
                                     r.gameObject.name == name);
        }

        static int ActiveObjects(string name)
        {
            return Resources.FindObjectsOfTypeAll<RectTransform>()
                .Count(r => r.gameObject.scene.IsValid() && r.gameObject.activeInHierarchy &&
                            r.gameObject.name == name);
        }

        static void Check(bool condition, string message, string detail = null)
        {
            var full = string.IsNullOrEmpty(detail) ? message : message + " · " + detail;
            if (!condition) problems.Add("断言失败: " + full);
            else { okCount++; Debug.Log("[TerrainPropShot] ok: " + full); }
        }

        static void Finish()
        {
            SessionState.SetBool(SessionKey, false);
            Application.logMessageReceived -= OnLog;
            EditorApplication.update -= Pump;
            if (slot > 0) controller?.Repository.Delete(slot);
            var scratch = Environment.GetEnvironmentVariable("TWELVEJADE_SAVEDIR");
            try { if (!string.IsNullOrEmpty(scratch) && Directory.Exists(scratch)) Directory.Delete(scratch, true); }
            catch (Exception ex) { Debug.LogWarning("无法清理验收临时目录：" + ex.Message); }
            if (camera != null) camera.targetTexture = null;
            foreach (var note in notes) Debug.Log("[TerrainPropShot] note: " + note);
            Debug.Log("[TerrainPropShot] ok: " + okCount + " 项断言通过");
            Debug.Log("[TerrainPropShot] suite finished, " + problems.Count + " problem(s)");
            foreach (var problem in problems.ToArray()) Debug.LogError(problem);
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += () => EditorApplication.Exit(problems.Count == 0 ? 0 : 1);
        }

        static void Fail(string message)
        {
            SessionState.SetBool(SessionKey, false);
            Debug.LogError(message);
            EditorApplication.update -= Pump;
            var scratch = Environment.GetEnvironmentVariable("TWELVEJADE_SAVEDIR");
            try { if (!string.IsNullOrEmpty(scratch) && Directory.Exists(scratch)) Directory.Delete(scratch, true); }
            catch { /* 尽力清理 */ }
            EditorApplication.Exit(1);
        }
    }
}
