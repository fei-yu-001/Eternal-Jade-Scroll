using System.Linq;
using UnityEngine;

namespace TwelveJade.Presentation
{
    public sealed partial class FrontEndController
    {
        AudioClip titleMusic, townMusic, mountainMusic, requestedMusic;
        float musicGain, musicRequestSince;
        bool musicPaused;
        const float MusicLevel = .8f;

        void SetupMusic()
        {
            titleMusic = LoadMusic("immortals-sigh");
            townMusic = LoadMusic("hidden-stream");
            mountainMusic = LoadMusic("clouds-rest");
            music = gameObject.AddComponent<AudioSource>();
            music.playOnAwake = false;
            music.loop = true;
            music.spatialBlend = 0;
            music.mute = false;
            music.volume = 0;
            requestedMusic = titleMusic;
            musicRequestSince = Time.unscaledTime;
            SwitchMusic(titleMusic);
        }

        static AudioClip LoadMusic(string id)
        {
            var clip = Resources.Load<AudioClip>("Audio/Music/" + id);
            if (clip == null) Debug.LogError("Missing background music: " + id);
            return clip;
        }

        AudioClip MusicForCurrentScene()
        {
            string destination = page == "settings" ? settingsReturnPage : page;
            if (activeSave == null || destination == "menu" || destination == "slots" ||
                destination == "character-creation" || destination == "credits") return titleMusic;
            var comic = modal != null ? modal.GetComponentInChildren<QingshiComicView>() : null;
            if (comic != null)
                return comic.SceneId == "tomb" || comic.SceneId == "calamity" || comic.SceneId == "departure"
                    ? mountainMusic : townMusic;
            bool inMountains = WildLayout != null && WildLayout.Regions.Any(r =>
                (worldPos - new Vector2(r.X, r.Y)).sqrMagnitude < r.Radius * r.Radius);
            return inMountains ? mountainMusic : townMusic;
        }

        float CurrentMusicVolume() => settings != null ? settings.musicVolume : .5f;

        void TickMusic()
        {
            if (music == null) return;
            if (musicPaused || AudioListener.pause)
            {
                if (musicPaused && music.isPlaying) music.Pause();
                return;
            }
            var wanted = MusicForCurrentScene();
            if (wanted != requestedMusic)
            {
                requestedMusic = wanted;
                musicRequestSince = Time.unscaledTime;
            }
            // A short dwell prevents position jitter at a region edge from restarting a track.
            bool change = requestedMusic != null && requestedMusic != music.clip &&
                Time.unscaledTime - musicRequestSince >= .4f;
            musicGain = Mathf.MoveTowards(musicGain, change ? 0 : 1,
                Time.unscaledDeltaTime / (change ? .35f : .7f));
            if (change && musicGain <= 0) SwitchMusic(requestedMusic);
            else if (!change && wanted != null && music.clip == wanted && !music.isPlaying)
            {
                if (wanted.loadState == AudioDataLoadState.Unloaded) wanted.LoadAudioData();
                music.Play();
            }
            ApplyMusicVolume(CurrentMusicVolume());
        }

        void SwitchMusic(AudioClip clip)
        {
            music.Stop();
            music.clip = clip;
            music.mute = false;
            musicGain = 0;
            music.volume = 0;
            if (clip == null) return;
            if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
            if (!musicPaused && !AudioListener.pause) music.Play();
        }

        public void SetMusicPaused(bool paused)
        {
            musicPaused = paused;
            if (music == null) return;
            if (paused)
            {
                if (music.isPlaying) music.Pause();
                return;
            }
            if (!AudioListener.pause && !music.isPlaying)
            {
                if (music.clip == null) SwitchMusic(MusicForCurrentScene());
                else if (music.time > 0f) music.UnPause();
                else
                {
                    if (music.clip.loadState == AudioDataLoadState.Unloaded) music.clip.LoadAudioData();
                    music.Play();
                }
            }
            ApplyMusicVolume(CurrentMusicVolume());
        }

        void ApplyMusicVolume(float volume)
        {
            if (music != null) music.volume = Mathf.Clamp01(volume) * MusicLevel * musicGain;
        }

        void ReleaseMusic()
        {
            musicPaused = false;
            requestedMusic = null;
            if (music == null) return;
            music.Stop();
            music.clip = null; // Imported Resources clips belong to Unity, not this controller.
        }

        public string MusicCueForCheck => music != null && music.clip != null ? music.clip.name : "";
        public bool MusicPlayingForCheck => music != null && music.isPlaying;
        public bool MusicPausedForCheck => musicPaused;
        public float MusicTimeForCheck => music != null ? music.time : 0f;
        public float MusicVolumeForCheck => music != null ? music.volume : 0f;
    }
}
