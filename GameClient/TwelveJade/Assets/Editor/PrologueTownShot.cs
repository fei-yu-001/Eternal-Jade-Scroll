using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using TMPro;
using TwelveJade.Combat;
using TwelveJade.Core;
using TwelveJade.Economy;
using TwelveJade.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TwelveJade.Editor
{
    // Run with a graphical editor (no -batchmode/-nographics/-quit):
    // -executeMethod TwelveJade.Editor.PrologueTownShot.Run
    // PROLOGUE_TOWN_SOURCE_PROJECT = absolute original Unity project, containing Assets/.
    // PROLOGUE_TOWN_EVIDENCE = new/empty absolute evidence directory outside either project.
    // The copy must live under a .batch/ directory; its .prologue-town-shot marker contains
    // PROLOGUE_TOWN_SOURCE_PROJECT verbatim. No scenes are opened, saved or discarded here:
    // playModeStartScene selects FrontEnd, and Unity restores the existing edit scenes on exit.
    // Dirty scenes are refused. A nonempty untitled scene is kept open after restoration.
    // For unattended exit, start from a clean saved scene (or a truly empty untitled scene).
    [InitializeOnLoad]
    public static partial class PrologueTownShot
    {
        const string Key = "PrologueTownShot.State";
        const string ScenePath = "Assets/Scenes/FrontEnd.unity";
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static RunState state;
        static FrontEndController host;
        static GameObject canvasRoot;
        static bool running, finalizing;
        static ItemTable items;
        static MerchantTable merchants;
        static RecipeTable recipes;
        static string itemsJson;

        static readonly string[] MerchantIds = { "npc-zhao-da", "npc-yao-lao", "huolang", "npc-shenhuai-an", "npc-shitou" };
        static readonly string[][] Shelves = {
            new[] { "farm-hoe", "farm-sickle", "cudao", "tiejian", "iron-ore" },
            new[] { "jinchuangyao", "caoyao", "licorice" },
            new[] { "ganliang", "chuibing", "larou", "wheat-flour", "cubupao", "zhudi" },
            new[] { "paper-charm" }, new[] { "chuibing", "ganliang", "cubupao" }
        };
        static readonly string[] BoundPaths = {
            "Assets/Editor/PrologueTownShot.cs", "Assets/Editor/PrologueTownShot.cs.meta", ScenePath,
            "Assets/Editor/QingshiStoryShot.cs", "Assets/Scripts/Core/QingshiStory.cs",
            "Assets/Scripts/Presentation/QingshiComicView.cs", "Assets/Scripts/Presentation/FrontEndQingshiStory.cs",
            "Assets/Scripts/Presentation/CharacterPreset.cs", "Assets/Scripts/Presentation/TownCharacterArt.cs",
            "Assets/Scripts/Presentation/ActionPoseGeometry.cs", "Assets/Scripts/Presentation/PosterCardView.cs",
            "Assets/Scripts/Presentation/ImmortalUi.cs", "Assets/Resources/Config/dialogue-portraits.json",
            "Assets/Resources/Config/town-character-art.json", "Assets/Resources/Config/qingshi-story-panels.json",
            "Assets/Scripts/Presentation/FrontEndController.cs", "Assets/Scripts/Presentation/FrontEndCharacters.cs",
            "Assets/Scripts/Presentation/FrontEndChapter.cs", "Assets/Scripts/Presentation/FrontEndWorld.cs",
            "Assets/Scripts/Presentation/QuestScrollReveal.cs", "Assets/Scripts/Presentation/V2ScrollSession.cs",
            "Assets/Scripts/Presentation/FrontEndCombat.cs", "Assets/Scripts/Presentation/UiKit.cs",
            "Assets/Scripts/Presentation/FrontEndGuofengHud.cs", "Assets/Scripts/Presentation/JadeLotusRotator.cs",
            "Assets/Scripts/Presentation/FrontEndInventory.cs", "Assets/Scripts/Presentation/FrontEndTrade.cs",
            "Assets/Scripts/Presentation/FrontEndCultivation.cs", "Assets/Scripts/Presentation/DaoIdentityView.cs",
            "Assets/Scripts/Presentation/FrontEndWorldCombat.cs", "Assets/Scripts/Presentation/CombatSkillWheel.cs",
            "Assets/Scripts/Core/PrologueRules.cs", "Assets/Scripts/Core/CultivationRules.cs",
            "Assets/Scripts/Core/ManualStudyRules.cs", "Assets/Scripts/Core/StudyPathRules.cs",
            "Assets/Scripts/Core/SaveModels.cs", "Assets/Scripts/Core/SaveRepository.cs", "Assets/Scripts/Core/Trade.cs",
            "Assets/Scripts/Economy/CraftingService.cs", "Assets/Scripts/Combat/CombatConsumables.cs",
            "Assets/Scripts/Combat/CombatEquipmentService.cs", "Assets/Scripts/Combat/CombatGear.cs"
        };
        [Serializable] sealed class Row { public string name, status, detail; }
        [Serializable] sealed class Stamp { public string path, sha256; }
        [Serializable] sealed class ProtectedDirectory { public string path; public bool existed; public string[] entries; }
        [Serializable] sealed class SceneRecord { public string path, name; public bool loaded, active, dirty; public string[] roots; }
        [Serializable] sealed class Shot { public string file, page, fixture, sha256; public int width, height, colors, frame; }
        [Serializable] sealed class RunState
        {
            public string output, source, project, scratch, phase, oldSave, oldStartScene, credentialFingerprint;
            public bool hadOldSave, oldBackground, story, music, dossier;
            public double started, restoreStarted;
            public bool checksStarted, editReady;
            public SceneRecord[] scenes;
            public List<ProtectedDirectory> saves = new List<ProtectedDirectory>();
            public List<Stamp> inputs = new List<Stamp>();
            public List<Row> checks = new List<Row>();
            public List<Shot> shots = new List<Shot>();
        }
        [Serializable] sealed class Report
        {
            public string status, unityVersion, sourceProject, copyProject;
            public string scope = "Qingshi mortal prologue, five real shops, HUD and locked legacy records; no later cultivation or full-town acceptance";
            public int passed, failed, pending;
            public List<Row> checks;
            public List<Shot> screenshots;
            public List<Stamp> inputs;
        }
        // Only durable assets: clock/exploration/NPC observations may legitimately change between frames.
        [Serializable] sealed class AssetLedger
        {
            public int coins, reputation;
            public string[] bag, gear, studies, crafts;
            public string cultivation;
        }

        static PrologueTownShot()
        {
            string saved = SessionState.GetString(Key, "");
            if (string.IsNullOrEmpty(saved)) return;
            state = JsonUtility.FromJson<RunState>(saved);
            if (state != null) Attach();
        }

        public static void Run() => Start(false);
        public static void RunStory() => Start(true);
        public static void RunMusic() => Start(false, true);
        public static void RunDossier() => Start(false, false, true);
        static void Start(bool story, bool music = false, bool dossier = false)
        {
            if (state != null || !string.IsNullOrEmpty(SessionState.GetString(Key, "")))
                throw new InvalidOperationException("A prologue run is already pending restoration.");
            if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode ||
                SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                throw new InvalidOperationException("Use an isolated graphical editor in Edit mode; omit -batchmode, -nographics and -quit.");
            string project = Full(Path.Combine(Application.dataPath, ".."));
            string source = AbsoluteEnvironment("PROLOGUE_TOWN_SOURCE_PROJECT");
            string output = AbsoluteEnvironment("PROLOGUE_TOWN_EVIDENCE");
            string marker = Path.Combine(project, ".prologue-town-shot");
            if (!project.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains(".batch", StringComparer.OrdinalIgnoreCase) ||
                SamePath(project, source) || !File.Exists(marker) || !SamePath(File.ReadAllText(marker).Trim(), source))
                throw new InvalidOperationException("Require a separate .batch project with .prologue-town-shot containing the absolute source Unity project path.");
            if (!File.Exists(Path.Combine(source, ScenePath)) || Overlaps(output, project) || Overlaps(output, source))
                throw new InvalidOperationException("Source must contain FrontEnd; evidence must be outside both Unity projects.");
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
                throw new InvalidOperationException("Evidence directory must be new/empty.");
            var scenes = ReadScenes();
            if (scenes.Any(s => s.dirty))
                throw new InvalidOperationException("An edit scene has unsaved changes. Refusing to enter Play Mode; no scene was saved or discarded.");
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (scene == null) throw new InvalidOperationException("Missing real FrontEnd scene.");
            string oldSave = Environment.GetEnvironmentVariable("TWELVEJADE_SAVEDIR");
            var protectedPaths = new[] { Path.Combine(Application.persistentDataPath, "Saves"), oldSave }
                .Where(p => !string.IsNullOrEmpty(p)).Select(Full).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (protectedPaths.Any(p => Overlaps(p, output)))
                throw new InvalidOperationException("Evidence/scratch must not overlap an existing save root.");
            var next = new RunState {
                output = output, source = source, project = project, scratch = Path.Combine(output, "scratch-saves"),
                oldSave = oldSave ?? "", hadOldSave = oldSave != null, oldBackground = Application.runInBackground,
                oldStartScene = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) ?? "", scenes = scenes,
                phase = "play", started = EditorApplication.timeSinceStartup, story = story, music = music, dossier = dossier,
                credentialFingerprint = dossier ? DossierCredentialsFingerprint() : null
            };
            foreach (string path in protectedPaths)
                next.saves.Add(new ProtectedDirectory { path = path, existed = Directory.Exists(path), entries = DirectoryEntries(path) });
            Directory.CreateDirectory(output);
            state = next; running = finalizing = false; host = null; canvasRoot = null; Attach(); Save();
            try {
                foreach (string path in BoundPaths) Bind(path);
                foreach (var editScene in scenes.Where(s => !string.IsNullOrEmpty(s.path)))
                    ProtectFile(Path.Combine(project, editScene.path));
                if (state.checks.Any(r => r.status == "FAIL")) { RequestFinish(); return; }
                Environment.SetEnvironmentVariable("TWELVEJADE_SAVEDIR", state.scratch);
                Application.runInBackground = true;
                EditorSceneManager.playModeStartScene = scene;
                // Complete editor search initialization before the Play Mode domain reload.
                UnityEditor.Search.SearchService.EnumerateDatabases().ToArray();
                Save(); EditorApplication.isPlaying = true;
            } catch (Exception e) { Check("bootstrap", false, e.ToString()); RequestFinish(); }
        }

        static void Attach()
        {
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged -= PlayState; EditorApplication.playModeStateChanged += PlayState;
            Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog;
        }
        static void Tick()
        {
            if (state == null || finalizing) return;
            EditorApplication.QueuePlayerLoopUpdate();
            try {
                if (state.phase == "restore") {
                    if (state.editReady && !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling &&
                        !EditorApplication.isUpdating && EditorApplication.timeSinceStartup - state.restoreStarted > .5) Restore();
                    return;
                }
                if (EditorApplication.timeSinceStartup - state.started > 240) {
                    Check("bounded run", false, "Timed out waiting for the real FrontEnd/UI or GameView frame."); RequestFinish(); return;
                }
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling || running) return;
                host = UnityEngine.Object.FindAnyObjectByType<FrontEndController>();
                if (host == null) return;
                if (state.checksStarted) {
                    Check("runtime domain remained stable", false, "Unexpected reload during the checks; refusing to overwrite scratch fixtures.");
                    RequestFinish(); return;
                }
                FocusGameView(); running = true; state.checksStarted = true; Save();
                new GameObject("Prologue town verification pump").AddComponent<PrologueTownShotPump>().StartCoroutine(Guard(Checks()));
            } catch (Exception e) { Check("editor runner", false, e.ToString()); RequestFinish(); }
        }
        static void PlayState(PlayModeStateChange change)
        {
            if (state == null) return;
            if (change == PlayModeStateChange.EnteredEditMode && state.phase == "restore") {
                state.editReady = true; state.restoreStarted = EditorApplication.timeSinceStartup; Save();
            }
            if (change == PlayModeStateChange.ExitingPlayMode && state.phase != "restore") {
                Check("complete runtime sequence", false, "Play Mode ended before the checks finished.");
                state.phase = "restore"; state.restoreStarted = EditorApplication.timeSinceStartup; Save();
            }
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
                sceneSafe = JsonUtility.ToJson(new SceneEnvelope { scenes = ReadScenes() }) ==
                    JsonUtility.ToJson(new SceneEnvelope { scenes = state.scenes });
                Check("user edit scenes preserved", sceneSafe, "Paths, loaded/active state, dirty flags and root names; no OpenScene/SaveScene calls.");
                EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(state.oldStartScene) ? null :
                    AssetDatabase.LoadAssetAtPath<SceneAsset>(state.oldStartScene);
                Check("playModeStartScene restored", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) == state.oldStartScene);
                // Keep scratch active until all Play Mode shutdown callbacks have completed.
                Environment.SetEnvironmentVariable("TWELVEJADE_SAVEDIR", state.hadOldSave ? state.oldSave : null);
                Application.runInBackground = state.oldBackground;
                restored = true;
                if (state.dossier) Check("real credential preferences preserved", state.credentialFingerprint == DossierCredentialsFingerprint());
                Check("save environment restored", Environment.GetEnvironmentVariable("TWELVEJADE_SAVEDIR") == (state.hadOldSave ? state.oldSave : null));
                foreach (var saved in state.saves)
                    Check("real save directory preserved " + saved.path, saved.existed == Directory.Exists(saved.path) &&
                        saved.entries.SequenceEqual(DirectoryEntries(saved.path)), "File/directory inventory and SHA-256 compared.");
                foreach (var input in state.inputs)
                    Check("input unchanged " + input.path, File.Exists(input.path) && Sha(input.path) == input.sha256);
            } catch (Exception e) { Check("safe restoration", false, e.ToString()); }
            finally {
                // Restore even if an I/O check fails; at this point the editor is no longer playing.
                Environment.SetEnvironmentVariable("TWELVEJADE_SAVEDIR", state.hadOldSave ? state.oldSave : null);
                Application.runInBackground = state.oldBackground;
                EditorApplication.update -= Tick; EditorApplication.playModeStateChanged -= PlayState;
                Application.logMessageReceived -= OnLog;
            }
            Pending("screenshot visual review", "Review this run's PNGs for art, readability and composition. Automated checks are not visual approval.");
            bool canExit = sceneSafe && restored && state.scenes.All(s => !string.IsNullOrEmpty(s.path) || s.roots.Length == 0);
            if (!canExit)
                Pending("editor left open", "An untitled scene has objects, or restoration was not proven; refusing to save/discard it by exiting.");
            Save(true);
            int code = state.checks.Any(r => r.status == "FAIL") ? 1 : 0;
            SessionState.EraseString(Key); state = null;
            if (canExit) EditorApplication.Exit(code);
        }
        [Serializable] sealed class SceneEnvelope { public SceneRecord[] scenes; }

        static IEnumerator Checks()
        {
            if (state.dossier) { yield return DossierChecks(); yield break; }
            if (state.music) { yield return MusicChecks(); yield break; }
            if (state.story) { yield return StoryChecks(); yield break; }
            CheckLedgerSerialization();
            Require("scratch repository", SamePath(Field(host.Repository, "root") as string, state.scratch) &&
                SamePath(Environment.GetEnvironmentVariable("TWELVEJADE_SAVEDIR"), state.scratch));
            Require("scratch starts empty", host.Repository.ReadAll().All(s => s.State == SlotState.Empty));
            canvasRoot = Field(host, "canvasObject") as GameObject;
            Require("real overlay canvas", canvasRoot != null && canvasRoot.GetComponent<Canvas>().renderMode == RenderMode.ScreenSpaceOverlay);
            itemsJson = ResourceText("Config/items"); items = ItemTable.Parse(itemsJson);
            merchants = MerchantTable.Parse(ResourceText("Config/merchants"));
            recipes = RecipeTable.Parse(ResourceText("Config/recipes"));
            Check("five real town merchants", merchants.Merchants.Select(m => m.Id).OrderBy(x => x).SequenceEqual(MerchantIds.OrderBy(x => x)));
            var preset = Resources.LoadAll<CharacterPreset>("Characters").OrderBy(p => p.id, StringComparer.Ordinal).FirstOrDefault();
            Require("real character preset", preset != null);
            BindAsset(preset);
            var baselines = new string[2];
            for (int slot = 1; slot <= 2; slot++) {
                var save = host.Repository.Create(slot, preset.id, slot == 1 ? "镇初新客" : "旧卷行旅", slot == 1 ? "male" : "female", 0, null, "shaxing");
                Give(save, "wheat-flour", 2); Give(save, "jinchuangyao", 1);
                if (slot == 2) {
                    foreach (string id in new[] { "red-dew-pill", "jade-renew-pill", "golden-return-pill", "qinglingcao",
                        "dark-iron-armor", "manual-sword-fragment", "manual-thunder-fragment", "manual-clarity-fragment", "jade-vein-core" }) Give(save, id, 1);
                    save.cultivation = new CultivationState { opened = 3, attempts = 3 };
                    save.manualStudies = ManualStudyRules.Skills.Where(s => !s.Basic).Select(s => new ManualStudyEntry {
                        id = s.Id, learned = true, attempts = 1, recorded = true, progress = 100
                    }).ToArray();
                    GearLedger.Record(save, GearSlots.Armor, "dark-iron-armor", 83);
                }
                baselines[slot - 1] = Ledger(save);
                host.Repository.Write(save);
                var disk = host.Repository.Read(slot);
                Require("fixture " + slot + " readable", disk.CanLoad);
                string actual = Ledger(disk.Data);
                Check("fixture " + slot + " initial assets survived disk", actual == baselines[slot - 1],
                    actual == baselines[slot - 1] ? "" : "expected=" + baselines[slot - 1] + "\nactual=" + actual);
            }
            yield return CheckFixture(1, "fresh", baselines[0]);
            for (int i = 0; i < MerchantIds.Length; i++) {
                string id = MerchantIds[i]; var merchant = merchants.Find(id);
                Require(id + " config present", merchant != null);
                Check(id + " hidden empty", merchant.Hidden.Count == 0);
                Check(id + " professional shelf", merchant.Stock.Select(s => s.itemId).OrderBy(x => x).SequenceEqual(Shelves[i].OrderBy(x => x)));
                Check(id + " all stock fanpin", merchant.Stock.All(s => items.Find(s.itemId)?.Tier == "fanpin"));
                foreach (string itemId in Shelves[i]) {
                    var icon = Resources.Load<Texture2D>("Art/Items/" + items.Find(itemId)?.Icon);
                    Require(id + " real icon " + itemId, icon != null); BindAsset(icon);
                }
                // A favoured destiny and an explicit black=true request must still show the normal shelf.
                Require(id + " black-market eligible test destiny", Trade.SeesBlackMarket(host.ActiveSave));
                host.ShowTrade(id, true); yield return Frames(3);
                Check(id + " real normal shop", host.CurrentPage == "trade" && host.TradeMerchantId == id && !host.TradeOnBlackMarket);
                Check(id + " black tab state absent", !host.BlackTabVisible());
                Check(id + " no visible black tab", !canvasRoot.GetComponentsInChildren<Button>(false).Any(b =>
                    b.gameObject.activeInHierarchy && b.GetComponentsInChildren<TMP_Text>(false).Any(t => Compact(t.text) == "黑市")));
                Check(id + " real shelf rows", host.RealShelfRowsForCheck() == Shelves[i].Length &&
                    host.ShelfOrder.OrderBy(x => x).SequenceEqual(Shelves[i].OrderBy(x => x)), string.Join(",", host.ShelfOrder));
                yield return Capture("shop-" + id, "fresh");
            }
            Check("fresh assets retained through five shop visits", Ledger(host.ActiveSave) == baselines[0]);
            host.Repository.Write(host.ActiveSave);
            Check("fresh assets persisted after shops", Ledger(host.Repository.Read(1).Data) == baselines[0]);
            yield return CheckFixture(2, "legacy", baselines[1]);
            Check("completed screenshot set", state.shots.Count == 17, "Expected six real pages for each fixture plus five shops.");
            Check("both scratch saves remain mortal", Enumerable.Range(1, 2).All(i => {
                var data = host.Repository.Read(i).Data;
                return data != null && !data.HasFlag(PrologueRules.DepartureFlag) && !data.HasFlag(PrologueRules.InitiationFlag);
            }));
        }

        static IEnumerator CheckFixture(int slot, string prefix, string baseline)
        {
            host.LoadSlot(host.Repository.Read(slot));
            yield return Until(() => host.CurrentPage == "town" && host.ActiveSave?.slot == slot &&
                UnityEngine.Object.FindAnyObjectByType<WorldPropLayer>()?.PlacedForCheck == true && host.HudDaoWheelForCheck != null,
                prefix + " real town ready", 20);
            yield return Frames(3);
            var save = host.ActiveSave; var wheel = host.HudDaoWheelForCheck;
            Check(prefix + " no departure/initiation flags", !save.HasFlag(PrologueRules.DepartureFlag) && !save.HasFlag(PrologueRules.InitiationFlag) && !PrologueRules.CanCultivate(save));
            Check(prefix + " load preserved assets", Ledger(save) == baseline);
            Check(prefix + " HUD real artwork", wheel.HasApprovedArtwork);
            Check(prefix + " HUD slot 4 cooking", LabelUnder(wheel.SlotButton(4), "Wheel label 4") == "烹饪");
            Check(prefix + " HUD slot 5 journal", LabelUnder(wheel.SlotButton(5), "Wheel label 5") == "行记");
            yield return Capture(prefix + "-hud", prefix);
            foreach (string method in new[] { "BuildMeridianMasterpiece", "BuildArtsMasterpiece" }) {
                string before = JsonUtility.ToJson(save); var oldModal = Field(host, "modal");
                Invoke(host, method);
                Check(prefix + " direct " + method + " refused", Equals(Field(host, "modal"), oldModal) &&
                    !Live("Meridian modal") && !Live("Arts modal") &&
                    (Field(host, "toast") as Component)?.GetComponentsInChildren<TMP_Text>(true).Any(t => t.text.Contains(PrologueRules.Refusal)) == true);
                Check(prefix + " direct " + method + " unchanged", before == JsonUtility.ToJson(save));
            }
            yield return Until(() => (Field(host, "toast") as RectTransform) == null, prefix + " refusal notice completed");
            yield return ClickHud(4, () => Live("Crafting"), prefix + " cooking via HUD");
            Check(prefix + " cooking retains inventory input shield", Field(host, "bagOverlay") is GameObject shield && shield.activeInHierarchy);
            var available = recipes.Recipes.Where(r => Crafting.IsAvailable(save, items, r)).Select(r => r.Id).ToArray();
            Check(prefix + " only ordinary recipe available", available.SequenceEqual(new[] { "recipe-chuibing" }), string.Join(",", available));
            var recipe = recipes.Find("recipe-chuibing");
            Check(prefix + " flour to two buns", recipe != null && recipe.CraftType == "cooking" && recipe.ResultItemId == "chuibing" && recipe.ResultCount == 2 &&
                recipe.Ingredients.Count == 1 && recipe.Ingredients[0].ItemId == "wheat-flour" && recipe.Ingredients[0].Count == 1 && items.Find(recipe.ResultItemId)?.Tier == "fanpin");
            var list = Find("Recipe content");
            Check(prefix + " actual single recipe row", list != null && list.transform.Cast<Transform>().Select(t => t.name).SequenceEqual(new[] { "Recipe recipe-chuibing" }));
            Check(prefix + " no alchemy/forging tabs", Live("Craft category cooking") && !Live("Craft category alchemy") && !Live("Craft category forging"));
            yield return Capture(prefix + "-cooking", prefix);
            host.CloseInventory(); yield return Frames(3);
            yield return ClickHud(5, () => host.ChapterScrollOpenForCheck, prefix + " journal via HUD");
            Check(prefix + " journal instead of arts", host.ChapterScrollOpenForCheck && !Live("Arts modal"));
            // The modal exists before its opening animation finishes; a fixed frame count
            // can capture a partially masked page on a fast editor. Wait for the real UI.
            yield return Until(() => {
                var panel = Field(host, "chapterPanel") as RectTransform;
                var modal = Field(host, "chapterModal") as RectTransform;
                var clip = panel != null ? panel.Find("Chapter unrolling mask") as RectTransform : null;
                return host.ChapterScrollOpenForCheck && modal != null &&
                    modal.GetComponent<CanvasGroup>()?.interactable == true && clip != null &&
                    Mathf.Abs(clip.rect.height - panel.rect.height) <= .5f;
            }, prefix + " journal fully unrolled before capture");
            yield return Capture(prefix + "-journal", prefix);
            host.CloseChapterPanel(); yield return Frames(3);
            yield return ClickHud(6, () => Live("Dao identity modal"), prefix + " identity via HUD");
            Check(prefix + " mortal identity caption", TextAt("Dao identity portrait caption") == "身世·乱世行牒");
            Check(prefix + " mortal identity body", TextAt("Dao identity roots title") == "凡人之身·前路未知" && Live("Mortal journey") && !Live("Dao roots"));
            Check(prefix + " no cultivation/study stat bonuses", CultivationRules.HpBonus(save) == 0 && CultivationRules.StaminaBonus(save) == 0 &&
                ManualStudyRules.HpBonus(save) == 0 && ManualStudyRules.AttackBonus(save) == 0 && ManualStudyRules.StaminaBonus(save) == 0);
            yield return Capture(prefix + "-identity", prefix);
            Invoke(host, "CloseModal"); yield return Frames(3);
            yield return ClickHud(1, () => Field(host, "bagOverlay") is GameObject bag && bag.activeInHierarchy, prefix + " inventory via HUD");
            yield return Capture(prefix + "-inventory", prefix);
            host.CloseInventory(); yield return Frames(3);
            yield return ClickHud(3, () => host.WorldCombatWheelOpen, prefix + " combat via HUD");
            yield return Frames(3);
            var combatWheel = host.WorldSkillWheelForCheck; var player = host.WorldCombatPlayer;
            Require(prefix + " real combat session and wheel", player != null && combatWheel != null && combatWheel.ArtworkReady);
            Check(prefix + " combat has no artificial enemy", !host.WorldCombatActive);
            Check(prefix + " only jinchuangyao usable", CombatConsumables.UsableIn(save, items, itemsJson).SequenceEqual(new[] { "jinchuangyao" }));
            Check(prefix + " actual pill selector", (string)Invoke(host, "FirstUsablePill") == "jinchuangyao");
            for (int index = 4; index <= 6; index++) {
                string id = CombatSkillWheel.Ids[index];
                var skill = (CombatSkillSlotState)Invoke(host, "ReadWorldCombatSlot", index);
                var button = combatWheel.SlotButton(index);
                var icon = button.GetComponentsInChildren<Image>(true).FirstOrDefault(image => image.name == "Combat skill icon " + id);
                Check(prefix + " " + id + " sealed state", skill.Sealed && !skill.Learned && !skill.Available && !ManualStudyRules.Learned(save, id));
                Check(prefix + " " + id + " disabled/sealed artwork", !button.interactable && icon != null && !icon.enabled &&
                    button.GetComponentsInChildren<TMP_Text>(false).Any(t => t.text == "未启") &&
                    button.GetComponentsInChildren<TMP_Text>(false).Any(t => t.text == "机缘未至"));
                string before = JsonUtility.ToJson(save); int hp = player.Hp, stamina = player.Stamina; var phase = player.Phase;
                var result = host.WorldCombatActForCheck(index);
                Check(prefix + " " + id + " real action gate", !result.Executed && result.Reason == PrologueRules.Refusal, result.Reason);
                Check(prefix + " " + id + " no action cost or record loss", hp == player.Hp && stamina == player.Stamina && phase == player.Phase && before == JsonUtility.ToJson(save));
            }
            yield return Capture(prefix + "-combat", prefix);
            host.CloseWorldCombatWheel(); yield return Frames(3);
            if (slot == 2) {
                Check("legacy learned records retained", save.manualStudies.Length == 3 && save.manualStudies.All(s => s.learned && s.recorded && s.progress == 100));
                Check("legacy cultivation retained", save.cultivation?.opened == 3 && save.cultivation.attempts == 3);
                var armor = GearLedger.Of(save, GearSlots.Armor);
                Check("legacy dark iron armor retains 83 durability", armor?.itemId == "dark-iron-armor" && armor.durability == 83);
                var equipped = (Field(host, "worldBattleGear") as CombatEquipmentService)?.Armor;
                Check("legacy real equipped armor retained but ineffective", equipped != null && equipped.Durability == 83 && !equipped.IsAvailable &&
                    equipped.EffectiveAttack == 0 && equipped.EffectiveDefense == 0 && equipped.EffectiveCritBonus == 0 && equipped.EffectiveRegenBonus == 0);
            }
            Check(prefix + " all assets/records retained", Ledger(save) == baseline);
            host.Repository.Write(save); var reloaded = host.Repository.Read(slot);
            Check(prefix + " records persisted through real repository", reloaded.CanLoad && Ledger(reloaded.Data) == baseline);
            if (slot == 2) yield return CheckLegacyEquipmentSwap(save);
        }

        static IEnumerator CheckLegacyEquipmentSwap(SaveData save)
        {
            var player = host.WorldCombatPlayer;
            var gear = Field(host, "worldBattleGear") as CombatEquipmentService;
            var battle = Field(host, "worldBattle") as WorldCombatSession;
            Require("legacy swap has existing player and equipment session", player != null && gear != null && battle != null &&
                gear.Armor?.Entry.ItemId == "dark-iron-armor" && gear.Armor.Durability == 83);
            Require("legacy swap owns mundane weapon", InventoryRules.Count(save.bag, "cudao") == 1 && gear.Equip("cudao"));
            Invoke(host, "RecordWorldGear"); host.Repository.Write(save);
            gear.WearWeapon(CombatActionType.Heavy);
            int weaponDurability = gear.Weapon.Durability;
            Require("legacy swap starts with unpersisted weapon wear", GearLedger.Of(save, GearSlots.Weapon).durability > weaponDurability);
            CombatRules.RestoreHp(player, player.MaxHp - 17);
            Require("legacy swap starts with real spent stamina", host.WorldCombatActForCheck(1).Executed && player.Stamina < player.MaxStamina);
            host.ShowInventory();
            int clothIndex = Array.FindIndex(save.bag, stack => stack != null && stack.id == "cubupao" && stack.count > 0);
            Require("legacy swap owns coarse cloth robe", clothIndex >= 0);
            Invoke(host, "BagClick", clothIndex);
            var equip = Field(host, "bagEquipButton") as Button;
            Require("legacy swap uses real enabled inventory button", equip != null && equip.isActiveAndEnabled && equip.interactable);
            int hp = player.Hp, stamina = player.Stamina;
            var phase = player.Phase; var action = player.CurrentAction; float phaseUntil = player.PhaseUntil;
            string bagBefore = JsonUtility.ToJson(new SaveData { bag = save.bag });
            equip.onClick.Invoke();
            Check("legacy swap keeps current player and combat session", ReferenceEquals(player, host.WorldCombatPlayer) &&
                ReferenceEquals(battle, Field(host, "worldBattle")) && ReferenceEquals(gear, Field(host, "worldBattleGear")));
            Check("legacy swap refreshes cached ordinary armor", gear.Armor?.Entry.ItemId == "cubupao" && gear.Armor.IsAvailable);
            Check("legacy swap applies ordinary defense immediately", gear.Armor?.EffectiveDefense == 4 &&
                player.DefenseModifier == 4 + gear.DestinyModifiers.DefenseBonus);
            Check("legacy swap retains wounded HP and spent stamina", player.Hp == hp && player.Stamina == stamina);
            Check("legacy swap does not restart the current action", player.Phase == phase && player.CurrentAction == action && player.PhaseUntil == phaseUntil);
            Check("legacy swap preserves other gear wear", gear.Weapon?.Durability == weaponDurability &&
                GearLedger.Of(save, GearSlots.Weapon)?.durability == weaponDurability);
            Check("legacy swap preserves every inventory item", bagBefore == JsonUtility.ToJson(new SaveData { bag = save.bag }));
            Check("legacy swap writes selected robe", GearLedger.Of(host.Repository.Read(save.slot).Data, GearSlots.Armor)?.itemId == "cubupao");
            host.CloseInventory();
            // Exercise the real disengage/persistence path synchronously, without moving a wild actor.
            battle.SetTarget(new CombatActorState("swap-check-foe", "Fixture opponent", 100, 1, 0, 30), null,
                new WorldCombatPoint(battle.PlayerPoint.X + 200, battle.PlayerPoint.Y));
            Require("legacy swap disengage has a target", host.WorldCombatActive);
            Invoke(host, "DisengageWorldCombat", false, false);
            var disk = host.Repository.Read(save.slot);
            Check("legacy swap disengages normally", !host.WorldCombatActive);
            Check("legacy swap survives disengage save", disk.CanLoad && GearLedger.Of(disk.Data, GearSlots.Armor)?.itemId == "cubupao" &&
                GearLedger.Of(disk.Data, GearSlots.Weapon)?.durability == weaponDurability);
            Check("legacy swap keeps old advanced armor owned", InventoryRules.Count(disk.Data.bag, "dark-iron-armor") == 1);
            host.ShowInventory(); Invoke(host, "BagClick", clothIndex);
            equip = Field(host, "bagEquipButton") as Button;
            Require("legacy unequip uses real enabled inventory button", equip != null && equip.isActiveAndEnabled && equip.interactable);
            hp = player.Hp; stamina = player.Stamina;
            equip.onClick.Invoke();
            Check("legacy unequip clears cache and save slot", gear.Armor == null && GearLedger.Of(save, GearSlots.Armor) == null);
            Check("legacy unequip removes ordinary defense immediately", player.DefenseModifier == gear.DestinyModifiers.DefenseBonus);
            Check("legacy unequip preserves HP and stamina", player.Hp == hp && player.Stamina == stamina);
            Invoke(host, "RecordWorldGear"); Invoke(host, "PersistBag");
            disk = host.Repository.Read(save.slot);
            Check("legacy unequip cannot be overwritten by cached gear", disk.CanLoad && GearLedger.Of(disk.Data, GearSlots.Armor) == null &&
                GearLedger.Of(disk.Data, GearSlots.Weapon)?.durability == weaponDurability);
            host.CloseInventory(); host.LoadSlot(disk);
            yield return Until(() => host.CurrentPage == "town" && host.ActiveSave?.slot == save.slot &&
                UnityEngine.Object.FindAnyObjectByType<WorldPropLayer>()?.PlacedForCheck == true, "legacy swapped save reloads", 20);
            Invoke(host, "EnsureWorldActions");
            gear = Field(host, "worldBattleGear") as CombatEquipmentService;
            Check("legacy equipment remains correct after real reload", gear != null && gear.Armor == null && gear.Weapon?.Durability == weaponDurability &&
                InventoryRules.Count(host.ActiveSave.bag, "dark-iron-armor") == 1);
        }

        static IEnumerator ClickHud(int slot, Func<bool> ready, string name)
        {
            yield return Until(() => host.HudDaoWheelForCheck != null && host.HudDaoWheelForCheck.isActiveAndEnabled &&
                !host.HudDaoWheelForCheck.PendingActivation && (bool)Invoke(host, "CanUseHudDaoWheel"), name + " input ready");
            var wheel = host.HudDaoWheelForCheck; var button = wheel.SlotButton(slot); int activations = wheel.ActivationCount;
            Require(name + " live button", button != null && button.isActiveAndEnabled && button.interactable);
            button.onClick.Invoke();
            yield return Until(ready, name + " real destination");
            Check(name + " dispatched once", wheel.ActivationCount == activations + 1 && wheel.LastActivatedSlot == slot);
            yield return Frames(3);
        }
        static IEnumerator Until(Func<bool> ready, string name, float timeout = 8)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (!ready() && Time.realtimeSinceStartup < end) yield return null;
            Require(name, ready());
        }
        static IEnumerator Frames(int count) { for (int i = 0; i < count; i++) yield return null; }
        static IEnumerator Capture(string name, string fixture)
        {
            FocusGameView(); Canvas.ForceUpdateCanvases();
            yield return null; yield return new WaitForEndOfFrame();
            Texture2D texture = null;
            try {
                texture = ScreenCapture.CaptureScreenshotAsTexture();
                Require(name + " real GameView image", texture != null && texture.width >= 320 && texture.height >= 180);
                var pixels = texture.GetPixels32(); var bins = new HashSet<int>(); int samples = 0, lit = 0;
                // Coarse colour/coverage gate catches blank frames, not art/composition defects.
                for (int y = 0; y < texture.height; y += Math.Max(1, texture.height / 80))
                    for (int x = 0; x < texture.width; x += Math.Max(1, texture.width / 120)) {
                        var p = pixels[y * texture.width + x]; samples++;
                        if (Math.Max(p.r, Math.Max(p.g, p.b)) > 18) lit++;
                        bins.Add((p.r >> 4) << 8 | (p.g >> 4) << 4 | p.b >> 4);
                    }
                Check(name + " rendered foreground", bins.Count >= 24 && lit > samples / 10,
                    "quantizedColours=" + bins.Count + "; lit=" + lit + "/" + samples);
                string path = Path.Combine(state.output, name + ".png"); File.WriteAllBytes(path, texture.EncodeToPNG());
                state.shots.Add(new Shot { file = name + ".png", fixture = fixture, page = host.CurrentPage,
                    width = texture.width, height = texture.height, frame = Time.frameCount, colors = bins.Count, sha256 = Sha(path) });
                Save();
            } finally { if (texture != null) UnityEngine.Object.Destroy(texture); }
        }
        static void Give(SaveData save, string id, int count)
        {
            Require("fixture item " + id, items.Find(id) != null && InventoryRules.Add(save.bag, items, id, count) == 0);
        }
        static void CheckLedgerSerialization()
        {
            var empty = new SaveData(); string baseline = Ledger(empty);
            Check("snapshot accepts Unity's empty-record roundtrip", baseline == Ledger(JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(empty))));
            foreach (string counter in new[] { "opened", "failures", "attempts" }) {
                var changed = new SaveData { cultivation = new CultivationState() };
                typeof(CultivationState).GetField(counter).SetValue(changed.cultivation, 1);
                Check("snapshot detects lost cultivation " + counter, Ledger(changed) != baseline);
            }
        }
        static string Ledger(SaveData save) => save == null ? "null" : JsonUtility.ToJson(new AssetLedger {
            coins = save.coins, reputation = save.localReputation,
            bag = (save.bag ?? Array.Empty<ItemStack>()).Where(s => s != null && !s.IsEmpty).GroupBy(s => s.id)
                .OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key + "=" + g.Sum(s => s.count)).ToArray(),
            gear = save.gear?.OrderBy(g => g.slot, StringComparer.Ordinal).Select(g => JsonUtility.ToJson(g)).ToArray(),
            studies = save.manualStudies?.OrderBy(s => s.id, StringComparer.Ordinal).Select(s => JsonUtility.ToJson(s)).ToArray(),
            crafts = save.crafts?.Select(c => JsonUtility.ToJson(c)).OrderBy(s => s, StringComparer.Ordinal).ToArray(),
            // Unity serializes an absent inline CultivationState as its all-zero record.
            // Compare those two empty forms equally, while retaining every nonzero counter.
            cultivation = JsonUtility.ToJson(save.cultivation ?? new CultivationState())
        });
        static GameObject Find(string name) => canvasRoot == null ? null : canvasRoot.GetComponentsInChildren<Transform>(false)
            .FirstOrDefault(t => t.name == name && t.gameObject.activeInHierarchy)?.gameObject;
        static bool Live(string name) => Find(name) != null;
        static string TextAt(string name) => Compact(Find(name)?.GetComponent<TMP_Text>()?.text);
        static string LabelUnder(Component parent, string name) => Compact(parent == null ? null : parent.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.name == name)?.text);
        static string Compact(string value) => new string((value ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray());
        static object Field(object target, string name) => target?.GetType().GetField(name, Flags)?.GetValue(target);
        static object Invoke(object target, string name, params object[] args)
        {
            var method = target?.GetType().GetMethod(name, Flags);
            if (method == null) throw new MissingMethodException(target?.GetType().Name, name);
            return method.Invoke(target, args);
        }
        static void FocusGameView()
        {
            var type = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
            if (type == null) throw new InvalidOperationException("Cannot find the real GameView.");
            EditorWindow.GetWindow(type).Focus();
        }
        static string ResourceText(string name)
        {
            var asset = Resources.Load<TextAsset>(name); Require("real resource " + name, asset != null);
            BindAsset(asset); return asset.text;
        }
        static void BindAsset(UnityEngine.Object asset)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            Require("resource has project asset " + asset.name, !string.IsNullOrEmpty(path) && path.StartsWith("Assets/", StringComparison.Ordinal));
            Bind(path); Bind(path + ".meta");
        }
        static void Bind(string relative)
        {
            string original = Path.Combine(state.source, relative), copy = Path.Combine(state.project, relative);
            bool exists = File.Exists(original) && File.Exists(copy);
            Require("source/copy input " + relative, exists && Sha(original) == Sha(copy));
            ProtectFile(original); ProtectFile(copy);
        }
        static void ProtectFile(string path)
        {
            path = Full(path);
            if (state.inputs.Any(s => SamePath(s.path, path))) return;
            state.inputs.Add(new Stamp { path = path, sha256 = Sha(path) }); Save();
        }
        static string Sha(string path)
        {
            using (var hash = SHA256.Create()) using (var file = File.OpenRead(path))
                return BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
        }
        static string[] DirectoryEntries(string root)
        {
            if (!Directory.Exists(root)) return Array.Empty<string>();
            return Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).Select(p => "dir:" + p.Substring(root.Length))
                .Concat(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(p => "file:" + p.Substring(root.Length) + ":" + Sha(p)))
                .OrderBy(p => p, StringComparer.Ordinal).ToArray();
        }
        static SceneRecord[] ReadScenes()
        {
            var result = new List<SceneRecord>();
            for (int i = 0; i < SceneManager.sceneCount; i++) {
                var scene = SceneManager.GetSceneAt(i);
                var roots = scene.isLoaded ? scene.GetRootGameObjects().Select(g => g.name).OrderBy(n => n, StringComparer.Ordinal).ToArray() : Array.Empty<string>();
                result.Add(new SceneRecord { path = scene.path, name = scene.name, loaded = scene.isLoaded,
                    active = scene == SceneManager.GetActiveScene(), dirty = scene.isDirty, roots = roots });
            }
            return result.ToArray();
        }
        static string AbsoluteEnvironment(string name)
        {
            string path = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) throw new InvalidOperationException("Set an absolute " + name + ".");
            return Full(path);
        }
        static string Full(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        static bool SamePath(string a, string b) => !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && string.Equals(Full(a), Full(b), StringComparison.OrdinalIgnoreCase);
        static bool Overlaps(string a, string b) => SamePath(a, b) || Full(a).StartsWith(Full(b) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            Full(b).StartsWith(Full(a) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        static void Require(string name, bool pass, string detail = "")
        {
            Check(name, pass, detail); if (!pass) throw new InvalidOperationException(name + ": " + detail);
        }
        static void Check(string name, bool pass, string detail = "")
        {
            state.checks.Add(new Row { name = name, status = pass ? "PASS" : "FAIL", detail = detail }); Save();
        }
        static void Pending(string name, string detail) { state.checks.Add(new Row { name = name, status = "PENDING", detail = detail }); }
        static void OnLog(string message, string trace, LogType type)
        {
            if (state != null && !finalizing && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert))
                Check("Unity runtime error", false, message + "\n" + trace);
        }
        static void Save(bool complete = false)
        {
            SessionState.SetString(Key, JsonUtility.ToJson(state));
            int passed = state.checks.Count(r => r.status == "PASS"), failed = state.checks.Count(r => r.status == "FAIL"), pending = state.checks.Count(r => r.status == "PENDING");
            string status = complete ? (failed == 0 ? "PASS" : "FAIL") : "RUNNING";
            File.WriteAllText(Path.Combine(state.output, "report.json"), JsonUtility.ToJson(new Report {
                status = status, unityVersion = Application.unityVersion, sourceProject = state.source, copyProject = state.project,
                scope = state.dossier ? "Title account icon, local save slots, transparent figures and four-way locomotion" : state.music ? "Supplied scene music, audio output, settings, quiet ambience and current costumes" : state.story ? "V2.5 sequential comic, choices, replay, failure retry and rural costumes" : "Qingshi mortal prologue",
                passed = passed, failed = failed, pending = pending, checks = state.checks, screenshots = state.shots, inputs = state.inputs
            }, true));
            File.WriteAllLines(Path.Combine(state.output, "report.txt"), new[] {
                "Automated=" + status + "; PASS=" + passed + " FAIL=" + failed + " PENDING=" + pending + " screenshots=" + state.shots.Count,
                "Visual review remains PENDING; only this run's real GameView PNGs are evidence."
            }.Concat(state.checks.Select(r => r.status + " " + r.name + (string.IsNullOrEmpty(r.detail) ? "" : " : " + r.detail)))
                .Concat(state.shots.Select(s => "SCREENSHOT " + s.file + " " + s.width + "x" + s.height + " frame=" + s.frame + " sha256=" + s.sha256)));
        }
    }
    public sealed class PrologueTownShotPump : MonoBehaviour { }
}
