using System;
using System.Collections.Generic;
using System.Text.Json;
using TwelveJade.Core;
using State = TwelveJade.Core.NpcLocalPatrol.PatrolState;

static class Program
{
    static readonly List<object> checks = new List<object>();
    static int failed;
    static void Check(string name, bool pass, string detail = "")
    { checks.Add(new { name, pass, detail }); if (!pass) failed++; }
    static TownMap Map(string npc = "") => TownMap.Parse(
        "{\"name\":\"local patrol check\",\"artWidth\":640,\"artHeight\":640," +
        "\"spawnX\":80,\"spawnY\":80,\"walkable\":[{\"x\":0,\"y\":0,\"width\":640,\"height\":640}]," +
        "\"blocked\":[],\"npcs\":[" + npc + "]}");
    const string OtherNpc = "{\"id\":\"existing-other\",\"name\":\"Other\",\"art\":\"existing-art\",\"height\":170,\"x\":380,\"y\":300,\"radius\":20}";
    const string SelfNpc = "{\"id\":\"existing-self\",\"name\":\"Self\",\"art\":\"existing-art\",\"height\":170,\"x\":240,\"y\":320,\"radius\":20}";
    static NpcLocalPatrol Patrol(TownMap map, int seed = 23, float radius = 150,
        Func<float,float,float,float,IReadOnlyList<(float x,float y)>> finder = null) =>
        new NpcLocalPatrol(240,320,radius,40,.25f,seed,
            finder ?? ((sx,sy,tx,ty) => TownNavigator.FindPath(map,sx,sy,tx,ty)),map.CanTraverse);
    static bool StartWalking(NpcLocalPatrol patrol)
    {
        for (int i = 0; i < 100 && patrol.State != State.Walking; i++) patrol.Advance(.05f,true);
        return patrol.State == State.Walking;
    }
    static float Distance(float ax,float ay,float bx,float by) => (float)Math.Sqrt((bx-ax)*(bx-ax)+(by-ay)*(by-ay));
    static void Throws(string name, Action action)
    {
        try { action(); Check(name,false,"No exception"); }
        catch (ArgumentException) { Check(name,true); }
    }
    static int Main()
    {
        try { Run(); } catch (Exception e) { Check("unexpected exception",false,e.ToString()); }
        Console.WriteLine(JsonSerializer.Serialize(new { status=failed==0?"PASS":"FAIL",scope="pure Core only; presentation/owner-aware NPC collision integration pending",checks },new JsonSerializerOptions{WriteIndented=true}));
        return failed==0?0:1;
    }
    static void Run()
    {
        var map=Map(OtherNpc);
        map.RegisterBlocker("real-building",300,260,316,375);
        map.RegisterFootCapsule("real-clutter",250,280,240,14);
        Check("fixtures are real registered rectangle/capsule/NPC obstacles",
            !map.CanTraverse(240,320,350,320) && !map.CanStand(265,240) && !map.CanStand(380,300));
        int bends=0, plans=0;
        var patrol=Patrol(map,finder:(sx,sy,tx,ty)=>{
            var path=TownNavigator.FindPath(map,sx,sy,tx,ty);plans++;
            if(path!=null && path.Count>1)bends++;
            return path;
        });
        bool valid=true, waited=false; float distance=0;
        for(int i=0;i<1200;i++)
        {
            float x=patrol.X,y=patrol.Y;var before=patrol.State;float moved=patrol.Advance(.05f,true);
            float actual=Distance(x,y,patrol.X,patrol.Y);distance+=actual;
            valid &= map.CanTraverse(x,y,patrol.X,patrol.Y) && Distance(240,320,patrol.X,patrol.Y)<=150.0001f && actual<=2.0001f && Math.Abs(moved-actual)<.0001f;
            waited |= before==State.Waiting && patrol.State==State.Waiting && distance>0 && actual==0;
        }
        Check("short walks and real dwell occur",distance>100 && waited,"distance="+distance+" plans="+plans);
        Check("all 1200 live steps remain inside home and avoid real obstacles",valid);
        Check("existing TownNavigator detours are exercised",bends>0,"detour plans="+bends);

        var open=Map();var a=Patrol(open,7);var b=Patrol(open,7);var other=Patrol(open,8);bool same=true,different=false;
        for(int i=0;i<200;i++)
        {
            a.Advance(.05f,true);b.Advance(.05f,true);other.Advance(.05f,true);
            same &= a.X==b.X && a.Y==b.Y && a.State==b.State;
            different |= a.X!=other.X || a.Y!=other.Y;
        }
        Check("seeded per-instance movement is repeatable and independent",same && different);

        var live=Patrol(Map());Check("dialogue fixture starts walking",StartWalking(live));live.Advance(.05f,true);
        float px=live.X,py=live.Y;live.SetDialogue(true);
        Check("dialogue stops synchronously without reporting arrival",live.State==State.Paused && live.X==px && live.Y==py);
        bool held=true;for(int i=0;i<20;i++)held &= live.Advance(30,true)==0 && live.X==px && live.Y==py && live.State==State.Paused;
        Check("dialogue advances neither position nor dwell",held);
        live.SetDialogue(false);float resumed=live.Advance(.05f,true);
        Check("dialogue release starts a fresh dwell without catch-up",resumed==0 && live.X==px && live.Y==py && live.State==State.Waiting);
        Check("dialogue release can plan again",StartWalking(live));
        px=live.X;py=live.Y;float longMove=live.Advance(1000,true);
        Check("long frame cannot teleport or consume its whole backlog",longMove>0 && longMove<=40*NpcLocalPatrol.MaxStepSeconds+.0001f && Distance(px,py,live.X,live.Y)<=2.0001f);
        px=live.X;py=live.Y;live.Advance(1000,false);
        bool stopped=live.State==State.Stopped;
        for(int i=0;i<20;i++){live.SetDialogue(false);stopped &= live.Advance(1,true)==0 && live.X==px && live.Y==py && live.State==State.Stopped;}
        Check("leaving Town permanently stops this visit instance",stopped);

        var movingMap=Map();IReadOnlyList<(float x,float y)> planned=null;
        var dynamic=Patrol(movingMap,finder:(sx,sy,tx,ty)=>planned=TownNavigator.FindPath(movingMap,sx,sy,tx,ty));
        Check("dynamic blocker fixture starts walking",StartWalking(dynamic));
        px=dynamic.X;py=dynamic.Y;var first=planned[0];float length=Distance(px,py,first.x,first.y);
        float cx=px+(first.x-px)/length*.5f,cy=py+(first.y-py)/length*.5f;
        movingMap.RegisterBlocker("appeared-after-plan",cx-.1f,cy-.1f,cx+.1f,cy+.1f);
        Check("new obstacle stops the next real segment without fake completion",
            dynamic.Advance(.05f,true)==0 && dynamic.X==px && dynamic.Y==py && dynamic.State==State.Blocked);

        var own=Patrol(Map(SelfNpc));for(int i=0;i<20;i++)own.Advance(.05f,true);
        Check("current static self-collider is exposed, never silently removed",own.X==240 && own.Y==320 && own.State==State.Blocked);
        int rejected=0;
        var outside=Patrol(open,radius:50,finder:(sx,sy,tx,ty)=>{rejected++;return new List<(float,float)>{(340,320),(tx,ty)};});
        for(int i=0;i<20;i++)outside.Advance(.05f,true);
        Check("walkable detours outside home are rejected in full",outside.State==State.Blocked && outside.X==240 && outside.Y==320 && rejected>0);
        var trappedMap=Map();trappedMap.RegisterBlocker("closed-home",230,310,250,330);var trapped=Patrol(trappedMap);
        for(int i=0;i<20;i++)trapped.Advance(.05f,true);
        Check("blocked home stays still without relocating",trapped.State==State.Blocked && trapped.X==240 && trapped.Y==320);
        var noRoute=Patrol(open,finder:(sx,sy,tx,ty)=>null);for(int i=0;i<20;i++)noRoute.Advance(.05f,true);
        Check("no route means blocked dwell, never arrival",noRoute.State==State.Blocked && noRoute.X==240 && noRoute.Y==320);
        var invalid=Patrol(open);
        Throws("non-finite delta rejected",()=>invalid.Advance(float.NaN,true));
        Throws("negative delta rejected",()=>invalid.Advance(-1,true));
        Throws("non-finite home rejected",()=>new NpcLocalPatrol(float.PositiveInfinity,0,50,40,1,1,(sx,sy,tx,ty)=>null,open.CanTraverse));
        Throws("zero home radius rejected",()=>new NpcLocalPatrol(240,320,0,40,1,1,(sx,sy,tx,ty)=>null,open.CanTraverse));
    }
}
