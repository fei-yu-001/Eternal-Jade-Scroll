using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace TwelveJade.Presentation
{
    /// <summary>Menu movie with its own soundtrack and a borrowed still fallback.</summary>
    [DisallowMultipleComponent]
    public sealed class MenuVideoBackdrop : MonoBehaviour
    {
        public const string ResourcePath = "Art/Background/menu-loop";
        public const float PrepareTimeoutSeconds = 15f;
        public const float FrameTimeoutSeconds = 8f;
        public const float LoopFadeSeconds = .45f;
        public enum PlaybackState { Stopped, Preparing, WaitingForFrame, Playing, StaticFallback, Released }

        RawImage image;
        VideoClip clip;
        VideoPlayer player;
        AudioSource soundtrack;
        Func<float> soundtrackVolume;
        Func<bool> onMenu, reduceMotion;
        bool configured, requested, released, framePending, waitingForLoopFrame;
        int frameReceivedAt;
        double prepareDeadline, lastFrameAt;
        float revealStarted;

        public PlaybackState State { get; private set; }
        public string Failure { get; private set; } = "";
        public bool HasDecodedFrame { get; private set; }
        public int DecodedFrames { get; private set; }
        public int CompletedLoops { get; private set; }
        public bool IsVisible => image != null && image.enabled && image.color.a > 0;
        public bool SoundtrackPlaying => player != null && player.isPlaying && HasDecodedFrame && player.audioTrackCount > 0;
        public bool SuppressProceduralMotion => requested && State != PlaybackState.StaticFallback;

        public void Configure(RawImage target, VideoClip source, Func<bool> isOnMenu, Func<bool> motionReduced, Func<float> volume = null)
        {
            if (configured || released) throw new InvalidOperationException("Menu backdrop may only be configured once.");
            image = target != null ? target : throw new ArgumentNullException(nameof(target));
            clip = source;
            onMenu = isOnMenu;
            reduceMotion = motionReduced;
            soundtrackVolume = volume;
            image.raycastTarget = false;
            configured = true;
            Hide();
            Refresh();
        }

        // Called synchronously at page transitions, and polled for settings loaded after BuildBackdrop.
        public void Refresh()
        {
            if (!configured || released) return;
            bool wanted = isActiveAndEnabled && onMenu != null && onMenu() &&
                (reduceMotion == null || !reduceMotion());
            if (wanted == requested) return;
            requested = wanted;
            if (!wanted)
            {
                StopPlayer();
                State = PlaybackState.Stopped;
                return;
            }
            BeginPlayback();
        }

        void BeginPlayback()
        {
            Hide();
            Failure = "";
            if (clip == null) { Fail("Missing VideoClip: " + ResourcePath); return; }
            try
            {
                // A fresh player on each visit also rejects callbacks from a cancelled preparation.
                player = gameObject.AddComponent<VideoPlayer>();
                player.playOnAwake = false;
                player.source = VideoSource.VideoClip;
                player.clip = clip;
                player.renderMode = VideoRenderMode.APIOnly;
                if (soundtrack == null)
                {
                    soundtrack = gameObject.AddComponent<AudioSource>();
                    soundtrack.playOnAwake = false;
                    soundtrack.spatialBlend = 0;
                }
                player.audioOutputMode = VideoAudioOutputMode.AudioSource;
                player.controlledAudioTrackCount = 1;
                player.EnableAudioTrack(0, true);
                player.SetTargetAudioSource(0, soundtrack);
                soundtrack.volume = 0;
                player.isLooping = true;
                player.waitForFirstFrame = true;
                player.skipOnDrop = true;
                player.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
                player.sendFrameReadyEvents = true;
                player.prepareCompleted += OnPrepared;
                player.frameReady += OnFrameReady;
                player.loopPointReached += OnLoop;
                player.errorReceived += OnError;
                State = PlaybackState.Preparing;
                prepareDeadline = Time.realtimeSinceStartupAsDouble + PrepareTimeoutSeconds;
                player.Prepare();
            }
            catch (Exception e) { Fail("Video preparation: " + e.Message); }
        }

        bool IsCurrent(VideoPlayer source) => !released && requested && source != null && source == player;

        void OnPrepared(VideoPlayer source)
        {
            if (!IsCurrent(source)) return;
            Refresh();
            if (!IsCurrent(source)) return;
            try
            {
                State = PlaybackState.WaitingForFrame;
                source.Play();
            }
            catch (Exception e) { Fail("Video start: " + e.Message); }
        }

        void OnFrameReady(VideoPlayer source, long frame)
        {
            if (!IsCurrent(source) || !source.isPrepared || frame < 0 || source.texture == null) return;
            if (waitingForLoopFrame && source.time < source.length * .5) waitingForLoopFrame = false;
            DecodedFrames++;
            lastFrameAt = Time.realtimeSinceStartupAsDouble;
            if (HasDecodedFrame || framePending) return;
            // Do not reveal in PrepareCompleted. Allow the decoded frame to reach the render thread first.
            framePending = true;
            frameReceivedAt = Time.frameCount;
        }

        void OnLoop(VideoPlayer source)
        {
            if (!IsCurrent(source)) return;
            CompletedLoops++;
            waitingForLoopFrame = true;
            // The delivered push-in is NOT seamless. Both sides fade through the untouched still.
            SetAlpha(0);
        }

        void OnError(VideoPlayer source, string message)
        {
            if (IsCurrent(source)) Fail("Video decoder: " + message);
        }

        void Update()
        {
            Refresh();
            if (!requested || player == null) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (!HasDecodedFrame && now >= prepareDeadline) Fail("Video prepare/first-frame timeout.");
            else if (HasDecodedFrame && now - lastFrameAt > FrameTimeoutSeconds) Fail("Video frame delivery timeout.");
        }

        void LateUpdate()
        {
            if (!requested || player == null) return;
            // A settings/page change late in this frame must still hide before UI rendering.
            Refresh();
            if (!requested || player == null) return;
            if (framePending && Time.frameCount > frameReceivedAt && player.texture != null)
            {
                framePending = false;
                HasDecodedFrame = true;
                image.texture = player.texture;
                FitTexture(image.texture);
                revealStarted = Time.unscaledTime;
                State = PlaybackState.Playing;
                image.enabled = true;
            }
            if (!HasDecodedFrame) return;
            if (player.texture == null) { Fail("Decoded video texture unavailable."); return; }
            if (waitingForLoopFrame) { SetAlpha(0); return; }
            // APIOnly owns its texture; never Destroy() that texture or the Resources clip.
            image.texture = player.texture;
            FitTexture(image.texture); // Refit after live window/aspect changes as well as the first frame.
            double duration = player.length;
            double time = player.time;
            float fade = duration > 0
                ? Mathf.Min((float)(time / LoopFadeSeconds), (float)((duration - time) / LoopFadeSeconds)) : 0;
            fade = Mathf.Min(fade, (Time.unscaledTime - revealStarted) / LoopFadeSeconds);
            SetAlpha(Mathf.SmoothStep(0, 1, Mathf.Clamp01(fade)));
        }

        void FitTexture(Texture texture) => UiKit.Cover(image);

        void SetAlpha(float alpha)
        {
            if (image != null) image.color = new Color(1, 1, 1, alpha);
            if (soundtrack != null) soundtrack.volume = Mathf.Clamp01(soundtrackVolume?.Invoke() ?? .7f) * alpha;
        }

        void Hide()
        {
            HasDecodedFrame = false;
            framePending = false;
            waitingForLoopFrame = false;
            if (image == null) return;
            image.enabled = false;
            image.texture = null;
            SetAlpha(0);
        }

        void Fail(string reason)
        {
            Failure = reason;
            State = PlaybackState.StaticFallback;
            StopPlayer();
            // Latch until a new menu visit; no Update-loop retries and no modal that blocks navigation.
            Debug.LogWarning("[MenuVideoBackdrop] Static fallback: " + reason, this);
        }

        void StopPlayer()
        {
            Hide();
            if (soundtrack != null) soundtrack.Stop();
            if (player == null) return;
            var previous = player;
            player = null;
            previous.prepareCompleted -= OnPrepared;
            previous.frameReady -= OnFrameReady;
            previous.loopPointReached -= OnLoop;
            previous.errorReceived -= OnError;
            previous.sendFrameReadyEvents = false;
            previous.Stop();
            previous.clip = null;
            Destroy(previous);
        }

        void OnDisable()
        {
            if (released) return;
            requested = false;
            StopPlayer();
            State = PlaybackState.Stopped;
        }

        public void Release()
        {
            if (released) return;
            released = true;
            requested = false;
            StopPlayer();
            clip = null;
            onMenu = null;
            reduceMotion = null;
            soundtrackVolume = null;
            if (soundtrack != null) Destroy(soundtrack);
            soundtrack = null;
            State = PlaybackState.Released;
        }

        void OnDestroy() => Release();
    }
}
