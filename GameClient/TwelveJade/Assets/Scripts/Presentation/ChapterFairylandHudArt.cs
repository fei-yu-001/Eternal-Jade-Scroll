using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // Adapts only the chapter HUD scroll; QuestBriefRoll still owns toggles, focus and detail gating.
    [DefaultExecutionOrder(100)]
    public sealed class ChapterFairylandHudArt : MonoBehaviour
    {
        RectTransform frame, viewport;
        ScrollPageFeatheredArt readingMist;
        float lastHeight = -1;
        public void Configure(UiKit ui, RectTransform host)
        {
            frame = host;
            viewport = host.Find("Hud quest reveal mask") as RectTransform;
            var old = host.Find("Hud fairyland jade silk");
            if (old != null) old.gameObject.SetActive(false);
            readingMist = ScrollPageArtComposition.Mist(ui, host, "Chapter brief painted cloud reading area", new Rect(42, 198, 396, 1));
            readingMist.CloudContour = false;
            readingMist.Feather = new Vector4(38, 48, 38, 48); readingMist.raycastTarget = true;
            readingMist.transform.SetAsFirstSibling();
            var scroll = ScrollPageArtComposition.Fairyland(ui, host, "Chapter brief complete fairyland artwork", new Rect(0, 0, 480, 240), true);
            scroll.transform.SetSiblingIndex(1);
            ScrollPageArtComposition.Place(host, "Hud quest golden seal", new Rect(84, 96, 42, 42));
            ScrollPageArtComposition.Place(host, "Hud quest scroll name", new Rect(140, 88, 180, 52));
            ScrollPageArtComposition.Place(host, "Hud quest fold state", new Rect(326, 96, 72, 40));
            ScrollPageArtComposition.Place(host, "Hud quest toggle", new Rect(34, 44, 410, 154));
            if (viewport != null) viewport.anchoredPosition = new Vector2(82, -218);
            // Keep the original text widths, point sizes, entry buttons and content height calculations.
            Apply();
        }
        void LateUpdate() => Apply();
        void Apply()
        {
            if (frame == null || viewport == null || readingMist == null) return;
            float visible = viewport.sizeDelta.y;
            frame.sizeDelta = new Vector2(480, 244 + visible);
            if (Mathf.Approximately(lastHeight, visible)) return;
            lastHeight = visible;
            readingMist.gameObject.SetActive(visible > .5f);
            readingMist.rectTransform.sizeDelta = new Vector2(396, visible + 76);
            readingMist.SetVerticesDirty();
        }
    }
}
