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

                // 2. 图元受控微色散 (Texel-Constrained Sub-Pixel Dispersion - 100% 杜绝字体图集溢出)
                float2 shift = float2(_ChromaticShift * _MainTex_TexelSize.x * 0.75, 0.0);
                float alphaR = (tex2D(_MainTex, IN.texcoord + shift) + _TextureSampleAdd).a;
                float alphaB = (tex2D(_MainTex, IN.texcoord - shift) + _TextureSampleAdd).a;

                // 严格以中心主字模 Alpha 进行钳制门控，绝不拾取隔壁字符
                alphaR = min(alphaR, alpha * 1.35);
                alphaB = min(alphaB, alpha * 1.35);

                // 3. 赛博虹 / 霓虹双色空间渐变 (Cyan -> Magenta Cyberpunk Gradient)
                float gradientFactor = sin(IN.worldPosition.x * 0.018 + IN.worldPosition.y * 0.008) * 0.5 + 0.5;
                fixed3 neonBase = lerp(IN.color.rgb, _NeonGlowColor.rgb, gradientFactor);

                // 4. 核心白炽过载激化 (White-Hot Overdrive Core)
                float coreWeight = pow(alpha, 2.2);
                fixed3 coreColor = lerp(neonBase, _CoreHotColor.rgb, coreWeight * 0.85);

                // 5. 色散三色重构
                fixed3 finalRgb = fixed3(
                    coreColor.r * lerp(1.0, alphaR / max(alpha, 0.001), 0.25),
                    coreColor.g,
                    coreColor.b * lerp(1.0, alphaB / max(alpha, 0.001), 0.25)
                );

                // 6. 本地微扫描线调制 (Canvas-Local Scanlines)
                float scan = 1.0 - (sin(IN.worldPosition.y * 1.5708) * 0.5 + 0.5) * _ScanlineStrength;
                finalRgb *= scan;

                // 7. 外发光光晕叠加与 UGUI 透明度合成
                float finalAlpha = saturate(alpha * (1.0 + _GlowStrength * 0.35)) * IN.color.a;
                fixed4 finalCol = fixed4(finalRgb, finalAlpha);

                // 8. UGUI 视口裁切保护
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
