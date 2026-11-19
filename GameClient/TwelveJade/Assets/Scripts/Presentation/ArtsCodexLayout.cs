using UnityEngine;
namespace TwelveJade.Presentation
{
    // 1920x1080 design space, also consumed by the offline composition review.
    public static class ArtsCodexLayout
    {
        public static readonly Rect Title=new Rect(86,52,650,88);
        public static readonly Rect Subtitle=new Rect(90,144,650,52);
        public static readonly Rect Mandala=new Rect(406,150,840,840);
        public static readonly Rect CoreArt=new Rect(708,412,236,236);
        public static readonly Rect CoreName=new Rect(634,648,384,70);
        public static readonly Rect CoreHint=new Rect(610,718,432,50);
        public static readonly Rect Dossier=new Rect(1302,126,526,862);
        public static readonly Rect SkillTitle=new Rect(1344,158,442,70);
        public static readonly Rect Quality=new Rect(1344,238,442,48);
        public static readonly Rect LargeSeal=new Rect(1454,298,222,202);
        public static readonly Rect ProgressCaption=new Rect(1354,505,422,54);
        public static readonly Rect ProgressBar=new Rect(1362,564,406,18);
        public static readonly Rect Cost=new Rect(1344,598,442,52);
        public static readonly Rect Description=new Rect(1344,664,442,98);
        public static readonly Rect Availability=new Rect(1344,776,442,50);
        public static readonly Rect Action=new Rect(1494,834,142,142);
        public static readonly Rect Notice=new Rect(470,1008,1270,54);
        public static readonly Rect RitualSummary=new Rect(1344,596,442,62);
        public static readonly Rect[] Tabs={new Rect(90,330,246,106),new Rect(90,476,246,106),new Rect(90,622,246,106)};
        public static readonly Vector2[] Slots={new Vector2(621,365),new Vector2(826,280),new Vector2(1031,365),new Vector2(1031,775),new Vector2(826,860),new Vector2(621,775)};
        public static readonly Rect[] Approaches={new Rect(1356,672,418,76),new Rect(1356,760,418,76),new Rect(1356,848,418,76)};
    }
}
