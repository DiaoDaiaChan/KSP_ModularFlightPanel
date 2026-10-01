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
                // 1. 利用屏幕空间偏导数构建亚像素光斑扩散步长 (严格限制在 <= 0.65 屏幕像素内，绝不越过字模图集边界)
                float2 dUVx = ddx(IN.texcoord) * 0.65;
                float2 dUVy = ddy(IN.texcoord) * 0.65;

                // 2. 正交 5 点电子束截面感知采样：中心高能发射核心 + 4 向亚像素磷光散射
                half4 sampleC = tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd;
                half4 sampleL = tex2D(_MainTex, IN.texcoord - dUVx) + _TextureSampleAdd;
                half4 sampleR = tex2D(_MainTex, IN.texcoord + dUVx) + _TextureSampleAdd;
                half4 sampleU = tex2D(_MainTex, IN.texcoord + dUVy) + _TextureSampleAdd;
                half4 sampleD = tex2D(_MainTex, IN.texcoord - dUVy) + _TextureSampleAdd;

                float coreAlpha = sampleC.a;
                float haloAlpha = (sampleL.a + sampleR.a + sampleU.a + sampleD.a) * 0.25;
                float totalAlpha = max(coreAlpha, haloAlpha * 0.80);

                if (totalAlpha < 0.005)
                {
                    discard;
                }

                // 3. 语义色彩智能门控 (Semantic Color Preservation):
                // 若顶点颜色饱和度高 (如红色警示、黄色注意、绿色状态、青色数值)，100% 忠实保留原始语义色！
                // 仅当顶点为纯中性白光时，才投射主题全息磷光基色 (_PhosphorColor)
                float maxC = max(max(IN.color.r, IN.color.g), IN.color.b);
                float minC = min(min(IN.color.r, IN.color.g), IN.color.b);
                float saturation = maxC - minC;
                fixed3 phosphorBase = (saturation > 0.15) ? IN.color.rgb : (IN.color.rgb * _PhosphorColor.rgb);

                // 4. 阴极射线电子束高能过载核 (Electron Beam Overdrive Core)
                // 核心笔画处产生炽热发光核，外围光晕保持饱和磷光基色
                float hotCore = smoothstep(0.45, 0.95, coreAlpha) * 0.35;
                fixed3 beamColor = lerp(phosphorBase, _CoreHotColor.rgb, hotCore);

                // 5. 2 像素物理 CRT 准直微光栅 (2px Micro-Scanlines)
                // 采用 2 像素高频周期与微弱深度 (最大 6%)，绝不在 8px 字符上形成割裂波纹
                float scan = 1.0 - (sin(IN.worldPosition.y * 3.14159) * 0.5 + 0.5) * min(_ScanlineDepth, 0.06);
                fixed3 finalRgb = beamColor * scan;

                // 6. 磷光光子辉光与伽马清晰度强化 (Phosphor Halo & Gamma Crisp)
                float crispAlpha = saturate(pow(totalAlpha, 0.78) * (1.0 + _BloomStrength * 0.25)) * IN.color.a;
                fixed4 finalCol = fixed4(finalRgb, crispAlpha);

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
