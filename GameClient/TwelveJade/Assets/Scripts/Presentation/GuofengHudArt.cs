using System;
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
}
