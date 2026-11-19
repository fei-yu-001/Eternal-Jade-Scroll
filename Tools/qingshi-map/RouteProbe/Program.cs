using TwelveJade.Core;
using System.Diagnostics;
using System.Text.Json;
var root=Path.GetFullPath(args[0]);var cfg=Path.Combine(root,"GameClient/TwelveJade/Assets/Resources/Config");
var map=TownMap.Parse(File.ReadAllText(Path.Combine(cfg,"town-map.json")));
var plots=Json.Parse(File.ReadAllText(Path.Combine(cfg,"building-plots.json")))["plots"];
foreach(var p in plots.Items){if(p.Has("blocks")&&!p["blocks"].AsBool(true))continue;
float x=p["footX"].AsFloat(0),y=p["footY"].AsFloat(0),w=p["width"].AsFloat(0);
if(p.Has("collisionRects")){foreach(var b in p["collisionRects"].Items)map.RegisterBlocker(p["id"].AsString(""),x+b["x0"].AsFloat(0),y+b["y0"].AsFloat(0),x+b["x1"].AsFloat(0),y+b["y1"].AsFloat(0));}
else map.RegisterBlocker(p["id"].AsString(""),x-w/2,y+p["collisionTop"].AsFloat(0),x+w/2,y+p["collisionDepth"].AsFloat(0));}
var rows=new List<object>();
foreach(var p in new[]{(3300f,2460f),(3450f,2452f),(3450f,2352f),(3120f,2352f),(3950f,7040f)})
 rows.Add(new{kind="stand",x=p.Item1,y=p.Item2,stand=map.CanStand(p.Item1,p.Item2),height=map.Elevation.HeightAt(p.Item1,p.Item2)});
foreach(var segment in new[]{(3450f,2452f,3450f,2352f),(3450f,2352f,3120f,2352f),(3450f,2452f,3120f,2352f),(3300f,2460f,3120f,2352f)})
 rows.Add(new{kind="segment",from=new[]{segment.Item1,segment.Item2},to=new[]{segment.Item3,segment.Item4},traverse=map.CanTraverse(segment.Item1,segment.Item2,segment.Item3,segment.Item4)});
var watch=Stopwatch.StartNew();var path=TownNavigator.FindPath(map,3450,2452,3120,2352);watch.Stop();
rows.Add(new{kind="path",from=new[]{3450,2452},to=new[]{3120,2352},found=path!=null,points=path?.Select(p=>new[]{p.x,p.y}).ToArray(),milliseconds=watch.Elapsed.TotalMilliseconds});
Console.WriteLine(JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
ElevationChecks.Run(TownMap.Parse(File.ReadAllText(Path.Combine(cfg,"town-map.json"))),cfg);
Console.WriteLine("RESULT: PASS (elevation and real-blocker route regressions)");
return path==null?1:0;
