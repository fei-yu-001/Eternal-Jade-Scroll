using UnityEngine;

namespace TwelveJade.Presentation
{
    // Borrow offline-derived, registered artwork. Imported textures belong to the resource catalogue.
    [DisallowMultipleComponent]
    public sealed class MemoScrollArtwork : MonoBehaviour
    {
        static readonly string[] ids = {
            "nov10/memo-silk-premium", "nov10/memo-jade-roller-premium", "nov10/memo-pendant-premium", "nov10/memo-cord-premium" };
        static readonly Texture2D[] textures = new Texture2D[4];
        static int leases;
        bool retained;
        public static void Retain(GameObject owner)
        {
            var lease = owner.GetComponent<MemoScrollArtwork>() ?? owner.AddComponent<MemoScrollArtwork>();
            if (lease.retained) return;
            lease.retained = true; leases++;
        }
        public static Texture2D Get(int part)
        {
            if (textures[part] == null) textures[part] = ApprovedUiArt.Load(ids[part]);
            return textures[part];
        }
        void OnDestroy()
        {
            if (!retained) return;
            retained = false;
            if (--leases > 0) return;
            // Clear only our references; never destroy a borrowed catalogue texture.
            for (int i = 0; i < textures.Length; i++) textures[i] = null;
            leases = 0;
        }
    }
}
