using System;
using TwelveJade.Core;
using UnityEngine;
using UnityEngine.UI;
namespace TwelveJade.Presentation
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class AtlasFogGraphic : MaskableGraphic
    {
        bool[] known=new bool[AtlasExploration.CellCount];Func<bool> reduced;float phase,nextTick;
        public void Configure(SaveData save,Func<bool> low)
        {
            reduced=low;raycastTarget=false;Array.Clear(known,0,known.Length);
            if(AtlasExploration.IsValid(save?.atlasExploration)&&save?.atlasExploration!=null)foreach(int cell in save.atlasExploration.cells??Array.Empty<int>())known[cell]=true;
            SetVerticesDirty();
        }
        void Update()
        {
            float now=(reduced?.Invoke()??true)?0:Time.unscaledTime;
            if(now==phase||Time.unscaledTime<nextTick)return;phase=now;nextTick=Time.unscaledTime+.125f;SetVerticesDirty();
        }
        float Fog(float u,float v)
        {
            int x=Mathf.Min(AtlasExploration.Columns-1,Mathf.FloorToInt(u*AtlasExploration.Columns));
            int y=Mathf.Min(AtlasExploration.Rows-1,Mathf.FloorToInt(v*AtlasExploration.Rows));
            return known[y*AtlasExploration.Columns+x]?0:1;
        }
        protected override void OnPopulateMesh(VertexHelper v)
        {
            v.Clear();var r=rectTransform.rect;const int nx=64,ny=80;
            for(int y=0;y<=ny;y++)for(int x=0;x<=nx;x++)
            {
                float u=x/(float)nx,t=y/(float)ny;
                float grain=Mathf.PerlinNoise(u*12+phase*.018f,t*13-phase*.011f),curl=Mathf.Sin(u*28+t*18+phase*.15f)*.012f;
                var ink=Color.Lerp(new Color(.70f,.74f,.66f,1),new Color(.91f,.89f,.79f,1),grain);
                ink.r+=curl;ink.g+=curl;ink.b+=curl;ink.a=Fog(u,t)*Mathf.SmoothStep(0,1,Mathf.InverseLerp(.40f,.80f,grain))*.43f;v.AddVert(new Vector2(r.xMin+u*r.width,r.yMax-t*r.height),ink*color,Vector2.zero);
            }
            for(int y=0;y<ny;y++)for(int x=0;x<nx;x++){int n=y*(nx+1)+x;v.AddTriangle(n,n+1,n+nx+2);v.AddTriangle(n,n+nx+2,n+nx+1);}
        }
        protected override void OnDestroy(){reduced=null;base.OnDestroy();}
    }
}
