using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using TwelveJade.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Editor
{
    // 运行时走帧的导入/渲染取证：真正用 PuppetActor.Frames 画全部 8 帧，深浅两底。
    // C1-C6 的数值判据只在 Python 门禁中；本夹具验证资源导入、帧接线与非空显示，
    // 不把截图生成成功当成六项质量检查通过。位移驱动相位，关闭淡化以逐帧看清边缘。
    public static class WalksheetCutoutShot
    {
        const int CellWidth = 200, RowHeight = 320, Width = CellWidth * 8, Height = RowHeight * 2;
        const float ActorHeight = 260f;
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        public static void Run()
        {
            var project = Directory.GetParent(Application.dataPath).FullName;
            var repo = Directory.GetParent(Directory.GetParent(project).FullName).FullName;
            var output = Path.Combine(repo, "Tools", "logs", "walksheet-cleanup-20261104", "unity");
            Directory.CreateDirectory(output);
            var report = new StringBuilder();
            var problems = 0;
            RenderTexture target = null;
            try
            {
                var loader = File.ReadAllText(Path.Combine(Application.dataPath, "Scripts", "Presentation", "FrontEndWorld.cs"));
                var refs = Regex.Matches(loader, @"""(Art/Characters/Walk/[^""\r\n]+)""")
                    .Cast<Match>().Select(m => m.Groups[1].Value).Distinct().OrderBy(p => p).ToArray();
                if (refs.Length == 0) throw new InvalidOperationException("运行时走帧引用清单为空");
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var camera = new GameObject("Walksheet capture camera", typeof(Camera)).GetComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = Height * .5f;
                camera.transform.position = new Vector3(Width * .5f, Height * .5f, -10f);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.10f, .14f, .16f, 1f);
                camera.allowHDR = false;
                camera.allowMSAA = false;
                target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
                camera.targetTexture = target;
                var canvasObject = new GameObject("Walksheet capture canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
                var canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 10f;
                var canvasRect = canvasObject.GetComponent<RectTransform>();
                var light = new GameObject("Light background", typeof(RectTransform), typeof(Image));
                var lightRect = light.GetComponent<RectTransform>();
                lightRect.SetParent(canvasRect, false);
                lightRect.anchorMin = lightRect.anchorMax = new Vector2(0f, 1f);
                lightRect.pivot = new Vector2(0f, 1f);
                lightRect.anchoredPosition = new Vector2(0f, -RowHeight);
                lightRect.sizeDelta = new Vector2(Width, RowHeight);
                light.GetComponent<Image>().color = new Color(.94f, .92f, .88f, 1f);
                var update = typeof(PuppetActor).GetMethod("Update", Private);
                var cycle = typeof(PuppetActor).GetField("frameCycleDistance", Private);
                var fade = typeof(PuppetActor).GetField("frameCrossFadeSeconds", Private);
                if (update == null || cycle == null || fade == null) throw new InvalidOperationException("走帧字段改名，夹具需要更新");
                report.AppendLine("Runtime import / PuppetActor.Frames capture; 8 phases on dark + light backgrounds.");
                foreach (var resource in refs)
                {
                    var assetPath = "Assets/Resources/" + resource + ".png";
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                    var texture = Resources.Load<Texture2D>(resource);
                    var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                    if (texture == null || importer == null || !importer.DoesSourceTextureHaveAlpha() || !importer.alphaIsTransparency)
                        throw new InvalidOperationException("走帧缺失或透明导入未启用：" + resource);
                    var actors = new GameObject("Captured actors", typeof(RectTransform));
                    var actorsRect = actors.GetComponent<RectTransform>();
                    actorsRect.SetParent(canvasRect, false);
                    actorsRect.anchorMin = Vector2.zero;
                    actorsRect.anchorMax = Vector2.one;
                    actorsRect.offsetMin = actorsRect.offsetMax = Vector2.zero;
                    Canvas.ForceUpdateCanvases();
                    for (var row = 0; row < 2; row++)
                    for (var frame = 0; frame < 8; frame++)
                    {
                        var actor = PuppetActor.Create(actorsRect, texture,
                            new Vector2(CellWidth * (frame + .5f), -(row * RowHeight + 300f)),
                            new Vector2(ActorHeight * .75f, ActorHeight));
                        actor.SetFrameSheet(texture);
                        actor.SetGaitMode(PuppetActor.GaitMode.Frames);
                        actor.SetMotion(PuppetActor.Motion.Walk);
                        fade.SetValue(actor, 0f);
                        actor.AdvanceGait((frame + .1f) * (float)cycle.GetValue(actor) / actor.FrameCountForCheck);
                        update.Invoke(actor, null);
                        if (!actor.FramesActiveForCheck || actor.FrameIndexForCheck != frame)
                            throw new InvalidOperationException("帧接线错误：" + resource + " frame=" + frame);
                    }
                    Canvas.ForceUpdateCanvases();
                    var previous = RenderTexture.active;
                    RenderTexture.active = target;
                    camera.Render();
                    var image = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
                    image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                    image.Apply();
                    RenderTexture.active = previous;
                    var pixels = image.GetPixels32();
                    for (var row = 0; row < 2; row++)
                    for (var frame = 0; frame < 8; frame++)
                    {
                        var bg = pixels[(Height - row * RowHeight - 4) * Width + frame * CellWidth + 4];
                        var count = 0;
                        for (var y = row * RowHeight + 20; y < (row + 1) * RowHeight - 10; y++)
                        for (var x = frame * CellWidth + 5; x < (frame + 1) * CellWidth - 5; x++)
                        {
                            var pixel = pixels[(Height - y - 1) * Width + x];
                            if (Math.Abs(pixel.r - bg.r) + Math.Abs(pixel.g - bg.g) + Math.Abs(pixel.b - bg.b) > 24) count++;
                        }
                        if (count < 500) throw new InvalidOperationException("空帧：" + resource + " frame=" + frame);
                    }
                    File.WriteAllBytes(Path.Combine(output, Path.GetFileName(resource) + ".png"), image.EncodeToPNG());
                    using (var sha = SHA256.Create())
                    {
                        var hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Path.Combine(project, assetPath)))).Replace("-", "").ToLowerInvariant();
                        report.AppendLine(resource + " sourceSha256=" + hash + " imported=" + texture.width + "x" + texture.height + " frames=8 backgrounds=2");
                    }
                    UnityEngine.Object.DestroyImmediate(image);
                    UnityEngine.Object.DestroyImmediate(actors);
                }
                report.AppendLine("sheets=" + refs.Length + " renderedFrames=" + refs.Length * 16);
            }
            catch (Exception error)
            {
                problems++;
                report.AppendLine(error.ToString());
                Debug.LogException(error);
            }
            finally
            {
                if (target != null) target.Release();
                File.WriteAllText(Path.Combine(output, "report.txt"), report.ToString(), new UTF8Encoding(false));
                Debug.Log("[WalksheetCutoutShot] finished, " + problems + " problem(s)");
                if (Application.isBatchMode) EditorApplication.Exit(problems == 0 ? 0 : 1);
            }
        }
    }
}
