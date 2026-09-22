Shader "ModularFlightPanel/GlassCockpitUI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _GlassBgColor ("Glass Background (Frosted)", Color) = (0.08, 0.12, 0.16, 0.72)
        _BorderColor ("Precision Border Color", Color) = (0.35, 0.75, 1.0, 0.6)
        _AccentColor ("Modern Accent Color", Color) = (0.0, 0.85, 1.0, 1.0)

        _BorderWidth ("Border Width", Range(0.001, 0.05)) = 0.015
        _CornerChamfer ("Corner Chamfer Cut", Range(0.0, 0.2)) = 0.05
        _GlassGradientStrength ("Top-Down Lighting Gradient", Range(0.0, 0.5)) = 0.2

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
            fixed4 _GlassBgColor;
            fixed4 _BorderColor;
            fixed4 _AccentColor;
            float _BorderWidth;
            float _CornerChamfer;
            float _GlassGradientStrength;

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

                // 四角微切角 (Corner Chamfer)
                float d1 = uv.x + uv.y;
                float d2 = (1.0 - uv.x) + uv.y;
                float d3 = uv.x + (1.0 - uv.y);
                float d4 = (1.0 - uv.x) + (1.0 - uv.y);
                if (d1 < _CornerChamfer || d2 < _CornerChamfer || d3 < _CornerChamfer || d4 < _CornerChamfer)
                {
                    discard;
                }

                // 距离边缘距离
                float distLeft = uv.x;
                float distRight = 1.0 - uv.x;
                float distBottom = uv.y;
                float distTop = 1.0 - uv.y;
                float distToEdge = min(min(distLeft, distRight), min(distBottom, distTop));

                float isBorder = step(distToEdge, _BorderWidth);

                // 顶部向下微妙透光渐变 (Frosted glass vertical ambient light)
                float topDownGradient = (1.0 - uv.y) * _GlassGradientStrength;
                fixed4 glass = _GlassBgColor;
                glass.rgb += topDownGradient;

                // 顶部航空条标线 (Modern Top Header Accent)
                float isTopAccent = step(distTop, _BorderWidth * 1.8);
                fixed4 edgeColor = lerp(_BorderColor, _AccentColor, isTopAccent);

                fixed4 finalCol = lerp(glass, edgeColor, isBorder);
                finalCol *= IN.color;

                return finalCol;
            }
            ENDCG
        }
    }
}
