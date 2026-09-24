Shader "ModularFlightPanel/DigitalSegmentUI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite / Font Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _SegmentLitColor ("Lit Segment Color", Color) = (1.0, 0.75, 0.1, 1.0)
        _SegmentGhostColor ("Unlit Ghost Segment Color", Color) = (0.16, 0.12, 0.05, 0.25)
        _SlantShear ("Italic Slant Shear Angle", Range(-0.3, 0.3)) = 0.12
        _BacklightColor ("Backlight Ambient Tint", Color) = (0.02, 0.03, 0.04, 0.85)
        _BorderHighlight ("Chamfer Border Glow", Color) = (0.4, 0.6, 0.8, 0.4)
        _BorderWidth ("Border Frame Width", Range(0.0, 0.05)) = 0.015

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

            fixed4 _SegmentLitColor;
            fixed4 _SegmentGhostColor;
            float _SlantShear;
            fixed4 _BacklightColor;
            fixed4 _BorderHighlight;
            float _BorderWidth;

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

                // 1. 航空仪表 8° 倾斜斜体变换 (Aerospace Slanted Shear)
                float2 shearedUv = uv;
                shearedUv.x += (0.5 - uv.y) * _SlantShear;

                // 2. 采样主文本 / 笔段字模
                half4 fontSample = (tex2D(_MainTex, shearedUv) + _TextureSampleAdd);
                float litAlpha = fontSample.a;

                // 3. 边框距离场 (Border Frame SDF)
                float distToEdge = min(min(uv.x, 1.0 - uv.x), min(uv.y, 1.0 - uv.y));
                float isBorder = step(distToEdge, _BorderWidth);

                // 4. 颜色与材质混合
                fixed4 charColor = _SegmentLitColor * IN.color;
                fixed3 finalRgb;
                float finalAlpha;

                if (litAlpha > 0.05)
                {
                    // 点亮状态的数码笔段 (高饱和发光 + 边缘锐利抗锯齿)
                    finalRgb = charColor.rgb;
                    finalAlpha = litAlpha * charColor.a;
                }
                else
                {
                    // 未点亮数码段的微妙阴影轮廓 (真实 LCD / 荧光管物理底纹)
                    finalRgb = _SegmentGhostColor.rgb;
                    finalAlpha = _SegmentGhostColor.a * 0.4;
                }

                // 叠加微弱边框微导光
                finalRgb = lerp(finalRgb, _BorderHighlight.rgb, isBorder * _BorderHighlight.a);

                fixed4 finalCol = fixed4(finalRgb, saturate(finalAlpha));

                // 5. UGUI 视口裁切支持
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
