using TMPro;
using TwelveJade.Core;
using UnityEngine;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    // Three authored poses share one transparent silhouette area. This only presents a committed result.
    public sealed class StudySpiritSequence : MonoBehaviour
    {
        RawImage reading,thinking,insight;StudyBookLeaf leaf;
        public string Phase { get; private set; }="reading";
        public void Configure(UiKit ui,RectTransform body)
        {
            reading=Pose(ui,body,"Spirit calmly reading paper book","nov10/study-spirit-reading");
            thinking=Pose(ui,body,"Spirit pondering authored pose","nov8/shenshi-doll-pondering");
            insight=Pose(ui,body,"Spirit authored insight","nov8/shenshi-doll-enlightened");
            leaf=ui.Stretch(body,"Spirit curved paper leaf").gameObject.AddComponent<StudyBookLeaf>();leaf.raycastTarget=false;
            Sample(0,false,StudyOutcome.Progress);
        }
        static RawImage Pose(UiKit ui,RectTransform body,string name,string resource)
        {
            var image=ui.Stretch(body,name).gameObject.AddComponent<RawImage>();
            image.texture=ApprovedUiArt.Load(resource);image.raycastTarget=false;return image;
        }
        public void Sample(float seconds,bool lowMotion,StudyOutcome outcome)
        {
            bool success=outcome==StudyOutcome.Understood||outcome==StudyOutcome.Insight||outcome==StudyOutcome.Progress;
            Phase=lowMotion?(success?"insight":"pondering"):seconds<1.1f?"reading":seconds<2.05f?"pondering":success?"insight":"pondering";
            float ponder=lowMotion?1:Mathf.SmoothStep(0,1,Mathf.Clamp01((seconds-1.02f)/.25f));
            float reveal=success?(lowMotion?1:Mathf.SmoothStep(0,1,Mathf.Clamp01((seconds-2.05f)/.30f))):0;
            if(reading!=null)reading.color=new Color(1,1,1,(1-ponder)*(1-reveal));
            if(thinking!=null)thinking.color=new Color(1,1,1,ponder*(1-reveal));
            if(insight!=null)insight.color=new Color(1,1,1,reveal);
            // One deliberate page turn, with a pause before/after; never an endless flat paddle loop.
            float page=Mathf.Clamp01((seconds-.18f)/.78f);
            if(leaf!=null)leaf.Pose(Mathf.SmoothStep(0,1,page),!lowMotion&&page>0&&page<1);
        }
    }
}
