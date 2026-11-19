using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using TwelveJade.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    public sealed partial class FrontEndController
    {
        sealed class WildActor
        { public MapEncounter Encounter; public WildSpawn Spec; public WildernessRoamer Motion; public RectTransform Rect; public Placeable Place; public WorldCreaturePose Pose; public RawImage Image; public CombatActorState State; public float Height, FootRatio; public Vector2 Position; }
        sealed class MineView { public MineNode Node; public RawImage Image; public TMP_Text Caption; public bool Ready; }
        WildernessLayout wilderness;
        readonly Dictionary<string,WildActor> wildActors=new Dictionary<string,WildActor>();
        readonly List<MineView> mineViews=new List<MineView>();
        TMP_Text wildRegionTitle,miningCaption; RectTransform miningPlate;
        MineNode miningNode; float miningElapsed, wildRetargetClock, wildReentryGrace;Vector2 miningOrigin;
        Vector2? wildernessReturn; SaveData wildernessReturnOwner;
        const string WildernessCollision="wilderness-domain";
        WildernessLayout WildLayout
        {
            get
            {
                if(wilderness!=null)return wilderness;
                var asset=Resources.Load<TextAsset>("Config/wilderness");
                try { if(asset!=null)wilderness=WildernessLayout.Parse(asset.text); }
                catch(FormatException ex){Debug.LogError(ex.Message);}
                return wilderness;
            }
        }
        void BuildWilderness()
        {
            CancelMining();wildActors.Clear();mineViews.Clear();wildRetargetClock=0;wildReentryGrace=2;
            var layout=WildLayout;if(layout==null||worldRoot==null)return;
            town.ClearBlockers(WildernessCollision);
            foreach(var block in layout.Blockers)town.RegisterBlocker(WildernessCollision,block.MinX,block.MinY,block.MaxX,block.MaxY);
            foreach(var region in layout.Regions)
            {
                if(string.IsNullOrEmpty(region.Art))continue;
                var texture=Resources.Load<Texture2D>(region.Art);if(texture==null)continue;
                // Ground lives beneath all depth-sorted feet; its cliff props never float over the HUD.
                var root=worldClickFeedback!=null?worldClickFeedback.transform:worldRoot;
                var image=ui.Rect(root,"Domain ground "+region.Id,(region.X-region.Width*.5f)*worldScale,
                    (region.Y-region.Height*.5f)*worldScale,region.Width*worldScale,region.Height*worldScale).gameObject.AddComponent<RawImage>();
                image.texture=texture;image.raycastTarget=false;
                if(region.Id=="qingshi-boss-domain")BuildDomainCliffs(region,texture);
            }
            foreach(var prop in layout.Decorations)
            {
                var art=Resources.Load<Texture2D>(prop.Art);
                PlaceInWorld(prop.Id,new Vector2(prop.X,prop.Y-prop.Height*.9453125f),prop.Height,prop.Height,art,prop.Y);
                town.RegisterFootCapsule(WildernessCollision,prop.X-prop.Height*.2f,prop.X+prop.Height*.2f,prop.Y-5,prop.Height*.055f);
            }
            BuildBuildingApproaches();
            foreach(var encounter in town.Encounters)
            {
                if(activeSave.HasFlag(encounter.Id))continue;
                var spec=layout.Spawns.FirstOrDefault(s=>s.Encounter==encounter.Id);if(spec==null)continue;
                var standPath=WildCreatureArt.Stand(encounter.Enemy);
                var art=string.IsNullOrEmpty(standPath)?null:Resources.Load<Texture2D>(standPath);
                bool newArt=art!=null;
                if(art==null)art=Resources.Load<Texture2D>(encounter.Art);
                float height=newArt?WildCreatureArt.Height(encounter.Enemy,spec.Height):spec.Height;
                float footRatio=newArt?WildCreatureArt.FootRatio(encounter.Enemy):.9453125f;
                var rect=PlaceInWorld("enc-"+encounter.Id,new Vector2(encounter.X,encounter.Y-height*footRatio),height,height,null,encounter.Y);
                var place=placeables[placeables.Count-1];
                var shadow=ui.Rect(rect,"Foot contact",height*.22f,height*(footRatio-.028f),height*.56f,height*.07f).gameObject.AddComponent<RawImage>();
                shadow.texture=DotTexture();shadow.color=new Color(.015f,.025f,.02f,.4f);shadow.raycastTarget=false;
                var image=ui.Rect(rect,"Living creature",0,0,height,height).gameObject.AddComponent<RawImage>();image.texture=art;image.raycastTarget=true;
                var walkPath=newArt?WildCreatureArt.Walk(encounter.Enemy):spec.Walk;
                var walking=string.IsNullOrEmpty(walkPath)?null:Resources.Load<Texture2D>(walkPath);
                var pose=image.gameObject.AddComponent<WorldCreaturePose>();pose.Configure(art,walking,encounter.Enemy=="qingshi-mountain-lord"?155:115,encounter.Enemy);
                var actor=new WildActor{Encounter=encounter,Spec=spec,Rect=rect,Place=place,Pose=pose,Image=image,Height=height,FootRatio=footRatio,Position=new Vector2(encounter.X,encounter.Y),
                    Motion=new WildernessRoamer(encounter.X,encounter.Y,spec,StableSeed(encounter.Id),
                        (x,y,tx,ty)=>TownNavigator.FindPath(town,x,y,tx,ty),(x,y,tx,ty)=>CanTraverseWild(encounter.Enemy,x,y,tx,ty))};
                wildActors.Add(encounter.Id,actor);encounterMarkers[encounter.Id]=rect;
                MakeClickable(rect,"接近 "+encounter.Name,()=>TrackWildEncounter(encounter.Id));
                if(encounter.Enemy=="qingshi-mountain-lord")
                {var title=ui.Label(rect,"玄岩山君",-35,-42,height+70,38,26,UiKit.Gold,TextAlignmentOptions.Center,false);title.raycastTarget=false;}
            }
            foreach(var node in layout.Mines)
            {
                var rect=PlaceInWorld("mine-"+node.Id,new Vector2(node.X,node.Y-node.Height*.9453125f),node.Height,node.Height,null,node.Y);
                var shadow=ui.Rect(rect,"Mineral ground shadow",10,node.Height*.88f,node.Height-20,16).gameObject.AddComponent<RawImage>();
                shadow.texture=DotTexture();shadow.color=new Color(.025f,.035f,.03f,.3f);shadow.raycastTarget=false;
                var image=ui.Rect(rect,"Harvestable ore",0,0,node.Height,node.Height).gameObject.AddComponent<RawImage>();image.raycastTarget=true;
                var caption=ui.Label(rect,"",-60,-34,node.Height+120,30,20,UiKit.Paper,TextAlignmentOptions.Center,false);caption.raycastTarget=false;
                var view=new MineView{Node=node,Image=image,Caption=caption};mineViews.Add(view);RefreshMine(view);
                MakeClickable(rect,"开采 "+node.Name,()=>RequestMine(node));
            }
            wildRegionTitle=ui.Label(content,"",710,48,500,58,29,UiKit.Paper,TextAlignmentOptions.Center,false);wildRegionTitle.raycastTarget=false;
            // An explicit trailhead makes the first wilderness discoverable without teleporting.
            var postArt=Resources.Load<Texture2D>("Art/Props/Wilderness/trail-stone");
            var post=PlaceInWorld("western-wilds-trail",new Vector2(2170,4270),130,130,postArt,4393);
            MakeClickable(post,"镇外山径",()=>ShowWildRoutes());
            var label=ui.Label(post,"西行 · 苍松荒坡",-70,-38,270,36,24,UiKit.Paper,TextAlignmentOptions.Center,false);label.raycastTarget=false;
        }
        void BuildDomainCliffs(WildRegion region,Texture2D texture)
        {
            // Authoring coordinates on the original 1600-square terrain plate.
            Vector2[][] outlines = {
                new[] { new Vector2(170,270),new Vector2(330,180),new Vector2(480,120),new Vector2(560,0),new Vector2(700,0),new Vector2(810,180),new Vector2(835,325),new Vector2(960,414),new Vector2(870,465),new Vector2(650,411),new Vector2(500,410),new Vector2(405,475),new Vector2(233,503),new Vector2(160,420) },
                new[] { new Vector2(850,170),new Vector2(960,195),new Vector2(1040,256),new Vector2(1170,262),new Vector2(1280,295),new Vector2(1330,470),new Vector2(1260,510),new Vector2(1100,470),new Vector2(1000,425),new Vector2(900,420) },
                new[] { new Vector2(150,410),new Vector2(300,445),new Vector2(345,540),new Vector2(425,591),new Vector2(360,650),new Vector2(230,676),new Vector2(150,708),new Vector2(20,690),new Vector2(15,550),new Vector2(90,525) },
                new[] { new Vector2(65,748),new Vector2(155,760),new Vector2(185,875),new Vector2(245,920),new Vector2(276,1030),new Vector2(360,1110),new Vector2(430,1210),new Vector2(565,1260),new Vector2(590,1342),new Vector2(440,1380),new Vector2(285,1300),new Vector2(230,1170),new Vector2(115,1100),new Vector2(28,960) },
                new[] { new Vector2(1440,425),new Vector2(1515,448),new Vector2(1580,520),new Vector2(1600,710),new Vector2(1480,748),new Vector2(1395,650),new Vector2(1415,550) },
                new[] { new Vector2(1480,750),new Vector2(1590,830),new Vector2(1600,1030),new Vector2(1515,1130),new Vector2(1370,1195),new Vector2(1310,1150),new Vector2(1375,1060),new Vector2(1420,960),new Vector2(1405,845) },
                new[] { new Vector2(830,1210),new Vector2(970,1220),new Vector2(1090,1235),new Vector2(1170,1290),new Vector2(1215,1425),new Vector2(1100,1495),new Vector2(900,1490),new Vector2(710,1460),new Vector2(720,1360),new Vector2(800,1320) }
            };
            for(int i=0;i<outlines.Length;i++)
            {
                float contact=outlines[i].Max(p=>p.y);
                var rect=PlaceInWorld("domain-cliff-"+i,new Vector2(region.X,region.Y-region.Height*.5f),
                    region.Width*worldScale,region.Height*worldScale,null,region.Y-region.Height*.5f+contact/1600*region.Height);
                rect.gameObject.AddComponent<WildernessCliffGraphic>().Configure(texture,outlines[i]);
            }
        }
        void RefreshMine(MineView view)
        {
            view.Ready=MiningRules.Available(activeSave,view.Node.Id);
            view.Image.texture=Resources.Load<Texture2D>(view.Ready?view.Node.Art:view.Node.SpentArt);
            view.Caption.text=view.Ready?"玄铁矿 · 可采集":"矿脉已采 · 明日复生";
        }
        bool TrackWildEncounter(string id)
        {
            CancelMining();if(!wildActors.TryGetValue(id,out var actor))return false;
            if(WorldCombatActive){Notify("先了结眼前的交锋，再换目标。");return false;}
            var approach=ArrivalPointNear(actor.Position,WildBodyRadius(actor)+65);
            if(Vector2.Distance(worldPos,actor.Position)<=WildBodyRadius(actor)+165){EngageWild(actor);return true;}
            return RequestTravelTo(approach,arrived=>
            {
                if(!wildActors.TryGetValue(arrived,out var current))return;
                if(Vector2.Distance(worldPos,current.Position)<=WildBodyRadius(current)+165)EngageWild(current);
                else TrackWildEncounter(arrived);
            },id);
        }
        void EngageWild(WildActor actor)
        {
            if(page!="town"||WorldDialogueInputBlocked||modal!=null||bagOverlay!=null||activeSave.HasFlag(actor.Encounter.Id))return;
            CancelTravel(); EncounterArrived?.Invoke(actor.Encounter.Id);
        }
        void TickWilderness(float dt)
        {
            if(page!="town"||WildLayout==null)return;
            wildRetargetClock-=dt;wildReentryGrace=Mathf.Max(0,wildReentryGrace-dt);
            foreach(var actor in wildActors.Values)
            {
                if(actor==worldBattleTarget)continue;
                if(actor.State!=null&&actor.State.IsDead)continue;
                float moved=actor.Motion.Advance(dt,worldPos.x,worldPos.y,false);
                actor.Position=new Vector2(actor.Motion.X,actor.Motion.Y);
                float y=town.Elevation.ProjectY(actor.Motion.X,actor.Motion.Y);
                actor.Rect.anchoredPosition=new Vector2(actor.Motion.X*worldScale,-(y-actor.Height*actor.FootRatio)*worldScale);
                actor.Place.BaseY=y;actor.Pose.Step(moved,actor.Motion.Facing);
                float distance=Vector2.Distance(worldPos,new Vector2(actor.Motion.X,actor.Motion.Y));
                if(!WorldCombatActive&&wildReentryGrace<=0&&actor.Motion.State==WildernessRoamer.Mode.Chase&&distance<=WildBodyRadius(actor)+110)
                {EngageWild(actor);return;}
                if(travelContextId==actor.Encounter.Id&&hasTarget&&wildRetargetClock<=0)
                {TrackWildEncounter(actor.Encounter.Id);wildRetargetClock=.5f;}
            }
            var region=WildLayout.Regions.LastOrDefault(r=>Vector2.Distance(worldPos,new Vector2(r.X,r.Y))<r.Radius);
            if(wildRegionTitle!=null)wildRegionTitle.text=region?.Name??"";
            foreach(var mine in mineViews)
                if(mine.Ready!=MiningRules.Available(activeSave,mine.Node.Id))RefreshMine(mine);
            if(miningNode!=null)
            {
                var keys=Keyboard.current;
                if(Vector2.Distance(worldPos,miningOrigin)>6||hasTarget||keys!=null&&(keys.escapeKey.wasPressedThisFrame||keys.wKey.isPressed||keys.aKey.isPressed||keys.sKey.isPressed||keys.dKey.isPressed))
                {CancelMining();return;}
                miningElapsed+=dt;
                if(miningCaption!=null)miningCaption.text="凿取玄铁  ·  "+Mathf.CeilToInt(Mathf.Max(0,miningNode.Seconds-miningElapsed))+"息";
                if(miningElapsed>=miningNode.Seconds)
                {
                    var node=miningNode;CancelMining();
                    try
                    {
                        if(MiningRules.Harvest(activeSave,Table,node,repository.Write,out var message))
                        {userBag=activeSave.bag;PlayClick();}
                        Notify(message);
                    }
                    catch(Exception ex){Debug.LogError("Mining save failed: "+ex.Message);Notify("采集未能写入，矿石仍保留。请稍后重试。");}
                    foreach(var view in mineViews)if(view.Node.Id==node.Id)RefreshMine(view);
                }
            }
        }
        void RequestMine(MineNode node)
        {
            CancelMining();if(!MiningRules.Available(activeSave,node.Id)){Notify("今日已采尽，明日再来。 ");return;}
            var point=ArrivalPointNear(new Vector2(node.X,node.Y),85);
            if(!RequestTravelTo(point,_=>BeginMining(node),"mine-"+node.Id))Notify("先走到矿脉旁再开采。");
        }
        void BeginMining(MineNode node)
        {
            if(Vector2.Distance(worldPos,new Vector2(node.X,node.Y))>145)return;
            CancelTravel();miningNode=node;miningOrigin=worldPos;miningElapsed=0;travelerSpeedNow=0;
            miningPlate=ui.Rect(content,"Mining jade notice",710,865,500,92);
            var art=ui.Rect(miningPlate,"Carved jade",0,0,500,92).gameObject.AddComponent<JadeCommandPlateGraphic>();
            art.texture=ApprovedUiArt.Load("nov9/immortal-command");art.raycastTarget=false;
            miningCaption=ui.Label(miningPlate,"凿取玄铁 · 移动可中止",48,19,404,54,25,UiKit.Ink,TextAlignmentOptions.Center,false);miningCaption.raycastTarget=false;
        }
        void CancelMining()
        {miningNode=null;miningElapsed=0;if(miningPlate!=null)Destroy(miningPlate.gameObject);miningPlate=null;miningCaption=null;}
        void ShowWildRoutes()
        {
            if(page!="town"||WildLayout==null||WorldDialogueInputBlocked)return;
            CloseModal();CancelTravel();CancelMining();
            var group=content.GetComponent<CanvasGroup>();group.interactable=false;group.blocksRaycasts=false;
            modal=ui.Rect(canvas,"Wild trail choices",580,245,760,540);
            modal.gameObject.AddComponent<V2FocusNavigation>();
            var bg=ui.Rect(modal,"Trail silk",0,0,760,540).gameObject.AddComponent<RawImage>();bg.texture=ApprovedUiArt.Load("nov11/artisan-silk-page");bg.raycastTarget=true;
            ui.Label(modal,"镇外山径",55,42,650,60,36,UiKit.Ink,TextAlignmentOptions.Center,false);
            ui.Label(modal,"野兽出没于西郊荒坡，玄岩山君盘踞西南石谷。",60,124,640,65,23,UiKit.Ink,TextAlignmentOptions.Center,false);
            int i=0;foreach(var region in WildLayout.Regions)
            {
                var r=region;MenuMemoArt.Command(ui,modal,"Travel "+r.Id,r.Name,new Rect(135,214+i*98,490,84),()=>
                {CloseModal();if(!RequestTravelTo(new Vector2(r.EntranceX,r.EntranceY)))Notify("山径暂不可达，请沿西街出镇。");},()=>settings.reduceMotion,PlayClick,25,false);i++;
            }
            MenuMemoArt.Command(ui,modal,"Close trail","收起",new Rect(235,438,290,76),CloseModal,()=>settings.reduceMotion,PlayClick,24,false);FocusFirst();
        }
    }
    public sealed class WorldCreaturePose:MonoBehaviour
    {
        RawImage image;Texture2D still,sheet;WorldCreatureRig rig;float distance,cycle;int face=1;
        public void Configure(Texture2D stand,Texture2D walk,float cycleDistance,string enemyId="")
        {
            image=GetComponent<RawImage>();still=stand;sheet=walk;cycle=Mathf.Max(1,cycleDistance);
            if(sheet==null){rig=gameObject.AddComponent<WorldCreatureRig>();rig.Configure(enemyId);}
            Step(0,1);
        }
        public void Combat(CombatPhase phase,float progress,bool reduced)=>rig?.Combat(phase,progress,reduced);
        public void Step(float actualDistance,int facing)
        {
            if(image==null)return;face=facing<0?-1:1;
            if(actualDistance>0)distance+=actualDistance;
            rig?.Walk(distance/cycle,actualDistance>.0001f,face);
            if(sheet!=null)
            {
                int frame=actualDistance>0?(int)(distance/cycle*4)%4:0;
                image.texture=sheet;image.uvRect=face>0?new Rect(frame*.25f,0,.25f,1):new Rect((frame+1)*.25f,0,-.25f,1);
            }
            else{image.texture=still;image.uvRect=face>0?new Rect(0,0,1,1):new Rect(1,0,-1,1);}
        }
    }
}
