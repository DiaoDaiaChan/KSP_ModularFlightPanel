Shader "ModularFlightPanel/DigitalSegmentUI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite / Font Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _SegmentLitColor ("Lit Segment Color", Color) = (1.0, 0.72, 0.08, 1.0) // 经典高能琥珀金 (Amber Gold)
        _CoreHotColor ("Core Overdrive Hot Color", Color) = (1.0, 0.98, 0.90, 1.0)
        _GlowStrength ("Segment Halo Bloom", Range(0.0, 1.5)) = 0.50
        _SegmentGrooveContrast ("Segment Micro-Groove Contrast", Range(0.0, 0.3)) = 0.08

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
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;

            fixed4 _SegmentLitColor;
            fixed4 _CoreHotColor;
            float _GlowStrength;
            float _SegmentGrooveContrast;

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
                // 1. 利用屏幕空间偏导数构建高保真灯丝截面感知采样 (严格限制在 0.5 像素内，保持笔画骨架饱满无缺口)
                float2 dUVx = ddx(IN.texcoord) * 0.50;
                float2 dUVy = ddy(IN.texcoord) * 0.50;

                // 2. 正交灯丝核采样：中心发射核 + 4 邻域边缘融合
                half4 sampleC = tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd;
                half4 sampleL = tex2D(_MainTex, IN.texcoord - dUVx) + _TextureSampleAdd;
                half4 sampleR = tex2D(_MainTex, IN.texcoord + dUVx) + _TextureSampleAdd;
                half4 sampleU = tex2D(_MainTex, IN.texcoord + dUVy) + _TextureSampleAdd;
                half4 sampleD = tex2D(_MainTex, IN.texcoord - dUVy) + _TextureSampleAdd;

                float coreAlpha = sampleC.a;
                float filamentAlpha = max(coreAlpha, max(max(sampleL.a, sampleR.a), max(sampleU.a, sampleD.a)) * 0.85);

                if (filamentAlpha < 0.005)
                {
                    discard;
                }

                // 3. 语义色彩智能门控 (Semantic Color Preservation):
                // 若顶点颜色饱和度高 (如红色警报、黄色注意、绿色状态)，100% 忠实保留原始语义色！
                // 仅当顶点为中性白光时，才投射数码管经典琥珀/数码原色 (_SegmentLitColor)
                float maxC = max(max(IN.color.r, IN.color.g), IN.color.b);
                float minC = min(min(IN.color.r, IN.color.g), IN.color.b);
                float saturation = maxC - minC;
                fixed3 baseColor = (saturation > 0.15) ? IN.color.rgb : (IN.color.rgb * _SegmentLitColor.rgb);

                // 4. 物理 VFD 控制栅极微金属网 (2px Fine Control Grid Mesh)
                // 替代摧毁笔画的 8px 粗正弦切痕，赋予真实真空荧光管金属网格质感
                float2 vfdGrid = abs(frac(IN.worldPosition.xy * 0.5) - 0.5) * 2.0;
                float meshMask = min(vfdGrid.x, vfdGrid.y);
                float wireMesh = 1.0 - (1.0 - smoothstep(0.12, 0.48, meshMask)) * min(_SegmentGrooveContrast, 0.08);

                // 5. 核心白炽发光核 (Filament Overdrive Core)
                // 仅在字模最致密的灯丝中心区域产生炽热发光核，外围保留鲜明荧光色
                float hotCore = smoothstep(0.50, 0.95, coreAlpha) * 0.38;
                fixed3 hotColor = lerp(baseColor, _CoreHotColor.rgb, hotCore);
                fixed3 finalRgb = hotColor * wireMesh;

                // 6. 边缘抗锯齿与灯丝光晕融合 (Crisp Filament Halo)
                float finalAlpha = saturate(pow(filamentAlpha, 0.75) * (1.0 + _GlowStrength * 0.22)) * IN.color.a;
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
