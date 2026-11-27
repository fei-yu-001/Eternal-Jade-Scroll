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
using TMPro;
using System.Text;
using System.Globalization;
using UnityEngine.Events;

namespace TwelveJade.Editor
{
    // Run only in a graphical .batch clone with a clean NAMED starting scene. No -batchmode/-nographics/-quit.
    // -executeMethod TwelveJade.Editor.InnateFateChecks.Run
    // EJS_INNATE_SOURCE_PROJECT: absolute source Unity project containing Assets/.
    // EJS_INNATE_EVIDENCE: exclusive new/empty directory outside both projects and all real saves.
    // The clone root's .innate-fate-checks contains the source Unity project path.
    // Real save directories are fingerprinted read-only. All test writes target evidence/scratch-saves.
    [InitializeOnLoad]
    public static class InnateFateChecks
    {
        const string Key = "InnateFateChecks.State", ScenePath = "Assets/Scenes/FrontEnd.unity", Marker = ".innate-fate-checks";
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly string[] BoundPaths = {
            "Assets/Editor/InnateFateChecks.cs", "Assets/Editor/InnateFateChecks.cs.meta", ScenePath,
            "Assets/Scripts/Presentation/InnateFateView.cs", "Assets/Scripts/Presentation/InnateFateView.cs.meta",
            "Assets/Scripts/Presentation/FrontEndInnateFate.cs", "Assets/Scripts/Presentation/FrontEndInnateFate.cs.meta",
            "Assets/Scripts/Presentation/ApprovedUiArt.cs", "Assets/Scripts/Presentation/ApprovedUiArt.cs.meta",
            "Assets/Resources/Config/ui-art-catalogue.json", "Assets/Resources/Config/ui-art-catalogue.json.meta",
            "Assets/Resources/Art/UI/Nov27/innate-scroll-stage.png", "Assets/Resources/Art/UI/Nov27/innate-scroll-stage.png.meta",
            "Assets/Resources/Art/UI/Nov27/innate-fate-card.png", "Assets/Resources/Art/UI/Nov27/innate-fate-card.png.meta",
            "Assets/Scripts/Presentation/QingshiComicPaperGraphic.cs",
            "Assets/Scripts/Presentation/FrontEndController.cs", "Assets/Scripts/Presentation/FrontEndCharacters.cs",
            "Assets/Scripts/Presentation/FrontEndGuofengHud.cs", "Assets/Scripts/Presentation/FrontEndSettings.cs",
            "Assets/Scripts/Presentation/DaoIdentityView.cs", "Assets/Scripts/Presentation/PosterCardView.cs",
            "Assets/Scripts/Presentation/CharacterPreset.cs", "Assets/Scripts/Presentation/UiKit.cs",
            "Assets/Scripts/Presentation/UnityJsonCodec.cs", "Assets/Scripts/Core/SaveModels.cs",
            "Assets/Scripts/Core/SaveRepository.cs", "Assets/Scripts/Core/QingshiStory.cs", "Assets/Scripts/Core/PrologueRules.cs",
            "Assets/Scripts/Presentation/TwelveJade.Presentation.asmdef", "ProjectSettings/ProjectSettings.asset",
            "Packages/manifest.json", "Packages/packages-lock.json"
        };
        [Serializable] sealed class CheckRow { public string name, status, detail; }
        [Serializable] sealed class Stamp { public string path, sha256; }
        [Serializable] sealed class ProtectedSave { public string path; public bool existed; public string[] entries; }
        [Serializable] sealed class SceneRecord { public string path, name; public bool loaded, active, dirty; public string[] roots; }
        [Serializable] sealed class SceneList { public SceneRecord[] scenes; }
        [Serializable] sealed class CaptureColorSample {
            public string name;
            public int sourceX, sourceY, resizedX, resizedY;
            public int[] sourceRgb, resizedRgb, jpegRgb, resizedRgbDelta, jpegRgbDelta;
            public int resizedMaxRgbDelta, jpegMaxRgbDelta;
        }
        [Serializable] sealed class Shot {
            public string file, sha256, page;
            public int width, height, gameViewWidth, gameViewHeight, quality, unityFrame;
            public long bytes;
            public string resizeMethod = "CPU RGB byte bilinear", sampleOrigin = "bottom-left";
            public string activeColorSpace, sourceGraphicsFormat;
            public bool sourceIsDataSRGB, canvasVertexColorAlwaysGammaSpace;
            public int resizedRgbTolerance = 4, jpegRgbTolerance = 8;
            public CaptureColorSample[] colorSamples;
        }
        [Serializable] sealed class RunState {
            public string phase, source, project, output, scratch, oldSave, oldStartScene, startedUtc;
            public bool hadOldSave, oldBackground, checksStarted, editReady;
            public float oldTimeScale;
            public double started, timeout = 180, restoreStarted;
            public SceneRecord[] scenes;
            public List<ProtectedSave> saves = new List<ProtectedSave>();
            public List<Stamp> inputs = new List<Stamp>();
            public List<CheckRow> checks = new List<CheckRow>();
            public List<Shot> screenshots = new List<Shot>();
            public List<int> disabledDevices = new List<int>();
        }
        [Serializable] sealed class Report {
            public string status, unityVersion, graphicsDevice, sourceProject, copyProject, scratch, startedUtc, finishedUtc;
            public string scope = "Innate fate read-only recruitment-style reveal: actual FrontEnd entries, prefixes, deterministic motion, figures, traits, lifecycle and save invariance.";
            public string visualReview = "PENDING: inspect at most three compressed real GameView JPEGs. Automated layout checks do not approve the art.";
            public int passed, failed, exitCode;
            public List<CheckRow> checks;
            public List<Shot> screenshots;
            public List<Stamp> inputs;
        }
        static RunState state;
        static FrontEndController host;
        static Canvas canvas;
        static bool finalizing;
        static double nextFocus;
        static SaveData male, female;
        static string[] seededFiles;
        static readonly string[] Elements = { "metal", "wood", "water", "fire", "earth" };
        static InnateFateView View => host.InnateFateForCheck;
        static UserSettings Settings => Field<UserSettings>(host, "settings");
        static InnateFateChecks()
        {
            string saved = SessionState.GetString(Key, "");
            if (!string.IsNullOrEmpty(saved)) { state = JsonUtility.FromJson<RunState>(saved); if (state != null) Attach(); }
        }
        public static void Run()
        {
            if (state != null || !string.IsNullOrEmpty(SessionState.GetString(Key, "")))
                throw new InvalidOperationException("A innate fate run is already pending restoration.");
            if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode ||
                SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null ||
                Environment.GetCommandLineArgs().Any(a => string.Equals(a, "-quit", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Use a separate graphical Editor in Edit mode, without -batchmode/-nographics/-quit.");
            string project = Full(Path.Combine(Application.dataPath, ".."));
            string source = AbsoluteEnvironment("EJS_INNATE_SOURCE_PROJECT");
            string output = AbsoluteEnvironment("EJS_INNATE_EVIDENCE");
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
            if (scenes.Length == 0 || scenes.Any(s => s.dirty || string.IsNullOrEmpty(s.path)))
                throw new InvalidOperationException("Start from an existing named, clean scene. Untitled/dirty scenes are refused before any run state or writes.");
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (scene == null) throw new InvalidOperationException("Real FrontEnd scene is missing.");
            string oldSave = Environment.GetEnvironmentVariable("TWELVEJADE_SAVEDIR");
            var protectedPaths = new[] { Path.Combine(Application.persistentDataPath, "Saves"), oldSave }
                .Where(p => !string.IsNullOrEmpty(p)).Select(Full).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (protectedPaths.Any(p => Overlaps(output, p))) throw new InvalidOperationException("Evidence/scratch overlaps a real save root.");
            var next = new RunState {
                source = source, project = project, output = output, scratch = Full(Path.Combine(output, "scratch-saves")),
                oldSave = oldSave ?? "", hadOldSave = oldSave != null, oldBackground = Application.runInBackground, oldTimeScale = Time.timeScale,
                oldStartScene = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) ?? "", scenes = scenes,
                phase = "play", started = EditorApplication.timeSinceStartup, startedUtc = DateTime.UtcNow.ToString("O")
            };
            // Real saves are ONLY enumerated and hashed. Never construct a repository for them.
            foreach (string path in protectedPaths)
                next.saves.Add(new ProtectedSave { path = path, existed = Directory.Exists(path), entries = DirectoryEntries(path) });
            Directory.CreateDirectory(output);
            state = next; finalizing = false; host = null; Attach(); Save();
            try {
                Protect(marker);
                foreach (string path in BoundPaths) Bind(path);
                foreach (var edit in scenes.Where(s => !string.IsNullOrEmpty(s.path))) Protect(Path.Combine(project, edit.path));
                AssertScratch(false);
                Environment.SetEnvironmentVariable("TWELVEJADE_SAVEDIR", state.scratch);
                Application.runInBackground = true;
                EditorSceneManager.playModeStartScene = scene;
                UnityEditor.Search.SearchService.EnumerateDatabases().ToArray();
                Save(); EditorApplication.isPlaying = true;
            } catch (Exception e) { Check("bootstrap", false, e.ToString()); RequestFinish(); }
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
                    Check("bounded innate fate run", false, "timeout=" + state.timeout); RequestFinish(); return;
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
            Require("runtime domain remained stable", !state.checksStarted);
            state.checksStarted = true; Save();
            foreach (var device in InputSystem.devices.ToArray()) DisableNative(device);
            AssertScratch(true);
            canvas = Field<GameObject>(host, "canvasObject").GetComponent<Canvas>();
            Require("actual FrontEnd overlay canvas", canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay);
            Require("scratch begins with empty slots", host.Repository.ReadAll().All(s => s.State == SlotState.Empty));
            new GameObject("Innate fate verification pump").AddComponent<InnateFateChecksPump>().StartCoroutine(Guard(Checks()));
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


        sealed class DataWitness
        {
            readonly SaveData data;
            readonly string json;
            readonly Dictionary<FieldInfo, object> references;
            public DataWitness(SaveData value) {
                data = value; json = JsonUtility.ToJson(value);
                references = typeof(SaveData).GetFields(Fields).Where(f => !f.FieldType.IsValueType)
                    .ToDictionary(f => f, f => f.GetValue(value));
            }
            public void CheckUnchanged(string name) {
                Require(name + " serialized data and timestamps unchanged", json == JsonUtility.ToJson(data));
                Require(name + " input object/collection references unchanged", references.All(pair => ReferenceEquals(pair.Value, pair.Key.GetValue(data))));
            }
        }
        static IEnumerator Checks()
        {
            Seed(); PrefixChecks();
            yield return Click("Menu primary");
            yield return Until(() => host.CurrentPage == "slots", "actual menu opens slots");
            yield return SelectSlot(1);
            yield return Click("Dossier fate inspect");
            yield return Until(() => View != null, "readable male entry opens real fate view");
            var oldMale = View;
            View.enabled = false;
            yield return SampleChecks(View, male, "male unknown");
            CheckFigure(View, "male");
            CheckLayout(View, null, Screen.width, Screen.height, "actual male");
            yield return Capture("01-male-unknown.jpg");
            yield return Click("Innate fate replay");
            Require("real replay restarts the existing sequence", View == oldMale && !View.IsSettled && View.TraitIds.SequenceEqual(male.traits));
            yield return Click("Innate fate skip");
            Require("real skip settles without replacing view", View == oldMale && View.IsSettled);
            var staleClose = ButtonNamed("Innate fate close").onClick;
            var staleSkip = ButtonNamed("Innate fate skip", false)?.onClick;
            var staleReplay = ButtonNamed("Innate fate replay").onClick;
            yield return CloseView();
            Require("closed component is destroyed", oldMale == null);
            CheckFiles("male sampling/replay/skip/close", seededFiles);

            yield return SelectSlot(2);
            yield return Click("Dossier fate inspect");
            yield return Until(() => View != null, "readable female entry opens real fate view");
            View.enabled = false;
            yield return SampleChecks(View, female, "female known");
            CheckFigure(View, "female");
            CheckLayout(View, null, Screen.width, Screen.height, "actual female");
            var current = View; string stable = VisualStamp(current);
            staleClose.Invoke(); staleSkip?.Invoke(); staleReplay.Invoke();
            yield return Frames(2);
            Require("callbacks from destroyed view cannot close/replay a new slot", View == current && View != null && stable == VisualStamp(View) && host.SelectedMemoSlot == 2);
            yield return Capture("02-female-water-fire.jpg");
            yield return CloseView();
            CheckFiles("female display and stale callbacks", seededFiles);

            // Prove Update uses unscaled time, in addition to manually sampling a disabled component.
            float oldScale = Time.timeScale;
            try {
                Time.timeScale = 0;
                yield return Click("Dossier fate inspect");
                yield return Until(() => View != null, "normal sequence opens while gameplay clock paused");
                Require("normal sequence begins unsettled", !View.IsSettled && View.enabled);
                string initial = VisualStamp(View);
                yield return new WaitForSecondsRealtime(.8f);
                Require("real unscaled Update changes submitted presentation", initial != VisualStamp(View));
                yield return Until(() => View != null && View.IsSettled, "real sequence settles with timeScale zero", InnateFateView.Duration + 2);
                CheckDisplay(View, female, "normal settled");
                yield return CloseView();
            } finally { Time.timeScale = oldScale; }

            bool oldReduced = Settings.reduceMotion;
            try {
                Settings.reduceMotion = true;
                yield return Click("Dossier fate inspect");
                yield return Until(() => View != null, "low-motion view opens");
                Require("low motion immediately settles", View.IsSettled);
                string reduced = VisualStamp(View);
                yield return new WaitForSecondsRealtime(.5f);
                Require("low motion has no ongoing visual drift", reduced == VisualStamp(View));
                yield return Click("Innate fate replay");
                Require("low-motion replay remains settled", View.IsSettled);
                View.enabled = false; View.Sample(0, true); string sample = VisualStamp(View);
                View.Sample(100, true);
                Require("all reduced sample times produce the final frame", View.IsSettled && sample == VisualStamp(View));
                yield return CloseView();
            } finally { Settings.reduceMotion = oldReduced; }
            CheckFiles("normal and low-motion playback", seededFiles);

            // The public entry receives this very object, allowing reference and timestamp checks.
            var sparse = Clone(male); sparse.oneTimeFlags = Array.Empty<string>(); sparse.MarkFlag(QingshiStory.FlagPrefix + "livelihood");
            var witness = new DataWitness(sparse);
            Require("sparse archive opens without unlocking roots", host.ShowInnateFate(sparse));
            yield return Until(() => View != null, "sparse view exists");
            View.enabled = false; View.RevealAll(); CheckDisplay(View, sparse, "isolated livelihood");
            View.Replay(); View.Sample(InnateFateView.Duration, false);
            yield return CloseView();
            witness.CheckUnchanged("sparse direct entry");
            CheckFiles("sparse direct entry does not save", seededFiles);

            yield return UnreadableAndDraftChecks();
            PreviewLayouts();
            yield return IdentityAndSwitchChecks();
            Require("at most three compressed GameView screenshots", state.screenshots.Count == 3);
            AssertScratch(true);
        }
        static void Seed()
        {
            AssertScratch(true);
            var preset = Resources.LoadAll<CharacterPreset>("Characters").OrderBy(p => p.id, StringComparer.Ordinal).First();
            BindAsset(preset);
            var ids = CharacterGen.Traits.Select(t => t.Id).ToArray();
            Require("official trait catalog supplies deterministic fixtures", ids.Length >= 4);
            male = host.Repository.Create(1, preset.id, "观命行客", "male", 0, ids.Take(2).ToArray(), CharacterGen.Destinies[0].Id);
            female = host.Repository.Create(2, preset.id, "照水行者", "female", 0, ids.Skip(2).Take(2).ToArray(), CharacterGen.Destinies[1].Id);
            female.MarkFlag(QingshiStory.FlagPrefix + "home"); female.MarkFlag(QingshiStory.FlagPrefix + "livelihood");
            host.Repository.Write(male); host.Repository.Write(female);
            Require("seeded archives have actual backups", File.Exists(ScratchFile("slot-1.json.bak")) && File.Exists(ScratchFile("slot-2.json.bak")));
            Require("root vision fixture is still mortal", !PrologueRules.CanCultivate(female));
            seededFiles = DirectoryEntries(state.scratch);
        }
        static void PrefixChecks()
        {
            Require("duration contract", Mathf.Approximately(InnateFateView.Duration, 3.8f));
            Require("null archive reveals nothing", !InnateFateView.RootsRevealed(null));
            var data = Clone(male); data.oneTimeFlags = Array.Empty<string>();
            CheckPrefix(data, false, "fresh");
            data.MarkFlag(QingshiStory.FlagPrefix + "home"); CheckPrefix(data, false, "home only");
            data.oneTimeFlags = new[] { QingshiStory.FlagPrefix + "livelihood" }; CheckPrefix(data, false, "isolated livelihood");
            data.oneTimeFlags = new[] { PrologueRules.DepartureFlag, PrologueRules.InitiationFlag };
            Require("legacy cultivation flags really enable cultivation", PrologueRules.CanCultivate(data));
            CheckPrefix(data, false, "legacy cultivation cannot reveal unseen roots");
            CheckPrefix(female, true, "continuous home and livelihood");
        }
        static void CheckPrefix(SaveData data, bool expected, string label)
        {
            var before = new DataWitness(data);
            for (int i = 0; i < 3; i++) Require(label + " read-only prefix query " + i, InnateFateView.RootsRevealed(data) == expected);
            before.CheckUnchanged(label);
        }
        static IEnumerator SampleChecks(InnateFateView view, SaveData data, string label)
        {
            Require(label + " manual sampling disables Update", !view.enabled);
            var witness = new DataWitness(data); var traits = view.TraitIds;
            view.Sample(0, false); string first = VisualStamp(view);
            view.Sample(InnateFateView.Duration * .5f, false); string middle = VisualStamp(view);
            view.Sample(InnateFateView.Duration, false); string end = VisualStamp(view);
            Require(label + " normal timeline visibly changes", first != middle && middle != end && view.IsSettled);
            view.Sample(InnateFateView.Duration * .5f, false); string repeated = VisualStamp(view);
            Require(label + " reverse sampling returns to identical frame", repeated == middle);
            yield return Frames(4);
            view.Sample(InnateFateView.Duration * .5f, false);
            Require(label + " identical sample does not drift across frames", repeated == VisualStamp(view));
            view.RevealAll();
            CheckDisplay(view, data, label);
            Require(label + " trait list retained through sampling", traits.SequenceEqual(view.TraitIds));
            witness.CheckUnchanged(label);
        }
        static void CheckDisplay(InnateFateView view, SaveData data, string label)
        {
            bool known = QingshiStory.CanReplay(data, "livelihood");
            Require(label + " visible roots follow consecutive story prefix", view.HasRootVision == known &&
                view.RootTitle == (known ? "坎离水火双极灵根" : "五行未测"));
            Require(label + " settled state", view.IsSettled);
            Require(label + " reads exact input trait IDs", view.TraitIds.SequenceEqual(data.traits ?? Array.Empty<string>()));
            Require(label + " trait IDs are not the input mutable array", !ReferenceEquals(view.TraitIds, data.traits));
            string allText = TextUnder(view.transform);
            Require(label + " root title is actually rendered", allText.Contains(view.RootTitle));
            foreach (string element in Elements) {
                var node = Find(view.transform, "Root node " + element);
                Require(label + " five-element node " + element, node != null && node.gameObject.activeInHierarchy);
                var status = Find(view.transform, "Root node " + element + " status")?.GetComponent<TMP_Text>();
                string expected = !known ? "未测" : element == "water" || element == "fire" ? "显影" : "未显";
                Require(label + " " + element + " status", status != null && status.text == expected);
            }
            if (!known) Require(label + " unknown view has no water-fire revelation", !allText.Contains("坎离水火双极灵根"));
            var expectedTraits = data.traits ?? Array.Empty<string>();
            for (int i = 0; i < expectedTraits.Length; i++) {
                var entry = CharacterGen.FindTrait(expectedTraits[i]);
                var row = Find(view.transform, "Innate trait " + i);
                Require(label + " actual trait row " + i, entry != null && row != null);
                string text = TextUnder(row);
                Require(label + " trait name/description from CharacterGen " + i, text.Contains(entry.Name) && text.Contains(entry.Description));
            }
            Require(label + " dynamic trait row count", view.GetComponentsInChildren<Transform>(true).Count(t => Regex.IsMatch(t.name, @"\AInnate trait \d+\z")) == expectedTraits.Length);
        }
        static IEnumerator UnreadableAndDraftChecks()
        {
            string path = ScratchFile("slot-3.json");
            File.WriteAllText(path, "{\"schemaVersion\":" + (SaveData.CurrentSchemaVersion + 1) + "}");
            var futureFiles = DirectoryEntries(state.scratch);
            yield return SelectSlot(3);
            Require("future save really classified as future", host.Repository.Read(3).State == SlotState.FutureVersion);
            Require("future archive disables inspect", !ButtonNamed("Dossier fate inspect").IsInteractable());
            Require("future archive cannot open view", View == null);
            CheckFiles("future archive is retained byte for byte", futureFiles);
            File.WriteAllText(path, "{broken-innate-fixture");
            var corruptFiles = DirectoryEntries(state.scratch);
            yield return SelectSlot(1); yield return SelectSlot(3);
            Require("broken save really classified as corrupt", host.Repository.Read(3).State == SlotState.Corrupt);
            Require("corrupt archive disables inspect", !ButtonNamed("Dossier fate inspect").IsInteractable());
            CheckFiles("corrupt archive is retained byte for byte", corruptFiles);
            File.Delete(ScratchFile("slot-3.json")); // Only this fixture-created, fully constrained scratch file.
            yield return SelectSlot(1); yield return SelectSlot(3);
            var traits = Field<string[]>(host, "draftTraits"); string[] ids = traits.ToArray();
            string destiny = Field<string>(host, "draftDestiny"); var files = DirectoryEntries(state.scratch);
            yield return Click("Draft gender female");
            Require("draft female switch preserves traits reference and destiny", ReferenceEquals(traits, Field<string[]>(host, "draftTraits")) &&
                ids.SequenceEqual(Field<string[]>(host, "draftTraits")) && destiny == Field<string>(host, "draftDestiny"));
            bool prior = Settings.reduceMotion;
            try {
                Settings.reduceMotion = true;
                yield return Click("Dossier fate inspect");
                yield return Until(() => View != null, "draft inspect opens");
                Require("draft opens immediately settled in low motion", View.IsSettled);
                var expected = new SaveData { gender = "female", traits = ids, oneTimeFlags = Array.Empty<string>() };
                CheckDisplay(View, expected, "draft female"); CheckFigure(View, "female");
                CheckLayout(View, null, Screen.width, Screen.height, "actual draft female");
                yield return Capture("03-draft-female-static.jpg");
                yield return CloseView();
            } finally { Settings.reduceMotion = prior; }
            yield return Click("Draft gender male");
            Require("draft gender round trip does not reroll", ReferenceEquals(traits, Field<string[]>(host, "draftTraits")) &&
                ids.SequenceEqual(Field<string[]>(host, "draftTraits")) && destiny == Field<string>(host, "draftDestiny"));
            Require("draft remains unsaved", host.Repository.Read(3).State == SlotState.Empty);
            CheckFiles("draft inspecting and gender switches preserve all files", files);
        }
        static IEnumerator IdentityAndSwitchChecks()
        {
            yield return SelectSlot(1); yield return Click("Slot enter");
            yield return Until(() => host.CurrentPage == "town" && host.ActiveSave?.slot == 1 && host.HudDaoWheelForCheck != null &&
                !host.HudDaoWheelForCheck.PendingActivation && (bool)Invoke(host, "CanUseHudDaoWheel"), "real save enters town and identity wheel is ready", 25);
            yield return ClickButton(host.HudDaoWheelForCheck.SlotButton(6));
            yield return Until(() => Find(canvas.transform, "Dao identity fate inspect") != null, "real HUD identity opens inspect entry", 15);
            yield return Click("Dao identity fate inspect");
            yield return Until(() => View != null, "identity entry opens fate view");
            var active = host.ActiveSave; var before = new DataWitness(active); var files = DirectoryEntries(state.scratch);
            View.enabled = false; View.RevealAll(); CheckDisplay(View, active, "identity");
            yield return Click("Innate fate replay"); yield return Click("Innate fate skip");
            before.CheckUnchanged("identity reveal/replay/skip"); CheckFiles("identity animation writes no archive or backup", files);
            yield return CloseView();
            Require("identity close returns to identity", Find(canvas.transform, "Dao identity fate inspect") != null);
            yield return Click("Dao identity fate inspect"); yield return Until(() => View != null, "identity reopens for switch interruption");
            var old = View; var late = ButtonNamed("Innate fate close").onClick;
            // Exercise the real public slot-load lifecycle while an old reveal is still alive.
            host.LoadSlot(host.Repository.Read(2));
            yield return Until(() => host.ActiveSave?.slot == 2 && host.CurrentPage == "town" && old == null && View == null,
                "loading another archive destroys old reveal", 20);
            yield return Until(() => !(bool)Invoke(host, "get_WorldDialogueInputBlocked"), "new archive input is ready", 10);
            Require("new archive can open its own reveal", host.ShowInnateFate(host.ActiveSave));
            yield return Until(() => View != null, "new archive owns reveal");
            var current = View; View.enabled = false; View.RevealAll(); string stable = VisualStamp(current);
            var active2 = host.ActiveSave; var afterSwitch = new DataWitness(active2); files = DirectoryEntries(state.scratch);
            late.Invoke(); yield return Frames(2);
            Require("old identity-close callback cannot affect new archive", View == current && host.ActiveSave == active2 && VisualStamp(View) == stable);
            afterSwitch.CheckUnchanged("stale callback on new archive"); CheckFiles("stale callback does not save new archive", files);
            yield return CloseView();
        }


        static IEnumerator SelectSlot(int slot)
        {
            yield return Click("Slot " + slot);
            yield return Until(() => host.SelectedMemoSlot == slot && Find(canvas.transform, "Dossier fate inspect") != null, "slot " + slot + " dossier ready");
            yield return Frames(3);
        }
        static IEnumerator CloseView()
        {
            var previous = View;
            yield return Click("Innate fate close");
            yield return Until(() => View == null && previous == null, "close destroys the owned fate view");
            yield return Frames(2);
        }
        static IEnumerator Click(string name)
        {
            yield return Until(() => ButtonNamed(name, false) != null && ButtonNamed(name, false).IsActive(), name + " exists");
            yield return ClickButton(ButtonNamed(name));
        }
        static IEnumerator ClickButton(Button button)
        {
            Require("real control exists", button != null);
            string name = button.name;
            yield return Until(() => Field<CanvasGroup>(host, "contentFade").alpha > .99f, name + " page ready");
            yield return null;
            Require(name + " interactable", button != null && button.IsActive() && button.IsInteractable());
            AssertScratch(true); Canvas.ForceUpdateCanvases();
            var rect = (RectTransform)button.transform;
            var point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            var pointer = new PointerEventData(EventSystem.current) { position = point, button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
            Require(name + " actual UGUI pointer hit", hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == button,
                "top=" + (hits.Count > 0 ? hits[0].gameObject.name : "none"));
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            yield return null;
            Require(name + " still live on release", button != null && button.IsActive() && button.IsInteractable());
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
            if (button != null && button.gameObject.activeInHierarchy) ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerExitHandler);
            yield return null;
        }
        static Button ButtonNamed(string name, bool required = true)
        {
            var matches = canvas.GetComponentsInChildren<Button>(true).Where(b => b.name == name && b.gameObject.activeInHierarchy).ToArray();
            if (required) Require("unique live button " + name, matches.Length == 1, "count=" + matches.Length);
            return matches.Length == 1 ? matches[0] : null;
        }
        static IEnumerator Until(Func<bool> ready, string name, double seconds = 8)
        {
            double end = Time.realtimeSinceStartupAsDouble + seconds;
            while (!ready() && Time.realtimeSinceStartupAsDouble < end) yield return null;
            Require(name, ready());
        }
        static IEnumerator Frames(int count) { for (int i = 0; i < count; i++) yield return null; }
        static Transform Find(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name && t.gameObject.activeInHierarchy);
        static string TextUnder(Transform root) => string.Join("\n", root.GetComponentsInChildren<TMP_Text>(true).Select(t => t.text));
        static SaveData Clone(SaveData data) => JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(data));
        static object Invoke(object target, string name, params object[] arguments)
        {
            var method = target.GetType().GetMethod(name, Fields);
            if (method == null) throw new MissingMethodException(target.GetType().FullName, name);
            return method.Invoke(target, arguments);
        }
        static string ScratchFile(string name)
        {
            AssertScratch(true);
            if (name != Path.GetFileName(name)) throw new InvalidOperationException("Scratch filename must be a leaf.");
            string path = Full(Path.Combine(state.scratch, name));
            if (!Within(path, state.scratch)) throw new InvalidOperationException("Scratch file escaped its root.");
            RejectLinks(path); return path;
        }
        static void CheckFiles(string label, string[] baseline) => Require(label, baseline.SequenceEqual(DirectoryEntries(state.scratch)), "File inventory, SHA-256 and UTC file timestamps including .bak.");
        static void BindAsset(UnityEngine.Object asset)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            Require("real project asset " + asset.name, !string.IsNullOrEmpty(path) && path.StartsWith("Assets/", StringComparison.Ordinal));
            Bind(path); if (File.Exists(Path.Combine(state.project, path + ".meta"))) Bind(path + ".meta");
        }

