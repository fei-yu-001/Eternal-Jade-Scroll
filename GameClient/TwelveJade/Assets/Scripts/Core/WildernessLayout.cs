using System;
using System.Collections.Generic;
using System.Linq;
namespace TwelveJade.Core
{
    public sealed class WildSpawn
    {
        public string Encounter, Walk; public float Roam, Leash, Aggro, Speed, Height;
    }
    public sealed class WildRegion
    {
        public string Id, Name, Art; public float X, Y, Radius, EntranceX, EntranceY, Width, Height;
    }
    public sealed class MineNode
    {
        public string Id, Name, Item, Art, SpentArt; public int Count;
        public float X, Y, Seconds, Height;
    }
    // One configuration supplies visible places, navigation destinations and gathering rules.
    public sealed class WildernessLayout
    {
        public readonly List<WildSpawn> Spawns = new List<WildSpawn>();
        public readonly List<WildRegion> Decorations = new List<WildRegion>();
        public readonly List<WildRegion> Regions = new List<WildRegion>();
        public readonly List<MineNode> Mines = new List<MineNode>();
        public readonly List<MapRect> Blockers = new List<MapRect>();
        public static WildernessLayout Parse(string source)
        {
            if (!Json.TryParse(source, out var root, out var error) || root.kind != JsonValue.Kind.Object)
                throw new FormatException("野外配置有误：" + error);
            var result = new WildernessLayout(); var ids = new HashSet<string>();
            foreach (var row in Rows(root, "spawns"))
            {
                var s = new WildSpawn { Encounter=Text(row,"encounter"), Walk=Text(row,"walk"), Roam=Number(row,"roamRadius"),
                    Leash=Number(row,"leashRadius"), Aggro=Number(row,"aggroRadius"), Speed=Number(row,"speed"), Height=Number(row,"height") };
                if (!ids.Add(s.Encounter) || s.Leash<s.Roam || s.Aggro>s.Leash) throw new FormatException("野怪活动范围无效");
                result.Spawns.Add(s);
            }
            ids.Clear();
            foreach (var row in Rows(root,"regions"))
            {
                var s=new WildRegion { Id=Text(row,"id"), Name=Text(row,"name"), X=Number(row,"x"), Y=Number(row,"y"),
                    Radius=Number(row,"radius"), EntranceX=Number(row,"entranceX"), EntranceY=Number(row,"entranceY"),
                    Art=row["art"].AsString(""), Width=row["width"].AsFloat(0), Height=row["height"].AsFloat(0) };
                if (!ids.Add(s.Id) || (!string.IsNullOrEmpty(s.Art) && (s.Width<=0 || s.Height<=0))) throw new FormatException("野外区域重复或尺寸无效");
                result.Regions.Add(s);
            }
            foreach(var row in Rows(root,"decorations"))result.Decorations.Add(new WildRegion{Id=Text(row,"id"),Art=Text(row,"art"),X=Number(row,"x"),Y=Number(row,"y"),Height=Number(row,"height")});
            ids.Clear();
            foreach (var row in Rows(root,"mines"))
            {
                var s=new MineNode {Id=Text(row,"id"),Name=Text(row,"name"),Item=Text(row,"item"),Art=Text(row,"art"),SpentArt=Text(row,"spentArt"),
                    X=Number(row,"x"),Y=Number(row,"y"),Count=(int)Number(row,"count"),Seconds=Number(row,"seconds"),Height=Number(row,"height")};
                if (!ids.Add(s.Id) || s.Id.Length>48 || s.Count<1 || s.Count>10 || s.Seconds<.5f) throw new FormatException("矿点配置无效");
                result.Mines.Add(s);
            }
            foreach(var row in Rows(root,"domainBlockers")) result.Blockers.Add(new MapRect(Number(row,"x"),Number(row,"y"),Number(row,"width"),Number(row,"height")));
            return result;
        }
        static IEnumerable<JsonValue> Rows(JsonValue root,string key)
        {
            var rows=root[key]; if(rows.kind!=JsonValue.Kind.Array) throw new FormatException(key+" 应为数组");
            foreach(var row in rows.Items) { if(row.kind!=JsonValue.Kind.Object)throw new FormatException(key+" 含非对象");yield return row; }
        }
        static string Text(JsonValue row,string key)
        { var s=row[key].AsString(""); if(string.IsNullOrWhiteSpace(s))throw new FormatException(key+" 不能为空");return s; }
        static float Number(JsonValue row,string key)
        { var v=row[key].AsFloat(float.NaN);if(float.IsNaN(v)||float.IsInfinity(v)||v<=0)throw new FormatException(key+" 应为正数");return v; }
    }
}
