using System;
using System.Collections.Generic;
using TwelveJade.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // 建筑 / 树物件层（P-01）：把 building-plots.json 里的地块摆进大世界。
    //
    // 本文件不修改世界页一行代码——世界页（FrontEndWorld.cs）归地形会话所有，
    // 物件层以"自举 + 每帧轮询"的方式接入：运行时拿到 FrontEndController 实例，
    // 调它的 public PlaceInWorld 摆放，之后物件由世界页自己的 UpdateDepthOrder 排序。
    // 好处是两条线零文件交集；代价是摆放要轮询到世界根建好为止。
    //
    // 数据全部来自 Resources/Config/building-plots.json（铁律二：所有内容数据化）。
    // 坏配置必须抛 FormatException 并显示可读浮签——静默空摆会让"配置坏了"看起来
    // 像"这个世界本来就没有房子"。
    public sealed class WorldPropLayer : MonoBehaviour
    {
        sealed class Plot
        {
            public string Id;
            public string Kind;
            public string Name;
            public string Art;
            public float FootX;
            public float FootY;
            public float Width;
            public float Height;
            public float GroundFootOffset;
            // 地面根基的半深（ADR-033）。0 = 未填，运行期退化成 DefaultBaseDepth——
            // 与静态校验器 base_rect 同一个兜底，两边不许一个填一个不填。
            public float Radius;
            // MAP-11：覆盖不透明内容带的根基深度（逐 plot 实测，>0 时优先于 radius×2）。
            // 0 = 未填，走 radius 回退；与 check-plot-overlap.py / TerrainPropShot 逐字同式。
            public float CollisionDepth;
            // MAP-12：首个不透明行距顶距离（逐 plot 实测，>0 时下移根基上沿）。
            // 0 = 未填，走图顶旧口径；与 check-plot-overlap.py / TerrainPropShot 逐字同式。
            public float CollisionTop;
            // MAP-27：可选的分段阻挡矩形（相对 FootX/FootY 的偏移，世界 px，y 向下为正）。
            // null = 未填 —— 完全走上面那个单矩形旧口径，行为一行不变；
            // 非 null（至少一条）= 按条注册多个 blocker，单矩形那三个字段本轮不参与。
            // 空数组是坏配置：写了字段却不给一条，世界里就变成"这栋楼不挡人"，而画面看不出来。
            public List<CollisionRect> CollisionRects;
            public bool HasFootCapsule;
            public float CapsuleX0,CapsuleX1,CapsuleY,CapsuleRadius;
            /// <summary>这块地挡不挡人。默认 true；牌坊这类"门洞可穿行"的物件写 false。
            /// 阻挡模型是一块矩形根基（ADR-033），表达不出"中间三个门洞、两侧是墩"，
            /// 所以宁可整块不挡，也不让 340 宽的门楼横封 140 宽的大道（T067）。
            /// 真要保留门墩的阻挡，得先把根基模型升级成"每块可带多个矩形"，那是另一轮的事。</summary>
            public bool Blocks = true;
            public RectTransform Rect;
            public RectTransform GroundContact;
            public bool OcclusionEnabled;
            public bool FrameCamera;

            /// <summary>MAP-27：一条分段阻挡矩形。x0/y0/x1/y1 是相对 FootX/FootY 的偏移
            /// （不是世界坐标、也不是画布像素），y 向下为正——与碰撞带 `[footY+top, footY+depth]`
            /// 同一套口径，所以单矩形那一带恰好等于 `{x0:-w/2, y0:top, x1:+w/2, y1:depth}`。</summary>
            public sealed class CollisionRect
            {
                public float X0;
                public float Y0;
                public float X1;
                public float Y1;
                public string Role;
            }
        }

        readonly List<Plot> plots = new List<Plot>();
        readonly HashSet<string> warnedArt = new HashSet<string>();
        // 注入 TownMap 的外部阻挡按这个域名分域清场（ADR-033.1）。
        const string BlockerSource = "world-prop";
        // 与 check-plot-overlap.py 的 DEFAULT_BASE_DEPTH 逐字同值；两处不同值时
        // "静态说能过、运行期走不过"会回来（T018 同形状原则）。
        const float DefaultBaseDepth = 24f;
        FrontEndController controller;
        RectTransform errorLabel;
        bool parsed;
        bool placed;
        bool configFailed;

        // 占位色：建筑偏土木褐、树偏苔绿，都是低饱和，截图里不会跟地形色打架。
        static readonly Color BuildingFallback = new Color(.42f, .36f, .30f);
        static readonly Color TreeFallback = new Color(.30f, .45f, .28f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            // 域重载后本方法会再跑一次，重复挂两个物件层会让每块地被摆两遍。
            if (FindAnyObjectByType<WorldPropLayer>() != null) return;
            // 这里刻意不设 hideFlags：一旦挂上 DontSave/HideAndDontSave，
            // FindAnyObjectByType<WorldPropLayer>() 就找不到它，验收脚本无法断言
            // （P-01 首跑正是被这一行判了 4 条"物件层缺失"）。Play 模式的对象本就不持久。
            var host = new GameObject("WorldPropLayer");
            host.AddComponent<WorldPropLayer>();
        }

        void Update()
        {
            UpdateTicksForCheck++;
            if (controller == null)
            {
                controller = FindAnyObjectByType<FrontEndController>();
                if (controller == null) { LastGateForCheck = "no-controller"; return; }
            }
            // 世界没加载（还在主菜单/创建页）时无事可做。
            if (controller.World == null) { LastGateForCheck = "no-world"; return; }
            // 只有城镇页才有世界根；在别处摆放会摆进被停用的旧世界根里，永远看不见。
            if (controller.CurrentPage != "town") { LastGateForCheck = "not-town:" + controller.CurrentPage; return; }
            LastGateForCheck = "pass";

            if (!parsed && !configFailed)
            {
                var asset = Resources.Load<TextAsset>("Config/building-plots");
                try
                {
                    ParsePlots(asset);
                    parsed = true;
                }
                catch (FormatException ex)
                {
                    configFailed = true;
                    ShowError(ex.Message);
                    return;
                }
            }
            if (configFailed || !parsed) return;

            if (placed && AllPlotsAlive()) return;
            PlaceAll();
        }

        // 已摆放的对象被销毁（页面销毁）或所在世界根被停用（ShowTown 重建了世界）→ 需要重摆。
        bool AllPlotsAlive()
        {
            foreach (var plot in plots)
                if (plot.Rect == null || !plot.Rect.gameObject.activeInHierarchy) return false;
            return true;
        }

        void PlaceAll()
        {
            // 先清自己那一份外部阻挡再整体重注：不清就会累积——每重进一次世界页，
            // 世界里多一层看不见的墙，而画面上完全看不出来（T038 的根因形态）。
            var town = controller.World;
            if (town != null) town.ClearBlockers(BlockerSource);
            foreach (var plot in plots)
            {
                // 旧对象还挂在停用的旧世界根下：先销毁再摆，否则每帧重摆会一直累积对象。
                if(plot.GroundContact!=null){Destroy(plot.GroundContact.gameObject);plot.GroundContact=null;}
                if (plot.Rect != null)
                {
                    Destroy(plot.Rect.gameObject);
                    plot.Rect = null;
                }
                var tex = string.IsNullOrEmpty(plot.Art) ? null : Resources.Load<Texture2D>(plot.Art);
                if (tex == null && !warnedArt.Contains(plot.Art))
                {
                    warnedArt.Add(plot.Art);
                    Debug.LogWarning("[WorldPropLayer] 地块 " + plot.Id + " 的立绘缺失：Resources/" +
                        (string.IsNullOrEmpty(plot.Art) ? "(art 为空)" : plot.Art) + "，先画色块占位。");
                }
                // 统一底边口径：所有 kind 的纵深排序键都取立绘底边（footY + height，= 画面南沿/墙脚），
                // 与玩家脚底键、建筑底边键同基准。顶边口径会让树/摊/矮物被错误地排在建筑与角色之前
                //（bug3 穿模同根）。
                var sortBaseY = plot.FootY + plot.GroundFootOffset;
                var rect = controller.PlaceInWorld(plot.Id, new Vector2(plot.FootX, plot.FootY),
                    plot.Width, plot.Height, tex, sortBaseY, plot.FrameCamera);
                // 世界根还没建好：本帧放弃，下一帧重试。
                if (rect == null) return;
                if (tex == null) AddFallback(rect, plot);
                plot.Rect = rect;
                if(tex!=null&&plot.Kind=="building"&&plot.CollisionRects!=null&&!plot.HasFootCapsule)
                {
                    var supports=new Vector4[plot.CollisionRects.Count];
                    for(int j=0;j<supports.Length;j++){var c=plot.CollisionRects[j];supports[j]=new Vector4(c.X0,c.Y0,c.X1,c.Y1);}
                    plot.GroundContact=controller.PlaceBuildingGroundContact(plot.Id,new Vector2(plot.FootX,plot.FootY),plot.Width,plot.Height,sortBaseY,supports);
                }
                if (plot.OcclusionEnabled)
                {
                    var fader = rect.gameObject.AddComponent<BuildingOcclusionFader>();
                    fader.Configure(controller, plot.FootX, sortBaseY, plot.Width, plot.Height,
                        town.Elevation.ProjectY(plot.FootX, sortBaseY));
                }
                // ADR-033.2 根基矩形，与 check-plot-overlap.py 的 base_rect 逐字同式：
                //   x ∈ [footX - w/2, footX + w/2]，y ∈ [footY + baseTop, footY + baseDepth]
                //   baseDepth = collisionDepth > 0 ? collisionDepth
                //             : radius > 0 ? radius×2 : DefaultBaseDepth
                //   baseTop   = collisionTop > 0 ? collisionTop : 0
                // ×2 这个系数以**代码为准**（python 与 TerrainPropShot 的 BaseRect 都是 ×2），
                // ADR-033.2 的文字漏写了 ×2；照文字实现会让"静态/运行期同形"断言当场红。
                // collisionDepth（MAP-11）：逐 plot 实测的"覆盖不透明内容带底边"深度。
                // collisionTop（MAP-12）：逐 plot 实测的"首个不透明行"距顶距离，消除透明顶部误挡。
                if (town != null && plot.Blocks)
                {
                    if(plot.HasFootCapsule)
                    {
                        town.RegisterFootCapsule(BlockerSource,plot.FootX+plot.CapsuleX0,plot.FootX+plot.CapsuleX1,
                            plot.FootY+plot.CapsuleY,plot.CapsuleRadius);
                    }
                    else if (plot.CollisionRects != null)
                    {
                        // MAP-27：分段阻挡（多矩形）。四条边已在解析期校验过 x0<x1 / y0<y1，
                        // 所以不会撞上 RegisterBlocker 的"空矩形静默丢弃"那条防御。
                        // 横向透明缺口与"屋脊以上"从此不再被当成墙，人只被真正画出来的墙体拦住。
                        foreach (var segment in plot.CollisionRects)
                            town.RegisterBlocker(BlockerSource,
                                plot.FootX + segment.X0, plot.FootY + segment.Y0,
                                plot.FootX + segment.X1, plot.FootY + segment.Y1);
                    }
                    else
                    {
                        var depth = plot.CollisionDepth > 0f ? plot.CollisionDepth
                            : plot.Radius > 0f ? plot.Radius * 2f : DefaultBaseDepth;
                        var top = plot.CollisionTop > 0f ? plot.CollisionTop : 0f;
                        town.RegisterBlocker(BlockerSource,
                            plot.FootX - plot.Width * .5f, plot.FootY + top,
                            plot.FootX + plot.Width * .5f, plot.FootY + depth);
                    }
                }
            }
            placed = true;
            // 统一底边口径：所有 kind 的纵深排序键都取立绘底边（footY+height）。
            // 验收截图不看这一行，它只是让“哪一类换了口径”在日志里有据可查。
            Debug.Log("[WorldPropLayer] 纵深排序键：实测脚底触地点，" + plots.Count + " 块使用footY+groundFootOffset（旧配置回退画布底边）。");
        }

        static void AddFallback(RectTransform rect, Plot plot)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = plot.Kind == "tree" ? TreeFallback : BuildingFallback;
            image.raycastTarget = false;
        }

        // ---------------- 配置解析（铁律二：数据化；坏配置必须炸出来）----------------

        void ParsePlots(TextAsset asset)
        {
            if (asset == null)
                throw new FormatException("物件层配置缺失：Resources/Config/building-plots.json。");
            ParsePlots(asset.text);
        }

        void ParsePlots(string source)
        {
            var root = Json.Parse(source);
            if (root.kind != JsonValue.Kind.Object)
                throw new FormatException("building-plots.json 的根节点必须是对象（实际是" + root.Describe() + "）。");
            var list = root["plots"];
            if (list.kind != JsonValue.Kind.Array)
                throw new FormatException("building-plots.json 缺少 plots 数组（实际" + list.Describe() + "）。");
            if (list.Count == 0)
                throw new FormatException("building-plots.json 的 plots 是空数组——物件层没有可摆的地块。");

            plots.Clear();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < list.Count; i++)
            {
                var item = list[i];
                if (item.kind != JsonValue.Kind.Object)
                    throw new FormatException("building-plots.json 第 " + (i + 1) + " 块不是对象（实际" + item.Describe() + "）。");
                var id = RequireString(item, "id", i);
                if (!seen.Add(id)) throw new FormatException("地块 id 重复：" + id + "。");
                plots.Add(new Plot
                {
                    Id = id,
                    Kind = RequireString(item, "kind", i),
                    Name = item["name"].AsString(""),
                    Art = item["art"].AsString(""),
                    FootX = RequireNumber(item, "footX", id),
                    FootY = RequireNumber(item, "footY", id),
                    Width = RequirePositive(item, "width", id),
                    Height = RequirePositive(item, "height", id),
                    GroundFootOffset = item.Has("groundFootOffset") ? RequirePositive(item, "groundFootOffset", id) : RequirePositive(item, "height", id),
                    Radius = OptionalRadius(item, id),
                    CollisionDepth = OptionalCollisionDepth(item, id),
                    CollisionTop = OptionalCollisionTop(item, id),
                    CollisionRects = OptionalCollisionRects(item, id),
                    HasFootCapsule = item.Has("collisionCapsule"),
                    CapsuleX0 = item.Has("collisionCapsule") ? RequireNumber(item["collisionCapsule"],"x0",id) : 0,
                    CapsuleX1 = item.Has("collisionCapsule") ? RequireNumber(item["collisionCapsule"],"x1",id) : 0,
                    CapsuleY = item.Has("collisionCapsule") ? RequireNumber(item["collisionCapsule"],"y",id) : 0,
                    CapsuleRadius = item.Has("collisionCapsule") ? RequirePositive(item["collisionCapsule"],"radius",id) : 0,
                    Blocks = !item.Has("blocks") || item["blocks"].AsBool(true),
                    OcclusionEnabled = OptionalOcclusion(item, id),
                    FrameCamera = OptionalCameraFraming(item, id),
                });
            }

            // 摆放顺序按 footY 升序——P-01 时这是世界页隐含契约的防御（UpdateDepthOrder
            // 曾假定"列表第 i 项 = sibling 第 i 个"）；P-02 已在世界页根治（排序写回兄弟序，
            // 见 FrontEndWorld.UpdateDepthOrder），这里的排序降级为稳定性保险，保留无害。
            plots.Sort((a, b) => a.FootY.CompareTo(b.FootY));
        }

        static bool OptionalCameraFraming(JsonValue item, string id)
        {
            if (!item.Has("cameraFraming")) return false;
            if (item["cameraFraming"].kind != JsonValue.Kind.Bool)
                throw new FormatException("地块 " + id + " 的 cameraFraming 必须为布尔值。");
            return item["cameraFraming"].AsBool(false);
        }

        static bool OptionalOcclusion(JsonValue item, string id)
        {
            if (!item.Has("occlusion")) return false;
            var value = item["occlusion"];
            if (value.kind != JsonValue.Kind.Object || !value.Has("enabled") ||
                value["enabled"].kind != JsonValue.Kind.Bool)
                throw new FormatException("地块 " + id + " 的 occlusion 必须为含布尔 enabled 的对象。");
            return value["enabled"].AsBool(false);
        }

        static string RequireString(JsonValue item, string field, int index)
        {
            var value = item[field];
            var text = value.kind == JsonValue.Kind.String ? value.AsString("") : "";
            if (string.IsNullOrWhiteSpace(text))
                throw new FormatException("building-plots.json 第 " + (index + 1) + " 块的 " + field + " 缺失或为空。");
            return text;
        }

        static float RequireNumber(JsonValue item, string field, string id)
        {
            var value = item[field];
            if (value.kind != JsonValue.Kind.Number)
                throw new FormatException("地块 " + id + " 的 " + field + " 必须是数字（实际" + value.Describe() + "）。");
            return value.AsFloat(0f);
        }

        static float RequirePositive(JsonValue item, string field, string id)
        {
            var value = RequireNumber(item, field, id);
            if (value <= 0f)
                throw new FormatException("地块 " + id + " 的 " + field + " 必须为正（实际 " + value + "）。");
            return value;
        }

        /// <summary>radius 是可选字段：没写 = 0（根基退化成 DefaultBaseDepth 薄皮），
        /// 写了就必须是数、且不许为负。与 check-plot-overlap.py 的 parse_plots 同口径——
        /// 两边口径不一致时，静态门禁放行、运行期炸，或反过来。</summary>
        static float OptionalRadius(JsonValue item, string id)
        {
            if (!item.Has("radius")) return 0f;
            var value = item["radius"];
            if (value.kind == JsonValue.Kind.Null) return 0f;
            if (value.kind != JsonValue.Kind.Number)
                throw new FormatException("地块 " + id + " 的 radius 必须是数字（实际" + value.Describe() + "）。");
            var radius = value.AsFloat(0f);
            if (radius < 0f)
                throw new FormatException("地块 " + id + " 的 radius 不能为负（实际 " + radius + "）。");
            return radius;
        }

        /// <summary>collisionDepth 是可选字段（MAP-11）：没写 = 0（根基走 radius 回退），
        /// 写了就必须是数、且不许为负。与 check-plot-overlap.py 的 parse_plots 同口径。</summary>
        static float OptionalCollisionDepth(JsonValue item, string id)
        {
            if (!item.Has("collisionDepth")) return 0f;
            var value = item["collisionDepth"];
            if (value.kind == JsonValue.Kind.Null) return 0f;
            if (value.kind != JsonValue.Kind.Number)
                throw new FormatException("地块 " + id + " 的 collisionDepth 必须是数字（实际" + value.Describe() + "）。");
            var depth = value.AsFloat(0f);
            if (depth < 0f)
                throw new FormatException("地块 " + id + " 的 collisionDepth 不能为负（实际 " + depth + "）。");
            return depth;
        }

        /// <summary>collisionTop 是可选字段（MAP-12）：没写 = 0（上沿走图顶旧口径），
        /// 写了就必须是数、且不许为负。与 check-plot-overlap.py 的 parse_plots 同口径。</summary>
        static float OptionalCollisionTop(JsonValue item, string id)
        {
            if (!item.Has("collisionTop")) return 0f;
            var value = item["collisionTop"];
            if (value.kind == JsonValue.Kind.Null) return 0f;
            if (value.kind != JsonValue.Kind.Number)
                throw new FormatException("地块 " + id + " 的 collisionTop 必须是数字（实际" + value.Describe() + "）。");
            var top = value.AsFloat(0f);
            if (top < 0f)
                throw new FormatException("地块 " + id + " 的 collisionTop 不能为负（实际 " + top + "）。");
            return top;
        }

        /// <summary>collisionRects 是可选字段（MAP-27）：没写/为 null = 未填，运行期走单矩形旧口径；
        /// 写了就必须是非空数组，每项带 x0/y0/x1/y1（数字）且 x0&lt;x1、y0&lt;y1，否则抛 FormatException。
        /// 空数组特别不许放过：写了这个字段却一条不给，结果就是"这栋楼不挡人"，
        /// 而画面上完全看不出来——与文件头说的"静默空摆"是同一种事故。
        /// 坐标是相对 footX/footY 的偏移（不是世界坐标、也不是画布像素），y 向下为正。</summary>
        static List<Plot.CollisionRect> OptionalCollisionRects(JsonValue item, string id)
        {
            if (!item.Has("collisionRects")) return null;
            var value = item["collisionRects"];
            if (value.kind == JsonValue.Kind.Null) return null;
            if (value.kind != JsonValue.Kind.Array)
                throw new FormatException("地块 " + id + " 的 collisionRects 必须是数组（实际" + value.Describe() + "）。");
            if (value.Count == 0)
                throw new FormatException("地块 " + id + " 的 collisionRects 是空数组——要么至少给一条矩形，" +
                    "要么删掉整个字段走单矩形旧口径（写了字段却一条不给 = 这栋楼静默不挡人）。");

            var rects = new List<Plot.CollisionRect>(value.Count);
            for (var i = 0; i < value.Count; i++)
            {
                var element = value[i];
                if (element.kind != JsonValue.Kind.Object)
                    throw new FormatException("地块 " + id + " 的第 " + (i + 1) + " 条 collisionRects 不是对象（实际" +
                        element.Describe() + "）。");
                var rect = new Plot.CollisionRect
                {
                    X0 = RequireSegmentNumber(element, "x0", id, i),
                    Y0 = RequireSegmentNumber(element, "y0", id, i),
                    X1 = RequireSegmentNumber(element, "x1", id, i),
                    Y1 = RequireSegmentNumber(element, "y1", id, i),
                    Role = element["role"].AsString(""),
                };
                if (rect.X0 >= rect.X1)
                    throw new FormatException("地块 " + id + " 的第 " + (i + 1) + " 条 collisionRects 的 x0 必须小于 x1（实际 x0=" +
                        rect.X0 + "，x1=" + rect.X1 + "）。");
                if (rect.Y0 >= rect.Y1)
                    throw new FormatException("地块 " + id + " 的第 " + (i + 1) + " 条 collisionRects 的 y0 必须小于 y1（实际 y0=" +
                        rect.Y0 + "，y1=" + rect.Y1 + "）。");
                rects.Add(rect);
            }
            return rects;
        }

        static float RequireSegmentNumber(JsonValue element, string field, string id, int index)
        {
            var value = element[field];
            if (value.kind != JsonValue.Kind.Number)
                throw new FormatException("地块 " + id + " 的第 " + (index + 1) + " 条 collisionRects 的 " + field +
                    " 必须是数字（实际" + value.Describe() + "）。");
            return value.AsFloat(0f);
        }

        // ---------------- 错误浮签（自己的显示层，不碰任何他人文件）----------------
        // FrontEndController.Notify 是私有方法，物件层调不到；坏配置若只进日志，
        // 玩家看到的就是"一个没有房子的世界"，而不是"配置坏了"。
        void ShowError(string message)
        {
            Debug.LogError("[WorldPropLayer] " + message);
            if (errorLabel != null) Destroy(errorLabel.gameObject);
            var canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null) return;

            var rect = new GameObject("PropLayer error", typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(canvas.transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
            rect.pivot = new Vector2(.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -24f);
            rect.sizeDelta = new Vector2(1400f, 64f);

            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = "物件层配置有误：" + message;
            label.fontSize = 26;
            label.color = new Color(.86f, .22f, .18f);
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            // 借场景里已有的 TMP 字体：自建字体资产要复制 FrontEndController 的一套流程，
            // 而这只是一条错误提示，不值得为它再建一份字体。
            var borrowed = BorrowFont();
            if (borrowed != null) label.font = borrowed;

            errorLabel = rect;
            Destroy(rect.gameObject, 5f);
        }

        static TMP_FontAsset BorrowFont()
        {
            // 借来的字体必须"能画中文"。物件层的提示全是中文，而它存在的全部意义就是
            // 让玩家看懂"配置坏了"——借到 TMP 默认字体（无汉字字形）会渲染成一排豆腐块，
            // 等于把唯一的报错通道也弄哑了。实测：坏配置浮签曾整条显示为 □□□□。
            foreach (var existing in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
            {
                var font = existing.font;
                if (CanDrawCjk(font)) return font;
            }

            // 自建的那份缓存住：每次报错都新建一张 2048×2048 图集是白扔几十兆显存，
            // 而且没有任何人销毁它。
            if (CanDrawCjk(propFont)) return propFont;

            // 场景里还没有能画中文的 TMP（例如物件层比主界面更早报错）：
            // 按 FrontEndController 同一配方从自带 Noto 动态建一份，不新增字体资产。
            var source = Resources.Load<Font>("Fonts/NotoSerifCJKsc-Regular");
            if (source == null) return null;
            propFont = TMP_FontAsset.CreateFontAsset(source, 48, 5,
                UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 2048, 2048,
                AtlasPopulationMode.Dynamic, true);
            if (propFont != null) propFont.name = "TwelveJade Noto Serif Dynamic (prop-layer)";
            return propFont;
        }

        static TMP_FontAsset propFont;

        /// <summary>这份字体到底画不画得出汉字。
        /// 不能只问 HasCharacter：动态字体的图集是按需填充的，刚建好时表里一个字形都没有，
        /// 用它当能力判据会把"其实能画中文"的字体一律拒掉。TryAddCharacters 问的才是
        /// "这份字体后头到底有没有这个字形"，顺手把字形补进图集也正是动态字体的用法。</summary>
        static bool CanDrawCjk(TMP_FontAsset font)
        {
            if (font == null) return false;
            return font.HasCharacter('物') || font.TryAddCharacters("物件误", out _);
        }

        // ---------------- 验收钩子（只供 FrontEndShotProps 调用，游戏逻辑不这么用）----------------

        /// <summary>验收用：地块数量（坏配置时为 0）。</summary>
        public int PlotCount => plots.Count;

        /// <summary>验收钩子（T032）：最近一条报错浮签用的字体到底画不画得出汉字。
        /// 豆腐块不是日志里的警告，是玩家唯一能看到的报错通道——断言必须打在浮签自己身上，
        /// 而不是"借字体那段代码看起来对"。</summary>
        public bool ErrorBannerDrawsCjkForCheck()
        {
            if (errorLabel == null) return false;
            var label = errorLabel.GetComponent<TextMeshProUGUI>();
            // 判据与 BorrowFont 共用同一个 CanDrawCjk：断言和生产用两套标准时，
            // "验收绿了、玩家看到的还是豆腐块"这种事故迟早会回来。
            return label != null && CanDrawCjk(label.font);
        }

        /// <summary>验收诊断（P-02）：摆放流水线状态。</summary>
        public bool PlacedForCheck => placed;
        public bool ConfigFailedForCheck => configFailed;
        public bool ParsedForCheck => parsed;
        public int UpdateTicksForCheck;
        public string LastGateForCheck = "init";

        /// <summary>验收用：地块 id 是否已摆放且仍存活。</summary>
        public bool HasPlot(string id)
        {
            foreach (var plot in plots)
                if (plot.Id == id && plot.Rect != null && plot.Rect.gameObject.activeInHierarchy) return true;
            return false;
        }

        /// <summary>验收用：地块脚底点的画布局部坐标（核对摆放位置）。</summary>
        public bool PlotAnchor(string id, out Vector2 anchored)
        {
            foreach (var plot in plots)
                if (plot.Id == id && plot.Rect != null)
                {
                    anchored = plot.Rect.anchoredPosition;
                    return true;
                }
            anchored = Vector2.zero;
            return false;
        }

        /// <summary>验收用：直接以给定 JSON 文本重解析，不走 Resources 缓存。
        /// 坏配置防御断言不能依赖"改磁盘文件 → Unity 重新导入"这条链——
        /// 批处理里导入时序不确定，Resources 缓存也可能不失效，断言会变成掷骰子。</summary>
        public void LoadPlotsFromTextForCheck(string json)
        {
            ClearPlaced();
            try
            {
                ParsePlots(json);
                parsed = true;
                configFailed = false;
            }
            catch (FormatException ex)
            {
                configFailed = true;
                ShowError(ex.Message);
            }
        }

        /// <summary>验收用：丢弃缓存重新从 Resources 解析正式配置（恢复现场）。</summary>
        public void ReloadForCheck()
        {
            var asset = Resources.Load<TextAsset>("Config/building-plots");
            if (asset != null) Resources.UnloadAsset(asset);
            ClearPlaced();
            warnedArt.Clear();
            // 坏配置断言跑完后必须能回到正常路径，否则后续断言全被这个闸门挡住。
            configFailed = false;
        }

        void OnDestroy()
        {
            foreach(var plot in plots)if(plot.GroundContact!=null)Destroy(plot.GroundContact.gameObject);
        }
        void ClearPlaced()
        {
            foreach (var plot in plots)
            {
                if (plot.Rect != null) Destroy(plot.Rect.gameObject);
                if (plot.GroundContact != null) Destroy(plot.GroundContact.gameObject);
            }
            plots.Clear();
            parsed = false;
            placed = false;
        }
    }
}