        static void PrepareGraphics(InnateFateView view)
        {
            Canvas.ForceUpdateCanvases();
            foreach (var text in view.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate(true);
            foreach (var graphic in view.GetComponentsInChildren<Graphic>(false)) {
                graphic.SetVerticesDirty(); graphic.Rebuild(CanvasUpdate.PreRender);
            }
            Canvas.ForceUpdateCanvases();
        }
        static string VisualStamp(InnateFateView view)
        {
            PrepareGraphics(view);
            var buffer = new StringBuilder();
            Action<float> number = n => buffer.Append(n.ToString("F4", CultureInfo.InvariantCulture)).Append(',');
            foreach (var rect in view.GetComponentsInChildren<RectTransform>(true)) {
                buffer.Append(rect.name).Append(':').Append(rect.gameObject.activeSelf).Append(';');
                number(rect.anchoredPosition.x); number(rect.anchoredPosition.y); number(rect.rect.width); number(rect.rect.height);
                number(rect.localScale.x); number(rect.localScale.y); number(rect.localRotation.z); number(rect.localRotation.w);
                var group = rect.GetComponent<CanvasGroup>(); if (group != null) number(group.alpha);
            }
            foreach (var graphic in view.GetComponentsInChildren<Graphic>(true)) {
                buffer.Append(graphic.name).Append(':').Append(graphic.enabled).Append(';');
                number(graphic.color.r); number(graphic.color.g); number(graphic.color.b); number(graphic.color.a);
                if (graphic is TMP_Text text) buffer.Append(text.text).Append(';');
                if (graphic is RawImage image) { number(image.uvRect.x); number(image.uvRect.y); number(image.uvRect.width); number(image.uvRect.height); }
                var mesh = graphic.canvasRenderer.GetMesh();
                if (mesh == null) continue;
                buffer.Append(mesh.vertexCount).Append(';');
                foreach (var vertex in mesh.vertices) { number(vertex.x); number(vertex.y); number(vertex.z); }
                foreach (var color in mesh.colors32) buffer.Append(color.r).Append(',').Append(color.g).Append(',').Append(color.b).Append(',').Append(color.a).Append(';');
            }
            // Selectable tint tweening is independent of Sample; compare submitted geometry/base colors, not its wall-clock crossfade.
            return Hash(Encoding.UTF8.GetBytes(buffer.ToString()));
        }
        static void CheckFigure(InnateFateView view, string gender)
        {
            var poster = view.GetComponentInChildren<PosterCardView>(true);
            Require(gender + " official full figure exists", poster != null && poster.HasArtwork && poster.IsCutout);
            Require(gender + " matching official figure resource", poster.ResourcePath == "Art/Characters/Qingshi/qingshi-" + gender + "-figure" &&
                poster.Artwork.texture == Resources.Load<Texture2D>(poster.ResourcePath));
            Require(gender + " full UV and no picture frame", poster.Crop == new Rect(0, 0, 1, 1) && poster.Frame == null);
            BindAsset(poster.Artwork.texture);
            var body = poster.Artwork.rectTransform.rect; var bounds = ((RectTransform)poster.transform).rect;
            Require(gender + " full undistorted figure fits", Mathf.Abs(body.width / body.height - poster.Artwork.texture.width / (float)poster.Artwork.texture.height) < .002f &&
                body.width <= bounds.width + .5f && body.height <= bounds.height + .5f);
            var small = ReadTexture(poster.Artwork.texture, 128, 128, true);
            try {
                var pixels = small.GetPixels32();
                Require(gender + " decoded figure really has cutout alpha", pixels.Count(c => c.a < 16) > pixels.Length / 20 && pixels.Count(c => c.a > 220) > pixels.Length / 20);
            } finally { UnityEngine.Object.DestroyImmediate(small); }
        }
        static void CheckLayout(InnateFateView view, Camera camera, int width, int height, string label)
        {
            PrepareGraphics(view);
            var design = Find(view.transform, "Innate fate design") as RectTransform;
            Require(label + " design exists", design != null);
            CheckBounds(design, camera, width, height, label + " complete design");
            foreach (var text in view.GetComponentsInChildren<TMP_Text>(false)) {
                if (string.IsNullOrEmpty(text.text)) continue;
                text.ForceMeshUpdate();
                Require(label + " text fits " + text.name, !text.isTextOverflowing && text.textBounds.size.x <= text.rectTransform.rect.width + 2 &&
                    text.textBounds.size.y <= text.rectTransform.rect.height + 2,
                    "font=" + text.fontSize + "; text=" + text.textBounds.size + "; rect=" + text.rectTransform.rect.size);
                Require(label + " text submits geometry " + text.name, text.textInfo.characterCount > 0 && text.textInfo.meshInfo.Any(m => m.vertexCount > 0));
                CheckBounds(text.rectTransform, camera, width, height, label + " text on screen " + text.name);
            }
            var buttons = view.GetComponentsInChildren<Button>(false);
            for (int i = 0; i < buttons.Length; i++) {
                var a = ScreenRect((RectTransform)buttons[i].transform, camera);
                Require(label + " visible click area " + buttons[i].name, a.width >= 40 && a.height >= 30);
                for (int j = i + 1; j < buttons.Length; j++) Require(label + " controls do not overlap " + buttons[i].name + "/" + buttons[j].name,
                    !a.Overlaps(ScreenRect((RectTransform)buttons[j].transform, camera)));
            }
            CheckAuthoredArt(view, design, camera, label);
            CheckRearrangedLayout(view, design, camera, width, height, label);
        }
        static void CheckRearrangedLayout(InnateFateView view, RectTransform design, Camera camera, int width, int height, string label)
        {
            var stage = ScreenRect(design, camera);
            var poster = view.GetComponentInChildren<PosterCardView>(true);
            Require(label + " full figure is actually visible", poster != null && poster.Artwork != null && VisibleGraphic(poster.Artwork));
            var figure = ScreenRect(poster.Artwork.rectTransform, camera);
            CheckBounds(poster.Artwork.rectTransform, camera, width, height, label + " complete figure on screen");
            Require(label + " large figure occupies the left of the design", figure.center.x < stage.center.x &&
                figure.xMax <= stage.center.x + 2 && figure.height >= stage.height * .60f && poster.Crop == new Rect(0, 0, 1, 1),
                "figure=" + figure + "; design=" + stage);
            var content = new List<RectTransform>();
            foreach (string element in Elements) {
                var node = Find(view.transform, "Root node " + element) as RectTransform;
                Require(label + " root node has visible submitted geometry " + element,
                    node != null && node.GetComponentsInChildren<Graphic>(false).Any(VisibleGraphic));
                content.Add(node);
            }
            content.AddRange(view.GetComponentsInChildren<RectTransform>(false).Where(t => Regex.IsMatch(t.name, @"\AInnate trait \d+\z")));
            var destiny = Find(view.transform, "Innate destiny title") as RectTransform;
            Require(label + " destiny title participates in right-side layout", destiny != null);
            content.Add(destiny);
            var contentBoxes = content.Select(rect => ScreenRect(rect, camera)).ToArray();
            Require(label + " destiny roots and traits occupy the right of the design",
                (contentBoxes.Min(box => box.xMin) + contentBoxes.Max(box => box.xMax)) * .5f > stage.center.x);
            for (int i = 0; i < content.Count; i++) {
                var rect = content[i]; var box = ScreenRect(rect, camera);
                CheckBounds(rect, camera, width, height, label + " content on screen " + rect.name);
                Require(label + " content stays right of figure " + rect.name, box.xMin >= figure.xMax - 2 && !box.Overlaps(figure),
                    "content=" + box + "; figure=" + figure);
                for (int j = i + 1; j < content.Count; j++)
                    Require(label + " right content does not overlap " + rect.name + "/" + content[j].name,
                        !box.Overlaps(ScreenRect(content[j], camera)));
                foreach (var button in view.GetComponentsInChildren<Button>(false))
                    Require(label + " content leaves click areas clear " + rect.name + "/" + button.name,
                        !box.Overlaps(ScreenRect((RectTransform)button.transform, camera)));
            }
        }
        static bool VisibleGraphic(Graphic graphic)
        {
            if (graphic == null || !graphic.isActiveAndEnabled || graphic.color.a <= .01f || graphic.canvasRenderer.cull) return false;
            if (graphic.GetComponentsInParent<CanvasGroup>(true).Any(g => g.alpha <= .01f)) return false;
            var mesh = graphic.canvasRenderer.GetMesh();
            return mesh != null && mesh.vertexCount >= 3 && mesh.triangles.Length >= 3 && mesh.colors32.Any(c => c.a > 0);
        }
        static void CheckAuthoredArt(InnateFateView view, RectTransform design, Camera camera, string label)
        {
            var landscape = Find(view.transform, "Innate fate authored landscape")?.GetComponent<RawImage>();
            CheckRequiredArt(landscape, "nov27/innate-scroll-stage", "Art/UI/Nov27/innate-scroll-stage", label + " landscape");
            var stage = ScreenRect(design, camera); var background = ScreenRect(landscape.rectTransform, camera);
            Require(label + " authored landscape covers the design", Mathf.Abs(stage.xMin - background.xMin) <= 2 &&
                Mathf.Abs(stage.yMin - background.yMin) <= 2 && Mathf.Abs(stage.xMax - background.xMax) <= 2 &&
                Mathf.Abs(stage.yMax - background.yMax) <= 2, "landscape=" + background + "; design=" + stage);
            for (int i = 0; i < view.TraitIds.Count; i++) {
                var card = Find(view.transform, "Innate trait " + i)?.GetComponent<RawImage>();
                CheckRequiredArt(card, "nov27/innate-fate-card", "Art/UI/Nov27/innate-fate-card", label + " trait " + i);
                var bounds = card.rectTransform.rect;
                Require(label + " authored card keeps full UV and original proportion " + i,
                    card.uvRect == new Rect(0, 0, 1, 1) && bounds.height > 0 &&
                    Mathf.Abs(bounds.width / bounds.height - card.texture.width / (float)card.texture.height) < .002f,
                    "rect=" + bounds.size + "; texture=" + card.texture.width + "x" + card.texture.height);
            }
        }
        static void CheckRequiredArt(RawImage image, string catalogueId, string resource, string label)
        {
            string path = "Assets/Resources/" + resource + ".png";
            Require(label + " required PNG and meta exist", File.Exists(Path.Combine(state.project, path)) &&
                File.Exists(Path.Combine(state.project, path + ".meta")), path);
            Bind(path); Bind(path + ".meta");
            var texture = Resources.Load<Texture2D>(resource);
            Require(label + " required PNG really loads", texture != null && AssetDatabase.GetAssetPath(texture) == path, resource);
            Require(label + " approved catalogue resolves the same PNG", ApprovedUiArt.Has(catalogueId) && ApprovedUiArt.Load(catalogueId) == texture, catalogueId);
            Require(label + " named RawImage uses the real PNG with visible submitted geometry",
                image != null && image.texture == texture && VisibleGraphic(image), resource);
        }

        static Rect ScreenRect(RectTransform rect, Camera camera)
        {
            var points = new Vector3[4]; rect.GetWorldCorners(points);
            var projected = points.Select(p => RectTransformUtility.WorldToScreenPoint(camera, p)).ToArray();
            return Rect.MinMaxRect(projected.Min(p => p.x), projected.Min(p => p.y), projected.Max(p => p.x), projected.Max(p => p.y));
        }
        static void CheckBounds(RectTransform rect, Camera camera, int width, int height, string label)
        {
            var box = ScreenRect(rect, camera);
            Require(label, box.xMin >= -2 && box.yMin >= -2 && box.xMax <= width + 2 && box.yMax <= height + 2, box + " in " + width + "x" + height);
        }
        static void PreviewLayouts()
        {
            // Real entry/click tests above remain on FrontEnd. Preview scenes only exercise deterministic viewport geometry.
            const string longCharacterName = "一二三四五六七八九十甲乙丙丁戊己";
            foreach (var preview in new[] {
                (size: new Vector2Int(1280, 720), longName: false),
                (size: new Vector2Int(1920, 1080), longName: false),
                (size: new Vector2Int(2560, 1440), longName: false),
                (size: new Vector2Int(1920, 1080), longName: true)
            }) {
                var size = preview.size; string label = size + (preview.longName ? " 16-character name preview" : " preview");
                Scene scene = default; RenderTexture target = null;
                try {
                    scene = EditorSceneManager.NewPreviewScene();
                    var cameraObject = new GameObject("Innate preview camera", typeof(Camera)); SceneManager.MoveGameObjectToScene(cameraObject, scene);
                    var camera = cameraObject.GetComponent<Camera>(); camera.enabled = false; camera.scene = scene;
                    camera.orthographic = true; camera.orthographicSize = size.y * .5f; camera.aspect = size.x / (float)size.y;
                    camera.transform.position = new Vector3(0, 0, -10); camera.nearClipPlane = .1f; camera.farClipPlane = 100;
                    camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                    target = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32); target.Create(); camera.targetTexture = target;
                    var root = new GameObject("Innate preview canvas", typeof(RectTransform), typeof(Canvas)); SceneManager.MoveGameObjectToScene(root, scene);
                    var previewCanvas = root.GetComponent<Canvas>(); previewCanvas.renderMode = RenderMode.WorldSpace; previewCanvas.worldCamera = camera;
                    var parent = (RectTransform)root.transform; parent.sizeDelta = size;
                    var holder = new GameObject("Innate preview holder", typeof(RectTransform), typeof(InnateFateView)); holder.transform.SetParent(parent, false);
                    var rect = (RectTransform)holder.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
                    var view = holder.GetComponent<InnateFateView>(); view.enabled = false;
                    var input = Clone(size.x == 1280 ? male : female);
                    if (preview.longName) input.characterName = longCharacterName;
                    var witness = new DataWitness(input);
                    view.Configure(Field<UiKit>(host, "ui"), rect, input, () => false, () => { }, () => { });
                    view.Sample(InnateFateView.Duration, false);
                    CheckDisplay(view, input, label); CheckLayout(view, camera, size.x, size.y, label);
                    if (preview.longName) {
                        var name = Find(view.transform, "Innate fate character name")?.GetComponent<TMP_Text>();
                        Require(label + " preserves the complete input name", name != null && name.text == longCharacterName);
                        name.ForceMeshUpdate();
                        Require(label + " renders all sixteen original glyphs", name.textInfo.characterCount == longCharacterName.Length &&
                            Enumerable.Range(0, longCharacterName.Length).All(i => {
                                var character = name.textInfo.characterInfo[i];
                                return character.character == longCharacterName[i] && character.isVisible && character.textElement != null &&
                                    character.textElement.unicode == longCharacterName[i];
                            }));
                        var bounds = name.textBounds; var nameRect = name.rectTransform.rect;
                        Require(label + " name fits without overflow or clipping", !name.isTextOverflowing &&
                            bounds.min.x >= nameRect.xMin - 2 && bounds.max.x <= nameRect.xMax + 2 &&
                            bounds.min.y >= nameRect.yMin - 2 && bounds.max.y <= nameRect.yMax + 2,
                            "text=" + bounds + "; rect=" + nameRect);
                        var nameBox = ScreenRect(name.rectTransform, camera);
                        var epigraph = Find(view.transform, "Innate fate epigraph")?.GetComponent<TMP_Text>();
                        Require(label + " epigraph remains visible", epigraph != null && VisibleGraphic(epigraph));
                        var epigraphBox = ScreenRect(epigraph.rectTransform, camera);
                        Require(label + " name does not overlap epigraph", !nameBox.Overlaps(epigraphBox),
                            "name=" + nameBox + "; epigraph=" + epigraphBox);
                        var poster = view.GetComponentInChildren<PosterCardView>(true);
                        var figureBox = ScreenRect(poster.Artwork.rectTransform, camera);
                        Require(label + " name does not overlap the actual full figure", !nameBox.Overlaps(figureBox),
                            "name=" + nameBox + "; figure=" + figureBox);
                        foreach (var text in view.GetComponentsInChildren<TMP_Text>(false).Where(t => t != name && t != epigraph && !string.IsNullOrEmpty(t.text))) {
                            var textBox = ScreenRect(text.rectTransform, camera);
                            Require(label + " name does not overlap other text " + text.name, !nameBox.Overlaps(textBox),
                                "name=" + nameBox + "; text=" + textBox);
                        }
                        foreach (var button in view.GetComponentsInChildren<Button>(false)) {
                            var buttonBox = ScreenRect((RectTransform)button.transform, camera);
                            Require(label + " name does not overlap button " + button.name, !nameBox.Overlaps(buttonBox),
                                "name=" + nameBox + "; button=" + buttonBox);
                        }
                    }
                    camera.Render();
                    var frame = ReadTexture(target, 128, 72, false);
                    try { Require(label + " renders actual pixels", frame.GetPixels32().Count(c => Math.Max(c.r, Math.Max(c.g, c.b)) > 20) > 500); }
                    finally { UnityEngine.Object.DestroyImmediate(frame); }
                    witness.CheckUnchanged(label);
                    string id = view.TraitIds[0]; input.traits[0] = CharacterGen.Traits.First(t => !input.traits.Contains(t.Id)).Id;
                    Require(label + " view owns a trait snapshot", view.TraitIds[0] == id);
                    foreach (var art in view.GetComponentsInChildren<RawImage>(true)) if (art.texture != null) BindAsset(art.texture);
                } finally {
                    if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                    if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
                }
            }
        }
        static Texture2D ReadTexture(Texture source, int width, int height, bool alpha = false)
        {
            var previous = RenderTexture.active; var temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            Texture2D result = null;
            try {
                Graphics.Blit(source, temporary); RenderTexture.active = temporary;
                result = new Texture2D(width, height, alpha ? TextureFormat.RGBA32 : TextureFormat.RGB24, false);
                result.ReadPixels(new Rect(0, 0, width, height), 0, 0); result.Apply(); return result;
            } catch { if (result != null) UnityEngine.Object.DestroyImmediate(result); throw; }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(temporary); }
        }

