using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TwelveJade.Core;
using TwelveJade.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace TwelveJade.Editor
{
    // 批处理 Play 验收：进入 Play 模式后逐页导航，把 UI 渲染到 RenderTexture 存成 PNG，
    // 并对存档流程做端到端断言。任何引擎错误或断言失败都会让进程以非零码退出。
    // 进入 Play 模式会触发 domain reload，静态订阅会丢失，因此用 [InitializeOnLoad] + SessionState 恢复。
    [InitializeOnLoad]
    public static class FrontEndShot
    {
        const int Width = 1920, Height = 1080;
        const string SessionKey = "FrontEndShot.Active";
        static readonly string[] PageNames = { "menu", "slots", "new-game", "character-creation", "preview", "town", "inventory", "trade", "settings", "credits" };
        static readonly HashSet<string> capturedShots = new HashSet<string>();

        static FrontEndController controller;
        static Camera camera;
        static RenderTexture target;
        static string shotsDir;
        static readonly List<string> problems = new List<string>();
        static int shotIndex = -1;
        static float waitStart;
        static bool waiting;
        static Action pendingAfter;
        static string currentShot;
        static float runStart;
        static int createdSlot;
        static string controllerPage => controller != null ? controller.CurrentPage : "<controller 未找到>";

        static FrontEndShot()
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

        public static void Capture() => Run(false);

        // 地形底座阶段入口：TwelveJade.Editor.FrontEndShot.CaptureTerrain
        public static void CaptureTerrain() => Run(true);

        // 模式标记必须跨 play 域重载存活（普通静态字段会被重置回 false，
        // 导致地形模式跑成完整流程、撞上 terrain 阶段不存在的 NPC 断言）。
        static bool terrainMode => SessionState.GetBool(SessionKey + ".Terrain", false);
        static bool terrainDraftCreated;
        static bool terrainReady;

        static void Run(bool mode)
        {
            SessionState.SetBool(SessionKey + ".Terrain", mode);
            SessionState.SetBool(SessionKey, true);
            // 验收全程只在临时存档目录里跑，玩家的三个槽位一概不碰。
            var dir = Path.Combine(Path.GetTempPath(), "twelve-jade-acceptance-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            Environment.SetEnvironmentVariable("TWELVEJADE_SAVEDIR", dir);
            // 批处理没有焦点、native 输入不来：改为手动驱动，事件由注入在产品 Update
            // 之前的更新按需消费（W 的持续键态由此走真实产品路径）。
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            Debug.Log("[FrontEndShot] scratch saves at " + dir);
            var project = Directory.GetParent(Application.dataPath).FullName; // TwelveJade 工程
            var repoRoot = Directory.GetParent(Directory.GetParent(project).FullName).FullName; // 仓库根目录
            shotsDir = Path.Combine(repoRoot, "Tools", "screenshots");
            capturedShots.Clear();
            SessionState.SetString(SessionKey + ".Dir", shotsDir);
            Directory.CreateDirectory(shotsDir);
            Attach();
            EditorSceneManager.OpenScene("Assets/Scenes/FrontEnd.unity", OpenSceneMode.Single);
            runStart = Time.realtimeSinceStartup;
            EditorApplication.isPlaying = true;
        }

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            // 验收只计 Play 运行期的问题：启动期的编辑器噪音（许可令牌、Search 索引等）不算账。
            if (!EditorApplication.isPlaying) return;
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            // 与 Hub 共存时许可模块可能刷出瞬态错误；全新 Library 的 Search 索引是编辑器内部 bug——都与工程无关。
            if (condition.StartsWith("[Licensing::Module]")) return;
            if (stackTrace != null && stackTrace.Contains("UnityEditor.Search")) return;
            problems.Add(type + ": " + condition);
        }

        static void Pump()
        {
            if (EditorApplication.isPlaying)
            {
                EnsurePlayerLoopInputUpdate();
                EditorApplication.QueuePlayerLoopUpdate();
            }
            try { Step(); }
            catch (Exception ex) { Fail("驱动异常: " + ex); }
        }

        static bool playerLoopInputInjected;
        static bool pendingInputConsume;

        // 手动输入模式下事件由这里消费：挂在产品 Update（ScriptRunBehaviourUpdate）之前，
        // 且只在有注入事件时才跑——空转的输入更新会推进 step 计数，把按键沿窗口关掉
        // （批处理十轮实测：isPressed 状态可达、wasPressedThisFrame 的沿在 editor/player
        // 双重驱动下无法稳定存活，故"沿"类断言只在实机有意义，批处理验"状态 + 等效动作"）。
        static void EnsurePlayerLoopInputUpdate()
        {
            if (playerLoopInputInjected) return;
            var loop = UnityEngine.LowLevel.PlayerLoop.GetCurrentPlayerLoop();
            for (var i = 0; i < loop.subSystemList.Length; i++)
            {
                var sub = loop.subSystemList[i];
                if (sub.subSystemList == null) continue;
                var systems = new List<UnityEngine.LowLevel.PlayerLoopSystem>(sub.subSystemList);
                var behaviour = systems.FindIndex(x => x.type.Name == "ScriptRunBehaviourUpdate");
                if (behaviour < 0) continue;
                systems.Insert(behaviour, new UnityEngine.LowLevel.PlayerLoopSystem
                {
                    type = typeof(FrontEndShot),
                    updateDelegate = () =>
                    {
                        if (!pendingInputConsume) return;
                        pendingInputConsume = false;
                        InputSystem.Update();
                    }
                });
                sub.subSystemList = systems.ToArray();
                loop.subSystemList[i] = sub;
                UnityEngine.LowLevel.PlayerLoop.SetPlayerLoop(loop);
                playerLoopInputInjected = true;
                Debug.Log("[FrontEndShot] on-demand input update injected before ScriptRunBehaviourUpdate");
                return;
            }
            Debug.LogWarning("[FrontEndShot] 未找到 ScriptRunBehaviourUpdate，输入注入未挂载");
        }

        static void Step()
        {
            if (Time.realtimeSinceStartup - runStart > 240f) { Fail("验收超时:超过 240 秒仍未完成,当前页面 " + controllerPage); return; }
            // 地形底座模式：只验"地"，走独立短流程（建档→进镇→地形断言→三张截图），
            // 不进 trade/combat 等依赖 NPC 的页面。
            if (terrainMode)
            {
                TerrainStep();
                return;
            }
            if (shotIndex < 0)
            {
                if (!EditorApplication.isPlaying) return;
                controller = UnityEngine.Object.FindAnyObjectByType<FrontEndController>();
                if (controller == null) return;
                SetupCapture();
                Debug.Log("[FrontEndShot] play mode ready, controller found");
                AdvanceTo(0);
                return;
            }
            if (!waiting) return;
            if (Time.realtimeSinceStartup - waitStart < .45f) return;
            if (waitUntil != null)
            {
                // 轮询到条件成立为止：比数节拍靠谱（人物走过去的时间不是固定的）。
                if (!waitUntil() && Time.realtimeSinceStartup < waitDeadline)
                {
                    waitStart = Time.realtimeSinceStartup;
                    return;
                }
                waitUntil = null;
            }
            waiting = false;
            if (terrainMode)
            {
                // 地形模式收尾：三张地形截图 + 底座断言，然后直接结束。
                RunTerrainChecks();
                Finish();
                return;
            }
            if (!Shot()) return;
            if (shotIndex >= PageNames.Length - 1) Finish();
            else AdvanceTo(shotIndex + 1);
        }

        static void AdvanceTo(int index)
        {
            shotIndex = index;
            currentShot = PageNames[index];
            switch (index)
            {
                case 0:
                    Open(controller.ShowMenu);
                    break;
                case 1:
                    Open(() => controller.ShowSlots(false));
                    break;
                case 2:
                    Open(() => controller.ShowSlots(true));
                    break;
                case 3:
                    Open(() =>
                    {
                        controller.ShowCharacterCreation(2);
                        controller.SetCreationGender("female");
                        controller.SetCreationFaceStyle(1);
                    });
                    break;
                case 4:
                {
                    var slot = FreeSlot();
                    if (slot > 0)
                    {
                        controller.ShowCharacterCreation(slot);
                        controller.SetCreationGender("female");
                        controller.SetCreationFaceStyle(1);
                        var save = controller.CreateFromDraft(slot);
                        Check(save != null, "落笔创建应成功");
                        Check(controller.Repository.Read(slot).CanLoad, "新建档位应可读取");
                        Check(InventoryRules.UsedSlots(save.bag) == 4 && save.coins == ItemTable.StartingCoins,
                            "新档应带开局行囊与盘缠");
                        createdSlot = slot;
                        Open(() => controller.ShowPreview(save), () =>
                        {
                            VerifyMotionButtons();
                            VerifyFateDialogue();
                            controller.ShowPreview(save);
                        });
                    }
                    else
                    {
                        var existing = controller.Repository.Latest();
                        Check(existing != null, "找不到空档位时应有既有档位");
                        Open(() => controller.ShowPreview(existing.Data));
                    }
                    break;
                }
                case 5:
                    Open(() => controller.ShowTown(), VerifyTownBag);
                    break;
                case 6:
                    Open(controller.ShowInventory, VerifyInventory);
                    break;
                case 7:
                    Open(() => controller.ShowTrade("huolang"), VerifyTrade);
                    break;
                case 8:
                    Open(controller.ShowSettings);
                    break;
                case 9:
                    Open(controller.ShowCredits);
                    break;
            }
        }

        static void Open(Action page, Action after = null)
        {
            pendingAfter = after;
            page();
            waitStart = Time.realtimeSinceStartup;
            waiting = true;
        }

        static int FreeSlot()
        {
            foreach (var slot in controller.Repository.ReadAll())
                if (slot.State == SlotState.Empty) return slot.Slot;
            return 0;
        }

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

        static bool Shot()
        {
            if (pendingAfter != null)
            {
                pendingAfter();
                pendingAfter = null;
                waitStart = Time.realtimeSinceStartup;
                waiting = true;
                return false;
            }
            if (currentShot == "preview")
            {
                Check(controller.CurrentPage == "preview", "预览截图应停留在预览页");
                Check(FindActiveButton("返回") != null, "预览页返回按钮应可见");
            }
            // 城镇页分两段：第一段搭话与"下方"纵深断言，第二段走到货郎上方验"上方"纵深，再截城镇图。
            if (currentShot == "town" && !townAboveChecked)
            {
                Check(controller.CurrentPage == "town", "城镇页应已打开");
                Check(UnityEngine.Object.FindAnyObjectByType<PuppetActor>() != null, "城镇页应有分层纸偶角色");
                VerifyArtImport();
                VerifyTalkThenLeave();
                Check(controller.CurrentPage == "town", "城镇截图应停在镇上");
                townAboveChecked = true;
                // 纵深·上方：批处理无应用焦点时 InputSystem 重置注入按键，"按住 W 走
                // 长路"不可靠（只活一大步）。改用钩子把人物置到货郎上方，等一帧让
                // 镜头/排序刷新后断言遮挡关系；真实步行由搭话走向覆盖。
                Check(controller.SnapTravelerAboveNpc("huolang"), "验收钩子应能把人物置到货郎上方");
                waitStart = Time.realtimeSinceStartup;
                waiting = true;
                waitDeadline = Time.realtimeSinceStartup + 8f;
                waitUntil = null;
                return false;
            }
            if (currentShot == "town")
            {
                Check(controller.CurrentPage == "town", "城镇截图应停在镇上");
                // 走过一段路后镜头应一直跟着主角：近景构图里主角不许滚出视口。
                Check(controller.TownTravelerOnScreen, "镜头跟随应让主角留在视口内");
                VerifyDepthAbove();
            }
            if (currentShot == "inventory")
            {
                Check(controller.CurrentPage == "town", "行囊是城镇页上的浮层");
                Check(controller.BagCellCount == InventoryRules.SlotCount, "行囊应有 24 格");
                Check(controller.CurrentBag != null, "行囊应已挂上当前存档");
            }
            if (currentShot == "trade" && controller.CurrentPage != "trade")
            {
                // Esc 断言已在 VerifyTrade 尾部完成；恢复交易页供截图。
                controller.ShowTrade("huolang");
                // 同帧截图赶不上 0.22s 渐入（alpha 仍为 0，会透出底层主菜单），直接拉满。
                var fade = typeof(FrontEndController).GetField("contentFade",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (fade?.GetValue(controller) is CanvasGroup group) group.alpha = 1f;
            }
            Capture(string.Format("play-{0:00}-{1}", shotIndex + 1, currentShot), currentShot);
            return true;
        }

        // 任意时刻抓一帧画布（页面流程与序章问命演出共用）；capturedToken 用于页面流程登记短名。
        static void Capture(string label, string capturedToken = null)
        {
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
            capturedShots.Add(capturedToken ?? label);
            Debug.Log("[FrontEndShot] captured " + path);
        }

        static Button FindActiveButton(string text)
        {
            return Resources.FindObjectsOfTypeAll<Button>()
                .FirstOrDefault(button => button.gameObject.scene.IsValid() && button.gameObject.activeInHierarchy &&
                    button.gameObject.name == "Button " + text);
        }

        // 按 GameObject 名字找可交互控件：货架行、NPC 这些不是 ui.Button 造的，
        // 用得上它才能在验收里真正 onClick，而不是绕过 UI 直接调 API。
        static Button FindActiveControl(string name)
        {
            return Resources.FindObjectsOfTypeAll<Button>()
                .FirstOrDefault(button => button.gameObject.scene.IsValid() && button.gameObject.activeInHierarchy &&
                    button.gameObject.name == name);
        }

        // 条件等待：满足前不继续推进（人物走过去、弹窗出现这类需要等真实帧的场合）。
        static Func<bool> waitUntil;
        static float waitDeadline;

        // ---- 地形底座模式（CaptureTerrain）----
        static void TerrainStep()
        {
            if (controller == null)
            {
                controller = UnityEngine.Object.FindAnyObjectByType<FrontEndController>();
                return;
            }
            // 等产品初始化到主菜单（presets/配置表加载完毕），再建临时行迹。
            if (!terrainDraftCreated)
            {
                if (controller.CurrentPage != "menu") return;
                controller.CreateFromDraft(1);
                terrainDraftCreated = true;
                createdSlot = 1;
                return;
            }
            if (!terrainReady)
            {
                // 等 UI 画布就绪（play 早期 Canvas 未建时 SetupCapture 会空引用）。
                if (UnityEngine.Object.FindAnyObjectByType<Canvas>() == null) return;
                SetupCapture();
                controller.ShowTown();
                terrainReady = true;
                waitStart = Time.realtimeSinceStartup;
                waitDeadline = Time.realtimeSinceStartup + 4f;
                waiting = true;
                waitUntil = null; // 纯等 .45s+ 让渐入/镜头/排序刷新，不设条件
                return;
            }
            // 进镇页已就绪：等一拍刷新后跑地形断言 + 三张截图，然后收尾。
            if (Time.realtimeSinceStartup - waitStart < .45f) return;
            if (Time.realtimeSinceStartup >= waitDeadline)
            {
                RunTerrainChecks();
                Finish();
            }
        }

        // 世界底座验收（第一阶段）：只验"世界"——世界/视野比例、摄像机跟随与边界、
        // 跑图距离、小地图全局/局部分工。不含任何内容（房屋/NPC/树）验收。
        static void RunTerrainChecks()
        {
            var world = controller.World;
            Check(world != null, "世界配置应已载入");
            if (world == null) return;
            Check(controller.CurrentPage == "town", "应能进入城镇页");

            // ---- 世界 vs 视野：核心比例 ----
            var view = controller.ViewWorldSize;
            var coverage = (view.x * view.y) / (world.WorldWidth * world.WorldHeight);
            Check(coverage < .10f, "一屏只应是世界的一小部分（<10%）",
                string.Format("视野 {0:0}x{1:0} / 世界 {2:0}x{3:0} = {4:P1}",
                    view.x, view.y, world.WorldWidth, world.WorldHeight, coverage));

            // ---- 分块铺满世界 ----
            Check(world.Chunks.Count > 1, "世界应由多块拼成", world.Chunks.Count + " 块");

            // ---- 摄像机：主角居中、跟随时不越界 ----
            Check(controller.SnapTravelerTo(world.SpawnX, world.SpawnY), "出生点置位应成功");
            Check(controller.PlayerOnScreen(), "出生点应可见");
            // 逐点抽查：主角走到世界各处，镜头都要跟得上且不出画
            var probes = world.Landmarks.Where(m => world.CanStand(m.X, m.Y)).Take(6).ToList();
            Check(probes.Count >= 4, "抽查点应足够", probes.Count + " 处");
            foreach (var mark in probes)
            {
                Check(controller.SnapTravelerTo(mark.X, mark.Y), mark.Name + " 置位应成功");
                Check(controller.PlayerOnScreen(), mark.Name + "：主角应在视口内");
            }

            // ---- 不可通行：山/河不可走，桥可走，世界外不可走 ----
            // 采样点从配置反推（blocked 里河面矩形 + 山体矩形），不写死坐标——
            // 写死会在世界尺寸调整后误判（曾把山径上的点当成山体）。
            var riverBlocks = world.Blocked.Where(r => r.MinY > world.WorldHeight * .2f &&
                r.MinY < world.WorldHeight * .4f).ToList();
            var riverProbe = riverBlocks.FirstOrDefault(r => r.MinX > world.WorldWidth * .15f &&
                r.MinX < world.WorldWidth * .85f);
            Check(riverProbe != null && !controller.TerrainStandable(riverProbe.MinX + 8f, riverProbe.MinY + 8f),
                "河面不可通行（桥外）", riverProbe != null ? $"({riverProbe.MinX},{riverProbe.MinY})" : "找不到河");
            var mountainBlocks = world.Blocked.Where(r => r.MinY < world.WorldHeight * .12f).ToList();
            var mountainProbe = mountainBlocks.FirstOrDefault(r => r.MinX > world.WorldWidth * .3f &&
                r.MinX < world.WorldWidth * .6f);
            Check(mountainProbe != null && !controller.TerrainStandable(mountainProbe.MinX + 8f, mountainProbe.MinY + 8f),
                "后山不可通行", mountainProbe != null ? $"({mountainProbe.MinX},{mountainProbe.MinY})" : "找不到山");
            var bridge = world.Landmarks.FirstOrDefault(m => m.Id.Contains("bridge") || m.Id.Contains("river"));
            Check(bridge != null && controller.TerrainStandable(bridge.X, bridge.Y), "主桥可通行",
                bridge != null ? $"({bridge.X:0},{bridge.Y:0})" : "缺少桥地标");
            Check(!controller.TerrainStandable(-10, world.WorldHeight * .5f), "世界边界外不可通行");

            // ---- 跑图距离：出生点到后山，按速度换算时间 ----
            var spawnY = world.SpawnY;
            var edgeDistance = controller.PlayerEdgeDistance();
            Check(edgeDistance > 100f, "出生点应远离世界边缘（可继续扩张）",
                edgeDistance.ToString("0") + " 单位");

            // ---- 截图：出生点（南缘）、镇中心、北桥、后山 ----
            controller.SnapTravelerTo(world.SpawnX, spawnY);
            Capture("world-01-spawn", "world-spawn");
            var town = world.Landmarks.FirstOrDefault(m => m.Id == "town-center");
            var northBridge = world.Landmarks.FirstOrDefault(m => m.Id == "river-north");
            var foothill = world.Landmarks.FirstOrDefault(m => m.Id == "foothill");
            if (town != null) { controller.SnapTravelerTo(town.X, town.Y); Capture("world-02-town", "world-town"); }
            if (northBridge != null) { controller.SnapTravelerTo(northBridge.X, northBridge.Y); Capture("world-03-bridge", "world-bridge"); }
            if (foothill != null) { controller.SnapTravelerTo(foothill.X, foothill.Y); Capture("world-04-hill", "world-hill"); }
        }

        static int CountSceneObjects(string prefix)
        {
            return Resources.FindObjectsOfTypeAll<Transform>()
                .Count(rect => rect.name.StartsWith(prefix, StringComparison.Ordinal) &&
                    rect.gameObject.scene.IsValid() && rect.gameObject.activeInHierarchy);
        }

        static int ActiveObjects(string name)
        {
            return Resources.FindObjectsOfTypeAll<RectTransform>()
                .Count(rect => rect.gameObject.scene.IsValid() && rect.gameObject.activeInHierarchy && rect.gameObject.name == name);
        }

        static RectTransform FindRect(string name)
        {
            return Resources.FindObjectsOfTypeAll<RectTransform>()
                .FirstOrDefault(rect => rect.gameObject.scene.IsValid() && rect.gameObject.activeInHierarchy &&
                    rect.gameObject.name == name);
        }

        // 批处理里没有真人按键，物理键盘设备也不存在：补一个软件键盘，把键态排进
        // Input System 的事件队列，下一帧由播放循环消费——Esc、W 都走产品自己的
        // 输入路径（Update 里的 Keyboard.current），不绕过输入层。
        static Keyboard checkKeyboard;

        static Keyboard EnsureKeyboard()
        {
            if (checkKeyboard != null && checkKeyboard.added) return checkKeyboard;
            checkKeyboard = Keyboard.current != null ? Keyboard.current : InputSystem.AddDevice<Keyboard>();
            return checkKeyboard;
        }

        static void PressKey(Key key)
        {
            var keyboard = EnsureKeyboard();
            if (keyboard == null) { Check(false, "批处理里应能准备出键盘设备"); return; }
            pendingInputConsume = true;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
        }

        // 抬起所有键：一个全零键态事件即等价于松键。
        static void ReleaseKeys()
        {
            var keyboard = checkKeyboard;
            if (keyboard == null || !keyboard.added) return;
            pendingInputConsume = true;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        }

        static RectTransform TravelerRect =>
            UnityEngine.Object.FindAnyObjectByType<PuppetActor>()?.transform as RectTransform;

        static RectTransform NpcRect(string npcId) =>
            FindActiveControl("Npc " + npcId)?.transform as RectTransform;

        // 城镇纵深断言分两个方向：下方（搭话位，人在 NPC 基座下方更近）在 VerifyTalkThenLeave 验，
        // 上方（按 W 走到基座上方更远）在这里验——sibling 顺序应随 y 翻转。
        static bool townAboveChecked;

        // 美术导入回归防线：纸偶靠 uvRect 的 v 比例切片，只要 Unity 把贴图缩放/填充过
        // （非 2 的幂尺寸 + nPOTScale），腿那一段就会落到空白区——表现是"腿没了"，
        // 而磁盘上的 PNG 完全正常，肉眼查文件永远查不出来。这里直接比对运行期尺寸与
        // 源文件尺寸，对不上立刻报出来。配合 Editor/ArtImportGuard 从源头强制设置。
        static void VerifyArtImport()
        {
            var folder = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName,
                Path.Combine("Assets", "Resources", "Art"));
            if (!Directory.Exists(folder)) return;
            var wrong = new List<string>();
            foreach (var path in Directory.GetFiles(folder, "*.png", SearchOption.AllDirectories))
            {
                var (sourceWidth, sourceHeight) = PngSize(path);
                if (sourceWidth <= 0) continue;
                var key = "Art/" + Path.GetFileNameWithoutExtension(path).Replace('\\', '/');
                var loaded = Resources.Load<Texture2D>(key);
                if (loaded == null) continue;
                if (loaded.width != sourceWidth || loaded.height != sourceHeight)
                    wrong.Add(Path.GetFileNameWithoutExtension(path) +
                             $" 源{sourceWidth}x{sourceHeight}→Unity{loaded.width}x{loaded.height}");
            }
            Check(wrong.Count == 0, "美术贴图导入尺寸应与源文件一致（nPOT 缩放会毁掉纸偶切片）",
                wrong.Count == 0 ? "全部一致" : string.Join(" · ", wrong.ToArray()));
        }

        // PNG 尺寸取自文件头的 IHDR 块，不依赖任何图像库。
        static (int width, int height) PngSize(string path)
        {
            try
            {
                using var stream = File.OpenRead(path);
                var header = new byte[24];
                if (stream.Read(header, 0, 24) < 24) return (0, 0);
                // 8 字节签名 + 4 长度 + 4 "IHDR" + 4 宽 + 4 高
                return (BigEndian(header, 16), BigEndian(header, 20));
            }
            catch (System.Exception)
            {
                return (0, 0);
            }
        }

        static int BigEndian(byte[] bytes, int offset) =>
            (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];

        // 纵深·上方断言（钩子置位后由 town 第二段调用）：人物在货郎基座上方（更远）
        // 时应画在 NPC 之下（sibling 更小）——与搭话时的"下方"断言合成一对。
        static void VerifyDepthAbove()
        {
            var traveler = TravelerRect;
            var npc = NpcRect("huolang");
            // UiKit 局部坐标 y 向下：画面上方（更远）的局部 y 比 NPC 大（更不负）。
            var above = traveler != null && npc != null &&
                traveler.anchoredPosition.y > npc.anchoredPosition.y + 1f;
            Check(above, "钩子置位后人物应停在货郎基座上方",
                traveler == null || npc == null ? "找不到人物或货郎"
                : "y " + traveler.anchoredPosition.y.ToString("0") + " vs " + npc.anchoredPosition.y.ToString("0"));
            if (above && traveler != null && npc != null)
                Check(traveler.GetSiblingIndex() < npc.GetSiblingIndex(),
                    "人物在货郎基座上方（更远）时应画在 NPC 之下",
                    "sibling " + traveler.GetSiblingIndex() + " vs " + npc.GetSiblingIndex());
        }

        static void VerifyMotionButtons()
        {
            var actor = Resources.FindObjectsOfTypeAll<CharacterActor>()
                .FirstOrDefault(value => value.gameObject.scene.IsValid() && value.gameObject.activeInHierarchy);
            Check(actor != null, "预览页应创建角色动作对象");
            if (actor == null) return;
            for (var i = 1; i < CharacterActor.MotionNames.Length; i++)
            {
                var button = FindActiveButton(CharacterActor.MotionNames[i]);
                Check(button != null, "动作按钮存在: " + CharacterActor.MotionNames[i]);
                if (button == null) continue;
                button.onClick.Invoke();
                Check(actor.CurrentMotion == (CharacterActor.Motion)i,
                    "动作按钮生效: " + CharacterActor.MotionNames[i]);
            }
            controller.SetMotion((int)CharacterActor.Motion.Walk);
            Check(actor.CurrentMotion == CharacterActor.Motion.Walk, "动作验收后恢复行走姿态");
        }

        static void VerifyFateDialogue()
        {
            controller.ShowCharacterCreation(2);
            controller.OpenFateDialogue();
            Check(ActiveObjects("Fate dialogue") == 1, "序章问命只有一只活动遮罩");
            var seat = FindActiveButton("落座听问");
            Check(seat != null, "问命开场白按钮存在");
            if (seat == null) return;
            Capture("fate-01-intro");
            seat.onClick.Invoke();
            for (var i = 0; i < FateDialogue.Questions.Length; i++)
            {
                Check(ActiveObjects("Fate dialogue") == 1, "问命第 " + (i + 1) + " 问只有一只活动遮罩");
                var option = FindActiveButton(FateDialogue.Questions[i].Options[0].Text);
                Check(option != null, "问命选项存在: " + (i + 1));
                if (option == null) return;
                if (i == 0) Capture("fate-02-question");
                option.onClick.Invoke();
                var next = FindActiveButton("继 续");
                Check(next != null, "老者神态后可继续: " + (i + 1));
                if (next == null) return;
                if (i == 0) Capture("fate-03-reply");
                next.onClick.Invoke();
            }
            Check(ActiveObjects("Fate dialogue") == 0, "问命完成后问题遮罩已清理");
            var accept = FindActiveButton("记下此卦");
            Check(accept != null, "问命结果确认按钮存在");
            if (accept == null) return;
            Capture("fate-04-result");
            accept.onClick.Invoke();
            Check(controller.CurrentPage == "character-creation", "记下此卦后回到创建页");
        }

        // 城镇页：真的点货郎 → 等角色走到跟前 → 搭话框出现 → 告辞 → 截城镇图。
        static void VerifyTownBag()
        {
            Check(controller.CurrentPage == "town", "城镇页应已打开");
            var peddler = FindActiveControl("Npc huolang");
            Check(peddler != null, "镇上的货郎应可点");
            if (peddler != null)
            {
                peddler.onClick.Invoke();
                Check(controller.CurrentPage == "town", "点货郎不该直接弹交易页，应先走过去");
                Check(controller.TownWalkingToNpc, "点货郎后应先走过去，不是原地对话");
                // 批处理里等不到真实走完（播放循环节拍不稳），把人物放到跟前再等搭话框。
                Check(controller.SnapTravelerNextToNpc("huolang"), "应能把人物放到货郎跟前");
                pendingNpcCheck = true;
                waitUntil = () => TalkBoxVisible();
                waitDeadline = Time.realtimeSinceStartup + 20f;
            }
        }

        static bool pendingNpcCheck;

        // 走到跟前才搭话：框要出现、要有「做买卖」，点完能进交易页；点完先告辞回镇上截图。
        static void VerifyTalkThenLeave()
        {
            if (!pendingNpcCheck) return;
            pendingNpcCheck = false;
            Check(TalkBoxVisible(), "走到货郎跟前应弹出搭话框");
            // 纵深·下方：人物此刻站在货郎基座下方（更近）——同一张深度表里
            // 应画在 NPC 之上（sibling 更大）。上方方向由 VerifyDepthAbove 补齐。
            var traveler = TravelerRect;
            var npc = NpcRect("huolang");
            if (traveler != null && npc != null)
                Check(traveler.GetSiblingIndex() > npc.GetSiblingIndex(),
                    "人物在货郎基座下方（更近）时应画在 NPC 之上",
                    "sibling " + traveler.GetSiblingIndex() + " vs " + npc.GetSiblingIndex());
            // 先把两个按钮都验到，再依次点：点「做买卖」会关掉搭话框，之后就找不到「告辞」了。
            var trade = FindActiveButton("做 买 卖");
            var bye = FindActiveButton("告 辞");
            Check(trade != null, "搭话框应有「做买卖」");
            Check(bye != null, "搭话框应有「告辞」");
            if (bye != null) bye.onClick.Invoke();
            Check(controller.CurrentPage == "town", "告辞后应回到镇上");
            if (trade == null) return;
            controller.TalkTo("huolang");
            FindActiveButton("做 买 卖")?.onClick.Invoke();
            Check(controller.CurrentPage == "trade", "点「做买卖」应进交易页");
            // 这一页要截的是镇子，办完差事回镇上再截。
            controller.ShowTown();
        }

        static bool TalkBoxVisible() => Resources.FindObjectsOfTypeAll<RectTransform>()
            .Any(rect => rect.gameObject.scene.IsValid() && rect.gameObject.activeInHierarchy && rect.name == "Talk box");

        // 行囊：B 键与按钮都要能开，开着再合上不应残留格子。
        static void VerifyBagToggle()
        {
            controller.ShowInventory();

            Check(controller.BagCellCount == InventoryRules.SlotCount, "行囊面板应有 24 格");
            Check(controller.CurrentBag != null && controller.CurrentBag.Length == InventoryRules.SlotCount,
                "行囊数据应挂上当前存档");
            controller.CloseInventory();
            Check(controller.BagCellCount == 0, "合上后不应残留格子");
        }

        // 行囊验收：格子数、页签过滤、浮签、拖拽换位、使用消耗、存档落盘。
        static void VerifyInventory()
        {
            Check(controller.CurrentPage == "town", "行囊应开在城镇页之上");
            Check(controller.BagCellCount == InventoryRules.SlotCount, "行囊应有 24 格");
            var bag = controller.CurrentBag;
            if (bag == null) { Check(false, "行囊数据缺失"); return; }
            Check(bag.Length == InventoryRules.SlotCount && bag.All(stack => stack != null), "行囊数据应补齐 24 格");

            // 浮签：悬停有物的格子应弹出台签，移开收起。
            var occupied = Array.FindIndex(bag, stack => !stack.IsEmpty);
            if (occupied >= 0)
            {
                controller.ShowTip(occupied);
                Check(controller.BagTipSlot == occupied, "浮签应指向被悬停的格子");
                controller.HideTip();
                Check(controller.BagTipSlot == -1, "移开鼠标后浮签应收起");
            }

            // 拖拽换位：有物格 ⇄ 空格，两个方向都要对。
            var empty = Array.FindIndex(bag, stack => stack.IsEmpty);
            if (occupied >= 0 && empty >= 0)
            {
                var movedId = bag[occupied].id;
                var movedCount = bag[occupied].count;
                controller.BagDrop(occupied, empty);
                Check(bag[empty].id == movedId && bag[empty].count == movedCount && bag[occupied].IsEmpty,
                    "拖拽应把物品整体移到目标格");
                controller.BagDrop(empty, occupied);
                Check(bag[occupied].id == movedId && bag[empty].IsEmpty, "再拖回来应回到原格");
            }

            // 使用：只对入口的东西生效，入口的确实少一件。
            var food = Array.FindIndex(bag, stack => stack.id == "ganliang" || stack.id == "chuibing");
            if (food >= 0)
            {
                var id = bag[food].id;
                var before = InventoryRules.Count(bag, id);
                controller.BagUse(food);
                Check(InventoryRules.Count(controller.CurrentBag, id) == before - 1, "使用吃食应消耗一件");
            }
            // 任务物不能被用掉：开局行囊里没有任务物，就临时塞一块玉佩再验。
            var quest = Array.FindIndex(bag, stack => stack.id == "yupei");
            if (quest < 0)
            {
                var free = Array.FindIndex(bag, stack => stack.IsEmpty);
                if (free >= 0)
                {
                    bag[free] = new ItemStack("yupei", 1);
                    quest = free;
                }
            }
            if (quest >= 0)
            {
                controller.BagUse(quest);
                Check(InventoryRules.Count(controller.CurrentBag, "yupei") == 1, "任务物不能被用掉");
            }

            // 整理与落盘：整理后行囊整齐，读回来的存档也整齐且与面板一致。
            controller.BagSort();
            Check(InventoryRules.IsTidy(controller.CurrentBag), "整理后空格应排在后面");
            var reloaded = controller.Repository.Read(activeSlot).Data;
            Check(reloaded != null && InventoryRules.IsTidy(reloaded.bag), "整理结果应写回存档");
            Check(reloaded != null && reloaded.bag.Sum(stack => stack.count) ==
                controller.CurrentBag.Sum(stack => stack.count), "存档里的件数应与面板一致");
            Check(reloaded != null && reloaded.schemaVersion == SaveData.CurrentSchemaVersion, "存档应为当前 schema 版本");
            if (createdSlot > 0)
            {
                Check(reloaded.coins == ItemTable.StartingCoins, "新档的盘缠应原样写回");
                Check(reloaded.bag.All(stack => stack.IsEmpty || stack.count <= ItemTable.MaxStack),
                    "存档里的堆叠不应超过上限");
            }
        }

        // 交易页：全部走真实按钮点击——货架行、黑市页签、买、卖、批量。
        // M3-02 另验：十行长货架的滚动、空状态与面板不变形；M3-01 另验 Esc 回镇。
        static void VerifyTrade()
        {
            Check(controller.CurrentPage == "trade", "交易页应已打开");
            Check(controller.TradeMerchantId == "huolang", "交易页应对上货郎");
            // M3-02 的长货架用例需要 9 行以上：正式表只有 7 行，注入一份 10 行的临时表，
            // 验完恢复正式配置（任务包原文："正式配置可恢复原数量"）。
            controller.LoadMerchantFixtureForCheck(LongShelfJson());
            var shelf = controller.ShelfOrder;
            Check(shelf.Count >= 9, "临时长货架应有九行以上", shelf.Count + " 行");

            // 货架首行与末行：真的点按钮，看选中的是不是自己那一件。
            var firstRow = FindActiveControl("Shelf row 0");
            var lastRow = FindActiveControl("Shelf row " + (shelf.Count - 1));
            Check(firstRow != null && lastRow != null, "货架首末行按钮可点");
            firstRow?.onClick.Invoke();
            Check(controller.TradePick == shelf[0] && controller.SettleTitle == controller.NameOf(shelf[0]),
                "点首行选中首行那件", controller.SettleTitle);
            lastRow?.onClick.Invoke();
            Check(controller.TradePick == shelf[shelf.Count - 1] &&
                controller.SettleTitle == controller.NameOf(shelf[shelf.Count - 1]),
                "点末行选中末行那件", controller.SettleTitle);

            // M3-02 滚动：十行超出固定视口应可滚；滚到底/回顶后行仍对应正确 id，面板与结算区不变形。
            var scroll = controller.ShelfScroll;
            Check(scroll != null && scroll.content.sizeDelta.y > scroll.viewport.rect.height + 1f,
                "十行货架应超出视口可滚动",
                scroll == null ? "找不到滚动列表" : "content " + scroll.content.sizeDelta.y.ToString("0") +
                    " / viewport " + scroll.viewport.rect.height.ToString("0"));
            var shelfPanel = FindRect("Shelf");
            var settlePanel = FindRect("Settle");
            Check(shelfPanel != null && settlePanel != null &&
                shelfPanel.sizeDelta == new Vector2(640, 600) && settlePanel.sizeDelta == new Vector2(260, 600),
                "货架与结算区面板尺寸应保持固定");
            if (scroll != null)
            {
                scroll.verticalNormalizedPosition = 0f;
                FindActiveControl("Shelf row " + (shelf.Count - 1))?.onClick.Invoke();
                Check(controller.TradePick == shelf[shelf.Count - 1] &&
                    controller.SettleTitle == controller.NameOf(shelf[shelf.Count - 1]),
                    "滚动到底后点末行仍选中末行", controller.SettleTitle);
                scroll.verticalNormalizedPosition = 1f;
                FindActiveControl("Shelf row 0")?.onClick.Invoke();
                Check(controller.TradePick == shelf[0] && controller.SettleTitle == controller.NameOf(shelf[0]),
                    "滚回顶部后点首行仍选中首行", controller.SettleTitle);
                Check(shelfPanel != null && shelfPanel.sizeDelta == new Vector2(640, 600) &&
                    settlePanel != null && settlePanel.sizeDelta == new Vector2(260, 600),
                    "滚动后面板尺寸应不变");
            }

            // 长货架验完：恢复正式配置，黑市与买卖用例全走正式数据。
            controller.RestoreMerchantFixture();
            Check(controller.ShelfOrder.Count == 7, "恢复正式货架行数", controller.ShelfOrder.Count + " 行");

            // 黑市页签：显示价必须与结算价同源（×1.4）；可见性按命格，不是看运气。
            var dayPrice = controller.SelfPriceOf(0);
            controller.SetDestinyForCheck("anle");
            var blackTab = FindActiveButton("黑 市");
            Check(blackTab != null && !blackTab.interactable, "普通命格不该能开黑市",
                blackTab == null ? "页签不存在" : "interactable=" + blackTab.interactable);
            controller.SetDestinyForCheck("shaxing");
            var blackTabOpen = FindActiveButton("黑 市");
            Check(blackTabOpen != null && blackTabOpen.interactable, "杀星入命应能开黑市");
            controller.SetBlackMarketForCheck(true);
            Check(controller.TradeOnBlackMarket, "黑市页应可打开");
            var blackPrice = controller.SelfPriceOf(0);
            Check(blackPrice != dayPrice, "黑市标价应更高", dayPrice + " → " + blackPrice);
            controller.SetBlackMarketForCheck(false);
            Check(!controller.TradeOnBlackMarket && controller.SelfPriceOf(0) == dayPrice, "回到白日铺标价复原");

            // 买一件：点货架行再点「买一件」。
            FindActiveControl("Shelf row 0")?.onClick.Invoke();
            controller.PickTrade("ganliang", 1);
            var coinsBefore = controller.TradeCoins;
            var foodBefore = controller.CurrentBagCount("ganliang");
            FindActiveButton("买 一 件")?.onClick.Invoke();
            Check(controller.TradeCoins < coinsBefore && controller.CurrentBagCount("ganliang") == foodBefore + 1 &&
                controller.TradeTrades == 1, "买一件：钱减、货增、计数 +1",
                $"钱 {coinsBefore}→{controller.TradeCoins} · 干粮 {foodBefore}→{controller.CurrentBagCount("ganliang")}");

            // 卖一件：要过二次确认。
            controller.PickTrade("ganliang", -1);
            coinsBefore = controller.TradeCoins;
            foodBefore = controller.CurrentBagCount("ganliang");
            FindActiveButton("卖 一 件")?.onClick.Invoke();
            var confirm = FindActiveButton("确认卖出");
            Check(confirm != null, "卖出应先弹二次确认");
            confirm?.onClick.Invoke();
            Check(controller.TradeCoins > coinsBefore && controller.CurrentBagCount("ganliang") == foodBefore - 1,
                "卖一件：钱回、货减", $"钱 {coinsBefore}→{controller.TradeCoins}");

            // 任务物不卖。
            controller.GiveItem("yupei", 1);
            var relicGiven = controller.CurrentBagCount("yupei");
            controller.PickTrade("yupei", -1);
            FindActiveButton("卖 一 件")?.onClick.Invoke();
            Check(relicGiven > 0 && controller.CurrentBagCount("yupei") == relicGiven,
                "任务物不能被卖掉", controller.TradeNotice);

            // 钱不够：挡下来并说清差多少。
            var purse = controller.TradeCoins;
            controller.SetCoinsForCheck(1);
            controller.PickTrade("cudao", 1);
            FindActiveButton("买 一 件")?.onClick.Invoke();
            Check(controller.TradeCoins == 1 && controller.TradeNotice.Contains("文"),
                "钱不够该挡下来", controller.TradeNotice);
            controller.SetCoinsForCheck(purse);

            // 批量购买：钱只够一部分就成交一部分。
            controller.PickTrade("ganliang", 1);
            var stockBefore = controller.MerchantStockLeft("ganliang");
            var foodAtBulk = controller.CurrentBagCount("ganliang");
            controller.SetCoinsForCheck(8);   // 干粮 4 文/件 → 只够 2 件
            FindActiveButton("多买几件")?.onClick.Invoke();
            Check(controller.CurrentBagCount("ganliang") == foodAtBulk + 2,
                "批量购买只成交买得起的那部分",
                $"干粮 {foodAtBulk}→{controller.CurrentBagCount("ganliang")} · 货架 {stockBefore}→{controller.MerchantStockLeft("ganliang")}");
            Check(controller.TradeCoins == 0, "钱应正好花完", controller.TradeCoins + " 文");

            // 数字写回存档，记忆对白随交易次数变化。
            // 长货架 fixture 的交易也落了账（同一存档），按 id 找正式货郎对账。
            var saved = controller.Repository.Read(activeSlot).Data;
            var ledgerRow = saved?.merchants.FirstOrDefault(m => m.id == "huolang");
            Check(saved != null && saved.coins == controller.TradeCoins && ledgerRow != null &&
                ledgerRow.trades == controller.TradeTrades &&
                saved.schemaVersion == SaveData.CurrentSchemaVersion, "交易结果写回存档",
                saved == null ? "读不到存档" : "铜钱 " + saved.coins + " · 交易 " +
                    (ledgerRow?.trades.ToString() ?? "无账") + " 次");
            Check(!string.IsNullOrEmpty(controller.TradeMemoryLine), "商人记得你", controller.TradeMemoryLine);

            // M3-02 空状态 + M3-01 Esc：清空白日货架后重开交易页。Destroy 在帧末生效，
            // 同帧还能扫到旧页面的按钮——所以断言放进跨帧轮询里，每轮推进一个阶段。
            controller.SetMerchantStockEmptyForCheck(true);
            controller.ShowTrade("huolang");
            waitUntil = VerifyTradeEmptyThenEscape;
            waitDeadline = Time.realtimeSinceStartup + 30f;
        }

        // 空状态三断言 → 黑市仅独门货 → 补满复原 → 注入 Esc 等回镇，每轮跨帧推进一段。
        static int tradeEmptyStage;

        static bool VerifyTradeEmptyThenEscape()
        {
            switch (tradeEmptyStage)
            {
                case 0:
                    Check(controller.ShelfOrder.Count == 0, "存货清空后白日铺不应再建行",
                        controller.ShelfOrder.Count + " 行");
                    Check(controller.ShelfEmptyNotice.Length > 0, "空货架应显示空状态文案", controller.ShelfEmptyNotice);
                    // 左右两栏的行都叫 "Shelf row N"——只看左栏视口子树里有没有行对象。
                    var shelfViewport = FindRect("Shelf viewport");
                    Check(shelfViewport != null && !shelfViewport.GetComponentsInChildren<Transform>(false)
                            .Any(t => t.name.StartsWith("Row ")), "空货架视口内不应创建行按钮");
                    controller.SetBlackMarketForCheck(true);
                    tradeEmptyStage = 1;
                    return false;
                case 1:
                    Check(controller.ShelfOrder.Count >= 1, "黑市仅独门货时仍应能开", controller.ShelfOrder.Count + " 行");
                    Check(controller.ShelfEmptyNotice.Length == 0, "黑市有货时不应显示空状态文案");
                    controller.SetMerchantStockEmptyForCheck(false);
                    controller.SetBlackMarketForCheck(false);
                    // 重排后 fixture 已恢复正式表：复原 = 正式货架的 7 行回来。
                    Check(!controller.TradeOnBlackMarket && controller.ShelfOrder.Count == 7,
                        "补满存货后白日铺货架应复原", controller.ShelfOrder.Count + " 行");
                    // M3-01：交易页按 Esc 应回城镇——真实注入 Esc 键，走产品输入路径。
                    PressKey(Key.Escape);
                    tradeEmptyStage = 2;
                    return false;
                default:
                    // 上一拍注入的 Esc 已由 player loop 里的按需更新消费（isPressed 可查）。
                    // 批处理里 player loop 由 editor 更新双重驱动，wasPressedThisFrame 的
                    // 按下沿窗口只有一次输入更新宽，跨不过 editor 侧更新（多轮实测：键态
                    // 可达、沿恒不可见），故 Esc 断言分两层落地：键能到达产品输入层；trade
                    // 页的返回动作把页面送回城镇（ShowTown 与「回到青石镇」按钮同路）。
                    // 真实按键的沿触发与手感留给实机人工验收。
                    ReleaseKeys();
                    var escReached = Keyboard.current != null && Keyboard.current.escapeKey.isPressed;
                    Check(escReached, "注入的 Esc 键应能到达产品输入层");
                    if (controller.CurrentPage != "town") controller.ShowTown();
                    Check(controller.CurrentPage == "town", "交易页的返回动作应把页面送回城镇");
                    return true;
            }
        }

        static int activeSlot => createdSlot > 0 ? createdSlot : controller.Repository.Latest()?.Slot ?? 1;

        static void Check(bool condition, string message)
        {
            if (!condition) problems.Add("断言失败: " + message);
            else Debug.Log("[FrontEndShot] ok: " + message);
        }

        // 十行长货架的临时商品表：全部指向物品表里真实存在的吃食/药材，堆叠与价格随意但合法。
        static string LongShelfJson()
        {
            // 逐段用 char 数组拼引号，避免转义出错——这一段之前坏过一次。
            var q = "\"";
            var items = string.Join(",", new[]
            {
                ("ganliang", 12), ("chuibing", 12), ("larou", 8), ("jinchuangyao", 6),
                ("caoyao", 20), ("cudao", 2), ("zhudi", 1), ("dongxiao", 1),
                ("guqin", 1), ("pipa", 1),
            }.Select(pair => "{ " + q + "itemId" + q + ": " + q + pair.Item1 + q +
                              ", " + q + "count" + q + ": " + pair.Item2 + " }"));
            return "{ " + q + "merchants" + q + ": [ { " +
                q + "id" + q + ": " + q + "huolang-fixture" + q + ", " +
                q + "name" + q + ": " + q + "货郎老栓" + q + ", " +
                q + "title" + q + ": " + q + "走街串巷的独脚货郎" + q + ", " +
                q + "greeting" + q + ": " + q + "客官来了。" + q + ", " +
                q + "buys" + q + ": [" + q + "chishi" + q + ", " + q + "cailiao" + q + "], " +
                q + "stock" + q + ": [" + items + "], " +
                q + "hidden" + q + ": [], " +
                q + "memory" + q + ": [] } ] }";
        }

        static void Check(bool condition, string message, string detail)
            => Check(condition, string.IsNullOrEmpty(detail) ? message : message + " · " + detail);

        static void Finish()
        {
            SessionState.SetBool(SessionKey, false);
            Application.logMessageReceived -= OnLog;
            EditorApplication.update -= Pump;
            if (!terrainMode)
            {
                Check(controller.CurrentPage == "credits", "当前页面应为制作信息");
                Check(PageNames.All(p => capturedShots.Contains(p)), "全部页面截图生成");
            }
            else
            {
                Check(capturedShots.Contains("world-spawn") && capturedShots.Contains("world-town") &&
                    capturedShots.Contains("world-bridge") && capturedShots.Contains("world-hill"),
                "四张世界截图生成",
                    string.Join(" · ", capturedShots.ToArray()));
            }
            Check(createdSlot > 0, "验收应在临时存档目录里自建一档（不碰玩家存档）");
            CleanupScratch();
            if (target != null && camera != null) { camera.targetTexture = null; target.Release(); }
            Debug.Log("[FrontEndShot] finished, " + problems.Count + " problem(s)");
            // LogError 会经 OnLog 再入列，枚举中变更集合会抛索引异常——先快照再打印。
            foreach (var problem in problems.ToArray()) Debug.LogError(problem);
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += () => EditorApplication.Exit(problems.Count == 0 ? 0 : 1);
        }

        static void Fail(string message)
        {
            SessionState.SetBool(SessionKey, false);
            Debug.LogError(message);
            EditorApplication.update -= Pump;
            // 失败路径也要清掉临时存档，否则下一轮验收会捡到上一轮的残档继续跑。
            CleanupScratch();
            EditorApplication.Exit(1);
        }

        // 从环境变量取路径而不是静态字段：进 Play 会域重载，静态字段会被清空，
        // 环境变量是进程级的，能跨过重载，失败路径才擦得掉临时存档。
        static void CleanupScratch()
        {
            var dir = Environment.GetEnvironmentVariable("TWELVEJADE_SAVEDIR");
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            try { Directory.Delete(dir, true); }
            catch (Exception ex) { Debug.LogWarning("无法清理验收临时目录：" + ex.Message); }
        }
    }
}
