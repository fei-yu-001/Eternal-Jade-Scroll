using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using TwelveJade.Core;
using TwelveJade.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TwelveJade.Editor
{
    // One short, isolated GameView run. No product hooks, terrain generation or slope tests.
    [InitializeOnLoad]
    public static class TownMemoIntegrationShot
    {
        const string Key = "TownMemoIntegrationShot";
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        static string output, scratch, source;
        static FrontEndController c;
        static Canvas canvas;
        static TownMemoIntegrationPump pump;
        static bool running, finished;
        static double started;
        static int screenshots;
        static string mode = "full"; static bool VisualFollowup => mode == "visual-followup"; static bool OcclusionFollowup => mode == "occlusion-followup"; static bool OcclusionExperience => mode == "occlusion-experience"; static bool VignetteLevelFollowup => mode == "vignette-level-followup"; static bool VignetteFollowup => mode == "vignette-followup" || VignetteLevelFollowup;
        static int frameSamples, lastView = -1; static bool frameFault, frameProbeMissing, frontBackTurn; static RectTransform visualTree;
        static readonly HashSet<int> firstFrameViews = new HashSet<int>();
        static readonly List<Row> rows = new List<Row>();
        static readonly List<Stamp> inputs = new List<Stamp>();
        static readonly string[] BoundPaths = {
            "Assets/Editor/TownMemoIntegrationShot.cs", "Assets/Editor/TownMemoIntegrationShot.cs.meta",
            "Assets/Scripts/Core/TownMap.cs", "Assets/Scripts/Core/SaveRepository.cs", "Assets/Scripts/Core/TownNavigator.cs",
            "Assets/Resources/Config/town-circulation.json", "Assets/Resources/Config/town-circulation.json.meta",
            "Assets/Scripts/Presentation/FrontEndController.cs", "Assets/Scripts/Presentation/FrontEndWorld.cs",
            "Assets/Scripts/Presentation/FrontEndCharacters.cs", "Assets/Scripts/Presentation/WorldPropLayer.cs",
            "Assets/Scripts/Presentation/TownWaterSurface.cs", "Assets/Scripts/Presentation/TownWaterGeometry.cs",
            "Assets/Resources/Config/town-map.json", "Assets/Resources/Config/building-plots.json",
            "Assets/Resources/Art/World/world-build-manifest.json", "Assets/Resources/Shaders/QingshiWater.shader",
            "Assets/Scenes/FrontEnd.unity", "Assets/Resources/Art/Buildings/town-gate-south-wide.png",
            "Assets/Resources/Art/Props/teahouse-v2.png", "Assets/Resources/Art/Props/firewood-pile.png"
        };
        [Serializable] sealed class Row { public string name, status, detail; }
        [Serializable] sealed class Stamp { public string path, sha256; }
        [Serializable] sealed class Report { public string status, overall = "NOT_APPROVED", unityVersion, mode, scope; public int screenshots; public List<Row> checks; public List<Stamp> inputs; }
        [Serializable] sealed class Trace { public string route; public int frame; public Vector2 from, to; public bool traversable, inside; }
        [Serializable] sealed class Shot { public string file, page, method, mode; public float worldScale; public Vector3 treeScale, playerScale; public Vector2 player; public int frame, width, height; public float phase; }
        [Serializable] sealed class FrameTrace { public string stage, sheet; public int frame, view, index, previous, count; public float alpha; public Vector4 uv; public bool valid; }
        static TownMemoIntegrationShot() { if (SessionState.GetBool(Key, false)) Attach(); }
        public static void Run()
        {
            if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use an isolated, non-batch editor in Edit mode.");
            output = Environment.GetEnvironmentVariable("TOWN_MEMO_EVIDENCE"); source = Environment.GetEnvironmentVariable("TOWN_MEMO_SOURCE_PROJECT");
            if (string.IsNullOrEmpty(output) || !Path.IsPathRooted(output) || string.IsNullOrEmpty(source) || !Path.IsPathRooted(source)) throw new InvalidOperationException("Set absolute TOWN_MEMO_EVIDENCE and TOWN_MEMO_SOURCE_PROJECT.");
            if (string.Equals(Path.GetFullPath(source).TrimEnd('\\','/'),Path.GetFullPath(Path.Combine(Application.dataPath,"..")).TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Refusing the source editor.");
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new InvalidOperationException("Evidence directory must be new/empty; no overwrites.");
            mode=Environment.GetEnvironmentVariable("TOWN_MEMO_MODE") ?? "full"; if(mode!="full" && !VisualFollowup && !OcclusionFollowup && !OcclusionExperience && !VignetteFollowup)throw new InvalidOperationException("TOWN_MEMO_MODE must be full, visual-followup, occlusion-followup occlusion-experience or vignette-followup/vignette-level-followup."); SessionState.SetString(Key+".Mode",mode);
            Application.runInBackground=true; Directory.CreateDirectory(output); scratch = Path.Combine(output, "scratch-saves");
            SessionState.SetString(Key+".Output",output); SessionState.SetString(Key+".Source",source);
            SessionState.SetString(Key+".OldSave",Environment.GetEnvironmentVariable("TWELVEJADE_SAVEDIR") ?? "");
            Environment.SetEnvironmentVariable("TWELVEJADE_SAVEDIR",scratch); SessionState.SetBool(Key,true); Attach();
            try
            {
                CaptureInputs();
                if(OcclusionExperience)CaptureInputs(new[]{
                    "Assets/Scripts/Presentation/BuildingOcclusionFader.cs", "Assets/Scripts/Presentation/BuildingOcclusionFader.cs.meta",
                    "Assets/Scripts/Presentation/FrontEndOcclusion.cs", "Assets/Scripts/Presentation/FrontEndOcclusion.cs.meta",
                    "Assets/Scripts/Presentation/TravelerSilhouette.cs", "Assets/Scripts/Presentation/TravelerSilhouette.cs.meta",
                    "Assets/Scripts/Presentation/TravelerSilhouetteGraphic.cs", "Assets/Scripts/Presentation/TravelerSilhouetteGraphic.cs.meta",
                    "Assets/Resources/Shaders/TravelerSilhouette.shader", "Assets/Resources/Shaders/TravelerSilhouette.shader.meta",
                    "Assets/Scripts/Presentation/PuppetActor.cs", "Assets/Scripts/Presentation/PuppetActor.cs.meta"});
                SaveReport("RUNNING");
                UnityEditor.Search.SearchService.EnumerateDatabases().ToArray();
                EditorSceneManager.OpenScene("Assets/Scenes/FrontEnd.unity",OpenSceneMode.Single); EditorApplication.isPlaying = true;
            }
            catch(Exception e) { Check("bootstrap",false,e.ToString()); Finish(); }
        }
        static void Attach()
        {
            mode=SessionState.GetString(Key+".Mode","full"); output=SessionState.GetString(Key+".Output",""); source=SessionState.GetString(Key+".Source",""); scratch=Path.Combine(output,"scratch-saves");
            var reportPath=Path.Combine(output,"runtime-results.json");
            if(File.Exists(reportPath)){var saved=JsonUtility.FromJson<Report>(File.ReadAllText(reportPath)); rows.Clear(); rows.AddRange(saved.checks); inputs.Clear(); inputs.AddRange(saved.inputs);}
            started=EditorApplication.timeSinceStartup; EditorApplication.update-=Tick; EditorApplication.update+=Tick;
            Application.logMessageReceived-=OnLog; Application.logMessageReceived+=OnLog;
        }
        static void Tick()
        {
            if(finished)return; EditorApplication.QueuePlayerLoopUpdate();
            if(EditorApplication.timeSinceStartup-started>150) { Check("bounded run timeout",false); Finish(); return; }
            if(!EditorApplication.isPlaying || running)return;
            c=UnityEngine.Object.FindAnyObjectByType<FrontEndController>(); if(c==null)return;
            try
            {
                canvas=c.GetComponentInParent<Canvas>(); if(canvas==null)canvas=UnityEngine.Object.FindAnyObjectByType<Canvas>();
                if(canvas!=null)canvas=canvas.rootCanvas;
                EditorWindow.GetWindow(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView")).Focus();
                running=true; pump=new GameObject("Town memo integration runner").AddComponent<TownMemoIntegrationPump>();
                pump.StartCoroutine(Guard(Checks()));
            }
            catch(Exception e) { Check("start checks",false,e.ToString()); Finish(); }
        }
        static IEnumerator Guard(IEnumerator test)
        {
            var stack=new Stack<IEnumerator>(); stack.Push(test);
            while(stack.Count>0 && !finished)
            {
                object next=null; bool moved=false;
                try { moved=stack.Peek().MoveNext(); if(moved)next=stack.Peek().Current; }
                catch(Exception e) { Check("runtime exception",false,e.ToString()); Finish(); }
                if(finished)yield break;
                if(!moved){stack.Pop();continue;}
                if(next is IEnumerator nested)stack.Push(nested); else yield return next;
            }
            if(!finished)Finish();
        }
        static object Field(object owner,string name) => owner.GetType().GetField(name,Flags)?.GetValue(owner);
        static object Call(string name,params object[] args)
        { var method=c.GetType().GetMethod(name,Flags); if(method==null)throw new MissingMethodException(name); return method.Invoke(c,args); }
        static void Check(string name,bool pass,string detail="") { rows.Add(new Row{name=name,status=pass?"PASS":"FAIL",detail=detail}); SaveReport("RUNNING"); }
        static void Pending(string name,string detail) { rows.Add(new Row{name=name,status="PENDING",detail=detail}); SaveReport("RUNNING"); }
        static void OnLog(string msg,string stack,LogType type)
        { if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert) { rows.Add(new Row{name="Unity error",status="FAIL",detail=msg+"\n"+stack}); SaveReport("RUNNING"); } }
        static void Isolated()
        {
            var actual=Field(c.Repository,"root") as string;
            if(actual==null || !string.Equals(Path.GetFullPath(actual),Path.GetFullPath(scratch),StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Environment.GetEnvironmentVariable("TWELVEJADE_SAVEDIR"),scratch,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Repository/save environment is not scratch.");
        }
        static float N(JsonValue v,string key) => v[key].AsFloat(float.NaN);
        static Vector2 Point(JsonValue p,float dx,float dy) => new Vector2(N(p,"footX")+dx,N(p,"footY")+dy);
        static bool Snap(Vector2 p) { Call("CancelTravel"); bool ok=c.SnapTravelerTo(p.x,p.y); Check("initial position "+p,ok); return ok; }
        static IEnumerator Checks()
        {
            if(VignetteFollowup){yield return VignetteChecks();yield break;}
            if(OcclusionExperience){yield return ExperienceChecks();yield break;}
            if(OcclusionFollowup){yield return OcclusionChecks();yield break;}
            Isolated();
            if(Directory.Exists(scratch) && Directory.EnumerateFiles(scratch,"slot-*").Any()) throw new InvalidOperationException("Scratch slots are not empty.");
            var preset=Resources.LoadAll<CharacterPreset>("Characters").OrderBy(p=>p.id).First();
            Isolated(); c.Repository.Create(1,preset.id,"城镇核验"); Isolated(); c.LoadSlot(c.Repository.Read(1));
            float until=Time.realtimeSinceStartup+15; WorldPropLayer props=null;
            while(Time.realtimeSinceStartup<until) { props=UnityEngine.Object.FindAnyObjectByType<WorldPropLayer>(); if(props!=null && props.PlacedForCheck)break; yield return null; }
            Check("scratch LoadSlot directly Town",c.CurrentPage=="town" && c.World!=null);
            if(c.World==null || props==null || !props.PlacedForCheck) { Check("real props placed",false); yield break; }
            Check("native Overlay",canvas!=null && canvas.renderMode==RenderMode.ScreenSpaceOverlay);
            var plots=Json.Parse(Resources.Load<TextAsset>("Config/building-plots").text)["plots"].Items;
            var shape=Json.Parse(Resources.Load<TextAsset>("Config/town-map").text)["terrainShape"];
            var gate=plots.Single(p=>p["id"].AsString("")=="plot-town-gate-south");
            float top=N(gate,"footY")+N(gate,"collisionTop"),bottom=N(gate,"footY")+N(gate,"collisionDepth");
            var north=new Vector2(c.World.Elevation.RoadX(top-24),top-24); var south=new Vector2(c.World.Elevation.RoadX(bottom+24),bottom+24);
            var piers=gate["collisionRects"].Items;
            float left=N(gate,"footX")+piers.Where(r=>N(r,"x1")<=0).Max(r=>N(r,"x1"));
            float right=N(gate,"footX")+piers.Where(r=>N(r,"x0")>=0).Min(r=>N(r,"x0"));
            if(VisualFollowup){yield return VisualViews(plots,south,north,left,right);yield break;}
            if(Snap(south)) { yield return Capture("01-gate-south"); yield return Travel("gate north",north,left,right); yield return Capture("02-gate-north"); yield return Travel("gate return",south,left,right); }
            foreach(bool west in new[]{true,false})
            {
                var pier=piers.First(r=>west?N(r,"x1")<=0:N(r,"x0")>=0);
                var inside=Point(gate,(N(pier,"x0")+N(pier,"x1"))/2,(N(pier,"y0")+N(pier,"y1"))/2);
                var outside=Point(gate,west?N(pier,"x1")+4:N(pier,"x0")-4,(N(pier,"y0")+N(pier,"y1"))/2);
                Blocked(west?"west gate pier":"east gate pier",outside,inside);
            }
            foreach(string id in new[]{"plot-teahouse","clutter-firewood-grain"})
            {
                var p=plots.Single(v=>v["id"].AsString("")==id); bool small=id=="clutter-firewood-grain";
                yield return SortProbe(p,small);
                if(small && p.Has("collisionCapsule"))
                {
                    var cap=p["collisionCapsule"]; var center=Point(p,(N(cap,"x0")+N(cap,"x1"))/2,N(cap,"y"));
                    Blocked("small actual capsule",center+Vector2.up*(N(cap,"radius")+4),center);
                    var live=Field(c.World,"footCapsules") as IDictionary;
                    bool registered=live!=null && live.Contains("world-prop") && ((IEnumerable)live["world-prop"]).Cast<object>().Any(o=>
                        o is ValueTuple<float,float,float,float> t && Mathf.Abs(t.Item1-(N(p,"footX")+N(cap,"x0")))<.02f && Mathf.Abs(t.Item2-(N(p,"footX")+N(cap,"x1")))<.02f && Mathf.Abs(t.Item3-center.y)<.02f && Mathf.Abs(t.Item4-N(cap,"radius"))<.02f);
                    Check("capsule actually registered",registered);
                }
                else if(small)Pending("small capsule","Fixed clutter-firewood-grain has no collisionCapsule; no replacement selected.");
                else
                {
                    var baseRect=p["collisionRects"].Items.First(); float bx=(N(baseRect,"x0")+N(baseRect,"x1"))/2;
                    Blocked("building foundation",Point(p,bx,N(baseRect,"y0")-4),Point(p,bx,(N(baseRect,"y0")+N(baseRect,"y1"))/2));
                }
            }
            yield return Circulation(plots);
            var bridges=shape["bridges"].Items; Check("three configured bridge mouths",bridges.Count==3);
            foreach(var bridge in bridges)
            {
                float x=N(bridge,"x"),y=c.World.Elevation.RiverY(x),span=N(shape["river"],"bankHalf")+28;
                if(Snap(new Vector2(x,y+span)))yield return Travel("bridge "+x,new Vector2(x,y-span),x-N(bridge,"half")+2,x+N(bridge,"half")-2);
            }
            var water=Field(c,"townWater") as TownWaterSurface;
            if(water==null || !water.IsConfigured) { Check("real attached water",false); Pending("water phase/mesh/release","No configured product water; no substitute created."); yield break; }
            Check("water attached below props",!water.raycastTarget && water.transform.parent==c.TravelerRectForShot.parent && water.transform.GetSiblingIndex()<c.TravelerRectForShot.GetSiblingIndex());
            var settings=Field(c,"settings"); var motion=settings?.GetType().GetField("reduceMotion",Flags);
            if(motion==null)Pending("water settings callback","reduceMotion field unavailable; no direct Configure/SetReduceMotion replacement.");
            else
            {
                bool previous=(bool)motion.GetValue(settings); motion.SetValue(settings,false); yield return null;
                float phase=water.FlowPhase; yield return new WaitForSecondsRealtime(.35f);
                Check("water phase advances through settings",!water.ReduceMotion && Mathf.Abs(water.FlowPhase-phase)>.00001f); yield return Capture("05-water-moving");
                motion.SetValue(settings,true); yield return null; phase=water.FlowPhase; yield return new WaitForSecondsRealtime(.35f);
                Check("water reduceMotion freezes phase",water.ReduceMotion && water.FlowPhase==phase); yield return Capture("06-water-frozen"); motion.SetValue(settings,previous);
            }
            MeshHoles(water,bridges,N(shape["river"],"bankHalf"));
            var mesh=Field(water,"ownedMesh") as Mesh; var material=Field(water,"ownedMaterial") as Material;
            Check("water owns live mesh/material",mesh!=null && material!=null); Isolated(); c.ShowMenu(); yield return null; yield return null;
            Check("leave Town releases water/mesh/material",c.CurrentPage!="town" && water==null && mesh==null && material==null);
            Check("4 to 6 real screenshots",screenshots>=4 && screenshots<=6);
        }
        static IEnumerator VisualViews(IReadOnlyList<JsonValue> plots,Vector2 south,Vector2 north,float left,float right)
        {
            Pending("full run not rerun","visual-followup only: building/small-prop blockers and sorting, three bridges, full circulation matrix, water phase/reduceMotion/mesh/release NOT RERUN; no Town02 evidence merged.");
            if(!Snap(south))yield break;
            yield return Capture("vf-01-gate-south"); yield return Travel("visual gate north",north,left,right); yield return Capture("vf-02-gate-north");
            yield return Travel("visual gate return",south,left,right);
            var config=Json.Parse(Resources.Load<TextAsset>("Config/town-circulation").text); var tree=plots.Single(p=>p["id"].AsString("")==config["landscape"]["plotId"].AsString(""));
            var points=config["routes"].Items.Single(r=>r["id"].AsString("")=="north-south-west")["points"].Items.Select(XY).ToList();
            var foot=Point(tree,0,tree.Has("groundFootOffset")?N(tree,"groundFootOffset"):N(tree,"height")); int ni=points.FindIndex(p=>p.x<foot.x && p.y<foot.y),si=points.FindIndex(p=>p.x<foot.x && p.y>foot.y);
            Check("tree views use configured route and actual foot",ni>=0 && si>ni,"foot="+foot+" route=north-south-west northIndex="+ni+" southIndex="+si); if(ni<0 || si<=ni)yield break;
            var places=Field(c,"placeables") as List<FrontEndController.Placeable>; visualTree=places?.FirstOrDefault(p=>p.Id==tree["id"].AsString(""))?.Rect;
            Check("tree and player share world parent",visualTree!=null && visualTree.parent==c.TravelerRectForShot.parent); if(visualTree==null)yield break;
            BindTexture(visualTree.GetComponent<UnityEngine.UI.RawImage>()?.texture); var treeScale=visualTree.localScale; var worldScale=c.WorldScale; var parentScale=visualTree.parent.localScale;
            if(Snap(points[ni]))
            {
                yield return Capture("vf-03-locust-north"); var bounds=Box(config["bounds"]);var bands=config["bands"].Items.Select(b=>Box(b["rect"])).ToList();
                for(int i=ni+1;i<=si;i++){var a=c.PlayerWorldPosition;var b=points[i];yield return Travel("visual tree leg "+i,b,Mathf.Min(a.x,b.x)-4,Mathf.Max(a.x,b.x)+4,p=>Within(bounds,p)&&bands.Any(r=>Within(r,p)));}
                yield return Capture("vf-04-locust-south");
            }
            Check("tree/player original scales retained",visualTree.localScale==treeScale && visualTree.parent.localScale==parentScale && c.WorldScale==worldScale); visualTree=null;
            Check("Frames sampled without invalid previous fade",frameSamples>0 && !frameFault,"samples="+frameSamples); Check("actual front/back facing change",frontBackTurn);
            if(firstFrameViews.Contains(0)&&firstFrameViews.Contains(2))Check("real front/back first Frames observed",true);else Pending("real front/back first Frames observed","Missing first post-Update frame: views="+string.Join(",",firstFrameViews));
            Check("four followup screenshots",screenshots==4);
        }

        // Independent baseline: no Snap, camera writes, warm-up or product mutation.
        [Serializable] sealed class OcclusionEvidence
        {
            public string file, objectId, side, status, geometry, method;
            public int frame, firstTownFrame, arrivalFrame, width, height, playerSibling, objectSibling;
            public int playerDepth, objectDepth, alphaOverlapPixels, bodyOverlapPixels, solidSamples, expectedWinnerMatches;
            public bool reached, standable, sameParent, playerInFront, projectedFront;
            public Vector2 goal, player, cameraPosition;
            public float playerSortY, objectSortY, worldScale;
            public Vector3 objectScale, playerScale;
            public Rect objectScreenRect, playerScreenRect, commonViewport;
            public List<PixelWitness> witnesses = new List<PixelWitness>();
        }
        [Serializable] sealed class PixelWitness
        {
            public Vector2 screen, objectUv;
            public Color screenshot, player, prop;
            public float playerError, propError;
        }
        [Serializable] sealed class WalkPlan
        {
            public string name; public int frame; public Vector2 start, goal;
            public bool standable, requested; public List<Vector2> route;
        }
        sealed class PixelSurface
        {
            public UnityEngine.UI.RawImage image;
            public Texture2D pixels;
            public Rect screen, clip;
        }
        static readonly Dictionary<int,Texture2D> occlusionPixels = new Dictionary<int,Texture2D>();
        static bool occlusionArrived;
        static int occlusionEntryFrame, occlusionArrivalFrame;
        static IEnumerator OcclusionChecks()
        {
            Isolated();
            if(Directory.Exists(scratch) && Directory.EnumerateFileSystemEntries(scratch).Any())throw new InvalidOperationException("Occlusion scratch must be empty.");
            var preset=Resources.LoadAll<CharacterPreset>("Characters").OrderBy(p=>p.id).First();
            c.Repository.Create(1,preset.id,"遮挡基线"); Isolated(); c.LoadSlot(c.Repository.Read(1));
            occlusionEntryFrame=Time.frameCount; SampleFrames("natural-town-entry; no warm-up");
            float deadline=Time.realtimeSinceStartup+15; WorldPropLayer props=null;
            while(Time.realtimeSinceStartup<deadline)
            {
                props=UnityEngine.Object.FindAnyObjectByType<WorldPropLayer>();
                if(props!=null && props.PlacedForCheck)break;
                yield return null;
            }
            Check("occlusion natural Town entry",c.CurrentPage=="town" && c.World!=null && props!=null && props.PlacedForCheck,
                "entryFrame="+occlusionEntryFrame+" firstPlacedFrame="+Time.frameCount+" spawn="+c.PlayerWorldPosition+"; no Snap/camera override");
            Check("occlusion native Overlay",canvas!=null && canvas.renderMode==RenderMode.ScreenSpaceOverlay);
            if(c.World==null || props==null || !props.PlacedForCheck || canvas==null || canvas.renderMode!=RenderMode.ScreenSpaceOverlay)yield break;
            Pending("occlusion scope","Only gate/locust front/back baseline. No full integration rerun or overall approval. First natural arrival images are retained; entry/facing Frames are logged continuously.");
            var plots=Json.Parse(Resources.Load<TextAsset>("Config/building-plots").text)["plots"].Items;
            var gate=plots.Single(p=>p["id"].AsString("")=="plot-town-gate-south");
            var tree=plots.Single(p=>p["id"].AsString("")=="plot-ancient-locust");
            var worldRoot=(RectTransform)c.TravelerRectForShot.parent;
            var initialWorldScale=c.WorldScale; var initialRootScale=worldRoot.localScale;
            // Fixed points derive only from the existing solid footprint, not from a preferred image/frame.
            float gateX=N(gate,"footX")-N(gate,"width")*.28f;
            var gateFront=new Vector2(gateX,N(gate,"footY")+N(gate,"groundFootOffset")+24);
            var gateBack=new Vector2(gateX,N(gate,"footY")+N(gate,"collisionTop")-24);
            var treeFront=Point(tree,0,N(tree,"groundFootOffset")+24);
            var treeBack=Point(tree,0,N(tree,"collisionTop")-24);
            yield return OcclusionView("oc-01-gate-front",gate,true,gateFront);
            yield return OcclusionView("oc-02-gate-back",gate,false,gateBack);
            yield return OcclusionView("oc-03-locust-front",tree,true,treeFront);
            yield return OcclusionView("oc-04-locust-back",tree,false,treeBack);
            Check("occlusion original world scales",c.WorldScale==initialWorldScale && worldRoot.localScale==initialRootScale);
            Check("occlusion four retained first-arrival images",screenshots==4);
            Check("occlusion Frames sampled without invalid previous fade",frameSamples>0 && !frameFault,"samples="+frameSamples+" firstViews="+string.Join(",",firstFrameViews));
            foreach(var texture in occlusionPixels.Values)UnityEngine.Object.Destroy(texture);
            occlusionPixels.Clear();
        }
        static IEnumerator OcclusionWalk(string name,Vector2 goal)
        {
            occlusionArrived=false; occlusionArrivalFrame=-1;
            var before=c.PlayerWorldPosition; bool standable=c.World.CanStand(goal.x,goal.y);
            bool requested=standable && c.RequestTravelTo(goal);
            var route=Field(c,"travelRoute") as List<Vector2>;
            File.AppendAllText(Path.Combine(output,"occlusion-plans.jsonl"),JsonUtility.ToJson(new WalkPlan{name=name,frame=Time.frameCount,start=before,goal=goal,standable=standable,requested=requested,route=route==null?new List<Vector2>():new List<Vector2>(route)})+"\n");
            if(!requested){Pending(name+" real route","Fixed footprint-derived goal="+goal+" standable="+standable+" RequestTravelTo="+requested+"; no alternate point or teleport.");yield break;}
            bool continuous=true; int moved=0; float deadline=Time.realtimeSinceStartup+28;
            while(Time.realtimeSinceStartup<deadline && Vector2.Distance(c.PlayerWorldPosition,goal)>8)
            {
                yield return null; var next=c.PlayerWorldPosition;
                bool traversable=c.World.CanTraverse(before.x,before.y,next.x,next.y);
                continuous &= traversable;
                if(Vector2.Distance(before,next)>.001f)moved++;
                File.AppendAllText(Path.Combine(output,"routes.jsonl"),JsonUtility.ToJson(new Trace{route=name,frame=Time.frameCount,from=before,to=next,traversable=traversable,inside=c.World.CanStand(next.x,next.y)})+"\n");
                before=next;
            }
            occlusionArrived=Vector2.Distance(c.PlayerWorldPosition,goal)<=8;
            occlusionArrivalFrame=Time.frameCount;
            Check(name+" continuous product movement",continuous && moved>0,"movingFrames="+moved+" final="+c.PlayerWorldPosition+" goal="+goal);
            if(!occlusionArrived)Pending(name+" reached fixed goal","Natural movement did not reach within 28s; no retry/teleport; actual="+c.PlayerWorldPosition+" goal="+goal);
            Call("CancelTravel");
        }
        static IEnumerator OcclusionView(string name,JsonValue plot,bool front,Vector2 goal)
        {
            string id=plot["id"].AsString("");
            var place=(Field(c,"placeables") as List<FrontEndController.Placeable>)?.FirstOrDefault(p=>p.Id==id);
            if(place==null){Pending(name+" placed object","Missing existing product placeable "+id);yield break;}
            var rect=place.Rect; var originalScale=rect.localScale; var originalPosition=rect.anchoredPosition;
            BindTexture(rect.GetComponent<UnityEngine.UI.RawImage>()?.texture);
            yield return OcclusionWalk(name,goal);
            // The first rendered arrival is kept, even when it is defective or does not overlap.
            yield return new WaitForEndOfFrame(); SampleFrames("first-natural-arrival "+name);
            if(screenshots>=4)throw new InvalidOperationException("Occlusion screenshot limit exceeded.");
            Texture2D screenshot=null;
            try
            {
                screenshot=ScreenCapture.CaptureScreenshotAsTexture();
                if(screenshot==null || screenshot.width<1)throw new InvalidOperationException("No natural GameView image.");
                File.WriteAllBytes(Path.Combine(output,name+".png"),screenshot.EncodeToPNG()); screenshots++;
                var evidence=MeasureOcclusion(name,plot,place,front,goal,screenshot);
                File.WriteAllText(Path.Combine(output,name+".json"),JsonUtility.ToJson(evidence,true));
                File.AppendAllText(Path.Combine(output,"screenshots.jsonl"),JsonUtility.ToJson(new Shot{file=name+".png",page=c.CurrentPage,mode=mode,worldScale=c.WorldScale,treeScale=rect.localScale,playerScale=c.TravelerRectForShot.localScale,method=evidence.method,player=c.PlayerWorldPosition,frame=Time.frameCount,width=screenshot.width,height=screenshot.height,phase=-1})+"\n");
                if(occlusionArrived)Check(name+" live render order",evidence.sameParent && evidence.projectedFront==front && evidence.playerInFront==front && (evidence.playerDepth>evidence.objectDepth)==front,
                    "playerSibling="+evidence.playerSibling+" objectSibling="+evidence.objectSibling+" playerDepth="+evidence.playerDepth+" objectDepth="+evidence.objectDepth);
                if(evidence.bodyOverlapPixels==0 || !occlusionArrived)Pending(name+" direct body overlap",evidence.geometry);
                else Pending(name+" direct occlusion visual review","Opaque body overlap="+evidence.bodyOverlapPixels+"; solid final-composite samples="+evidence.solidSamples+" expected winner matches="+evidence.expectedWinnerMatches+". Raw image requires visual review; indices/alpha overlap alone are not a PASS.");
                Check(name+" original object placement/scale",rect.localScale==originalScale && rect.anchoredPosition==originalPosition);
            }
            finally{if(screenshot!=null)UnityEngine.Object.Destroy(screenshot);}
        }
        static Rect ScreenRect(RectTransform rect)
        {
            var corners=new Vector3[4];rect.GetWorldCorners(corners);
            var a=RectTransformUtility.WorldToScreenPoint(null,corners[0]);var b=RectTransformUtility.WorldToScreenPoint(null,corners[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x,b.x),Mathf.Min(a.y,b.y),Mathf.Max(a.x,b.x),Mathf.Max(a.y,b.y));
        }
        static Rect Intersect(Rect a,Rect b) => Rect.MinMaxRect(Mathf.Max(a.xMin,b.xMin),Mathf.Max(a.yMin,b.yMin),Mathf.Min(a.xMax,b.xMax),Mathf.Min(a.yMax,b.yMax));
        static PixelSurface Surface(UnityEngine.UI.RawImage image,int width,int height)
        {
            if(image==null || image.texture==null || !image.isActiveAndEnabled || image.canvasRenderer.cull)return null;
            var texture=image.texture; int key=texture.GetInstanceID();
            if(!occlusionPixels.TryGetValue(key,out var copy))
            {
                var previous=RenderTexture.active; var rt=RenderTexture.GetTemporary(texture.width,texture.height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
                try{Graphics.Blit(texture,rt);RenderTexture.active=rt;copy=new Texture2D(texture.width,texture.height,TextureFormat.RGBA32,false);copy.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);copy.Apply();occlusionPixels.Add(key,copy);}
                finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);}
            }
            var clip=new Rect(0,0,width,height);
            foreach(var mask in image.GetComponentsInParent<UnityEngine.UI.RectMask2D>())if(mask.isActiveAndEnabled)clip=Intersect(clip,ScreenRect(mask.rectTransform));
            return new PixelSurface{image=image,pixels=copy,screen=ScreenRect(image.rectTransform),clip=clip};
        }
        static Color Pixel(PixelSurface surface,Vector2 screen,out Vector2 uv)
        {
            uv=Vector2.zero;
            if(surface==null || !surface.screen.Contains(screen) || !surface.clip.Contains(screen))return Color.clear;
            if(!RectTransformUtility.ScreenPointToLocalPointInRectangle(surface.image.rectTransform,screen,null,out var local))return Color.clear;
            var r=surface.image.rectTransform.rect;var u=surface.image.uvRect;
            uv=new Vector2(u.x+(local.x-r.xMin)/r.width*u.width,u.y+(local.y-r.yMin)/r.height*u.height);
            var color=surface.pixels.GetPixelBilinear(uv.x,uv.y)*surface.image.color;
            color.a*=surface.image.canvasRenderer.GetInheritedAlpha();return color;
        }
        static float ColorError(Color a,Color b) => Mathf.Max(Mathf.Abs(a.r-b.r),Mathf.Max(Mathf.Abs(a.g-b.g),Mathf.Abs(a.b-b.b)));
        static OcclusionEvidence MeasureOcclusion(string name,JsonValue plot,FrontEndController.Placeable place,bool front,Vector2 goal,Texture2D screenshot)
        {
            var actor=c.TravelerPuppetForShot; var main=actor==null?null:Field(actor,"frameImage") as UnityEngine.UI.RawImage;
            var fade=actor==null?null:Field(actor,"frameFadeImage") as UnityEngine.UI.RawImage;
            var prop=place.Rect.GetComponent<UnityEngine.UI.RawImage>();
            var p=Surface(main,screenshot.width,screenshot.height);var f=Surface(fade,screenshot.width,screenshot.height);var o=Surface(prop,screenshot.width,screenshot.height);
            var traveler=c.TravelerRectForShot;var pos=c.PlayerWorldPosition;
            var evidence=new OcclusionEvidence{file=name+".png",objectId=place.Id,side=front?"front":"back",status="PENDING_VISUAL_REVIEW",frame=Time.frameCount,firstTownFrame=occlusionEntryFrame,arrivalFrame=occlusionArrivalFrame,width=screenshot.width,height=screenshot.height,
                reached=occlusionArrived,goal=goal,player=pos,standable=c.World.CanStand(pos.x,pos.y),sameParent=traveler.parent==place.Rect.parent,playerSibling=traveler.GetSiblingIndex(),objectSibling=place.Rect.GetSiblingIndex(),playerDepth=main==null?-1:main.depth,objectDepth=prop==null?-1:prop.depth,
                playerSortY=c.World.Elevation.ProjectY(pos.x,pos.y),objectSortY=place.BaseY,playerInFront=traveler.GetSiblingIndex()>place.Rect.GetSiblingIndex(),projectedFront=c.World.Elevation.ProjectY(pos.x,pos.y)>place.BaseY,worldScale=c.WorldScale,objectScale=place.Rect.localScale,playerScale=traveler.localScale,cameraPosition=((RectTransform)traveler.parent).anchoredPosition,
                method="ScreenCapture.CaptureScreenshotAsTexture; first WaitForEndOfFrame after real RequestTravelTo arrival; no warm-up/teleport/camera override; live texture GPU readback alpha + final-composite sample diagnostics"};
            evidence.geometry="actualFoot="+pos+" target="+goal+" objectGround="+(N(plot,"footY")+N(plot,"groundFootOffset"))+" collisionNorth="+(N(plot,"footY")+N(plot,"collisionTop"))+" collisionSouth="+(N(plot,"footY")+N(plot,"collisionDepth"))+"; reached="+occlusionArrived+"; actual opaque overlap required, rectangles do not count";
            if(p==null || o==null){evidence.geometry+="; live visible Frames/object surface unavailable";return evidence;}
            evidence.playerScreenRect=p.screen;evidence.objectScreenRect=o.screen;evidence.commonViewport=Intersect(p.clip,o.clip);
            var scan=Intersect(Intersect(p.screen,o.screen),evidence.commonViewport); var pixels=screenshot.GetPixels32();
            for(int y=Mathf.Max(0,Mathf.CeilToInt(scan.yMin));y<Mathf.Min(screenshot.height,Mathf.FloorToInt(scan.yMax));y++)
            for(int x=Mathf.Max(0,Mathf.CeilToInt(scan.xMin));x<Mathf.Min(screenshot.width,Mathf.FloorToInt(scan.xMax));x++)
            {
                var screen=new Vector2(x+.5f,y+.5f);var pc=Pixel(p,screen,out _);var fc=Pixel(f,screen,out _);var oc=Pixel(o,screen,out var uv);
                float alpha=fc.a+pc.a*(1-fc.a);if(alpha<.5f || oc.a<.5f)continue;
                evidence.alphaOverlapPixels++;
                // Ancient-locust trunk/root band in the existing art; canopy alone does not count.
                bool body=place.Id!="plot-ancient-locust" || (uv.x>=.35f && uv.x<=.65f && uv.y>=.025f && uv.y<=.55f);
                if(!body)continue;evidence.bodyOverlapPixels++;
                if(alpha<.98f || oc.a<.98f)continue;
                var combined=(fc*fc.a+pc*(pc.a*(1-fc.a)))/alpha; combined.a=alpha;
                var captured=(Color)pixels[y*screenshot.width+x];float pe=ColorError(captured,combined),oe=ColorError(captured,oc);
                evidence.solidSamples++; if((front?pe:oe)<=.12f)evidence.expectedWinnerMatches++;
                if(evidence.witnesses.Count<16 && evidence.solidSamples%37==1)evidence.witnesses.Add(new PixelWitness{screen=screen,objectUv=uv,screenshot=captured,player=combined,prop=oc,playerError=pe,propError=oe});
            }
            evidence.geometry+="; alphaOverlapPixels="+evidence.alphaOverlapPixels+" bodyOverlapPixels="+evidence.bodyOverlapPixels+"; objectScreen="+o.screen+" playerScreen="+p.screen+" viewport="+evidence.commonViewport;
            return evidence;
        }


        [Serializable] sealed class ExperienceSample
        {
            public string stage, gait, sheet;
            public int frame, frameIndex, actorSibling, gateSibling, outlineSibling, outlineDepth, gateDepth;
            public Vector2 player;
            public bool inside, occluded, framesSupported, sourceFrameEnabled, outlineRaycast, maskable;
            public float realtime, deltaTime, buildingAlpha, outlineAlpha, currentFrameAlpha, fadingFrameAlpha;
            public float lowestGroundAlpha, lowestOtherPropAlpha, waterAlpha, worldInheritedAlpha;
            public bool reduceMotion; public float pulsePhase; public Rect currentUv, previousUv;
        }
        static BuildingOcclusionFader experienceGate;
        static FrontEndController.Placeable experiencePlace;
        static JsonValue experiencePlot;
        static UnityEngine.UI.RawImage[] experienceGround;
        static List<FrontEndController.Placeable> experiencePlaces;
        static TownWaterSurface experienceWater;
        static int experienceSamples, experienceBlind, experienceIsolationFaults;
        static bool experienceReached, experienceEntryShot, experienceCoveredShot;
        static int experienceMaxBindings;
        static readonly List<ExperienceSample> experienceTrace = new List<ExperienceSample>();

        static IEnumerator ExperienceChecks()
        {
            Isolated();
            if(Directory.Exists(scratch) && Directory.EnumerateFileSystemEntries(scratch).Any())throw new InvalidOperationException("Experience scratch must be empty.");
            var preset=Resources.LoadAll<CharacterPreset>("Characters").OrderBy(p=>p.id).First();
            c.Repository.Create(1,preset.id,"门楼体验");Isolated();c.LoadSlot(c.Repository.Read(1));
            SampleFrames("experience natural town entry");
            var deadline=Time.realtimeSinceStartup+15;WorldPropLayer props=null;
            while(Time.realtimeSinceStartup<deadline){props=UnityEngine.Object.FindAnyObjectByType<WorldPropLayer>();if(props!=null&&props.PlacedForCheck)break;yield return null;}
            Check("experience natural scratch Town",c.CurrentPage=="town"&&props!=null&&props.PlacedForCheck);
            if(c.CurrentPage!="town"||props==null||!props.PlacedForCheck)yield break;
            experiencePlaces=Field(c,"placeables") as List<FrontEndController.Placeable>;
            experiencePlace=experiencePlaces?.FirstOrDefault(p=>p.Id=="plot-town-gate-south");
            experienceGate=experiencePlace?.Rect.GetComponent<BuildingOcclusionFader>();
            experiencePlot=Json.Parse(Resources.Load<TextAsset>("Config/building-plots").text)["plots"].Items.Single(p=>p["id"].AsString("")=="plot-town-gate-south");
            var chunks=Field(c,"chunkRects") as IEnumerable;
            experienceGround=chunks==null?Array.Empty<UnityEngine.UI.RawImage>():chunks.Cast<RectTransform>().Select(r=>r.GetComponent<UnityEngine.UI.RawImage>()).Where(r=>r!=null).ToArray();
            experienceWater=Field(c,"townWater") as TownWaterSurface;
            Check("experience opt-in south gate only",experienceGate!=null&&c.OcclusionBuildingCountForCheck==1&&experiencePlaces.Count(p=>p.Rect.GetComponent<BuildingOcclusionFader>()!=null)==1);
            if(experienceGate==null)yield break;
            BindTexture(experiencePlace.Rect.GetComponent<UnityEngine.UI.RawImage>()?.texture);
            var bounds=experienceGate.FootBounds;
            Check("experience actual groundFoot-height bounds",Mathf.Abs(bounds.yMin-(N(experiencePlot,"footY")+N(experiencePlot,"groundFootOffset")-N(experiencePlot,"height")))<.01f);
            var north=new Vector2(N(experiencePlot,"footX"),bounds.yMin-40);
            var south=new Vector2(north.x,bounds.yMax+80);
            yield return ExperienceWalk("natural-spawn-to-north",north,false);
            if(!experienceReached)yield break;
            yield return ExperienceWalk("north-to-south-full-channel",south,true);
            if(!experienceReached)yield break;
            // Functional recovery is sampled, not hidden: the exit image is explicitly post-transition.
            for(float until=Time.realtimeSinceStartup+.4f;Time.realtimeSinceStartup<until;){yield return new WaitForEndOfFrame();ExperienceObserve("exit-restoring");yield return null;}
            yield return ExperienceCapture("fx-03-south-restored");
            Check("experience outside restores gate and hides outline",Mathf.Abs(experienceGate.CurrentAlpha-1)<.001f&&c.TravelerSilhouetteForCheck!=null&&c.TravelerSilhouetteForCheck.VisibleAlpha==0);
            Check("experience first entry and deep-overlap captures",experienceEntryShot&&experienceCoveredShot);
            Check("experience no observed blind frame in supported branch",experienceSamples>0&&experienceBlind==0,"samples="+experienceSamples+" blind="+experienceBlind+"; state gate only, GPU visibility separately pending");
            Check("experience ground road water other props retain alpha",experienceGround.Length>0&&experienceIsolationFaults==0,"faults="+experienceIsolationFaults);
            var settings=Field(c,"settings");var motion=settings?.GetType().GetField("reduceMotion",Flags);
            if(motion==null){Pending("experience reduceMotion","Actual settings field unavailable; no substitute.");yield break;}
            bool previousMotion=(bool)motion.GetValue(settings);
            try
            {
                motion.SetValue(settings,true);
                var interior=new Vector2(north.x,bounds.yMin+experienceGate.FootBounds.height*.32f);
                yield return ExperienceWalk("reduceMotion-south-to-interior",interior,false);
                if(!experienceReached)yield break;
                float phase=c.TravelerSilhouetteForCheck.PulsePhase;
                for(float until=Time.realtimeSinceStartup+.32f;Time.realtimeSinceStartup<until;){yield return new WaitForEndOfFrame();ExperienceObserve("reduceMotion-held");yield return null;}
                var silhouette=c.TravelerSilhouetteForCheck;
                Check("experience reduceMotion freezes pulse keeps function",silhouette.ReduceMotion&&silhouette.PulsePhase==phase&&silhouette.VisibleAlpha>0&&Mathf.Abs(experienceGate.CurrentAlpha-.425f)<.002f);
                yield return ExperienceCapture("fx-04-reduce-motion");
                // Inject a long dt into the existing fader's transition only. No position, collision,
                // camera or character visibility mutation. This is NOT a fabricated engine long frame.
                float heldAlpha=experienceGate.CurrentAlpha;
                typeof(BuildingOcclusionFader).GetMethod("Observe",Flags).Invoke(experienceGate,new object[]{c.PlayerWorldPosition,c.World.Elevation.ProjectY(c.PlayerWorldPosition.x,c.PlayerWorldPosition.y),c.TravelerRectForShot,.6f});
                Check("experience long-delta transition bounded",experienceGate.CurrentAlpha>=.425f&&experienceGate.CurrentAlpha<=1f&&Mathf.Abs(experienceGate.CurrentAlpha-heldAlpha)<.002f);
                Pending("experience actual long engine frame","0.6s fader delta injection checks numerical bounds only. No deliberate main-thread stall; real long-frame visibility remains PENDING unless trace contains one.");
                var group=experienceGate.Group;var owned=silhouette.Graphic?.OwnedMaterialForCheck;
                Isolated();c.ShowMenu();
                Check("experience page disable restores alpha immediately",group!=null&&Mathf.Abs(group.alpha-1)<.001f&&silhouette.VisibleAlpha==0);
                yield return null;yield return null;
                Check("experience page destroy releases overlay and material",silhouette==null&&owned==null&&c.OcclusionBuildingCountForCheck==0&&c.TravelerSilhouetteForCheck==null);
            }
            finally {motion.SetValue(settings,previousMotion);}
            Check("experience three to six native screenshots",screenshots>=3&&screenshots<=6);
            Check("experience Frames fade observations",frameSamples>0&&!frameFault,"samples="+frameSamples+" bindingRefreshMax="+experienceMaxBindings);
            Pending("experience GPU visibility and clip/stencil","Review first-entry/deep-overlap/exit/reduceMotion images and trace. State flags alone do not prove 100% visible pixels. No synthetic mask/no-mask mutation; active Mask/RectMask2D constraints recorded for review.");
            Pending("experience less-than-0.1ms performance","Profiler marker TwelveJade.OcclusionExperience supplied; actual Profiler measurement (including Graphic rebuild/GPU) not yet collected. No source-only performance PASS.");
            Pending("experience other actor rendering branches","Frames current/fade pair with live UV/mirror supported. MeshDeform/Slices keep fade but have no silhouette; no pose replacement and no PASS for those branches.");
            Pending("experience unfinished character/layout gates","Current installed character assets retained. New identity/walk candidates and all-layout visual acceptance are not claimed.");
            foreach(var texture in occlusionPixels.Values)UnityEngine.Object.Destroy(texture);occlusionPixels.Clear();
        }

        static IEnumerator ExperienceWalk(string stage,Vector2 goal,bool captureCrossing)
        {
            experienceReached=false;var before=c.PlayerWorldPosition;
            bool requested=c.RequestTravelTo(goal);
            File.AppendAllText(Path.Combine(output,"experience-plans.jsonl"),JsonUtility.ToJson(new WalkPlan{name=stage,frame=Time.frameCount,start=before,goal=goal,standable=c.World.CanStand(goal.x,goal.y),requested=requested,route=new List<Vector2>((List<Vector2>)Field(c,"travelRoute"))})+"\n");
            if(!requested){Pending(stage+" route","Fixed target unavailable; no teleport or replacement: "+goal);yield break;}
            var deadline=Time.realtimeSinceStartup+30;bool continuous=true;int samples=0;
            while(Time.realtimeSinceStartup<deadline)
            {
                yield return new WaitForEndOfFrame();
                var next=c.PlayerWorldPosition;bool traversable=c.World.CanTraverse(before.x,before.y,next.x,next.y),standable=c.World.CanStand(next.x,next.y);
                continuous&=traversable&&standable;samples++;
                File.AppendAllText(Path.Combine(output,"routes.jsonl"),JsonUtility.ToJson(new Trace{route=stage,frame=Time.frameCount,from=before,to=next,traversable=traversable,inside=standable})+"\n");
                ExperienceObserve(stage);before=next;
                if(captureCrossing&&experienceGate.IsOccluding&&!experienceEntryShot)
                {experienceEntryShot=true;ExperienceCaptureNow("fx-01-north-entry");}
                if(captureCrossing&&experienceGate.IsOccluding&&!experienceCoveredShot&&next.y>experienceGate.FootBounds.yMin+experienceGate.FootBounds.height*.30f)
                {experienceCoveredShot=true;ExperienceCaptureNow("fx-02-deep-overlap");}
                if(Vector2.Distance(next,goal)<=8){experienceReached=true;break;}
                yield return null;
            }
            Call("CancelTravel");
            Check(stage+" real path collision",continuous&&samples>1,"samples="+samples+" actual="+c.PlayerWorldPosition+" goal="+goal);
            if(!experienceReached)Pending(stage+" arrival","Did not reach fixed point in 30s; no retry or Snap.");
        }

        static void ExperienceObserve(string stage)
        {
            var silhouette=c.TravelerSilhouetteForCheck;if(silhouette==null){experienceBlind++;return;}
            var current=silhouette.CurrentFrame;var previous=silhouette.PreviousFrame;var graphic=silhouette.Graphic;
            float ground=1f,other=1f;
            foreach(var image in experienceGround)ground=Mathf.Min(ground,image.color.a*image.canvasRenderer.GetInheritedAlpha());
            foreach(var place in experiencePlaces)
            {
                if(place==experiencePlace||place.Rect==null)continue;
                var image=place.Rect.GetComponent<UnityEngine.UI.RawImage>();
                if(image!=null)other=Mathf.Min(other,image.color.a*image.canvasRenderer.GetInheritedAlpha());
            }
            float water=experienceWater==null?1:experienceWater.canvasRenderer.GetInheritedAlpha();
            var sample=new ExperienceSample{stage=stage,frame=Time.frameCount,realtime=Time.realtimeSinceStartup,deltaTime=Time.unscaledDeltaTime,player=c.PlayerWorldPosition,
                inside=experienceGate.ContainsFoot(c.PlayerWorldPosition),occluded=experienceGate.IsOccluding,buildingAlpha=experienceGate.CurrentAlpha,outlineAlpha=silhouette.VisibleAlpha,framesSupported=silhouette.FramesSupported,
                sourceFrameEnabled=current!=null&&current.isActiveAndEnabled,currentFrameAlpha=current==null?0:current.color.a,fadingFrameAlpha=previous==null?0:previous.color.a,
                actorSibling=c.TravelerRectForShot.GetSiblingIndex(),gateSibling=experiencePlace.Rect.GetSiblingIndex(),outlineSibling=silhouette.transform.GetSiblingIndex(),outlineDepth=graphic==null?-1:graphic.depth,gateDepth=experiencePlace.Rect.GetComponent<UnityEngine.UI.RawImage>().depth,
                outlineRaycast=graphic!=null&&graphic.raycastTarget,maskable=graphic!=null&&graphic.maskable,lowestGroundAlpha=ground,lowestOtherPropAlpha=other,waterAlpha=water,worldInheritedAlpha=current==null?0:current.canvasRenderer.GetInheritedAlpha(),reduceMotion=silhouette.ReduceMotion,pulsePhase=silhouette.PulsePhase,
                currentUv=current==null?new Rect():current.uvRect,previousUv=previous==null?new Rect():previous.uvRect,gait=c.TravelerPuppetForShot.GaitModeForCheck.ToString(),sheet=current?.texture==null?"":current.texture.name,frameIndex=c.TravelerPuppetForShot.FrameIndexForCheck};
            experienceSamples++;experienceMaxBindings=Mathf.Max(experienceMaxBindings,silhouette.BindingRefreshCount);
            if(sample.occluded&&(!sample.framesSupported||sample.outlineAlpha<=0||sample.outlineDepth<=sample.gateDepth))experienceBlind++;
            // Ignore only the existing shared page fade for alpha isolation; log it verbatim.
            float parentAlpha=current==null?1:current.canvasRenderer.GetInheritedAlpha();
            if(Mathf.Abs(ground-parentAlpha)>.01f||Mathf.Abs(other-parentAlpha)>.01f||Mathf.Abs(water-parentAlpha)>.01f||sample.outlineRaycast||!sample.maskable)experienceIsolationFaults++;
            experienceTrace.Add(sample);
            File.AppendAllText(Path.Combine(output,"experience-frames.jsonl"),JsonUtility.ToJson(sample)+"\n");
        }

        static IEnumerator ExperienceCapture(string name){yield return new WaitForEndOfFrame();ExperienceObserve(name);ExperienceCaptureNow(name);}
        static void ExperienceCaptureNow(string name)
        {
            if(screenshots>=6)throw new InvalidOperationException("Experience six-image limit exceeded.");
            Texture2D texture=null;
            try
            {
                texture=ScreenCapture.CaptureScreenshotAsTexture();if(texture==null)throw new InvalidOperationException("Missing natural GameView image.");
                File.WriteAllBytes(Path.Combine(output,name+".png"),texture.EncodeToPNG());screenshots++;
                occlusionArrived=experienceReached;occlusionArrivalFrame=Time.frameCount;
                var geometry=MeasureOcclusion(name,experiencePlot,experiencePlace,c.PlayerWorldPosition.y>experienceGate.FootBounds.yMax,c.PlayerWorldPosition,texture);
                geometry.method="Live moving crossing projection; baseline opaque-winner matching is diagnostic only because gate alpha is now intentionally fractional. No arrival claim for crossing samples.";
                File.WriteAllText(Path.Combine(output,name+"-projection.json"),JsonUtility.ToJson(geometry,true));
                var silhouette=c.TravelerSilhouetteForCheck;
                var constraints=silhouette.Graphic.GetComponentsInParent<UnityEngine.UI.Mask>().Select(m=>m.name+":active="+m.isActiveAndEnabled)
                    .Concat(silhouette.Graphic.GetComponentsInParent<UnityEngine.UI.RectMask2D>().Select(m=>m.name+":active="+m.isActiveAndEnabled));
                File.AppendAllText(Path.Combine(output,"mask-constraints.txt"),name+": "+string.Join(",",constraints)+"\n");
                File.AppendAllText(Path.Combine(output,"screenshots.jsonl"),JsonUtility.ToJson(new Shot{file=name+".png",page=c.CurrentPage,mode=mode,worldScale=c.WorldScale,playerScale=c.TravelerRectForShot.localScale,method="Native Overlay at real crossing end-of-frame; projection diagnostics are not GPU visibility acceptance",player=c.PlayerWorldPosition,frame=Time.frameCount,width=texture.width,height=texture.height,phase=-1})+"\n");
            }
            finally{if(texture!=null)UnityEngine.Object.Destroy(texture);}
        }

        // Independent Nov6 layout evidence. Never moves props, changes camera/scale or bypasses collision.
        [Serializable] sealed class VignetteCollider
        {
            public string shape; public Rect worldRect; public Vector2 capsuleStart, capsuleEnd, center;
            public float radius; public bool registered, centerBlocked;
        }
        [Serializable] sealed class VignetteMember
        {
            public string id, art, liveArt, host, group; public Rect artRect, screenRect;
            public Vector2 ground; public float projectedGround, sortBase, hostGroundDistance, inheritedAlpha;
            public bool present, sourceArtMatches, fullyInFrame, inHudSafeFrame;
            public List<VignetteCollider> collisions = new List<VignetteCollider>();
        }
        [Serializable] sealed class VignetteNpc
        {
            public string id, art; public Vector2 configuredGround; public float liveSortBase, nearestMemberDistance;
            public Rect screenRect; public bool livePresent, visibleInFrame, lineReferencesStall;
        }
        [Serializable] sealed class VignetteCandidate
        {
            public string group; public Vector2 point; public bool standable, predictedWholeGroup, routeFound;
            public float routeLength; public int routePoints;
        }
        [Serializable] sealed class VignetteGeometry
        {
            public string group, file, status, method; public Vector2 player, goal, cameraPosition;
            public float worldScale; public int frame; public bool reached, wholeGroupInFrame, wholeGroupHudSafe;
            public Rect viewport, hudSafeViewport; public List<VignetteMember> members = new List<VignetteMember>();
            public List<VignetteNpc> npcs = new List<VignetteNpc>();
        }
        [Serializable] sealed class VignetteLane
        {
            public string name; public Vector2 from, to; public bool fromStandable, toStandable, traversable;
            public string method = "Read-only live collision query; actual player movement is separately recorded in routes.jsonl";
        }
        static IReadOnlyList<JsonValue> vignettePlots;
        static List<FrontEndController.Placeable> vignettePlaces;
        static bool vignetteReached;
        static float vignetteScale;
        static void VignetteGate(string name, bool observed, string detail)
        { if(observed)Check(name,true,detail);else Pending(name,detail); }
        static Rect VignetteHudSafe(int width,int height) => Rect.MinMaxRect(16,16,width-16,height-height*150f/1080f);
        static bool VignetteContains(Rect outer,Rect inner) => inner.width>0 && inner.height>0 &&
            inner.xMin>=outer.xMin && inner.xMax<=outer.xMax && inner.yMin>=outer.yMin && inner.yMax<=outer.yMax;
        static List<JsonValue> VignetteMembers(string host) => vignettePlots.Where(p=>p["id"].AsString("")==host || p["hostPlotId"].AsString("")==host).ToList();

        static IEnumerator VignetteChecks()
        {
            Isolated();
            if(Directory.Exists(scratch)&&Directory.EnumerateFileSystemEntries(scratch).Any())throw new InvalidOperationException("Vignette scratch must be empty.");
            var preset=Resources.LoadAll<CharacterPreset>("Characters").OrderBy(p=>p.id).First();
            c.Repository.Create(1,preset.id,"Layout followup");Isolated();c.LoadSlot(c.Repository.Read(1));
            float deadline=Time.realtimeSinceStartup+15;WorldPropLayer props=null;
            while(Time.realtimeSinceStartup<deadline){props=UnityEngine.Object.FindAnyObjectByType<WorldPropLayer>();if(props!=null&&props.PlacedForCheck)break;yield return null;}
            Check("vignette natural scratch Town",c.CurrentPage=="town"&&props!=null&&props.PlacedForCheck);
            if(c.World==null||props==null||!props.PlacedForCheck)yield break;
            Check("vignette native Overlay",canvas!=null&&canvas.renderMode==RenderMode.ScreenSpaceOverlay);
            vignettePlots=Json.Parse(Resources.Load<TextAsset>("Config/building-plots").text)["plots"].Items;
            vignettePlaces=Field(c,"placeables") as List<FrontEndController.Placeable>;
            vignetteScale=c.WorldScale;
            Check("vignette 100 configured and live plots",vignettePlots.Count==100&&vignettePlots.All(p=>vignettePlaces.Any(v=>v.Id==p["id"].AsString(""))));
            var lions=vignettePlots.Where(p=>p["art"].AsString("")=="Art/Props/stone-lion").ToArray();
            Check("vignette exactly two ancestral lions; no well or bridge lone lion",lions.Length==2&&lions.All(p=>p["hostPlotId"].AsString("")=="plot-ancestral-hall"));
            var west=vignettePlots.Single(p=>p["id"].AsString("")=="plot-ancestral-lion-west");
            var east=vignettePlots.Single(p=>p["id"].AsString("")=="plot-ancestral-lion-east");
            if(VignetteLevelFollowup)
            {
                float wg=N(west,"footY")+N(west,"groundFootOffset"),eg=N(east,"footY")+N(east,"groundFootOffset");
                Check("vignette-level lions symmetric axis3420 ground2380",Mathf.Abs(N(west,"footX")-3150)<.01f&&Mathf.Abs(N(east,"footX")-3690)<.01f&&Mathf.Abs(wg-2380)<.01f&&Mathf.Abs(eg-2380)<.01f);
                Check("vignette-level projected lion feet are level",Mathf.Abs(c.World.Elevation.ProjectY(N(west,"footX"),wg)-c.World.Elevation.ProjectY(N(east,"footX"),eg))<.01f);
                bool level=true;
                foreach(var lion in new[]{west,east})
                {
                    var cap=lion["collisionCapsule"];float cx=N(lion,"footX"),cy=N(lion,"footY")+N(cap,"y"),radius=N(cap,"radius");
                    // Sample the entire capsule bounding box; both supports must be on flat ground.
                    for(int ix=0;ix<=16;ix++)for(int iy=0;iy<=8;iy++)
                    {
                        float x=cx+Mathf.Lerp(N(cap,"x0")-radius,N(cap,"x1")+radius,ix/16f);
                        float y=cy+Mathf.Lerp(-radius,radius,iy/8f);
                        level &= Mathf.Abs(c.World.Elevation.HeightAt(x,y))<.01f;
                    }
                }
                Check("vignette-level both complete collider supports remain on ground0",level);
                Check("vignette-level central steps stay traversable",c.World.CanTraverse(3420,2352,3420,2470));
            }
            else
            {
            Check("vignette lions axis3420 and level ground2470",Mathf.Abs((N(west,"footX")+N(east,"footX"))*.5f-3420)<.01f&&
                Mathf.Abs(N(west,"footY")+N(west,"groundFootOffset")-2470)<.01f&&Mathf.Abs(N(east,"footY")+N(east,"groundFootOffset")-2470)<.01f);
            }
            var gate=vignettePlots.Single(p=>p["id"].AsString("")=="plot-town-gate-south");var piers=gate["collisionRects"].Items;
            float gap=piers.Where(p=>N(p,"x0")>=0).Min(p=>N(p,"x0"))-piers.Where(p=>N(p,"x1")<=0).Max(p=>N(p,"x1"));
            var shape=Json.Parse(Resources.Load<TextAsset>("Config/town-map").text)["terrainShape"];
            Check("vignette gate628.1981 mainStreet500 retained",Mathf.Abs(gap-628.1981f)<.02f&&Mathf.Abs(N(shape["mainRoad"],"half")*2-500)<.01f,"gate="+gap+" street="+(N(shape["mainRoad"],"half")*2));
            Pending("vignette inherited layout failures","Existing layout20FAIL/4WARN retained; this three-group run does not rerun or approve the full layout.");
            // Near-to-far order keeps all travel natural and fits the unchanged run time bound.
            yield return VignetteVisit("plot-grain-shop","vg-02-grain-front",new[]{new Vector2(2400,7210),new Vector2(2400,7260),new Vector2(2500,7180)});
            yield return VignetteVisit("plot-cloth-shop","vg-03-cloth-front",new[]{new Vector2(5400,7310),new Vector2(5400,7420),new Vector2(5520,7300)});
            // Existing main-road waypoints bound navigator searches without changing any geometry.
            foreach(float y in new[]{6200f,4300f,3000f})
            {
                var waypoint=new Vector2(c.World.Elevation.RoadX(y),y);
                if(c.World.CanStand(waypoint.x,waypoint.y))yield return VignetteWalk("ancestral-main-road-"+y,waypoint,25f);
                else Pending("ancestral waypoint "+y,"Actual CanStand=false; waypoint not forced.");
            }
            yield return VignetteVisit("plot-ancestral-hall","vg-01-ancestral-lions",new[]{new Vector2(3800,2090),new Vector2(3800,2120),new Vector2(3000,2090),new Vector2(3420,2090)});
            Check("vignette exactly three native captures",screenshots==3);
            Check("vignette world scale unchanged",Mathf.Abs(c.WorldScale-vignetteScale)<.0001f);
            Pending("vignette complete art and host visual acceptance","Whole artRect and source-art binding are recorded, but pixel visibility/overlays and believable group composition require the three JPEG reviews; rectangles alone do not approve the group.");
            Pending("vignette relevant NPC staging","All configured/live NPC relationships and distances are recorded per group. Existing stall dialogue associations do not prove NPCs were relocated with these stalls; source remains unchanged.");
        }

        static IEnumerator VignetteVisit(string host,string file,Vector2[] candidates)
        {
            var members=VignetteMembers(host);bool selected=false;Vector2 goal=c.PlayerWorldPosition;
            foreach(var point in candidates)
            {
                var candidate=new VignetteCandidate{group=host,point=point,standable=c.World.CanStand(point.x,point.y)};
                var world=(RectTransform)c.TravelerRectForShot.parent;
                var target=new Vector2(960-c.World.ClampCameraX(point.x,960/c.WorldScale)*c.WorldScale,
                    -540+c.World.ClampCameraY(c.World.Elevation.ProjectY(point.x,point.y),540/c.WorldScale)*c.WorldScale);
                var shiftWorld=world.parent.TransformVector((Vector3)(target-world.anchoredPosition));
                var shift=RectTransformUtility.WorldToScreenPoint(null,world.position+shiftWorld)-RectTransformUtility.WorldToScreenPoint(null,world.position);
                candidate.predictedWholeGroup=members.All(p=>{var placed=vignettePlaces.FirstOrDefault(v=>v.Id==p["id"].AsString(""));if(placed==null)return false;var rect=ScreenRect(placed.Rect);rect.position+=shift;return VignetteContains(VignetteHudSafe(Screen.width,Screen.height),rect);});
                if(candidate.standable)
                {
                    var path=TownNavigator.FindPath(c.World,c.PlayerWorldPosition.x,c.PlayerWorldPosition.y,point.x,point.y);
                    candidate.routeFound=path!=null&&path.Count>0;candidate.routePoints=path?.Count??0;
                    Vector2 before=c.PlayerWorldPosition;if(path!=null)foreach(var step in path){var next=new Vector2(step.x,step.y);candidate.routeLength+=Vector2.Distance(before,next);before=next;}
                }
                File.AppendAllText(Path.Combine(output,"vignette-candidates.jsonl"),JsonUtility.ToJson(candidate)+"\n");
                if(candidate.standable&&candidate.routeFound&&candidate.predictedWholeGroup){selected=true;goal=point;break;}
            }
            if(!selected)
            {
                // Preserve a partial natural view if no complete-frame point can be reached.
                foreach(var point in candidates)if(c.World.CanStand(point.x,point.y)){goal=point;selected=true;break;}
                Pending(host+" planned whole-group view","No candidate satisfies standability, path and predicted safe frame together; capture remains diagnostic.");
            }
            if(selected)yield return VignetteWalk(host,goal,40f);else{vignetteReached=false;Pending(host+" reachable point","No candidate is standable; no position override.");}
            // Allow the unchanged follow camera and actual movement deceleration to settle.
            for(float until=Time.realtimeSinceStartup+.5f;Time.realtimeSinceStartup<until;){yield return null;}
            VignetteLanes(host);
            yield return new WaitForEndOfFrame();
            var geometry=new VignetteGeometry{group=host,file=file+".png",status="PENDING_VISUAL_REVIEW",method="Natural spawn and RequestTravelTo only; source art/full rectangles/collision recorded without camera or scale changes",player=c.PlayerWorldPosition,goal=goal,reached=vignetteReached&&Vector2.Distance(c.PlayerWorldPosition,goal)<=12,worldScale=c.WorldScale,frame=Time.frameCount,cameraPosition=((RectTransform)c.TravelerRectForShot.parent).anchoredPosition,viewport=new Rect(0,0,Screen.width,Screen.height),hudSafeViewport=VignetteHudSafe(Screen.width,Screen.height)};
            foreach(var p in members)geometry.members.Add(VignetteDescribe(p,geometry.viewport,geometry.hudSafeViewport));
            foreach(var npc in c.World.Npcs)
            {
                var placed=vignettePlaces.FirstOrDefault(p=>p.Id=="npc-"+npc.Id);
                var row=new VignetteNpc{id=npc.Id,art=npc.Art,configuredGround=new Vector2(npc.X,npc.Y),livePresent=placed!=null,lineReferencesStall=npc.Line.Contains("摊")};
                row.nearestMemberDistance=geometry.members.Min(p=>Vector2.Distance(row.configuredGround,p.ground));
                if(placed!=null){row.liveSortBase=placed.BaseY;row.screenRect=ScreenRect(placed.Rect);row.visibleInFrame=VignetteContains(geometry.viewport,row.screenRect);}
                geometry.npcs.Add(row);
            }
            if(VignetteLevelFollowup && host=="plot-ancestral-hall")
            {
                var left=geometry.members.Single(p=>p.id=="plot-ancestral-lion-west");
                var right=geometry.members.Single(p=>p.id=="plot-ancestral-lion-east");
                Check("vignette-level live screen lion feet remain level",Mathf.Abs(left.screenRect.yMin-right.screenRect.yMin)<.5f&&Mathf.Abs(left.projectedGround-right.projectedGround)<.01f,
                    "left="+left.screenRect.yMin+" right="+right.screenRect.yMin+" projected="+left.projectedGround+"/"+right.projectedGround);
            }
            geometry.wholeGroupInFrame=geometry.members.All(p=>p.present&&p.sourceArtMatches&&p.fullyInFrame);
            geometry.wholeGroupHudSafe=geometry.members.All(p=>p.inHudSafeFrame);
            VignetteGate(host+" arrived by real path",geometry.reached,"actual="+geometry.player+" goal="+goal);
            VignetteGate(host+" complete group frame",geometry.reached&&geometry.wholeGroupInFrame&&geometry.wholeGroupHudSafe,"All host/member artRects, real source textures and safe viewport required; pixel-level visual review remains separate.");
            File.WriteAllText(Path.Combine(output,file+"-geometry.json"),JsonUtility.ToJson(geometry,true));
            Texture2D texture=null;
            try
            {
                if(screenshots>=3)throw new InvalidOperationException("Vignette three-image limit exceeded.");
                texture=ScreenCapture.CaptureScreenshotAsTexture();if(texture==null)throw new InvalidOperationException("Missing native vignette frame.");
                File.WriteAllBytes(Path.Combine(output,file+".png"),texture.EncodeToPNG());screenshots++;
                File.AppendAllText(Path.Combine(output,"screenshots.jsonl"),JsonUtility.ToJson(new Shot{file=file+".png",page=c.CurrentPage,method=geometry.method,mode=mode,player=c.PlayerWorldPosition,frame=Time.frameCount,width=texture.width,height=texture.height,worldScale=c.WorldScale,playerScale=c.TravelerRectForShot.localScale})+"\n");
            }
            finally{if(texture!=null)UnityEngine.Object.Destroy(texture);}
        }

        static IEnumerator VignetteWalk(string stage,Vector2 goal,float seconds)
        {
            vignetteReached=false;Vector2 before=c.PlayerWorldPosition;bool requested=c.RequestTravelTo(goal);
            File.AppendAllText(Path.Combine(output,"vignette-plans.jsonl"),JsonUtility.ToJson(new WalkPlan{name=stage,frame=Time.frameCount,start=before,goal=goal,standable=c.World.CanStand(goal.x,goal.y),requested=requested,route=new List<Vector2>((List<Vector2>)Field(c,"travelRoute"))})+"\n");
            if(!requested){Pending(stage+" path request","Actual RequestTravelTo rejected goal; no teleport or retry.");yield break;}
            bool continuous=true;int samples=0,moved=0;float until=Time.realtimeSinceStartup+seconds;
            while(Time.realtimeSinceStartup<until)
            {
                yield return new WaitForEndOfFrame();var next=c.PlayerWorldPosition;
                bool traverse=c.World.CanTraverse(before.x,before.y,next.x,next.y),stand=c.World.CanStand(next.x,next.y);
                continuous&=traverse&&stand;samples++;if((next-before).sqrMagnitude>.0001f)moved++;
                File.AppendAllText(Path.Combine(output,"routes.jsonl"),JsonUtility.ToJson(new Trace{route=stage,frame=Time.frameCount,from=before,to=next,traversable=traverse,inside=stand})+"\n");before=next;
                if(Vector2.Distance(next,goal)<=6){vignetteReached=true;break;}yield return null;
            }
            Call("CancelTravel");Check(stage+" real continuous collision",continuous&&samples>0,"samples="+samples+" moving="+moved);
            if(!vignetteReached)Pending(stage+" bounded arrival","Fixed real-path goal not reached; actual="+c.PlayerWorldPosition+" goal="+goal);
        }

        static VignetteMember VignetteDescribe(JsonValue p,Rect viewport,Rect safe)
        {
            string id=p["id"].AsString("");var placed=vignettePlaces.FirstOrDefault(v=>v.Id==id);
            float x=N(p,"footX"),y=N(p,"footY"),ground=y+(p.Has("groundFootOffset")?N(p,"groundFootOffset"):N(p,"height"));
            var row=new VignetteMember{id=id,art=p["art"].AsString(""),host=p["hostPlotId"].AsString(""),group=p["vignetteGroup"].AsString(""),artRect=new Rect(x-N(p,"width")*.5f,y,N(p,"width"),N(p,"height")),ground=new Vector2(x,ground),projectedGround=c.World.Elevation.ProjectY(x,ground),present=placed!=null};
            var host=vignettePlots.FirstOrDefault(v=>v["id"].AsString("")==row.host);
            if(host!=null)row.hostGroundDistance=Vector2.Distance(row.ground,new Vector2(N(host,"footX"),N(host,"footY")+N(host,"groundFootOffset")));
            if(placed!=null)
            {
                var image=placed.Rect.GetComponent<UnityEngine.UI.RawImage>();row.liveArt=image?.texture==null?"":AssetDatabase.GetAssetPath(image.texture);
                row.sourceArtMatches=image!=null&&image.texture!=null&&row.liveArt=="Assets/Resources/"+row.art+".png";
                if(image?.texture!=null)BindTexture(image.texture);
                row.screenRect=ScreenRect(placed.Rect);row.fullyInFrame=VignetteContains(viewport,row.screenRect);row.inHudSafeFrame=VignetteContains(safe,row.screenRect);
                row.sortBase=placed.BaseY;row.inheritedAlpha=image==null?0:image.color.a*image.canvasRenderer.GetInheritedAlpha();
                Check(id+" real ground sort",Mathf.Abs(row.sortBase-row.projectedGround)<.02f);
            }
            if(p.Has("collisionCapsule"))
            {
                var cap=p["collisionCapsule"];var start=new Vector2(x+N(cap,"x0"),y+N(cap,"y"));var end=new Vector2(x+N(cap,"x1"),start.y);float radius=N(cap,"radius");
                var registered=Field(c.World,"footCapsules") as IDictionary;
                bool found=registered!=null&&registered.Contains("world-prop")&&((IEnumerable)registered["world-prop"]).Cast<object>().Any(o=>o is ValueTuple<float,float,float,float> t&&Mathf.Abs(t.Item1-start.x)<.02f&&Mathf.Abs(t.Item2-end.x)<.02f&&Mathf.Abs(t.Item3-start.y)<.02f&&Mathf.Abs(t.Item4-radius)<.02f);
                var center=(start+end)*.5f;row.collisions.Add(new VignetteCollider{shape="capsule",capsuleStart=start,capsuleEnd=end,center=center,radius=radius,registered=found,centerBlocked=!c.World.CanStand(center.x,center.y)});
            }
            else if(p.Has("collisionRects"))foreach(var rect in p["collisionRects"].Items)
            {
                var world=Rect.MinMaxRect(x+N(rect,"x0"),y+N(rect,"y0"),x+N(rect,"x1"),y+N(rect,"y1"));var registered=Field(c.World,"externalBlockers") as IDictionary;
                bool found=registered!=null&&registered.Contains("world-prop")&&((IEnumerable)registered["world-prop"]).Cast<MapRect>().Any(r=>Mathf.Abs(r.MinX-world.xMin)<.02f&&Mathf.Abs(r.MaxX-world.xMax)<.02f&&Mathf.Abs(r.MinY-world.yMin)<.02f&&Mathf.Abs(r.MaxY-world.yMax)<.02f);
                row.collisions.Add(new VignetteCollider{shape="rectangle",worldRect=world,center=world.center,registered=found,centerBlocked=!c.World.CanStand(world.center.x,world.center.y)});
            }
            Check(id+" original collision registered and solid",row.collisions.Count>0&&row.collisions.All(v=>v.registered&&v.centerBlocked));
            return row;
        }
        static void VignetteLaneProbe(string name,Vector2 a,Vector2 b)
        {
            var lane=new VignetteLane{name=name,from=a,to=b,fromStandable=c.World.CanStand(a.x,a.y),toStandable=c.World.CanStand(b.x,b.y),traversable=c.World.CanTraverse(a.x,a.y,b.x,b.y)};
            File.AppendAllText(Path.Combine(output,"vignette-lanes.jsonl"),JsonUtility.ToJson(lane)+"\n");
            VignetteGate(name+" collision-query passage",lane.fromStandable&&lane.toStandable&&lane.traversable,"Actual collision query only; continuous player routes are recorded separately.");
        }
        static void VignetteLanes(string host)
        {
            if(host=="plot-ancestral-hall")VignetteLaneProbe("ancestral central approach between lions",new Vector2(3420,2352),new Vector2(3420,2570));
            else if(host=="plot-grain-shop")
            {
                VignetteLaneProbe("grain front central aisle",new Vector2(2080,7260),new Vector2(2080,7500));
                VignetteLaneProbe("grain west longitudinal street",new Vector2(2400,6900),new Vector2(2400,7600));
            }
            else
            {
                VignetteLaneProbe("cloth front central aisle",new Vector2(4950,7500),new Vector2(4950,7780));
                VignetteLaneProbe("cloth east street farmland access",new Vector2(5400,7100),new Vector2(5400,7820));
            }
        }

        static void BindTexture(Texture texture)
        {
            string path=texture==null?"":AssetDatabase.GetAssetPath(texture); if(string.IsNullOrEmpty(path)){Pending("visual texture binding","Live texture has no source asset path.");return;} CaptureInputs(new[]{path,path+".meta"});
        }
        static Bounds ViewBounds(RectTransform rect) => RectTransformUtility.CalculateRelativeRectTransformBounds(c.TravelerRectForShot.parent.parent,rect);
        internal static void SampleFrames(string stage="late-update")
        {
            if((!VisualFollowup && !OcclusionFollowup && !OcclusionExperience) || finished || c==null || c.CurrentPage!="town")return;
            var actor=c.TravelerPuppetForShot; if(actor==null)return;
            var fade=Field(actor,"frameFadeImage") as UnityEngine.UI.RawImage; var image=Field(actor,"frameImage") as UnityEngine.UI.RawImage; var previous=Field(actor,"framePrevIndex");
            if(fade==null || image==null || !(previous is int)){if(!frameProbeMissing){frameProbeMissing=true;Pending("Frames probe available","Actual Frames layer or private previous index unavailable.");}return;}
            BindTexture(image.texture); int prev=(int)previous,view=c.TravelerViewForShot,count=actor.FrameCountForCheck; var uv=fade.uvRect; float alpha=fade.color.a;
            bool valid=actor.GaitModeForCheck==PuppetActor.GaitMode.Frames && count>0 && alpha>=0 && alpha<=1 && (alpha<=.0001f || (prev>=0 && prev<count && Mathf.Abs(Mathf.Abs(uv.width)-1f/count)<.0001f && Mathf.Abs(Mathf.Min(uv.x,uv.x+uv.width)-prev/(float)count)<.0001f && uv.y==0 && uv.height==1));
            var trace=new FrameTrace{stage=stage,sheet=image.texture==null?"":image.texture.name,frame=Time.frameCount,view=view,index=actor.FrameIndexForCheck,previous=prev,count=count,alpha=alpha,uv=new Vector4(uv.x,uv.y,uv.width,uv.height),valid=valid}; string json=JsonUtility.ToJson(trace);
            File.AppendAllText(Path.Combine(output,"frames.jsonl"),json+"\n"); frameSamples++; if(!valid && !frameFault){frameFault=true;Check("invalid previous-frame fade",false,json);}
            if(actor.FrameIndexForCheck>=0 && prev<0)firstFrameViews.Add(view); frontBackTurn|=(lastView==0 && view==2)||(lastView==2 && view==0); lastView=view;
            if(visualTree!=null){var content=(RectTransform)actor.Root.parent.parent;var combined=ViewBounds(visualTree);combined.Encapsulate(ViewBounds(actor.Root));((RectTransform)actor.Root.parent).anchoredPosition+=content.rect.center-(Vector2)combined.center;}
        }
        static Vector2 XY(JsonValue v) => new Vector2(v[0].AsFloat(float.NaN),v[1].AsFloat(float.NaN));
        static Rect Box(JsonValue v) => Rect.MinMaxRect(v[0].AsFloat(float.NaN),v[1].AsFloat(float.NaN),v[2].AsFloat(float.NaN),v[3].AsFloat(float.NaN));
        static bool Within(Rect r,Vector2 p) => p.x>=r.xMin && p.x<=r.xMax && p.y>=r.yMin && p.y<=r.yMax;
        static IEnumerator Circulation(IReadOnlyList<JsonValue> plots)
        {
            var asset=Resources.Load<TextAsset>("Config/town-circulation"); if(asset==null){Check("tree island contract exists",false);yield break;}
            var config=Json.Parse(asset.text); var bounds=Box(config["bounds"]); float clearance=N(config,"characterClearance");
            var bands=config["bands"].Items.Select(b=>Box(b["rect"])).ToList(); Func<Vector2,bool> allowed=v=>Within(bounds,v)&&bands.Any(b=>Within(b,v));
            Check("tree island four bands and clearance",bands.Count==4 && clearance==60 && bands.All(b=>b.width>=2*clearance&&b.height>=2*clearance));
            var tree=plots.Single(p=>p["id"].AsString("")==config["landscape"]["plotId"].AsString("")); var cap=tree["collisionCapsule"];
            float cx0=N(tree,"footX")+N(cap,"x0"),cx1=N(tree,"footX")+N(cap,"x1"),cy=N(tree,"footY")+N(cap,"y"),radius=N(cap,"radius");
            var registered=Field(c.World,"footCapsules") as IDictionary; var landscape=Box(config["landscape"]["rect"]);
            bool same=registered!=null && registered.Contains("world-prop") && ((IEnumerable)registered["world-prop"]).Cast<object>().Any(o=>o is ValueTuple<float,float,float,float> t && Mathf.Abs(t.Item1-cx0)<.02f&&Mathf.Abs(t.Item2-cx1)<.02f&&Mathf.Abs(t.Item3-cy)<.02f&&Mathf.Abs(t.Item4-radius)<.02f);
            Check("tree island uses existing solid capsule",same && !c.World.CanStand((cx0+cx1)/2,cy) && Mathf.Abs(landscape.xMin-(cx0-radius))<.02f && Mathf.Abs(landscape.xMax-(cx1+radius))<.02f && Mathf.Abs(landscape.yMin-(cy-radius))<.02f && Mathf.Abs(landscape.yMax-(cy+radius))<.02f);
            foreach(string side in new[]{"north","east","south","west"}){var p=XY(config["entrances"][side]);Check("tree island entrance "+side,allowed(p)&&c.World.CanStand(p.x,p.y));}
            var routes=config["routes"].Items; var expected=new[]{"north-south-west","north-south-east","west-east-north","west-east-south"};
            Check("four declared circulation routes",routes.Select(r=>r["id"].AsString("")).OrderBy(v=>v).SequenceEqual(expected.OrderBy(v=>v)));
            foreach(var route in routes)
            {
                string name=route["id"].AsString(""); var points=route["points"].Items.Select(XY).ToList(); bool ns=name.StartsWith("north-south-",StringComparison.Ordinal);
                Check(name+" declared entrances/bands",points.Count>=2 && points.All(allowed) && Vector2.Distance(points[0],XY(config["entrances"][ns?"north":"west"]))<.01f && Vector2.Distance(points[points.Count-1],XY(config["entrances"][ns?"south":"east"]))<.01f);
                if(points.Count<2)continue;
                bool sweep=true; float minClearance=float.PositiveInfinity;
                for(int i=1;i<points.Count;i++)
                {
                    var a=points[i-1];var b=points[i];var delta=b-a; var normal=new Vector2(-delta.y,delta.x).normalized;
                    sweep &= Mathf.Abs(delta.x)<.001f || Mathf.Abs(delta.y)<.001f;
                    float dx=Mathf.Max(0,Mathf.Max(Mathf.Min(a.x,b.x)-cx1,cx0-Mathf.Max(a.x,b.x))),dy=Mathf.Max(0,Mathf.Max(Mathf.Min(a.y,b.y)-cy,cy-Mathf.Max(a.y,b.y)));
                    minClearance=Mathf.Min(minClearance,Mathf.Sqrt(dx*dx+dy*dy)-radius);
                    for(int lane=-3;lane<=3;lane++){var shift=normal*(clearance*lane/3);var u=a+shift;var v=b+shift;sweep &= allowed(u)&&allowed(v)&&c.World.CanTraverse(u.x,u.y,v.x,v.y);}
                }
                Check(name+" exact root clearance and seven collision lanes",sweep&&minClearance>=clearance,"root clearance="+minClearance+"; other registered collision sampled at seven offsets across +/-"+clearance);
                if(!Snap(points[0]))continue;
                for(int i=1;i<points.Count;i++){var a=c.PlayerWorldPosition;var b=points[i];yield return Travel("plaza "+name+" leg "+i,b,Mathf.Min(a.x,b.x)-4,Mathf.Max(a.x,b.x)+4,allowed);}
            }
        }
        static IEnumerator Travel(string name,Vector2 goal,float left,float right,Func<Vector2,bool> allowed=null)
        {
            bool requested=c.RequestTravelTo(goal); Check(name+" requested",requested); if(!requested)yield break;
            Vector2 previous=c.PlayerWorldPosition; float yMin=Mathf.Min(previous.y,goal.y)-4,yMax=Mathf.Max(previous.y,goal.y)+4; float deadline=Time.realtimeSinceStartup+12; bool valid=true; int moved=0;
            while(Time.realtimeSinceStartup<deadline && (c.PlayerWorldPosition-goal).magnitude>8)
            {
                yield return null; Vector2 next=c.PlayerWorldPosition;
                bool traversable=c.World.CanTraverse(previous.x,previous.y,next.x,next.y),inside=next.x>=left && next.x<=right && next.y>=yMin && next.y<=yMax && (allowed==null || allowed(next));
                valid &= traversable && inside; if((next-previous).sqrMagnitude>.0001f)moved++;
                File.AppendAllText(Path.Combine(output,"routes.jsonl"),JsonUtility.ToJson(new Trace{route=name,frame=Time.frameCount,from=previous,to=next,traversable=traversable,inside=inside})+"\n"); previous=next;
            }
            Check(name+" actual continuous movement",valid && moved>1 && (c.PlayerWorldPosition-goal).magnitude<=8,"frames="+moved+" final="+c.PlayerWorldPosition+" goal="+goal+" x=["+left+","+right+"]"); Call("CancelTravel");
        }
        static void Blocked(string name,Vector2 outside,Vector2 inside)
        {
            Check(name+" actual map solid",!c.World.CanStand(inside.x,inside.y) && !c.World.CanTraverse(outside.x,outside.y,inside.x,inside.y));
            if(!Snap(outside)){Pending(name+" TryMove","Fixed approach is blocked; no alternate position selected.");return;}
            var method=c.GetType().GetMethod("TryMove",Flags); if(method==null){Pending(name+" TryMove","Private movement entry unavailable.");return;}
            Vector2 before=c.PlayerWorldPosition; method.Invoke(c,new object[]{inside-before});
            Check(name+" actual traveler blocked",(c.PlayerWorldPosition-before).sqrMagnitude<.0001f,"before="+before+" after="+c.PlayerWorldPosition);
        }
        static IEnumerator SortProbe(JsonValue p,bool small)
        {
            string id=p["id"].AsString(""); var places=Field(c,"placeables") as List<FrontEndController.Placeable>;
            var place=places?.FirstOrDefault(v=>v.Id==id); if(place==null){Check(id+" placed",false);yield break;}
            float x=N(p,"footX"),y=N(p,"footY"),foot=y+(p.Has("groundFootOffset")?N(p,"groundFootOffset"):N(p,"height"));
            Check(id+" projected foot key",Mathf.Abs(place.BaseY-c.World.Elevation.ProjectY(x,foot))<.02f);
            foreach(bool front in new[]{false,true})
            {
                float py=front?foot+20:y+N(p,"collisionTop")-20;
                if(!Snap(new Vector2(x,py))){Pending(id+" sort side",front?"Fixed front blocked":"Fixed back blocked");continue;}
                yield return null;
                var actor=c.TravelerRectForShot; bool expected=c.World.Elevation.ProjectY(x,py)>place.BaseY;
                Check(id+(front?" front":" back")+" sibling order",expected==front && (actor.GetSiblingIndex()>place.Rect.GetSiblingIndex())==front);
                if(!small)yield return Capture(front?"04-building-front":"03-building-back");
            }
        }
        static void MeshHoles(TownWaterSurface water,IReadOnlyList<JsonValue> bridges,float bank)
        {
            var mesh=Field(water,"ownedMesh") as Mesh; var ribbon=Field(water,"ribbon") as TownWaterGeometry.Ribbon;
            if(mesh==null || ribbon==null){Pending("bridge deck mesh holes","Actual owned mesh/ribbon unavailable.");return;}
            var indices=mesh.triangles; var vertices=mesh.vertices;
            bool bound=vertices.Length==ribbon.Vertices.Count && indices.SequenceEqual(ribbon.Indices);
            var worldRoot=(RectTransform)water.transform.parent;
            for(int i=0;i<vertices.Length && bound;i++)
            { var v=ribbon.Vertices[i]; var actual=worldRoot.InverseTransformPoint(water.transform.TransformPoint(vertices[i])); bound &= Mathf.Abs(actual.x-v.X*c.WorldScale)<.05f && Mathf.Abs(actual.y+c.World.Elevation.ProjectY(v.X,v.Y)*c.WorldScale)<.05f; }
            Check("actual mesh bound to live world ribbon",bound); if(!bound)return;
            foreach(var b in bridges)
            {
                float x=N(b,"x"),y=c.World.Elevation.RiverY(x),half=N(b,"half"); var box=Rect.MinMaxRect(x-half,y-bank-20,x+half,y+bank+20); int hits=0;
                for(int i=0;i<indices.Length;i+=3)
                { var tri=new List<Vector2>(); for(int k=0;k<3;k++){var v=ribbon.Vertices[indices[i+k]];tri.Add(new Vector2(v.X,v.Y));} if(ClippedArea(tri,box)>.0001f)hits++; }
                Check("actual bridge deck subtraction "+x,hits==0,"overlapping triangles="+hits);
            }
        }
        static float ClippedArea(List<Vector2> polygon,Rect box)
        {
            for(int edge=0;edge<4 && polygon.Count>0;edge++)
            {
                var clipped=new List<Vector2>(); Func<Vector2,float> d=v=>edge==0?v.x-box.xMin:edge==1?box.xMax-v.x:edge==2?v.y-box.yMin:box.yMax-v.y;
                Vector2 a=polygon[polygon.Count-1]; float da=d(a);
                foreach(var b in polygon){float db=d(b);if((da>=0)!=(db>=0))clipped.Add(Vector2.LerpUnclamped(a,b,da/(da-db)));if(db>=0)clipped.Add(b);a=b;da=db;} polygon=clipped;
            }
            double area=0; for(int i=0;i<polygon.Count;i++){var a=polygon[i];var b=polygon[(i+1)%polygon.Count];area+=(double)a.x*b.y-(double)a.y*b.x;} return (float)Math.Abs(area*.5);
        }
        static IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame(); Texture2D texture=null;
            if(VisualFollowup){SampleFrames("capture");if(visualTree!=null){var content=(RectTransform)visualTree.parent.parent;var b=ViewBounds(visualTree);b.Encapsulate(ViewBounds(c.TravelerRectForShot));Check(name+" tree/player inside common viewport",content.rect.Contains((Vector2)b.min)&&content.rect.Contains((Vector2)b.max));}}
            try
            {
                if(canvas==null || canvas.renderMode!=RenderMode.ScreenSpaceOverlay)throw new InvalidOperationException("Native Overlay required.");
                texture=ScreenCapture.CaptureScreenshotAsTexture(); if(texture==null || texture.width<1 || texture.height<1)throw new InvalidOperationException("No real GameView image.");
                File.WriteAllBytes(Path.Combine(output,name+".png"),texture.EncodeToPNG()); screenshots++;
                var water=Field(c,"townWater") as TownWaterSurface;
                File.AppendAllText(Path.Combine(output,"screenshots.jsonl"),JsonUtility.ToJson(new Shot{file=name+".png",page=c.CurrentPage,mode=mode,worldScale=c.WorldScale,treeScale=visualTree==null?Vector3.zero:visualTree.localScale,playerScale=c.TravelerRectForShot.localScale,method="ScreenCapture.CaptureScreenshotAsTexture after WaitForEndOfFrame",player=c.PlayerWorldPosition,frame=Time.frameCount,width=texture.width,height=texture.height,phase=water==null?-1:water.FlowPhase})+"\n");
            }
            finally { if(texture!=null)UnityEngine.Object.Destroy(texture); }
        }
        static string Sha(string path) { using(var hash=SHA256.Create())using(var stream=File.OpenRead(path))return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").ToLowerInvariant(); }
        static void CaptureInputs(IEnumerable<string> paths=null)
        {
            foreach(string relative in paths ?? BoundPaths.Concat(VisualFollowup ? new[]{"Assets/Scripts/Presentation/PuppetActor.cs","Assets/Scripts/Presentation/PuppetActor.cs.meta","Assets/Scripts/Presentation/PuppetMeshGraphic.cs","Assets/Scripts/Presentation/PuppetRig.cs","Assets/Scripts/Presentation/PuppetGait.cs"} : Array.Empty<string>()))
            {
                string live=Path.GetFullPath(Path.Combine(Application.dataPath,"..",relative)),original=Path.Combine(source,relative);
                if(inputs.Any(s=>s.path==live))continue;
                if(!File.Exists(live)||!File.Exists(original)){Check("input exists "+relative,false);continue;}
                string a=Sha(live),b=Sha(original); inputs.Add(new Stamp{path=live,sha256=a}); inputs.Add(new Stamp{path=original,sha256=b}); Check("source/fixture parity "+relative,a==b);
            }
        }
        static void SaveReport(string status) { if(!string.IsNullOrEmpty(output))File.WriteAllText(Path.Combine(output,"runtime-results.json"),JsonUtility.ToJson(new Report{status=status,mode=mode,scope=VignetteLevelFollowup?"nov6-ancestral-lions-level; natural paths and original collider shapes":VignetteFollowup?"nov6-three-vignettes; natural paths only; product unchanged":OcclusionExperience?"nov6-gate-fade-live-Frames-outline; independent natural passage":OcclusionFollowup?"gate-locust-direct-occlusion-baseline; first natural arrivals; full run NOT RERUN":VisualFollowup?"character-tree-views-only; full run NOT RERUN":"full-town-integration",unityVersion=Application.unityVersion,screenshots=screenshots,checks=rows,inputs=inputs},true)); }
        static void Finish()
        {
            if(finished)return; finished=true; EditorApplication.update-=Tick; Application.logMessageReceived-=OnLog;
            foreach(var stamp in inputs)rows.Add(new Row{name="input drift "+stamp.path,status=File.Exists(stamp.path)&&Sha(stamp.path)==stamp.sha256?"PASS":"FAIL"});
            Pending("visual acceptance","Screenshots need pE review; no full-map, slope or overall acceptance claimed.");
            string status=rows.Any(r=>r.status=="FAIL")?"FAIL":rows.Any(r=>r.status=="PENDING" && r.name!="visual acceptance" && !(VisualFollowup && r.name=="full run not rerun"))?"PENDING":VisualFollowup?"VISUAL_FOLLOWUP_CHECKS_PASS_VISUAL_PENDING":"CHECKS_PASS_VISUAL_PENDING";
            SaveReport(status); SessionState.SetBool(Key,false); var old=SessionState.GetString(Key+".OldSave",""); Environment.SetEnvironmentVariable("TWELVEJADE_SAVEDIR",old.Length==0?null:old);
            EditorApplication.Exit(status=="CHECKS_PASS_VISUAL_PENDING" || status=="VISUAL_FOLLOWUP_CHECKS_PASS_VISUAL_PENDING"?0:1);
        }
    }
    public sealed class TownMemoIntegrationPump : MonoBehaviour { void LateUpdate(){TownMemoIntegrationShot.SampleFrames();} }
}
