using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // Local memoir origin control. Button owns selection input; this graphic only draws feedback.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class MemoOriginPlaque : MaskableGraphic, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        const float BreathSeconds = 5.6f;
        static readonly Color Wood = UiKit.Hex("49443B"), WoodEdge = UiKit.Hex("81745E");
        static readonly Color Ivory = UiKit.Hex("EEEBDD"), Ink = UiKit.Hex("352D27");
        static readonly Color WarmGold = UiKit.Hex("B7A16F");
        Texture2D faceTexture;
        public override Texture mainTexture => faceTexture != null ? faceTexture : Texture2D.whiteTexture;
        Button owner;
        TMP_Text caption;
        Func<bool> reduceMotion;
        bool chosen, hovered, focused, pressed, disabled, reduced = true;
        float phase, glow = -1;

        public bool ChosenForShot => chosen;
        public bool ReducedMotionForShot => reduced;
        public bool BreathingForShot => isActiveAndEnabled && chosen && !disabled && !reduced;
        public float GlowForShot => glow;
        public TMP_Text CaptionForShot => caption;

        public static Button Create(UiKit ui, Transform parent, string name, string text,
            float x, float y, float width, float height, UnityAction action,
            Func<bool> reducedMotion, bool selected, Action click)
        {
            var rect = ui.Rect(parent, name, x, y, width, height);
            var plate = rect.gameObject.AddComponent<MemoOriginPlaque>();
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = plate;
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            var mount = MemoVisualAssets.Rect(rect, "Origin carved cloud corners").gameObject.AddComponent<MemoMountGraphic>();
            mount.Configure(MemoMountGraphic.Form.Command, selected);
            var label = ui.Label(rect, text, 22, 5, width - 44, height - 10, 27,
                selected ? Ink : Ivory, TextAlignmentOptions.Center, true);
            label.name = name + " label";
            label.richText = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.enableAutoSizing = false;
            label.overflowMode = TextOverflowModes.Overflow;
            label.extraPadding = false; label.margin = new Vector4(2, 2, 2, 2);
            SdfInkWeight.Apply(label);
            button.onClick.AddListener(() => click?.Invoke());
            button.onClick.AddListener(action);
            plate.owner = button; plate.caption = label;
            plate.reduceMotion = reducedMotion; plate.chosen = selected;
            plate.faceTexture = Resources.Load<Texture2D>("Art/UI/Nov8/moon-jade-command");
            plate.SetMaterialDirty();
            plate.Refresh(true);
            return button;
        }

        protected override void Awake() { base.Awake(); raycastTarget = true; }
        protected override void OnEnable() { base.OnEnable(); phase = 0; Refresh(true); }
        protected override void OnDisable()
        {
            hovered = focused = pressed = false; phase = 0; glow = -1;
            base.OnDisable();
        }
        protected override void OnDestroy()
        {
            reduceMotion = null; owner = null; caption = null; faceTexture = null;
            base.OnDestroy(); // Graphic releases its normal UI stencil/mask state.
        }
        void LateUpdate()
        {
            bool nextReduced = reduceMotion?.Invoke() ?? true;
            if (nextReduced != reduced) { reduced = nextReduced; phase = 0; }
            if (chosen && owner != null && owner.IsActive() && owner.IsInteractable() && !reduced)
                phase = Mathf.Repeat(phase + Time.unscaledDeltaTime / BreathSeconds, 1f);
            Refresh(false);
        }
        public void OnPointerEnter(PointerEventData data) { hovered = true; Refresh(true); }
        public void OnPointerExit(PointerEventData data) { hovered = pressed = false; Refresh(true); }
        public void OnPointerDown(PointerEventData data)
        {
            if (data.button == PointerEventData.InputButton.Left) { pressed = true; Refresh(true); }
        }
        public void OnPointerUp(PointerEventData data) { pressed = false; Refresh(true); }
        public void OnSelect(BaseEventData data) { focused = true; Refresh(true); }
        public void OnDeselect(BaseEventData data) { focused = pressed = false; Refresh(true); }
        void Refresh(bool force)
        {
            if (owner == null || caption == null) return;
            bool nextDisabled = !owner.IsActive() || !owner.IsInteractable();
            bool nextReduced = reduceMotion?.Invoke() ?? true;
            if (reduced != nextReduced) { reduced = nextReduced; phase = 0; force = true; }
            if (disabled != nextDisabled) { disabled = nextDisabled; force = true; }
            float breath = reduced ? .5f : .5f - .5f * Mathf.Cos(phase * Mathf.PI * 2);
            float nextGlow = disabled ? 0 : chosen ? .32f + breath * .12f : 0;
            if (!force && Mathf.Abs(nextGlow - glow) < .001f) return;
            glow = nextGlow;
            caption.color = disabled ? new Color(.57f, .54f, .48f, 1)
                : chosen ? Ink : hovered || focused ? Color.Lerp(Ivory, Color.white, .16f) : Ivory;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); var bounds = rectTransform.rect;
            if (bounds.width < 24 || bounds.height < 20) return;
            // Every layer stays inside the original hit rect. No overlay can steal the pointer.
            float opacity = disabled ? .48f : 1;
            var body = Inset(bounds, 4);
            Fill(mesh, body, 5, Tint(Wood, .96f * opacity));
            Ring(mesh, Inset(bounds, 2), 7, 1.8f, Tint(new Color(.13f, .11f, .09f), .45f * opacity));
            Line(mesh, new Vector2(body.xMin + 8, body.yMax - 1), new Vector2(body.xMax - 8, body.yMax - 1), 1.1f, Tint(WoodEdge, .88f * opacity));
            Line(mesh, new Vector2(body.xMin + 8, body.yMin + 1), new Vector2(body.xMax - 8, body.yMin + 1), 1.8f, Tint(new Color(.13f, .11f, .09f), .65f * opacity));
            if (chosen)
            {
                Fill(mesh, Inset(body, 2.8f), 4, Tint(new Color(.84f, .90f, .81f), .98f * opacity));
                Fill(mesh, Inset(body, 4f), 3, Tint(new Color(.94f, .96f, .85f), .25f * opacity));
                Ring(mesh, Inset(bounds, 1.2f), 7, .8f, Tint(WarmGold, glow * .18f));
                Ring(mesh, Inset(bounds, 2.6f), 6, .8f, Tint(WarmGold, glow * .32f));
                Ring(mesh, Inset(body, 1.4f), 4, .85f, Tint(WarmGold, glow + .12f));
                // Small fixed jade end marks identify the chosen origin even when motion is reduced.
                Mark(mesh, new Vector2(body.xMin + 10, body.center.y), 1.8f, Tint(WarmGold, .60f * opacity));
                Mark(mesh, new Vector2(body.xMax - 10, body.center.y), 1.8f, Tint(WarmGold, .60f * opacity));
            }
            else
            {
                Ring(mesh, Inset(body, 1.2f), 4, .7f, Tint(WoodEdge, .60f * opacity));
                // Quiet end-grain, outside the caption inset; no sweep or animated glints.
                for (int i = 0; i < 3; i++)
                {
                    float y = body.yMin + 9 + i * 9;
                    Line(mesh, new Vector2(body.xMin + 6, y), new Vector2(body.xMin + 15, y + .7f), .65f, Tint(Ivory, .10f * opacity));
                    Line(mesh, new Vector2(body.xMax - 15, y), new Vector2(body.xMax - 6, y + .7f), .65f, Tint(Ivory, .10f * opacity));
                }
            }
            if (!disabled && (focused || hovered))
                Ring(mesh, Inset(bounds, .5f), 7.5f, focused ? 1.2f : .7f,
                    Tint(chosen ? WarmGold : WoodEdge, focused ? .82f : .48f));
            if (!disabled && pressed && (hovered || focused))
                Fill(mesh, Inset(body, 2), 3.5f, new Color(.15f, .12f, .08f, .09f));
            // Use existing jade grain with a muted agarwood tint; never resurrect the red lacquer roller.
            var region = new Rect(.25f, .28f, .50f, .44f);
            var vertex = UIVertex.simpleVert;
            for (int i = 0; i < mesh.currentVertCount; i++)
            {
                mesh.PopulateUIVertex(ref vertex, i);
                vertex.uv0 = new Vector4(region.x + Mathf.InverseLerp(bounds.xMin, bounds.xMax, vertex.position.x) * region.width,
                    region.y + Mathf.InverseLerp(bounds.yMin, bounds.yMax, vertex.position.y) * region.height, 0, 0);
                mesh.SetUIVertex(vertex, i);
            }
        }

        static Color Tint(Color value, float alpha) { value.a = alpha; return value; }
        static Rect Inset(Rect r, float value) => Rect.MinMaxRect(r.xMin + value, r.yMin + value, r.xMax - value, r.yMax - value);
        static Vector2 Corner(Rect r, float cut, int i)
        {
            cut = Mathf.Min(cut, Mathf.Min(r.width, r.height) * .25f);
            switch (i & 7)
            {
                case 0: return new Vector2(r.xMin + cut, r.yMin);
                case 1: return new Vector2(r.xMax - cut, r.yMin);
                case 2: return new Vector2(r.xMax, r.yMin + cut);
                case 3: return new Vector2(r.xMax, r.yMax - cut);
                case 4: return new Vector2(r.xMax - cut, r.yMax);
                case 5: return new Vector2(r.xMin + cut, r.yMax);
                case 6: return new Vector2(r.xMin, r.yMax - cut);
                default: return new Vector2(r.xMin, r.yMin + cut);
            }
        }
        static void Fill(VertexHelper mesh, Rect r, float cut, Color tint)
        {
            int start = mesh.currentVertCount;
            mesh.AddVert(r.center, tint, Vector2.zero);
            for (int i = 0; i < 8; i++) mesh.AddVert(Corner(r, cut, i), tint, Vector2.zero);
            for (int i = 0; i < 8; i++) mesh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % 8);
        }
        static void Ring(VertexHelper mesh, Rect r, float cut, float thickness, Color tint)
        {
            int start = mesh.currentVertCount; var inner = Inset(r, thickness);
            for (int i = 0; i < 8; i++)
            {
                mesh.AddVert(Corner(r, cut, i), tint, Vector2.zero);
                mesh.AddVert(Corner(inner, Mathf.Max(0, cut - thickness), i), tint, Vector2.zero);
            }
            for (int i = 0; i < 8; i++)
            {
                int a = start + i * 2, b = start + ((i + 1) % 8) * 2;
                mesh.AddTriangle(a, b, a + 1); mesh.AddTriangle(a + 1, b, b + 1);
            }
        }
        static void Line(VertexHelper mesh, Vector2 a, Vector2 b, float width, Color tint)
        {
            var normal = new Vector2(-(b - a).y, (b - a).x).normalized * width * .5f;
            int start = mesh.currentVertCount;
            mesh.AddVert(a - normal, tint, Vector2.zero); mesh.AddVert(a + normal, tint, Vector2.zero);
            mesh.AddVert(b + normal, tint, Vector2.zero); mesh.AddVert(b - normal, tint, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
        }
        static void Mark(VertexHelper mesh, Vector2 center, float radius, Color tint)
        {
            int start = mesh.currentVertCount;
            mesh.AddVert(center + Vector2.down * radius, tint, Vector2.zero);
            mesh.AddVert(center + Vector2.right * radius, tint, Vector2.zero);
            mesh.AddVert(center + Vector2.up * radius, tint, Vector2.zero);
            mesh.AddVert(center + Vector2.left * radius, tint, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
