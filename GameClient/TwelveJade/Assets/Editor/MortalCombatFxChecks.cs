using System;
using System.Collections.Generic;
using System.IO;
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
    // ExecuteMethod: TwelveJade.Editor.MortalCombatFxChecks.Run (Edit mode, batch or graphical).
    // Uses isolated PreviewScene + WorldSpace Canvas with real UGUI mesh rebuild and Camera.Render output.
    public static class MortalCombatFxChecks
    {
        const int Width = 1280, Height = 720;
        static readonly Color CanvasBg = new Color(.22f, .24f, .21f, 1f);
        static int assertions, failures;
        static string context;

        [MenuItem("Tools/Twelve Jade/Check mortal combat fx")]
        public static void Run()
        {
            assertions = failures = 0;
            context = "init";
            string project = Directory.GetParent(Application.dataPath).FullName;
            string repoRoot = Directory.GetParent(Directory.GetParent(project).FullName).FullName;
            string output = Environment.GetEnvironmentVariable("EJS_MORTAL_FX_OUTPUT");
            if (string.IsNullOrWhiteSpace(output))
                output = Path.Combine(repoRoot, "Tools", "screenshots", "MortalCombatFxChecks");
            Directory.CreateDirectory(output);

            var report = new StringBuilder("MortalCombatFxChecks: isolated PreviewScene + real UGUI Canvas + Camera.Render.\n");
            var activeScene = SceneManager.GetActiveScene();
            int prevPreviewCount = EditorSceneManager.previewSceneCount;
            var userScenes = new List<(Scene scene, bool loaded, bool dirty, GameObject[] roots)>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                userScenes.Add((s, s.isLoaded, s.isDirty, s.isLoaded ? s.GetRootGameObjects() : Array.Empty<GameObject>()));
            }

            Scene preview = default;
            GameObject camObj = null, canvasObj = null, markersObj = null;
            RenderTexture target = null;
            var previousTarget = RenderTexture.active;

            void CleanUp(Action action)
            {
                try { action(); }
                catch (Exception ex) { failures++; report.AppendLine("FAIL cleanup: " + ex); Debug.LogException(ex); }
            }

            try
            {
                Check(!EditorApplication.isPlayingOrWillChangePlaymode, "requires Edit mode");
                preview = EditorSceneManager.NewPreviewScene();
                Check(preview.IsValid() && EditorSceneManager.IsPreviewScene(preview), "isolated preview scene created");

                camObj = EditorUtility.CreateGameObjectWithHideFlags("Mortal fx check camera", HideFlags.HideAndDontSave, typeof(Camera));
                SceneManager.MoveGameObjectToScene(camObj, preview);
                var cam = camObj.GetComponent<Camera>();
                cam.cameraType = CameraType.Game; cam.enabled = false; cam.scene = preview;
                cam.renderingPath = RenderingPath.Forward;
                cam.orthographic = true; cam.orthographicSize = Height * .5f; cam.aspect = Width / (float)Height;
                cam.nearClipPlane = .1f; cam.farClipPlane = 100f; cam.useOcclusionCulling = false;
                cam.transform.position = new Vector3(Width * .5f, Height * .5f, -10f);
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = CanvasBg;
                cam.allowHDR = false; cam.allowMSAA = false;
                target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = target;

                canvasObj = EditorUtility.CreateGameObjectWithHideFlags("Mortal fx check canvas", HideFlags.HideAndDontSave, typeof(RectTransform), typeof(Canvas));
                SceneManager.MoveGameObjectToScene(canvasObj, preview);
                var canvas = canvasObj.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = cam;
                var parent = (RectTransform)canvas.transform;
                parent.pivot = new Vector2(0f, 1f);
                parent.sizeDelta = new Vector2(Width, Height);
                parent.position = new Vector3(0f, Height, 0f);
                parent.localScale = Vector3.one;

                markersObj = new GameObject("Board markers", typeof(RectTransform));
                var markersRect = (RectTransform)markersObj.transform;
                markersRect.SetParent(parent, false);
                markersRect.anchorMin = Vector2.zero; markersRect.anchorMax = Vector2.one;
                markersRect.offsetMin = Vector2.zero; markersRect.offsetMax = Vector2.zero;
                markersObj.SetActive(false);

                bool reduced = false;
                var ui = new UiKit(null, null, null);
                var fx = WorldCombatEffects.Create(ui, parent, p => p, () => reduced);
                Check(fx != null && fx.Installed, "WorldCombatEffects installed on real Canvas");

                var poolSnap = CapturePoolSnapshot(fx);
                CheckMortalEvents(fx, parent, cam, target, markersRect, output, report);
                CheckFourDirections(fx, parent, cam, target, markersRect, output, report);
                CheckPauseResume(fx, parent, cam, target, report);
                CheckReduceMotion(fx, parent, v => reduced = v, report);
                CheckMagicRetentionAndBossWarn(fx, parent, cam, target, markersRect, output, report);
                CheckPooling(fx, poolSnap, report);
                CheckClear(fx, parent, cam, target, markersObj, report);
            }
            catch (Exception ex)
            {
                failures++;
                report.AppendLine("FAIL " + context + ": " + ex);
                Debug.LogException(ex);
            }
            finally
            {
                CleanUp(() => { if (camObj != null) camObj.GetComponent<Camera>().targetTexture = null; });
                CleanUp(() => RenderTexture.active = previousTarget);
                CleanUp(() => { if (target != null) { target.Release(); Object.DestroyImmediate(target); } });
                CleanUp(() => { if (canvasObj != null) Object.DestroyImmediate(canvasObj); });
                CleanUp(() => { if (camObj != null) Object.DestroyImmediate(camObj); });
                CleanUp(() => { if (preview.IsValid() && EditorSceneManager.IsPreviewScene(preview)) EditorSceneManager.ClosePreviewScene(preview); });
                CleanUp(() =>
                {
                    context = "scene preservation";
                    Check(SceneManager.GetActiveScene() == activeScene, "active scene unchanged");
                    Check(SceneManager.sceneCount == userScenes.Count, "scene count unchanged");
                    Check(EditorSceneManager.previewSceneCount == prevPreviewCount, "preview scene released");
                    foreach (var saved in userScenes)
                    {
                        Check(saved.scene.IsValid() && saved.scene.isLoaded == saved.loaded, "user scene load state retained");
                        Check(saved.scene.isDirty == saved.dirty, "user scene dirty flag retained");
                        if (!saved.loaded) continue;
                        var roots = saved.scene.GetRootGameObjects();
                        Check(roots.Length == saved.roots.Length, "no leaked roots in user scene");
                    }
                });
                report.AppendLine((failures == 0 ? "PASS" : "FAIL") + " assertions=" + assertions + " failures=" + failures);
                File.WriteAllText(Path.Combine(output, "report.txt"), report.ToString(), new UTF8Encoding(false));
                Debug.Log("[MortalCombatFxChecks] assertions=" + assertions + " failures=" + failures + "; output=" + output);
                if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
        }

        static void CheckMortalEvents(WorldCombatEffects fx, RectTransform parent, Camera cam, RenderTexture target,
            RectTransform markers, string output, StringBuilder report)
        {
            context = "mortal events";
            var o = new Vector2(420f, -360f);
            var t = new Vector2(520f, -360f);

            fx.Clear();
            fx.Play("cast:light", o, t, 10f, false);
            fx.Sample(10.05f);
            Check(fx.ActiveMortalKindCount(MortalFxKind.StepDust) == 1 && fx.ActiveMortalKindCount(MortalFxKind.LightSlash) == 0,
                "light startup shows dust only");
            Check(fx.CasterActiveCount == 0 && fx.SheetActiveCount == 0 && fx.ActiveMortalKindCount(MortalFxKind.ImpactGrit) == 0,
                "light startup uses no caster ring, sheet, or hit grit");
            fx.Sample(10.16f);
            Check(fx.ActiveMortalKindCount(MortalFxKind.LightSlash) == 1 && fx.CasterActiveCount == 0 && fx.SheetActiveCount == 0,
                "light active shows mortal blade wind without halo or sheet");
            Check(fx.ActiveMortalKindCount(MortalFxKind.ImpactGrit) == 0, "whiff light cast emits no impact grit");
            CheckMeshCount(fx, parent, 1, "cast:light active mesh");

            fx.Clear();
            fx.Play("cast:heavy", o, t, 20f, false);
            fx.Sample(20.10f);
            Check(fx.ActiveMortalKindCount(MortalFxKind.StepDust) == 1 && fx.SheetActiveCount == 0 && fx.CasterActiveCount == 0,
                "heavy startup shows ground bracing dust only");
            fx.Sample(20.34f);
            Check(fx.ActiveMortalKindCount(MortalFxKind.HeavySlash) == 1 && fx.SheetActiveCount == 0 && fx.CasterActiveCount == 0,
                "heavy active removes swordSheet and caster aura");
            Check(fx.ActiveMortalKindCount(MortalFxKind.ImpactGrit) == 0, "whiff heavy cast emits no impact grit");
            CheckMeshCount(fx, parent, 1, "cast:heavy active mesh");

            fx.Clear();
            fx.Play("dodge", o, t, 30f, false);
            fx.Sample(30.08f);
            Check(fx.ActiveMortalKindCount(MortalFxKind.StepDust) == 1 && fx.SlashActiveCount == 0 && fx.SheetActiveCount == 0 && fx.CasterActiveCount == 0,
                "dodge uses mortal step dust without glowing slash");
            CheckMeshCount(fx, parent, 1, "dodge mesh");

            fx.Clear();
            fx.Play("hit:foe", o, t, 40f, false);
            fx.Sample(40.07f);
            Check(fx.ActiveMortalKindCount(MortalFxKind.ImpactGrit) == 1 && fx.SheetActiveCount == 0 && LegacyHitActiveCount(fx) == 0,
                "hit:foe uses compact mortal impact grit without swordSheet");
            CheckMeshCount(fx, parent, 1, "hit:foe mesh");

            fx.Clear();
            fx.Play("hit:player", o, t, 50f, false);
            fx.Sample(50.07f);
            Check(fx.ActiveMortalKindCount(MortalFxKind.ImpactGrit) == 1 && fx.SheetActiveCount == 0 && fx.CasterActiveCount == 0 && LegacyHitActiveCount(fx) == 0,
                "hit:player removes thunderSheet and caster halo");
            CheckMeshCount(fx, parent, 1, "hit:player mesh");

            fx.Clear();
            fx.Play("heal", o, o, 60f, false);
            fx.Sample(60.16f);
            Check(fx.ActiveMortalKindCount(MortalFxKind.HerbBreath) == 1 && fx.SheetActiveCount == 0 && fx.MoteActiveCount == 0,
                "heal uses mortal herb breath without clarity lotus sheet or petal motes");
            CheckMeshCount(fx, parent, 1, "heal mesh");

            fx.Clear();
            fx.Play("death", o, t, 70f, false);
            fx.Sample(70.20f);
            Check(fx.ActiveMortalKindCount(MortalFxKind.DefeatAsh) == 1 && fx.SheetActiveCount == 0 && LegacyHitActiveCount(fx) == 0 && fx.MoteActiveCount == 0,
                "death removes clarity lotus and impact flash");
            CheckMeshCount(fx, parent, 1, "death mesh");

            // Render 3x3 board for visual inspection: each cell staggered to its representative frame.
            fx.Clear();
            ClearMarkers(markers);
            markers.gameObject.SetActive(true);
            float now = 100f;
            var cells = new (string evt, float offset, bool targetDiff, float age)[] {
                ("cast:light", 0f, true, .05f),
                ("cast:light", 0f, true, .16f),
                ("cast:heavy", 0f, true, .34f),
                ("dodge", 0f, true, .08f),
                ("hit:foe", 0f, true, .14f),
                ("hit:player", 0f, true, .14f),
                ("heal", 0f, false, .18f),
                ("death", 0f, true, .20f),
                ("warn:boss", 0f, true, .16f)
            };
            int colW = Width / 3, rowH = Height / 3;
            for (int i = 0; i < cells.Length; i++)
            {
                int col = i % 3, row = i / 3;
                float cx = col * colW + colW * .42f;
                float cy = -(row * rowH + rowH * .66f);
                var cellO = new Vector2(cx, cy);
                var cellT = cells[i].targetDiff ? cellO + new Vector2(88f, 0f) : cellO;
                AddMarker(markers, cellO, new Color(.78f, .76f, .66f, .45f), 8f);
                if (cells[i].targetDiff) AddMarker(markers, cellT, new Color(.65f, .36f, .28f, .45f), 8f);
                fx.Play(cells[i].evt, cellO, cellT, now - cells[i].age, cells[i].evt == "warn:boss");
            }
            fx.Sample(now);
            var tex = Capture(parent, cam, target, Path.Combine(output, "mortal-moves.png"));
            for (int i = 0; i < cells.Length; i++)
            {
                int col = i % 3, row = i / 3;
                int ink = CountCellInk(tex, col * colW + 8, row * rowH + 8, colW - 16, rowH - 16);
                Check(ink > 40, "mortal-moves cell " + i + " (" + cells[i].evt + ") has rendered pixels (" + ink + ")");
            }
            Object.DestroyImmediate(tex);
            markers.gameObject.SetActive(false);
            ClearMarkers(markers);
            report.AppendLine("PASS mortal events + 3x3 board");
        }

        static void CheckFourDirections(WorldCombatEffects fx, RectTransform parent, Camera cam, RenderTexture target,
            RectTransform markers, string output, StringBuilder report)
        {
            context = "four directions";
            var origin = new Vector2(640f, -360f);
            var dirs = new (string name, Vector2 dir)[] {
                ("right", Vector2.right),
                ("left", Vector2.left),
                ("up", Vector2.up),
                ("down", Vector2.down)
            };
            var events = new (string evt, MortalFxKind kind, float age, bool atTarget, float lift)[] {
                ("cast:light", MortalFxKind.LightSlash, .16f, false, 40f),
                ("cast:heavy", MortalFxKind.HeavySlash, .34f, false, 44f),
                ("dodge", MortalFxKind.StepDust, .08f, false, 12f),
                ("hit:foe", MortalFxKind.ImpactGrit, .14f, true, 36f)
            };

            foreach (var ev in events)
            foreach (var d in dirs)
            {
                fx.Clear();
                var aim = origin + d.dir * 100f;
                fx.Play(ev.evt, origin, aim, 10f, false);
                fx.Sample(10f + ev.age);
                RebuildGraphics(parent);
                var active = FindActiveMortal(fx, ev.kind);
                Check(active != null, ev.evt + " " + d.name + " active node exists");
                Check(Quaternion.Angle(active.rectTransform.localRotation, Quaternion.identity) < .01f,
                    ev.evt + " " + d.name + " stays upright (no inverted rotation)");
                var mesh = active.canvasRenderer.GetMesh();
                Check(mesh != null && mesh.vertexCount >= 6, ev.evt + " " + d.name + " generated real mesh");

                var anchor = (ev.atTarget ? aim : origin) + (ev.kind == MortalFxKind.StepDust ? Vector2.zero : Vector2.up * ev.lift);
                Vector2 centroid = Vector2.zero;
                float maxForward = float.MinValue;
                var verts = mesh.vertices;
                var basePos = active.rectTransform.anchoredPosition;
                for (int i = 0; i < verts.Length; i++)
                {
                    var p = basePos + new Vector2(verts[i].x, verts[i].y);
                    centroid += p;
                    maxForward = Mathf.Max(maxForward, Vector2.Dot(p - anchor, d.dir));
                }
                centroid /= verts.Length;
                Check(maxForward > 24f, ev.evt + " " + d.name + " extends toward target (" + maxForward.ToString("F1") + ")");
                if (ev.kind == MortalFxKind.ImpactGrit)
                    Check(maxForward < 65f, ev.evt + " " + d.name + " remains compact small-range impact (" + maxForward.ToString("F1") + ")");
                if (ev.kind != MortalFxKind.StepDust)
                    Check(Vector2.Dot(centroid - anchor, d.dir) > 4f, ev.evt + " " + d.name + " centroid biases along direction");
            }

            // Render 4-direction visual showcase (4 quadrants)
            fx.Clear();
            ClearMarkers(markers);
            markers.gameObject.SetActive(true);
            float now = 200f;
            var quadCenters = new[] {
                new Vector2(320f, -180f), new Vector2(960f, -180f),
                new Vector2(320f, -540f), new Vector2(960f, -540f)
            };
            for (int i = 0; i < 4; i++)
            {
                var c = quadCenters[i];
                var aim = c + dirs[i].dir * 96f;
                AddMarker(markers, c, new Color(.78f, .76f, .66f, .45f), 8f);
                AddMarker(markers, aim, new Color(.65f, .36f, .28f, .45f), 8f);
                fx.Play("cast:heavy", c, aim, now - .34f, false);
                fx.Play("hit:foe", c, aim, now - .07f, false);
            }
            fx.Sample(now);
            var tex = Capture(parent, cam, target, Path.Combine(output, "mortal-directions.png"));
            int hw = Width / 2, hh = Height / 2;
            for (int i = 0; i < 4; i++)
            {
                int col = i % 2, row = i / 2;
                int ink = CountCellInk(tex, col * hw + 10, row * hh + 10, hw - 20, hh - 20);
                Check(ink > 120, "mortal-directions quadrant " + dirs[i].name + " rendered pixels (" + ink + ")");
            }
            Object.DestroyImmediate(tex);
            markers.gameObject.SetActive(false);
            ClearMarkers(markers);
            report.AppendLine("PASS four directions");
        }

        static void CheckPauseResume(WorldCombatEffects fx, RectTransform parent, Camera cam, RenderTexture target, StringBuilder report)
        {
            context = "pause and resume";
            fx.Clear();
            var o = new Vector2(540f, -360f);
            var t = new Vector2(650f, -360f);
            fx.Play("cast:heavy", o, t, 10f, false);
            fx.Play("hit:foe", o, t, 10.30f, false);

            fx.Sample(10.36f);
            RebuildGraphics(parent);
            var frozenVerts = SnapshotActiveVertices(fx);
            var frameA = CapturePixels(cam, target);

            fx.Sample(10.36f);
            fx.Sample(10.36f);
            RebuildGraphics(parent);
            var frozenVerts2 = SnapshotActiveVertices(fx);
            var frameB = CapturePixels(cam, target);

            Check(frozenVerts.Count > 0 && frozenVerts.Count == frozenVerts2.Count, "frozen vertex count stays constant");
            for (int i = 0; i < frozenVerts.Count; i++)
                Check((frozenVerts[i] - frozenVerts2[i]).sqrMagnitude < .00001f, "frozen vertex " + i + " unchanged when now paused");
            Check(PixelDiffCount(frameA, frameB) == 0, "frozen render buffer 0 pixel changes");

            fx.Sample(10.45f);
            RebuildGraphics(parent);
            var resumedVerts = SnapshotActiveVertices(fx);
            var frameC = CapturePixels(cam, target);
            Check(PixelDiffCount(frameA, frameC) > 20, "resuming now advances rendered pixels");
            bool moved = false;
            for (int i = 0; i < Mathf.Min(frozenVerts.Count, resumedVerts.Count); i++)
                if ((frozenVerts[i] - resumedVerts[i]).sqrMagnitude > .01f) { moved = true; break; }
            Check(moved, "resuming now advances mesh vertices");

            fx.Sample(12f);
            Check(fx.ActiveCount == 0, "all effects finish cleanly after expiration");
            report.AppendLine("PASS pause/resume freeze and expiration");
        }

        static void CheckReduceMotion(WorldCombatEffects fx, RectTransform parent, Action<bool> setReduced, StringBuilder report)
        {
            context = "reduceMotion";
            var o = new Vector2(640f, -360f);
            var t = new Vector2(730f, -360f);
            var pairs = new (string evt, MortalFxKind kind, float age)[] {
                ("hit:foe", MortalFxKind.ImpactGrit, .09f),
                ("dodge", MortalFxKind.StepDust, .10f),
                ("heal", MortalFxKind.HerbBreath, .22f)
            };

            foreach (var p in pairs)
            {
                setReduced(false);
                fx.Clear();
                fx.Play(p.evt, o, t, 10f, false);
                fx.Sample(10f + p.age);
                RebuildGraphics(parent);
                var normalStats = MeasureMortalSpreadAndAlpha(FindActiveMortal(fx, p.kind));

                setReduced(true);
                fx.Clear();
                fx.Play(p.evt, o, t, 10f, false);
                fx.Sample(10f + p.age);
                RebuildGraphics(parent);
                var lowStats = MeasureMortalSpreadAndAlpha(FindActiveMortal(fx, p.kind));

                Check(lowStats.spread < normalStats.spread, p.evt + " reduceMotion lowers spatial drift/spread (" +
                    lowStats.spread.ToString("F1") + " < " + normalStats.spread.ToString("F1") + ")");
                Check(lowStats.maxAlpha < normalStats.maxAlpha, p.evt + " reduceMotion lowers peak alpha (" +
                    lowStats.maxAlpha.ToString("F2") + " < " + normalStats.maxAlpha.ToString("F2") + ")");
            }
            setReduced(false);
            report.AppendLine("PASS reduceMotion drift and alpha damping");
        }

        static void CheckMagicRetentionAndBossWarn(WorldCombatEffects fx, RectTransform parent, Camera cam, RenderTexture target,
            RectTransform markers, string output, StringBuilder report)
        {
            context = "magic retention and boss warn";
            var mortalSave = new SaveData();
            Check(!PrologueRules.CanCultivate(mortalSave), "PrologueRules gate blocks cultivation before departure/initiation");
            mortalSave.MarkFlag(PrologueRules.DepartureFlag);
            Check(!PrologueRules.CanCultivate(mortalSave), "PrologueRules gate requires both flags");
            mortalSave.MarkFlag(PrologueRules.InitiationFlag);
            Check(PrologueRules.CanCultivate(mortalSave), "PrologueRules gate opens when both flags exist");

            var o = new Vector2(520f, -360f);
            var t = new Vector2(680f, -360f);

            fx.Clear();
            fx.Play("cast:sword", o, t, 10f, false);
            fx.Sample(10.08f);
            Check(fx.CasterActiveCount >= 1, "cast:sword retains startup caster");
            fx.Sample(10.35f);
            Check(fx.SheetActiveCount >= 1 && fx.SlashActiveCount >= 1, "cast:sword retains sword sheet and slash");

            fx.Clear();
            fx.Play("cast:thunder", o, t, 20f, false);
            fx.Sample(20.20f);
            Check(fx.CasterActiveCount >= 2, "cast:thunder retains dual casters");
            fx.Sample(20.70f);
            Check(fx.SheetActiveCount >= 1 && fx.ColumnActiveCount >= 1,
                "cast:thunder retains thunder sheet and lightning column");

            fx.Clear();
            fx.Play("cast:clarity", o, t, 30f, false);
            fx.Sample(30.20f);
            Check(fx.CasterActiveCount >= 1, "cast:clarity retains startup caster");
            fx.Sample(30.60f);
            Check(fx.SheetActiveCount >= 1 && fx.MoteActiveCount >= 1, "cast:clarity retains clarity lotus sheet and motes");

            fx.Clear();
            fx.Play("warn:boss", o, t, 40f, true);
            fx.Telegraph(t, 110f, .55f, true, true);
            fx.Sample(40.14f);
            Check(fx.TelegraphVisible && fx.ActiveMortalKindCount(MortalFxKind.BossWarnRing) == 1,
                "warn:boss + Telegraph shows ground warning ring");
            Check(fx.ActiveMortalKindCount(MortalFxKind.ImpactGrit) == 0 && LegacyHitActiveCount(fx) == 0 &&
                  fx.CasterActiveCount == 0 && fx.SheetActiveCount == 0,
                "warn:boss never triggers hit impact, grit, halo, or skill sheet");

            // Render 4-column showcase for the 3 retained magic events + boss warn
            fx.Clear();
            ClearMarkers(markers);
            markers.gameObject.SetActive(true);
            float now = 100f;
            int colW = Width / 4;
            fx.Play("cast:sword", new Vector2(colW * .3f, -420f), new Vector2(colW * .85f, -420f), now - .35f, false);
            fx.Play("cast:thunder", new Vector2(colW * 1.3f, -420f), new Vector2(colW * 1.65f, -420f), now - .70f, false);
            fx.Play("cast:clarity", new Vector2(colW * 2.5f, -420f), new Vector2(colW * 2.5f, -420f), now - .60f, false);
            var warnAt = new Vector2(colW * 3.5f, -420f);
            fx.Play("warn:boss", warnAt - new Vector2(80f, 0f), warnAt, now - .14f, true);
            fx.Telegraph(warnAt, 105f, .60f, true, true);
            fx.Sample(now);
            var tex = Capture(parent, cam, target, Path.Combine(output, "magic-and-boss-warn.png"));
            for (int c = 0; c < 4; c++)
            {
                int ink = CountCellInk(tex, c * colW + 8, 20, colW - 16, Height - 40);
                Check(ink > 80, "magic-and-boss-warn column " + c + " rendered pixels (" + ink + ")");
            }
            Object.DestroyImmediate(tex);
            markers.gameObject.SetActive(false);
            ClearMarkers(markers);
            report.AppendLine("PASS 3 magic events preserved + gate untouched + boss warn isolated");
        }

        static void CheckPooling(WorldCombatEffects fx, List<int> snapshot, StringBuilder report)
        {
            context = "pooling";
            var allEvents = new[] {
                "cast:light", "cast:heavy", "dodge", "hit:foe", "hit:player",
                "heal", "death", "warn:boss", "cast:sword", "cast:thunder", "cast:clarity"
            };
            float now = 500f;
            for (int i = 0; i < 66; i++)
            {
                string evt = allEvents[i % allEvents.Length];
                fx.Play(evt, new Vector2(400f, -360f), new Vector2(510f, -360f), now, i % 3 == 0);
                now += .06f;
                fx.Sample(now);
            }
            var after = CapturePoolSnapshot(fx);
            Check(after.Count == snapshot.Count, "no objects added or destroyed during 66 Play calls (" + after.Count + ")");
            for (int i = 0; i < Mathf.Min(after.Count, snapshot.Count); i++)
                Check(after[i] == snapshot[i], "pooled node instance ID unchanged at slot " + i);
            report.AppendLine("PASS pooling zero runtime allocations (" + snapshot.Count + " pooled graphics)");
        }

        static void CheckClear(WorldCombatEffects fx, RectTransform parent, Camera cam, RenderTexture target,
            GameObject markersObj, StringBuilder report)
        {
            context = "clear";
            markersObj.SetActive(false);
            fx.Play("cast:heavy", new Vector2(540f, -360f), new Vector2(660f, -360f), 10f, false);
            fx.Play("hit:foe", new Vector2(540f, -360f), new Vector2(660f, -360f), 10.30f, false);
            fx.Play("cast:sword", new Vector2(400f, -360f), new Vector2(500f, -360f), 10.05f, false);
            fx.Telegraph(new Vector2(660f, -360f), 100f, .5f, true, true);
            fx.Sample(10.38f);
            Check(fx.ActiveCount > 0 && fx.TelegraphVisible, "pre-clear effects are active");

            fx.Clear();
            Check(fx.ActiveCount == 0 && !fx.TelegraphVisible, "Clear zeroes ActiveCount and hides Telegraph");
            foreach (var g in fx.GetComponentsInChildren<WorldCombatFxGraphic>(true))
                Check(!g.enabled, "cleared graphic disabled: " + g.name);
            foreach (var r in fx.GetComponentsInChildren<RawImage>(true))
                Check(!r.enabled, "cleared sheet image disabled: " + r.name);

            RebuildGraphics(parent);
            var pixels = CapturePixels(cam, target);
            int remaining = CountAllInk(pixels);
            Check(remaining == 0, "Clear leaves zero rendered foreground pixels (got " + remaining + ")");
            report.AppendLine("PASS Clear zero residue");
        }

        static List<int> CapturePoolSnapshot(WorldCombatEffects fx)
        {
            var ids = new List<int>();
            foreach (var g in fx.GetComponentsInChildren<WorldCombatFxGraphic>(true)) ids.Add(g.GetInstanceID());
            return ids;
        }

        static WorldCombatMortalFx FindActiveMortal(WorldCombatEffects fx, MortalFxKind kind)
        {
            foreach (var m in fx.GetComponentsInChildren<WorldCombatMortalFx>(false))
                if (m.enabled && m.Kind == kind) return m;
            return null;
        }

        static int LegacyHitActiveCount(WorldCombatEffects fx)
        {
            int n = 0;
            foreach (var h in fx.GetComponentsInChildren<WorldCombatFxHit>(true))
                if (h.enabled) n++;
            return n;
        }

        static void CheckMeshCount(WorldCombatEffects fx, RectTransform parent, int minActiveMeshes, string label)
        {
            RebuildGraphics(parent);
            int built = 0;
            foreach (var m in fx.GetComponentsInChildren<WorldCombatMortalFx>(false))
            {
                if (!m.enabled) continue;
                var mesh = m.canvasRenderer.GetMesh();
                if (mesh != null && mesh.vertexCount >= 3 && !m.canvasRenderer.cull) built++;
            }
            Check(built >= minActiveMeshes, label + " built " + built + " real UGUI meshes");
        }

        static (float spread, float maxAlpha) MeasureMortalSpreadAndAlpha(WorldCombatMortalFx fx)
        {
            Check(fx != null, "mortal graphic active for reduceMotion probe");
            var mesh = fx.canvasRenderer.GetMesh();
            Check(mesh != null && mesh.vertexCount > 0, "mortal mesh non-empty for reduceMotion probe");
            var verts = mesh.vertices;
            var cols = mesh.colors32;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            float maxA = 0f;
            for (int i = 0; i < verts.Length; i++)
            {
                minX = Mathf.Min(minX, verts[i].x); maxX = Mathf.Max(maxX, verts[i].x);
                minY = Mathf.Min(minY, verts[i].y); maxY = Mathf.Max(maxY, verts[i].y);
                if (i < cols.Length) maxA = Mathf.Max(maxA, cols[i].a / 255f);
            }
            return (Mathf.Sqrt((maxX - minX) * (maxX - minX) + (maxY - minY) * (maxY - minY)), maxA);
        }

        static List<Vector3> SnapshotActiveVertices(WorldCombatEffects fx)
        {
            var result = new List<Vector3>();
            foreach (var m in fx.GetComponentsInChildren<WorldCombatMortalFx>(false))
            {
                if (!m.enabled) continue;
                var mesh = m.canvasRenderer.GetMesh();
                if (mesh != null) result.AddRange(mesh.vertices);
            }
            return result;
        }

        static void RebuildGraphics(RectTransform parent)
        {
            Canvas.ForceUpdateCanvases();
            foreach (var g in parent.GetComponentsInChildren<Graphic>())
                if (g.isActiveAndEnabled) { g.SetAllDirty(); g.Rebuild(CanvasUpdate.PreRender); }
            Canvas.ForceUpdateCanvases();
        }

        static Texture2D Capture(RectTransform parent, Camera cam, RenderTexture target, string path)
        {
            RebuildGraphics(parent);
            var prev = RenderTexture.active;
            RenderTexture.active = target;
            cam.Render();
            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            return tex;
        }

        static Color32[] CapturePixels(Camera cam, RenderTexture target)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = target;
            cam.Render();
            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            var pixels = tex.GetPixels32();
            Object.DestroyImmediate(tex);
            return pixels;
        }

        static int PixelDiffCount(Color32[] a, Color32[] b)
        {
            int diff = 0;
            for (int i = 0; i < Mathf.Min(a.Length, b.Length); i++)
                if (Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b) > 4) diff++;
            return diff;
        }

        static int CountAllInk(Color32[] pixels)
        {
            var bg = (Color32)CanvasBg;
            int count = 0;
            for (int i = 0; i < pixels.Length; i++)
                if (Math.Abs(pixels[i].r - bg.r) + Math.Abs(pixels[i].g - bg.g) + Math.Abs(pixels[i].b - bg.b) > 6) count++;
            return count;
        }

        static int CountCellInk(Texture2D tex, int x0, int topY, int w, int h)
        {
            var pixels = tex.GetPixels32();
            var bg = (Color32)CanvasBg;
            int ink = 0;
            int yStart = Mathf.Max(0, Height - topY - h);
            int yEnd = Mathf.Min(Height - 1, Height - topY);
            int xEnd = Mathf.Min(Width - 1, x0 + w);
            for (int y = yStart; y <= yEnd; y++)
            for (int x = Mathf.Max(0, x0); x <= xEnd; x++)
            {
                var p = pixels[y * Width + x];
                if (Math.Abs(p.r - bg.r) + Math.Abs(p.g - bg.g) + Math.Abs(p.b - bg.b) > 10) ink++;
            }
            return ink;
        }

        static void AddMarker(Transform parent, Vector2 pos, Color color, float size)
        {
            var go = new GameObject("Marker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(.5f, .5f);
            r.anchoredPosition = pos;
            r.sizeDelta = new Vector2(size, size);
            var img = go.GetComponent<Image>();
            img.color = color; img.raycastTarget = false;
        }

        static void ClearMarkers(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(parent.GetChild(i).gameObject);
        }

        static void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException(context + ": " + message);
        }
    }
}
