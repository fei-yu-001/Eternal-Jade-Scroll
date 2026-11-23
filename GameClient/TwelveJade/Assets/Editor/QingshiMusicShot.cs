using System;
using System.Collections;
using System.Linq;
using TwelveJade.Core;
using TwelveJade.Presentation;
using UnityEngine;
using UnityEngine.Video;

namespace TwelveJade.Editor
{
    public static partial class PrologueTownShot
    {
        static IEnumerator MusicChecks()
        {
            foreach (var path in new[] { "Assets/Editor/QingshiMusicShot.cs", "Assets/Scripts/Presentation/FrontEndMusic.cs",
                "Assets/Scripts/Presentation/FrontEndSettings.cs", "Assets/Scripts/Presentation/MenuVideoBackdrop.cs" })
            { Bind(path); Bind(path + ".meta"); }
            Require("scratch music saves", SamePath(Field(host.Repository, "root") as string, state.scratch));
            foreach (string id in new[] { "immortals-sigh", "hidden-stream", "clouds-rest" })
            {
                var clip = Resources.Load<AudioClip>("Audio/Music/" + id);
                Require(id + " imported recording", clip != null && clip.length > 160 && clip.length < 185 && clip.channels == 2);
                Check(id + " streamed", clip.loadType == AudioClipLoadType.Streaming);
                BindAsset(clip);
            }
            var settings = (UserSettings)Field(host, "settings");
            settings.masterVolume = 1; settings.musicVolume = .7f; settings.effectsVolume = 0;
            Invoke(host, "ApplySettings", settings, false);
            yield return MusicAudible("immortals-sigh");
            yield return Until(() => host.GetComponentInChildren<VideoPlayer>() != null ||
                UnityEngine.Object.FindObjectsByType<VideoPlayer>(FindObjectsSortMode.None).Any(), "menu video exists", 15);
            Check("title video has no competing soundtrack", UnityEngine.Object.FindObjectsByType<VideoPlayer>(FindObjectsSortMode.None)
                .All(p => p.audioOutputMode == VideoAudioOutputMode.None));
            Check("no default synthesized bird track", !host.AmbiencePlayingForCheck && host.AmbienceClipLengthForCheck == 0);

            var preset = Resources.LoadAll<CharacterPreset>("Characters").OrderBy(p => p.id).First();
            host.Repository.Create(1, preset.id, "青石听泉", "male", 0, null, "shaxing");
            host.LoadSlot(host.Repository.Read(1));
            yield return Until(() => host.CurrentPage == "town" && host.TravelerPuppetForShot != null, "music town ready", 20);
            yield return MusicAudible("hidden-stream");
            Check("current build wears rural male clothes", host.TravelerPuppetForShot.View.name.StartsWith("qingshi-male-"));
            yield return Capture("music-current-male-town", "real-resources");
            var source = (AudioSource)Field(host, "music");
            float beforeSettings = source.time;
            host.ShowSettings(); yield return new WaitForSecondsRealtime(1);
            Check("settings keeps town music and position", host.MusicCueForCheck == "hidden-stream" && source.time > beforeSettings);
            yield return Capture("music-settings", "current-settings");
            settings.musicVolume = 0; Invoke(host, "ApplySettings", settings, false); yield return Frames(3);
            Check("music slider zero silences music", source.volume == 0 && source.isPlaying);
            float mutedTime = source.time;
            settings.musicVolume = .7f; Invoke(host, "ApplySettings", settings, false); yield return Frames(4);
            Check("music unmute preserves playback", source.volume > .4f && source.time >= mutedTime);
            settings.masterVolume = 0; Invoke(host, "ApplySettings", settings, false);
            Check("master volume still applies", AudioListener.volume == 0);
            settings.masterVolume = 1; Invoke(host, "ApplySettings", settings, false);
            host.Repository.SaveSettings(settings);
            Check("music volume persists in isolated save", Mathf.Approximately(host.Repository.LoadSettings(out _).musicVolume, .7f));
            Invoke(host, "CloseSettings"); yield return Frames(4);
            Require("home comic opens with town music", host.ShowQingshiComic("home")); yield return new WaitForSecondsRealtime(1);
            Check("home comic retains town score", host.MusicCueForCheck == "hidden-stream");
            Invoke(host, "CloseModal");

            Invoke(host, "CancelTravel");
            var position = typeof(FrontEndController).GetField("worldPos", Flags);
            var townPosition = (Vector2)position.GetValue(host);
            position.SetValue(host, new Vector2(900, 5300)); yield return new WaitForSecondsRealtime(.1f);
            position.SetValue(host, townPosition); yield return new WaitForSecondsRealtime(.2f);
            Check("brief boundary crossing does not restart town music", host.MusicCueForCheck == "hidden-stream");
            position.SetValue(host, new Vector2(900, 5300));
            yield return MusicAudible("clouds-rest");
            source.time = source.clip.length - .25f;
            yield return new WaitForSecondsRealtime(1.2f);
            Check("recording loops after natural end", source.isPlaying && source.time < 4);
            position.SetValue(host, townPosition); yield return MusicAudible("hidden-stream");
            host.TickAmbience();
            Check("return to town cannot restart birds", !host.AmbiencePlayingForCheck && host.AmbienceClipLengthForCheck == 0);
            host.ShowMenu(); yield return MusicAudible("immortals-sigh");
            Check("one looping music voice", host.GetComponents<AudioSource>().Count(s => s.loop && s.isPlaying) == 1);
            Check("source settings are nonspatial and looped", source.spatialBlend == 0 && source.loop && !source.playOnAwake);
        }

        static IEnumerator MusicAudible(string id)
        {
            yield return Until(() => host.MusicCueForCheck == id && host.MusicPlayingForCheck,
                id + " actually playing", 12);
            yield return new WaitForSecondsRealtime(1);
            var source = (AudioSource)Field(host, "music");
            float firstTime = source.time, peak = 0;
            var samples = new float[2048];
            for (int i = 0; i < 20; i++)
            {
                source.GetOutputData(samples, 0);
                foreach (float sample in samples) peak = Mathf.Max(peak, Mathf.Abs(sample));
                yield return new WaitForSecondsRealtime(.1f);
            }
            Check(id + " audio clock advances", source.time > firstTime + 1);
            Check(id + " real output contains audio", peak > .00001f, "peak=" + peak.ToString("G6"));
            Check(id + " music volume audible", source.volume > .4f && !source.mute && AudioListener.volume > 0);
        }
    }
}
