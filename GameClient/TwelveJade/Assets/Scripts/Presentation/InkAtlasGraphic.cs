using TwelveJade.Core;
using UnityEngine;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    [RequireComponent(typeof(RectTransform),typeof(CanvasRenderer))]
    public sealed class InkAtlasGraphic : MaskableGraphic
    {
        System.Func<MapLandmark,bool> visible;System.Collections.Generic.IReadOnlyList<MapLandmark> places;TownMap map;JsonValue terrain;string selected;bool drawTerrain=true, detailed;
        static readonly Color Ink=new Color(.27f,.34f,.29f,.63f),Jade=new Color(.40f,.58f,.47f,.18f),Water=new Color(.44f,.68f,.63f,.66f);
        public void Configure(TownMap town,string json,Vector2 foot,bool proceduralTerrain=true,System.Func<MapLandmark,bool> landmarkVisible=null,System.Collections.Generic.IReadOnlyList<MapLandmark> actualPlaces=null)
        {
            map=town;places=actualPlaces??town.Landmarks;drawTerrain=proceduralTerrain;visible=landmarkVisible;raycastTarget=false;
            if(Json.TryParse(json,out var root,out _))terrain=root["terrainShape"];
            SetVerticesDirty();
        }
        public Vector2 Project(float x,float y)
        {
            var fit=new TownAtlasProjection(map.WorldWidth,map.WorldHeight,rectTransform.rect.width,rectTransform.rect.height);
            var p=fit.Project(x,y);return new Vector2(p.x,p.y);
        }
        public void Select(string id){selected=id;SetVerticesDirty();}
        public void HighlightDetails(bool value){detailed=value;SetVerticesDirty();}
        Vector2 Local(Vector2 p)=>new Vector2(rectTransform.rect.xMin+p.x,rectTransform.rect.yMax-p.y);
        void Line(VertexHelper vh,Vector2 a,Vector2 b,float width,Color ink)
        {
            var delta=b-a;if(delta.sqrMagnitude<.00001f)return;
            var normal=new Vector2(-delta.y,delta.x).normalized*width*.5f;int v=vh.currentVertCount;
            vh.AddVert(Local(a-normal),ink*color,Vector2.zero);vh.AddVert(Local(a+normal),ink*color,Vector2.zero);
            vh.AddVert(Local(b+normal),ink*color,Vector2.zero);vh.AddVert(Local(b-normal),ink*color,Vector2.zero);
            vh.AddTriangle(v,v+1,v+2);vh.AddTriangle(v,v+2,v+3);
        }
        void Stroke(VertexHelper vh,Vector2 a,Vector2 b,float width,Color ink)
        {
            var wash=ink;wash.a*=.18f;Line(vh,a,b,width+3,wash);Line(vh,a,b,width,ink);
        }
        void Path(VertexHelper vh,System.Func<float,Vector2> point,int steps,float width,Color ink)
        {var last=point(0);for(int i=1;i<=steps;i++){var next=point((float)i/steps);Stroke(vh,last,next,width,ink);last=next;}}
        void Roof(VertexHelper vh,Vector2 p,Color ink)
        {
            Stroke(vh,p+new Vector2(-9,1),p+new Vector2(0,-6),1.5f,ink);Stroke(vh,p+new Vector2(0,-6),p+new Vector2(9,1),1.5f,ink);
            Line(vh,p+new Vector2(-6,2),p+new Vector2(6,2),1,ink);Line(vh,p+new Vector2(-4,3),p+new Vector2(-4,7),1,ink);
            Line(vh,p+new Vector2(4,3),p+new Vector2(4,7),1,ink);
        }
        void Mountain(VertexHelper vh,Vector2 foot,float half,float height,bool near)
        {
            var fill=near?new Color(.43f,.57f,.43f,.28f):new Color(.55f,.65f,.58f,.17f);
            var edge=near?new Color(.30f,.42f,.34f,.51f):new Color(.47f,.57f,.49f,.28f);
            var last=foot+new Vector2(-half,0);int origin=vh.currentVertCount;
            vh.AddVert(Local(foot+new Vector2(0,12)),new Color(fill.r,fill.g,fill.b,0)*color,Vector2.zero);
            for(int i=0;i<=24;i++)
            {
                float t=i/24f;var point=foot+new Vector2(Mathf.Lerp(-half,half,t),-Mathf.Pow(Mathf.Sin(t*Mathf.PI),1.3f)*height*(1+.09f*Mathf.Sin(t*19)));
                vh.AddVert(Local(point),fill*color,Vector2.zero);
            }
            for(int i=0;i<24;i++)vh.AddTriangle(origin,origin+i+1,origin+i+2);
            for(int i=1;i<=24;i++)
            {
                float t=i/24f;var point=foot+new Vector2(Mathf.Lerp(-half,half,t),-Mathf.Pow(Mathf.Sin(t*Mathf.PI),1.3f)*height*(1+.09f*Mathf.Sin(t*19)));
                Stroke(vh,last,point,1,edge);last=point;
            }
            for(int i=0;i<4;i++)
            {
                var top=foot+new Vector2(-half*.12f+i*half*.18f,-height*(.82f-i*.13f));
                Stroke(vh,top,top+new Vector2(half*.18f,height*.48f),.7f,new Color(edge.r,edge.g,edge.b,edge.a*.6f));
            }
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();if(map==null)return;
            if(drawTerrain&&terrain!=null)PaintTerrain(vh);
            foreach(var mark in places)
            {
                if(visible!=null&&!visible(mark))continue;
                var point=Project(mark.X,mark.Y);bool picked=mark.Id==selected;
                Roof(vh,point,picked?new Color(.10f,.43f,.27f,1):detailed?new Color(.25f,.36f,.23f,.95f):Ink);
                if(!picked&&!detailed)continue;
                float radius=picked?19:14;
                var tint=picked?new Color(.90f,.65f,.20f,.98f):new Color(.73f,.55f,.23f,.62f);
                for(int i=0;i<36;i++)
                {
                    float a=i*Mathf.PI/18,b=(i+1)*Mathf.PI/18;
                    Line(vh,point+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius,
                        point+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*radius,picked?2.4f:1.2f,tint);
                }
            }
            // Player position is the screen-size green seal owned by WideInkAtlasView, never a second yellow dot.
        }
        void PaintTerrain(VertexHelper vh)
        {
            float scale=new TownAtlasProjection(map.WorldWidth,map.WorldHeight,rectTransform.rect.width,rectTransform.rect.height).Scale;
            // Mountain silhouettes follow the configured northern mountain boundary, not invented roads.
            var mountains=terrain["mountain"];
            System.Func<float,float> ridge=x=>{float y=mountains["baseY"].AsFloat(1400);foreach(var w in mountains["wave"].Items)y+=w["amp"].AsFloat(0)*Mathf.Sin(x/Mathf.Max(1,w["period"].AsFloat(0))+w["phase"].AsFloat(0));return y;};
            // Layered mountain pictograms inhabit the actual configured mountain area.
            // They describe the northern landscape; only TownMap answers walkability.
            for(int layer=0;layer<2;layer++)for(int i=0;i<8;i++)
            {
                float x=(i+.45f+layer*.28f)*map.WorldWidth/8.4f;
                var foot=Project(x,Mathf.Max(300,ridge(x)-layer*370));
                Mountain(vh,foot,61+15*Mathf.Sin(i*2.2f+layer),62+21*Mathf.Cos(i*1.4f+layer),layer==0);
            }
            var roadInk=new Color(.55f,.43f,.27f,.46f);
            Path(vh,t=>{float y=t*map.WorldHeight;return Project(map.Elevation.RoadX(y),y);},160,4,roadInk);
            foreach(var road in terrain["crossRoads"].Items)
                Path(vh,t=>{float x=t*map.WorldWidth;return Project(x,road["baseY"].AsFloat(0)+road["amp"].AsFloat(0)*Mathf.Sin(x/road["period"].AsFloat(800)));},100,1.6f,roadInk);
            foreach(var road in terrain["sideRoads"].Items)
                Path(vh,t=>{float y=Mathf.Lerp(road["y0"].AsFloat(0),road["y1"].AsFloat(map.WorldHeight),t);return Project(road["baseX"].AsFloat(0)+road["amp"].AsFloat(0)*Mathf.Sin(y/road["periodY"].AsFloat(1500)+road["phase"].AsFloat(0)),y);},100,1.5f,roadInk);
            var mountainPath=terrain["mountainPath"];
            Path(vh,t=>{float y=Mathf.Lerp(mountainPath["y0"].AsFloat(0),mountainPath["y1"].AsFloat(2500),t);return Project(mountainPath["baseX"].AsFloat(3796)+mountainPath["amp"].AsFloat(60)*Mathf.Sin(y/mountainPath["periodY"].AsFloat(600)),y);},70,1.2f,roadInk);
            float half=terrain["river"]["waterHalf"].AsFloat(125);
            Path(vh,t=>{float x=t*map.WorldWidth;return Project(x,map.Elevation.RiverY(x));},180,half*2*scale,Water);
            for(int edge=-1;edge<=1;edge+=2)
            {int side=edge;Path(vh,t=>{float x=t*map.WorldWidth;return Project(x,map.Elevation.RiverY(x)+half*side);},180,1,new Color(.28f,.48f,.43f,.45f));}
            foreach(var bridge in terrain["bridges"].Items)
            {
                float x=bridge["x"].AsFloat(0),y=map.Elevation.RiverY(x),w=bridge["half"].AsFloat(0)*2*scale;
                var a=Project(x,y-half-45);var b=Project(x,y+half+45);
                Line(vh,a,b,w,new Color(.83f,.78f,.62f,1));
                Line(vh,a+Vector2.left*w*.5f,b+Vector2.left*w*.5f,1,Ink);Line(vh,a+Vector2.right*w*.5f,b+Vector2.right*w*.5f,1,Ink);
            }
        }
        protected override void OnDestroy(){map=null;terrain=null;visible=null;base.OnDestroy();}
    }
}
