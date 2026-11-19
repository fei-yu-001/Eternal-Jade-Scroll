using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using TwelveJade.Core;
using TwelveJade.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Editor
{
    // P-01 物件层 Play 验收：地块摆放、纵深排序、坏配置防御、遭遇表化开战。
    // 独立于 FrontEndShot / CombatShot（那两个归别的会话维护），批处理入口
    // TwelveJade.Editor.FrontEndShotProps.Capture，截图到 Tools/screenshots/props-*.png。
    //
    // P-02 追加（交互全流程）：点货郎 → 走过去 → 搭话 → 做买卖 → 回镇 →
    // 点遭遇金环 → 开战；外加 T026 乱序断言（fixture 故意乱序注入地块）。
    //
    // 纵深方向说明（与任务书文字描述的差异已核实，见 09_开发日志）：
    // 世界 y 越大 = 画面越靠下 = 离观察者越近。UpdateDepthOrder 把玩家插到第一个
    // BaseY 大于玩家 y 的实体之前，因此"玩家 y 比建筑大（更近）→ 玩家画在建筑之上"。
    // 任务书 B6-M6 把两个方向写反了，本文件按代码真实语义断言，两个方向都验。
    [InitializeOnLoad]
    public static class FrontEndShotProps
    {
        const int Width = 1920, Height = 1080;
        const string SessionKey = "FrontEndShotProps.Active";

        static FrontEndController controller;
        static WorldPropLayer propLayer;
        static Camera camera;
        static RenderTexture target;
        static string shotsDir;
        static readonly List<string> problems = new List<string>();
        static float runStart;
        static float phaseStart;
        static int slot;
        static int frames;
        static Phase phase;

        // ---- P-03 立绘 fixture 与战利品基线 ----
        static Texture2D portraitTexture;
        static Sprite portraitSprite;
        static int coinsBeforeSpoils;
        static int liveEncountersBefore;
        static string saveFilePath;
        static DateTime saveMtimeBefore;

        // 重入/抢断断言用的两个已知可站立点（本文件其他相位已经断言过它们可置位）。
        const float FarSpotX = 4260f, FarSpotY = 4020f;
        const float OtherSpotX = 3980f, OtherSpotY = 1512f;
        // 货郎的落点不写死：见 PeddlerHome()。
        const string BlackMarketNote = "入夜后才挪到双桥底下，独门货只在这里出";

        /// <summary>货郎的常驻点——一律从 town-map 现读，不在夹具里抄一份坐标。
        /// M2 按 ADR-030 重锚八景时他从 (3160,4980) 搬到 (3245,4820)，
        /// 抄死的那四条断言当场红：红的是配置快照，不是游戏（T092 同型）。</summary>
        static Vector2 PeddlerHome()
        {
            var npc = controller.World != null
                ? controller.World.Npcs.FirstOrDefault(n => n.Id == "huolang") : null;
            Check(npc != null, "断言前提：town-map.npcs 里应能找到货郎 huolang");
            return npc == null ? Vector2.zero : new Vector2(npc.X, npc.Y);
        }

        /// <summary>货郎正南 gap 单位处＝玩家"走到跟前"的那一点
        /// （与 FrontEndWorld.ArrivalPointNear 的首选项同方向，人脚下是他的圆形阻挡圆心，不可站）。</summary>
        static Vector2 SouthOfPeddler(float gap)
        {
            var home = PeddlerHome();
            return new Vector2(home.x, home.y + gap);
        }

        // 纵深断言用的地块与相对偏移：world y 大 = 近。
        const string DepthPlotId = "shop-north-street-01";
        const float DepthNearOffset = 300f;   // 玩家比地块更近
        const float DepthFarOffset = -300f;   // 玩家比地块更远

        // T026 乱序 fixture：三块地块按 footY **降序**声明（创建顺序与纵深序相反的最坏情形）。
        // M4 接线之后合成块自己也带根基带（缺 radius → 兜底 24），"zz-high 的 ±300 全都可站"
        // 这个老前提不再成立：北侧落点必须避开 aa-mid(3420) 自己的带，见 OrderNear 相位的 3380。
        const string OrderPlotId = "zz-high";
        static readonly string OrderFixtureJson =
            "{\"plots\":[" +
            "{\"id\":\"zz-high\",\"kind\":\"building\",\"footX\":4260,\"footY\":3720,\"width\":200,\"height\":200,\"art\":\"\"}," +
            "{\"id\":\"aa-mid\",\"kind\":\"tree\",\"footX\":4260,\"footY\":3420,\"width\":200,\"height\":200,\"art\":\"\"}," +
            "{\"id\":\"mm-low\",\"kind\":\"tree\",\"footX\":4260,\"footY\":3120,\"width\":200,\"height\":200,\"art\":\"\"}]}";

        enum Phase
        {
            Idle, Create, EnterTown, WaitPlots, DepthNear, DepthFar, BadConfig, Combat,
            // ---- P-02 交互链 ----
            InteractionStart, TownReady, TalkWalk, TalkBox, TradeOpen, BackTown, EncWalk, EncOpen,
            // ---- T026 乱序 ----
            OrderRestart, OrderInject, OrderPlace, OrderNear, OrderFar, OrderRestore,
            // ---- P-03：立绘槽位 / 防穿透 / 移动抢断 / 作息 / 战利品 ----
            P3Portrait, P3PortraitArt, P3PortraitFade, P3Blocked, P3Keys,
            P3SchedDay, P3Night, P3BlackTrade, P3SchedClosed, P3ClosedCheck, P3BlackWalk,
            P3Spoils, P3Fight, P3Persist, P3SpoilsBack,
            Done
        }

        static FrontEndShotProps()
        {
            if (!SessionState.GetBool(SessionKey, false)) return;
            shotsDir = SessionState.GetString(SessionKey + ".Dir", "");
            runStart = Time.realtimeSinceStartup;
            Attach();
        }

        static void Attach()
        {
            Application.logMessageReceived -= OnLog;
            EditorApplication.update -= Pump;
            Application.logMessageReceived += OnLog;
            EditorApplication.update += Pump;
        }

        public static void Capture()
        {
            SessionState.SetBool(SessionKey, true);
            var project = Directory.GetParent(Application.dataPath).FullName;
            var repoRoot = Directory.GetParent(Directory.GetParent(project).FullName).FullName;
            shotsDir = Path.Combine(repoRoot, "Tools", "screenshots");
            Directory.CreateDirectory(shotsDir);
            // 与 FrontEndShot / CombatShot 同一套临时存档约定：验收绝不写进玩家真实存档。
            var scratch = Path.Combine(Path.GetTempPath(), "twelve-jade-props-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            Environment.SetEnvironmentVariable("TWELVEJADE_SAVEDIR", scratch);
            SessionState.SetString(SessionKey + ".Dir", shotsDir);
            Debug.Log("[FrontEndShotProps] scratch saves at " + scratch);
            Attach();
            EditorSceneManager.OpenScene("Assets/Scenes/FrontEnd.unity", OpenSceneMode.Single);
            runStart = Time.realtimeSinceStartup;
            EditorApplication.isPlaying = true;
        }

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (!EditorApplication.isPlaying) return;
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            if (condition.StartsWith("[Licensing::Module]")) return;
            if (stackTrace != null && stackTrace.Contains("UnityEditor.Search")) return;
            // 下面两条是本验收**预期内**的错误：物件层坏配置提示与遭遇表缺 id 拒开战。
            // 它们正是被断言的行为，计进 problems 会让"验到了"变成"验收失败"。
            if (condition.Contains("[WorldPropLayer]")) return;
            if (condition.Contains("[FrontEndCombat] 遭遇表里没有")) return;
            problems.Add(type + ": " + condition);
        }

        static void Pump()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            try { Step(); }
            catch (Exception ex) { Fail("驱动异常: " + ex); }
        }

        static void Step()
        {
            // 批量模式下玩家循环节拍稀疏（见 Next() 注释），全流程真实时间比原估的长。
            // P-02 的 600 秒是按 22 个相位标定的；P-03 加了 15 个相位（立绘/射线/作息/战利品），
            // 看门狗按相位数量等比放到 1200 秒。这是测试基建自身的挂钟保护，不是验收阈值——
            // 它不会让任何一条断言更容易通过，只保证不在半途被自己掐死。
            if (Time.realtimeSinceStartup - runStart > 1200f) { Fail("验收超时:超过 1200 秒仍未完成"); return; }
            if (!EditorApplication.isPlaying) return;
            if (controller == null)
            {
                controller = UnityEngine.Object.FindAnyObjectByType<FrontEndController>();
                if (controller == null) return;
                SetupCapture();
                Debug.Log("[FrontEndShotProps] play mode ready");
            }
            if (propLayer == null)
                propLayer = UnityEngine.Object.FindAnyObjectByType<WorldPropLayer>();

            frames++;
            switch (phase)
            {
                case Phase.Idle:
                    Check(propLayer != null, "物件层 WorldPropLayer 应已自举挂载");
                    slot = FreeSlot();
                    Check(slot > 0, "应有空档位供验收使用");
                    controller.ShowCharacterCreation(slot);
                    Check(controller.CreateFromDraft(slot) != null, "建档成功");
                    Next(Phase.Create);
                    return;

                case Phase.Create:
                    controller.ShowTown();
                    Check(controller.CurrentPage == "town", "城镇页应已打开");
                    Next(Phase.EnterTown);
                    return;

                case Phase.EnterTown:
                    // 物件层是每帧轮询摆放的（世界根建好才摆），给它几帧。
                    if (frames < 10) return;
                    CheckPlots();
                    Capture("props-01-town");
                    SnapRelative(DepthNearOffset);
                    Next(Phase.WaitPlots);
                    return;

                case Phase.WaitPlots:
                    if (frames < 3) return;   // 等一帧让纵深排序真正落到 sibling 上
                    CheckDepth(DepthNearOffset);
                    SnapRelative(DepthFarOffset);
                    Next(Phase.DepthNear);
                    return;

                case Phase.DepthNear:
                    if (frames < 3) return;
                    CheckDepth(DepthFarOffset);
                    Capture("props-02-behind");
                    Next(Phase.DepthFar);
                    return;

                case Phase.DepthFar:
                    CheckBadConfig();
                    Next(Phase.BadConfig);
                    return;

                case Phase.BadConfig:
                    // 恢复正式配置后要等物件层重新摆一轮。
                    if (frames < 10) return;
                    Check(propLayer != null && propLayer.HasPlot(DepthPlotId), "恢复正式配置后地块应重新摆出");
                    CheckCombat();
                    Next(Phase.Combat);
                    return;

                case Phase.Combat:
                    Next(Phase.InteractionStart);
                    return;

                // ---------------- P-02 交互链 ----------------

                case Phase.InteractionStart:
                    // P-01 流程结束时页面停在 combat：回世界页开交互链。
                    controller.ShowTown();
                    Next(Phase.TownReady);
                    return;

                case Phase.TownReady:
                    // 等世界页重建（物件层重摆要玩家循环心跳落地）。
                    if (!PlayerTicksWaited(3))
                    {
                        if (WaitTimedOut(90f)) { Check(false, "回世界页后 90 秒未见玩家循环推进"); Next(Phase.Done); }
                        return;
                    }
                    Check(ActiveObjects("Placeable npc-huolang") == 1, "货郎应已摆进世界（Placeable npc-huolang）");
                    Check(HasComponent<RawImage>("Placeable npc-huolang"), "货郎应挂真立绘（RawImage）");
                    Check(ActiveObjects("Placeable enc-encounter-boar-01") == 1, "遭遇金环应已摆进世界（Placeable enc-encounter-boar-01）");
                    var peddlerPoint = PeddlerHome();
                    Check(!controller.SnapTravelerTo(peddlerPoint.x, peddlerPoint.y),
                        "货郎位置不可重叠站立（npc radius 应进 CanStand 阻挡）",
                        "(" + peddlerPoint.x + "," + peddlerPoint.y + ")");
                    Capture("p02-01-town");
                    // 快进到货郎南边再点（置位失败就留在原地走过去，都不影响结论）。
                    var near = SouthOfPeddler(160f);
                    var nearer = SouthOfPeddler(80f);
                    if (!controller.SnapTravelerTo(near.x, near.y))
                        controller.SnapTravelerTo(nearer.x, nearer.y);
                    var npcButton = FindObjectButton("Placeable npc-huolang");
                    Check(npcButton != null, "货郎 placeable 应可点击（Button）");
                    if (npcButton != null)
                    {
                        npcButton.onClick.Invoke();
                        // onClick 同步走 RequestTravelTo：此刻就该有走向目标在途。
                        Check(controller.TravelBusy, "点击货郎后应有走向目标在途（TravelBusy）");
                    }
                    Next(Phase.TalkWalk);
                    return;

                case Phase.TalkWalk:
                    if (frames < 3) return;
                    if (controller.TravelBusy)
                    {
                        if (Time.realtimeSinceStartup - phaseStart > 45f)
                        {
                            Check(false, "走向货郎超时（45 秒仍未到达）");
                            Next(Phase.Done);
                        }
                        return;
                    }
                    Next(Phase.TalkBox);
                    return;

                case Phase.TalkBox:
                    // 到达事件已 raise，搭话框由 FrontEndTrade.TalkTo 弹出。
                    if (!PlayerTicksWaited(2)) return;
                    Check(ActiveObjects("Talk box") == 1, "到达后应出现搭话框（Talk box）");
                    Check(AnyText("货郎老栓"), "搭话框应显示货郎名字（货郎老栓）");
                    Capture("p02-02-talk");
                    FindActiveButton("做 买 卖")?.onClick.Invoke();
                    Next(Phase.TradeOpen);
                    return;

                case Phase.TradeOpen:
                    if (!PlayerTicksWaited(1)) return;
                    Check(controller.CurrentPage == "trade", "做买卖应打开交易页",
                        "实际 " + controller.CurrentPage);
                    Check(controller.TradeMerchantId == "huolang", "交易商户应是货郎（huolang）",
                        "实际 " + controller.TradeMerchantId);
                    FindActiveButton("回到青石镇")?.onClick.Invoke();
                    Next(Phase.BackTown);
                    return;

                case Phase.BackTown:
                    if (!PlayerTicksWaited(3))
                    {
                        if (WaitTimedOut(90f)) { Check(false, "回到青石镇后 90 秒未见玩家循环推进"); Next(Phase.Done); }
                        return;
                    }
                    Check(controller.CurrentPage == "town", "回到青石镇应回到世界页",
                        "实际 " + controller.CurrentPage);
                    Check(propLayer != null && propLayer.HasPlot(DepthPlotId), "回镇后物件层应已重摆");
                    // 快进到金环附近再点（foothill 地标是已知可站立点，作回退）。
                    var atRing = controller.SnapTravelerTo(3980f, 1512f);
                    if (!atRing) controller.SnapTravelerTo(4096f, 2100f);
                    // 金环在世界页的可见证据——必须在**点它之前**拍。
                    // 到达回调会直接 ShowCombat 切页，"走到了但还在世界页"这个状态用
                    // TravelBusy 观测不到（它转 false 时页面已经是战斗页），拍晚了就是战斗页。
                    // SnapTravelerTo 同帧同步了镜头/小地图/纵深，所以这一帧就能拍到位。
                    Check(atRing, "金环旁应可置位（世界页金环截图的前提）");
                    if (atRing) Capture("p02-04-ring-in-world");
                    var ringButton = FindObjectButton("Placeable enc-encounter-boar-01");
                    Check(ringButton != null, "遭遇金环应可点击（Button）");
                    if (ringButton != null)
                    {
                        ringButton.onClick.Invoke();
                        Check(controller.TravelBusy, "点击金环后应有走向目标在途（TravelBusy）");
                    }
                    Next(Phase.EncWalk);
                    return;

                case Phase.EncWalk:
                    if (frames < 3) return;
                    if (controller.TravelBusy)
                    {
                        if (Time.realtimeSinceStartup - phaseStart > 45f)
                        {
                            Check(false, "走向遭遇金环超时（45 秒仍未到达）");
                            Next(Phase.Done);
                        }
                        return;
                    }
                    Next(Phase.EncOpen);
                    return;

                case Phase.EncOpen:
                    if (!PlayerTicksWaited(2)) return;
                    Check(controller.CurrentPage == "combat", "点金环走近后应开战",
                        "实际 " + controller.CurrentPage);
                    Check(controller.CombatFoe != null && controller.CombatFoe.Id == "boar-demon",
                        "战斗敌人应来自遭遇表（boar-demon）",
                        controller.CombatFoe == null ? "无敌人" : controller.CombatFoe.Id);
                    Capture("p02-03-combat");
                    Next(Phase.OrderRestart);
                    return;

                // ---------------- T026 乱序断言 ----------------

                case Phase.OrderRestart:
                    // 深度断言要在世界页做（物件层只在 town 页摆放）。
                    controller.ShowTown();
                    Next(Phase.OrderInject);
                    return;

                case Phase.OrderInject:
                    // 先让第 4 次世界页重建的重摆在玩家循环里落地，再注入乱序 fixture——
                    // 否则 ClearPlaced 的延迟 Destroy 与 PlaceAll 会全部悬空（首轮教训）。
                    if (!PlayerTicksWaited(3))
                    {
                        if (WaitTimedOut(90f)) { Check(false, "乱序注入前 90 秒未见玩家循环推进"); Next(Phase.Done); }
                        return;
                    }
                    Debug.Log("[FrontEndShotProps] order-inject 前: page=" + controller.CurrentPage +
                        " plots=" + propLayer.PlotCount + " placed=" + propLayer.PlacedForCheck +
                        " parsed=" + propLayer.ParsedForCheck + " cfgFailed=" + propLayer.ConfigFailedForCheck +
                        " realShop=" + ActiveObjects("Placeable " + DepthPlotId));
                    // 故意乱序注入：创建顺序 zz-high(3720) → aa-mid(3420) → mm-low(3120)，
                    // 与 footY 升序相反——T026 根治前这种摆放会整体错位。
                    propLayer.LoadPlotsFromTextForCheck(OrderFixtureJson);
                    Debug.Log("[FrontEndShotProps] order-inject 后: plots=" + propLayer.PlotCount +
                        " parsed=" + propLayer.ParsedForCheck + " cfgFailed=" + propLayer.ConfigFailedForCheck);
                    Next(Phase.OrderPlace);
                    return;

                case Phase.OrderPlace:
                    // fixture 的 PlaceAll 与被 ClearPlaced 销毁的旧地块，都要等玩家循环落地。
                    if (!PlayerTicksWaited(3))
                    {
                        if (WaitTimedOut(90f)) { Check(false, "乱序摆放 90 秒未见玩家循环推进"); Next(Phase.Done); }
                        return;
                    }
                    Debug.Log("[FrontEndShotProps] order-place: page=" + controller.CurrentPage +
                        " plots=" + propLayer.PlotCount + " placed=" + propLayer.PlacedForCheck +
                        " zz=" + ActiveObjects("Placeable " + OrderPlotId) +
                        " realShop=" + ActiveObjects("Placeable " + DepthPlotId) +
                        " npc=" + ActiveObjects("Placeable npc-huolang") +
                        " frameCount=" + Time.frameCount + " wplTicks=" + propLayer.UpdateTicksForCheck +
                        " gate=" + propLayer.LastGateForCheck);
                    Check(ActiveObjects("Placeable " + OrderPlotId) == 1, "乱序 fixture 应摆出 " + OrderPlotId);
                    Check(controller.SnapTravelerTo(4260f, 4020f), "乱序断言：玩家置位到 zz-high 南侧应成功");
                    Next(Phase.OrderNear);
                    return;

                case Phase.OrderNear:
                    if (!PlayerTicksWaited(1)) return;
                    CheckDepthAgainst(OrderPlotId, expectPlayerAbove: true);
                    Check(controller.SnapTravelerTo(4260f, 3380f),
                        "乱序断言：玩家置位到 zz-high 北侧应成功",
                        "3380 = aa-mid(3420) 的根基带以北；M4 之后合成块也是实体，站不回它自己的地基线上");
                    Next(Phase.OrderFar);
                    return;

                case Phase.OrderFar:
                    if (!PlayerTicksWaited(1)) return;
                    CheckDepthAgainst(OrderPlotId, expectPlayerAbove: false);
                    propLayer.ReloadForCheck();
                    Next(Phase.OrderRestore);
                    return;

                case Phase.OrderRestore:
                    if (!PlayerTicksWaited(3))
                    {
                        if (WaitTimedOut(90f)) { Check(false, "乱序恢复后 90 秒未见玩家循环推进"); Next(Phase.Done); }
                        return;
                    }
                    Check(propLayer != null && propLayer.HasPlot(DepthPlotId), "乱序断言恢复后正式地块应重新摆出");
                    Next(Phase.P3Portrait);
                    return;

                // ---------------- P-03 M2：ADR-025 对话立绘槽位（降级 → 有图 → 淡入）----------------

                case Phase.P3Portrait:
                {
                    var huolang = controller.World?.Npcs.FirstOrDefault(n => n.Id == "huolang");
                    Check(huolang != null, "地图上应查到货郎（立绘断言的前提）");
                    Check(controller.PortraitResourcePaths.Contains("Art/Portraits/"),
                        "立绘加载路径应符合 ADR-025（Resources/Art/Portraits/）");
                    // 前提变更（D-21 落地后）：五张灵魂 NPC 立绘已入库且生产端改走
                    // Load<Texture2D> + Sprite.Create，"仓库里还没有立绘资产"这个老前提已经过期。
                    // 按 D-13 改两态：走哪一态由这张图现在加载得到吗现场决定，两态都断言。
                    var realPortrait = PortraitLoaded("huolang");
                    try
                    {
                        controller.TalkTo("huolang");
                        Check(true, realPortrait
                            ? "立绘已入库，TalkTo 应走有图分支（不抛异常）"
                            : "仓库里还没有立绘资产，TalkTo 应平滑降级而不是抛异常");
                    }
                    catch (Exception ex)
                    {
                        Check(false, "TalkTo 不应抛异常（无论立绘在不在库里）",
                            ex.GetType().Name + ": " + ex.Message);
                    }
                    Check(controller.TalkPortraitSlotExists, "对话应预留立绘槽位节点（虚位以待，美术灌进来即生效）");
                    Check(controller.TalkPortraitShown == realPortrait,
                        "挂不挂立绘图形必须与「这张立绘在不在库里」一致（库里没有就绝不显示白块）",
                        "库里立绘=" + realPortrait + " 实际挂出=" + controller.TalkPortraitShown);
                    Check(ActiveObjects("Talk box") == 1, "这一态的对话框应在场");
                    CheckTalkComposition("搭话首帧");
                    Check(huolang != null && controller.TalkLineText == huolang.Line,
                        "对话正文应是 NPC 原台词", controller.TalkLineText);
                    Next(Phase.P3PortraitArt);
                    return;
                }

                case Phase.P3PortraitArt:
                {
                    CloseTalk();
                    portraitTexture = MakePortraitTexture();
                    portraitSprite = Sprite.Create(portraitTexture,
                        new Rect(0, 0, PortraitArtSize.x, PortraitArtSize.y), new Vector2(.5f, .5f), 100f);
                    controller.SetPortraitFixtureForCheck("huolang", portraitSprite);
                    controller.TalkTo("huolang");
                    Check(controller.TalkPortraitShown, "注入立绘后应挂出立绘图形（ADR-025 有图分支）");
                    Check(controller.TalkPortraitPreservesAspect, "立绘应保宽高比（preserveAspect）");
                    var size = controller.TalkPortraitSize;
                    Check(Mathf.Abs(size.x - 512f) < 1f && Mathf.Abs(size.y - 768f) < 1f,
                        "对话立绘槽位应是 Type-A 规格 512×768", size.ToString());
                    var composed = controller.TalkBoxGeometry;
                    Check(Mathf.Abs(composed.x - 580f) < 1f && Mathf.Abs(composed.z - 1200f) < 1f,
                        "有立绘时对话框应让位到右侧（x=580 宽=1200，左立绘 + 右对话框）", composed.ToString());
                    Check(controller.TalkPortraitHasFade, "立绘应带淡入通道（CanvasGroup）");
                    Next(Phase.P3PortraitFade);
                    return;
                }

                case Phase.P3PortraitFade:
                {
                    // 淡入按绝对时刻算，只要玩家循环补上一帧就该到位；等不到就是真问题，不放过。
                    if (controller.TalkPortraitAlpha < .999f)
                    {
                        if (WaitTimedOut(25f))
                            Check(false, "立绘淡入 25 秒仍未到全不透明", "alpha=" + controller.TalkPortraitAlpha);
                        else return;
                    }
                    else Check(true, "立绘淡入应到达全不透明（CanvasGroup alpha=1）");
                    Capture("p03-01-portrait");
                    // 降级必须是可逆的：清掉注入后重新搭话，应回到"库里有没有这张图"那一态，
                    // 不许留下注入残留（既不许凭空挂出，也不许把真立绘一起清没）。
                    controller.ClearPortraitFixtureForCheck();
                    CloseTalk();
                    controller.TalkTo("huolang");
                    var backToReal = PortraitLoaded("huolang");
                    Check(controller.TalkPortraitShown == backToReal,
                        "清掉立绘注入后应回到「库里有没有这张图」那一态（降级可逆，不留注入残留）",
                        "库里立绘=" + backToReal + " 实际挂出=" + controller.TalkPortraitShown);
                    CheckTalkComposition("清掉注入后重新搭话");
                    Next(Phase.P3Blocked);
                    return;
                }

                // ---------------- P-03 M3：T031 真实射线防穿透 ----------------

                case Phase.P3Blocked:
                {
                    CloseTalk();
                    // 射线打的是屏幕点：货郎不在视口里时，投影点落在画面外，全屏遮罩也"盖不住"他，
                    // 拦截断言就会假阴性。先把玩家挪到他跟前（这个点位在 P-02 段落已验过可站立）。
                    var inFront = SouthOfPeddler(160f);
                    Check(controller.SnapTravelerTo(inFront.x, inFront.y),
                        "防穿透断言前玩家应能置位到货郎跟前（保证他在视口内）",
                        "(" + inFront.x + "," + inFront.y + ")");
                    Check(!controller.TownPointerBlockedByUiAt("huolang"),
                        "遮罩没开时，货郎的屏幕点不该被判成「被 UI 挡住」");
                    Debug.Log("[FrontEndShotProps] T031 探针（遮罩关）: " + controller.TownRaycastProbeAt("huolang"));
                    controller.TalkTo("huolang");
                    // 模态是**这一帧**建出来的：CanvasRenderer 还没进过一次渲染时，
                    // GraphicRaycaster 不收它（首轮教训：几何判定 inside=True，射线 hits 却只有 placeable）。
                    // Capture 会真的走一遍 camera.Render()，把 UI 并进渲染树——真实玩家每帧都经历这一步。
                    Capture("p03-04-blocked");
                    Debug.Log("[FrontEndShotProps] T031 探针（遮罩开）: " + controller.TownRaycastProbeAt("huolang"));
                    Check(controller.TownPointerBlockedByUiAt("huolang"),
                        "对话遮罩开着时，货郎应被真实射线判成被 UI 挡住（T031）");
                    var coveredNpc = FindObjectButton("Placeable npc-huolang");
                    Check(coveredNpc != null, "遮罩下货郎 placeable 仍应在场（只是点不动）");
                    coveredNpc?.onClick.Invoke();
                    Check(!controller.TravelBusy, "遮罩下点 NPC 不该向世界发移动指令（防穿透）");
                    CloseTalk();
                    Check(!controller.TownPointerBlockedByUiAt("huolang"), "关掉对话框后应解除拦截");
                    coveredNpc?.onClick.Invoke();
                    Check(controller.TravelBusy, "关掉对话框后点同一位货郎应能正常发起走向");
                    Next(Phase.P3Keys);
                    return;
                }

                // ---------------- P-03 M4：T033 键盘抢断与寻路重入 ----------------

                case Phase.P3Keys:
                {
                    Check(controller.TravelBusy, "键盘抢断前提：应有走向在途");
                    Check(controller.TownKeyboardTakeoverForCheck(), "按移动键应作废在途走向（T033）");
                    Check(!controller.TravelBusy, "抢断后 TravelBusy 应归 false");
                    Check(!controller.TownWalkingToNpc, "抢断后不该仍报「走向中」");

                    var fired = 0;
                    System.Action<string> count = id => fired++;
                    Check(controller.TerrainStandable(FarSpotX, FarSpotY) &&
                          controller.TerrainStandable(OtherSpotX, OtherSpotY),
                        "抢断/重入断言用的两个目标点应可站立");
                    Check(controller.RequestTravelTo(new Vector2(FarSpotX, FarSpotY), count, "a"),
                        "发起一趟远距离走向应成功");
                    Check(controller.TownKeyboardTakeoverForCheck(), "键盘抢断应再次生效");
                    Check(fired == 0, "被抢断的到达回调不该被触发（半路弹对话框比不响应更怪）", "触发次数=" + fired);

                    Check(controller.RequestTravelTo(new Vector2(FarSpotX, FarSpotY), count, "a2"),
                        "重入前提：先发起一趟走向");
                    var peddlerFeet = PeddlerHome();
                    Check(!controller.RequestTravelTo(peddlerFeet, count, "bad"),
                        "货郎脚下被自身阻挡占着，不可达目标应返回 false",
                        "(" + peddlerFeet.x + "," + peddlerFeet.y + ")");
                    Check(controller.TravelBusy, "不可达的请求不该打断当前在途路线");
                    Check(controller.RequestTravelTo(new Vector2(OtherSpotX, OtherSpotY), count, "b"),
                        "寻路途中改点新目标应成功（覆盖语义）");
                    Check(fired == 0 && controller.TravelBusy,
                        "重入应静默丢弃旧回调并改向新目标", "触发次数=" + fired);
                    controller.TownKeyboardTakeoverForCheck();
                    Next(Phase.P3SchedDay);
                    return;
                }

                // ---------------- P-03 M5：货郎作息（市集 ↔ 双桥黑市）----------------

                case Phase.P3SchedDay:
                {
                    if (!WaitTownTicks("白天作息")) return;
                    Check(controller.SetWorldMinuteForCheck(600), "应能把世界时间拨到 10:00");
                    Next(Phase.P3Night);
                    return;
                }

                case Phase.P3Night:
                {
                    if (!WaitTownTicks("黑市落位")) return;
                    var day = controller.TownNpcFootPosition("huolang");
                    var homePoint = PeddlerHome();
                    Check(NearPoint(day, homePoint.x, homePoint.y),
                        "白天 10:00 货郎应在 town-map 的常驻点（无作息窗口指走他）",
                        "常驻 (" + homePoint.x + "," + homePoint.y + ") 实际 " + Point(day));
                    Check(controller.TownNpcOnDuty("huolang"), "白天窗口内货郎应在班");
                    Check(controller.SetWorldMinuteForCheck(1380), "应能把世界时间拨到 23:00（黑市窗口）");
                    Next(Phase.P3BlackTrade);
                    return;
                }

                case Phase.P3BlackTrade:
                {
                    if (!WaitTownTicks("黑市台词")) return;
                    var night = controller.TownNpcFootPosition("huolang");
                    Check(NearPoint(night, 4096f, 2560f), "黑市窗口货郎应迁到双桥桥头 (4096,2560)", Point(night));
                    Check(controller.TownNpcOnDuty("huolang"), "黑市窗口内货郎应在班");
                    // 黑市两个条件缺一不可：窗口 + 命格。命格直接写进临时档（ActiveSave 是公开的）。
                    controller.ActiveSave.destiny = "shaxing";
                    controller.TalkTo("huolang");
                    Check(controller.TalkLineText == BlackMarketNote,
                        "黑市时段对话应显示窗口按语，而不是白日台词", controller.TalkLineText);
                    var peek = FindActiveButton("看 看 货");
                    Check(peek != null, "黑市时段对话按钮应是【看看货】");
                    Check(FindActiveButton("做 买 卖") == null, "黑市时段不该再出现白日【做买卖】");
                    Capture("p03-02-blackmarket");
                    peek?.onClick.Invoke();
                    Next(Phase.P3SchedClosed);
                    return;
                }

                case Phase.P3SchedClosed:
                {
                    if (!PlayerTicksWaited(2))
                    {
                        if (WaitTimedOut(90f)) { Check(false, "黑市货架 90 秒未见玩家循环推进"); Next(Phase.Done); }
                        return;
                    }
                    Check(controller.CurrentPage == "trade", "【看看货】应打开交易页", controller.CurrentPage);
                    Check(controller.TradeOnBlackMarket, "从黑市窗口进去的应是黑市货架");
                    Check(AnyText("黑市：价钱贵四成，独门货只在夜里出。"), "黑市页应有黑市提示行");
                    FindActiveButton("回到青石镇")?.onClick.Invoke();
                    Check(controller.SetWorldMinuteForCheck(1200), "应能拨到 20:00（两个窗口之间的空档）");
                    Next(Phase.P3ClosedCheck);
                    return;
                }

                case Phase.P3ClosedCheck:
                {
                    if (!WaitTownTicks("收摊时段")) return;
                    Check(controller.CurrentPage == "town", "回镇后应在世界页", controller.CurrentPage);
                    Check(!controller.TownNpcOnDuty("huolang"), "20:00 既不在市集也不在黑市窗口，应判定收摊");
                    var home = controller.TownNpcFootPosition("huolang");
                    var fallbackHome = PeddlerHome();
                    Check(NearPoint(home, fallbackHome.x, fallbackHome.y),
                        "无窗口时段应回退到地图常驻点",
                        "常驻 (" + fallbackHome.x + "," + fallbackHome.y + ") 实际 " + Point(home));
                    controller.TalkTo("huolang");
                    Check(FindActiveButton("做 买 卖") == null, "收摊时段不该给下单入口");
                    Check(FindActiveButton("告 辞") != null, "收摊时段仍应能搭话（人在，只是不做买卖）");
                    CloseTalk();
                    Check(controller.SetWorldMinuteForCheck(1380), "应能再拨回黑市窗口验寻路");
                    Next(Phase.P3BlackWalk);
                    return;
                }

                case Phase.P3BlackWalk:
                {
                    if (!WaitTownTicks("桥头寻路")) return;
                    controller.SnapTravelerTo(4096f, 2100f);
                    var bridgeButton = FindObjectButton("Placeable npc-huolang");
                    Check(bridgeButton != null, "黑市时段双桥桥头应有货郎 placeable");
                    bridgeButton?.onClick.Invoke();
                    Check(controller.TravelBusy, "点桥头货郎应能发起寻路（迁移后仍走得到脚下）");
                    controller.TownKeyboardTakeoverForCheck();
                    Next(Phase.P3Spoils);
                    return;
                }

                // ---------------- P-03 M6/M7：战利品入包落盘 + 战斗特写降级 ----------------

                case Phase.P3Spoils:
                {
                    if (!WaitTownTicks("开战前")) return;
                    Check(controller.SetWorldMinuteForCheck(600), "作息断言完，拨回白天再收口");
                    liveEncountersBefore = controller.TownLiveEncounterCount;
                    Check(liveEncountersBefore >= 1, "讨伐前应有活着的遭遇点", "实际 " + liveEncountersBefore);
                    Check(ActiveObjects("Placeable enc-encounter-boar-01") == 1, "讨伐前金环应在大地图上");
                    coinsBeforeSpoils = controller.TradeCoins;
                    saveFilePath = Path.Combine(
                        Environment.GetEnvironmentVariable("TWELVEJADE_SAVEDIR") ?? "", "slot-" + slot + ".json");
                    Check(File.Exists(saveFilePath), "结算前应已有档位文件（建档即落盘）", saveFilePath);
                    saveMtimeBefore = File.GetLastWriteTimeUtc(saveFilePath);
                    controller.ShowCombat("encounter-boar-01");
                    Next(Phase.P3Fight);
                    return;
                }

                case Phase.P3Fight:
                {
                    Check(controller.CurrentPage == "combat", "已知遭遇 id 应开战", controller.CurrentPage);
                    // T090 之后这一条才真的会红。改加载口径之前它写的是 `!CombatPortraitShown`，
                    // 而那一支用的是 Load<Sprite> 取 Default 档资源——Unity 里恒返 null，
                    // 于是"没走特写"是恒真、不是判据：实现坏了它也不会红。现在按盘上有没有 Type-C 图两态断（D-13）。
                    var typeCExists = Resources.Load<Texture2D>("Art/Combat/" + controller.CombatFoe.Id) != null;
                    Check(controller.CombatPortraitShown == typeCExists,
                        "有 Type-C 特写图就该走真图，没有就该走剪影降级（不许白块，也不许有图还降级）",
                        "盘上 Type-C " + (typeCExists ? "有" : "没有") + " · 页面走 " +
                        (controller.CombatPortraitShown ? "特写" : "降级"));
                    var scale = controller.CombatFoeScale;
                    if (typeCExists)
                        Check(scale >= 1f && scale <= 1.031f,
                            "特写档的待机呼吸应落在 1.000~1.030 之间", "scale=" + scale);
                    else
                        Check(Mathf.Abs(scale - 1f) < .001f, "降级路径不该带呼吸缩放", "scale=" + scale);
                    for (var i = 0; i < 12 && !controller.CombatFoe.IsDead; i++)
                    {
                        controller.CombatPlaceForCheck(-0.4f, 0.5f);
                        FindActiveButton("重 击")?.onClick.Invoke();
                        controller.CombatClockAdvance(0.78f);
                    }
                    Check(controller.CombatFoe.IsDead, "重击循环应击倒野猪妖");
                    Check(controller.CombatSettled, "击倒后战斗应结算");
                    Check(controller.TradeCoins == coinsBeforeSpoils + 24,
                        "胜利赏钱应实打实进钱包（+24 文）", controller.TradeCoins + " vs " + (coinsBeforeSpoils + 24));
                    Check(controller.CurrentBagCount("larou") >= 2, "腊肉×2 应入行囊",
                        "实际 " + controller.CurrentBagCount("larou"));
                    Check(controller.CurrentBagCount("caoyao") >= 3, "药草×3 应入行囊",
                        "实际 " + controller.CurrentBagCount("caoyao"));
                    var spoils = controller.LastSpoils;
                    Check(spoils != null && spoils.EverythingFits, "本次掉落应全部装得下（溢出分支由 Core 的 Describe 报余量）");
                    Check(AnyText("野猪妖 倒下了"), "结算面板应报出倒下的是谁");
                    Capture("p03-03-spoils");
                    Next(Phase.P3Persist);
                    return;
                }

                case Phase.P3Persist:
                {
                    // 落盘要读磁盘，不能只信内存里那份：内存改了、磁盘没写是最常见的假绿。
                    var info = controller.Repository.Read(slot);
                    var disk = info.Data;
                    Check(disk != null, "结算后磁盘上应能读回档位");
                    Check(disk != null && disk.coins == coinsBeforeSpoils + 24,
                        "磁盘存档里的铜钱应已 +24", disk == null ? "无档" : disk.coins + "");
                    Check(disk != null && InventoryRules.Count(disk.bag, "larou") == 2 &&
                          InventoryRules.Count(disk.bag, "caoyao") == 3,
                        "磁盘存档里应有腊肉×2、药草×3");
                    Check(disk != null && disk.HasFlag("encounter-boar-01"), "讨伐标记应记进存档");
                    var mtimeAfter = File.GetLastWriteTimeUtc(saveFilePath);
                    Check(mtimeAfter > saveMtimeBefore, "胜利结算后存档文件修改时间应被刷新",
                        saveMtimeBefore.ToString("O") + " → " + mtimeAfter.ToString("O"));
                    FindActiveButton("返回青石镇")?.onClick.Invoke();
                    Next(Phase.P3SpoilsBack);
                    return;
                }

                case Phase.P3SpoilsBack:
                {
                    if (!WaitTownTicks("讨伐后回镇")) return;
                    Check(controller.CurrentPage == "town", "结算面板回镇应回世界页", controller.CurrentPage);
                    Check(controller.TownLiveEncounterCount == liveEncountersBefore - 1,
                        "讨伐后活遭遇应少一个", controller.TownLiveEncounterCount + " vs " + (liveEncountersBefore - 1));
                    Check(ActiveObjects("Placeable enc-encounter-boar-01") == 0,
                        "已讨伐的遭遇不该再摆回大地图（否则玩家以为奖励没生效）");
                    Check(ActiveObjects("Placeable npc-huolang") == 1, "回镇后货郎仍应正常摆放（没被连带清掉）");
                    Next(Phase.Done);
                    return;
                }

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
            // 玩家循环节拍在批量模式下很稀疏（一次 Pump 未必带一次 MonoBehaviour Update，
            // 编辑器帧数 ≠ 玩家帧数）。需要等"摆放/销毁真正落地"的相位一律以物件层的
            // Update 心跳为基准（waitBaseTicks），不再数编辑器帧。
            waitBaseTicks = propLayer == null ? 0 : propLayer.UpdateTicksForCheck;
        }

        static int waitBaseTicks;

        /// <summary>是否已等够 n 次物件层 Update（玩家循环心跳）——摆放/延迟销毁都已落地。</summary>
        static bool PlayerTicksWaited(int n)
        {
            return propLayer != null && propLayer.UpdateTicksForCheck >= waitBaseTicks + n;
        }

        /// <summary>本相位真实时间是否超时（玩家循环意外停摆时兜底，不准死等）。</summary>
        static bool WaitTimedOut(float seconds)
        {
            return Time.realtimeSinceStartup - phaseStart > seconds;
        }

        /// <summary>等世界页重建/摆放落地（以玩家循环心跳为准），超时记一条失败并收尾。</summary>
        static bool WaitTownTicks(string what)
        {
            if (PlayerTicksWaited(3)) return true;
            if (!WaitTimedOut(90f)) return false;
            Check(false, what + "：90 秒未见玩家循环推进，后续断言无法进行");
            Next(Phase.Done);
            return false;
        }

        static void CloseTalk() => FindActiveButton("告 辞")?.onClick.Invoke();

        static bool NearPoint(Vector2? p, float x, float y) =>
            p.HasValue && Mathf.Abs(p.Value.x - x) < 1f && Mathf.Abs(p.Value.y - y) < 1f;

        static string Point(Vector2? p) => p.HasValue ? p.Value.ToString() : "(查不到)";

        static readonly Vector2Int PortraitArtSize = new(512, 768);

        /// <summary>造一张 Type-A 规格的假立绘：越往下越透明，模拟"下方自然截断"，
        /// 这样 p03-01 截图能一眼看出"左立绘 + 右对话框"的构图，而不是一个纯色矩形。</summary>
        static Texture2D MakePortraitTexture()
        {
            var tex = new Texture2D(PortraitArtSize.x, PortraitArtSize.y, TextureFormat.RGBA32, false);
            var pixels = new Color32[PortraitArtSize.x * PortraitArtSize.y];
            for (var y = 0; y < PortraitArtSize.y; y++)
            {
                // 纹理第 0 行在底部：底部透明、上半身实心，才像"立在框边上"。
                var alpha = (byte)(255 * Mathf.Clamp01(y / (float)PortraitArtSize.y * 2.4f));
                var color = new Color32(0x6c, 0x4a, 0x2f, alpha);
                for (var x = 0; x < PortraitArtSize.x; x++) pixels[y * PortraitArtSize.x + x] = color;
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        // ---------------- 断言 ----------------

        static void CheckPlots()
        {
            var plots = Resources.Load<TextAsset>("Config/building-plots");
            Check(plots != null, "building-plots.json 应可加载");

            // 三个 "Placeable <id>" 全部存在（PlaceInWorld 的命名前缀）。
            foreach (var id in new[] { "shop-north-street-01", "old-willow-riverside-01", "bamboo-market-01" })
            {
                Check(ActiveObjects("Placeable " + id) == 1, "地块对象应存在：Placeable " + id,
                    "实际 " + ActiveObjects("Placeable " + id) + " 个");
                Check(propLayer != null && propLayer.HasPlot(id), "物件层应记录地块 " + id);
            }

            // 真图 / 色块分支：有合格立绘的挂 RawImage，抠底不合格或缺失的挂 Image 色块。
            // 这条断言的意义是把"素材到底接上没有"钉死——色块和真图在整屏截图里都能看，
            // 但只有组件类型能证明走的是哪条路。
            //
            // 走哪条分支必须由"这张图现在到底加载得到吗"现场决定，不能写死在夹具里（D-13）：
            // 老柳长期缺图（T025），当年把它钉成"必走色块"是对的；图补齐入库后前提过期，
            // 夹具自己变红灯——红的是断言前提，不是画面（T037/T056 同型）。
            // 缺图分支没有被删掉：它在这里照样是一条完整断言，只是由现场分支决定。
            CheckArtBranch("shop-north-street-01", "Art/Props/shop-front");
            CheckArtBranch("bamboo-market-01", "Art/Props/bamboo");
            CheckArtBranch("old-willow-riverside-01", "Art/Props/willow");

            // 位置：anchoredPosition = (footX, -footY) × worldScale（UiKit y 轴向下）。
            var scale = controller.WorldScale;
            Vector2 anchor;
            if (propLayer != null && propLayer.PlotAnchor(DepthPlotId, out anchor))
            {
                Check(Mathf.Abs(anchor.x - 4260f * scale) <= 2f, "地块 X 应落在 footX", anchor.x + " vs " + 4260f * scale);
                Check(Mathf.Abs(anchor.y - (-3720f * scale)) <= 2f, "地块 Y 应落在 footY", anchor.y + " vs " + (-3720f * scale));
            }
            else Check(false, "应能读到地块 " + DepthPlotId + " 的锚点");
        }

        /// <summary>把玩家放到地块脚底相对偏移处，并断言置位成功。</summary>
        static void SnapRelative(float offset)
        {
            var ok = controller.SnapTravelerTo(4260f, 3720f + offset);
            Check(ok, "玩家应能置位到 footY" + (offset >= 0 ? "+" : "") + offset);
        }

        /// <summary>纵深：比较玩家与地块的 sibling index（大 = 后绘制 = 画在上层）。</summary>
        static void CheckDepth(float offset)
        {
            var player = controller.TravelerRectForShot;   // 显式钩子，不按猜的物体名（T092/P6）
            var plot = FindRect("Placeable " + DepthPlotId);
            if (player == null || plot == null)
            {
                Check(false, "纵深断言需要玩家与地块对象都在场");
                return;
            }
            var playerAbove = player.GetSiblingIndex() > plot.GetSiblingIndex();
            // 玩家 y 比地块大（更近）→ 玩家画在上层；更远 → 被地块挡住。
            var expectAbove = offset > 0f;
            Check(playerAbove == expectAbove,
                "纵深方向（玩家 y=footY" + (offset >= 0 ? "+" : "") + offset + "）：" +
                (expectAbove ? "玩家应画在地块之上" : "玩家应被地块挡住"),
                "player=" + player.GetSiblingIndex() + " plot=" + plot.GetSiblingIndex());
            Debug.Log("[FrontEndShotProps] depth offset=" + offset + " playerIdx=" +
                player.GetSiblingIndex() + " plotIdx=" + plot.GetSiblingIndex());
        }

        /// <summary>T026 乱序断言核心：任意地块 id 的玩家遮挡方向断言。</summary>
        static void CheckDepthAgainst(string plotId, bool expectPlayerAbove)
        {
            var player = controller.TravelerRectForShot;   // 显式钩子，不按猜的物体名（T092/P6）
            var plot = FindRect("Placeable " + plotId);
            if (player == null || plot == null)
            {
                Check(false, "纵深断言需要玩家与 " + plotId + " 都在场");
                return;
            }
            var playerAbove = player.GetSiblingIndex() > plot.GetSiblingIndex();
            Check(playerAbove == expectPlayerAbove,
                "T026 乱序纵深（" + plotId + "，创建顺序与纵深序相反）：" +
                (expectPlayerAbove ? "玩家应画在其上" : "玩家应被其挡住"),
                "player=" + player.GetSiblingIndex() + " plot=" + plot.GetSiblingIndex());
        }

        /// <summary>找挂在世界对象上的 Button（placeable 的点击入口）。</summary>
        static Button FindObjectButton(string objectName)
        {
            var rect = FindRect(objectName);
            return rect == null ? null : rect.GetComponent<Button>();
        }

        /// <summary>活动 UI 里是否存在指定文本（搭话框名字断言用）。</summary>
        static bool AnyText(string text)
        {
            return Resources.FindObjectsOfTypeAll<TextMeshProUGUI>()
                .Any(t => t.gameObject.scene.IsValid() && t.gameObject.activeInHierarchy && t.text == text);
        }

        static void CheckBadConfig()
        {
            if (propLayer == null) { Check(false, "物件层缺失，无法验坏配置"); return; }
            // 注入坏配置（缺 kind / 坐标）：必须抛 FormatException 并给出可读浮签，且不崩。
            propLayer.LoadPlotsFromTextForCheck("{\"plots\":[{\"id\":\"bad-plot\"}]}");
            Check(ActiveObjects("PropLayer error") >= 1, "坏配置应显示 PropLayer error 浮签");
            // T032：浮签是物件层唯一能向玩家发声的通道。借到没有汉字字形的 TMP 默认字体时，
            // 整条提示会渲染成 □□□□——断言打在浮签自己的字体上，而不是"代码看起来对"。
            Check(propLayer.ErrorBannerDrawsCjkForCheck(),
                "报错浮签借用的字体应含有效汉字字形（T032：不许豆腐块）");
            Check(propLayer.PlotCount == 0, "坏配置不应产出任何地块", "实际 " + propLayer.PlotCount);
            Capture("props-03-badconfig");
            // 浮签靠 Destroy(go, 5f) 自动消失，那 5 秒是**游戏时间**；批量模式下玩家循环
            // 极稀疏（见 T030），计时器往往不触发，红字就会漏进之后的世界页截图——
            // p02-01-town 实测被污染过。取证之后显式清干净，并断言确实清了。
            foreach (var leftover in Resources.FindObjectsOfTypeAll<RectTransform>()
                         .Where(t => t.name == "PropLayer error" && t.gameObject.scene.IsValid()))
                UnityEngine.Object.DestroyImmediate(leftover.gameObject);
            Check(ActiveObjects("PropLayer error") == 0, "浮签取证后应清理干净（不污染后续截图）");
            // 恢复正式配置。
            propLayer.ReloadForCheck();
        }

        static void CheckCombat()
        {
            // 遭遇表化：敌人来自遭遇表而不是写死的 boar-demon。
            controller.ShowCombat("encounter-boar-01");
            Check(controller.CurrentPage == "combat", "已知遭遇 id 应开战");
            Check(controller.CombatFoe != null && controller.CombatFoe.Id == "boar-demon",
                "敌人应来自遭遇表（enemy=boar-demon）",
                controller.CombatFoe == null ? "无敌人" : controller.CombatFoe.Id);
            Capture("props-04-combat");

            // 打一下让血量变化，才能区分"拒开战"与"重开一场"。
            controller.CombatPlaceForCheck(-0.4f, 0.5f);
            FindActiveButton("轻 击")?.onClick.Invoke();
            controller.CombatClockAdvance(0.13f);
            var hpAfterHit = controller.CombatFoe.Hp;
            Check(hpAfterHit == 44, "轻击命中：野猪 60→44", hpAfterHit + "");

            // 未知遭遇 id：拒开战（血量不变、页面不重开）。
            controller.ShowCombat("encounter-not-exist");
            Check(controller.CurrentPage == "combat", "未知遭遇不应切走页面");
            Check(controller.CombatFoe != null && controller.CombatFoe.Hp == hpAfterHit,
                "未知遭遇应拒开战（敌人血量不变）",
                controller.CombatFoe == null ? "无敌人" : controller.CombatFoe.Hp + " vs " + hpAfterHit);
        }

        // ---------------- 基础设施（照 CombatShot 的模式）----------------

        static void SetupCapture()
        {
            camera = Camera.main;
            Check(camera != null, "主相机存在");
            var canvas = UnityEngine.Object.FindAnyObjectByType<Canvas>();
            Check(canvas != null, "Canvas 存在");
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 10;
            target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            camera.targetTexture = target;
            camera.Render();
            Canvas.ForceUpdateCanvases();
        }

        static int FreeSlot()
        {
            foreach (var info in controller.Repository.ReadAll())
                if (info.State == SlotState.Empty) return info.Slot;
            return 0;
        }

        // 批处理里整个流程同帧推进：页面渐入的 CanvasGroup 没等到真实帧，alpha 还停在 0。
        // 截图是验画面不是验渐入动画，先把当前页拉满再拍。
        static void ShowPageContent()
        {
            foreach (var group in Resources.FindObjectsOfTypeAll<CanvasGroup>())
                if (group.gameObject.scene.IsValid() && group.gameObject.name.StartsWith("Page "))
                    group.alpha = 1f;
        }

        static void Capture(string label)
        {
            ShowPageContent();
            Canvas.ForceUpdateCanvases();
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            camera.Render();
            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            var path = Path.Combine(shotsDir, label + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
            Debug.Log("[FrontEndShotProps] captured " + path);
        }

        static bool HasComponent<T>(string objectName) where T : Component
        {
            var rect = FindRect(objectName);
            return rect != null && rect.GetComponent<T>() != null;
        }

        /// <summary>这个 NPC 的立绘现在加载得到吗——用生产端同一组路径前缀
        /// （controller.PortraitResourcePaths）与同一加载口径（Load&lt;Texture2D&gt;，D-21 之后的写法）
        /// 现场判定。夹具不许自己记"库里应该没有图"，那种快照在美术入库的当天就变成红灯（D-13/T090）。</summary>
        static bool PortraitLoaded(string npcId)
        {
            var prefixes = controller.PortraitResourcePaths;
            if (prefixes == null) return false;
            foreach (var prefix in prefixes)
                if (!string.IsNullOrEmpty(prefix) && Resources.Load<Texture2D>(prefix + npcId) != null)
                    return true;
            return false;
        }

        /// <summary>对话框构图两态判据：有立绘＝左立绘 + 右对话（x=580 宽=1200），
        /// 无立绘＝居中（x=470 宽=980）。走哪一态由 TalkPortraitShown 自己说，两态都断。</summary>
        static void CheckTalkComposition(string when)
        {
            var geo = controller.TalkBoxGeometry;
            if (controller.TalkPortraitShown)
                Check(Mathf.Abs(geo.x - 580f) < 1f && Mathf.Abs(geo.z - 1200f) < 1f,
                    "有立绘时对话框应让位到右侧（x=580 宽=1200，左立绘 + 右对话框）",
                    when + " · " + geo);
            else
                Check(Mathf.Abs(geo.x - 470f) < 1f && Mathf.Abs(geo.z - 980f) < 1f,
                    "无立绘时对话框应回到居中构图（x=470 宽=980）", when + " · " + geo);
        }
        /// <summary>
        /// 按立绘当前能否加载断言对应分支：加载得到 → RawImage 且不是占位 Image；
        /// 加载不到 → 占位 Image 且不是 RawImage。两态都完整断言（D-13）。
        /// 加载方式与 WorldPropLayer.PlaceAll 逐字相同（Load&lt;Texture2D&gt;），
        /// 否则夹具看到的分支和画面走的分支可以不是同一条。
        /// </summary>
        static void CheckArtBranch(string plotId, string art)
        {
            var name = "Placeable " + plotId;
            var hasArt = Resources.Load<Texture2D>(art) != null;
            if (hasArt)
                Check(HasComponent<RawImage>(name) && !HasComponent<Image>(name),
                    name + " 有立绘时应走真图分支（RawImage，不是占位 Image）", art + " 可加载");
            else
                Check(HasComponent<Image>(name) && !HasComponent<RawImage>(name),
                    name + " 缺立绘时应走色块占位（Image，不是 RawImage）", art + " 加载不到");
        }

        static RectTransform FindRect(string name)
        {
            return Resources.FindObjectsOfTypeAll<RectTransform>()
                .FirstOrDefault(r => r.gameObject.scene.IsValid() && r.gameObject.activeInHierarchy && r.gameObject.name == name);
        }

        static int ActiveObjects(string name)
        {
            return Resources.FindObjectsOfTypeAll<RectTransform>()
                .Count(r => r.gameObject.scene.IsValid() && r.gameObject.activeInHierarchy && r.gameObject.name == name);
        }

        static Button FindActiveButton(string text)
        {
            return Resources.FindObjectsOfTypeAll<Button>()
                .FirstOrDefault(b => b.gameObject.scene.IsValid() && b.gameObject.activeInHierarchy &&
                    b.gameObject.name == "Button " + text);
        }

        static void Check(bool condition, string message, string detail = null)
        {
            var full = string.IsNullOrEmpty(detail) ? message : message + " · " + detail;
            if (!condition) problems.Add("断言失败: " + full);
            else Debug.Log("[FrontEndShotProps] ok: " + full);
        }

        static void Finish()
        {
            SessionState.SetBool(SessionKey, false);
            Application.logMessageReceived -= OnLog;
            EditorApplication.update -= Pump;
            // 假立绘是本次验收自己造的对象：留在场上会污染之后的截图与别的批处理入口。
            if (portraitSprite != null) UnityEngine.Object.Destroy(portraitSprite);
            if (portraitTexture != null) UnityEngine.Object.Destroy(portraitTexture);
            if (slot > 0) controller?.Repository.Delete(slot);
            var scratch = Environment.GetEnvironmentVariable("TWELVEJADE_SAVEDIR");
            try { if (!string.IsNullOrEmpty(scratch) && Directory.Exists(scratch)) Directory.Delete(scratch, true); }
            catch (Exception ex) { Debug.LogWarning("无法清理验收临时目录：" + ex.Message); }
            if (target != null && camera != null) { camera.targetTexture = null; target.Release(); }
            Debug.Log("[FrontEndShotProps] finished, " + problems.Count + " problem(s)");
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
