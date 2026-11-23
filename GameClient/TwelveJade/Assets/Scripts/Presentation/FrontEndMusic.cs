using System.Linq;
using UnityEngine;

namespace TwelveJade.Presentation
{
    public sealed partial class FrontEndController
    {
        AudioClip titleMusic, townMusic, mountainMusic, requestedMusic;
        float musicGain, musicRequestSince;
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
            music.volume = 0;
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
            var comic = modal != null ? modal.GetComponent<QingshiComicView>() : null;
            if (comic != null)
                return comic.SceneId == "tomb" || comic.SceneId == "calamity" || comic.SceneId == "departure"
                    ? mountainMusic : townMusic;
            bool inMountains = WildLayout != null && WildLayout.Regions.Any(r =>
                (worldPos - new Vector2(r.X, r.Y)).sqrMagnitude < r.Radius * r.Radius);
            return inMountains ? mountainMusic : townMusic;
        }

        void TickMusic()
        {
            if (music == null) return;
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
            ApplyMusicVolume(settings?.musicVolume ?? .7f);
        }

        void SwitchMusic(AudioClip clip)
        {
            music.Stop();
            music.clip = clip;
            musicGain = 0;
            music.volume = 0;
            if (clip != null) music.Play();
        }

        void ApplyMusicVolume(float volume)
        {
            if (music != null) music.volume = Mathf.Clamp01(volume) * MusicLevel * musicGain;
        }

        void ReleaseMusic()
        {
            if (music == null) return;
            music.Stop();
            music.clip = null; // Imported Resources clips belong to Unity, not this controller.
        }

        public string MusicCueForCheck => music != null && music.clip != null ? music.clip.name : "";
        public bool MusicPlayingForCheck => music != null && music.isPlaying;
    }
}
