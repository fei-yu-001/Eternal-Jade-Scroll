using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // One owned UI material and one Sprite per command; never alters shared font/sprite materials.
    public sealed class JadeCommandView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        Image image; Button button; Func<bool> reduced; Material owned, fontMaterial; Sprite sprite;
        bool hover, pressed, selected; float phase;
        public bool HasArtwork => sprite != null;
        public bool IsAnimating => owned != null && !(reduced?.Invoke() ?? true);
        public void Configure(Image graphic, Button action, TMP_Text label, Func<bool> reduceMotion)
        {
            image = graphic; button = action; reduced = reduceMotion;
            var texture = ApprovedUiArt.Load("nov7/white-jade-command");
            if (texture != null)
            {
                sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f,.5f), 100);
                sprite.name = "White jade command — original aspect"; image.sprite = sprite;
                image.type = Image.Type.Simple; image.preserveAspect = true; image.color = Color.white;
            }
            var shader = Resources.Load<Shader>("Shaders/JadeCommandGlow");
            if (shader != null)
            {
                owned = new Material(shader) { name = "Jade command glow (owned)", hideFlags = HideFlags.DontSave };
                image.material = owned;
            }
            if (label.fontSharedMaterial != null)
            {
                fontMaterial = new Material(label.fontSharedMaterial) { hideFlags = HideFlags.DontSave };
                label.fontMaterial = fontMaterial; label.outlineColor = new Color(.67f,.49f,.21f,1); label.outlineWidth = .075f;
                fontMaterial.EnableKeyword("OUTLINE_ON"); label.extraPadding = true;
            }
            Refresh();
        }
        public void OnPointerEnter(PointerEventData data) { hover = true; Refresh(); }
        public void OnPointerExit(PointerEventData data) { hover = pressed = false; Refresh(); }
        public void OnPointerDown(PointerEventData data) { if (data.button == PointerEventData.InputButton.Left) pressed = true; Refresh(); }
        public void OnPointerUp(PointerEventData data) { pressed = false; Refresh(); }
        public void OnSelect(BaseEventData data) { selected = true; Refresh(); }
        public void OnDeselect(BaseEventData data) { selected = pressed = false; Refresh(); }
        void Update()
        {
            bool freeze = reduced?.Invoke() ?? true;
            if (!freeze) phase = Mathf.Repeat(phase + Time.unscaledDeltaTime / 3.8f, 1);
            if (owned != null) { owned.SetFloat("_Phase", freeze ? 0 : phase); owned.SetFloat("_Motion", freeze ? 0 : 1); }
            Refresh();
        }
        void Refresh()
        {
            if (image == null || button == null) return;
            bool enabled = button.IsInteractable();
            image.color = !enabled ? new Color(.68f,.67f,.61f,.7f) : pressed ? new Color(.86f,.90f,.82f,1) : Color.white;
            if (owned != null) owned.SetFloat("_Emphasis", enabled && (hover || selected) ? 1 : 0);
        }
        void OnDisable() { hover = pressed = selected = false; phase = 0; Refresh(); }
        void OnDestroy()
        {
            if (owned != null) Destroy(owned);
            if (fontMaterial != null) Destroy(fontMaterial);
            if (sprite != null) Destroy(sprite);
        }
    }
}
