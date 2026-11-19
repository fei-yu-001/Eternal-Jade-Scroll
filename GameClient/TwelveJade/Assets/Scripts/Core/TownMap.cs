using System;
using System.Collections.Generic;
using System.Linq;

namespace TwelveJade.Core
{
    // 地图数据（走廊 + 立绘 + 地标）：M1 的数据内联在 FrontEndMap.cs 里，手感定稿后外置到
    // Resources/Config/town-map.json。Core 层只认"画布坐标"，不知道 Unity 的 RectTransform。
    public sealed class MapRect
    {
        public MapRect(float x, float y, float width, float height)
        { X = x; Y = y; Width = width; Height = height; }

        public float X { get; }
        public float Y { get; }
        public float Width { get; }
        public float Height { get; }

        public float MinX => X;
        public float MaxX => X + Width;
        public float MinY => Y;
        public float MaxY => Y + Height;

        public bool Contains(float x, float y) => x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;
    }

    // 大世界地图块：一块地形贴图在世界坐标里的位置。分块的意义是"世界远大于一屏"——
    // 每块独立加载、按需显示，摄像机只移动世界根节点，不缩放整张巨图。
    public sealed class MapChunk
    {
        public MapChunk(string id, string art, string overview, float x, float y, float width, float height)
        {
            Id = id; Art = art; Overview = overview; X = x; Y = y; Width = width; Height = height;
        }

        public string Id { get; }
        public string Art { get; }        // Resources 相对路径（不带扩展名）
        public string Overview { get; }   // 整幅世界的缩略图（小地图用）
        public float X { get; }
        public float Y { get; }
        public float Width { get; }
        public float Height { get; }
    }

    public sealed class MapProp
    {
        public MapProp(string slug, float x, float y, float height, float radius)
        { Slug = slug; X = x; Y = y; Height = height; Radius = radius; }

        public string Slug { get; }
        public float X { get; }
        public float Y { get; }
        public float Height { get; }
        public float Radius { get; }
        public bool Blocks => Radius > 0f;
    }

    // 镇上的活人：与立绘一样按地平线摆放，但会被点击搭话。
    public sealed class MapNpc
    {
        public MapNpc(string id, string merchant, string name, string art,
            float x, float y, float height, float radius, string line)
        {
            Id = id; Merchant = merchant; Name = name; Art = art;
            X = x; Y = y; Height = height; Radius = radius; Line = line;
        }

        public string Id { get; }
        // 对应 merchants.json 里的商人 id；为空表示只聊天不卖货。
        public string Merchant { get; }
        public string Name { get; }
        public string Art { get; }
        public float X { get; }
        public float Y { get; }
        public float Height { get; }
        public float Radius { get; }
        public string Line { get; }
        public bool Blocks => Radius > 0f;
    }

    // 镇上的战斗遭遇点：走到跟前触发战斗（M4-04）。位置与 NPC 同语义——地平线坐标。
    public sealed class MapEncounter
    {
        public MapEncounter(string id, string enemy, string name, string line, float x, float y,
            string art = "", string tint = "", string shape = "ring")
        {
            Id = id; Enemy = enemy; Name = name; Line = line; X = x; Y = y;
            Art = art; Tint = tint; Shape = shape;
        }

        /// <summary>遭遇标记的立绘（T069）。空 = 该妖的图还没灌装，运行期画彩色标记，
        /// 而不是画一个和别的妖一模一样的黄方块。</summary>
        public string Art { get; }
        /// <summary>标记底色，"r,g,b" 三个 0~1 的数；空 = 默认金。</summary>
        public string Tint { get; }
        /// <summary>标记形状：ring（圆角方块，默认）/ diamond（菱形）/ plate（扁牌）。
        /// 三只妖必须「形状或配色至少一项不同」——同一种图形代表三种风险等级，
        /// 等于把选择权偷偷拿掉（T069 与 T055 同族：看不出来 = 没做）。</summary>
        public string Shape { get; }

        public string Id { get; }
        public string Enemy { get; }
        public string Name { get; }
        public string Line { get; }
        public float X { get; }
        public float Y { get; }
    }

    public sealed class MapLandmark
    {
        public MapLandmark(string id, string name, float x, float y)
        { Id = id; Name = name; X = x; Y = y; }

        public string Id { get; }
        public string Name { get; }
        public float X { get; }
        public float Y { get; }
    }

    public sealed class TownMap
    {
        public const float MinWalkWidth = 24f, MinWalkHeight = 24f;

