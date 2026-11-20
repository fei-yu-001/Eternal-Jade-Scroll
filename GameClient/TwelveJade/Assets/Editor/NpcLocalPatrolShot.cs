using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using TwelveJade.Core;
using TwelveJade.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TwelveJade.Editor
{
    // Observes the real host. Never advances, pauses or relocates an NPC for a passing check.
    [InitializeOnLoad]
    public static class NpcLocalPatrolShot
    {
        const string Key = "NpcLocalPatrolShot";
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static string output, source, scratch, stage = "bootstrap", target, arrived;
        static FrontEndController c; static Canvas canvas; static TownMap map;
        static bool running, finished, observing; static double started; static int shots, lastFrame = -1;
        static object settings; static FieldInfo reduceField; static bool oldReduce; static Vector2 lastPlayer;
        static StreamWriter frames; static int clickFrame = -1, reducedFrame = -1; static bool clickedNpc, approachHook, reducedWindow;
        static readonly List<Row> checks = new List<Row>();
        static readonly List<Stamp> inputs = new List<Stamp>();
        static readonly List<Watch> watches = new List<Watch>();
        static readonly string[] BoundPaths = {
            "Assets/Editor/NpcLocalPatrolShot.cs", "Assets/Editor/NpcLocalPatrolShot.cs.meta",
            "Assets/Scripts/Core/NpcLocalPatrol.cs", "Assets/Scripts/Core/TownMap.cs", "Assets/Scripts/Core/TownNavigator.cs",
            "Assets/Scripts/Core/NpcSchedule.cs", "Assets/Scripts/Core/TownElevation.cs", "Assets/Scripts/Core/SaveRepository.cs",
            "Assets/Scripts/Presentation/FrontEndController.cs", "Assets/Scripts/Presentation/FrontEndWorld.cs",
            "Assets/Scripts/Presentation/FrontEndNpcPatrol.cs", "Assets/Scripts/Presentation/FrontEndTrade.cs",
            "Assets/Scripts/Presentation/NpcFrameAnimator.cs", "Assets/Scripts/Presentation/PuppetGait.cs",
            "Assets/Scripts/Presentation/TownCharacterArt.cs", "Assets/Resources/Config/town-character-art.json",
            "Assets/Scripts/Core/Dialogue.cs", "Assets/Scripts/Core/DialogueTree.cs", "Assets/Scripts/Core/Quest.cs",
            "Assets/Scripts/Presentation/WorldPropLayer.cs", "Assets/Scripts/Presentation/NpcQuestOverhead.cs",
            "Assets/Scripts/Presentation/NpcQuestOverhead.cs.meta", "Assets/Resources/Config/town-map.json",
            "Assets/Resources/Config/building-plots.json", "Assets/Resources/Config/town-circulation.json", "Assets/Scenes/FrontEnd.unity"
        };
        [Serializable] sealed class Row { public string name, status, detail; }
        [Serializable] sealed class Stamp { public string path, sha256; }
        [Serializable] sealed class Report { public string status, overall = "NOT_APPROVED", scope = "NPC patrol, world-driven gait and dialogue approach; no full-town visual acceptance", unityVersion, target; public int screenshots; public List<Row> checks; public List<Stamp> inputs; }
        [Serializable] sealed class Frame {
            public string id, stage, state, markerLabel, markerState; public int frame, sibling, playerSibling; public float time, dt, baseY, expectedY, homeRadius;
            public Vector2 position, foot, centre, rectFoot, previous, home, player; public Vector3 markerLocal;
            public int commandFrame, gaitFrame; public float gaitDistance; public string dialogueNode; public bool inputLocked, markerVisible, markerFloating; public bool reduced, traversable, inside, sync, selfExcluded, playerBlocked, othersRetained, sorted, approach, talk, markerPresent, markerParent;
        }
        sealed class Watch {
            public string id; public NpcLocalPatrol patrol; public RectTransform rect; public Vector2 previous, stoppedAt; public float radius; public bool blocks;
            public int samples, moved, normalMoved, reducedMoved, resumedMoved, dwells, approachFrames, approachPlayerMoved, talkFrames, blocked, markers, visibleMarkers, reducedMarkers, oldAnchorChecks;
            public bool geometry = true, collision = true, depth = true, approachPaused = true, talkPaused = true, markerParent = true, markerStill = true, oldAnchorFree = true;
            public NpcFrameAnimator animator; public float gaitDistance, cycleDistance; public bool gait = true; public int gaitMovingSamples, gaitIdleSamples;
            public readonly HashSet<int> gaitFrames = new HashSet<int>();
            public bool previousReduced; public Vector3 markerLocal; public RectTransform previousMarker; public readonly HashSet<int> sides = new HashSet<int>();
        }
        static NpcLocalPatrolShot() { if (SessionState.GetBool(Key, false)) Attach(); }
        public static void Run()
        {
            if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use an isolated non-batch editor in Edit mode.");
            output = Environment.GetEnvironmentVariable("NPC_PATROL_EVIDENCE"); source = Environment.GetEnvironmentVariable("NPC_PATROL_SOURCE_PROJECT");
            if (string.IsNullOrEmpty(output) || !Path.IsPathRooted(output) || string.IsNullOrEmpty(source) || !Path.IsPathRooted(source)) throw new InvalidOperationException("Set absolute NPC_PATROL_EVIDENCE and NPC_PATROL_SOURCE_PROJECT.");
            if (SamePath(source, Path.Combine(Application.dataPath, ".."))) throw new InvalidOperationException("Refusing the source editor.");
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new InvalidOperationException("Evidence directory must be new/empty.");
            Directory.CreateDirectory(output); scratch = Path.Combine(output, "scratch-saves"); Application.runInBackground = true;
            SessionState.SetString(Key + ".Output", output); SessionState.SetString(Key + ".Source", source);
            SessionState.SetString(Key + ".OldSave", Environment.GetEnvironmentVariable("TWELVEJADE_SAVEDIR") ?? "");
            Environment.SetEnvironmentVariable("TWELVEJADE_SAVEDIR", scratch); SessionState.SetBool(Key, true); Attach();
            try {
                Bind(BoundPaths);
                var scripts = Path.Combine(Application.dataPath, "Scripts", "Presentation"); var config = Path.Combine(Application.dataPath, "Resources", "Config");
                Bind(Directory.EnumerateFiles(scripts, "FrontEnd*.cs").Concat(Directory.EnumerateFiles(scripts, "Dialogue*.cs")).Concat(Directory.EnumerateFiles(config, "*schedule*.json")).Concat(Directory.EnumerateFiles(config, "*quest*.json")).Concat(Directory.EnumerateFiles(config, "*dialogue*.json")).Select(p => "Assets/" + p.Substring(Application.dataPath.Length + 1).Replace('\\', '/')));
                Save("RUNNING"); if (checks.Any(r => r.status == "FAIL")) { Finish(); return; }
                UnityEditor.Search.SearchService.EnumerateDatabases().ToArray();
                EditorSceneManager.OpenScene("Assets/Scenes/FrontEnd.unity", OpenSceneMode.Single); EditorApplication.isPlaying = true;
            } catch (Exception e) { Check("bootstrap", false, e.ToString()); Finish(); }
        }
        static void Attach()
        {
            output = SessionState.GetString(Key + ".Output", ""); source = SessionState.GetString(Key + ".Source", ""); scratch = Path.Combine(output, "scratch-saves");
            var path = Path.Combine(output, "runtime-results.json");
            if (File.Exists(path)) { var saved = JsonUtility.FromJson<Report>(File.ReadAllText(path)); checks.Clear(); checks.AddRange(saved.checks); inputs.Clear(); inputs.AddRange(saved.inputs); }
            started = EditorApplication.timeSinceStartup; EditorApplication.update -= Tick; EditorApplication.update += Tick;
            Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog;
        }
        static void Tick()
        {
            if (finished) return; EditorApplication.QueuePlayerLoopUpdate();
            if (EditorApplication.timeSinceStartup - started > 110) { Check("bounded run timeout", false); Finish(); return; }
            if (!EditorApplication.isPlaying || running) return;
            c = UnityEngine.Object.FindAnyObjectByType<FrontEndController>(); if (c == null) return;
            try {
                canvas = c.GetComponentInParent<Canvas>() ?? UnityEngine.Object.FindAnyObjectByType<Canvas>(); if (canvas != null) canvas = canvas.rootCanvas;
                EditorWindow.GetWindow(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView")).Focus();
                frames = new StreamWriter(Path.Combine(output, "frames.jsonl"), false); running = true;
                new GameObject("NPC patrol evidence").AddComponent<NpcLocalPatrolShotPump>().StartCoroutine(Guard(Checks()));
            } catch (Exception e) { Check("runner", false, e.ToString()); Finish(); }
        }
        static IEnumerator Guard(IEnumerator test)
        {
            var stack = new Stack<IEnumerator>(); stack.Push(test);
            while (stack.Count > 0 && !finished) {
                object next = null; bool moved = false;
                try { moved = stack.Peek().MoveNext(); if (moved) next = stack.Peek().Current; }
                catch (Exception e) { Check("runtime exception", false, e.ToString()); Finish(); }
                if (finished) yield break; if (!moved) { stack.Pop(); continue; }
                if (next is IEnumerator nested) stack.Push(nested); else yield return next;
            }
            if (!finished) Finish();
        }
        static object Field(object obj, string name) => obj?.GetType().GetField(name, Flags)?.GetValue(obj);
        static DialogueUI Panel => Field(c, "dialoguePanel") as DialogueUI;
        static bool Talking(string id) => Panel != null && Panel.IsOpen && (string)Field(c, "talkNpcId") == id;
        static bool Reduced => reduceField != null && (bool)reduceField.GetValue(settings);
        static Vector2 Position(Watch w) => new Vector2(w.patrol.X, w.patrol.Y);
        static bool Near(Vector2 a, Vector2 b) => (a - b).sqrMagnitude <= .01f;
        static bool SamePath(string a, string b) => !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) && string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        static IEnumerator Wait(float seconds) { float end = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < end) yield return null; frames?.Flush(); }
        static IEnumerator Checks()
        {
            if (!(Field(c.Repository, "root") is string root) || !SamePath(root, scratch) || !SamePath(Environment.GetEnvironmentVariable("TWELVEJADE_SAVEDIR"), scratch)) throw new InvalidOperationException("Repository is not scratch.");
            if (Directory.Exists(scratch) && Directory.EnumerateFiles(scratch, "slot-*").Any()) throw new InvalidOperationException("Scratch slots must start empty.");
            var preset = Resources.LoadAll<CharacterPreset>("Characters").OrderBy(p => p.id, StringComparer.Ordinal).First();
            c.Repository.Create(1, preset.id, "巡游核验"); c.LoadSlot(c.Repository.Read(1));
            float deadline = Time.realtimeSinceStartup + 15; WorldPropLayer props = null;
            while (Time.realtimeSinceStartup < deadline) { props = UnityEngine.Object.FindAnyObjectByType<WorldPropLayer>(); if (props != null && props.PlacedForCheck) break; yield return null; }
            Check("real props/collision registration ready", props != null && props.PlacedForCheck); if (props == null || !props.PlacedForCheck) yield break;
            var presetPath = AssetDatabase.GetAssetPath(preset); if (!string.IsNullOrEmpty(presetPath)) Bind(new[] { presetPath, presetPath + ".meta" });
            Check("empty scratch directly Town", c.CurrentPage == "town" && c.World != null); map = c.World; if (map == null) yield break;
            foreach (var npc in map.Npcs.OrderBy(n => n.Id, StringComparer.Ordinal)) {
                var patrol = c.NpcPatrolForShot(npc.Id); var rect = c.TownNpcRectForShot(npc.Id);
                Check(npc.Id + " real instance/Rect", patrol != null && rect != null); if (patrol == null || rect == null) continue;
                var animator = rect.GetComponent<NpcFrameAnimator>(); var entry = TownCharacterArt.Find(npc.Id);
                Check(npc.Id + " actual gait component/config", animator != null && animator.HasSheetForCheck && entry != null && entry.cycleDistance > 0);
                watches.Add(new Watch { id = npc.Id, patrol = patrol, rect = rect, previous = new Vector2(patrol.X, patrol.Y), radius = npc.Radius, blocks = npc.Blocks,
                    animator = animator, gaitDistance = animator == null ? 0 : animator.DistanceForCheck, cycleDistance = entry == null ? 0 : entry.cycleDistance });
                if (entry != null) foreach (var resource in new[] { entry.stand, entry.walk }) {
                    var texture = TownCharacterArt.Texture(resource); string path = texture == null ? "" : AssetDatabase.GetAssetPath(texture);
                    Check(npc.Id + " gait source " + resource, !string.IsNullOrEmpty(path));
                    if (!string.IsNullOrEmpty(path)) Bind(new[] { path, path + ".meta" });
                }
                var art = string.IsNullOrEmpty(npc.Art) ? null : Resources.Load<Texture2D>(npc.Art); string artPath = art == null ? "" : AssetDatabase.GetAssetPath(art);
                Check(npc.Id + " actual portrait input", !string.IsNullOrEmpty(artPath)); if (!string.IsNullOrEmpty(artPath)) Bind(new[] { artPath, artPath + ".meta" });
            }
            Check("all NPC dynamic centres registered", watches.Count > 0 && map.NpcDynamicPositionCount == map.Npcs.Count && c.NpcPatrolsLiveForShot);
            if (watches.Count == 0 || checks.Any(r => r.status == "FAIL")) yield break;
            target = watches[0].id; var chosen = watches[0]; c.NpcArrived += Arrived;
            approachHook = c.GetType().GetField("hasTarget", Flags) != null && c.GetType().GetField("travelContextId", Flags) != null;
            if (!approachHook) Pending("appointment state observation", "Existing target fields unavailable; no synthetic pause call.");
            settings = Field(c, "settings"); reduceField = settings?.GetType().GetField("reduceMotion", Flags);
            if (reduceField != null) { oldReduce = Reduced; reduceField.SetValue(settings, false); } else Pending("reduceMotion", "Settings callback missing; no substitute patrol setter.");
            Check("initial player viewpoint", c.SnapTravelerAboveNpc(target)); lastPlayer = c.PlayerWorldPosition;
            stage = "normal"; observing = true; yield return Capture("01-patrol-start"); yield return Wait(12); yield return Capture("02-patrol-moved");
            var live = c.TownNpcFootPosition(target); bool setup = live.HasValue && c.SnapTravelerTo(live.Value.x, live.Value.y - QuestRuntime.InteractionDistance - 64f);
            Check("player starts outside immediate dialogue radius", setup && Vector2.Distance(c.PlayerWorldPosition, live.Value) > QuestRuntime.InteractionDistance + 16f); lastPlayer = c.PlayerWorldPosition; yield return null;
            stage = "approach"; bool? click = setup ? Click(c.NpcButtonForShot(target), "actual NPC click") : null; clickedNpc = click == true;
            deadline = Time.realtimeSinceStartup + 10;
            while (clickedNpc && !Talking(target) && Time.realtimeSinceStartup < deadline) yield return null;
            bool talk = Talking(target);
            var arrivalFoot = c.TownNpcFootPosition(target);
            Check("dialogue begins within live NPC interaction distance", talk && arrivalFoot.HasValue && Vector2.Distance(c.PlayerWorldPosition, arrivalFoot.Value) <= QuestRuntime.InteractionDistance,
                "distance=" + (arrivalFoot.HasValue ? Vector2.Distance(c.PlayerWorldPosition, arrivalFoot.Value).ToString("F3") : "missing"));
            if (clickedNpc) Check("real arrival opens selected DialogueUI and acquires input lease", talk && arrived == target && c.DialogueInputLockedForShot); else Pending("real arrival/DialogueUI", "No dispatched NPC click; inspect the UI entry/raycast row.");
            if (talk) {
                Check("dialogue arrival clears travel condition", !c.TravelBusy && Field(c, "travelArrivalCondition") == null);
                stage = "talk"; yield return Wait(1); yield return Capture("03-dialogue-paused");
                var close = Panel.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == "Dialogue close");
                bool? closed = Click(close, "actual farewell click"); yield return null;
                if (closed == true) {
                    deadline = Time.realtimeSinceStartup + 2;
                    while (((Panel != null && Panel.IsOpen) || c.DialogueInputLockedForShot) && Time.realtimeSinceStartup < deadline) yield return null;
                    Check("farewell closes actual panel/releases lease", (Panel == null || !Panel.IsOpen) && !c.DialogueInputLockedForShot);
                    stage = "resume"; yield return Wait(8); Check("real dialogue resume", chosen.resumedMoved > 1, "movedFrames=" + chosen.resumedMoved);
                }
                else Pending("dialogue resume", "Actual farewell unavailable/blocked; no direct CloseModal call.");
            } else Pending("dialogue hold/close/resume", "Real DialogueUI did not open; no direct TalkTo/SetDialogue substitute.");
            if (reduceField != null && (Panel == null || !Panel.IsOpen) && !c.DialogueInputLockedForShot) { reducedWindow = true; reduceField.SetValue(settings, true); reducedFrame = Time.frameCount; stage = "reduced"; yield return Wait(9); yield return Capture("04-reduced-motion-active"); }
            else Pending("reduceMotion activity window", "Settings entry missing or actual modal still open; cannot isolate motion setting.");
            observing = false; Summarize(); foreach (var w in watches) w.stoppedAt = Position(w);
            stage = "exit"; c.ShowMenu(); yield return null; yield return null; yield return Wait(.3f);
            Check("Town visit released", !c.NpcPatrolsLiveForShot && map.NpcDynamicPositionCount == 0 && !c.DialogueInputLockedForShot && (Panel == null || !Panel.IsOpen));
            foreach (var w in watches) Check(w.id + " exit Stop/hooks/position", w.patrol.State == NpcLocalPatrol.PatrolState.Stopped && Near(Position(w), w.stoppedAt) && c.NpcPatrolForShot(w.id) == null && c.TownNpcRectForShot(w.id) == null && c.TownNpcBaseYForShot(w.id) == null && c.NpcHeadMarkerForShot(w.id) == null && c.QuestOverheadLabelForShot(w.id) == null && w.rect == null);
            yield return Capture("05-after-exit");
        }
        static void Arrived(string id) { arrived = id; }
        public static void Sample()
        {
            if (!observing || finished || c == null || c.CurrentPage != "town" || lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            try { foreach (var w in watches) Sample(w); lastPlayer = c.PlayerWorldPosition; }
            catch (Exception e) { Check("frame observation", false, e.ToString()); Finish(); }
        }
        static void Sample(Watch w)
        {
            var p = Position(w); var foot = c.TownNpcFootPosition(w.id); var rect = c.TownNpcRectForShot(w.id); var key = c.TownNpcBaseYForShot(w.id);
            bool hasCentre = map.TryNpcPosition(w.id, out float cx, out float cy); float expected = map.Elevation.ProjectY(p.x, p.y);
            Vector2 actual = Vector2.zero;
            if (rect != null && rect.parent is RectTransform worldRoot) { Vector3 local = worldRoot.InverseTransformPoint(rect.TransformPoint(new Vector3(rect.rect.center.x, rect.rect.yMin, 0))); actual = new Vector2((local.x - worldRoot.rect.xMin) / c.WorldScale, (worldRoot.rect.yMax - local.y) / c.WorldScale); }
            bool sync = foot.HasValue && Near(foot.Value, p) && hasCentre && Near(new Vector2(cx, cy), p) && key.HasValue && Mathf.Abs(key.Value - expected) <= .1f && Near(actual, new Vector2(p.x, expected)) && c.NpcPatrolForShot(w.id) == w.patrol;
            bool moved = (p - w.previous).sqrMagnitude > .0001f, inside = Vector2.Distance(p, new Vector2(w.patrol.HomeX, w.patrol.HomeY)) <= w.patrol.HomeRadius + .01f;
            bool traverse = map.CanTraverse(w.previous.x, w.previous.y, p.x, p.y, w.id), self = map.CanStand(p.x, p.y, w.id), blocked = !map.CanStand(p.x, p.y);
            bool others = true; foreach (var other in watches) if (other.id != w.id && other.blocks) { var q = Position(other); others &= !map.CanStand(q.x, q.y, w.id) && !map.CanTraverse(p.x, p.y, q.x, q.y, w.id); }
            bool sorted = rect != null && c.TravelerRectForShot != null && rect.parent == c.TravelerRectForShot.parent;
            float playerY = map.Elevation.ProjectY(c.PlayerWorldPosition.x, c.PlayerWorldPosition.y); int side = Math.Sign(expected - playerY);
            if (sorted && Mathf.Abs(expected - playerY) > .1f) { sorted = side == Math.Sign(rect.GetSiblingIndex() - c.TravelerRectForShot.GetSiblingIndex()); w.sides.Add(side); }
            foreach (var other in watches) { var otherKey = c.TownNpcBaseYForShot(other.id); var otherRect = c.TownNpcRectForShot(other.id); if (rect != null && otherRect != null && otherKey.HasValue && Mathf.Abs(expected - otherKey.Value) > .1f) sorted &= Math.Sign(expected - otherKey.Value) == Math.Sign(rect.GetSiblingIndex() - otherRect.GetSiblingIndex()); }
            bool approach = Field(c, "hasTarget") is bool active && active && (string)Field(c, "travelContextId") == w.id, talk = Talking(w.id);
            if (w.animator != null && w.cycleDistance > 0) {
                float expectedDistance = (w.gaitDistance + (p - w.previous).magnitude) % w.cycleDistance;
                float actualDistance = w.animator.DistanceForCheck;
                float phaseError = Mathf.Abs(expectedDistance - actualDistance);
                phaseError = Mathf.Min(phaseError, w.cycleDistance - phaseError);
                w.gait &= phaseError < .002f;
                int frame = w.animator.FrameForCheck;
                // Observe after all regular LateUpdate calls, so this is the frame actually displayed.
                // The older host statistics ignore sub-.01 steps. Gait still consumes a real
                // short final step, so only an exactly stationary foot should display idle here.
                if ((p - w.previous).sqrMagnitude > 0f) { w.gaitMovingSamples++; w.gaitFrames.Add(frame); w.gait &= frame >= 0 && frame < w.animator.FrameCountForCheck; }
                else { w.gaitIdleSamples++; w.gait &= frame == -1; }
                w.gaitDistance = actualDistance;
            } else w.gait = false;
            if (approach && Time.frameCount > clickFrame) { w.approachFrames++; if ((c.PlayerWorldPosition - lastPlayer).sqrMagnitude > .0001f) w.approachPlayerMoved++; }
            if (talk) { w.talkFrames++; w.talkPaused &= c.DialogueInputLockedForShot && w.patrol.State == NpcLocalPatrol.PatrolState.Paused && !moved; }
            // A click dispatched after Update cannot retroactively pause movement earlier in that frame; retain the raw row.
            if (approach && Time.frameCount > clickFrame) w.approachPaused &= w.patrol.State == NpcLocalPatrol.PatrolState.Paused && !moved;
            if (moved) { w.moved++; if (stage == "normal") w.normalMoved++; if (stage == "reduced") w.reducedMoved++; if (stage == "resume") w.resumedMoved++; }
            else if (w.moved > 0 && w.patrol.State == NpcLocalPatrol.PatrolState.Waiting) w.dwells++;
            if (w.patrol.State == NpcLocalPatrol.PatrolState.Blocked) w.blocked++;
            var marker = c.NpcHeadMarkerForShot(w.id); var overhead = marker == null ? null : marker.GetComponent<NpcQuestOverhead>(); var label = c.QuestOverheadLabelForShot(w.id);
            bool markerParent = marker != null && rect != null && marker.IsChildOf(rect) && overhead != null && overhead.Rect == marker && label != null && label == overhead.Label && label.transform.IsChildOf(rect) && !label.raycastTarget;
            bool markerVisible = label != null && label.isActiveAndEnabled && !string.IsNullOrWhiteSpace(label.text); if (markerVisible) w.visibleMarkers++;
            if (marker != null) { w.markers++; w.markerParent &= markerParent; if (markerVisible && Reduced && Time.frameCount > reducedFrame + 1 && w.previousReduced && w.previousMarker == marker) { w.reducedMarkers++; w.markerStill &= (marker.localPosition - w.markerLocal).sqrMagnitude <= .0001f && (overhead == null || !overhead.FloatingForShot); } w.markerLocal = marker.localPosition; }
            w.previousMarker = marker; w.previousReduced = Reduced;
            var home = new Vector2(w.patrol.HomeX, w.patrol.HomeY);
            if (w.blocks && Vector2.Distance(home, p) > w.radius + .1f && map.CanStand(home.x, home.y, w.id)) { w.oldAnchorChecks++; w.oldAnchorFree &= map.CanStand(home.x, home.y); }
            w.samples++; w.geometry &= sync && inside; w.collision &= traverse && self && (!w.blocks || blocked) && others; w.depth &= sorted;
            frames.WriteLine(JsonUtility.ToJson(new Frame { id = w.id, stage = stage, state = w.patrol.State.ToString(), commandFrame = clickFrame, gaitFrame = w.animator == null ? -2 : w.animator.FrameForCheck, gaitDistance = w.gaitDistance, dialogueNode = Panel == null ? "" : Panel.CurrentNodeId, inputLocked = c.DialogueInputLockedForShot, markerVisible = markerVisible, markerLabel = label == null ? "" : label.text, markerState = overhead == null ? "unregistered" : overhead.State.ToString(), markerFloating = overhead != null && overhead.FloatingForShot, frame = Time.frameCount, time = Time.realtimeSinceStartup, dt = Time.unscaledDeltaTime, position = p, previous = w.previous, home = home, homeRadius = w.patrol.HomeRadius, foot = foot ?? Vector2.zero, centre = new Vector2(cx, cy), rectFoot = actual, baseY = key ?? 0f, expectedY = expected, player = c.PlayerWorldPosition, sibling = rect == null ? -1 : rect.GetSiblingIndex(), playerSibling = c.TravelerRectForShot == null ? -1 : c.TravelerRectForShot.GetSiblingIndex(), reduced = Reduced, traversable = traverse, inside = inside, sync = sync, selfExcluded = self, playerBlocked = blocked, othersRetained = others, sorted = sorted, approach = approach, talk = talk, markerPresent = marker != null, markerParent = markerParent, markerLocal = marker == null ? Vector3.zero : marker.localPosition }));
            w.previous = p;
        }
        static void Summarize()
        {
            foreach (var w in watches) {
                string detail = "samples=" + w.samples + " normalMoved=" + w.normalMoved + " reducedMoved=" + w.reducedMoved + " waiting=" + w.dwells + " blocked=" + w.blocked;
                Check(w.id + " observed continuous local movement/dwell", w.normalMoved > 1 && w.dwells > 1, detail);
                Check(w.id + " actual foot/Rect/BaseY/home", w.samples > 1 && w.geometry, detail);
                Check(w.id + " self exclusion/other blockers/actual segments", w.samples > 1 && w.collision, detail);
                Check(w.id + " actual sibling ordering", w.samples > 1 && w.depth);
                Check(w.id + " gait follows accepted world movement and rests when stationary", w.gait && w.gaitMovingSamples > 1 && w.gaitIdleSamples > 1 && w.gaitFrames.Count == w.animator.FrameCountForCheck,
                    "moving=" + w.gaitMovingSamples + " idle=" + w.gaitIdleSamples + " frames=" + w.gaitFrames.Count);
                if (w.sides.Count < 2) Pending(w.id + " player depth reversal", "Only sampled relation covered; no forced NPC move.");
                if (reducedWindow) Check(w.id + " reduceMotion preserves movement", w.reducedMoved > 1, detail);
                if (w.oldAnchorChecks > 0) Check(w.id + " old home circle released", w.oldAnchorFree, "eligible samples=" + w.oldAnchorChecks); else Pending(w.id + " old home circle release", "No sampled position far enough with other obstacles absent.");
                if (w.markers == 0) Pending(w.id + " actual head marker", "No registered real node; no synthetic marker attached.");
                else { Check(w.id + " head marker/label share real NPC parent", w.markerParent); if (w.visibleMarkers == 0) Pending(w.id + " visible head marker", "Real registered node is hidden/None; no visible marker proof."); if (w.reducedMarkers > 1) Check(w.id + " reduced marker float stopped", w.markerStill); else Pending(w.id + " reduced marker float", "Insufficient consecutive real marker samples."); }
            }
            var chosen = watches[0];
            if (clickedNpc && approachHook) Check("appointment pause with real player travel", chosen.approachFrames > 1 && chosen.approachPlayerMoved > 1 && chosen.approachPaused, "approach=" + chosen.approachFrames + " playerMoved=" + chosen.approachPlayerMoved);
            else Pending("appointment pause", "Actual click or target observation missing.");
            if (chosen.talkFrames > 0) Check("dialogue pause throughout real DialogueUI", chosen.talkFrames > 1 && chosen.talkPaused, "talkFrames=" + chosen.talkFrames);
            else Pending("dialogue pause", "No actual selected DialogueUI samples.");
            Pending("NPC gait visual quality", "Real host/frame/collision checks cover timing; subjective animation quality still needs desktop play review.");
        }
        static bool? Click(Button button, string name)
        {
            if (button == null || EventSystem.current == null || !button.IsActive() || !button.IsInteractable()) { Pending(name, "Actual UI entry missing/unavailable; no direct callback."); return null; }
            var rect = (RectTransform)button.transform; var screen = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            var data = new PointerEventData(EventSystem.current) { position = screen, button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(data, hits);
            var top = hits.Count == 0 ? null : hits[0].gameObject; bool ok = top != null && ExecuteEvents.GetEventHandler<IPointerClickHandler>(top) == button.gameObject;
            Check(name + " top raycast", ok, "screen=" + screen + " top=" + (top == null ? "none" : top.name));
            if (!ok) return false; if (stage == "approach") clickFrame = Time.frameCount; data.pointerCurrentRaycast = hits[0]; data.pressPosition = screen; data.pointerPressRaycast = hits[0];
            ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerDownHandler); ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerUpHandler); ExecuteEvents.Execute(button.gameObject, data, ExecuteEvents.pointerClickHandler); return true;
        }
        static IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame(); Texture2D texture = null;
            try {
                if (canvas == null || canvas.renderMode != RenderMode.ScreenSpaceOverlay) throw new InvalidOperationException("Native Overlay required.");
                texture = ScreenCapture.CaptureScreenshotAsTexture(); if (texture == null || texture.width < 1 || texture.height < 1) throw new InvalidOperationException("No real GameView image.");
                File.WriteAllBytes(Path.Combine(output, name + ".png"), texture.EncodeToPNG()); shots++;
                File.AppendAllText(Path.Combine(output, "screenshots.jsonl"), JsonUtility.ToJson(new Row { name = name + ".png", status = "CAPTURED", detail = "frame=" + Time.frameCount + " stage=" + stage + " page=" + c.CurrentPage + " target=" + target + " size=" + texture.width + "x" + texture.height + " ScreenCapture.CaptureScreenshotAsTexture after WaitForEndOfFrame" }) + "\n"); frames?.Flush();
            } finally { if (texture != null) UnityEngine.Object.Destroy(texture); }
        }
        static string Sha(string path) { using (var hash = SHA256.Create()) using (var stream = File.OpenRead(path)) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
        static void Bind(IEnumerable<string> paths)
        {
            foreach (var path in paths) {
                var live = Path.GetFullPath(Path.Combine(Application.dataPath, "..", path)); var original = Path.GetFullPath(Path.Combine(source, path)); if (inputs.Any(s => s.path == live)) continue;
                if (!File.Exists(live) || !File.Exists(original)) { Check("bound input exists " + path, false); continue; }
                string a = Sha(live), b = Sha(original); inputs.Add(new Stamp { path = live, sha256 = a }); inputs.Add(new Stamp { path = original, sha256 = b }); Check("source/fixture parity " + path, a == b);
            }
        }
        static void Check(string name, bool pass, string detail = "") { checks.Add(new Row { name = name, status = pass ? "PASS" : "FAIL", detail = detail }); Save("RUNNING"); }
        static void Pending(string name, string detail) { checks.Add(new Row { name = name, status = "PENDING", detail = detail }); Save("RUNNING"); }
        static void Save(string status) { if (!string.IsNullOrEmpty(output)) File.WriteAllText(Path.Combine(output, "runtime-results.json"), JsonUtility.ToJson(new Report { status = status, unityVersion = Application.unityVersion, target = target, screenshots = shots, checks = checks, inputs = inputs }, true)); }
        static void OnLog(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Assert || type == LogType.Exception) Check("Unity error", false, message + "\n" + stack); }
        static void Finish()
        {
            if (finished) return; finished = true; observing = false; frames?.Dispose(); frames = null;
            EditorApplication.update -= Tick; Application.logMessageReceived -= OnLog; if (c != null) c.NpcArrived -= Arrived;
            if (reduceField != null && settings != null) reduceField.SetValue(settings, oldReduce);
            foreach (var stamp in inputs) checks.Add(new Row { name = "input drift " + stamp.path, status = File.Exists(stamp.path) && Sha(stamp.path) == stamp.sha256 ? "PASS" : "FAIL" });
            Pending("visual review", "Only this run is evidence; create <=1280-edge JPEG previews before review. No older town results merged.");
            string status = checks.Any(r => r.status == "FAIL") ? "FAIL" : "PENDING"; Save(status);
            SessionState.SetBool(Key, false); string old = SessionState.GetString(Key + ".OldSave", ""); Environment.SetEnvironmentVariable("TWELVEJADE_SAVEDIR", old.Length == 0 ? null : old);
            EditorApplication.Exit(status == "FAIL" ? 1 : 0);
        }
    }
    [DefaultExecutionOrder(10000)]
    public sealed class NpcLocalPatrolShotPump : MonoBehaviour { void LateUpdate() { NpcLocalPatrolShot.Sample(); } }
}
