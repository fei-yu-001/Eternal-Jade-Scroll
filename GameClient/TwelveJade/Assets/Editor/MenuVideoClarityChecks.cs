using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using TwelveJade.Core;
using TwelveJade.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

namespace TwelveJade.Editor
{
    // Graphical, isolated Editor only; no -batchmode/-nographics/-quit:
    // -executeMethod TwelveJade.Editor.MenuVideoClarityChecks.Run
    // EJS_MENU_VIDEO_SOURCE_PROJECT: absolute original Unity project (containing Assets).
    // EJS_MENU_VIDEO_EVIDENCE: new/empty absolute directory outside both projects and real saves.
    // EJS_MENU_VIDEO_SHA: exactly 64 hexadecimal characters, for the delivered video bytes.
    // The project must be under .batch; .menu-video-clarity-checks contains the source project path.
    // Import the same video/meta in source and copy first. This fixture never reimports or saves scenes.
    [InitializeOnLoad]
    public static class MenuVideoClarityChecks
    {
        const string Key = "MenuVideoClarityChecks.State";
        const string ScenePath = "Assets/Scenes/FrontEnd.unity";
        const string VideoPath = "Assets/Resources/Art/Background/menu-loop.mp4";
        const string Marker = ".menu-video-clarity-checks";
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly string[] BoundPaths = {
            "Assets/Editor/MenuVideoClarityChecks.cs", "Assets/Editor/MenuVideoClarityChecks.cs.meta",
            ScenePath, VideoPath, VideoPath + ".meta",
            "Assets/Scripts/Presentation/FrontEndController.cs", "Assets/Scripts/Presentation/FrontEndCharacters.cs",
            "Assets/Scripts/Presentation/FrontEndSettings.cs", "Assets/Scripts/Presentation/MenuVideoBackdrop.cs",
            "Assets/Scripts/Presentation/MemoBackdrop.cs", "Assets/Scripts/Presentation/MenuMemoArt.cs",
            "Assets/Scripts/Presentation/SettingsSanctumArt.cs", "Assets/Scripts/Presentation/UiKit.cs",
            "Assets/Scripts/Presentation/UnityJsonCodec.cs", "Assets/Scripts/Core/SaveRepository.cs",
            "Assets/Scripts/Core/SaveModels.cs", "Assets/Scripts/Presentation/TwelveJade.Presentation.asmdef",
            "Assets/Resources/Art/Background/menu-painting.png", "Assets/Resources/Art/Background/menu-painting.png.meta",
            "ProjectSettings/ProjectSettings.asset", "Packages/manifest.json", "Packages/packages-lock.json"
        };
        [Serializable] sealed class CheckRow { public string name, status, detail; }
        [Serializable] sealed class Stamp { public string path, sha256; }
        [Serializable] sealed class ProtectedSave { public string path; public bool existed; public string[] entries; }
        [Serializable] sealed class SceneRecord { public string path, name; public bool loaded, active, dirty; public string[] roots; }
        [Serializable] sealed class SceneList { public SceneRecord[] scenes; }
        [Serializable] sealed class FrameSample
        {
            public string stage, sha256;
            public long videoFrame;
            public double time, meanByteDelta, litFraction;
            public int unityFrame, decodedFrames, loops, width, height;
        }
        [Serializable] sealed class Shot
        {
            public string file, sha256, page;
            public int width, height, gameViewWidth, gameViewHeight, quality, unityFrame;
            public long bytes;
        }
        [Serializable] sealed class Media
        {
            public string expectedSha256, sourceSha256, copySha256, sourceGuid, copyGuid, importedGuid;
            public uint width, height;
            public ulong frames;
            public double fps, seconds;
            public bool defaultTranscoding, standaloneTranscoding;
        }
        [Serializable] sealed class RunState
        {
            public string phase, source, project, output, scratch, oldSave, oldStartScene, startedUtc;
            public bool hadOldSave, oldBackground, checksStarted, editReady;
            public double started, timeout = 180, restoreStarted;
            public Media media = new Media();
            public SceneRecord[] scenes;
            public List<ProtectedSave> saves = new List<ProtectedSave>();
            public List<Stamp> inputs = new List<Stamp>();
            public List<CheckRow> checks = new List<CheckRow>();
            public List<FrameSample> samples = new List<FrameSample>();
            public List<Shot> screenshots = new List<Shot>();
            public List<int> disabledDevices = new List<int>();
        }
        [Serializable] sealed class Report
        {
            public string status, unityVersion, graphicsDevice, sourceProject, copyProject, scratch, startedUtc, finishedUtc;
            public string scope = "Real FrontEnd video decode, natural loop, slots/menu lifecycle and saved reduceMotion; no character creation or full menu suite.";
            public string visualReview = "PENDING: inspect the two compressed GameView images; dimensions and changing pixels do not prove perceptual sharpness.";
            public int passed, failed, exitCode;
            public Media media;
            public List<CheckRow> checks;
            public List<FrameSample> decodedSamples;
            public List<Shot> screenshots;
            public List<Stamp> inputs;
        }
        static RunState state;
        static FrontEndController host;
        static MenuVideoBackdrop video;
        static RawImage image, still;
        static Canvas canvas;
        static VideoPlayer originalPlayer;
        static bool finalizing, observing, naturalLoop, wrapped, sawFirst;
        static double lastTime = -1, nextFocus;
        static long lastFrame = -1, firstReadyFrame = -1;
        static int baselineLoops, readyEvents, changedPairs, consecutivePairs, observedFrames, postLoopFrames;
        static byte[] previousPixels;

