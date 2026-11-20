namespace TwelveJade.Presentation
{
    // Alpha >= 32 bounds in the approved Nov7 standing / Nov10 strike PNGs (top-origin,
    // bottom exclusive). Tools/ActionPoseChecks remeasures the source assets, so replacing
    // art cannot silently leave this registration stale. No CPU-readable textures needed.
    public static class ActionPoseGeometry
    {
        public const int StrikeCellPixels = 576, StrikeFrameCount = 3;
        public const float StrikeBodyPixels = 450f, StrikeFootFromBottom = 28f;

        public readonly struct Bounds
        {
            public readonly int CanvasHeight, Top, Bottom;
            public Bounds(int height, int top, int bottom)
            { CanvasHeight = height; Top = top; Bottom = bottom; }
            public float BodyFraction => (Bottom - Top) / (float)CanvasHeight;
            public float FootFraction => (CanvasHeight - Bottom) / (float)CanvasHeight;
        }

        public readonly struct Layout
        {
            public readonly float Height, PivotY;
            public Layout(float height, float pivotY) { Height = height; PivotY = pivotY; }
        }

        public static Bounds StandingBounds(string textureName)
        {
            switch (textureName)
            {
                case "hero-male-front": return new Bounds(512, 26, 508);
                case "hero-male-side": return new Bounds(512, 28, 512);
                case "hero-male-back": return new Bounds(512, 35, 512);
                case "hero-female-front": return new Bounds(512, 33, 512);
                case "hero-female-side":
                case "hero-female-back": return new Bounds(512, 27, 512);
                // Unregistered fallback art keeps the previous full-rectangle convention.
                default: return new Bounds(1, 0, 1);
            }
        }

        public static Bounds StrikeBounds(string textureName, int frame)
        {
            if (textureName == "combat-hero-male-strikes")
            {
                if (frame == 0) return new Bounds(576, 96, 549);
                if (frame == 1) return new Bounds(576, 99, 549);
                if (frame == 2) return new Bounds(576, 97, 550);
            }
            if (textureName == "combat-hero-female-strikes")
            {
                if (frame == 0) return new Bounds(576, 100, 550);
                if (frame == 1) return new Bounds(576, 103, 550);
                if (frame == 2) return new Bounds(576, 96, 550);
            }
            return new Bounds(StrikeCellPixels,
                (int)(StrikeCellPixels - StrikeFootFromBottom - StrikeBodyPixels),
                (int)(StrikeCellPixels - StrikeFootFromBottom));
        }

        public static Layout Strike(float travelRectHeight, string standingTexture, string strikeTexture, int frame)
        {
            var standing = StandingBounds(standingTexture);
            var attack = StrikeBounds(strikeTexture, frame);
            float height = travelRectHeight * standing.BodyFraction / attack.BodyFraction;
            // Keep the image pivot on the actor's registered ground point. The opaque sole
            // retains the standing art's bottom padding (male/front has four pixels).
            float pivot = height > 0f ? attack.FootFraction - travelRectHeight * standing.FootFraction / height : attack.FootFraction;
            return new Layout(height, pivot);
        }
    }
}
