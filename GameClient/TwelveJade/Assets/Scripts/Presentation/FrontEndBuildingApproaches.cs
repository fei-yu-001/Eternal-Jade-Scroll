using System;
using TwelveJade.Core;
using UnityEngine;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    public sealed partial class FrontEndController
    {
        void BuildBuildingApproaches()
        {
            if(worldClickFeedback==null)return;
            var config=Resources.Load<TextAsset>("Config/building-plots");
            if(config==null||!Json.TryParse(config.text,out var root,out _))return;
            var texture=Resources.Load<Texture2D>("Art/Textures/GroundQuality20261107/stone-road");
            if(texture==null)return;
            if(!Json.TryParse(townJson,out var townData,out _))return;
            var shape=townData["terrainShape"];
            foreach(var p in root["plots"].Items)
            {
                if(p["kind"].AsString("")!="building")continue;
                var kind=p["buildingClass"].AsString("");if(kind=="gateway"||kind=="gate-landmark")continue;
                float x=p["footX"].AsFloat(0),y=p["footY"].AsFloat(0)+p["groundFootOffset"].AsFloat(p["height"].AsFloat(0));
                float ground=town.Elevation.ProjectY(x,y),height=p["height"].AsFloat(200),buildingWidth=p["width"].AsFloat(200);
                float left=-buildingWidth*.42f,right=buildingWidth*.42f,back=p["groundFootOffset"].AsFloat(height)-height*.18f;
                bool measured=false;
                foreach(var support in p["collisionRects"].Items)
                {
                    if(!measured){left=support["x0"].AsFloat(left);right=support["x1"].AsFloat(right);back=support["y0"].AsFloat(back);measured=true;}
                    else {left=Mathf.Min(left,support["x0"].AsFloat(left));right=Mathf.Max(right,support["x1"].AsFloat(right));back=Mathf.Min(back,support["y0"].AsFloat(back));}
                }
                // A full opaque foundation covers the existing road/soil seam beneath walls and stairs.
                // The feather lies outside the measured supports; collision and door routes stay where they are.
                float rear=ground-(p["groundFootOffset"].AsFloat(height)-back)-34;
                PavingPatch("Foundation "+p["id"].AsString(""),x+left-38,rear,right-left+76,ground+74-rear,texture,true);
                // A doorstep already on a square/alley needs no extra stripe across the courtyard.
                // Connect only a short real gap, considering cross streets as well as the main roads.
                var entrance=new Vector2(x,y+40);var near=NearestPaving(shape,entrance);
                if(Vector2.Distance(entrance,near)>16 && Vector2.Distance(entrance,near)<240)
                {
                    float targetGround=town.Elevation.ProjectY(near.x,near.y);
                    PavingPatch("Approach "+p["id"].AsString(""),Mathf.Min(x,near.x)-36,ground+4,Mathf.Abs(near.x-x)+72,72,texture);
                    if(Mathf.Abs(targetGround-ground-40)>16)
                        PavingPatch("Approach end "+p["id"].AsString(""),near.x-36,Mathf.Min(ground+40,targetGround)-36,72,Mathf.Abs(targetGround-ground-40)+72,texture);
                }
            }
        }
        static Vector2 NearestPaving(JsonValue shape,Vector2 foot)
        {
            var nearest=foot;float best=float.MaxValue;
            Action<Vector2> consider=point=>{float d=(point-foot).sqrMagnitude;if(d<best){best=d;nearest=point;}};
            Action<float,float,float,float> rectangle=(left,top,right,bottom)=>
                consider(new Vector2(Mathf.Clamp(foot.x,left,right),Mathf.Clamp(foot.y,top,bottom)));
            var roads=new System.Collections.Generic.List<JsonValue>{shape["mainRoad"]};
            roads.AddRange(shape["sideRoads"].Items);
            foreach(var road in roads)
            {
                float c=road["baseX"].AsFloat(0)+road["amp"].AsFloat(0)*Mathf.Sin(foot.y/Mathf.Max(1,road["periodY"].AsFloat(1))+road["phase"].AsFloat(0));
                float half=Mathf.Max(1,road["half"].AsFloat(0)-20);
                consider(new Vector2(Mathf.Clamp(foot.x,c-half,c+half),foot.y));
            }
            foreach(var road in shape["crossRoads"].Items)
            {
                float c=road["baseY"].AsFloat(0)+road["amp"].AsFloat(0)*Mathf.Sin(foot.x/Mathf.Max(1,road["period"].AsFloat(1)));
                float half=Mathf.Max(1,road["half"].AsFloat(0)-20);
                consider(new Vector2(foot.x,Mathf.Clamp(foot.y,c-half,c+half)));
            }
            foreach(var alley in shape["alleys"].Items)
            {
                float half=Mathf.Max(1,alley["half"].AsFloat(0)-20);
                if(alley["axis"].AsString("")=="ns")
                    rectangle(alley["x"].AsFloat(0)-half,alley["y0"].AsFloat(0),alley["x"].AsFloat(0)+half,alley["y1"].AsFloat(0));
                else rectangle(alley["x0"].AsFloat(0),alley["y"].AsFloat(0)-half,alley["x1"].AsFloat(0),alley["y"].AsFloat(0)+half);
            }
            foreach(var area in shape["pavingRects"].Items)
                rectangle(area["x0"].AsFloat(0),area["y0"].AsFloat(0),area["x1"].AsFloat(0),area["y1"].AsFloat(0));
            return nearest;
        }
        void PavingPatch(string name,float x,float y,float w,float h,Texture2D texture,bool foundation=false)
        {if(w<1||h<1)return;var rect=ui.Rect(worldClickFeedback.transform,name,x*worldScale,y*worldScale,w*worldScale,h*worldScale);
            rect.gameObject.AddComponent<WorldPavingPatch>().Configure(texture,new Rect(x,y,w,h),foundation);
            rect.SetAsFirstSibling();}
    }
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class WorldPavingPatch:MaskableGraphic
    {
        Texture2D tile;Rect world;bool foundation;
        public Rect WorldBounds=>world;
        public bool IsFoundation=>foundation;
        public override Texture mainTexture=>tile!=null?tile:Texture2D.whiteTexture;
        public void Configure(Texture2D source,Rect bounds,bool buildingBase){tile=source;world=bounds;foundation=buildingBase;raycastTarget=false;SetAllDirty();}
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();var r=rectTransform.rect;var c=r.center;const int n=12;
            var stone=new Color(.93f,.94f,.91f,foundation?1f:.94f);
            vh.AddVert(c,stone,Uv(c,r));
            for(int i=0;i<n;i++)
            {
                float angle=(i+.5f)*Mathf.PI*2/n;float cs=Mathf.Cos(angle),sn=Mathf.Sin(angle);
                // Superellipse follows a worn paved apron, with a continuous transparent soil-facing rim.
                float x=Mathf.Sign(cs)*Mathf.Pow(Mathf.Abs(cs),.30f),y=Mathf.Sign(sn)*Mathf.Pow(Mathf.Abs(sn),.30f);
                var outer=c+new Vector2(x*r.width*.5f,y*r.height*.5f);
                var inner=c+new Vector2(x*Mathf.Max(1,r.width*.5f-15),y*Mathf.Max(1,r.height*.5f-15));
                vh.AddVert(inner,stone,Uv(inner,r));vh.AddVert(outer,new Color(.93f,.94f,.91f,0),Uv(outer,r));
            }
            for(int i=0;i<n;i++){int a=1+i*2,b=1+((i+1)%n)*2;vh.AddTriangle(0,a,b);vh.AddTriangle(a,a+1,b);vh.AddTriangle(a+1,b+1,b);}
        }
        Vector2 Uv(Vector2 p,Rect r)
        {float uw=Mathf.Min(.95f,world.width/768f),vh=Mathf.Min(.95f,world.height/768f);
            float u=Mathf.Repeat(world.x/768f,1-uw),v=Mathf.Repeat(world.y/768f,1-vh);
            return new Vector2(u+(p.x-r.xMin)/Mathf.Max(1,r.width)*uw,v+(p.y-r.yMin)/Mathf.Max(1,r.height)*vh); }
    }
}