        TownMap(string name, float artWidth, float artHeight, float spawnX, float spawnY, bool spawnFacingLeft,
            float perspectiveTop, float perspectiveBottom, List<MapRect> walkable, List<MapProp> props,
            List<MapLandmark> landmarks, List<MapNpc> npcs, List<MapEncounter> encounters,
            string phase, List<MapRect> terrainZones, List<string> terrainTypes,
            float worldWidth, float worldHeight, float chunkSize, List<MapChunk> chunks, List<MapRect> blocked,
            float worldScale, string overviewArt, string chunkArtTemplate, TownElevation elevation)
        {
            Name = name; ArtWidth = artWidth; ArtHeight = artHeight;
            SpawnX = spawnX; SpawnY = spawnY; SpawnFacingLeft = spawnFacingLeft;
            PerspectiveTop = perspectiveTop; PerspectiveBottom = perspectiveBottom;
            Walkable = walkable; Props = props; Landmarks = landmarks; Npcs = npcs; Encounters = encounters;
            Phase = phase; TerrainZones = terrainZones; TerrainTypes = terrainTypes;
            WorldWidth = worldWidth; WorldHeight = worldHeight; ChunkSize = chunkSize;
            Chunks = chunks; Blocked = blocked; WorldScale = worldScale; OverviewArt = overviewArt;
            foreach (var r in blocked)
            {
                for (int iy=(int)(r.MinY/128);iy<=(int)(r.MaxY/128);iy++)
                    for (int ix=(int)(r.MinX/128);ix<=(int)(r.MaxX/128);ix++)
                    {
                        int key=iy*1024+ix;
                        if (!blockedIndex.TryGetValue(key,out var bucket)) blockedIndex[key]=bucket=new List<MapRect>();
                        bucket.Add(r);
                    }
            }
            ChunkArtTemplate = chunkArtTemplate;
            Elevation = elevation;
            // 网格数由 chunks 派生，不再抄一遍配置——抄一遍就是多一个会脱节的真相。
            // chunks 缺省时退回世界尺寸换算（老配置没有 chunks 也能跑）。
            WorldColumns = chunks.Count > 0 && chunkSize > 0f
                ? chunks.Max(c => (int)(c.X / chunkSize)) + 1
                : (chunkSize > 0f ? (int)(worldWidth / chunkSize) : 0);
            WorldRows = chunks.Count > 0 && chunkSize > 0f
                ? chunks.Max(c => (int)(c.Y / chunkSize)) + 1
                : (chunkSize > 0f ? (int)(worldHeight / chunkSize) : 0);
        }

        readonly Dictionary<int,List<MapRect>> blockedIndex = new Dictionary<int,List<MapRect>>();
        IEnumerable<MapRect> NearbyBlocked(float x0,float y0,float x1,float y1)
        {
            var seen=new HashSet<MapRect>();
            for(int iy=(int)(Math.Min(y0,y1)/128);iy<=(int)(Math.Max(y0,y1)/128);iy++)
                for(int ix=(int)(Math.Min(x0,x1)/128);ix<=(int)(Math.Max(x0,x1)/128);ix++)
                    if(blockedIndex.TryGetValue(iy*1024+ix,out var list))
                        foreach(var r in list) if(seen.Add(r)) yield return r;
        }
        // Navigation refines narrow free strips using the same geometry registered for movement.
        // Returning boundaries (not a second occupancy map) keeps furniture and height limits authoritative.
        public IEnumerable<float> NavigationBoundaries(bool vertical, bool includeBlockedRects = true)
        {
            foreach (var value in Elevation.CriticalCoordinates(vertical)) yield return value;
            foreach(var c in FootCapsules())
            {
                yield return vertical?c.y-c.radius:c.x0-c.radius;
                yield return vertical?c.y+c.radius:c.x1+c.radius;
            }
            // This switch only chooses search coordinates; all collision queries still include Blocked.
            foreach (var rect in (includeBlockedRects ? Blocked : Enumerable.Empty<MapRect>()).Concat(ExternalBlockers()))
            {
                yield return vertical ? rect.MinY : rect.MinX;
                yield return vertical ? rect.MaxY : rect.MaxX;
            }
        }
        public TownElevation Elevation { get; }
        public bool CanTraverse(float x0, float y0, float x1, float y1) => CanTraverse(x0, y0, x1, y1, null);

