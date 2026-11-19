using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    public sealed class GuofengHudArt : MonoBehaviour
    {
        Sprite owned;
        public static Image Create(UiKit ui, Transform parent, string name, string art, float x, float y, float w, float h)
        {
            var image = ui.Rect(parent,name,x,y,w,h).gameObject.AddComponent<Image>();
            image.raycastTarget=false; image.preserveAspect=true;
            var texture=ApprovedUiArt.Load("nov7/"+art);
            if(texture != null)
            {
                var owner=image.gameObject.AddComponent<GuofengHudArt>();
                owner.owned=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),100);
                image.sprite=owner.owned;
            }
            else { image.enabled=false; Debug.LogWarning("HUD art missing: "+art); }
            return image;
        }
        void OnDestroy() { if(owned!=null) Destroy(owned); }
    }
    public sealed class GuofengHudRefresh : MonoBehaviour
    {
        Action refresh; float next;
        public void Configure(Action update) { refresh=update; next=0; refresh?.Invoke(); }
        void Update() { if(Time.unscaledTime<next)return; next=Time.unscaledTime+.2f; refresh?.Invoke(); }
        void OnDestroy() { refresh=null; }
    }
    public sealed class JadeLotusMotion : MonoBehaviour
    {
        Func<bool> reduce; RectTransform[] petals; Quaternion[] rest; float time;
        public void Configure(Func<bool> reduced, RectTransform[] items)
        { reduce=reduced; petals=items;rest=new Quaternion[items.Length];for(int i=0;i<items.Length;i++)rest[i]=items[i].localRotation; }
        void Update()
        {
            if(petals==null)return;
            bool still=reduce?.Invoke()??true;if(!still)time+=Time.unscaledDeltaTime;
            for(int i=0;i<petals.Length;i++)if(petals[i]!=null)
                petals[i].localRotation=rest[i]*Quaternion.Euler(0,0,still?0:Mathf.Sin(time*.65f+i*.7f)*.6f);
        }
    }
}
