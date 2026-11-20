using System;
using System.Linq;
using TMPro;
using TwelveJade.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    public sealed partial class FrontEndController
    {
        readonly System.Random meridianRng=new System.Random();
        RectTransform BeginImmortalLedger(string name)
        {
            if(page!="town"||activeSave==null||WorldDialogueInputBlocked)return null;
            CloseInventory();CloseChapterPanel();CloseModal();CancelTravel();
            var group=content.GetComponent<CanvasGroup>();group.interactable=false;group.blocksRaycasts=false;
            modal=ui.Rect(canvas,name,0,0,1920,1080);modal.gameObject.AddComponent<V2FocusNavigation>();
            var veil=modal.gameObject.AddComponent<Image>();veil.color=new Color(0,0,0,.7f);
            return modal;
        }
        Button LedgerClose(RectTransform face,Action closing=null)
        {
            var r=CultivationLayout.Close;
            return AestheticUi.Jade(ui,face,"Ledger close","收 起",r,()=>{closing?.Invoke();PlayClick();CloseModal();},()=>settings.reduceMotion,null,25);
        }
        void BuildMeridianMasterpiece()
        {
            if(!PrologueRules.CanCultivate(activeSave)){Notify(PrologueRules.Refusal);return;}
            var host=BeginImmortalLedger("Meridian modal");if(host==null)return;
            host.gameObject.AddComponent<MeridianSanctumView>().Build(ui,host,activeSave,()=>settings.reduceMotion,repository.Write,()=>userBag=activeSave.bag,PlayClick,CloseModal);
            ScrollPageArtComposition.Meridian(ui,host);
        }
        ItemStack[] CopyLedgerBag()=>activeSave.bag.Select(s=>s==null?null:new ItemStack(s.id,s.count)).ToArray();
        void BuildArtsMasterpiece()
        {
            if(!PrologueRules.CanCultivate(activeSave)){Notify(PrologueRules.Refusal);return;}
            var host=BeginImmortalLedger("Arts modal");if(host==null)return;
            var view=host.gameObject.AddComponent<ArtsCodexView>();
            view.Build(ui,host,activeSave,Table,repository.Write,()=>settings.reduceMotion,
                ()=>userBag=activeSave.bag,PlayClick,CloseModal);
        }
        ChibiStudyAnimation BuildStudyAnimation(RectTransform face)
        {
            // Its centered pivot is essential: scale animates about the screen center, never a top-left corner.
            var host=ui.Rect(face,"Chibi study animation",660,150,600,780);
            host.pivot=new Vector2(.5f,.5f);host.anchoredPosition=new Vector2(960,-540);
            var group=host.gameObject.AddComponent<CanvasGroup>();group.blocksRaycasts=false;group.interactable=false;
            var body=ui.Rect(host,"Chibi body",70,160,460,460);
            ImmortalUi.Texture(ui,body,"Chibi apprentice sprite",ApprovedUiArt.Load("nov8/chibi-study"),0,0,460,460);
            ImmortalUi.Texture(ui,host,"Study caption jade",ApprovedUiArt.Load("nov7/white-jade-command"),55,610,490,108);
            var bubble=CultivationLayout.Label(ui,host,"Chibi bubble","",new Rect(83,626,434,66),27,1,ImmortalUi.Ink,TextAlignmentOptions.Center);
            var aura=CultivationLayout.Label(ui,host,"Chibi verse","",new Rect(22,713,556,60),22,1,ImmortalUi.Gold,TextAlignmentOptions.Center);
            var glyphs=new RectTransform[6];var glows=new CanvasGroup[6];
            for(int i=0;i<glyphs.Length;i++)
            {
                var text=ImmortalUi.Text(ui,host,"Study flying word "+i,new[]{"道","法","心","静","明","悟"}[i],282,282,40,46,27,ImmortalUi.Gold,true,TextAlignmentOptions.Center);
                glyphs[i]=text.rectTransform;glows[i]=text.gameObject.AddComponent<CanvasGroup>();glows[i].blocksRaycasts=false;
            }
            var animation=host.gameObject.AddComponent<ChibiStudyAnimation>();
            animation.Bind(body,group,bubble,aura,()=>settings.reduceMotion,glyphs,glows);animation.DismissImmediately();return animation;
        }
    }
}
