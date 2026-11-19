using System;
using System.Collections.Generic;

namespace TwelveJade.Core
{
    /// <summary>点击寻路使用真实通行判定；32单位基础格加几何窄缝采样，避免漏掉台沿与建筑间通路。</summary>
    public static class TownNavigator
    {
        const float Cell=32f;
        sealed class Node { public int Id; public float Score; }
        static void Push(List<Node> heap,Node node)
        {
            heap.Add(node);int i=heap.Count-1;
            while(i>0){int p=(i-1)/2;if(heap[p].Score<=node.Score)break;heap[i]=heap[p];i=p;}heap[i]=node;
        }
        static Node Pop(List<Node> heap)
        {
            var result=heap[0];var last=heap[heap.Count-1];heap.RemoveAt(heap.Count-1);
            if(heap.Count==0)return result;
            int i=0;while(i*2+1<heap.Count){int c=i*2+1;if(c+1<heap.Count && heap[c+1].Score<heap[c].Score)c++;if(last.Score<=heap[c].Score)break;heap[i]=heap[c];i=c;}heap[i]=last;return result;
        }
        static List<float> Coordinates(TownMap map,bool vertical,float start,float target,bool includeBlockedRects)
        {
            float extent=vertical?map.WorldHeight:map.WorldWidth;
            var values=new SortedSet<float>{0,extent,start,target};
            for(float v=Cell;v<extent;v+=Cell)values.Add(v);
            var boundaries=new SortedSet<float>{0,extent};
            foreach(float v in map.NavigationBoundaries(vertical,includeBlockedRects))
                if(v>=0 && v<=extent)boundaries.Add(v);
            float previous=0;
            foreach(float v in boundaries)
            {
                // A legal open strip narrower than a cell can fall entirely between regular grid rows.
                // Its midpoint is stable for any positive width; sampling an occupied boundary is unnecessary.
                if(v>previous && v-previous<=Cell)values.Add((previous+v)*.5f);
                previous=v;
            }
            return new List<float>(values);
        }
        public static List<(float x,float y)> FindPath(TownMap map,float sx,float sy,float tx,float ty) =>
            FindPath(map,sx,sy,tx,ty,null);
        /// <summary>excludeNpcId 只跳过查询者自身那一个 NPC；所有起终点/邻边/直线合并共用同一上下文。</summary>
        public static List<(float x,float y)> FindPath(TownMap map,float sx,float sy,float tx,float ty,string excludeNpcId)
        {
            if(!map.CanStand(sx,sy,excludeNpcId)||!map.CanStand(tx,ty,excludeNpcId))return null;
            if(map.CanTraverse(sx,sy,tx,ty,excludeNpcId))return new List<(float,float)>{(tx,ty)};
            // Raster terrain contributes thousands of nearly coincident boundaries. Crossing their
            // x/y coordinates can exhaust the search before a nearby bridge is reached. First refine
            // only elevation and placed-object gaps, while still testing ALL terrain collisions.
            // Keep the original full refinement as a fallback for narrow gaps in blocked terrain.
            int roadBudget=8000;
            var road=RoadRoute(map,sx,sy,tx,ty,excludeNpcId,ref roadBudget);
            if(road!=null)return road;
            int remaining=32000+roadBudget,coarseBudget=remaining/2;
            var coarse=SearchBudget(map,sx,sy,tx,ty,excludeNpcId,false,ref coarseBudget);
            if(coarse!=null)return coarse;
            int fineBudget=remaining-remaining/2;
            return SearchBudget(map,sx,sy,tx,ty,excludeNpcId,true,ref fineBudget);
        }
        // The configured main road is a sparse route hint, never a collision exemption.
        // Its local searches share 8k expansions; all fallbacks remain within the prior 40k total.
        static List<(float x,float y)> RoadRoute(TownMap map,float sx,float sy,float tx,float ty,string excludeNpcId,ref int budget)
        {
            if(map.Elevation.IsFlat || (tx-sx)*(tx-sx)+(ty-sy)*(ty-sy)<1600f*1600f)return null;
            var points=new List<(float x,float y)>();
            float direction=Math.Sign(ty-sy);
            for(float y=sy;direction*(ty-y)>0;y+=direction*256)
                points.Add((map.Elevation.RoadX(y),y));
            points.Add((map.Elevation.RoadX(ty),ty));points.Add((tx,ty));
            var result=new List<(float x,float y)>();float ax=sx,ay=sy;
            foreach(var point in points)
            {
                // An occupied road hint is not a destination. Continue to the next hint;
                // the connecting segment must still pass the same traversal or bounded search.
                if(!map.CanStand(point.x,point.y,excludeNpcId))continue;
                if(map.CanTraverse(ax,ay,point.x,point.y,excludeNpcId))result.Add(point);
                else
                {
                    var leg=SearchBudget(map,ax,ay,point.x,point.y,excludeNpcId,false,ref budget);
                    if(leg==null)return null;result.AddRange(leg);
                }
                ax=point.x;ay=point.y;
            }
            return Simplify(map,sx,sy,result,excludeNpcId);
        }
        static List<(float x,float y)> Search(TownMap map,float sx,float sy,float tx,float ty,string excludeNpcId,bool includeBlockedRects)
        {
            int budget=20000;
            return SearchBudget(map,sx,sy,tx,ty,excludeNpcId,includeBlockedRects,ref budget);
        }
        static List<(float x,float y)> SearchBudget(TownMap map,float sx,float sy,float tx,float ty,string excludeNpcId,bool includeBlockedRects,ref int budget)
        {
            var xs=Coordinates(map,false,sx,tx,includeBlockedRects);var ys=Coordinates(map,true,sy,ty,includeBlockedRects);
            int cols=xs.Count,rows=ys.Count;
            var heap=new List<Node>();var cost=new Dictionary<int,float>();var prev=new Dictionary<int,int>();var closed=new HashSet<int>();
            Func<int,(float x,float y)> point=id=>(xs[id%cols],ys[id/cols]);
            Func<float,float,float> heuristic=(x,y)=>(float)Math.Sqrt((tx-x)*(tx-x)+(ty-y)*(ty-y));
            int startId=ys.BinarySearch(sy)*cols+xs.BinarySearch(sx);
            cost[startId]=0;prev[startId]=-1;Push(heap,new Node{Id=startId,Score=heuristic(sx,sy)});
            int found=-1;
            while(heap.Count>0 && budget>0)
            {
                int id=Pop(heap).Id;if(!closed.Add(id))continue;
                budget--;var p=point(id);
                if(heuristic(p.x,p.y)<Cell*2 && map.CanTraverse(p.x,p.y,tx,ty,excludeNpcId)){found=id;break;}
                for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                {
                    if(dx==0&&dy==0)continue;int x=id%cols+dx,y=id/cols+dy;if(x<0||y<0||x>=cols||y>=rows)continue;
                    int next=y*cols+x;if(closed.Contains(next))continue;
                    var q=point(next);float dxWorld=q.x-p.x,dyWorld=q.y-p.y;
                    float g=cost[id]+(float)Math.Sqrt(dxWorld*dxWorld+dyWorld*dyWorld);
                    if(cost.TryGetValue(next,out var old)&&g>=old)continue;
                    if(!map.CanTraverse(p.x,p.y,q.x,q.y,excludeNpcId))continue;
                    cost[next]=g;prev[next]=id;Push(heap,new Node{Id=next,Score=g+heuristic(q.x,q.y)});
                }
            }
            if(found<0)return null;
            var path=new List<(float x,float y)>{(tx,ty)};
            for(int id=found;id>=0 && id!=startId;id=prev[id])path.Add(point(id));path.Reverse();
            return Simplify(map,sx,sy,path,excludeNpcId);
        }
        static List<(float x,float y)> Simplify(TownMap map,float sx,float sy,List<(float x,float y)> path,string excludeNpcId)
        {
            // 只在实测可通行时合并直线段，绝不在台沿/门墩处切角。
            var result=new List<(float x,float y)>();float ax=sx,ay=sy;int start=0;
            while(start<path.Count)
            {
                int far=start;
                for(int j=start+1;j<path.Count;j++) {if(!map.CanTraverse(ax,ay,path[j].x,path[j].y,excludeNpcId))break;far=j;}
                result.Add(path[far]);ax=path[far].x;ay=path[far].y;start=far+1;
            }
            return result;
        }
    }
}
