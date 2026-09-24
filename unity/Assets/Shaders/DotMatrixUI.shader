Shader "ModularFlightPanel/DotMatrixUI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite / Font Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _DotSpacing ("Dot Spacing (Pixels)", Float) = 5.0
        _DotRadius ("Dot Fill Radius (0.1 - 0.5)", Range(0.1, 0.5)) = 0.38
        _DotSmoothness ("Dot Edge Softness", Range(0.01, 0.25)) = 0.08
        _UnlitDotColor ("Unlit Ghost Dot Color", Color) = (0.03, 0.07, 0.04, 0.20)
        _LitDotColor ("Lit Dot Base Tint", Color) = (0.25, 1.0, 0.45, 1.0)
        _GlowStrength ("Phosphor Glow Halo", Range(0.0, 1.0)) = 0.40
        _ScanlineFreq ("Scanline Frequency", Float) = 1.5
        _ScanlineStrength ("Scanline Contrast", Range(0.0, 0.5)) = 0.12

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
                OUT.texcoord = v.texcoord;
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // 1. 采样 UGUI 文本字模或精灵纹理
                half4 texCol = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd);
                float sourceAlpha = texCol.a;

                // 2. 物理网格点阵坐标解算 (基于 Canvas Pixel 空间)
                float spacing = max(_DotSpacing, 2.0);
                float2 dotCoord = IN.worldPosition.xy / spacing;
                float2 cellFrac = frac(dotCoord);
                float distToCenter = length(cellFrac - 0.5);

                // 3. LED / 荧光点矩阵圆形边缘 SDF 计算
                float dotShape = 1.0 - smoothstep(_DotRadius - _DotSmoothness, _DotRadius, distToCenter);
                float glowHalo = exp(-distToCenter * 4.5) * _GlowStrength;

                // 4. 扫描线效果 (Scanlines)
                float scan = 1.0 - (sin(IN.worldPosition.y * _ScanlineFreq) * 0.5 + 0.5) * _ScanlineStrength;

                // 5. 组合点亮状态 (Lit) 与物理未激活幽灵状态 (Unlit Ghost Pixels)
                fixed4 litCol = texCol * IN.color * _LitDotColor;
                litCol.rgb *= scan;

                // 当源纹理覆盖率 > 0.05 时点亮该点阵，并叠加微光；否则渲染极淡的未激活底点
                fixed4 finalCol = fixed4(0, 0, 0, 0);

                if (sourceAlpha > 0.05)
                {
                    float totalAlpha = saturate(dotShape + glowHalo) * sourceAlpha;
                    finalCol = fixed4(litCol.rgb, totalAlpha * litCol.a);
                }
                else
                {
                    // 在背景留有微弱的物理点阵未通电残影 (经典 DSKY / 航电 LCD 硬件质感)
                    float ghostAlpha = dotShape * _UnlitDotColor.a;
                    finalCol = fixed4(_UnlitDotColor.rgb, ghostAlpha);
                }

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
