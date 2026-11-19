using System;
using System.Collections.Generic;
using TwelveJade.Core;

namespace TwelveJade.Presentation
{
    /// <summary>
    /// CPU-only river ribbon and deck subtraction, in map coordinates (X right, Y down).
    /// No Unity dependency: the same geometry can be checked without starting the editor.
    /// </summary>
    public static class TownWaterGeometry
    {
        public readonly struct Wave
        {
            public readonly float Amplitude, Period, Phase;
            public Wave(float amplitude, float period, float phase)
            { Amplitude = amplitude; Period = period; Phase = phase; }
        }

        public readonly struct Exclusion
        {
            public readonly float MinX, MinY, MaxX, MaxY;
            public Exclusion(float minX, float minY, float maxX, float maxY)
            { MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY; }
            public bool Contains(float x, float y) => x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;
            public Exclusion Expanded(float margin) => new Exclusion(MinX - margin, MinY - margin, MaxX + margin, MaxY + margin);
        }

        /// <summary>Explicit inputs; no guessed map or water rectangle is ever supplied.</summary>
        public sealed class Definition
        {
            public float WorldWidth, WorldHeight, BaseY, HalfWidth;
            public Wave[] Waves = Array.Empty<Wave>();
            public Exclusion[] Exclusions = Array.Empty<Exclusion>();
            public float BankInset = 8f;
            public float DeckClearance = 6f;
            public float SampleStep = 32f;
            public int AcrossSegments = 8;
            // Existing config describes geometry, not downstream direction; callers may reverse it.
            public float FlowDirection = 1f;

            public float CenterY(float x)
            {
                double result = BaseY;
                for (int i = 0; i < Waves.Length; i++)
                    result += Waves[i].Amplitude * Math.Sin(x / Waves[i].Period + Waves[i].Phase);
                return (float)result;
            }
        }

        public readonly struct Vertex
        {
            public readonly float X, Y, Along, Across, ShoreDistance, DeckDistance;
            public Vertex(float x, float y, float along, float across, float shoreDistance = 0, float deckDistance = 0)
            { X = x; Y = y; Along = along; Across = across; ShoreDistance = shoreDistance; DeckDistance = deckDistance; }
        }

        public sealed class Ribbon
        {
            public readonly List<Vertex> Vertices = new List<Vertex>();
            public readonly List<int> Indices = new List<int>();
            public Exclusion[] Cutouts { get; internal set; }
        }

        static bool Finite(float n) => !float.IsNaN(n) && !float.IsInfinity(n);
        static float Number(JsonValue obj, string key)
        {
            var value = obj[key];
            float result = value.AsFloat(float.NaN);
            if (value.kind != JsonValue.Kind.Number || !Finite(result))
                throw new FormatException("water: missing/invalid " + key);
            return result;
        }
        static IReadOnlyList<JsonValue> RequiredArray(JsonValue obj, string key)
        {
            if (obj[key].kind != JsonValue.Kind.Array) throw new FormatException("water: missing array " + key);
            return obj[key].Items;
        }
        static float Positive(JsonValue obj, string key)
        {
            float value = Number(obj, key);
            if (value <= 0) throw new FormatException("water: nonpositive " + key);
            return value;
        }

        /// <summary>
        /// Mirrors make-world-mask.py: river wave sum, bridge rectangles, south ferry deck,
        /// and the ramp/ferry-landing paving overrides. Missing/unknown schemas fail closed.
        /// </summary>
        public static Definition FromTownMapJson(string json)
        {
            var root = Json.Parse(json);
            var shape = root["terrainShape"];
            if (Number(shape, "version") != 1) throw new FormatException("water: unsupported terrainShape version");
            if (root["terrainZones"].Count > 0)
                throw new FormatException("water: explicit terrainZones require an explicit water definition");
            var river = shape["river"];
            var result = new Definition
            {
                WorldWidth = Positive(root, "worldWidth"), WorldHeight = Positive(root, "worldHeight"),
                BaseY = Number(river, "baseY"), HalfWidth = Positive(river, "waterHalf")
            };
            float bankHalf = Positive(river, "bankHalf");
            if (bankHalf < result.HalfWidth) throw new FormatException("water: bankHalf is smaller than waterHalf");
            var waves = RequiredArray(river, "wave");
            result.Waves = new Wave[waves.Count];
            for (int i = 0; i < waves.Count; i++)
                result.Waves[i] = new Wave(Number(waves[i], "amp"), Positive(waves[i], "period"), Number(waves[i], "phase"));
            var holes = new List<Exclusion>();
            foreach (var bridge in RequiredArray(shape, "bridges"))
            {
                float x = Number(bridge, "x"), half = Positive(bridge, "half"), y = result.CenterY(x);
                holes.Add(new Exclusion(x - half, y - bankHalf - 20, x + half, y + bankHalf + 20));
            }
            if (!shape["ferry"].IsNull)
            {
                float x = Number(shape["ferry"], "x"), half = Positive(shape["ferry"], "half"), y = result.CenterY(x);
                holes.Add(new Exclusion(x - half, y + 50, x + half, y + bankHalf + 30));
            }
            if (!root["elevation"].IsNull)
            {
                if (Number(root["elevation"], "version") != 1) throw new FormatException("water: unsupported elevation version");
                foreach (var region in RequiredArray(root["elevation"], "regions"))
                {
                    if (region["shape"].AsString("") != "ramp" && region["id"].AsString("") != "ferry-landing") continue;
                    var r = RequiredArray(region, "rect");
                    if (r.Count != 4) throw new FormatException("water: paving rectangle requires four coordinates");
                    holes.Add(new Exclusion(r[0].AsFloat(float.NaN), r[1].AsFloat(float.NaN),
                        r[2].AsFloat(float.NaN), r[3].AsFloat(float.NaN)));
                }
            }
            result.Exclusions = holes.ToArray();
            Validate(result);
            // Mountain overrides water in the terrain composer. This adapter deliberately supports
            // only a separated river; overlapping future biomes need explicit geometry/mask support.
            var mountain = shape["mountain"];
            float mountainMax = Number(mountain, "baseY"), riverMin = result.BaseY - result.HalfWidth;
            foreach (var w in RequiredArray(mountain, "wave")) mountainMax += Math.Abs(Number(w, "amp"));
            foreach (var w in result.Waves) riverMin -= Math.Abs(w.Amplitude);
            if (mountainMax >= riverMin) throw new FormatException("water: mountain/water envelopes overlap; supply explicit geometry");
            return result;
        }

