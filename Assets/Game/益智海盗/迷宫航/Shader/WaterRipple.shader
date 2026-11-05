Shader "UI/WaterRipple"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Strength ("Wave Strength", Range(0, 0.08)) = 0.018
        _Speed ("Wave Speed", Range(0, 4)) = 0.8
        _Frequency ("Wave Density", Range(0.5, 12)) = 3
        _BorderWidth ("Fixed Border Width", Range(0, 0.2)) = 0.035
        _BorderFade ("Border Transition", Range(0.001, 0.2)) = 0.045
        _Shimmer ("Water Shimmer", Range(0, 0.3)) = 0.04
        [HideInInspector] _UVRect ("Sprite UV MinMax", Vector) = (0, 0, 1, 1)
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
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "WaterRipple"

            CGPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile __ UNITY_UI_CLIP_RECT
            #pragma multi_compile __ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float2 spriteMin : TEXCOORD1;
                float2 spriteMax : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 spriteRect : TEXCOORD1;
                float4 mask : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _UVRect;
            float4 _ClipRect;
            float _UIMaskSoftnessX;
            float _UIMaskSoftnessY;
            float _Strength;
            float _Speed;
            float _Frequency;
            float _BorderWidth;
            float _BorderFade;
            float _Shimmer;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color * _Color;
                float2 suppliedSize = IN.spriteMax - IN.spriteMin;
                OUT.spriteRect = suppliedSize.x > 0.000001 && suppliedSize.y > 0.000001 ? float4(IN.spriteMin, IN.spriteMax) : _UVRect;

                #ifdef UNITY_UI_CLIP_RECT
                float2 pixelSize = OUT.vertex.w / max(abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy)), float2(0.00001, 0.00001));
                float4 clipRect = clamp(_ClipRect, -2e10, 2e10);
                float2 softness = max(float2(_UIMaskSoftnessX, _UIMaskSoftnessY), float2(0, 0));
                OUT.mask = float4(IN.vertex.xy * 2 - clipRect.xy - clipRect.zw, 0.25 / max(0.25 * softness + abs(pixelSize), float2(0.00001, 0.00001)));
                #else
                OUT.mask = float4(0, 0, 0, 0);
                #endif
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 rectMin = IN.spriteRect.xy;
                float2 rectSize = max(IN.spriteRect.zw - rectMin, float2(0.000001, 0.000001));
                float2 localUV = saturate((IN.texcoord - rectMin) / rectSize);
                float2 edgeDistance = min(localUV, 1 - localUV);
                float edge = min(edgeDistance.x, edgeDistance.y);
                float borderWidth = clamp(_BorderWidth, 0, 0.2);
                float edgeMask = smoothstep(borderWidth, borderWidth + max(_BorderFade, 0.0001), edge);

                float time = _Time.y * _Speed;
                float phaseX = (localUV.y * _Frequency + localUV.x * 0.35) * 6.2831853 + time * 1.3;
                float phaseY = (localUV.x * _Frequency * 0.8 - localUV.y * 0.2) * 6.2831853 - time * 1.05;
                float2 wave = float2(sin(phaseX) + 0.35 * sin(phaseY * 1.7 + time * 0.4), cos(phaseY) + 0.35 * cos(phaseX * 1.4 - time * 0.3));
                float2 distortedUV = IN.texcoord + wave * _Strength * edgeMask * rectSize;

                // Stay inside this sprite, including its half-texel sampling footprint.
                float2 padding = min(abs(_MainTex_TexelSize.xy) * 0.5, rectSize * 0.49);
                distortedUV = clamp(distortedUV, rectMin + padding, IN.spriteRect.zw - padding);

                fixed4 original = tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd;
                fixed4 distorted = tex2D(_MainTex, distortedUV) + _TextureSampleAdd;
                float shimmer = pow(saturate(0.5 + 0.5 * sin(phaseX + sin(phaseY))), 6);
                float3 water = distorted.rgb * (1 + shimmer * _Shimmer);
                fixed4 color = fixed4(lerp(original.rgb, water, edgeMask), original.a) * IN.color;

                #ifdef UNITY_UI_CLIP_RECT
                float2 clipFactor = saturate((_ClipRect.zw - _ClipRect.xy - abs(IN.mask.xy)) * IN.mask.zw);
                color.a *= clipFactor.x * clipFactor.y;
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