        /// <summary>excludeNpcId 只跳过这一个 NPC 圆（查询者自身）；未知/空值不排除任何人。</summary>
        public bool CanTraverse(float x0, float y0, float x1, float y1, string excludeNpcId)
        {
            if (x1 < 0 || y1 < 0 || x1 > WorldWidth || y1 > WorldHeight) return false;
            // 线段与实体精确求交，窄门墩也不能被大 dt 或点击移动跨过。高度另按地面采样。
            foreach (var rect in NearbyBlocked(x0,y0,x1,y1))
                if (SegmentHits(rect,x0,y0,x1,y1)) return false;
            foreach (var rect in ExternalBlockers())
                if (SegmentHits(rect,x0,y0,x1,y1)) return false;
            foreach(var c in FootCapsules()) if(SegmentHitsCapsule(x0,y0,x1,y1,c)) return false;
            float dx=x1-x0,dy=y1-y0,length2=dx*dx+dy*dy;
            foreach (var (bx,by,radius) in Blockers(excludeNpcId))
            {
                float t=length2<=.0001f?0:Math.Max(0,Math.Min(1,((bx-x0)*dx+(by-y0)*dy)/length2));
                float ox=x0+dx*t-bx,oy=y0+dy*t-by;
                if (ox*ox+oy*oy<=radius*radius) return false;
            }
            return Elevation.CanTraverse(x0,y0,x1,y1,
                Walkable.Count>0 ? (Func<float,float,bool>)WalkableContains : null);
        }
        static bool SegmentHits(MapRect rect,float x0,float y0,float x1,float y1)
        {
            if (Math.Max(x0,x1)<rect.MinX || Math.Min(x0,x1)>rect.MaxX ||
                Math.Max(y0,y1)<rect.MinY || Math.Min(y0,y1)>rect.MaxY) return false;
            float t0=0,t1=1;
            return ClipAxis(x0,x1-x0,rect.MinX,rect.MaxX,ref t0,ref t1) &&
                ClipAxis(y0,y1-y0,rect.MinY,rect.MaxY,ref t0,ref t1);
        }
        static bool ClipAxis(float start,float delta,float low,float high,ref float t0,ref float t1)
        {
            if (Math.Abs(delta)<.000001f) return start>=low && start<=high;
            float a=(low-start)/delta,b=(high-start)/delta;
            t0=Math.Max(t0,Math.Min(a,b));t1=Math.Min(t1,Math.Max(a,b));
            return t0<=t1;
        }

        public string Name { get; }
        public float ArtWidth { get; }
        public float ArtHeight { get; }
        public float SpawnX { get; }
        public float SpawnY { get; }
        public bool SpawnFacingLeft { get; }
        // 鸟瞰透视：画顶（远）与画底（近）的角色缩放。
        public float PerspectiveTop { get; }
        public float PerspectiveBottom { get; }
        public IReadOnlyList<MapRect> Walkable { get; }
        public IReadOnlyList<MapProp> Props { get; }
        public IReadOnlyList<MapLandmark> Landmarks { get; }
        public IReadOnlyList<MapNpc> Npcs { get; }
        public IReadOnlyList<MapEncounter> Encounters { get; }

        // 场景阶段："terrain" = 地形底座阶段，ShowTown 不摆放 props/npcs/encounters
        // （它们的数据仍在本配置里，二期"建筑/环境立绘"阶段切回 "full" 直接启用）。
        public string Phase { get; }

        // 地形分区标注（河/山/田/镇区等矩形），本期是纯数据：坐标体系的一部分，
        // 供验收断言"河/山不可走"与二期建筑落位参考；不驱动任何运行期逻辑。
        public IReadOnlyList<MapRect> TerrainZones { get; }
        public IReadOnlyList<string> TerrainTypes { get; }

        // ---- 大世界语义（World Size 与 Camera View 严格分离）----
        /// <summary>整个世界的尺寸（世界单位）。远大于任何一屏可见范围。</summary>
        public float WorldWidth { get; }
        public float WorldHeight { get; }
        /// <summary>单块地图的世界边长。</summary>
        public float ChunkSize { get; }
        public IReadOnlyList<MapChunk> Chunks { get; }
        /// <summary>不可通行区域（山体、河面、岩石）。与 <see cref="Blocked"/> 语义：
        /// 落在其中即不可站立（河桥、渡口、山径由配置留出缺口）。</summary>
        public IReadOnlyList<MapRect> Blocked { get; }
        /// <summary>世界单位 → 画布像素。配合世界尺寸决定"一屏能看到多少世界"。</summary>
        public float WorldScale { get; }
        /// <summary>整幅世界的缩略图路径（小地图底图）。</summary>
        public string OverviewArt { get; }

        /// <summary>正式贴图的 Resources 路径模板，含 {x}/{y} 占位（Gate C1-C 起）。
        /// 为空 = 沿用 chunks[].art（一期灰盒），保持旧配置可运行。</summary>
        public string ChunkArtTemplate { get; }
        /// <summary>世界网格数，由 chunks 派生——不再另配一份，抄一遍就是多一个会脱节的真相。</summary>
        public int WorldColumns { get; }
        public int WorldRows { get; }

        /// <summary>某块该加载哪张贴图：配了模板就走模板（正式产物），否则回落该格的 chunks[].art（灰盒）。</summary>
        public string ChunkArtAt(int col, int row)
        {
            if (!string.IsNullOrEmpty(ChunkArtTemplate))
                return ChunkArtTemplate.Replace("{x}", col.ToString()).Replace("{y}", row.ToString());
            if (ChunkSize <= 0f) return null;
            var match = Chunks.FirstOrDefault(c =>
                (int)(c.X / ChunkSize) == col && (int)(c.Y / ChunkSize) == row);
            return match == null ? null : match.Art;
        }