        static MenuVideoClarityChecks()
        {
            string saved = SessionState.GetString(Key, "");
            if (!string.IsNullOrEmpty(saved)) { state = JsonUtility.FromJson<RunState>(saved); if (state != null) Attach(); }
        }

        public static void Run()
        {
            if (state != null || !string.IsNullOrEmpty(SessionState.GetString(Key, "")))
                throw new InvalidOperationException("A clarity run is already pending restoration.");
            if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode ||
                SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null ||
                Environment.GetCommandLineArgs().Any(a => string.Equals(a, "-quit", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Use a separate graphical Editor in Edit mode, without -batchmode/-nographics/-quit.");
            string project = Full(Path.Combine(Application.dataPath, ".."));
            string source = AbsoluteEnvironment("EJS_MENU_VIDEO_SOURCE_PROJECT");
            string output = AbsoluteEnvironment("EJS_MENU_VIDEO_EVIDENCE");
            string expected = Environment.GetEnvironmentVariable("EJS_MENU_VIDEO_SHA") ?? "";
            if (!Regex.IsMatch(expected, @"\A[0-9a-fA-F]{64}\z"))
                throw new InvalidOperationException("EJS_MENU_VIDEO_SHA must contain exactly 64 hexadecimal characters.");
            string marker = Path.Combine(project, Marker);
            RejectLinks(project); RejectLinks(source); RejectLinks(output);
            if (!project.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains(".batch", StringComparer.OrdinalIgnoreCase) ||
                Overlaps(project, source) || !File.Exists(marker) || !SamePath(AbsolutePath(File.ReadAllText(marker).Trim()), source))
                throw new InvalidOperationException("Require a separate .batch project with " + Marker + " naming the absolute source Unity project.");
            if (!File.Exists(Path.Combine(source, ScenePath)) || Overlaps(output, project) || Overlaps(output, source))
                throw new InvalidOperationException("Source must contain FrontEnd; evidence must be outside both projects.");
            if (File.Exists(output) || (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()))
                throw new InvalidOperationException("Evidence directory must be new/empty.");
            var scenes = ReadScenes();
            if (scenes.Any(s => s.dirty)) throw new InvalidOperationException("Dirty edit scenes are refused; no scene is saved or discarded.");
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (scene == null) throw new InvalidOperationException("Real FrontEnd scene is missing.");
            string oldSave = Environment.GetEnvironmentVariable("TWELVEJADE_SAVEDIR");
            var protectedPaths = new[] { Path.Combine(Application.persistentDataPath, "Saves"), oldSave }
                .Where(p => !string.IsNullOrEmpty(p)).Select(Full).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (protectedPaths.Any(p => Overlaps(output, p))) throw new InvalidOperationException("Evidence/scratch overlaps a real save root.");
            var next = new RunState {
                source = source, project = project, output = output, scratch = Full(Path.Combine(output, "scratch-saves")),
                oldSave = oldSave ?? "", hadOldSave = oldSave != null, oldBackground = Application.runInBackground,
                oldStartScene = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) ?? "", scenes = scenes,
                phase = "play", started = EditorApplication.timeSinceStartup, startedUtc = DateTime.UtcNow.ToString("O")
            };
            next.media.expectedSha256 = expected.ToLowerInvariant();
            // Real saves are ONLY enumerated and hashed. Never construct a repository for them.
            foreach (string path in protectedPaths)
                next.saves.Add(new ProtectedSave { path = path, existed = Directory.Exists(path), entries = DirectoryEntries(path) });
            Directory.CreateDirectory(output);
            state = next; finalizing = observing = false; host = null; Attach(); Save();
            try {
                Protect(marker);
                foreach (string path in BoundPaths) Bind(path);
                foreach (var edit in scenes.Where(s => !string.IsNullOrEmpty(s.path))) Protect(Path.Combine(project, edit.path));
                ReadMedia();
                AssertScratch(false);
                Environment.SetEnvironmentVariable("TWELVEJADE_SAVEDIR", state.scratch);
                Application.runInBackground = true;
                EditorSceneManager.playModeStartScene = scene;
                UnityEditor.Search.SearchService.EnumerateDatabases().ToArray();
                Save(); EditorApplication.isPlaying = true;
            } catch (Exception e) { Check("bootstrap", false, e.ToString()); RequestFinish(); }
        }

        static void ReadMedia()
        {
            var media = state.media;
            media.sourceSha256 = Sha(Path.Combine(state.source, VideoPath));
            media.copySha256 = Sha(Path.Combine(state.project, VideoPath));
            Require("source/copy video match supplied SHA", media.sourceSha256 == media.expectedSha256 && media.copySha256 == media.expectedSha256,
                "expected=" + media.expectedSha256 + "; source=" + media.sourceSha256 + "; copy=" + media.copySha256);
            media.sourceGuid = MetaGuid(Path.Combine(state.source, VideoPath + ".meta"));
            media.copyGuid = MetaGuid(Path.Combine(state.project, VideoPath + ".meta"));
            media.importedGuid = AssetDatabase.AssetPathToGUID(VideoPath);
            Require("source/copy/imported video GUID agrees", media.sourceGuid == media.copyGuid && media.copyGuid == media.importedGuid, media.importedGuid);
            var clip = AssetDatabase.LoadAssetAtPath<VideoClip>(VideoPath);
            Require("real imported VideoClip", clip != null);
            media.width = clip.width; media.height = clip.height; media.fps = clip.frameRate; media.frames = clip.frameCount; media.seconds = clip.length;
            Require("imported VideoClip is at least 1920x1080", media.width >= 1920 && media.height >= 1080,
                media.width + "x" + media.height + "; fps=" + media.fps + "; frames=" + media.frames);
            Require("finite nonempty video duration", !double.IsNaN(clip.length) && !double.IsInfinity(clip.length) && clip.length > 0 && clip.frameCount > 1 && clip.frameRate > 0);
            var importer = AssetImporter.GetAtPath(VideoPath) as VideoClipImporter;
            Require("native VideoClipImporter exists", importer != null);
            media.defaultTranscoding = importer.defaultTargetSettings.enableTranscoding;
            var standalone = importer.GetTargetSettings("Standalone");
            media.standaloneTranscoding = standalone != null && standalone.enableTranscoding;
            state.timeout = Math.Max(180, clip.length + 120);
        }

        static void Attach()
        {
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged -= PlayState; EditorApplication.playModeStateChanged += PlayState;
            SceneManager.sceneLoaded -= SceneLoaded; SceneManager.sceneLoaded += SceneLoaded;
            Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog;
            InputSystem.onDeviceChange -= DeviceChanged; InputSystem.onDeviceChange += DeviceChanged;
        }
        static void Tick()
        {
            if (state == null || finalizing) return;
            try {
                if (state.phase == "restore") {
                    if (state.editReady && !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling &&
                        !EditorApplication.isUpdating && EditorApplication.timeSinceStartup - state.restoreStarted > .5) Restore();
                    return;
                }
                if (EditorApplication.timeSinceStartup - state.started > state.timeout) {
                    Check("bounded real playback run", false, "timeout=" + state.timeout); RequestFinish(); return;
                }
                EditorApplication.QueuePlayerLoopUpdate();
                if (EditorApplication.timeSinceStartup >= nextFocus) { FocusGameView(); nextFocus = EditorApplication.timeSinceStartup + .5; }
                if (EditorApplication.isPlaying && !EditorApplication.isCompiling && host == null) StartRuntime();
            } catch (Exception e) { Check("editor runner", false, e.ToString()); RequestFinish(); }
        }
        static void PlayState(PlayModeStateChange change)
        {
            if (state == null) return;
            if (change == PlayModeStateChange.ExitingPlayMode && state.phase != "restore") {
                Check("complete runtime sequence", false, "Play Mode ended early."); state.phase = "restore";
            }
            if (change == PlayModeStateChange.EnteredEditMode && state.phase == "restore") {
                state.editReady = true; state.restoreStarted = EditorApplication.timeSinceStartup; Save();
            }
        }
        static void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (state == null || state.phase != "play" || scene.path != ScenePath) return;
            try { StartRuntime(); } catch (Exception e) { Check("cold-start observation", false, e.ToString()); RequestFinish(); }
        }
        static void StartRuntime()
        {
            if (host != null) return;
            var candidate = UnityEngine.Object.FindAnyObjectByType<FrontEndController>();
            if (candidate == null || candidate.Repository == null) return;
            host = candidate;
            Require("runtime domain remained stable", !state.checksStarted, "No retry or scratch reseeding after a reload.");
            state.checksStarted = true; Save();
            foreach (var device in InputSystem.devices.ToArray()) DisableNative(device);
            AssertScratch(true);
            video = Field<MenuVideoBackdrop>(host, "menuVideo"); image = video != null ? video.GetComponent<RawImage>() : null;
            Require("real menu backdrop", video != null && image != null && host.CurrentPage == "menu");
            canvas = image.GetComponentInParent<Canvas>().rootCanvas;
            still = canvas.GetComponentsInChildren<RawImage>(true).Single(r => r.name == "Artwork");
            Require("original overlay canvas", canvas.renderMode == RenderMode.ScreenSpaceOverlay);
            Require("fresh scratch starts with motion enabled and empty slots", !Settings.reduceMotion && host.Repository.ReadAll().All(s => s.State == SlotState.Empty));
            originalPlayer = Player;
            Require("observe original cold player before first decode", originalPlayer != null && video.DecodedFrames == 0 && !video.HasDecodedFrame && !image.enabled,
                "state=" + video.State + "; decoded=" + video.DecodedFrames);
            Require("cold-start painting retained", still.enabled && still.texture == Resources.Load<Texture2D>("Art/Background/menu-painting") && !image.raycastTarget);
            baselineLoops = video.CompletedLoops; readyEvents = changedPairs = consecutivePairs = observedFrames = postLoopFrames = 0;
            firstReadyFrame = lastFrame = -1; lastTime = -1; previousPixels = null; wrapped = sawFirst = false;
            originalPlayer.frameReady += FrameReady;
            observing = naturalLoop = true;
            var pump = new GameObject("Menu video clarity verification pump").AddComponent<MenuVideoClarityPump>();
            pump.StartCoroutine(Observe()); pump.StartCoroutine(Guard(Checks()));
        }
        static void FrameReady(VideoPlayer source, long frame)
        {
            if (source != originalPlayer || !observing) return;
            if (readyEvents++ == 0) firstReadyFrame = frame;
        }
        static IEnumerator Guard(IEnumerator work)
        {
            var stack = new Stack<IEnumerator>(); stack.Push(work);
            while (stack.Count > 0 && state != null && state.phase == "play") {
                object next = null; bool moved = false; Exception failure = null;
                try { moved = stack.Peek().MoveNext(); if (moved) next = stack.Peek().Current; }
                catch (Exception e) { failure = e; }
                if (failure != null) { Check("runtime exception", false, failure.ToString()); RequestFinish(); yield break; }
                if (!moved) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                if (next is IEnumerator nested) stack.Push(nested); else yield return next;
            }
            RequestFinish();
        }

