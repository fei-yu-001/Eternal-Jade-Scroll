using System;
using TMPro;
using TwelveJade.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    // Modal-local owner. All actions commit through Core; closing only disposes presentation.
    public sealed class StudySanctumView : MonoBehaviour
    {
        SaveData save;ItemTable table;ArtsSkill skill;Action<SaveData> commit;Action close,stateChanged;
        readonly System.Random random=new System.Random();
        ChibiStudyAnimation studyMotion;StudyProgressRing progressRing;TMP_Text title,status,prompt,notice;
        Button[] choices;RawImage[] choiceArt;TMP_Text[] choiceText;Button record,breathe,returnButton;
        public static void Open(UiKit ui,RectTransform parent,SaveData save,ItemTable table,string id,
            Action<SaveData> write,Func<bool> reduce,Action finished,Action changed)
        {
            var root=ui.Rect(parent,"Daoist study choices",0,0,1920,1080);
            var hit=root.gameObject.AddComponent<Image>();hit.color=new Color(.24f,.21f,.16f,.45f);
            var owner=root.gameObject.AddComponent<StudySanctumView>();owner.save=save;owner.table=table;
            owner.skill=ManualStudyRules.Find(id);owner.commit=write;owner.close=finished;owner.stateChanged=changed;
            var face=ui.Rect(root,"Study mounted silk",350,98,1220,884);
            ScholarScrollArt.Horizontal(ui,face,"Study silk",0,0,1220,884);
            ScholarScrollArt.WritingSilk(ui,face,"Study quiet writing surface",54,46,1112,790);
            owner.title=CultivationLayout.Label(ui,face,"Study subject",owner.skill.Name+" · 伴童参悟",new Rect(390,64,600,70),34,1,ImmortalUi.Ink);
            owner.status=CultivationLayout.Label(ui,face,"Study persisted progress","",new Rect(540,150,610,150),25,3,ImmortalUi.Ink);
            owner.prompt=CultivationLayout.Label(ui,face,"Study comprehension and advice","",new Rect(540,304,610,170),24,4,ImmortalUi.Ink);
            owner.progressRing=ui.Rect(face,"Study actual knowledge ring",32,156,484,484).gameObject.AddComponent<StudyProgressRing>();
            var stageClip=ui.Rect(face,"Study motion boundary",30,154,490,490);stageClip.gameObject.AddComponent<RectMask2D>();
            var figure=ui.Rect(stageClip,"Study apprentice stage",4,6,480,480);
            figure.pivot=new Vector2(.5f,.5f);figure.anchoredPosition=new Vector2(244,-246);
            var group=figure.gameObject.AddComponent<CanvasGroup>();group.blocksRaycasts=false;group.interactable=false;
            var body=ui.Rect(figure,"Study body",10,10,460,460);
            ImmortalUi.Texture(ui,body,"Meditating apprentice",ApprovedUiArt.Load("nov8/chibi-study"),0,0,460,460);
            var bubble=CultivationLayout.Label(ui,face,"Study apprentice speech","小道友，我们一起读。",new Rect(36,648,472,60),25,1,ImmortalUi.Ink,TextAlignmentOptions.Center);
            var outcome=CultivationLayout.Label(ui,face,"Study apprentice outcome","择一法门 · 莫急于求成",new Rect(36,711,472,92),22,2,ImmortalUi.Ink,TextAlignmentOptions.Center);
            var glyphs=new RectTransform[6];var glows=new CanvasGroup[6];
            for(int i=0;i<6;i++)
            {
                var word=ImmortalUi.Text(ui,figure,"Study insight word "+i,new[]{"道","法","心","静","明","悟"}[i],220,220,40,50,27,ImmortalUi.Gold,true);
                glyphs[i]=word.rectTransform;glows[i]=word.gameObject.AddComponent<CanvasGroup>();glows[i].blocksRaycasts=false;
            }
            owner.studyMotion=figure.gameObject.AddComponent<ChibiStudyAnimation>();
            owner.studyMotion.Bind(body,group,bubble,outcome,reduce,glyphs,glows);owner.studyMotion.Hold();
            owner.choices=new Button[3];owner.choiceArt=new RawImage[3];owner.choiceText=new TMP_Text[3];
            for(int i=0;i<3;i++)
            {
                int index=i;float x=540+(i%2)*300,y=493+(i/2)*120;
                owner.choiceArt[i]=ImmortalUi.Texture(ui,face,"Study jade choice "+i,ApprovedUiArt.Load("nov7/white-jade-command"),x,y,282,72);
                owner.choices[i]=ImmortalUi.Hotspot(ui,face,"Study approach "+i,new[]{"循 章","推 演","强 参"}[i],x+12,y+5,258,56,()=>owner.Act((StudyApproach)(index+1)),25,ImmortalUi.Ink);
                owner.choiceText[i]=CultivationLayout.Label(ui,face,"Study exact risk "+i,"",new Rect(x,y+72,282,44),20,1,ImmortalUi.Ink,TextAlignmentOptions.Center);
            }
            owner.breathe=ImmortalUi.Button(ui,face,"Study breathe","抱 元 调 息",840,619,282,74,()=>owner.Act(StudyApproach.Breathe));
            owner.record=ImmortalUi.Button(ui,face,"Study transcribe","拓 录 残 卷",540,493,582,88,()=>owner.Act(StudyApproach.Record));
            owner.notice=CultivationLayout.Label(ui,face,"Study transaction notice","首次拓录一卷，此后参悟不耗卷。",new Rect(540,738,598,60),23,1,ImmortalUi.Ink);
            owner.returnButton=ImmortalUi.Hotspot(ui,face,"Study return","收 心",1040,60,140,70,()=>owner.Close(),25,ImmortalUi.Ink);
            owner.Refresh();owner.FocusChoice();
        }
        void Refresh()
        {
            if(save==null)return;var entry=ManualStudyRules.Entry(save,skill.Id);bool learned=ManualStudyRules.Learned(save,skill.Id);
            bool recorded=entry?.recorded==true,ready=recorded&&!learned,busy=studyMotion.IsPlaying;
            progressRing.State(learned?100:entry?.progress??0,entry?.strain??0);
            title.text=skill.Name+" · "+(learned?"融会贯通":recorded?"伴童参悟":"古卷初展");
            status.text="领会  "+(learned?100:entry?.progress??0)+" / 100\n心得  "+(entry?.notes??0)+" / 12\n心神负担  "+(entry?.strain??0)+" / 100";
            prompt.text=learned?skill.Effect+"\n温习不耗卷，不重复加属性":!recorded?
                "对应残卷  "+InventoryRules.Count(save.bag,skill.ManualId)+" 册\n先拓录，后选参悟之法\n旧日偶得会带入领会与心得\n缺卷可向货郎老栓求购":
                "悟性 "+StudyPathRules.Comprehension(save)+" · 心神负担满80须调息\n循章 +"+StudyPathRules.Gain(save,skill.Id,StudyApproach.Steady)+" · 负担-8 · 心得+1\n推演 +"+StudyPathRules.Gain(save,skill.Id,StudyApproach.Deduce)+" · 负担+18 · 心得+2\n强参 +"+StudyPathRules.Gain(save,skill.Id,StudyApproach.Force)+" · 负担+30 · 心得+2";
            for(int i=0;i<3;i++)
            {
                var approach=(StudyApproach)(i+1);choices[i].gameObject.SetActive(recorded||learned);choiceArt[i].gameObject.SetActive(recorded||learned);
                choiceText[i].transform.parent.gameObject.SetActive(recorded||learned);
                choices[i].interactable=!busy&&(learned||(ready&&entry.strain<80));
                choiceText[i].text=learned?"只温习 · 不叠属性":"走火"+Math.Round(StudyPathRules.Risk(save,skill.Id,approach)*100)+"% · 顿悟"+Math.Round(StudyPathRules.Epiphany(save,skill.Id,approach)*100)+"%";
            }
            record.transform.parent.gameObject.SetActive(!recorded&&!learned);record.interactable=!busy&&InventoryRules.Count(save.bag,skill.ManualId)>0;
            breathe.transform.parent.gameObject.SetActive(recorded&&!learned);breathe.interactable=!busy&&(entry?.strain??0)>0;
        }
        void Act(StudyApproach approach)
        {
            if(save==null||studyMotion.IsPlaying)return;
            StudySessionResult result;
            try{result=StudyPathRules.ActAndCommit(save,table,skill.Id,approach,random.NextDouble(),commit);}
            catch(Exception ex){notice.text="未能落卷，残卷与研读记录已恢复。";Debug.LogError("Study path persistence failed: "+ex.Message);stateChanged?.Invoke();Refresh();return;}
            stateChanged?.Invoke();notice.text=result.Changed?"已落卷 · 关闭后仍可续读":result.Detail;
            Refresh();
            if(result.Outcome==StudyOutcome.Refused)return;
            studyMotion.Play(skill.Name,result.Outcome,result.Heading,result.Detail,()=>{if(this==null)return;studyMotion.Hold();Refresh();FocusChoice();});Refresh();
        }
        void FocusChoice()
        {
            if(EventSystem.current==null)return;
            Button target=record.gameObject.activeInHierarchy&&record.interactable?record:
                choices[0].gameObject.activeInHierarchy&&choices[0].interactable?choices[0]:
                breathe.gameObject.activeInHierarchy&&breathe.interactable?breathe:returnButton;
            EventSystem.current.SetSelectedGameObject(target.gameObject);
        }
        void Close(){studyMotion?.DismissImmediately();var done=close;close=null;gameObject.SetActive(false);Destroy(gameObject);done?.Invoke();}
        void OnDestroy(){stateChanged=null;close=null;commit=null;save=null;table=null;}
    }
}
