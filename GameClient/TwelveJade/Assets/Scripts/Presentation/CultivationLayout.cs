using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    // Design pixels: one registry consumed by product and offline layout evidence.
    public static class CultivationLayout
    {
        public static readonly Rect MeridianPainting=new Rect(220,100,920,920);
        // Separate the interactive sequence from the painted face/torso: these are UI selection points.
        public static readonly Vector2[] MeridianSequence={new Vector2(1178,300),new Vector2(1178,410),
            new Vector2(1178,530),new Vector2(1178,650),new Vector2(1178,770)};
        public static readonly Rect MeridianTitle=new Rect(1404,185,408,60);
        public static readonly Rect MeridianBody=new Rect(1404,254,408,126);
        public static readonly Rect MeridianChance=new Rect(1404,384,408,48);
        public static readonly Rect MeridianPillCount=new Rect(1404,436,408,48);
        public static readonly Rect MeridianPillRule=new Rect(1404,488,408,44);
        public static readonly Rect MeridianOutcome=new Rect(1404,536,408,86);
        public static readonly Rect MeridianAction=new Rect(1452,636,288,94);
        // This includes the visible glow, not merely the center of the painted pill.
        public static readonly Rect MeridianGlow=new Rect(1491,789,222,180);
        public static readonly Rect MeridianPillAction=new Rect(1462,838,280,60);
        public static readonly Rect ArtsTitle=new Rect(1320,153,432,64);
        public static readonly Rect ArtsVerse=new Rect(1320,224,432,48);
        public static readonly Rect ArtsBody=new Rect(1320,282,432,248);
        public static readonly Rect ArtsStatus=new Rect(1320,544,432,48);
        public static readonly Rect ArtsOutcome=new Rect(1320,610,432,104);
        public static readonly Rect ArtsSeal=new Rect(1434,732,198,180);
        public static readonly Rect ArtsActionCaption=new Rect(1400,916,264,52);
        public static readonly Rect Close=new Rect(1704,15,192,86);
        public static readonly Rect[] Volumes={
            new Rect(394,366,96,207),new Rect(522,366,96,207),new Rect(650,366,96,207),
            new Rect(267,579,123,174),new Rect(511,601,228,153),new Rect(891,579,114,174)
        };
        public const float TextInset=6;
        // Product-owned labels never shrink fonts to hide overflow. Valid messages are measured separately.
        // Any unexpected long text is bounded and truncated inside its own mask, never over the next field.
        public static TMP_Text Label(UiKit ui,Transform parent,string name,string text,Rect box,float size,int lines,
            Color ink,TextAlignmentOptions alignment=TextAlignmentOptions.TopLeft)
        {
            var host=ui.Rect(parent,name+" boundary",box.x,box.y,box.width,box.height);
            host.gameObject.AddComponent<RectMask2D>();
            var label=ImmortalUi.Text(ui,host,name,text,TextInset,TextInset,box.width-2*TextInset,box.height-2*TextInset,size,ink,false,alignment);
            label.textWrappingMode=TextWrappingModes.NoWrap;label.overflowMode=TextOverflowModes.Truncate;
            label.maxVisibleLines=lines;label.lineSpacing=4;return label;
        }
        public static string LimitLines(string text,int lines)=>string.Join("\n",(text??"").Replace("\r","").Split('\n').Take(Math.Max(1,lines)));
    }
}
