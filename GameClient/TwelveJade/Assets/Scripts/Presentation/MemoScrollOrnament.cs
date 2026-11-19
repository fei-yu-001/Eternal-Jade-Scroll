using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace TwelveJade.Presentation
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MemoScrollOrnament : Image, ISelectHandler, IDeselectHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public enum Surface { Paper, HangingScroll }
        RectTransform header, body, footer, hanger;
        ScrollSelectAura aura;
        MemoPendantMotion pendant;
        MemoScrollAuraGraphic focusCorners;
        bool hovered, focused;
        public bool IsAnimating => aura != null && aura.IsAnimating;
        public float ShimmerPhase => aura == null ? 0 : aura.Phase;
        public bool Selected => aura != null && aura.Selected;
        public ScrollSelectAura SelectionAura => aura;
        public MemoPendantMotion PendantForShot => pendant;
        public bool FocusVisibleForShot => focusCorners != null && focusCorners.gameObject.activeInHierarchy;

        public void Configure(Surface style, bool picked, Func<bool> reducedMotion)
        {
            MemoVisualAssets.KeepAlive(gameObject);
            raycastTarget = style == Surface.HangingScroll;
            color = Color.clear; sprite = null;
            if (style == Surface.Paper)
            {
                if (transform.Find("Memo translucent silk") == null)
                {
                    var silk = MemoVisualAssets.Rect(transform, "Memo translucent silk");
                    silk.gameObject.AddComponent<MenuMemoSurfaceGraphic>().Configure(MenuMemoSurfaceGraphic.Surface.Dossier);
                    MemoVisualAssets.Rect(transform, "Memo ink mounting").gameObject.AddComponent<MemoMountGraphic>()
                        .Configure(MemoMountGraphic.Form.Dossier);
                }
                return;
            }
            if (header == null)
            {
                hanger = MemoVisualAssets.Rect(transform, "Scroll copper sky hook and jade ring");
                hanger.gameObject.AddComponent<MemoScrollArtGraphic>().Configure(MemoScrollArtGraphic.Part.Hanger);
                body = MemoVisualAssets.Rect(transform, "Scroll complete cloud brocade and warm silk");
                body.gameObject.AddComponent<MemoScrollArtGraphic>().Configure(MemoScrollArtGraphic.Part.Body);
                header = MemoVisualAssets.Rect(transform, "Scroll carved copper and dark jade header");
                header.gameObject.AddComponent<MemoScrollArtGraphic>().Configure(MemoScrollArtGraphic.Part.Header);
                footer = MemoVisualAssets.Rect(transform, "Scroll jade tassel footer");
                pendant = footer.gameObject.AddComponent<MemoPendantMotion>(); pendant.Configure(reducedMotion);
                aura = MemoVisualAssets.Rect(transform, "Scroll selected aura").gameObject.AddComponent<ScrollSelectAura>();
                aura.Configure(reducedMotion);
                focusCorners = MemoVisualAssets.Rect(transform, "Scroll keyboard focus corners").gameObject.AddComponent<MemoScrollAuraGraphic>();
                focusCorners.ConfigureFocus(); RefreshFocus();
            }
            LayoutScroll(); SetSelected(picked);
        }
        public void SetSelected(bool picked) { aura?.SetSelected(picked); RefreshFocus(); }
        public void SetVacant(bool vacant) => pendant?.SetVacant(vacant);
        public void OnSelect(BaseEventData data) { focused = true; RefreshFocus(); }
        public void OnDeselect(BaseEventData data) { focused = false; RefreshFocus(); }
        public void OnPointerEnter(PointerEventData data) { hovered = true; RefreshFocus(); }
        public void OnPointerExit(PointerEventData data) { hovered = false; RefreshFocus(); }
        void RefreshFocus() { if (focusCorners != null) focusCorners.gameObject.SetActive((hovered || focused) && !Selected); }
        protected override void OnDisable() { base.OnDisable(); hovered = focused = false; RefreshFocus(); }
        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            if (header != null) LayoutScroll();
        }
        void LayoutScroll()
        {
            if (body == null || header == null || footer == null || aura == null) return;
            float width = rectTransform.rect.width, height = rectTransform.rect.height;
            if (width <= 0 || height <= 0) return;
            const float bodyTop = 56;
            float footerTop = height - 106, bodyWidth = width - 14, bodyHeight = footerTop + 10 - bodyTop;
            Place(hanger, 7, 0, width - 14, 44);
            Place(body, 7, bodyTop, bodyWidth, bodyHeight);
            Place(header, -5, 44, width + 10, 14);
            Place(footer, -5, footerTop, width + 10, 96);
            float padding = MemoScrollGeometry.AuraPadding;
            // Align the painted reference silk edges (70..194, 100..550) to this scroll body.
            float sx = bodyWidth / 124f, sy = bodyHeight / 450f;
            Place((RectTransform)aura.transform, 7 - 24 * sx, bodyTop - 78 * sy, 172 * sx, 552 * sy);
            if (focusCorners != null) Place(focusCorners.rectTransform, 7 - padding, bodyTop - padding, bodyWidth + padding * 2, bodyHeight + padding * 2);
        }
        static void Place(RectTransform rect, float x, float y, float w, float h)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(w, h);
        }
    }
}
