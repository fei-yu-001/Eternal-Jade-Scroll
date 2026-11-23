using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using TMPro;
using TwelveJade.Core;
using TwelveJade.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Video;

namespace TwelveJade.Editor
{
    /// <summary>Run ONLY in the map owner's isolated Unity project, with graphics/video decoding enabled.</summary>
    [InitializeOnLoad]
    public static class MenuVideoShot
    {
        const string Key = "MenuVideoShot.Active";
        const string Asset = "Assets/Resources/Art/Background/menu-loop.mp4";
        const string ClipGuid = "240ec81ead1541c59f852f433c9e960e";
        const string ExpectedSha = "f971fd3400368a8561ea14b9dfc754f698ec27060a01ab01f4ebf5d0d130bd16";
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly List<Row> rows = new List<Row>();
        static string output, sourceProject;
        static readonly string[] InputPaths = { Asset, Asset + ".meta", "Assets/Resources/Art/Background/menu-painting.png",
            "Assets/Resources/Art/Background/memo-terrace.png", "Assets/Resources/Art/Background/memo-terrace.png.meta",
            "Assets/Scripts/Presentation/MemoBackdrop.cs", "Assets/Scripts/Presentation/MemoBackdrop.cs.meta",
            "Assets/Scripts/Presentation/MemoScrollOrnament.cs", "Assets/Scripts/Presentation/MemoScrollOrnament.cs.meta",
            "Assets/Scripts/Presentation/FrontEndController.cs", "Assets/Scripts/Presentation/MenuVideoBackdrop.cs",
            "Assets/Scripts/Presentation/MenuVideoBackdrop.cs.meta", "Assets/Scripts/Presentation/MenuAmbience.cs",
            "Assets/Scripts/Presentation/FrontEndSettings.cs", "Assets/Scripts/Presentation/UiKit.cs",
            "Assets/Scripts/Presentation/MenuOrnament.cs", "Assets/Scripts/Presentation/MenuOrnament.cs.meta",
            "Assets/Scripts/Presentation/MenuButtonGlow.cs", "Assets/Scripts/Presentation/MenuButtonGlow.cs.meta",
            "Assets/Scripts/Presentation/FrontEndCharacters.cs", "Assets/Scripts/Presentation/FrontEndWorld.cs",
            "Assets/Scripts/Presentation/FrontEndMap.cs", "Assets/Scripts/Presentation/CharacterPreset.cs",
            "Assets/Scripts/Presentation/UnityJsonCodec.cs", "Assets/Scripts/Core/SaveModels.cs",
            "Assets/Scripts/Core/SaveRepository.cs", "Assets/Scripts/Core/WorldClock.cs",
            "Assets/Editor/MenuVideoShot.cs", "Assets/Editor/MenuVideoShot.cs.meta", "Assets/Scenes/FrontEnd.unity",
            "Packages/manifest.json", "Packages/packages-lock.json",
            "Assets/Scripts/Presentation/TwelveJade.Presentation.asmdef" };
        static FrontEndController controller;
        static MenuVideoBackdrop video, probe;
        static MemoBackdrop memoBackdrop;
        static RawImage image, still;
        static VideoPlayer player;
        static UserSettings settings;
        static Camera camera;
        static Canvas canvas;
        static MenuVideoCapturePump capturePump;
        static readonly Queue<string> captureQueue = new Queue<string>();
        static bool captureActive;
        static bool CapturePending => captureActive || captureQueue.Count > 0;
        static AudioSource music;
        static AudioClip musicClip;
        static float musicVolume, masterVolume;
        static int phase, lastObservedFrame = -1, screenshots, baselineLoops, stageFrame;
        static double started, stageAt, lastVideoTime = -1;
        static bool initialized, finishing, capturedFirstDecoded, sampledA, sampledB, endFade, startFade, wrapped, guardFailed;
        static string pixelsA;
        static readonly HashSet<int> disabledNativeDevices = new HashSet<int>();
        static string lastScriptedClick = "none";
        static long frameA;
        static Vector3[] reducedPositions;
        static int memoStep;
        static bool memoComplete;
        static int memoMotionProbe;
        static float memoAuraPhase;
        static int[] memoScrollIds;
        static string draftFateBeforeChoice;
        static SaveData[] seededSaves;
        static Dictionary<string, string> deletionBaseline, beforeEmptyCreation;
        const string NewMemoName = "新卷验客";
        static bool FaultProbePhase => phase >= 10 && phase <= 13;
        static string ScratchRoot => Path.GetFullPath(Path.Combine(output, "scratch-saves"));

        [Serializable] sealed class Row { public string name; public bool pass; public string detail; public string category = "assertion"; }
        [Serializable] sealed class Input { public string path, sha256; public long bytes; }
        [Serializable] sealed class Inputs { public string capturedUtc; public List<Input> files = new List<Input>(); }
        [Serializable] sealed class Parity { public string path, sourceSha, fixtureSha; public bool match; }
        [Serializable] sealed class Parities { public string capturedUtc, sourceProject; public List<Parity> files = new List<Parity>(); }
        [Serializable] sealed class Event { public string utc, type, message, stack; }
        [Serializable] sealed class Sample
        {
            public int frame, decodedFrames, loops, phase, memoStep;
            public string lastScriptedClick;
            public double realtime, videoTime;
            public long videoFrame;
            public string state, page;
            public bool decoded, visible, imageEnabled, reduceMotion, musicPlaying;
            public float alpha;
        }
        [Serializable] sealed class Report
        {
            public string status, unityVersion, graphicsDevice, startedUtc, finishedUtc, scope;
            public int exitCode, screenshots, runtimeErrors;
            public List<Row> assertions;
            public string inputManifest = "inputs.json", playbackTrace = "playback-trace.jsonl", engineEvents = "engine-events.jsonl";
        }
        [Serializable] sealed class ImportReport
        {
            public string guid, sha256;
            public bool defaultTranscoding, standaloneTranscoding, importAudio;
            public ulong frames;
            public double fps;
            public uint width, height;
        }

        static MenuVideoShot()
        {
            if (SessionState.GetBool(Key, false)) Attach();
        }

        // Optional -executeMethod TwelveJade.Editor.MenuVideoShot.PrepareMedia -quit.
        // Native importer serialization/readback belongs to the authorized Unity owner.
        public static void PrepareMedia()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("PrepareMedia requires Edit mode.");
            if (!File.Exists(Asset)) throw new FileNotFoundException("Delivered menu video is required.", Asset);
            string before = HashFile(Asset);
            if (before != ExpectedSha) throw new InvalidOperationException("Menu video SHA differs from the delivered bytes.");
            AssetDatabase.ImportAsset(Asset, ImportAssetOptions.ForceSynchronousImport);
            if (AssetDatabase.AssetPathToGUID(Asset) != ClipGuid)
                throw new InvalidOperationException("Menu video GUID differs from the stable meta.");
            var importer = AssetImporter.GetAtPath(Asset) as VideoClipImporter;
            if (importer == null) throw new InvalidOperationException("Native VideoClipImporter was not created.");
            var defaults = importer.defaultTargetSettings;
            defaults.enableTranscoding = false;
            importer.defaultTargetSettings = defaults;
            importer.ClearTargetSettings("Standalone");
            importer.SaveAndReimport();
            ReadMediaImport();
        }

        // Run calls only this readback. The first native import and any meta normalization happen in PrepareMedia.
        static void ReadMediaImport()
        {
            string before = HashFile(Asset);
            if (before != ExpectedSha) throw new InvalidOperationException("Menu video input SHA differs from delivery.");
            VideoClipImporter importer;
            importer = (VideoClipImporter)AssetImporter.GetAtPath(Asset);
            var standalone = importer.GetTargetSettings("Standalone");
            var clip = AssetDatabase.LoadAssetAtPath<VideoClip>(Asset);
            if (clip == null || importer.defaultTargetSettings.enableTranscoding ||
                (standalone != null && standalone.enableTranscoding) || HashFile(Asset) != before ||
                AssetDatabase.AssetPathToGUID(Asset) != ClipGuid)
                throw new InvalidOperationException("Video importer readback failed or bytes/GUID changed.");
            var report = new ImportReport { guid = ClipGuid, sha256 = before,
                defaultTranscoding = importer.defaultTargetSettings.enableTranscoding,
                standaloneTranscoding = standalone != null && standalone.enableTranscoding,
                importAudio = importer.importAudio, frames = clip.frameCount, fps = clip.frameRate,
                width = clip.width, height = clip.height };
            string evidence = Environment.GetEnvironmentVariable("MENU_VIDEO_EVIDENCE");
            if (!string.IsNullOrEmpty(evidence))
            {
                Directory.CreateDirectory(evidence);
                File.WriteAllText(Path.Combine(evidence, "importer-readback.json"), JsonUtility.ToJson(report, true));
                File.Copy(Asset + ".meta", Path.Combine(evidence, "menu-loop.mp4.meta.readback"), true);
            }
            Debug.Log("[MenuVideoShot] Native video import: " + JsonUtility.ToJson(report));
        }

