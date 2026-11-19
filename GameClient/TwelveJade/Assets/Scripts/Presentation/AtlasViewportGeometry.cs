using UnityEngine;
namespace TwelveJade.Presentation
{
    // Top-left UI coordinates: y in anchoredPosition is positive upward. Pure math for edge/anchor checks.
    public static class AtlasViewportGeometry
    {
        public static float CoverScale(Vector2 view,Vector2 sheet)=>Mathf.Max(view.x/Mathf.Max(1,sheet.x),view.y/Mathf.Max(1,sheet.y));
        public static Vector2 Clamp(Vector2 position,Vector2 view,Vector2 scaledSheet)
            =>new Vector2(Mathf.Clamp(position.x,Mathf.Min(0,view.x-scaledSheet.x),0),
                Mathf.Clamp(position.y,0,Mathf.Max(0,scaledSheet.y-view.y)));
        public static Vector2 FocusPosition(Vector2 uv,Vector2 view,Vector2 scaledSheet)
            =>Clamp(new Vector2(view.x*.5f-uv.x*scaledSheet.x,-view.y*.5f+uv.y*scaledSheet.y),view,scaledSheet);
        public static Vector2 UnderPointer(Vector2 pointer,Vector2 position,Vector2 scaledSheet)
            =>new Vector2((pointer.x-position.x)/Mathf.Max(.001f,scaledSheet.x),(pointer.y+position.y)/Mathf.Max(.001f,scaledSheet.y));
        public static Vector2 AnchorPosition(Vector2 uv,Vector2 pointer,Vector2 view,Vector2 scaledSheet)
            =>Clamp(new Vector2(pointer.x-uv.x*scaledSheet.x,uv.y*scaledSheet.y-pointer.y),view,scaledSheet);
    }
}