        public bool WalkableContains(float x, float y) => Walkable.Any(rect => rect.Contains(x, y));

        // ---------------- 外部矩形阻挡注入口（ADR-033）----------------
        // 为什么是**轴对齐矩形**而不是圆：静态门禁 check-plot-overlap.py 的 base_rect 就是矩形
        // （[footX-w/2, footX+w/2] × [footY, footY + (radius>0 ? radius×2 : 24)]）。
        // 运行期若改用圆形，"校验说走廊够、实际走不过"必然回来——这正是 T018 同形状原则
        // 在碰撞上的形态。source 分域清场：物件层重绘时先清自己那一份再整体注入，
        // 不许累积（累积一次，世界里就多一层看不见的墙）。
        readonly Dictionary<string, List<MapRect>> externalBlockers = new Dictionary<string, List<MapRect>>();
        readonly Dictionary<string,List<(float x0,float x1,float y,float radius)>> footCapsules = new Dictionary<string,List<(float x0,float x1,float y,float radius)>>();

        // Small props rest on a horizontal capsule, not their entire sprite rectangle.
        public void RegisterFootCapsule(string source,float x0,float x1,float y,float radius)
        {
            if(string.IsNullOrEmpty(source) || radius<=0 || x1<x0) throw new ArgumentException("Invalid foot capsule");
            if(!footCapsules.TryGetValue(source,out var list)) footCapsules[source]=list=new List<(float,float,float,float)>();
            list.Add((x0,x1,y,radius));
        }
        IEnumerable<(float x0,float x1,float y,float radius)> FootCapsules()
        { foreach(var list in footCapsules.Values) foreach(var c in list) yield return c; }
        static float Distance2ToSegment(float x,float y,float ax,float ay,float bx,float by)
        {
            float dx=bx-ax,dy=by-ay,length=dx*dx+dy*dy;
            float t=length<.000001f?0:Math.Max(0,Math.Min(1,((x-ax)*dx+(y-ay)*dy)/length));
            float ox=x-ax-t*dx,oy=y-ay-t*dy;return ox*ox+oy*oy;
        }
        static bool SegmentHitsCapsule(float ax,float ay,float bx,float by,(float x0,float x1,float y,float radius) c)
        {
            if(Math.Abs(by-ay)>.000001f){float t=(c.y-ay)/(by-ay),x=ax+t*(bx-ax);if(t>=0&&t<=1&&x>=c.x0&&x<=c.x1)return true;}
            float d=Math.Min(Math.Min(Distance2ToSegment(ax,ay,c.x0,c.y,c.x1,c.y),Distance2ToSegment(bx,by,c.x0,c.y,c.x1,c.y)),
                Math.Min(Distance2ToSegment(c.x0,c.y,ax,ay,bx,by),Distance2ToSegment(c.x1,c.y,ax,ay,bx,by)));
            return d<=c.radius*c.radius;
        }

        /// <summary>注入一个外部矩形阻挡。坐标是世界单位，min/max 语义，与 blocked 光栅同形。</summary>
        public void RegisterBlocker(string source, float xMin, float yMin, float xMax, float yMax)
        {
            if (string.IsNullOrEmpty(source)) return;
            if (xMax <= xMin || yMax <= yMin) return;      // 空矩形不配当墙，静默收下就是给未来留幽灵
            List<MapRect> list;
            if (!externalBlockers.TryGetValue(source, out list))
                externalBlockers[source] = list = new List<MapRect>();
            list.Add(new MapRect(xMin, yMin, xMax - xMin, yMax - yMin));
        }

        /// <summary>清掉某个来源注入的全部矩形（重绘前必调）。</summary>
        public void ClearBlockers(string source)
        {
            if (!string.IsNullOrEmpty(source)) { externalBlockers.Remove(source); footCapsules.Remove(source); }
        }

        /// <summary>验收用：某来源当前注入了多少个阻挡矩形。</summary>
        public int ExternalBlockerCount(string source)
        {
            List<MapRect> list;
            return (externalBlockers.TryGetValue(source, out list) ? list.Count : 0) +
                (footCapsules.TryGetValue(source,out var capsules)?capsules.Count:0);
        }

        IEnumerable<MapRect> ExternalBlockers()
        {
            foreach (var kv in externalBlockers)
                foreach (var rect in kv.Value)
                    yield return rect;
        }

