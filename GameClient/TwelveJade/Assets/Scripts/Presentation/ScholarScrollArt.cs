using UnityEngine;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    public static class ScholarScrollArt
    {
        public static Image Horizontal(UiKit ui,Transform parent,string name,float x,float y,float width,float height)
        {
            var root=ui.Rect(parent,name,x,y,width,height);
            var turned=ui.Rect(root,name+" woven cross-mount",0,0,height,width);
            turned.anchorMin=turned.anchorMax=new Vector2(.5f,.5f);turned.pivot=new Vector2(.5f,.5f);
            turned.anchoredPosition=Vector2.zero;turned.localRotation=Quaternion.Euler(0,0,90);
            var cloth=MemoVisualAssets.Art(turned,name+" silk","scroll-body");cloth.raycastTarget=true;
            cloth.color=Color.white;
            foreach(float edge in new[]{0f,width-40})
            {
                var rod=ui.Rect(root,name+" roller",edge,-12,40,height+24);
                var ink=MemoVisualAssets.Art(rod,name+" sandalwood","scroll-header");ink.preserveAspect=false;
                ink.rectTransform.anchorMin=ink.rectTransform.anchorMax=new Vector2(.5f,.5f);ink.rectTransform.pivot=new Vector2(.5f,.5f);
                ink.rectTransform.sizeDelta=new Vector2(height+24,40);ink.rectTransform.anchoredPosition=Vector2.zero;
                ink.rectTransform.localRotation=Quaternion.Euler(0,0,90);
            }
            return cloth;
        }
        public static RawImage WritingSilk(UiKit ui,Transform parent,string name,float x,float y,float width,float height)
        {
            var paper=ui.Rect(parent,name,x,y,width,height).gameObject.AddComponent<RawImage>();
            paper.texture=ApprovedUiArt.Load("nov8/moon-xuan-paper");paper.uvRect=new Rect(.19f,.22f,.62f,.56f);
            paper.raycastTarget=false;return paper;
        }
    }
}
