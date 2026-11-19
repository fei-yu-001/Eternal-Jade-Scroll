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
using UnityEngine.Video;

namespace TwelveJade.Presentation
{
    public sealed partial class FrontEndController : MonoBehaviour
    {
        UiKit ui;
        RectTransform canvas, screenCanvas, content, modal, toast;
        RawImage menuStill;
        GameObject canvasObject;
        CanvasGroup contentFade;
        MenuVideoBackdrop menuVideo;
        MemoBackdrop memoBackdrop;
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
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            camera.orthographic = true; cameraObject.tag = "MainCamera";
            var font = Resources.Load<Font>("Fonts/NotoSerifCJKsc-Regular");
            if (font == null) throw new InvalidOperationException("Missing bundled Noto Serif CJK font.");
            runtimeFont = TMP_FontAsset.CreateFontAsset(font, 64, 8, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                2048, 2048, AtlasPopulationMode.Dynamic, true);
            runtimeFont.name = "TwelveJade Noto Serif Dynamic";
            var brushSource = Resources.Load<Font>("Fonts/ZhiMangXing-Regular");
            if (brushSource == null) throw new InvalidOperationException("Missing bundled Zhi Mang Xing brush font.");
            brushFont = TMP_FontAsset.CreateFontAsset(brushSource, 96, 10, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                2048, 2048, AtlasPopulationMode.Dynamic, true);
            brushFont.name = "TwelveJade Zhi Mang Xing Dynamic 96 SDF";
            brushFont.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset> { runtimeFont };
            const string menuGlyphs = "叩弦长生乾坤玉牒掩卷回卷承卷入世立传开卷归尘留卷暂留未定名存档第一二三卷甲乙丙…";
            runtimeFont.TryAddCharacters(menuGlyphs, out var missingBody);
            if (!string.IsNullOrEmpty(missingBody)) Debug.LogError("Bundled serif font misses menu glyphs: " + missingBody);
            brushFont.TryAddCharacters(menuGlyphs, out var missingBrush);
            if (!string.IsNullOrEmpty(missingBrush) && !runtimeFont.TryAddCharacters(missingBrush, out var missingFallback))
                Debug.LogError("Bundled brush and serif fonts miss menu glyphs: " + missingFallback);
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

            // World pages retain their 1920x1080 coordinates; menu imagery fills the actual viewport.
            screenCanvas = canvas;
            BuildBackdrop();
            var design = ui.Rect(canvas, "Design canvas", 0, 0, 1920, 1080);
            design.anchorMin = design.anchorMax = new Vector2(.5f, .5f);
            design.pivot = new Vector2(.5f, .5f); design.anchoredPosition = Vector2.zero;
            canvas = design;
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

        void Update()
        {
            if (music != null) music.mute = menuVideo != null && menuVideo.SoundtrackPlaying;
            UiKit.Cover(menuStill);
            RefreshSlotLayout();
            if (contentFade != null)
                contentFade.alpha = settings.reduceMotion ? 1 : Mathf.Clamp01((Time.unscaledTime - fadeStart) / .22f);
            if (toast != null && Time.unscaledTime > toastUntil) { Destroy(toast.gameObject); toast = null; }
            var dialogueInput = HandleTownDialogueInput();
            if (!dialogueInput && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (bagOverlay != null) CloseInventory();
                else if (modal != null)
                {
                    var inner=modal.GetComponent<MeridianSanctumView>();
                    var arts=modal.GetComponent<ArtsCodexView>();
                    if((inner==null||!inner.TryCloseInner())&&(arts==null||!arts.TryCloseStudy()))CloseModal();
                }
                else if (page == "trade") ShowTown();
                else if (page == "combat") ShowTown();
                else if (page == "town" && WorldCombatWheelOpen) CloseWorldCombatWheel();
                else if (page == "settings") CloseSettings();
                else if (page == "menu" || page == "town") ShowSettings();
                else ShowMenu();
            }
            if (!dialogueInput && Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame &&
                page == "town" && modal == null)
            {
                if (bagOverlay != null) CloseInventory();
                else ShowInventory();
            }
            // A settings visit suspends the existing town; do not tear down its NPC collision centres.
            if(page!="settings"||settingsReturnPage!="town")UpdateWorld();
            UpdateTrade();
            CombatClockAdvance(Time.unscaledDeltaTime);
        }

