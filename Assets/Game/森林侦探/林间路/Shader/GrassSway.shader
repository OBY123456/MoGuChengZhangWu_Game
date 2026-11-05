// Wind map: R = tip bend weight, G = per-tuft phase. Use the matching grass tile.
Shader "UI/GrassSway"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        [NoScaleOffset] _WindMap ("Grass Wind Map (Linear)", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _WindStrength ("Wind Strength", Range(0,0.025)) = 0.012
        _WindSpeed ("Wind Speed", Range(0,3)) = 0.7
        _Flutter ("Leaf Flutter", Range(0,1)) = 0.25
        _Phase ("Wind Phase", Float) = 0
        _FixedBorder ("Fixed Border Width", Range(0,0.2)) = 0.035
        _BorderFeather ("Border Feather", Range(0.001,0.1)) = 0.025
        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
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
            Name "GrassWind"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 mask : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            sampler2D _WindMap;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float _UIMaskSoftnessX;
            float _UIMaskSoftnessY;
            float _WindStrength;
            float _WindSpeed;
            float _Flutter;
            float _Phase;
            float _FixedBorder;
            float _BorderFeather;

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                o.color = v.color * _Color;
                float2 pixelSize = o.vertex.w / abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
                float4 rect = clamp(_ClipRect, -2e10, 2e10);
                o.mask.xy = v.vertex.xy * 2 - rect.xy - rect.zw;
                o.mask.zw = 0.25 / (0.25 * float2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize));
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // This UV is for one unpacked full-rect sprite, never an atlas.
                float2 uv = i.uv;
                float2 wind = tex2D(_WindMap, uv).rg;
                float edge = min(min(uv.x, 1 - uv.x), min(uv.y, 1 - uv.y));
                float border = max(_FixedBorder, 0);
                float edgeWeight = smoothstep(border, border + max(_BorderFeather, 0.001), edge);
                float phase = wind.g * 6.2831853;
                float time = _Time.y * max(_WindSpeed, 0) * 6.2831853 + _Phase;
                float flutter = saturate(_Flutter) * 0.25;
                float sway = (sin(time + phase) + flutter * sin(2 * time + phase * 1.7)) / (1 + flutter);
                float offset = clamp(_WindStrength, 0, 0.025) * wind.r * edgeWeight * sway;
                float2 sampleUV = saturate(uv - float2(offset, 0));
                fixed4 color = (tex2D(_MainTex, sampleUV) + _TextureSampleAdd) * i.color;
                #ifdef UNITY_UI_CLIP_RECT
                float2 mask = saturate((_ClipRect.zw - _ClipRect.xy - abs(i.mask.xy)) * i.mask.zw);
                color.a *= mask.x * mask.y;
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif
                return color;
            }
            ENDCG
        }
    }
    Fallback Off
}
