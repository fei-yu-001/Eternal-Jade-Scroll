using System;
namespace TwelveJade.Core
{
    // Same world plane as movement. Display fitting never mutates or snaps a navigation target.
    public sealed class TownAtlasProjection
    {
        public readonly float Scale, OffsetX, OffsetY;
        public TownAtlasProjection(float worldWidth,float worldHeight,float viewWidth,float viewHeight)
        {
            if(!Valid(worldWidth)||!Valid(worldHeight)||!Valid(viewWidth)||!Valid(viewHeight))throw new ArgumentOutOfRangeException();
            Scale=Math.Min(viewWidth/worldWidth,viewHeight/worldHeight);
            OffsetX=(viewWidth-worldWidth*Scale)*.5f;OffsetY=(viewHeight-worldHeight*Scale)*.5f;
        }
        static bool Valid(float x)=>x>0&&!float.IsInfinity(x)&&!float.IsNaN(x);
        public (float x,float y) Project(float x,float y)=>(OffsetX+x*Scale,OffsetY+y*Scale);
        public (float x,float y) World(float x,float y)=>((x-OffsetX)/Scale,(y-OffsetY)/Scale);
    }
}
