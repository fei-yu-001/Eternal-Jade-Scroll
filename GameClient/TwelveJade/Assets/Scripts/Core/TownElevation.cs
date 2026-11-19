using System;
using System.Collections.Generic;

namespace TwelveJade.Core
{
    /// <summary>世界平面坐标与高度分离。等级只在这里换算为世界单位；显示、点击与通行共用此求值器。</summary>
    public sealed class TownElevation
    {
        public sealed class Region
        {
            public string Id { get; internal set; }
            public string Shape { get; internal set; }
            public MapRect Rect { get; internal set; }
            public float Level { get; internal set; }
            public float Feather { get; internal set; }
            public float From { get; internal set; }
            public float To { get; internal set; }
            public string Axis { get; internal set; }
            public int BridgeIndex { get; internal set; }
        }
        sealed class Wave { public float Amp, Period, Phase; }
        sealed class WidthZone { public float Y0, Y1, Half, Blend; }
        readonly List<Region> regions = new List<Region>();
        readonly List<Wave> riverWaves = new List<Wave>();
        readonly List<WidthZone> widthZones = new List<WidthZone>();
        readonly List<(float x, float half)> bridges = new List<(float x, float half)>();
        float riverBase, waterHalf, roadX, roadAmp, roadPeriod, roadHalf;
        public IReadOnlyList<Region> Regions => regions;
        public float StepHeight { get; private set; } = 24f;
        public float DefaultLevel { get; private set; }
        public float MaxStepHeight { get; private set; } = 5f;
        public float SampleDistance { get; private set; } = 4f;
        public float MaxAbsHeight { get; private set; }
        public bool IsFlat => regions.Count == 0;

