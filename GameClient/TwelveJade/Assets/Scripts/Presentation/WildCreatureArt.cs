namespace TwelveJade.Presentation
{
    // Stable art keys for wilderness creatures. Integration checks the path before
    // loading and keeps the encounter fallback when a key is empty or missing.
    public static class WildCreatureArt
    {
        const string Root = "Art/Characters/Nov19/";

        public static string Stand(string enemyId)
        {
            switch (enemyId)
            {
                case "boar-demon": return Root + "boar-demon-stand";
                case "nether-wolf": return Root + "nether-wolf-stand";
                case "stone-mandrill": return Root + "stone-mandrill-stand";
                case "qingshi-mountain-lord": return Root + "qingshi-mountain-lord-stand";
                default: return "";
            }
        }

        public static string Walk(string enemyId)
        {
            // Empty until a reviewed four-frame sheet exists; avoids old-frame face swaps.
            return "";
        }

        public static string Strikes(string enemyId)
        {
            // Empty until reviewed strike sheets exist.
            return "";
        }

        public static float Height(string enemyId, float fallback)
        {
            switch (enemyId)
            {
                case "boar-demon": return 160f;
                case "nether-wolf": return 190f;
                case "stone-mandrill": return 235f;
                case "qingshi-mountain-lord": return 560f;
                default: return fallback;
            }
        }

        public static float FootRatio(string enemyId)
        {
            switch (enemyId)
            {
                case "boar-demon":
                case "nether-wolf":
                case "stone-mandrill":
                case "qingshi-mountain-lord":
                    return 0.9453125f;
                default:
                    return 0.9453125f;
            }
        }
    }
}