        static void Validate(Definition d)
        {
            if (d == null || !Finite(d.WorldWidth) || !Finite(d.WorldHeight) || d.WorldWidth <= 0 || d.WorldHeight <= 0 ||
                !Finite(d.BaseY) || !Finite(d.HalfWidth) || !Finite(d.BankInset) || d.BankInset <= 0 || d.HalfWidth <= d.BankInset ||
                !Finite(d.DeckClearance) || d.DeckClearance < 0 || !Finite(d.SampleStep) || d.SampleStep < 1 || d.SampleStep > 32 ||
                d.AcrossSegments < 2 || d.AcrossSegments > 32 || !Finite(d.FlowDirection) || Math.Abs(d.FlowDirection) != 1 ||
                d.Waves == null || d.Exclusions == null)
                throw new FormatException("water: invalid ribbon dimensions, sampling or flow direction");
            if (d.Waves.Length > 32 || d.Exclusions.Length > 256) throw new FormatException("water: definition exceeds geometry budget");
            foreach (var w in d.Waves)
                if (!Finite(w.Amplitude) || !Finite(w.Period) || w.Period <= 0 || !Finite(w.Phase))
                    throw new FormatException("water: invalid river wave");
            foreach (var r in d.Exclusions)
                if (!Finite(r.MinX) || !Finite(r.MinY) || !Finite(r.MaxX) || !Finite(r.MaxY) || r.MinX >= r.MaxX || r.MinY >= r.MaxY)
                    throw new FormatException("water: invalid exclusion rectangle");
        }

        public static Ribbon Build(Definition d)
        {
            Validate(d);
            double curvature = 0;
            foreach (var w in d.Waves) curvature += Math.Abs((double)w.Amplitude) / w.Period / w.Period;
            // A chord deviates by at most max|y''| * dx^2 / 8. Keep it inside the bank inset.
            float step = curvature > 0 ? Math.Min(d.SampleStep, (float)Math.Sqrt(4 * d.BankInset / curvature)) : d.SampleStep;
            if (step < 1 || d.WorldWidth / step > 4096) throw new FormatException("water: river requires excessive sampling");
            var ribbon = new Ribbon { Cutouts = new Exclusion[d.Exclusions.Length] };
            var xs = new SortedSet<float> { 0, d.WorldWidth };
            for (int i = 1; i * (double)step < d.WorldWidth; i++) xs.Add(i * step);
            for (int i = 0; i < d.Exclusions.Length; i++)
            {
                var r = d.Exclusions[i].Expanded(d.DeckClearance);
                ribbon.Cutouts[i] = r;
                if (r.MinX > 0 && r.MinX < d.WorldWidth) xs.Add(r.MinX);
                if (r.MaxX > 0 && r.MaxX < d.WorldWidth) xs.Add(r.MaxX);
            }
            var cuts = new List<float>(xs);
            float half = d.HalfWidth - d.BankInset, along = 0;
            for (int i = 0; i < cuts.Count - 1; i++)
            {
                float x0 = cuts[i], x1 = cuts[i + 1], y0 = d.CenterY(x0), y1 = d.CenterY(x1);
                float nextAlong = along + (float)Math.Sqrt((double)(x1 - x0) * (x1 - x0) + (double)(y1 - y0) * (y1 - y0));
                for (int j = 0; j < d.AcrossSegments; j++)
                {
                    float v0 = -half + 2 * half * j / d.AcrossSegments;
                    float v1 = -half + 2 * half * (j + 1) / d.AcrossSegments;
                    var polygon = new List<Vertex>
                    {
                        new Vertex(x0, y0 + v0, along, v0), new Vertex(x1, y1 + v0, nextAlong, v0),
                        new Vertex(x1, y1 + v1, nextAlong, v1), new Vertex(x0, y0 + v1, along, v1)
                    };
                    polygon = Clip(Clip(polygon, 1, 0, true), 1, d.WorldHeight, false);
                    var pieces = new List<List<Vertex>> { polygon };
                    foreach (var hole in ribbon.Cutouts)
                    {
                        var next = new List<List<Vertex>>();
                        foreach (var piece in pieces) Subtract(piece, hole, next);
                        pieces = next;
                        if (pieces.Count == 0) break;
                    }
                    foreach (var piece in pieces) Emit(piece, ribbon, half);
                }
                // Continue phase across holes; no reset of flow at bridges/chunks.
                along = nextAlong;
            }
            if (ribbon.Indices.Count == 0) throw new FormatException("water: no exposed water remains");
            return ribbon;
        }

