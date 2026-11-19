using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using TwelveJade.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;

namespace TwelveJade.Editor
{
    [InitializeOnLoad]
    public static class MemoryAuditShot
    {
        const string SessionKey = "MemoryAuditShot.Active";
        const float StateWaitSeconds = .75f;
        static readonly string[] StateNames = { "主菜单", "角色页", "城镇页", "对话态" };
        static readonly List<string> problems = new List<string>();
        static FrontEndController controller;
        static StringBuilder report;
        static string reportPath;
        static int stateIndex = -1;
        static float stateStarted;
        static bool stateRequested;
        static bool finished;
        static int sceneTextureCount;
        static int allTextureCount;

        static MemoryAuditShot()
        {
            if (!SessionState.GetBool(SessionKey, false)) return;
            reportPath = SessionState.GetString(SessionKey + ".Report", "");
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
            Begin(Path.Combine(RepoRoot(), "Tools", "logs", "perf-01", "perf-01-memory-audit.md"));
        }

        // PERF-02 前后对照:同一套夹具,报告落到 perf-02 目录,不覆盖 perf-01 基线。
        public static void RunPerf02()
        {
            Begin(Path.Combine(RepoRoot(), "Tools", "logs", "perf-02", "perf-02-memory-audit.md"));
        }

        static void Begin(string path)
        {
            SessionState.SetBool(SessionKey, true);
            reportPath = path;
            // 进 Play 会触发 domain reload，静态字段全部清零；构造函数从 SessionState 复原
            // reportPath——这里不落 String，Play 侧读回来的就是空串，Finish 的 WriteAllText("")
            // 会抛 ArgumentException（PERF-01 首跑实测：数字全采到了却 FAIL + Unity 挂死不退出）。
            SessionState.SetString(SessionKey + ".Report", reportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            report = NewReport();
            File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(false));
            EditorSceneManager.OpenScene("Assets/Scenes/FrontEnd.unity", OpenSceneMode.Single);
            Attach();
            EditorApplication.isPlaying = true;
        }

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (!EditorApplication.isPlaying) return;
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                problems.Add(type + ": " + condition);
        }

        static void Pump()
        {
            if (!EditorApplication.isPlaying || finished) return;
            try
            {
                if (controller == null)
                {
                    controller = UnityEngine.Object.FindAnyObjectByType<FrontEndController>();
                    return;
                }
                if (stateIndex < 0)
                {
                    RequestState(0);
                    return;
                }
                if (!stateRequested) return;
                if (Time.realtimeSinceStartup - stateStarted < StateWaitSeconds) return;
                CaptureState(StateNames[stateIndex]);
                if (stateIndex >= StateNames.Length - 1)
                {
                    Finish();
                    return;
                }
                RequestState(stateIndex + 1);
            }
            catch (Exception ex)
            {
                problems.Add("fixture exception: " + ex);
                Finish();
            }
        }

        static void RequestState(int index)
        {
            stateIndex = index;
            stateRequested = true;
            switch (index)
            {
                case 0:
                    controller.ShowMenu();
                    break;
                case 1:
                    controller.ShowCharacterCreation(1);
                    break;
                case 2:
                    var save = controller.CreateFromDraft(1);
                    if (save == null) throw new InvalidOperationException("角色页无法创建临时存档，不能进入城镇页。");
                    controller.ShowTown();
                    break;
                case 3:
                    controller.TalkTo("huolang");
                    break;
            }
            stateStarted = Time.realtimeSinceStartup;
            Debug.Log("[Perf01] state requested: " + StateNames[index]);
        }

