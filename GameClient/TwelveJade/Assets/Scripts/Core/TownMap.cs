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
        public MapEncounter(string id, string enemy, string name, string line, float x, float y)
        {
            Id = id; Enemy = enemy; Name = name; Line = line; X = x; Y = y;
        }

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
            float worldScale, string overviewArt)
        {
            Name = name; ArtWidth = artWidth; ArtHeight = artHeight;
            SpawnX = spawnX; SpawnY = spawnY; SpawnFacingLeft = spawnFacingLeft;
            PerspectiveTop = perspectiveTop; PerspectiveBottom = perspectiveBottom;
            Walkable = walkable; Props = props; Landmarks = landmarks; Npcs = npcs; Encounters = encounters;
            Phase = phase; TerrainZones = terrainZones; TerrainTypes = terrainTypes;
            WorldWidth = worldWidth; WorldHeight = worldHeight; ChunkSize = chunkSize;
            Chunks = chunks; Blocked = blocked; WorldScale = worldScale; OverviewArt = overviewArt;
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

        public bool WalkableContains(float x, float y) => Walkable.Any(rect => rect.Contains(x, y));

        // 站立判定（世界语义）：世界边界内 ∧ 不在不可通行区 ∧ 不撞立绘/NPC 障碍 ∧
        // （若配置了 walkable 白名单则还要落在白名单内；白名单为空 = 除 blocked 外皆可走）。
        public bool CanStand(float x, float y)
        {
            if (x < 0f || y < 0f || x > WorldWidth || y > WorldHeight) return false;
            if (Blocked.Any(rect => rect.Contains(x, y))) return false;
            foreach (var (blockX, blockY, blockRadius) in Blockers())
            {
                var dx = x - blockX; var dy = y - blockY;
                if (dx * dx + dy * dy <= blockRadius * blockRadius) return false;
            }
            if (Walkable.Count > 0 && !Walkable.Any(rect => rect.Contains(x, y))) return false;
            return true;
        }

        // 点击落点钳到最近的可走处：先把点拉到最近的走廊矩形内，再从圆形障碍推出。
        public (float x, float y) ClampToWalkable(float x, float y)
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
                foreach (var (blockX, blockY, blockRadius) in Blockers())
                {
                    var offsetX = bestX - blockX;
                    var offsetY = bestY - blockY;
                    var length = (float)Math.Sqrt(offsetX * offsetX + offsetY * offsetY);
                    if (length >= blockRadius || length <= .001f) continue;
                    bestX = blockX + offsetX / length * (blockRadius + 3f);
                    bestY = blockY + offsetY / length * (blockRadius + 3f);
                }
            }
            return (bestX, bestY);
        }

        // 立绘与 NPC 都是挡路的圆：统一成 (x, y, r) 供站立判定与落点钳制共用。
        IEnumerable<(float x, float y, float radius)> Blockers()
        {
            foreach (var prop in Props)
                if (prop.Blocks) yield return (prop.X, prop.Y, prop.Radius);
            foreach (var npc in Npcs)
                if (npc.Blocks) yield return (npc.X, npc.Y, npc.Radius);
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
                if (ex < 0f || ey < 0f || ex > artWidth || ey > artHeight)
                    throw new FormatException("遭遇点 " + encounterId + " 超出画布。");
                encounters.Add(new MapEncounter(encounterId, Required(entry, "enemy"), Required(entry, "name"),
                    entry["line"].AsString(""), ex, ey));
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

            // ---- 大世界：世界尺寸 / 分块 / 不可通行区 ----
            // 未配置 worldWidth 时退回旧语义（整张图 = 世界），保证旧配置仍能解析。
            var worldWidth = Number(root, "worldWidth", artWidth);
            var worldHeight = Number(root, "worldHeight", artHeight);
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

            return new TownMap(root["name"].AsString("无名之地"), artWidth, artHeight, spawnX, spawnY,
                root["spawnFacingLeft"].AsBool(false), Number(root, "perspectiveTop", .72f), Number(root, "perspectiveBottom", 1.05f),
                walkable, props, landmarks, npcs, encounters,
                root["phase"].AsString("full"), terrainZones, terrainTypes,
                worldWidth, worldHeight, chunkSize, chunks, blocked,
                Number(root, "worldScale", 1f), overviewArt);
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
