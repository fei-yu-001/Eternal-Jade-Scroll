using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using TwelveJade.Core;
using TwelveJade.Presentation;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TwelveJade.Editor
{
    // Independent evidence runner; existing product code and old assertions are untouched.
    [InitializeOnLoad]
    public static class OcclusionValidationShot
    {
        const string Key="OcclusionValidationShot";
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        static string output,source,scratch,stage="bootstrap";
        static bool running,finished,measuring,markerCreated,originalReduce;
        static double started;
        static int shots,beforeFrame;
        static long beforeTicks,beforeGc;
        static FrontEndController controller;
        static BuildingOcclusionFader gate;
        static TravelerSilhouette silhouette;
        static object settings;
        static FieldInfo reduceField;
        static Func<long> allocated;
        static ProfilerRecorder marker;
        static readonly List<Row> rows=new List<Row>();
        static readonly List<Frame> frames=new List<Frame>(4096);
        static readonly List<CpuFrame> cpu=new List<CpuFrame>(1024);
        static readonly List<RebuildFrame> rebuild=new List<RebuildFrame>(400);
        static readonly WaitForEndOfFrame EndFrame=new WaitForEndOfFrame();
        static readonly List<Panel> panels=new List<Panel>();
        static GameObject fixture;
        static Texture2D controlTexture;
        static RawImage frameSource,fadeSource,controlSource,controlFade;
        [Serializable] public sealed class Row{public string name,status,detail;}
        [Serializable] public sealed class Report{public string status,overall="NOT_APPROVED",mode="occlusion-active-validation";public int screenshots;public List<Row> checks;}
        [Serializable] public sealed class Frame
        {
            public string stage;public int frame,frameIndex;public double elapsed,wallSeconds;
            public float dt,buildingAlpha,outlineAlpha,pulse,frameAlpha,previousAlpha;
            public Vector2 player;public bool inside,occluded,supported,reduceMotion,routeStandable,routeTraversable;
        }
        [Serializable] public struct CpuFrame{public int frame;public long envelopeNs,gcBytes,markerLastValue,markerSampleCount;public float frameDt;public bool occluded,markerValid;}
        [Serializable] public struct RebuildFrame{public int frame;public long nanoseconds,gcBytes,timerBaselineNs;public bool ready,culled,rectClip;}
        [Serializable] public sealed class MarkerRow{public int index;public long nanoseconds,calls;}
        [Serializable] public sealed class Distribution{public string scope,unit="nanoseconds";public int samples,over100us;public long min,p50,p95,p99,max,gcSum,gcMax;public double mean;}
        [Serializable] public sealed class PixelRow
        {
            public string name,source,mode;public Rect graphic,clip;public bool maskActive,rectActive,actualRectClip;
            public int expectedStencil,actualStencil,stencilCompare,insidePositive,outsidePositive,insideRetained,outsideLeaked;
            public float retainedFraction,leakedFraction;public string material,shader;public bool passed;
        }
        [Serializable] public sealed class PixelSample{public string panel,region;public int x,y,baselineR,baselineG,baselineB,unmaskedR,unmaskedG,unmaskedB,maskedR,maskedG,maskedB;}
        [Serializable] public sealed class Pause{public int requestedMs;public double actualBlockedSeconds;public Frame before;public List<Frame> after;public bool reduced;}
        [Serializable] public sealed class Binding{public string currentTexture,previousTexture,shader;public Rect currentUv,previousUv;public float currentAlpha,previousAlpha;public int currentInstance,previousInstance;public bool currentLive,previousLive;}
        [Serializable] public sealed class Shot{public string file,stage;public int frame,width,height;public Vector2 player;}
        sealed class Panel
        {
            public string name,mode,source;public RectTransform graphicRect,clipRect;
            public TravelerSilhouetteGraphic graphic;public Mask[] masks;public RectMask2D rectMask;
            public Action<RawImage,RawImage,float> sync;public RawImage current,previous;
        }
        static OcclusionValidationShot(){if(SessionState.GetBool(Key,false))Attach();}
        public static void Run()
        {
            if(Application.isBatchMode||EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Isolated non-batch editor required");
            output=Environment.GetEnvironmentVariable("OCCLUSION_VALIDATION_EVIDENCE");source=Environment.GetEnvironmentVariable("OCCLUSION_VALIDATION_SOURCE");
            if(string.IsNullOrEmpty(output)||!Path.IsPathRooted(output)||string.IsNullOrEmpty(source)||!Path.IsPathRooted(source))throw new InvalidOperationException("Absolute evidence/source required");
            if(string.Equals(Path.GetFullPath(source).TrimEnd('\\','/'),Path.GetFullPath(Path.Combine(Application.dataPath,"..")).TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Source editor prohibited");
            if(Directory.Exists(output)&&Directory.EnumerateFileSystemEntries(output).Any())throw new InvalidOperationException("Nonempty evidence prohibited");
            Directory.CreateDirectory(output);scratch=Path.Combine(output,"scratch-saves");
            SessionState.SetString(Key+".Output",output);SessionState.SetString(Key+".Source",source);
            SessionState.SetString(Key+".OldSave",Environment.GetEnvironmentVariable("TWELVEJADE_SAVEDIR")??"");
            Environment.SetEnvironmentVariable("TWELVEJADE_SAVEDIR",scratch);SessionState.SetBool(Key,true);
            Application.runInBackground=true;Attach();Save("RUNNING");
            try{UnityEditor.Search.SearchService.EnumerateDatabases().ToArray();EditorSceneManager.OpenScene("Assets/Scenes/FrontEnd.unity",OpenSceneMode.Single);EditorApplication.isPlaying=true;}
            catch(Exception e){Check("bootstrap",false,e.ToString());Finish();}
        }
        static void Attach()
        {
            output=SessionState.GetString(Key+".Output","");source=SessionState.GetString(Key+".Source","");scratch=Path.Combine(output,"scratch-saves");
            if(File.Exists(Path.Combine(output,"runtime-results.json"))){var r=JsonUtility.FromJson<Report>(File.ReadAllText(Path.Combine(output,"runtime-results.json")));rows.Clear();rows.AddRange(r.checks);}
            started=EditorApplication.timeSinceStartup;EditorApplication.update-=Tick;EditorApplication.update+=Tick;
            Application.logMessageReceived-=OnLog;Application.logMessageReceived+=OnLog;
        }
        static void Tick()
        {
            if(finished)return;EditorApplication.QueuePlayerLoopUpdate();
            if(EditorApplication.timeSinceStartup-started>150){Check("150s run bound",false);Finish();return;}
            if(!EditorApplication.isPlaying||running)return;controller=Object.FindAnyObjectByType<FrontEndController>();if(controller==null)return;
            EditorWindow.GetWindow(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView")).Focus();
            running=true;var go=new GameObject("Independent occlusion validation");var pump=go.AddComponent<OcclusionValidationPump>();
            go.AddComponent<OcclusionValidationBefore>();go.AddComponent<OcclusionValidationAfter>();pump.StartCoroutine(Guard(Checks()));
        }
        static IEnumerator Guard(IEnumerator test)
        {
            var stack=new Stack<IEnumerator>();stack.Push(test);
            while(stack.Count>0&&!finished)
            {
                object next=null;bool moved=false;
                try{moved=stack.Peek().MoveNext();if(moved)next=stack.Peek().Current;}
                catch(Exception e){Check("runner exception",false,e.ToString());Finish();}
                if(finished)yield break;if(!moved){stack.Pop();continue;}if(next is IEnumerator nested)stack.Push(nested);else yield return next;
            }
            if(!finished)Finish();
        }
        static void Check(string name,bool pass,string detail=""){rows.Add(new Row{name=name,status=pass?"PASS":"FAIL",detail=detail});Save("RUNNING");}
        static void Pending(string name,string detail){rows.Add(new Row{name=name,status="PENDING",detail=detail});Save("RUNNING");}
        static void OnLog(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert){rows.Add(new Row{name="Unity managed error",status="FAIL",detail=message+"\n"+stack});Save("RUNNING");}}
        static void Write(string name,object value){File.WriteAllText(Path.Combine(output,name),JsonUtility.ToJson(value,true));}
        static void Lines<T>(string name,IEnumerable<T> values){using(var f=new StreamWriter(Path.Combine(output,name)))foreach(var value in values)f.WriteLine(JsonUtility.ToJson(value));}
        static object Field(object owner,string name)=>owner.GetType().GetField(name,Flags)?.GetValue(owner);
        static long Now()=>Stopwatch.GetTimestamp();
        static long Ns(long ticks)=>(long)(ticks*(1e9/Stopwatch.Frequency));
        static void Isolated()
        {
            var actual=Field(controller.Repository,"root") as string;
            if(actual==null||!string.Equals(Path.GetFullPath(actual),Path.GetFullPath(scratch),StringComparison.OrdinalIgnoreCase)||Environment.GetEnvironmentVariable("TWELVEJADE_SAVEDIR")!=scratch)throw new InvalidOperationException("Save isolation failed");
        }
        static IEnumerator Checks()
        {
            Isolated();if(Directory.Exists(scratch)&&Directory.EnumerateFileSystemEntries(scratch).Any())throw new InvalidOperationException("Scratch not empty");
            var preset=Resources.LoadAll<CharacterPreset>("Characters").OrderBy(p=>p.id).First();
            controller.Repository.Create(1,preset.id,"Occlusion validation");controller.LoadSlot(controller.Repository.Read(1));
            for(int i=0;i<180&&controller.TravelerSilhouetteForCheck==null;i++)yield return null;
            var places=Field(controller,"placeables") as List<FrontEndController.Placeable>;
            gate=places?.FirstOrDefault(p=>p.Id=="plot-town-gate-south")?.Rect.GetComponent<BuildingOcclusionFader>();silhouette=controller.TravelerSilhouetteForCheck;
            Check("real scratch town and single gate",controller.CurrentPage=="town"&&gate!=null&&silhouette!=null&&controller.OcclusionBuildingCountForCheck==1);
            if(gate==null||silhouette==null)yield break;
            settings=Field(controller,"settings");reduceField=settings?.GetType().GetField("reduceMotion",Flags);
            if(reduceField==null)throw new InvalidOperationException("Actual reduceMotion setting missing");
            originalReduce=(bool)reduceField.GetValue(settings);reduceField.SetValue(settings,false);
            var target=new Vector2(gate.FootBounds.center.x,gate.FootBounds.yMin+gate.FootBounds.height*.32f);
            yield return Walk("natural-entry",target,40);
            for(int i=0;i<45;i++){yield return EndFrame;Observe("settle-entry");yield return null;}
            Check("inside active default Frames gate",gate.IsOccluding&&silhouette.FramesSupported&&silhouette.VisibleAlpha>0&&Mathf.Abs(gate.CurrentAlpha-.425f)<.002f);
            if(!gate.IsOccluding||!silhouette.FramesSupported){Pending("remaining live validation","Natural route did not produce supported occlusion; no teleport/fake dt");yield break;}
            yield return CpuChecks();yield return PauseCheck(350,false,"pause-normal");yield return PauseCheck(650,true,"pause-reduced");yield return MaskChecks();
            var material=silhouette.Graphic.OwnedMaterialForCheck;var oldSilhouette=silhouette;
            if(fixture!=null)Object.Destroy(fixture);if(controlTexture!=null)Object.Destroy(controlTexture);
            yield return null;reduceField.SetValue(settings,originalReduce);Isolated();controller.ShowMenu();
            Check("page cleanup immediate building restore",gate==null||Mathf.Abs(gate.CurrentAlpha-1)<.001f);
            yield return null;yield return null;
            Check("page cleanup silhouette/material released",oldSilhouette==null&&material==null&&controller.TravelerSilhouetteForCheck==null&&controller.OcclusionBuildingCountForCheck==0);
            Pending("non-Frames","MeshDeform/Slices remain unsupported/unmeasured; no replacement pose");
            Pending("full Graphic native canvas GPU budget","Marker excludes subsequent Canvas rebuilding, native batching/render thread and GPU. Forced Graphic.Rebuild is diagnostic only; no aggregate <0.1ms claim.");
            Pending("all-frame real-world GPU visibility","Finite PNG pixel tests do not prove all natural occlusion frames; fixture mask scope is separate from unmodified Town hierarchy.");
        }
        static IEnumerator Walk(string label,Vector2 target,float timeout)
        {
            bool requested=controller.RequestTravelTo(target);Check(label+" route requested",requested,"goal="+target);if(!requested)yield break;
            float until=Time.realtimeSinceStartup+timeout;Vector2 last=controller.PlayerWorldPosition;bool legal=true;
            while(Time.realtimeSinceStartup<until)
            {
                yield return EndFrame;var f=Observe(label);f.routeTraversable=controller.World.CanTraverse(last.x,last.y,f.player.x,f.player.y);f.routeStandable=controller.World.CanStand(f.player.x,f.player.y);legal&=f.routeTraversable&&f.routeStandable;last=f.player;
                if(Vector2.Distance(f.player,target)<9&&!((bool?)Field(controller,"hasTarget")??true))break;yield return null;
            }
            Check(label+" actual arrival",Vector2.Distance(controller.PlayerWorldPosition,target)<12,"actual="+controller.PlayerWorldPosition+" target="+target);Check(label+" actual edges legal",legal);
        }
        static Frame Observe(string label)
        {
            var f=new Frame{stage=label,frame=Time.frameCount,elapsed=Time.realtimeSinceStartupAsDouble,wallSeconds=(double)Now()/Stopwatch.Frequency,dt=Time.unscaledDeltaTime,player=controller.PlayerWorldPosition,
                inside=gate!=null&&gate.ContainsFoot(controller.PlayerWorldPosition),occluded=gate!=null&&gate.IsOccluding,buildingAlpha=gate==null?1:gate.CurrentAlpha,
                outlineAlpha=silhouette==null?0:silhouette.VisibleAlpha,supported=silhouette!=null&&silhouette.FramesSupported,reduceMotion=silhouette!=null&&silhouette.ReduceMotion,pulse=silhouette==null?0:silhouette.PulsePhase,
                frameIndex=controller.TravelerPuppetForShot==null?-1:controller.TravelerPuppetForShot.FrameIndexForCheck,frameAlpha=silhouette?.CurrentFrame==null?0:silhouette.CurrentFrame.color.a,previousAlpha=silhouette?.PreviousFrame==null?0:silhouette.PreviousFrame.color.a,
                routeStandable=controller.World.CanStand(controller.PlayerWorldPosition.x,controller.PlayerWorldPosition.y),routeTraversable=true};frames.Add(f);return f;
        }
        public static void BeforeLate(){if(!measuring)return;beforeFrame=Time.frameCount;beforeGc=allocated==null?-1:allocated();beforeTicks=Now();}
        public static void AfterLate()
        {
            if(!measuring||beforeFrame!=Time.frameCount)return;long ticks=Now()-beforeTicks;long gc=allocated==null?-1:allocated()-beforeGc;
            cpu.Add(new CpuFrame{frame=Time.frameCount,envelopeNs=Ns(ticks),gcBytes=gc,frameDt=Time.unscaledDeltaTime,occluded=gate!=null&&gate.IsOccluding,markerValid=markerCreated&&marker.Valid,markerLastValue=markerCreated&&marker.Valid?marker.LastValue:-1,markerSampleCount=markerCreated&&marker.Valid?marker.Count:0});
        }
        static Distribution Stats(string scope,IEnumerable<long> data,IEnumerable<long> gc=null)
        {
            var a=data.OrderBy(v=>v).ToArray();var g=gc==null?Array.Empty<long>():gc.Where(v=>v>=0).ToArray();
            return new Distribution{scope=scope,samples=a.Length,min=a.Length==0?0:a[0],p50=Percentile(a,.5),p95=Percentile(a,.95),p99=Percentile(a,.99),max=a.Length==0?0:a[a.Length-1],mean=a.Length==0?0:a.Average(v=>(double)v),over100us=a.Count(v=>v>=100000),gcSum=g.Sum(),gcMax=g.Length==0?-1:g.Max()};
        }
        static long Percentile(long[] a,double p)=>a.Length==0?0:a[Math.Min(a.Length-1,(int)Math.Ceiling(p*a.Length)-1)];
        static IEnumerator CpuChecks()
        {
            var allocationMethod=typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread",BindingFlags.Public|BindingFlags.Static);
            if(allocationMethod!=null)allocated=(Func<long>)Delegate.CreateDelegate(typeof(Func<long>),allocationMethod);
            else Pending("thread GC measure","Runtime API not available; total-frame GC cannot identify this component.");
            for(int i=0;i<120;i++){yield return EndFrame;Observe("cpu-warmup");yield return null;}
            marker=ProfilerRecorder.StartNew(ProfilerCategory.Scripts,"TwelveJade.OcclusionExperience",1024,ProfilerRecorderOptions.StartImmediately|ProfilerRecorderOptions.CollectOnlyOnCurrentThread|ProfilerRecorderOptions.SumAllSamplesInFrame);
            markerCreated=true;measuring=true;
            var start=controller.PlayerWorldPosition;var goal=start+new Vector2(0,160);bool route=controller.World.CanTraverse(start.x,start.y,goal.x,goal.y)&&controller.RequestTravelTo(goal);Vector2 previous=start;
            for(int i=0;i<360;i++)
            {
                yield return EndFrame;var f=Observe("cpu-natural-measured");f.routeTraversable=controller.World.CanTraverse(previous.x,previous.y,f.player.x,f.player.y);previous=f.player;yield return null;
            }
            measuring=false;marker.Stop();var markerRows=new List<MarkerRow>();if(marker.Valid)for(int i=0;i<marker.Count;i++){var sample=marker.GetSample(i);markerRows.Add(new MarkerRow{index=i,nanoseconds=sample.Value,calls=sample.Count});}
            Lines("cpu-marker-raw.jsonl",markerRows);Lines("cpu-envelope-raw.jsonl",cpu);var natural=frames.Where(f=>f.stage=="cpu-natural-measured").ToArray();
            Check("CPU measured while occluded on legal real path",route&&natural.Length==360&&natural.All(f=>f.occluded&&f.inside&&f.routeStandable&&f.routeTraversable),"natural frames="+natural.Length);
            Write("cpu-envelope-summary.json",Stats("9999..10001 LateUpdate wall/GC envelope, includes dispatch and incidental main-thread work, not exact marker",cpu.Select(f=>f.envelopeNs),cpu.Select(f=>f.gcBytes)));
            bool cpuUnit=marker.Valid&&marker.UnitType.ToString().IndexOf("Nanoseconds",StringComparison.OrdinalIgnoreCase)>=0;var positive=markerRows.Where(r=>r.calls>0).ToArray();
            var distribution=Stats("existing TwelveJade.OcclusionExperience CPU marker including SyncFrames; excludes later UGUI/native canvas/GPU",positive.Select(v=>v.nanoseconds));Write("cpu-marker-summary.json",distribution);
            if(!cpuUnit||positive.Length<300)Pending("CPU marker budget","valid="+marker.Valid+" unit="+(marker.Valid?marker.UnitType.ToString():"missing")+" positive samples="+positive.Length);
            else Check("existing occlusion CPU marker every measured frame under 0.1ms",distribution.max<100000,"samples="+distribution.samples+" maxNs="+distribution.max+" over="+distribution.over100us);
            marker.Dispose();markerCreated=false;
            for(int i=0;i<120;i++){yield return EndFrame;silhouette.Graphic.SetAllDirty();silhouette.Graphic.Rebuild(CanvasUpdate.PreRender);yield return null;}
            for(int i=0;i<360;i++)
            {
                yield return EndFrame;var g=silhouette.Graphic;g.SetAllDirty();long base0=Now(),base1=Now();long gc0=allocated==null?-1:allocated();long t=Now();g.Rebuild(CanvasUpdate.PreRender);long duration=Ns(Now()-t);long bytes=allocated==null?-1:allocated()-gc0;
                rebuild.Add(new RebuildFrame{frame=Time.frameCount,nanoseconds=duration,gcBytes=bytes,timerBaselineNs=Ns(base1-base0),ready=g.Ready,culled=g.canvasRenderer.cull,rectClip=g.canvasRenderer.hasRectClipping});yield return null;
            }
            Lines("cpu-forced-rebuild-raw.jsonl",rebuild);var forced=Stats("controlled forced-dirty Graphic.Rebuild(PreRender), one call/frame; not automatic Canvas total",rebuild.Select(v=>v.nanoseconds),rebuild.Select(v=>v.gcBytes));Write("cpu-forced-rebuild-summary.json",forced);
            Check("forced Graphic.Rebuild diagnostic under 0.1ms",rebuild.All(v=>v.ready&&!v.culled)&&forced.max<100000,"samples="+forced.samples+" maxNs="+forced.max+" over="+forced.over100us+"; diagnostic only");
        }
        static IEnumerator PauseCheck(int milliseconds,bool reduced,string label)
        {
            stage=label;reduceField.SetValue(settings,reduced);for(int i=0;i<3;i++){yield return EndFrame;yield return null;}
            yield return EndFrame;var before=Observe(label+"-before");long t=Now();Thread.Sleep(milliseconds);double blocked=(double)(Now()-t)/Stopwatch.Frequency;
            var after=new List<Frame>();for(int i=0;i<4;i++){yield return null;yield return EndFrame;after.Add(Observe(label+"-after"));if(i==0)Object.Destroy(Capture(label+".png"));}
            Write(label+".json",new Pause{requestedMs=milliseconds,actualBlockedSeconds=blocked,before=before,after=after,reduced=reduced});
            bool longFrame=after.Any(f=>f.dt>.1f&&f.inside&&f.occluded);
            Check(label+" actual blocking and occluded long frame",before.occluded&&before.inside&&blocked>=milliseconds*.0009&&longFrame,"requestedMs="+milliseconds+" wallSeconds="+blocked+" maxActualDt="+after.Max(f=>f.dt));
            Check(label+" visible state survives actual long frame",after.All(f=>f.occluded&&f.inside&&f.supported&&f.outlineAlpha>0&&Mathf.Abs(f.buildingAlpha-.425f)<.002f&&f.reduceMotion==reduced));
            if(reduced)Check(label+" pulse stays fixed",after.All(f=>Mathf.Abs(f.pulse-before.pulse)<.00001f));
        }
        static Texture2D Capture(string name)
        {
            var image=ScreenCapture.CaptureScreenshotAsTexture();if(image==null)throw new InvalidOperationException("Screenshot unavailable");
            File.WriteAllBytes(Path.Combine(output,name),image.EncodeToPNG());shots++;
            File.AppendAllText(Path.Combine(output,"screenshots.jsonl"),JsonUtility.ToJson(new Shot{file=name,stage=stage,frame=Time.frameCount,width=image.width,height=image.height,player=controller.PlayerWorldPosition})+"\n");return image;
        }
        static RectTransform RectObject(string name,Transform parent,Vector2 size,Vector2 position)
        {
            var go=new GameObject(name,typeof(RectTransform));var r=(RectTransform)go.transform;r.SetParent(parent,false);r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f);r.sizeDelta=size;r.anchoredPosition=position;return r;
        }
        static RawImage Source(string name,Transform parent,RawImage sourceFrame,Texture texture=null)
        {
            var r=RectObject(name,parent,new Vector2(1,1),Vector2.zero);var raw=r.gameObject.AddComponent<RawImage>();raw.texture=texture??sourceFrame.texture;raw.uvRect=texture==null?sourceFrame.uvRect:new Rect(0,0,1,1);raw.color=texture==null?sourceFrame.color:Color.white;raw.raycastTarget=false;raw.enabled=false;return raw;
        }
        static Mask AddMask(RectTransform rect)
        {
            var image=rect.gameObject.AddComponent<Image>();image.color=Color.white;image.raycastTarget=false;var mask=rect.gameObject.AddComponent<Mask>();mask.showMaskGraphic=false;return mask;
        }
        static void BuildFixture()
        {
            Write("mask-frame-binding.json",new Binding{currentTexture=AssetDatabase.GetAssetPath(silhouette.CurrentFrame.texture),previousTexture=AssetDatabase.GetAssetPath(silhouette.PreviousFrame.texture),shader=AssetDatabase.GetAssetPath(silhouette.Graphic.OwnedMaterialForCheck.shader),currentUv=silhouette.CurrentFrame.uvRect,previousUv=silhouette.PreviousFrame.uvRect,currentAlpha=silhouette.CurrentFrame.color.a*silhouette.CurrentFrame.canvasRenderer.GetAlpha(),previousAlpha=silhouette.PreviousFrame.color.a*silhouette.PreviousFrame.canvasRenderer.GetAlpha(),currentInstance=silhouette.CurrentFrame.texture.GetInstanceID(),previousInstance=silhouette.PreviousFrame.texture.GetInstanceID(),currentLive=silhouette.CurrentFrame.isActiveAndEnabled,previousLive=silhouette.PreviousFrame.isActiveAndEnabled});
            fixture=new GameObject("Active mask pixel fixture",typeof(RectTransform),typeof(Canvas));var canvas=fixture.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=32760;
            var backdrop=RectObject("controlled background",fixture.transform,new Vector2(Screen.width,Screen.height),Vector2.zero).gameObject.AddComponent<Image>();backdrop.color=new Color(.035f,.05f,.07f,1);backdrop.raycastTarget=false;
            frameSource=Source("snapshot actual current Frames binding",fixture.transform,silhouette.CurrentFrame);fadeSource=Source("snapshot actual previous Frames binding",fixture.transform,silhouette.PreviousFrame);
            frameSource.color=new Color(1,1,1,silhouette.CurrentFrame.color.a*silhouette.CurrentFrame.canvasRenderer.GetAlpha());fadeSource.color=new Color(1,1,1,silhouette.PreviousFrame.color.a*silhouette.PreviousFrame.canvasRenderer.GetAlpha());
            controlTexture=new Texture2D(128,128,TextureFormat.RGBA32,false);controlTexture.name="deterministic alpha ring positive control";var colors=new Color32[128*128];for(int y=0;y<128;y++)for(int x=0;x<128;x++){float d=Vector2.Distance(new Vector2(x+.5f,y+.5f),new Vector2(64,64));colors[y*128+x]=new Color32(255,255,255,(byte)(d>29&&d<47?255:0));}controlTexture.SetPixels32(colors);controlTexture.Apply();
            controlSource=Source("control current",fixture.transform,null,controlTexture);controlFade=Source("control previous",fixture.transform,null,controlTexture);controlFade.color=new Color(1,1,1,0);
            float width=Mathf.Min(330,Screen.width/3f*.72f),height=Mathf.Min(330,Screen.height/2f*.76f);
            for(int row=0;row<2;row++)for(int col=0;col<3;col++)
            {
                string mode=col==0?"Mask":col==1?"RectMask2D":"NestedMaskAndRect";var root=RectObject((row==0?"Frames ":"Control ")+mode,fixture.transform,new Vector2(width,height),new Vector2((col-1)*Screen.width/3f,(row==0?.25f:-.25f)*Screen.height));
                var masks=new List<Mask>();RectMask2D rectMask=null;Transform parent=root;
                var clip=RectObject("clip left 60 percent",parent,new Vector2(width*.6f,height),new Vector2(-width*.2f,0));
                if(col==0)masks.Add(AddMask(clip));if(col==1)rectMask=clip.gameObject.AddComponent<RectMask2D>();parent=clip;
                if(col==2)
                {
                    masks.Add(AddMask(clip));var inner=RectObject("nested stencil depth2",clip,new Vector2(width*.6f,height),Vector2.zero);masks.Add(AddMask(inner));
                    var rect=RectObject("nested rect clipping",inner,new Vector2(width*.6f,height),Vector2.zero);rectMask=rect.gameObject.AddComponent<RectMask2D>();parent=rect;
                }
                var graphicRect=RectObject("real shipped Frames outline",parent,new Vector2(width,height),new Vector2(width*.2f,0));
                graphicRect.gameObject.AddComponent<CanvasRenderer>();var g=graphicRect.gameObject.AddComponent<TravelerSilhouetteGraphic>();
                var init=(Action)Delegate.CreateDelegate(typeof(Action),g,typeof(TravelerSilhouetteGraphic).GetMethod("Initialize",Flags));init();
                var sync=(Action<RawImage,RawImage,float>)Delegate.CreateDelegate(typeof(Action<RawImage,RawImage,float>),g,typeof(TravelerSilhouetteGraphic).GetMethod("SyncFrames",Flags));
                panels.Add(new Panel{name=(row==0?"frames-":"control-")+mode,mode=mode,source=row==0?"actual Frames texture/UV/alpha snapshot":"deterministic alpha ring",graphicRect=graphicRect,clipRect=col==2?parent as RectTransform:clip,graphic=g,masks=masks.ToArray(),rectMask=rectMask,sync=sync,current=row==0?frameSource:controlSource,previous=row==0?fadeSource:controlFade});
            }
        }
        static Rect ScreenRect(RectTransform rect)
        {
            var corners=new Vector3[4];rect.GetWorldCorners(corners);var min=RectTransformUtility.WorldToScreenPoint(null,corners[0]);var max=RectTransformUtility.WorldToScreenPoint(null,corners[2]);return Rect.MinMaxRect(min.x,min.y,max.x,max.y);
        }
        static void Masks(bool active,float strength)
        {
            foreach(var p in panels)
            {
                foreach(var mask in p.masks){mask.enabled=active;mask.GetComponent<Image>().enabled=active;}
                if(p.rectMask!=null)p.rectMask.enabled=active;p.sync(p.current,p.previous,strength);p.graphic.SetAllDirty();
            }
            Canvas.ForceUpdateCanvases();
        }
        static bool Interior(Rect r,int x,int y,float margin)=>x+.5f>=r.xMin+margin&&x+.5f<r.xMax-margin&&y+.5f>=r.yMin+margin&&y+.5f<r.yMax-margin;
        static bool Exterior(Rect r,int x,int y,float margin)=>x+.5f<r.xMin-margin||x+.5f>=r.xMax+margin||y+.5f<r.yMin-margin||y+.5f>=r.yMax+margin;
        static int Difference(Color32 a,Color32 b)=>Math.Max(Math.Abs(a.r-b.r),Math.Max(Math.Abs(a.g-b.g),Math.Abs(a.b-b.b)));
        static IEnumerator MaskChecks()
        {
            stage="active-mask-fixture";BuildFixture();Masks(false,0);yield return null;yield return EndFrame;var baseline=Capture("mask-01-background.png");
            Masks(false,.65f);yield return null;yield return EndFrame;var unmasked=Capture("mask-02-unmasked.png");
            Masks(true,.65f);yield return null;yield return EndFrame;var masked=Capture("mask-03-active.png");
            var b=baseline.GetPixels32();var u=unmasked.GetPixels32();var m=masked.GetPixels32();var results=new List<PixelRow>();var samples=new List<PixelSample>();
            foreach(var p in panels)
            {
                var bounds=ScreenRect(p.graphicRect);var clip=ScreenRect(p.clipRect);var material=p.graphic.canvasRenderer.GetMaterial(0);
                var row=new PixelRow{name=p.name,source=p.source,mode=p.mode,graphic=bounds,clip=clip,maskActive=p.masks.Length>0&&p.masks.All(a=>a.isActiveAndEnabled),rectActive=p.rectMask!=null&&p.rectMask.isActiveAndEnabled,actualRectClip=p.graphic.canvasRenderer.hasRectClipping,expectedStencil=p.masks.Length==0?0:(1<<p.masks.Length)-1,actualStencil=material==null?-1:(int)material.GetFloat("_Stencil"),stencilCompare=material==null?-1:(int)material.GetFloat("_StencilComp"),material=material==null?"missing":material.name,shader=material==null?"missing":material.shader.name};
                for(int y=Mathf.Max(0,(int)bounds.yMin);y<Mathf.Min(masked.height,(int)bounds.yMax);y++)for(int x=Mathf.Max(0,(int)bounds.xMin);x<Mathf.Min(masked.width,(int)bounds.xMax);x++)
                {
                    int i=y*masked.width+x;int signal=Difference(u[i],b[i]);if(signal<12)continue;bool inside=Interior(clip,x,y,3),outside=Exterior(clip,x,y,3);if(!inside&&!outside)continue;
                    if(inside){row.insidePositive++;if(Difference(m[i],u[i])<=4)row.insideRetained++;}else{row.outsidePositive++;if(Difference(m[i],b[i])>4)row.outsideLeaked++;}
                    samples.Add(new PixelSample{panel=p.name,region=inside?"inside":"outside",x=x,y=y,baselineR=b[i].r,baselineG=b[i].g,baselineB=b[i].b,unmaskedR=u[i].r,unmaskedG=u[i].g,unmaskedB=u[i].b,maskedR=m[i].r,maskedG=m[i].g,maskedB=m[i].b});
                }
                row.retainedFraction=row.insidePositive==0?0:(float)row.insideRetained/row.insidePositive;row.leakedFraction=row.outsidePositive==0?1:(float)row.outsideLeaked/row.outsidePositive;
                bool active=p.masks.Length==0||(row.maskActive&&row.actualStencil==row.expectedStencil&&row.stencilCompare==3);active&=p.rectMask==null||(row.rectActive&&row.actualRectClip);
                row.passed=active&&row.insidePositive>=12&&row.outsidePositive>=12&&row.retainedFraction>=.9f&&row.leakedFraction<=.01f;results.Add(row);
                Check(p.name+" active mask visible interior and clipped exterior",row.passed,"inside="+row.insidePositive+" retained="+row.retainedFraction+" outside="+row.outsidePositive+" leaked="+row.leakedFraction+" stencil="+row.actualStencil+" rect="+row.actualRectClip);
            }
            Lines("mask-pixel-summary.jsonl",results);Lines("mask-pixels-raw.jsonl",samples);Object.Destroy(baseline);Object.Destroy(unmasked);Object.Destroy(masked);
            var owned=panels.Select(p=>p.graphic.OwnedMaterialForCheck).ToArray();Object.Destroy(fixture);fixture=null;yield return null;yield return null;Check("fixture mask-owned materials cleanup",owned.All(x=>x==null));
        }
        static void Save(string status){if(!string.IsNullOrEmpty(output))Write("runtime-results.json",new Report{status=status,screenshots=shots,checks=rows});}
        static void Finish()
        {
            if(finished)return;finished=true;measuring=false;EditorApplication.update-=Tick;Application.logMessageReceived-=OnLog;
            if(markerCreated){marker.Dispose();markerCreated=false;}if(settings!=null&&reduceField!=null)reduceField.SetValue(settings,originalReduce);
            Lines("frame-trace.jsonl",frames);if(cpu.Count>0&&!File.Exists(Path.Combine(output,"cpu-envelope-raw.jsonl")))Lines("cpu-envelope-raw.jsonl",cpu);
            if(fixture!=null)Object.Destroy(fixture);if(controlTexture!=null)Object.Destroy(controlTexture);Save(rows.Any(r=>r.status=="FAIL")?"FAIL":"PENDING");SessionState.SetBool(Key,false);
            var old=SessionState.GetString(Key+".OldSave","");Environment.SetEnvironmentVariable("TWELVEJADE_SAVEDIR",old.Length==0?null:old);EditorApplication.Exit(1);
        }
    }
    public sealed class OcclusionValidationPump:MonoBehaviour{}
    [DefaultExecutionOrder(9999)] public sealed class OcclusionValidationBefore:MonoBehaviour{void LateUpdate(){OcclusionValidationShot.BeforeLate();}}
    [DefaultExecutionOrder(10001)] public sealed class OcclusionValidationAfter:MonoBehaviour{void LateUpdate(){OcclusionValidationShot.AfterLate();}}
}

