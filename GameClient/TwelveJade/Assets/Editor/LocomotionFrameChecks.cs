using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using TwelveJade.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TwelveJade.Editor
{
    // ExecuteMethod: TwelveJade.Editor.LocomotionFrameChecks.Run (Edit mode, including batch).
    // Uses real Resources and PuppetActor. No FrontEnd, saves, scene saving or artwork mutation.
    public static class LocomotionFrameChecks
    {
        // Acceptance contract, deliberately independent of PuppetActor defaults and the manifest.
        const int RequiredFrames = 8;
        const float WalkCycle = 140f, RunCycleFactor = 1.35f, Height = 170f;
        const float Epsilon = .0002f;
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly string[] Views = { "front", "side", "back" };
        static readonly PuppetActor.Motion[] Moving = { PuppetActor.Motion.Walk, PuppetActor.Motion.Run };
        static StringBuilder report;
        static string context;
        static int assertions, cases, passed, failures;

        sealed class Artwork
        {
            public string Id;
            public TownCharacterArt.Entry Entry;
            public Texture2D Stand, Sheet;
        }

        sealed class Drawables
        {
            public RawImage Current, Fade;
            public CanvasRenderer CurrentCanvas, FadeCanvas;
            // Test-owned expectation; never infer expected visibility from the actor under test.
            public bool Suppressed;
        }

        [MenuItem("Tools/Twelve Jade/Check locomotion frames")]
        public static void Run()
        {
            assertions = cases = passed = failures = 0;
            report = new StringBuilder("Locomotion EditMode checks: real actor / real Resources.\n");
            report.AppendLine("Contract: 8 frames; walk cycle 140; run cycle 189 (1.35x). Matrix: 2 genders x 3 views x 2 mirror states x Walk/Run.");
            report.AppendLine("Renderer checks inspect live UI components/UV/opacity, not GPU screenshots. Only owned animation clocks are sampled through reflection.");
            string output = Environment.GetEnvironmentVariable("EJS_LOCOMOTION_OUTPUT");
            if (string.IsNullOrWhiteSpace(output))
                output = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Temp", "LocomotionFrameChecks");
            var activeScene = SceneManager.GetActiveScene();
            int previewCount = EditorSceneManager.previewSceneCount;
            var userScenes = new List<(Scene scene, bool loaded, bool dirty, GameObject[] roots)>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var saved = SceneManager.GetSceneAt(i);
                userScenes.Add((saved, saved.isLoaded, saved.isDirty,
                    saved.isLoaded ? saved.GetRootGameObjects() : Array.Empty<GameObject>()));
            }
            Scene preview = default;
            GameObject owner = null;
            try
            {
                context = "initialization";
                Check(!EditorApplication.isPlayingOrWillChangePlaymode, "requires Edit mode");
                Directory.CreateDirectory(output);
                preview = EditorSceneManager.NewPreviewScene();
                Check(preview.IsValid() && EditorSceneManager.IsPreviewScene(preview), "isolated PreviewScene exists");
                owner = EditorUtility.CreateGameObjectWithHideFlags("Locomotion frame checks", HideFlags.HideAndDontSave,
                    typeof(RectTransform), typeof(Canvas));
                SceneManager.MoveGameObjectToScene(owner, preview);
                owner.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                owner.GetComponent<RectTransform>().sizeDelta = new Vector2(800, 600);
                foreach (string gender in new[] { "male", "female" })
                foreach (string view in Views)
                foreach (bool mirror in new[] { false, true })
                foreach (var motion in Moving)
                    RunCase(owner.transform, gender, view, mirror, motion);
            }
            catch (Exception error) { Fail(context, error); }
            finally
            {
                // Independent cleanup: a failed assertion never saves/discards user scenes or skips closing our preview.
                Safely("destroy owned actors", () => { if (owner != null) Object.DestroyImmediate(owner); });
                Safely("close owned preview", () =>
                {
                    if (preview.IsValid() && EditorSceneManager.IsPreviewScene(preview)) EditorSceneManager.ClosePreviewScene(preview);
                });
                Safely("user scene preservation", () =>
                {
                    Check(SceneManager.GetActiveScene() == activeScene, "active user scene unchanged");
                    Check(SceneManager.sceneCount == userScenes.Count, "open scene count unchanged");
                    Check(EditorSceneManager.previewSceneCount == previewCount, "preview scene count restored");
                    foreach (var saved in userScenes)
                    {
                        Check(saved.scene.IsValid() && saved.scene.isLoaded == saved.loaded, "original scene/load state retained");
                        Check(saved.scene.isDirty == saved.dirty, "original unsaved scene state retained");
                        if (!saved.loaded) continue;
                        var roots = saved.scene.GetRootGameObjects();
                        Check(roots.Length == saved.roots.Length, "no leaked or removed user roots");
                        for (int i = 0; i < roots.Length; i++)
                            Check(roots[i] == saved.roots[i] && roots[i] != null && roots[i].scene == saved.scene && roots[i].transform.parent == null,
                                "user root identity/order/parent retained");
                    }
                    report.AppendLine("User scene preservation PASS; no scenes saved, discarded or reopened.");
                });
                report.AppendLine((failures == 0 ? "PASS" : "FAIL") + " cases=" + cases + " passed=" + passed +
                    " assertions=" + assertions + " failures=" + failures);
                try
                {
                    Directory.CreateDirectory(output);
                    File.WriteAllText(Path.Combine(output, "report.txt"), report.ToString(), new UTF8Encoding(false));
                }
                catch (Exception error) { Fail("write report", error); }
                Debug.Log("[LocomotionFrameChecks] cases=" + cases + " passed=" + passed + " assertions=" + assertions +
                    " failures=" + failures + "; report=" + Path.Combine(output, "report.txt"));
                if (Application.isBatchMode) EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
        }

        static void RunCase(Transform parent, string gender, string view, bool mirror, PuppetActor.Motion motion)
        {
            context = gender + "/" + view + "/mirror=" + mirror + "/" + motion;
            string caseName = context;
            cases++;
            PuppetActor actor = null;
            try
            {
                var art = Load(gender, view);
                actor = PuppetActor.Create(parent, art.Stand, new Vector2(120, -280),
                    new Vector2(Height * art.Stand.width / art.Stand.height, Height));
                actor.SetFrameSheet(art.Sheet, art.Entry.frames, art.Entry.cycleDistance);
                actor.SetGaitMode(PuppetActor.GaitMode.Frames);
                actor.SetLegSplit(view != "side");
                actor.SetMirror(mirror);
                actor.SetMotion(motion);
                actor.SetGaitConveyor(0);
                var draw = Capture(actor);
                Tick(actor, draw);
                Check(actor.gameObject.scene == parent.gameObject.scene, "actor belongs to preview scene");
                Check(actor.FrameCountForCheck == RequiredFrames, "actor uses all eight frames");
                Check(actor.MirrorForCheck == mirror && actor.FrameMirroredForCheck == mirror, "requested mirror reaches real frame UV");
                Check(draw.Current.texture == art.Sheet, "moving frame uses real approved sheet");
                Move(actor, draw, Cycle(motion) * .173f, Cycle(motion));
                Check(actor.FramePhaseForCheck > .1f && actor.FramePhaseForCheck < .9f, "checks begin partway through a stride");

                context = caseName + " / cycle";
                CoverCycle(actor, draw);
                context = caseName + " / blocked movement";
                Freeze(actor, draw, false);
                context = caseName + " / mirror";
                MirrorRoundTrip(actor, draw);
                context = caseName + " / direction changes";
                // Exercise every ordered pair from this starting view, including same-sheet reapplication.
                foreach (string next in Views)
                {
                    ChangeArt(actor, draw, Load(gender, next));
                    MirrorRoundTrip(actor, draw);
                    ChangeArt(actor, draw, art);
                    Move(actor, draw, Cycle(motion) / 19f, Cycle(motion));
                }
                context = caseName + " / WalkRun";
                WalkRun(actor, draw, motion);
                context = caseName + " / cycle reconfiguration";
                SwitchMotion(actor, draw, PuppetActor.Motion.Walk);
                // Only this disposable actor receives a different period; resource/config assets remain untouched.
                ChangeArt(actor, draw, art, WalkCycle * 1.5f);
                Move(actor, draw, WalkCycle * 1.5f * .137f, WalkCycle * 1.5f);
                ChangeArt(actor, draw, art);
                SwitchMotion(actor, draw, motion);
                context = caseName + " / turn scale";
                NoSquash(actor, draw);
                context = caseName + " / Idle";
                foreach (bool reduced in new[] { false, true })
                {
                    actor.ReducedMotionForRest = reduced;
                    SwitchMotion(actor, draw, PuppetActor.Motion.Idle);
                    Check(actor.StandingFrameForCheck && draw.Current.texture == actor.View, "Idle uses planted standing artwork");
                    Near(Mathf.Abs(draw.Current.uvRect.width), 1f, "Idle uses full standing texture");
                    Freeze(actor, draw, true);
                    MirrorRoundTrip(actor, draw);
                    SwitchMotion(actor, draw, motion);
                    Check(!actor.StandingFrameForCheck && draw.Current.texture == art.Sheet, "movement resumes walking sheet");
                    Move(actor, draw, Cycle(motion) / 19f, Cycle(motion));
                }
                context = caseName + " / suppression";
                Suppression(actor, draw, gender);
                passed++;
                report.AppendLine("PASS " + caseName + " renderers=" + draw.Current.GetInstanceID() + "," + draw.Fade.GetInstanceID() +
                    " finalPhase=" + Number(actor.FramePhaseForCheck) + " stride=" + Number(actor.StrideDistanceForCheck));
            }
            catch (Exception error) { Fail(context, error); }
            finally { Safely(caseName + " / dispose actor", () => { if (actor != null) Object.DestroyImmediate(actor.gameObject); }); }
        }

        static Artwork Load(string gender, string view)
        {
            string id = "hero-" + gender + "-" + view;
            var entry = TownCharacterArt.Find(id);
            Check(entry != null, id + " manifest exists");
            Check(entry.frames == RequiredFrames, id + " manifest frames must be 8; got " + entry.frames);
            Near(entry.cycleDistance, WalkCycle, id + " manifest cycleDistance must be 140");
            var stand = TownCharacterArt.Hero(gender, view, false);
            var sheet = TownCharacterArt.Hero(gender, view, true);
            Check(stand != null && sheet != null && stand != sheet, id + " real standing/walking textures exist separately");
            Check(stand.width > 0 && stand.height > 0 && sheet.height > 0 && sheet.width >= RequiredFrames && sheet.width % RequiredFrames == 0,
                id + " imported sheet can be divided into eight cells");
            return new Artwork { Id = id, Entry = entry, Stand = stand, Sheet = sheet };
        }

        static Drawables Capture(PuppetActor actor)
        {
            var current = actor.Root.Find("Puppet frame");
            var fade = actor.Root.Find("Puppet frame fade");
            Check(current != null && fade != null, "real Frames drawables exist");
            var images = new Drawables { Current = current.GetComponent<RawImage>(), Fade = fade.GetComponent<RawImage>() };
            Check(images.Current != null && images.Fade != null, "both frame drawables are RawImages");
            images.CurrentCanvas = images.Current.canvasRenderer;
            images.FadeCanvas = images.Fade.canvasRenderer;
            return images;
        }

        static void Renderers(PuppetActor actor, Drawables draw)
        {
            Check(actor.GaitModeForCheck == PuppetActor.GaitMode.Frames && actor.FramesActiveForCheck, "Frames remains active");
            var graphics = actor.GetComponentsInChildren<Graphic>(true);
            Check(graphics.Length == 2 && draw.Current != null && draw.Fade != null,
                "exactly the original two Graphics survive (no orphan/double renderer)");
            Check((graphics[0] == draw.Current && graphics[1] == draw.Fade) || (graphics[1] == draw.Current && graphics[0] == draw.Fade),
                "SetFrameSheet preserves both RawImage instances");
            Check(draw.Current.canvasRenderer == draw.CurrentCanvas && draw.Fade.canvasRenderer == draw.FadeCanvas,
                "CanvasRenderer instances remain identical");
            Check(actor.RenderSuppressed == draw.Suppressed, "requested suppression state persists");
            int visible = 0;
            foreach (var graphic in graphics)
                if (graphic.isActiveAndEnabled && graphic.color.a > Epsilon) visible++;
            Check(visible == (draw.Suppressed ? 0 : 1), "expected active nontransparent body count " + (draw.Suppressed ? 0 : 1) + "; got " + visible);
            Check(draw.Current.gameObject.activeSelf == !draw.Suppressed && draw.Fade.gameObject.activeSelf == !draw.Suppressed,
                "both renderer GameObjects obey suppression");
            Near(draw.Current.color.a, 1f, "current frame stays opaque");
            Near(draw.Fade.color.a, 0f, "fade remains transparent (no overlapping bodies)");
            Check(actor.FramePhaseForCheck >= 0 && actor.FramePhaseForCheck < 1, "phase is finite and normalized to [0,1)");
        }

        static void Tick(PuppetActor actor, Drawables draw)
        {
            Invoke(actor, "Update");
            Renderers(actor, draw);
        }

        static float Move(PuppetActor actor, Drawables draw, float distance, float cycle)
        {
            float phase = actor.FramePhaseForCheck, stride = actor.StrideDistanceForCheck;
            actor.AdvanceGait(distance);
            Tick(actor, draw);
            Near(actor.StrideDistanceForCheck, stride + distance, "positive real displacement is recorded", .002f);
            float advance = Mathf.Repeat(actor.FramePhaseForCheck - phase, 1f);
            Near(advance, distance / cycle, "positive real displacement advances normalized phase");
            return advance;
        }

        static void CoverCycle(PuppetActor actor, Drawables draw)
        {
            float phase = actor.FramePhaseForCheck;
            int first = actor.FrameIndexForCheck, previous = first, transitions = 0;
            Rect initialUV = draw.Current.uvRect;
            var visited = new HashSet<int>();
            float cycle = Cycle(actor.CurrentMotion);
            for (int sample = 0; sample <= RequiredFrames * 4; sample++)
            {
                if (sample > 0) Move(actor, draw, cycle / (RequiredFrames * 4), cycle);
                int frame = actor.FrameIndexForCheck;
                Check(frame >= 0 && frame < RequiredFrames, "frame index lies in the eight-cell sheet");
                visited.Add(frame);
                Near(actor.Root.anchoredPosition.x, 120f, "authored gait anchor has no procedural sideways slide");
                Near(actor.Root.anchoredPosition.y, -280f, "supporting sole has no duplicate procedural bounce");
                Rect uv = draw.Current.uvRect;
                Near(Mathf.Abs(uv.width), 1f / RequiredFrames, "rendered UV covers one cell");
                // Cell center is mirror-independent: verify the actual renderer, not only the diagnostic index.
                Near(uv.center.x, (frame + .5f) / RequiredFrames, "rendered UV belongs to reported frame");
                Near(uv.y, 0, "cell UV starts at bottom"); Near(uv.height, 1, "cell UV uses full height");
                if (frame != previous)
                {
                    Check(frame == (previous + 1) % RequiredFrames, "frame sequence advances by one with wrap");
                    transitions++;
                }
                previous = frame;
            }
            Check(visited.Count == RequiredFrames && transitions == RequiredFrames, "one cycle covers all eight distinct rendered frames in order");
            for (int i = 0; i < RequiredFrames; i++) Check(visited.Contains(i), "full-cycle frame " + i + " observed");
            SamePhase(actor, phase, "full-cycle phase returns");
            Check(actor.FrameIndexForCheck == first, "full cycle returns to starting frame");
            SameUV(draw.Current.uvRect, initialUV, "full-cycle UV returns");
            report.AppendLine("COVERAGE " + context + " frames=0,1,2,3,4,5,6,7 transitions=" + transitions + " distance=" + Number(cycle));
        }

        static void ChangeArt(PuppetActor actor, Drawables draw, Artwork art, float? cycle = null)
        {
            float phase = actor.FramePhaseForCheck, stride = actor.StrideDistanceForCheck;
            int frame = actor.FrameIndexForCheck;
            actor.SetView(art.Stand);
            actor.SetLegSplit(art.Id.EndsWith("-side", StringComparison.Ordinal) == false);
            actor.SetFrameSheet(art.Sheet, art.Entry.frames, cycle ?? art.Entry.cycleDistance);
            Renderers(actor, draw); // Check the switching call itself, before Update could hide a transient second body.
            Continuous(actor, phase, stride, frame, "sheet switch");
            actor.SetGaitMode(PuppetActor.GaitMode.Frames);
            Tick(actor, draw);
            Continuous(actor, phase, stride, frame, "sheet switch after Update");
            Check(actor.View == art.Stand && actor.WalkingSheet == art.Sheet && actor.FrameCountForCheck == RequiredFrames,
                "direction selects requested standing/sheet resources");
            Check(draw.Current.texture == (actor.CurrentMotion == PuppetActor.Motion.Idle ? art.Stand : art.Sheet), "drawable uses selected direction");
            Near(draw.Current.rectTransform.sizeDelta.y, Height, "sheet change preserves display height");
        }

        static void SwitchMotion(PuppetActor actor, Drawables draw, PuppetActor.Motion next)
        {
            float phase = actor.FramePhaseForCheck, stride = actor.StrideDistanceForCheck;
            int frame = actor.FrameIndexForCheck;
            actor.SetMotion(next);
            Check(actor.CurrentMotion == next, "requested motion applied");
            Renderers(actor, draw);
            Continuous(actor, phase, stride, frame, "motion switch");
            Tick(actor, draw);
            Continuous(actor, phase, stride, frame, "motion switch after Update");
        }

        static void WalkRun(PuppetActor actor, Drawables draw, PuppetActor.Motion original)
        {
            SwitchMotion(actor, draw, PuppetActor.Motion.Walk);
            float walk = Move(actor, draw, WalkCycle / 5f, WalkCycle);
            SwitchMotion(actor, draw, PuppetActor.Motion.Run);
            float run = Move(actor, draw, WalkCycle / 5f, WalkCycle * RunCycleFactor);
            Near(walk / run, RunCycleFactor, "equal distance advances Run phase 1/1.35 as far as Walk");
            SwitchMotion(actor, draw, PuppetActor.Motion.Walk);
            SwitchMotion(actor, draw, original);
        }

        static void MirrorRoundTrip(PuppetActor actor, Drawables draw)
        {
            bool original = actor.MirrorForCheck;
            float phase = actor.FramePhaseForCheck, stride = actor.StrideDistanceForCheck;
            int frame = actor.FrameIndexForCheck;
            Rect uv = draw.Current.uvRect;
            Vector2 size = draw.Current.rectTransform.sizeDelta;
            actor.SetMirror(!original);
            Renderers(actor, draw);
            Continuous(actor, phase, stride, frame, "mirror switch");
            Check(actor.MirrorForCheck == !original && actor.FrameMirroredForCheck == !original, "mirror switches actual rendered orientation");
            // Compare endpoints of the same texture interval. Do not reproduce SetFrameSlice's mirror implementation.
            Near(draw.Current.uvRect.x, uv.x + uv.width, "mirror exchanges first endpoint");
            Near(draw.Current.uvRect.x + draw.Current.uvRect.width, uv.x, "mirror exchanges last endpoint");
            Rect reflected = draw.Current.uvRect;
            Near(reflected.y, uv.y, "mirror keeps vertical origin"); Near(reflected.height, uv.height, "mirror keeps vertical interval");
            Tick(actor, draw);
            SameUV(draw.Current.uvRect, reflected, "mirror survives Update");
            Check(draw.Current.rectTransform.sizeDelta == size, "mirror keeps frame dimensions");
            actor.SetMirror(original);
            Tick(actor, draw);
            Continuous(actor, phase, stride, frame, "mirror round trip");
            Check(actor.FrameMirroredForCheck == original, "rendered orientation restored");
            SameUV(draw.Current.uvRect, uv, "mirror round-trip UV");
        }

        static void Freeze(PuppetActor actor, Drawables draw, bool idle)
        {
            float phase = actor.FramePhaseForCheck, stride = actor.StrideDistanceForCheck;
            int frame = actor.FrameIndexForCheck;
            Rect uv = draw.Current.uvRect;
            Vector2 position = actor.Root.anchoredPosition;
            void Unchanged()
            {
                Continuous(actor, phase, stride, frame, "zero movement freeze");
                SameUV(draw.Current.uvRect, uv, "zero movement keeps rendered frame");
                if (idle)
                {
                    Check(actor.StandingFrameForCheck && draw.Current.texture == actor.View, "Idle remains planted");
                    Check(actor.Root.anchoredPosition == position, "Idle feet do not drift");
                }
            }
            foreach (float elapsed in new[] { 1f / 120, 1f / 30, .25f, 1f })
            {
                actor.AdvanceGait(0);
                // EditMode has no reliable player delta. Advance only this actor's time clock, never stride or phase.
                WriteFloat(actor, "time", ReadFloat(actor, "time") + elapsed);
                Tick(actor, draw);
                Unchanged();
                Invoke(actor, "UpdateFrame", elapsed); // Also exercise real frame ticking with a nonzero explicit dt.
                Renderers(actor, draw);
                Unchanged();
            }
        }

        static void NoSquash(PuppetActor actor, Drawables draw)
        {
            bool originalMirror = actor.MirrorForCheck;
            float duration = ReadFloat(actor, "turnSeconds");
            Check(duration > 0, "turn sampling duration is positive");
            foreach (float scale in new[] { .8f, 1f, 1.25f })
            {
                actor.SetScale(scale);
                foreach (float progress in new[] { 0f, .25f, .5f, .75f, 1f })
                {
                    actor.SetMirror(!actor.MirrorForCheck);
                    // Hit the real Update's turn midpoint even when an editor tick is zero or longer than the turn.
                    WriteFloat(actor, "turnTime", duration * progress - Time.unscaledDeltaTime);
                    Tick(actor, draw);
                    Near(ReadFloat(actor, "turnTime"), duration * progress, "requested turn instant was sampled");
                    Near(actor.Root.localScale.x, scale, "turn preserves horizontal scale");
                    Near(actor.Root.localScale.y, scale, "turn preserves vertical scale");
                    Near(actor.Root.localScale.z, scale, "turn preserves depth scale");
                    Check(actor.BaseScale == Vector3.one * scale, "turn preserves configured base scale");
                    Check(draw.Current.rectTransform.localScale == Vector3.one && draw.Fade.rectTransform.localScale == Vector3.one,
                        "frame children do not apply secondary squash");
                }
            }
            actor.SetScale(1f);
            actor.SetMirror(originalMirror);
            Tick(actor, draw);
        }

        static void Suppression(PuppetActor actor, Drawables draw, string gender)
        {
            foreach (var motion in new[] { PuppetActor.Motion.Walk, PuppetActor.Motion.Run, PuppetActor.Motion.Idle })
            {
                SwitchMotion(actor, draw, motion);
                draw.Suppressed = true;
                actor.SetRenderSuppressed(true);
                Renderers(actor, draw);
                foreach (string view in Views)
                {
                    ChangeArt(actor, draw, Load(gender, view));
                    actor.SetMirror(!actor.MirrorForCheck);
                    Tick(actor, draw);
                    if (motion != PuppetActor.Motion.Idle) Move(actor, draw, Cycle(motion) / 19f, Cycle(motion));
                }
                float phase = actor.FramePhaseForCheck, stride = actor.StrideDistanceForCheck;
                int frame = actor.FrameIndexForCheck;
                draw.Suppressed = false;
                actor.SetRenderSuppressed(false);
                Renderers(actor, draw);
                Continuous(actor, phase, stride, frame, "unsuppression");
                Tick(actor, draw);
                Continuous(actor, phase, stride, frame, "unsuppression after Update");
                Check(draw.Current.texture == (motion == PuppetActor.Motion.Idle ? actor.View : actor.WalkingSheet),
                    "unsuppression restores current direction and motion artwork");
            }
        }

        static float Cycle(PuppetActor.Motion motion) => motion == PuppetActor.Motion.Run ? WalkCycle * RunCycleFactor : WalkCycle;
        static void Continuous(PuppetActor actor, float phase, float stride, int frame, string label)
        {
            SamePhase(actor, phase, label + " phase");
            Near(actor.StrideDistanceForCheck, stride, label + " distance");
            Check(actor.FrameIndexForCheck == frame, label + " frame unchanged");
        }
        static void SamePhase(PuppetActor actor, float phase, string label)
        {
            float actual = actor.FramePhaseForCheck;
            Check(actual >= 0 && actual < 1, label + " normalized");
            float error = Mathf.Abs(actual - phase);
            Check(Mathf.Min(error, 1f - error) <= Epsilon, label + " expected=" + Number(phase) + " actual=" + Number(actual));
        }
        static void SameUV(Rect actual, Rect expected, string label)
        {
            Near(actual.x, expected.x, label + " x"); Near(actual.y, expected.y, label + " y");
            Near(actual.width, expected.width, label + " width"); Near(actual.height, expected.height, label + " height");
        }
        static void Invoke(PuppetActor actor, string method, params object[] args)
        {
            var target = typeof(PuppetActor).GetMethod(method, Private);
            if (target == null) throw new MissingMethodException(typeof(PuppetActor).FullName, method);
            target.Invoke(actor, args);
        }
        static FieldInfo Clock(string name) => typeof(PuppetActor).GetField(name, Private) ?? throw new MissingFieldException(typeof(PuppetActor).FullName, name);
        static float ReadFloat(PuppetActor actor, string field) => (float)Clock(field).GetValue(actor);
        static void WriteFloat(PuppetActor actor, string field, float value) => Clock(field).SetValue(actor, value);
        static string Number(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);
        static void Near(float actual, float expected, string label, float tolerance = Epsilon) =>
            Check(Mathf.Abs(actual - expected) <= tolerance, label + " expected=" + Number(expected) + " actual=" + Number(actual));
        static void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException(context + ": " + message);
        }
        static void Fail(string label, Exception error)
        {
            failures++;
            report.AppendLine("FAIL " + label + "\n" + error);
            Debug.LogException(error);
        }
        static void Safely(string label, Action action)
        {
            context = label;
            try { action(); }
            catch (Exception error) { Fail(label, error); }
        }
    }
}
