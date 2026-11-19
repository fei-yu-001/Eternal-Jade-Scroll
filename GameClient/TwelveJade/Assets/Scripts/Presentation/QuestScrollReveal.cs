using System.Linq;
using UnityEngine;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    // The paper and glyphs never scale: a mask reveals the sheet as the two rollers part.
    public sealed class QuestScrollReveal : MonoBehaviour
    {
        RectTransform clip,upper,lower;float fullHeight;
        public void Configure(UiKit ui,RectTransform panel)
        {
            fullHeight=panel.rect.height;
            var mount=panel.Find("Chapter fairyland jade scroll") as RectTransform;
            if(mount!=null)
            {
                upper=mount.Find("Chapter fairyland jade scroll upper jade roller") as RectTransform;
                lower=mount.Find("Chapter fairyland jade scroll lower jade roller") as RectTransform;
                foreach(var rod in new[]{upper,lower})if(rod!=null)
                {rod.SetParent(panel,false);rod.anchorMin=rod.anchorMax=new Vector2(0,1);rod.pivot=new Vector2(0,.5f);rod.sizeDelta=new Vector2(panel.rect.width+20,FairylandScrollArt.RollerHeight);}
            }
            var children=panel.Cast<Transform>().Where(t=>t!=upper&&t!=lower).ToArray();
            clip=ui.Rect(panel,"Chapter unrolling mask",0,0,panel.rect.width,fullHeight);
            clip.anchorMin=clip.anchorMax=clip.pivot=new Vector2(.5f,.5f);clip.anchoredPosition=Vector2.zero;
            clip.gameObject.AddComponent<RectMask2D>();
            var sheet=ui.Rect(clip,"Chapter unscaled sheet",0,0,panel.rect.width,fullHeight);
            sheet.anchorMin=sheet.anchorMax=sheet.pivot=new Vector2(.5f,.5f);sheet.anchoredPosition=Vector2.zero;
            foreach(var child in children)child.SetParent(sheet,false);
            if(upper!=null)upper.SetAsLastSibling();if(lower!=null)lower.SetAsLastSibling();
            SetOpenness(0);
        }
        public void SetOpenness(float amount)
        {
            if(clip==null)return;float t=Mathf.SmoothStep(0,1,Mathf.Clamp01(amount));
            clip.sizeDelta=new Vector2(clip.sizeDelta.x,Mathf.Lerp(76,fullHeight,t));
            if(upper!=null)upper.anchoredPosition=new Vector2(-10,-Mathf.Lerp(fullHeight*.5f-13,24,t));
            if(lower!=null)lower.anchoredPosition=new Vector2(-10,-Mathf.Lerp(fullHeight*.5f+13,fullHeight-24,t));
        }
    }
}
