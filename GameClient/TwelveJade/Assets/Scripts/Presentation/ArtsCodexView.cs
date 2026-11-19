using System;
using System.Linq;
using TMPro;
using TwelveJade.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    // One live page: the mandala, dossier and apprentice all read the same committed save.
    public sealed class ArtsCodexView : MonoBehaviour
    {
        UiKit ui;RectTransform face;SaveData save;ItemTable table;Action<SaveData> commit;
        Action changed,click,close;Func<bool> reduce;readonly System.Random rng=new System.Random();
        int selected=3;bool ritual;Button primary,returnToBook,breathe,closeButton,spiritDismiss;
        Button[] slots=new Button[6],tabs=new Button[3],approaches=new Button[3];
        CanvasGroup[] slotGroups=new CanvasGroup[6];RawImage[] tabArt=new RawImage[3];
        TMP_Text[] slotNames=new TMP_Text[6],tabCounts=new TMP_Text[3],choiceStats=new TMP_Text[3];
        TMP_Text title,quality,progress,coreName,coreHint,cost,description,availability,notice,actionLabel,ritualSummary;
        RectTransform commonDetails,choiceDetails;ArtsCodexGraphic mainGlyph,largeGlyph,largeSocket,bar,primaryGlow;
        RectTransform mainEmblem;RawImage mainPainting,largePainting;ChibiStudyAnimation motion;
        ArtsSkill Skill=>ManualStudyRules.Skills[selected];
        bool Busy=>motion!=null&&motion.IsPlaying;
        TMP_Text Text(string name,string value,Rect box,float size=24,int lines=1,Color? ink=null,bool centered=false,bool brush=false,Transform parent=null)=>
            ArtsCodexArt.Text(ui,parent??face,name,value,box,size,lines,ink,centered?TextAlignmentOptions.Center:TextAlignmentOptions.TopLeft,brush);
        public void Build(UiKit kit,RectTransform host,SaveData data,ItemTable items,Action<SaveData> write,
            Func<bool> lowMotion,Action stateChanged,Action sound,Action closing)
        {
            ui=kit;save=data;table=items;commit=write;reduce=lowMotion;changed=stateChanged;click=sound;close=closing;
            ManualStudyRules.CrossCheck(table);face=ui.Rect(host,"Arts codex living mandala",0,0,1920,1080);
            bool paintedLibrary=ApprovedUiArt.Has("nov8/arts-library-vr"),paintedDisc=ApprovedUiArt.Has("nov8/arts-mandala-vr");
            var background=ImmortalUi.Art(ui,face,"Arts celestial Song library",paintedLibrary?"nov8/arts-library-vr":"nov8/arts-bg",0,0,1920,1080,true);
            if(background!=null)
            {
                background.uvRect=paintedLibrary?new Rect(0,.078125f,1,.84375f):new Rect(0,.165f,.67f,.67f);
                background.color=paintedLibrary?new Color(.62f,.69f,.64f,1):new Color(.38f,.46f,.40f,1);
            }
            Text("Arts page title","万 法 道 藏",ArtsCodexLayout.Title,52,1,ArtsCodexArt.Ivory,false,true);
            Text("Arts page subtitle","",ArtsCodexLayout.Subtitle,25,1,ArtsCodexArt.Gold);
            ArtsCodexArt.RoundTexture(ui,face,"Arts bronze and jade base",paintedDisc?ArtsCodexLayout.Mandala:new Rect(448,192,756,756),paintedDisc?"nov8/arts-mandala-vr":"nov7-v2/wheel/base",paintedDisc?new Rect(0,0,1,1):new Rect(.09f,.09f,.82f,.82f));
            ArtsCodexArt.Graphic(ui,face,"Eight trigram orbit",ArtsCodexLayout.Mandala,paintedDisc?ArtsCodexGraphic.Form.Orbit:ArtsCodexGraphic.Form.Mandala,ArtsCodexArt.Gold,reduce);
            if(!paintedDisc)ImmortalUi.Texture(ui,face,"Arts authored bronze outer rim",ApprovedUiArt.Load("nov7/hud-map"),406,150,840,840,new Color(1,1,.88f,.75f));
            ImmortalUi.Art(ui,face,"Arts mandala name silk mount",ArtsCodexArt.TabId,644,98,364,68);
            Text("Arts mandala name","八 卦 · 真 武",new Rect(644,100,364,64),31,1,ArtsCodexArt.Gold,true,true);
            for(int i=0;i<3;i++)BuildTab(i);
            Text("Arts lineage caption","典 藏 品 阶",new Rect(92,800,244,48),24,1,ArtsCodexArt.Gold,true);
            string[] grades={"黄阶","玄阶","地阶","天阶"};
            for(int i=0;i<4;i++)Text("Arts grade legend "+i,grades[i],new Rect(96+(i%2)*124,856+(i/2)*54,118,48),22,1,ArtsCodexArt.GradeColor(grades[i]),true);
            for(int i=0;i<6;i++)BuildSlot(i);
            mainEmblem=ui.Rect(face,"Arts selected large central seal",ArtsCodexLayout.CoreArt.x,ArtsCodexLayout.CoreArt.y,ArtsCodexLayout.CoreArt.width,ArtsCodexLayout.CoreArt.height);
            mainGlyph=ArtsCodexArt.Graphic(ui,mainEmblem,"Arts center living glyph",new Rect(0,0,236,236),ArtsCodexGraphic.Form.Glyph,ArtsCodexArt.Gold,null,selected);
            mainPainting=ImmortalUi.Texture(ui,mainEmblem,"Arts central painted spell",null,0,0,236,236);
            coreName=Text("Arts selected central name","",ArtsCodexLayout.CoreName,36,1,ArtsCodexArt.Ivory,true,true);
            coreHint=Text("Arts selected central instruction","",ArtsCodexLayout.CoreHint,22,1,ArtsCodexArt.Gold,true);
            var dossier=ArtsCodexLayout.Dossier;
            if(ApprovedUiArt.Has("nov8/arts-dossier-vr"))ImmortalUi.Art(ui,face,"Arts painted secret dossier","nov8/arts-dossier-vr",dossier.x,dossier.y,dossier.width,dossier.height);
            else
            {
                var silk=ImmortalUi.Silk(ui,face,"Arts dark jade secret dossier",dossier.x,dossier.y,dossier.width,dossier.height);
                silk.color=new Color(.24f,.36f,.30f,1);silk.raycastTarget=false;
                ImmortalUi.Rod(ui,face,"Arts secret dossier upper wood",1320,126,490,42);
                ImmortalUi.Rod(ui,face,"Arts secret dossier lower wood",1320,956,490,42);
            }
            title=Text("Arts selected skill name","",ArtsCodexLayout.SkillTitle,38,1,ArtsCodexArt.Ivory,true,true);
            quality=Text("Arts actual grade and category","",ArtsCodexLayout.Quality,24,1,ArtsCodexArt.Gold,true);
            largeSocket=ArtsCodexArt.Graphic(ui,face,"Arts dossier seal socket",ArtsCodexLayout.LargeSeal,ArtsCodexGraphic.Form.Socket,ArtsCodexArt.Bronze);
            var glyphBox=ArtsCodexLayout.LargeSeal;glyphBox.x+=32;glyphBox.y+=22;glyphBox.width=158;glyphBox.height=158;
            largeGlyph=ArtsCodexArt.Graphic(ui,face,"Arts dossier skill illustration",glyphBox,ArtsCodexGraphic.Form.Glyph,ArtsCodexArt.Bronze,null,selected);
            largePainting=ImmortalUi.Texture(ui,face,"Arts dossier painted spell",null,glyphBox.x,glyphBox.y,glyphBox.width,glyphBox.height);
            progress=Text("Arts true progress label","",ArtsCodexLayout.ProgressCaption,27,1,ArtsCodexArt.Ivory,true);
            bar=ArtsCodexArt.Graphic(ui,face,"Arts jade progress beads",ArtsCodexLayout.ProgressBar,ArtsCodexGraphic.Form.Progress,ArtsCodexArt.Jade);
            commonDetails=ui.Rect(face,"Arts selected skill details",0,0,1920,1080);
            cost=Text("Arts actual battle cost","",ArtsCodexLayout.Cost,26,1,ArtsCodexArt.Gold,true,parent:commonDetails);
            description=Text("Arts short battle description","",ArtsCodexLayout.Description,24,2,ArtsCodexArt.Ivory,true,parent:commonDetails);
            availability=Text("Arts actual fragment state","",ArtsCodexLayout.Availability,23,1,ArtsCodexArt.Muted,true,parent:commonDetails);
            var buttonRoot=ui.Rect(commonDetails,"Arts yin yang study action",ArtsCodexLayout.Action.x,ArtsCodexLayout.Action.y,142,142);
            primaryGlow=ArtsCodexArt.Graphic(ui,buttonRoot,"Arts study jade breathing rim",new Rect(0,0,142,142),ArtsCodexGraphic.Form.Taiji,ArtsCodexArt.Gold,reduce);
            primary=ArtsCodexArt.Hit(ui,buttonRoot,"Arts contemplate",new Rect(0,0,142,142),BeginStudy,reduce,primaryGlow);
            // Caption sits below the icon; it never covers the yin-yang eye or becomes a white form button.
            actionLabel=Text("Arts study action caption","",new Rect(1414,972,302,48),24,1,ArtsCodexArt.Ivory,true);
            choiceDetails=ui.Rect(face,"Arts true study choices",0,0,1920,1080);
            ritualSummary=Text("Arts live study resources","",ArtsCodexLayout.RitualSummary,22,1,ArtsCodexArt.Ivory,true,parent:choiceDetails);
            for(int i=0;i<3;i++)BuildApproach(i);
            breathe=ArtsCodexArt.WoodButton(ui,choiceDetails,"Arts breathe","调 息",new Rect(1358,934,192,58),()=>Act(StudyApproach.Breathe),reduce);
            returnToBook=ArtsCodexArt.Hit(ui,choiceDetails,"Arts return to folio",new Rect(1570,934,192,58),()=>{ritual=false;motion.DismissImmediately();Refresh();Focus(primary);},reduce);
            Text("Arts return caption","观 法",new Rect(1570,934,192,58),24,1,ArtsCodexArt.Gold,true,parent:choiceDetails);
            notice=Text("Arts committed result and hint","",ArtsCodexLayout.Notice,23,1,ArtsCodexArt.Ivory,true);
            BuildApprentice();
            closeButton=ArtsCodexArt.WoodButton(ui,face,"Arts close","收 起",new Rect(1620,44,192,64),()=>{motion.DismissImmediately();click?.Invoke();close?.Invoke();},reduce);
            Refresh();Focus(slots[selected]);
        }
        void BuildTab(int i)
        {
            int index=i;var r=ArtsCodexLayout.Tabs[i];var root=ui.Rect(face,"Arts category "+i,r.x,r.y,r.width,r.height);
            tabArt[i]=AestheticUi.CommandPlate(ui,root,"Arts category jade "+i,new Rect(0,0,r.width,r.height));
            tabs[i]=ArtsCodexArt.Hit(ui,root,"Arts category choose "+i,new Rect(0,0,r.width,r.height),()=>
            {if(Busy)return;Select(new[]{0,3,5}[index]);},reduce);
            AestheticUi.Text(ui,root,"Arts category name "+i,ArtsCodexArt.Categories[i],new Rect(38,24,r.width-76,32),23,TextAlignmentOptions.Center,AestheticUi.Ink);
            tabCounts[i]=AestheticUi.Text(ui,root,"Arts category mastered count "+i,"",new Rect(38,58,r.width-76,24),16,TextAlignmentOptions.Center,AestheticUi.Ink);
        }
        void BuildSlot(int i)
        {
            int index=i;var p=ArtsCodexLayout.Slots[i];var root=ui.Rect(face,"Arts skill socket "+i,p.x-80,p.y-80,160,160);
            slotGroups[i]=root.gameObject.AddComponent<CanvasGroup>();
            var grade=ArtsCodexArt.GradeColor(ArtsCodexArt.Grade(table,i));
            var frame=ArtsCodexArt.Graphic(ui,root,"Arts true quality rim "+i,new Rect(0,0,160,160),ArtsCodexGraphic.Form.Socket,grade,reduce);
            ArtsCodexArt.Emblem(ui,root,"Arts skill sigil "+i,new Rect(28,28,104,104),i,grade);
            slots[i]=ArtsCodexArt.Hit(ui,root,"Arts select "+i,new Rect(0,0,160,160),()=>{if(!Busy)Select(index);},reduce,frame);
            ImmortalUi.Art(ui,root,"Arts socket caption silk "+i,ArtsCodexArt.ChoiceId,-8,156,176,50);
            slotNames[i]=Text("Arts socket name "+i,ArtsCodexArt.Names[i],new Rect(-28,158,216,48),24,1,ArtsCodexArt.Ivory,true,parent:root);
        }
        void BuildApproach(int i)
        {
            int index=i;var r=ArtsCodexLayout.Approaches[i];var root=ui.Rect(choiceDetails,"Arts approach mount "+i,r.x,r.y,r.width,r.height);
            AestheticUi.CommandPlate(ui,root,"Arts approach jade "+i,new Rect(0,0,r.width,r.height));
            var glow=ArtsCodexArt.Graphic(ui,root,"Arts approach light "+i,new Rect(10,5,66,66),ArtsCodexGraphic.Form.Socket,ArtsCodexArt.Gold,reduce);
            approaches[i]=ArtsCodexArt.Hit(ui,root,"Arts choose approach "+i,new Rect(0,0,r.width,r.height),()=>Act((StudyApproach)(index+1)),reduce,glow);
            Text("Arts approach name "+i,new[]{"循章","推演","强参"}[i],new Rect(14,10,110,56),27,1,AestheticUi.Ink,true,parent:root);
            choiceStats[i]=Text("Arts exact choice effect "+i,"",new Rect(126,3,280,70),18,2,AestheticUi.Ink,true,parent:root);
        }
        void BuildApprentice()
        {
            var overlay=ui.Rect(face,"Arts consciousness study popup",0,0,1920,1080);
            var veil=overlay.gameObject.AddComponent<Image>();veil.color=new Color(.025f,.045f,.04f,.64f);veil.raycastTarget=true;
            var group=overlay.gameObject.AddComponent<CanvasGroup>();group.blocksRaycasts=true;
            var host=ui.Rect(overlay,"Study spirit centered chamber",610,150,700,780);
            host.pivot=new Vector2(.5f,.5f);host.anchoredPosition=new Vector2(960,-540);
            // Floating transparent character, with a small separate silk result note; no full white poster board.
            AestheticUi.Text(ui,host,"Study spirit heading","识 海 · 神 识",new Rect(70,4,560,58),32,TextAlignmentOptions.Center,UiKit.Paper);
            var body=ui.Rect(host,"Study spirit animated body",150,78,400,400);
            body.gameObject.AddComponent<StudySpiritSequence>().Configure(ui,body);
            AestheticUi.Text(ui,host,"Study current owner",save.characterName+" · 识海",new Rect(86,490,528,36),22,TextAlignmentOptions.Center,UiKit.Paper);
            var note=PaintedScrollArt.Note(ui,host,"Study local silk result",new Rect(70,540,560,140));
            var heading=AestheticUi.Text(ui,note,"Study spirit result","",new Rect(24,14,512,48),27,TextAlignmentOptions.Center);
            var detail=AestheticUi.Text(ui,note,"Study spirit detail","",new Rect(24,66,512,62),22,TextAlignmentOptions.Center);
            detail.textWrappingMode=TextWrappingModes.Normal;
            motion=overlay.gameObject.AddComponent<ChibiStudyAnimation>();
            motion.Bind(body,group,heading,detail,reduce,Array.Empty<RectTransform>(),Array.Empty<CanvasGroup>(),host);
            group.blocksRaycasts=true;group.interactable=true;
            spiritDismiss=PaintedScrollArt.Command(ui,host,"Study spirit dismiss","收念",new Rect(255,704,190,62),()=>
            {motion.DismissImmediately();Refresh();Focus(ritual?((ManualStudyRules.Entry(save,Skill.Id)?.strain??0)>=80?breathe:approaches[0]):primary);},click,23);
            motion.DismissImmediately();
        }
        void Select(int index)
        {
            if(index<0||index>=6||Busy)return;selected=index;ritual=false;motion.DismissImmediately();
            notice.text="";Refresh();click?.Invoke();
        }
        void Refresh()
        {
            var skill=Skill;bool learned=ManualStudyRules.Learned(save,skill.Id);var entry=ManualStudyRules.Entry(save,skill.Id);
            bool recorded=entry?.recorded==true;int score=learned?100:entry?.progress??0;
            int category=ArtsCodexArt.Category(selected);Color grade=ArtsCodexArt.GradeColor(ArtsCodexArt.Grade(table,selected));
            for(int i=0;i<3;i++)
            {
                var group=Enumerable.Range(0,6).Where(n=>ArtsCodexArt.Category(n)==i).ToArray();
                tabCounts[i].text="已会 "+group.Count(n=>ManualStudyRules.Learned(save,ManualStudyRules.Skills[n].Id))+" / "+group.Length;
                tabArt[i].color=i==category?new Color(.95f,1,.96f,1):new Color(.76f,.85f,.80f,1);tabs[i].interactable=!Busy;
            }
            for(int i=0;i<6;i++)
            {
                slots[i].interactable=!Busy;slots[i].GetComponent<ArtsCodexFocus>().Mark(i==selected);
                slotGroups[i].alpha=1; // Keep every socket opaque so the carved disc cannot show through the skill artwork.
                slotNames[i].color=i==selected?ArtsCodexArt.Gold:ArtsCodexArt.Category(i)==category?ArtsCodexArt.Ivory:ArtsCodexArt.Muted;
            }
            title.text=ArtsCodexArt.Names[selected];quality.text=ArtsCodexArt.Grade(table,selected)+" · "+(skill.Basic?"主动武艺":"可施神通 · 常驻增益");quality.color=grade;
            largeSocket.Configure(ArtsCodexGraphic.Form.Socket,grade);
            largeGlyph.Configure(ArtsCodexGraphic.Form.Glyph,ArtsCodexArt.SymbolColor(selected),null,selected);mainGlyph.Configure(ArtsCodexGraphic.Form.Glyph,ArtsCodexArt.SymbolColor(selected),null,selected);
            var painting=ArtsCodexArt.Sigil(selected);mainPainting.texture=painting;largePainting.texture=painting;
            mainPainting.enabled=largePainting.enabled=painting!=null;mainGlyph.enabled=largeGlyph.enabled=painting==null;
            progress.text=(learned?"融会贯通":recorded?"领会进境":"尚待开卷")+"   "+score+" / 100";bar.Value(score/100f);
            var activeSkill=TwelveJade.Combat.PlayerSkillCombat.Find(skill.Id);
            cost.text=skill.Basic?"精力 "+CombatRules.StaminaCost(skill.Action)+" · "+skill.Key:activeSkill==null?skill.Effect:
                "精力 "+activeSkill.Move.StaminaCost+" · 调息 "+activeSkill.Cooldown+"秒 · "+activeSkill.Key;
            description.text=learned&&!skill.Basic?skill.Effect+"\n"+ArtsCodexArt.Descriptions[selected].Split('\n')[1]:ArtsCodexArt.Descriptions[selected];
            int fragments=skill.Basic?0:InventoryRules.Count(save.bag,skill.ManualId);
            availability.text=skill.Basic?"入门已会":learned?"已融会贯通":recorded?"心得 "+entry.notes+" · 心神负担 "+entry.strain:"拓录需残卷1册 · 持有 "+fragments+" 册";
            actionLabel.text=skill.Basic||learned?"温 习":recorded?"入 定 参 悟":"拓 录 一 卷";
            primary.interactable=!Busy&&(skill.Basic||learned||recorded||fragments>0);primary.GetComponent<ArtsCodexFocus>().Mark(true);
            commonDetails.gameObject.SetActive(!ritual);choiceDetails.gameObject.SetActive(ritual);actionLabel.transform.parent.gameObject.SetActive(!ritual);
            mainEmblem.gameObject.SetActive(true);coreName.transform.parent.gameObject.SetActive(true);
            coreName.text=ArtsCodexArt.Names[selected];
            coreHint.text=ritual?"凝神参悟":"";
            ritualSummary.text="悟性 "+StudyPathRules.Comprehension(save)+" · 心得 "+(entry?.notes??0)+" · 心神 "+(entry?.strain??0);
            for(int i=0;i<3;i++)
            {
                var way=(StudyApproach)(i+1);approaches[i].interactable=!Busy&&!learned&&(entry?.strain??0)<80;
                choiceStats[i].text="领会 +"+StudyPathRules.Gain(save,skill.Id,way)+" · 负担"+new[]{"-8","+18","+30"}[i]+"\n走火 "+Math.Round(StudyPathRules.Risk(save,skill.Id,way)*100)+"% · 顿悟 "+Math.Round(StudyPathRules.Epiphany(save,skill.Id,way)*100)+"%";
            }
            breathe.interactable=!Busy&&!learned&&(entry?.strain??0)>0;returnToBook.interactable=!Busy;closeButton.interactable=!Busy;
        }
        void BeginStudy()
        {
            if(Busy)return;
            if(Skill.Basic||ManualStudyRules.Learned(save,Skill.Id))
            {
                var result=ManualStudyRules.Study(save,table,Skill.Id,rng.NextDouble());
                if(!result.Attempted){notice.text=result.Detail;Refresh();return;}
                PlayResult(result.Outcome,result.Heading,result.Detail);return;
            }
            if(ManualStudyRules.Entry(save,Skill.Id)?.recorded!=true){Act(StudyApproach.Record);return;}
            ritual=true;coreHint.text="心神安定 · 择一法继续参悟";Refresh();Focus((ManualStudyRules.Entry(save,Skill.Id)?.strain??0)>=80?breathe:approaches[0]);click?.Invoke();
        }
        void Act(StudyApproach approach)
        {
            if(Busy)return;
            StudySessionResult result;
            try{result=StudyPathRules.ActAndCommit(save,table,Skill.Id,approach,rng.NextDouble(),commit);}
            catch(Exception ex){changed?.Invoke();notice.text="本次未能落卷，残卷与参悟记录已恢复。";Debug.LogError("Arts codex save failed: "+ex.Message);Refresh();return;}
            changed?.Invoke();
            if(result.Outcome==StudyOutcome.Refused){notice.text=result.Detail;Refresh();return;}
            ritual=!ManualStudyRules.Learned(save,Skill.Id);
            PlayResult(result.Outcome,result.Heading,result.Detail);
        }
        void PlayResult(StudyOutcome outcome,string heading,string detail)
        {
            notice.text=heading+" · "+detail;
            motion.Play(Skill.Name,outcome,heading,detail,()=>
            {
                if(this==null||face==null||!face.gameObject.activeInHierarchy)return;
                if(ritual)coreHint.text="心得已落卷 · 可续读或调息";
                Refresh();Focus(ritual?((ManualStudyRules.Entry(save,Skill.Id)?.strain??0)>=80?breathe:approaches[0]):primary);
            });Refresh();Focus(spiritDismiss);click?.Invoke();
        }
        void Focus(Button target){if(target!=null&&target.IsInteractable()&&EventSystem.current!=null)EventSystem.current.SetSelectedGameObject(target.gameObject);}
        public bool TryCloseStudy()
        {
            if(motion==null||!motion.gameObject.activeSelf)return false;
            motion.DismissImmediately();Refresh();Focus(ritual?((ManualStudyRules.Entry(save,Skill.Id)?.strain??0)>=80?breathe:approaches[0]):primary);return true;
        }
        void OnDisable(){if(motion!=null)motion.DismissImmediately();}
        void OnDestroy(){commit=null;changed=click=close=null;reduce=null;save=null;table=null;}
    }
}
