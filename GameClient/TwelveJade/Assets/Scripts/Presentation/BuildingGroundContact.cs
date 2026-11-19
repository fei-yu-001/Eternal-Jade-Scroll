using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BuildingGroundContact : MaskableGraphic
    {
        Vector3[] edges=Array.Empty<Vector3>();
        // x-start, x-end, southmost actual support y. No bridge is painted across doorway gaps.
        public void Configure(Vector4[] supports)
        {
            raycastTarget=false;var cuts=new List<float>();
            foreach(var r in supports){cuts.Add(r.x);cuts.Add(r.z);}cuts.Sort();var result=new List<Vector3>();
            for(int i=0;i+1<cuts.Count;i++)
            {
                float x0=cuts[i],x1=cuts[i+1];if(x1-x0<.1f)continue;float x=(x0+x1)*.5f,y=float.NegativeInfinity;
                foreach(var r in supports)if(x>=r.x&&x<=r.z)y=Mathf.Max(y,r.w);
                if(float.IsNegativeInfinity(y))continue;
                if(result.Count>0){var prev=result[result.Count-1];if(Mathf.Abs(prev.z-y)<.1f&&Mathf.Abs(prev.y-x0)<.1f){result[result.Count-1]=new Vector3(prev.x,x1,y);continue;}}
                result.Add(new Vector3(x0,x1,y));
            }
            edges=result.ToArray();SetVerticesDirty();
        }
        public int ContactSegmentCount=>edges.Length;
        void Strip(VertexHelper v,float left,float right,float top,float bottom,Color upper,Color lower)
        {int n=v.currentVertCount;v.AddVert(new Vector2(left,-top),upper*color,Vector2.zero);v.AddVert(new Vector2(right,-top),upper*color,Vector2.zero);v.AddVert(new Vector2(right,-bottom),lower*color,Vector2.zero);v.AddVert(new Vector2(left,-bottom),lower*color,Vector2.zero);v.AddTriangle(n,n+1,n+2);v.AddTriangle(n,n+2,n+3);}
        protected override void OnPopulateMesh(VertexHelper v)
        {
            v.Clear();foreach(var edge in edges)
            {
                float left=edge.x,right=edge.y,y=edge.z;
                Strip(v,left-5,right+5,y-2,y+12,new Color(.13f,.17f,.15f,.21f),new Color(.13f,.17f,.15f,0));
                // Two shallow, cool stone courses meet each real wall/pier, never span the open passage.
                Strip(v,left-2,right+2,y-1,y+2,new Color(.49f,.54f,.49f,.58f),new Color(.35f,.41f,.38f,.40f));
                Strip(v,left-4,right+4,y+2,y+5,new Color(.60f,.64f,.57f,.36f),new Color(.39f,.46f,.41f,.08f));
            }
        }
    }
}
