using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    public sealed class PosterCardView : MonoBehaviour
    {
        RawImage artwork;
        Image frame;
        bool upperComposition;
        public string ResourcePath { get; private set; }
        public Rect Crop => artwork == null ? new Rect() : artwork.uvRect;
        public RawImage Artwork => artwork;
        public Image Frame => frame;
        public bool HasArtwork => artwork != null && artwork.texture != null;

        public void Configure(string presetId, string gender, bool favorUpperPortrait = false)
        {
            upperComposition=favorUpperPortrait;
            MemoVisualAssets.KeepAlive(gameObject);
            ResourcePath = string.IsNullOrEmpty(presetId) ? "" : MemoVisualAssets.Prefix + "Posters/" + presetId + "-" + gender;
            if (artwork == null)
            {
                artwork = MemoVisualAssets.Rect(transform, "Poster illustration").gameObject.AddComponent<RawImage>();
                artwork.raycastTarget = false;
                artwork.rectTransform.offsetMin = new Vector2(6, 6); artwork.rectTransform.offsetMax = new Vector2(-6, -6);
                frame = MemoVisualAssets.Art(transform, "Poster gold nine-slice", "paper-panel", true);
                frame.pixelsPerUnitMultiplier = 4;
            }
            artwork.texture = ResourcePath.Length == 0 ? null : MemoVisualAssets.GetTexture("Posters/" + presetId + "-" + gender);
            artwork.enabled = artwork.texture != null;
            FrameArtwork();
        }

        void FrameArtwork()
        {
            UiKit.Cover(artwork);
            if(upperComposition&&artwork!=null&&artwork.uvRect.height<1)
            {
                var crop=artwork.uvRect;crop.y=Mathf.Min(1-crop.height,crop.y+.10f);artwork.uvRect=crop;
            }
        }
        void OnRectTransformDimensionsChange() { if(artwork!=null)FrameArtwork(); }
    }
}
