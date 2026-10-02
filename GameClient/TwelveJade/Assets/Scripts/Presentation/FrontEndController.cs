using System;
using System.IO;
using System.Linq;
using TMPro;
using TwelveJade.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    public sealed partial class FrontEndController : MonoBehaviour
    {
        UiKit ui;
        RectTransform canvas, content, modal, toast;
        GameObject canvasObject;
        CanvasGroup contentFade;
        SaveRepository repository;
        UserSettings settings;
        CharacterPreset[] presets;
        AudioSource music, effects;
        AudioClip clickTone;
        TMP_FontAsset runtimeFont, brushFont;
        SaveData activeSave;
        string page = "menu";
        float toastUntil, fadeStart;
        const string ProductVersion = "0.0.1 · 初见青石";

        public string CurrentPage => page;
        // 验收与章系统都需要读当前档；写入口仍是各 Core 账本。
        public SaveData ActiveSave => activeSave;
        public SaveRepository Repository => repository;

        void Awake()
        {
            Application.targetFrameRate = 60;
            var cameraObject = new GameObject("Menu Camera", typeof(Camera), typeof(AudioListener));
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = UiKit.Dark;
            camera.orthographic = true; cameraObject.tag = "MainCamera";
            var font = Resources.Load<Font>("Fonts/NotoSerifCJKsc-Regular");
            if (font == null) throw new InvalidOperationException("Missing bundled Noto Serif CJK font.");
            runtimeFont = TMP_FontAsset.CreateFontAsset(font, 48, 5, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                2048, 2048, AtlasPopulationMode.Dynamic, true);
            runtimeFont.name = "TwelveJade Noto Serif Dynamic";
            var brushSource = Resources.Load<Font>("Fonts/ZhiMangXing-Regular");
            if (brushSource == null) throw new InvalidOperationException("Missing bundled Zhi Mang Xing brush font.");
            brushFont = TMP_FontAsset.CreateFontAsset(brushSource, 48, 5, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                2048, 2048, AtlasPopulationMode.Dynamic, true);
            brushFont.name = "TwelveJade Zhi Mang Xing Dynamic";
            ui = new UiKit(runtimeFont, brushFont, PlayClick);
            var canvas_ = new GameObject("Front End Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject = canvas_;
            canvas = canvas_.GetComponent<RectTransform>();
            canvas_.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            if (EventSystem.current == null)
                new GameObject("UI Input", typeof(EventSystem), typeof(InputSystemUIInputModule));

            // Keep layout centered when window aspect is not 16:9. The edges remain an ink border.
            var design = ui.Rect(canvas, "Design canvas", 0, 0, 1920, 1080);
            design.anchorMin = design.anchorMax = new Vector2(.5f, .5f);
            design.pivot = new Vector2(.5f, .5f); design.anchoredPosition = Vector2.zero;
            canvas = design;
            BuildBackdrop();
            NpcArrived += id => TalkTo(id);
            EncounterArrived += id => ShowCombat(id);
            presets = Resources.LoadAll<CharacterPreset>("Characters").OrderBy(x => x.id).ToArray();
            if (presets.Length != 3) throw new InvalidOperationException("Three character presets are required. Run Twelve Jade/Prepare project.");
            // 批处理验收会设 TWELVEJADE_SAVEDIR 指到临时目录：验收不该写进玩家真实的存档。
            var saveRoot = Environment.GetEnvironmentVariable("TWELVEJADE_SAVEDIR");
            repository = new SaveRepository(
                string.IsNullOrEmpty(saveRoot) ? Path.Combine(Application.persistentDataPath, "Saves") : saveRoot,
                new UnityJsonCodec());
            // 存档校验要用正式物品表卡单品堆叠上限，注入晚了读档就会放过脏数据。
            repository.Items = Table;
            settings = repository.LoadSettings(out var settingsState);
            SetupAudio(); ApplySettings(settings, true); ShowMenu();
            if (settingsState == SettingsLoadState.Recovered) Notify("设置已从备份恢复。");
            else if (settingsState == SettingsLoadState.Reset) Notify("设置文件无法读取，已使用默认设置。");
            else if (settingsState == SettingsLoadState.FutureVersion)
                Notify("设置来自更新版本。当前使用默认设置，原文件已保留。");
        }

        static UnityEngine.InputSystem.Controls.KeyControl[] digitKeys;

        void Update()
        {
            if (contentFade != null)
                contentFade.alpha = settings.reduceMotion ? 1 : Mathf.Clamp01((Time.unscaledTime - fadeStart) / .22f);
            if (toast != null && Time.unscaledTime > toastUntil) { Destroy(toast.gameObject); toast = null; }
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (bagOverlay != null) CloseInventory();
                else if (modal != null) CloseModal();
                else if (page == "trade") ShowTown();
                else if (page == "combat") ShowTown();
                else if (page == "menu") ConfirmExit();
                else ShowMenu();
            }
            if (Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame &&
                page == "town" && modal == null)
            {
                if (bagOverlay != null) CloseInventory();
                else ShowInventory();
            }
            if (page == "preview" && Keyboard.current != null)
            {
                if (digitKeys == null)
                    digitKeys = new[] { Keyboard.current.digit1Key, Keyboard.current.digit2Key, Keyboard.current.digit3Key,
                        Keyboard.current.digit4Key, Keyboard.current.digit5Key, Keyboard.current.digit6Key };
                for (var i = 0; i < digitKeys.Length; i++)
                    if (digitKeys[i].wasPressedThisFrame) SetMotion(i + 1);
            }
            UpdateWorld();
            UpdateTrade();
            CombatClockAdvance(Time.unscaledDeltaTime);
        }

        void BuildBackdrop()
        {
            var background = Resources.Load<Texture2D>("Art/menu-painting");
            if (background == null) background = Resources.Load<Texture2D>("Art/menu-background");
            ui.Panel(canvas, "Ink base", 0, 0, 1920, 1080, UiKit.Ink);
            RawImage backdropImage = null;
            if (background != null)
            {
                var image = ui.Art(canvas, background, 0, 0, 1920, 1080);
                image.rectTransform.anchoredPosition = Vector2.zero;
                image.rectTransform.sizeDelta = new Vector2(1920, 1080);
                var imageRatio = (float)background.width / background.height;
                var screenRatio = 1920f / 1080;
                if (imageRatio < screenRatio)
                {
                    var height = imageRatio / screenRatio;
                    image.uvRect = new Rect(0, (1 - height) / 2, 1, height);
                }
                else { var width = screenRatio / imageRatio; image.uvRect = new Rect((1 - width) / 2, 0, width, 1); }
                backdropImage = image;
            }
            ui.Panel(canvas, "Atmosphere", 0, 0, 1920, 1080, new Color(.025f, .09f, .075f, .30f));
            var ambience = canvas.gameObject.AddComponent<MenuAmbience>();
            ambience.Configure(backdropImage, () => settings.reduceMotion);
            // 左侧文字区纱罩：横向渐变代替竖直硬边。
            var scrim = ui.Rect(canvas, "Left scrim", 0, 0, 900, 1080).gameObject.AddComponent<RawImage>();
            scrim.texture = UiKit.LeftFadeTexture(); scrim.raycastTarget = false;
            scrim.color = new Color(.015f, .06f, .05f, .96f);
            ui.Label(canvas, "九 州 界  /  凡 尘 篇", 82, 25, 900, 30, 16, UiKit.Paper);
            ui.Label(canvas, "十二玉楼长生经", 80, 1020, 600, 30, 17, UiKit.Muted);
            ui.Label(canvas, ProductVersion, 1370, 1020, 475, 30, 17, UiKit.Muted, TextAlignmentOptions.TopRight);
        }

        void BeginPage(string name)
        {
            CloseModal();
            if (content != null) { content.gameObject.SetActive(false); Destroy(content.gameObject); }
            page = name;
            content = ui.Rect(canvas, "Page " + name, 0, 0, 1920, 1080);
            contentFade = content.gameObject.AddComponent<CanvasGroup>(); fadeStart = Time.unscaledTime;
            contentFade.alpha = settings.reduceMotion ? 1 : 0;
        }

        void PageHeading(string kicker, string title, string subtitle)
        {
            ui.Label(content, kicker, 100, 106, 1500, 32, 18, UiKit.Gold);
            ui.Label(content, title, 100, 155, 1250, 85, 56, UiKit.Paper, TextAlignmentOptions.TopLeft, true);
            ui.Label(content, subtitle, 102, 251, 1580, 54, 23, UiKit.Muted);
            ui.Button(content, "返回", 1620, 145, 200, 60, ShowMenu);
        }

        public void ShowMenu()
        {
            BeginPage("menu"); activeSave = null;
            // 标题与印章已烘焙在主视觉画里，UI 不再叠印文字，避免两种字体打架。
            ui.Label(content, "山河无定数，凡尘亦长生。", 102, 461, 500, 50, 24, UiKit.Muted);
            ui.MenuItem(content, "启  程", "开始一段新的人生", 100, 545, 400, 84, () => ShowSlots(true));
            ui.MenuItem(content, "续  缘", "回到上次的旅程", 100, 637, 400, 84, () => LoadSlot(repository.Latest()),
                repository.Latest() != null);
            ui.MenuItem(content, "行  迹", "查看与整理三卷存档", 100, 729, 400, 84, () => ShowSlots(false));
            ui.MenuItem(content, "设  置", "声色与观感", 100, 823, 196, 76, ShowSettings);
            ui.MenuItem(content, "制作信息", "谁在写这个故事", 310, 823, 196, 76, ShowCredits);
            ui.MenuItem(content, "离  去", "暂别此间", 100, 907, 400, 76, ConfirmExit);
            ui.Label(content, "卷 一", 1700, 116, 92, 54, 30, UiKit.Paper, TextAlignmentOptions.Top, true);
            ui.Label(content, "青\n石\n残\n梦", 1690, 208, 104, 440, 60, UiKit.Paper, TextAlignmentOptions.Top, true);
            ui.Label(content, "一缕炊烟，一条归路。\n远山之外，旧世正在醒来。", 1040, 817, 720, 110, 28, UiKit.Paper, TextAlignmentOptions.TopRight);
            FocusFirst();
        }

        void FocusFirst()
        {
            var first = content.GetComponentsInChildren<Selectable>().FirstOrDefault(x => x.interactable);
            if (first != null) EventSystem.current.SetSelectedGameObject(first.gameObject);
        }

        void Guard(Action action)
        {
            try { action(); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException)
            {
                Debug.LogWarning("Front end operation failed: " + ex.GetType().Name + ": " + ex.Message);
                Notify(ex is IOException || ex is UnauthorizedAccessException ? "无法写入存档，请检查磁盘空间和文件权限。原有记录已保留。" : ex.Message);
            }
        }

        void Notify(string message)
        {
            if (toast != null) Destroy(toast.gameObject);
            toast = ui.Panel(canvas, "Notification", 540, 910, 1290, 65, UiKit.Dark).rectTransform;
            ui.Label(toast, message, 22, 10, 1246, 50, 22, UiKit.Paper, TextAlignmentOptions.MidlineLeft);
            toastUntil = Time.unscaledTime + 6;
        }

        void CloseModal()
        {
            if (modal == null) return;
            modal.gameObject.SetActive(false); Destroy(modal.gameObject); modal = null;
            if (content != null) { content.GetComponent<CanvasGroup>().interactable = true; FocusFirst(); }
        }

        void Confirm(string title, string message, string confirm, Action action)
        {
            CloseModal();
            content.GetComponent<CanvasGroup>().interactable = false;
            modal = ui.Panel(canvas, "Confirmation", 0, 0, 1920, 1080, new Color(0, .035f, .03f, .82f), true).rectTransform;
            var box = ui.Panel(modal, "Dialog", 525, 330, 870, 400, UiKit.Dark, true);
            ui.Label(box.transform, title, 48, 40, 780, 65, 40, UiKit.Paper);
            ui.Label(box.transform, message, 48, 122, 770, 118, 25, UiKit.Muted);
            var cancel = ui.Button(box.transform, "取消", 48, 292, 350, 62, CloseModal);
            ui.Button(box.transform, confirm, 445, 292, 375, 62, () => { CloseModal(); Guard(action); }, true);
            EventSystem.current.SetSelectedGameObject(cancel.gameObject);
        }

        void ConfirmExit() => Confirm("暂别此间", "当前档位已自动保存。下次归来，可从主菜单继续。", "退出游戏", () =>
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        });

        void OnDestroy()
        {
            if (runtimeFont != null) Destroy(runtimeFont);
            if (brushFont != null) Destroy(brushFont);
            if (clickTone != null) Destroy(clickTone);
            if (music != null && music.clip != null) Destroy(music.clip);
        }
    }
}
