Shader "ModularFlightPanel/DotMatrixUI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite / Font Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _DotSpacing ("Dot Spacing (Canvas Units)", Float) = 1.3
        _DotRadius ("Dot Fill Radius (0.1 - 0.5)", Range(0.1, 0.5)) = 0.44
        _DotSmoothness ("Dot Edge Softness", Range(0.01, 0.3)) = 0.10
        _UnlitDotColor ("Unlit Ghost Dot Color", Color) = (0.03, 0.07, 0.04, 0.08)
        _LitDotColor ("Lit Dot Base Tint", Color) = (1.0, 1.0, 1.0, 1.0)
        _GlowStrength ("Phosphor Glow Halo", Range(0.0, 1.0)) = 0.35
        _ScanlineStrength ("Scanline Contrast", Range(0.0, 0.5)) = 0.04

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
                // 1. 采样 UGUI 动态字体 Atlas 或精灵 Alpha
                half4 texCol = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd);
                float sourceAlpha = texCol.a;

                if (sourceAlpha < 0.005)
                {
                    discard;
                }

                // 2. 本地 Canvas 坐标系解算点阵格栅 (Micro-LED Aperture Grille)
                float spacing = max(_DotSpacing, 0.75);
                float2 dotCoord = IN.worldPosition.xy / spacing;
                float2 cellFrac = frac(dotCoord);
                float distToCenter = length(cellFrac - 0.5);

                // 3. Micro-LED 圆孔形态与透镜高光 (SDF Calculation)
                float dotShape = 1.0 - smoothstep(_DotRadius - _DotSmoothness, _DotRadius, distToCenter);
                float glowHalo = exp(-distToCenter * 4.0) * _GlowStrength;

                // 4. 航电级高清晰度保真调制 (Avionics Legibility Guarantee):
                // 基础笔画保留至少 80% 亮度底衬，点孔中心激发到 125% 激发态，
                // 绝不斩断细小笔画与中文复杂字形，同时呈现鲜明物理 Micro-LED 点阵质感！
                float ledFactor = lerp(0.80, 1.25, dotShape) + glowHalo * 0.20;

                // 5. 与点阵行对齐的微弱光栅纹理
                float scan = 1.0 - (sin(cellFrac.y * 3.14159) * 0.5) * min(_ScanlineStrength, 0.08);

                // 6. 纯正语义色彩还原：以 IN.color 为基准，避免字体纹理 RGB 污染
                fixed3 textRgb = IN.color.rgb * _LitDotColor.rgb;
                // 点阵中心白炽过载核 (仅在字模致密区微泛白)
                float hotCore = dotShape * saturate((sourceAlpha - 0.4) * 2.0) * 0.25;
                fixed3 finalRgb = lerp(textRgb, fixed3(1.0, 1.0, 1.0), hotCore) * ledFactor * scan;

                float finalAlpha = sourceAlpha * IN.color.a;
                fixed4 finalCol = fixed4(finalRgb, finalAlpha);

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
