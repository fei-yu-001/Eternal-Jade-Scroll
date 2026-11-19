using System;
using TMPro;
using TwelveJade.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    public static class ArtsCodexArt
    {
        public static readonly Color Ivory=UiKit.Hex("F2E5C5"),Gold=UiKit.Hex("E6C27D"),Bronze=UiKit.Hex("C9A16A"),
            Jade=UiKit.Hex("79D1BA"),Violet=UiKit.Hex("C6A1E8"),DeepJade=UiKit.Hex("102D2A"),Muted=UiKit.Hex("BCC7B3");
        public static string TabId=>ApprovedUiArt.Has("nov8/arts-tab-vr")?"nov8/arts-tab-vr":"nov8/ledger-tab";
        public static string ChoiceId=>ApprovedUiArt.Has("nov8/arts-choice-vr")?"nov8/arts-choice-vr":"nov8/ledger-tab";
        public static readonly string[] Categories={"主动招式","玄门神通","先天心法"};
        public static readonly string[] Names={"轻击","重击","闪避","御剑术","五雷正法","清心诀"};
        public static readonly string[] Descriptions={
            "顺势出手，轻灵试探。\n精力充盈时可施展。","蓄力破势，待隙而进。\n强攻所需精力更多。","移步避锋，气定神闲。\n进退时机，由你掌握。",
            "意随剑行，剑气出岫。\n习得后可在战斗轮盘施放剑气。","五气朝元，雷印镇邪。\n习得后可锁定落点施放五雷。","抱元守一，清心归元。\n习得后可施放清心恢复气血。"};
        public static Color SymbolColor(int index)=>index==4?UiKit.Hex("AAC9F4"):index==5?Jade:Gold;
        public static readonly string[] SigilIds={"nov8/arts-sigil-light-vr","nov8/arts-sigil-heavy-vr","nov8/arts-sigil-dodge-vr","nov8/arts-sigil-sword-vr","nov8/arts-sigil-thunder-vr","nov8/arts-sigil-clarity-vr"};
        public static Texture2D Sigil(int index)
        {
            if(index<0||index>=SigilIds.Length)return null;
            return ApprovedUiArt.Load(SigilIds[index]);
        }
        public static int Category(int index)=>index<3?0:index<5?1:2;
        public static string Grade(ItemTable table,int index)
        {var skill=ManualStudyRules.Skills[index];return skill.Basic?"入门":table.FindTier(table.Find(skill.ManualId)?.Tier)?.Name??"未定品阶";}
        public static Color GradeColor(string grade)=>grade=="天阶"?Gold:grade=="地阶"?Violet:grade=="玄阶"?Jade:Bronze;
        public static TMP_Text Text(UiKit ui,Transform parent,string name,string text,Rect box,float size=24,int lines=1,
            Color? ink=null,TextAlignmentOptions align=TextAlignmentOptions.TopLeft,bool brush=false)
        {
            var clip=ui.Rect(parent,name+" boundary",box.x,box.y,box.width,box.height);clip.gameObject.AddComponent<RectMask2D>();
            var label=ImmortalUi.Text(ui,clip,name,text,6,6,box.width-12,box.height-12,size,ink??Ivory,false,align); // Upright local typography; no global font replacement.
            label.textWrappingMode=TextWrappingModes.NoWrap;label.maxVisibleLines=lines;label.lineSpacing=4;
            label.enableAutoSizing=false;label.extraPadding=false;label.overflowMode=TextOverflowModes.Truncate;return label;
        }
        public static ArtsCodexGraphic Graphic(UiKit ui,Transform parent,string name,Rect box,ArtsCodexGraphic.Form form,Color color,Func<bool> reduce=null,int glyph=0)
        {
            var graphic=ui.Rect(parent,name,box.x,box.y,box.width,box.height).gameObject.AddComponent<ArtsCodexGraphic>();
            graphic.Configure(form,color,reduce,glyph);return graphic;
        }
        public static RawImage RoundTexture(UiKit ui,Transform parent,string name,Rect box,string art,Rect uv,Color? tint=null)
        {
            var clip=Graphic(ui,parent,name+" round mask",box,ArtsCodexGraphic.Form.Disc,Color.white);
            clip.gameObject.AddComponent<Mask>().showMaskGraphic=false;
            var image=ui.Stretch(clip.transform,name).gameObject.AddComponent<RawImage>();image.texture=ApprovedUiArt.Load(art);
            image.uvRect=uv;image.color=tint??Color.white;image.raycastTarget=false;return image;
        }
        public static RectTransform Emblem(UiKit ui,Transform parent,string name,Rect box,int index,Color grade)
        {
            var root=ui.Rect(parent,name,box.x,box.y,box.width,box.height);
            var texture=Sigil(index);
            if(texture!=null)ImmortalUi.Texture(ui,root,name+" painted skill art",texture,0,0,box.width,box.height);
            else Graphic(ui,root,name+" inscribed sigil",new Rect(0,0,box.width,box.height),ArtsCodexGraphic.Form.Glyph,SymbolColor(index),null,index);
            return root;
        }
        public static Button Hit(UiKit ui,Transform parent,string name,Rect box,UnityAction click,
            Func<bool> low,ArtsCodexGraphic focus=null)
        {
            var root=ui.Rect(parent,name,box.x,box.y,box.width,box.height);
            var image=root.gameObject.AddComponent<Image>();image.color=Color.clear;
            var button=root.gameObject.AddComponent<Button>();button.targetGraphic=image;button.transition=Selectable.Transition.None;
            if(click!=null)button.onClick.AddListener(click);
            root.gameObject.AddComponent<ArtsCodexFocus>().Configure(button,focus);return button;
        }
        public static Button WoodButton(UiKit ui,Transform parent,string name,string label,Rect box,UnityAction click,Func<bool> low)
        {
            return AestheticUi.Jade(ui,parent,name,label,box,click,low,null,27);
        }
    }
}
