using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using TwelveJade.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // M5-05 S2：把章进度、任务、线索、对话接到城镇页。
    // 这一层只做"入口与显示"：推进与结算一律调 Core，界面不许自己加好感、发奖励或改状态。
    // 所有表在这里统一加载一次并注入 Core 的静态入口（ChapterLedger.Schedule / Quests 等），
    // 免得每个界面各加载一份、出现两处真相。
    public sealed partial class FrontEndController
    {
        NpcTable chapterNpcs;
        QuestTable chapterQuests;
        DialogueTable chapterDialogues;
        ClueTable chapterClues;
        ChapterTable chapterTable;
        NpcScheduleTable chapterSchedules;

        RectTransform chapterPanel;
        TMP_Text chapterTitle, chapterBody, chapterLog;
        RectTransform chapterModal, chapterBodyHost;
        ScrollRect chapterScroll;
        V2ScrollSession chapterSession;
        Button chapterAction;
        public bool ChapterScrollOpenForCheck => ChapterModalAlive;
        bool ChapterModalAlive => page == "town" && chapterPanel != null && chapterModal == modal && chapterSession != null && !chapterSession.Closing;

        public ChapterTable Chapter => LoadTables() ? chapterTable : null;
        public QuestTable Quests => LoadTables() ? chapterQuests : null;
        public NpcTable TownNpcs => LoadTables() ? chapterNpcs : null;
        public bool TablesReady => LoadTables();
        public int ChapterReached => activeSave == null ? 0 : (int)ChapterLedger.Reached(activeSave);
        public int UnreadClues => ClueLedger.UnreadCount(activeSave);
        public int SettledChapterCount() => activeSave == null ? 0 : ChapterLedger.SettledCount(activeSave);
        public string SavedDialogueNode => activeSave?.dialogue?.nodeId ?? "";
        // 验收脚本要显式落盘：对话推进只改了内存里的对象，存档不写进去就等于没存。
        public void SaveChapterNow() => PersistChapter();

        /// 一次性加载全部表并注入 Core 的静态入口。任何一张坏了都不抛——返回 false，
        /// 由调用方给出"这版数据不完整"的可读提示，而不是让整个城镇页崩掉。
        bool LoadTables()
        {
            if (chapterTable != null) return true;
            try
            {
                chapterNpcs = NpcTable.Parse(Read("Config/npcs"));
                chapterQuests = QuestTable.Parse(Read("Config/quests"));
                chapterDialogues = DialogueTable.Parse(Read("Config/dialogues"));
                chapterClues = ClueTable.Parse(Read("Config/clues"));
                chapterTable = ChapterTable.Parse(Read("Config/chapters"));
                chapterSchedules = NpcScheduleTable.Parse(Read("Config/npc-schedules"));
                chapterDialogues.CrossCheck(chapterNpcs);
                chapterClues.CrossCheck(chapterNpcs);
                chapterTable.CrossCheck(chapterQuests);
                ChapterLedger.Schedule = chapterSchedules;
                ChapterLedger.Quests = chapterQuests;
                return true;
            }
            catch (FormatException ex)
            {
                Debug.LogWarning("配置表有问题：" + ex.Message);
                return false;
            }
        }

        static string Read(string path)
        {
            var asset = Resources.Load<TextAsset>(path);
            if (asset == null) throw new FormatException("缺少配置：" + path);
            return asset.text;
        }

        public void ShowTownChapterPanel()
        {
            if(ChapterModalAlive){chapterSession.RequestClose();return;}
            if (activeSave == null || page != "town" || WorldDialogueInputBlocked) return;
            if (!LoadTables()) { Notify("配置表有问题，这一页暂时打不开。"); return; }
            var returnFocus = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            var owner = activeSave; var ownerContent = content;
            CloseInventory(); CloseChapterPanel(); CloseModal(); StopDialogueMovement();
            var contentGroup = content.GetComponent<CanvasGroup>();
            contentGroup.interactable = false; contentGroup.blocksRaycasts = false;
            var host = ui.Rect(canvas, "Chapter scroll modal", 0, 0, 1920, 1080);
            chapterModal = modal = host;
            host.gameObject.AddComponent<V2FocusNavigation>();
            var veil = V2ScrollUi.Art(ui, host, "Chapter outside veil", V2ScrollOrnament.Shape.Veil, 0, 0, 1920, 1080, true);
            var outside = veil.gameObject.AddComponent<Button>(); outside.targetGraphic = veil;
            outside.transition = Selectable.Transition.None; outside.navigation = new Navigation { mode = Navigation.Mode.None };
            outside.onClick.AddListener(() => chapterSession?.RequestClose());
            // Restore the complete authored fairyland scroll at its native 2:1 aspect.
            chapterPanel = ui.Rect(host,"Chapter panel",40,80,1840,920);
            chapterPanel.anchorMin=chapterPanel.anchorMax=new Vector2(.5f,.5f);
            chapterPanel.pivot=new Vector2(.5f,.5f);chapterPanel.anchoredPosition=Vector2.zero;
            var mounting=ScrollPageArtComposition.Fairyland(ui,chapterPanel,"Chapter fairyland jade scroll",new Rect(0,0,1840,920),true);mounting.raycastTarget=true;
            AestheticUi.Text(ui,chapterPanel,"Chapter scroll title","青 石 · 天 机 行 卷",new Rect(430,90,970,64),34,TextAlignmentOptions.Center,ImmortalUi.Pale);
            var tabs=new[]{("眼前事",(UnityAction)ShowSegmentPanel),("镇民",(UnityAction)ShowResidentsPanel),
                ("线索",(UnityAction)ShowCluePanel),("货郎",(UnityAction)ShowMerchantPanel)};
            Button first=null;
            for(int i=0;i<tabs.Length;i++)
            {
                var entry=tabs[i];var button=AestheticUi.Jade(ui,chapterPanel,"Tab "+entry.Item1,entry.Item1,
                    new Rect(424+i*251,190,230,70),entry.Item2,()=>settings.reduceMotion,PlayClick);if(first==null)first=button;
            }
            chapterTitle=ImmortalUi.Text(ui,chapterPanel,"Chapter current heading","",430,284,960,62,34,ImmortalUi.Ink,false);
            var viewport=ui.Rect(chapterPanel,"Chapter detail viewport",423,362,983,268);
            viewport.gameObject.AddComponent<RectMask2D>();
            var hit=viewport.gameObject.AddComponent<Image>();hit.color=Color.clear;
            chapterScroll=viewport.gameObject.AddComponent<ScrollRect>();chapterScroll.viewport=viewport;
            chapterScroll.horizontal=false;chapterScroll.vertical=true;chapterScroll.movementType=ScrollRect.MovementType.Clamped;
            chapterScroll.inertia=!settings.reduceMotion;chapterScroll.scrollSensitivity=38;
            chapterBodyHost=ui.Rect(viewport,"Chapter readable content",0,0,970,313);chapterScroll.content=chapterBodyHost;
            chapterBody=ImmortalUi.Text(ui,chapterBodyHost,"Chapter real ledger","",8,0,954,313,26,ImmortalUi.Ink);
            chapterLog=ImmortalUi.Text(ui,chapterPanel,"Chapter ledger totals","",430,724,754,66,22,ImmortalUi.Pale);
            chapterAction=AestheticUi.Jade(ui,chapterPanel,"Chapter action","",new Rect(424,800,660,70),null,()=>settings.reduceMotion);chapterAction.gameObject.SetActive(false);
            var close=AestheticUi.Jade(ui,chapterPanel,"Chapter close","回卷",new Rect(1194,800,214,70),()=>chapterSession?.RequestClose(),()=>settings.reduceMotion,PlayClick);
            ImmortalUi.Text(ui,chapterPanel,"Chapter close hint","滚轮阅卷 · Esc / 卷外收起",992,874,414,45,21,ImmortalUi.Pale,false,TextAlignmentOptions.Right);
            chapterPanel.gameObject.AddComponent<QuestScrollReveal>().Configure(ui,chapterPanel);
            chapterSession = host.gameObject.AddComponent<V2ScrollSession>();
            chapterSession.Configure(chapterPanel, () => settings.reduceMotion,
                () => dialogueConsumeThroughFrame = Math.Max(dialogueConsumeThroughFrame, Time.frameCount + 1),
                () => { if (modal == host) CloseModal(); },
                () =>
                {
                    if (ReferenceEquals(chapterModal, host))
                    {
                        chapterModal = null; chapterPanel = null; chapterBodyHost = null; chapterScroll = null;
                        chapterSession = null; chapterTitle = chapterBody = chapterLog = null; chapterAction = null;
                    }
                    if (isActiveAndEnabled) StartCoroutine(RestoreChapterFocus(ownerContent, owner, returnFocus));
                });
            ShowSegmentPanel();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(first.gameObject);
        }

        IEnumerator RestoreChapterFocus(RectTransform ownerContent, SaveData owner, GameObject previous)
        {
            yield return null; yield return null;
            if (page != "town" || content != ownerContent || !ReferenceEquals(activeSave, owner) || modal != null || WorldDialogueInputBlocked) yield break;
            var selectable = previous != null ? previous.GetComponent<Selectable>() : null;
            if (selectable != null && selectable.IsActive() && selectable.IsInteractable() && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(previous);
        }

        public void CloseChapterPanel()
        {
            var host = chapterModal;
            if (host != null && modal == host) CloseModal();
            else if (host != null) { host.gameObject.SetActive(false); Destroy(host.gameObject); }
        }

        void ResetChapterBody(string title)
        {
            if (!ChapterModalAlive) return;
            chapterTitle.text = title; chapterLog.text = ""; chapterBody.text = "";
            foreach (Transform child in chapterBodyHost)
                if (child != chapterBody.transform) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            chapterAction.onClick.RemoveAllListeners(); chapterAction.gameObject.SetActive(false);
            chapterScroll.StopMovement(); chapterScroll.verticalNormalizedPosition = 1;
        }
        void FitChapterBody(float minimum = 313)
        {
            if (!ChapterModalAlive) return;
            float height = Mathf.Max(minimum, chapterBody.GetPreferredValues(chapterBody.text, 954, float.PositiveInfinity).y + 12);
            chapterBody.rectTransform.sizeDelta = new Vector2(954, height);
            chapterBodyHost.sizeDelta = new Vector2(970, height);
            chapterScroll.verticalNormalizedPosition = 1;
        }
        void ChapterAction(string caption, UnityAction action)
        {
            chapterAction.GetComponentInChildren<TMP_Text>().text = caption;
            chapterAction.gameObject.SetActive(true); chapterAction.onClick.AddListener(PlayClick); chapterAction.onClick.AddListener(action);
        }
        V2ChapterBrief ChapterBrief()
        {
            // This owner also serves the HUD summary. Attach once after its existing factory has built it.
            // Keep HUD/controller/wheel sources outside this page's write set.
            var briefHost = content != null ? content.Find("Hanging quest silk") as RectTransform : null;
            if (briefHost != null && briefHost.GetComponent<ChapterFairylandHudArt>() == null)
                briefHost.gameObject.AddComponent<ChapterFairylandHudArt>().Configure(ui, briefHost);
            return V2ChapterBrief.Build(activeSave, chapterTable, chapterQuests, chapterSchedules);
        }

        public void ShowSegmentPanel()
        {
            if (!ChapterModalAlive) return;
            var brief = ChapterBrief(); ResetChapterBody(brief.Title); chapterBody.text = brief.Details; chapterLog.text = brief.Footer; FitChapterBody();
            BuildQuestEntries(brief);
            var next = brief.NextDefinition;
            if (next != null && brief.NextReady && !string.IsNullOrEmpty(next.EncounterId))
                ChapterAction("前往 · " + next.Title, () => CloseChapterThen(() =>
                {
                    var result = ChapterLedger.Enter(activeSave, chapterTable, next.Segment, activeSave.WorldTimeNow(), out var error);
                    if (!result.Entered) { Notify(error); return; }
                    PersistChapter(); ShowCombat(next.EncounterId);
                }));
        }
        void BuildQuestEntries(V2ChapterBrief brief)
        {
            chapterBody.text="";var entries=new List<QuestEntryView>();
            Action reflow=()=>
            {
                float y=0;foreach(var entry in entries){((RectTransform)entry.transform).anchoredPosition=new Vector2(8,-y);y+=entry.Height+12;}
                chapterBodyHost.sizeDelta=new Vector2(970,Mathf.Max(313,y));
            };
            Action<string,string,string> add=(kind,title,details)=>
            {
                var row=ui.Rect(chapterBodyHost,"Chapter quest entry "+entries.Count,8,0,954,78);var view=row.gameObject.AddComponent<QuestEntryView>();
                view.Build(ui,row,kind,title,details,()=>settings.reduceMotion,PlayClick,reflow);entries.Add(view);
            };
            add("主",brief.Title,brief.Progress+"\n"+brief.Next);
            var main=chapterTable.Segments.SelectMany(segment=>segment.Quests).ToArray();
            foreach(var quest in chapterQuests.Quests)
            {
                string status=QuestLedger.DisplayStatus(activeSave,quest);if(status!=QuestLedger.StatusAvailable&&status!=QuestLedger.StatusActive&&status!=QuestLedger.StatusCompleted&&status!=QuestLedger.StatusFailed)continue;
                string progress=string.Join("\n",quest.Objectives.Select(o=>
                    (o.Type=="reach"?"抵达":o.Type=="talk"?"交谈":o.Type=="collect"?"收集":"目标")+"  "+
                    Math.Min(Math.Max(1,o.Count),QuestLedger.Progress(activeSave,quest,o.Id))+" / "+Math.Max(1,o.Count)));
                add(main.Contains(quest.Id)?"主":string.IsNullOrEmpty(quest.GiverNpc)?"支":"令",quest.Title,QuestStatusName(status)+"\n"+progress);
            }
            reflow();
        }
        void ShowResidentsPanel()
        {
            if (!ChapterModalAlive) return;
            ResetChapterBody("镇民 · 故人可访");
            chapterBody.text = "择一位镇民，收卷后循真实道路前往。近处可直接交谈；未到身边不会隔空开启对话。";
            var residents = town.Npcs.ToArray();
            for (int i=0;i<residents.Length;i++)
            {
                var npc = residents[i];
                AestheticUi.Jade(ui,chapterBodyHost,"Chapter visit "+npc.Id,"寻访 · "+npc.Name,new Rect(8+(i%2)*478,108+(i/2)*92,454,76),
                    ()=>ShowNpcPanel(npc.Id),()=>settings.reduceMotion,PlayClick,25);
            }
            chapterLog.text = "当前镇民 " + residents.Length + " 位 · 位置随真实日程而变";
            FitChapterBody(108+((residents.Length+1)/2)*92);
        }
        void ShowMerchantPanel()
        {
            if (!ChapterModalAlive) return;
            ResetChapterBody("货郎 · 行路买卖");
            var merchant = town.Npcs.FirstOrDefault(n => n.Id == "huolang");
            chapterBody.text = merchant == null ? "此刻镇中没有可寻访的货郎。" : merchant.Name + "\n\n循路前往货郎身边，再依真实对话与交易条件开市。日程、货品、价格和夜访门槛沿用当前行卷。";
            chapterLog.text = "不替玩家跨越距离、日程或交易条件。";FitChapterBody();
            if (merchant != null) ChapterAction("寻访 · " + merchant.Name, () => ShowNpcPanel(merchant.Id));
        }
        public void ShowNpcPanel(string npcId)
        {
            if (!ChapterModalAlive || !town.Npcs.Any(n => n.Id == npcId)) return;
            CloseChapterThen(() => RequestNpcDialogue(npcId));
        }
        void CloseChapterThen(Action action)
        {
            if (!ChapterModalAlive) return;
            var owner = activeSave; var ownerContent = content;
            chapterSession.RequestClose(() => StartCoroutine(AfterChapterClose(ownerContent, owner, action)));
        }
        IEnumerator AfterChapterClose(RectTransform ownerContent, SaveData owner, Action action)
        {
            yield return null; yield return null;
            if (content == ownerContent && page == "town" && ReferenceEquals(activeSave, owner) && modal == null && !WorldDialogueInputBlocked)
                action?.Invoke();
        }
        public void ShowCluePanel()
        {
            if (!ChapterModalAlive) return;
            ResetChapterBody("线索 · 所知所见");
            var known = ClueLedger.Of(activeSave).Where(e => e != null).ToList();
            var lines = new List<string>();
            foreach (var entry in known)
            {
                var def = chapterClues.Find(entry.id); if (def == null) continue;
                lines.Add(def.Title + ReliabilityLabel(def.Reliability) + (entry.read ? "" : "（新）")); lines.Add(def.Text + "\n");
            }
            chapterBody.text = lines.Count == 0 ? "还没有录入见闻。可向镇民打听，亦可留心他们在忙什么。" : string.Join("\n", lines);
            chapterLog.text = "已知 " + known.Count + " 条 · 未读 " + UnreadClues + " 条"; FitChapterBody();
            if (known.Any(e => !e.read)) ChapterAction("已阅此卷线索", () =>
            {
                foreach (var entry in known) ClueLedger.MarkRead(activeSave, entry.id);
                PersistChapter(); ShowCluePanel();
            });
        }
        static string ReliabilityLabel(int reliability) => reliability >= 2 ? "（亲见）" : reliability == 1 ? "（听说）" : "（道听途说）";
        void Say(string message) { if (ChapterModalAlive && chapterLog != null) chapterLog.text = message; }
        void PersistChapter() => Guard(() => repository.Write(activeSave));
        static string QuestStatusName(string status) => V2ChapterBrief.StatusName(status);
    }
}
