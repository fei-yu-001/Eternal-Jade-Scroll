using System;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // Preserve the original copper collar and contiguous suspension; pivot jade and thread at their real joins.
    [DisallowMultipleComponent]
    public sealed class MemoPendantMotion : MonoBehaviour
    {
        readonly Sprite[] owned = new Sprite[2];
        MemoScrollArtGraphic roller;
        Image jade, tassel;
        RectTransform pendantPivot, tasselPivot;
        Func<bool> reduceMotion;
        float phase;
        bool reduced = true, vacant;
        public static int OwnedSpriteCount { get; private set; }
        public float PendantAngleForShot => pendantPivot == null ? 0 : Mathf.DeltaAngle(0, pendantPivot.localEulerAngles.z);
        public float TasselAngleForShot => tasselPivot == null ? 0 : Mathf.DeltaAngle(0, tasselPivot.localEulerAngles.z);
        public bool IsAnimating => !reduced && isActiveAndEnabled;
        public void Configure(Func<bool> reducedMotion)
        {
            reduceMotion = reducedMotion;
            if (roller != null) return;
            MemoVisualAssets.KeepAlive(gameObject);
            MemoScrollArtwork.Retain(gameObject);
            var texture = MemoScrollArtwork.Get(2);
            if (texture == null) return;
            owned[0] = Crop(texture, 112, 20, 32, 65, "Jade pendant and copper suspension");
            owned[1] = Crop(texture, 104, 85, 46, 90, "Naturally hanging tassel");
            roller = MemoVisualAssets.Rect(transform, "Scroll original carved copper lower roller").gameObject.AddComponent<MemoScrollArtGraphic>();
            roller.Configure(MemoScrollArtGraphic.Part.Footer);
            jade = Part(transform, "White jade moving pendant", owned[0]); pendantPivot = jade.rectTransform;
            tassel = Part(pendantPivot, "Emerald independent tassel", owned[1]); tasselPivot = tassel.rectTransform;
            reduced = reduceMotion?.Invoke() ?? true;
            phase = MemoScrollGeometry.Wrap(transform.parent.GetSiblingIndex() * .19f);
            Layout(); SetVacant(vacant); Apply();
        }
        static Image Part(Transform parent, string name, Sprite sprite)
        {
            var image = MemoVisualAssets.Rect(parent, name).gameObject.AddComponent<Image>();
            image.sprite = sprite; image.raycastTarget = false; image.preserveAspect = true;
            return image;
        }
        static Sprite Crop(Texture2D texture, float x, float top, float width, float height, string name)
        {
            float sx = texture.width / 254f, sy = texture.height / 182f;
            var sprite = Sprite.Create(texture, new Rect(x * sx, (182 - top - height) * sy, width * sx, height * sy),
                new Vector2(.5f, 1), 100, 0, SpriteMeshType.FullRect);
            sprite.name = "Memo C " + name; sprite.hideFlags = HideFlags.DontSave; OwnedSpriteCount++; return sprite;
        }
        public void SetVacant(bool value)
        {
            vacant = value; float alpha = value ? .76f : 1;
            if (jade != null) jade.color = new Color(1, 1, 1, alpha);
            // The generated jade and sage silk retain their authored material colour.
            if (tassel != null) tassel.color = new Color(1, 1, 1, alpha);
        }
        void Layout()
        {
            if (roller == null || pendantPivot == null || tasselPivot == null) return;
            var r = ((RectTransform)transform).rect;
            float scale = (r.width - 24) / 254f;
            Place(roller.rectTransform, r.width * .5f, 0, r.width, 14);
            Place(pendantPivot, r.width * .5f, 12f, 32 * scale, 65 * scale);
            Place(tasselPivot, 16 * scale, 63 * scale, 46 * scale, 90 * scale);
        }
        static void Place(RectTransform rect, float centreX, float top, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(.5f, 1);
            rect.anchoredPosition = new Vector2(centreX, -top); rect.sizeDelta = new Vector2(width, height);
        }
        void Apply()
        {
            if (pendantPivot == null) return;
            pendantPivot.localRotation = Quaternion.Euler(0, 0, MemoScrollGeometry.PendantAngle(phase, reduced));
            tasselPivot.localRotation = Quaternion.Euler(0, 0, MemoScrollGeometry.TasselAngle(phase, reduced));
        }
        void Update()
        {
            if (pendantPivot == null) return;
            bool next = reduceMotion?.Invoke() ?? true;
            if (next != reduced) { reduced = next; Apply(); }
            if (reduced) return;
            phase = MemoScrollGeometry.Step(phase, Time.unscaledDeltaTime, MemoScrollGeometry.PendantSeconds, false); Apply();
        }
        void OnDisable() { if (pendantPivot != null) pendantPivot.localRotation = Quaternion.identity; if (tasselPivot != null) tasselPivot.localRotation = Quaternion.identity; }
        void OnEnable() { reduced = reduceMotion?.Invoke() ?? true; Apply(); }
        void OnRectTransformDimensionsChange() { Layout(); }
        void OnDestroy()
        {
            reduceMotion = null;
            for (int i = 0; i < owned.Length; i++)
                if (owned[i] != null) { Destroy(owned[i]); owned[i] = null; OwnedSpriteCount--; }
        }
    }
}
