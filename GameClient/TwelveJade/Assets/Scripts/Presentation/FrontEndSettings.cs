using System.Linq;
using TMPro;
using TwelveJade.Core;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace TwelveJade.Presentation
{
    public sealed partial class FrontEndController
    {
        static readonly Vector2Int[] Resolutions = { new(1280, 720), new(1600, 900), new(1920, 1080), new(2560, 1440) };

        [SerializeField] AmbienceKind ambienceKind = AmbienceKind.Wind;
        bool ambienceCleaned;

        RectTransform settingsReturnContent;
        CanvasGroup settingsReturnFade;
        GameObject settingsReturnFocus;
        string settingsReturnPage;
        float settingsReturnFadeStart;
        public void ShowSettings()
        {
            if(page!="settings")
            {
                CloseInventory();CloseChapterPanel();CloseModal();
                if(page=="town")CancelTravel();
                settingsReturnContent=content;settingsReturnPage=page;
                settingsReturnFade=contentFade;settingsReturnFadeStart=fadeStart;
                settingsReturnFocus=EventSystem.current!=null?EventSystem.current.currentSelectedGameObject:null;
                if(content!=null)content.gameObject.SetActive(false);
                content=null;
            }
            BeginPage("settings");
            content.gameObject.AddComponent<V2FocusNavigation>();
            var draftSession = new V2SettingsDraft(settings); var draft = draftSession.Value;
            SettingsSanctumArt.Backdrop(ui, content, screenCanvas);
            var paper = SettingsSanctumArt.Panel(ui, content, new Rect(144,112,1632,880));
            SettingsSanctumArt.Text(ui,paper,"Settings title","系 统 设 置 · 静 室",new Rect(326,24,980,64),40,TextAlignmentOptions.Center);
            SettingsSanctumArt.Button(ui,content,"Settings back","收起",1690,28,180,62,CloseSettings,()=>draft.reduceMotion,PlayClick);
            SettingsSanctumArt.Text(ui,paper,"Settings audio heading","听 山",new Rect(92,116,630,60),36,TextAlignmentOptions.Center);
            SettingsSanctumArt.Text(ui,paper,"Settings audio subheading","琴音入室 · 风过松间",new Rect(92,181,630,40),22,TextAlignmentOptions.Center,true);
            SettingsSanctumArt.Text(ui,paper,"Settings display heading","观 照",new Rect(884,116,650,60),36,TextAlignmentOptions.Center);
            SettingsSanctumArt.Text(ui,paper,"Settings display subheading","一窗天地 · 动静由心",new Rect(884,181,650,40),22,TextAlignmentOptions.Center,true);
            var master = SettingsSanctumArt.Slider(ui, paper, "Master volume", "总音量", draft.masterVolume, 236,
                v => draft.masterVolume = v);
            SettingsSanctumArt.Slider(ui, paper, "Music volume", "背景音乐", draft.musicVolume, 346,
                v => draft.musicVolume = v);
            SettingsSanctumArt.Slider(ui, paper, "Effects volume", "交互音效", draft.effectsVolume, 456,
                v => draft.effectsVolume = v);
            SettingsSanctumArt.Text(ui, paper, "Settings display mode label", "显示模式", new Rect(864,255,182,46), 26);
            var mode = SettingsSanctumArt.Button(ui, paper, "Settings display mode", draft.fullscreen ? "无边框全屏" : "窗口", 1056, 239, 476, 66, null, () => draft.reduceMotion, PlayClick);
            var modeKnob = SettingsKnob(mode, draft.fullscreen);
            mode.onClick.AddListener(() =>
            {
                draft.fullscreen = !draft.fullscreen;
                mode.GetComponentInChildren<TMP_Text>().text = draft.fullscreen ? "无边框全屏" : "窗口";
                PlaceSettingsKnob(modeKnob, draft.fullscreen);
            });
            if (MobileControls.Enabled)
            {
                mode.interactable = false;
                mode.GetComponentInChildren<TMP_Text>().text = "横屏全屏";
            }
            SettingsSanctumArt.Text(ui, paper, "Settings resolution label", MobileControls.Enabled ? "画面尺寸" : "窗口分辨率", new Rect(864,365,186,46), 26);
            var available = Resolutions.Where(r => r.x <= Display.main.systemWidth && r.y <= Display.main.systemHeight).ToArray();
            if (available.Length == 0) available = new[] { Resolutions[0] };
            var resolution = SettingsSanctumArt.Button(ui, paper, "Settings resolution", draft.width + " × " + draft.height, 1056, 349, 476, 66, null, () => draft.reduceMotion, PlayClick);
            resolution.onClick.AddListener(() =>
            {
                var current = System.Array.FindIndex(available, r => r.x == draft.width && r.y == draft.height);
                var next = available[(current + 1) % available.Length]; draft.width = next.x; draft.height = next.y;
                resolution.GetComponentInChildren<TMP_Text>().text = next.x + " × " + next.y;
            });
            SettingsKnob(resolution, true);
            if (MobileControls.Enabled)
            {
                resolution.interactable = false;
                resolution.GetComponentInChildren<TMP_Text>().text = "随设备调整";
            }
            SettingsSanctumArt.Text(ui, paper, "Settings motion label", "界面过渡", new Rect(864,475,186,46), 26);
            var motion = SettingsSanctumArt.Button(ui, paper, "Settings reduced motion", draft.reduceMotion ? "即时切换" : "柔和渐入", 1056, 459, 476, 66, null, () => draft.reduceMotion, PlayClick);
            var motionKnob = SettingsKnob(motion, draft.reduceMotion);
            motion.onClick.AddListener(() =>
            {
                draft.reduceMotion = !draft.reduceMotion;
                motion.GetComponentInChildren<TMP_Text>().text = draft.reduceMotion ? "即时切换" : "柔和渐入";
                PlaceSettingsKnob(motionKnob, draft.reduceMotion);
            });
            SettingsSanctumArt.Text(ui, paper, "Settings draft notice", MobileControls.Enabled ? "保存后生效 · 轻触右上角收起。\n标题、青石镇与山野配乐随场景切换。" : "保存后生效 · Esc 收起设置。\n标题、青石镇与山野配乐随场景切换。", new Rect(92,568,660,86), 23);
            SettingsSanctumArt.Text(ui, paper, "Settings display notice", MobileControls.Enabled ? "手机采用横屏全屏，画面尺寸随设备调整。" : "全屏使用显示器原生分辨率；窗口尺寸循环切换。", new Rect(866,568,666,76), 22);
            SettingsSanctumArt.Button(ui, paper, "Settings defaults", "恢复默认并保存", 874, 656, 320, 64,
                () => Confirm("恢复默认设置？", "音量和画面设置将恢复初始值。角色档位不受影响。", "恢复并保存", () =>
                {
                    var defaults = new UserSettings(); repository.SaveSettings(defaults); settings = V2SettingsDraft.Copy(defaults);
                    ApplySettings(settings, true); ShowSettings(); Notify("已恢复默认设置。");
                }), () => draft.reduceMotion, PlayClick);
            SettingsSanctumArt.Button(ui, paper, "Settings save", "保存设置", 1220, 656, 312, 64, () => Guard(() =>
            {
                var committed = draftSession.Snapshot(); repository.SaveSettings(committed); settings = V2SettingsDraft.Copy(committed);
                ApplySettings(settings, true); Notify("设置已保存。");
            }), () => draft.reduceMotion, PlayClick);
            SettingsSanctumArt.Text(ui, paper, "Settings keyboard help", MobileControls.Enabled ? "拖动音量珠 · 轻触选项进行调整" : "方向键调珠 · Tab / 方向键择项 · 回车确认", new Rect(92,674,660,38), 20);
            if(activeSave!=null)
                SettingsSanctumArt.Button(ui,paper,"Settings return to menu","返回主卷",92,770,320,64,()=>
                    Confirm("暂歇此行", "封存此刻行迹，返回主卷？", "封存并返回", ()=>
                    {repository.Write(activeSave);ShowMenu();}),()=>draft.reduceMotion,PlayClick);
            SettingsSanctumArt.Button(ui,paper,"Settings exit game","归卷 · 退出游戏",1180,770,352,64,
                ConfirmExit,()=>draft.reduceMotion,PlayClick);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(master.gameObject);
        }
        void CloseSettings()
        {
            if(page!="settings")return;
            CloseModal();
            if(settingsReturnContent==null){ShowMenu();return;}
            content.gameObject.SetActive(false);Destroy(content.gameObject);
            content=settingsReturnContent;page=settingsReturnPage;contentFade=settingsReturnFade;fadeStart=settingsReturnFadeStart;
            var focus=settingsReturnFocus;
            settingsReturnContent=null;settingsReturnFade=null;settingsReturnPage=null;settingsReturnFocus=null;
            content.gameObject.SetActive(true);memoBackdrop?.SetPage(page);
            dialogueConsumeThroughFrame=System.Math.Max(dialogueConsumeThroughFrame,Time.frameCount+1);
            if(focus!=null&&focus.activeInHierarchy&&EventSystem.current!=null)EventSystem.current.SetSelectedGameObject(focus);
            else FocusFirst();
        }
        void DiscardSettingsReturn()
        {
            if(settingsReturnPage=="town")StopNpcPatrols(true);
            if(settingsReturnContent!=null){settingsReturnContent.gameObject.SetActive(false);Destroy(settingsReturnContent.gameObject);}
            settingsReturnContent=null;settingsReturnFade=null;settingsReturnFocus=null;settingsReturnPage=null;
        }
        RectTransform SettingsKnob(Button button, bool on)
        {
            var label = button.GetComponentInChildren<TMP_Text>();
            label.rectTransform.sizeDelta = new Vector2(354, 58); label.rectTransform.anchoredPosition = new Vector2(40, -4);
            var knob = XuanSettingsUi.Art(ui,button.transform,button.name+" jade ruyi",new Rect(420,17,30,30),XuanStoneGraphic.Form.Ruyi).rectTransform;
            PlaceSettingsKnob(knob, on); return knob;
        }
        static void PlaceSettingsKnob(RectTransform knob, bool on)
        {
            knob.anchoredPosition = new Vector2(on ? 428 : 407, -18);
            knob.GetComponent<XuanStoneGraphic>().color = on ? Color.white : new Color(.67f,.72f,.70f,.7f);
        }

        void ApplySettings(UserSettings value, bool applyDisplay)
        {
            if (value == null) return;
            value.Normalize();
            AudioListener.volume = value.masterVolume;
            ApplyMusicVolume(value.musicVolume);
            if (effects != null) effects.volume = value.effectsVolume * .4f;
            DisableAmbience();
            if (!applyDisplay || MobileControls.Enabled) return;
#if !UNITY_EDITOR
            Screen.SetResolution(value.fullscreen ? Display.main.systemWidth : value.width,
                value.fullscreen ? Display.main.systemHeight : value.height,
                value.fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
#endif
        }

        public void ShowCredits()
        {
            BeginPage("credits");
            var art=Resources.Load<Texture2D>("Art/UI/Nov11/credits-silk-landscape");
            if(art!=null)ui.Art(content,art,0,0,1920,1080).raycastTarget=false;
            var ink=UiKit.Hex("35473E");var muted=UiKit.Hex("566258");var gold=UiKit.Hex("8F713D");
            ui.Label(content,"共 创  ·  玉 牒",205,184,950,44,25,gold,TextAlignmentOptions.Left,false);
            ui.Label(content,"写给同行者",200,252,1020,84,55,ink,TextAlignmentOptions.Left,false);
            ui.Label(content,"一卷未尽的山河，一个慢慢生长的世界。",205,356,1000,50,27,muted,TextAlignmentOptions.Left,false);
            ui.Label(content,"十二玉楼长生经",205,461,940,60,36,ink,TextAlignmentOptions.Left,false);
            ui.Label(content,"创作与方向  ·  fei-yu-001\n开发协作  ·  AI 辅助实现与迭代",208,548,960,95,26,muted,TextAlignmentOptions.Left,false);
            ui.Label(content,"字体  ·  Noto Serif CJK、志莽行书\n           SIL Open Font License 1.1\n美术  ·  本项目素材来源详见仓库素材记录\n配乐  ·  斐哥提供的三首原创曲目\n音效  ·  程序合成的脚步与交互音效",208,678,1030,154,23,muted,TextAlignmentOptions.Left,false);
            ui.Label(content,"感谢你愿意走进这个尚在生长的世界。",208,863,1060,46,25,gold,TextAlignmentOptions.Left,false);
            MenuMemoArt.Command(ui,content,"Credits return","归卷",new Rect(1530,888,250,78),ShowMenu,()=>settings.reduceMotion,PlayClick,26,false);
            FocusFirst();
        }

        void SetupAudio()
        {
            DisableAmbience();
            SetupMusic();
            effects = gameObject.AddComponent<AudioSource>(); effects.playOnAwake = false;
            SetupUiTones();
            SetupFootsteps();
            // The supplied recordings provide the environment bed; synthetic bird/insect ambience is permanently disabled.
        }

        void DisableAmbience()
        {
            ambienceKind = AmbienceKind.Wind;
            foreach (var source in GetComponents<AudioSource>())
            {
                if (source == music || source == effects) continue;
                if (source.clip != null && source.clip.name.StartsWith("Ambience", System.StringComparison.Ordinal))
                {
                    source.Stop();
                    source.clip = null;
                    source.volume = 0f;
                }
            }
            ambienceCleaned = true;
        }

        AudioSource FindAmbienceSource()
        {
            foreach (var source in GetComponents<AudioSource>())
            {
                if (source == music || source == effects) continue;
                if (source.clip != null && source.clip.name.StartsWith("Ambience", System.StringComparison.Ordinal))
                    return source;
            }
            return null;
        }

        AmbienceKind AmbienceKindForNow() => AmbienceKind.Wind;

        void SetupAmbience() => DisableAmbience();

        public void SetAmbienceKind(AmbienceKind kind) => DisableAmbience();

        public void TickAmbience()
        {
            ambienceKind = AmbienceKind.Wind;
            if (!ambienceCleaned) DisableAmbience();
        }

        // 夹具口径：真实枚举自身 AudioSource 上是否仍残留 Ambience* 片段或在播放（0 = 没接上）
        public int AmbienceClipLengthForCheck
        {
            get
            {
                var source = FindAmbienceSource();
                return source != null && source.clip != null ? source.clip.samples : 0;
            }
        }
        public AmbienceKind AmbienceKindForCheck => ambienceKind;
        public bool AmbiencePlayingForCheck
        {
            get
            {
                var source = FindAmbienceSource();
                return source != null && source.isPlaying;
            }
        }

        // C-17 之后交互音统一走 `UiToneSynth`（可被离线断言、且钉死了 <=120ms 的上限）。
        // 本函数**保留不删**：别的 partial 文件可能在途引用它，删掉会让人家编译断（AA-1 第 11 条的教训）。
        static AudioClip Tone(string name, int rate, float duration, float frequency, float gain)
        {
            var samples = new float[(int)(rate * duration)];
            for (var i = 0; i < samples.Length; i++)
            {
                var t = i / (float)rate;
                samples[i] = Mathf.Sin(2 * Mathf.PI * frequency * t) * Mathf.Min(t * 400, 1) * Mathf.Exp(-t * 35) * gain;
            }
            var clip = AudioClip.Create(name, samples.Length, 1, rate, false); clip.SetData(samples, 0); return clip;
        }

        // ---- C-17 / C-27 / TC-31~TC-33：交互音四类 ------------------------------------
        // 与 `UiToneKind` 同序：[0]=Click [1]=Page [2]=Acquire [3]=Fail。
        AudioClip[] uiTones;

        void SetupUiTones()
        {
            var names = new[] { "Click", "Page", "Acquire", "Fail" };
            var kinds = new[] { UiToneKind.Click, UiToneKind.Page, UiToneKind.Acquire, UiToneKind.Fail };
            uiTones = new AudioClip[kinds.Length];
            for (var i = 0; i < kinds.Length; i++)
            {
                var spec = UiToneSpec.For(kinds[i]);
                var samples = UiToneSynth.Render(spec);
                var clip = AudioClip.Create("UI tone · " + names[i], samples.Length, 1, spec.Rate, false);
                clip.SetData(samples, 0);
                uiTones[i] = clip;
            }
            clickTone = uiTones[0];   // `UiKit` 的点击回调仍走 `PlayClick`，同一条音、不再另造一份
        }

        /// <summary>C-17/C-27/TC-31~TC-33：播一条交互音。
        /// **接线的落点不在本文件**（翻页/获得/失败的事件源在对话页/行囊页，属 B 线主责），
        /// 这里只提供入口 —— 我把入口做完，接线点清单写进 `Tools/logs/c-17-report.md` 交裁决。</summary>
        public void PlayUiTone(UiToneKind kind)
        {
            if (effects == null || uiTones == null) return;
            var idx = (int)kind;
            if (idx < 0 || idx >= uiTones.Length) return;
            if (uiTones[idx] != null) effects.PlayOneShot(uiTones[idx]);
        }

        // 夹具口径：四类交互音有没有都合成出来（0 = 没接上）
        public int UiToneCountForCheck => uiTones == null ? 0 : uiTones.Length;
        public int UiToneMaxSamplesForCheck
        {
            get
            {
                if (uiTones == null) return 0;
                var max = 0;
                foreach (var c in uiTones) if (c != null && c.samples > max) max = c.samples;
                return max;
            }
        }

        void PlayClick() { if (effects != null && clickTone != null) effects.PlayOneShot(clickTone); }
    }
}
