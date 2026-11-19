using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // Deliberately local to FrontEndCultivation / FrontEndChapter, not a shared UI recipe.
    public static class ScrollPageArtComposition
    {
        public static readonly Rect MistUv = new Rect(.34f, .36f, .28f, .30f);
        public static ScrollPageFeatheredArt Mist(UiKit ui, Transform parent, string name, Rect box, float alpha = 1)
        {
            var mist = Feathered(ui, parent, name, "nov8/tianji-scroll-fairyland", box, MistUv,
                new Vector4(64, 64, 64, 64), alpha);
            mist.CloudContour = true; mist.SetVerticesDirty(); return mist;
        }
        static ScrollPageFeatheredArt Feathered(UiKit ui, Transform parent, string name, string id,
            Rect box, Rect sourceUv, Vector4 feather, float alpha = 1)
        {
            var art = ui.Rect(parent, name, box.x, box.y, box.width, box.height).gameObject.AddComponent<ScrollPageFeatheredArt>();
            art.texture = ApprovedUiArt.Load(id); art.SourceUv = sourceUv; art.Feather = feather;
            art.color = new Color(1, 1, 1, alpha); art.raycastTarget = false; art.SetVerticesDirty();
            return art;
        }
        public static RawImage Fairyland(UiKit ui, Transform parent, string name, Rect box, bool block = false)
        {
            // Full 1774 x 887 RGBA art, including both jade rollers and all flying silk.
            var art = ImmortalUi.Texture(ui, parent, name, ApprovedUiArt.Load("nov8/tianji-scroll-fairyland"),
                box.x, box.y, box.width, box.height);
            art.raycastTarget = block; return art;
        }
        public static void Meridian(UiKit ui, RectTransform host)
        {
            var main = host.Find("Meridian immortal sanctum") as RectTransform;
            if (main == null) return;
            // Leave the existing view, controls, aura, callbacks and inner-vision state machine alive.
            Hide(main, "Meridian cold xuan mounting"); Hide(main, "Meridian moon paper");
            Hide(main, "Meridian adult immortal painting");
            var scenery = ui.Rect(main, "Meridian authored cloud landscape", 0, 0, 1920, 1080);
            scenery.SetAsFirstSibling();
            Feathered(ui, scenery, "Meridian distant golden cloud sky", "nov8/meridian-daoist-masterpiece",
                new Rect(0, 0, 1920, 1080), new Rect(.74f, .58f, .26f, .42f), Vector4.zero);
            // The complete square painting is retained. Its head, hands and lotus never enter a crop.
            Feathered(ui, scenery, "Meridian full immortal and lotus", "nov8/meridian-daoist-masterpiece",
                new Rect(56, 0, 1080, 1080), new Rect(0, 0, 1, 1), new Vector4(76, 22, 96, 28));
            // Separate softly feathered, painted reading patches: no right-hand paper sheet.
            Mist(ui, scenery, "Meridian title cloud silk", new Rect(1112, 118, 692, 218), .88f);
            Mist(ui, scenery, "Meridian summary cloud silk", new Rect(1138, 298, 640, 286), .94f);
            Mist(ui, scenery, "Meridian verse cloud silk", new Rect(1152, 518, 612, 194), .90f);
            Mist(ui, scenery, "Meridian roots cloud silk", new Rect(1138, 772, 640, 230), .92f);
            Place(main, "Meridian breathing golden qi", new Rect(56, 0, 1080, 1080));
            Place(main, "Meridian title", new Rect(1228, 188, 460, 80));
            Place(main, "Meridian actual summary", new Rect(1234, 344, 450, 190));
            Place(main, "Meridian meditation verse", new Rect(1234, 518, 450, 80));
            Place(main, "Meridian enter inner vision", new Rect(1278, 690, 364, 90));
            Place(main, "Meridian dormant roots sleeping jade compass", new Rect(1210, 804, 184, 184));
            Place(main, "Meridian roots unknown", new Rect(1420, 850, 260, 90));
            // Existing Jade button geometry and all 42/28/27/30/23/25 pt labels are untouched.
            Place(main, "Ledger close", new Rect(1686, 38, 190, 76));
        }
        static void Hide(Transform parent, string name)
        {
            var old = parent.Find(name); if (old != null) old.gameObject.SetActive(false);
        }
        public static void Place(Transform parent, string name, Rect box)
        {
            var rect = parent.Find(name) as RectTransform; if (rect == null) return;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(box.x, -box.y); rect.sizeDelta = box.size;
        }
    }
}
