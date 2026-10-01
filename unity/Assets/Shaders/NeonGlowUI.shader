Shader "ModularFlightPanel/NeonGlowUI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite / Font Texture", 2D) = "white" {}
        _Color ("Primary Neon Tint", Color) = (0.0, 1.0, 0.85, 1.0) // 电光青 (Cyber Cyan)

        _NeonGlowColor ("Secondary Neon / Glow Tint", Color) = (1.0, 0.15, 0.85, 1.0) // 荧光品红 (Neon Magenta)
        _CoreHotColor ("Overdrive Hot Core", Color) = (1.0, 1.0, 1.0, 1.0)
        _GlowStrength ("Neon Halo Intensity", Range(0.0, 2.0)) = 0.85
        _ChromaticShift ("Sub-Texel Rainbow Shift", Range(0.0, 2.0)) = 0.6
        _ScanlineStrength ("Micro Scanline Depth", Range(0.0, 0.4)) = 0.08

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
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;

            fixed4 _NeonGlowColor;
            fixed4 _CoreHotColor;
            float _GlowStrength;
            float _ChromaticShift;
            float _ScanlineStrength;

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
                // 1. 利用屏幕空间偏导数构建亚像素放电管正交光晕采样 (<= 0.65 像素，严禁图集越界)
                float2 dUVx = ddx(IN.texcoord) * 0.65;
                float2 dUVy = ddy(IN.texcoord) * 0.65;

                // 2. 正交 5 点等离子放电柱感知采样：中心玻璃管柱 + 4 邻域气辉光晕
                half4 sampleC = tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd;
                half4 sampleL = tex2D(_MainTex, IN.texcoord - dUVx) + _TextureSampleAdd;
                half4 sampleR = tex2D(_MainTex, IN.texcoord + dUVx) + _TextureSampleAdd;
                half4 sampleU = tex2D(_MainTex, IN.texcoord + dUVy) + _TextureSampleAdd;
                half4 sampleD = tex2D(_MainTex, IN.texcoord - dUVy) + _TextureSampleAdd;

                float coreAlpha = sampleC.a;
                float haloAlpha = (sampleL.a + sampleR.a + sampleU.a + sampleD.a) * 0.25;
                float totalAlpha = max(coreAlpha, haloAlpha * 0.85);

                if (totalAlpha < 0.005)
                {
                    discard;
                }

                // 3. 物理霓虹放电管层级解耦 (Decoupled Plasma Tube & Gas Corona Model):
                // 核心管体：优先保持高纯度基色 (IN.color.rgb) 与白炽高能核，绝不因单像素抗锯齿发生泥泞偏色
                // 外缘光晕：在管体外围亚像素晕影区向副霓虹辉光 (_NeonGlowColor.rgb) 平滑过渡，呈现纯正赛博双色霓虹！
                fixed3 primaryNeon = IN.color.rgb;
                fixed3 fringeNeon = _NeonGlowColor.rgb;

                float tubeWeight = smoothstep(0.18, 0.55, coreAlpha);
                fixed3 tubeColor = lerp(fringeNeon, primaryNeon, tubeWeight);

                // 4. 核心白炽等离子放电 (White-Hot Plasma Core)
                float hotCore = smoothstep(0.55, 0.95, coreAlpha) * 0.40;
                fixed3 dischargeRgb = lerp(tubeColor, _CoreHotColor.rgb, hotCore);

                // 5. 2 像素高频赛博微扫描光栅 (2px Micro Scanlines)
                float scan = 1.0 - (sin(IN.worldPosition.y * 3.14159) * 0.5 + 0.5) * min(_ScanlineStrength, 0.05);
                fixed3 finalRgb = dischargeRgb * scan;

                // 6. 霓虹气体电离辉光与高反差伽马强化 (Crisp Neon Corona)
                float finalAlpha = saturate(pow(totalAlpha, 0.76) * (1.0 + _GlowStrength * 0.28)) * IN.color.a;
                fixed4 finalCol = fixed4(finalRgb, finalAlpha);

                // 7. UGUI 视口裁切保护
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
