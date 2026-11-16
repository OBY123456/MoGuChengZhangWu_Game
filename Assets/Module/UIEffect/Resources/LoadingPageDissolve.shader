Shader "UI/Loading Page Dissolve"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _TransitionTex ("Dissolve Mask", 2D) = "gray" {}
        _EffectFactor ("Effect Factor", Range(0, 1)) = 1
        _DissolveWidth ("Edge Width", Range(0, 1)) = 0.305
        _DissolveSoftness ("Edge Softness", Range(0, 1)) = 0.739
        _DissolveColor ("Edge Color", Color) = (0, 0.25, 1, 1)
        [HideInInspector] _AlphaTex ("Sprite Alpha Texture", 2D) = "white" {}
        [HideInInspector] _UseSplitAlpha ("Use Split Alpha", Float) = 0
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
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_local __ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local __ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            sampler2D _MainTex;
            sampler2D _TransitionTex;
            sampler2D _AlphaTex;
            fixed4 _Color;
            fixed4 _DissolveColor;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float _EffectFactor;
            float _DissolveWidth;
            float _DissolveSoftness;
            float _UseSplitAlpha;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float2 maskUV : TEXCOORD1;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 localPosition : TEXCOORD1;
                float2 maskUV : TEXCOORD2;
                fixed4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.localPosition = v.vertex.xy;
                o.maskUV = any(v.maskUV != 0.0) ? (v.maskUV - 0.25) * 2.0 : v.uv;
                o.color = v.color * _Color;
                return o;
            }

            float DissolveNoise(float2 position)
            {
                float2 cell = floor(position);
                float2 weight = frac(position);
                weight = weight * weight * (3.0 - 2.0 * weight);
                float seed = dot(cell, float2(0.3183099, 0.3678794));
                float4 corners = frac((seed + float4(0.0, 0.3183099, 0.3678794, 0.6861893)) * 5.37);
                corners = frac(corners * (corners + 19.19) * 17.17);
                return lerp(lerp(corners.x, corners.y, weight.x), lerp(corners.z, corners.w, weight.x), weight.y);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 sprite = tex2D(_MainTex, i.uv) + _TextureSampleAdd;
                if (_UseSplitAlpha > 0.5) sprite.a = tex2D(_AlphaTex, i.uv).r;
                fixed4 color = sprite * i.color;

                float factor = saturate(_EffectFactor);
                if (factor <= 0.0) discard;
                if (factor < 1.0)
                {
                    float2 maskUV = saturate(i.maskUV);
                    // The noise creates holes even when the transition texture is flat.
                    float noise = DissolveNoise(maskUV * float2(6.4, 4.0) + float2(1.37, 3.18));
                    float detail = tex2D(_TransitionTex, maskUV).r;
                    float mask = saturate((noise - 0.5) * 1.25 + 0.5 + (detail - 0.5) * 0.18);
                    float edgeWidth = _DissolveWidth * 0.065;
                    float feather = lerp(0.002, 0.018, _DissolveSoftness);
                    float threshold = lerp(1.0, -max(edgeWidth, feather), factor);
                    float distanceToEdge = mask - threshold;
                    float coverage = smoothstep(0.0, feather, distanceToEdge);

                    // Center the light on the cutout and let the halo extend into the hole.
                    float edgeDistance = abs(distanceToEdge - feather * 0.5);
                    float glowWidth = max(_DissolveWidth * 0.19, 0.0001);
                    float haloWidth = glowWidth * lerp(0.75, 1.3, _DissolveSoftness);
                    float core = 1.0 - smoothstep(glowWidth * 0.06, glowWidth * 0.32, edgeDistance);
                    float halo = 1.0 - smoothstep(glowWidth * 0.15, haloWidth, edgeDistance);
                    float glowFade = smoothstep(0.0, 0.09, factor) * (1.0 - smoothstep(0.91, 1.0, factor));
                    float glow = max(core * 0.94, halo * 0.68) * glowFade * saturate(_DissolveColor.a) * step(0.0001, _DissolveWidth);
                    float3 glowColor = lerp(_DissolveColor.rgb, float3(1.0, 1.0, 1.0), 0.16 + core * 0.44) * i.color.rgb;

                    float innerLight = coverage * glow;
                    float outerLight = (1.0 - coverage) * glow * (0.30 + core * 0.40);
                    float combinedCoverage = coverage + outerLight;
                    color.rgb = lerp(color.rgb, glowColor, (innerLight + outerLight) / max(combinedCoverage, 0.0001));
                    color.a *= combinedCoverage;
                }

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(i.localPosition, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
