using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TwelveJade.Combat;
using TwelveJade.Core;
using TwelveJade.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Editor
{
    // M4-03/M4-04 的战斗页 Play 验收：开战、命中、闪避、受击、胜利结算（奖励入包 + 只发一次）、
    // 失败返回、Esc 等价路径与重复进出。独立于 FrontEndShot 运行（FrontEndShot 归交易会话维护）：
    // 批处理入口 TwelveJade.Editor.CombatShot.Capture，截图到 Tools/screenshots/combat-*.png。
    // 战斗钟可注入：除了真实帧，全部用 CombatClockAdvance 确定性推进（内部已切小步）。
    [InitializeOnLoad]
    public static class CombatShot
    {
        const int Width = 1920, Height = 1080;
        const string SessionKey = "CombatShot.Active";
        static readonly string[] PageNames = { "combat" };

        static FrontEndController controller;
        static Camera camera;
        static RenderTexture target;
        static string shotsDir;
        static readonly List<string> problems = new List<string>();
        static float runStart;
        static bool ran;
        static int victorySlot, defeatSlot;

        static CombatShot()
        {
            if (!SessionState.GetBool(SessionKey, false)) return;
            shotsDir = SessionState.GetString(SessionKey + ".Dir", "");
            runStart = Time.realtimeSinceStartup;
            Attach();
        }

        static void Attach()
        {
            Application.logMessageReceived -= OnLog;
            EditorApplication.update -= Pump;
            Application.logMessageReceived += OnLog;
            EditorApplication.update += Pump;
        }

        public static void Capture()
        {
            SessionState.SetBool(SessionKey, true);
            var project = Directory.GetParent(Application.dataPath).FullName;
            var repoRoot = Directory.GetParent(Directory.GetParent(project).FullName).FullName;
            shotsDir = Path.Combine(repoRoot, "Tools", "screenshots");
            Directory.CreateDirectory(shotsDir);
            // 与 FrontEndShot 同一套临时存档约定：验收绝不写进玩家真实存档。
            var scratch = Path.Combine(Path.GetTempPath(), "twelve-jade-combat-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            Environment.SetEnvironmentVariable("TWELVEJADE_SAVEDIR", scratch);
            SessionState.SetString(SessionKey + ".Dir", shotsDir);
            Debug.Log("[CombatShot] scratch saves at " + scratch);
            Attach();
            EditorSceneManager.OpenScene("Assets/Scenes/FrontEnd.unity", OpenSceneMode.Single);
            runStart = Time.realtimeSinceStartup;
            EditorApplication.isPlaying = true;
        }

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (!EditorApplication.isPlaying) return;
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            if (condition.StartsWith("[Licensing::Module]")) return;
            if (stackTrace != null && stackTrace.Contains("UnityEditor.Search")) return;
            problems.Add(type + ": " + condition);
        }

        static void Pump()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            try { Step(); }
            catch (Exception ex) { Fail("驱动异常: " + ex); }
        }

        static void Step()
        {
            if (Time.realtimeSinceStartup - runStart > 300f) { Fail("验收超时:超过 300 秒仍未完成"); return; }
            if (!EditorApplication.isPlaying) return;
            if (controller == null)
            {
                controller = UnityEngine.Object.FindAnyObjectByType<FrontEndController>();
                if (controller == null) return;
                SetupCapture();
                Debug.Log("[CombatShot] play mode ready");
            }
            if (ran) return;
            ran = true;
            try { Run(); }
            catch (Exception ex) { Fail("验收异常: " + ex); }
            Finish();
        }

        static void Run()
        {
            // 胜利路径档：先建——战斗打在 activeSave 上，断言也读同一档。
            victorySlot = FreeSlot();
            Check(victorySlot > 0, "应有空档位供验收使用");
            controller.ShowCharacterCreation(victorySlot);
            Check(controller.CreateFromDraft(victorySlot) != null, "胜利路径建档成功");

            // 开战：走 M4-04 的城镇入口语义，直接以遭遇 id 进战斗页。
            controller.ShowCombat("encounter-boar-01");
            Check(controller.CurrentPage == "combat", "战斗页应已打开");
            Check(controller.CombatPlayer != null && controller.CombatFoe != null, "战斗双方应已就位");
            Check(controller.CombatPlayer.Hp == 60 && controller.CombatFoe.Hp == 60, "开战时双方满血");
            Check(ActiveObjects("Combat settle") == 0, "开战时不应有结算面板");
            // 先推一次钟再拍：妖的 anchoredPosition 由 RefreshCombatView 落位，刚开战时它还停在
            // RectTransform 的原点（画面左上角之外）。这张 combat-01-start 从 M4-03 起一直没画过妖——
            // N5 这批加像素探针时才撞出来，图上"妖不见了"与"妖是块白方"是两回事，都该红。
            controller.CombatClockAdvance(.05f);
            Capture("combat-01-start");

            // ==== N5-EnemyArt 的状态断言（本夹具那一条）====
            // 野猪这一场该读到遭遇声明里那一张真图：不是回退标记，也不是一块静默的白方。
            // 期望从 encounter.art 现推（夜4 换图或撤图都会自动改判，不会变成一片红），
            // 而判定的一侧来自**渲染结果**——只拿新加的钩子说"我读到图了"是空的：
            // 旧版那块 whiteTexture 在任何钩子上都长得像"有东西画在那里"。
            var foeEncounter = controller.World?.Encounters.FirstOrDefault(e => e.Id == "encounter-boar-01");
            var declaredArt = foeEncounter == null || string.IsNullOrEmpty(foeEncounter.Art) ? null
                : Resources.Load<Texture2D>(foeEncounter.Art);
            var foePixels = ProbeFoeSlot(controller.CombatFoeSlotRect());
            var whitePercent = foePixels.total == 0 ? -1 : foePixels.white * 100 / foePixels.total;
            // 8000 个像素是**样本闸**：探针取不到像素时比例是 0/0，"纯白 < 20%"会拿
            // "一个像素都没数到"判成通过——同批在 CombatEngineShot 里真就这么空转过一轮。
            Check(foePixels.total >= 8000 &&
                (declaredArt != null
                    ? !controller.CombatFoeArtIsFallback &&
                      controller.CombatFoeArtSource == foeEncounter.Art &&
                      controller.CombatFoePictureVisible && whitePercent < 20
                    : controller.CombatFoeArtIsFallback && whitePercent < 20),
                "野猪这一场读的是声明那一张真图（不是回退标记，也不是一块静默白方）",
                "声明=" + (foeEncounter?.Art ?? "(读不到遭遇表)") + "，实取=" +
                (string.IsNullOrEmpty(controller.CombatFoeArtSource) ? "(回退绘制)" : controller.CombatFoeArtSource) +
                "，槽位纯白 " + (whitePercent < 0 ? "无样本" : whitePercent + "%（" + foePixels.total + " 个像素）"));

            // 命中：贴身 → 真点「轻 击」→ 推进到命中帧 → 野猪掉血。
            controller.CombatPlaceForCheck(-0.4f, 0.5f);
            var light = FindActiveButton("轻 击");
            Check(light != null, "轻击按钮应可点");
            light?.onClick.Invoke();
            Check(controller.CombatPlayerPhase == "Startup" && controller.CombatPlayer.Stamina == 52,
                "起手进入前摇并扣精力");
            controller.CombatClockAdvance(0.13f);
            Check(controller.CombatFoe.Hp == 44, "轻击命中：野猪 60→44",
                controller.CombatFoe.Hp + "");
            Check(ActiveObjects("Puppet actor") >= 1, "玩家纸偶应存在（分层纸偶 v3 切线）");
            controller.CombatClockAdvance(0.4f);
            Check(controller.CombatFoe.Hp == 44, "同一击不重复扣血");
            Check(controller.CombatPlayerPhase == "Idle", "收招后回到静立");
            Capture("combat-02-strike");

            // 受击：等野猪近身出手，玩家挨一下。
            var playerBefore = controller.CombatPlayer.Hp;
            var sawHitstun = false;
            for (var i = 0; i < 200 && controller.CombatPlayer.Hp == playerBefore; i++)
            {
                controller.CombatClockAdvance(0.06f);
                sawHitstun |= controller.CombatPlayerPhase == "Hitstun";
            }
            Check(controller.CombatPlayer.Hp < playerBefore, "野猪的轻击应打掉玩家血");
            Check(sawHitstun, "受击应进入硬直");

            // 闪避：盯到野猪前摇就闪，这一交换分毫不少。
            playerBefore = controller.CombatPlayer.Hp;
            var dodged = false;
            for (var i = 0; i < 400 && !dodged; i++)
            {
                controller.CombatClockAdvance(0.05f);
                if (controller.CombatFoePhase != "Startup") continue;
                FindActiveButton("闪 避")?.onClick.Invoke();
                controller.CombatClockAdvance(0.3f);
                dodged = controller.CombatPlayer.Hp == playerBefore;
            }
            Check(dodged, "闪避应躲过野猪的轻击");
            Capture("combat-03-dodge");

            // 胜利：贴身重击到野猪倒下，结算面板、奖励入包、标记记档。
            // 章推进在战斗前：轴线先推到 NightRoar（任务链也先做完——defeat 目标
            // 由这场真战斗等效达成，进度在胜利后补满）。
            var chapters = controller.Chapter;
            // 命格是建档时随机掷的：夜里那段要"八字够重"才开得起来，测试显式定成杀星入命。
            controller.ActiveSave.destiny = "shaxing";
            var when = controller.ActiveSave.WorldTimeNow();
            ChapterLedger.Enter(controller.ActiveSave, chapters, ChapterSegment.Prologue, when, out _);
            ChapterLedger.Enter(controller.ActiveSave, chapters, ChapterSegment.Arrival, when, out _);
            // 税与役段的前置任务：TaxAndLabor 段要求它已了结，而任务状态是惰性的。
            // （-defeat 类目标要真实战斗，这里只把 talk 类目标补满、任务推到 Completed。）
            var taxQuest = controller.Quests.Find("tax-and-labor");
            QuestLedger.Of(controller.ActiveSave, controller.Quests, "tax-and-labor");
            QuestLedger.TryStart(controller.ActiveSave, taxQuest);
            foreach (var objective in taxQuest.Objectives)
                for (var i = 0; i < objective.Count + 2; i++)
                {
                    if (QuestLedger.Progress(controller.ActiveSave, taxQuest, objective.Id) >= objective.Count) break;
                    if (!QuestLedger.Advance(controller.ActiveSave, taxQuest, objective.Id, 1))
                        throw new Exception("税与役目标推不动：" + objective.Id);
                }
            QuestLedger.TryComplete(controller.ActiveSave, taxQuest);
            ChapterLedger.Enter(controller.ActiveSave, chapters, ChapterSegment.TaxAndLabor, when, out _);
            ChapterLedger.Enter(controller.ActiveSave, chapters, ChapterSegment.FreeRoam, when, out _);
            var coinsBefore = controller.TradeCoins;
            for (var i = 0; i < 12 && !controller.CombatFoe.IsDead; i++)
            {
                controller.CombatPlaceForCheck(-0.4f, 0.5f);
                FindActiveButton("重 击")?.onClick.Invoke();
                controller.CombatClockAdvance(0.78f);
            }
            Check(controller.CombatFoe.IsDead, "重击循环应击倒野猪妖");
            Check(controller.CombatSettled && ActiveObjects("Combat settle") == 1, "胜利应出现唯一一张结算面板");
            // FreeRoam 结算后 night-beast-roar 才 Available：刷新后接取，补满进度（defeat 目标
            // 由刚才那场真战斗等效达成），完成，然后 NightRoar 段的章结算（SettleVictory 已做）。
            var nightQuest = controller.Quests.Find("night-beast-roar");
            QuestLedger.RefreshAvailability(controller.ActiveSave, controller.Quests);
            QuestLedger.TryStart(controller.ActiveSave, nightQuest);
            foreach (var objective in nightQuest.Objectives)
                while (QuestLedger.Progress(controller.ActiveSave, nightQuest, objective.Id) < objective.Count)
                    QuestLedger.Advance(controller.ActiveSave, nightQuest, objective.Id, objective.Count);
            if (!QuestLedger.TryComplete(controller.ActiveSave, nightQuest))
                throw new Exception("night-beast-roar 结算不了，当前状态：" +
                    QuestLedger.StatusOf(controller.ActiveSave, "night-beast-roar"));
            // 任务了结后推进夜里的兽吼段：时钟推到夜里（nightOnly=1），命格要够重。
            var nightWhen = new WorldTime { day = 2, minuteOfDay = 1320 };
            ChapterLedger.Enter(controller.ActiveSave, chapters, ChapterSegment.NightRoar, nightWhen, out var nightError);
            if ((ChapterSegment)controller.ChapterReached < ChapterSegment.NightRoar)
                throw new Exception("进不了夜里的兽吼段：" + nightError);
            var save = controller.Repository.Read(victorySlot).Data;
            Check(save.coins == coinsBefore + 24, "胜利赏钱入档");
            Check(InventoryRules.Count(save.bag, "larou") == 2 && InventoryRules.Count(save.bag, "caoyao") == 3,
                "掉落腊肉×2、药草×3 应入行囊");
            Check(save.HasFlag("encounter-boar-01"), "一次性奖励标记应记档");
            Check(QuestLedger.IsDone(controller.ActiveSave, "night-beast-roar"), "夜里的兽吼任务应已了结");
            // 章推进：NightRoar 段的结算在胜利时已由 SettleVictory 完成。
            Check((ChapterSegment)controller.ChapterReached == ChapterSegment.NightRoar,
                "轴线应已推到 NightRoar", "当前 " + (ChapterSegment)controller.ChapterReached);
            Check((ChapterSegment)controller.ChapterReached >= ChapterSegment.BeastFight == false,
                "BeastFight 尚未结算（要等这一仗）");
            Capture("combat-04-victory");

            // 重复讨伐：再开一场并再次击杀，赏钱与掉落不再发。
            var coinsAfterFirst = controller.TradeCoins;
            controller.ShowCombat("encounter-boar-01");
            Check(controller.CombatFoe.Hp == 60, "重开一场野猪满血");
            for (var i = 0; i < 12 && !controller.CombatFoe.IsDead; i++)
            {
                controller.CombatPlaceForCheck(-0.4f, 0.5f);
                FindActiveButton("重 击")?.onClick.Invoke();
                controller.CombatClockAdvance(0.78f);
            }
            Check(controller.CombatSettled && controller.TradeCoins == coinsAfterFirst,
                "重复讨伐不再发赏钱");
            Check(controller.LastSpoils != null && controller.LastSpoils.CoinsGranted == 0 &&
                controller.LastSpoils.Drops.Count == 0, "重复讨伐不掉落");

            // Esc 等价路径：撤离回镇，重进满血、无残留面板。
            controller.ShowTown();
            Check(controller.CurrentPage == "town", "撤离应回到青石镇");
            controller.ShowCombat("encounter-boar-01");
            Check(controller.CombatPlayer.Hp == 60 && ActiveObjects("Combat settle") == 0,
                "重进战斗应满血且无残留结算");
            controller.ShowTown();

            // 失败路径：另起一档站桩挨打，验失败结算与不发奖励。
            defeatSlot = FreeSlot();
            Check(defeatSlot > 0, "应有空档位供失败路径使用");
            controller.ShowCharacterCreation(defeatSlot);
            Check(controller.CreateFromDraft(defeatSlot) != null, "失败路径建档成功");
            controller.ShowCombat("encounter-boar-01");
            var defeatCoins = controller.TradeCoins;
            for (var i = 0; i < 600 && !controller.CombatPlayer.IsDead; i++)
                controller.CombatClockAdvance(0.06f);
            Check(controller.CombatPlayer.IsDead && controller.CombatSettled &&
                ActiveObjects("Combat settle") == 1, "站桩挨打应进入失败结算");
            // D-08：原来这一条把"不发战利品"和"不扣钱"混在一起断言，等于同时封死了两件事——
            // 战败要扣药资（ADR-028）就必然撞它。现在拆成两条各管各的，再加一条落盘往返。
            Check(controller.LastSpoils == null ||
                (controller.LastSpoils.CoinsGranted == 0 && controller.LastSpoils.Drops.Count == 0),
                "断言 A：战败不发战利品（赏钱 0 且掉落 0 件）");
            var expectedFee = (int)Math.Floor(defeatCoins * CombatDefeatProtection.MedicalFeeRatio);
            Check(controller.TradeCoins == Math.Max(0, defeatCoins - expectedFee),
                "断言 B：药资按 10% 另计并真扣（" + defeatCoins + " → " + controller.TradeCoins + "）");
            var defeatDisk = controller.Repository.Read(defeatSlot).Data;
            Check( defeatDisk.coins == controller.TradeCoins && defeatDisk.coins >= 0,
                "断言 C：扣款落盘往返（重新读档，铜钱是扣后的值且不为负）· 磁盘 " + defeatDisk.coins);
            Capture("combat-05-defeat");
            FindActiveButton("返回青石镇")?.onClick.Invoke();
            Check(controller.CurrentPage == "town", "失败后应能返回青石镇");
        }

        static void SetupCapture()
        {
            camera = Camera.main;
            Check(camera != null, "主相机存在");
            var canvas = UnityEngine.Object.FindAnyObjectByType<Canvas>();
            Check(canvas != null, "Canvas 存在");
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 10;
            target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            camera.targetTexture = target;
            camera.Render();
            Canvas.ForceUpdateCanvases();
        }

        static int FreeSlot()
        {
            foreach (var slot in controller.Repository.ReadAll())
                if (slot.State == SlotState.Empty) return slot.Slot;
            return 0;
        }

        // 批处理里整个 Run 同帧跑完：页面渐入的 CanvasGroup 没等到真实帧，alpha 还停在 0。
        // 截图是验画面不是验渐入动画，先把当前页拉满再拍。
        static void ShowPageContent()
        {
            foreach (var group in Resources.FindObjectsOfTypeAll<CanvasGroup>())
                if (group.gameObject.scene.IsValid() && group.gameObject.name.StartsWith("Page "))
                    group.alpha = 1f;
        }

        static void Capture(string label)
        {
            ShowPageContent();
            Canvas.ForceUpdateCanvases();
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            camera.Render();
            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            var path = Path.Combine(shotsDir, label + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
            Debug.Log("[CombatShot] captured " + path);
        }

        static Button FindActiveButton(string text)
        {
            return Resources.FindObjectsOfTypeAll<Button>()
                .FirstOrDefault(button => button.gameObject.scene.IsValid() && button.gameObject.activeInHierarchy &&
                    button.gameObject.name == "Button " + text);
        }

        // 把妖那块槽位渲染一遍再数像素（纯白 = 三通道都 ≥251）。
        // 判据要的独立一侧就是这个：钩子说的是"我打算画什么"，像素说的是"屏幕上真有什么"。
        // 画布 1920×1080 与这张 RenderTexture 同尺寸，所以画布坐标即像素坐标（自上而下翻转一次）。
        static (int white, int total) ProbeFoeSlot(Rect slot)
        {
            ShowPageContent();
            Canvas.ForceUpdateCanvases();
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            camera.Render();
            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            var white = 0;
            var total = 0;
            if (!float.IsNaN(slot.xMin))
            {
                var x0 = Mathf.Max(0, Mathf.CeilToInt(slot.xMin));
                var x1 = Mathf.Min(Width - 1, Mathf.FloorToInt(slot.xMax));
                var y0 = Mathf.Max(0, Height - 1 - Mathf.FloorToInt(slot.yMax));
                var y1 = Mathf.Min(Height - 1, Height - 1 - Mathf.CeilToInt(slot.yMin));
                for (var y = y0; y <= y1; y++)
                    for (var x = x0; x <= x1; x++)
                    {
                        var c = tex.GetPixel(x, y);
                        total++;
                        if (c.r > .985f && c.g > .985f && c.b > .985f) white++;
                    }
            }
            UnityEngine.Object.Destroy(tex);
            Debug.Log("[CombatShot] 像素探针：妖槽位 " + total + " 个像素，纯白 " + white +
                (total == 0 ? "（无样本：槽位矩形取不到）" : "（" + white * 100 / total + "%）"));
            return (white, total);
        }

        static int ActiveObjects(string name)
        {
            return Resources.FindObjectsOfTypeAll<RectTransform>()
                .Count(rect => rect.gameObject.scene.IsValid() && rect.gameObject.activeInHierarchy && rect.gameObject.name == name);
        }

        static void Check(bool condition, string message, string detail = null)
        {
            var full = string.IsNullOrEmpty(detail) ? message : message + " · " + detail;
            if (!condition) problems.Add("断言失败: " + full);
            else Debug.Log("[CombatShot] ok: " + full);
        }

        static void Finish()
        {
            SessionState.SetBool(SessionKey, false);
            Application.logMessageReceived -= OnLog;
            EditorApplication.update -= Pump;
            if (victorySlot > 0) controller?.Repository.Delete(victorySlot);
            if (defeatSlot > 0) controller?.Repository.Delete(defeatSlot);
            var scratch = Environment.GetEnvironmentVariable("TWELVEJADE_SAVEDIR");
            try { if (!string.IsNullOrEmpty(scratch) && Directory.Exists(scratch)) Directory.Delete(scratch, true); }
            catch (Exception ex) { Debug.LogWarning("无法清理验收临时目录：" + ex.Message); }
            if (target != null && camera != null) { camera.targetTexture = null; target.Release(); }
            Debug.Log("[CombatShot] finished, " + problems.Count + " problem(s)");
            foreach (var problem in problems.ToArray()) Debug.LogError(problem);
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += () => EditorApplication.Exit(problems.Count == 0 ? 0 : 1);
        }

        static void Fail(string message)
        {
            SessionState.SetBool(SessionKey, false);
            Debug.LogError(message);
            EditorApplication.update -= Pump;
            var scratch = Environment.GetEnvironmentVariable("TWELVEJADE_SAVEDIR");
            try { if (!string.IsNullOrEmpty(scratch) && Directory.Exists(scratch)) Directory.Delete(scratch, true); }
            catch { /* 尽力清理 */ }
            EditorApplication.Exit(1);
        }
    }
}