        // ScreenCapture bytes are already the capture result. Resize those bytes directly;
        // the asset/PreviewLayouts ReadTexture path above deliberately remains unchanged.
        static Texture2D ResizeCaptureBytes(Color32[] source, int sourceWidth, int sourceHeight, int width, int height)
        {
            var pixels = new Color32[width * height];
            float scaleX = sourceWidth / (float)width, scaleY = sourceHeight / (float)height;
            for (int y = 0; y < height; y++) {
                float sy = Mathf.Clamp((y + .5f) * scaleY - .5f, 0, sourceHeight - 1);
                int y0 = (int)sy, y1 = Math.Min(y0 + 1, sourceHeight - 1); float ty = sy - y0;
                for (int x = 0; x < width; x++) {
                    float sx = Mathf.Clamp((x + .5f) * scaleX - .5f, 0, sourceWidth - 1);
                    int x0 = (int)sx, x1 = Math.Min(x0 + 1, sourceWidth - 1); float tx = sx - x0;
                    var a = source[y0 * sourceWidth + x0]; var b = source[y0 * sourceWidth + x1];
                    var c = source[y1 * sourceWidth + x0]; var d = source[y1 * sourceWidth + x1];
                    pixels[y * width + x] = new Color32(
                        CaptureByte(a.r, b.r, c.r, d.r, tx, ty), CaptureByte(a.g, b.g, c.g, d.g, tx, ty),
                        CaptureByte(a.b, b.b, c.b, d.b, tx, ty), 255);
                }
            }
            var result = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try {
                // SetPixels32 and EncodeToJPG use the CPU copy; no Apply/Blit/RenderTexture conversion.
                result.SetPixels32(pixels); return result;
            } catch { UnityEngine.Object.DestroyImmediate(result); throw; }
        }
        static byte CaptureByte(byte a, byte b, byte c, byte d, float x, float y)
            => (byte)Mathf.RoundToInt(Mathf.Lerp(Mathf.Lerp(a, b, x), Mathf.Lerp(c, d, x), y));