        static float N(JsonValue obj, string key, float fallback = float.NaN)
        {
            var v = obj[key];
            if (v.IsNull && !float.IsNaN(fallback)) return fallback;
            if (v.kind != JsonValue.Kind.Number || float.IsNaN(v.AsFloat(float.NaN)) || float.IsInfinity(v.AsFloat(float.NaN)))
                throw new FormatException("elevation: invalid number " + key);
            return v.AsFloat(float.NaN);
        }
        static float Positive(JsonValue obj, string key, float fallback = float.NaN)
        {
            var n = N(obj, key, fallback);
            if (n <= 0) throw new FormatException("elevation: " + key + " must be positive");
            return n;
        }
        static float Level(JsonValue obj, string key, float fallback = float.NaN)
        {
            var n = N(obj, key, fallback);
            if (n < -1 || n > 2) throw new FormatException("elevation: level must be in [-1,2]");
            return n;
        }
        static IEnumerable<JsonValue> Array(JsonValue obj, string key, bool required = false)
        {
            var v = obj[key];
            if (v.IsNull && !required) yield break;
            if (v.kind != JsonValue.Kind.Array) throw new FormatException("elevation: " + key + " must be array");
            foreach (var item in v.Items)
            {
                if (item.kind != JsonValue.Kind.Object) throw new FormatException("elevation: invalid " + key + " element");
                yield return item;
            }
        }
        public static TownElevation Parse(JsonValue root)
        {
            var result = new TownElevation();
            var cfg = root["elevation"];
            if (cfg.IsNull) return result; // 旧地图保持平面语义。
            if (cfg.kind != JsonValue.Kind.Object || N(cfg, "version") != 1)
                throw new FormatException("elevation: unsupported version");
            result.StepHeight = Positive(cfg, "stepHeight");
            result.DefaultLevel = Level(cfg, "defaultLevel", 0);
            result.MaxStepHeight = Positive(cfg, "maxStepHeight", 5);
            result.SampleDistance = Positive(cfg, "sampleDistance", 4);
            if (result.SampleDistance > 4 || result.MaxStepHeight >= result.StepHeight)
                throw new FormatException("elevation: sampling would permit skipping a cliff");
            var terrain = root["terrainShape"];
            var river = terrain["river"]; var road = terrain["mainRoad"];
            result.riverBase = N(river, "baseY"); result.waterHalf = Positive(river, "waterHalf");
            foreach (var w in Array(river, "wave", true))
                result.riverWaves.Add(new Wave { Amp = N(w, "amp"), Period = Positive(w, "period"), Phase = N(w, "phase", 0) });
            result.roadX = N(road, "baseX"); result.roadAmp = N(road, "amp");
            result.roadPeriod = Positive(road, "periodY"); result.roadHalf = Positive(road, "half");
            foreach (var z in Array(road, "widthZones"))
            {
                var zone = new WidthZone { Y0 = N(z,"y0"), Y1 = N(z,"y1"), Half = Positive(z,"half"), Blend = Positive(z,"blend") };
                if (zone.Y1 <= zone.Y0) throw new FormatException("elevation: invalid road width zone");
                result.widthZones.Add(zone);
            }
            foreach (var b in Array(terrain, "bridges", true)) result.bridges.Add((N(b,"x"), Positive(b,"half")));
            var ids = new HashSet<string>();
            foreach (var r in Array(cfg, "regions", true))
            {
                var entry = new Region { Id = r["id"].AsString(""), Shape = r["shape"].AsString(""),
                    Level = Level(r,"level",0), Feather = N(r,"feather",0), Axis = r["axis"].AsString("y") };
                if (entry.Id == "" || !ids.Add(entry.Id) || entry.Feather < 0) throw new FormatException("elevation: duplicate/missing id or negative feather");
                if (entry.Shape == "rect" || entry.Shape == "ramp")
                {
                    var rect = r["rect"];
                    if (rect.kind != JsonValue.Kind.Array || rect.Items.Count != 4) throw new FormatException("elevation: rect must have four coordinates");
                    var v = new float[4];
                    for (int i=0;i<4;i++)
                    {
                        if (rect.Items[i].kind != JsonValue.Kind.Number) throw new FormatException("elevation: invalid rect number");
                        v[i] = rect.Items[i].AsFloat(float.NaN);
                        if (float.IsNaN(v[i]) || float.IsInfinity(v[i])) throw new FormatException("elevation: invalid rect number");
                    }
                    if (v[2] <= v[0] || v[3] <= v[1]) throw new FormatException("elevation: empty rect");
                    entry.Rect = new MapRect(v[0],v[1],v[2]-v[0],v[3]-v[1]);
                    if (entry.Shape == "ramp")
                    {
                        if (entry.Axis != "x" && entry.Axis != "y") throw new FormatException("elevation: invalid ramp axis");
                        entry.From = Level(r,"from"); entry.To = Level(r,"to");
                    }
                }
                else if (entry.Shape == "bridge")
                {
                    var index = N(r,"bridgeIndex"); entry.BridgeIndex = (int)index;
                    if (index != entry.BridgeIndex || index < 0 || index >= result.bridges.Count) throw new FormatException("elevation: invalid bridge index");
                }
                else if (entry.Shape != "river" && entry.Shape != "road") throw new FormatException("elevation: unknown shape " + entry.Shape);
                result.regions.Add(entry);
            }
            if (result.regions.Count == 0) throw new FormatException("elevation: regions must not be empty");
            result.MaxAbsHeight = 2 * result.StepHeight;
            return result;
        }
        static float Clamp01(float x) => Math.Max(0,Math.Min(1,x));
        static float Blend(float distance, float feather) => distance <= 0 ? 1 : feather <= 0 ? 0 : Clamp01(1-distance/feather);
        public float RiverY(float x)
        {
            float y = riverBase;
            foreach (var w in riverWaves) y += w.Amp * (float)Math.Sin(x / w.Period + w.Phase);
            return y;
        }
        public float RoadX(float y) => roadX + roadAmp * (float)Math.Sin(y / roadPeriod);
        public float RoadHalf(float y)
        {
            float half = roadHalf;
            foreach (var z in widthZones)
            {
                var distance = Math.Max(z.Y0-y,Math.Max(y-z.Y1,0));
                var weight = Blend(distance,z.Blend);
                half = half*(1-weight)+z.Half*weight;
            }
            return half;
        }
        public float HeightAt(float x, float y)
        {
            if (IsFlat) return 0;
            float level = DefaultLevel;
            var riverDistance = Math.Abs(y-RiverY(x))-waterHalf;
            foreach (var r in regions)
            {
                float weight = 0, target = r.Level;
                switch (r.Shape)
                {
                    case "river": weight = Blend(riverDistance,r.Feather); break;
                    case "road":
                        // 河床不能被整条道路抬成陆地；跨河只有下面的 bridge 区域抬高。
                        weight = Blend(Math.Abs(x-RoadX(y))-RoadHalf(y),r.Feather) * Clamp01(riverDistance/64f);
                        break;
                    case "rect":
                        weight = Blend(Math.Max(Math.Max(r.Rect.MinX-x,x-r.Rect.MaxX),Math.Max(r.Rect.MinY-y,y-r.Rect.MaxY)),r.Feather);
                        break;
                    case "ramp":
                        if (r.Rect.Contains(x,y))
                        {
                            weight = 1;
                            float t = r.Axis == "y" ? (y-r.Rect.MinY)/r.Rect.Height : (x-r.Rect.MinX)/r.Rect.Width;
                            target = r.From+(r.To-r.From)*t;
                        }
                        break;
                    case "bridge": break; // 桥面在水系/岸线之后统一消费，保留配置顺序中的覆盖语义。
                }
                level += (target-level)*weight;
            }
            foreach (var r in regions)
            {
                if (r.Shape != "bridge") continue;
                var bridge=bridges[r.BridgeIndex];
                if (Math.Abs(x-bridge.x)>bridge.half) continue;
                var bridgeWeight=Blend(Math.Abs(y-RiverY(bridge.x))-(waterHalf+60),r.Feather);
                level=level*(1-bridgeWeight)+r.Level*bridgeWeight;
            }
            return level*StepHeight;
        }
        public float ProjectY(float x, float y) => y-HeightAt(x,y);
        public float UnprojectY(float x, float projectedY)
        {
            if (IsFlat) return projectedY;
            // 悬崖投影会重叠；从南向北找第一个连续表面，选择画面最前方可见的地面。
            float best = projectedY, error = float.MaxValue;
            float previousY = projectedY+MaxAbsHeight+1, previous = ProjectY(x,previousY)-projectedY;
            for (float y=previousY-1;y>=projectedY-MaxAbsHeight-1;y-=1)
            {
                float value = ProjectY(x,y)-projectedY;
                if (Math.Abs(value)<error) { error=Math.Abs(value); best=y; }
                if (value<=0 && previous>=0 && Math.Abs(HeightAt(x,y)-HeightAt(x,previousY))<=MaxStepHeight)
                {
                    float low=y,high=previousY;
                    for(int i=0;i<16;i++) { float mid=(low+high)*.5f; if(ProjectY(x,mid)<projectedY) low=mid;else high=mid; }
                    return (low+high)*.5f;
                }
                previousY=y;previous=value;
            }
            return best; // 点在护壁立面上：返回最近的边沿，随后由 CanStand/ClampToWalkable 处理。
        }
        public IEnumerable<float> CriticalCoordinates(bool vertical)
        {
            foreach(var r in regions)
            {
                if(r.Rect!=null)
                {
                    yield return vertical?r.Rect.MinY:r.Rect.MinX;
                    yield return vertical?r.Rect.MaxY:r.Rect.MaxX;
                }
                else if(r.Shape=="bridge")
                {
                    var b=bridges[r.BridgeIndex];
                    if(!vertical){yield return b.x-b.half;yield return b.x+b.half;}
                    else
                    {
                        float cy=RiverY(b.x),span=waterHalf+60;
                        yield return cy-span-r.Feather;yield return cy-span;
                        yield return cy+span;yield return cy+span+r.Feather;
                    }
                }
            }
        }
        public bool CanTraverse(float x0, float y0, float x1, float y1, Func<float,float,bool> canStand = null)
        {
            float dx=x1-x0,dy=y1-y0;
            float length=(float)Math.Sqrt(dx*dx+dy*dy);
            if(float.IsNaN(length)||float.IsInfinity(length))return false;
            int steps=Math.Max(1,(int)Math.Ceiling(length/SampleDistance));
            var samples=new SortedSet<float>();
            for(int i=1;i<=steps;i++)samples.Add((float)i/steps);
            // 每条硬边都采交点和两侧。矩形再窄也不能被等距步长整个跳过。
            float epsilon=Math.Min(.0001f,.001f/Math.Max(length,.001f));
            foreach(bool vertical in new[]{false,true})
            {
                float delta=vertical?dy:dx;if(Math.Abs(delta)<.000001f)continue;
                float start=vertical?y0:x0;
                foreach(float boundary in CriticalCoordinates(vertical))
                {
                    float t=(boundary-start)/delta;if(t<0||t>1)continue;
                    samples.Add(Math.Max(0,t-epsilon));samples.Add(t);samples.Add(Math.Min(1,t+epsilon));
                }
            }
            float previous=HeightAt(x0,y0);
            foreach(float t in samples)
            {
                float x=x0+dx*t,y=y0+dy*t,current=HeightAt(x,y);
                if (Math.Abs(current-previous)>MaxStepHeight || (canStand!=null && !canStand(x,y))) return false;
                previous=current;
            }
            return true;
        }
    }
}
