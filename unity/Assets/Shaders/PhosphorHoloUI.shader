Shader "ModularFlightPanel/PhosphorHoloUI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite / Font Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _PhosphorColor ("Phosphor Emission Color", Color) = (0.2, 0.95, 0.65, 1.0)
        _CoreHotColor ("Beam Overdrive Hot Core", Color) = (0.95, 1.0, 0.98, 1.0)
        _Aberration ("Chromatic Dispersion (Texels)", Range(0.0, 1.5)) = 0.65
        _ScanlineDepth ("Scanline Shadow Depth", Range(0.0, 0.5)) = 0.12
        _BloomStrength ("Phosphor Halo Bloom", Range(0.0, 1.0)) = 0.40

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

            fixed4 _PhosphorColor;
            fixed4 _CoreHotColor;
            float _Aberration;
            float _ScanlineDepth;
            float _BloomStrength;

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
                // 1. 采样字模主通道 Alpha
                half4 sampleG = tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd;
                float alphaG = sampleG.a;

                // 严格绝缘：若字模主笔画透明，直接丢弃
                if (alphaG < 0.005)
                {
                    discard;
                }

                // 2. 语义色彩智能门控 (Semantic Color Preservation):
                // 若顶点颜色饱和度高 (如红色危险、黄色注意、绿色就绪、青色数值)，100% 忠实保留语义警示色！
                // 仅当顶点为纯中性白光时，才投射主题全息磷光基色 (_PhosphorColor)
                float maxC = max(max(IN.color.r, IN.color.g), IN.color.b);
                float minC = min(min(IN.color.r, IN.color.g), IN.color.b);
                float saturation = maxC - minC;
                fixed3 baseColor = (saturation > 0.15) ? IN.color.rgb : (IN.color.rgb * _PhosphorColor.rgb);

                // 3. 阴极射线电子束高能过载核 (Electron Beam Overdrive Core)
                // 仅在字模最核心区域产生 22% 炽热过载微提亮，绝不冲淡色彩或导致字形模糊泛白
                float coreWeight = saturate(pow(alphaG, 3.2) * 0.22);
                fixed3 finalRgb = lerp(baseColor, _CoreHotColor.rgb, coreWeight);

                // 4. 平滑准直器微扫描线 (Anti-Aliased CRT Collimator Scanlines)
                // 采用 6 像素平滑周期与受控深度 (最大 6%)，彻底杜绝 4px 高频切断文字横折笔画
                float scan = 1.0 - (sin(IN.worldPosition.y * 1.0472) * 0.5 + 0.5) * min(_ScanlineDepth, 0.06);
                finalRgb *= scan;

                // 5. 磷光光子辉光与伽马清晰度强化 (Phosphor Halo & Gamma Crisp)
                float crispAlpha = saturate(pow(alphaG, 0.85) * (1.0 + _BloomStrength * 0.20)) * IN.color.a;
                fixed4 finalCol = fixed4(finalRgb, crispAlpha);

                // 6. UGUI 视口裁切保护
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