        static IEnumerator Observe()
        {
            while (state != null && state.phase == "play" && observing) {
                yield return new WaitForEndOfFrame();
                if (state == null || state.phase != "play" || !observing) yield break;
                try { ObserveFrame(); }
                catch (Exception e) { Check("continuous playback guard", false, e.ToString()); RequestFinish(); yield break; }
            }
        }
        static void ObserveFrame()
        {
            if (video == null || !string.IsNullOrEmpty(video.Failure) || video.State == MenuVideoBackdrop.PlaybackState.StaticFallback)
                throw new InvalidOperationException("Unexpected video fallback: " + (video == null ? "missing backdrop" : video.Failure));
            if (image.enabled && image.color.a > 0 && (!video.HasDecodedFrame || image.texture == null))
                throw new InvalidOperationException("Video exposed before a decoded texture was ready.");
            if (!naturalLoop) return;
            var player = Player;
            if (player != originalPlayer || host.CurrentPage != "menu" || Settings.reduceMotion)
                throw new InvalidOperationException("Cold playback was replaced or interrupted before the natural loop.");
            if (!video.HasDecodedFrame || (!sawFirst && (!image.enabled || image.color.a <= 0))) return;
            if (player.texture == null || image.texture != player.texture || !player.isPrepared || !player.isPlaying ||
                !player.isLooping || !Mathf.Approximately(player.playbackSpeed, 1) ||
                player.texture.width != state.media.width || player.texture.height != state.media.height)
                throw new InvalidOperationException("Decoded texture/player contract changed during natural playback.");
            if (lastTime >= 0 && player.time < lastTime - .2) wrapped = true;
            lastTime = player.time;
            if (player.frame == lastFrame) return;
            observedFrames++;
            if (wrapped && video.CompletedLoops > baselineLoops) postLoopFrames++;
            // Sample the first actually presented texture after LateUpdate, never an unuploaded frameReady texture.
            // Keep collecting adjacent decoded frames until there are several real pixel changes.
            if (!sawFirst || changedPairs < 3 || consecutivePairs < 2) {
                var sample = Sample(player, sawFirst ? "consecutive-decode" : "first-presented-decode", out var pixels);
                if (!sawFirst) {
                    Require("first presented decoded frame is not black", readyEvents > 0 && firstReadyFrame >= 0 && sample.litFraction > .05,
                        "firstReady=" + firstReadyFrame + "; presented=" + player.frame + "; lit=" + sample.litFraction);
                    sawFirst = true;
                }
                if (previousPixels != null) {
                    sample.meanByteDelta = MeanDelta(previousPixels, pixels);
                    if (player.frame > lastFrame && sample.meanByteDelta > .01) {
                        changedPairs++;
                        if (player.frame == lastFrame + 1) consecutivePairs++;
                    }
                }
                previousPixels = pixels;
                // A frozen movie cannot grow the report indefinitely while waiting for a timeout.
                if (state.samples.Count < 96) state.samples.Add(sample);
            }
            lastFrame = player.frame;
        }