        static float Coordinate(Vertex v, int axis) => axis == 0 ? v.X : v.Y;
        static List<Vertex> Clip(List<Vertex> input, int axis, float edge, bool greater)
        {
            var output = new List<Vertex>();
            if (input.Count == 0) return output;
            var prev = input[input.Count - 1];
            bool prevIn = greater ? Coordinate(prev, axis) >= edge : Coordinate(prev, axis) <= edge;
            foreach (var current in input)
            {
                bool currentIn = greater ? Coordinate(current, axis) >= edge : Coordinate(current, axis) <= edge;
                if (currentIn != prevIn)
                {
                    float t = (edge - Coordinate(prev, axis)) / (Coordinate(current, axis) - Coordinate(prev, axis));
                    output.Add(new Vertex(axis == 0 ? edge : prev.X + (current.X - prev.X) * t,
                        axis == 1 ? edge : prev.Y + (current.Y - prev.Y) * t,
                        prev.Along + (current.Along - prev.Along) * t, prev.Across + (current.Across - prev.Across) * t));
                }
                if (currentIn) output.Add(current);
                prev = current; prevIn = currentIn;
            }
            return output;
        }

        static void Subtract(List<Vertex> polygon, Exclusion r, List<List<Vertex>> output)
        {
            if (polygon.Count < 3) return;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var v in polygon)
            { minX = Math.Min(minX, v.X); minY = Math.Min(minY, v.Y); maxX = Math.Max(maxX, v.X); maxY = Math.Max(maxY, v.Y); }
            if (maxX <= r.MinX || minX >= r.MaxX || maxY <= r.MinY || minY >= r.MaxY)
            { output.Add(polygon); return; }
            // Partition a convex polygon into four disjoint outside pieces; discard the inside.
            output.Add(Clip(polygon, 0, r.MinX, false)); polygon = Clip(polygon, 0, r.MinX, true);
            output.Add(Clip(polygon, 0, r.MaxX, true)); polygon = Clip(polygon, 0, r.MaxX, false);
            output.Add(Clip(polygon, 1, r.MinY, false)); polygon = Clip(polygon, 1, r.MinY, true);
            output.Add(Clip(polygon, 1, r.MaxY, true));
        }

        static void Emit(List<Vertex> polygon, Ribbon ribbon, float half)
        {
            if (polygon.Count < 3) return;
            double area = 0;
            for (int i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i]; var b = polygon[(i + 1) % polygon.Count];
                area += (double)a.X * b.Y - (double)b.X * a.Y;
            }
            if (Math.Abs(area) < .002) return;
            if (ribbon.Vertices.Count + polygon.Count >= 60000) throw new FormatException("water: UI mesh vertex budget exceeded");
            int start = ribbon.Vertices.Count;
            foreach (var v in polygon)
            {
                float deckDistance = 100000;
                foreach (var r in ribbon.Cutouts)
                {
                    float dx = Math.Max(0, Math.Max(r.MinX - v.X, v.X - r.MaxX));
                    float dy = Math.Max(0, Math.Max(r.MinY - v.Y, v.Y - r.MaxY));
                    deckDistance = Math.Min(deckDistance, (float)Math.Sqrt(dx * dx + dy * dy));
                }
                ribbon.Vertices.Add(new Vertex(v.X, v.Y, v.Along, v.Across, Math.Max(0, half - Math.Abs(v.Across)), deckDistance));
            }
            for (int i = 1; i < polygon.Count - 1; i++)
            {
                var a = polygon[0]; var b = polygon[i]; var c = polygon[i + 1];
                if (Math.Abs((double)(b.X - a.X) * (c.Y - a.Y) - (double)(b.Y - a.Y) * (c.X - a.X)) < .002) continue;
                ribbon.Indices.Add(start); ribbon.Indices.Add(start + i); ribbon.Indices.Add(start + i + 1);
            }
        }
    }
}
