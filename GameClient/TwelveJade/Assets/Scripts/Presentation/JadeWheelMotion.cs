using System;
namespace TwelveJade.Presentation
{
    // Angle convention matches the approved HTML: clockwise logical degrees; Unity uses its negative Z.
    public sealed class JadeWheelMotion
    {
        public const int SlotCount = 8;
        public double Angle { get; private set; }
        public double TargetAngle { get; private set; }
        public double Velocity { get; private set; }
        public int TargetSlot { get; private set; }
        public int Revision { get; private set; }
        public bool Moving { get; private set; }
        public bool Armed { get; private set; }
        public double Elapsed { get; private set; }
        public static double ShortestDelta(double current, double target)
        {
            double delta = (target - current) % 360;
            if (delta > 180) delta -= 360;
            if (delta < -180) delta += 360;
            return delta;
        }
        public void Request(int slot, bool reducedMotion)
        {
            if (slot < 0 || slot >= SlotCount) throw new ArgumentOutOfRangeException(nameof(slot));
            double delta = ShortestDelta(Angle, -slot * 45.0);
            TargetAngle = Angle + delta; TargetSlot = slot; Revision++; Armed = true; Elapsed = 0;
            if (Velocity * delta < 0) Velocity = 0; // Do not carry momentum onto the long branch after a retarget.
            if (reducedMotion || Math.Abs(delta) < .001) { Angle = TargetAngle; Velocity = 0; Moving = false; }
            else Moving = true;
        }
        public void Advance(double seconds, bool reducedMotion)
        {
            if (!Moving) return;
            if (reducedMotion) { Angle = TargetAngle; Velocity = 0; Moving = false; return; }
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0) return;
            Elapsed += seconds;
            double remaining = Math.Min(seconds, .10);
            while (remaining > .0000001)
            {
                double dt = Math.Min(remaining, 1.0 / 120); remaining -= dt;
                double acceleration = (TargetAngle - Angle) * 900 - Velocity * 46;
                Velocity = Math.Max(-2400, Math.Min(2400, Velocity + acceleration * dt));
                Angle += Velocity * dt;
            }
            if ((Elapsed >= .28 && Math.Abs(TargetAngle - Angle) <= .06 && Math.Abs(Velocity) <= .65) || Elapsed >= .32)
            { Angle = TargetAngle; Velocity = 0; Moving = false; }
        }
        public bool TakeArrival(out int slot)
        {
            slot = TargetSlot;
            if (!Armed || Moving) return false;
            Armed = false; return true;
        }
        // A new pointer-down cancels stale activation before its click/up decides a new destination.
        public void Disarm() { Armed = false; Revision++; }
        public void Cancel() { Disarm(); Moving = false; Velocity = 0; TargetAngle = Angle; }
    }
    public static class JadeWheelTone
    {
        public const int Rate = 22050;
        public static float[] Render(bool arrival)
        {
            double duration = arrival ? .115 : .095;
            var samples = new float[(int)(Rate * duration)];
            for (int i = 0; i < samples.Length; i++)
            {
                double t = i / (double)Rate, tail = 1 - i / (double)samples.Length;
                double tone = arrival
                    ? Math.Sin(2 * Math.PI * 1480 * t) + .32 * Math.Sin(2 * Math.PI * 2383 * t) + .14 * Math.Sin(2 * Math.PI * 3700 * t)
                    : Math.Sin(2 * Math.PI * 780 * t) + .24 * Math.Sin(2 * Math.PI * 1217 * t);
                samples[i] = (float)(tone * Math.Min(1, t * 1400) * Math.Exp(-t * (arrival ? 23 : 31)) * tail * tail * .13);
            }
            return samples;
        }
    }
}