        static IEnumerator Checks()
        {
            yield return Until(() => sawFirst && changedPairs >= 3 && consecutivePairs >= 2 && Playing(), "actual consecutive decoded frames change", 20);
            Require("decoder and visible RawImage retain the imported clip dimensions", Player.clip == AssetDatabase.LoadAssetAtPath<VideoClip>(VideoPath) &&
                Player.texture == image.texture && Player.texture.width == state.media.width && Player.texture.height == state.media.height &&
                Player.texture.width >= 1920 && Player.texture.height >= 1080,
                "decoded=" + Player.texture.width + "x" + Player.texture.height + "; changedPairs=" + changedPairs + "; adjacentChangedPairs=" + consecutivePairs);
            yield return Capture("01-menu-decoded.jpg");
            yield return Until(() => wrapped && video.CompletedLoops > baselineLoops && postLoopFrames >= 3 && Playing(),
                "natural loop completes and continues decoding", state.media.seconds + 25);
            Require("natural loop without fallback or player replacement", Player == originalPlayer && string.IsNullOrEmpty(video.Failure) &&
                readyEvents > 3 && observedFrames > 3, "loops=" + (video.CompletedLoops - baselineLoops) + "; frameReady=" + readyEvents +
                "; observed=" + observedFrames + "; postLoop=" + postLoopFrames + "; retained-still boundary fade is allowed");
            state.samples.Add(Sample(Player, "after-natural-loop", out _));
            naturalLoop = false;

            var beforeSlots = Player;
            yield return Click("Menu primary");
            yield return Until(() => host.CurrentPage == "slots" && Stopped(), "slots stops and releases the real video", 5);
            int stoppedFrames = video.DecodedFrames;
            yield return new WaitForSecondsRealtime(.7f);
            Require("slots stays stopped after transition", Stopped() && beforeSlots == null && video.DecodedFrames == stoppedFrames &&
                host.Repository.ReadAll().All(s => s.State == SlotState.Empty));
            yield return Click("Slots back");
            yield return Until(() => host.CurrentPage == "menu" && Playing(), "return to menu resumes actual video", 25);
            yield return MovingPair("menu-return");

            yield return SetReducedMotion(true);
            yield return Until(() => host.CurrentPage == "menu" && Settings.reduceMotion && Stopped(), "reduceMotion returns to a static menu", 5);
            Require("reduced-motion uses retained artwork", still.enabled && still.texture == Resources.Load<Texture2D>("Art/Background/menu-painting") &&
                still.GetComponentInParent<CanvasGroup>().alpha > .99f && still.color.a > .99f);
            stoppedFrames = video.DecodedFrames;
            var stillPosition = still.rectTransform.anchoredPosition;
            var stillScale = still.rectTransform.localScale;
            var stillUv = still.uvRect;
            yield return new WaitForSecondsRealtime(.7f);
            Require("reduced-motion remains static and decoder-free", Stopped() && video.DecodedFrames == stoppedFrames &&
                still.rectTransform.anchoredPosition == stillPosition && still.rectTransform.localScale == stillScale && still.uvRect == stillUv);
            yield return Capture("02-menu-reduced-motion.jpg");
            yield return SetReducedMotion(false);
            yield return Until(() => host.CurrentPage == "menu" && !Settings.reduceMotion && Playing(), "restoring motion resumes decoded video", 25);
            yield return MovingPair("motion-restored");
            AssertScratch(true);
            Require("no character saves created", host.Repository.ReadAll().All(s => s.State == SlotState.Empty) &&
                !Directory.EnumerateFiles(state.scratch, "slot-*", SearchOption.AllDirectories).Any());
            Require("two compressed screenshots delivered", state.screenshots.Count == 2);
            observing = false;
        }
        static IEnumerator MovingPair(string label)
        {
            var player = Player;
            var first = Sample(player, label + "-a", out var a);
            yield return Until(() => Playing() && Player == player && Player.frame != first.videoFrame &&
                Math.Abs(Player.time - first.time) >= .25, label + " advances after restart", 5);
            var second = Sample(player, label + "-b", out var b);
            second.meanByteDelta = MeanDelta(a, b);
            Require(label + " decoded pixels really change", first.sha256 != second.sha256 && second.meanByteDelta > .01 &&
                first.litFraction > .05 && second.litFraction > .05,
                "frames=" + first.videoFrame + "/" + second.videoFrame + "; delta=" + second.meanByteDelta);
            state.samples.Add(first); state.samples.Add(second);
        }
        static IEnumerator SetReducedMotion(bool value)
        {
            AssertScratch(true);
            Require("motion toggle starts from opposite value", Settings.reduceMotion != value);
            yield return Click("Menu settings");
            yield return Until(() => host.CurrentPage == "settings", "real settings page opens");
            yield return Click("Settings reduced motion");
            yield return Click("Settings save");
            yield return Until(() => Settings.reduceMotion == value, "motion setting applied by real save button");
            var saved = host.Repository.LoadSettings(out var loadState);
            Require("motion preference persisted only in scratch", loadState == SettingsLoadState.Ready && saved.reduceMotion == value);
            yield return Click("Settings back");
        }
        static IEnumerator Click(string name)
        {
            yield return Until(() => Field<CanvasGroup>(host, "contentFade").alpha > .99f, name + " page visible");
            // Keep each real pointer activation on a different Unity frame.
            yield return null;
            var button = canvas.GetComponentsInChildren<Button>().SingleOrDefault(b => b.name == name && b.isActiveAndEnabled);
            Require(name + " is interactable", button != null && button.IsInteractable());
            AssertScratch(true); Canvas.ForceUpdateCanvases();
            var rect = (RectTransform)button.transform;
            var point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            var pointer = new PointerEventData(EventSystem.current) { position = point, button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            Require(name + " real pointer raycast", hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == button,
                "top=" + (hits.Count == 0 ? "none" : hits[0].gameObject.name));
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            yield return null;
            Require(name + " remains the live control", button != null && button.IsActive() && button.IsInteractable());
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
            if (button != null) ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerExitHandler);
            yield return null;
        }
        static IEnumerator Until(Func<bool> ready, string name, double seconds = 8)
        {
            double end = Time.realtimeSinceStartupAsDouble + seconds;
            while (!ready() && Time.realtimeSinceStartupAsDouble < end) yield return null;
            Require(name, ready());
        }
        static VideoPlayer Player => video == null ? null : Field<VideoPlayer>(video, "player");
        static UserSettings Settings => Field<UserSettings>(host, "settings");
        static bool Stopped() => video.State == MenuVideoBackdrop.PlaybackState.Stopped && Player == null &&
            !image.enabled && image.texture == null && video.GetComponents<VideoPlayer>().Length == 0;
        static bool Playing() => Player != null && Player.isPrepared && Player.isPlaying && video.HasDecodedFrame &&
            video.State == MenuVideoBackdrop.PlaybackState.Playing && image.enabled && image.color.a > .95f &&
            image.texture == Player.texture && Player.texture != null && Player.texture.width == state.media.width && Player.texture.height == state.media.height;

