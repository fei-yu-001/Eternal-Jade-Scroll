using System;
using System.Collections.Generic;
using System.IO;
using TwelveJade.Core;
using TwelveJade.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
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
        static readonly string[] PageNames = { "menu", "slots", "new-game", "character-creation", "preview", "settings", "credits" };

        static FrontEndController controller;
        static Camera camera;
        static RenderTexture target;
        static string shotsDir;
        static readonly List<string> problems = new List<string>();
        static int shotIndex = -1;
        static float waitStart;
        static bool waiting, createdSlot;
        static Action pendingAfter;
        static string currentShot;
        static float runStart;
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

        public static void Capture()
        {
            SessionState.SetBool(SessionKey, true);
            var project = Directory.GetParent(Application.dataPath).FullName; // TwelveJade 工程
            var repoRoot = Directory.GetParent(Directory.GetParent(project).FullName).FullName; // 仓库根目录
            shotsDir = Path.Combine(repoRoot, "Tools", "screenshots");
            SessionState.SetString(SessionKey + ".Dir", shotsDir);
            Directory.CreateDirectory(shotsDir);
            Attach();
            EditorSceneManager.OpenScene("Assets/Scenes/FrontEnd.unity", OpenSceneMode.Single);
            runStart = Time.realtimeSinceStartup;
            EditorApplication.isPlaying = true;
        }

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
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
            if (Time.realtimeSinceStartup - runStart > 240f) { Fail("验收超时:超过 240 秒仍未完成,当前页面 " + controllerPage); return; }
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
            waiting = false;
            Shot();
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
                        createdSlot = true;
                        controller.ShowCharacterCreation(slot);
                        controller.SetCreationGender("female");
                        controller.SetCreationFaceStyle(1);
                        var save = controller.CreateFromDraft(slot);
                        Check(save != null, "落笔创建应成功");
                        Check(controller.Repository.Read(slot).CanLoad, "新建档位应可读取");
                        var created = slot;
                        Open(() => controller.ShowPreview(save), () => controller.Repository.Delete(created));
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
                    Open(controller.ShowSettings);
                    break;
                case 6:
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

        static void Shot()
        {
            if (pendingAfter != null) { pendingAfter(); pendingAfter = null; }
            Canvas.ForceUpdateCanvases();
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            camera.Render();
            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            var path = Path.Combine(shotsDir, string.Format("play-{0:00}-{1}.png", shotIndex + 1, currentShot));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
            Debug.Log("[FrontEndShot] captured " + path);
        }

        static void Check(bool condition, string message)
        {
            if (!condition) problems.Add("断言失败: " + message);
            else Debug.Log("[FrontEndShot] ok: " + message);
        }

        static void Finish()
        {
            SessionState.SetBool(SessionKey, false);
            Application.logMessageReceived -= OnLog;
            EditorApplication.update -= Pump;
            if (createdSlot)
                Check(controller.Repository.Latest() == null, "测试档位删除后不应有可继续档位");
            Check(controller.CurrentPage == "credits", "当前页面应为制作信息");
            Application.logMessageReceived -= OnLog;
            if (target != null && camera != null) { camera.targetTexture = null; target.Release(); }
            Debug.Log("[FrontEndShot] finished, " + problems.Count + " problem(s)");
            foreach (var problem in problems) Debug.LogError(problem);
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += () => EditorApplication.Exit(problems.Count == 0 ? 0 : 1);
        }

        static void Fail(string message)
        {
            SessionState.SetBool(SessionKey, false);
            Debug.LogError(message);
            EditorApplication.update -= Pump;
            EditorApplication.Exit(1);
        }
    }
}
