Shader "UI/Edge Flow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        [HDR] _FlowColor ("Flow Color", Color) = (0.3, 1, 1, 1)
        _Intensity ("Flow Intensity", Range(0, 8)) = 2
        _EdgeWidth ("Edge Width (Texture Pixels)", Range(0.5, 20)) = 3
        _Speed ("Flow Speed (Signed Cycles / Second)", Range(-2, 2)) = 0.25
        _Length ("Flow Length", Range(0.02, 1)) = 0.25
        _Sharpness ("Flow Sharpness", Range(0.5, 8)) = 1.5
        _LightCount ("Light Count (Rounded To Integer)", Range(1, 4)) = 1
        _BaseEdge ("Constant Edge Brightness", Range(0, 1)) = 0
        _UVRect ("Sprite UV Rect (Min X, Min Y, Max X, Max Y)", Vector) = (0, 0, 1, 1)
        _PhaseOffset ("Phase Offset", Range(0, 1)) = 0
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
            Name "EdgeFlow"
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
            float4 _FlowColor;
            float4 _ClipRect;
            float4 _UVRect;
            float _Intensity;
            float _EdgeWidth;
            float _Speed;
            float _Length;
            float _Sharpness;
            float _LightCount;
            float _BaseEdge;
            float _PhaseOffset;

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

            float SampleAlpha(float2 uv)
            {
                // Outside the sprite rectangle is transparent even with Clamp wrapping.
                float2 inside = step(_UVRect.xy, uv) * step(uv, _UVRect.zw);
                float2 safeUV = clamp(uv, _UVRect.xy, _UVRect.zw);
                return saturate(tex2D(_MainTex, safeUV).a + _TextureSampleAdd.a) * inside.x * inside.y;
            }

            float4 frag(v2f i) : SV_Target
            {
                float4 sampleColor = tex2D(_MainTex, i.texcoord) + _TextureSampleAdd;
                float4 color = sampleColor * i.color;
                float2 offset = abs(_MainTex_TexelSize.xy) * _EdgeWidth;
                float alphaMin = SampleAlpha(i.texcoord + float2(offset.x, 0));
                alphaMin = min(alphaMin, SampleAlpha(i.texcoord - float2(offset.x, 0)));
                alphaMin = min(alphaMin, SampleAlpha(i.texcoord + float2(0, offset.y)));
                alphaMin = min(alphaMin, SampleAlpha(i.texcoord - float2(0, offset.y)));
                float edge = saturate((sampleColor.a - alphaMin) * 4);
                float2 normalizedUV = (i.texcoord - _UVRect.xy) / max(_UVRect.zw - _UVRect.xy, float2(0.00001, 0.00001));
                float2 centeredUV = normalizedUV - 0.5;
                // Avoid atan2(0, 0) on fully opaque sprites at their exact center.
                centeredUV.x += 0.000001;
                float angle = atan2(centeredUV.y, centeredUV.x) * 0.159154943;
                float count = floor(clamp(_LightCount, 1, 4) + 0.5);
                float phase = frac((angle - _Time.y * _Speed + _PhaseOffset) * count);
                float distanceToLight = min(phase, 1 - phase);
                float pulse = pow(saturate(1 - distanceToLight / max(_Length * 0.5, 0.001)), _Sharpness);
                float brightness = edge * (_BaseEdge + pulse) * _Intensity * _FlowColor.a;
                color.rgb += _FlowColor.rgb * brightness;

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
