using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using TwelveJade.Core;

public static class TownAtlasRegistrationChecks
{
    static void Check(string name,bool passed)
    { if(!passed)throw new Exception("FAIL: Local atlas "+name);Console.WriteLine("PASS: Local atlas "+name); }
    public static void Run(TownMap map,string configDir)
    {
        string source=File.ReadAllText(Path.Combine(configDir,"building-plots.json"));
        var places=TownAtlasRegistry.Places(map,source);
        using var plots=JsonDocument.Parse(source);
        var buildings=plots.RootElement.GetProperty("plots").EnumerateArray().Where(x=>x.GetProperty("kind").GetString()=="building").ToArray();
        Check("every actual building has one map registration",places.Count(p=>p.Id.StartsWith("plot-")||p.Id.StartsWith("shop-"))==buildings.Length);
        Check("labels use exact ground foot, never roof/top anchor",buildings.All(b=>{
            var p=places.Single(x=>x.Id==b.GetProperty("id").GetString());
            float offset=b.TryGetProperty("groundFootOffset",out var y)?y.GetSingle():b.GetProperty("height").GetSingle();
            return p.Name==b.GetProperty("name").GetString()&&Math.Abs(p.X-b.GetProperty("footX").GetSingle())<.001f&&Math.Abs(p.Y-(b.GetProperty("footY").GetSingle()+offset))<.001f;
        }));
        Check("no fictional illustration settlements",places.All(p=>buildings.Any(b=>b.GetProperty("id").GetString()==p.Id)||p.Id=="river-north"||p.Id=="back-mountain"||p.Id=="outskirts"));
        string fixture="{\"plots\":[{\"id\":\"real\",\"kind\":\"building\",\"name\":\"Real\",\"footX\":100,\"footY\":200,\"height\":600,\"groundFootOffset\":40}]}";
        Check("ground offset takes precedence over canvas height",TownAtlasRegistry.Places(map,fixture).First().Y==240);
        Check("legacy missing ground offset uses canvas height",TownAtlasRegistry.Places(map,fixture.Replace(",\"groundFootOffset\":40"," ")).First().Y==800);
        Check("non-building props do not become fabricated buildings",TownAtlasRegistry.Places(map,fixture.Replace("\"building\"","\"prop\"")).All(p=>p.Id!="real"));
        foreach(var invalid in new[]{fixture.Replace("\"footX\":100","\"footX\":-1"),fixture.Replace("\"footY\":200","\"footY\":9000"),fixture.Replace("\"groundFootOffset\":40","\"groundFootOffset\":0"),"{\"plots\":{}}"})
        {bool rejected=false;try{TownAtlasRegistry.Places(map,invalid);}catch(FormatException){rejected=true;}Check("invalid chart coordinate rejected",rejected);}
        var projection=new TownAtlasProjection(map.WorldWidth,map.WorldHeight,1456,1456);
        Check("all map coordinates round trip at uniform scale",places.All(p=>{var at=projection.Project(p.X,p.Y);var back=projection.World(at.x,at.y);return Math.Abs(back.x-p.X)<.002&&Math.Abs(back.y-p.Y)<.002;}));
        // This catches future runtime layout/art edits that would silently make the baked atlas inaccurate.
        string root=Path.GetFullPath(Path.Combine(configDir,"../../../../../"));
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(configDir,"qingshi-atlas-inputs.json")));
        var m=manifest.RootElement;
        Check("generated atlas bound to current authoritative config",m.GetProperty("configInputs").EnumerateArray().All(x=>Matches(root,x)));
        Check("actual building/tree artwork hashes match chart",m.GetProperty("artInputs").EnumerateArray().All(x=>Matches(root,x)));
        Check("decorative ink texture bound to reviewed source",Matches(root,m.GetProperty("styleInput")));
        string png=Path.GetFullPath(Path.Combine(configDir,"../Art/UI/Nov8/qingshi-town-atlas.png"));
        Check("runtime map equals reviewed chart bytes",Hash(png)==m.GetProperty("outputSHA256").GetString());
        Check("map extent equals actual playable QingShi world",m.GetProperty("worldSize")[0].GetSingle()==map.WorldWidth&&m.GetProperty("worldSize")[1].GetSingle()==map.WorldHeight);
        Check("all configured props represented in chart manifest",m.GetProperty("plots").GetArrayLength()==plots.RootElement.GetProperty("plots").GetArrayLength());
        // Painted art may change style; calibration must remain bound to the same live world.
        Check("painted source and user style bytes remain bound",m.GetProperty("paintingInputs").EnumerateArray().All(x=>Matches(root,x)));
        var calibration=m.GetProperty("calibration");
        var calibrated=calibration.GetProperty("buildings").EnumerateArray().ToArray();
        Check("all real building feet have unique painted calibration",calibrated.Length==buildings.Length
            &&calibrated.Select(x=>x.GetProperty("id").GetString()).Distinct().Count()==buildings.Length
            &&buildings.All(b=>calibrated.Any(c=>c.GetProperty("id").GetString()==b.GetProperty("id").GetString())));
        Check("painted calibration retains authoritative ground and uniform projection",calibrated.All(c=>{
            var registered=places.Single(x=>x.Id==c.GetProperty("id").GetString());
            var world=c.GetProperty("targetWorld");var pixel=c.GetProperty("targetPixel");
            return Math.Abs(world[0].GetDouble()-registered.X)<.002&&Math.Abs(world[1].GetDouble()-registered.Y)<.002
                &&Math.Abs(pixel[0].GetDouble()-registered.X/map.WorldWidth*m.GetProperty("canvas")[0].GetDouble())<.002
                &&Math.Abs(pixel[1].GetDouble()-registered.Y/map.WorldHeight*m.GetProperty("canvas")[1].GetDouble())<.002;
        }));
        using var townConfig=JsonDocument.Parse(File.ReadAllText(Path.Combine(configDir,"town-map.json")));
        var realBridges=townConfig.RootElement.GetProperty("terrainShape").GetProperty("bridges").EnumerateArray().ToArray();
        var calibratedBridges=calibration.GetProperty("bridges").EnumerateArray().ToArray();
        Check("painted bridge registration matches real crossing centres",calibratedBridges.Length==realBridges.Length
            &&realBridges.All(b=>calibratedBridges.Count(c=>Math.Abs(c.GetProperty("worldCenter")[0].GetDouble()-b.GetProperty("x").GetDouble())<.002
                &&Math.Abs(c.GetProperty("worldCenter")[1].GetDouble()-map.Elevation.RiverY(b.GetProperty("x").GetSingle()))<.01)==1));
    }
    static bool Matches(string root,JsonElement item)=>Hash(Path.Combine(root,item.GetProperty("path").GetString()))==item.GetProperty("sha256").GetString();
    static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
