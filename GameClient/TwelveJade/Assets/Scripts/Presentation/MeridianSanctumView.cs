using System;
using TMPro;
using TwelveJade.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    public sealed class MeridianSanctumView : MonoBehaviour
    {
        UiKit ui;SaveData save;Func<bool> low;Action<SaveData> commit;Action changed,click,close;RectTransform host,main,popup,diagram;
        CanvasGroup mainGroup;TMP_Text summary,result,mainPills;Button enter,mainPill;
        MeridianInnerGraphic flow;bool usePill,resultPending;int selected;string pendingMessage;
        Vector2 diagramOrigin;
        readonly System.Random rng=new System.Random();
        const string InnerArtId="nov8/meridian-inner-vision-doll";
        public void Build(UiKit kit,RectTransform parent,SaveData owner,Func<bool> reduced,Action<SaveData> write,Action refresh,Action sound,Action dismiss)
        {
            ui=kit;host=parent;save=owner;low=reduced;commit=write;changed=refresh;click=sound;close=dismiss;
            main=ui.Rect(host,"Meridian immortal sanctum",0,0,1920,1080);mainGroup=main.gameObject.AddComponent<CanvasGroup>();
            ScholarScrollArt.Horizontal(ui,main,"Meridian cold xuan mounting",40,55,1840,970);
            ScholarScrollArt.WritingSilk(ui,main,"Meridian moon paper",105,116,1710,842);
            ImmortalUi.Texture(ui,main,"Meridian adult immortal painting",ApprovedUiArt.Load("nov8/meridian-daoist-masterpiece"),200,96,916,916);
            var aura=ui.Rect(main,"Meridian breathing golden qi",200,96,916,916).gameObject.AddComponent<MeridianAuraGraphic>();aura.Configure(low);
            AestheticUi.Text(ui,main,"Meridian title","紫 府 · 周 天",new Rect(1238,180,460,80),42,TextAlignmentOptions.Center);
            summary=AestheticUi.Text(ui,main,"Meridian actual summary","",new Rect(1244,292,450,190),28,TextAlignmentOptions.Center);
            mainPills=AestheticUi.Text(ui,main,"Meridian meditation verse","",new Rect(1244,518,450,70),25,TextAlignmentOptions.Center);
            mainPill=AestheticUi.Jade(ui,main,"Meridian select protection pill","选择护丹",new Rect(1288,614,364,62),TogglePill,low,click,25);
            enter=AestheticUi.Jade(ui,main,"Meridian enter inner vision","冲 穴",new Rect(1288,690,364,90),BeginAttempt,low,click,30);
            DaoIdentityView.Roots(ui,main,"Meridian dormant roots",new Rect(1250,778,184,184));
            AestheticUi.Text(ui,main,"Meridian roots unknown","灵根未测",new Rect(1450,830,260,90),23);
            AestheticUi.Jade(ui,main,"Ledger close","收起",new Rect(1686,38,190,76),()=>close?.Invoke(),low,click,25);
            Refresh();
        }
        void Refresh()
        {
            int opened=CultivationRules.Opened(save),count=InventoryRules.Count(save.bag,CultivationRules.ProtectionPill);
            if(count==0)usePill=false;
            summary.text="已通经脉  "+opened+" / 5\n气血增益  +"+CultivationRules.HpBonus(save)+"\n精力增益  +"+CultivationRules.StaminaBonus(save);
            mainPills.text="暖阳丹 × "+count+(usePill?" · 护持":" · 未选")+"\n"+(opened<5?CultivationRules.Points[opened].Name+" · 成功率 "+Math.Round(CultivationRules.Chance(save,usePill)*100)+"%":"五穴已通 · 周天圆满");
            mainPill.GetComponentInChildren<TMP_Text>().text=usePill?"撤下护丹":"选择护丹";mainPill.interactable=count>0;
            enter.GetComponentInChildren<TMP_Text>().text=opened<5?"冲 穴":"内 视";
            if(flow==null)return;
            flow.State(opened,selected);
        }
        void TogglePill()
        {
            if(popup!=null)return;
            if(!usePill&&InventoryRules.Count(save.bag,CultivationRules.ProtectionPill)<1)return;
            usePill=!usePill;Refresh();
        }
        void BeginAttempt()
        {
            if(popup!=null)return;
            if(!OpenVision())return;
            if(CultivationRules.Opened(save)<5)Attempt();
        }
        bool OpenVision()
        {
            var innerArt=ApprovedUiArt.Load(InnerArtId);
            if(innerArt==null){mainPills.text="内视图暂未备妥，请稍后再试。";return false;}
            mainGroup.interactable=mainGroup.blocksRaycasts=false;selected=Math.Min(CultivationRules.Opened(save),4);
            popup=ui.Rect(host,"Meridian inner vision popup",0,0,1920,1080);
            var veil=popup.gameObject.AddComponent<Image>();veil.color=new Color(.025f,.055f,.052f,.76f);veil.raycastTarget=true;
            // A single transparent body presentation. Pill choice and the transaction stay on the main page.
            var panel=ui.Rect(popup,"Meridian breath presentation",600,78,720,924);
            AestheticUi.Text(ui,panel,"Inner vision title","灵 台 内 视 · "+CultivationRules.Points[selected].Name,
                new Rect(60,12,600,60),34,TextAlignmentOptions.Center,UiKit.Paper);
            diagram=ui.Rect(panel,"True inner meridian diagram",100,88,520,640);diagramOrigin=diagram.anchoredPosition;
            var art=ImmortalUi.Texture(ui,diagram,"Authored jade inner body",innerArt,0,0,520,640).rectTransform;
            var artRect=new Rect(art.anchoredPosition.x,-art.anchoredPosition.y,art.sizeDelta.x,art.sizeDelta.y);
            flow=ui.Stretch(diagram,"Meridian actual qi channels").gameObject.AddComponent<MeridianInnerGraphic>();flow.Configure(low,artRect);
            var node=flow.Node(selected);
            AestheticUi.Text(ui,diagram,"Inner target point",CultivationRules.Points[selected].Name,
                new Rect(node.x+38,node.y-18,116,38),24,TextAlignmentOptions.Left,UiKit.Paper);
            result=AestheticUi.Text(ui,panel,"Inner committed result","凝神守一 · 气归丹田",
                new Rect(70,744,580,84),25,TextAlignmentOptions.Center,UiKit.Paper);
            var dismiss=PaintedScrollArt.Command(ui,panel,"Inner close","收息",new Rect(265,842,190,66),CloseVision,click,25);
            Refresh();if(EventSystem.current!=null)EventSystem.current.SetSelectedGameObject(dismiss.gameObject);return true;
        }
        void Attempt()
        {
            if(flow==null||flow.Busy||resultPending)return;
            try
            {
                // Persist exactly once, before any success presentation. Closing never repeats a transaction.
                var outcome=CultivationRules.AttemptAndCommit(save,selected,usePill,rng.NextDouble(),commit);
                if(!outcome.Attempted){result.text=outcome.Message;Refresh();return;}
                pendingMessage=outcome.Message;resultPending=true;
                result.text="气聚丹田 · 循脉而行";flow.Result(outcome.Succeeded,selected);
                changed?.Invoke();Refresh();
            }
            catch(Exception ex)
            {resultPending=false;result.text="未能落卷，穴位与丹药已恢复。";Debug.LogError("Meridian save failed: "+ex.Message);changed?.Invoke();Refresh();}
        }
        void Update()
        {
            if(flow==null)return;diagram.anchoredPosition=diagramOrigin+flow.Tremor;
            if(!resultPending||flow.Busy)return;
            resultPending=false;result.text=pendingMessage;
            Refresh();
        }
        public bool TryCloseInner(){if(popup==null)return false;CloseVision();return true;}
        void CloseVision()
        {
            if(popup==null)return;popup.gameObject.SetActive(false);Destroy(popup.gameObject);popup=null;flow=null;diagram=null;result=null;resultPending=false;pendingMessage=null;
            mainGroup.interactable=mainGroup.blocksRaycasts=true;Refresh();
            if(EventSystem.current!=null)EventSystem.current.SetSelectedGameObject(enter.gameObject);
        }
        void OnDestroy(){low=null;commit=null;changed=click=close=null;save=null;}
    }
}
