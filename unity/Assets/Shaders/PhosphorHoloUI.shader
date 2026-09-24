Shader "ModularFlightPanel/PhosphorHoloUI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite / Font Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _PhosphorColor ("Phosphor Emission Color", Color) = (0.2, 0.95, 0.65, 1.0)
        _CoreHotColor ("Beam Overdrive Hot Core", Color) = (0.95, 1.0, 0.98, 1.0)
        _Aberration ("Chromatic Aberration Offset", Range(0.0, 0.006)) = 0.0003
        _ScanlineFreq ("Micro Scanline Frequency", Float) = 320.0
        _ScanlineDepth ("Scanline Shadow Depth", Range(0.0, 0.5)) = 0.18
        _BloomStrength ("Phosphor Halo Bloom", Range(0.0, 1.0)) = 0.35
        _OverdriveThreshold ("Core Overdrive Intensity", Range(0.3, 1.0)) = 0.65

        // UGUI Stencil Mask Support
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
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile __ UNITY_UI_CLIP_RECT
            #pragma multi_compile __ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;

            fixed4 _PhosphorColor;
            fixed4 _CoreHotColor;
            float _Aberration;
            float _ScanlineFreq;
            float _ScanlineDepth;
            float _BloomStrength;
            float _OverdriveThreshold;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = v.texcoord;
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // 1. 三通道视差微色散采样 (RGB Chromatic Aberration - 全息衍射镜片效应)
                float2 uv = IN.texcoord;
                float offset = _Aberration;

                half4 sampleR = (tex2D(_MainTex, uv + float2(offset, 0)) + _TextureSampleAdd);
                half4 sampleG = (tex2D(_MainTex, uv) + _TextureSampleAdd);
                half4 sampleB = (tex2D(_MainTex, uv - float2(offset, 0)) + _TextureSampleAdd);

                float alphaR = sampleR.a;
                float alphaG = sampleG.a;
                float alphaB = sampleB.a;

                float maxAlpha = max(alphaG, max(alphaR, alphaB));
                if (maxAlpha < 0.005) discard;

                // 2. 电子束高频微扫描线 (CRT / Holographic Scanlines)
                float scan = 1.0 - (sin(IN.worldPosition.y * _ScanlineFreq) * 0.5 + 0.5) * _ScanlineDepth;

                // 3. 矢量高能过载白热核 (Beam Overdrive White-Hot Core)
                // 笔画较厚或能量集中处呈现亮白核心，边缘呈饱和磷光微光
                float coreWeight = smoothstep(0.82, 1.0, alphaG) * 0.75;
                fixed3 tint = _PhosphorColor.rgb * IN.color.rgb;
                fixed3 baseColor = lerp(tint, _CoreHotColor.rgb, coreWeight);

                // 4. 边缘微色散重组
                fixed3 finalRgb = fixed3(
                    baseColor.r * alphaR,
                    baseColor.g * alphaG,
                    baseColor.b * alphaB
                ) * scan;

                // 5. 磷光光子辉光余辉 (Phosphor Bleed)
                float totalAlpha = saturate(maxAlpha * (1.0 + _BloomStrength * 0.4));
                fixed4 finalCol = fixed4(finalRgb, totalAlpha * IN.color.a);

                // 6. UGUI 视口裁切支持
                #ifdef UNITY_UI_CLIP_RECT
                finalCol.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(finalCol.a - 0.001);
                #endif

                return finalCol;
            }
            ENDCG
        }
    }
}
