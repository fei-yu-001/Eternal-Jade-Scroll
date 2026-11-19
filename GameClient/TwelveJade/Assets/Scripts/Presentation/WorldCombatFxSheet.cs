using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    /// <summary>
    /// 复用现有特效表（Art/Fx/Nov11 的四格横排）的实心表现层，垫在折线之上补清晰度。
    /// 纹理只在创建时缓存一次,这里只翻 uv 与透明度,不逐帧生纹理。
    /// </summary>
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class WorldCombatFxSheet : WorldCombatFxGraphic
    {
        RawImage image;
        Texture2D sheet;
        int from, count = 1;
        float scale = 120, lift = 36;
        Vector2 travelFrom, travelTo; bool travelling;
        public void Travel(Vector2 origin, Vector2 target) { travelFrom = origin; travelTo = target; travelling = true; }

        public void Attach()
        {
            if (image != null) return;
            var rect = (RectTransform)new GameObject("Authored frame", typeof(RectTransform)).transform;
            rect.SetParent(transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0);
            rect.pivot = new Vector2(.5f, 0);
            image = rect.gameObject.AddComponent<RawImage>();
            image.raycastTarget = false;
            image.enabled = false;
        }

        public void SetSheet(Texture2D texture, int firstFrame, int frames, float size)
        {
            travelling = false;
            sheet = texture; from = Mathf.Clamp(firstFrame, 0, 3); count = Mathf.Clamp(frames, 1, 4 - from); scale = size;
        }

        protected override void OnPopulateMesh(VertexHelper v) => v.Clear();

        protected override void Sync()
        {
            if (image == null) return;
            bool live = enabled && sheet != null && t > 0 && t < 1;
            image.enabled = live;
            if (!live) return;
            if (travelling) rectTransform.anchoredPosition = Vector2.Lerp(travelFrom, travelTo, t);
            int frame = from + Mathf.Min(count - 1, Mathf.FloorToInt(t * count));
            bool mirrored = transform.localEulerAngles.z > 90 && transform.localEulerAngles.z < 270;
            image.texture = sheet;
            image.uvRect = mirrored ? new Rect((frame + 1) / 4f, 0, -.25f, 1) : new Rect(frame / 4f, 0, .25f, 1);
            var rect = image.rectTransform;
            rect.sizeDelta = new Vector2(scale, scale);
            // Keep the artwork upright and above the ground for every attack direction.
            // Rotating the parent's lift used to put leftward casts below the feet.
            var upright = Quaternion.Inverse(rectTransform.localRotation);
            rect.localRotation = upright;
            rect.anchoredPosition = upright * new Vector3(0, lift * (1 - t * .25f), 0);
            image.color = new Color(1, 1, 1, Mathf.Clamp01(t * 6f) * (1 - t) * .92f);
        }
    }
}
