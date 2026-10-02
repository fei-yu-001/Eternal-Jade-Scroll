using TMPro;
using TwelveJade.Core;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    // 城镇页的界面外壳（状态 HUD + 版本/指纹自证信息）。
    // 世界渲染、摄像机、玩家、纵深实体在 FrontEndWorld.cs；本文件只管"屏幕上的框"。
    public sealed partial class FrontEndController
    {
        static readonly string[] HourBranches = { "子", "丑", "寅", "卯", "辰", "巳", "午", "未", "申", "酉", "戌", "亥" };

        // 左上状态条：头像/生命/精力/铜钱/行历。数据取自已有 Core（战斗同源）。
        void BuildTownHud()
        {
            var preset = System.Array.Find(presets, p => p.id == activeSave.characterId) ?? presets[0];
            var stats = PlayerStatsFor(activeSave.characterId);
            var hud = ui.Panel(content, "Town hud", 88, 26, 700, 130, new Color(.06f, .1f, .08f, .72f));
            var portrait = ui.Rect(hud.transform, "Hud portrait", 14, 12, 94, 94);
            var portraitImage = portrait.gameObject.AddComponent<RawImage>();
            var face = preset.Facing(0, activeSave.gender, activeSave.faceStyle);
            portraitImage.texture = face != null ? face : Texture2D.whiteTexture;
            portraitImage.uvRect = new Rect(.32f, .5f, .36f, .44f);   // 裁头肩段
            ui.Label(hud.transform, activeSave.characterName, 126, 12, 264, 36, 24, UiKit.Paper,
                TextAlignmentOptions.TopLeft, true);
            ui.Label(hud.transform, preset.displayName, 126, 52, 264, 28, 17, UiKit.Gold);
            var hpBack = ui.Panel(hud.transform, "Hp back", 126, 86, 270, 16, new Color(.2f, .12f, .1f, .95f));
            ui.Panel(hpBack.transform, "Hp fill", 2, 2, 266, 12, new Color(.7f, .22f, .16f, .96f));
            ui.Label(hud.transform, stats.hp + "/" + stats.hp, 402, 84, 90, 20, 14,
                new Color(.95f, .8f, .75f, .95f), TextAlignmentOptions.MidlineLeft);
            var spBack = ui.Panel(hud.transform, "Sp back", 126, 108, 270, 8, new Color(.16f, .17f, .12f, .95f));
            ui.Panel(spBack.transform, "Sp fill", 2, 2, 266, 4, new Color(.85f, .68f, .3f, .96f));
            var now = activeSave.WorldTimeNow();
            var branch = HourBranches[(now.minuteOfDay / 60 + 1) / 2 % 12];
            ui.Label(hud.transform, "铜钱 " + activeSave.coins + " 文", 500, 12, 184, 30, 18, UiKit.Gold,
                TextAlignmentOptions.TopLeft);
            ui.Label(hud.transform, "第 " + now.day + " 日 · " + branch + "时", 500, 46, 184, 26, 15, UiKit.Paper,
                TextAlignmentOptions.TopLeft);
            ui.Label(hud.transform, town.Name, 500, 76, 184, 32, 20, UiKit.Paper, TextAlignmentOptions.TopLeft, true);

            ui.Button(content, "行囊", 1408, 26, 200, 52, ShowInventory);
            ui.Button(content, "返回主菜单", 1620, 26, 200, 52, ShowMenu, true);
        }

        // 构建版本戳 + 贴图指纹：左下角自证"跑的是不是最新代码、加载的是不是最新贴图"。
        // "改了没变化"这类反馈没法自证，屏幕上看得到版本号与尺寸，对不上就是没跑到最新。
        void BuildVersionStamp(Texture2D travelerView)
        {
            var asset = Resources.Load<TextAsset>("Config/build-stamp");
            var text = "build " + (asset == null ? "?" : asset.text.Trim());
            if (travelerView != null)
            {
                text += $"　{travelerView.name} {travelerView.width}x{travelerView.height}";
                try
                {
                    var pixels = travelerView.GetPixels();   // 自下而上：后 70% = 画面上方
                    long sum = 0;
                    for (var i = (int)(pixels.Length * .30f); i < pixels.Length; i++)
                        sum += (long)(pixels[i].a * 255f);
                    text += $" #{sum % 1000000}";
                }
                catch (System.Exception) { text += " #unreadable"; }
            }
            var world = town != null
                ? $"　world {town.WorldWidth:0}x{town.WorldHeight:0} · {town.Chunks.Count} 块 · 视野 {ViewWorldSize.x:0}x{ViewWorldSize.y:0}"
                : "";
            ui.Label(content, text + world, 24, 1024, 1100, 34, 18, new Color(.62f, .66f, .6f, .85f),
                TextAlignmentOptions.BottomLeft);
        }
    }
}
