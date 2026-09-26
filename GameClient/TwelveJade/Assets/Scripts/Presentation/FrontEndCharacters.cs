using System;
using System.Linq;
using TMPro;
using TwelveJade.Core;
using UnityEngine;

namespace TwelveJade.Presentation
{
    public sealed partial class FrontEndController
    {
        public void ShowSlots(bool creating)
        {
            BeginPage(creating ? "new-game" : "slots");
            PageHeading("行 迹  /  三 卷 人 生", creating ? "择一卷，启新程" : "故人行迹",
                creating ? "选择空白档位，写下你的故事。已有行迹会完整保留。" : "每一卷都是独立的人生。可继续旅程，也可整理旧卷。");
            foreach (var slot in repository.ReadAll())
            {
                var x = 100 + (slot.Slot - 1) * 575;
                var card = ui.Panel(content, "Slot " + slot.Slot, x, 342, 545, 540, new Color(.045f, .115f, .10f, .96f), true);
                ui.Label(card.transform, "第 " + new[] { "一", "二", "三" }[slot.Slot - 1] + " 卷", 32, 30, 450, 45, 23, UiKit.Gold);
                ui.Panel(card.transform, "Rule", 32, 91, 481, 1, UiKit.Jade);
                string title, description;
                if (slot.CanLoad)
                {
                    var preset = presets.FirstOrDefault(p => p.id == slot.Data.characterId);
                    title = slot.Data.characterName;
                    description = (preset != null ? preset.displayName : "未知外观") + "\n停留于：行旅小憩\n\n" +
                        "上次归来\n" + DateTimeOffset.Parse(slot.Data.updatedUtc).ToLocalTime().ToString("yyyy.MM.dd  HH:mm");
                    if (slot.State == SlotState.Recovered) description += "\n\n主记录损坏，将从备份恢复。";
                }
                else if (slot.State == SlotState.Empty) { title = "尚未落笔"; description = "山河辽阔，前路未定。\n这一卷，等你写下第一笔。"; }
                else if (slot.State == SlotState.FutureVersion) { title = "来自未来的行迹"; description = "该档位由更新版本创建。\n请使用更新的游戏版本打开。"; }
                else { title = "行迹暂不可读"; description = "档位和备份均无法读取。\n可保留等待修复，或删除后重新开始。"; }
                ui.Label(card.transform, title, 32, 120, 475, 65, 37, UiKit.Paper);
                ui.Label(card.transform, description, 34, 210, 472, 215, 22, UiKit.Muted);
                if (slot.State == SlotState.Empty)
                    ui.Button(card.transform, "写下新的人生", 32, 450, 481, 62, () => ShowCharacterCreation(slot.Slot), true);
                else
                {
                    ui.Button(card.transform, "继续", 32, 450, 292, 62, () => LoadSlot(slot), true, slot.CanLoad);
                    ui.Button(card.transform, "删除", 340, 450, 173, 62, () => Confirm("删除第 " + slot.Slot + " 卷？",
                        "这会移除该档位和它的备份。删除后无法恢复，其他档位不受影响。", "确认删除", () =>
                        { repository.Delete(slot.Slot); ShowSlots(creating); Notify("此卷已清空。"); }));
                }
            }
            ui.Label(content, "记录保存在本机。外观选择暂不影响人物能力与剧情。", 102, 926, 1500, 40, 21, UiKit.Muted);
            FocusFirst();
        }

