using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using TwelveJade.Core;
using TwelveJade.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TwelveJade.Editor
{
    // Edit-mode integration check using the real actor, pose, mesh and Resources assets.
    // Source alpha is decoded into temporary readable copies; import settings stay untouched.
    public static class ActionPoseSizeChecks
    {
        const float Height = 170f;
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly Dictionary<Texture2D, Texture2D> sources = new Dictionary<Texture2D, Texture2D>();
        static readonly Dictionary<string, Vector2> alphaSpans = new Dictionary<string, Vector2>();
        static int checks;
        static string context;
        static readonly Vector2 Foot = new Vector2(120, -280);
        static readonly CombatPhase[] Phases = { CombatPhase.Startup, CombatPhase.Active, CombatPhase.Recovery };

        [MenuItem("Tools/Twelve Jade/Check action pose sizes")]
        public static void Run()
        {
            string project = Directory.GetParent(Application.dataPath).FullName;
            string output = Environment.GetEnvironmentVariable("EJS_ACTION_POSE_OUTPUT");
            if (string.IsNullOrEmpty(output)) output = Path.Combine(project, "Temp", "ActionPoseSizeChecks");
            Directory.CreateDirectory(output);
            var report = new StringBuilder("Action pose edit-mode checks; source alpha threshold=32.\n");
            report.AppendLine("Contact sheet columns: standing | light startup | light active | light recovery | active dodge.");
            checks = 0; int cases = 0, failures = 0;
            var previousScene = SceneManager.GetActiveScene();
            var previousTarget = RenderTexture.active;
            int previousPreviewCount = EditorSceneManager.previewSceneCount;
            var userScenes = new List<(Scene scene, bool loaded, bool dirty, GameObject[] roots)>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var userScene = SceneManager.GetSceneAt(i);
                userScenes.Add((userScene, userScene.isLoaded, userScene.isDirty,
                    userScene.isLoaded ? userScene.GetRootGameObjects() : Array.Empty<GameObject>()));
            }
            Scene scene = default;
            GameObject cameraObject = null, canvasObject = null;
            RenderTexture target = null;
            void CleanUp(Action action)
            {
                try { action(); }
                catch (Exception error)
                { failures++; report.AppendLine("FAIL cleanup\n" + error); Debug.LogException(error); }
            }
            try
            {
                context = "preview scene initialization";
                // An untitled unsaved user scene forbids NewScene(Additive). Preview scenes
                // require no save/discard prompt and never replace the active user scene.
                scene = EditorSceneManager.NewPreviewScene();
                Check(scene.IsValid() && EditorSceneManager.IsPreviewScene(scene), "isolated preview scene exists");
                cameraObject = EditorUtility.CreateGameObjectWithHideFlags("Action pose check camera", HideFlags.HideAndDontSave, typeof(Camera));
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                var camera = cameraObject.GetComponent<Camera>();
                // Use the ordinary camera path (as in NpcMotionShot), but keep its scene
                // restricted to this preview. No GameView or preview-camera UI routing.
                camera.cameraType = CameraType.Game; camera.enabled = false; camera.scene = scene;
                camera.renderingPath = RenderingPath.Forward;
                camera.orthographic = true; camera.orthographicSize = 160; camera.aspect = 1200f / 320f;
                camera.nearClipPlane = .1f; camera.farClipPlane = 100f; camera.useOcclusionCulling = false;
                camera.transform.position = new Vector3(600, 160, -10);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.12f, .16f, .18f, 1);
                camera.allowHDR = false; camera.allowMSAA = false;
                target = new RenderTexture(1200, 320, 24, RenderTextureFormat.ARGB32);
                camera.targetTexture = target;
                canvasObject = EditorUtility.CreateGameObjectWithHideFlags("Action pose check canvas", HideFlags.HideAndDontSave, typeof(RectTransform), typeof(Canvas));
                SceneManager.MoveGameObjectToScene(canvasObject, scene);
                var canvas = canvasObject.GetComponent<Canvas>();
                // Preview scenes do not have a GameView to size/submit a screen-space
                // canvas. Use the same UI geometry in an explicit 1 unit = 1 pixel plane.
                canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera;
                var parent = (RectTransform)canvas.transform;
                parent.pivot = new Vector2(0, 1); parent.sizeDelta = new Vector2(1200, 320);
                parent.position = new Vector3(0, 320, 0); parent.localScale = Vector3.one;
                Canvas.ForceUpdateCanvases();
                var line = new GameObject("Registered ground", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                var lineRect = (RectTransform)line.transform; lineRect.SetParent(parent, false);
                lineRect.anchorMin = lineRect.anchorMax = new Vector2(0, 1); lineRect.pivot = new Vector2(0, .5f);
                lineRect.anchoredPosition = new Vector2(0, Foot.y); lineRect.sizeDelta = new Vector2(1200, 1);
                line.GetComponent<Image>().color = new Color(.4f, .5f, .5f, 1);
                foreach (string gender in new[] { "male", "female" })
                foreach (string direction in new[] { "front", "right", "back", "left" })
                foreach (float tier in new[] { .8f, 1f, 1.25f })
                foreach (bool low in new[] { false, true })
                {
                    context = gender + "/" + direction + "/" + tier.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "/low=" + low;
                    string view = direction == "left" || direction == "right" ? "side" : direction;
                    bool mirror = direction == "left";
                    int facing = mirror ? -1 : 1;
                    var actor = MakeActor(parent, gender, view, mirror, tier, low, Foot, out var pose);
                    CheckScenario(actor, pose, gender, facing, tier, low, report);
                    Object.DestroyImmediate(actor.gameObject);
                    CaptureBoard(parent, camera, target, output, gender, view, direction, mirror, tier, low);
                    cases++;
                }
            }
            catch (Exception error)
            {
                failures++; report.AppendLine("FAIL " + context + "\n" + error); Debug.LogException(error);
            }
            finally
            {
                // Dispose only objects owned by this invocation, including partial setup.
                // Independent cleanup steps ensure one failure cannot skip closing the preview.
                foreach (var copy in sources.Values) CleanUp(() => Object.DestroyImmediate(copy));
                sources.Clear(); alphaSpans.Clear();
                CleanUp(() => { if (cameraObject != null) cameraObject.GetComponent<Camera>().targetTexture = null; });
                CleanUp(() => RenderTexture.active = previousTarget);
                CleanUp(() => { if (target != null) { target.Release(); Object.DestroyImmediate(target); } });
                CleanUp(() => { if (canvasObject != null) Object.DestroyImmediate(canvasObject); });
                CleanUp(() => { if (cameraObject != null) Object.DestroyImmediate(cameraObject); });
                CleanUp(() => { if (scene.IsValid() && EditorSceneManager.IsPreviewScene(scene)) EditorSceneManager.ClosePreviewScene(scene); });
                CleanUp(() =>
                {
                    context = "user scene preservation";
                    Check(SceneManager.GetActiveScene() == previousScene, "active user scene unchanged");
                    Check(SceneManager.sceneCount == userScenes.Count, "open user scenes unchanged");
                    Check(EditorSceneManager.previewSceneCount == previousPreviewCount, "preview scene released");
                    foreach (var saved in userScenes)
                    {
                        Check(saved.scene.IsValid() && saved.scene.isLoaded == saved.loaded, "user scene still open with original load state");
                        Check(saved.scene.isDirty == saved.dirty, "user scene unsaved state preserved");
                        if (!saved.loaded) continue;
                        Check(saved.scene.rootCount == saved.roots.Length, "no test roots left in user scene");
                        foreach (var root in saved.roots)
                            Check(root != null && root.scene == saved.scene && root.transform.parent == null, "user root preserved");
                    }
                    report.AppendLine("User scene preservation PASS; no scene saved, discarded, reopened or replaced.");
                });
                report.AppendLine((failures == 0 ? "PASS" : "FAIL") + " cases=" + cases + " assertions=" + checks + " renderedBoards=" + cases + " failures=" + failures);
                File.WriteAllText(Path.Combine(output, "report.txt"), report.ToString(), new UTF8Encoding(false));
                Debug.Log("[ActionPoseSizeChecks] " + cases + " cases, " + checks + " checks, " + failures + " failures; " + output);
                if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
        }

        static PuppetActor MakeActor(RectTransform parent, string gender, string view, bool mirror, float tier, bool low, Vector2 at, out ActionCombatPose pose)
        {
            var stand = TownCharacterArt.Hero(gender, view, false);
            var walk = TownCharacterArt.Hero(gender, view, true);
            var entry = TownCharacterArt.Find("hero-" + gender + "-" + view);
            var strikes = TownCharacterArt.HeroStrikes(gender);
            Check(stand != null && walk != null && entry != null && strikes != null, "approved resources exist");
            var actor = PuppetActor.Create(parent, stand, at, new Vector2(Height * stand.width / stand.height, Height));
            actor.SetFrameSheet(walk, entry.frames, entry.cycleDistance); actor.SetGaitMode(PuppetActor.GaitMode.Frames);
            actor.SetMirror(mirror); actor.SetScale(tier); actor.ReducedMotionForRest = low;
            Rest(actor);
            pose = actor.gameObject.AddComponent<ActionCombatPose>(); pose.Configure(actor, strikes, Height);
            pose.Sample(at, mirror ? -1 : 1, CombatPhase.Idle, CombatActionType.Wait, 0, low);
            return actor;
        }

        static void CheckScenario(PuppetActor actor, ActionCombatPose pose, string gender, int facing, float tier, bool low, StringBuilder report)
        {
            var frame = Field<RawImage>(actor, "frameImage");
            var strike = Field<RawImage>(pose, "strike");
            var baseline = VisibleBounds(frame, (RectTransform)actor.Root.parent);
            var registeredFoot = actor.Root.parent.InverseTransformPoint(actor.Root.TransformPoint(Vector3.zero));
            foreach (var action in new[] { CombatActionType.Light, CombatActionType.Heavy })
            for (int i = 0; i < Phases.Length; i++)
            {
                // Simulate the actor update that runs before the action LateUpdate, including a turn squash.
                actor.Root.localScale = Vector3.Scale(actor.BaseScale, new Vector3(1.04f, .96f, 1));
                pose.Sample(Foot, facing, Phases[i], action, .5f, low);
                Invoke(pose, "LateUpdate");
                CheckScale(actor, tier);
                Check(pose.StrikeVisible && pose.TravelRenderersHidden && !pose.DodgeVisible, "attack replaces travel");
                Check(pose.StrikeFrame == i && pose.StrikeMirrored == (facing < 0), "phase and UV mirror");
                Check(!frame.gameObject.activeSelf, "travel image hidden");
                var attack = VisibleBounds(strike, (RectTransform)actor.Root.parent);
                Near(attack.y - attack.x, baseline.y - baseline.x, "visible body height");
                Near(attack.x, baseline.x, "visible shoe/ground registration");
                var foot = pose.StrikeFootInParent();
                Near(foot.x, registeredFoot.x, "registered foot x"); Near(foot.y, registeredFoot.y, "registered foot y");
                pose.Sample(Foot, facing, CombatPhase.Idle, CombatActionType.Wait, 0, low);
                CheckRest(actor, pose, tier, baseline);
            }
            foreach (var phase in Phases)
            foreach (float progress in new[] { 0f, .5f, 1f })
            {
                pose.Sample(Foot, facing, phase, CombatActionType.Dodge, progress, low);
                Invoke(pose, "LateUpdate"); CheckScale(actor, tier);
                Check(pose.DodgeVisible && pose.TravelRenderersHidden, "dodge replaces travel");
                Check(strike.texture == actor.WalkingSheet && (strike.uvRect.width < 0) == actor.MirrorForCheck, "dodge sheet and mirror");
                Near(strike.rectTransform.sizeDelta.y, Height, "dodge keeps travel rectangle height");
                Near(strike.rectTransform.sizeDelta.x, Height * actor.WalkingSheet.width / (actor.FrameCountForCheck * (float)actor.WalkingSheet.height), "dodge keeps travel aspect");
                Near(strike.rectTransform.pivot.y, 0, "dodge resets attack pivot");
                CheckDodgeMesh(strike);
                pose.Sample(Foot, facing, CombatPhase.Idle, CombatActionType.Wait, 0, low);
                CheckRest(actor, pose, tier, baseline);
            }
            foreach (var phase in new[] { CombatPhase.Hitstun, CombatPhase.Dead })
            {
                pose.Sample(Foot, facing, phase, CombatActionType.Wait, .5f, low);
                Check(!pose.StrikeVisible && !actor.RenderSuppressed, "non-attack phase restores travel");
                CheckScale(actor, tier);
                pose.Sample(Foot, facing, CombatPhase.Idle, CombatActionType.Wait, 0, low);
                CheckRest(actor, pose, tier, baseline);
            }
            // Retarget while hidden: layout must read the current view, and newly built
            // renderers must stay hidden until the action actually ends.
            var originalView = actor.View; var originalWalk = actor.WalkingSheet;
            var nextView = originalView.name.EndsWith("-back") ? "front" : "back";
            var next = TownCharacterArt.Find("hero-" + gender + "-" + nextView);
            pose.Sample(Foot, facing, CombatPhase.Active, CombatActionType.Light, .5f, low);
            actor.SetView(TownCharacterArt.Hero(gender, nextView, false));
            actor.SetFrameSheet(TownCharacterArt.Hero(gender, nextView, true), next.frames, next.cycleDistance);
            Invoke(actor, "Update"); // Resolve the rebuilt renderer to standing art while still suppressed.
            frame = Field<RawImage>(actor, "frameImage");
            Check(!frame.gameObject.activeSelf && actor.RenderSuppressed, "rebuilt travel remains hidden");
            var turned = VisibleBounds(frame, (RectTransform)actor.Root.parent);
            Invoke(pose, "LateUpdate");
            var turnedAttack = VisibleBounds(strike, (RectTransform)actor.Root.parent);
            Near(turnedAttack.x, turned.x, "changed view sole"); Near(turnedAttack.y, turned.y, "changed view height");
            pose.Sample(Foot, facing, CombatPhase.Idle, CombatActionType.Wait, 0, low);
            actor.SetView(originalView);
            var originalEntry = TownCharacterArt.Find("hero-" + gender + "-" + (originalView.name.EndsWith("-front") ? "front" : originalView.name.EndsWith("-back") ? "back" : "side"));
            actor.SetFrameSheet(originalWalk, originalEntry.frames, originalEntry.cycleDistance);
            Rest(actor); frame = Field<RawImage>(actor, "frameImage");
            CheckRest(actor, pose, tier, baseline);
            // Changing tier while attacking must use the current owner size, not a cached entry scale.
            foreach (float changedTier in new[] { .8f, 1.25f, tier })
            {
                actor.SetScale(changedTier);
                pose.Sample(Foot, facing, CombatPhase.Active, CombatActionType.Heavy, .5f, low);
                Invoke(pose, "LateUpdate"); CheckScale(actor, changedTier);
            }
            // Explicit lifecycle calls make this executable in edit mode without a Play-mode clock.
            pose.enabled = false; Invoke(pose, "OnDisable");
            CheckRest(actor, pose, tier, baseline);
            Near(Quaternion.Angle(actor.Root.localRotation, Quaternion.identity), 0, "disable removes tilt");
            pose.enabled = true;
            pose.Sample(Foot, facing, CombatPhase.Active, CombatActionType.Light, .5f, low);
            Invoke(pose, "OnDisable"); Invoke(pose, "OnDestroy"); Object.DestroyImmediate(pose);
            Check(!actor.RenderSuppressed && frame.gameObject.activeSelf, "destroy restores travel");
            CheckScale(actor, tier);
            report.AppendLine(context + " standHeight=" + (baseline.y - baseline.x).ToString("F4") + " PASS");
        }

        static void CheckRest(PuppetActor actor, ActionCombatPose pose, float tier, Vector2 baseline)
        {
            Check(!pose.StrikeVisible && !actor.RenderSuppressed && actor.StandingFrameForCheck, "returns to planted standing art");
            CheckScale(actor, tier);
            var frame = Field<RawImage>(actor, "frameImage");
            Check(frame.gameObject.activeSelf, "travel image restored");
            var after = VisibleBounds(frame, (RectTransform)actor.Root.parent);
            Near(after.x, baseline.x, "restored sole"); Near(after.y, baseline.y, "restored height");
        }

        static void CheckDodgeMesh(RawImage image)
        {
            using (var mesh = new VertexHelper())
            {
                var box = image.rectTransform.rect;
                mesh.AddVert(new Vector3(box.xMin, box.yMin), Color.white, Vector2.zero);
                mesh.AddVert(new Vector3(box.xMax, box.yMin), Color.white, Vector2.right);
                mesh.AddVert(new Vector3(box.xMax, box.yMax), Color.white, Vector2.one);
                mesh.AddVert(new Vector3(box.xMin, box.yMax), Color.white, Vector2.up);
                mesh.AddTriangle(0, 1, 2); mesh.AddTriangle(2, 3, 0);
                image.GetComponent<ActionDodgeMesh>().ModifyMesh(mesh);
                float minY = float.MaxValue, maxY = float.MinValue; var vertex = UIVertex.simpleVert;
                for (int i = 0; i < mesh.currentVertCount; i++)
                {
                    mesh.PopulateUIVertex(ref vertex, i); minY = Mathf.Min(minY, vertex.position.y); maxY = Mathf.Max(maxY, vertex.position.y);
                }
                Near(minY, 0, "dodge mesh feet stay planted");
                Check(maxY > Height * .94f && maxY <= Height + .001f, "dodge bends without enlargement");
            }
        }

        static void CaptureBoard(RectTransform parent, Camera camera, RenderTexture target, string output, string gender, string view, string direction, bool mirror, float tier, bool low)
        {
            var actors = new List<PuppetActor>();
            Texture2D image = null; var previous = RenderTexture.active;
            string file = gender + "-" + direction + "-tier" + tier.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "-low" + low;
            var diagnostics = new StringBuilder(context + "\n");
            try
            {
                for (int i = 0; i < 5; i++)
                {
                    var at = new Vector2(120 + 240 * i, Foot.y);
                    var actor = MakeActor(parent, gender, view, mirror, tier, low, at, out var pose); actors.Add(actor);
                    if (i > 0) pose.Sample(at, mirror ? -1 : 1, i == 4 ? CombatPhase.Active : Phases[i - 1], i == 4 ? CombatActionType.Dodge : CombatActionType.Light, .5f, low);
                }
                PrepareBoardGraphics(parent, camera, target, diagnostics);
                camera.Render(); RenderTexture.active = target;
                image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
                // Keep the actual framebuffer even on failure; a blank image must fail below.
                File.WriteAllBytes(Path.Combine(output, file + ".png"), image.EncodeToPNG());
                // A non-empty PNG alone is not evidence: each actor column must contain foreground ink.
                var pixels = image.GetPixels32();
                for (int column = 0; column < 5; column++)
                {
                    int ink = 0; var background = pixels[0];
                    for (int y = 60; y < 290; y++) for (int x = column * 240 + 10; x < column * 240 + 230; x++)
                    {
                        var p = pixels[y * target.width + x];
                        if (Math.Abs(p.r - background.r) + Math.Abs(p.g - background.g) + Math.Abs(p.b - background.b) > 35) ink++;
                    }
                    diagnostics.AppendLine("column=" + column + " foregroundPixels=" + ink + " threshold>300");
                    Check(ink > 300, "rendered foreground in column " + column + " (pixels=" + ink + ")");
                }
            }
            finally
            {
                RenderTexture.active = previous;
                if (image != null) Object.DestroyImmediate(image);
                foreach (var actor in actors) Object.DestroyImmediate(actor.gameObject);
                File.WriteAllText(Path.Combine(output, file + ".render.txt"), diagnostics.ToString(), new UTF8Encoding(false));
            }
        }

        static void PrepareBoardGraphics(RectTransform parent, Camera camera, RenderTexture target, StringBuilder diagnostics)
        {
            Canvas.ForceUpdateCanvases();
            // No editor frame elapses in this batch method. Rebuild the real UGUI components
            // explicitly, including mesh modifiers, instead of waiting for a GameView repaint.
            var graphics = parent.GetComponentsInChildren<Graphic>();
            foreach (var graphic in graphics)
                if (graphic.isActiveAndEnabled) { graphic.SetAllDirty(); graphic.Rebuild(CanvasUpdate.PreRender); }
            Canvas.ForceUpdateCanvases();
            diagnostics.AppendLine("cameraType=" + camera.cameraType + " scene=" + camera.scene.handle +
                " canvasScene=" + parent.gameObject.scene.handle + " mode=" + parent.GetComponent<Canvas>().renderMode +
                " canvasRect=" + parent.rect + " target=" + target.width + "x" + target.height);
            Check(camera.scene == parent.gameObject.scene, "camera renders only the test scene");
            var lowerLeft = camera.WorldToScreenPoint(parent.TransformPoint(new Vector3(parent.rect.xMin, parent.rect.yMin)));
            var upperRight = camera.WorldToScreenPoint(parent.TransformPoint(new Vector3(parent.rect.xMax, parent.rect.yMax)));
            diagnostics.AppendLine("canvas projected lowerLeft=" + lowerLeft + " upperRight=" + upperRight);
            Near(lowerLeft.x, 0, "canvas left projects to pixel zero"); Near(lowerLeft.y, 0, "canvas bottom projects to pixel zero");
            Near(upperRight.x, target.width, "canvas right projects to render width"); Near(upperRight.y, target.height, "canvas top projects to render height");
            int visibleGraphics = 0;
            foreach (var graphic in graphics)
            {
                if (!graphic.isActiveAndEnabled || graphic.color.a <= 0) continue;
                var renderer = graphic.canvasRenderer;
                var mesh = renderer.GetMesh(); // Renderer owns this mesh; inspect without clearing/destroying it.
                int vertices = mesh != null ? mesh.vertexCount : 0;
                var center = camera.WorldToScreenPoint(graphic.rectTransform.TransformPoint(graphic.rectTransform.rect.center));
                diagnostics.AppendLine(graphic.name + " vertices=" + vertices + " culled=" + renderer.cull +
                    " materials=" + renderer.materialCount + " texture=" + graphic.mainTexture.name + " screenCenter=" + center);
                Check(graphic.gameObject.scene == camera.scene, "graphic belongs to camera preview scene");
                Check(vertices >= 4 && renderer.materialCount > 0 && !renderer.cull, "real UGUI geometry/material submitted: " + graphic.name);
                Check(center.z > camera.nearClipPlane && center.z < camera.farClipPlane && center.x > 0 && center.x < target.width && center.y > 0 && center.y < target.height,
                    "graphic projects inside render target: " + graphic.name);
                visibleGraphics++;
            }
            Check(visibleGraphics == 6, "five characters and one ground line submitted");
        }

        static Vector2 VisibleBounds(RawImage image, RectTransform parent)
        {
            var texture = (Texture2D)image.texture;
            if (!sources.TryGetValue(texture, out var copy))
            {
                copy = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, AssetDatabase.GetAssetPath(texture));
                Check(copy.LoadImage(File.ReadAllBytes(path)), "decode source alpha"); sources[texture] = copy;
            }
            var uv = image.uvRect;
            int left = Mathf.RoundToInt(Mathf.Min(uv.x, uv.x + uv.width) * copy.width);
            int right = Mathf.RoundToInt(Mathf.Max(uv.x, uv.x + uv.width) * copy.width);
            string key = texture.GetInstanceID() + ":" + left + ":" + right;
            if (!alphaSpans.TryGetValue(key, out var span))
            {
                int bottom = copy.height, top = 0; var pixels = copy.GetPixels32();
                for (int y = 0; y < copy.height; y++) for (int x = left; x < right; x++)
                    if (pixels[y * copy.width + x].a >= 32) { bottom = Mathf.Min(bottom, y); top = Mathf.Max(top, y + 1); }
                Check(top > bottom, "nonempty alpha span"); span = new Vector2(bottom, top); alphaSpans[key] = span;
            }
            var box = image.rectTransform.rect;
            float low = box.yMin + box.height * span.x / copy.height, high = box.yMin + box.height * span.y / copy.height;
            return new Vector2(parent.InverseTransformPoint(image.transform.TransformPoint(new Vector3(0, low))).y,
                parent.InverseTransformPoint(image.transform.TransformPoint(new Vector3(0, high))).y);
        }

        static void Rest(PuppetActor actor)
        {
            actor.SetMotion(PuppetActor.Motion.Idle); typeof(PuppetActor).GetField("turnTime", Private).SetValue(actor, 1f);
            Invoke(actor, "Update"); actor.SetRenderSuppressed(true); actor.SetRenderSuppressed(false);
        }
        static T Field<T>(object target, string field) => (T)target.GetType().GetField(field, Private).GetValue(target);
        static void Invoke(object target, string method) => target.GetType().GetMethod(method, Private).Invoke(target, null);
        static void CheckScale(PuppetActor actor, float tier)
        {
            Near(actor.Root.localScale.x, tier, "root scale x"); Near(actor.Root.localScale.y, tier, "root scale y"); Near(actor.Root.localScale.z, tier, "root scale z");
        }
        static void Near(float actual, float expected, string message)
        { Check(!float.IsNaN(actual) && !float.IsInfinity(actual) && Mathf.Abs(actual - expected) < .01f, message + ": " + actual + " vs " + expected); }
        static void Check(bool condition, string message)
        { checks++; if (!condition) throw new InvalidOperationException(context + ": " + message); }
    }
}
