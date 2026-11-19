using System;
using UnityEngine;
using UnityEngine.EventSystems;
namespace TwelveJade.Presentation
{
    public sealed class AtlasPanZoom : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
    {
        RectTransform viewport,sheet;Func<bool> reduced;
        Vector2 pointer,focus=new Vector2(.5f,.5f),positionVelocity,anchorUv,anchorPixel,lastViewSize;
        float zoom=1,targetZoom=1,zoomVelocity;bool animating,dragging,anchoredZoom;
        public float RelativeZoom=>zoom;
        public float TargetRelativeZoom=>targetZoom;
        public bool IsAnimating=>animating;
        public Vector2 FocusNormalized=>focus;
        float FitScale=>AtlasViewportGeometry.CoverScale(viewport.rect.size,sheet.rect.size);
        Vector2 ScaledSize(float value)=>sheet.rect.size*(FitScale*value);
        public void Configure(RectTransform view,RectTransform content,Vector2 normalizedPlayer,Func<bool> low=null)
        {viewport=view;sheet=content;reduced=low;focus=new Vector2(.5f,.5f);zoom=targetZoom=1;lastViewSize=view.rect.size;ApplyImmediate();}
        public void FitTown()=>FitWorld();
        public void FitWorld()=>MoveTo(new Vector2(.5f,.5f),1);
        public void Focus(Vector2 uv)=>MoveTo(uv,targetZoom);
        public void CloseView(Vector2 uv)=>MoveTo(uv,2.25f);
        public void Scale(float amount)
        {if(viewport==null||sheet==null||amount<=0)return;ZoomAt(amount,viewport.rect.size*.5f);}
        void ZoomAt(float amount,Vector2 pixel)
        {
            if(viewport==null||sheet==null)return;
            anchorPixel=pixel;anchorUv=AtlasViewportGeometry.UnderPointer(pixel,sheet.anchoredPosition,ScaledSize(zoom));
            targetZoom=Mathf.Clamp(targetZoom*amount,1,4.5f);anchoredZoom=true;dragging=false;animating=true;
            if(reduced?.Invoke()??false)ApplyImmediate();
        }
        void MoveTo(Vector2 uv,float magnification)
        {
            focus=new Vector2(Mathf.Clamp01(uv.x),Mathf.Clamp01(uv.y));targetZoom=Mathf.Clamp(magnification,1,4.5f);
            dragging=false;anchoredZoom=false;animating=true;if(reduced?.Invoke()??false)ApplyImmediate();
        }
        Vector2 Position(float value)=>anchoredZoom?AtlasViewportGeometry.AnchorPosition(anchorUv,anchorPixel,viewport.rect.size,ScaledSize(value)):
            AtlasViewportGeometry.FocusPosition(focus,viewport.rect.size,ScaledSize(value));
        Vector2 CentreWorld()=>AtlasViewportGeometry.UnderPointer(viewport.rect.size*.5f,sheet.anchoredPosition,ScaledSize(zoom));
        void LateUpdate()
        {
            if(viewport==null||sheet==null)return;
            if((viewport.rect.size-lastViewSize).sqrMagnitude>.01f){lastViewSize=viewport.rect.size;anchoredZoom=false;ApplyImmediate();}
            if(dragging||!animating)return;if(reduced?.Invoke()??false){ApplyImmediate();return;}
            float dt=Time.unscaledDeltaTime;zoom=Mathf.SmoothDamp(zoom,targetZoom,ref zoomVelocity,.18f,Mathf.Infinity,dt);
            sheet.localScale=Vector3.one*(FitScale*zoom);var target=Position(zoom);
            var next=anchoredZoom?target:Vector2.SmoothDamp(sheet.anchoredPosition,target,ref positionVelocity,.20f,Mathf.Infinity,dt);
            sheet.anchoredPosition=AtlasViewportGeometry.Clamp(next,viewport.rect.size,ScaledSize(zoom));
            if(Mathf.Abs(zoom-targetZoom)<.0003f&&(sheet.anchoredPosition-Position(targetZoom)).sqrMagnitude<.04f)ApplyImmediate();
        }
        void ApplyImmediate()
        {
            if(viewport==null||sheet==null)return;zoom=targetZoom;zoomVelocity=0;positionVelocity=Vector2.zero;
            sheet.localScale=Vector3.one*(FitScale*zoom);sheet.anchoredPosition=Position(zoom);
            focus=CentreWorld();animating=false;anchoredZoom=false;
        }
        public void OnBeginDrag(PointerEventData data)
        {
            if(viewport==null||sheet==null||data.button!=PointerEventData.InputButton.Left)return;
            dragging=true;animating=anchoredZoom=false;targetZoom=zoom;zoomVelocity=0;positionVelocity=Vector2.zero;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport,data.position,data.pressEventCamera,out pointer);
        }
        public void OnDrag(PointerEventData data)
        {
            if(!dragging)return;
            if(RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport,data.position,data.pressEventCamera,out var at))
            {sheet.anchoredPosition=AtlasViewportGeometry.Clamp(sheet.anchoredPosition+at-pointer,viewport.rect.size,ScaledSize(zoom));pointer=at;focus=CentreWorld();}
        }
        public void OnEndDrag(PointerEventData data){if(!dragging)return;dragging=false;focus=CentreWorld();}
        public void OnScroll(PointerEventData data)
        {
            if(viewport==null||Mathf.Abs(data.scrollDelta.y)<.001f)return;
            if(RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport,data.position,data.enterEventCamera,out var at))
            {var pixel=new Vector2(at.x-viewport.rect.xMin,viewport.rect.yMax-at.y);ZoomAt(data.scrollDelta.y>0?1.1f:1/1.1f,pixel);}
        }
        void OnDisable(){animating=dragging=anchoredZoom=false;zoomVelocity=0;positionVelocity=Vector2.zero;}
        void OnDestroy(){viewport=sheet=null;reduced=null;}
    }
}
