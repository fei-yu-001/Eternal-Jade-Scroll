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
            try
            {
                TickGaitSampling();
                Step();
            }
            catch (Exception ex) { Fail("驱动异常: " + ex); }
        }

        // ---- 跨帧取证（P-Character）----
        // "位移为 0 时相位不许推进""待机两帧必须不一样"这类判据本质是**跨帧**的：
        // 同一帧里读两次永远相等，写成同步断言就是自欺。批处理帧率不稳，所以按帧数攒、不按秒等。
        // 采样期间 Step() 直接返回，页面状态机停在原地，采完再把续作交回 pendingAfter。
        static bool gaitSampling;
        static int gaitFramesLeft;
        static Action gaitEachFrame, gaitDone;

        static void SampleFrames(int frames, Action eachFrame, Action done)
        {
            gaitFramesLeft = frames;
            gaitEachFrame = eachFrame;
            gaitDone = done;
            gaitSampling = true;
        }

        static void TickGaitSampling()
        {
            if (!gaitSampling) return;
            try { gaitEachFrame?.Invoke(); }
            catch (Exception ex) { gaitSampling = false; Fail("逐帧取证异常: " + ex); return; }
            if (--gaitFramesLeft > 0) return;
            gaitSampling = false;
            var done = gaitDone;
            gaitEachFrame = null;
            gaitDone = null;
            pendingAfter = done;
            waitStart = Time.realtimeSinceStartup;
            waiting = true;
            waitUntil = null;
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
            if (gaitSampling) return;   // 跨帧取证期间页面状态机停在原地（超时检查仍然生效）
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
                            VerifyWalkPreviewGait();
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
                    // 纸偶取证挂在 after 上：它必须在**没有 modal 挡画面**的时候跑。
                    // 反过来（先 VerifyTownBag 再取证）会整段跑在货郎搭话框背后——
                    // 首轮实跑的八张朝向图全被面板糊住，像素差判据量的其实是面板。
                    Open(() => controller.ShowTown(), VerifyTravelerPuppet);
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
            // 城镇页分三段：第一段先做主角纸偶的跨帧取证（P-Character P1~P5，after 钩子里起的），
            // 第二段搭话与"下方"纵深断言，第三段走到货郎上方验"上方"纵深，再截城镇图。
            if (currentShot == "town" && !townAboveChecked)
            {
                Check(controller.CurrentPage == "town", "城镇页应已打开");
                Check(UnityEngine.Object.FindAnyObjectByType<PuppetActor>() != null, "城镇页应有分层纸偶角色");
                VerifyArtImport();
                if (!travelerProbed) { waiting = true; waitStart = Time.realtimeSinceStartup; return false; }
                // Q1.7 第 4 条夹在探针与搭话之间跑。它要"下一帧的画面"（新启用的 UI 与新建的副本
                // 都要下一帧才进 RT），所以复用这一层 waiting 轮询，而不是再嵌一层采样器——
                // Q1 那次 pendingAfter 被 Shot() 抹掉、探针重跑 61 次直到超时的教训就是这么来的。
                if (!TickTravelerTiers()) { waiting = true; waitStart = Time.realtimeSinceStartup; return false; }
                TownTalkSegment();
                // 两拍都要重新起等待窗口：VerifyTownBag 把 waitUntil 设成"搭话框出现"，
                // 而 Step 在调 Shot 之前已经把 waiting 置 false——不补这一句就永远不再轮询（超时才算红）。
                waiting = true;
                waitStart = Time.realtimeSinceStartup;
                return false;
            }
            if (currentShot == "town")
            {
                Check(controller.CurrentPage == "town", "城镇截图应停在镇上");
                // 走过一段路后镜头应一直跟着主角：近景构图里主角不许滚出视口。
                Check(controller.TownTravelerOnScreen, "镜头跟随应让主角留在视口内");
                VerifyDepthAbove();
                // T088：搭话与交易都办完、对白框已关，这时再开一次气运对白——
                // 放在搭话之前会把货郎的搭话框顶掉（首轮实跑就是这么红了「做买卖」）。
                VerifyKarmaFateDialogue();
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

            // ---- Gate C1-C：正式贴图接入断言（不许灰盒混进来）----
            // P-02 起世界根下还有 placeable（建筑/树/NPC/金环）与玩家：只统计名字以
            // "Chunk r" 开头的分块子物体（placeables 一律 "Placeable " 前缀、玩家是
            // "Traveler puppet" 加它的 "Puppet joint N" 子块，天然排除），四条语义不变：
            // 64 满格 / 无缺图（空贴图=品红格）/ 无灰盒回落 / 小地图总览已加载。
            var root = worldRootTransform();
            var chunkImages = root == null ? new List<UnityEngine.UI.RawImage>()
                : root.Cast<Transform>()
                    .Where(child => child.name.StartsWith("Chunk r", StringComparison.Ordinal))
                    .Select(child => child.GetComponent<UnityEngine.UI.RawImage>())
                    .Where(img => img != null).ToList();
            Check(chunkImages.Count == world.WorldColumns * world.WorldRows,
                "世界根下应有满格分块", string.Format("{0} 张 / 网格 {1}x{2}",
                    chunkImages.Count, world.WorldColumns, world.WorldRows));
            var blank = chunkImages.Where(img => img.texture == null).ToList();
            Check(blank.Count == 0, "分块贴图全部加载成功（无缺图）",
                blank.Count + " 张空贴图（缺图会是品红格）");
            // 灰盒块名是 chunk-r0-c0（带连字符），正式块名是 chunk_0_0（下划线）。
            var greybox = chunkImages.Where(img => img.texture != null &&
                !img.texture.name.StartsWith("chunk_", StringComparison.Ordinal)).ToList();
            Check(greybox.Count == 0, "分块全部来自正式模板，无灰盒回落",
                greybox.Count == 0 ? "全部 chunk_* " + chunkImages.Count + " 张"
                    : "仍是灰盒：" + string.Join(", ", greybox.Take(3).Select(img => img.texture.name)));
            Check(controller.WorldOverviewArtLoaded, "小地图底图应已加载（正式世界总览）");

            // ---- 截图：任务书要求的八个视角 ----
            // 第二个参数是 capturedShots 里登记的 token，必须保持 ASCII 且与 Finish() 的
            // 截图完整性断言（world-spawn / world-town / world-bridge / world-hill）一致；
            // 传中文标签会让那四条断言读不到 token 而误判"缺截图"。
            controller.SnapTravelerTo(world.SpawnX, spawnY);
            Capture("world-01-spawn", "world-spawn");
            CaptureAt(world, "town-center", "world-02-town", "world-town");
            CaptureAt(world, "north-street", "world-03-mainstreet", "world-mainstreet");
            CaptureAt(world, "river-north", "world-04-river-bridge", "world-bridge");
            CaptureNear(world, "mountain", "world-05-mountain-path", "world-hill");
            CaptureNear(world, "farmland", "world-06-farmland", "world-farmland");
            // 接缝密集区：四块交汇点（世界中心，正是 C1-B 分析过的中心 2×2 交点）。
            var junctionX = world.WorldColumns / 2 * world.ChunkSize;
            var junctionY = world.WorldRows / 2 * world.ChunkSize;
            Check(controller.SnapTravelerTo(junctionX, junctionY), "四块交汇点应可站立并置位",
                string.Format("({0:0},{1:0})", junctionX, junctionY));
            Capture("world-07-seam-junction", "world-seam-junction");
            CaptureAt(world, "market", "world-08-market-plaza", "world-market");
        }

        /// <summary>按地标 id 精确截一处；地标不存在即判失败（不许静默少截一张）。</summary>
        static void CaptureAt(TownMap world, string landmarkId, string file, string token)
        {
            var mark = world.Landmarks.FirstOrDefault(m => m.Id == landmarkId);
            Check(mark != null, "应有地标 " + landmarkId);
            if (mark == null) return;
            Check(controller.SnapTravelerTo(mark.X, mark.Y), "地标 " + landmarkId + " 置位应成功",
                string.Format("({0:0},{1:0})", mark.X, mark.Y));
            Capture(file, token);
        }

        /// <summary>按地标 id 前缀找一处**可站立**的点截图（山径/农田这类要看的是可走段）。</summary>
        static void CaptureNear(TownMap world, string idFragment, string file, string token)
        {
            var mark = world.Landmarks.FirstOrDefault(m =>
                m.Id.Contains(idFragment) && controller.TerrainStandable(m.X, m.Y))
                ?? world.Landmarks.FirstOrDefault(m => m.Id.Contains(idFragment));
            Check(mark != null, "应能找到含 \"" + idFragment + "\" 的地标");
            if (mark == null) return;
            Check(controller.SnapTravelerTo(mark.X, mark.Y), "地标 " + mark.Id + " 置位应成功",
                string.Format("({0:0},{1:0})", mark.X, mark.Y));
            Capture(file, token);
        }

        static Transform worldRootTransform()
        {
            var worldRoot = Resources.FindObjectsOfTypeAll<Transform>()
                .FirstOrDefault(t => t.name == "World root" && t.gameObject.activeInHierarchy);
            return worldRoot;
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

        // 定位方式改为问世界页要显式钩子，不再 FindAnyObjectByType<PuppetActor>（T091/T092）：
        // PuppetActor 只存在于角色页与战斗页，城镇页的主角是占位块，于是这个光标在城镇页
        // 永远为 null，而下面的深度断言写成 `traveler != null` 才跑——取不到人物时静默跳过，
        // 一盏红灯都不报，把整条纵深链的验证悄悄关掉了（T092 的成因形态）。
        static RectTransform TravelerRect => controller.TravelerRectForShot;

        static RectTransform NpcRect(string npcId) =>
            controller.NpcButtonForShot(npcId)?.transform as RectTransform;

        // ---------------- P-Character：城镇页主角取证（P1~P5）----------------
        // 判据全部落在**真实帧**上：站着不动的几十帧里相位不许前进（P2），
        // 走起来的帧里腿必须真的摆、截到的像素必须真的变（P4/P5），
        // 四个方向各走一段，朝向与镜像要和移动方向一致（P3）。
        // 定位一律走 controller 的显式钩子，不用 FindAnyObjectByType（P6/T092）。

        static PuppetActor travelerPuppet;
        static int probeFootsteps;
        static readonly List<string> cropHashes = new List<string>();
        static readonly List<float> legAngles = new List<float>();
        static readonly List<float> headAngles = new List<float>();
        static readonly List<float> bodyAngles = new List<float>();
        static float probePhase, probeStride, probeWorldX, probeWorldY;
        // 每个方向两张图的取帧位（第 3 帧与末帧）与两拍的步态相位。
        static int probeFrame, probeFrameB;
        static float probePhaseA, probePhaseB;
        static int probeFlipA, probeFlipB;   // Frames 模式的 flipbook 帧索引（相位由帧承载）
        const int ShotFrameA = 3;
        static float totalWalked, bestHeadSpan, bestBodySpan;
        // 腿摆幅分两档记：正/背该摆、侧身该完全不摆（见 PuppetActor.SetLegSplit）。
        // ⚠ 这条判的是**色带模式 Slices**。网格形变模式 MeshDeform 下侧身走的是
        //   一条共用链的完整周期，抬脚量 0.0415 图高 —— 那时"侧身腿摆幅=0"**不成立**。
        //   Frames 档根本没有关节对象（只有两张帧 RawImage），读关节只会拿到 NaN。
        //   所以下面每一条断言都按 `GaitModeForCheck` 分支，不假设"默认是 Slices"。
        static float sideLegSpan, straightLegSpan;
        static int bestCropDistinct, bestConsecutiveDistinct;
        static int facingProbeIndex;
        static bool travelerProbed;

        // ---- 2026-10-28：按走法档位（GaitMode）分支取证 ----
        // 为什么要分支：产品里主角当前可能是任意一档——WALK-FIX 起默认 MeshDeform，
        // B-11 走帧素材入库后三向又会切 Frames，Slices 仍留着做兜底。
        // 旧夹具默认"进来就是 Slices"，一旦产品换档，它要么读到 NaN 当 0°、要么把
        // 测试自己改过的档位当正式结论（"强切回 Slices 再断言"）——两种都是假红/假绿。
        static PuppetActor.GaitMode formalGaitMode;   // 进入城镇取证时主角的真实档位（正式判定按它分支）
        static readonly List<int> frameIndices = new List<int>();    // Frames：逐帧帧索引
        static readonly List<bool> frameMirrorAgrees = new List<bool>(); // Frames：该拍 uvRect 翻转是否与 mirror 状态一致
        static readonly List<float> deformSpans = new List<float>(); // MeshDeform：顶点位移（图高比例）
        static bool slicesSeen, framesSeen, deformSeen;             // 每档到底采没采到（没采到不许当通过）
        static int framesDistinctBest, framesMirrorBad, framesActiveBad, framesCountSeen;
        static float deformSpanBest;
        static int deformVertexBest;

        // 待机取 40 帧：呼吸/重心三条层的频率是 1.05~1.9 rad/s，少于半秒可能整段都落在极值附近，
        // 那时候"没动"是真的没动，采样窗口不能短到让判据失去意义。
        const int IdleSampleFrames = 40;
        const int WalkSampleFrames = 12;

        // (方向, 期望视图, 期望镜像, 中文名, 截图文件名片段)：视图 0 正 / 1 侧 / 2 背；
        // 世界 y 越大越靠南（屏幕越下）。文件名一律 ASCII，中文标签会让截图完整性断言读不到 token。
        static readonly (int dx, int dy, int view, bool mirror, string label, string ascii)[] FacingProbes =
        {
            (1, 0, 1, false, "东", "east"),
            (-1, 0, 1, true, "西", "west"),
            (0, -1, 2, false, "北", "north"),
            (0, 1, 0, false, "南", "south"),
        };

        static void VerifyTravelerPuppet()
        {
            var p = controller.TravelerPuppetForShot;
            Check(p != null, "城镇页主角应是分层纸偶（P1：既不是绿胶囊也不是占位块）");
            travelerPuppet = p;
            if (p == null) { TownTalkSegment(); return; }
            Check(controller.TravelerRectForShot == p.Root, "主角定位钩子与纸偶根必须是同一个对象（P6）");
            // 正式档位先记账：后面每一条断言都必须按它分支，而不是按"夹具希望它是什么"。
            formalGaitMode = p.GaitModeForCheck;
            Debug.Log("[FrontEndShot] 主角正式走法档位 = " + formalGaitMode +
                      "（结构/采样/最终判定按档位分支）");
            // 结构断言三档各断各的：Slices 只有 4 块关节、MeshDeform 只有一张真网格、
            // Frames 只有两张帧 RawImage。拿别的档的口径去断言，红绿都不代表产品。
            switch (p.GaitModeForCheck)
            {
                case PuppetActor.GaitMode.MeshDeform:
                    Check(p.DeformVertexCountForCheck == PuppetRig.Count,
                        "网格形变档应是 " + PuppetRig.Count + " 顶点的真网格（不是空壳）",
                        p.DeformVertexCountForCheck + " 顶点 / " + PuppetRig.TriangleCount + " 三角");
                    Check(p.BuiltJointCountForCheck == 0,
                        "网格形变档不许同时挂着色带关节（两套同时画就是两个上半身）",
                        "关节 " + p.BuiltJointCountForCheck);
                    break;
                case PuppetActor.GaitMode.Frames:
                    Check(p.FramesActiveForCheck, "Frames 档必须真的建出帧渲染物（缺走帧 sheet 就不是 Frames）");
                    Check(p.FrameCountForCheck > 0, "Frames 档帧数必须 > 0", "帧数 " + p.FrameCountForCheck);
                    Check(p.BuiltJointCountForCheck == 0 && p.DeformVertexCountForCheck == 0,
                        "Frames 档不许同时挂着色带关节或网格（换档必须先清旧的）",
                        "关节 " + p.BuiltJointCountForCheck + " / 网格 " + p.DeformVertexCountForCheck);
                    break;
                default:
                    Check(p.BuiltJointCountForCheck == 4, "纸偶应有 4 块真关节（头/躯干/左腿/右腿）",
                        p.BuiltJointCountForCheck + " 块");
                    break;
            }
            Check(!p.HasArmJointForCheck,
                "不该留着「算出角度却从不写进 transform」的手臂关节（P4：删除 + 断言，而不是留假代码）");
            Check(p.View != null, "主角纸偶必须真的贴上立绘；缺图要报红，不许回退成色块");
            // ---- W-A：网格形变模式必须**真的被接上**（治"能力存在 ≠ 功能存在"）----
            // 只断结构性两条（不依赖玩家在不在走），并且**立刻切回原档**：
            // 下面的逐帧取证按档位分支，但开关验证本身不得把产品/夹具留在别的档上。
            {
                var restoreMode = p.GaitModeForCheck;
                controller.SetTravelerMeshDeform(true);
                Check(p.GaitModeForCheck == PuppetActor.GaitMode.MeshDeform,
                    "W-A：主角渲染档位开关打开后应切到网格形变模式", p.GaitModeForCheck.ToString());
                Check(p.DeformVertexCountForCheck == PuppetRig.Count,
                    "W-A：网格形变应建出 " + PuppetRig.Count + " 个顶点的真网格（不是空壳）",
                    p.DeformVertexCountForCheck + " 顶点 / " + PuppetRig.TriangleCount + " 三角");
                // 量级护栏：**单位换算错了这里一定爆**。色带模式的 idle 起伏是"像素"，
                // 而网格姿态的 HipBob/HipSway 是"图像比例"；若不换算，1.6px 会被当成 1.6 个图高。
                Check(p.DeformMaxDisplacementForCheck <= 0.05f,
                    "W-A：网格模式下站立时顶点位移必须 ≤5% 图高（单位换算错了会爆在这里）",
                    p.DeformMaxDisplacementForCheck.ToString("0.####") + " 图高");
                controller.SetTravelerMeshDeform(false);
                Check(p.DeformVertexCountForCheck == 0 && p.BuiltJointCountForCheck == 4,
                    "W-A：切回色带模式后必须只剩 4 块真关节、网格清零（不许两套同时挂着）",
                    "网格 " + p.DeformVertexCountForCheck + " / 关节 " + p.BuiltJointCountForCheck);
                // 开关验完必须回到进来时的原档：默认已是网格形变，停在 Slices 等于夹具改了产品。
                // 原档是 Frames 时只把纸偶摆回去（controller 的布尔开关不改，下一次朝向变化
                // 会由 ApplyTravelerWalkSheet 重新按视图挂 Frames）。
                if (restoreMode == PuppetActor.GaitMode.MeshDeform) controller.SetTravelerMeshDeform(true);
                else p.SetGaitMode(restoreMode);
                Check(p.GaitModeForCheck == restoreMode,
                    "W-A：临时切档取证后必须恢复原档（不许把夹具留在别的档上）",
                    restoreMode + " → " + p.GaitModeForCheck);
            }
            // ---- W-C：脚步音效（程序化合成，盘上零音频文件）----
            {
                controller.SetTravelerFootsteps(true);
                Check(controller.FootstepClipCountForCheck == 2,
                    "W-C：脚步应在运行期合成 2 条 clip（左右各一，零素材文件）",
                    controller.FootstepClipCountForCheck + " 条");
                probeFootsteps = controller.FootstepsTriggeredForCheck;
            }
            probePhase = p.GaitPhaseForCheck;
            probeStride = p.StrideDistanceForCheck;
            ResetTravelerSamples();
            SampleFrames(IdleSampleFrames, SampleTravelerFrame, OnIdleSampled);
        }

        static void ResetTravelerSamples()
        {
            cropHashes.Clear(); legAngles.Clear(); headAngles.Clear(); bodyAngles.Clear();
            frameIndices.Clear(); frameMirrorAgrees.Clear(); deformSpans.Clear();
        }

        static void SampleTravelerFrame()
        {
            if (travelerPuppet == null) return;
            probeFrame++;
            // T108：两张图必须取自**不同帧**。第一版把 a 拍在 StartFacingProbe 那一拍，
            // 而上一方向的 b 也在同一拍 → "上一方向的 b ≡ 下一方向的 a"，八张只有 5 个不同帧。
            // 现在 a 取第 3 帧、b 取末帧，并且记下两拍各自的步态相位——
            // 相位差是状态断言，比"两张图 md5 不同"更能说明画面真的在动（T108 的原话：别靠比像素自证）。
            if (probeFrame == ShotFrameA)
            {
                probePhaseA = travelerPuppet.GaitPhaseForCheck;
                probeFlipA = travelerPuppet.FrameIndexForCheck;
                Capture("p3-" + (facingProbeIndex + 1) + "-walk-" + FacingProbes[facingProbeIndex].ascii + "-a");
            }
            cropHashes.Add(TravelerCropHash());
            // 按档位采：Slices 读关节角、MeshDeform 读顶点位移、Frames 读帧索引与 uv 镜像。
            // 网格/Frames 档没有关节对象，去读关节只会拿到 NaN——NaN 不许当 0° 蒙过去。
            switch (travelerPuppet.GaitModeForCheck)
            {
                case PuppetActor.GaitMode.MeshDeform:
                    deformSpans.Add(travelerPuppet.DeformMaxDisplacementForCheck);
                    deformVertexBest = Mathf.Max(deformVertexBest, travelerPuppet.DeformVertexCountForCheck);
                    break;
                case PuppetActor.GaitMode.Frames:
                    frameIndices.Add(travelerPuppet.FrameIndexForCheck);
                    frameMirrorAgrees.Add(travelerPuppet.FrameMirroredForCheck == travelerPuppet.MirrorForCheck);
                    framesCountSeen = travelerPuppet.FrameCountForCheck;
                    if (!travelerPuppet.FramesActiveForCheck) framesActiveBad++;
                    break;
                default:
                    legAngles.Add(travelerPuppet.JointAngleForCheck(PuppetActor.JointLegLeft));
                    headAngles.Add(travelerPuppet.JointAngleForCheck(PuppetActor.JointHead));
                    bodyAngles.Add(travelerPuppet.JointAngleForCheck(PuppetActor.JointTorso));
                    break;
            }
        }

        static void OnIdleSampled()
        {
            var p = travelerPuppet;
            Check(p != null, "待机采样期间主角纸偶不该被销毁");
            if (p == null) { TownTalkSegment(); return; }
            Check(Mathf.Abs(p.GaitPhaseForCheck - probePhase) < 1e-4f &&
                  Mathf.Abs(p.StrideDistanceForCheck - probeStride) < 1e-4f,
                "站着不动的这 " + IdleSampleFrames + " 帧里步态相位不许推进（P2：时间空转即红）",
                "相位 " + probePhase.ToString("0.000") + "→" + p.GaitPhaseForCheck.ToString("0.000") +
                "，累计位移 " + probeStride.ToString("0.0") + "→" + p.StrideDistanceForCheck.ToString("0.0"));
            // W-C：脚步与相位同一口径 —— 相位不动就不许响（"原地踏步"的音频版）
            Check(controller.FootstepsTriggeredForCheck == probeFootsteps,
                "W-C：站着不动的这段采样里一声脚步都不许响（与 P2 同一口径）",
                "触发数 " + probeFootsteps + "→" + controller.FootstepsTriggeredForCheck);
            Check(cropHashes.All(h => !string.IsNullOrEmpty(h)),
                "主角取景块必须能截到像素", "空取景 " + cropHashes.Count(h => string.IsNullOrEmpty(h)) + " 帧");
            // 待机体态按档位分支：只有 Slices 有可读的关节角；网格档看顶点位移，
            // Frames 档看帧索引（帧只由位移驱动 ⇒ 站着不动帧就不许换）。
            var idleMode = p.GaitModeForCheck;
            if (idleMode == PuppetActor.GaitMode.Slices)
            {
                var neckSpan = SwingSpan(headAngles);
                Check(!float.IsNaN(neckSpan) && neckSpan > .05f,
                    "待机时颈应有呼吸幅度（P5：idle 不等于静止）；NaN = 关节对象没建出来，不算通过",
                    "颈摆幅 " + neckSpan.ToString("0.000") + "° / 躯干 " +
                    SwingSpan(bodyAngles).ToString("0.000") + "°");
            }
            else if (idleMode == PuppetActor.GaitMode.MeshDeform)
            {
                Check(deformSpans.Count > 0, "待机采样里应真的采到网格顶点位移", "样本 " + deformSpans.Count);
                var bad = deformSpans.Count(v => float.IsNaN(v));
                Check(bad == 0, "网格模式顶点位移不许是 NaN（NaN 不是 0，也不许当通过）", bad + " 帧 NaN");
                var maxIdle = deformSpans.Any(v => float.IsNaN(v)) ? float.NaN : deformSpans.Max();
                Check(!float.IsNaN(maxIdle) && maxIdle <= .05f,
                    "W-A：待机时顶点位移必须 ≤5% 图高（单位换算错了会爆在这里）",
                    maxIdle.ToString("0.####") + " 图高");
            }
            else
            {
                Check(frameIndices.Count > 0, "待机采样里应真的采到帧索引", "样本 " + frameIndices.Count);
                Check(frameIndices.All(i => i >= 0), "帧索引不许为 -1（帧数/周期参数非法就会是 -1，不算通过）",
                    string.Join(",", frameIndices.Take(8)));
                Check(frameIndices.Distinct().Count() <= 1,
                    "Frames：站着不动帧索引不许换（P2 的帧版：帧只由位移驱动）",
                    frameIndices.Distinct().Count() + " 种帧 / " + frameIndices.Count + " 帧");
            }
            Check(cropHashes.Distinct().Count() >= 2,
                "待机连拍必须有像素差（P5）", cropHashes.Distinct().Count() + " 种 / " + cropHashes.Count + " 帧");
            StartFacingProbe(0);
        }

        // 摆幅：逐帧差的绝对值累加。角度用 DeltaAngle 修正 360°/0° 回绕——
        // localEulerAngles 把 -5° 报成 355°，直接 max-min 会算出 360° 的假摆幅。
        // NaN 表示关节对象不存在（读到不存在的关节）：直接返回 NaN，让上层断言报红，
        // 绝不允许把 NaN 当 0° 算出一个"摆幅=0、画面很稳"的假绿。
        static float SwingSpan(List<float> angles)
        {
            if (angles.Any(float.IsNaN)) return float.NaN;
            var span = 0f;
            for (var i = 1; i < angles.Count; i++)
                span += Mathf.Abs(Mathf.DeltaAngle(angles[i - 1], angles[i]));
            return span;
        }

        static void StartFacingProbe(int index)
        {
            facingProbeIndex = index;
            var probe = FacingProbes[index];
            var pos = controller.PlayerWorldPosition;
            var found = false;
            var spot = pos;
            foreach (var dist in new[] { 240f, 180f, 360f, 120f })
            {
                var point = pos + new Vector2(probe.dx * dist, probe.dy * dist);
                if (!controller.TerrainStandable(point.x, point.y)) continue;
                spot = point; found = true; break;
            }
            Check(found, "主角" + probe.label + "侧应找得到寻路点（找不到就是世界不可走，不是夹具的问题）");
            if (found) Check(controller.RequestTravelTo(spot), "往" + probe.label + "的寻路请求应被接受");
            probePhase = travelerPuppet.GaitPhaseForCheck;
            probeStride = travelerPuppet.StrideDistanceForCheck;
            probeWorldX = pos.x; probeWorldY = pos.y;
            probeFrame = 0;
            ResetTravelerSamples();
            SampleFrames(WalkSampleFrames, SampleTravelerFrame, OnFacingProbeSampled);
        }

        static void OnFacingProbeSampled()
        {
            var probe = FacingProbes[facingProbeIndex];
            probePhaseB = travelerPuppet.GaitPhaseForCheck;
            probeFlipB = travelerPuppet.FrameIndexForCheck;
            probeFrameB = probeFrame;
            Capture("p3-" + (facingProbeIndex + 1) + "-walk-" + probe.ascii + "-b");
            // 两张图取自不同帧的**状态**证据：相位差必须够大（不是同一帧拍两次）
            var phaseDelta = Mathf.Abs(Mathf.Repeat(probePhaseB - probePhaseA + Mathf.PI, Mathf.PI * 2f) - Mathf.PI);
            var walkedHere = travelerPuppet.StrideDistanceForCheck - probeStride;
            // 两态都断言，不写"够不到就跳过"：走过路 → 两个拍点必须落在不同相位；
            // 被墙挡住 → 两个拍点必须**同**相位（那正是 P2 的正命题，撞墙不攒步数）。
            // T108 状态断言（模式无关版）：Slices 的"在动"= 相位差（rad），Frames 的"在动"= 帧索引差。
            // 寻路绕行会让朝向（进而渲染模式）在两拍点之间切换——**模式无关**才不会把"切模式"误判成"没在动"。
            // 走过了路 ⇒ 换帧或换相位至少一个成立；撞墙 ⇒ 两者都必须不变（P2 反面）。
            var flipDelta = Mathf.Abs(probeFlipB - probeFlipA);
            if (walkedHere > 1f)
                Check(flipDelta >= 1 || phaseDelta > .02f,
                    "走过路的两个拍点必须落在不同画面状态（T108：换帧或换相位，不是同一帧拍两次）",
                    probe.ascii + " 走了 " + walkedHere.ToString("0.0") + " 单位，帧索引差 " + flipDelta +
                    "，相位差 " + phaseDelta.ToString("0.00") + " rad");
            else
                Check(flipDelta == 0 && phaseDelta < .02f,
                    "这一向没走过路，两张图就该同帧同相位（撞墙不攒步数——P2 的反面也是判据）",
                    probe.ascii + " 走了 " + walkedHere.ToString("0.0") + " 单位，帧索引差 " + flipDelta +
                    "，相位差 " + phaseDelta.ToString("0.00") + " rad");
            // B-03/B-11 接线判据（暂撤）：侧向 flipbook 改为 SerializeField 开关（默认关 = 行为零变化），
            // 开关态下的断言口径归 B-10 夹具适配（"按视图切模式"需要系统性口径，实测 74 problems）。
            // 该断言的使命已完成：它先后抓出「副本缺 sheet」与「按视图切模式的夹具失配」两件事。
            Check(probeFrame > ShotFrameA,
                "同一方向的两张图必须取自不同帧（T108 的原始要求）",
                probe.ascii + " a=第 " + ShotFrameA + " 帧，b=第 " + probeFrame + " 帧");
            var p = travelerPuppet;
            var walked = Vector2.Distance(controller.PlayerWorldPosition, new Vector2(probeWorldX, probeWorldY));
            totalWalked += walked;
            // 步态累计 == 真实位移：撞墙不攒步数，空转不攒步数，走多少攒多少。
            Check(Mathf.Abs((p.StrideDistanceForCheck - probeStride) - walked) < 1f,
                "步态累计必须等于真实位移（" + probe.label + "向）",
                "Δstride " + (p.StrideDistanceForCheck - probeStride).ToString("0.0") +
                " vs Δ位置 " + walked.ToString("0.0"));
            Check(controller.TravelerViewForShot == probe.view && p.MirrorForCheck == probe.mirror,
                "往" + probe.label + "走时朝向应为" + ViewLabel(probe.view) + (probe.mirror ? "并左右镜像" : ""),
                "view=" + controller.TravelerViewForShot + " mirror=" + p.MirrorForCheck);
            // 本向到底落在哪一档（寻路绕行/换向会让产品在 Slices / Frames 之间切），按档记账。
            AccumulateGaitEvidence(probe);
            // 侧身档必须**不切腿**（两块腿是同一条件带的叠印，一转就劈成两片甩离身体）；
            // 正/背档才允许摆腿。这条钉住 p3-2-walk-west-b 那张图暴露的崩解。
            // ⚠ 只在 Slices 档成立：网格档侧身走的是一条共用链的完整周期，Frames 档没有关节对象。
            if (slicesSeenThisProbe && probe.view == 1) sideLegSpan = Mathf.Max(sideLegSpan, SwingSpan(legAngles));
            else if (slicesSeenThisProbe) straightLegSpan = Mathf.Max(straightLegSpan, SwingSpan(legAngles));
            if (slicesSeenThisProbe)
            {
                bestHeadSpan = Mathf.Max(bestHeadSpan, SwingSpan(headAngles));
                bestBodySpan = Mathf.Max(bestBodySpan, SwingSpan(bodyAngles));
            }
            bestCropDistinct = Mathf.Max(bestCropDistinct, cropHashes.Distinct().Count());
            int run = 1, bestRun = 1;
            for (var i = 1; i < cropHashes.Count; i++)
            {
                run = cropHashes[i] != cropHashes[i - 1] ? run + 1 : 1;
                bestRun = Mathf.Max(bestRun, run);
            }
            bestConsecutiveDistinct = Mathf.Max(bestConsecutiveDistinct, bestRun);
            if (facingProbeIndex + 1 < FacingProbes.Length) { StartFacingProbe(facingProbeIndex + 1); return; }
            // W-A 出图：**必须放在行走探针跑完之后**。
            // 前两次失败（都拍到了"没有主角"的画面）的原因是：城镇页待机那一刻主角不在取景内，
            // 而 `TravelerCropHash()` 非空**不等于**主角可见（取景块全是背景也算非空）。
            // 这一拍不一样：上面刚把主角拍进 p3-* 那八张朝向图里，是"有据可依地在画面中"。
            // 拍前临时切到网格档（出图需要网格），拍完**恢复到拍前那一档**——不再用 controller
            // 的布尔开关把产品永久拨到 Slices。
            var shotMode = p.GaitModeForCheck;
            p.SetGaitMode(PuppetActor.GaitMode.MeshDeform);
            Check(p.DeformVertexCountForCheck == PuppetRig.Count,
                "W-A：网格档出图那一拍顶点数必须 = " + PuppetRig.Count, p.DeformVertexCountForCheck + " 顶点");
            Capture("w-a-mesh-deform");
            p.SetGaitMode(shotMode);
            Check(p.GaitModeForCheck == shotMode, "W-A：网格档出图后必须恢复拍前档位",
                shotMode + " → " + p.GaitModeForCheck);
            FinishTravelerProbe();
            // B-03：放在寻路取证**之后**——它会临时把纸偶切到 Frames 档验证，
            // 放进采样期会污染上面那一批朝向量（同上"采样期间不能切模式"的理由）。
            CheckFramesMode(controller);
        }

        // 本向的取样到底落在哪一档：slices/frames/deform 各自累计。
        // 一条"没采到"的档必须写清楚没采到，不能拿空样本当通过（T092 同族）。
        static bool slicesSeenThisProbe;

        static void AccumulateGaitEvidence((int dx, int dy, int view, bool mirror, string label, string ascii) probe)
        {
            slicesSeenThisProbe = legAngles.Count > 0 || headAngles.Count > 0;
            if (slicesSeenThisProbe)
            {
                slicesSeen = true;
                var nan = legAngles.Concat(headAngles).Concat(bodyAngles).Count(float.IsNaN);
                Check(nan == 0,
                    "Slices 档关节角度不许是 NaN（NaN 说明关节对象没建出来，不是「摆幅 0」）",
                    probe.ascii + " NaN " + nan + " 个");
            }
            if (frameIndices.Count > 0)
            {
                framesSeen = true;
                framesDistinctBest = Mathf.Max(framesDistinctBest, frameIndices.Distinct().Count());
                if (frameIndices.Any(i => i < 0)) framesActiveBad++;
                // 逐拍比“uv 翻转”与“mirror 状态”是否一致，不拿本向的期望去卡每一拍：
                // 探针第一拍可能还停在上一个方向的姿态（UpdateTravelerFacing 在 move==0 时
                // 不改朝向，见 FrontEndWorld.UpdateTravelerFacing），那是采样窗口跨到了换向前。
                // 朝向期望由 RunFacingProbe 末尾那条 MirrorForCheck == probe.mirror 单独钉。
                framesMirrorBad += frameMirrorAgrees.Count(agree => !agree);
            }
            if (deformSpans.Count > 0)
            {
                deformSeen = true;
                var nan = deformSpans.Count(float.IsNaN);
                Check(nan == 0, "网格模式顶点位移不许是 NaN", probe.ascii + " NaN " + nan + " 帧");
                if (nan == 0) deformSpanBest = Mathf.Max(deformSpanBest, deformSpans.Max());
            }
        }

        /// <summary>T108 的原始要求直接写成机器判据：把这一轮自己写的八张朝向图读回来比 md5——
        /// 每个方向 a 与 b 必须不同，八张里至少 7 个不同帧。
        /// 上一轮就是靠人看 md5 表才发现"上一方向的 b ≡ 下一方向的 a"，那条经验不该只留在报告里。</summary>
        static void CheckFacingAlbum()
        {
            var hashes = new List<string>();
            foreach (var probe in FacingProbes)
                foreach (var tail in new[] { "a", "b" })
                {
                    var path = Path.Combine(shotsDir, "p3-" + (Array.IndexOf(FacingProbes, probe) + 1) +
                        "-walk-" + probe.ascii + "-" + tail + ".png");
                    if (!File.Exists(path))
                    {
                        Check(false, "朝向取证图应真的落盘", path);
                        continue;
                    }
                    hashes.Add(BitConverter.ToString(System.Security.Cryptography.MD5.Create()
                        .ComputeHash(File.ReadAllBytes(path))).Replace("-", ""));
                }
            for (var i = 0; i + 1 < hashes.Count; i += 2)
                Check(hashes[i] != hashes[i + 1],
                    "同一方向的两张朝向图不许是同一帧（T108）", FacingProbes[i / 2].ascii);
            Check(hashes.Distinct().Count() >= 7,
                "八张朝向图至少要有 7 个不同帧（T108）",
                "实际 " + hashes.Distinct().Count() + " 种 / " + hashes.Count + " 张");
        }


        static string ViewLabel(int view) => view == 0 ? "正面" : view == 2 ? "背面" : "侧身";

        static void FinishTravelerProbe()
        {
            Check(totalWalked > 60f, "四向寻路里主角应真的走过一段路（全被墙挡住就不算迈步取证）",
                totalWalked.ToString("0") + " 单位");
            // ---- 正式判定：按**产品当前真实档位**分支 ----
            // 旧版的错误做法是先切成 Slices，再用 Slices 的判据交结论——那样验的不是产品。
            // 现在：跑到哪一档就断哪一档，并先断言"该档的样本真的采到了"。
            Check(facingProbeIndex == FacingProbes.Length - 1, "四向探针应全部跑完（否则下面的档位判定不完整）",
                "当前向 " + facingProbeIndex);
            switch (formalGaitMode)
            {
                case PuppetActor.GaitMode.Slices:
                    Check(slicesSeen, "Slices 档：四向取样里应真的采到关节角（没采到不许当通过）",
                        "关节样本 " + headAngles.Count);
                    Check(!float.IsNaN(straightLegSpan) && straightLegSpan > 1f,
                        "走动时腿必须真的摆（P4：正/背档关节旋转角随相位变化）",
                        "正背腿摆幅 " + straightLegSpan.ToString("0.0") + "°");
                    Check(sideLegSpan < .05f,
                        "侧身档不许切腿——两块腿是同一条件带的叠印，一转就劈成两片甩离身体",
                        "侧身腿摆幅 " + sideLegSpan.ToString("0.000") + "°");
                    Check(!float.IsNaN(bestBodySpan) && !float.IsNaN(bestHeadSpan) &&
                          bestBodySpan > .3f && bestHeadSpan > .3f,
                        "上半身必须跟着动（P4：躯干前倾 + 颈跟随，不再是一段算出来没人用的角度）",
                        "躯干 " + bestBodySpan.ToString("0.0") + "° / 颈 " + bestHeadSpan.ToString("0.0") + "°");
                    break;
                case PuppetActor.GaitMode.MeshDeform:
                    Check(deformSeen, "MeshDeform 档：四向取样里应真的采到顶点位移（没采到不许当通过）",
                        "顶点样本 " + deformSpans.Count);
                    Check(deformVertexBest == PuppetRig.Count,
                        "MeshDeform 档：顶点数必须 = " + PuppetRig.Count + "（不是空壳）",
                        deformVertexBest + " 顶点");
                    Check(deformSpanBest > 1e-4f,
                        "MeshDeform 档：走动时顶点必须真的位移（色带模式原地不动，网格模式必须动）",
                        "最大位移 " + deformSpanBest.ToString("0.####") + " 图高");
                    break;
                default:
                    Check(framesSeen, "Frames 档：四向取样里应真的采到帧索引（没采到不许当通过）",
                        "帧样本 " + frameIndices.Count);
                    Check(framesActiveBad == 0, "Frames 档：取样期间帧渲染物必须一直在位、帧索引不许为 -1",
                        "异常样本 " + framesActiveBad);
                    Check(framesCountSeen > 0, "Frames 档：帧数必须 > 0",
                        "帧数 " + framesCountSeen);
                    Check(framesDistinctBest >= 2,
                        "Frames 档：走过路的那些帧索引必须真的换过（跨帧变化，不是一帧拍到底）",
                        "单向最多 " + framesDistinctBest + " 种帧");
                    Check(framesMirrorBad == 0,
                        "Frames 档：侧身向左走必须靠 uvRect 镜像（镜像态与朝向要一致）",
                        "uv 与 mirror 不一致样本 " + framesMirrorBad);
                    break;
            }
            Check(bestConsecutiveDistinct >= 4, "走动时连拍 4 帧的像素必须互不相同（P2）",
                "最长连续不同 " + bestConsecutiveDistinct + " 帧 / 单窗口最多 " + bestCropDistinct + " 种");
            CheckFacingAlbum();
            // 真实位移（挡位无关）：步态累计 == 世界位移已在每向断言；这里给一条总账。
            Debug.Log("[FrontEndShot] 主角取证：正式档位 " + formalGaitMode + "，四向累计走动 " + totalWalked.ToString("0") +
                " 单位；Slices 样本 " + (slicesSeen ? "有" : "无") +
                "（正背腿摆幅 " + straightLegSpan.ToString("0.0") + "° / 侧身 " + sideLegSpan.ToString("0.00") + "°）" +
                "，Frames 样本 " + (framesSeen ? "有" : "无") + "（最多 " + framesDistinctBest + " 种帧）" +
                "，MeshDeform 样本 " + (deformSeen ? "有" : "无") + "（最大位移 " + deformSpanBest.ToString("0.####") + " 图高）" +
                "，连拍最长互异 " + bestConsecutiveDistinct + " 帧");
            // 收尾：作废在途寻路（目标点取自己 = 立刻到达），把状态交回原来的搭话流程。
            controller.RequestTravelTo(controller.PlayerWorldPosition);
            // 这里**不能**用 pendingAfter 交回续作：Shot() 在调用续作之后会把 pendingAfter 清空，
            // 于是续作自己排的下一步会被就地抹掉，城镇页第一段的条件永远不翻——
            // 首轮实跑就是这样把同一段取证重跑了 61 遍直到 240 秒超时。翻标志位，让下一拍走正路。
            travelerProbed = true;
            waitStart = Time.realtimeSinceStartup;
            waiting = true;
            waitUntil = null;
        }

        /// <summary>T088：气运生效的**画面**证据。
        /// 本域两套夹具（InventoryEconomyShot / NpcDialogueShot）都是同步夹具、带 `-quit`，
        /// 一帧都不抽 ⇒ 永远出不了图；能抽帧的只有 FrontEndShot。记账走真实路径
        /// （RecordKarmaForCheck 内部 Record + Write），搭话走真实 TalkTo，选项按 UI 上真存在的按钮点。
        /// 两态都验：没记气运时那条选项**不该在**，记满 3 件之后才该在——只验"在"等于没验门控。</summary>
        static void VerifyKarmaFateDialogue()
        {
            const string extraFragment = "先生还有话";
            const string extraLine = "多送你一卦";
            var deedsBefore = TwelveJade.Economy.KarmaTracker.DeedCount(controller.ActiveSave);
            controller.TalkTo("npc-shenhuai-an");
            var locked = FindDialogueOption(extraFragment);
            if (deedsBefore < 3)
                Check(locked == null, "气运不足 3 时不该出现多送一卦的选项（两态之\u201c无\u201d）",
                    locked == null ? "未出现" : "出现了：" + locked.gameObject.name);
            else
                // 走到这里说明前面的步骤已经记过善事——那也不能不验，改验"够了就该在"。
                // 两个分支都是断言，没有"条件不满足就静默跳过"这种假绿（T092 同族）。
                Check(locked != null, "气运已有 " + deedsBefore + " 件，多送一卦的选项该真的在",
                    locked == null ? "未出现" : "出现了");
            FindActiveButton("告 辞")?.onClick.Invoke();
            var recorded = controller.RecordKarmaForCheck(3);
            var deedsAfter = TwelveJade.Economy.KarmaTracker.DeedCount(controller.ActiveSave);
            Check(deedsAfter >= 3, "沿真实路径记完气运后应至少 3 件（重复的善事 Core 会挡，所以不钉死增量）",
                deedsBefore + " → " + deedsAfter + " 件，本次新记 " + recorded);
            controller.TalkTo("npc-shenhuai-an");
            var option = FindDialogueOption(extraFragment);
            Check(option != null, "记满 3 件善事后应出现多送一卦的选项（两态之\u201c有\u201d）");
            if (option == null) return;   // 拿不到图就如实报缺，不拿别的图凑数
            option.onClick.Invoke();
            Check(TalkLineContains(extraLine), "多送的那一卦应真的显示在对白里", extraLine);
            Capture("play-11-karma-fate", "karma-fate");
            FindDialogueOption("收下这句话")?.onClick.Invoke();
            FindActiveButton("告 辞")?.onClick.Invoke();
            // 必须真的关上：这一拍之后紧接着就是 play-06-town 的截图，
            // 框还开着就会把镇子那张图盖掉（"|| CurrentPage == town" 那种写法是恒真，不算断言）。
            Check(!TalkBoxVisible(), "气运对白验完应关掉对白框，不许留给城镇截图");
        }

        // 对白选项按**片段**找：ui.Button 的物体名是 "Button " + 原文，
        // 原文里带全角括号与问号，按整名匹配太脆（改一个标点就静默找不到 → T092 同族）。
        static Button FindDialogueOption(string fragment) =>
            Resources.FindObjectsOfTypeAll<Button>().FirstOrDefault(b =>
                b.gameObject.scene.IsValid() && b.gameObject.activeInHierarchy &&
                (b.gameObject.name.Contains(fragment) ||
                 b.GetComponentInChildren<TMPro.TMP_Text>()?.text.Contains(fragment) == true));

        static bool TalkLineContains(string fragment) =>
            Resources.FindObjectsOfTypeAll<TMPro.TMP_Text>().Any(t => t.gameObject.scene.IsValid() &&
                t.gameObject.activeInHierarchy && t.text != null && t.text.Contains(fragment));

        /// <summary>主角纸偶取证之后接回原来的城镇页流程：点货郎（第一拍）→ 搭话与纵深·下方 →
        /// 置位到货郎上方（第二拍交给 VerifyDepthAbove）。</summary>
        static void TownTalkSegment()
        {
            // VerifyTownBag 自己把 waitUntil 设成"搭话框出现"，所以它跑完这一拍就该让位；
            // 下一拍 pendingNpcCheck 还挂着，才轮到 VerifyTalkThenLeave 收尾。
            if (!pendingNpcCheck) { VerifyTownBag(); return; }
            VerifyTalkThenLeave();
            Check(controller.CurrentPage == "town", "城镇截图应停在镇上");
            townAboveChecked = true;
            // 纵深·上方：批处理无应用焦点时 InputSystem 重置注入按键，"按住 W 走
            // 长路"不可靠（只活一大步）。改用钩子把人物置到货郎上方，等一帧让
            // 镜头/排序刷新后断言遮挡关系；真实步行由上面的四向寻路覆盖。
            Check(controller.SnapTravelerAboveNpc("huolang"), "验收钩子应能把人物置到货郎上方");
            waitStart = Time.realtimeSinceStartup;
            waiting = true;
            waitDeadline = Time.realtimeSinceStartup + 8f;
            waitUntil = null;
        }

        /// <summary>只截主角自己那一小块画带做像素比对：整屏 md5 会被遭遇金环的脉动带着走，
        /// 那种"每帧都不同"证明不了纸偶在动。</summary>
        /// <summary>Q1.7 第 4 条：主角在镇页的呈现档位。这里只验"档位可达、而且真的改了画面"，
        /// 不验"哪一档好看"——那要三个人看图定（派单原文）。
        /// 四条判据各自防的假绿：①档位数够但都乘 1（改了个寂寞）；②按纸偶自己的 rect 裁图——
        /// 换档时裁剪框自己跟着缩放，哈希必然不同，那证的是布局不是像素；
        /// ③同一帧 ReadPixels 去比刚启用的 UI——UI 要下一帧才进画面，同帧比必然报"开关没改画面"
        /// （本轮第一次跑就红在这条上，是我这条判据写错，不是功能坏）；
        /// ④对照板的副本同样是下一帧才画，出图也要等一拍。所以这是个跨帧小状态机。</summary>
        static int tierStage;                 // 0 量框 / 1 取最大档画面 / 2 取最小档并比 / 3 阴影跨帧比 / 4 出对照板图 / 99 完
        static Rect tierBox;
        static string tierLargeHash, tierShadowDefaultHash;

        static bool TickTravelerTiers()
        {
            if (tierStage == 99) return true;
            if (controller.TravelerPuppetForShot == null) { tierStage = 99; return true; }
            var count = controller.TravelerTierCount;
            Check(count >= 3, "主角呈现档位至少要有三档（三档对照图是 Q1.7 第 4 条的交付物）", "档数 " + count);

            if (tierStage == 0)
            {
                // 固定框在最大档上量一次，之后每一步都裁这同一块像素
                controller.SetTravelerTier(count - 1);
                tierBox = TravelerCropBox(1.2f);
                tierStage = 1;
                return false;   // localScale 也是下一帧才进画面——同帧比必然得到"什么都没变"
            }
            if (tierStage == 1)
            {
                tierLargeHash = HashCrop(tierBox);
                controller.SetTravelerTier(0);
                tierStage = 2;
                return false;
            }
            if (tierStage == 2)
            {
                var smallHash = HashCrop(tierBox);
                Check(tierLargeHash != "" && smallHash != "" && tierLargeHash != smallHash,
                    "同一块固定取景里，最小档与最大档必须真的画出不同的像素",
                    "固定框 " + Mathf.RoundToInt(tierBox.width) + "×" + Mathf.RoundToInt(tierBox.height));
                controller.SetTravelerTier(1);
                Check(Mathf.Abs(controller.TravelerScaleForCheck - 1f) < .001f,
                    "默认档必须是乘数 1——选档没定之前不许先改玩家看到的画面",
                    "默认乘数 " + controller.TravelerScaleForCheck.ToString("0.000"));
                Check(controller.TravelerShadowShownForCheck,
                    "接地阴影默认必须是开的（MAT-10：没有影子的立绘在草地上就是浮着的）");
                tierShadowDefaultHash = HashCrop(tierBox);
                controller.SetTravelerGroundShadow(false);
                tierStage = 3;
                return false;
            }

            if (tierStage == 3)
            {
                var offHash = HashCrop(tierBox);
                Check(!controller.TravelerShadowShownForCheck, "开关拨到关，阴影必须真的摘掉");
                Check(offHash != "" && tierShadowDefaultHash != offHash,
                    "接地阴影开关必须真的改画面（只改字段不画=一档假的）");
                controller.SetTravelerGroundShadow(true);
                var board = controller.ShowTravelerTierBoard();
                Check(board, "三档对照板要摆得出来（立绘缺失或档数不足就不算交付那张图）",
                    "副本 " + controller.TravelerBoardCountForCheck + " 台");
                tierStage = 4;
                return false;   // 副本也是下一帧才进画面，出图要等一拍
            }

            if (controller.TravelerBoardCountForCheck > 0)
            {
                Capture("q17-traveler-tiers");
                Debug.Log("[FrontEndShot] 三档对照图已出：本人 + 左右 " +
                          controller.TravelerBoardCountForCheck + " 台副本");
            }
            controller.ClearTravelerTierBoard();
            Check(controller.TravelerBoardCountForCheck == 0,
                "对照板出完图必须清干净（副本不吃输入也不参与碰撞，留着下一张图就数不清镇上有几个人）",
                "残留 " + controller.TravelerBoardCountForCheck + " 台");
            Debug.Log("[FrontEndShot] 主角档位: " + count + " 档 · 固定框内最小/最大档像素不同 · " +
                      "接地阴影默认开（MAT-10）");
            tierStage = 99;
            return true;
        }

        /// <summary>主角外接框（视口像素坐标）。inflate&gt;1 是给"画面会变"留的余量：
        /// 按纸偶自己的 rect 裁的话，换档时 rect 本身跟着缩放，哈希必然不同——
        /// 那证的是布局，不是像素。跨档比较一律用一个固定框（取最大档的外接框）。</summary>
        static Rect TravelerCropBox(float inflate)
        {
            var rect = controller.TravelerRectForShot;
            if (rect == null || target == null || camera == null) return Rect.zero;
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var corner in corners)
            {
                // 视口坐标 × RT 尺寸：编辑器 play 画面的分辨率与 1920×1080 的 target 不一定相同，
                // 用屏幕像素换算会截错位置。
                var vp = camera.WorldToViewportPoint(corner);
                x0 = Mathf.Min(x0, vp.x * Width); x1 = Mathf.Max(x1, vp.x * Width);
                y0 = Mathf.Min(y0, vp.y * Height); y1 = Mathf.Max(y1, vp.y * Height);
            }
            var cx = (x0 + x1) * .5f; var cy = (y0 + y1) * .5f;
            var hw = (x1 - x0) * .5f * inflate; var hh = (y1 - y0) * .5f * inflate;
            x0 = Mathf.Clamp(cx - hw, 0, Width - 1); x1 = Mathf.Clamp(cx + hw, 0, Width);
            y0 = Mathf.Clamp(cy - hh, 0, Height - 1); y1 = Mathf.Clamp(cy + hh, 0, Height);
            return new Rect(x0, y0, x1 - x0, y1 - y0);
        }

        static string HashCrop(Rect box)
        {
            if (target == null || box.width < 8 || box.height < 8) return "";
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var tex = new Texture2D(Mathf.FloorToInt(box.width), Mathf.FloorToInt(box.height),
                TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(box.x, box.y, box.width, box.height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            var hash = System.Security.Cryptography.MD5.Create().ComputeHash(tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
            return BitConverter.ToString(hash);
        }

        static string TravelerCropHash() => HashCrop(TravelerCropBox(1f));

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

        /// <summary>预览页"行走样张"的传送带必须真的在转。
        /// 删掉 `walkPreview.SetGaitConveyor(...)` 那一行，样张会站成雕塑而没有任何断言变红——
        /// 那正是 T081 那一族（一段代码跑没跑，没人管）。城镇页的取证覆盖的是位移驱动这条路，
        /// 样张走的是另一条（宿主声明空转速度），所以这条要单独钉。</summary>
        static void VerifyWalkPreviewGait()
        {
            var p = controller.WalkPreview;
            Check(p != null, "预览页应有行走样张纸偶");
            if (p == null) return;
            var phase = p.GaitPhaseForCheck;
            SampleFrames(12, () => { }, () => Check(p.GaitPhaseForCheck > phase + .5f,
                "行走样张的相位应随传送带推进（样张没有真实位移，靠宿主声明空转速度）",
                phase.ToString("0.00") + " → " + p.GaitPhaseForCheck.ToString("0.00")));
        }

        static void VerifyMotionButtons()
        {
            var actor = Resources.FindObjectsOfTypeAll<CharacterActor>()
                .FirstOrDefault(value => value.gameObject.scene.IsValid() && value.gameObject.activeInHierarchy);
            Check(actor != null, "预览页应创建角色动作对象");
            if (actor == null) return;
            // 从 0 档开始：0 = 静立，既是进页面的默认值，也是玩家想让人物停下来时的去路。
            // 旧循环从 i = 1 起，恰好把 0 档漏在外面（它从来没被点过）。
            for (var i = 0; i < CharacterActor.MotionNames.Length; i++)
            {
                var button = FindActiveButton(CharacterActor.MotionNames[i]);
                Check(button != null, "动作按钮存在: " + CharacterActor.MotionNames[i]);
                if (button == null) continue;
                button.onClick.Invoke();
                Check(actor.CurrentMotion == (CharacterActor.Motion)i,
                    "动作按钮生效: " + CharacterActor.MotionNames[i]);
            }
            controller.SetMotion((int)CharacterActor.Motion.Idle);
            Check(actor.CurrentMotion == CharacterActor.Motion.Idle,
                "动作验收后恢复静立档（预览页默认姿势，不许把玩家留在行走/跑步上）",
                actor.CurrentMotion.ToString());
        }

        // ---- B-03：flipbook（`GaitMode.Frames`）--------------------------------------------
        // 判据三条：①存在性（枚举里有这一档）；②**默认仍是 Slices**（对玩家零变化）；
        // ③**站着不动 ⇒ 帧索引不动**（P2 的索引版）。
        // ⚠ 这里只跑"不依赖 frameSheet 的那一半"：没有逐帧素材时 `FramesActiveForCheck` 为 false，
        //    切帧渲染建不起来 ⇒ **如实记一条"未跑"**，不许把"没素材"当通过。
        //    B-11 素材（Art/Characters/farmer-male-0-walksheet-side-v1）已随 bb95fd3 入库，
        //    "喂真实位移 ⇒ 索引跟着走"那一条待 travelerUseWalksheet 有公开开关后补跑。
        // 【2026-10-11 B-10 修】本方法在**城镇页 phase** 被调（寻路取证之后，见调用点），
        // 原来却拿 `controller.WalkPreview`——那是**角色预览页**才创建的样张纸偶
        // （FrontEndCharacters.cs:488，`sampleView != null` 时才建），城镇页恒为 null ⇒ 断言
        // 从写好那天起就必然红（"B-03：WalkPreview 为空"，工作树全貌六关唯一红灯）。
        // 改用 `controller.TravelerPuppetForShot`（FrontEndWorld.cs:1117 的城镇 traveler 钩子）：
        // 同一个类的 partial，语义还更对——验的是**镇上真主角**这一台，不是预览页样张。
        // 【2026-10-07 修】参数类型原写 FrontEndCharacters——该类已并入 FrontEndController（partial 重构），
        // 全工程不存在这个类型 ⇒ CS0246 卡死编辑器进不了 Play。
        static void CheckFramesMode(FrontEndController controller)
        {
            var preview = controller.TravelerPuppetForShot;
            if (preview == null)
            {
                Check(false, "B-03：拿到城镇主角纸偶", "TravelerPuppetForShot 为空");
                return;
            }
            // 2026-10-28：先把进来时的档记下来。旧版这里先断"默认仍是 Slices"、末了又强切回 Slices，
            // 等于用夹具自己改过的状态交正式结论（产品默认已不是 Slices，这条必红且毫无信息）。
            var before = preview.GaitModeForCheck;
            preview.SetGaitMode(PuppetActor.GaitMode.Frames);
            Check(preview.GaitModeForCheck == PuppetActor.GaitMode.Frames,
                "B-03：切到 Frames 档应真的生效", "当前 " + preview.GaitModeForCheck);
            Check(preview.FramesActiveForCheck,
                "B-03：Frames 档必须真的建出帧渲染物（只有枚举值不算接上）",
                "FramesActive=" + preview.FramesActiveForCheck);
            var idx0 = preview.FrameIndexForCheck;
            var idx1 = preview.FrameIndexForCheck;   // 中间不喂任何位移
            Check(idx0 == idx1, "B-03：Frames 模式下不喂位移 ⇒ 帧索引不动（P2 索引版）", idx0 + " → " + idx1);
            if (preview.FramesActiveForCheck)
                Check(preview.FrameCountForCheck > 0, "B-03：Frames 渲染物的帧数必须 > 0",
                    "帧数 " + preview.FrameCountForCheck);
            // 关键：测完把档位**恢复成进来时的原档**，并断言恢复成功。
            // “跨帧变化/侧向镜像/真实位移”那三条在寻路探针里按档位验（见 FinishTravelerProbe），
            // 这里只负责“Frames 这一档本身能被切上且不喂位移就不动”，并且不把夹具留在测试档上。
            preview.SetGaitMode(before);
            Check(preview.GaitModeForCheck == before,
                "B-03：临时切档取证后必须恢复到原档（不许留在 Frames，也不许强切回 Slices 当正式结论）",
                before + " → " + preview.GaitModeForCheck);
            if (before == PuppetActor.GaitMode.Frames)
                Check(preview.FramesActiveForCheck, "B-03：原档是 Frames，恢复后帧渲染物必须还在");
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
            // 按显式钩子取货郎，不按猜的物体名（世界里它叫 "Placeable npc-huolang"，
            // ADR-018 的命名规则一改，按名字找就会永远找不到 → T092）。
            var peddler = controller.NpcButtonForShot("huolang");
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
            // 取不到人物或货郎必须是红灯：这里的 sibling 断言过去写成 `!= null` 才跑，
            // 于是定位失效时整条纵深验证静默消失，绿灯是假的（T092）。
            Check(traveler != null && npc != null, "纵深·下方应能取到世界页人物与货郎",
                traveler == null ? "取不到人物" : "取不到货郎");
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
                // T085：原来写"使用吃食应消耗一件"——D-09 + O6 已明令不许假装消耗
                // （回血效果在镇上没接入，扣了数量就是骗玩家）。三行按 3号 投稿原文逐字落地。
                Check(InventoryRules.Count(controller.CurrentBag, id) == before &&
                    controller.LastBagUseRefusal.Contains("尚未接入"), "吃食在镇上使用应拒绝且不消耗");
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
            // T085：行数不再写死（O2 给货郎加了 4 件来源后是 11 行），走同源访问器。
            Check(controller.ShelfOrder.Count == controller.RealShelfRowsForCheck(),
                "恢复正式货架行数应等于商品表算出来的行数", controller.ShelfOrder.Count + " 行");

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
                    // 重排后 fixture 已恢复正式表：复原 = 商品表算出来的那些行都回来（T085，不再写死 7）。
                    Check(!controller.TradeOnBlackMarket &&
                        controller.ShelfOrder.Count == controller.RealShelfRowsForCheck(),
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
