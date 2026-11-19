using UnityEngine;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    // The scroll is a construction, not a stretched painting: tiled paper, fixed woven edges, independent rollers.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class FairylandScrollArt : RawImage
    {
        bool border;
        public const float PaperTile=192,EdgeWidth=12,RollerHeight=26;
        public void Configure(bool ignored=false){texture=ApprovedUiArt.Load("nov8/moon-xuan-paper");raycastTarget=false;SetVerticesDirty();}
        public static FairylandScrollArt Create(UiKit ui,Transform parent,string name,Rect box,bool ignored=false)
        {
            var root=ui.Rect(parent,name,box.x,box.y,box.width,box.height);
            var art=root.gameObject.AddComponent<FairylandScrollArt>();art.Configure();
            if(box.width>600)
            {
                // Separate, fixed-aspect ink motif; it never inherits changing scroll height.
                var mark=ui.Rect(root,name+" quiet cloud watermark",0,0,700,350);
                mark.anchorMin=mark.anchorMax=mark.pivot=new Vector2(.5f,.5f);mark.anchoredPosition=Vector2.zero;
                var painting=mark.gameObject.AddComponent<RawImage>();painting.texture=ApprovedUiArt.Load("nov8/tianji-scroll-fairyland");
                painting.uvRect=new Rect(.20f,.28f,.60f,.44f);mark.sizeDelta=new Vector2(700,700*(887f*.44f)/(1774f*.60f));
                painting.color=new Color(1,1,1,.12f);painting.raycastTarget=false;
            }
            foreach(bool right in new[]{false,true})
            {
                var trim=ui.Rect(root,name+(right?" east xuan selvedge":" west xuan selvedge"),0,0,EdgeWidth,0);
                trim.anchorMin=new Vector2(right?1:0,0);trim.anchorMax=new Vector2(right?1:0,1);trim.pivot=new Vector2(right?1:0,.5f);trim.anchoredPosition=Vector2.zero;trim.sizeDelta=new Vector2(EdgeWidth,0);
                var fabric=trim.gameObject.AddComponent<FairylandScrollArt>();fabric.border=true;fabric.texture=ApprovedUiArt.Load("nov8/xuan-ink-silk");fabric.raycastTarget=false;
            }
            foreach(bool bottom in new[]{false,true})
            {
                var rod=ui.Rect(root,name+(bottom?" lower jade roller":" upper jade roller"),-10,-8,box.width+20,RollerHeight);
                rod.anchorMin=rod.anchorMax=new Vector2(0,bottom?0:1);rod.pivot=new Vector2(0,.5f);rod.anchoredPosition=new Vector2(-10,bottom?0:0);
                rod.anchorMax=new Vector2(1,bottom?0:1);rod.sizeDelta=new Vector2(20,RollerHeight);
                var image=rod.gameObject.AddComponent<JadeCommandPlateGraphic>();image.texture=ApprovedUiArt.Load("nov8/white-jade-roller");image.raycastTarget=false;
            }
            return art;
        }
        protected override void OnPopulateMesh(VertexHelper v)
        {
            v.Clear();var r=rectTransform.rect;
            for(float y=r.yMin;y<r.yMax;y+=PaperTile)for(float x=r.xMin;x<r.xMax;x+=PaperTile)
            {
                float w=Mathf.Min(PaperTile,r.xMax-x),h=Mathf.Min(PaperTile,r.yMax-y);int n=v.currentVertCount;
                // Only the uninscribed central paper is sampled; each texel keeps a fixed design-pixel density.
                float u0=border?.42f:.24f,v0=.25f,u1=u0+(border?.08f:.5f)*w/PaperTile,v1=v0+.5f*h/PaperTile;
                v.AddVert(new Vector2(x,y),color,new Vector2(u0,v0));v.AddVert(new Vector2(x,y+h),color,new Vector2(u0,v1));
                v.AddVert(new Vector2(x+w,y+h),color,new Vector2(u1,v1));v.AddVert(new Vector2(x+w,y),color,new Vector2(u1,v0));v.AddTriangle(n,n+1,n+2);v.AddTriangle(n,n+2,n+3);
            }
        }
    }
}
