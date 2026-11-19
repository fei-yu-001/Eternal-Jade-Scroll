using TMPro;
using UnityEngine;
namespace TwelveJade.Presentation
{
    // A local SDF weight keeps the regular glyph lookup, including the ellipsis character.
    [DisallowMultipleComponent]
    public sealed class SdfInkWeight : MonoBehaviour
    {
        Material owned;
        public static void Apply(TMP_Text label)
        {
            if (label == null || label.fontSharedMaterial == null) return;
            var owner = label.gameObject.GetComponent<SdfInkWeight>() ?? label.gameObject.AddComponent<SdfInkWeight>();
            owner.Configure(label);
        }
        void Configure(TMP_Text label)
        {
            if (owned != null) return;
            owned = new Material(label.fontSharedMaterial) { name = "Readable ink SDF (owned)", hideFlags = HideFlags.DontSave };
            if (owned.HasProperty("_WeightNormal") && owned.HasProperty("_WeightBold"))
                owned.SetFloat("_WeightNormal", owned.GetFloat("_WeightBold"));
            label.fontStyle &= ~FontStyles.Bold;
            label.fontMaterial = owned;
            // Keep the original label geometry; SDF weight already supplies its own padding.
            label.extraPadding = false;
            label.UpdateMeshPadding();
        }
        void OnDestroy() { if (owned != null) Destroy(owned); }
    }
}
