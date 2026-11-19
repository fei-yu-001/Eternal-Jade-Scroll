using System;
using UnityEngine;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    // Foot-root position remains authoritative. A child renders real frames; zero movement stops the walk phase.
    public sealed class NpcFrameAnimator : MonoBehaviour
    {
        RawImage image; RectTransform root, body; Texture2D stand, sheet; Func<bool> reduce;
        Vector2 previous; float distance, idleClock; int count, last=-1; bool initialized, mirrored, nativeLeft;
        public int FrameForCheck => last;
        public int FrameCountForCheck => count;
        public bool HasSheetForCheck => sheet != null;
        public void Configure(TownCharacterArt.Entry art,Func<bool> reduced)
        {
            root=(RectTransform)transform;reduce=reduced;stand=TownCharacterArt.Texture(art.stand);
            sheet=TownCharacterArt.Texture(art.walk);count=Mathf.Max(1,art.frames);nativeLeft=art.nativeFacing=="left";
            var old=GetComponent<RawImage>();if(old!=null)old.color=Color.clear;
            body=new GameObject("Independent NPC frame",typeof(RectTransform)).GetComponent<RectTransform>();
            body.SetParent(transform,false);body.anchorMin=new Vector2(0,0);body.anchorMax=new Vector2(1,1);
            body.pivot=new Vector2(.5f,0);body.offsetMin=body.offsetMax=Vector2.zero;
            image=body.gameObject.AddComponent<RawImage>();image.raycastTarget=false;
            previous=root.anchoredPosition;initialized=true;Apply(false);
        }
        void LateUpdate()
        {
            if(!initialized||image==null)return;
            Vector2 position=root.anchoredPosition;Vector2 delta=position-previous;float moved=delta.magnitude;previous=position;
            // Scene repositioning must not fast-forward an animation cycle.
            bool walking=moved>.005f&&moved<40;
            if(walking)
            {
                // Scale the gait to the rendered body height, so Canvas scaling cannot change stride length.
                distance+=moved;idleClock=0;
                if(Mathf.Abs(delta.x)>.004f)
                {
                    bool next=(delta.x<0)!=nativeLeft;
                    if(next!=mirrored){mirrored=next;last=-2;}
                }
            }else idleClock+=Time.unscaledDeltaTime;
            Apply(walking);
            float breath=(reduce?.Invoke()??true)||walking?0:Mathf.Sin(idleClock*1.8f)*.004f;
            body.localScale=new Vector3(1,1+breath,1);
        }
        void Apply(bool moving)
        {
            int frame=moving&&sheet!=null?Mathf.FloorToInt(distance/Mathf.Max(1,root.rect.height*.10f))%count:-1;
            if(frame==last&&image.texture!=null)return;last=frame;
            image.texture=frame<0?stand:sheet;
            float width=frame<0?1:1f/count;
            float x=frame<0?0:frame/(float)count;
            image.uvRect=mirrored?new Rect(x+width,0,-width,1):new Rect(x,0,width,1);
        }
    }
}
