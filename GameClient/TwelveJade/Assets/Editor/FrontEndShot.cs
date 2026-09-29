using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TwelveJade.Core;
using TwelveJade.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Editor
{
    // 批处理 Play 验收：进入 Play 模式后逐页导航，把 UI 渲染到 RenderTexture 存成 PNG，
    // 并对存档流程做端到端断言。任何引擎错误或断言失败都会让进程以非零码退出。
    // 进入 Play 模式会触发 domain reload，静态订阅会丢失，因此用 [InitializeOnLoad] + SessionState 恢复。
    [InitializeOnLoad]
    public static class FrontEndShot
    {
        const int Width = 1920, Height = 1080;
        const string SessionKey = "FrontEndShot.Active";
        static readonly string[] PageNames = { "menu", "slots", "new-game", "character-creation", "preview", "town", "inventory", "settings", "credits" };
        static readonly HashSet<string> capturedShots = new HashSet<string>();

        static FrontEndController controller;
        static Camera camera;
        static RenderTexture target;
        static string shotsDir;
        static readonly List<string> problems = new List<string>();
        static int shotIndex = -1;
        static float waitStart;
        static bool waiting;
        static Action pendingAfter;
        static string currentShot;
        static float runStart;
        static int createdSlot;
        static string controllerPage => controller != null ? controller.CurrentPage : "<controller 未找到>";

        static FrontEndShot()
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
            var project = Directory.GetParent(Application.dataPath).FullName; // TwelveJade 工程
            var repoRoot = Directory.GetParent(Directory.GetParent(project).FullName).FullName; // 仓库根目录
            shotsDir = Path.Combine(repoRoot, "Tools", "screenshots");
            capturedShots.Clear();
            SessionState.SetString(SessionKey + ".Dir", shotsDir);
            Directory.CreateDirectory(shotsDir);
            Attach();
            EditorSceneManager.OpenScene("Assets/Scenes/FrontEnd.unity", OpenSceneMode.Single);
            runStart = Time.realtimeSinceStartup;
            EditorApplication.isPlaying = true;
        }

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            // 验收只计 Play 运行期的问题：启动期的编辑器噪音（许可令牌、Search 索引等）不算账。
            if (!EditorApplication.isPlaying) return;
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            // 与 Hub 共存时许可模块可能刷出瞬态错误；全新 Library 的 Search 索引是编辑器内部 bug——都与工程无关。
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
            if (Time.realtimeSinceStartup - runStart > 240f) { Fail("验收超时:超过 240 秒仍未完成,当前页面 " + controllerPage); return; }
            if (shotIndex < 0)
            {
                if (!EditorApplication.isPlaying) return;
                controller = UnityEngine.Object.FindAnyObjectByType<FrontEndController>();
                if (controller == null) return;
                SetupCapture();
                Debug.Log("[FrontEndShot] play mode ready, controller found");
                AdvanceTo(0);
                return;
            }
            if (!waiting) return;
            if (Time.realtimeSinceStartup - waitStart < .45f) return;
            waiting = false;
            if (!Shot()) return;
            if (shotIndex >= PageNames.Length - 1) Finish();
            else AdvanceTo(shotIndex + 1);
        }

        static void AdvanceTo(int index)
        {
            shotIndex = index;
            currentShot = PageNames[index];
            switch (index)
            {
                case 0:
                    Open(controller.ShowMenu);
                    break;
                case 1:
                    Open(() => controller.ShowSlots(false));
                    break;
                case 2:
                    Open(() => controller.ShowSlots(true));
                    break;
                case 3:
                    Open(() =>
                    {
                        controller.ShowCharacterCreation(2);
                        controller.SetCreationGender("female");
                        controller.SetCreationFaceStyle(1);
                    });
                    break;
                case 4:
                {
                    var slot = FreeSlot();
                    if (slot > 0)
                    {
                        controller.ShowCharacterCreation(slot);
                        controller.SetCreationGender("female");
                        controller.SetCreationFaceStyle(1);
                        var save = controller.CreateFromDraft(slot);
                        Check(save != null, "落笔创建应成功");
                        Check(controller.Repository.Read(slot).CanLoad, "新建档位应可读取");
                        Check(InventoryRules.UsedSlots(save.bag) == 4 && save.coins == ItemTable.StartingCoins,
                            "新档应带开局行囊与盘缠");
                        createdSlot = slot;
                        Open(() => controller.ShowPreview(save), () =>
                        {
                            VerifyMotionButtons();
                            VerifyFateDialogue();
                            controller.ShowPreview(save);
                        });
                    }
                    else
                    {
                        var existing = controller.Repository.Latest();
                        Check(existing != null, "找不到空档位时应有既有档位");
                        Open(() => controller.ShowPreview(existing.Data));
                    }
                    break;
                }
                case 5:
                    Open(() => controller.ShowTown(), VerifyTownBag);
                    break;
                case 6:
                    Open(controller.ShowInventory, VerifyInventory);
                    break;
                case 7:
                    Open(controller.ShowSettings);
                    break;
                case 8:
                    Open(controller.ShowCredits);
                    break;
            }
        }

        static void Open(Action page, Action after = null)
        {
            pendingAfter = after;
            page();
            waitStart = Time.realtimeSinceStartup;
            waiting = true;
        }

        static int FreeSlot()
        {
            foreach (var slot in controller.Repository.ReadAll())
                if (slot.State == SlotState.Empty) return slot.Slot;
            return 0;
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

        static bool Shot()
        {
            if (pendingAfter != null)
            {
                pendingAfter();
                pendingAfter = null;
                waitStart = Time.realtimeSinceStartup;
                waiting = true;
                return false;
            }
            if (currentShot == "preview")
            {
                Check(controller.CurrentPage == "preview", "预览截图应停留在预览页");
                Check(FindActiveButton("返回") != null, "预览页返回按钮应可见");
            }
            if (currentShot == "town")
            {
                Check(controller.CurrentPage == "town", "城镇页应已打开");
                Check(UnityEngine.Object.FindAnyObjectByType<PuppetActor>() != null, "城镇页应有分层纸偶角色");
                Check(UnityEngine.Object.FindAnyObjectByType<PuppetActor>()?.CurrentMotion == PuppetActor.Motion.Idle,
                    "入镇时角色先静立");
            }
            if (currentShot == "inventory")
            {
                Check(controller.CurrentPage == "town", "行囊是城镇页上的浮层");
                Check(controller.BagCellCount == InventoryRules.SlotCount, "行囊应有 24 格");
                Check(controller.CurrentBag != null, "行囊应已挂上当前存档");
            }
            Capture(string.Format("play-{0:00}-{1}", shotIndex + 1, currentShot), currentShot);
            return true;
        }

        // 任意时刻抓一帧画布（页面流程与序章问命演出共用）；capturedToken 用于页面流程登记短名。
        static void Capture(string label, string capturedToken = null)
        {
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
            capturedShots.Add(capturedToken ?? label);
            Debug.Log("[FrontEndShot] captured " + path);
        }

        static Button FindActiveButton(string text)
        {
            return Resources.FindObjectsOfTypeAll<Button>()
                .FirstOrDefault(button => button.gameObject.scene.IsValid() && button.gameObject.activeInHierarchy &&
                    button.gameObject.name == "Button " + text);
        }

        static int ActiveObjects(string name)
        {
            return Resources.FindObjectsOfTypeAll<RectTransform>()
                .Count(rect => rect.gameObject.scene.IsValid() && rect.gameObject.activeInHierarchy && rect.gameObject.name == name);
        }

        static void VerifyMotionButtons()
        {
            var actor = Resources.FindObjectsOfTypeAll<CharacterActor>()
                .FirstOrDefault(value => value.gameObject.scene.IsValid() && value.gameObject.activeInHierarchy);
            Check(actor != null, "预览页应创建角色动作对象");
            if (actor == null) return;
            for (var i = 1; i < CharacterActor.MotionNames.Length; i++)
            {
                var button = FindActiveButton(CharacterActor.MotionNames[i]);
                Check(button != null, "动作按钮存在: " + CharacterActor.MotionNames[i]);
                if (button == null) continue;
                button.onClick.Invoke();
                Check(actor.CurrentMotion == (CharacterActor.Motion)i,
                    "动作按钮生效: " + CharacterActor.MotionNames[i]);
            }
            controller.SetMotion((int)CharacterActor.Motion.Walk);
            Check(actor.CurrentMotion == CharacterActor.Motion.Walk, "动作验收后恢复行走姿态");
        }

        static void VerifyFateDialogue()
        {
            controller.ShowCharacterCreation(2);
            controller.OpenFateDialogue();
            Check(ActiveObjects("Fate dialogue") == 1, "序章问命只有一只活动遮罩");
            var seat = FindActiveButton("落座听问");
            Check(seat != null, "问命开场白按钮存在");
            if (seat == null) return;
            Capture("fate-01-intro");
            seat.onClick.Invoke();
            for (var i = 0; i < FateDialogue.Questions.Length; i++)
            {
                Check(ActiveObjects("Fate dialogue") == 1, "问命第 " + (i + 1) + " 问只有一只活动遮罩");
                var option = FindActiveButton(FateDialogue.Questions[i].Options[0].Text);
                Check(option != null, "问命选项存在: " + (i + 1));
                if (option == null) return;
                if (i == 0) Capture("fate-02-question");
                option.onClick.Invoke();
                var next = FindActiveButton("继 续");
                Check(next != null, "老者神态后可继续: " + (i + 1));
                if (next == null) return;
                if (i == 0) Capture("fate-03-reply");
                next.onClick.Invoke();
            }
            Check(ActiveObjects("Fate dialogue") == 0, "问命完成后问题遮罩已清理");
            var accept = FindActiveButton("记下此卦");
            Check(accept != null, "问命结果确认按钮存在");
            if (accept == null) return;
            Capture("fate-04-result");
            accept.onClick.Invoke();
            Check(controller.CurrentPage == "character-creation", "记下此卦后回到创建页");
        }

        // 城镇页的行囊检查：B 键与按钮都要能开，开着时再按 B 应合上。
        static void VerifyTownBag()
        {
            Check(controller.CurrentPage == "town", "城镇页应已打开");
            controller.ShowInventory();
            Check(controller.BagCellCount == InventoryRules.SlotCount, "行囊面板应有 24 格");
            Check(controller.CurrentBag != null && controller.CurrentBag.Length == InventoryRules.SlotCount,
                "行囊数据应挂上当前存档");
            controller.CloseInventory();
            Check(controller.BagCellCount == 0, "合上后不应残留格子");
        }

        // 行囊验收：格子数、页签过滤、浮签、拖拽换位、使用消耗、存档落盘。
        static void VerifyInventory()
        {
            Check(controller.CurrentPage == "town", "行囊应开在城镇页之上");
            Check(controller.BagCellCount == InventoryRules.SlotCount, "行囊应有 24 格");
            var bag = controller.CurrentBag;
            if (bag == null) { Check(false, "行囊数据缺失"); return; }
            Check(bag.Length == InventoryRules.SlotCount && bag.All(stack => stack != null), "行囊数据应补齐 24 格");

            // 浮签：悬停有物的格子应弹出台签，移开收起。
            var occupied = Array.FindIndex(bag, stack => !stack.IsEmpty);
            if (occupied >= 0)
            {
                controller.ShowTip(occupied);
                Check(controller.BagTipSlot == occupied, "浮签应指向被悬停的格子");
                controller.HideTip();
                Check(controller.BagTipSlot == -1, "移开鼠标后浮签应收起");
            }

            // 拖拽换位：有物格 ⇄ 空格，两个方向都要对。
            var empty = Array.FindIndex(bag, stack => stack.IsEmpty);
            if (occupied >= 0 && empty >= 0)
            {
                var movedId = bag[occupied].id;
                var movedCount = bag[occupied].count;
                controller.BagDrop(occupied, empty);
                Check(bag[empty].id == movedId && bag[empty].count == movedCount && bag[occupied].IsEmpty,
                    "拖拽应把物品整体移到目标格");
                controller.BagDrop(empty, occupied);
                Check(bag[occupied].id == movedId && bag[empty].IsEmpty, "再拖回来应回到原格");
            }

            // 使用：只对入口的东西生效，入口的确实少一件。
            var food = Array.FindIndex(bag, stack => stack.id == "ganliang" || stack.id == "chuibing");
            if (food >= 0)
            {
                var id = bag[food].id;
                var before = InventoryRules.Count(bag, id);
                controller.BagUse(food);
                Check(InventoryRules.Count(controller.CurrentBag, id) == before - 1, "使用吃食应消耗一件");
            }
            // 任务物不能被用掉：开局行囊里没有任务物，就临时塞一块玉佩再验。
            var quest = Array.FindIndex(bag, stack => stack.id == "yupei");
            if (quest < 0)
            {
                var free = Array.FindIndex(bag, stack => stack.IsEmpty);
                if (free >= 0)
                {
                    bag[free] = new ItemStack("yupei", 1);
                    quest = free;
                }
            }
            if (quest >= 0)
            {
                controller.BagUse(quest);
                Check(InventoryRules.Count(controller.CurrentBag, "yupei") == 1, "任务物不能被用掉");
            }

            // 整理与落盘：整理后行囊整齐，读回来的存档也整齐且与面板一致。
            controller.BagSort();
            Check(InventoryRules.IsTidy(controller.CurrentBag), "整理后空格应排在后面");
            var reloaded = controller.Repository.Read(activeSlot).Data;
            Check(reloaded != null && InventoryRules.IsTidy(reloaded.bag), "整理结果应写回存档");
            Check(reloaded != null && reloaded.bag.Sum(stack => stack.count) ==
                controller.CurrentBag.Sum(stack => stack.count), "存档里的件数应与面板一致");
            Check(reloaded != null && reloaded.schemaVersion == SaveData.CurrentSchemaVersion, "存档应为当前 schema 版本");
            if (createdSlot > 0)
            {
                Check(reloaded.coins == ItemTable.StartingCoins, "新档的盘缠应原样写回");
                Check(reloaded.bag.All(stack => stack.IsEmpty || stack.count <= ItemTable.MaxStack),
                    "存档里的堆叠不应超过上限");
            }
        }

        static int activeSlot => createdSlot > 0 ? createdSlot : controller.Repository.Latest()?.Slot ?? 1;

        static void Check(bool condition, string message)
        {
            if (!condition) problems.Add("断言失败: " + message);
            else Debug.Log("[FrontEndShot] ok: " + message);
        }

        static void Finish()
        {
            SessionState.SetBool(SessionKey, false);
            Application.logMessageReceived -= OnLog;
            EditorApplication.update -= Pump;
            Check(controller.CurrentPage == "credits", "当前页面应为制作信息");
            Check(PageNames.All(p => capturedShots.Contains(p)), "全部页面截图生成");
            if (createdSlot > 0)
            {
                controller.Repository.Delete(createdSlot);
                // 玩家的真实存档可能占据其他槽位，只断言测试档本身已不在。
                Check(controller.Repository.Latest()?.Slot != createdSlot, "验收用的临时档位应已清理");
            }
            if (target != null && camera != null) { camera.targetTexture = null; target.Release(); }
            Debug.Log("[FrontEndShot] finished, " + problems.Count + " problem(s)");
            // LogError 会经 OnLog 再入列，枚举中变更集合会抛索引异常——先快照再打印。
            foreach (var problem in problems.ToArray()) Debug.LogError(problem);
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += () => EditorApplication.Exit(problems.Count == 0 ? 0 : 1);
        }

        static void Fail(string message)
        {
            SessionState.SetBool(SessionKey, false);
            Debug.LogError(message);
            EditorApplication.update -= Pump;
            EditorApplication.Exit(1);
        }
    }
}
