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
                // 1. 字体或精灵基础采样
                half4 fontTex = tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd;
                float alpha = fontTex.a;

                if (alpha < 0.005)
                {
                    discard;
                }

                // 2. 霓虹放电管物理层级 (Neon Discharge Tube Model):
                // 核心管体 (Core Tube, alpha > 0.5): 纯粹鲜艳的主基色 (IN.color.rgb)；
                // 边缘气辉 (Fringe Glow, alpha <= 0.5): 向副霓虹辉光 (_NeonGlowColor.rgb) 平滑过渡，形成标志性赛博霓虹双色轮廓！
                fixed3 primaryNeon = IN.color.rgb;
                fixed3 fringeNeon = _NeonGlowColor.rgb;

                float fringeFactor = 1.0 - smoothstep(0.15, 0.65, alpha);
                fixed3 tubeColor = lerp(primaryNeon, fringeNeon, fringeFactor * 0.70);

                // 3. 核心白炽等离子放电 (White-Hot Plasma Discharge - 25% 饱和微过载)
                float coreWeight = saturate(pow(alpha, 3.0) * 0.25);
                fixed3 finalRgb = lerp(tubeColor, _CoreHotColor.rgb, coreWeight);

                // 4. 外发光光晕叠加与透明度合成 (Crisp Neon Halo)
                float finalAlpha = saturate(pow(alpha, 0.85) * (1.0 + _GlowStrength * 0.25)) * IN.color.a;
                fixed4 finalCol = fixed4(finalRgb, finalAlpha);

                // 5. UGUI 视口裁切保护
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
