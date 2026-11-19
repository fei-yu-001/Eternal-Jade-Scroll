using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // White RGB with exactly the authored alpha, rendered by ordinary Maskable RawImages.
    // No special unmasked shader, source mutation, Resources asset or per-hit GPU readback.
    [DefaultExecutionOrder(240)]
    public sealed class ActionCombatHitFlash : MonoBehaviour
    {
        readonly Dictionary<Texture, Texture2D> whites = new Dictionary<Texture, Texture2D>();
        readonly List<RawImage> sources = new List<RawImage>(), overlays = new List<RawImage>();
        float strength;
        public bool HasSilhouette => whites.Count > 0 && overlays.Count > 0;
        public float Strength
        {
            get
            {
                float visible = 0;
                foreach (var overlay in overlays)
                    if (overlay != null && overlay.enabled && overlay.gameObject.activeInHierarchy)
                        visible = Mathf.Max(visible, overlay.color.a);
                return visible;
            }
        }
        public void Configure(Transform actor, params Texture2D[] textures)
        {
            foreach (var texture in textures) if (texture != null && !whites.ContainsKey(texture)) whites.Add(texture, WhiteAlpha(texture));
            foreach (var source in actor.GetComponentsInChildren<RawImage>(true))
            {
                if (source.GetComponentInParent<ActionCombatHitFlash>() != this && source.transform != transform) continue;
                var go = new GameObject("Action hit white alpha", typeof(RectTransform), typeof(CanvasRenderer));
                var r = (RectTransform)go.transform; r.SetParent(source.transform, false);
                r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
                var image = go.AddComponent<RawImage>(); image.raycastTarget = false; image.enabled = false;
                sources.Add(source); overlays.Add(image);
            }
        }
        public void SetStrength(float value) { strength = Mathf.Clamp01(value); Sync(); }
        void LateUpdate() { Sync(); }
        void Sync()
        {
            for (int i = 0; i < sources.Count; i++)
            {
                var source = sources[i]; var overlay = overlays[i];
                if (source == null || overlay == null) continue;
                Texture2D white = null;
                bool visible = strength > 0 && source.enabled && source.texture != null && whites.TryGetValue(source.texture, out white);
                overlay.enabled = visible;
                if (!visible) continue;
                overlay.texture = white; overlay.uvRect = source.uvRect;
                overlay.color = new Color(1, 1, 1, strength * source.color.a);
            }
        }
        static Texture2D WhiteAlpha(Texture2D texture)
        {
            Texture2D result = null; var previous = RenderTexture.active;
            var target = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
            try
            {
                Graphics.Blit(texture, target); RenderTexture.active = target;
                result = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                result.name = "Action flash alpha " + texture.name;
                result.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                var pixels = result.GetPixels32();
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, pixels[i].a);
                result.SetPixels32(pixels); result.Apply(false, true); return result;
            }
            catch { if (result != null) Destroy(result); throw; }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); }
        }
        void OnDisable() { strength = 0; Sync(); }
        void OnDestroy() { foreach (var tex in whites.Values) if (tex != null) Destroy(tex); whites.Clear(); sources.Clear(); overlays.Clear(); }
    }
}
