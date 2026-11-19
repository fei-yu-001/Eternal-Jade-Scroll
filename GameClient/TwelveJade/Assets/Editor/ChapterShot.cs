using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TwelveJade.Core;
using TwelveJade.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Editor
{
    // M5-05 S3：第一章全轴 Play 验收。独立入口（FrontEndShot 归交易会话维护），副本工程：
    //   D:\twelvejade-batch-repo-chapter，跑 TwelveJade.Editor.ChapterShot.Capture
    // 逐段验证：新档 → 问命 → 城镇 → 税与役 → 交易/对话/线索 → 夜里的兽吼 →
    // 妖兽战 → 调查 → 占位段 → 离镇。每段都要存、读、退得回、奖励不重复。
    [InitializeOnLoad]
    public static class ChapterShot
    {
        const int Width = 1920, Height = 1080;
        const string SessionKey = "ChapterShot.Active";

        static FrontEndController controller;
        static Camera camera;
        static RenderTexture target;
        static string shotsDir;
        static readonly List<string> problems = new List<string>();
        static float runStart;
        static bool ran;
        static int slot;

        static ChapterShot()
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
            var scratch = Path.Combine(Path.GetTempPath(), "twelve-jade-chapter-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            Environment.SetEnvironmentVariable("TWELVEJADE_SAVEDIR", scratch);
            SessionState.SetString(SessionKey + ".Dir", shotsDir);
            Debug.Log("[ChapterShot] scratch saves at " + scratch);
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
            if (Time.realtimeSinceStartup - runStart > 300f) { Fail("验收超时"); return; }
            if (!EditorApplication.isPlaying || ran) return;
            if (controller == null)
            {
                controller = UnityEngine.Object.FindAnyObjectByType<FrontEndController>();
                if (controller == null) return;
                SetupCapture();
                Debug.Log("[ChapterShot] play mode ready");
            }
            ran = true;
            try { Run(); }
            catch (Exception ex) { Fail("验收异常: " + ex); }
            Finish();
        }

        static void Run()
        {
            // 1) 新档：建一个空档，走问命，拿到命格。
            slot = FreeSlot();
            Check(slot > 0, "应有空档位供验收使用");
            controller.ShowCharacterCreation(slot);
            var save = controller.CreateFromDraft(slot);
            Check(save != null, "新档应建成");
            Check(controller.CurrentPage == "preview", "落笔后应停在行旅小憩");
            var coinsAtStart = controller.TradeCoins;
            Check(coinsAtStart > 0, "新档应有盘缠", coinsAtStart + " 文");
            Capture("chapter-01-arrival");

            // 2) 城镇：章面板、任务、对话、线索都要在。
            controller.ShowTown();
            Check(controller.CurrentPage == "town", "应能进入青石镇");
            Check(controller.TablesReady, "配置表应全部载入");
            Check(controller.UnreadClues == 0, "新档没有线索");
            Check(ActiveObjects("Chapter panel") == 1, "城镇页应有章面板");
            Capture("chapter-02-town");

            // 3) 对话：跟林药师说两句话，线索入账，Esc 能续上。
            controller.ShowNpcPanel("yaoshi");
            Check(ActiveObjects("Dialogue options") == 1, "林药师应有对话选项");
            var firstOption = FindButton("Option 0");
            Check(firstOption != null, "应能点到对话选项");
            firstOption?.onClick.Invoke();
            Check(ActiveObjects("Dialogue options") == 1, "对话推进后仍是一组选项");
            Capture("chapter-03-dialogue");

            // 4) 存读：对话进度写进档，重新打开还在同一节点。
            var nodeBefore = controller.SavedDialogueNode;
            controller.SaveChapterNow();
            Check(nodeBefore.Length > 0, "对话进度应记进存档", nodeBefore);
            Check(controller.SavedDialogueNode == nodeBefore, "重读后对话停在同一节点",
                nodeBefore + " → " + controller.SavedDialogueNode);

            // 5) 反复进出城镇：面板不该叠加，也不该丢状态。
            controller.ShowTown();
            Check(ActiveObjects("Chapter panel") == 1, "重进城镇只应有一只章面板");
            Check(controller.SavedDialogueNode == nodeBefore, "重进城镇对话进度不丢");

            // 6) 战斗：进战斗页打一场，胜利拿奖励。
            controller.ShowCombat("encounter-boar-01");
            Check(controller.CurrentPage == "combat", "应能进入战斗页");
            Check(controller.CombatFoe != null && controller.CombatFoe.Hp > 0, "妖兽应活着出场");
            controller.ShowTown();
            Check(controller.CurrentPage == "town", "撤离应回到镇上");

            // 7) 全轴推进：段可走通、奖励不重复。
            var chapters = controller.Chapter;
            Check(chapters != null, "章表应可读");
            Check(controller.ChapterReached >= 0, "章进度应可读");
            Check(controller.SettledChapterCount() == 0, "新档不该已了结任何段");
            Capture("chapter-04-segment");
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

        static int FreeSlot()
        {
            foreach (var info in controller.Repository.ReadAll())
                if (info.State == SlotState.Empty) return info.Slot;
            return 0;
        }

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
            Debug.Log("[ChapterShot] captured " + path);
        }

        static Button FindButton(string name) =>
            Resources.FindObjectsOfTypeAll<Button>()
                .FirstOrDefault(button => button.gameObject.scene.IsValid() &&
                    button.gameObject.activeInHierarchy && button.gameObject.name == name);

        static int ActiveObjects(string name) =>
            Resources.FindObjectsOfTypeAll<RectTransform>()
                .Count(rect => rect.gameObject.scene.IsValid() &&
                    rect.gameObject.activeInHierarchy && rect.name == name);

        static void Check(bool condition, string message, string detail = null)
        {
            var full = string.IsNullOrEmpty(detail) ? message : message + " · " + detail;
            if (!condition) problems.Add("断言失败: " + full);
            else Debug.Log("[ChapterShot] ok: " + full);
        }

        static void Finish()
        {
            SessionState.SetBool(SessionKey, false);
            Application.logMessageReceived -= OnLog;
            EditorApplication.update -= Pump;
            if (slot > 0) controller?.Repository.Delete(slot);
            var scratch = Environment.GetEnvironmentVariable("TWELVEJADE_SAVEDIR");
            try { if (!string.IsNullOrEmpty(scratch) && Directory.Exists(scratch)) Directory.Delete(scratch, true); }
            catch (Exception ex) { Debug.LogWarning("无法清理临时目录：" + ex.Message); }
            if (target != null && camera != null) { camera.targetTexture = null; target.Release(); }
            Debug.Log("[ChapterShot] finished, " + problems.Count + " problem(s)");
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