        static CaptureColorSample[] CaptureColors(string file, Color32[] source, int sourceWidth, int sourceHeight,
            Texture2D resized, byte[] jpeg)
        {
            // The outer 1% stays clear of titles, figures, the ring and controls. Texture arrays
            // use a bottom-left origin; map each output pixel center back to its raw source pixel.
            var points = new[] { new Vector2(.01f, .99f), new Vector2(.99f, .99f), new Vector2(.01f, .5f),
                new Vector2(.99f, .5f), new Vector2(.01f, .01f) };
            var names = new[] { "top-left", "top-right", "left-middle", "right-middle", "bottom-left" };
            var samples = new CaptureColorSample[points.Length];
            var decoded = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try {
                Require(file + " JPEG decodes at delivered size", decoded.LoadImage(jpeg, false) &&
                    decoded.width == resized.width && decoded.height == resized.height);
                var resizedPixels = resized.GetPixels32(); var jpegPixels = decoded.GetPixels32();
                for (int i = 0; i < points.Length; i++) {
                    int x = Mathf.RoundToInt(points[i].x * (resized.width - 1)), y = Mathf.RoundToInt(points[i].y * (resized.height - 1));
                    int sx = Mathf.Clamp(Mathf.RoundToInt((x + .5f) * sourceWidth / resized.width - .5f), 0, sourceWidth - 1);
                    int sy = Mathf.Clamp(Mathf.RoundToInt((y + .5f) * sourceHeight / resized.height - .5f), 0, sourceHeight - 1);
                    var a = source[sy * sourceWidth + sx]; var b = resizedPixels[y * resized.width + x]; var c = jpegPixels[y * resized.width + x];
                    var resizedDelta = new[] { Math.Abs(a.r - b.r), Math.Abs(a.g - b.g), Math.Abs(a.b - b.b) };
                    var jpegDelta = new[] { Math.Abs(a.r - c.r), Math.Abs(a.g - c.g), Math.Abs(a.b - c.b) };
                    samples[i] = new CaptureColorSample {
                        name = names[i], sourceX = sx, sourceY = sy, resizedX = x, resizedY = y,
                        sourceRgb = new[] { (int)a.r, a.g, a.b }, resizedRgb = new[] { (int)b.r, b.g, b.b }, jpegRgb = new[] { (int)c.r, c.g, c.b },
                        resizedRgbDelta = resizedDelta, jpegRgbDelta = jpegDelta,
                        resizedMaxRgbDelta = resizedDelta.Max(), jpegMaxRgbDelta = jpegDelta.Max()
                    };
                }
                return samples;
            } finally { UnityEngine.Object.DestroyImmediate(decoded); }
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
                var sourcePixels = screen.GetPixels32();
                small = ResizeCaptureBytes(sourcePixels, screen.width, screen.height, width, height);
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
                    small = ResizeCaptureBytes(sourcePixels, screen.width, screen.height, width, height); jpeg = small.EncodeToJPG(quality);
                }
                Require(file + " compressed preview budget", Math.Max(width, height) <= 1440 && jpeg.Length <= 200 * 1024,
                    width + "x" + height + "; bytes=" + jpeg.Length);
                File.WriteAllBytes(Path.Combine(state.output, file), jpeg);
                var shot = new Shot { file = file, sha256 = Hash(jpeg), page = host.CurrentPage,
                    width = width, height = height, gameViewWidth = screen.width, gameViewHeight = screen.height,
                    quality = quality, bytes = jpeg.Length, unityFrame = Time.frameCount,
                    sourceIsDataSRGB = screen.isDataSRGB, sourceGraphicsFormat = screen.graphicsFormat.ToString(),
                    activeColorSpace = QualitySettings.activeColorSpace.ToString(), canvasVertexColorAlwaysGammaSpace = canvas.vertexColorAlwaysGammaSpace,
                    colorSamples = CaptureColors(file, sourcePixels, screen.width, screen.height, small, jpeg) };
                state.screenshots.Add(shot); Save();
                foreach (var sample in shot.colorSamples)
                    Check(file + " background color fidelity " + sample.name,
                        sample.resizedMaxRgbDelta <= shot.resizedRgbTolerance && sample.jpegMaxRgbDelta <= shot.jpegRgbTolerance,
                        "sourceRGB=" + string.Join(",", sample.sourceRgb) + "; resizedRGB=" + string.Join(",", sample.resizedRgb) +
                        "; jpegRGB=" + string.Join(",", sample.jpegRgb) + "; maxDelta resize/jpeg=" + sample.resizedMaxRgbDelta + "/" + sample.jpegMaxRgbDelta);
            } finally { if (screen != null) UnityEngine.Object.Destroy(screen); if (small != null) UnityEngine.Object.Destroy(small); }
        }

        static void RequestFinish()
        {
            if (state == null || state.phase == "restore") return;
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
                Application.runInBackground = state.oldBackground; Time.timeScale = state.oldTimeScale;
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
                Application.runInBackground = state.oldBackground; Time.timeScale = state.oldTimeScale;
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
                    else entries.Add("file:" + path.Substring(root.Length) + ":" + new FileInfo(path).Length + ":" + File.GetLastWriteTimeUtc(path).Ticks + ":" + Sha(path));
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
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
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
                passed = state.checks.Count(c => c.status == "PASS"), failed = failed,
                checks = state.checks, screenshots = state.screenshots, inputs = state.inputs
            };
            File.WriteAllText(Path.Combine(state.output, "report.json"), JsonUtility.ToJson(report, true));
        }
    }
    internal sealed class InnateFateChecksPump : MonoBehaviour { }
}