        // Missing media exercises fallback but ALWAYS fails the required real playback gates.
        public static void Run()
        {
            if (Application.isBatchMode) throw new InvalidOperationException("UI acceptance requires a real GameView; launch the isolated editor without -batchmode.");
            Application.runInBackground = true;
            output = Environment.GetEnvironmentVariable("MENU_VIDEO_EVIDENCE");
            if (string.IsNullOrWhiteSpace(output)) throw new InvalidOperationException("MENU_VIDEO_EVIDENCE must be an exclusive absolute directory.");
            if (!Path.IsPathRooted(output)) throw new InvalidOperationException("Evidence directory must be absolute.");
            output = Path.GetFullPath(output);
            sourceProject = Environment.GetEnvironmentVariable("MENU_VIDEO_SOURCE_PROJECT");
            if (string.IsNullOrWhiteSpace(sourceProject) || !Path.IsPathRooted(sourceProject))
                throw new InvalidOperationException("MENU_VIDEO_SOURCE_PROJECT must name the original project for frozen SHA comparison.");
            sourceProject = Path.GetFullPath(sourceProject);
            string fixture = Path.GetFullPath(Directory.GetParent(Application.dataPath).FullName);
            if (string.Equals(sourceProject.TrimEnd('/', '\\'), fixture.TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Run requires a separate isolated project, not the production source project.");
            Directory.CreateDirectory(output);
            if (File.Exists(Path.Combine(output, "runtime-results.json")) || File.Exists(Path.Combine(output, "assertions.jsonl")))
                throw new InvalidOperationException("Use a fresh evidence directory; previous results must not be overwritten.");
            SessionState.SetString(Key + ".Output", output);
            SessionState.SetString(Key + ".SourceProject", sourceProject);
            SessionState.SetString(Key + ".Start", EditorApplication.timeSinceStartup.ToString("R", CultureInfo.InvariantCulture));
            SessionState.SetString(Key + ".Utc", DateTime.UtcNow.ToString("O"));
            SessionState.SetBool(Key, true);
            SessionState.SetString(Key + ".DisabledNative", "");
            Environment.SetEnvironmentVariable("TWELVEJADE_SAVEDIR", Path.Combine(output, "scratch-saves"));
            Attach();
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Acceptance must start in Edit mode.");
                CheckSourceParity("source-fixture-before.json");
                Check("delivered video exists", File.Exists(Asset), Asset);
                if (File.Exists(Asset)) ReadMediaImport();
                CaptureInputs("inputs.json");
                SeedIsolatedMemoSaves();
                // Native Search bootstrap before Play; no editor/runtime error exclusions.
                var databases = UnityEditor.Search.SearchService.EnumerateDatabases().ToArray();
                Check("native Search initialized before Play", databases.Length > 0 && File.Exists("UserSettings/Search.index"), "databases=" + databases.Length);
                if (rows.Any(r => !r.pass && r.name != "delivered video exists")) { Finish(); return; }
                EditorSceneManager.OpenScene("Assets/Scenes/FrontEnd.unity", OpenSceneMode.Single);
                Type viewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView", true);
                var view = EditorWindow.GetWindow(viewType);
                view.position = new Rect(0, 0, 1920, 1080);
                view.Show(); view.Focus(); view.Repaint();
                EditorApplication.isPlaying = true;
            }
            catch (Exception e) { Check("editor bootstrap", false, e.ToString()); Finish(); }
        }

        // Only this acceptance-owned process is isolated. Real raycasts and pointer callbacks below stay unchanged.
        static void IsolateNativeInput()
        {
            disabledNativeDevices.Clear();
            foreach (var token in SessionState.GetString(Key + ".DisabledNative", "").Split(','))
                if (int.TryParse(token, out var id)) disabledNativeDevices.Add(id);
            InputSystem.onDeviceChange -= OnNativeDeviceChange;
            InputSystem.onDeviceChange += OnNativeDeviceChange;
            foreach (var device in InputSystem.devices.ToArray()) DisableNativeInput(device);
        }
        static void DisableNativeInput(InputDevice device)
        {
            if (finishing || device == null || !device.native || !device.enabled) return;
            disabledNativeDevices.Add(device.deviceId);
            SessionState.SetString(Key + ".DisabledNative", string.Join(",", disabledNativeDevices));
            InputSystem.DisableDevice(device);
            File.AppendAllText(Path.Combine(output, "native-input-isolation.log"),
                "disabled id=" + device.deviceId + " layout=" + device.layout + " enabled=" + device.enabled + "\n");
        }
        static void OnNativeDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (change == InputDeviceChange.Added || change == InputDeviceChange.Reconnected || change == InputDeviceChange.Enabled)
                DisableNativeInput(device);
        }
        static void RestoreNativeInput()
        {
            InputSystem.onDeviceChange -= OnNativeDeviceChange;
            foreach (var device in InputSystem.devices.ToArray())
                if (disabledNativeDevices.Contains(device.deviceId) && device.native && !device.enabled)
                    InputSystem.EnableDevice(device);
            disabledNativeDevices.Clear(); SessionState.SetString(Key + ".DisabledNative", "");
        }

