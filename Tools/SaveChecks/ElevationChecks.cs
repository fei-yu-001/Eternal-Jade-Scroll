using System;
using System.IO;
using System.Linq;

namespace TwelveJade.Core
{
    static class ElevationChecks
    {
        static void Check(string name,bool good)
        {
            if(!good) throw new Exception("FAIL: [Elevation] "+name);
            Console.WriteLine("PASS: [Elevation] "+name);
        }
        public static void Run(TownMap town,string configDir)
        {
            var e=town.Elevation;
            Check("实图四级高差已加载", !e.IsFlat && e.StepHeight==24 && e.Regions.Count>=10);
            Check("宗祠旧台面回归平地", Math.Abs(e.HeightAt(3200,2100))<.01f);
            Check("观景旧台面回归平地", Math.Abs(e.HeightAt(5070,2100))<.01f);
            Check("南北主街 +1", Math.Abs(e.HeightAt(e.RoadX(5700),5700)-24)<.01f);
            Check("西市平地 0", Math.Abs(e.HeightAt(3100,4950))<.01f);
            Check("河床 -1", Math.Abs(e.HeightAt(4700,e.RiverY(4700))+24)<.01f);
            Check("桥在河面上方两级", Math.Abs(e.HeightAt(4236,e.RiverY(4236))-24)<.01f && !town.CanStand(4700,e.RiverY(4700)));
            Check("祠堂门前平地可达", town.CanTraverse(3450,2460,3450,2355));
            Check("祠堂门前平地可返回", town.CanTraverse(3450,2355,3450,2460));
            Check("旧观景空台区域可通行", town.CanTraverse(5070,2330,5070,2190));
            Check("删除台基不遗留高差护壁", town.CanTraverse(3000,2370,3000,2350) && town.CanTraverse(3000,2350,3000,2370));
            Check("旧宗祠地块无隐形高台高差", e.CanTraverse(2560,2100,3740,2100) && e.CanTraverse(3740,2100,2560,2100));
            Check("同级平地高差未变", Math.Abs(e.HeightAt(3000,5000))<.01f && Math.Abs(e.HeightAt(3050,5000))<.01f);
            // 旧路径已被搬迁后的摊主占据；先验实际同级，再测旁侧无NPC的平地路径。
            Check("同级平地可行走", Math.Abs(e.HeightAt(3100,4950)-e.HeightAt(3150,4950))<.01f && town.CanTraverse(3100,4950,3150,4950));
            Check("码头下沉 -1", Math.Abs(e.HeightAt(5100,2820)+24)<.01f);
            Check("渡口阶梯可双向行走", town.CanTraverse(5100,3020,5100,2850) && town.CanTraverse(5100,2850,5100,3020));
            Check("主街全段保持批准的500宽", Math.Abs(e.RoadHalf(6600)-250)<.01f && Math.Abs(e.RoadHalf(5700)-250)<.01f);
            Check("主桥含两端桥坡连续可通行", town.CanTraverse(4236,e.RiverY(4236)-400,4236,e.RiverY(4236)+400));
            Check("桥侧河水不得穿越", !town.CanTraverse(4700,e.RiverY(4700)-250,4700,e.RiverY(4700)+250));
            foreach(var p in new[]{(3200f,2100f),(5070f,2100f),(3450f,2400f),(3100f,4950f),(5100f,2930f),(4236f,e.RiverY(4236))})
                Check("点击反投影回地面 "+p.Item1+","+p.Item2,Math.Abs(e.UnprojectY(p.Item1,e.ProjectY(p.Item1,p.Item2))-p.Item2)<.02f);
            Check("旧地图无 elevation 保持平面",TownElevation.Parse(JsonValue.Object()).HeightAt(100,100)==0);
            // Producer directive 2026-11-08 removes both empty platforms; the new street gate has a real clear opening.
            var path=TownNavigator.FindPath(town,3420,2340,3420,2130);
            Check("祠堂门洞可规划真实路线",path!=null && path.Count>=1);
            if(path!=null)
            {
                float x=3420,y=2340;
                foreach(var q in path){Check("规划段与真实步行同形",town.CanTraverse(x,y,q.x,q.y));x=q.x;y=q.y;}
            }
            Check("水中目标不可寻路",TownNavigator.FindPath(town,4700,3000,4700,e.RiverY(4700))==null);
            var raw=File.ReadAllText(Path.Combine(configDir,"town-map.json"));
            var removed=new[]{"ancestral-terrace","ancestral-steps","view-terrace","view-steps"};
            var current=Json.Parse(raw);
            Check("四项废弃空台及台阶配置均删除",!current["elevation"]["regions"].Items.Any(r=>removed.Contains(r["id"].AsString(""))));
            var changed=Json.Parse(raw);
            var bridgeRegion=changed["elevation"]["regions"].Items.First(r=>r["id"].AsString("")=="main-bridge");
            bridgeRegion.Add("level",JsonValue.Number(0));
            var changedElevation=TownElevation.Parse(changed);
            Check("桥高消费Region.Level：改为0必须下降24",Math.Abs(changedElevation.HeightAt(4236,e.RiverY(4236)))<.01f);
            Check("桥高配置改变投影",Math.Abs(changedElevation.ProjectY(4236,e.RiverY(4236))-e.ProjectY(4236,e.RiverY(4236))-24)<.01f);
            var narrow=Json.Parse(raw);
            var thin=JsonValue.Object();
            thin.Add("id",JsonValue.String("thin-platform"));thin.Add("shape",JsonValue.String("rect"));
            thin.Add("level",JsonValue.Number(2));thin.Add("feather",JsonValue.Number(0));
            thin.Add("rect",JsonValue.Array(new[]{JsonValue.Number(1000.5),JsonValue.Number(7490),JsonValue.Number(1002.5),JsonValue.Number(7510)}));
            narrow["elevation"].Add("regions",JsonValue.Array(narrow["elevation"]["regions"].Items.Concat(new[]{thin})));
            var narrowElevation=TownElevation.Parse(narrow);
            Check("2单位高台反例：端点0、中间48",narrowElevation.HeightAt(999,7500)==0 && narrowElevation.HeightAt(1001,7500)==48 && narrowElevation.HeightAt(1003,7500)==0);
            Check("几何边界采样不跨过2单位高台",!narrowElevation.CanTraverse(999,7500,1003,7500) && !narrowElevation.CanTraverse(1003,7500,999,7500));
            Check("斜向也不能跨过窄台",!narrowElevation.CanTraverse(999,7495,1004,7505));
            foreach(var variant in new[]{raw.Replace("\"stepHeight\": 24","\"stepHeight\": 0"),raw.Replace("\"sampleDistance\": 4","\"sampleDistance\": 40"),raw.Replace("\"shape\": \"river\"","\"shape\": \"unknown\"")})
            {
                bool rejected=false;
                try{TownMap.Parse(variant);}catch(FormatException){rejected=true;}
                Check("坏高差配置必须拒绝",rejected);
            }
            town.RegisterBlocker("elevation-test",49,7490,51,7510);
            try { Check("2单位薄墙不能被100单位移动穿透",!town.CanTraverse(0,7500,100,7500)); }
            finally { town.ClearBlockers("elevation-test"); }
            RunNavigationWithPlots(town,configDir);
        }
        public static void RunNavigationWithPlots(TownMap town,string configDir)
        {
            const string source="navigation-regression";
            try
            {
                var plots=Json.Parse(File.ReadAllText(Path.Combine(configDir,"building-plots.json")))["plots"];
                foreach(var p in plots.Items)
                {
                    if(p.Has("blocks")&&!p["blocks"].AsBool(true))continue;
                    float x=p["footX"].AsFloat(0),y=p["footY"].AsFloat(0),w=p["width"].AsFloat(0);
                    if(p.Has("collisionCapsule"))
                    {
                        var cap=p["collisionCapsule"];
                        town.RegisterFootCapsule(source,x+cap["x0"].AsFloat(0),x+cap["x1"].AsFloat(0),y+cap["y"].AsFloat(0),cap["radius"].AsFloat(0));
                    }
                    else if(p.Has("collisionRects"))
                        foreach(var b in p["collisionRects"].Items)
                            town.RegisterBlocker(source,x+b["x0"].AsFloat(0),y+b["y0"].AsFloat(0),x+b["x1"].AsFloat(0),y+b["y1"].AsFloat(0));
                    else town.RegisterBlocker(source,x-w/2,y+p["collisionTop"].AsFloat(0),x+w/2,y+p["collisionDepth"].AsFloat(0));
                }
                RunPlazaCirculation(town,plots,configDir);
                var gate=plots.Items.Single(p=>p["id"].AsString("")=="plot-town-gate-south");
                var pier=gate["collisionRects"].Items.First();
                float gx=gate["footX"].AsFloat(0),gy=gate["footY"].AsFloat(0);
                float bx=gx+(pier["x0"].AsFloat(0)+pier["x1"].AsFloat(0))*.5f;
                float by=gy+(pier["y0"].AsFloat(0)+pier["y1"].AsFloat(0))*.5f;
                Check("真实城门墩反例：实体内部不可站立",!town.CanStand(bx,by));
                Check("被挡起点必须拒绝寻路",TownNavigator.FindPath(town,bx,by,3120,2352)==null);
                bool gateSweep=true;
                float top=gy+gate["collisionTop"].AsFloat(0)-4,bottom=gy+gate["collisionDepth"].AsFloat(0)+4;
                for(float y=top;y<bottom;y+=4)
                {
                    float next=Math.Min(y+4,bottom);
                    foreach(float offset in new[]{-310f,0f,310f})
                        gateSweep &= town.CanTraverse(town.Elevation.RoadX(y)+offset,y,town.Elevation.RoadX(next)+offset,next);
                }
                Check("城门地标主街500及两侧60真实扫掠贯通",gateSweep);
                Check("SE拐角可通行",town.CanTraverse(4400,5350,4700,5350) && town.CanTraverse(4700,5350,4700,5550));
                Check("真实门楼碰撞下门洞可双向寻路",
                    ValidPath(town,3420,2340,3420,2130) && ValidPath(town,3420,2130,3420,2340));
                Check("断头西墙不再注册",!plots.Items.Any(p=>p["id"].AsString("")=="plot-courtyard-wall-west"));
            }
            finally{town.ClearBlockers(source);}
            try
            {
                town.RegisterFootCapsule(source,2000,2030,7500,10);
                Check("微胶囊中心不可站立",!town.CanStand(2000,7500));
                Check("长步不得穿透微胶囊",!town.CanTraverse(1980,7500,2050,7500));
                Check("微胶囊切线仍拦截",!town.CanTraverse(1980,7490,2050,7490));
                Check("微胶囊圆角外保留通行",town.CanStand(1991,7491));
                Check("微胶囊外侧不扩成空气墙",town.CanTraverse(1980,7489,2050,7489));
            }
            finally{town.ClearBlockers(source);}
            Check("微胶囊清理后不留幽灵阻挡",town.CanStand(2000,7500));
            try
            {
                // Neither endpoint nor a regular 32-unit row lies inside this 16-unit free corridor.
                town.RegisterBlocker(source,1000,7300,1200,7490);
                town.RegisterBlocker(source,1000,7506,1200,7700);
                var path=TownNavigator.FindPath(town,980,7480,1220,7480);
                Check("端点不在窄缝行时仍能发现16单位通路",path!=null && path.Any(p=>p.y>7490 && p.y<7506));
                Check("窄缝路线每一段仍通过真实碰撞检查",ValidPath(town,980,7480,1220,7480));
            }
            finally{town.ClearBlockers(source);}
        }
        static void RunPlazaCirculation(TownMap town,JsonValue plots,string configDir)
        {
            var config=Json.Parse(File.ReadAllText(Path.Combine(configDir,"town-circulation.json")));
            var tree=plots.Items.Single(p=>p["id"].AsString("")==config["landscape"]["plotId"].AsString(""));
            var cap=tree["collisionCapsule"];
            float cx0=tree["footX"].AsFloat(0)+cap["x0"].AsFloat(0),cx1=tree["footX"].AsFloat(0)+cap["x1"].AsFloat(0);
            float cy=tree["footY"].AsFloat(0)+cap["y"].AsFloat(0),radius=cap["radius"].AsFloat(0),margin=config["characterClearance"].AsFloat(0);
            var rows=new System.Collections.Generic.List<object>();
            bool all=true;
            // Clearance is a test-only sweep. Runtime keeps the original capsule, not this envelope.
            const string clearanceSource="plaza-clearance-regression";
            town.RegisterFootCapsule(clearanceSource,cx0,cx1,cy,radius+margin);
            try
            {
                foreach(var route in config["routes"].Items)
                {
                    var points=route["points"].Items.Select(p=>p.Items.Select(v=>v.AsFloat(0)).ToArray()).ToArray();
                    bool pass=true;
                    for(int i=1;i<points.Length;i++)
                    {
                        float ax=points[i-1][0],ay=points[i-1][1],bx=points[i][0],by=points[i][1];
                        pass &= town.CanTraverse(ax,ay,bx,by) && town.CanTraverse(bx,by,ax,ay);
                        pass &= ValidPath(town,ax,ay,bx,by) && ValidPath(town,bx,by,ax,ay);
                    }
                    rows.Add(new{id=route["id"].AsString(""),stage="core-route-with-root-clearance",pass,points,margin});all &= pass;
                }
            }
            finally{town.ClearBlockers(clearanceSource);}
            foreach(var route in config["routes"].Items)
            {
                var points=route["points"].Items.Select(p=>p.Items.Select(v=>v.AsFloat(0)).ToArray()).ToArray();
                bool pass=true;
                for(int i=1;i<points.Length;i++)
                {
                    float ax=points[i-1][0],ay=points[i-1][1],bx=points[i][0],by=points[i][1];
                    bool vertical=Math.Abs(ax-bx)<.01f;
                    for(float offset=-margin;offset<=margin;offset+=1)
                        pass &= town.CanTraverse(ax+(vertical?offset:0),ay+(vertical?0:offset),bx+(vertical?offset:0),by+(vertical?0:offset));
                }
                rows.Add(new{id=route["id"].AsString(""),stage="full-width-real-blocker-sweep",pass,margin});all &= pass;
            }
            foreach(string direction in new[]{"north","east","south","west"})
            {
                var point=config["entrances"][direction].Items.Select(v=>v.AsFloat(0)).ToArray();
                float dx=direction=="west"?-80:direction=="east"?80:0,dy=direction=="north"?-80:direction=="south"?80:0;
                bool pass=town.CanStand(point[0],point[1]) && ValidPath(town,point[0]+dx,point[1]+dy,point[0],point[1]) && ValidPath(town,point[0],point[1],point[0]+dx,point[1]+dy);
                rows.Add(new{id=direction,stage="entrance-connects-existing-road-both-ways",pass,point});all &= pass;
            }
            bool blocked=!town.CanStand((cx0+cx1)/2,cy) && !town.CanTraverse(cx0-radius-2,cy,cx1+radius+2,cy);
            bool corner=town.CanStand(cx0-radius+1,cy-radius+1) && town.CanStand(cx1+radius-1,cy-radius+1);
            bool outside=town.CanTraverse(cx0-radius-2,cy-radius-1,cx1+radius+2,cy-radius-1);
            rows.Add(new{id="real-root-negative-and-no-air-wall",pass=blocked&&corner&&outside,blocked,corner,outside});all &= blocked&&corner&&outside;
            var evidence=Environment.GetEnvironmentVariable("TOWN_CIRCULATION_EVIDENCE");
            if(!string.IsNullOrEmpty(evidence))File.WriteAllText(evidence,System.Text.Json.JsonSerializer.Serialize(new{status=all?"PASS":"FAIL",characterClearance=margin,checks=rows},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
            Check("中央树岛四向入口/左右环行/60角色余量/真实树根反例/无空气墙",all);
        }
        static bool ValidPath(TownMap town,float x,float y,float tx,float ty)
        {
            var path=TownNavigator.FindPath(town,x,y,tx,ty);
            if(path==null || path.Count==0)return false;
            foreach(var p in path){if(!town.CanTraverse(x,y,p.x,p.y))return false;x=p.x;y=p.y;}
            return Math.Abs(x-tx)<.01f && Math.Abs(y-ty)<.01f;
        }
    }
}
