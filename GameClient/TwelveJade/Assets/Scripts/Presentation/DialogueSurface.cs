using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    /// <summary>Dialogue-only painted silk and jade surfaces; borrowed textures, owned sliced sprites.</summary>
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class DialogueSurface : MaskableGraphic
    {
        public enum Style { BambooSlip, PaperCorners }
        public static readonly Color QuietInk = new Color(.30f, .32f, .27f, 1f);
        public static readonly Color AgedGold = new Color(.46f, .37f, .22f, .85f);
        public Style Kind = Style.BambooSlip;
        float emphasis;
        RawImage painted; Texture2D normalArt, activeArt;
        public void ConfigurePainted(UiKit kit, bool choice)
        {
            normalArt=ApprovedUiArt.Load(choice?"nov10/dialogue-choice-silk":"nov10/dialogue-control-jade");
            activeArt=choice?normalArt:ApprovedUiArt.Load("nov10/dialogue-control-active");
            painted=kit.Stretch(transform,"Dialogue painted silk control").gameObject.AddComponent<RawImage>();
            painted.texture=normalArt;painted.raycastTarget=false;ApplyPaint();SetVerticesDirty();
        }
        void ApplyPaint()
        {
            if(painted==null)return;painted.texture=emphasis>.35f?activeArt:normalArt;
            painted.color=dimmed?new Color(.83f,.85f,.80f,.70f):pressed?new Color(.83f,.91f,.84f,1):Color.white;
        }
        bool dimmed, pressed;

        protected override void Awake() { base.Awake(); raycastTarget = false; }
        public void SetFeedback(float value, bool disabled, bool isPressed)
        {
            if (Mathf.Approximately(emphasis, value) && dimmed == disabled && pressed == isPressed) return;
            emphasis = value; dimmed = disabled; pressed = isPressed; ApplyPaint(); SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); if(painted!=null&&normalArt!=null)return; var r = rectTransform.rect;
            if (r.width <= 0 || r.height <= 0) return;
            if (Kind == Style.PaperCorners) { Corners(mesh, r); return; }
            float opacity = dimmed ? .54f : 1f;
            var wood = new Color(.225f, .19f, .135f, .9f * opacity);
            if (pressed) wood = new Color(.18f, .15f, .105f, .94f * opacity);
            else wood = Color.Lerp(wood, new Color(.25f, .215f, .15f, .92f * opacity), emphasis * .35f);
            Quad(mesh, Rect.MinMaxRect(r.xMin + 2, r.yMin, r.xMax - 2, r.yMax), wood);
            // The two narrow end caps and interrupted fibres stay behind a generous text inset.
            Quad(mesh, new Rect(r.xMin, r.yMin + 2, 2, r.height - 4), wood);
            Quad(mesh, new Rect(r.xMax - 2, r.yMin + 2, 2, r.height - 4), wood);
            for (int i = 0; i < 5; i++)
            {
                float y = Mathf.Lerp(r.yMin + 6, r.yMax - 6, (i + .35f) / 5f);
                float start = r.xMin + 12 + i * 3;
                Stroke(mesh, new Vector2(start, y), new Vector2(r.xMax - 16 - (i % 3) * 17, y + ((i & 1) == 0 ? 1.1f : -.8f)),
                    .65f, new Color(.67f, .55f, .34f, .075f * opacity));
            }
            var edge = new Color(.58f, .47f, .28f, (.40f + emphasis * .22f) * opacity);
            LineRect(mesh, Rect.MinMaxRect(r.xMin + 3, r.yMin + 3, r.xMax - 3, r.yMax - 3), .85f, edge);
            var end = new Color(.65f, .54f, .33f, .22f * opacity);
            Stroke(mesh, new Vector2(r.xMin + 8, r.yMin + 9), new Vector2(r.xMin + 8, r.yMax - 9), 1, end);
            Stroke(mesh, new Vector2(r.xMax - 8, r.yMin + 9), new Vector2(r.xMax - 8, r.yMax - 9), 1, end);
            if (emphasis > .01f)
                Quad(mesh, new Rect(r.xMin + 3, r.yMin + 8, 2, r.height - 16), new Color(.74f, .61f, .36f, .7f * emphasis * opacity));
        }

        static void Corners(VertexHelper mesh, Rect r)
        {
            // Open L corners, never a gold frame around the dialogue's full perimeter.
            for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++)
            {
                float sx = x == 0 ? 1 : -1, sy = y == 0 ? 1 : -1;
                Vector2 at = new Vector2(x == 0 ? r.xMin + 8 : r.xMax - 8, y == 0 ? r.yMin + 8 : r.yMax - 8);
                Stroke(mesh, at, at + new Vector2(sx * 26, 0), 1.4f, AgedGold);
                Stroke(mesh, at, at + new Vector2(0, sy * 19), 1.4f, AgedGold);
                Vector2 inner = at + new Vector2(sx * 4, sy * 4);
                var muted = AgedGold; muted.a = .34f;
                Stroke(mesh, inner, inner + new Vector2(sx * 13, 0), .8f, muted);
                Stroke(mesh, inner, inner + new Vector2(0, sy * 10), .8f, muted);
            }
        }
        static void LineRect(VertexHelper mesh, Rect r, float width, Color tint)
        {
            Quad(mesh, new Rect(r.xMin, r.yMin, r.width, width), tint);
            Quad(mesh, new Rect(r.xMin, r.yMax - width, r.width, width), tint);
            Quad(mesh, new Rect(r.xMin, r.yMin, width, r.height), tint);
            Quad(mesh, new Rect(r.xMax - width, r.yMin, width, r.height), tint);
        }
        static void Quad(VertexHelper mesh, Rect r, Color tint)
        {
            if (r.width <= 0 || r.height <= 0) return;
            int n = mesh.currentVertCount; var color = (Color32)tint;
            mesh.AddVert(new Vector3(r.xMin, r.yMin, 0), color, Vector2.zero);
            mesh.AddVert(new Vector3(r.xMin, r.yMax, 0), color, Vector2.zero);
            mesh.AddVert(new Vector3(r.xMax, r.yMax, 0), color, Vector2.zero);
            mesh.AddVert(new Vector3(r.xMax, r.yMin, 0), color, Vector2.zero);
            mesh.AddTriangle(n, n + 1, n + 2); mesh.AddTriangle(n + 2, n + 3, n);
        }
        static void Stroke(VertexHelper mesh, Vector2 a, Vector2 b, float width, Color tint)
        {
            Vector2 normal = new Vector2(-(b - a).y, (b - a).x).normalized * width * .5f;
            int n = mesh.currentVertCount; var color = (Color32)tint;
            mesh.AddVert(a - normal, color, Vector2.zero); mesh.AddVert(a + normal, color, Vector2.zero);
            mesh.AddVert(b + normal, color, Vector2.zero); mesh.AddVert(b - normal, color, Vector2.zero);
            mesh.AddTriangle(n, n + 1, n + 2); mesh.AddTriangle(n + 2, n + 3, n);
        }

        public static RectTransform CreatePaper(UiKit kit, Transform parent, string name, float x, float y, float width, float height)
        {
            var rect = kit.Rect(parent, name, x, y, width, height);
            MemoVisualAssets.KeepAlive(rect.gameObject);
            // The large center is painted warm silk, with real jade rods and cloud embroidery outside text.
            var mount=kit.Rect(rect,"Dialogue cloud scroll mounting",-26,-18,width+52,height+36);
            var image=mount.gameObject.AddComponent<Image>();image.raycastTarget=false;
            mount.gameObject.AddComponent<DialogueBorrowedArt>().InitializeMount(image,ApprovedUiArt.Load("nov10/dialogue-scroll-mount"));
            for(int i=0;i<2;i++)
            {
                var tassel=kit.Rect(rect,"Dialogue jade tassel "+i,i==0?-24:width-8,height-14,30,85).gameObject.AddComponent<RawImage>();
                tassel.texture=ApprovedUiArt.Load("nov10/dialogue-jade-tassel");tassel.raycastTarget=false;
                if(i==1)tassel.uvRect=new Rect(1,0,-1,1);
            }
            return rect;
        }
        public static TextMeshProUGUI CreateNameSeal(UiKit kit, RectTransform parent, float x, float y, float maxWidth, float height)
        {
            var rect = kit.Rect(parent, "Dialogue cinnabar name seal", x, y, 160, height);
            var image = rect.gameObject.AddComponent<Image>(); image.raycastTarget = false;
            rect.gameObject.AddComponent<DialogueBorrowedArt>().Initialize(image, MemoVisualAssets.GetTexture("seal-cinnabar"), false);
            image.color = new Color(.73f, .50f, .46f, .82f);
            var name = kit.Label(rect, "", 16, 4, 128, height - 8, 32,
                new Color(.43f, .13f, .10f, 1), TextAlignmentOptions.Center, true);
            name.name = "Dialogue speaker"; name.richText = false; name.characterSpacing = 1;
            name.enableAutoSizing = false; name.textWrappingMode = TextWrappingModes.NoWrap;
            name.overflowMode = TextOverflowModes.Overflow;
            rect.gameObject.AddComponent<DialogueNameSealFit>().Configure(name, maxWidth);
            return name;
        }
    }

    /// <summary>Owns only the derived Sprite; source RGBA texture belongs to the existing art cache.</summary>
    public sealed class DialogueBorrowedArt : MonoBehaviour
    {
        Sprite owned;
        public void InitializeMount(Image image,Texture2D source,float sliceScale=1.6f)
        {
            if(source==null){image.enabled=false;return;}
            owned=Sprite.Create(source,new Rect(0,0,source.width,source.height),new Vector2(.5f,.5f),100,0,
                SpriteMeshType.FullRect,new Vector4(140,92,140,92));
            owned.name="Dialogue painted mounting slices";owned.hideFlags=HideFlags.DontSave;
            image.sprite=owned;image.type=Image.Type.Sliced;image.pixelsPerUnitMultiplier=sliceScale;image.raycastTarget=false;
        }
        public void Initialize(Image image, Texture2D source, bool paper)
        {
            if (source == null) { image.enabled = false; return; }
            Rect crop = paper ? new Rect(source.width * .19f, source.height * .25f, source.width * .62f, source.height * .5f)
                : new Rect(0, 0, source.width, source.height);
            Vector4 border = paper ? Vector4.zero : new Vector4(source.width * .14f, source.height * .14f, source.width * .14f, source.height * .14f);
            owned = Sprite.Create(source, crop, new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, border);
            owned.name = paper ? "Dialogue central paper fibres" : "Dialogue cinnabar seal slices";
            owned.hideFlags = HideFlags.DontSave;
            image.sprite = owned; image.type = paper ? Image.Type.Tiled : Image.Type.Sliced;
            image.fillCenter = true; image.preserveAspect = false;
        }
        void OnDestroy() { if (owned != null) Destroy(owned); owned = null; }
    }

    public sealed class DialogueNameSealFit : MonoBehaviour
    {
        TextMeshProUGUI label; float maxWidth; string prior;
        public void Configure(TextMeshProUGUI name, float limit) { label = name; maxWidth = limit; }
        void LateUpdate()
        {
            if (label == null || prior == label.text) return;
            prior = label.text;
            float width = Mathf.Clamp(Mathf.Ceil(label.GetPreferredValues(label.text, Mathf.Infinity, Mathf.Infinity).x) + 32, 120, maxWidth);
            var rect = (RectTransform)transform; rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
            label.rectTransform.sizeDelta = new Vector2(width - 32, label.rectTransform.sizeDelta.y);
        }
    }
}
