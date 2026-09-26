Shader "ModularFlightPanel/DotMatrixUI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite / Font Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _DotSpacing ("Dot Spacing (Canvas Units)", Float) = 3.0
        _DotRadius ("Dot Fill Radius (0.1 - 0.5)", Range(0.1, 0.5)) = 0.40
        _DotSmoothness ("Dot Edge Softness", Range(0.01, 0.3)) = 0.10
        _UnlitDotColor ("Unlit Ghost Dot Color", Color) = (0.03, 0.07, 0.04, 0.12)
        _LitDotColor ("Lit Dot Base Tint", Color) = (1.0, 1.0, 1.0, 1.0)
        _GlowStrength ("Phosphor Glow Halo", Range(0.0, 1.0)) = 0.40
        _ScanlineStrength ("Scanline Contrast", Range(0.0, 0.5)) = 0.08

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

            float _DotSpacing;
            float _DotRadius;
            float _DotSmoothness;
            fixed4 _UnlitDotColor;
            fixed4 _LitDotColor;
            float _GlowStrength;
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
                // 1. 采样 UGUI 动态字体 Atlas 或精灵
                half4 texCol = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd);
                float sourceAlpha = texCol.a;

                if (sourceAlpha < 0.005)
                {
                    discard;
                }

                // 2. 本地 Canvas 坐标系解算 (彻底根除屏幕绝对空间导致的“纱窗游走”Bug)
                float spacing = max(_DotSpacing, 1.8);
                float2 dotCoord = IN.worldPosition.xy / spacing;
                float2 cellFrac = frac(dotCoord);
                float distToCenter = length(cellFrac - 0.5);

                // 3. Micro-LED 圆孔形态与透镜高光 (SDF Calculation)
                float dotShape = 1.0 - smoothstep(_DotRadius - _DotSmoothness, _DotRadius, distToCenter);
                float glowHalo = exp(-distToCenter * 4.5) * _GlowStrength;

                // 4. 与点阵行对齐的微扫描线 (Anti-Aliased Grid Scanlines)
                float scan = 1.0 - (sin(cellFrac.y * 3.14159) * 0.5) * _ScanlineStrength;

                // 5. 航电级笔画保真算法 (Critical Legibility Guarantee):
                // 在点孔中心激发 1.45 倍 Micro-LED 白炽过载核；
                // 在孔洞之间保留 55% 基础笔画覆盖，确保 8px~10px 微型文字笔画绝不被吃掉断裂！
                float ledIntensity = lerp(0.55, 1.45, dotShape) + glowHalo * 0.35;
                float finalAlpha = sourceAlpha * saturate(dotShape * 0.65 + 0.45 + glowHalo * 0.25);

                // 点阵中心白炽核 (White-Hot Core)
                fixed4 baseCol = texCol * IN.color * _LitDotColor;
                fixed3 hotColor = lerp(baseCol.rgb, fixed3(1.0, 1.0, 1.0), dotShape * 0.40);
                fixed3 finalRgb = hotColor * ledIntensity * scan;

                fixed4 finalCol = fixed4(finalRgb, finalAlpha * baseCol.a);

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