        static void Attach()
        {
            output = SessionState.GetString(Key + ".Output", "");
            sourceProject = SessionState.GetString(Key + ".SourceProject", "");
            started = double.Parse(SessionState.GetString(Key + ".Start", "0"), CultureInfo.InvariantCulture);
            rows.Clear();
            IsolateNativeInput();
            string checks = Path.Combine(output, "assertions.jsonl");
            if (File.Exists(checks)) foreach (var line in File.ReadLines(checks)) rows.Add(JsonUtility.FromJson<Row>(line));
            EditorApplication.update -= Pump; EditorApplication.update += Pump;
            Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }
        static void OnLog(string message, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert && type != LogType.Warning) return;
            File.AppendAllText(Path.Combine(output, "engine-events.jsonl"), JsonUtility.ToJson(new Event {
                utc = DateTime.UtcNow.ToString("O"), type = type.ToString(), message = message, stack = stack }) + "\n");
            if (type == LogType.Warning)
            {
                // Real cold-start failures remain failures, even if a later user visit recovers.
                // Only phases 10-13 are injected probes; memo navigation keeps real failures fatal.
                if (!FaultProbePhase && message.StartsWith("[MenuVideoBackdrop] Static fallback:", StringComparison.Ordinal))
                    Add(new Row { name = "real menu video fallback warning", pass = false,
                        detail = message, category = "runtime-error" });
                return;
            }
            Add(new Row { name = "Unity " + type, pass = false, detail = message + "\n" + stack,
                category = stack != null && stack.Contains("UnityEditor.Search") ? "editor-environment" : "runtime-error" });
        }
        static void Add(Row row)
        {
            rows.Add(row);
            File.AppendAllText(Path.Combine(output, "assertions.jsonl"), JsonUtility.ToJson(row) + "\n");
        }
        static void Check(string name, bool pass, string detail = "")
        {
            Add(new Row { name = name, pass = pass, detail = detail });
            Debug.Log("[MenuVideoShot] " + (pass ? "PASS " : "FAIL ") + name + " " + detail);
        }
        static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
        static void SetField(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
        static VideoPlayer Player(MenuVideoBackdrop target) => Field<VideoPlayer>(target, "player");
        static void Next(int value) { phase = value; stageAt = EditorApplication.timeSinceStartup; stageFrame = Time.frameCount; }
        static bool Waited(float seconds) => EditorApplication.timeSinceStartup - stageAt >= seconds;

        static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            if (initialized || finishing || !EditorApplication.isPlaying) return;
            try
            {
                controller = UnityEngine.Object.FindAnyObjectByType<FrontEndController>();
                if (controller != null) InitializeRuntime();
            }
            catch (Exception e) { Check("cold-start observation", false, e.ToString()); Finish(); }
        }

        static void Pump()
        {
            if (finishing) return;
            EditorApplication.QueuePlayerLoopUpdate();
            try
            {
                if (EditorApplication.timeSinceStartup - started > 240) { Check("acceptance timeout", false, "phase=" + phase); Finish(); return; }
                if (!EditorApplication.isPlaying) return;
                if (!initialized)
                {
                    controller = UnityEngine.Object.FindAnyObjectByType<FrontEndController>();
                    if (controller == null) return;
                    InitializeRuntime();
                    return;
                }
                if (Time.frameCount == lastObservedFrame) return;
                lastObservedFrame = Time.frameCount;
                Observe();
                if (CapturePending) return;
                if (!FaultProbePhase && video.State == MenuVideoBackdrop.PlaybackState.StaticFallback)
                {
                    Check("real video must decode", false, video.Failure);
                    Check("real failure falls back to still", StaticVisible(video));
                    Capture("unexpected-static-fallback");
                    Next(10); // Fallback probes continue; real playback still fails.
                }
                switch (phase)
                {
                    case 0: Playback(); break;
                    case 1: ReducedMotion(); break;
                    case 2: RestoredMotion(); break;
                    case 3: AwayFromMenu(); break;
                    case 4: ReturnedToMenu(); break;
                    case 10: MissingResource(); break;
                    case 11: PreparationTimeout(); break;
                    case 12: DecoderError(); break;
                    case 13: Cleanup(); break;
                    case 20: MemoAcceptance(); break;
                }
            }
            catch (Exception e) { Check("acceptance exception", false, e.ToString()); Finish(); }
        }

        static void InitializeRuntime()
        {
            Require("acceptance native input isolated", InputSystem.devices.All(d => !d.native || !d.enabled),
                "physical input is disabled only in this isolated process; real script raycasts/click callbacks retained");
            video = UnityEngine.Object.FindAnyObjectByType<MenuVideoBackdrop>();
            if (video == null) throw new InvalidOperationException("MenuVideoBackdrop not installed.");
            image = video.GetComponent<RawImage>();
            canvas = video.GetComponentInParent<Canvas>().rootCanvas;
            still = canvas.GetComponentsInChildren<RawImage>(true).Single(r => r.name == "Artwork");
            memoBackdrop = canvas.GetComponentInChildren<MemoBackdrop>();
            Require("memo background component installed", memoBackdrop != null);
            AssertIsolatedRepository();
            seededSaves = new[] { controller.Repository.Read(1).Data, controller.Repository.Read(2).Data };
            Require("isolated seeded saves readable", seededSaves.All(d => d != null) &&
                controller.Repository.Read(3).State == SlotState.Empty);
            settings = Field<UserSettings>(controller, "settings");
            camera = Camera.main;
            canvas = video.GetComponentInParent<Canvas>().rootCanvas;
            capturePump = new GameObject("Menu acceptance frame capture").AddComponent<MenuVideoCapturePump>();
            Check("native product overlay canvas retained", canvas.renderMode == RenderMode.ScreenSpaceOverlay);
            // Observe the original Awake-created player. Never toggle settings/Refresh to retry cold start.
            Check("cold startup preserves first attempt", video.State != MenuVideoBackdrop.PlaybackState.StaticFallback &&
                string.IsNullOrEmpty(video.Failure), "state=" + video.State + " failure=" + video.Failure);
            music = Field<AudioSource>(controller, "music");
            musicClip = music.clip; musicVolume = music.volume; masterVolume = AudioListener.volume;
            // sceneLoaded observes this before the first normal frame; fallback Pump never restarts it.
            baselineLoops = video.CompletedLoops;
            Check("hidden while preparing before any decoded frame", !image.enabled && image.texture == null && !video.HasDecodedFrame);
            Check("static painting retained", still.enabled && still.texture == Resources.Load<Texture2D>("Art/Background/menu-painting"));
            Check("video never intercepts pointer", !image.raycastTarget);
            Capture("00-static-before-frame");
            initialized = true;
            Next(0);
            if (Resources.Load<VideoClip>(MenuVideoBackdrop.ResourcePath) == null)
            {
                Check("real playback requires delivered clip", false, "Missing clip is fallback-only; never a playback PASS.");
                Check("missing real clip retains still", StaticVisible(video));
                Next(10);
            }
        }

        static void Observe()
        {
            player = Player(video);
            File.AppendAllText(Path.Combine(output, "playback-trace.jsonl"), JsonUtility.ToJson(new Sample {
                frame = Time.frameCount, phase = phase, memoStep = memoStep, lastScriptedClick = lastScriptedClick,
                realtime = Time.realtimeSinceStartupAsDouble, state = video.State.ToString(),
                page = controller.CurrentPage, decoded = video.HasDecodedFrame, decodedFrames = video.DecodedFrames,
                loops = video.CompletedLoops, videoTime = player == null ? -1 : player.time,
                videoFrame = player == null ? -1 : player.frame, visible = video.IsVisible, imageEnabled = image.enabled,
                alpha = image.color.a, reduceMotion = settings.reduceMotion, musicPlaying = music != null && music.isPlaying }) + "\n");
            if (phase == 0 && controller.CurrentPage != "menu")
                throw new InvalidOperationException("Unexpected page during natural playback: " + controller.CurrentPage +
                    "; frame=" + Time.frameCount + "; last scripted click=" + lastScriptedClick);

            if (!guardFailed && image.enabled && (!video.HasDecodedFrame || image.texture == null || player == null || image.texture != player.texture))
            {
                guardFailed = true;
                Check("no video exposure before decoded texture", false);
            }
        }

        static void Playback()
        {
            if (Waited(40)) { Check("actual playback and natural loop deadline", false); Next(10); return; }
            if (!video.HasDecodedFrame || player == null) return;
            if (!capturedFirstDecoded)
            {
                capturedFirstDecoded = true;
                string first = SaveDecodedFrame("00-first-decoded-texture");
                Check("real first frame and 720p dimensions", player.isPrepared && player.isPlaying && video.DecodedFrames > 0 &&
                    image.texture.width == 1280 && image.texture.height == 720,
                    "First observed decoded texture: frame=" + player.frame + " time=" + player.time + " pixelSHA=" + first);
                Capture("00-first-frame-reveal");
                return;
            }
            if (lastVideoTime >= 0 && player.time < lastVideoTime - 1) wrapped = true;
            lastVideoTime = player.time;
            if (!sampledA && image.color.a >= .99f && player.time >= .8)
            {
                sampledA = true;
                frameA = player.frame;
                pixelsA = SaveDecodedFrame("01-decoded-a");
                Check("soundtrack looping player", player.isLooping && player.audioOutputMode == VideoAudioOutputMode.AudioSource &&
                    player.audioTrackCount > 0 && player.GetTargetAudioSource(0) != null);
                CheckMusic("existing menu music during playback");
                Check("procedural zoom suppressed during video", video.SuppressProceduralMotion && still.transform.localScale == Vector3.one);
                Capture("01-menu-playing");
                return;
            }
            if (sampledA && !sampledB && player.time >= 3)
            {
                sampledB = true;
                string second = SaveDecodedFrame("02-decoded-b");
                Check("actual decoded pixels and frame index change", frameA != player.frame && pixelsA != second,
                    "frameA=" + frameA + " frameB=" + player.frame + " pixelSHA=" + pixelsA + " / " + second);
            }
            if (!endFade && player.time >= player.length - .35 && player.time < player.length && image.color.a < .9f)
            {
                endFade = true;
                Check("loop end fades through still", still.enabled && image.color.a < .9f, "time=" + player.time + " alpha=" + image.color.a);
                Capture("03-loop-end-fade");
                return;
            }
            if (video.CompletedLoops > baselineLoops && !startFade && player.time >= 0 && player.time < .35 && image.color.a < .9f)
            {
                startFade = true;
                Check("loop start fades from still", still.enabled && image.color.a < .9f, "time=" + player.time + " alpha=" + image.color.a);
                Capture("04-loop-start-fade");
                return;
            }
            if (sampledA && sampledB && endFade && startFade && wrapped && video.CompletedLoops > baselineLoops && image.color.a >= .99f)
            {
                Check("real natural loop and continued decoded playback", player.isPlaying && wrapped,
                    "loops=" + (video.CompletedLoops - baselineLoops));
                Check("no first-frame exposure violations", !guardFailed);
                settings.reduceMotion = true; // Verify product polling, without test Refresh().
                Next(1);
            }
        }

        static Vector3[] AmbiencePositions() => canvas.GetComponentsInChildren<Transform>()
            .Where(t => t.name.StartsWith("Mist ") || t.name.StartsWith("Mote ")).Select(t => t.localPosition).ToArray();
        static bool StaticVisible(MenuVideoBackdrop target) => !target.IsVisible &&
            !target.GetComponent<RawImage>().enabled && target.GetComponent<RawImage>().texture == null &&
            still != null && still.enabled && still.texture != null;

        static void ReducedMotion()
        {
            if (Time.frameCount <= stageFrame + 1) return;
            if (reducedPositions == null)
            {
                Check("reduceMotion stops releases and hides video", Player(video) == null && StaticVisible(video) &&
                    video.State == MenuVideoBackdrop.PlaybackState.Stopped && still.transform.localScale == Vector3.one);
                reducedPositions = AmbiencePositions();
                Capture("05-reduced-motion-static");
                return;
            }
            if (!Waited(.6f)) return;
            Check("reduceMotion freezes all procedural movement", reducedPositions.SequenceEqual(AmbiencePositions()));
            CheckMusic("music retained with reduced motion");
            settings.reduceMotion = false;
            Next(2);
        }
        static void RestoredMotion()
        {
            if (Waited(20)) { Check("motion restore playback deadline", false); Next(10); return; }
            if (!video.IsVisible || image.color.a < .99f) return;
            Check("motion setting restores actual decoded video", video.HasDecodedFrame && player.isPlaying);
            Click(ContractButton("Menu settings", "乾坤", "调谐"), true);
            Check("page exit hides video synchronously", controller.CurrentPage == "settings" && StaticVisible(video));
            Next(3);
        }
        static void AwayFromMenu()
        {
            if (!Waited(.4f)) return;
            Check("no video player remains off homepage", Player(video) == null && video.GetComponents<VideoPlayer>().Length == 0 && StaticVisible(video));
            if (!File.Exists(Path.Combine(output, "06-settings-static.png")))
            { Capture("06-settings-static"); return; }
            Click(FindButton("返回"), true);
            Check("return menu still hidden until new decoded frame", controller.CurrentPage == "menu" && !image.enabled && !video.HasDecodedFrame);
            Next(4);
        }
        static void ReturnedToMenu()
        {
            if (Waited(20)) { Check("menu return playback deadline", false); Next(10); return; }
            if (!video.IsVisible || image.color.a < .99f) return;
            Check("menu return restores real playback", video.HasDecodedFrame && player.isPlaying);
            CheckMusic("music retained across page navigation");
            Capture("07-menu-returned");
            memoStep = 0;
            Next(20);
        }


        // Memo parts 1-3. All save writes are limited to a fresh acceptance-owned directory.
        static void SeedIsolatedMemoSaves()
        {
            if (!string.Equals(Path.GetFullPath(Environment.GetEnvironmentVariable("TWELVEJADE_SAVEDIR") ?? ""),
                ScratchRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Acceptance save directory is not isolated.");
            if (Directory.Exists(ScratchRoot) && Directory.EnumerateFileSystemEntries(ScratchRoot).Any())
                throw new InvalidOperationException("scratch-saves must be empty; existing saves will never be reused or deleted.");
            Directory.CreateDirectory(ScratchRoot);
            var presets = Resources.LoadAll<CharacterPreset>("Characters").OrderBy(p => p.id).ToArray();
            if (presets.Length < 2) throw new InvalidOperationException("Two real character presets are required for dossier switching.");
            var repository = new SaveRepository(ScratchRoot, new UnityJsonCodec());
            var a = repository.Create(1, presets[0].id, "听泉客", "male", 0, new[] { "clever", "deft" }, "jadeloft");
            a.coins = 1237; a.worldDay = 3; a.worldMinuteOfDay = 8 * 60 + 17;
            repository.Write(a); // Also creates a genuine backup used by the deletion check.
            var b = repository.Create(2, presets[1].id, "照月客", "female", 0, new[] { "curious", "literate" }, "scholar");
            b.coins = 8563; b.worldDay = 9; b.worldMinuteOfDay = 17 * 60 + 25;
            repository.Write(b);
            Require("memo isolated fixtures created", repository.Read(1).CanLoad && repository.Read(2).CanLoad &&
                repository.Read(3).State == SlotState.Empty, ScratchRoot);
            File.WriteAllText(Path.Combine(output, "memo-save-fixtures.json"), JsonUtility.ToJson(new MemoFixtures {
                root = ScratchRoot, first = repository.Read(1).Data, second = repository.Read(2).Data, emptySlot = 3 }, true));
        }

        [Serializable] sealed class MemoFixtures { public string root; public SaveData first, second; public int emptySlot; }
        [Serializable] sealed class MemoDossier
        {
            public int slot, coins, day, minuteOfDay;
            public string name, fate, wealth, journey, age, realm, sect;
        }
        [Serializable] sealed class MemoGeometry
        {
            public string label;
            public int width, height;
            public Rect screenRect, uv;
            public float sourceAspect;
        }

        static void Require(string name, bool pass, string detail = "")
        {
            Check(name, pass, detail);
            if (!pass) throw new InvalidOperationException("Required acceptance failed: " + name + " " + detail);
        }
        static void AssertIsolatedRepository()
        {
            string actual = Path.GetFullPath(Field<string>(controller.Repository, "root"));
            if (!string.Equals(actual, ScratchRoot, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetFullPath(Environment.GetEnvironmentVariable("TWELVEJADE_SAVEDIR") ?? ""),
                    ScratchRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing to inspect or mutate saves outside scratch-saves.");
        }
        static string Compact(string value) => string.Concat((value ?? "").Where(c => !char.IsWhiteSpace(c)));
        static Transform Named(string name) => canvas.GetComponentsInChildren<Transform>()
            .SingleOrDefault(t => t.gameObject.activeInHierarchy && t.name == name);
        static RectTransform NamedRect(string name)
        {
            var value = Named(name) as RectTransform;
            if (value == null) throw new InvalidOperationException("Missing memo UI contract object: " + name);
            return value;
        }
        static Button ContractButton(string name, params string[] labels)
        {
            var buttons = canvas.GetComponentsInChildren<Button>().Where(b => b.gameObject.activeInHierarchy).ToArray();
            var named = buttons.Where(b => !string.IsNullOrEmpty(name) && b.name == name).ToArray();
            if (named.Length == 1) return named[0];
            var modal = Named("Confirmation");
            if (modal != null) buttons = buttons.Where(b => b.transform.IsChildOf(modal)).ToArray();
            var matches = buttons.Where(b => b.GetComponentsInChildren<TMP_Text>().Any(t =>
                labels.Any(label => Compact(t.text) == Compact(label)))).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Expected one button: " + name + " / " + string.Join("|", labels));
            return matches[0];
        }
        static bool HasButtonLabel(Button button, params string[] choices) => button.GetComponentsInChildren<TMP_Text>()
            .Any(t => choices.Any(s => Compact(t.text) == Compact(s)));
        static string VisibleText(Transform root) => string.Join("\n", root.GetComponentsInChildren<TMP_Text>()
            .Where(t => t.isActiveAndEnabled && t.color.a > .01f).Select(t => t.text));
        static Rect ScreenRect(RectTransform rect)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            var points = corners.Select(c => RectTransformUtility.WorldToScreenPoint(null, c)).ToArray();
            return Rect.MinMaxRect(points.Min(p => p.x), points.Min(p => p.y), points.Max(p => p.x), points.Max(p => p.y));
        }
        static bool DrawnBefore(Transform first, Transform second)
        {
            var a = new List<Transform>(); var b = new List<Transform>();
            for (var t = first; t != null; t = t.parent) a.Insert(0, t);
            for (var t = second; t != null; t = t.parent) b.Insert(0, t);
            int common = 0;
            while (common < a.Count && common < b.Count && a[common] == b[common]) common++;
            return common > 0 && common < a.Count && common < b.Count &&
                a[common].GetSiblingIndex() < b[common].GetSiblingIndex();
        }
        static bool OnScreen(Rect rect) => rect.width > 0 && rect.height > 0 && rect.xMin >= -2 && rect.yMin >= -2 &&
            rect.xMax <= Screen.width + 2 && rect.yMax <= Screen.height + 2;
        static void CheckCover(RawImage art, string label)
        {
            Canvas.ForceUpdateCanvases();
            Rect rect = ScreenRect(art.rectTransform), uv = art.uvRect;
            Texture texture = art.texture;
            Require(label + " texture exists", texture != null);
            float sourceAspect = (float)texture.width / texture.height;
            float shownAspect = sourceAspect * uv.width / uv.height;
            bool covering = rect.xMin <= 2 && rect.yMin <= 2 && rect.xMax >= Screen.width - 2 && rect.yMax >= Screen.height - 2;
            bool centered = Vector2.Distance(rect.center, new Vector2(Screen.width, Screen.height) * .5f) <= 3 &&
                Vector2.Distance(uv.center, new Vector2(.5f, .5f)) < .005f;
            bool undistorted = uv.width > 0 && uv.height > 0 && uv.xMin >= -.001f && uv.yMin >= -.001f &&
                uv.xMax <= 1.001f && uv.yMax <= 1.001f && Mathf.Abs(rect.width / rect.height / shownAspect - 1) < .01f;
            Check(label, covering && centered && undistorted, "screen=" + Screen.width + "x" + Screen.height + " rect=" + rect + " uv=" + uv);
            File.AppendAllText(Path.Combine(output, "memo-geometry.jsonl"), JsonUtility.ToJson(new MemoGeometry {
                label = label, width = Screen.width, height = Screen.height, screenRect = rect, uv = uv, sourceAspect = sourceAspect }) + "\n");
        }
        static void CheckPosterCover(PosterCardView poster, string label)
        {
            Canvas.ForceUpdateCanvases();
            var art = poster.Artwork; Texture texture = art.texture;
            Require(label + " texture exists", texture != null);
            Rect shown = ScreenRect(art.rectTransform), frame = ScreenRect((RectTransform)poster.transform), uv = art.uvRect;
            float sourceAspect = (float)texture.width / texture.height;
            if (poster.IsCutout)
            {
                // Full-body cutouts fit inside the card; cropping or stretching would lose the figure.
                Check(label + " cutout full UV", Mathf.Abs(uv.x) < .001f && Mathf.Abs(uv.y) < .001f &&
                    Mathf.Abs(uv.width - 1) < .001f && Mathf.Abs(uv.height - 1) < .001f, "uv=" + uv);
                Check(label + " cutout preserves aspect", shown.width > 0 && shown.height > 0 &&
                    Mathf.Abs(shown.width / shown.height / sourceAspect - 1) < .01f,
                    "art=" + shown + " sourceAspect=" + sourceAspect);
                Check(label + " cutout fully contained", art.isActiveAndEnabled && OnScreen(frame) && OnScreen(shown) &&
                    shown.xMin >= frame.xMin - 1 && shown.xMax <= frame.xMax + 1 &&
                    shown.yMin >= frame.yMin - 1 && shown.yMax <= frame.yMax + 1,
                    "card=" + frame + " art=" + shown);
                Check(label + " cutout has no frame", poster.Frame == null &&
                    poster.transform.Find("Memo fine mounting") == null);
            }
            else
            {
                float shownAspect = sourceAspect * uv.width / uv.height;
                var local = art.rectTransform;
                bool fillsCard = local.anchorMin == Vector2.zero && local.anchorMax == Vector2.one &&
                    Vector2.Distance(local.offsetMin, new Vector2(6, 6)) < .01f &&
                    Vector2.Distance(local.offsetMax, new Vector2(-6, -6)) < .01f &&
                    shown.xMin >= frame.xMin && shown.xMax <= frame.xMax && shown.yMin >= frame.yMin && shown.yMax <= frame.yMax;
                bool centered = Vector2.Distance(shown.center, frame.center) <= 1f &&
                    Vector2.Distance(uv.center, new Vector2(.5f, .5f)) < .005f;
                bool undistorted = uv.width > 0 && uv.height > 0 && uv.xMin >= -.001f && uv.yMin >= -.001f &&
                    uv.xMax <= 1.001f && uv.yMax <= 1.001f && Mathf.Abs(shown.width / shown.height / shownAspect - 1) < .01f &&
                    (Mathf.Abs(uv.width - 1) < .001f || Mathf.Abs(uv.height - 1) < .001f);
                Check(label, OnScreen(frame) && fillsCard && centered && undistorted,
                    "card=" + frame + " art=" + shown + " uv=" + uv);
            }
            File.AppendAllText(Path.Combine(output, "memo-geometry.jsonl"), JsonUtility.ToJson(new MemoGeometry {
                label = label, width = Screen.width, height = Screen.height, screenRect = shown, uv = uv, sourceAspect = sourceAspect }) + "\n");
        }
        static void CheckNoLegacyDecor(string label)
        {
            string text = Compact(VisibleText(canvas.transform));
            var fragments = new[] { "九州界", "凡尘篇", "十二玉楼长生经", "山河无定数", "青石残梦", "一缕炊烟", "远山之外",
                "0.0.1", "择一卷，启新程", "记录保存在本机", "行迹/三卷人生" };
            bool legacyGraphic = canvas.GetComponentsInChildren<Graphic>().Any(g => g.isActiveAndEnabled && g.color.a > .01f &&
                (g.name == "Left scrim" || g.name == "Atmosphere" || g.name == "Menu title" ||
                 g.name.StartsWith("Mist ") || g.name.StartsWith("Mote ")));
            Check(label, !legacyGraphic && !fragments.Any(text.Contains) &&
                !canvas.GetComponentsInChildren<TMP_Text>().Any(t => Compact(t.text) == "卷一"), text);
        }
        static void CheckHomeComposition()
        {
            Canvas.ForceUpdateCanvases();
            Require("memo homepage is menu", controller.CurrentPage == "menu");
            var primary = ContractButton("Menu primary", "叩弦长生");
            var settingsButton = ContractButton("Menu settings", "乾坤", "调谐");
            var credits = ContractButton("Menu credits", "玉牒", "撰卷");
            var controls = new[] { primary, settingsButton, credits };
            Check("process exit is absent from homepage", Named("Menu exit") == null);
            var actual = canvas.GetComponentsInChildren<Button>().Where(b => b.gameObject.activeInHierarchy).ToArray();
            Rect main = ScreenRect((RectTransform)primary.transform);
            bool topRight = controls.Skip(1).All(b => { var r = ScreenRect((RectTransform)b.transform);
                return OnScreen(r) && r.center.x > Screen.width * .65f && r.yMin > Screen.height * .7f; });
            bool distinct = controls.Distinct().Count() == 3 && actual.Length == 3 && controls.All(b => b.IsInteractable());
            var bounds = controls.Select(b => ScreenRect((RectTransform)b.transform)).ToArray();
            bool nonoverlap = Enumerable.Range(0, bounds.Length).All(i =>
                Enumerable.Range(i + 1, bounds.Length - i - 1).All(j => !bounds[i].Overlaps(bounds[j])));
            bool unobtrusive = controls.All(b => { var r = ScreenRect((RectTransform)b.transform);
                return r.width * r.height < Screen.width * Screen.height * .08f; });
            Check("memo homepage three lightweight actions", distinct && nonoverlap && topRight && unobtrusive && OnScreen(main) &&
                Mathf.Abs(main.center.x / Screen.width - .5f) < .06f && main.center.y < Screen.height * .5f &&
                HasButtonLabel(primary, "叩弦长生") && HasButtonLabel(settingsButton, "乾坤", "调谐") &&
                HasButtonLabel(credits, "玉牒", "撰卷"));
            CheckNoLegacyDecor("memo clean homepage");
            Check("memo camera background black", camera != null && camera.backgroundColor.r < .001f &&
                camera.backgroundColor.g < .001f && camera.backgroundColor.b < .001f);
            CheckCover(still, "memo still aspect cover");
            var page = NamedRect("Page menu");
            Check("memo backdrop below page", page != null && !still.raycastTarget && !image.raycastTarget &&
                DrawnBefore(still.transform, image.transform) && DrawnBefore(image.transform, primary.transform));
        }
        static Button SlotButton(int slot) => ContractButton("Slot " + slot, new[] { "", "第一卷", "第二卷", "第三卷" }[slot]);
        static void CheckSlotsLayout()
        {
            Canvas.ForceUpdateCanvases();
            Require("memo primary opens slots", controller.CurrentPage == "slots");
            var rail = ScreenRect(NamedRect("Slots rail"));
            var dossier = ScreenRect(NamedRect("Slot dossier"));
            var slots = Enumerable.Range(1, 3).Select(i => ScreenRect((RectTransform)SlotButton(i).transform)).ToArray();
            bool contained = slots.All(r => OnScreen(r) && r.xMin >= rail.xMin - 2 && r.xMax <= rail.xMax + 2 &&
                r.yMin >= rail.yMin - 2 && r.yMax <= rail.yMax + 2);
            bool separated = !slots[0].Overlaps(slots[1]) && !slots[0].Overlaps(slots[2]) && !slots[1].Overlaps(slots[2]);
            Check("memo left rail and right dossier", OnScreen(rail) && OnScreen(dossier) &&
                rail.width >= Screen.width * .20f && rail.width <= Screen.width * .30f && rail.xMax <= Screen.width * .34f &&
                dossier.xMin >= rail.xMax - 2 && !rail.Overlaps(dossier) && contained && separated,
                "rail=" + rail + " dossier=" + dossier + "; no orientation requirement for the three scrolls");
            var back = ContractButton("Slots back", "回卷");
            var backRect = ScreenRect((RectTransform)back.transform);
            Check("memo slots back upper left", HasButtonLabel(back, "回卷") && OnScreen(backRect) &&
                backRect.xMax < Screen.width * .3f && backRect.yMin > Screen.height * .75f);
            CheckNoLegacyDecor("memo clean slots");
            CheckMemoScene();
            memoScrollIds = Enumerable.Range(1, 3).Select(i => SlotButton(i).GetInstanceID()).ToArray();
            CheckCover(NamedRect("Memo terrace").GetComponent<RawImage>(), "memo slots terrace aspect cover");
        }
        static void CheckMemoScene()
        {
            var terrace = NamedRect("Memo terrace").GetComponent<RawImage>();
            var outgoing = NamedRect("Outgoing menu frame").GetComponent<RawImage>();
            float Opacity(Graphic graphic) => graphic.GetComponentsInParent<CanvasGroup>()
                .Aggregate(graphic.color.a, (alpha, group) => alpha * group.alpha);
            Check("memo independent terrace visible", memoBackdrop.HasMemoArtwork && terrace.isActiveAndEnabled &&
                terrace.texture == Resources.Load<Texture2D>("Art/Background/memo-terrace") && terrace.texture != still.texture &&
                Opacity(terrace) >= .99f && !terrace.canvasRenderer.cull && !terrace.raycastTarget &&
                DrawnBefore(terrace.transform, NamedRect("Slot dossier")));
            Check("memo menu portrait layers cannot leak", !memoBackdrop.IsTransitioning &&
                Opacity(still) <= .001f && !outgoing.enabled && outgoing.texture == null && !image.enabled && image.texture == null);
            Check("memo slots stops and releases video", Player(video) == null && video.GetComponents<VideoPlayer>().Length == 0 &&
                video.State == MenuVideoBackdrop.PlaybackState.Stopped);
        }
        static bool MemoSceneReady()
        {
            if (Waited(3)) throw new InvalidOperationException("Independent memo scene did not settle; step=" + memoStep);
            return !memoBackdrop.IsTransitioning && memoBackdrop.MemoOpacity >= .99f;
        }
        static string DossierField(string name) => VisibleText(NamedRect("Dossier " + name));
        static void CheckDossier(int slot)
        {
            AssertIsolatedRepository();
            var expected = controller.Repository.Read(slot).Data;
            Require("memo selected save readable " + slot, expected != null && controller.SelectedMemoSlot == slot);
            string name = DossierField("name"), fate = DossierField("fate"), wealth = DossierField("wealth"), journey = DossierField("journey");
            string normalizedWealth = Compact(wealth).Replace(",", "").Replace("，", "");
            string time = WorldClock.Clock(expected.worldMinuteOfDay);
            string normalizedJourney = Compact(journey).Replace("：", ":");
            string otherName = seededSaves.First(d => d.slot != slot).characterName;
            // The persisted legacy sentinel is not a real saved town position.
            bool locationMapped = expected.location == "CharacterPreview"
                ? journey.Contains("驻足地点 · 行卷初启") : journey.Contains(expected.location);
            bool trueFields = locationMapped && name.Contains(expected.characterName) && !name.Contains(otherName) &&
                expected.traits.All(id => fate.Contains(CharacterGen.FindTrait(id).Name)) &&
                fate.Contains(CharacterGen.FindDestiny(expected.destiny).Name) &&
                normalizedWealth.Contains(expected.coins.ToString(CultureInfo.InvariantCulture)) &&
                normalizedJourney.Contains(expected.worldDay.ToString(CultureInfo.InvariantCulture)) && normalizedJourney.Contains(time);
            Check("memo slot " + slot + " real fields", trueFields, "name=" + name + " fate=" + fate + " wealth=" + wealth + " journey=" + journey);
            CheckDossierRenderedText(slot);
            CheckNov6MemoArtwork(slot);
            var portrait = NamedRect("Dossier portrait");
            Check("memo slot " + slot + " portrait populated", portrait.GetComponentsInChildren<RawImage>().Any(r => r.enabled && r.texture != null) ||
                portrait.GetComponentsInChildren<Image>().Any(r => r.enabled && r.sprite != null));
            string age = DossierField("age"), realm = DossierField("realm"), sect = DossierField("sect");
            var unspecified = new[] { "未记", "未载", "未入", "未明", "尚未", "暂无", "无门", "凡人", "凡胎", "散修", "待考" };
            Check("memo no invented progression " + slot, new[] { age, realm, sect }.All(t => unspecified.Any(t.Contains)),
                "Current SaveData has no age/realm/sect fields: " + age + " / " + realm + " / " + sect);
            Check("memo existing action label " + slot, HasButtonLabel(ContractButton("Slot enter", "承卷入世", "叩弦续道"), "承卷入世", "叩弦续道"));
            File.AppendAllText(Path.Combine(output, "memo-dossiers.jsonl"), JsonUtility.ToJson(new MemoDossier {
                slot = slot, coins = expected.coins, day = expected.worldDay, minuteOfDay = expected.worldMinuteOfDay,
                name = name, fate = fate, wealth = wealth, journey = journey, age = age, realm = realm, sect = sect }) + "\n");
        }
        static MemoScrollOrnament SelectedScroll() => SlotButton(controller.SelectedMemoSlot).GetComponent<MemoScrollOrnament>();
        static void CheckImmediateMemoSelection(int slot)
        {
            Check("memo selection immediate with persistent rail", controller.SelectedMemoSlot == slot &&
                Enumerable.Range(1, 3).All(i => SlotButton(i).GetComponent<MemoScrollOrnament>().Selected == (i == slot)) &&
                memoScrollIds.SequenceEqual(Enumerable.Range(1, 3).Select(i => SlotButton(i).GetInstanceID())) &&
                SelectedScroll().SelectionAura.VisibleAlpha >= .65f);
        }
        static void CheckNov6HomeArtwork()
        {
            Check("memo approved textures all present", MemoVisualAssets.RequiredTextures.All(n => MemoVisualAssets.GetTexture(n) != null));
            string[] names = { "Menu settings", "Menu credits" };
            string[] icons = { "icon-compass", "icon-document" };
            bool correct = true;
            for (int i = 0; i < names.Length; i++)
            {
                var plate = ContractButton(names[i]).GetComponent<MenuOrnament>();
                var icon = plate.transform.Find(names[i] + " ornament").GetComponent<MenuOrnament>();
                correct &= plate.IsUtilityPlate && plate.color.a >= .75f && plate.color.a <= .80f &&
                    icon.sprite != null && icon.sprite.texture == MemoVisualAssets.GetTexture(icons[i]);
            }
            Check("memo utility controls consume approved icons", correct);
            var primary = ContractButton("Menu primary").GetComponent<MenuOrnament>();
            var label = primary.GetComponentInChildren<TMP_Text>(); label.ForceMeshUpdate();
            Check("memo primary gold nine-slice and dark SDF text", primary.sprite != null &&
                primary.sprite.texture == MemoVisualAssets.GetTexture("button-gold") && primary.type == Image.Type.Sliced &&
                Vector4.Distance(label.color, UiKit.Hex("382314")) < .01f && label.font.atlasWidth >= 2048 &&
                label.textInfo.characterCount > 0 && label.textInfo.characterInfo.Take(label.textInfo.characterCount)
                    .Where(c => c.isVisible).All(c => c.textElement != null && c.textElement.glyph != null && c.textElement.glyph.index != 0));
        }
        static void CheckNov6MemoArtwork(int slot)
        {
            var paper = NamedRect("Slot dossier").GetComponent<MemoScrollOrnament>();
            var poster = NamedRect("Dossier portrait").GetComponent<PosterCardView>();
            Check("memo poster artwork and complete frame " + slot, poster != null && poster.HasArtwork &&
                (poster.IsCutout
                    ? (poster.ResourcePath == "Art/Characters/Qingshi/qingshi-male-figure" ||
                        poster.ResourcePath == "Art/Characters/Qingshi/qingshi-female-figure") && poster.Frame == null
                    : poster.ResourcePath.StartsWith(MemoVisualAssets.Prefix + "Posters/", StringComparison.Ordinal) &&
                        poster.Frame != null && poster.Frame.type == Image.Type.Sliced && !poster.Frame.fillCenter) &&
                poster.GetComponentsInChildren<PuppetActor>().Length == 0,
                poster != null && poster.IsCutout ? "full-body cutout without frame" : "framed portrait");
            if (poster != null && poster.HasArtwork) CheckPosterCover(poster, "memo poster aspect cover " + slot);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            float sourceAlpha = -1;
            try
            {
                if (ImageConversion.LoadImage(texture, File.ReadAllBytes("Assets/Resources/Art/UI/Nov6/paper-panel-translucent.png")))
                    sourceAlpha = texture.GetPixel(texture.width / 2, texture.height / 2).a;
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
            float effective = paper.GetComponentsInParent<CanvasGroup>().Aggregate(sourceAlpha * paper.color.a, (a, g) => a * g.alpha);
            Check("memo paper effective alpha 82 to 88 percent " + slot, paper.type == Image.Type.Sliced &&
                paper.sprite.texture == MemoVisualAssets.GetTexture("paper-panel-translucent") && effective >= .82f && effective <= .88f,
                "sourceCenter=" + sourceAlpha + " effective=" + effective);
            Check("memo contextual unknown language " + slot, !VisibleText(paper.rectTransform).Contains("尚未记载"));
            File.AppendAllText(Path.Combine(output, "memo-artwork.jsonl"), JsonUtility.ToJson(new MemoArtwork {
                slot = slot, poster = poster == null ? "" : poster.ResourcePath, crop = poster == null ? new Rect() : poster.Crop,
                paperSourceAlpha = sourceAlpha, paperEffectiveAlpha = effective, cachedSprites = MemoVisualAssets.CachedSpriteCount }) + "\n");
        }
        [Serializable] sealed class MemoArtwork { public int slot, cachedSprites; public string poster; public Rect crop; public float paperSourceAlpha, paperEffectiveAlpha; }
        [Serializable] sealed class MemoTextLayout
        {
            public int slot, lines, visibleCharacters;
            public string field, expected, rendered;
            public Rect box, screen;
            public Vector2 preferred;
            public bool overflowing, truncated, fits, complete;
        }
        static void CheckDossierRenderedText(int slot)
        {
            Canvas.ForceUpdateCanvases();
            Rect paper = ScreenRect(NamedRect("Slot dossier"));
            var buttons = NamedRect("Slot dossier").GetComponentsInChildren<Button>()
                .Select(b => ScreenRect((RectTransform)b.transform)).ToArray();
            bool all = true;
            var boxes = new List<Rect>();
            var failures = new List<string>();
            foreach (string field in FrontEndController.MemoInformationFields.Concat(slot == 3 ? Array.Empty<string>() :
                new[] { "caption:age", "caption:realm", "caption:sect", "caption:roots" }))
            {
                string objectName = field.StartsWith("caption:", StringComparison.Ordinal) ? "Caption " + field.Substring(8) : "Dossier " + field;
                var text = NamedRect(objectName).GetComponent<TMP_Text>();
                Require("memo text component " + field, text != null);
                text.ForceMeshUpdate();
                var chars = text.textInfo.characterInfo.Take(text.textInfo.characterCount).Where(c => c.isVisible).ToArray();
                string rendered = new string(chars.Select(c => c.character).ToArray());
                Rect box = text.rectTransform.rect, screen = ScreenRect(text.rectTransform);
                bool overflowing = text.isTextOverflowing, truncated = text.isTextTruncated;
                Vector2 preferred = text.GetPreferredValues(text.text, box.width, Mathf.Infinity);
                bool fits = chars.Length > 0 && chars.All(c => c.bottomLeft.x >= box.xMin - 2 &&
                    c.bottomLeft.y >= box.yMin - 2 && c.topRight.x <= box.xMax + 2 && c.topRight.y <= box.yMax + 2) &&
                    preferred.y <= box.height + 2 && OnScreen(screen) && screen.xMin >= paper.xMin - 2 &&
                    screen.xMax <= paper.xMax + 2 && screen.yMin >= paper.yMin - 2 && screen.yMax <= paper.yMax + 2 &&
                    buttons.All(b => !screen.Overlaps(b)) && boxes.All(b => !screen.Overlaps(b));
                bool complete = !overflowing && !truncated && Compact(rendered) == Compact(text.text) &&
                    text.isActiveAndEnabled && !text.canvasRenderer.cull && text.color.a > .01f &&
                    text.GetComponentsInParent<CanvasGroup>().All(g => g.alpha >= .99f) &&
                    chars.All(c => c.textElement != null && c.textElement.glyph != null && c.textElement.glyph.index != 0);
                all &= fits && complete;
                if (!fits || !complete) failures.Add(field + ": rendered=" + rendered + "; preferred=" + preferred + "; box=" + box);
                boxes.Add(screen);
                File.AppendAllText(Path.Combine(output, "memo-text-layout.jsonl"), JsonUtility.ToJson(new MemoTextLayout {
                    slot = slot, field = field, expected = text.text, rendered = rendered, box = box, screen = screen,
                    preferred = preferred, lines = text.textInfo.lineCount, visibleCharacters = chars.Length,
                    overflowing = overflowing, truncated = truncated, fits = fits, complete = complete }) + "\n");
            }
            Check("memo slot " + slot + " rendered fields complete", all, string.Join(" | ", failures));
        }
        static Dictionary<string, string> SlotFileHashes()
        {
            AssertIsolatedRepository();
            var files = new Dictionary<string, string>();
            for (int slot = 1; slot <= SaveRepository.SlotCount; slot++)
                foreach (string suffix in new[] { "", ".bak", ".tmp" })
                {
                    string key = "slot-" + slot + ".json" + suffix;
                    string path = Path.Combine(ScratchRoot, key);
                    files[key] = File.Exists(path) ? HashFile(path) : "MISSING";
                }
            return files;
        }
        static void SaveSlotHashes(string name, Dictionary<string, string> hashes)
        {
            var data = new Inputs { capturedUtc = DateTime.UtcNow.ToString("O") };
            foreach (var pair in hashes) data.files.Add(new Input { path = pair.Key, sha256 = pair.Value });
            File.WriteAllText(Path.Combine(output, name), JsonUtility.ToJson(data, true));
        }
        static bool SameSlotFiles(Dictionary<string, string> a, Dictionary<string, string> b) => a.Count == b.Count &&
            a.All(pair => b.TryGetValue(pair.Key, out var value) && value == pair.Value);
        static void CheckDeleteDialog(int slot)
        {
            var modal = NamedRect("Confirmation");
            string body = VisibleText(modal);
            Check("memo deletion identifies selected slot " + slot,
                body.Contains(controller.Repository.Read(slot).Data.characterName) || body.Contains(slot.ToString(CultureInfo.InvariantCulture)) ||
                body.Contains(new[] { "", "第一卷", "第二卷", "第三卷" }[slot]), body);
            var rect = (RectTransform)SlotButton(slot).transform;
            var pointer = new PointerEventData(EventSystem.current) {
                position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)) };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            Check("memo deletion modal blocks background", hits.Count > 0 && hits[0].gameObject.transform.IsChildOf(modal));
            Check("memo no deletion before confirmation " + slot, SameSlotFiles(deletionBaseline, SlotFileHashes()));
        }
        static void DirectTown(int slot, string gate)
        {
            Require(gate, controller.CurrentPage == "town" && controller.ActiveSave != null && controller.ActiveSave.slot == slot &&
                Named("Page preview") == null && Named("Page character-creation") == null);
        }
        static void CheckTownReady(int slot)
        {
            var world = NamedRect("World root");
            Check("memo real town instantiated " + slot, controller.CurrentPage == "town" && world.childCount > 0 &&
                controller.ActiveSave != null && controller.ActiveSave.slot == slot);
            Check("memo town releases menu video " + slot, Player(video) == null && video.GetComponents<VideoPlayer>().Length == 0 && StaticVisible(video));
            var held = NamedRect("Outgoing menu frame").GetComponent<RawImage>();
            Check("memo town clears backdrop transition " + slot, !memoBackdrop.IsTransitioning &&
                memoBackdrop.MemoOpacity <= .001f && !held.enabled && held.texture == null);
        }
        static void AdvanceMemo() { memoStep++; stageAt = EditorApplication.timeSinceStartup; stageFrame = Time.frameCount; }
        static bool MemoVideoReady()
        {
            if (Waited(20)) throw new InvalidOperationException("Memo background failed to resume, step=" + memoStep + " state=" + video.State);
            return video.HasDecodedFrame && video.IsVisible && image.color.a >= .99f && Player(video) != null && Player(video).isPlaying;
        }
        static void MemoAcceptance()
        {
            if (!Waited(.35f) || Time.frameCount <= stageFrame + 1) return;
            AssertIsolatedRepository();
            if (controller.CurrentPage == "preview" || controller.CurrentPage == "character-creation")
                Require("memo avoids preview step " + memoStep, false, controller.CurrentPage);
            switch (memoStep)
            {
                case 0:
                    if (!MemoVideoReady()) return;
                    CheckHomeComposition(); CheckNov6HomeArtwork(); CheckCover(image, "memo video aspect cover");
                    Click(ContractButton("Menu credits", "玉牒", "撰卷"), true); AdvanceMemo(); break;
                case 1:
                    Require("memo credits button works", controller.CurrentPage == "credits");
                    Click(ContractButton("", "返回", "回卷"), true); AdvanceMemo(); break;
                case 2:
                    Require("memo credits returns home", controller.CurrentPage == "menu");
                    Click(ContractButton("Menu settings", "乾坤"), true);
                    Require("memo settings opens before exit", controller.CurrentPage == "settings");
                    AdvanceMemo(); break;
                case 3:
                    Click(ContractButton("Settings exit game", "归卷 · 退出游戏"), true);
                    Require("settings exit opens confirmation", Named("Confirmation") != null);
                    AdvanceMemo(); break;
                case 4:
                    if (Named("Confirmation") != null)
                    {
                        Require("exit defaults to the safe cancel", EventSystem.current.currentSelectedGameObject == ContractButton("Modal cancel").gameObject);
                        Click(ContractButton("Modal cancel", "否 · 留驻"), true);
                        Require("exit cancel retains settings", controller.CurrentPage == "settings" && Named("Confirmation") == null);
                        stageAt = EditorApplication.timeSinceStartup; stageFrame = Time.frameCount; return;
                    }
                    if (controller.CurrentPage == "settings")
                    { Click(ContractButton("Settings back", "收起"), true); stageAt = EditorApplication.timeSinceStartup; stageFrame = Time.frameCount; return; }
                    Require("settings close restores menu", controller.CurrentPage == "menu");
                    Click(ContractButton("Menu primary", "叩弦长生"), true); AdvanceMemo(); break;
                case 5:
                    Require("memo primary opens slots", controller.CurrentPage == "slots");
                    if (!MemoSceneReady()) return;
                    CheckSlotsLayout(); Click(SlotButton(1), true); AdvanceMemo(); break;
                case 6:
                    CheckDossier(1); memoAuraPhase = SelectedScroll().ShimmerPhase; Capture("09-memo-slot-1"); AdvanceMemo(); break;
                case 7:
                    Check("memo selected scroll breathes", SelectedScroll().IsAnimating && Mathf.Abs(SelectedScroll().ShimmerPhase - memoAuraPhase) > .0001f);
                    Click(SlotButton(2), true);
                    CheckImmediateMemoSelection(2); AdvanceMemo(); break;
                case 8:
                    if (memoMotionProbe == 0)
                    {
                        CheckDossier(2); settings.reduceMotion = true; memoMotionProbe = 1;
                        stageAt = EditorApplication.timeSinceStartup; stageFrame = Time.frameCount; return;
                    }
                    if (memoMotionProbe == 1)
                    {
                        memoAuraPhase = SelectedScroll().ShimmerPhase; memoMotionProbe = 2;
                        stageAt = EditorApplication.timeSinceStartup; stageFrame = Time.frameCount; return;
                    }
                    if (memoMotionProbe == 2)
                    {
                        var reducedScroll = SelectedScroll();
                        Check("memo reduced motion freezes aura but retains selection", !reducedScroll.IsAnimating &&
                            reducedScroll.Selected && reducedScroll.SelectionAura.VisibleAlpha >= .65f &&
                            reducedScroll.ShimmerPhase == memoAuraPhase);
                        settings.reduceMotion = false; memoMotionProbe = 3;
                        stageAt = EditorApplication.timeSinceStartup; stageFrame = Time.frameCount; return;
                    }
                    Check("memo aura resumes after reduced motion", SelectedScroll().IsAnimating && SelectedScroll().ShimmerPhase != memoAuraPhase);
                    Capture("10-memo-slot-2"); AdvanceMemo(); break;
                case 9:
                    Click(ContractButton("Slot enter", "承卷入世", "叩弦续道"), true);
                    DirectTown(2, "memo existing slot enters town directly"); AdvanceMemo(); break;
                case 10:
                    CheckTownReady(2); Capture("11-memo-existing-town"); AdvanceMemo(); break;
                case 11:
                    Click(ContractButton("", "返回主菜单", "回卷"), true); AdvanceMemo(); break;
                case 12:
                    if (!MemoVideoReady()) return;
                    Require("memo town returns to homepage", controller.CurrentPage == "menu");
                    Click(ContractButton("Menu primary", "叩弦长生"), true); AdvanceMemo(); break;
                case 13:
                    Click(SlotButton(3), true); AdvanceMemo(); break;
                case 14:
                    Require("memo third slot initially empty", controller.Repository.Read(3).State == SlotState.Empty);
                    Check("memo empty slot has no stale identity", !seededSaves.Any(d => VisibleText(NamedRect("Slot dossier")).Contains(d.characterName)));
                    Check("memo empty slot action label", HasButtonLabel(ContractButton("Slot enter", "立传开卷", "初叩仙门"), "立传开卷", "初叩仙门"));
                    if (beforeEmptyCreation == null)
                    {
                        var draftInput = NamedRect("Draft name").GetComponent<TMP_InputField>();
                        Require("memo empty draft has real name input", draftInput != null && draftInput.IsInteractable());
                        draftFateBeforeChoice = DossierField("fate");
                        draftInput.text = NewMemoName; // Product onValueChanged receives the edit; no private draft injection.
                        Click(ContractButton("Draft gender female"), true);
                        beforeEmptyCreation = SlotFileHashes();
                        SaveSlotHashes("memo-before-empty-create.json", beforeEmptyCreation);
                        stageAt = EditorApplication.timeSinceStartup; stageFrame = Time.frameCount;
                        return; // Let the actual rebuilt dossier finish its normal fade before capturing/clicking.
                    }
                    Check("memo empty draft choice preserves fate", DossierField("fate") == draftFateBeforeChoice);
                    CheckDossierRenderedText(3); CheckNov6MemoArtwork(3);
                    Capture("12-memo-empty-slot"); AdvanceMemo(); break;
                case 15:
                    Click(ContractButton("Slot enter", "立传开卷", "初叩仙门"), true);
                    DirectTown(3, "memo empty slot enters town directly");
                    var created = controller.Repository.Read(3);
                    Require("memo new save persists entered draft", created.CanLoad && created.Data.characterName == NewMemoName &&
                        created.Data.gender == "female" && controller.ActiveSave.characterName == NewMemoName &&
                        controller.ActiveSave.gender == "female");
                    var afterCreate = SlotFileHashes();
                    Check("memo creating empty slot preserves existing slots", afterCreate.Where(p =>
                        !p.Key.StartsWith("slot-3.", StringComparison.Ordinal)).All(p => beforeEmptyCreation[p.Key] == p.Value));
                    SaveSlotHashes("memo-after-empty-create.json", afterCreate);
                    AdvanceMemo(); break;
                case 16:
                    CheckTownReady(3); Capture("13-memo-new-town"); AdvanceMemo(); break;
                case 17:
                    Click(ContractButton("", "返回主菜单", "回卷"), true); AdvanceMemo(); break;
                case 18:
                    if (!MemoVideoReady()) return;
                    Click(ContractButton("Menu primary", "叩弦长生"), true); AdvanceMemo(); break;
                case 19:
                    Click(SlotButton(1), true); AdvanceMemo(); break;
                case 20:
                    deletionBaseline = SlotFileHashes(); SaveSlotHashes("memo-before-delete.json", deletionBaseline);
                    Click(ContractButton("Slot delete", "归尘", "焚卷"), true); AdvanceMemo(); break;
                case 21:
                    CheckDeleteDialog(1); Capture("14-memo-delete-cancel-dialog"); AdvanceMemo(); break;
                case 22:
                    Click(ContractButton("Delete cancel", "取消", "留卷"), true);
                    Check("memo cancel deletion preserves every slot", Named("Confirmation") == null && SameSlotFiles(deletionBaseline, SlotFileHashes()));
                    AdvanceMemo(); break;
                case 23:
                    // Change selection after cancelling, to expose stale-slot closures in the confirm handler.
                    Click(SlotButton(2), true); AdvanceMemo(); break;
                case 24:
                    Click(ContractButton("Slot delete", "归尘", "焚卷"), true); AdvanceMemo(); break;
                case 25:
                    CheckDeleteDialog(2); Capture("15-memo-delete-confirm-dialog"); AdvanceMemo(); break;
                case 26:
                    Click(ContractButton("Delete confirm", "确认归尘", "归尘", "焚卷", "确认焚卷", "确认删除"), true);
                    var after = SlotFileHashes(); SaveSlotHashes("memo-after-delete.json", after);
                    Check("memo confirm deletes only selected slot", controller.Repository.Read(2).State == SlotState.Empty &&
                        after.Where(p => p.Key.StartsWith("slot-2.", StringComparison.Ordinal)).All(p => p.Value == "MISSING") &&
                        after.Where(p => !p.Key.StartsWith("slot-2.", StringComparison.Ordinal)).All(p => deletionBaseline[p.Key] == p.Value) &&
                        controller.Repository.Read(1).CanLoad && controller.Repository.Read(3).CanLoad && Named("Confirmation") == null);
                    Capture("16-memo-after-delete"); AdvanceMemo(); break;
                case 27:
                    Click(ContractButton("Slots back", "回卷"), true); AdvanceMemo(); break;
                case 28:
                    if (!MemoVideoReady()) return;
                    Require("memo restored menu playback after save flows", controller.CurrentPage == "menu" && Player(video).isPlaying);
                    memoComplete = true; Check("memo full flow completed", memoComplete);
                    Capture("17-memo-final-home"); Next(10); break;
            }
        }

        static MenuVideoBackdrop MakeProbe(VideoClip source)
        {
            var go = new GameObject("Menu video fallback probe", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(video.transform.parent, false);
            go.GetComponent<RectTransform>().sizeDelta = Vector2.one;
            var component = go.AddComponent<MenuVideoBackdrop>();
            component.Configure(go.GetComponent<RawImage>(), source, () => true, () => false);
            return component;
        }
        static void MissingResource()
        {
            probe = MakeProbe(null);
            Check("missing resource fallback is nonblocking", probe.State == MenuVideoBackdrop.PlaybackState.StaticFallback &&
                StaticVisible(probe) && Player(probe) == null && ContractButton("Menu settings", "乾坤", "调谐").interactable);
            probe.Refresh();
            Check("missing resource does not retry each update", Player(probe) == null && probe.State == MenuVideoBackdrop.PlaybackState.StaticFallback);
            UnityEngine.Object.Destroy(probe.gameObject);
            var clip = Resources.Load<VideoClip>(MenuVideoBackdrop.ResourcePath);
            if (clip == null)
            {
                Check("timeout and decoder probes require real clip", false);
                Next(13); return;
            }
            probe = MakeProbe(clip);
            Player(probe).Stop(); // Interrupt native preparation, then expire the production watchdog clock.
            SetField(probe, "prepareDeadline", Time.realtimeSinceStartupAsDouble - 1);
            Next(11);
        }
        static void PreparationTimeout()
        {
            if (Time.frameCount <= stageFrame + 1) return;
            Check("interrupted preparation watchdog falls back", probe.State == MenuVideoBackdrop.PlaybackState.StaticFallback &&
                probe.Failure.Contains("timeout") && StaticVisible(probe) && Player(probe) == null,
                "Fault injection: native Stop during preparation; deadline expired through reflection. Not a naturally failing codec.");
            UnityEngine.Object.Destroy(probe.gameObject);
            probe = MakeProbe(Resources.Load<VideoClip>(MenuVideoBackdrop.ResourcePath));
            var cancelled = Player(probe);
            cancelled.Stop();
            typeof(MenuVideoBackdrop).GetMethod("OnError", Private).Invoke(probe, new object[] { cancelled, "MenuVideoShot injected decoder error" });
            // An obsolete async callback must not resurrect a stopped/disposed player.
            typeof(MenuVideoBackdrop).GetMethod("OnPrepared", Private).Invoke(probe, new object[] { cancelled });
            Check("decoder callback fallback rejects late preparation", probe.State == MenuVideoBackdrop.PlaybackState.StaticFallback &&
                StaticVisible(probe) && Player(probe) == null, "Injected callback; all actual Unity engine errors still fail.");
            Next(12);
        }
        static void DecoderError()
        {
            if (Time.frameCount <= stageFrame + 1) return;
            Check("fallback probe releases native player", probe.GetComponents<VideoPlayer>().Length == 0);
            UnityEngine.Object.Destroy(probe.gameObject);
            video.Release(); video.Release();
            Check("release is idempotent and clears video texture", video.State == MenuVideoBackdrop.PlaybackState.Released && StaticVisible(video));
            Next(13);
        }
        static void Cleanup()
        {
            if (Time.frameCount <= stageFrame + 1) return;
            video.Release();
            Check("no orphan video components after release", video.GetComponents<VideoPlayer>().Length == 0);
            Check("input bytes unchanged after playback", File.Exists(Asset) && HashFile(Asset) == ExpectedSha);
            if (!File.Exists(Path.Combine(output, "08-final-static.png")))
            { Capture("08-final-static"); return; }
            Finish();
        }

        static Button FindButton(string label) => ContractButton("", label);
        static void Click(Button button, bool verifyRaycast)
        {
            Require("clicked control is interactable " + button.name, button.IsActive() && button.IsInteractable());
            Canvas.ForceUpdateCanvases();
            var rect = (RectTransform)button.transform;
            Check("button raycast uses native overlay", canvas.renderMode == RenderMode.ScreenSpaceOverlay);
            Vector2 point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            var pointer = new PointerEventData(EventSystem.current) { position = point, button = PointerEventData.InputButton.Left };
            if (verifyRaycast)
            {
                var hits = new List<RaycastResult>();
                EventSystem.current.RaycastAll(pointer, hits);
                Require("actual button raycast " + button.name, hits.Count > 0 &&
                    hits[0].gameObject.GetComponentInParent<Button>() == button && hits.All(h => h.gameObject != image.gameObject),
                    "top=" + (hits.Count == 0 ? "none" : hits[0].gameObject.name));
            }
            lastScriptedClick = button.name + " @frame=" + Time.frameCount + " phase=" + phase + " memo=" + memoStep;
            File.AppendAllText(Path.Combine(output, "scripted-clicks.log"), lastScriptedClick + "\n");
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }
        static void CheckMusic(string label) => Check(label, music != null && music.clip == musicClip && music.isPlaying &&
            Mathf.Approximately(music.volume, musicVolume) && Mathf.Approximately(AudioListener.volume, masterVolume));

        static string SaveDecodedFrame(string name)
        {
            if (image.texture == null) throw new InvalidOperationException("No decoded texture to capture.");
            var rt = RenderTexture.GetTemporary(image.texture.width, image.texture.height, 0, RenderTextureFormat.ARGB32);
            Texture2D tex = null;
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(image.texture, rt); RenderTexture.active = rt;
                tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                File.WriteAllBytes(Path.Combine(output, name + ".png"), tex.EncodeToPNG());
                byte[] data = tex.GetRawTextureData();
                Check("decoded frame is not black " + name, data.Count(b => b > 8) > data.Length / 20);
                return Hash(data);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
            }
        }
        static void Capture(string name)
        {
            if (canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                throw new InvalidOperationException("Product overlay Canvas changed before capture.");
            captureQueue.Enqueue(name);
            if (!captureActive)
            {
                captureActive = true;
                capturePump.StartCoroutine(CaptureAtEndOfFrame());
            }
        }
        static IEnumerator CaptureAtEndOfFrame()
        {
            while (captureQueue.Count > 0)
            {
                string name = captureQueue.Dequeue();
                // Public capture after all cameras AND the unchanged overlay UI have rendered.
                // This coroutine requires a real GameView; batchmode is rejected by Run().
                yield return new WaitForEndOfFrame();
                Texture2D texture = null;
                try
                {
                    if (canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                        throw new InvalidOperationException("Overlay mode changed before real screen capture.");
                    texture = ScreenCapture.CaptureScreenshotAsTexture();
                    if (texture == null || texture.width <= 0 || texture.height <= 0)
                        throw new InvalidOperationException("No rendered GameView screen available.");
                    File.WriteAllBytes(Path.Combine(output, name + ".png"), texture.EncodeToPNG());
                    File.AppendAllText(Path.Combine(output, "screen-captures.jsonl"), JsonUtility.ToJson(new CaptureRow {
                        file = name + ".png", frame = Time.frameCount, width = texture.width, height = texture.height,
                        page = controller.CurrentPage, state = video.State.ToString(), alpha = image.color.a,
                        videoTime = Player(video) == null ? -1 : Player(video).time,
                        canvasMode = canvas.renderMode.ToString(), method = "ScreenCapture.CaptureScreenshotAsTexture after WaitForEndOfFrame" }) + "\n");
                    screenshots++;
                }
                catch (Exception e) { Check("real GameView capture " + name, false, e.ToString()); }
                finally { if (texture != null) UnityEngine.Object.Destroy(texture); }
            }
            captureActive = false;
        }
        [Serializable] sealed class CaptureRow
        {
            public string file, page, state, canvasMode, method;
            public int frame, width, height;
            public float alpha;
            public double videoTime;
        }
        static string HashFile(string path) { using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(stream)); }
        static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(bytes)); }
        static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        static IEnumerable<string> CurrentInputPaths()
        {
            var addedCode = new[] { "MemoVisualAssets", "PosterCardView", "ScrollSelectAura" }
                .SelectMany(n => new[] { "Assets/Scripts/Presentation/" + n + ".cs", "Assets/Scripts/Presentation/" + n + ".cs.meta" });
            var addedArt = MemoVisualAssets.RequiredTextures.SelectMany(n => new[] {
                "Assets/Resources/" + MemoVisualAssets.Prefix + n + ".png",
                "Assets/Resources/" + MemoVisualAssets.Prefix + n + ".png.meta" });
            var fonts = new[] { "NotoSerifCJKsc-Regular.otf", "ZhiMangXing-Regular.ttf" }.SelectMany(n => new[] {
                "Assets/Resources/Fonts/" + n, "Assets/Resources/Fonts/" + n + ".meta" });
            return InputPaths.Concat(addedCode).Concat(addedArt).Concat(fonts).Distinct();
        }
        static void CaptureInputs(string name)
        {
            var manifest = new Inputs { capturedUtc = DateTime.UtcNow.ToString("O") };
            foreach (var path in CurrentInputPaths())
                manifest.files.Add(new Input { path = path, bytes = File.Exists(path) ? new FileInfo(path).Length : -1,
                    sha256 = File.Exists(path) ? HashFile(path) : "MISSING" });
            File.WriteAllText(Path.Combine(output, name), JsonUtility.ToJson(manifest, true));
        }
        static void CheckSourceParity(string name)
        {
            var result = new Parities { capturedUtc = DateTime.UtcNow.ToString("O"), sourceProject = sourceProject };
            foreach (var path in CurrentInputPaths())
            {
                string original = Path.Combine(sourceProject, path);
                string source = File.Exists(original) ? HashFile(original) : "MISSING";
                string fixture = File.Exists(path) ? HashFile(path) : "MISSING";
                result.files.Add(new Parity { path = path, sourceSha = source, fixtureSha = fixture, match = source != "MISSING" && source == fixture });
            }
            File.WriteAllText(Path.Combine(output, name), JsonUtility.ToJson(result, true));
            Check("frozen source/fixture SHA equality " + name, result.files.All(f => f.match),
                string.Join(", ", result.files.Where(f => !f.match).Select(f => f.path)));
        }

        static void Finish()
        {
            if (finishing) return;
            finishing = true;
            // Missing media/early exits cannot pass a reduced subset of the required acceptance.
            foreach (var gate in new[] { "acceptance native input isolated", "real first frame and 720p dimensions", "actual decoded pixels and frame index change",
                "real natural loop and continued decoded playback", "soundtrack looping player", "loop end fades through still",
                "loop start fades from still", "reduceMotion stops releases and hides video", "motion setting restores actual decoded video",
                "menu return restores real playback", "missing resource fallback is nonblocking", "interrupted preparation watchdog falls back",
                "decoder callback fallback rejects late preparation", "native product overlay canvas retained",
                "cold startup preserves first attempt", "hidden while preparing before any decoded frame",
                "memo homepage three lightweight actions", "memo clean homepage", "memo still aspect cover",
                "memo video aspect cover", "memo left rail and right dossier", "memo slot 1 real fields",
                "memo slots terrace aspect cover", "memo independent terrace visible", "memo menu portrait layers cannot leak",
                "memo slots stops and releases video", "memo town clears backdrop transition 2", "memo town clears backdrop transition 3",
                "memo slot 2 real fields", "memo slot 1 rendered fields complete", "memo slot 2 rendered fields complete",
                "memo existing slot enters town directly", "memo empty slot enters town directly",
                "memo new save persists entered draft", "memo creating empty slot preserves existing slots",
                "memo cancel deletion preserves every slot", "memo confirm deletes only selected slot",
                "memo restored menu playback after save flows", "memo full flow completed",
                "memo approved textures all present", "memo utility controls consume approved icons", "memo primary gold nine-slice and dark SDF text",
                "memo poster artwork and complete frame 1", "memo poster artwork and complete frame 2", "memo poster artwork and complete frame 3",
                "memo paper effective alpha 82 to 88 percent 1", "memo paper effective alpha 82 to 88 percent 2", "memo paper effective alpha 82 to 88 percent 3",
                "memo selection immediate with persistent rail", "memo selected scroll breathes", "memo reduced motion freezes aura but retains selection",
                "memo aura resumes after reduced motion", "memo empty draft choice preserves fate", "memo slot 3 rendered fields complete" })
                if (!rows.Any(r => r.name == gate)) Check("required gate not reached: " + gate, false);
            if (video != null) video.Release();
            if (probe != null) probe.Release();
            if (capturePump != null) UnityEngine.Object.Destroy(capturePump.gameObject);
            CheckSourceParity("source-fixture-after.json");
            if (File.Exists(Path.Combine(output, "inputs.json")))
            {
                var frozen = JsonUtility.FromJson<Inputs>(File.ReadAllText(Path.Combine(output, "inputs.json")));
                Check("fixture inputs unchanged throughout Run", frozen.files.All(f =>
                    f.sha256 == (File.Exists(f.path) ? HashFile(f.path) : "MISSING")));
            }
            bool ok = rows.Count > 0 && rows.All(r => r.pass);
            File.WriteAllText(Path.Combine(output, "runtime-results.json"), JsonUtility.ToJson(new Report {
                status = ok ? "PASS" : "FAIL", exitCode = ok ? 0 : 1, unityVersion = Application.unityVersion,
                graphicsDevice = SystemInfo.graphicsDeviceType + " / " + SystemInfo.graphicsDeviceName,
                startedUtc = SessionState.GetString(Key + ".Utc", ""), finishedUtc = DateTime.UtcNow.ToString("O"),
                scope = "Memo real UI/isolated saves plus checklist5 independent terrace; slots video-wait replaced with terrace/hidden menu/released decoder gates; unchanged interactions/text/save/delete and Overlay/public end-of-frame capture; real homepage loop; injected fallback probes.",
                screenshots = screenshots, runtimeErrors = rows.Count(r => r.category != "assertion" && !r.pass), assertions = rows }, true));
            RestoreNativeInput();
            SessionState.SetBool(Key, false);
            EditorApplication.update -= Pump; Application.logMessageReceived -= OnLog;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            Debug.Log("[MenuVideoShot] RESULT " + (ok ? "PASS" : "FAIL") + " assertions=" + rows.Count + " failures=" + rows.Count(r => !r.pass));
            EditorApplication.Exit(ok ? 0 : 1);
        }
    }

    internal sealed class MenuVideoCapturePump : MonoBehaviour { }
}
