using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    // A real collapsible quest scroll in the HUD. Hidden details neither intercept input nor retain focus.
    public sealed class QuestBriefRoll : MonoBehaviour
    {
        RectTransform frame,clip,lower;CanvasGroup details;ScrollRect scroll;Button toggle;TMP_Text prompt;
        Func<bool> lowMotion;float openness,bodyHeight;bool expanded,lastReduced;
        public bool Expanded=>expanded;
        public void Configure(RectTransform host,RectTransform viewport,RectTransform bottom,CanvasGroup body,ScrollRect bodyScroll,
            Button header,TMP_Text caption,Func<bool> reduced)
        {frame=host;clip=viewport;lower=bottom;details=body;scroll=bodyScroll;toggle=header;prompt=caption;lowMotion=reduced;lastReduced=reduced?.Invoke()??true;Apply();}
        public void Toggle()
        {
            expanded=!expanded;
            if(!expanded&&UnityEngine.EventSystems.EventSystem.current!=null)
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(toggle.gameObject);
            Apply();
        }
        public void SetBodyHeight(float height){bodyHeight=Mathf.Clamp(height,80,486);Apply();}
        void Update()
        {
            bool reduced=lowMotion?.Invoke()??true;
            if(reduced!=lastReduced){lastReduced=reduced;if(scroll!=null){scroll.inertia=!reduced;if(reduced)scroll.StopMovement();}}
            float target=expanded?1:0;
            float next=reduced?target:Mathf.MoveTowards(openness,target,Time.unscaledDeltaTime/.24f);
            if(Mathf.Approximately(next,openness))return;openness=next;Apply();
        }
        void Apply()
        {
            if(clip==null)return;
            if(lowMotion?.Invoke()??true)openness=expanded?1:0;
            float visible=bodyHeight*Mathf.SmoothStep(0,1,openness);
            clip.sizeDelta=new Vector2(clip.sizeDelta.x,visible);
            frame.sizeDelta=new Vector2(frame.sizeDelta.x,106+visible);
            if(lower!=null)lower.anchoredPosition=new Vector2(-12,-(84+visible));
            details.interactable=details.blocksRaycasts=expanded&&openness>=.999f;
            prompt.text=expanded?"回卷":"展卷";
            if(scroll!=null)scroll.inertia=!(lowMotion?.Invoke()??true);
        }
        void OnDisable(){expanded=false;openness=0;Apply();}
        void OnDestroy(){lowMotion=null;}
    }
}