        static FrameSample Sample(VideoPlayer player, string stage, out byte[] pixels)
        {
            if (player == null || player.texture == null) throw new InvalidOperationException("Missing decoded pixels at " + stage);
            var small = ReadTexture(player.texture, 256, 144);
            try {
                pixels = small.GetRawTextureData();
                int lit = 0;
                for (int i = 0; i < pixels.Length; i += 3)
                    if (Math.Max(pixels[i], Math.Max(pixels[i + 1], pixels[i + 2])) > 16) lit++;
                return new FrameSample {
                    stage = stage, videoFrame = player.frame, time = player.time, unityFrame = Time.frameCount,
                    width = player.texture.width, height = player.texture.height, decodedFrames = video.DecodedFrames,
                    loops = video.CompletedLoops, sha256 = Hash(pixels), litFraction = lit / (double)(pixels.Length / 3)
                };
            } finally { UnityEngine.Object.Destroy(small); }
        }
        static double MeanDelta(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return double.PositiveInfinity;
            long total = 0; for (int i = 0; i < a.Length; i++) total += Math.Abs(a[i] - b[i]);
            return total / (double)a.Length;
        }
        static Texture2D ReadTexture(Texture source, int width, int height)
        {
            var previous = RenderTexture.active;
            var temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            Texture2D result = null;
            try {
                Graphics.Blit(source, temporary); RenderTexture.active = temporary;
                result = new Texture2D(width, height, TextureFormat.RGB24, false);
                result.ReadPixels(new Rect(0, 0, width, height), 0, 0); result.Apply(); return result;
            } catch { if (result != null) UnityEngine.Object.Destroy(result); throw; }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(temporary); }
        }
        static IEnumerator Capture(string file)
        {
            FocusGameView(); Canvas.ForceUpdateCanvases();
            yield return new WaitForEndOfFrame();
            Texture2D screen = null, small = null;
            try {
                screen = ScreenCapture.CaptureScreenshotAsTexture();
                Require(file + " is actual GameView capture", screen != null && screen.width >= 640 && screen.height >= 360);
                float scale = Math.Min(1, 1440f / Math.Max(screen.width, screen.height));
                int width = Mathf.RoundToInt(screen.width * scale), height = Mathf.RoundToInt(screen.height * scale);
                int quality = 86; byte[] jpeg;
                small = ReadTexture(screen, width, height);
                var colors = small.GetPixels32(); int lit = 0; var bins = new HashSet<int>();
                for (int i = 0; i < colors.Length; i += Math.Max(1, colors.Length / 12000)) {
                    var pixel = colors[i];
                    if (Math.Max(pixel.r, Math.Max(pixel.g, pixel.b)) > 18) lit++;
                    bins.Add((pixel.r >> 4) << 8 | (pixel.g >> 4) << 4 | (pixel.b >> 4));
                }
                Require(file + " rendered GameView is not blank", lit > 120 && bins.Count >= 24,
                    "litSamples=" + lit + "; coarseColors=" + bins.Count);
                jpeg = small.EncodeToJPG(quality);
                while (jpeg.Length > 200 * 1024 && quality > 46) { quality -= 8; jpeg = small.EncodeToJPG(quality); }
                // Preserve the whole frame; reduce size only if JPEG quality alone misses the review budget.
                while (jpeg.Length > 200 * 1024 && width > 960) {
                    UnityEngine.Object.Destroy(small);
                    width = Mathf.RoundToInt(width * .9f); height = Mathf.RoundToInt(height * .9f);
                    small = ReadTexture(screen, width, height); jpeg = small.EncodeToJPG(quality);
                }
                Require(file + " compressed preview budget", Math.Max(width, height) <= 1440 && jpeg.Length <= 200 * 1024,
                    width + "x" + height + "; bytes=" + jpeg.Length);
                File.WriteAllBytes(Path.Combine(state.output, file), jpeg);
                state.screenshots.Add(new Shot { file = file, sha256 = Hash(jpeg), page = host.CurrentPage,
                    width = width, height = height, gameViewWidth = screen.width, gameViewHeight = screen.height,
                    quality = quality, bytes = jpeg.Length, unityFrame = Time.frameCount });
                Save();
            } finally { if (screen != null) UnityEngine.Object.Destroy(screen); if (small != null) UnityEngine.Object.Destroy(small); }
        }

