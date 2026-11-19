using TMPro;

namespace TwelveJade.Presentation
{
    // Keep the same wrapping behavior on Unity 6 (TMP 4) and Tuanjie 2022 (TMP 3).
    public static class TextWrapCompat
    {
        public static void SetWrapping(TMP_Text text, bool enabled)
        {
#if UNITY_6000_0_OR_NEWER
            text.textWrappingMode = enabled ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
#else
            text.enableWordWrapping = enabled;
#endif
        }
    }
}
