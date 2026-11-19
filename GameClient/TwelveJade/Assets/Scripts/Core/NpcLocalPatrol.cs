using System;
using System.Collections.Generic;

namespace TwelveJade.Core
{
    /// <summary>
    /// Transient movement for an existing NPC; the host owns its identity, schedule and view.
    /// Callbacks must use current world collisions, excluding only this NPC's own collider.
    /// No timers, persistence, rendering, or reduceMotion policy live here.
    /// </summary>
    public sealed class NpcLocalPatrol
    {
        public enum PatrolState { Waiting, Walking, Paused, Blocked, Stopped }
        public const float MaxStepSeconds = .05f;
        const int DestinationAttempts = 6;
        readonly Func<float, float, float, float, IReadOnlyList<(float x, float y)>> findPath;
        readonly Func<float, float, float, float, bool> canTraverse;
        readonly Random random;
        readonly List<(float x, float y)> route = new List<(float x, float y)>();
        readonly float speed, dwellSeconds;
        int waypoint;
        float waitLeft;

        public float HomeX { get; }
        public float HomeY { get; }
        public float HomeRadius { get; }
        public float X { get; private set; }
        public float Y { get; private set; }
        public PatrolState State { get; private set; }

        public NpcLocalPatrol(float homeX, float homeY, float homeRadius, float speed,
            float dwellSeconds, int seed,
            Func<float, float, float, float, IReadOnlyList<(float x, float y)>> findPath,
            Func<float, float, float, float, bool> canTraverse)
        {
            if (!Finite(homeX) || !Finite(homeY)) throw new ArgumentException("Home must be finite.");
            if (!Finite(homeRadius) || homeRadius <= 0) throw new ArgumentOutOfRangeException(nameof(homeRadius));
            if (!Finite(speed) || speed <= 0) throw new ArgumentOutOfRangeException(nameof(speed));
            if (!Finite(dwellSeconds) || dwellSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(dwellSeconds));
            this.findPath = findPath ?? throw new ArgumentNullException(nameof(findPath));
            this.canTraverse = canTraverse ?? throw new ArgumentNullException(nameof(canTraverse));
            HomeX = X = homeX; HomeY = Y = homeY; HomeRadius = homeRadius;
            this.speed = speed; this.dwellSeconds = dwellSeconds;
            random = new Random(seed);
            waitLeft = dwellSeconds * (float)random.NextDouble();
            State = PatrolState.Waiting;
        }

        /// <summary>Pause synchronously when dialogue starts; resume from this position after a fresh dwell.</summary>
        public void SetDialogue(bool active)
        {
            if (State == PatrolState.Stopped) return;
            if (active) { route.Clear(); State = PatrolState.Paused; }
            else if (State == PatrolState.Paused) Wait(PatrolState.Waiting);
        }

        /// <summary>Permanent for this visit. Re-entering Town creates a new instance from the schedule.</summary>
        public void Stop()
        {
            route.Clear(); waitLeft = 0; State = PatrolState.Stopped;
        }

        /// <returns>Actual distance moved this call, for presentation to consume together with X/Y.</returns>
        public float Advance(float deltaSeconds, bool inTown)
        {
            if (!inTown) { Stop(); return 0; }
            if (State == PatrolState.Stopped || State == PatrolState.Paused) return 0;
            if (!Finite(deltaSeconds) || deltaSeconds < 0) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            if (deltaSeconds == 0) return 0;
            float dt = Math.Min(deltaSeconds, MaxStepSeconds);
            if (State != PatrolState.Walking)
            {
                waitLeft = Math.Max(0, waitLeft - dt);
                if (waitLeft == 0 && !Plan()) Wait(PatrolState.Blocked);
                return 0;
            }
            var target = route[waypoint];
            float distance = Distance(X, Y, target.x, target.y);
            float step = Math.Min(distance, speed * dt);
            float nx = step >= distance ? target.x : X + (target.x - X) * (step / distance);
            float ny = step >= distance ? target.y : Y + (target.y - Y) * (step / distance);
            // Recheck every real segment: a blocker may have appeared after the path was planned.
            if (!InsideHome(nx, ny) || !canTraverse(X, Y, nx, ny)) { Wait(PatrolState.Blocked); return 0; }
            float moved = Distance(X, Y, nx, ny); X = nx; Y = ny;
            if (step >= distance && ++waypoint >= route.Count) Wait(PatrolState.Waiting);
            return moved;
        }

        bool Plan()
        {
            if (!InsideHome(X, Y) || !canTraverse(X, Y, X, Y)) return false;
            for (int attempt = 0; attempt < DestinationAttempts; attempt++)
            {
                double angle = random.NextDouble() * Math.PI * 2;
                double radius = Math.Sqrt(random.NextDouble()) * HomeRadius;
                float tx = HomeX + (float)(Math.Cos(angle) * radius);
                float ty = HomeY + (float)(Math.Sin(angle) * radius);
                if (!InsideHome(tx, ty) || Distance(X, Y, tx, ty) <= .001f || !canTraverse(tx, ty, tx, ty)) continue;
                var path = findPath(X, Y, tx, ty);
                if (path == null || path.Count == 0) continue;
                var end = path[path.Count - 1];
                if (end.x != tx || end.y != ty) continue;
                route.Clear(); float ax = X, ay = Y; bool valid = true;
                foreach (var point in path)
                {
                    // A disk is convex: in-range waypoints keep every straight segment in range.
                    if (!InsideHome(point.x, point.y) || !canTraverse(ax, ay, point.x, point.y)) { valid = false; break; }
                    if (point.x == ax && point.y == ay) continue;
                    route.Add(point); ax = point.x; ay = point.y;
                }
                if (valid && route.Count > 0) { waypoint = 0; State = PatrolState.Walking; return true; }
            }
            route.Clear(); return false;
        }

        void Wait(PatrolState state)
        {
            route.Clear(); waypoint = 0; waitLeft = dwellSeconds; State = state;
        }
        bool InsideHome(float x, float y)
        {
            if (!Finite(x) || !Finite(y)) return false;
            double dx = (double)x - HomeX, dy = (double)y - HomeY;
            return dx * dx + dy * dy <= (double)HomeRadius * HomeRadius;
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static float Distance(float ax, float ay, float bx, float by)
        {
            double dx = (double)bx - ax, dy = (double)by - ay;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
