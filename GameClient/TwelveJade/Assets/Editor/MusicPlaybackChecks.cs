using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TwelveJade.Core;
using TwelveJade.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;
using Object = UnityEngine.Object;

namespace TwelveJade.Editor
{
    // ExecuteMethod: TwelveJade.Editor.MusicPlaybackChecks.Run (batch or graphical Editor).
    // Verifies real AudioSource playback, muted legacy settings preservation, UI settings saving,
    // all synthetic bird/insect recovery paths disabled, scene/comic/wilderness routing, pause/resume,
    // and preserved footsteps/UI tones using isolated TWELVEJADE_SAVEDIR.
    [InitializeOnLoad]
    public static class MusicPlaybackChecks
    {
        const string ActiveKey = "MusicPlaybackChecks.Active";
        const string OutDirKey = "MusicPlaybackChecks.OutDir";
        const string ScratchKey = "MusicPlaybackChecks.Scratch";
        const string OldSaveKey = "MusicPlaybackChecks.OldSave";
        const string HadOldSaveKey = "MusicPlaybackChecks.HadOldSave";
        const string OldStartSceneKey = "MusicPlaybackChecks.OldStartScene";
        const string OldBgKey = "MusicPlaybackChecks.OldBg";
        const string ScenePath = "Assets/Scenes/FrontEnd.unity";
        const string ScratchPrefix = "twelve-jade-music-checks-";
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        static readonly List<string> problems = new List<string>();
        static readonly StringBuilder report = new StringBuilder();
        static FrontEndController host;
        static double startedAt;
        static bool active, startedChecks, restoring;
        static string outDir, scratchDir;

        static MusicPlaybackChecks()
        {
            if (!SessionState.GetBool(ActiveKey, false)) return;
            active = true;
            outDir = SessionState.GetString(OutDirKey, "");
            scratchDir = SessionState.GetString(ScratchKey, "");
            startedAt = EditorApplication.timeSinceStartup;
            Attach();
        }

        static void Attach()
        {
            EditorApplication.update -= Pump;
            EditorApplication.update += Pump;
            EditorApplication.playModeStateChanged -= OnPlayStateChanged;
            EditorApplication.playModeStateChanged += OnPlayStateChanged;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
        }

