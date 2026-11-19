Shader "TwelveJade/UI/QingshiWater"
{
    Properties
    {
        [PerRendererData] _MainTex ("UI compatibility texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _WaterColor ("Subtle river tint", Color) = (0.32,0.47,0.45,0.075)
        _HighlightColor ("Reflected sky", Color) = (0.87,0.91,0.79,0.24)
        _WaterHalfWidth ("Inset river half width", Float) = 117
        _ShoreFeather ("Bank feather in world units", Float) = 24
        _Wavelength ("Wave length in world units", Float) = 112
        _FlowPhase ("CPU controlled phase (frozen for reduce motion)", Float) = 0
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
            Name "RiverUI"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0; // arc length along river, signed distance across river
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
            fixed4 _Color, _WaterColor, _HighlightColor;
            float _FlowPhase, _WaterHalfWidth, _ShoreFeather, _Wavelength;
            float4 _ClipRect;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.localPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float wavelength = max(_Wavelength, 1.0);
                float halfWidth = max(_WaterHalfWidth, 1.0);
                float feather = max(_ShoreFeather, 1.0);
                float along = (i.uv.x / wavelength - _FlowPhase) * 6.2831853;
                float across = i.uv.y / wavelength * 6.2831853;
                float edge = halfWidth - abs(i.uv.y);
                float bank = smoothstep(0.0, feather * 1.35, edge);
                float ribbonPhase = across * 5.0 + 0.62 * sin(along) +
                    0.20 * sin(along * 3.0 + across * 0.6);
                float secondaryPhase = across * 9.0 + 0.35 * sin(along * 2.0 - across * 0.35);
                float crest = pow(saturate(0.5 + 0.5 * sin(ribbonPhase)), 10.0);
                float secondary = pow(saturate(0.5 + 0.5 * sin(secondaryPhase)), 12.0);
                float gate = smoothstep(0.15, 0.80, 0.5 + 0.5 * sin(along + across * 0.4)) *
                    smoothstep(0.10, 0.75, 0.5 + 0.5 * sin(along * 3.0 - across * 1.1 + 2.4));
                float detail = 1.0 - smoothstep(0.8, 3.0, fwidth(ribbonPhase));
                float lit = saturate(crest * 0.85 + secondary * 0.25) * gate * detail;
                float tone = 0.5 + 0.5 * sin(along + across * 0.2);
                fixed4 result;
                result.rgb = lerp(_WaterColor.rgb, _HighlightColor.rgb, saturate(lit * 1.2));
                result.a = (_WaterColor.a * (0.32 + 0.08 * tone) + _HighlightColor.a * lit * 0.45) * bank;
                result *= i.color;
                #ifdef UNITY_UI_CLIP_RECT
                result.a *= UnityGet2DClipping(i.localPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(result.a - 0.001);
                #endif
                return result;
            }
            ENDCG
        }
    }
    Fallback Off
}
