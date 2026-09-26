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
                // 1. 采样字模纹理 Alpha
                half4 fontSample = tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd;
                float litAlpha = fontSample.a;

                // 彻底丢弃非笔画区域，杜绝任何实心灰色方块底盒产生！
                if (litAlpha < 0.005)
                {
                    discard;
                }

                // 2. 语义色彩智能门控 (Semantic Color Preservation):
                // 若顶点颜色饱和度高 (如红色警报、黄色注意、绿色状态)，100% 忠实保留原始语义色！
                // 仅当顶点为中性白光时，才投射数码管经典琥珀/数码原色 (_SegmentLitColor)
                float maxC = max(max(IN.color.r, IN.color.g), IN.color.b);
                float minC = min(min(IN.color.r, IN.color.g), IN.color.b);
                float saturation = maxC - minC;
                fixed3 baseColor = (saturation > 0.15) ? IN.color.rgb : (IN.color.rgb * _SegmentLitColor.rgb);

                // 3. 数码管物理微缝隙与分段纹理 (Segment Micro-Grooves)
                // 周期设为 8 像素平滑滤波，微刻槽深度受控 (最大 6%)，绝不破坏文字笔画
                float groove = 1.0 - (sin(IN.worldPosition.y * 0.7854) * 0.5 + 0.5) * min(_SegmentGrooveContrast, 0.06);

                // 4. 核心白炽发光核 (Filament Overdrive Core - 25% 饱和微过载)
                float coreWeight = saturate(pow(litAlpha, 3.0) * 0.25);
                fixed3 hotColor = lerp(baseColor, _CoreHotColor.rgb, coreWeight);
                fixed3 finalRgb = hotColor * groove;

                // 5. 边缘抗锯齿与辉光融合 (Crisp Filament Halo)
                float finalAlpha = saturate(pow(litAlpha, 0.90) * (1.0 + _GlowStrength * 0.20)) * IN.color.a;
                fixed4 finalCol = fixed4(finalRgb, finalAlpha);

                // 6. UGUI 视口裁切保护
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
