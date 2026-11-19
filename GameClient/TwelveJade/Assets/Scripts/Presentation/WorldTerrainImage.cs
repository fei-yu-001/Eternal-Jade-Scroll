using System;
using System.Collections.Generic;
using TwelveJade.Core;
using UnityEngine;
using UnityEngine.UI;

namespace TwelveJade.Presentation
{
    /// <summary>连续世界 UV + 高度位移的地形网格。每块使用相同世界采样，跨块边缘完全对齐。</summary>
    public sealed class WorldTerrainImage : RawImage
    {
        TownElevation elevation;
        float originX, originY, worldSize, scale;
        public void Configure(TownElevation value, float x, float y, float size, float worldScale)
        {
            elevation=value;originX=x;originY=y;worldSize=size;scale=worldScale;
            raycastTarget=false;SetVerticesDirty();
        }
        List<float> Cuts(float start, bool vertical)
        {
            var cuts=new SortedSet<float> {start,start+worldSize};
            for(float p=(float)Math.Ceiling(start/32f)*32f;p<start+worldSize;p+=32f) cuts.Add(p);
            foreach(float boundary in elevation.CriticalCoordinates(vertical))
                if(boundary>start && boundary<start+worldSize)cuts.Add(boundary);
            return new List<float>(cuts);
        }
        void Vertex(VertexHelper vh,float x,float y,float height,Color tint)
        {
            vh.AddVert(new Vector3((x-originX)*scale,(-(y-originY)+height)*scale,0),tint,
                new Vector2(uvRect.x+(x-originX)/worldSize*uvRect.width,
                    uvRect.y+(1-(y-originY)/worldSize)*uvRect.height));
        }
        Color SlopeTint(float slope)
        {
            float light = Mathf.Clamp(1 + slope * .24f, .86f, 1.04f);
            return color * new Color(light, light, light, 1);
        }
        Color BasinTint(float x, float y, float height, Color faceTint)
        {
            const float radius = 16f;
            float north = elevation.HeightAt(x, y - radius), south = elevation.HeightAt(x, y + radius);
            float ledge = elevation.StepHeight * .5f;
            if (Mathf.Abs(north - height) > ledge || Mathf.Abs(south - height) > ledge) return faceTint;
            return SlopeTint((south - north) / (2 * radius));
        }
        void Quad(VertexHelper vh,float x0,float y0,float x1,float y1,float h00,float h10,float h11,float h01,
            Color tint00, Color tint10, Color tint11, Color tint01)
        {
            int first=vh.currentVertCount;
            Vertex(vh,x0,y0,h00,tint00);Vertex(vh,x1,y0,h10,tint10);
            Vertex(vh,x1,y1,h11,tint11);Vertex(vh,x0,y1,h01,tint01);
            vh.AddTriangle(first,first+1,first+2);vh.AddTriangle(first,first+2,first+3);
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            if(elevation==null || elevation.IsFlat || worldSize<=0) {base.OnPopulateMesh(vh);return;}
            vh.Clear();var xs=Cuts(originX,false);var ys=Cuts(originY,true);
            const float epsilon=.002f;
            float groundHeight = elevation.DefaultLevel * elevation.StepHeight;
            var wallTint = color * new Color(.58f, .61f, .59f, 1);
            for(int j=0;j<ys.Count-1;j++) for(int i=0;i<xs.Count-1;i++)
            {
                float x0=xs[i],x1=xs[i+1],y0=ys[j],y1=ys[j+1];
                float h00=elevation.HeightAt(x0+epsilon,y0+epsilon),h10=elevation.HeightAt(x1-epsilon,y0+epsilon);
                float h01=elevation.HeightAt(x0+epsilon,y1-epsilon),h11=elevation.HeightAt(x1-epsilon,y1-epsilon);
                float slope=((h01+h11)-(h00+h10))*.5f/Math.Max(1,y1-y0);
                var faceTint = SlopeTint(slope);
                if (Mathf.Min(Mathf.Min(h00, h10), Mathf.Min(h11, h01)) < groundHeight - epsilon)
                    Quad(vh,x0,y0,x1,y1,h00,h10,h11,h01,
                        BasinTint(x0+epsilon,y0+epsilon,h00,faceTint), BasinTint(x1-epsilon,y0+epsilon,h10,faceTint),
                        BasinTint(x1-epsilon,y1-epsilon,h11,faceTint), BasinTint(x0+epsilon,y1-epsilon,h01,faceTint));
                else
                    Quad(vh,x0,y0,x1,y1,h00,h10,h11,h01,faceTint,faceTint,faceTint,faceTint);
                // 南向护壁：上层台面与下层地面之间的真实竖面，不能靠一条阴影假装高差。
                float next0=elevation.HeightAt(x0+epsilon,y1+epsilon),next1=elevation.HeightAt(x1-epsilon,y1+epsilon);
                if(h01-next0>elevation.MaxStepHeight || h11-next1>elevation.MaxStepHeight)
                    Quad(vh,x0,y1,x1,y1,h01,h11,next1,next0,wallTint,wallTint,wallTint,wallTint);
            }
        }
    }
}
