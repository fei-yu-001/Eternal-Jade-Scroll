using System;
using System.Collections.Generic;
namespace TwelveJade.Core
{
    // Transient ecology. All movement segments obey live world collision; no teleport on leash reset.
    public sealed class WildernessRoamer
    {
        public enum Mode { Rest, Patrol, Chase, Return }
        readonly float homeX,homeY,roam,leash,aggro,speed;
        readonly Func<float,float,float,float,IReadOnlyList<(float x,float y)>> find;
        readonly Func<float,float,float,float,bool> clear;
        readonly Random random;
        readonly List<(float x,float y)> route=new List<(float x,float y)>();
        int next;float wait,repath;
        public float X {get;private set;} public float Y {get;private set;}
        public int Facing {get;private set;}=1; public Mode State {get;private set;}
        public WildernessRoamer(float x,float y,WildSpawn spec,int seed,
            Func<float,float,float,float,IReadOnlyList<(float x,float y)>> path,
            Func<float,float,float,float,bool> traverse)
        {
            if(spec==null||path==null||traverse==null)throw new ArgumentNullException();
            if(spec.Roam<=0||spec.Leash<spec.Roam||spec.Speed<=0||spec.Aggro<=0||spec.Aggro>spec.Leash)throw new ArgumentException("Invalid wildlife ranges");
            homeX=X=x;homeY=Y=y;roam=spec.Roam;leash=spec.Leash;aggro=spec.Aggro;speed=spec.Speed;
            find=path;clear=traverse;random=new Random(seed);wait=.4f+(float)random.NextDouble()*2;
        }
        public float Advance(float seconds,float playerX,float playerY,bool paused)
        {
            if(paused||seconds<=0||float.IsNaN(seconds)||float.IsInfinity(seconds))return 0;
            float dt=Math.Min(seconds,.05f);wait=Math.Max(0,wait-dt);repath=Math.Max(0,repath-dt);
            float homePlayer=Distance(homeX,homeY,playerX,playerY),near=Distance(X,Y,playerX,playerY);
            if(State==Mode.Chase && (homePlayer>leash || Distance(X,Y,homeX,homeY)>leash-10))
            {State=Mode.Return;route.Clear();repath=0;}
            if(State!=Mode.Return && homePlayer<=leash-24 && near<=aggro)
            {if(State!=Mode.Chase){State=Mode.Chase;route.Clear();repath=0;}}
            if(State==Mode.Return && Distance(X,Y,homeX,homeY)<12)
            {State=Mode.Rest;route.Clear();wait=2;}
            if(State==Mode.Chase && repath==0)
            {Plan(playerX,playerY,leash);repath=.55f;}
            else if(State==Mode.Return && repath==0)
            {Plan(homeX,homeY,leash);repath=1.2f;}
            else if((State==Mode.Rest||State==Mode.Patrol)&&route.Count==0&&wait==0)
            {
                State=Mode.Patrol;
                for(int n=0;n<5;n++)
                {double a=random.NextDouble()*Math.PI*2;float radius=(float)Math.Sqrt(random.NextDouble())*roam;
                    if(Plan(homeX+(float)Math.Cos(a)*radius,homeY+(float)Math.Sin(a)*radius,roam))break;}
                if(route.Count==0){State=Mode.Rest;wait=2.3f;}
            }
            if(route.Count==0||next>=route.Count||State==Mode.Chase&&near<=65)return 0;
            var target=route[next];float distance=Distance(X,Y,target.x,target.y);
            if(distance<.05f){Next();return 0;}
            float step=Math.Min(distance,speed*(State==Mode.Chase?1.35f:1)*dt);
            float x=X+(target.x-X)*step/distance,y=Y+(target.y-Y)*step/distance;
            if(Distance(homeX,homeY,x,y)>leash || !clear(X,Y,x,y)){route.Clear();wait=1;repath=.6f;return 0;}
            if(Math.Abs(x-X)>.002f)Facing=x>X?1:-1;X=x;Y=y;
            if(step>=distance-.001f)Next();return step;
        }
        public bool ResumeAt(float x,float y)
        {
            if(float.IsNaN(x)||float.IsNaN(y)||float.IsInfinity(x)||float.IsInfinity(y)||!clear(x,y,x,y))return false;
            X=x;Y=y;route.Clear();next=0;State=Mode.Return;repath=0;wait=1;
            return true;
        }
        bool Plan(float x,float y,float radius)
        {
            var points=find(X,Y,x,y);if(points==null||points.Count==0)return false;
            var end=points[points.Count-1];if(Distance(end.x,end.y,x,y)>1)return false;
            float ax=X,ay=Y;
            foreach(var p in points)
            {if(Distance(homeX,homeY,p.x,p.y)>radius||!clear(ax,ay,p.x,p.y))return false;ax=p.x;ay=p.y;}
            route.Clear();route.AddRange(points);next=0;return true;
        }
        void Next(){if(++next>=route.Count){route.Clear();next=0;if(State==Mode.Patrol){State=Mode.Rest;wait=1.4f+(float)random.NextDouble()*2;}}}
        static float Distance(float x,float y,float bx,float by){float dx=x-bx,dy=y-by;return(float)Math.Sqrt(dx*dx+dy*dy);}
    }
}
