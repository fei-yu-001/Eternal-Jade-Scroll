using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TwelveJade.Core;
using TwelveJade.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Editor
{
    /// <summary>Map-only runtime acceptance. Runs in an isolated project; never writes player saves.</summary>
    [InitializeOnLoad]
    public static class QingshiMapShot
    {
        const string Key = "QingshiMapShot.Active";
        static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static FrontEndController c;
        static Camera camera;
        static Canvas canvas;
        static string output;
        static int phase, frames, shot, routeLeg, screenshots;
        static bool routeOnly;
        static readonly List<MapStairCrossing.Passage> stairPassages = new List<MapStairCrossing.Passage>();
        static Vector2 routeGoal;
        static double started, routeStart;
        static Vector2 previous;
        static bool routeValid = true;
        [Serializable] sealed class Row { public string name; public bool pass; public string detail; public string category = "runtime"; }
        [Serializable] sealed class Report { public string status; public List<Row> assertions; public int screenshots; public string unityVersion; public string runtimeStatus; public int environmentErrors; }
        static readonly List<Row> rows = new List<Row>();
        [Serializable] sealed class RouteSample { public int leg, frame; public float fromX,fromY,x,y,height,deltaTime; public bool traversable; public MapStairCrossing.Passage stairs; }
        [Serializable] sealed class RouteTrace { public List<RouteSample> samples; }
        static readonly List<RouteSample> trace = new List<RouteSample>();
        static readonly (string name,float x,float y)[] sites = {
            ("ancestral-stairs",3450,2420),("viewing-terrace",5070,2180),
            ("main-bridge",4236,2480),("ferry-stairs",5100,2950),
            ("market",3100,4950),("main-street",4100,4020),("south-gate",3950,7040)
        };
        static QingshiMapShot()
        {
            if(SessionState.GetBool(Key,false)) Attach();
        }
        public static void Run()
        {
            output=Environment.GetEnvironmentVariable("QINGSHI_MAP_EVIDENCE");
            if(string.IsNullOrEmpty(output))throw new InvalidOperationException("QINGSHI_MAP_EVIDENCE must name an exclusive evidence directory");
            Directory.CreateDirectory(output);
            SessionState.SetString(Key+".Output",output);
            SessionState.SetBool(Key,true);
            Environment.SetEnvironmentVariable("TWELVEJADE_SAVEDIR",Path.Combine(output,"scratch-saves"));
            Attach();
            try
            {
                // Unity 6000.3.25f1 skips first-use Search.index creation once Play is pending,
                // but its delayed startup indexer still reads the first database. Initialize the
                // real default database in Edit mode; keep indexing and every error assertion enabled.
                if(EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new InvalidOperationException("Map acceptance must start in Edit mode");
                var databases=UnityEditor.Search.SearchService.EnumerateDatabases().ToArray();
                if(databases.Length==0 || !File.Exists("UserSettings/Search.index"))
                    throw new InvalidOperationException("Default Search database was not initialized before Play");
                Debug.Log("[QingshiMapShot] Search bootstrap before Play: databases="+databases.Length+
                    " indexExists="+File.Exists("UserSettings/Search.index"));
                if(rows.Any(r=>!r.pass)){Finish();return;}
                EditorSceneManager.OpenScene("Assets/Scenes/FrontEnd.unity",OpenSceneMode.Single);
                EditorApplication.isPlaying=true;
            }
            catch(Exception e){Check("editor bootstrap",false,e.ToString());Finish();}
        }
        static void Attach()
        {
            output=SessionState.GetString(Key+".Output",output??"");
            routeOnly=Environment.GetEnvironmentVariable("QINGSHI_MAP_ROUTE_ONLY")=="1";
            started=EditorApplication.timeSinceStartup;
            EditorApplication.update-=Pump;EditorApplication.update+=Pump;
            Application.logMessageReceived-=OnLog;Application.logMessageReceived+=OnLog;
        }
        static void OnLog(string msg,string stack,LogType type)
        {
            if(type==LogType.Exception || type==LogType.Error || type==LogType.Assert)
                rows.Add(new Row{name=stack!=null && stack.Contains("UnityEditor.Search.SearchDatabase") ? "Unity editor environment error: SearchDatabase" : "Unity runtime error",pass=false,detail=msg+"\n"+stack,
                    category=stack!=null && stack.Contains("UnityEditor.Search.SearchDatabase") ? "editor-environment" : "runtime"});
        }
        static T Field<T>(string name)=>(T)typeof(FrontEndController).GetField(name,Private).GetValue(c);
        static object Call(string name,params object[] args)=>typeof(FrontEndController).GetMethod(name,Private).Invoke(c,args);
        static void Check(string name,bool pass,string detail="")
        {
            rows.Add(new Row{name=name,pass=pass,detail=detail});
            Debug.Log("[QingshiMapShot] "+(pass?"PASS ":"FAIL ")+name+" "+detail);
        }
        static void Pump()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            try
            {
                if(EditorApplication.timeSinceStartup-started>600){Check("runtime timeout",false);Finish();return;}
                if(!EditorApplication.isPlaying)return;
                if(c==null)
                {
                    c=UnityEngine.Object.FindAnyObjectByType<FrontEndController>();if(c==null)return;
                    camera=Camera.main;canvas=UnityEngine.Object.FindAnyObjectByType<Canvas>().rootCanvas;
                    canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=10;
                    camera.aspect=1920f/1080f;
                    int slot=c.Repository.ReadAll().First(s=>s.State==SlotState.Empty).Slot;
                    c.ShowCharacterCreation(slot);Check("create isolated save",c.CreateFromDraft(slot)!=null);c.ShowTown();
                }
                frames++;
                if(phase==0 && frames>24)
                {
                    if(routeOnly){BeginRoute();phase=2;return;}
                    VerifyWorld();phase=1;frames=0;
                }
                else if(phase==1)
                {
                    if(shot>=sites.Length){BeginRoute();phase=2;return;}
                    var p=sites[shot];
                    if(frames==1)Check("shot location "+p.name,c.SnapTravelerTo(p.x,p.y));
                    if(frames>=6){Capture(p.name);shot++;frames=0;}
                }
                else if(phase==2)
                {
                    var now=c.PlayerWorldPosition;
                    if((now-previous).sqrMagnitude>0.0001f)
                    {
                        bool traversable=c.World.CanTraverse(previous.x,previous.y,now.x,now.y);
                        routeValid &= traversable;
                        float height=c.World.Elevation.HeightAt(now.x,now.y);
                        MapStairCrossing.Passage passage;
                        if(MapStairCrossing.TryObserve(c.World,"ancestral-steps",previous.x,previous.y,now.x,now.y,out passage))
                            stairPassages.Add(passage);
                        trace.Add(new RouteSample{leg=routeLeg,frame=Time.frameCount,fromX=previous.x,fromY=previous.y,
                            x=now.x,y=now.y,height=height,deltaTime=Time.unscaledDeltaTime,traversable=traversable,stairs=passage});
                    }
                    previous=now;
                    if(!Field<bool>("hasTarget") || EditorApplication.timeSinceStartup-routeStart>35)
                    {
                        Check("route leg "+routeLeg+" reaches goal",(now-routeGoal).magnitude<12,"position="+now+" goal="+routeGoal);
                        Check("route leg "+routeLeg+" every actual segment traversable",routeValid);
                        Check("route leg "+routeLeg+" actually crossed stairs",MapStairCrossing.CoversWholeRamp(stairPassages),
                            "actual traversable segment passages="+stairPassages.Count);
                        Capture("route-leg-"+routeLeg);
                        if(routeLeg==0){routeLeg=1;StartRoute(new Vector2(3450,2452));}
                        else Finish();
                    }
                }
            }
            catch(Exception e){Check("fixture exception",false,e.ToString());Finish();}
        }
        static void VerifyWorld()
        {
            var e=c.World.Elevation;
            var props=UnityEngine.Object.FindAnyObjectByType<WorldPropLayer>();
            Check("102 plots instantiated",props!=null && props.PlotCount==102);
            var tiles=Field<List<RectTransform>>("chunkRects");
            Check("64 elevation terrain chunks",tiles.Count==64 && tiles.All(t=>t.GetComponent<WorldTerrainImage>()!=null));
            Check("runtime overview loaded",c.WorldOverviewArtLoaded);
            foreach(var art in new[]{"Art/Props/qingshi-courtyard-wall","Art/Buildings/town-gate-south-wide","Art/Props/qingshi-market-vignette"})
                Check("runtime asset "+art,Resources.Load<Texture2D>(art)!=null);
            var places=Field<List<FrontEndController.Placeable>>("placeables");
            var plots=Json.Parse(Resources.Load<TextAsset>("Config/building-plots").text)["plots"];
            foreach(var p in plots.Items)
            {
                string id=p["id"].AsString("");var live=places.FirstOrDefault(v=>v.Id==id);
                float x=p["footX"].AsFloat(0),y=p["footY"].AsFloat(0),h=p["height"].AsFloat(0);
                Check("plot projection/sort "+id,live!=null && Mathf.Abs(live.BaseY-e.ProjectY(x,y+h))<.02f &&
                    Mathf.Abs(live.Rect.anchoredPosition.y+(y-e.HeightAt(x,y+h))*c.WorldScale)<.02f);
            }
            foreach(var npc in c.World.Npcs)
            {
                var foot=(Vector2)Call("NpcStandingAt",npc);var live=places.FirstOrDefault(v=>v.Id=="npc-"+npc.Id);
                if(live!=null)Check("NPC projection "+npc.Id,Mathf.Abs(live.BaseY-e.ProjectY(foot.x,foot.y))<.02f);
            }
            foreach(var enc in c.World.Encounters)
            {
                var live=places.FirstOrDefault(v=>v.Id=="enc-"+enc.Id);
                if(live!=null)Check("encounter ground anchor "+enc.Id,Mathf.Abs(live.BaseY-e.ProjectY(enc.X,enc.Y))<.02f);
            }
            Check("gate opening traversable",c.World.CanTraverse(3960,6740,3960,6990));
            Check("gate west pier blocks",!c.World.CanStand(3607,6863));
            Check("gate east pier blocks",!c.World.CanStand(4313,6863));
            Check("gate opening full 500 road traversable",Enumerable.Range(0,51).All(i=>{
                float x=3710+i*10;return c.World.CanTraverse(x,6777,x,6949);
            }));
            Check("gate piers and foot share +1 platform",new[]{new Vector2(3564,6777),new Vector2(3650,6949),
                new Vector2(4270,6777),new Vector2(4356,6949),new Vector2(3960,6955)}.All(p=>Mathf.Abs(e.HeightAt(p.x,p.y)-24)<.01f));
            var terrain=Json.Parse(Resources.Load<TextAsset>("Config/town-map").text)["terrainShape"];
            foreach(var b in terrain["bridges"].Items)
            {
                float x=b["x"].AsFloat(0),y=e.RiverY(x);
                Check("bridge runtime center/cross "+x,c.World.CanStand(x,y)&&c.World.CanTraverse(x,y-400,x,y+400));
            }
            Check("water rejected",!c.World.CanStand(4700,e.RiverY(4700)));
            Check("cliff rejected with runtime blockers",!c.World.CanTraverse(3100,2370,3100,2350));
            Check("ancestral stairs both directions",c.World.CanTraverse(3450,2460,3450,2355)&&c.World.CanTraverse(3450,2355,3450,2460));
            foreach(var p in new[]{new Vector2(3120,2352),new Vector2(5070,2180),new Vector2(3100,4950),new Vector2(5100,2950),new Vector2(4236,e.RiverY(4236))})
            {
                Check("snap for projection "+p,c.SnapTravelerTo(p.x,p.y));
                Canvas.ForceUpdateCanvases();
                var traveler=Field<RectTransform>("traveler");
                Check("player projected root "+p,Mathf.Abs(traveler.anchoredPosition.y+e.ProjectY(p.x,p.y)*c.WorldScale)<.02f);
                var content=Field<RectTransform>("content");var camPos=Field<Vector2>("camPos");
                var local=camPos+new Vector2(p.x,-e.ProjectY(p.x,p.y))*c.WorldScale;
                var screen=RectTransformUtility.WorldToScreenPoint(camera,content.TransformPoint(local));
                var result=(Vector2?)Call("ScreenToWorld",screen);
                Check("real camera click inverse "+p,result.HasValue&&(result.Value-p).magnitude<.05f,"result="+result);
                Check("PlayerOnScreen agrees with camera "+p,c.PlayerOnScreen());
            }
            // Same ground y on flat and raised surfaces: projection must change sibling order.
            var low=c.PlaceInWorld("acceptance-flat",new Vector2(1000,2332),20,20,null,2352);
            var high=c.PlaceInWorld("acceptance-raised",new Vector2(3120,2332),20,20,null,2352);
            c.SnapTravelerTo(3120,2352);Call("UpdateDepthOrder");
            Check("projected depth reorders equal ground y",high.GetSiblingIndex()<low.GetSiblingIndex());
            UnityEngine.Object.Destroy(low.gameObject);UnityEngine.Object.Destroy(high.gameObject);
            MeshSample(e,4116.5f,2462.8403f,24f);
            MeshSample(e,4236f,e.RiverY(4236f),24f);
            MeshSample(e,5070f,2180f,48f);
        }
        static void MeshSample(TownElevation e,float x,float y,float expected)
        {
            var go=new GameObject("Acceptance mesh",typeof(RectTransform));var image=go.AddComponent<WorldTerrainImage>();
            float ox=Mathf.Floor(x/1024)*1024,oy=Mathf.Floor(y/1024)*1024;
            image.Configure(e,ox,oy,1024,1);
            using(var vh=new VertexHelper())
            {
                typeof(WorldTerrainImage).GetMethod("OnPopulateMesh",Private,null,new[]{typeof(VertexHelper)},null).Invoke(image,new object[]{vh});
                var mesh=new Mesh();vh.FillMesh(mesh);var vs=mesh.vertices;var uv=mesh.uv;var ix=mesh.triangles;
                float best=float.PositiveInfinity,actual=float.NaN;
                for(int i=0;i<ix.Length;i+=3)
                {
                    var a=ix[i];var b=ix[i+1];var d=ix[i+2];
                    Vector2 A=new Vector2(ox+uv[a].x*1024,oy+(1-uv[a].y)*1024),B=new Vector2(ox+uv[b].x*1024,oy+(1-uv[b].y)*1024),D=new Vector2(ox+uv[d].x*1024,oy+(1-uv[d].y)*1024);
                    float det=(B.y-D.y)*(A.x-D.x)+(D.x-B.x)*(A.y-D.y);if(Mathf.Abs(det)<.00001f)continue;
                    float wa=((B.y-D.y)*(x-D.x)+(D.x-B.x)*(y-D.y))/det,wb=((D.y-A.y)*(x-D.x)+(A.x-D.x)*(y-D.y))/det,wc=1-wa-wb;
                    if(wa<-.00001f||wb<-.00001f||wc<-.00001f)continue;
                    float h=wa*(vs[a].y+A.y-oy)+wb*(vs[b].y+B.y-oy)+wc*(vs[d].y+D.y-oy);
                    if(Mathf.Abs(h-expected)<best){best=Mathf.Abs(h-expected);actual=h;}
                }
                Check("actual Unity mesh height "+x+","+y,best<.05f,"logic="+e.HeightAt(x,y)+" mesh="+actual+" expected="+expected);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
            UnityEngine.Object.DestroyImmediate(go);
        }
        static void BeginRoute()
        {
            Call("CancelTravel");
            Check("route regression starting control",c.SnapTravelerTo(3950,7040));
            var before=c.PlayerWorldPosition;
            bool invalid=c.SnapTravelerTo(3300,2460);
            Check("old fixture origin is blocked and teleport refused",!invalid && c.PlayerWorldPosition==before,"retained="+c.PlayerWorldPosition);
            Check("route target is standable",c.World.CanStand(3120,2352));
            Check("known continuous stairs then ledge route",c.World.CanTraverse(3450,2452,3450,2352) && c.World.CanTraverse(3450,2352,3120,2352));
            Check("direct diagonal cliff shortcut refused",!c.World.CanTraverse(3450,2452,3120,2352));
            bool snapped=c.SnapTravelerTo(3450,2452);
            Check("valid route origin teleport succeeds",snapped && (c.PlayerWorldPosition-new Vector2(3450,2452)).magnitude<.01f);
            if(!snapped)throw new InvalidOperationException("Route fixture origin failed: abort instead of reusing last screenshot position");
            StartRoute(new Vector2(3120,2352));
        }
        static void StartRoute(Vector2 goal)
        {
            previous=c.PlayerWorldPosition;routeGoal=goal;stairPassages.Clear();routeValid=true;
            Check("route leg "+routeLeg+" requested",c.RequestTravelTo(goal),"from="+previous+" to="+goal);
            routeStart=EditorApplication.timeSinceStartup;
        }

        static void Capture(string label)
        {
            foreach(var group in c.GetComponentsInChildren<CanvasGroup>(true))if(group.gameObject.activeInHierarchy)group.alpha=1;
            var rt=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32);var prior=RenderTexture.active;
            camera.targetTexture=rt;camera.aspect=1920f/1080f;Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=rt;
            var tex=new Texture2D(1920,1080,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1920,1080),0,0);tex.Apply();
            File.WriteAllBytes(Path.Combine(output,label+".png"),tex.EncodeToPNG());screenshots++;
            camera.targetTexture=null;RenderTexture.active=prior;UnityEngine.Object.DestroyImmediate(tex);UnityEngine.Object.DestroyImmediate(rt);
        }
        static void Finish()
        {
            EditorApplication.update-=Pump;Application.logMessageReceived-=OnLog;SessionState.SetBool(Key,false);
            bool ok=rows.Count>0 && rows.All(r=>r.pass);
            int environment=rows.Count(r=>r.category=="editor-environment" && !r.pass);
            bool runtimeOk=rows.Count>0 && rows.Where(r=>r.category!="editor-environment").All(r=>r.pass);
            File.WriteAllText(Path.Combine(output,"route-trace.json"),JsonUtility.ToJson(new RouteTrace{samples=trace},true));
            File.WriteAllText(Path.Combine(output,"runtime-results.json"),JsonUtility.ToJson(new Report{status=ok?"PASS":"FAIL",assertions=rows,screenshots=screenshots,unityVersion=Application.unityVersion,runtimeStatus=runtimeOk?"PASS":"FAIL",environmentErrors=environment},true));
            Debug.Log("[QingshiMapShot] RESULT: "+(ok?"PASS":"FAIL")+" assertions="+rows.Count+" failures="+rows.Count(r=>!r.pass));
            EditorApplication.Exit(ok?0:1);
        }
    }
    // Pure evidence geometry, also replayed against the raw Unity trajectory outside the Editor.
    public static class MapStairCrossing
    {
        [Serializable] public sealed class Passage
        {
            public string regionId;
            public float fromX,fromY,toX,toY;
            public double enterT,exitT,enterX,enterY,exitX,exitY,minProgress,maxProgress;
            public float midpointHeight;
        }
        static bool Clip(double origin,double delta,double low,double high,ref double enter,ref double exit)
        {
            if(delta==0)return origin>=low && origin<=high;
            double a=(low-origin)/delta,b=(high-origin)/delta;
            if(a>b){double swap=a;a=b;b=swap;}
            enter=Math.Max(enter,a);exit=Math.Min(exit,b);
            return enter<exit; // A corner touch or zero-length overlap is not a traversal.
        }
        public static bool TryObserve(TownMap map,string regionId,float x0,float y0,float x1,float y1,out Passage passage)
        {
            passage=null;
            if(float.IsNaN(x0)||float.IsNaN(y0)||float.IsNaN(x1)||float.IsNaN(y1)||
                float.IsInfinity(x0)||float.IsInfinity(y0)||float.IsInfinity(x1)||float.IsInfinity(y1))return false;
            var ramp=map.Elevation.Regions.SingleOrDefault(r=>r.Id==regionId);
            if(ramp==null || ramp.Shape!="ramp" || ramp.Rect==null || ramp.From==ramp.To)return false;
            double dx=(double)x1-x0,dy=(double)y1-y0;
            double axisDelta=ramp.Axis=="y"?dy:dx;
            if(axisDelta==0 || !map.CanTraverse(x0,y0,x1,y1))return false;
            double enter=0,exit=1;
            var r=ramp.Rect;
            if(!Clip(x0,dx,r.MinX,r.MaxX,ref enter,ref exit) ||
                !Clip(y0,dy,r.MinY,r.MaxY,ref enter,ref exit))return false;
            double middle=(enter+exit)*.5;
            float h=map.Elevation.HeightAt((float)(x0+dx*middle),(float)(y0+dy*middle));
            float low=Math.Min(ramp.From,ramp.To)*map.Elevation.StepHeight;
            float high=Math.Max(ramp.From,ramp.To)*map.Elevation.StepHeight;
            if(!(h>low && h<high))return false;
            double origin=ramp.Axis=="y"?y0:x0,minimum=ramp.Axis=="y"?r.MinY:r.MinX;
            double length=ramp.Axis=="y"?r.Height:r.Width;
            // Clipping already proved the segment lies in the exact configured rectangle.
            // Clamp arithmetic roundoff at its two boundaries, never enlarge that rectangle.
            double a=Math.Max(0,Math.Min(1,(origin+axisDelta*enter-minimum)/length));
            double b=Math.Max(0,Math.Min(1,(origin+axisDelta*exit-minimum)/length));
            if(a==b)return false;
            passage=new Passage{regionId=regionId,fromX=x0,fromY=y0,toX=x1,toY=y1,
                enterT=enter,exitT=exit,enterX=x0+dx*enter,enterY=y0+dy*enter,
                exitX=x0+dx*exit,exitY=y0+dy*exit,minProgress=Math.Min(a,b),maxProgress=Math.Max(a,b),midpointHeight=h};
            return true;
        }
        public static bool CoversWholeRamp(IEnumerable<Passage> passages)
        {
            double covered=0;
            foreach(var p in passages.Where(p=>p!=null).OrderBy(p=>p.minProgress))
            {
                if(p.minProgress>covered)return false; // Missing actual segments cannot be filled by arrival.
                covered=Math.Max(covered,p.maxProgress);
                if(covered>=1)return true;
            }
            return false;
        }
    }

}
