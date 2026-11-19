using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using TwelveJade.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.LowLevel;
using UnityEngine.UI;

namespace TwelveJade.Editor
{
    // A-06 170px 可读性基线（交接包 2026-10-06-Z · Z-2，员工 A 第一批第 1 项，Z-6 第 1 步）。
    // 回答一个问题：170 / 230 / 340 px 三档身高的主角，在同一机位、同一地形下还认不认得出
    // "这是个人、在待机"。交付三张同机位 PNG 到 Tools/logs/a-06/（.gitignore 挡 Tools/screenshots，
    // 证据存那里等于没交——O3 的教训）。
    //
    // 实现口径：**不改** FrontEndWorld.TravelerHeight（const 170f，角色构建段归 B 线，AA-4 分区表），
    // 用公开钩子 TravelerPuppetForShot.SetScale(height/170f) 做几何等价缩放：
    // 着地阴影是纸偶 Root 的孩子（SyncTravelerShadow），随 SetScale 同步缩放，
    // 与"把 const 改成 230/340"对该节点全部线性尺寸（立绘、阴影宽 = .58×Height）逐像素等价。
    // 批处理骨架照抄 FrontEndShot（Play 模式 + SessionState 跨域重载 + 手动输入泵 + 判决行口径），
    // 页面流精简为：建档 → 进镇 → 镇心置位 → 三档各拍一张。判据只认本文件的 finished 行，退出码不作数（W-14）。
    [InitializeOnLoad]
    public static class A06ReadabilityShot
    {
        const int Width = 1920, Height = 1080;
        const float BaseHeight = 170f;
        static readonly float[] Targets = { 170f, 230f, 340f };
        // 镇心机位 = town-map landmarks 的 market（3100,4950，T036 记录与地标逐字吻合），三档共用。
        const float MarketX = 3100f, MarketY = 4950f;
        const string SessionKey = "A06ReadabilityShot.Active";

        static FrontEndController controller;
        static Camera camera;
        static RenderTexture target;
        static string outDir;
        static readonly List<string> problems = new List<string>();
        static readonly List<string> shotMd5 = new List<string>();
        static int stage;          // 0 等 play+控制器 → 1 等画布+进镇 → 2 等渐入+置位 → 3 等帧 → 4 拍档
        static int tierIndex;
        static int framesLeft;
        static float stageWait;
        static float runStart;
        static bool playerLoopInputInjected;

