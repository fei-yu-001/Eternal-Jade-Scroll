using System;
using System.Collections.Generic;
using System.Linq;
namespace TwelveJade.Core
{
    [Serializable]
    public sealed class AtlasExplorationState
    {
        public int version=1;
        public int[] cells=Array.Empty<int>();
        public AtlasExplorationState Copy()=>new AtlasExplorationState{version=version,cells=cells==null?null:(int[])cells.Clone()};
    }
    public static class AtlasExploration
    {
        public const int Columns=32,Rows=40,CellCount=Columns*Rows;
        public const float SightRadius=480;
        // Unity may materialize a missing nested object with zeroed fields. Treat only that empty shape as unknown terrain.
        public static bool IsValid(AtlasExplorationState state)=>state==null||
            (state.version==0&&(state.cells==null||state.cells.Length==0))||
            (state.version==1&&state.cells!=null&&state.cells.Length<=CellCount&&state.cells.All(i=>i>=0&&i<CellCount)&&state.cells.Distinct().Count()==state.cells.Length);
        static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        public static int Cell(float x,float y,float width,float height)
        {
            if(!Finite(x)||!Finite(y)||!Finite(width)||!Finite(height)||width<=0||height<=0||x<0||y<0||x>width||y>height)return -1;
            return Math.Min(Rows-1,(int)(y/height*Rows))*Columns+Math.Min(Columns-1,(int)(x/width*Columns));
        }
        public static bool Known(SaveData save,float x,float y,float width,float height)
        {int cell=Cell(x,y,width,height);return cell>=0&&IsValid(save?.atlasExploration)&&save?.atlasExploration?.cells?.Contains(cell)==true;}
        // Caller supplies an actual accepted world foot, or an earned reach event. Merely opening the atlas reveals nothing.
        public static bool Reveal(SaveData save,float x,float y,float width,float height,float radius=SightRadius)
        {
            if(save==null||!IsValid(save.atlasExploration)||Cell(x,y,width,height)<0||!Finite(radius)||radius<0||radius>1600)return false;
            var cells=new HashSet<int>(save.atlasExploration?.cells??Array.Empty<int>());int before=cells.Count;
            for(int row=0;row<Rows;row++)for(int col=0;col<Columns;col++)
            {
                float left=col*width/Columns,right=(col+1)*width/Columns,top=row*height/Rows,bottom=(row+1)*height/Rows;
                float dx=Math.Max(left-x,Math.Max(0,x-right)),dy=Math.Max(top-y,Math.Max(0,y-bottom));
                if(dx*dx+dy*dy<=radius*radius)cells.Add(row*Columns+col);
            }
            if(cells.Count==before)return false;
            save.atlasExploration=new AtlasExplorationState{cells=cells.OrderBy(i=>i).ToArray()};return true;
        }
        public static bool RevealEarnedEvents(SaveData save,TownMap map,QuestTable quests)
        {
            if(save==null||map==null||quests==null)return false;bool changed=false;
            foreach(var quest in quests.Quests)foreach(var objective in quest.Objectives)
            {
                if(objective.Type!="reach"||
                    (QuestLedger.StatusOf(save,quest.Id)!=QuestLedger.StatusCompleted&&QuestLedger.Progress(save,quest,objective.Id)<Math.Max(1,objective.Count)))continue;
                var mark=map.Landmarks.FirstOrDefault(m=>m.Id==objective.Target);
                if(mark!=null)changed|=Reveal(save,mark.X,mark.Y,map.WorldWidth,map.WorldHeight);
            }
            return changed;
        }
        public static bool Commit(SaveData save,Func<bool> reveal,Action<SaveData> write)
        {
            if(save==null||reveal==null||write==null)throw new ArgumentNullException();
            var old=save.atlasExploration?.Copy();
            try{bool changed=reveal();if(changed)write(save);return changed;}
            catch{save.atlasExploration=old;throw;}
        }
    }
}