        // 站立判定（世界语义）：世界边界内 ∧ 不在不可通行区 ∧ 不撞立绘/NPC 障碍 ∧
        // （若配置了 walkable 白名单则还要落在白名单内；白名单为空 = 除 blocked 外皆可走）。
        // 判定顺序是 ADR-033 钉死的：bounds → blocked → blockers（含外部矩形）→ walkable。
        // 顺序变了，"白名单优先还是障碍优先"就会在两处门禁里得出不同结论。
        public bool CanStand(float x, float y) => CanStand(x, y, null);

        public bool CanStand(float x, float y, string excludeNpcId)
        {
            if (x < 0f || y < 0f || x > WorldWidth || y > WorldHeight) return false;
            if (blockedIndex.TryGetValue((int)(y/128)*1024+(int)(x/128),out var bucket) && bucket.Any(rect=>rect.Contains(x,y))) return false;
            foreach (var (blockX, blockY, blockRadius) in Blockers(excludeNpcId))
            {
                var dx = x - blockX; var dy = y - blockY;
                if (dx * dx + dy * dy <= blockRadius * blockRadius) return false;
            }
            foreach (var rect in ExternalBlockers())
                if (rect.Contains(x, y)) return false;
            foreach(var c in FootCapsules()) if(Distance2ToSegment(x,y,c.x0,c.y,c.x1,c.y)<=c.radius*c.radius)return false;
            if (Walkable.Count > 0 && !Walkable.Any(rect => rect.Contains(x, y))) return false;
            return true;
        }

        // 点击落点钳到最近的可走处：先把点拉到最近的走廊矩形内，再从圆形障碍推出。
        public (float x, float y) ClampToWalkable(float x, float y) => ClampToWalkable(x, y, null);

        public (float x, float y) ClampToWalkable(float x, float y, string excludeNpcId)
        {
            var bestX = x;
            var bestY = y;
            var bestDistance = float.MaxValue;
            foreach (var rect in Walkable)
            {
                var candidateX = Math.Min(Math.Max(x, rect.MinX), rect.MaxX);
                var candidateY = Math.Min(Math.Max(y, rect.MinY), rect.MaxY);
                var distance = (candidateX - x) * (candidateX - x) + (candidateY - y) * (candidateY - y);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                bestX = candidateX;
                bestY = candidateY;
            }
            for (var pass = 0; pass < 2; pass++)
            {
                foreach (var (blockX, blockY, blockRadius) in Blockers(excludeNpcId))
                {
                    var offsetX = bestX - blockX;
                    var offsetY = bestY - blockY;
                    var length = (float)Math.Sqrt(offsetX * offsetX + offsetY * offsetY);
                    if (length >= blockRadius || length <= .001f) continue;
                    bestX = blockX + offsetX / length * (blockRadius + 3f);
                    bestY = blockY + offsetY / length * (blockRadius + 3f);
                }
                foreach(var c in FootCapsules())
                {
                    float cx=Math.Max(c.x0,Math.Min(c.x1,bestX)),dx=bestX-cx,dy=bestY-c.y;
                    float distance=(float)Math.Sqrt(dx*dx+dy*dy);
                    if(distance>c.radius)continue;
                    if(distance<.001f){bestY=c.y+c.radius+3;continue;}
                    bestX=cx+dx/distance*(c.radius+3);bestY=c.y+dy/distance*(c.radius+3);
                }
                // 外部矩形也要推得出：点房子那侧时应当"绕到门口"，而不是原地不动。
                // 不推的后果不是崩，是玩家点了没反应——最难归因的那类手感 bug。
                foreach (var rect in ExternalBlockers())
                {
                    if (!rect.Contains(bestX, bestY)) continue;
                    var left = bestX - rect.MinX;
                    var right = rect.MaxX - bestX;
                    var up = bestY - rect.MinY;
                    var down = rect.MaxY - bestY;
                    var min = Math.Min(Math.Min(left, right), Math.Min(up, down));
                    if (min == left) bestX = rect.MinX - 3f;
                    else if (min == right) bestX = rect.MaxX + 3f;
                    else if (min == up) bestY = rect.MinY - 3f;
                    else bestY = rect.MaxY + 3f;
                }
            }
            return (bestX, bestY);
        }

        // ---------------- NPC 动态圆心（仅本次 Town 会话） ----------------
        // 配置里的 MapNpc.X/Y 是日程锚点，不是实时位置：巡游走后仍按旧坐标出圆，原地就留一堵
        // 空气墙；同理查询者自己的圆会挡住寻路起点。这里按原 NPC ID 覆盖本次城镇会话的圆心，
        // 不写存档、不进静态数据、不跨场景（TownMap 实例消失即消失）。
        readonly Dictionary<string, (float x, float y)> npcDynamicPositions =
            new Dictionary<string, (float x, float y)>(StringComparer.Ordinal);

        /// <summary>本次会话里当前有多少个 NPC 使用了动态圆心（验收用）。</summary>
        public int NpcDynamicPositionCount => npcDynamicPositions.Count;

