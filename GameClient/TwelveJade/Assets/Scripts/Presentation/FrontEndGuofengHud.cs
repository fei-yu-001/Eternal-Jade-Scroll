using System;
using System.Linq;
using TMPro;
using TwelveJade.Core;
using UnityEngine;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    public sealed partial class FrontEndController
    {
        JadeLotusRotator hudDaoWheel;
        TMP_Text worldHealthCaption, worldStaminaCaption;
        bool hudWheelInputHeld;
        public JadeLotusRotator HudDaoWheelForCheck => hudDaoWheel;
        void BuildGuofengHud()
        {
            var state=ui.Rect(content,"Town hud",24,20,740,244);
            var stats=PlayerStatsFor(activeSave.characterId);
            // Preserve the lookup name as an empty layout node; no full-size paper/Graphic remains.
            var identity=ui.Rect(state,"Hud identity paper",178,6,540,204);
            var nameMist=HudSoftMist(identity,"Hud name mist",16,0,240,54);
            var wealthMist=HudSoftMist(identity,"Hud wealth mist",16,150,480,48);
            var portrait = ui.PortraitBadge(state,DialoguePortraitCatalog.Hero(activeSave.gender),
                new UiKit.PortraitFrame { X=.24f,Y=.46f,W=.52f,H=.52f },20,20,164);
            var maskRoot = (RectTransform)portrait.transform.parent;
            maskRoot.gameObject.AddComponent<Mask>().showMaskGraphic=false;
            GuofengHudArt.Create(ui,state,"White jade portrait frame","hud-avatar",0,0,204,204);
            var name=HudLabel(state,"Hud character name",activeSave.characterName,220,16,468,44,30,UiKit.Ink,true);
            FitHudMist(nameMist,name);
            HudCapacityRail(state,"Cinnabar capacity engraving",188,60,490,52,UiKit.Hex("81382F"));
            worldHealthCaption=HudLabel(state,"Hud health capacity","气血上限  "+stats.hp,234,70,410,34,23,UiKit.Hex("452D29"));
            HudCapacityRail(state,"Jade capacity engraving",188,108,490,52,UiKit.Hex("406D5F"));
            worldStaminaCaption=HudLabel(state,"Hud stamina capacity","精力上限  "+stats.stamina,234,118,410,34,23,UiKit.Hex("233F36"));
            // Realm progression is absent from SaveData: present no invented level or current-resource number.
            var realmPaper=HudSoftMist(state,"Hud realm tag",22,207,160,36);
            HudLabel(realmPaper.transform,"Hud realm state","修为未录",8,0,144,36,20,UiKit.Ink,true,TextAlignmentOptions.Center);
            var wealth=HudLabel(state,"Hud wealth and clock","",218,164,480,38,21,UiKit.Ink);
            var refreshBrief=BuildChapterBriefHud();
            var refresh=state.gameObject.AddComponent<GuofengHudRefresh>();
            refresh.Configure(()=>
            {
                if(activeSave==null||page!="town"||wealth==null)return;
                var now=activeSave.WorldTimeNow();var branch=HourBranches[(now.minuteOfDay/60+1)/2%12];
                string value="铜钱 "+activeSave.coins+" 文 · 第 "+now.day+" 日 · "+branch+"时";
                if(wealth.text!=value){wealth.text=value;FitHudMist(wealthMist,wealth);}
                refreshBrief();
            });
            hudDaoWheel=JadeLotusRotator.Create(ui,content,()=>settings.reduceMotion,CanUseHudDaoWheel,
                AcquireHudWheelInput,ReleaseHudWheelInput,
                ()=>dialogueConsumeThroughFrame=Math.Max(dialogueConsumeThroughFrame,Time.frameCount+1),ActivateHudDaoWheel,effects,gone=>{if(ReferenceEquals(hudDaoWheel,gone))hudDaoWheel=null;},
                PrologueRules.CanCultivate(activeSave)?null:new[]{"设置","行囊","舆图","防身","烹饪","行记","身世","落卷"});
            HudLabel(content,"Hud town name",town.Name,1580,342,280,40,24,UiKit.Ink,true,TextAlignmentOptions.Center);
        }
        TMP_Text HudLabel(Transform parent,string name,string text,float x,float y,float width,float height,float size,Color ink,bool brush=false,TextAlignmentOptions align=TextAlignmentOptions.TopLeft)
        {
            var label=ui.Label(parent,text,x,y,width,height,size,ink,align,brush);
            label.name=name;label.richText=false;label.extraPadding=false;
            if(!brush)SdfInkWeight.Apply(label);
            return label;
        }
        Action BuildChapterBriefHud()
        {
            const float width=360,inner=316;
            var tracking=ui.Rect(content,"Hanging quest silk",28,312,width,106);
            var paper=FairylandScrollArt.Create(ui,tracking,"Hud fairyland jade silk",new Rect(0,0,360,106));
            paper.rectTransform.anchorMin=Vector2.zero;paper.rectTransform.anchorMax=Vector2.one;
            paper.rectTransform.pivot=new Vector2(.5f,.5f);paper.rectTransform.offsetMin=Vector2.zero;paper.rectTransform.offsetMax=Vector2.zero;paper.raycastTarget=true;
            RectTransform lower=null;
            var marker=ui.Rect(tracking,"Hud quest golden seal",22,26,42,42).gameObject.AddComponent<QuestGoldenMarker>();
            marker.Configure(()=>settings==null||settings.reduceMotion);
            var storyHeading = HudLabel(tracking,"Hud quest scroll name","青石故里",78,22,180,52,26,UiKit.Ink);
            var prompt=HudLabel(tracking,"Hud quest fold state","展卷",263,30,72,40,21,UiKit.Ink);
            var roll=tracking.gameObject.AddComponent<QuestBriefRoll>();
            var header=ImmortalUi.Hotspot(ui,tracking,"Hud quest toggle","",0,10,width,76,()=>{PlayClick();roll.Toggle();});
            var viewport=ui.Rect(tracking,"Hud quest reveal mask",22,84,inner,0);viewport.gameObject.AddComponent<RectMask2D>();
            var hit=viewport.gameObject.AddComponent<Image>();hit.color=Color.clear;
            var scroll=viewport.gameObject.AddComponent<ScrollRect>();scroll.viewport=viewport;scroll.horizontal=false;scroll.vertical=true;
            scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=32;
            var sheet=ui.Rect(viewport,"Hud quest unscaled text",0,0,inner,300);scroll.content=sheet;
            var detailGroup=sheet.gameObject.AddComponent<CanvasGroup>();
            var entries=new System.Collections.Generic.List<QuestEntryView>();
            var open=AestheticUi.Jade(ui,sheet,"Hud quest full scroll","详阅行卷",new Rect(42,0,232,68),ShowTownChapterPanel,()=>settings.reduceMotion,PlayClick,24);
            var journeyText = HudLabel(sheet, "Hud story objective", "", 8, 4, inner-16, 90, 23, UiKit.Ink);
            journeyText.richText = false;
            var journeyAction = StoryJourneyButton(sheet, "Hud story follow", "寻访", new Rect(12, 98, inner-24, 60), FollowQingshiStory);
            float journeyHeight = 170;
            Action reflow=()=>
            {
                float y=journeyHeight;foreach(var entry in entries){((RectTransform)entry.transform).anchoredPosition=new Vector2(0,-y);y+=entry.Height+10;}
                ((RectTransform)open.transform).anchoredPosition=new Vector2(42,-y);y+=80;
                sheet.sizeDelta=new Vector2(inner,y);roll.SetBodyHeight(y);
            };
            roll.Configure(tracking,viewport,lower,detailGroup,scroll,header,prompt,()=>settings.reduceMotion);
            string last="";
            return () =>
            {
                if(tracking==null||!tracking.gameObject.activeInHierarchy||activeSave==null||page!="town")return;
                bool ready=LoadTables();var data=ready?ChapterBrief():V2ChapterBrief.Build(null,null,null,null);
                var state=ChapterBriefNotice(activeSave,ready?chapterQuests:null,data);marker.SetNotice(state);
                int storyIndex = QingshiStory.CurrentIndex(activeSave);
                var story = storyIndex >= 0 && storyIndex < QingshiStory.Scenes.Count ? QingshiStory.Scenes[storyIndex] : null;
                if (story != null && QingshiStory.CanEnter(activeSave, story.Id)) state |= QuestGoldenMarker.Notice.CanAdvance;
                marker.SetNotice(state);
                var lead = QingshiStoryJourney.Current(activeSave);
                storyHeading.text = lead == null ? "故里回望" : string.IsNullOrEmpty(lead.NpcId) ? "续 · 这一夜" : "寻 · " + lead.NpcName;
                string signature=data.Title+data.Goal+data.Progress+data.Next+state+storyIndex;
                if(signature==last)return;last=signature;
                journeyText.text = lead?.Objective ?? "你已离开青石镇。此处为故里回望，旧事可展卷重读。";
                float objectiveHeight = Mathf.Max(64, journeyText.GetPreferredValues(journeyText.text, inner-20, float.PositiveInfinity).y+10);
                journeyText.rectTransform.sizeDelta = new Vector2(inner-16, objectiveHeight);
                ((RectTransform)journeyAction.transform).anchoredPosition = new Vector2(12, -(objectiveHeight+14));
                journeyAction.GetComponentInChildren<TMP_Text>().text = lead == null ? "展卷回看" : string.IsNullOrEmpty(lead.NpcId) ? "接着读下去" : "去见" + lead.NpcName;
                journeyAction.interactable = lead == null || QingshiStory.CanEnter(activeSave, lead.SceneId);
                journeyHeight = objectiveHeight+90;
                foreach(var entry in entries){entry.gameObject.SetActive(false);Destroy(entry.gameObject);}entries.Clear();
                Action<string,string,string> add=(kind,title,text)=>
                {
                    var row=ui.Rect(sheet,"Hud quest entry "+entries.Count,0,0,inner,78);var entry=row.gameObject.AddComponent<QuestEntryView>();
                    entry.Build(ui,row,kind,title,text,()=>settings.reduceMotion,PlayClick,reflow);entries.Add(entry);
                };
                // The current goal and follow button above already carry the main story.
                // Keep the remaining rows for side quests instead of duplicating a narrow title card.
                if(ready)
                {
                    var main=chapterTable.Segments.SelectMany(seg=>seg.Quests).ToArray();
                    var extra=chapterQuests.Quests.FirstOrDefault(q=>!main.Contains(q.Id)&&QuestLedger.DisplayStatus(activeSave,q)!=QuestLedger.StatusLocked);
                    if(extra!=null)add(string.IsNullOrEmpty(extra.GiverNpc)?"支":"令",extra.Title,QuestStatusName(QuestLedger.DisplayStatus(activeSave,extra)));
                }
                reflow();
            };
        }
        static QuestGoldenMarker.Notice ChapterBriefNotice(SaveData save,QuestTable quests,V2ChapterBrief brief)
        {
            var state=QuestGoldenMarker.Notice.None;
            if(save==null)return state;
            // No quest unread flag exists: available is the real, actionable new-task signal.
            if(quests!=null)foreach(var quest in quests.Quests)
            {
                var status=QuestLedger.DisplayStatus(save,quest);
                if(status==QuestLedger.StatusAvailable)state|=QuestGoldenMarker.Notice.AvailableQuest;
                if(status==QuestLedger.StatusActive&&!string.IsNullOrEmpty(quest.TurnInNpc)&&QuestLedger.ObjectivesMetReadOnly(save,quest))
                    state|=QuestGoldenMarker.Notice.CanAdvance;
            }
            if(ClueLedger.Of(save).Any(clue=>clue!=null&&!clue.read))state|=QuestGoldenMarker.Notice.UnreadClue;
            if(brief!=null&&brief.NextDefinition!=null&&brief.NextReady)state|=QuestGoldenMarker.Notice.CanAdvance;
            return state;
        }
        static string ChapterBriefNoticeText(QuestGoldenMarker.Notice state)
        {
            string text="";
            if((state&QuestGoldenMarker.Notice.AvailableQuest)!=0)text="有任务可接";
            if((state&QuestGoldenMarker.Notice.UnreadClue)!=0)text+=(text.Length==0?"":" · ")+"有新线索";
            if((state&QuestGoldenMarker.Notice.CanAdvance)!=0)text+=(text.Length==0?"":" · ")+"可推进";
            return text;
        }
        void HudCapacityRail(RectTransform parent,string name,float x,float y,float w,float h,Color stone)
        {
            // Sculpted translucent capacity jade, with quiet cloud light; no current-value or fill-ratio input.
            var rail=ui.Rect(parent,name,x,y,w,h).gameObject.AddComponent<GuofengHudStreamer>();
            rail.Configure(GuofengHudStreamer.Shape.Capacity,stone,()=>settings.reduceMotion);
        }
        GuofengHudStreamer HudSoftMist(RectTransform parent,string name,float x,float y,float w,float h)
        {
            var mist=ui.Rect(parent,name,x,y,w,h).gameObject.AddComponent<GuofengHudStreamer>();
            mist.Configure(GuofengHudStreamer.Shape.CaptionMist,UiKit.Hex("E2EEE4"));
            return mist;
        }
        static void FitHudMist(GuofengHudStreamer mist,TMP_Text label)
        {
            // Back only the actual text, retaining the original label box/font and the 2px glyph gate.
            var rect=mist.rectTransform;
            rect.sizeDelta=new Vector2(Mathf.Clamp(label.preferredWidth+72f,152f,524f),rect.sizeDelta.y);
        }
        bool CanUseHudDaoWheel() => page=="town" && activeSave!=null && modal==null && bagOverlay==null &&
            (dialoguePanel==null || !dialoguePanel.IsOpen) && dialogueLocks==(hudWheelInputHeld?1:0);
        void AcquireHudWheelInput()
        {
            if(hudWheelInputHeld)return;hudWheelInputHeld=true;dialogueLocks++;StopDialogueMovement();
        }
        void ReleaseHudWheelInput()
        {
            if(!hudWheelInputHeld)return;hudWheelInputHeld=false;dialogueLocks=Math.Max(0,dialogueLocks-1);StopDialogueMovement();
            dialogueConsumeThroughFrame=Math.Max(dialogueConsumeThroughFrame,Time.frameCount+1);
        }
        void ActivateHudDaoWheel(int slot)
        {
            if(!CanUseHudDaoWheel())return;
            Guard(()=>
            {
                switch(slot)
                {
                    case 0:ShowSettings();break;
                    case 1:ShowInventory();break;
                    case 2:ShowWorldOverlay();break;
                    case 3:OpenWorldCombatWheel();break;
                    case 4:if(PrologueRules.CanCultivate(activeSave))ShowMeridianLedger();else ShowCrafting();break;
                    case 5:if(PrologueRules.CanCultivate(activeSave))ShowArtsLedger();else ShowTownChapterPanel();break;
                    case 6:ShowDaoIdentityLedger();break;
                    case 7:repository.Write(activeSave);Notify("行迹已落卷。");break;
                }
            });
        }
        void ShowDaoIdentityLedger()
        {
            if(activeSave==null)return;
            var host=BeginImmortalLedger("Dao identity modal");if(host==null)return;
            var preset=presets.FirstOrDefault(p=>p.id==activeSave.characterId);
            var stats=PlayerStatsFor(activeSave.characterId);
            DaoIdentityView.Build(ui,host,activeSave,preset?.id,preset?.displayName??"来路待访",
                stats.hp+CultivationRules.HpBonus(activeSave)+ManualStudyRules.HpBonus(activeSave),
                stats.stamina+CultivationRules.StaminaBonus(activeSave)+ManualStudyRules.StaminaBonus(activeSave),
                stats.attack+ManualStudyRules.AttackBonus(activeSave),stats.defense,()=>settings.reduceMotion,CloseModal,PlayClick);
        }
        void ShowMeridianLedger() => BuildMeridianMasterpiece();
        void ShowArtsLedger() => BuildArtsMasterpiece();
        void ShowHudLedger(string title,string body)
        {
            if(page!="town"||activeSave==null||WorldDialogueInputBlocked)return;
            CloseInventory();CloseChapterPanel();CloseModal();CancelTravel();
            var group=content.GetComponent<CanvasGroup>();group.interactable=false;group.blocksRaycasts=false;
            modal=ui.Panel(canvas,"Hud ledger veil",0,0,1920,1080,new Color(0,.025f,.02f,.55f),true).rectTransform;
            var paper=DialogueSurface.CreatePaper(ui,modal,"Hud ledger paper",520,214,880,652);
            ui.Label(paper,title,50,38,780,65,38,UiKit.Ink,TextAlignmentOptions.Center,true);
            ui.Label(paper,body,70,138,740,380,27,UiKit.Ink);
            ui.Button(paper,"收起",332,562,216,60,CloseModal);
        }
    }
}
