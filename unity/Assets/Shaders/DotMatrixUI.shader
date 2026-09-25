Shader "ModularFlightPanel/DotMatrixUI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite / Font Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _DotSpacing ("Dot Spacing (Screen Pixels)", Float) = 3.2
        _DotRadius ("Dot Fill Radius (0.1 - 0.5)", Range(0.1, 0.5)) = 0.42
        _DotSmoothness ("Dot Edge Softness", Range(0.01, 0.3)) = 0.12
        _UnlitDotColor ("Unlit Ghost Dot Color", Color) = (0.03, 0.07, 0.04, 0.15)
        _LitDotColor ("Lit Dot Base Tint", Color) = (1.0, 1.0, 1.0, 1.0)
        _GlowStrength ("Phosphor Glow Halo", Range(0.0, 1.0)) = 0.45
        _ScanlineFreq ("Scanline Frequency", Float) = 1.0
        _ScanlineStrength ("Scanline Contrast", Range(0.0, 0.5)) = 0.10

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
                float4 screenPos     : TEXCOORD2;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;

            float _DotSpacing;
            float _DotRadius;
            float _DotSmoothness;
            fixed4 _UnlitDotColor;
            fixed4 _LitDotColor;
            float _GlowStrength;
            float _ScanlineFreq;
            float _ScanlineStrength;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.screenPos = ComputeScreenPos(OUT.vertex);
                OUT.texcoord = v.texcoord;
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // 1. 采样 UGUI 动态字体 Atlas 或精灵
                half4 texCol = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd);
                float sourceAlpha = texCol.a;

                // 2. 物理屏幕像素坐标解算 (精确对齐全屏幕点阵微结构栅格)
                float2 screenPixel = (IN.screenPos.xy / max(IN.screenPos.w, 0.00001)) * _ScreenParams.xy;

                // 3. 点距栅格取模与中心距离计算
                float spacing = max(_DotSpacing, 2.0);
                float2 dotCoord = screenPixel / spacing;
                float2 cellFrac = frac(dotCoord);
                float distToCenter = length(cellFrac - 0.5);

                // 4. Micro-LED 点阵高亮核与微辉光 (SDF Calculation)
                float dotShape = 1.0 - smoothstep(_DotRadius - _DotSmoothness, _DotRadius, distToCenter);
                float glowHalo = exp(-distToCenter * 4.0) * _GlowStrength;

                // 5. 军规级微扫描线调制 (水平微晶格)
                float scan = 1.0 - (sin(screenPixel.y * _ScanlineFreq) * 0.5 + 0.5) * _ScanlineStrength;

                // 6. 核心色彩混合与笔画保真 (Critical Legibility Guarantee)
                fixed4 baseCol = texCol * IN.color * _LitDotColor;
                baseCol.rgb *= scan;

                fixed4 finalCol = fixed4(0, 0, 0, 0);

                if (sourceAlpha > 0.04)
                {
                    // 笔画保真算法：
                    // 在笔画中心对准圆孔处获得白热过载高亮激化 (1.65)，在圆孔之间保留 48% 的发光笔画骨架，绝对不切断！
                    float ledIntensity = lerp(0.48, 1.65, dotShape) + glowHalo * 0.50;
                    float finalAlpha = sourceAlpha * saturate(dotShape * 0.75 + 0.35 + glowHalo * 0.35);

                    // 点阵中心呈现真实的 Micro-LED 白热晶体核 (White-hot core)
                    fixed3 hotColor = lerp(baseCol.rgb, fixed3(1.0, 1.0, 1.0), dotShape * 0.35);
                    finalCol = fixed4(hotColor * ledIntensity, finalAlpha * baseCol.a);
                }

                // 7. UGUI 视口裁切支持
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
