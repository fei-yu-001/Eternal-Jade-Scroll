using System;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // Scene art is borrowed. Only the short-lived outgoing video copy is owned here.
    public sealed class MemoBackdrop : MonoBehaviour
    {
        public const string ArtworkResourcePath = "Art/Background/memo-terrace";
        public const float TransitionSeconds = .4f;
        CanvasGroup menuGroup, memoGroup;
        RawImage liveVideo, heldVideo, memoArt;
        MenuVideoBackdrop video;
        Func<bool> reduceMotion;
        RenderTexture heldTexture;
        string currentPage = "menu";
        float fromAlpha, targetAlpha, elapsed;
        bool transitioning, configured, released;

        public bool HasMemoArtwork => memoArt != null && memoArt.texture != null;
        public bool IsTransitioning => transitioning;
        public float MemoOpacity => memoGroup == null ? 0 : memoGroup.alpha;
        public bool WantsMenuVideo => !released && isActiveAndEnabled && currentPage == "menu";

        public void Configure(UiKit ui, RectTransform root, CanvasGroup menu, RawImage videoImage,
            MenuVideoBackdrop player, Func<bool> reduced)
        {
            if (configured) throw new InvalidOperationException("Memo backdrop is already configured.");
            configured = true;
            MemoVisualAssets.KeepAlive(gameObject);
            menuGroup = menu; liveVideo = videoImage; video = player; reduceMotion = reduced;
            heldVideo = ui.Stretch(menu.transform, "Outgoing menu frame").gameObject.AddComponent<RawImage>();
            heldVideo.raycastTarget = false; heldVideo.enabled = false;
            var memoRoot = ui.Stretch(root, "Memo backdrop");
            memoGroup = memoRoot.gameObject.AddComponent<CanvasGroup>();
            memoGroup.interactable = false; memoGroup.blocksRaycasts = false; memoGroup.alpha = 0;
            memoArt = ui.Stretch(memoRoot, "Memo terrace").gameObject.AddComponent<RawImage>();
            memoArt.raycastTarget = false;
            SetArtwork(Resources.Load<Texture2D>("Art/Background/memo-terrace"));
        }

        // A missing approved image leaves an opaque neutral surface, never the menu portrait.
        // Missing art is exposed to evidence via HasMemoArtwork, not through product copy.
        public void SetArtwork(Texture2D texture)
        {
            if (released || memoArt == null) return;
            memoArt.texture = texture;
            memoArt.color = texture != null ? Color.white : new Color(.20f, .23f, .21f, 1);
            UiKit.Cover(memoArt);
        }

        public void SetPage(string next)
        {
            if (!configured || released) return;
            bool immediate = reduceMotion?.Invoke() ?? true;
            if (next == currentPage)
            {
                if (immediate && transitioning) Finish();
                video?.Refresh();
                return;
            }
            bool betweenArtPages = (currentPage == "menu" && next == "slots") ||
                (currentPage == "slots" && next == "menu");
            if (betweenArtPages && next == "slots" && !immediate) HoldVisibleVideo();
            else ReleaseHeldVideo();
            currentPage = next;
            targetAlpha = next == "slots" ? 1 : 0;
            fromAlpha = memoGroup.alpha; elapsed = 0;
            transitioning = betweenArtPages && !immediate;
            // The outgoing copy survives this call, so stopping the real decoder cannot flash the still.
            video?.Refresh();
            if (transitioning) menuGroup.alpha = 1;
            else Finish();
        }

        void HoldVisibleVideo()
        {
            ReleaseHeldVideo();
            if (liveVideo == null || !liveVideo.enabled || liveVideo.texture == null || liveVideo.color.a <= 0) return;
            var texture = liveVideo.texture;
            heldTexture = new RenderTexture(texture.width, texture.height, 0, RenderTextureFormat.ARGB32)
            {
                name = "Memo outgoing video", hideFlags = HideFlags.DontSave,
                filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
            };
            try
            {
                if (!heldTexture.Create()) throw new InvalidOperationException("Cannot retain outgoing menu frame.");
                Graphics.Blit(texture, heldTexture);
                heldVideo.texture = heldTexture; heldVideo.color = liveVideo.color;
                heldVideo.uvRect = liveVideo.uvRect; heldVideo.enabled = true;
            }
            catch
            {
                ReleaseHeldVideo();
                throw;
            }
        }

        void LateUpdate()
        {
            if (!configured || released) return;
            UiKit.Cover(memoArt);
            if (heldVideo != null && heldVideo.enabled) UiKit.Cover(heldVideo);
            if (!transitioning) return;
            if (reduceMotion?.Invoke() ?? true) { Finish(); return; }
            elapsed += Time.unscaledDeltaTime;
            memoGroup.alpha = Mathf.Lerp(fromAlpha, targetAlpha, Mathf.SmoothStep(0, 1, elapsed / TransitionSeconds));
            if (elapsed >= TransitionSeconds) Finish();
        }

        void Finish()
        {
            transitioning = false;
            if (memoGroup != null) memoGroup.alpha = currentPage == "slots" ? 1 : 0;
            if (menuGroup != null) menuGroup.alpha = currentPage == "menu" ? 1 : 0;
            ReleaseHeldVideo();
        }

        void ReleaseHeldVideo()
        {
            if (heldVideo != null) { heldVideo.enabled = false; heldVideo.texture = null; }
            if (heldTexture == null) return;
            heldTexture.Release(); Destroy(heldTexture); heldTexture = null;
        }

        void OnDisable()
        {
            Finish();
            video?.Refresh();
        }

        public void Release()
        {
            if (released) return;
            released = true; transitioning = false;
            ReleaseHeldVideo();
            if (menuGroup != null) menuGroup.alpha = 0;
            if (memoGroup != null) memoGroup.alpha = 0;
            if (memoArt != null) memoArt.texture = null;
            reduceMotion = null;
            video?.Refresh(); video = null;
        }

        void OnDestroy() => Release();
    }
}
