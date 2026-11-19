using System;
using UnityEngine;
using UnityEngine.InputSystem;
namespace TwelveJade.Presentation
{
    // Consume Escape before the controller sees it; use its existing one-frame input gate.
    [DefaultExecutionOrder(-250)]
    public sealed class V2ScrollSession : MonoBehaviour
    {
        RectTransform paper;CanvasGroup group;QuestScrollReveal reveal;Func<bool> reduced;Action consume,finish,cleanup,after;
        float openness;bool configured,closing,released;
        public bool Closing=>closing;
        public void Configure(RectTransform face,Func<bool> lowMotion,Action consumeFrame,Action finishClose,Action onDisposed)
        {
            paper=face;reveal=face.GetComponent<QuestScrollReveal>();reduced=lowMotion;consume=consumeFrame;finish=finishClose;cleanup=onDisposed;
            group=gameObject.AddComponent<CanvasGroup>();configured=true;openness=reduced()?1:0;Apply();
        }
        public void RequestClose(Action continuation=null)
        {
            if(!configured||closing)return;consume?.Invoke();closing=true;after=continuation;group.interactable=false;
            if(reduced?.Invoke()??true)Finish();
        }
        void Update()
        {
            if(!configured)return;
            if(Keyboard.current!=null&&Keyboard.current.escapeKey.wasPressedThisFrame)RequestClose();
            if(!configured)return;
            if(reduced?.Invoke()??true)openness=closing?0:1;
            else openness=Mathf.MoveTowards(openness,closing?0:1,Time.unscaledDeltaTime/(closing?.15f:.20f));
            Apply();if(closing&&openness<=0)Finish();
        }
        void Apply()
        {
            if(reveal!=null)
            {if(group!=null){group.alpha=1;group.interactable=!closing&&openness>=.999f;}paper.localScale=Vector3.one;reveal.SetOpenness(openness);}
            else {if(group!=null)group.alpha=openness;if(paper!=null)paper.localScale=Vector3.one*Mathf.Lerp(.975f,1,Mathf.SmoothStep(0,1,openness));}
        }
        void Finish()
        {
            if(!configured)return;configured=false;consume?.Invoke();var continuation=after;after=null;finish?.Invoke();continuation?.Invoke();
        }
        void Release(){if(released)return;released=true;configured=false;cleanup?.Invoke();cleanup=null;consume=null;finish=null;after=null;}
        void OnDisable()=>Release();
        void OnDestroy()=>Release();
    }
}
