Shader "ModularFlightPanel/NeonGlowUI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _BackgroundColor ("Background Color", Color) = (0.02, 0.04, 0.06, 0.88)
        _BorderColor ("Border Color", Color) = (0.0, 1.0, 0.8, 1.0)
        _CornerColor ("Corner Highlight Color", Color) = (1.0, 0.9, 0.2, 1.0)
        _GlowColor ("Outer Glow Color", Color) = (0.0, 1.0, 0.8, 0.35)

        _BorderWidth ("Border Width", Range(0.001, 0.1)) = 0.025
        _CornerSize ("Corner Cut/Marker Size", Range(0.01, 0.3)) = 0.12
        _ScanlineDensity ("Scanline Density", Float) = 150.0
        _ScanlineStrength ("Scanline Strength", Range(0.0, 0.5)) = 0.15

        // UGUI Stencil Mask Support
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
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

            fixed4 _Color;
            fixed4 _BackgroundColor;
            fixed4 _BorderColor;
            fixed4 _CornerColor;
            fixed4 _GlowColor;
            float _BorderWidth;
            float _CornerSize;
            float _ScanlineDensity;
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
                float2 uv = IN.texcoord;

                // 距离各边缘的最小距离
                float distLeft = uv.x;
                float distRight = 1.0 - uv.x;
                float distBottom = uv.y;
                float distTop = 1.0 - uv.y;
                float distEdgeX = min(distLeft, distRight);
                float distEdgeY = min(distBottom, distTop);
                float distToEdge = min(distEdgeX, distEdgeY);

                // 判断是否在边框范围内
                float isBorder = step(distToEdge, _BorderWidth);

                // 判断是否在四角高亮标记范围内 (现代科技风切角标)
                float isCornerX = step(distEdgeX, _CornerSize);
                float isCornerY = step(distEdgeY, _CornerSize);
                float isCorner = isCornerX * isCornerY * isBorder;

                // CRT 细微扫描线效果
                float scanline = sin(uv.y * _ScanlineDensity * 3.14159) * 0.5 + 0.5;
                fixed4 bg = _BackgroundColor;
                bg.rgb = lerp(bg.rgb, bg.rgb * (1.0 - _ScanlineStrength), scanline);

                // 颜色合成
                fixed4 col = bg;
                col = lerp(col, _BorderColor, isBorder);
                col = lerp(col, _CornerColor, isCorner);

                col *= IN.color;
                return col;
            }
            ENDCG
        }
    }
}
