Shader "ModularFlightPanel/DotMatrixUI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite / Font Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _DotSpacing ("Dot Spacing (Screen Pixels)", Float) = 2.0
        _DotRadius ("Dot Fill Radius (0.1 - 0.5)", Range(0.1, 0.5)) = 0.42
        _DotSmoothness ("Dot Edge Softness", Range(0.01, 0.3)) = 0.12
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
                // 1. 高保真原样字模采样 (严格避免 4 向扩张造成 6-10px 微型字模与中文字孔隙闭合粘连)
                half4 texCol = tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd;
                float strokeAlpha = texCol.a;

                if (strokeAlpha < 0.01)
                {
                    discard;
                }

                // 2. 物理屏幕像素坐标解算与整数像素锁频 (Integer Pixel Grid Snapping)
                // 彻底根除因非整数点距在屏幕离散像素上引发的高频漂移、割裂断画与摩尔波纹 (Moiré)
                float2 screenPixel = (IN.screenPos.xy / max(IN.screenPos.w, 0.00001)) * _ScreenParams.xy;
                float spacing = max(round(_DotSpacing), 2.0);
                float2 dotCoord = screenPixel / spacing;
                float2 cellFrac = frac(dotCoord);
                float distToCenter = length(cellFrac - 0.5);

                // 3. 物理圆孔透镜透光率 (Micro-Lens Aperture) 与光学弥散晕 (Diffusion Halo)
                float dotShape = 1.0 - smoothstep(_DotRadius - _DotSmoothness, _DotRadius, distToCenter);
                float halo = exp(-distToCenter * 3.5) * _GlowStrength;

                // 4. 高动态发光调制比 (High Contrast Optical Modulation: 3:1):
                // 保持孔内核 125% 激发，孔间导光基质维持 38% 连接底衬，
                // 彻底根除因点阵挖孔造成的细小笔画断裂与小字杂碎斑驳，同时完美呈现物理 LED 圆点微雕质感！
                float dotMod = (0.38 + 0.87 * dotShape) + halo * 0.25;

                // 5. 中心白炽微二极管发光核 (White-Hot Diode Core)
                // 仅在字模致密核心区激发，呈现真实物理 LED 晶体芯片高能亮点
                float diodeCore = dotShape * saturate(1.0 - distToCenter / 0.25) * saturate((strokeAlpha - 0.25) * 1.5) * 0.45;

                // 6. 语义色彩智能门控 (Semantic Color Preservation):
                // 若顶点颜色饱和度高 (如警示黄、危险红、正向绿、青色读数)，100% 忠实保留原始语义色！
                float maxC = max(max(IN.color.r, IN.color.g), IN.color.b);
                float minC = min(min(IN.color.r, IN.color.g), IN.color.b);
                float saturation = maxC - minC;
                fixed3 baseColor = (saturation > 0.15) ? IN.color.rgb : (IN.color.rgb * _LitDotColor.rgb);

                fixed3 litRgb = (baseColor * dotMod + fixed3(0.85, 1.0, 0.90) * diodeCore) * strokeAlpha;

                // 7. 伽马抗蚀刻强化与最终合成
                float finalAlpha = saturate(pow(strokeAlpha, 0.85) * (1.0 + _GlowStrength * 0.20)) * IN.color.a;
                fixed4 finalCol = fixed4(litRgb, finalAlpha);

                // 8. UGUI 视口裁切支持
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
