using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // Borrow textures from Resources; own only FullRect sprites. A backdrop lease keeps the
    // cache warm across page changes. The final consumer destroys sprites, never source art.
    [DisallowMultipleComponent]
    public sealed class MemoVisualAssets : MonoBehaviour
    {
        public const string Prefix = "Art/UI/Nov6/";
        public static readonly string[] RequiredTextures = {
            "paper-panel-translucent", "paper-panel", "button-gold", "icon-compass", "icon-document", "icon-sword",
            "scroll-whole", "scroll-header", "scroll-body", "scroll-footer", "seal-cinnabar", "icon-coins", "icon-spirit", "jade-pendant",
            "Posters/farmer-male", "Posters/farmer-female", "Posters/merchant-male", "Posters/merchant-female",
            "Posters/traveller-male", "Posters/traveller-female" };
        static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        static int leases;
        bool retained;
        public static int CachedSpriteCount => sprites.Count;

        public static void KeepAlive(GameObject owner)
        {
            var lease = owner.GetComponent<MemoVisualAssets>() ?? owner.AddComponent<MemoVisualAssets>();
            if (lease.retained) return;
            lease.retained = true; leases++;
        }

        public static Texture2D GetTexture(string name)
        {
            if (textures.TryGetValue(name, out var texture)) return texture;
            string id=name=="scroll-body"?"nov8/xuan-ink-silk":name=="scroll-header"||name=="scroll-footer"?"nov8/white-jade-roller":
                name=="paper-panel-translucent"?"nov8/moon-xuan-paper-translucent":name=="paper-panel"?"nov8/moon-xuan-paper":name=="button-gold"?"nov8/moon-jade-command":"memo/"+name;
            texture = ApprovedUiArt.Load(id);
            textures.Add(name, texture);
            if (texture == null) Debug.LogError("Missing approved UI texture: " + Prefix + name);
            return texture;
        }

        public static Sprite GetSprite(string name)
        {
            if (sprites.TryGetValue(name, out var cached)) return cached;
            var texture = GetTexture(name);
            if (texture == null) return null;
            var border = name.StartsWith("paper-panel", StringComparison.Ordinal) ? new Vector4(92, 78, 92, 78) :
                name == "button-gold" ? new Vector4(112, 36, 112, 36) :
                name == "scroll-body" ? new Vector4(18, 30, 18, 30) : Vector4.zero;
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, border);
            sprite.name = "Nov6 " + name; sprite.hideFlags = HideFlags.DontSave;
            sprites.Add(name, sprite);
            return sprite;
        }

        // A crop of the actual upper gold rail, used for the moving glints; no generated bitmap or material.
        public static Sprite GoldThread()
        {
            const string key = "gold-thread-crop";
            if (sprites.TryGetValue(key, out var cached)) return cached;
            var texture = GetTexture("paper-panel");
            if (texture == null) return null;
            var sprite = Sprite.Create(texture, new Rect(100, 294, 335, 10), new Vector2(.5f, .5f),
                100, 0, SpriteMeshType.FullRect);
            sprite.name = "Nov6 gold thread (100,294,335,10)"; sprite.hideFlags = HideFlags.DontSave;
            sprites.Add(key, sprite);
            return sprite;
        }

        public static RectTransform Rect(Transform parent, string name)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        public static Image Art(Transform parent, string name, string resource, bool borderOnly = false)
        {
            KeepAlive(parent.gameObject);
            var image = Rect(parent, name).gameObject.AddComponent<Image>();
            image.sprite = GetSprite(resource); image.raycastTarget = false;
            image.type = image.sprite != null && image.sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            image.fillCenter = !borderOnly; image.preserveAspect = image.type == Image.Type.Simple;
            return image;
        }

        void OnDestroy()
        {
            if (!retained) return;
            retained = false;
            if (--leases > 0) return;
            foreach (var sprite in sprites.Values) if (sprite != null) Destroy(sprite);
            sprites.Clear(); textures.Clear(); leases = 0;
        }
    }
}