        void BuildBackdrop()
        {
            var layer = ui.Stretch(screenCanvas, "Menu backdrop");
            var black = ui.Stretch(layer, "Ink base").gameObject.AddComponent<Image>();
            black.color = Color.black; black.raycastTarget = false;
            var background = Resources.Load<Texture2D>("Art/Background/menu-painting");
            if (background == null) background = Resources.Load<Texture2D>("Art/Background/menu-background");
            var menuLayer = ui.Stretch(layer, "Menu artwork layers");
            var menuGroup = menuLayer.gameObject.AddComponent<CanvasGroup>();
            menuGroup.interactable = false; menuGroup.blocksRaycasts = false;
            menuStill = ui.Stretch(menuLayer, "Artwork").gameObject.AddComponent<RawImage>();
            menuStill.texture = background; menuStill.raycastTarget = false;
            menuStill.enabled = background != null;
            UiKit.Cover(menuStill);
            var videoImage = ui.Stretch(menuLayer, "Menu video").gameObject.AddComponent<RawImage>();
            menuVideo = videoImage.gameObject.AddComponent<MenuVideoBackdrop>();
            memoBackdrop = layer.gameObject.AddComponent<MemoBackdrop>();
            memoBackdrop.Configure(ui, layer, menuGroup, videoImage, menuVideo,
                () => settings == null || settings.reduceMotion);
            menuVideo.Configure(videoImage, Resources.Load<VideoClip>(MenuVideoBackdrop.ResourcePath),
                () => settings != null && page == "menu" && memoBackdrop.WantsMenuVideo,
                () => settings == null || settings.reduceMotion, () => settings?.musicVolume ?? .7f);
        }

