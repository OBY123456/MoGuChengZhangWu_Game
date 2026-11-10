Shader "UI/Progress UV Scroll Smooth"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _SpeedPixels ("Scroll Speed (Source Pixels / Second)", Range(-200, 200)) = 60
        _SampleStartPixels ("Stripe Sample Start (Source Pixels)", Float) = 285
        _PeriodPixels ("Stripe Period (Source Pixels)", Float) = 76.6
        _StripeSlope ("Stripe Lean (X Pixels Per Y Pixel)", Float) = 0.99
        _SeamPixels ("Seam Transition (Source Pixels)", Range(0.1, 8)) = 2
        _BorderPixels ("Fixed Border (Source Pixels)", Range(0, 8)) = 2
        _FeatherPixels ("Border Feather (Source Pixels)", Range(0.1, 4)) = 1
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
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "UVScroll"
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
            float4 _ClipRect;
            float4 _UVRect;
            float _SpeedPixels;
            float _SampleStartPixels;
            float _PeriodPixels;
            float _StripeSlope;
            float _SeamPixels;
            float _BorderPixels;
            float _FeatherPixels;

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

            float4 frag(v2f i) : SV_Target
            {
                // The unshifted alpha keeps the sprite silhouette and Image.fillAmount intact.
                float4 original = tex2D(_MainTex, i.texcoord) + _TextureSampleAdd;
                float2 spriteSize = max(_UVRect.zw - _UVRect.xy, float2(0.00001, 0.00001));
                float2 uv = (i.texcoord - _UVRect.xy) / spriteSize;
                float2 sourcePixels = max(spriteSize * abs(_MainTex_TexelSize.zw), float2(1, 1));
                float period = clamp(_PeriodPixels, 1, sourcePixels.x * 0.25);
                // Place the wrap in a flat part of the stripe at every height.
                float rowShift = (uv.y - 0.5) * sourcePixels.y * _StripeSlope;
                float start = clamp(_SampleStartPixels + rowShift, period + 1, sourcePixels.x - period - 1);
                float x = uv.x * sourcePixels.x;
                float position = frac((x - start - _Time.y * _SpeedPixels) / period) * period;
                // Both source ranges stay in the middle, away from the rounded end caps.
                float xA = start + position;
                float xB = xA - period;
                float2 uvA = _UVRect.xy + float2(xA / sourcePixels.x, uv.y) * spriteSize;
                float2 uvB = _UVRect.xy + float2(xB / sourcePixels.x, uv.y) * spriteSize;
                float2 inset = min(abs(_MainTex_TexelSize.xy) * 0.5, spriteSize * 0.25);
                uvA = clamp(uvA, _UVRect.xy + inset, _UVRect.zw - inset);
                uvB = clamp(uvB, _UVRect.xy + inset, _UVRect.zw - inset);
                float3 stripeA = (tex2D(_MainTex, uvA) + _TextureSampleAdd).rgb;
                float3 stripeB = (tex2D(_MainTex, uvB) + _TextureSampleAdd).rgb;
                // Blend across the wrap while preserving the measured stripe period.
                float seam = min(max(_SeamPixels, 0.1), period * 0.25);
                float transition = smoothstep(period - seam, period, position);
                float3 rollingColor = lerp(stripeA, stripeB, transition);
                // Protect a narrow capsule-shaped border in source pixels, not an inset box.
                float2 p = (uv - 0.5) * sourcePixels;
                float radius = sourcePixels.y * 0.5;
                float2 q = float2(max(abs(p.x) - max(sourcePixels.x * 0.5 - radius, 0), 0), p.y);
                float distanceToEdge = length(q) - radius;
                float movingMask = 1 - smoothstep(-_BorderPixels - _FeatherPixels, -_BorderPixels, distanceToEdge);
                float4 color = float4(lerp(original.rgb, rollingColor, movingMask), original.a) * i.color;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(i.localPosition.xy, _ClipRect);
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
