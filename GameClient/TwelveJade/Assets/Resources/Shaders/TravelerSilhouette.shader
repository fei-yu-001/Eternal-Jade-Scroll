Shader "TwelveJade/UI/TravelerSilhouette"
{
    Properties
    {
        [PerRendererData] _MainTex ("Current live Frames sheet", 2D) = "white" {}
        _PreviousTex ("Fading live Frames sheet", 2D) = "white" {}
        _FrameUv ("Current frame UV (signed width preserves mirror)", Vector) = (0,0,1,1)
        _PreviousUv ("Previous frame UV", Vector) = (0,0,1,1)
        _FrameAlpha ("Current and previous alpha", Vector) = (0,0,0,0)
        _OutlineStep ("Outline step in local frame UV", Vector) = (.02,.02,0,0)
        _Strength ("Visible outline alpha", Range(0,.65)) = 0
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="False" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            Name "LiveFramesOutline"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 localPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            sampler2D _MainTex, _PreviousTex;
            float4 _MainTex_TexelSize, _PreviousTex_TexelSize;
            float4 _FrameUv, _PreviousUv, _FrameAlpha, _OutlineStep, _ClipRect;
            float _Strength;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.localPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            float2 frameUv(float2 localUv, float4 frame, float2 texel)
            {
                float2 first = min(frame.xy, frame.xy + frame.zw) + abs(texel) * .5;
                float2 last = max(frame.xy, frame.xy + frame.zw) - abs(texel) * .5;
                return clamp(frame.xy + localUv * frame.zw, first, last);
            }

            float poseAlpha(float2 uv)
            {
                // Stop outline taps at this frame's boundary; never sample an adjacent sheet pose.
                float inside = step(0, uv.x) * step(uv.x, 1) * step(0, uv.y) * step(uv.y, 1);
                float a = tex2D(_MainTex, frameUv(uv, _FrameUv, _MainTex_TexelSize.xy)).a * _FrameAlpha.x;
                float b = tex2D(_PreviousTex, frameUv(uv, _PreviousUv, _PreviousTex_TexelSize.xy)).a * _FrameAlpha.y;
                return saturate(b + a * (1 - b)) * inside;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                clip(_Strength - .001);
                float a = poseAlpha(i.uv);
                float hi = a, lo = a;
                float2 d = _OutlineStep.xy;
                float n = poseAlpha(i.uv + float2(d.x,0)); hi=max(hi,n); lo=min(lo,n);
                n = poseAlpha(i.uv - float2(d.x,0)); hi=max(hi,n); lo=min(lo,n);
                n = poseAlpha(i.uv + float2(0,d.y)); hi=max(hi,n); lo=min(lo,n);
                n = poseAlpha(i.uv - float2(0,d.y)); hi=max(hi,n); lo=min(lo,n);
                n = poseAlpha(i.uv + d * .7071); hi=max(hi,n); lo=min(lo,n);
                n = poseAlpha(i.uv - d * .7071); hi=max(hi,n); lo=min(lo,n);
                n = poseAlpha(i.uv + float2(d.x,-d.y) * .7071); hi=max(hi,n); lo=min(lo,n);
                n = poseAlpha(i.uv + float2(-d.x,d.y) * .7071); hi=max(hi,n); lo=min(lo,n);
                // Alpha-derived outer/inner contour, never a filled rectangular tint.
                fixed4 result = i.color;
                result.a *= saturate(max(hi-a, (a-lo)*.8) * 2) * min(.65, _Strength);
                #ifdef UNITY_UI_CLIP_RECT
                result.a *= UnityGet2DClipping(i.localPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(result.a - .001);
                #endif
                return result;
            }
            ENDCG
        }
    }
    FallBack Off
}