        [MenuItem("Tools/Twelve Jade/Check music playback")]
        public static void Run()
        {
            if (SessionState.GetBool(ActiveKey, false))
                throw new InvalidOperationException("MusicPlaybackChecks is already active.");
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Run MusicPlaybackChecks from Edit mode.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("An edit scene has unsaved changes; refusing to enter Play Mode.");
            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (sceneAsset == null) throw new InvalidOperationException("Missing scene: " + ScenePath);

            string project = Full(Path.Combine(Application.dataPath, ".."));
            string sourceRaw = Environment.GetEnvironmentVariable("PROLOGUE_TOWN_SOURCE_PROJECT");
            if (string.IsNullOrWhiteSpace(sourceRaw) || !Path.IsPathRooted(sourceRaw))
                throw new InvalidOperationException("Set an absolute PROLOGUE_TOWN_SOURCE_PROJECT.");
            string source = Full(sourceRaw);
            string marker = Path.Combine(project, ".prologue-town-shot");
            if (!project.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains(".batch", StringComparer.OrdinalIgnoreCase) ||
                SamePath(project, source) || !File.Exists(marker) || !SamePath(File.ReadAllText(marker).Trim(), source))
                throw new InvalidOperationException("Require a separate .batch project with .prologue-town-shot containing the absolute source Unity project path.");
            if (!File.Exists(Path.Combine(source, ScenePath)))
                throw new InvalidOperationException("Source must contain FrontEnd scene.");

            string repoRoot = Full(Path.Combine(project, "..", ".."));
            string output = Environment.GetEnvironmentVariable("EJS_MUSIC_CHECKS_OUTPUT");
            if (string.IsNullOrWhiteSpace(output))
                output = Path.Combine(repoRoot, "Tools", "screenshots", "MusicPlaybackChecks");
            output = Full(output);
            if (Overlaps(output, project) || Overlaps(output, source))
                throw new InvalidOperationException("Output directory must stay outside both Unity projects.");
            Directory.CreateDirectory(output);

            string scratch = Full(Path.Combine(Path.GetTempPath(), ScratchPrefix + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(scratch);
            // Seed an existing muted settings.json before Awake runs to verify user volume is never force-overwritten.
            File.WriteAllText(Path.Combine(scratch, "settings.json"),
                "{\"schemaVersion\":1,\"masterVolume\":0,\"musicVolume\":0,\"effectsVolume\":0.5," +
                "\"width\":1600,\"height\":900,\"fullscreen\":false,\"reduceMotion\":false}",
                new UTF8Encoding(false));

            string oldSave = Environment.GetEnvironmentVariable("TWELVEJADE_SAVEDIR");
            string oldStart = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) ?? "";
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetString(OutDirKey, output);
            SessionState.SetString(ScratchKey, scratch);
            SessionState.SetString(OldSaveKey, oldSave ?? "");
            SessionState.SetBool(HadOldSaveKey, oldSave != null);
            SessionState.SetString(OldStartSceneKey, oldStart);
            SessionState.SetBool(OldBgKey, Application.runInBackground);

            Environment.SetEnvironmentVariable("TWELVEJADE_SAVEDIR", scratch);
            Application.runInBackground = true;
            EditorSceneManager.playModeStartScene = sceneAsset;

            active = true; startedChecks = false; restoring = false;
            outDir = output; scratchDir = scratch;
            startedAt = EditorApplication.timeSinceStartup;
            Attach();
            EditorApplication.isPlaying = true;
        }

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (!active || restoring || !EditorApplication.isPlaying) return;
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            if (condition.StartsWith("[Licensing::Module]", StringComparison.Ordinal)) return;
            if (stackTrace != null && stackTrace.Contains("UnityEditor.Search")) return;
            if (condition.StartsWith("[MusicPlaybackChecks] FAIL:", StringComparison.Ordinal)) return;
            problems.Add(type + ": " + condition);
        }

        static void OnPlayStateChanged(PlayModeStateChange change)
        {
            if (!active) return;
            if (change == PlayModeStateChange.ExitingPlayMode && !restoring)
            {
                problems.Add("Play Mode exited before checks finished.");
                restoring = true;
            }
            if (change == PlayModeStateChange.EnteredEditMode && restoring)
                FinalizeRun();
        }

