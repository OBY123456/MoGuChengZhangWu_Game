Shader "UI/Outer Glow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        [HDR] _GlowColor ("Glow Color", Color) = (0.1, 0.9, 1, 0.85)
        _GlowRadius ("Glow Radius (Source Pixels)", Range(0, 24)) = 8
        _GlowStrength ("Glow Strength", Range(0, 4)) = 1.6
        _UVRect ("Sprite UV Rect (Min X, Min Y, Max X, Max Y)", Vector) = (0, 0, 1, 1)
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
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

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
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "OuterGlow"
            CGPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local __ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local __ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 localPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float4 _TextureSampleAdd;
            float4 _Color;
            float4 _GlowColor;
            float4 _ClipRect;
            float4 _UVRect;
            float _GlowRadius;
            float _GlowStrength;

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.localPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = v.texcoord;
                o.color = v.color * _Color;
                return o;
            }

            float4 SampleSprite(float2 uv)
            {
                float2 inside = step(_UVRect.xy, uv) * step(uv, _UVRect.zw);
                float2 inset = min(abs(_MainTex_TexelSize.xy) * 0.5, (_UVRect.zw - _UVRect.xy) * 0.25);
                float2 safeUV = clamp(uv, _UVRect.xy + inset, _UVRect.zw - inset);
                float4 pixel = tex2D(_MainTex, safeUV) + _TextureSampleAdd;
                pixel.a = saturate(pixel.a) * inside.x * inside.y;
                return pixel;
            }

            float4 frag(v2f i) : SV_Target
            {
                float4 source = SampleSprite(i.texcoord);
                float2 radius = abs(_MainTex_TexelSize.xy) * _GlowRadius;
                float2 x = float2(radius.x, 0);
                float2 y = float2(0, radius.y);
                float2 d1 = radius * 0.70710678;
                float2 d2 = float2(d1.x, -d1.y);

                float halo = SampleSprite(i.texcoord + x).a + SampleSprite(i.texcoord - x).a;
                halo += SampleSprite(i.texcoord + y).a + SampleSprite(i.texcoord - y).a;
                halo += SampleSprite(i.texcoord + d1).a + SampleSprite(i.texcoord - d1).a;
                halo += SampleSprite(i.texcoord + d2).a + SampleSprite(i.texcoord - d2).a;

                float glowAlpha = saturate(halo * 0.125 * _GlowStrength) * _GlowColor.a;
                glowAlpha *= (1 - source.a) * step(0.001, _GlowRadius);
                float3 rgb = source.rgb * i.color.rgb * source.a + _GlowColor.rgb * glowAlpha;
                float4 color = float4(rgb, source.a + glowAlpha) * i.color.a;

                #ifdef UNITY_UI_CLIP_RECT
                color *= UnityGet2DClipping(i.localPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }
    Fallback "UI/Default"
}
