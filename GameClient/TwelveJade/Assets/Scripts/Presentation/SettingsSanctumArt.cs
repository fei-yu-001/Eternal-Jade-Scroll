using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // Settings owns these surfaces. Borrowed Resources textures live with Unity;
    // no shared style, catalogue entry, setting value or input handler is changed.
    public sealed class SettingsSanctumArt : RawImage
    {
        const string Terrace = "Art/Background/memo-terrace";
        const string ScrollMount = "Art/UI/Nov10/dialogue-scroll-mount";
        const string JadeControl = "Art/UI/Nov10/dialogue-control-jade";
        const string ActiveControl = "Art/UI/Nov10/dialogue-control-active";
        static readonly Color Ink = UiKit.Hex("304B46");
        static readonly Color Muted = UiKit.Hex("58675B");
        static readonly Color Gold = UiKit.Hex("92784A");

        enum Surface { Terrace, Mount, Rule }
        Surface surface;
        RectTransform viewport;

        public static void Backdrop(UiKit ui, Transform parent, RectTransform screen)
        {
            var art = ui.Rect(parent, "Settings terrace daylight", 0, 0, 1920, 1080)
                .gameObject.AddComponent<SettingsSanctumArt>();
            art.surface = Surface.Terrace;
            art.texture = Resources.Load<Texture2D>(Terrace);
            art.color = art.texture != null ? Color.white : UiKit.Hex("CCCDBB");
            art.raycastTarget = false;
            art.viewport = screen;
            art.rectTransform.anchorMin = art.rectTransform.anchorMax = new Vector2(.5f, .5f);
            art.rectTransform.pivot = new Vector2(.5f, .5f);
            art.rectTransform.anchoredPosition = Vector2.zero;
            art.FitViewport();
        }

        public static RectTransform Panel(UiKit ui, Transform parent, Rect box)
        {
            var host = ui.Rect(parent, "Settings silk sanctum", box.x, box.y, box.width, box.height);
            var mount = ui.Rect(host, "Settings cloud silk mount", -56, -52, box.width + 112, box.height + 100)
                .gameObject.AddComponent<SettingsSanctumArt>();
            mount.surface = Surface.Mount;
            mount.texture = Resources.Load<Texture2D>(ScrollMount);
            // Warm grey silk; the authored jade, gold clouds and mountain wash remain visible.
            mount.color = new Color(.94f, .98f, .98f, .97f);
            mount.raycastTarget = true;
            Rule(ui, host, "Settings audio gold thread", new Rect(112, 222, 630, 1.5f));
            Rule(ui, host, "Settings display gold thread", new Rect(884, 222, 630, 1.5f));
            Rule(ui, host, "Settings middle gold thread", new Rect(808, 132, 1.5f, 438));
            Rule(ui, host, "Settings footer gold thread", new Rect(112, 746, 1420, 1.5f));
            return host;
        }

        static void Rule(UiKit ui, Transform parent, string name, Rect box)
        {
            var rule = ui.Rect(parent, name, box.x, box.y, box.width, box.height)
                .gameObject.AddComponent<SettingsSanctumArt>();
            rule.surface = Surface.Rule;
            rule.color = new Color(Gold.r, Gold.g, Gold.b, .48f);
            rule.raycastTarget = false;
        }

        public static TMP_Text Text(UiKit ui, Transform parent, string name, string value, Rect box,
            float size = 26, TextAlignmentOptions align = TextAlignmentOptions.TopLeft, bool muted = false)
        {
            var label = XuanSettingsUi.Text(ui, parent, name, value, box, size, align, muted);
            label.color = muted ? Muted : Ink;
            return label;
        }

        public static Button Button(UiKit ui, Transform parent, string name, string value,
            float x, float y, float width, float height, UnityAction action, Func<bool> low, Action click)
        {
            // Retain the original click sound, live reduced-motion callback and keyboard halo.
            var button = XuanSettingsUi.Button(ui, parent, name, value, x, y, width, height, action, low, click);
            var plate = button.GetComponentInChildren<JadeCommandPlateGraphic>();
            var art = Resources.Load<Texture2D>(name == "Settings save" ? ActiveControl : JadeControl);
            if (plate != null && art != null) plate.texture = art;
            var label = button.GetComponentInChildren<TMP_Text>();
            if (label != null) label.color = Ink;
            return button;
        }

        public static Slider Slider(UiKit ui, Transform parent, string name, string title,
            float value, float y, UnityAction<float> changed)
        {
            // Put the authored rail behind the existing Slider and its unchanged drag area.
            var rail = ui.Rect(parent, name + " jade rail", 342, y + 1, 306, 58)
                .gameObject.AddComponent<JadeCommandPlateGraphic>();
            rail.texture = Resources.Load<Texture2D>(JadeControl);
            rail.raycastTarget = false;
            var slider = XuanSettingsUi.Slider(ui, parent, name, title, value, y, changed);
            parent.Find(name + " caption").GetComponent<TMP_Text>().color = Ink;
            parent.Find(name + " percentage").GetComponent<TMP_Text>().color = Ink;
            slider.GetComponent<XuanStoneGraphic>().color = new Color(.73f, .85f, .79f, .66f);
            return slider;
        }

        void LateUpdate()
        {
            // Follow window/aspect changes without reparenting the art outside its settings page.
            if (surface == Surface.Terrace) FitViewport();
        }

        void FitViewport()
        {
            if (viewport != null && rectTransform.sizeDelta != viewport.rect.size)
                rectTransform.sizeDelta = viewport.rect.size;
            UiKit.Cover(this);
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            if (surface == Surface.Terrace)
            {
                base.OnPopulateMesh(mesh);
                return;
            }
            mesh.Clear();
            var r = rectTransform.rect;
            if (surface == Surface.Rule)
            {
                bool vertical = r.height > r.width;
                var transparent = new Color(color.r, color.g, color.b, 0);
                for (int i = 0; i < 3; i++)
                {
                    float t = i * .5f;
                    var tint = i == 1 ? color : transparent;
                    var a = vertical ? new Vector2(r.xMin, r.yMin + r.height * t) : new Vector2(r.xMin + r.width * t, r.yMin);
                    var b = vertical ? new Vector2(r.xMax, a.y) : new Vector2(a.x, r.yMax);
                    mesh.AddVert(a, tint, Vector2.zero);
                    mesh.AddVert(b, tint, Vector2.zero);
                }
                for (int i = 0; i < 2; i++)
                {
                    int n = i * 2;
                    mesh.AddTriangle(n, n + 1, n + 3);
                    mesh.AddTriangle(n, n + 3, n + 2);
                }
                return;
            }
            if (texture == null) return;
            // Nine slices keep the cloud corners and jade finials at their authored size.
            // Only the middle silk and straight roller shafts extend to fit the settings.
            float left = Mathf.Min(240, r.width * .24f), right = left;
            float bottom = Mathf.Min(190, r.height * .24f), top = Mathf.Min(180, r.height * .24f);
            float[] x = { r.xMin, r.xMin + left, r.xMax - right, r.xMax };
            float[] y = { r.yMin, r.yMin + bottom, r.yMax - top, r.yMax };
            float[] u = { 0, 240f / texture.width, 1 - 240f / texture.width, 1 };
            float[] v = { 0, 190f / texture.height, 1 - 180f / texture.height, 1 };
            for (int row = 0; row < 4; row++)
                for (int col = 0; col < 4; col++)
                    mesh.AddVert(new Vector2(x[col], y[row]), color, new Vector2(u[col], v[row]));
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 3; col++)
                {
                    int n = row * 4 + col;
                    mesh.AddTriangle(n, n + 4, n + 5);
                    mesh.AddTriangle(n, n + 5, n + 1);
                }
        }
    }
}