        static A06ReadabilityShot()
        {
            if (!SessionState.GetBool(SessionKey, false)) return;
            // domain reload 会清空全部静态字段：outDir 必须从 SessionState 恢复
            //（FrontEndShot 同款；首轮实跑就是漏了它，Capture 在 Path.Combine 处抛 null）。
            outDir = SessionState.GetString(SessionKey + ".Dir", "");
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

        public static void Run()
        {
            SessionState.SetBool(SessionKey, true);
            // 验收全程在临时存档目录里跑，玩家槽位一概不碰（与 FrontEndShot 同一口径）。
            var dir = Path.Combine(Path.GetTempPath(), "twelve-jade-a06-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            Environment.SetEnvironmentVariable("TWELVEJADE_SAVEDIR", dir);
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            var project = Directory.GetParent(Application.dataPath).FullName;             // TwelveJade 工程
            var repoRoot = Directory.GetParent(Directory.GetParent(project).FullName).FullName; // 仓库根
            outDir = Path.Combine(repoRoot, "Tools", "logs", "a-06");
            Directory.CreateDirectory(outDir);
            SessionState.SetString(SessionKey + ".Dir", outDir);
            runStart = Time.realtimeSinceStartup;
            Attach();
            EditorSceneManager.OpenScene("Assets/Scenes/FrontEnd.unity", OpenSceneMode.Single);
            EditorApplication.isPlaying = true;
        }

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (!EditorApplication.isPlaying) return;
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            if (condition != null && condition.StartsWith("[Licensing::Module]")) return;
            if (stackTrace != null && stackTrace.Contains("UnityEditor.Search")) return;
            if (condition != null && condition.Contains("[A06ReadabilityShot]")) return; // 自己打的判据行不算引擎错误
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
            catch (Exception ex) { problems.Add("驱动异常: " + ex); Finish(); }
        }

        // 与 FrontEndShot 同款：手动输入模式下挂一个空消费位，保持与既有通道同一形态。
        // A-06 不做任何按键断言，此委托恒为空转，只为骨架一致、少踩未知坑。
        static void EnsurePlayerLoopInputUpdate()
        {
            if (playerLoopInputInjected) return;
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            for (var i = 0; i < loop.subSystemList.Length; i++)
            {
                var sub = loop.subSystemList[i];
                if (sub.subSystemList == null) continue;
                var systems = new List<PlayerLoopSystem>(sub.subSystemList);
                var behaviour = systems.FindIndex(x => x.type.Name == "ScriptRunBehaviourUpdate");
                if (behaviour < 0) continue;
                systems.Insert(behaviour, new PlayerLoopSystem
                {
                    type = typeof(A06ReadabilityShot),
                    updateDelegate = () => { }
                });
                sub.subSystemList = systems.ToArray();
                loop.subSystemList[i] = sub;
                PlayerLoop.SetPlayerLoop(loop);
                playerLoopInputInjected = true;
                Debug.Log("[A06ReadabilityShot] player loop injection ready");
                return;
            }
        }

        static void Step()
        {
            if (Time.realtimeSinceStartup - runStart > 300f) { problems.Add("验收超时: 超过 300 秒，停在 stage " + stage); Finish(); return; }
            if (!EditorApplication.isPlaying) return;
            switch (stage)
            {
                case 0:
                    controller = UnityEngine.Object.FindAnyObjectByType<FrontEndController>();
                    if (controller == null) return;
                    if (UnityEngine.Object.FindAnyObjectByType<Canvas>() == null) return; // 画布未建时 SetupCapture 会空引用
                    SetupCapture();
                    controller.CreateFromDraft(1);
                    Debug.Log("[A06ReadabilityShot] play mode ready, draft created");
                    stage = 1;
                    return;
                case 1:
                    controller.ShowTown();
                    stageWait = Time.realtimeSinceStartup;
                    stage = 2;
                    return;
                case 2:
                    // 纯等 .45s+：让渐入 / 镜头跟随 / 深度排序刷新（FrontEndShot.TerrainStep 同款等待）。
                    if (Time.realtimeSinceStartup - stageWait < .45f) return;
                    if (!controller.SnapTravelerTo(MarketX, MarketY))
                    {
                        problems.Add("镇心置位应成功（market " + MarketX + "," + MarketY + "）");
                        Finish();
                        return;
                    }
                    framesLeft = 5;   // 置位与镜头跟随都要下一帧才进渲染目标
                    stage = 3;
                    return;
                case 3:
                    if (--framesLeft > 0) return;
                    ApplyTier();
                    stage = 4;
                    return;
                case 4:
                    if (--framesLeft > 0) return;   // 换档后新尺寸下一帧才进 RT（Q1.7 实测教训）
                    CaptureTier();
                    return;
            }
        }

        static void ApplyTier()
        {
            var targetHeight = Targets[tierIndex];
            var scale = targetHeight / BaseHeight;
            controller.TravelerPuppetForShot.SetScale(scale);
            framesLeft = 4;
            Debug.Log("[A06ReadabilityShot] tier " + tierIndex + " → 目标 " + targetHeight.ToString("0") +
                      "px（SetScale " + scale.ToString("0.0000") + "，基准 const " + BaseHeight.ToString("0") + "）");
        }

        static void CaptureTier()
        {
            var label = "a06-height-" + ((int)Targets[tierIndex]).ToString("000");
            Capture(label);
            var expect = Targets[tierIndex] / BaseHeight;
            var got = controller.TravelerPuppetForShot.Root.localScale.x;
            Check(Mathf.Abs(got - expect) < 0.01f,
                "档 " + tierIndex + " 纸偶 localScale 应为 " + expect.ToString("0.0000"),
                "实测 " + got.ToString("0.0000"));
            tierIndex++;
            if (tierIndex >= Targets.Length) { Finish(); return; }
            ApplyTier();
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

        static void Capture(string label)
        {
            Canvas.ForceUpdateCanvases();
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            camera.Render();
            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            var path = Path.Combine(outDir, label + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                var hash = BitConverter.ToString(md5.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
                Debug.Log("[A06ReadabilityShot] captured " + path + " md5 " + hash + " bytes " + new FileInfo(path).Length);
                Check(!shotMd5.Contains(hash),
                    label + " 的 md5 必须与已拍档位互不相同（同机位不同身高必然不同图；T055 平色假图一族）", hash);
                shotMd5.Add(hash + " " + label);
            }
        }

        static void Check(bool ok, string what, string detail = null)
        {
            var line = "[A06ReadabilityShot] " + (ok ? "ok: " : "problem: ") + what + (detail != null ? " ｜ " + detail : "");
            Debug.Log(line);
            if (!ok) problems.Add(what + (detail != null ? " ｜ " + detail : ""));
        }

        static void Finish()
        {
            // 判决行必须是本日志最后一条（判据只认它；退出码被看守进程污染不作数，W-14）。
            foreach (var p in problems) Debug.Log("[A06ReadabilityShot] FAIL 明细: " + p);
            Debug.Log("[A06ReadabilityShot] finished, " + problems.Count + " problem(s)");
            EditorApplication.update -= Step;
            EditorApplication.Exit(problems.Count > 0 ? 1 : 0);
        }
    }
}
