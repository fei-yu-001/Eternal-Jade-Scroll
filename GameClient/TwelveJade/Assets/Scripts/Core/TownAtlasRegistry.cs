using System;
using System.Collections.Generic;
namespace TwelveJade.Core
{
    // The local atlas describes built QingShi town, not fictional geography from an illustrative background.
    public static class TownAtlasRegistry
    {
        public static IReadOnlyList<MapLandmark> Places(TownMap map,string plotsJson)
        {
            if(map==null)throw new ArgumentNullException(nameof(map));
            if(!Json.TryParse(plotsJson,out var root,out var error))throw new FormatException(error);
            var rows=root["plots"];if(rows.kind!=JsonValue.Kind.Array)throw new FormatException("Atlas: plots must be an array.");
            var places=new List<MapLandmark>();var ids=new HashSet<string>();
            foreach(var item in rows.Items)
            {
                if(item["kind"].AsString("")!="building")continue;
                string id=item["id"].AsString(""),name=item["name"].AsString("");
                float x=item["footX"].AsFloat(float.NaN),top=item["footY"].AsFloat(float.NaN);
                float offset=(item.Has("groundFootOffset")?item["groundFootOffset"]:item["height"]).AsFloat(float.NaN),y=top+offset;
                if(string.IsNullOrWhiteSpace(id)||string.IsNullOrWhiteSpace(name)||!ids.Add(id)||float.IsNaN(x)||float.IsNaN(y)||
                    float.IsInfinity(x)||float.IsInfinity(y)||x<0||y<0||x>map.WorldWidth||y>map.WorldHeight||offset<=0)
                    throw new FormatException("Atlas: invalid building ground coordinate "+id);
                places.Add(new MapLandmark(id,name,x,y));
            }
            foreach(var place in map.Landmarks)
                if(place.Id=="river-north"||place.Id=="back-mountain"||place.Id=="outskirts")places.Add(place);
            return places;
        }
    }
}