        static void Pump()
        {
            if (!active) return;
            EditorApplication.QueuePlayerLoopUpdate();
            if (restoring)
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode) FinalizeRun();
                return;
            }
            if (EditorApplication.timeSinceStartup - startedAt > 120.0)
            {
                problems.Add("Timed out after 120 seconds.");
                BeginRestore();
                return;
            }
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling || startedChecks) return;
            host = Object.FindAnyObjectByType<FrontEndController>();
            if (host == null) return;
            startedChecks = true;
            new GameObject("MusicPlaybackChecksPump").AddComponent<MusicPlaybackChecksPump>().StartCoroutine(RunChecks());
        }

        sealed class MusicPlaybackChecksPump : MonoBehaviour { }

        static IEnumerator RunChecks()
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(VerifyAll());
            while (stack.Count > 0)
            {
                object next = null;
                bool moved;
                try
                {
                    moved = stack.Peek().MoveNext();
                    if (moved) next = stack.Peek().Current;
                }
                catch (Exception ex)
                {
                    problems.Add("Unhandled exception: " + ex);
                    Debug.LogError("[MusicPlaybackChecks] FAIL: " + ex);
                    break;
                }
                if (!moved) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                if (next is IEnumerator nested) stack.Push(nested);
                else yield return next;
            }
            BeginRestore();
        }

        static IEnumerator VerifyAll()
        {
            report.Clear();
            report.AppendLine("MusicPlaybackChecks: real FrontEndController + AudioSource + Audio/Music/*.mp3");

            foreach (string id in new[] { "immortals-sigh", "hidden-stream", "clouds-rest" })
            {
                var clip = Resources.Load<AudioClip>("Audio/Music/" + id);
                Check(clip != null && clip.loadType == AudioClipLoadType.Streaming && clip.channels == 2 && clip.length > 120f,
                    id + " recording loaded as stereo Streaming AudioClip");
            }

            var music = Field<AudioSource>(host, "music");
            var effects = Field<AudioSource>(host, "effects");
            var settings = Field<UserSettings>(host, "settings");
            Check(music != null && effects != null && settings != null, "music/effects AudioSources and loaded settings exist");

            // 1) Old muted settings.json must not be force-overwritten at startup.
            Check(settings.masterVolume == 0f && settings.musicVolume == 0f,
                "muted settings preserved without forcing user volume");
            Check(Mathf.Approximately(AudioListener.volume, 0f) && music.volume == 0f && music.isPlaying,
                "muted startup keeps music silent on AudioListener/AudioSource without stopping clock");
            Check(SamplePeak(music) == 0f, "muted startup produces zero AudioSource output");
            Check(music.clip != null && music.clip.name == "immortals-sigh", "title cue starts on immortals-sigh");

            // 2) All synthetic bird/insect recovery paths must be permanently disabled (inspect real AudioSources).
            CheckNoAmbienceSources("startup");
            host.SetAmbienceKind(AmbienceKind.Bird);
            CheckNoAmbienceSources("after SetAmbienceKind(Bird)");
            host.SetAmbienceKind(AmbienceKind.Insect);
            CheckNoAmbienceSources("after SetAmbienceKind(Insect)");
            host.TickAmbience();
            CheckNoAmbienceSources("after TickAmbience");

            // 3) Footsteps and UI tones remain functional (inspect real AudioClip sample buffers).
            var uiTones = Field<AudioClip[]>(host, "uiTones");
            Check(uiTones != null && uiTones.Length == 4 && uiTones.All(c => ClipAudibleSamples(c) > 0f),
                "4 real UI tone AudioClips synthesized with non-zero waveforms");
            host.SetTravelerFootsteps(true);
            var slowClips = Field<AudioClip[]>(host, "footstepClips");
            var fastClips = Field<AudioClip[]>(host, "footstepClipsFast");
            Check(slowClips != null && slowClips.Length == 2 && slowClips.All(c => ClipAudibleSamples(c) > 0f) &&
                  fastClips != null && fastClips.Length == 2 && fastClips.All(c => ClipAudibleSamples(c) > 0f),
                "footstep slow/fast AudioClips preserved with non-zero waveforms");
            host.SetTravelerFootsteps(false);

            // 4) Settings UI unmutes, persists settings.json, and plays immortals-sigh audibly.
            host.ShowSettings();
            yield return null;
            var masterSlider = FindSlider("Master volume slider");
            var musicSlider = FindSlider("Music volume slider");
            var saveButton = FindButton("Settings save");
            Check(masterSlider != null && musicSlider != null && saveButton != null,
                "settings sliders and save button exist");
            masterSlider.value = .85f;
            musicSlider.value = .65f;
            saveButton.onClick.Invoke();
            yield return null;
            var reloaded = host.Repository.LoadSettings(out _);
            Check(Mathf.Abs(reloaded.masterVolume - .85f) < .001f && Mathf.Abs(reloaded.musicVolume - .65f) < .001f,
                "saved settings persisted accurately to disk");
            Invoke(host, "CloseSettings");
            yield return Until(() => host.CurrentPage == "menu" && !host.DialogueInputLockedForShot, "menu ready after CloseSettings", 5f);
            Check(!effects.mute && effects.volume > 0f, "effects AudioSource remains unmuted with positive gain");
            host.PlayUiTone(UiToneKind.Click);
            yield return VerifyAudible(music, "immortals-sigh", .35f);
            Check(Object.FindObjectsByType<VideoPlayer>(FindObjectsSortMode.None)
                .All(p => p.audioOutputMode == VideoAudioOutputMode.None),
                "menu video soundtrack remains muted");

            // 5) Menu / Slots / Creation / Credits keep immortals-sigh.
            host.ShowSlots(false); yield return null;
            Check(host.MusicCueForCheck == "immortals-sigh", "slots keep immortals-sigh");
            host.ShowCharacterCreation(1); yield return null;
            Check(host.MusicCueForCheck == "immortals-sigh", "character creation keeps immortals-sigh");
            host.ShowCredits(); yield return null;
            Check(host.MusicCueForCheck == "immortals-sigh", "credits keep immortals-sigh");

            // 6) Enter town -> hidden-stream starts audibly; settings preserves town music position.
            var preset = Resources.LoadAll<CharacterPreset>("Characters").OrderBy(p => p.id).First();
            host.Repository.Create(1, preset.id, "青石听音", "male", 0, null, "shaxing");
            host.LoadSlot(host.Repository.Read(1));
            yield return Until(() => host.CurrentPage == "town" && host.TravelerPuppetForShot != null, "town ready", 15f);
            yield return VerifyAudible(music, "hidden-stream", .35f);

            float beforeSettings = music.time;
            host.ShowSettings();
            yield return new WaitForSecondsRealtime(.6f);
            Check(host.MusicCueForCheck == "hidden-stream" && music.time > beforeSettings,
                "opening settings keeps town music advancing");
            Invoke(host, "CloseSettings");
            yield return Until(() => host.CurrentPage == "town" && !host.DialogueInputLockedForShot,
                "town ready and unlocked after CloseSettings", 5f);

            // 7) Story comics: home -> hidden-stream, tomb (Act 3) -> clouds-rest.
            var active = host.ActiveSave;
            foreach (string flag in new[] { "qingshi-v25-home", "qingshi-v25-livelihood", "qingshi-v25-tiger", "qingshi-v25-undercurrent" })
                active.MarkFlag(flag);
            Check(host.ShowQingshiComic("home") && host.QingshiComicForCheck != null && host.QingshiComicForCheck.SceneId == "home",
                "home comic opens");
            yield return new WaitForSecondsRealtime(.6f);
            Check(host.MusicCueForCheck == "hidden-stream", "home comic keeps hidden-stream");
            Invoke(host, "CloseModal");
            yield return Until(() => host.CurrentPage == "town" && host.QingshiComicForCheck == null && !host.DialogueInputLockedForShot,
                "town ready and unlocked after home comic", 5f);

            Check(host.ShowQingshiComic("tomb") && host.QingshiComicForCheck != null && host.QingshiComicForCheck.SceneId == "tomb",
                "tomb comic opens");
            yield return VerifyAudible(music, "clouds-rest", .35f);
            Invoke(host, "CloseModal");
            yield return Until(() => host.CurrentPage == "town" && host.QingshiComicForCheck == null && !host.DialogueInputLockedForShot,
                "town ready and unlocked after tomb comic", 5f);
            yield return VerifyAudible(music, "hidden-stream", .35f);

            // 8) Wilderness region switch & anti-jitter dwell.
            Invoke(host, "CancelTravel");
            var posField = typeof(FrontEndController).GetField("worldPos", Flags);
            var townPos = (Vector2)posField.GetValue(host);
            posField.SetValue(host, new Vector2(900f, 5300f));
            yield return new WaitForSecondsRealtime(.1f);
            posField.SetValue(host, townPos);
            yield return new WaitForSecondsRealtime(.25f);
            Check(host.MusicCueForCheck == "hidden-stream", "brief boundary crossing does not interrupt town music");

            posField.SetValue(host, new Vector2(900f, 5300f));
            yield return VerifyAudible(music, "clouds-rest", .35f);
            posField.SetValue(host, townPos);
            yield return VerifyAudible(music, "hidden-stream", .35f);
            host.TickAmbience();
            Check(!host.AmbiencePlayingForCheck && host.AmbienceClipLengthForCheck == 0,
                "returning to town cannot restart synthetic birds");

            // 9) Pause / Resume compliance.
            host.SetMusicPaused(true);
            Check(host.MusicPausedForCheck && !music.isPlaying, "SetMusicPaused(true) pauses real AudioSource");
            float pausedAt = music.time;
            yield return new WaitForSecondsRealtime(.35f);
            Check(!music.isPlaying && Mathf.Abs(music.time - pausedAt) < .05f,
                "paused music freezes playback position");
            Check(SamplePeak(music) == 0f, "paused AudioSource outputs zero samples");
            host.SetMusicPaused(false);
            Check(!host.MusicPausedForCheck && music.isPlaying, "SetMusicPaused(false) resumes real AudioSource");
            yield return new WaitForSecondsRealtime(.45f);
            float peakAfterResume = SamplePeak(music);
            Check(music.time > pausedAt + .15f && peakAfterResume > .00001f,
                "resumed music advances from paused time with real output (peak=" + peakAfterResume.ToString("G5") + ")");

            // 10) Return to menu -> immortals-sigh & single looping AudioSource invariant across whole scene.
            host.ShowMenu();
            yield return VerifyAudible(music, "immortals-sigh", .35f);
            var allSources = Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
            Check(allSources.Count(s => s.loop && s.isPlaying) == 1 && allSources.Single(s => s.loop && s.isPlaying) == music,
                "exactly one looping AudioSource is playing in scene (music)");
            Check(music.spatialBlend == 0f && music.loop && !music.playOnAwake,
                "music AudioSource properties compliant (2D, looped, no playOnAwake)");
            CheckNoAmbienceSources("final scene sweep");
        }

        static void CheckNoAmbienceSources(string stage)
        {
            var sources = Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
            foreach (var s in sources)
            {
                bool isAmbience = s.clip != null && s.clip.name.StartsWith("Ambience", StringComparison.Ordinal);
                Check(!isAmbience && (!s.loop || s == Field<AudioSource>(host, "music")),
                    stage + ": no ambience or stray looping AudioSource (" + (s.clip != null ? s.clip.name : "null") + ")");
                if (s != Field<AudioSource>(host, "music") && s != Field<AudioSource>(host, "effects"))
                    Check(!s.isPlaying || SamplePeak(s) == 0f, stage + ": unexpected AudioSource not audible (" + s.name + ")");
            }
            Check(!host.AmbiencePlayingForCheck && host.AmbienceClipLengthForCheck == 0,
                stage + ": real Ambience check reports zero");
        }

        static IEnumerator VerifyAudible(AudioSource music, string expectedCue, float minVolume)
        {
            yield return Until(() => music.clip != null && music.clip.name == expectedCue && music.isPlaying && music.volume >= minVolume,
                expectedCue + " playing and faded in", 10f);
            float t0 = music.time;
            float peak = 0f;
            for (int i = 0; i < 8; i++)
            {
                peak = Mathf.Max(peak, SamplePeak(music));
                yield return new WaitForSecondsRealtime(.1f);
            }
            Check(music.time > t0 + .4f, expectedCue + " clock advances (" + t0.ToString("F2") + "->" + music.time.ToString("F2") + ")");
            Check(peak > .00001f, expectedCue + " produces real non-zero AudioSource output (peak=" + peak.ToString("G5") + ")");
            Check(!music.mute && music.volume >= minVolume && AudioListener.volume > 0f,
                expectedCue + " audible volume");
        }

        static float SamplePeak(AudioSource source)
        {
            var buf = new float[1024];
            source.GetOutputData(buf, 0);
            float peak = 0f;
            for (int i = 0; i < buf.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(buf[i]));
            return peak;
        }

        static float ClipAudibleSamples(AudioClip clip)
        {
            if (clip == null || clip.samples <= 0) return 0f;
            var data = new float[clip.samples * clip.channels];
            if (!clip.GetData(data, 0)) return 0f;
            float peak = 0f;
            for (int i = 0; i < data.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            return peak;
        }

        static IEnumerator Until(Func<bool> predicate, string label, float timeoutSeconds)
        {
            double deadline = EditorApplication.timeSinceStartup + timeoutSeconds;
            while (!predicate())
            {
                if (EditorApplication.timeSinceStartup > deadline)
                    throw new TimeoutException("Timed out waiting for: " + label);
                yield return null;
            }
        }

        static Slider FindSlider(string name) =>
            Resources.FindObjectsOfTypeAll<Slider>().FirstOrDefault(s => s.gameObject.scene.IsValid() && s.gameObject.activeInHierarchy && s.gameObject.name == name);

        static Button FindButton(string name) =>
            Resources.FindObjectsOfTypeAll<Button>().FirstOrDefault(b => b.gameObject.scene.IsValid() && b.gameObject.activeInHierarchy && b.gameObject.name == name);

        static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Flags).GetValue(target);
        static void Invoke(object target, string name) => target.GetType().GetMethod(name, Flags).Invoke(target, null);

        static void Check(bool condition, string message)
        {
            if (condition) report.AppendLine("PASS: " + message);
            else
            {
                problems.Add(message);
                report.AppendLine("FAIL: " + message);
                Debug.LogError("[MusicPlaybackChecks] FAIL: " + message);
            }
        }

        static void BeginRestore()
        {
            if (restoring) return;
            restoring = true;
            if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false;
            else FinalizeRun();
        }

        static void FinalizeRun()
        {
            if (!active) return;
            active = false;
            EditorApplication.update -= Pump;
            EditorApplication.playModeStateChanged -= OnPlayStateChanged;
            Application.logMessageReceived -= OnLog;

            bool hadOldSave = SessionState.GetBool(HadOldSaveKey, false);
            string oldSave = SessionState.GetString(OldSaveKey, "");
            string oldStart = SessionState.GetString(OldStartSceneKey, "");
            bool oldBg = SessionState.GetBool(OldBgKey, false);

            EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(oldStart) ? null :
                AssetDatabase.LoadAssetAtPath<SceneAsset>(oldStart);
            Environment.SetEnvironmentVariable("TWELVEJADE_SAVEDIR", hadOldSave ? oldSave : null);
            Application.runInBackground = oldBg;

            CleanupScratch(scratchDir);
            SessionState.SetString(ScratchKey, "");
            SessionState.SetBool(ActiveKey, false);
            report.AppendLine((problems.Count == 0 ? "PASS" : "FAIL") + " failures=" + problems.Count);
            if (!string.IsNullOrEmpty(outDir))
            {
                Directory.CreateDirectory(outDir);
                File.WriteAllText(Path.Combine(outDir, "report.txt"), report.ToString(), new UTF8Encoding(false));
            }
            Debug.Log("[MusicPlaybackChecks] finished, " + problems.Count + " problem(s); report=" + outDir);
            if (Application.isBatchMode) EditorApplication.Exit(problems.Count == 0 ? 0 : 1);
        }

        static void CleanupScratch(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                string full = Full(path);
                string parent = Path.GetDirectoryName(full);
                string tempRoot = Full(Path.GetTempPath());
                string folderName = Path.GetFileName(full);
                if (string.IsNullOrWhiteSpace(parent) ||
                    !string.Equals(Full(parent), tempRoot, StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(folderName) ||
                    !folderName.StartsWith(ScratchPrefix, StringComparison.Ordinal))
                {
                    Debug.LogWarning("Refused unsafe scratch cleanup path: " + path);
                    return;
                }
                if (Directory.Exists(full)) Directory.Delete(full, true);
            }
            catch (Exception ex) { Debug.LogWarning("Cleanup scratch failed: " + ex.Message); }
        }

        static string Full(string path) =>
            Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        static bool SamePath(string a, string b) =>
            !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) &&
            string.Equals(Full(a), Full(b), StringComparison.OrdinalIgnoreCase);

        static bool Overlaps(string a, string b) =>
            SamePath(a, b) ||
            Full(a).StartsWith(Full(b) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            Full(b).StartsWith(Full(a) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
