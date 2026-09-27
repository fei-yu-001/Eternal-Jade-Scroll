using System.Linq;
using TMPro;
using TwelveJade.Core;
using UnityEngine;

namespace TwelveJade.Presentation
{
    public sealed partial class FrontEndController
    {
        static readonly Vector2Int[] Resolutions = { new(1280, 720), new(1600, 900), new(1920, 1080), new(2560, 1440) };

        public void ShowSettings()
        {
            BeginPage("settings");
            PageHeading("声 色  /  随 心", "此间设置", "调一段山间声色，寻一种自在观感。点击保存后生效。");
            var draft = JsonUtility.FromJson<UserSettings>(JsonUtility.ToJson(settings));
            var panel = ui.Panel(content, "Settings panel", 100, 337, 1720, 592, new Color(.045f, .115f, .10f, .97f));
            ui.Label(panel.transform, "声音", 44, 27, 500, 40, 25, UiKit.Gold);
            ui.Slider(panel.transform, "总音量", draft.masterVolume, 44, 101, v => draft.masterVolume = v);
            ui.Slider(panel.transform, "环境音乐", draft.musicVolume, 44, 188, v => draft.musicVolume = v);
            ui.Slider(panel.transform, "交互音效", draft.effectsVolume, 44, 275, v => draft.effectsVolume = v);
            ui.Label(panel.transform, "画面", 940, 27, 500, 40, 25, UiKit.Gold);
            ui.Label(panel.transform, "显示模式", 940, 101, 250, 40, 26);
            var modeLabel = ui.Button(panel.transform, draft.fullscreen ? "无边框全屏" : "窗口", 1200, 87, 440, 61, () => { });
            modeLabel.onClick.AddListener(() =>
            {
                draft.fullscreen = !draft.fullscreen;
                modeLabel.GetComponentInChildren<TextMeshProUGUI>().text = draft.fullscreen ? "无边框全屏" : "窗口";
            });
            ui.Label(panel.transform, "窗口分辨率", 940, 190, 260, 40, 26);
            var available = Resolutions.Where(r => r.x <= Display.main.systemWidth && r.y <= Display.main.systemHeight).ToArray();
            if (available.Length == 0) available = new[] { Resolutions[0] };
            var resolution = ui.Button(panel.transform, draft.width + " × " + draft.height, 1200, 174, 440, 61, () => { });
            resolution.onClick.AddListener(() =>
            {
                var current = System.Array.FindIndex(available, r => r.x == draft.width && r.y == draft.height);
                var next = available[(current + 1) % available.Length]; draft.width = next.x; draft.height = next.y;
                resolution.GetComponentInChildren<TextMeshProUGUI>().text = next.x + " × " + next.y;
            });
            ui.Label(panel.transform, "界面过渡", 940, 279, 260, 40, 26);
            var motion = ui.Button(panel.transform, draft.reduceMotion ? "即时切换" : "柔和渐入", 1200, 261, 440, 61, () => { });
            motion.onClick.AddListener(() =>
            {
                draft.reduceMotion = !draft.reduceMotion;
                motion.GetComponentInChildren<TextMeshProUGUI>().text = draft.reduceMotion ? "即时切换" : "柔和渐入";
            });
            ui.Rule(panel.transform, "Rule", 44, 365, 1596, 2, UiKit.Jade);
            ui.Label(panel.transform, "全屏采用显示器原生分辨率。窗口尺寸可循环切换。\nEsc 返回主菜单；未保存的改动将放弃。", 44, 401, 1540, 75, 22, UiKit.Muted);
            ui.Button(panel.transform, "恢复默认并保存", 44, 502, 360, 60, () => Confirm("恢复默认设置？", "音量和画面设置将恢复初始值。角色档位不受影响。", "恢复并保存", () =>
            {
                var defaults = new UserSettings(); repository.SaveSettings(defaults); settings = defaults;
                ApplySettings(settings, true); ShowSettings(); Notify("已恢复默认设置。");
            }));
            ui.Button(panel.transform, "保存设置", 1280, 502, 360, 60, () => Guard(() =>
            { repository.SaveSettings(draft); settings = draft; ApplySettings(settings, true); Notify("设置已保存。"); }), true);
            FocusFirst();
        }

        void ApplySettings(UserSettings value, bool applyDisplay)
        {
            AudioListener.volume = value.masterVolume;
            if (music != null) music.volume = value.musicVolume * .3f;
            if (effects != null) effects.volume = value.effectsVolume * .4f;
            if (!applyDisplay) return;
#if !UNITY_EDITOR
            Screen.SetResolution(value.fullscreen ? Display.main.systemWidth : value.width,
                value.fullscreen ? Display.main.systemHeight : value.height,
                value.fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
#endif
        }

        public void ShowCredits()
        {
            BeginPage("credits");
            PageHeading("共 创  /  致 谢", "写给同行者", "一卷未尽的山河，一个慢慢生长的世界。");
            var panel = ui.Panel(content, "Credits", 100, 337, 1720, 592, new Color(.045f, .115f, .10f, .97f));
            ui.Label(panel.transform, "十二玉楼长生经", 48, 42, 1520, 75, 45, UiKit.Paper);
            ui.Label(panel.transform, "创作与方向  ·  fei-yu-001\n开发协作  ·  AI 辅助实现与迭代\n\n字体  ·  Noto Serif CJK（SIL Open Font License 1.1）\n美术  ·  本项目素材来源详见仓库素材记录\n声音  ·  原创程序合成的环境琴音与交互音效", 51, 154, 1530, 285, 27, UiKit.Muted);
            ui.Label(panel.transform, "感谢你愿意走进这个尚在生长的世界。", 51, 499, 1300, 50, 25, UiKit.Gold);
            FocusFirst();
        }

        void SetupAudio()
        {
            music = gameObject.AddComponent<AudioSource>(); music.loop = true; music.playOnAwake = false;
            effects = gameObject.AddComponent<AudioSource>(); effects.playOnAwake = false;
            clickTone = Tone("Soft stone", 22050, .15f, 660, .13f);
            var rate = 22050;
            var samples = new float[rate * 24];
            float[] notes = { 130.8128f, 195.9977f, 261.6256f, 293.6648f, 195.9977f, 174.6141f, 146.8324f, 195.9977f };
            for (var note = 0; note < notes.Length; note++)
            {
                var start = note * 3 * rate;
                for (var i = 0; i < rate * 6; i++)
                {
                    var t = i / (float)rate;
                    var envelope = Mathf.Min(t * 12, 1) * Mathf.Exp(-t * 1.1f);
                    var signal = Mathf.Sin(2 * Mathf.PI * notes[note] * t) + .24f * Mathf.Sin(4 * Mathf.PI * notes[note] * t);
                    samples[(start + i) % samples.Length] += .13f * envelope * signal;
                }
            }
            music.clip = AudioClip.Create("Mountain interval · original synthesis", samples.Length, 1, rate, false);
            music.clip.SetData(samples, 0); music.Play();
        }

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

        void PlayClick() { if (effects != null && clickTone != null) effects.PlayOneShot(clickTone); }
    }
}
