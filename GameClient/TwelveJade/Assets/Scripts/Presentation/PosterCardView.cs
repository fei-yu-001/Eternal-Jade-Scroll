using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    public sealed class PosterCardView : MonoBehaviour
    {
        RawImage artwork;
        Image frame;
        bool upperComposition, cutoutComposition;
        public bool IsCutout => cutoutComposition;
        public string ResourcePath { get; private set; }
        public Rect Crop => artwork == null ? new Rect() : artwork.uvRect;
        public RawImage Artwork => artwork;
        public Image Frame => frame;
        public bool HasArtwork => artwork != null && artwork.texture != null;

        public void Configure(string presetId, string gender, bool favorUpperPortrait = false, bool useFigure = false)
        {
            upperComposition=favorUpperPortrait;
            cutoutComposition=useFigure;
            MemoVisualAssets.KeepAlive(gameObject);
            ResourcePath = string.IsNullOrEmpty(presetId) ? "" : "Art/Characters/Qingshi/qingshi-" + (gender == "female" ? "female" : "male") + (cutoutComposition ? "-figure" : "-portrait");
            if (artwork == null)
            {
                artwork = MemoVisualAssets.Rect(transform, "Poster illustration").gameObject.AddComponent<RawImage>();
                artwork.raycastTarget = false;
                artwork.rectTransform.offsetMin = new Vector2(6, 6); artwork.rectTransform.offsetMax = new Vector2(-6, -6);
            }
            if (!cutoutComposition && frame == null)
            {
                frame = MemoVisualAssets.Art(transform, "Poster gold nine-slice", "paper-panel", true);
                frame.pixelsPerUnitMultiplier = 4;
            }
            if (frame != null) frame.gameObject.SetActive(!cutoutComposition);
            artwork.texture = ResourcePath.Length == 0 ? null : Resources.Load<Texture2D>(ResourcePath);
            artwork.enabled = artwork.texture != null;
            FrameArtwork();
        }

        void FrameArtwork()
        {
            if (cutoutComposition)
            {
                if (artwork == null || artwork.texture == null) return;
                var bounds = ((RectTransform)transform).rect;
                float scale = Mathf.Min(Mathf.Max(1, bounds.width - 24) / artwork.texture.width,
                    Mathf.Max(1, bounds.height - 12) / artwork.texture.height);
                var rect = artwork.rectTransform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = new Vector2(artwork.texture.width, artwork.texture.height) * scale;
                artwork.uvRect = new Rect(0, 0, 1, 1);
                return;
            }
            if (artwork == null) return;
            artwork.rectTransform.anchorMin = Vector2.zero;
            artwork.rectTransform.anchorMax = Vector2.one;
            artwork.rectTransform.offsetMin = new Vector2(6, 6);
            artwork.rectTransform.offsetMax = new Vector2(-6, -6);
            UiKit.Cover(artwork);
            if(upperComposition&&artwork!=null&&artwork.uvRect.height<1)
            {
                var crop=artwork.uvRect;crop.y=Mathf.Min(1-crop.height,crop.y+.10f);artwork.uvRect=crop;
            }
        }
        void OnRectTransformDimensionsChange() { if(artwork!=null)FrameArtwork(); }
    }
}