        static void RequestFinish()
        {
            if (state == null || state.phase == "restore") return;
            observing = naturalLoop = false;
            if (originalPlayer != null) originalPlayer.frameReady -= FrameReady;
            state.phase = "restore"; state.restoreStarted = EditorApplication.timeSinceStartup;
            state.editReady = !EditorApplication.isPlayingOrWillChangePlaymode; Save();
            if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false;
        }
        static void Restore()
        {
            if (state == null || finalizing) return;
            finalizing = true; bool sceneSafe = false, restored = false;
            try {
                sceneSafe = JsonUtility.ToJson(new SceneList { scenes = ReadScenes() }) == JsonUtility.ToJson(new SceneList { scenes = state.scenes });
                Check("edit scenes preserved", sceneSafe, "Paths, roots, active/loaded/dirty state; no OpenScene/SaveScene calls.");
                EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(state.oldStartScene) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(state.oldStartScene);
                Check("playModeStartScene restored", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) == state.oldStartScene);
                // All Play Mode shutdown callbacks have finished before restoring the real environment.
                Environment.SetEnvironmentVariable("TWELVEJADE_SAVEDIR", state.hadOldSave ? state.oldSave : null);
                Application.runInBackground = state.oldBackground;
                RestoreInput(); restored = true;
                Check("save environment restored", Environment.GetEnvironmentVariable("TWELVEJADE_SAVEDIR") == (state.hadOldSave ? state.oldSave : null));
                foreach (var saved in state.saves)
                    Check("real save root unchanged " + saved.path, saved.existed == Directory.Exists(saved.path) && saved.entries.SequenceEqual(DirectoryEntries(saved.path)),
                        "Read-only file/directory inventory and SHA-256; no real SaveRepository was opened.");
                foreach (var input in state.inputs)
                    Check("bound input unchanged " + input.path, File.Exists(input.path) && Sha(input.path) == input.sha256);
            } catch (Exception e) { Check("restoration and frozen-input audit", false, e.ToString()); }
            finally {
                Environment.SetEnvironmentVariable("TWELVEJADE_SAVEDIR", state.hadOldSave ? state.oldSave : null);
                Application.runInBackground = state.oldBackground;
                InputSystem.onDeviceChange -= DeviceChanged;
                RestoreInput();
                EditorApplication.update -= Tick; EditorApplication.playModeStateChanged -= PlayState;
                SceneManager.sceneLoaded -= SceneLoaded; Application.logMessageReceived -= OnLog;
            }
            bool canExit = sceneSafe && restored && state.scenes.All(s => !string.IsNullOrEmpty(s.path) || s.roots.Length == 0);
            if (!canExit) Check("safe unattended exit", false, "Editor left open: restoration was not proven or an untitled scene contains objects.");
            Save(true);
            int code = state.checks.Any(c => c.status == "FAIL") ? 1 : 0;
            SessionState.EraseString(Key); state = null; host = null;
            if (canExit) EditorApplication.Exit(code); // Only this separately launched acceptance process.
        }
        static void AssertScratch(bool runtime)
        {
            string expected = Full(Path.Combine(state.output, "scratch-saves"));
            if (!SamePath(state.scratch, expected) || !Within(state.scratch, state.output) ||
                Overlaps(state.scratch, state.source) || Overlaps(state.scratch, state.project) || state.saves.Any(s => Overlaps(s.path, state.scratch)))
                throw new InvalidOperationException("Scratch root escaped its exclusive evidence directory.");
            RejectLinks(state.scratch);
            if (runtime && (!SamePath(Environment.GetEnvironmentVariable("TWELVEJADE_SAVEDIR"), expected) || host.Repository == null ||
                !SamePath(Field<string>(host.Repository, "root"), expected)))
                throw new InvalidOperationException("Actual runtime repository/environment is not the exact scratch root.");
        }
        static void DisableNative(InputDevice device)
        {
            if (state == null || state.phase != "play" || device == null || !device.native || !device.enabled) return;
            if (!state.disabledDevices.Contains(device.deviceId)) state.disabledDevices.Add(device.deviceId);
            InputSystem.DisableDevice(device); Save();
        }
        static void DeviceChanged(InputDevice device, InputDeviceChange change)
        {
            if (EditorApplication.isPlaying && (change == InputDeviceChange.Added || change == InputDeviceChange.Reconnected || change == InputDeviceChange.Enabled))
                DisableNative(device);
        }
        static void RestoreInput()
        {
            foreach (var device in InputSystem.devices.ToArray())
                if (state.disabledDevices.Contains(device.deviceId) && device.native && !device.enabled) InputSystem.EnableDevice(device);
        }
        static void FocusGameView()
        {
            var type = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView", true);
            var view = EditorWindow.GetWindow(type); view.Show(); view.Focus(); view.Repaint();
        }
        static T Field<T>(object target, string name)
        {
            var field = target?.GetType().GetField(name, Fields);
            if (field == null) throw new MissingFieldException(target?.GetType().FullName, name);
            return (T)field.GetValue(target);
        }
        static void Bind(string relative)
        {
            string source = Path.Combine(state.source, relative), copy = Path.Combine(state.project, relative);
            RejectLinks(source); RejectLinks(copy);
            Require("source/copy input " + relative, File.Exists(source) && File.Exists(copy) && Sha(source) == Sha(copy));
            Protect(source); Protect(copy);
        }
        static void Protect(string path)
        {
            path = Full(path); RejectLinks(path);
            if (!state.inputs.Any(s => SamePath(s.path, path))) state.inputs.Add(new Stamp { path = path, sha256 = Sha(path) });
        }
        static string MetaGuid(string path)
        {
            var match = Regex.Match(File.ReadAllText(path), @"(?m)^guid:\s*([0-9a-fA-F]{32})\s*$");
            if (!match.Success) throw new InvalidOperationException("Invalid video meta GUID: " + path);
            return match.Groups[1].Value.ToLowerInvariant();
        }
        static string Sha(string path)
        {
            using (var algorithm = SHA256.Create()) using (var stream = File.OpenRead(path))
                return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        static string Hash(byte[] bytes)
        {
            using (var algorithm = SHA256.Create()) return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        static string[] DirectoryEntries(string root)
        {
            RejectLinks(root);
            if (!Directory.Exists(root)) return Array.Empty<string>();
            var entries = new List<string>(); var pending = new Stack<string>(); pending.Push(root);
            while (pending.Count > 0) {
                string directory = pending.Pop();
                foreach (string path in Directory.EnumerateFileSystemEntries(directory)) {
                    RejectLinks(path);
                    if (Directory.Exists(path)) { entries.Add("dir:" + path.Substring(root.Length)); pending.Push(path); }
                    else entries.Add("file:" + path.Substring(root.Length) + ":" + new FileInfo(path).Length + ":" + Sha(path));
                }
            }
            return entries.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        }
        static SceneRecord[] ReadScenes()
        {
            var scenes = new List<SceneRecord>();
            for (int i = 0; i < SceneManager.sceneCount; i++) {
                var scene = SceneManager.GetSceneAt(i);
                scenes.Add(new SceneRecord { path = scene.path, name = scene.name, loaded = scene.isLoaded,
                    active = scene == SceneManager.GetActiveScene(), dirty = scene.isDirty,
                    roots = scene.isLoaded ? scene.GetRootGameObjects().Select(g => g.name).OrderBy(n => n, StringComparer.Ordinal).ToArray() : Array.Empty<string>() });
            }
            return scenes.ToArray();
        }
        static string AbsoluteEnvironment(string name)
        {
            string value = Environment.GetEnvironmentVariable(name);
            try { return AbsolutePath(value); }
            catch (Exception e) { throw new InvalidOperationException("Set an absolute " + name + ".", e); }
        }
        static string AbsolutePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) throw new InvalidOperationException("Absolute path required.");
            string root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root) || root == "\\" || root == "/" || root.EndsWith(":", StringComparison.Ordinal))
                throw new InvalidOperationException("Fully qualified Windows path required.");
            return Full(path);
        }
        static string Full(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        static bool SamePath(string a, string b) => !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) && string.Equals(Full(a), Full(b), StringComparison.OrdinalIgnoreCase);
        static bool Within(string child, string parent) => Full(child).StartsWith(Full(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        static bool Overlaps(string a, string b) => SamePath(a, b) || Within(a, b) || Within(b, a);
        static void RejectLinks(string path)
        {
            string next = Path.GetFullPath(path);
            while (!string.IsNullOrEmpty(next)) {
                if ((File.Exists(next) || Directory.Exists(next)) && (File.GetAttributes(next) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Isolation paths must not traverse a junction/symlink: " + next);
                next = Path.GetDirectoryName(next);
            }
        }
        static void Require(string name, bool pass, string detail = "")
        {
            Check(name, pass, detail); if (!pass) throw new InvalidOperationException(name + ": " + detail);
        }
        static void Check(string name, bool pass, string detail = "")
        {
            if (state == null) return;
            state.checks.Add(new CheckRow { name = name, status = pass ? "PASS" : "FAIL", detail = detail }); Save();
        }
        static void OnLog(string message, string trace, LogType type)
        {
            if (state == null || finalizing) return;
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert ||
                (type == LogType.Warning && message.StartsWith("[MenuVideoBackdrop] Static fallback:", StringComparison.Ordinal)))
                Check("Unity " + type, false, message + "\n" + trace);
        }
        static void Save(bool complete = false)
        {
            SessionState.SetString(Key, JsonUtility.ToJson(state));
            int failed = state.checks.Count(c => c.status == "FAIL");
            var report = new Report {
                status = complete ? (failed == 0 ? "PASS" : "FAIL") : "RUNNING", exitCode = complete ? (failed == 0 ? 0 : 1) : -1,
                unityVersion = Application.unityVersion, graphicsDevice = SystemInfo.graphicsDeviceType.ToString(),
                sourceProject = state.source, copyProject = state.project, scratch = state.scratch,
                startedUtc = state.startedUtc, finishedUtc = complete ? DateTime.UtcNow.ToString("O") : "",
                passed = state.checks.Count(c => c.status == "PASS"), failed = failed, media = state.media,
                checks = state.checks, decodedSamples = state.samples, screenshots = state.screenshots, inputs = state.inputs
            };
            File.WriteAllText(Path.Combine(state.output, "report.json"), JsonUtility.ToJson(report, true));
        }
    }
    internal sealed class MenuVideoClarityPump : MonoBehaviour { }
}
