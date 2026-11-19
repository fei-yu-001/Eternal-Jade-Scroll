using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // Borrows existing painted silk/jade texels; owns no Texture, Sprite or Material.
    // Local to menu/memo: shared UiKit surfaces and catalogues are intentionally untouched.
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class MenuMemoSurfaceGraphic : MaskableGraphic
    {
        public enum Surface { Dossier, ScrollSilk, Command, Choice, Input, Utility, Notice }
        Surface surface;
        Texture2D artwork;
        Rect textureRegion;
        public Surface Style => surface;
        public override Texture mainTexture => artwork != null ? artwork : Texture2D.whiteTexture;
        public void Configure(Surface value, bool hit = false)
        {
            surface=value;raycastTarget=hit;color=Color.white;
            string path=value==Surface.ScrollSilk ? "Art/UI/Nov6/scroll-body" :
                (value==Surface.Command||value==Surface.Choice) ? "Art/UI/Nov8/moon-jade-command" : "Art/UI/Nov6/paper-panel";
            artwork=Resources.Load<Texture2D>(path);
            textureRegion=value==Surface.ScrollSilk ? new Rect(.12f,.12f,.76f,.76f) :
                (value==Surface.Command||value==Surface.Choice) ? new Rect(.25f,.28f,.50f,.44f) : new Rect(.17f,.20f,.66f,.60f);
            SetAllDirty();
        }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();var r=rectTransform.rect;if(r.width<2||r.height<2)return;
            if(surface==Surface.Utility)
            {
                // A small caption slip only; the icon above it is truly transparent.
                var slip=new Rect(r.xMin+4,r.yMin+4,r.width-8,34);
                Paint(mesh,slip,5,new Color(.91f,.92f,.85f,.91f),new Color(.86f,.88f,.80f,.92f));return;
            }
            Color left,right;float cut=4;
            if(surface==Surface.Dossier){left=new Color(.91f,.94f,.90f,.57f);right=new Color(.94f,.96f,.91f,.84f);cut=9;}
            else if(surface==Surface.ScrollSilk){left=new Color(.97f,.96f,.86f,.98f);right=new Color(.95f,.94f,.85f,.98f);cut=1;}
            else if(surface==Surface.Command){left=new Color(.84f,.89f,.78f,.98f);right=new Color(.95f,.95f,.84f,.98f);cut=9;}
            else if(surface==Surface.Choice){left=new Color(.82f,.87f,.77f,.94f);right=new Color(.94f,.94f,.83f,.98f);cut=5;}
            else if(surface==Surface.Input){left=new Color(.91f,.95f,.87f,.38f);right=new Color(.95f,.97f,.91f,.59f);cut=3;}
            else {left=new Color(.91f,.94f,.86f,.94f);right=new Color(.96f,.96f,.87f,.97f);cut=7;}
            Paint(mesh,r,cut,left,right);
        }
        void Paint(VertexHelper mesh,Rect r,float cut,Color left,Color right)
        {
            int first=mesh.currentVertCount;Vertex(mesh,r.center,r,Color.Lerp(left,right,.5f));
            for(int i=0;i<8;i++)
            {
                var point=MemoMountGraphic.Corner(r,cut,i);
                Vertex(mesh,point,r,Color.Lerp(left,right,Mathf.InverseLerp(r.xMin,r.xMax,point.x)));
            }
            for(int i=0;i<8;i++)mesh.AddTriangle(first,first+1+i,first+1+(i+1)%8);
        }
        void Vertex(VertexHelper mesh,Vector2 point,Rect r,Color tint)
        {
            var uv=new Vector2(textureRegion.x+Mathf.InverseLerp(r.xMin,r.xMax,point.x)*textureRegion.width,
                textureRegion.y+Mathf.InverseLerp(r.yMin,r.yMax,point.y)*textureRegion.height);
            mesh.AddVert(point,tint*color,uv);
        }
        protected override void OnDestroy(){artwork=null;base.OnDestroy();}
    }
}
