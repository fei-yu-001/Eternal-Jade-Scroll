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
            float perspectiveTop, float perspectiveBottom, List<MapRect> walkable, List<MapProp> props, List<MapLandmark> landmarks)
        {
            Name = name; ArtWidth = artWidth; ArtHeight = artHeight;
            SpawnX = spawnX; SpawnY = spawnY; SpawnFacingLeft = spawnFacingLeft;
            PerspectiveTop = perspectiveTop; PerspectiveBottom = perspectiveBottom;
            Walkable = walkable; Props = props; Landmarks = landmarks;
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

        public bool WalkableContains(float x, float y) => Walkable.Any(rect => rect.Contains(x, y));

        public bool CanStand(float x, float y)
        {
            if (!WalkableContains(x, y)) return false;
            return !Props.Any(prop => prop.Blocks && (x - prop.X) * (x - prop.X) + (y - prop.Y) * (y - prop.Y) <= prop.Radius * prop.Radius);
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
                foreach (var prop in Props)
                {
                    if (!prop.Blocks) continue;
                    var offsetX = bestX - prop.X;
                    var offsetY = bestY - prop.Y;
                    var length = (float)Math.Sqrt(offsetX * offsetX + offsetY * offsetY);
                    if (length >= prop.Radius || length <= .001f) continue;
                    bestX = prop.X + offsetX / length * (prop.Radius + 3f);
                    bestY = prop.Y + offsetY / length * (prop.Radius + 3f);
                }
            }
            return (bestX, bestY);
        }

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
            if (walkable.Count == 0) throw new FormatException("地图至少要有一条可行走走廊。");

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

            var spawnX = root["spawn"]?["x"].AsFloat(0f) ?? 0f;
            var spawnY = root["spawn"]?["y"].AsFloat(0f) ?? 0f;
            if (!ZoneContains(walkable, props, spawnX, spawnY))
                throw new FormatException("出生点不在可行走区域内。");

            return new TownMap(root["name"].AsString("无名之地"), artWidth, artHeight, spawnX, spawnY,
                root["spawnFacingLeft"].AsBool(false), Number(root, "perspectiveTop", .72f), Number(root, "perspectiveBottom", 1.05f),
                walkable, props, landmarks);
        }

        static bool ZoneContains(List<MapRect> walkable, List<MapProp> props, float x, float y)
        {
            if (!walkable.Any(rect => rect.Contains(x, y))) return false;
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
