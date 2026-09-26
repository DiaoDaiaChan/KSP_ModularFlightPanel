Shader "ModularFlightPanel/CrispAvionicsText"
{
    Properties
    {
        [PerRendererData] _MainTex ("Font Texture", 2D) = "white" {}
        _Color ("Text Tint", Color) = (1,1,1,1)

        // 感知对比度与伽马微调参数 (Avionics High-Legibility Gamma Curve)
        _GammaBoost ("Gamma Curve Power", Range(0.5, 1.2)) = 0.72
        _AlphaScale ("Alpha Density Scale", Range(0.8, 1.5)) = 1.15

        // UGUI 模板遮罩 (Stencil Masking)
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

            float _GammaBoost;
            float _AlphaScale;

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
                // 1. 字体基础灰度纹理采样与采样修正
                fixed4 fontTex = tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd;

                // 2. 伽马抗蚀刻锐化 (Gamma-Aware Stroke Anti-Thinning):
                // FreeType 在生成矢量灰度抗锯齿时，边缘半透明像素在线性 Alpha 混合到暗色座舱玻璃底板时，
                // 会因人眼感知的 sRGB 非线性响应产生严重 "边缘蚀刻"（字形变细、空洞感、阅读疲劳）。
                // 采用航空级 Gamma 曲线提升抗锯齿边缘的中间调覆盖率，令字体骨架坚实饱满、笔画分明。
                float rawAlpha = fontTex.a;
                float crispAlpha = saturate(pow(max(0.0, rawAlpha), _GammaBoost) * _AlphaScale);

                fixed4 finalCol = fixed4(IN.color.rgb, crispAlpha * IN.color.a);

                // 3. UGUI 视口裁切保护
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