        public void ShowCharacterCreation(int slot, int selected = 0, string playerName = "无名")
        {
            BeginPage("character-creation");
            PageHeading("众 生  /  初 见", "你从人间来", "每一个普通人，都有自己的来路。择一身行装，赴一程山河。");
            var preset = presets[selected];
            for (var i = 0; i < presets.Length; i++)
            {
                var index = i;
                ui.Button(content, presets[i].displayName, 102 + i * 269, 337, 247, 60,
                    () => ShowCharacterCreation(slot, index, nameInput.text), i == selected);
            }
            var artPanel = ui.Panel(content, "Character portrait", 102, 418, 785, 512, new Color(.78f, .76f, .65f, .97f));
            ShowPortrait(artPanel.transform, preset, 0, 20, 15, 745, 480);
            ui.Panel(content, "Character details", 949, 336, 873, 594, new Color(.045f, .115f, .10f, .96f));
            ui.Label(content, "平 民 出 身", 998, 372, 715, 40, 19, UiKit.Gold);
            ui.Label(content, preset.displayName, 991, 430, 724, 65, 45, UiKit.Paper);
            ui.Label(content, preset.description, 998, 516, 748, 115, 27, UiKit.Paper);
            ui.Label(content, preset.detail, 998, 650, 740, 75, 21, UiKit.Muted);
            ui.Label(content, "你的名字", 998, 746, 200, 40, 22, UiKit.Gold);
            nameInput = ui.Input(content, playerName, 998, 793, 407);
            ui.Button(content, "落笔 · 创建行迹", 1432, 793, 342, 62, () => Guard(() =>
            {
                activeSave = repository.Create(slot, preset.id, nameInput.text);
                ShowPreview(activeSave);
            }), true);
            ui.Label(content, "名字可用中文，最多十六字。此时只确定外观与称呼。", 998, 874, 750, 40, 20, UiKit.Muted);
            FocusFirst();
        }

        TMP_InputField nameInput;

        void ShowPortrait(Transform parent, CharacterPreset preset, int facing, float x, float y, float w, float h)
        {
            var texture = preset.Facing(facing);
            if (texture != null) ui.Art(parent, texture, x, y, w, h, facing == 3);
            else
            {
                // The explicit development fallback is removed automatically when final art is imported.
                ui.Label(parent, preset.displayName, x, y + h * .32f, w, 80, 50, UiKit.Ink, TextAlignmentOptions.Center);
                ui.Label(parent, "造型画稿待接入", x, y + h * .55f, w, 45, 24, UiKit.Ink, TextAlignmentOptions.Center);
            }
        }

        public void LoadSlot(SlotInfo slot)
        {
            if (slot == null || !slot.CanLoad) { Notify("暂无可继续的行迹。"); return; }
            Guard(() =>
            {
                if (!presets.Any(p => p.id == slot.Data.characterId))
                    throw new InvalidOperationException("该角色的外观资源暂未找到，档位已保留。");
                activeSave = slot.Data;
                if (slot.State == SlotState.Recovered) repository.Write(activeSave);
                ShowPreview(activeSave);
                if (slot.State == SlotState.Recovered) Notify("已从上一份有效备份恢复行迹。");
            });
        }

        public void ShowPreview(SaveData data)
        {
            BeginPage("preview"); activeSave = data;
            var preset = presets.First(p => p.id == data.characterId);
            PageHeading("行 旅 小 憩", data.characterName + "的行装", "一身寻常衣衫，一段尚未展开的人生。");
            var art = ui.Panel(content, "Turnaround", 102, 336, 920, 593, new Color(.78f, .76f, .65f, .97f));
            ShowPortrait(art.transform, preset, data.facing, 80, 15, 760, 516);
            string[] directions = { "正面", "右侧", "背面", "左侧" };
            for (var i = 0; i < 4; i++)
            {
                var direction = i;
                ui.Button(art.transform, directions[i], 43 + i * 220, 520, 176, 51, () => Guard(() =>
                {
                    var previous = data.facing;
                    data.facing = direction;
                    try { repository.Write(data); }
                    catch { data.facing = previous; throw; }
                    ShowPreview(data);
                }), i == data.facing);
            }
            ui.Panel(content, "Biography", 1074, 336, 746, 593, new Color(.045f, .115f, .10f, .96f));
            ui.Label(content, "第 " + data.slot + " 卷  /  已落笔", 1117, 373, 650, 40, 19, UiKit.Gold);
            ui.Label(content, preset.displayName, 1110, 436, 650, 70, 46, UiKit.Paper);
            ui.Label(content, preset.description, 1117, 541, 625, 135, 28, UiKit.Paper);
            ui.Label(content, "此刻，故事尚未启程。\n先记住这身行装，以及来时的路。", 1117, 702, 625, 90, 24, UiKit.Muted);
            ui.Button(content, "返回主菜单", 1117, 817, 310, 66, ShowMenu, true);
            ui.Button(content, "查看行迹", 1456, 817, 310, 66, () => ShowSlots(false));
            FocusFirst();
        }
    }
}
