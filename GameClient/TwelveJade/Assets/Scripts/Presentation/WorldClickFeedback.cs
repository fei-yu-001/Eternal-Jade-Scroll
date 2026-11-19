using System;
using System.Collections.Generic;
using TwelveJade.Core;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // A ground-only view of the accepted production route. It never chooses or changes a route.
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class WorldClickFeedback : MaskableGraphic
    {
        public const float RippleSeconds = .25f, FadeSeconds = .18f;
        public const float RippleDiameterPixels = 48f, BeaconWidthPixels = 20f, BeaconHeightPixels = 45f;
        public const float DashSpacingPixels = 16f, BobPixels = 3f, BobSeconds = .5f;
        const int MaxRetiringTrails = 3, MaxMeshVertices = 60000;
        static readonly Color Gold = new Color(1f, .79f, .28f, 1f);
        static readonly Color Light = new Color(1f, .94f, .65f, 1f);
        sealed class CachedSegment
        {
            public bool traversable;
            public readonly List<Vector2> projected = new List<Vector2>();
        }
        sealed class Trail
        {
            public Vector2 target;
            public Vector2[] route;
            public CachedSegment[] tail;
            public int nextTailCheck, routeIndex;
            public readonly List<Vector2> projected = new List<Vector2>();
            public float ended = -1f, phase, bob;
            public int validSegments, blockedSegment = -1;
        }
        TownMap map;
        float worldScale;
        Func<bool> reduceMotion;
        Trail active;
        readonly List<Trail> retiring = new List<Trail>();
        Vector2 rippleWorld;
        float rippleStarted = -10f;
        bool lastReduced;
        float pixelUnit = 1f;
        Rect visible;

        // Managed CPU timings only: exclude native Canvas batching, upload and GPU work.
        public double LastConfirmMilliseconds { get; private set; }
        public double LastFollowMilliseconds { get; private set; }
        public double LastMeshMilliseconds { get; private set; }
        public int FollowCanTraverseCalls { get; private set; }
        public int ConfirmCanTraverseCalls { get; private set; }
        static double ElapsedMilliseconds(long start) => (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        public bool HasActiveTarget => active != null;
        public Vector2 TargetWorld => active != null ? active.target : Vector2.zero;
        public int RemainingPointCount => active != null ? active.route.Length - active.routeIndex + 1 : 0;
        public int ValidSegmentCount => active != null ? active.validSegments : 0;
        public int BlockedSegment => active != null ? active.blockedSegment : -1;
        public int FadingTrailCount => retiring.Count;
        public int AcceptedClickCount { get; private set; }
        public float RippleAge => Time.unscaledTime - rippleStarted;
        public bool Reduced => reduceMotion?.Invoke() ?? true;
        public float DashOffset => Reduced ? 0f : Mathf.Repeat(Time.unscaledTime * 20f, DashSpacingPixels);
        public float BeaconBob => Reduced ? 0f : Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / BobSeconds) * BobPixels;

        public void Configure(TownMap value, float scale, Func<bool> reduced)
        {
            map = value; worldScale = scale; reduceMotion = reduced;
            raycastTarget = false; color = Color.white;
            ClearImmediate();
        }
        public void ConfirmGroundClick(Vector2 target, Vector2 foot, IReadOnlyList<Vector2> route, int index)
        {
            long started = System.Diagnostics.Stopwatch.GetTimestamp(); ConfirmCanTraverseCalls = 0;
            try
            {
                if (map == null || route == null || index < 0 || index >= route.Count) return;
                End();
                active = new Trail { target = route[route.Count - 1], route = new Vector2[route.Count], tail = new CachedSegment[route.Count], nextTailCheck = index + 1 };
                for (int i = 0; i < route.Count; i++) active.route[i] = route[i];
                for (int i = index + 1; i < route.Count; i++)
                {
                    var a = route[i - 1]; var b = route[i];
                    var cached = new CachedSegment { traversable = map.CanTraverse(a.x, a.y, b.x, b.y) }; ConfirmCanTraverseCalls++;
                    cached.projected.Add(Project(a)); AddProjection(cached.projected, a, b); active.tail[i] = cached;
                }
                rippleWorld = target; rippleStarted = Time.unscaledTime; AcceptedClickCount++;
                Follow(foot, route, index, true);
            }
            finally { LastConfirmMilliseconds = ElapsedMilliseconds(started); }
        }
        public void Follow(Vector2 foot, IReadOnlyList<Vector2> route, int index, bool travelling)
        {
            long started = System.Diagnostics.Stopwatch.GetTimestamp(); FollowCanTraverseCalls = 0;
            try
            {
                if (active == null) return;
                if (!travelling || route == null || index < 0 || index >= route.Count || route.Count != active.route.Length || route[route.Count - 1] != active.target) { End(); return; }
                active.routeIndex = index;
                // Check the changing current edge plus one cached tail edge; never re-search or move nodes.
                if (index + 1 < route.Count)
                {
                    int k = active.nextTailCheck;
                    if (k <= index || k >= route.Count) k = index + 1;
                    var a = route[k - 1]; var b = route[k];
                    active.tail[k].traversable = map.CanTraverse(a.x,a.y,b.x,b.y); FollowCanTraverseCalls++;
                    active.nextTailCheck = k + 1;
                }
                active.projected.Clear(); active.projected.Add(Project(foot)); active.validSegments = 0; active.blockedSegment = -1;
                var next = route[index];
                bool currentValid = map.CanTraverse(foot.x,foot.y,next.x,next.y); FollowCanTraverseCalls++;
                if (!currentValid) active.blockedSegment = 0;
                else
                {
                    AddProjection(active.projected,foot,next); active.validSegments++;
                    for (int i = index + 1; i < route.Count; i++)
                    {
                        if (!active.tail[i].traversable) { active.blockedSegment = i - index; break; }
                        active.validSegments++;
                    }
                }
                SetVerticesDirty();
            }
            finally { LastFollowMilliseconds = ElapsedMilliseconds(started); }
        }
        public void End()
        {
            if (active == null) return; // Repeated modal/keyboard cancellation must not restart fading.
            active.ended = Time.unscaledTime; active.phase = DashOffset; active.bob = BeaconBob;
            retiring.Add(active); active = null;
            while (retiring.Count > MaxRetiringTrails) retiring.RemoveAt(0);
            SetVerticesDirty();
        }
        public void ClearImmediate()
        {
            active = null; retiring.Clear(); rippleStarted = -10f;
            SetVerticesDirty();
        }
        protected override void OnDisable()
        {
            ClearImmediate();
            base.OnDisable();
        }
        protected override void OnDestroy()
        {
            reduceMotion = null; map = null;
            base.OnDestroy();
        }
        void LateUpdate()
        {
            float now = Time.unscaledTime;
            bool dirty = false;
            for (int i = retiring.Count - 1; i >= 0; i--)
            {
                if (now - retiring[i].ended >= FadeSeconds) { retiring.RemoveAt(i); dirty = true; }
                else
                {
                    // The previous accepted intent stays frozen for its bounded .18s fade.
                    dirty = true;
                }
            }
            bool reduced = Reduced;
            if (dirty || reduced != lastReduced || now - rippleStarted <= RippleSeconds + .05f ||
                (active != null && !reduced)) SetVerticesDirty();
            lastReduced = reduced;
        }
        Vector2 Project(Vector2 p) => new Vector2(p.x * worldScale, -map.Elevation.ProjectY(p.x, p.y) * worldScale);
        void AddProjection(List<Vector2> output, Vector2 from, Vector2 to)
        {
            // Project along the accepted A* edge, never across skipped nodes.
            int samples = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(from,to) / 8f));
            for (int i = 1; i <= samples; i++) output.Add(Project(Vector2.Lerp(from,to,i / (float)samples)));
        }
        void MeasureViewport()
        {
            var owner = canvas;
            var camera = owner == null || owner.renderMode == RenderMode.ScreenSpaceOverlay ? null : owner.worldCamera;
            var origin = RectTransformUtility.WorldToScreenPoint(camera, rectTransform.TransformPoint(Vector3.zero));
            var unit = RectTransformUtility.WorldToScreenPoint(camera, rectTransform.TransformPoint(Vector3.right));
            pixelUnit = 1f / Mathf.Max(.001f, Vector2.Distance(origin, unit));
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, Vector2.zero, camera, out var lo);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, new Vector2(Screen.width, Screen.height), camera, out var hi);
            float margin = 60f * pixelUnit;
            visible = Rect.MinMaxRect(Mathf.Min(lo.x, hi.x) - margin, Mathf.Min(lo.y, hi.y) - margin,
                Mathf.Max(lo.x, hi.x) + margin, Mathf.Max(lo.y, hi.y) + margin);
        }
        static Color Tint(Color color, float alpha) { color.a *= Mathf.Clamp01(alpha); return color; }
        bool InView(Vector2 a, Vector2 b) => Mathf.Max(a.x, b.x) >= visible.xMin && Mathf.Min(a.x, b.x) <= visible.xMax &&
            Mathf.Max(a.y, b.y) >= visible.yMin && Mathf.Min(a.y, b.y) <= visible.yMax;
        void Line(VertexHelper vh, Vector2 a, Vector2 b, float widthPixels, Color tint)
        {
            if (!InView(a, b) || vh.currentVertCount > MaxMeshVertices - 4 || (b - a).sqrMagnitude < .000001f) return;
            var n = new Vector2(a.y - b.y, b.x - a.x).normalized * (widthPixels * pixelUnit * .5f);
            int first = vh.currentVertCount;
            vh.AddVert(a - n, tint, Vector2.zero); vh.AddVert(a + n, tint, Vector2.zero);
            vh.AddVert(b + n, tint, Vector2.zero); vh.AddVert(b - n, tint, Vector2.zero);
            vh.AddTriangle(first, first + 1, first + 2); vh.AddTriangle(first, first + 2, first + 3);
        }
        void Disc(VertexHelper vh, Vector2 center, float radiusPixels, Color tint)
        {
            if (!visible.Contains(center) || vh.currentVertCount > MaxMeshVertices - 34) return;
            int first = vh.currentVertCount; vh.AddVert(center, tint, Vector2.zero);
            for (int i = 0; i <= 32; i++)
            {
                float angle = i * Mathf.PI * 2f / 32;
                vh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radiusPixels * pixelUnit, Tint(tint, 0f), Vector2.zero);
                if (i > 0) vh.AddTriangle(first, first + i, first + i + 1);
            }
        }
        void Arc(VertexHelper vh, Vector2 center, float radiusPixels, float start, float end, float width, Color tint)
        {
            int steps = Mathf.Max(4, Mathf.CeilToInt(Mathf.Abs(end - start) * 9));
            var a = center + new Vector2(Mathf.Cos(start), Mathf.Sin(start)) * radiusPixels * pixelUnit;
            for (int i = 1; i <= steps; i++)
            {
                float angle = Mathf.Lerp(start, end, i / (float)steps);
                var b = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radiusPixels * pixelUnit;
                Line(vh, a, b, width, tint); a = b;
            }
        }
        void DrawPath(VertexHelper vh, Trail trail, float alpha, float phase)
        {
            float distance = 0f;
            DrawPathPart(vh, trail.projected, alpha, phase, ref distance);
            // Cached projected tail geometry is reused, without copying or reprojecting the whole route.
            for (int i = trail.routeIndex + 1; i < trail.routeIndex + trail.validSegments; i++)
                DrawPathPart(vh, trail.tail[i].projected, alpha, phase, ref distance);
        }
        void DrawPathPart(VertexHelper vh, List<Vector2> points, float alpha, float phase, ref float distance)
        {
            float period = DashSpacingPixels * pixelUnit, dash = 7f * pixelUnit;
            for (int i = 1; i < points.Count; i++)
            {
                var a = points[i - 1]; var b = points[i]; float length = Vector2.Distance(a, b);
                if (length < .0001f) continue;
                if (InView(a, b))
                {
                    float start = Mathf.Floor((distance - phase * pixelUnit) / period) * period + phase * pixelUnit;
                    for (float at = start; at < distance + length; at += period)
                    {
                        float from = Mathf.Max(distance, at), to = Mathf.Min(distance + length, at + dash);
                        if (to <= from) continue;
                        var p = Vector2.Lerp(a, b, (from - distance) / length); var q = Vector2.Lerp(a, b, (to - distance) / length);
                        Line(vh, p, q, 4f, Tint(Gold, alpha * .11f));
                        Line(vh, p, q, 1.4f, Tint(Gold, alpha * .72f));
                    }
                }
                distance += length;
            }
        }
        void DrawBeacon(VertexHelper vh, Vector2 foot, float alpha, float bob)
        {
            if (!visible.Contains(foot)) return;
            Disc(vh, foot, 12, Tint(Gold, alpha * .22f));
            Arc(vh, foot, 8, 0, Mathf.PI * 2f, 1f, Tint(Gold, alpha * .55f));
            var b = foot + Vector2.up * bob * pixelUnit;
            // 20 x 45 px: a warm stone column capped by a small geometric talisman seal.
            Line(vh, b + new Vector2(0, 5) * pixelUnit, b + new Vector2(0, 34) * pixelUnit, 16, Tint(Gold, alpha * .12f));
            Line(vh, b + new Vector2(0, 6) * pixelUnit, b + new Vector2(0, 33) * pixelUnit, 8, Tint(Gold, alpha * .50f));
            Line(vh, b + new Vector2(-4, 7) * pixelUnit, b + new Vector2(-4, 33) * pixelUnit, 1, Tint(Light, alpha * .85f));
            Line(vh, b + new Vector2(4, 7) * pixelUnit, b + new Vector2(4, 33) * pixelUnit, 1, Tint(Gold, alpha * .8f));
            var top = b + new Vector2(0, BeaconHeightPixels) * pixelUnit;
            var left = b + new Vector2(-BeaconWidthPixels * .5f, 37) * pixelUnit;
            var right = b + new Vector2(BeaconWidthPixels * .5f, 37) * pixelUnit;
            var bottom = b + new Vector2(0, 29) * pixelUnit;
            Line(vh, top, left, 1.3f, Tint(Light, alpha)); Line(vh, left, bottom, 1.3f, Tint(Gold, alpha));
            Line(vh, bottom, right, 1.3f, Tint(Gold, alpha)); Line(vh, right, top, 1.3f, Tint(Light, alpha));
            Line(vh, b + new Vector2(-3, 39) * pixelUnit, b + new Vector2(3, 39) * pixelUnit, 1.1f, Tint(Light, alpha));
            Line(vh, b + new Vector2(0, 41) * pixelUnit, b + new Vector2(0, 34) * pixelUnit, 1.1f, Tint(Light, alpha));
            Line(vh, b + new Vector2(-2, 35) * pixelUnit, b + new Vector2(3, 35) * pixelUnit, 1.1f, Tint(Light, alpha));
            if (!Reduced)
            {
                float y = 8 + Mathf.Repeat(Time.unscaledTime * 18, 24);
                Line(vh, b + new Vector2(-3, y) * pixelUnit, b + new Vector2(3, y + 2) * pixelUnit, 1f, Tint(Light, alpha * .38f));
            }
        }
        void DrawRipple(VertexHelper vh)
        {
            float t = RippleAge / RippleSeconds;
            if (t < 0 || t >= 1) return;
            var at = Project(rippleWorld); if (!visible.Contains(at)) return;
            float radius = RippleDiameterPixels * .5f * (Reduced ? 1f : Mathf.Lerp(.58f, 1f, 1f - (1f - t) * (1f - t)));
            float alpha = (1f - t) * (Reduced ? .55f : .85f);
            Disc(vh, at, radius, Tint(Gold, alpha * .18f));
            Arc(vh, at, radius, 0, Mathf.PI * 2f, 1.2f, Tint(Gold, alpha));
            Arc(vh, at, radius * .7f, 0, Mathf.PI * 2f, .8f, Tint(Light, alpha * .75f));
            // Tai-chi S, two eyes, and eight quiet radial accents; no text or atlas dependency.
            float r = radius * .35f;
            Arc(vh, at + Vector2.up * r * pixelUnit, r, -Mathf.PI * .5f, Mathf.PI * .5f, 1.1f, Tint(Light, alpha));
            Arc(vh, at - Vector2.up * r * pixelUnit, r, Mathf.PI * .5f, Mathf.PI * 1.5f, 1.1f, Tint(Gold, alpha));
            Disc(vh, at + Vector2.up * r * pixelUnit, 2.5f, Tint(Light, alpha));
            Disc(vh, at - Vector2.up * r * pixelUnit, 2.5f, Tint(Gold, alpha));
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI / 4; var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var tangent = new Vector2(-direction.y, direction.x); var p = at + direction * radius * .85f * pixelUnit;
                Line(vh, p - tangent * 2 * pixelUnit, p + tangent * 2 * pixelUnit, .8f, Tint(Gold, alpha));
            }
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                vh.Clear(); if (map == null) return; MeasureViewport();
                foreach (var trail in retiring)
                {
                    float alpha = Mathf.Clamp01(1f - (Time.unscaledTime - trail.ended) / FadeSeconds);
                    DrawPath(vh, trail, alpha, trail.phase); DrawBeacon(vh, Project(trail.target), alpha, Reduced ? 0 : trail.bob);
                }
                if (active != null)
                {
                    DrawPath(vh, active, 1f, DashOffset); DrawBeacon(vh, Project(active.target), 1f, BeaconBob);
                }
                DrawRipple(vh);
            }
            finally { LastMeshMilliseconds = ElapsedMilliseconds(started); }
        }
    }
}