        /// <summary>更新宿主实际位移成功后的 NPC 圆心。未知 ID、非有限值或出世界一律拒绝且不改状态。</summary>
        public bool SetNpcPosition(string npcId, float x, float y)
        {
            if (string.IsNullOrEmpty(npcId) || FindNpc(npcId) == null) return false;
            if (!Finite(x) || !Finite(y)) return false;
            if (x < 0f || y < 0f || x > WorldWidth || y > WorldHeight) return false;
            npcDynamicPositions[npcId] = (x, y);
            return true;
        }

        /// <summary>单个 NPC 恢复配置锚点。未知 ID 返回 false（没有实体可恢复）。</summary>
        public bool ClearNpcPosition(string npcId)
        {
            if (string.IsNullOrEmpty(npcId) || FindNpc(npcId) == null) return false;
            return npcDynamicPositions.Remove(npcId);
        }

        /// <summary>全部恢复配置锚点：离场时调用，避免下个场景残留旧圆心。</summary>
        public void ClearNpcPositions() => npcDynamicPositions.Clear();

        /// <summary>读当前圆心（动态优先，否则配置锚点）。未知 ID 返回 false。</summary>
        public bool TryNpcPosition(string npcId, out float x, out float y)
        {
            x = 0f; y = 0f;
            var npc = FindNpc(npcId);
            if (npc == null) return false;
            if (npcDynamicPositions.TryGetValue(npcId, out var live)) { x = live.x; y = live.y; return true; }
            x = npc.X; y = npc.Y; return true;
        }

