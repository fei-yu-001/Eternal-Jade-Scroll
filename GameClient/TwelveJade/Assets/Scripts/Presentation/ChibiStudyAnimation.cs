using System;
using System.Collections;
using TMPro;
using TwelveJade.Core;
using UnityEngine;
namespace TwelveJade.Presentation
{
    // One owned, unscaled presentation. It consumes a committed result; never changes skills or items.
    public sealed class ChibiStudyAnimation : MonoBehaviour
    {
        RectTransform bodyRect,presentationRect;StudySpiritSequence sequence;
        CanvasGroup canvasGroup;
        TMP_Text bubbleText,auraText;
        RectTransform[] glyphs=Array.Empty<RectTransform>();
        CanvasGroup[] glyphGroups=Array.Empty<CanvasGroup>();
        Vector2[] glyphOrigins=Array.Empty<Vector2>();
        Func<bool> reduced;
        Action completed;
        Coroutine running;
        Vector2 originalBodyPos;
        public bool IsPlaying { get; private set; }
        public StudyOutcome Outcome { get; private set; }
        public void Bind(RectTransform body,CanvasGroup group,TMP_Text bubble,TMP_Text aura,
            Func<bool> lowMotion,RectTransform[] words,CanvasGroup[] wordGroups,RectTransform animatedPanel=null)
        {
            presentationRect=animatedPanel!=null?animatedPanel:GetComponent<RectTransform>();
            bodyRect=body;sequence=body!=null?body.GetComponent<StudySpiritSequence>():null;canvasGroup=group;bubbleText=bubble;auraText=aura;reduced=lowMotion;
            glyphs=words??Array.Empty<RectTransform>();glyphGroups=wordGroups??Array.Empty<CanvasGroup>();
            glyphOrigins=new Vector2[glyphs.Length];for(int i=0;i<glyphs.Length;i++)glyphOrigins[i]=glyphs[i].anchoredPosition;
            if(bodyRect!=null)originalBodyPos=bodyRect.anchoredPosition;
            if(canvasGroup!=null){canvasGroup.blocksRaycasts=false;canvasGroup.interactable=false;}
        }
        public void Play(string skillName,StudyOutcome outcome,string heading,string detail,Action done=null)
        {
            Cancel();Outcome=outcome;completed=done;
            gameObject.SetActive(true);transform.SetAsLastSibling();IsPlaying=true;
            if(bubbleText!=null)bubbleText.text=heading;
            if(auraText!=null)auraText.text=detail;
            running=StartCoroutine(Animate());
        }
        void RestorePose()
        {
            if(presentationRect!=null)presentationRect.localScale=Vector3.one;
            if(bodyRect!=null){bodyRect.anchoredPosition=originalBodyPos;bodyRect.localRotation=Quaternion.identity;}
            for(int i=0;i<glyphs.Length;i++)
            {
                if(glyphs[i]!=null){glyphs[i].anchoredPosition=glyphOrigins[i];glyphs[i].localScale=Vector3.one;}
                if(i<glyphGroups.Length&&glyphGroups[i]!=null)glyphGroups[i].alpha=0;
            }
        }
        void Cancel()
        {
            if(running!=null){StopCoroutine(running);running=null;}
            IsPlaying=false;completed=null;RestorePose();if(canvasGroup!=null)canvasGroup.alpha=0;
        }
        public void Hold(){Cancel();gameObject.SetActive(true);if(canvasGroup!=null)canvasGroup.alpha=1;}
        public void DismissImmediately(){Cancel();gameObject.SetActive(false);}
        void OnDisable()=>Cancel();
        void OnDestroy(){Cancel();reduced=null;sequence=null;}
        IEnumerator Animate()
        {
            float elapsed=0;const float duration=3.1f;
            while(elapsed<duration)
            {
                bool low=reduced?.Invoke()??true;
                sequence?.Sample(elapsed,low,Outcome);
                if(low)
                {
                    RestorePose();if(canvasGroup!=null)canvasGroup.alpha=1;
                    if(elapsed>=1.7f)break;
                }
                else
                {
                    float enter=Mathf.Clamp01(elapsed/.4f),leave=Mathf.Clamp01((elapsed-2.65f)/.45f);
                    float scale=Mathf.Lerp(.97f,1,Mathf.SmoothStep(0,1,enter));
                    if(presentationRect!=null)presentationRect.localScale=Vector3.one*(scale*(1-.015f*leave));
                    if(canvasGroup!=null)canvasGroup.alpha=Mathf.Min(1,enter*1.4f)*(1-leave);
                    float floatY=Mathf.Sin(Mathf.Max(0,elapsed-.4f)*2.7f)*2;
                    if(bodyRect!=null)
                    {
                        bodyRect.anchoredPosition=originalBodyPos+new Vector2(0,floatY+leave*10);
                        float nod=elapsed<1.1f?Mathf.Sin(elapsed*4)*.3f:elapsed<2.05f?Mathf.Sin(elapsed*3)*.5f:0;
                        bodyRect.localRotation=Quaternion.Euler(0,0,nod);
                    }
                    for(int i=0;i<glyphs.Length;i++)
                    {
                        float t=Mathf.Clamp01((elapsed-.35f-i*.085f)/1.9f);
                        float angle=i*Mathf.PI/3+t*2.2f;
                        float radius=Outcome==StudyOutcome.Understood?Mathf.Lerp(206,20,t):190+Mathf.Sin(t*Mathf.PI)*20;
                        glyphs[i].anchoredPosition=new Vector2(280+Mathf.Cos(angle)*radius,-280+Mathf.Sin(angle)*radius*.68f);
                        glyphs[i].localScale=Vector3.one*Mathf.Lerp(.72f,1,t);
                        if(i<glyphGroups.Length)glyphGroups[i].alpha=Mathf.Sin(t*Mathf.PI)*(1-leave)*.9f;
                    }
                }
                elapsed+=Time.unscaledDeltaTime;yield return null;
            }
            // Detach completion before deactivation so OnDisable cannot replay a result or cancel a newer run.
            running=null;IsPlaying=false;var done=completed;completed=null;RestorePose();
            if(canvasGroup!=null)canvasGroup.alpha=0;gameObject.SetActive(false);done?.Invoke();
        }
    }
}
