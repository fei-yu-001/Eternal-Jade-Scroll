using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using TMPro;
using TwelveJade.Core;
using TwelveJade.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace TwelveJade.Editor
{
    // One isolated real-Town run. Goal completion is explicitly injected; no combat-flow claim.
    [InitializeOnLoad]
    public static class DialogueQuestIntegrationShot
    {
        const string KeyName="DialogueQuestIntegrationShot", Npc="npc-yao-lao", Quest="quest-herbal-rescue";
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        static string output, scratch, source; static bool running, finished; static double started;
        static FrontEndController c; static DialogueUI ui; static DialogueTable table; static DialogueQuestIntegrationPump pump;
        static Keyboard keyboard, previousKeyboard; static UserSettings settings; static bool oldReduced; static int shots, errors, diagnostics;
        static readonly List<Row> rows=new List<Row>(); static readonly List<Stamp> stamps=new List<Stamp>();
        [Serializable] sealed class Row { public string name,status,detail; }
        [Serializable] sealed class Stamp { public string path,sha256; }
        [Serializable] sealed class Report { public string status,overall="NOT_APPROVED",scope="real Town; INJECTED quest goals, combat NOT TESTED",portrait="PENDING"; public int screenshots,runtimeErrors,passed,failed,pending; public List<Row> checks; public List<Stamp> inputs; }
        [Serializable] sealed class Shot { public string file,method,page,node; public int frame,width,height; }
        [Serializable] sealed class Layout { public string name,text,rendered; public Rect bounds; public bool valid; }
        sealed class HostPending:Exception { public HostPending(string message):base(message) {} }
        static DialogueQuestIntegrationShot() { if(SessionState.GetBool(KeyName,false))Attach(); }
        public static void Run()
        {
            if(Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Use isolated non-batch Edit mode.");
            output=Environment.GetEnvironmentVariable("DIALOGUE_QUEST_EVIDENCE"); source=Environment.GetEnvironmentVariable("DIALOGUE_QUEST_SOURCE_PROJECT");
            if(string.IsNullOrEmpty(output)||!Path.IsPathRooted(output)||string.IsNullOrEmpty(source)||!Path.IsPathRooted(source))throw new InvalidOperationException("Set absolute DIALOGUE_QUEST_EVIDENCE / DIALOGUE_QUEST_SOURCE_PROJECT.");
            if(Same(source,Path.Combine(Application.dataPath,"..")))throw new InvalidOperationException("Refusing the source editor.");
            if(Directory.Exists(output)&&Directory.EnumerateFileSystemEntries(output).Any())throw new InvalidOperationException("Evidence must be new/empty; failures are never overwritten.");
            Directory.CreateDirectory(output); scratch=Path.Combine(output,"scratch-saves"); rows.Clear(); stamps.Clear(); shots=errors=diagnostics=0; running=finished=false;
            SessionState.SetString(KeyName+".Output",output); SessionState.SetString(KeyName+".Source",source);
            SessionState.SetString(KeyName+".OldSave",Environment.GetEnvironmentVariable("TWELVEJADE_SAVEDIR")??"");
            Environment.SetEnvironmentVariable("TWELVEJADE_SAVEDIR",scratch); SessionState.SetBool(KeyName,true); Application.runInBackground=true; Attach();
            try { Inputs(); Save("RUNNING"); EditorSceneManager.OpenScene("Assets/Scenes/FrontEnd.unity",OpenSceneMode.Single); EditorApplication.isPlaying=true; }
            catch(Exception e) { Record("bootstrap","FAIL",e.ToString()); Finish(); }
        }
        static bool Same(string a,string b)=>string.Equals(Path.GetFullPath(a).TrimEnd('/','\\'),Path.GetFullPath(b).TrimEnd('/','\\'),StringComparison.OrdinalIgnoreCase);
        static void Attach()
        {
            output=SessionState.GetString(KeyName+".Output",""); source=SessionState.GetString(KeyName+".Source",""); scratch=Path.Combine(output,"scratch-saves");
            var saved=Path.Combine(output,"runtime-results.json");
            if(File.Exists(saved)){var r=JsonUtility.FromJson<Report>(File.ReadAllText(saved));rows.Clear();rows.AddRange(r.checks);stamps.Clear();stamps.AddRange(r.inputs);shots=r.screenshots;errors=r.runtimeErrors;}
            started=EditorApplication.timeSinceStartup; EditorApplication.update-=Tick;EditorApplication.update+=Tick;
            Application.logMessageReceived-=OnLog;Application.logMessageReceived+=OnLog;
        }
        static void Tick()
        {
            if(finished)return; EditorApplication.QueuePlayerLoopUpdate();
            if(EditorApplication.timeSinceStartup-started>150){Record("timeout","FAIL","150s bound");Finish();return;}
            if(!EditorApplication.isPlaying||running)return; c=UnityEngine.Object.FindAnyObjectByType<FrontEndController>(); if(c==null)return;
            EditorWindow.GetWindow(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView")).Focus(); running=true;
            pump=new GameObject("Dialogue quest integration runner").AddComponent<DialogueQuestIntegrationPump>();pump.StartCoroutine(Guard(Suite()));
        }
        static IEnumerator Guard(IEnumerator run)
        {
            var stack=new Stack<IEnumerator>();stack.Push(run);bool aborted=false;
            while(stack.Count>0&&!finished&&!aborted)
            {
                bool moved=false;object next=null;
                try{moved=stack.Peek().MoveNext();if(moved)next=stack.Peek().Current;}
                catch(HostPending e){Record("host integration","PENDING",e.Message);aborted=true;}
                catch(Exception e){Record("run exception","FAIL",e.ToString());aborted=true;}
                if(aborted)break;if(!moved){stack.Pop();continue;}if(next is IEnumerator nested)stack.Push(nested);else yield return next;
            }
            if(!finished){if(aborted&&shots<6)yield return Capture("failure-context");Finish();}
        }
        static void Record(string name,string status,string detail=""){rows.Add(new Row{name=name,status=status,detail=detail});Save("RUNNING");}
        static void Check(string name,bool pass,string detail=""){Record(name,pass?"PASS":"FAIL",detail);}
        static void Need(string name,bool pass,string detail=""){Check(name,pass,detail);if(!pass)throw new InvalidOperationException(name+": "+detail);}
        static void OnLog(string message,string stack,LogType type)
        {if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert){errors++;Record("Unity runtime error","FAIL",message+"\n"+stack);}}
        static void Isolated()
        {
            var actual=typeof(SaveRepository).GetField("root",Private)?.GetValue(c.Repository) as string;
            if(actual==null||!Same(actual,scratch)||!Same(Environment.GetEnvironmentVariable("TWELVEJADE_SAVEDIR")??".",scratch))throw new InvalidOperationException("Repository is not scratch.");
        }
        static string State()=>JsonUtility.ToJson(c.ActiveSave);
        static string QuestStatus()=>QuestLedger.DisplayStatus(c.ActiveSave,c.Quests.Find(Quest));
        static int Medicine()=>InventoryRules.Count(c.ActiveSave.bag,"jinchuangyao");
        static IEnumerator Wait(Func<bool> done,float seconds,string name,bool pending=false)
        {
            float end=Time.realtimeSinceStartup+seconds;while(!done()&&Time.realtimeSinceStartup<end)yield return null;
            if(!done()){if(pending)throw new HostPending(name);Need(name,false);}else Check(name,true);
        }
        static IEnumerator KeyPress(UnityEngine.InputSystem.Key key,float seconds=.05f)
        {InputSystem.QueueStateEvent(keyboard,new KeyboardState(key));yield return new WaitForSecondsRealtime(seconds);InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;}
        static IEnumerator OpenNpc(bool firstKey)
        {
            Need("fixture snap near actual Yao Lao",c.SnapTravelerNextToNpc(Npc));yield return new WaitForSecondsRealtime(.25f);
            if(firstKey)yield return KeyPress(UnityEngine.InputSystem.Key.E,.12f);else Click(c.NpcButtonForShot(Npc));
            yield return Wait(()=> (ui=UnityEngine.Object.FindAnyObjectByType<DialogueUI>())!=null&&ui.IsOpen,4,"Town NPC must open shared DialogueUI");
            Need("real opening node",ui.CurrentNodeId=="yaolao-intro"&&!ui.IsShowingPlayerLine,ui.CurrentNodeId);
            Need("host input lease acquired",c.DialogueInputLockedForShot);
            yield return new WaitForSecondsRealtime(.25f);
        }
        static IEnumerator Settle()
        {
            float end=Time.realtimeSinceStartup+8;
            while(ui!=null&&ui.IsOpen&&(ui.IsTyping||ui.IsShowingPlayerLine||ui.CurrentPage<ui.PageCount)&&Time.realtimeSinceStartup<end)
            {Click(ButtonNamed("Dialogue continue"));yield return new WaitForSecondsRealtime(.24f);}
            Need("settled NPC line",ui!=null&&ui.IsOpen&&!ui.IsTyping&&!ui.IsShowingPlayerLine&&ui.CurrentPage==ui.PageCount);TextVisible(ui.DialogueText);
        }
        static Button ButtonNamed(string name)=>ui.GetComponentsInChildren<Button>().Single(b=>b.name==name);
        static IEnumerator Choose(string text,string next)
        {
            yield return Settle(); var before=State();var view=DialogueRules.Open(c.ActiveSave,table,Npc,ui.CurrentNodeId);
            Need("mapping query is read-only",before==State());var buttons=ui.GetComponentsInChildren<Button>().Where(b=>b.name.StartsWith("Dialogue choice ",StringComparison.Ordinal)).ToArray();
            foreach(var b in buttons){int i=int.Parse(b.name.Substring("Dialogue choice ".Length));Need("original option index "+i,i>=0&&i<view.Options.Count&&b.GetComponentInChildren<TMP_Text>().text==view.Options[i].Text);}
            var matches=buttons.Where(b=>b.GetComponentInChildren<TMP_Text>().text.Contains(text)).ToArray();
            if(matches.Length!=1)DiagnoseChoices(text,view,buttons);
            Need("unique matching real branch "+text,matches.Length==1,"matches="+matches.Length);
            var selected=buttons.Single(b=>b.GetComponentInChildren<TMP_Text>().text.Contains(text));int index=int.Parse(selected.name.Substring("Dialogue choice ".Length));
            Need("expected real branch target",view.Options[index].NextNode==next);ScrollTo(selected);yield return null;TextVisible(selected.GetComponentInChildren<TMP_Text>());
            string frameToken=ui.CurrentToken;
            Click(selected);Check("same-frame public UI repeat rejected",!ui.SelectOption(index));
            yield return Wait(()=>ui!=null&&ui.CurrentNodeId==next&&!ui.IsAwaitingResult,3,"branch reaches "+next);
            Check("host request acknowledged with new frame",ui.CurrentToken!=frameToken&&string.IsNullOrEmpty(ui.PendingRequestToken));
            yield return new WaitForSecondsRealtime(.24f);
        }
        static IEnumerator CloseUi(bool completed=false)
        {
            if(completed){yield return Settle();var node=table.Find(ui.CurrentNodeId);Need("complete a real terminal node",node.Options.Count==0&&string.IsNullOrEmpty(node.Next));Click(ButtonNamed("Dialogue continue"));}
            else yield return KeyPress(UnityEngine.InputSystem.Key.Escape);
            yield return Wait(()=>ui==null||!ui.IsOpen,2,completed?"terminal closes dialogue":"Esc closes dialogue");
            yield return Wait(()=>!c.DialogueInputLockedForShot,2,"host input lease and closing input released");
        }
        static IEnumerator Suite()
        {
            Isolated();Need("empty scratch",!Directory.Exists(scratch)||!Directory.EnumerateFiles(scratch,"slot-*").Any());
            table=DialogueTable.Parse(Resources.Load<TextAsset>("Config/dialogues").text);
            c.Repository.Create(1,"farmer","对白核验");c.LoadSlot(c.Repository.Read(1));yield return Wait(()=>c.CurrentPage=="town"&&c.World!=null,12,"scratch directly Town");
            settings=(UserSettings)typeof(FrontEndController).GetField("settings",Private).GetValue(c);oldReduced=settings.reduceMotion;settings.reduceMotion=false;
            previousKeyboard=Keyboard.current;keyboard=InputSystem.AddDevice<Keyboard>("DialogueQuestShotKeyboard");keyboard.MakeCurrent();
            Need("daytime fixture",c.SetWorldMinuteForCheck(600));Need("near real NPC",c.SnapTravelerNextToNpc(Npc));yield return new WaitForSecondsRealtime(.4f);
            Head("Available","!",true);yield return Capture("01-available");int medicine=Medicine(),coins=c.ActiveSave.coins;
            yield return OpenNpc(true);Need("opening E did not consume first line",ui.CurrentNodeId=="yaolao-intro"&&ui.IsTyping&&ui.DialogueText.maxVisibleCharacters>0);
            string firstToken=ui.CurrentToken;yield return KeyPress(UnityEngine.InputSystem.Key.Space);yield return null;
            Need("second key reveals not advances",ui.CurrentToken==firstToken&&!ui.IsTyping);TextVisible(ui.DialogueText);yield return Capture("02-intro-full");
            var standing=c.PlayerWorldPosition;var move=MoveKey(standing);yield return KeyPress(move,.25f);Check("dialogue lease blocks real movement",Vector2.Distance(standing,c.PlayerWorldPosition)<.1f);
            string branchState=State();ui.HandleInput(DialogueUICommand.ToggleAuto);yield return new WaitForSecondsRealtime(1.4f);
            Check("Auto stops at branch",!ui.AutoEnabled&&ui.CurrentToken==firstToken&&State()==branchState);ui.HandleInput(DialogueUICommand.Skip);yield return new WaitForSecondsRealtime(.25f);
            Check("Skip stops at branch",!ui.SkipActive&&ui.CurrentToken==firstToken&&State()==branchState);
            Record("automatic linear path to branch","PENDING","This run tests the actual Yao Lao branch gate, not an invented linear dialogue/Auto path.");
            yield return CloseUi();yield return KeyPress(move,.25f);Check("close restores real movement",Vector2.Distance(standing,c.PlayerWorldPosition)>2);
            yield return OpenNpc(false);yield return Choose("后山是不是出事了","yaolao-rescue");Check("asking does not accept",QuestStatus()==QuestLedger.StatusAvailable);
            yield return Choose("这我管不了","yaolao-bye");Check("refusal does not accept/reward",QuestStatus()==QuestLedger.StatusAvailable&&Medicine()==medicine);yield return CloseUi(true);
            yield return OpenNpc(false);yield return Choose("后山是不是出事了","yaolao-rescue");Check("still available before confirmation",QuestStatus()==QuestLedger.StatusAvailable);
            yield return Choose("我去看看","yaolao-accept");Need("only confirmation accepts",QuestStatus()==QuestLedger.StatusActive&&Medicine()==medicine);
            settings.reduceMotion=true;yield return null;yield return null;Check("reduceMotion reveals current text",!ui.IsTyping);
            yield return Settle();var art=ui.GetComponentsInChildren<RawImage>();var tints=art.Select(a=>a.color).ToArray();yield return new WaitForSecondsRealtime(.3f);
            Check("reduceMotion portrait tint stable",art.Length>0&&art.Select(a=>a.color).SequenceEqual(tints));yield return Choose("我这就去","yaolao-bye");yield return CloseUi(true);Head("InProgress","?",false);
            var label=HeadLabel();var headPos=label.rectTransform.anchoredPosition;yield return new WaitForSecondsRealtime(.3f);Check("reduceMotion overhead frozen",Vector2.Distance(headPos,label.rectTransform.anchoredPosition)<.01f);yield return Capture("03-accepted");
            Isolated();File.WriteAllText(Path.Combine(output,"fixture-before.json"),State());var quest=c.Quests.Find(Quest);
            foreach(var goal in quest.Objectives)QuestLedger.Advance(c.ActiveSave,quest,goal.Id,Math.Max(1,goal.Count));
            Record("controlled objective completion","INJECTED","QuestLedger.Advance for defeat/talk goals on scratch only; NO combat or travel completion claim.");
            File.WriteAllText(Path.Combine(output,"fixture-after.json"),State());c.Repository.Write(c.ActiveSave);c.LoadSlot(c.Repository.Read(1));yield return new WaitForSecondsRealtime(.4f);
            Need("goals ready, not automatically rewarded",QuestStatus()==QuestLedger.StatusActive&&Medicine()==medicine);Need("ready NPC snap",c.SnapTravelerNextToNpc(Npc));yield return new WaitForSecondsRealtime(.25f);
            Head("Completable","?",true);yield return Capture("04-turn-in-ready");string guardBefore=State();
            var wrong=QuestRuntime.TryTurnIn(c.ActiveSave,c.Quests,c.Repository.Items,Quest,"npc-shenhuai-an",0);
            Check("Core rejects wrong turn-in NPC",wrong.Completed.Count==0&&State()==guardBefore);
            var far=QuestRuntime.TryTurnIn(c.ActiveSave,c.Quests,c.Repository.Items,Quest,Npc,201);
            Check("Core rejects distant turn-in",far.Completed.Count==0&&State()==guardBefore);
            yield return OpenNpc(false);yield return Choose("交付","yaolao-thanks");yield return Settle();
            Need("actual NPC hand-in rewards exactly three",QuestStatus()==QuestLedger.StatusCompleted&&Medicine()==medicine+3&&c.ActiveSave.coins==coins);
            Check("thanks promises three to player",ui.DialogueText.text.Contains("三帖")&&!ui.DialogueText.text.Contains("两帖你拿着"));yield return Capture("05-thanks");
            if(!ui.HasPortrait(DialogueUISpeaker.Npc))Record("dual portrait visual","PENDING","Yao Lao art HOLD; empty artwork/nameplate allowed; not visually approved.");
            yield return Choose("药老客气","yaolao-bye");yield return CloseUi(true);c.ShowMenu();Isolated();c.LoadSlot(c.Repository.Read(1));yield return new WaitForSecondsRealtime(.4f);
            Need("product persistence survives reload",QuestStatus()==QuestLedger.StatusCompleted&&Medicine()==medicine+3&&c.ActiveSave.coins==coins);
            yield return OpenNpc(false);yield return Settle();Check("completed task cannot be handed in again",!ui.GetComponentsInChildren<Button>().Any(b=>b.name.StartsWith("Dialogue choice ")&&b.GetComponentInChildren<TMP_Text>().text.Contains("交付")));
            yield return CloseUi();var repeated=QuestRuntime.TryTurnIn(c.ActiveSave,c.Quests,c.Repository.Items,Quest,Npc,0);
            Check("repeat after reload does not reward",repeated.Completed.Count==0&&Medicine()==medicine+3&&c.ActiveSave.coins==coins);Head("None","",false);yield return Capture("06-reloaded");
            Check("six bounded screenshots",shots==6);Record("human visual review","PENDING","Review 01..06 actual GameView; injected goal completion is not combat coverage.");
        }
        static UnityEngine.InputSystem.Key MoveKey(Vector2 p)
        {
            var keys=new[]{UnityEngine.InputSystem.Key.D,UnityEngine.InputSystem.Key.A,UnityEngine.InputSystem.Key.S,UnityEngine.InputSystem.Key.W};var offsets=new[]{Vector2.right,Vector2.left,Vector2.up,Vector2.down};
            for(int i=0;i<keys.Length;i++){var end=p+offsets[i]*100;if(c.TerrainStandable(end.x,end.y))return keys[i];}throw new InvalidOperationException("No standable movement probe direction.");
        }
        static TMP_Text HeadLabel() => c.QuestOverheadLabelForShot(Npc);
        static void Head(string expected,string glyph,bool gold)
        {
            string before=State();var label=HeadLabel();string state=QuestRuntime.OverheadState(c.ActiveSave,c.Quests,Npc).ToString();
            Check("head query is read-only",State()==before);Check("head state "+expected,state==expected,state);
            if(glyph.Length==0){Check("completed overhead hidden",label==null||!label.isActiveAndEnabled||string.IsNullOrWhiteSpace(label.text));return;}
            Need("actual overhead "+glyph,label!=null&&label.isActiveAndEnabled&&label.text.Trim()==glyph);TextVisible(label);
            var npcRect=c.TownNpcRectForShot(Npc);Need("overhead belongs to actual NPC",npcRect!=null&&label.transform.IsChildOf(npcRect)&&!label.raycastTarget);
            var color=label.color;Check("head color "+expected,gold?color.r>.55f&&color.g>.35f&&color.r-color.b>.15f:Mathf.Abs(color.r-color.g)<.15f&&Mathf.Abs(color.g-color.b)<.15f,color.ToString());
        }
        static void ScrollTo(Button button)
        {
            var scroll=button.GetComponentInParent<ScrollRect>();if(scroll==null)return;var r=(RectTransform)button.transform;
            float span=scroll.content.rect.height-scroll.viewport.rect.height;if(span>0)scroll.verticalNormalizedPosition=1-Mathf.Clamp01(-r.anchoredPosition.y/span);Canvas.ForceUpdateCanvases();
        }
        static void Click(Button button)
        {
            Need("click target active",button!=null&&button.isActiveAndEnabled&&button.IsInteractable());Canvas.ForceUpdateCanvases();
            var rect=(RectTransform)button.transform;var canvas=button.GetComponentInParent<Canvas>().rootCanvas;Need("native Overlay",canvas.renderMode==RenderMode.ScreenSpaceOverlay);
            var point=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center));var data=new PointerEventData(EventSystem.current){position=point,button=PointerEventData.InputButton.Left};
            var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(data,hits);Need("real raycast reaches "+button.name,hits.Count>0&&hits[0].gameObject.GetComponentInParent<Button>()==button,hits.Count==0?"none":hits[0].gameObject.name);
            ExecuteEvents.Execute(button.gameObject,data,ExecuteEvents.pointerDownHandler);ExecuteEvents.Execute(button.gameObject,data,ExecuteEvents.pointerUpHandler);ExecuteEvents.Execute(button.gameObject,data,ExecuteEvents.pointerClickHandler);
        }
        static void TextVisible(TMP_Text text)
        {
            Canvas.ForceUpdateCanvases();text.ForceMeshUpdate();var bounds=text.rectTransform.rect;int page=text.overflowMode==TextOverflowModes.Page?text.pageToDisplay-1:0;
            var chars=text.textInfo.characterInfo.Take(text.textInfo.characterCount).Where(ch=>ch.pageNumber==page).ToArray();var visible=chars.Where(ch=>ch.isVisible).ToArray();
            string compact(string s)=>new string(s.Where(ch=>!char.IsWhiteSpace(ch)).ToArray());string rendered=new string(visible.Select(ch=>ch.character).ToArray());
            bool valid=text.isActiveAndEnabled&&!text.canvasRenderer.cull&&text.color.a>.01f&&!text.isTextTruncated&&visible.Length>0&&text.GetComponentsInParent<CanvasGroup>().All(g=>g.alpha>.99f);
            valid&=chars.Length>0&&compact(rendered)==compact(new string(chars.Select(ch=>ch.character).ToArray()))&&text.maxVisibleCharacters>=chars.Last().index+1;
            foreach(var ch in visible)
            {
                var vertices=text.textInfo.meshInfo[ch.materialReferenceIndex].colors32;int vertex=ch.vertexIndex;
                valid&=vertices!=null&&vertices.Length>vertex+3&&vertices[vertex].a>0&&vertices[vertex+1].a>0&&vertices[vertex+2].a>0&&vertices[vertex+3].a>0;
                valid&=ch.bottomLeft.x>=bounds.xMin-2&&ch.bottomLeft.y>=bounds.yMin-2&&ch.topRight.x<=bounds.xMax+2&&ch.topRight.y<=bounds.yMax+2;
                foreach(var local in new[]{ch.bottomLeft,ch.topRight}){var world=text.transform.TransformPoint(local);var screen=RectTransformUtility.WorldToScreenPoint(null,world);valid&=screen.x>=0&&screen.y>=0&&screen.x<=Screen.width&&screen.y<=Screen.height;
                    foreach(var mask in text.GetComponentsInParent<RectMask2D>())valid&=mask.rectTransform.rect.Contains((Vector2)mask.transform.InverseTransformPoint(world));}
            }
            File.AppendAllText(Path.Combine(output,"text-layout.jsonl"),JsonUtility.ToJson(new Layout{name=text.name,text=text.text,rendered=rendered,bounds=bounds,valid=valid})+"\n");Check("actual rendered text "+text.name,valid,rendered);
            if(!valid)DiagnoseText(text,chars,visible,bounds,rendered);
        }

        [Serializable] sealed class TextFailure { public int predicateFrame; public string name,text,rendered,font,fontMaterial; public string[] predicates; public Vector4 bounds; public GlyphFailure[] glyphs; public MaskFailure[] masks; public GraphicFailure[] graphics; }
        [Serializable] sealed class GlyphFailure { public string character; public int sourceIndex,materialIndex,vertexIndex; public bool meshAlpha,labelBounds; public int[] alpha; public Vector3[] local; public Vector2[] screen; public bool[] screenBounds,maskBounds; }
        [Serializable] sealed class MaskFailure { public string name; public bool active; public Rect bounds; public Vector3[] world; }
        [Serializable] sealed class GraphicFailure { public string name,type,material,shader,texture; public bool active,cull; public Color color; public float alpha; }
        [Serializable] sealed class ChoiceFailure { public int predicateFrame,matching; public string wanted,node,token,hostNpc,relation; public bool terminal,hasSourceOptions,canAdvance; public string[] queryOptions,hostOptions,frameOptions,buttons; }
        [Serializable] sealed class DiagnosticImage { public int predicateFrame,captureFrame,width,height; public string file,error,method="immediate framebuffer readback; may precede predicate rendering; no render fence or retry"; }
        static string Identity(UnityEngine.Object value)=>value==null?"null":value.name+"#"+value.GetInstanceID();
        static string[] OptionDetails(DialogueView view)=>view==null?Array.Empty<string>():view.Options.Select((o,i)=>i+":"+o.Text+" -> "+o.NextNode+" [trust="+o.MinTrust+", goodwill="+o.MinGoodwill+"]").ToArray();
        static void DiagnoseChoices(string wanted,DialogueView query,Button[] buttons)
        {
            int frame=Time.frameCount;
            try
            {
                var host=typeof(FrontEndController).GetField("talkView",Private)?.GetValue(c) as DialogueView;
                var request=typeof(DialogueUI).GetField("requests",Private)?.GetValue(ui);
                var shown=request?.GetType().GetProperty("Frame")?.GetValue(request) as DialogueUIFrame;
                var relation=NpcLedger.RelationOf(c.ActiveSave,Npc);
                var data=new ChoiceFailure{predicateFrame=frame,wanted=wanted,node=ui.CurrentNodeId,token=ui.CurrentToken,
                    hostNpc=typeof(FrontEndController).GetField("talkNpcId",Private)?.GetValue(c) as string,
                    relation=relation==null?"null":"trust="+relation.trust+",goodwill="+relation.goodwill,
                    matching=buttons.Count(b=>b.GetComponentInChildren<TMP_Text>().text.Contains(wanted)),
                    terminal=shown!=null&&shown.IsTerminal,hasSourceOptions=shown!=null&&shown.HasSourceOptions,canAdvance=shown!=null&&shown.CanAdvance,
                    queryOptions=OptionDetails(query),hostOptions=OptionDetails(host),frameOptions=shown==null?Array.Empty<string>():shown.Choices.Select(v=>v.SourceOptionIndex+":"+v.Text).ToArray(),
                    buttons=buttons.Select(b=>b.name+" | "+b.GetComponentInChildren<TMP_Text>().text+" | active="+b.isActiveAndEnabled+" | interactable="+b.IsInteractable()).ToArray()};
                File.AppendAllText(Path.Combine(output,"choice-failure.jsonl"),JsonUtility.ToJson(data)+"\n");
                DiagnosticCapture(frame);
            }
            catch(Exception e){Record("choice failure diagnostic","FAIL",e.ToString());}
        }
        static void DiagnoseText(TMP_Text text,TMP_CharacterInfo[] chars,TMP_CharacterInfo[] visible,Rect bounds,string rendered)
        {
            int frame=Time.frameCount;
            try
            {
                var masks=text.GetComponentsInParent<RectMask2D>();var glyphs=new List<GlyphFailure>();
                foreach(var ch in visible)
                {
                    var colors=text.textInfo.meshInfo[ch.materialReferenceIndex].colors32;int v=ch.vertexIndex;
                    bool mesh=colors!=null&&colors.Length>v+3;
                    var local=new[]{ch.bottomLeft,ch.topLeft,ch.topRight,ch.bottomRight};
                    var screen=local.Select(p=>RectTransformUtility.WorldToScreenPoint(null,text.transform.TransformPoint(p))).ToArray();
                    // Existing gate checks bottom-left/top-right; all four corners are recorded only for diagnosis.
                    glyphs.Add(new GlyphFailure{character=ch.character.ToString(),sourceIndex=ch.index,materialIndex=ch.materialReferenceIndex,vertexIndex=v,
                        alpha=mesh?new[]{(int)colors[v].a,(int)colors[v+1].a,(int)colors[v+2].a,(int)colors[v+3].a}:Array.Empty<int>(),
                        meshAlpha=mesh&&colors[v].a>0&&colors[v+1].a>0&&colors[v+2].a>0&&colors[v+3].a>0,
                        labelBounds=ch.bottomLeft.x>=bounds.xMin-2&&ch.bottomLeft.y>=bounds.yMin-2&&ch.topRight.x<=bounds.xMax+2&&ch.topRight.y<=bounds.yMax+2,
                        local=local,screen=screen,screenBounds=screen.Select(p=>p.x>=0&&p.y>=0&&p.x<=Screen.width&&p.y<=Screen.height).ToArray(),
                        maskBounds=masks.SelectMany(m=>local.Select(p=>m.rectTransform.rect.Contains((Vector2)m.transform.InverseTransformPoint(text.transform.TransformPoint(p))))).ToArray()});
                }
                string compact(string value)=>new string(value.Where(ch=>!char.IsWhiteSpace(ch)).ToArray());
                var data=new TextFailure{predicateFrame=frame,name=text.name,text=text.text,rendered=rendered,font=Identity(text.font),fontMaterial=Identity(text.fontSharedMaterial),
                    bounds=new Vector4(bounds.xMin,bounds.yMin,bounds.xMax,bounds.yMax),glyphs=glyphs.ToArray(),
                    predicates=new[]{"active="+text.isActiveAndEnabled,"notCulled="+!text.canvasRenderer.cull,"textAlpha="+text.color.a,"notTruncated="+!text.isTextTruncated,
                        "visibleCount="+visible.Length,"canvasGroupAlpha="+string.Join(",",text.GetComponentsInParent<CanvasGroup>().Select(g=>g.alpha)),
                        "charsCount="+chars.Length,"renderedComplete="+(compact(rendered)==compact(new string(chars.Select(ch=>ch.character).ToArray()))),
                        "maxVisibleCharacters="+text.maxVisibleCharacters,"lastCharacterEnd="+(chars.Length==0?0:chars.Last().index+1),"pageToDisplay="+text.pageToDisplay},
                    masks=masks.Select(m=>{var corners=new Vector3[4];m.rectTransform.GetWorldCorners(corners);return new MaskFailure{name=m.name,active=m.isActiveAndEnabled,bounds=m.rectTransform.rect,world=corners};}).ToArray(),
                    graphics=(ui==null?new Graphic[]{text}:ui.GetComponentsInChildren<Graphic>(true)).Select(g=>{var mat=g.materialForRendering;return new GraphicFailure{name=g.name,type=g.GetType().Name,active=g.isActiveAndEnabled,
                        cull=g.canvasRenderer.cull,color=g.color,alpha=g.canvasRenderer.GetAlpha(),material=Identity(mat),shader=Identity(mat==null?null:mat.shader),texture=Identity(g.mainTexture)};}).ToArray()};
                File.AppendAllText(Path.Combine(output,"text-failure.jsonl"),JsonUtility.ToJson(data)+"\n");DiagnosticCapture(frame);
            }
            catch(Exception e){Record("text failure diagnostic","FAIL",e.ToString());}
        }
        static void DiagnosticCapture(int predicateFrame)
        {
            var info=new DiagnosticImage{predicateFrame=predicateFrame,captureFrame=Time.frameCount,file="diagnostic-"+predicateFrame+"-"+(++diagnostics)+".png"};Texture2D image=null;
            try{image=ScreenCapture.CaptureScreenshotAsTexture();if(image==null)throw new InvalidOperationException("No diagnostic framebuffer");info.width=image.width;info.height=image.height;File.WriteAllBytes(Path.Combine(output,info.file),image.EncodeToPNG());}
            catch(Exception e){info.error=e.ToString();}
            finally{if(image!=null)UnityEngine.Object.Destroy(image);File.AppendAllText(Path.Combine(output,"diagnostic-images.jsonl"),JsonUtility.ToJson(info)+"\n");}
        }

        static IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();Texture2D texture=null;
            try{Need("capture is non-batch GameView",!Application.isBatchMode);var canvas=UnityEngine.Object.FindAnyObjectByType<Canvas>();Need("capture Overlay",canvas!=null&&canvas.rootCanvas.renderMode==RenderMode.ScreenSpaceOverlay);texture=ScreenCapture.CaptureScreenshotAsTexture();Need("framebuffer image",texture!=null&&texture.width>0&&texture.height>0);
                File.WriteAllBytes(Path.Combine(output,name+".png"),texture.EncodeToPNG());shots++;File.AppendAllText(Path.Combine(output,"screenshots.jsonl"),JsonUtility.ToJson(new Shot{file=name+".png",method="public ScreenCapture after WaitForEndOfFrame",page=c.CurrentPage,node=ui==null?"":ui.CurrentNodeId,frame=Time.frameCount,width=texture.width,height=texture.height})+"\n");}
            finally{if(texture!=null)UnityEngine.Object.Destroy(texture);}
        }
        static string Hash(string path){using(var sha=SHA256.Create())using(var f=File.OpenRead(path))return BitConverter.ToString(sha.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
        static void Inputs()
        {
            var paths=new[]{"Editor/DialogueQuestIntegrationShot.cs","Editor/DialogueQuestIntegrationShot.cs.meta","Scripts/Core/Quest.cs","Scripts/Core/Dialogue.cs","Scripts/Core/DialogueTree.cs","Scripts/Presentation/DialogueUI.cs","Scripts/Presentation/DialogueUIModel.cs","Scripts/Presentation/NpcQuestOverhead.cs","Scripts/Core/NpcLocalPatrol.cs","Scripts/Core/TownMap.cs","Scripts/Core/TownNavigator.cs","Scripts/Economy/KarmaTriggers.cs","Resources/Config/town-map.json","Resources/Config/building-plots.json","Resources/Config/dialogues.json","Resources/Config/quests.json","Scenes/FrontEnd.unity"};
            foreach(var relative in paths.Concat(Directory.GetFiles(Path.Combine(Application.dataPath,"Scripts/Presentation"),"FrontEnd*.cs").Select(p=>"Scripts/Presentation/"+Path.GetFileName(p))))
            {var local=Path.Combine(Application.dataPath,relative);var original=Path.Combine(source,"Assets",relative);Need("isolated/source SHA "+relative,File.Exists(original)&&Hash(local)==Hash(original));stamps.Add(new Stamp{path=relative,sha256=Hash(local)});}
        }
        static void Save(string status){if(!string.IsNullOrEmpty(output))File.WriteAllText(Path.Combine(output,"runtime-results.json"),JsonUtility.ToJson(new Report{status=status,screenshots=shots,runtimeErrors=errors,passed=rows.Count(r=>r.status=="PASS"),failed=rows.Count(r=>r.status=="FAIL"),pending=rows.Count(r=>r.status=="PENDING"),checks=rows,inputs=stamps},true));}
        static void Finish()
        {
            if(finished)return;finished=true;
            try{if(ui!=null&&ui.IsOpen)ui.Close(DialogueUICloseReason.Host);if(settings!=null)settings.reduceMotion=oldReduced;
                if(keyboard!=null)InputSystem.RemoveDevice(keyboard);if(previousKeyboard!=null&&previousKeyboard.added)previousKeyboard.MakeCurrent();
                foreach(var stamp in stamps)Check("input unchanged "+stamp.path,Hash(Path.Combine(Application.dataPath,stamp.path))==stamp.sha256&&Hash(Path.Combine(source,"Assets",stamp.path))==stamp.sha256);}
            catch(Exception e){Record("cleanup/input verification","FAIL",e.ToString());}
            string status=rows.Any(r=>r.status=="FAIL")?"FAIL":rows.Any(r=>r.status=="PENDING")?"PENDING":"CHECKS_PASS_VISUAL_PENDING";Save(status);
            SessionState.SetBool(KeyName,false);EditorApplication.update-=Tick;Application.logMessageReceived-=OnLog;
            var old=SessionState.GetString(KeyName+".OldSave","");Environment.SetEnvironmentVariable("TWELVEJADE_SAVEDIR",old.Length==0?null:old);EditorApplication.Exit(status=="FAIL"?1:status=="PENDING"?2:0);
        }
    }
    public sealed class DialogueQuestIntegrationPump:MonoBehaviour {}
}
