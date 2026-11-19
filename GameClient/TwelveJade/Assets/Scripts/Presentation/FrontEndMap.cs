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
        void BuildTownHud() => BuildGuofengHud();

        // 取景表是 Config 下的 TextAsset(运行时不读像素,见 build-portrait-frames.py)。
        static string PortraitFrameConfig()
        {
            var asset = Resources.Load<TextAsset>("Config/portrait-frames");
            return asset != null ? asset.text : null;
        }

        // 键 = {id}-{gender}-{faceStyle}。缺失时回退到 {id}-male-0,与 CharacterPreset.Facing 的回退一致。
        static string FaceFrameKey(CharacterPreset preset, SaveData save) =>
            preset.id + "-" + save.gender + "-" + save.faceStyle;

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