        MapNpc FindNpc(string npcId)
        {
            foreach (var npc in Npcs) if (npc.Id == npcId) return npc;
            return null;
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        // 立绘与 NPC 都是挡路的圆：统一成 (x, y, r) 供站立判定与落点钳制共用。
        // excludeNpcId 只跳过这一个 ID（查询者自身）；未知/空值不排除任何人——宁可挡住，也不放行。
        IEnumerable<(float x, float y, float radius)> Blockers(string excludeNpcId)
        {
            foreach (var prop in Props)
                if (prop.Blocks) yield return (prop.X, prop.Y, prop.Radius);
            foreach (var npc in Npcs)
            {
                if (!npc.Blocks) continue;
                if (excludeNpcId != null && string.Equals(npc.Id, excludeNpcId, StringComparison.Ordinal)) continue;
                float bx = npc.X, by = npc.Y;
                if (npcDynamicPositions.TryGetValue(npc.Id, out var live)) { bx = live.x; by = live.y; }
                yield return (bx, by, npc.Radius);
            }
        }

        // 摄像机钳制：世界远大于一屏时，视口四边不得越出世界（否则露黑）。
        // 返回被夹过的"视口中心"（世界坐标）。世界比视口还小时居中显示，不产生黑边。
        public float ClampCameraX(float centerX, float viewHalfWidth) =>
            viewHalfWidth * 2f >= WorldWidth
                ? WorldWidth * .5f
                : Math.Min(Math.Max(centerX, viewHalfWidth), WorldWidth - viewHalfWidth);

        public float ClampCameraY(float centerY, float viewHalfHeight) =>
            viewHalfHeight * 2f >= WorldHeight
                ? WorldHeight * .5f
                : Math.Min(Math.Max(centerY, viewHalfHeight), WorldHeight - viewHalfHeight);

        /// <summary>一屏能看到的世界尺寸——用于验收断言"视野远小于世界"。</summary>
        public (float width, float height) ViewWorldSize(float viewWidth, float viewHeight, float scale) =>
            (viewWidth / scale, viewHeight / scale);

        public static TownMap Parse(string json)
        {
            if (!Json.TryParse(json, out var root, out var error))
                throw new FormatException("城镇地图读取失败：" + error);
            if (root.kind != JsonValue.Kind.Object) throw new FormatException("城镇地图应是一个对象。");

            var artWidth = Number(root, "artWidth", 1920f);
            var artHeight = Number(root, "artHeight", 1080f);
            if (artWidth < 320f || artHeight < 180f) throw new FormatException("地图画布尺寸不合理。");

            var walkable = new List<MapRect>();
            foreach (var entry in Elements(root, "walkable"))
            {
                var rect = new MapRect(Number(entry, "x", 0f), Number(entry, "y", 0f),
                    Number(entry, "width", 0f), Number(entry, "height", 0f));
                if (rect.Width < MinWalkWidth || rect.Height < MinWalkHeight)
                    throw new FormatException("可行走矩形过小：" + (entry["comment"].AsString(rect.X + "," + rect.Y)));
                if (rect.MinX < 0f || rect.MinY < 0f || rect.MaxX > artWidth || rect.MaxY > artHeight)
                    throw new FormatException("可行走矩形超出画布：" + (entry["comment"].AsString(rect.X + "," + rect.Y)));
                walkable.Add(rect);
            }
            // 大世界模式（配置了 blocked 或分块）：白名单可空——"除不可通行区外皆可走"。
            if (walkable.Count == 0 && root["blocked"] == null && root["chunks"] == null && root["blocked"] == null)
                throw new FormatException("地图至少要有一条可行走走廊，或声明 blocked 不可通行区。");

            var props = new List<MapProp>();
            foreach (var entry in Elements(root, "props"))
            {
                var slug = Required(entry, "slug");
                var height = Number(entry, "height", 0f);
                if (height <= 0f) throw new FormatException("立绘 " + slug + " 的高度应为正数。");
                props.Add(new MapProp(slug, Number(entry, "x", 0f), Number(entry, "y", 0f), height, Number(entry, "radius", 0f)));
            }

            // 世界尺寸必须先于任何坐标校验拿到：大世界配置不写 artWidth/artHeight（解析默认
            // 1920×1080），拿画布尺度量 8192 世界的坐标会把合法遭遇点判成"超出画布"，
            // 整张地图直接加载失败。未配置 worldWidth 时退回旧语义（整张图 = 世界）。
            var worldWidth = Number(root, "worldWidth", artWidth);
            var worldHeight = Number(root, "worldHeight", artHeight);

            var landmarks = new List<MapLandmark>();
            foreach (var entry in Elements(root, "landmarks"))
                landmarks.Add(new MapLandmark(Required(entry, "id"), Required(entry, "name"),
                    Number(entry, "x", 0f), Number(entry, "y", 0f)));

            var npcs = new List<MapNpc>();
            foreach (var entry in Elements(root, "npcs"))
            {
                var height = Number(entry, "height", 0f);
                if (height <= 0f) throw new FormatException("NPC " + Required(entry, "id") + " 的高度应为正数。");
                npcs.Add(new MapNpc(Required(entry, "id"), entry["merchant"].AsString(""),
                    Required(entry, "name"), Required(entry, "art"),
                    Number(entry, "x", 0f), Number(entry, "y", 0f), height,
                    Number(entry, "radius", 0f), entry["line"].AsString("")));
            }
            if (npcs.Any(n => npcs.Count(o => o.Id == n.Id) > 1))
                throw new FormatException("NPC id 重复。");

            var encounters = new List<MapEncounter>();
            foreach (var entry in Elements(root, "encounters"))
            {
                var encounterId = Required(entry, "id");
                if (encounters.Any(e => e.Id == encounterId))
                    throw new FormatException("遭遇点 id 重复：" + encounterId);
                var ex = Number(entry, "x", 0f);
                var ey = Number(entry, "y", 0f);
                // 校验用世界尺寸而非画布：见上方 worldWidth 提前解析的注释。
                // 文案保留"超出画布"是契约——M4Checks 断言这段文字，改字会打掉一条既有断言。
                if (ex < 0f || ey < 0f || ex > worldWidth || ey > worldHeight)
                    throw new FormatException("遭遇点 " + encounterId + " 超出画布。");
                encounters.Add(new MapEncounter(encounterId, Required(entry, "enemy"), Required(entry, "name"),
                    entry["line"].AsString(""), ex, ey,
                    Text(entry, "art", ""), Text(entry, "tint", ""), Text(entry, "shape", "ring")));
            }

            var overviewArt = "";
            var blocked = new List<MapRect>();
            foreach (var entry in Elements(root, "blocked"))
            {
                var rect = new MapRect(Number(entry, "x", 0f), Number(entry, "y", 0f),
                    Number(entry, "width", 0f), Number(entry, "height", 0f));
                if (rect.Width <= 0f || rect.Height <= 0f)
                    throw new FormatException("不可通行区尺寸应为正：" + entry["why"].AsString("blocked"));
                blocked.Add(rect);
            }

            var spawnX = root["spawn"]?["x"].AsFloat(0f) ?? 0f;
            var spawnY = root["spawn"]?["y"].AsFloat(0f) ?? 0f;
            if (blocked.Any(rect => rect.Contains(spawnX, spawnY)) ||
                !ZoneContains(walkable, props, spawnX, spawnY))
                throw new FormatException("出生点不在可行走区域内。");

            // 地形分区标注（纯数据）：type + 矩形，供验收与二期建筑落位参考。
            var terrainZones = new List<MapRect>();
            var terrainTypes = new List<string>();
            foreach (var entry in Elements(root, "terrain"))
            {
                terrainTypes.Add(Required(entry, "type"));
                terrainZones.Add(new MapRect(Number(entry, "x", 0f), Number(entry, "y", 0f),
                    Number(entry, "width", 0f), Number(entry, "height", 0f)));
            }
            if (terrainZones.Count != terrainTypes.Count)
                throw new FormatException("terrain 分区数据不一致。");

            // ---- 大世界：分块 / 不可通行区 ----
            // worldWidth / worldHeight 已在 landmarks 之前解析（坐标校验要用它），此处不重复取。
            var chunkSize = Number(root, "chunkSize", 0f);
            var chunks = new List<MapChunk>();
            foreach (var entry in Elements(root, "chunks"))
            {
                var art = Required(entry, "art");
                var cx = Number(entry, "x", 0f);
                var cy = Number(entry, "y", 0f);
                var cw = Number(entry, "width", chunkSize);
                var chh = Number(entry, "height", chunkSize);
                if (cx < 0f || cy < 0f || cx + cw > worldWidth + 1f || cy + chh > worldHeight + 1f)
                    throw new FormatException("地图块越出世界：" + Required(entry, "id"));
                chunks.Add(new MapChunk(Required(entry, "id"), art, entry["overview"].AsString(""),
                    cx, cy, cw, chh));
                if (overviewArt == "" ) overviewArt = entry["overview"].AsString("");
            }

            // ---- 正式贴图路径模板（Gate C1-C）----
            // 配了模板就走模板，chunks[].art 退为兼容回落。占位符缺失是配置错误，
            // 解析期就拒：运行期静默加载失败表现为"世界凭空少一块"，比当场报错难查得多。
            var chunkArtTemplate = root["chunkArtTemplate"].AsString("");
            if (chunkArtTemplate != "")
            {
                if (!chunkArtTemplate.Contains("{x}") || !chunkArtTemplate.Contains("{y}"))
                    throw new FormatException("chunkArtTemplate 必须同时含 {x} 与 {y} 占位：" + chunkArtTemplate);
                if (chunkArtTemplate.StartsWith("Assets/Resources/") || chunkArtTemplate.EndsWith(".png"))
                    throw new FormatException("chunkArtTemplate 须是 Resources 相对路径" +
                        "（不带 Assets/Resources/ 前缀、不带 .png）：" + chunkArtTemplate);
            }
            // 小地图底图属于世界，不属于某一块：顶层 overviewArt 优先于 chunks[].overview。
            var topOverview = root["overviewArt"].AsString("");
            if (topOverview != "") overviewArt = topOverview;

            return new TownMap(root["name"].AsString("无名之地"), artWidth, artHeight, spawnX, spawnY,
                root["spawnFacingLeft"].AsBool(false), Number(root, "perspectiveTop", .72f), Number(root, "perspectiveBottom", 1.05f),
                walkable, props, landmarks, npcs, encounters,
                root["phase"].AsString("full"), terrainZones, terrainTypes,
                worldWidth, worldHeight, chunkSize, chunks, blocked,
                Number(root, "worldScale", 1f), overviewArt, chunkArtTemplate, TownElevation.Parse(root));
        }

        // 白名单为空 = 大世界模式（除 blocked 外皆可走），此时不做白名单判定。
        static bool ZoneContains(List<MapRect> walkable, List<MapProp> props, float x, float y)
        {
            if (walkable.Count > 0 && !walkable.Any(rect => rect.Contains(x, y))) return false;
            return !props.Any(prop => prop.Radius > 0f &&
                (x - prop.X) * (x - prop.X) + (y - prop.Y) * (y - prop.Y) <= prop.Radius * prop.Radius);
        }

        static IEnumerable<JsonValue> Elements(JsonValue parent, string key)
        {
            var array = parent[key];
            if (array == null || array.IsNull) yield break;
            if (array.kind != JsonValue.Kind.Array) throw new FormatException("字段 " + key + " 应是数组。");
            foreach (var item in array.Items)
            {
                if (item == null || item.kind != JsonValue.Kind.Object)
                    throw new FormatException("字段 " + key + " 的元素应是对象。");
                yield return item;
            }
        }

        static string Text(JsonValue entry, string key, string fallback)
        {
            if (!entry.Has(key)) return fallback;
            var value = entry[key];
            if (value == null || value.IsNull) return fallback;
            if (value.kind != JsonValue.Kind.String) throw new FormatException("字段 " + key + " 应是字符串。");
            return value.AsString(fallback);
        }

        static float Number(JsonValue entry, string key, float fallback)
        {
            var value = entry[key];
            if (value == null || value.IsNull) return fallback;
            if (value.kind != JsonValue.Kind.Number) throw new FormatException("字段 " + key + " 应是数字。");
            return value.AsFloat(fallback);
        }

        static string Required(JsonValue entry, string key)
        {
            var value = entry[key].AsString(null);
            if (string.IsNullOrWhiteSpace(value)) throw new FormatException("缺少字段 " + key + "（应为非空字符串）。");
            return value;
        }
    }
}
