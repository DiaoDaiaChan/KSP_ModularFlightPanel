Shader "ModularFlightPanel/PhosphorHoloUI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite / Font Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _PhosphorColor ("Phosphor Emission Color", Color) = (0.2, 0.95, 0.65, 1.0)
        _CoreHotColor ("Beam Overdrive Hot Core", Color) = (0.95, 1.0, 0.98, 1.0)
        _Aberration ("Chromatic Dispersion (Texels)", Range(0.0, 1.5)) = 0.65
        _ScanlineDepth ("Scanline Shadow Depth", Range(0.0, 0.5)) = 0.12
        _BloomStrength ("Phosphor Halo Bloom", Range(0.0, 1.0)) = 0.40

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
            float4 _MainTex_TexelSize;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;

            fixed4 _PhosphorColor;
            fixed4 _CoreHotColor;
            float _Aberration;
            float _ScanlineDepth;
            float _BloomStrength;

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

                // 1. 中心主通道采样 (Green Channel)
                half4 sampleG = tex2D(_MainTex, uv) + _TextureSampleAdd;
                float alphaG = sampleG.a;

                // 严格绝缘：若字模主笔画透明，直接丢弃，绝不允许任何邻近字符污染
                if (alphaG < 0.005)
                {
                    discard;
                }

                // 2. 贴图像素受控微色散 (Texel-Constrained Dispersion)
                float2 offset = float2(_Aberration * _MainTex_TexelSize.x * 0.75, 0.0);
                float alphaR = (tex2D(_MainTex, uv + offset) + _TextureSampleAdd).a;
                float alphaB = (tex2D(_MainTex, uv - offset) + _TextureSampleAdd).a;

                // 严格以主字模 Alpha 进行钳制门控，杜绝越界拾取邻近字符
                alphaR = min(alphaR, alphaG * 1.35);
                alphaB = min(alphaB, alphaG * 1.35);

                // 3. 电子束高能过载白热核 (Beam Overdrive White-Hot Core)
                float coreWeight = pow(alphaG, 2.2);
                fixed3 tint = _PhosphorColor.rgb * IN.color.rgb;
                fixed3 baseColor = lerp(tint, _CoreHotColor.rgb, coreWeight * 0.85);

                // 4. 边缘微色散重组
                fixed3 finalRgb = fixed3(
                    baseColor.r * lerp(1.0, alphaR / max(alphaG, 0.001), 0.25),
                    baseColor.g,
                    baseColor.b * lerp(1.0, alphaB / max(alphaG, 0.001), 0.25)
                );

                // 5. 本地 Canvas 空间平滑微扫描线 (Canvas-Local Anti-Aliased Scanlines)
                float scan = 1.0 - (sin(IN.worldPosition.y * 1.5708) * 0.5 + 0.5) * _ScanlineDepth;
                finalRgb *= scan;

                // 6. 磷光光子辉光与透明度合成
                float totalAlpha = saturate(alphaG * (1.0 + _BloomStrength * 0.35)) * IN.color.a;
                fixed4 finalCol = fixed4(finalRgb, totalAlpha);

                // 7. UGUI 视口裁切保护
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
