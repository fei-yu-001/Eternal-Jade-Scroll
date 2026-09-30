using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using TwelveJade.Core;
using UnityEngine;
using UnityEngine.Events;
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
        readonly List<(string label, UnityAction action)> chapterTabs = new();

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
            if (!LoadTables())
            {
                Notify("配置表有问题，这一页暂时打不开。");
                return;
            }
            CloseChapterPanel();
            chapterTabs.Clear();
            chapterTabs.Add(("眼前事", ShowSegmentPanel));
            chapterTabs.Add(("镇民", () => ShowNpcPanel("yaoshi")));
            chapterTabs.Add(("线索", ShowCluePanel));
            chapterTabs.Add(("货郎", () => ShowNpcPanel("huolang")));

            chapterPanel = ui.Panel(content, "Chapter panel", 1240, 336, 580, 594,
                new Color(.045f, .115f, .10f, .97f), true).rectTransform;
            var tabs = new RectTransform[chapterTabs.Count];
            for (var i = 0; i < chapterTabs.Count; i++)
            {
                var label = chapterTabs[i].label;
                var action = chapterTabs[i].action;
                tabs[i] = ui.Rect(chapterPanel, "Tab " + label, 24 + i * 132, 20, 124, 48);
                var background = tabs[i].gameObject.AddComponent<Image>();
                background.sprite = UiKit.RoundedSprite();
                background.type = Image.Type.Sliced;
                background.color = new Color(.12f, .25f, .22f, .92f);
                background.raycastTarget = true;
                var button = tabs[i].gameObject.AddComponent<Button>();
                button.targetGraphic = background;
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(action);
                ui.Label(tabs[i], label, 0, 0, 124, 48, 20, UiKit.Paper, TextAlignmentOptions.Center);
            }
            chapterTitle = ui.Label(chapterPanel, "", 24, 86, 532, 44, 28, UiKit.Gold, TextAlignmentOptions.TopLeft, true);
            chapterBody = ui.Label(chapterPanel, "", 24, 140, 532, 300, 21, UiKit.Paper);
            chapterLog = ui.Label(chapterPanel, "", 24, 452, 532, 110, 19, UiKit.Muted);
            ShowSegmentPanel();
        }

        public void CloseChapterPanel()
        {
            if (chapterPanel == null) return;
            Destroy(chapterPanel.gameObject);
            chapterPanel = null;
        }

        // 眼前事：当前段 + 能不能往前推。
        public void ShowSegmentPanel()
        {
            if (chapterPanel == null) return;
            var segment = ChapterLedger.Reached(activeSave);
            var def = chapterTable.Find(segment);
            chapterTitle.text = def.Title;
            var lines = new List<string> { def.Summary };
            lines.Add("");
            lines.Add("进度：" + ((int)segment + 1) + " / " + chapterTable.Segments.Count);
            foreach (var quest in chapterQuests.Quests
                         .Where(q => q.Chapter == (int)segment || chapterTable.Find(segment).RequiresQuests.Contains(q.Id)))
                lines.Add("· " + quest.Title + "　" + QuestStatusName(QuestLedger.StatusOf(activeSave, quest.Id)));
            var next = (ChapterSegment)((int)segment + 1);
            if ((int)next <= (int)ChapterSegment.Departure)
            {
                var can = ChapterLedger.CanEnter(activeSave, chapterTable, next, chapterQuests);
                lines.Add("");
                lines.Add(can
                    ? "下一步：" + chapterTable.Find(next).Title + "（可以去了）"
                    : "下一步：" + chapterTable.Find(next).Title + "（还差些条件）");
            }
            chapterBody.text = string.Join("\n", lines.ToArray());
            chapterLog.text = "已经了结 " + ChapterLedger.SettledCount(activeSave) + " 段 · " +
                               "线索 " + ClueLedger.Of(activeSave).Count() + " 条（未读 " + UnreadClues + "）";
        }

        // 镇民：对话面板。推进节点只调 DialogueRules，Esc 存档续上。
        public void ShowNpcPanel(string npcId)
        {
            if (chapterPanel == null) return;
            var npc = chapterNpcs.Find(npcId);
            if (npc == null) { chapterBody.text = "镇上没有这个人。"; return; }
            var view = DialogueRules.Resume(activeSave, chapterDialogues, npcId, out _);
            RenderDialogue(npc, view);
        }

        void RenderDialogue(NpcDef npc, DialogueView view)
        {
            chapterTitle.text = npc.Name;
            chapterBody.text = view.Text;
            // 旧选项先停用再重建：Destroy 是延迟的，同帧 Find 还能捞到旧面板，
            // 断言只有一组选项就会假失败。SetActive(false) 立刻生效。
            var old = chapterPanel.Find("Dialogue options");
            if (old != null) old.gameObject.SetActive(false);
            var options = ui.Rect(chapterPanel, "Dialogue options", 24, 360, 532, 120);
            if (view.EndsConversation)
            {
                ui.Label(options, "（他说完了。）", 0, 0, 532, 30, 19, UiKit.Muted);
                var leave = ui.Rect(options, "Dialogue leave", 0, 44, 200, 52);
                var background = leave.gameObject.AddComponent<Image>();
                background.sprite = UiKit.RoundedSprite();
                background.type = Image.Type.Sliced;
                background.color = new Color(.12f, .25f, .22f, .92f);
                background.raycastTarget = true;
                var button = leave.gameObject.AddComponent<Button>();
                button.targetGraphic = background;
                button.onClick.AddListener(() => { Say("他记下了。"); ShowSegmentPanel(); });
                ui.Label(leave, "告辞", 0, 0, 200, 52, 21, UiKit.Paper, TextAlignmentOptions.Center);
                return;
            }
            for (var i = 0; i < view.Options.Count; i++)
            {
                var index = i;
                var slot = ui.Rect(options, "Option " + i, 0, i * 56, 532, 48);
                var background = slot.gameObject.AddComponent<Image>();
                background.sprite = UiKit.RoundedSprite();
                background.type = Image.Type.Sliced;
                background.color = new Color(.12f, .25f, .22f, .92f);
                background.raycastTarget = true;
                var button = slot.gameObject.AddComponent<Button>();
                button.targetGraphic = background;
                button.onClick.AddListener(() =>
                {
                    var next = DialogueRules.Choose(activeSave, chapterDialogues, chapterNpcs,
                        chapterClues, view, index, activeSave.WorldTimeNow(), out var notice);
                    PersistChapter();
                    if (!string.IsNullOrEmpty(notice)) Say(notice);
                    // 对话存进存档：Esc 关掉面板再开，从同一节点续上。
                    DialogueRules.Save(activeSave, DialogueRules.Snapshot(activeSave, next, activeSave.WorldTimeNow()));
                    RenderDialogue(npc, next);
                });
                ui.Label(slot, view.Options[i].Text, 14, 0, 504, 48, 20, UiKit.Paper, TextAlignmentOptions.MidlineLeft);
            }
        }

        // 线索：已知的、还没读的。
        public void ShowCluePanel()
        {
            if (chapterPanel == null) return;
            chapterTitle.text = "线 索";
            var known = ClueLedger.Of(activeSave).ToList();
            if (known.Count == 0)
            {
                chapterBody.text = "还什么都不知道。跟人打听打听，或者看看他们在忙什么。";
                chapterLog.text = "";
                return;
            }
            var lines = new List<string>();
            foreach (var entry in known)
            {
                var def = chapterClues.Find(entry.id);
                if (def == null) continue;
                lines.Add("· " + def.Title + ReliabilityLabel(def.Reliability) +
                          (entry.read ? "" : "　（新）"));
                lines.Add("　" + def.Text);
            }
            chapterBody.text = string.Join("\n", lines.ToArray());
            chapterLog.text = "共 " + known.Count + " 条，未读 " + UnreadClues + " 条。看过的不再算新。";
        }

        static string ReliabilityLabel(int reliability) =>
            reliability >= 2 ? "　（亲见）" : reliability == 1 ? "　（听说）" : "　（道听途说）";

        void Say(string message)
        {
            if (chapterLog != null) chapterLog.text = message;
        }

        void PersistChapter()
        {
            Guard(() => repository.Write(activeSave));
        }

        static string QuestStatusName(string status) => status switch
        {
            QuestLedger.StatusCompleted => "已了结",
            QuestLedger.StatusActive => "在做",
            QuestLedger.StatusAvailable => "可接",
            QuestLedger.StatusFailed => "办砸了",
            _ => "还没到时候",
        };
    }
}