        void BeginPage(string name)
        {
            ReleaseWorldCombat();
            CloseModal();
            if(name!="settings") DiscardSettingsReturn();
            if (content != null) { content.gameObject.SetActive(false); Destroy(content.gameObject); }
            page = name;
            memoBackdrop?.SetPage(name);
            content = name == "menu" || name == "slots"
                ? ui.Stretch(screenCanvas, "Page " + name)
                : ui.Rect(canvas, "Page " + name, 0, 0, 1920, 1080);
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
            var enter = MenuMemoArt.Command(ui, content, "Menu primary", "叩弦长生", new Rect(0, 0, 420, 132),
                () => ShowSlots(false), () => settings.reduceMotion, PlayJadeChime, 40);
            var mainRect = (RectTransform)enter.transform;
            mainRect.anchorMin = mainRect.anchorMax = new Vector2(.5f, .22f);
            mainRect.pivot = new Vector2(.5f, .5f); mainRect.anchoredPosition = Vector2.zero;
            var tools = ui.Rect(content, "Menu utilities", 0, 0, 220, 92);
            tools.anchorMin = tools.anchorMax = Vector2.one;
            tools.pivot = Vector2.one; tools.anchoredPosition = new Vector2(-40, -28);
            MenuMemoArt.Utility(ui, tools, "Menu settings", "乾坤", 0, MenuOrnament.Motif.Compass, ShowSettings, () => settings.reduceMotion, PlayClick);
            MenuMemoArt.Utility(ui, tools, "Menu credits", "玉牒", 116, MenuOrnament.Motif.Book, ShowCredits, () => settings.reduceMotion, PlayClick);
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

        // 提示条几何的单一真源（Q2-4）：面板与它上面的文字历来是**两处字面量、一对数值**——
        // T075 把面板从 y=910 挪到 y=1014 时，Label 的宽 1246 若不跟着改就溢出到条外。
        // 现在文字尺寸由面板推导，只留 padding 一个旋钮；几何与合入后的值逐字相同
        // （1130 − 22×2 = 1086、58 − 8×2 = 42），所以这一笔是纯重构，画面不动。
        const float ToastX = 700f, ToastY = 1014f, ToastW = 1130f, ToastH = 58f;
        const float ToastPadX = 22f, ToastPadY = 8f, ToastFont = 22f, ToastSeconds = 6f;
        static float ToastTextWidth => ToastW - ToastPadX * 2f;
        static float ToastTextHeight => ToastH - ToastPadY * 2f;

        void Notify(string message)
        {
            if (toast != null) Destroy(toast.gameObject);
            // T075（架构师 2026-10-05 授权改这一行几何）：原来贴在 y=910..975，正好压住战斗页
            // 动作行（轻/重/闪在 950..1008）上沿 25px——玩家按"服 药"或"磨刃"被拒时，
            // 弹出理由的那条黑幕恰好盖住按钮本身（真图 k-combat-07 修复前为证）。
            // 顶部 y=60 一带不去：行囊页的 手艺/整理/丢弃/合上行囊 在 y=42..100，交易页铜钱行在 y=54..94，
            // 挪上去只是把遮挡从一处搬到另一处，而且搬到了更要紧的按钮上。
            // 落到底带最后一条：x 从 700 起是给左下角常驻落款"十二玉楼长生经"(80..680, y=1020) 让位，
            // y=1014 起是给战斗页动作行（到 1008 结束）让位；Panel 的 block 默认 false，不抢点击。
            toast = MenuMemoArt.Notice(ui, canvas, message, new Rect(ToastX, ToastY, ToastW, ToastH), ToastFont);
            toastUntil = Time.unscaledTime + ToastSeconds;
        }

        void CloseModal()
        {
            CloseTownDialogue();
            if (modal == null) return;
            modal.gameObject.SetActive(false); Destroy(modal.gameObject); modal = null;
            if (content != null)
            {
                var group = content.GetComponent<CanvasGroup>();
                group.interactable = true; group.blocksRaycasts = true; FocusFirst();
            }
        }

        void Confirm(string title, string message, string confirm, Action action, string cancelCaption = "暂留")
        {
            CloseModal();
            var group = content.GetComponent<CanvasGroup>();
            group.interactable = false; group.blocksRaycasts = false;
            modal = ui.Stretch(screenCanvas, "Confirmation");
            var veil = modal.gameObject.AddComponent<Image>();
            veil.color = new Color(0, 0, 0, .42f); veil.raycastTarget = true;
            var box = ui.JadeFrame(modal, "Dialog", 0, 0, 870, 400, new Color(.94f, .9f, .8f, .96f), true);
            box.rectTransform.anchorMin = box.rectTransform.anchorMax = new Vector2(.5f, .5f);
            box.rectTransform.pivot = new Vector2(.5f, .5f); box.rectTransform.anchoredPosition = Vector2.zero;
            ui.Label(box.transform, title, 48, 40, 780, 65, 40, UiKit.Ink, TextAlignmentOptions.TopLeft, true);
            ui.Label(box.transform, message, 48, 122, 770, 118, 25, UiKit.Ink).richText = false;
            bool deleting = confirm == "归尘";
            var cancel = ui.JadeAction(box.transform, deleting ? "Delete cancel" : "Modal cancel",
                deleting ? "留卷" : cancelCaption, 48, 292, 350, 62, CloseModal, () => settings.reduceMotion);
            ui.JadeAction(box.transform, deleting ? "Delete confirm" : "Modal confirm", confirm,
                445, 292, 375, 62, () => { CloseModal(); Guard(action); }, () => settings.reduceMotion, true);
            EventSystem.current.SetSelectedGameObject(cancel.gameObject);
        }

        void ConfirmExit()
        {
            // Only the settings footer can request process exit. Escape always dismisses a layer.
            if(page!="settings")return;
            Confirm("归卷暂别", "道友当真要暂离红尘，归卷封存？", "是 · 归卷", () =>
            {
                if(activeSave!=null)repository.Write(activeSave);
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }, "否 · 留驻");
        }

        void OnDestroy()
        {
            CloseTownDialogue();
            StopNpcPatrols(true);
            memoBackdrop?.Release();
            menuVideo?.Release();
            if (runtimeFont != null) Destroy(runtimeFont);
            if (brushFont != null) Destroy(brushFont);
            if (clickTone != null) Destroy(clickTone);
            if (jadeChime != null) Destroy(jadeChime);
            if (music != null && music.clip != null) Destroy(music.clip);
        }
    }
}
