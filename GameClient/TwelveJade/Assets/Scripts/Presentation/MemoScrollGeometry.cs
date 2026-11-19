using System;

namespace TwelveJade.Presentation
{
    // Pure geometry used by the live aura and independent clearance / motion checks.
    public static class MemoScrollGeometry
    {
        public const float AuraPadding = 18f;
        public const float PendantSeconds = 4.8f;
        public static float Step(float phase, float delta, float seconds, bool reduced)
        {
            if (reduced || !Finite(delta) || delta <= 0 || !Finite(seconds) || seconds <= 0) return phase;
            return Wrap(phase + Math.Min(delta, .1f) / seconds);
        }
        public static float Wrap(float value) => Finite(value) ? value - (float)Math.Floor(value) : 0;
        public static float PendantAngle(float phase, bool reduced) => reduced ? 0 : 1.2f * (float)Math.Sin(phase * Math.PI * 2);
        public static float TasselAngle(float phase, bool reduced) => reduced ? 0 : 2.1f * (float)Math.Sin(phase * Math.PI * 2 + .65);
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        // Two organic closed paths around the silk. Rounded shoulders replace the rectangular frame.
        // The narrow waist leaves all title/status/date glyphs inside the quiet paper area.
        public static void Orbit(float phase, float width, float height, int lane, out float x, out float y, out float angle)
        {
            Point(phase, width, height, lane, out x, out y);
            Point(phase + .0002f, width, height, lane, out float nx, out float ny);
            Point(phase - .0002f, width, height, lane, out float px, out float py);
            angle = (float)(Math.Atan2(ny - py, nx - px) * 180 / Math.PI);
        }
        static void Point(float phase, float width, float height, int lane, out float x, out float y)
        {
            double a = Wrap(phase) * Math.PI * 2;
            float c = (float)Math.Cos(a), s = (float)Math.Sin(a);
            float hx = Math.Max(12, width * .5f - (lane == 0 ? 5 : 10));
            float hy = Math.Max(12, height * .5f - (lane == 0 ? 4 : 10));
            float shoulder = (float)Math.Pow(Math.Abs(c), .24);
            float drift = (float)Math.Sin(a * 5 + lane * 1.7) * 2.1f * Math.Abs(c);
            x = Math.Sign(c) * shoulder * hx + drift;
            y = s * hy;
        }
        // Front-facing silk-like curls cross only the cloud caps, above/below every text rectangle.
        public static void Curl(float phase, float width, float height, int lane, out float x, out float y)
        {
            double a = phase * Math.PI * 2;
            float rx = width * .49f;
            x = (float)Math.Cos(a) * rx;
            y = (lane == 0 ? height * .48f : -height * .48f)
                + (float)Math.Sin(a) * (lane == 0 ? 16 : 19)
                + x * (lane == 0 ? .08f : -.12f);
        }
    }
}