        static void CaptureState(string state)
        {
            if (report == null)
                report = new StringBuilder(File.Exists(reportPath) ? File.ReadAllText(reportPath) : NewReport().ToString());
            stateRequested = false;
            var gcBytes = GC.GetTotalMemory(true);
#pragma warning disable 0618
            var sceneTextures = UnityEngine.Object.FindObjectsOfType<Texture2D>();
#pragma warning restore 0618
            var allTextures = Resources.FindObjectsOfTypeAll<Texture2D>()
                .GroupBy(x => x.GetInstanceID()).Select(g => g.First()).OrderBy(x => x.name).ToList();
            sceneTextureCount = sceneTextures.Length;
            allTextureCount = allTextures.Count;
            var textureBytes = allTextures.Sum(RuntimeBytes);
            var atlases = FindAtlases();
            var renderTextures = Resources.FindObjectsOfTypeAll<RenderTexture>()
                .GroupBy(x => x.GetInstanceID()).Select(g => g.First()).OrderBy(x => x.name).ToList();
            var renderBytes = renderTextures.Sum(RuntimeBytes);
            var chunkTextures = allTextures.Where(x => x.name.StartsWith("chunk_", StringComparison.Ordinal)).ToList();
            var overviewTextures = allTextures.Where(x => x.name.IndexOf("overview", StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            report.Append('|').Append(state).Append('|').Append(Bytes(gcBytes)).Append('|')
                .Append(allTextureCount).Append(" (scene ").Append(sceneTextureCount).Append(")|")
                .Append(Bytes(textureBytes)).Append('|').Append(atlases.Count).Append(" /").Append(Bytes(atlases.Sum(x => x.Bytes))).Append('|')
                .Append(renderTextures.Count).Append(" /").Append(Bytes(renderBytes)).AppendLine("|");
            report.AppendLine();
            report.Append("## ").AppendLine(state);
            report.AppendLine();
            report.Append("- `System.GC.GetTotalMemory(true)`: `").Append(gcBytes).Append("` bytes (`").Append(Bytes(gcBytes)).AppendLine("`).");
            report.Append("- `Object.FindObjectsOfType<Texture2D>()`: `").Append(sceneTextureCount).AppendLine("` 个场景 Texture2D。");
            report.Append("- `Resources.FindObjectsOfTypeAll<Texture2D>()` 去重对象：").Append(allTextureCount)
                .Append(" 张，逐张 `Profiler.GetRuntimeMemorySizeLong` 合计 `").Append(textureBytes).Append("` bytes (`")
                .Append(Bytes(textureBytes)).AppendLine("`)。");
            report.Append("- chunk 纹理：").Append(chunkTextures.Count).Append(" 张 / ").Append(Bytes(chunkTextures.Sum(RuntimeBytes)))
                .Append("；overview 候选：").Append(overviewTextures.Count).Append(" 张 / ").Append(Bytes(overviewTextures.Sum(RuntimeBytes))).AppendLine("。");
            report.Append("- TMP 图集：").AppendLine();
            foreach (var atlas in atlases)
                report.Append("  - ").Append(atlas.FontName).Append(" / ").Append(atlas.TextureName).Append(" / ")
                    .Append(atlas.Width).Append('x').Append(atlas.Height).Append(" / ").Append(atlas.Bytes).Append(" bytes / ")
                    .Append(Bytes(atlas.Bytes)).AppendLine();
            report.Append("- RenderTexture：").Append(renderTextures.Count).Append(" 个 / ").Append(renderBytes)
                .Append(" bytes (`").Append(Bytes(renderBytes)).AppendLine("`)。");
            report.AppendLine();
            report.AppendLine("### Texture2D 逐张开销");
            report.AppendLine();
            report.AppendLine("| name | dimensions | runtime bytes | asset path |");
            report.AppendLine("|---|---:|---:|---|");
            foreach (var texture in allTextures)
                report.Append('|').Append(Escape(texture.name)).Append('|').Append(texture.width).Append('x').Append(texture.height).Append('|')
                    .Append(RuntimeBytes(texture)).Append('|').Append(Escape(AssetDatabase.GetAssetPath(texture))).AppendLine("|");
            report.AppendLine();
            Debug.Log("[Perf01] captured " + state + " | GC=" + gcBytes + " | textures=" + allTextureCount + " / " + textureBytes + " bytes | TMP=" + atlases.Count + " / " + atlases.Sum(x => x.Bytes) + " bytes | RT=" + renderTextures.Count + " / " + renderBytes + " bytes");
        }

        static List<AtlasStat> FindAtlases()
        {
            var result = new List<AtlasStat>();
            var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>()
                .GroupBy(x => x.GetInstanceID()).Select(g => g.First()).OrderBy(x => x.name);
            foreach (var font in fonts)
            {
                var textures = font.atlasTextures ?? Array.Empty<Texture2D>();
                foreach (var texture in textures.Where(x => x != null).GroupBy(x => x.GetInstanceID()).Select(g => g.First()))
                    result.Add(new AtlasStat(font.name, texture.name, texture.width, texture.height, RuntimeBytes(texture)));
            }
            return result;
        }

        static StringBuilder NewReport()
        {
            var text = new StringBuilder();
            text.AppendLine("# PERF-01 Play 模式内存审计");
            text.AppendLine();
            text.AppendLine("- 采集方式：Unity Play 模式，`System.GC.GetTotalMemory(true)` + `Profiler.GetRuntimeMemorySizeLong`。");
            text.AppendLine("- Texture 统计：按任务口径记录 `Object.FindObjectsOfType<Texture2D>()` 场景对象数，并对 `Resources.FindObjectsOfTypeAll<Texture2D>()` 去重后的每张对象调用 `Profiler.GetRuntimeMemorySizeLong`。");
            text.AppendLine("- 口径：四态各采一次；不使用静态文件大小替代运行时数字；本夹具不设未经标定的性能阈值。");
            text.AppendLine();
            text.AppendLine("| 状态 | GC 总量 | Texture2D 对象数 | Texture2D runtime bytes | TMP atlas 数/bytes | RenderTexture 数/bytes |");
            text.AppendLine("|---|---:|---:|---:|---:|---:|");
            return text;
        }

        static long RuntimeBytes(UnityEngine.Object obj) => obj == null ? 0 : Profiler.GetRuntimeMemorySizeLong(obj);
        static string Bytes(long bytes) => (bytes / (1024d * 1024d)).ToString("F2") + " MiB";
        static string Escape(string value) => (value ?? "").Replace("|", "\\|").Replace("\n", " ");
        static string RepoRoot() => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));

        static void Finish()
        {
            finished = true;
            report.AppendLine("## 夹具判决");
            report.AppendLine();
            if (problems.Count == 0)
            {
                report.AppendLine("`PERF-01_JUDGEMENT: PASS` — 四态均已在 Play 模式采集；本轮只建立真实基线，不预设未经标定的优化阈值。");
                Debug.Log("[Perf01] PERF-01_JUDGEMENT: PASS | four states captured | no uncalibrated threshold asserted");
            }
            else
            {
                report.AppendLine("`PERF-01_JUDGEMENT: FAIL` — 夹具运行期出现 " + problems.Count + " 个错误。");
                foreach (var problem in problems) report.AppendLine("- " + problem);
                Debug.LogError("[Perf01] PERF-01_JUDGEMENT: FAIL | problems=" + problems.Count);
            }
            File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(false));
            SessionState.SetBool(SessionKey, false);
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += () => EditorApplication.Exit(problems.Count == 0 ? 0 : 1);
        }

        sealed class AtlasStat
        {
            public readonly string FontName, TextureName;
            public readonly int Width, Height;
            public readonly long Bytes;
            public AtlasStat(string fontName, string textureName, int width, int height, long bytes)
            {
                FontName = fontName; TextureName = textureName; Width = width; Height = height; Bytes = bytes;
            }
        }
    }
}
