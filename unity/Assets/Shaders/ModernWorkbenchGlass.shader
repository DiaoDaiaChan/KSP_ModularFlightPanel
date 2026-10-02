Shader "ModularFlightPanel/ModernWorkbenchGlass"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        // 核心暗晶磨砂玻璃底色
        _GlassBgColor ("Glass Background (Frosted)", Color) = (0.05, 0.08, 0.12, 0.94)
        _BorderColor ("Precision Border Color", Color) = (0.22, 0.35, 0.50, 0.85)
        _AccentColor ("Modern Accent Color", Color) = (0.0, 0.88, 1.0, 1.0)
        _GlowColor ("Outer Glow Color", Color) = (0.0, 0.88, 1.0, 0.35)

        // 几何形态与发光参数
        _BorderWidth ("Border Width", Range(0.0, 0.08)) = 0.008
        _CornerRadius ("SDF Corner Radius", Range(0.0, 0.25)) = 0.035
        _GlassGradientStrength ("Top-Down Lighting Sheen", Range(0.0, 0.5)) = 0.12
        _ScanlineStrength ("Cockpit Grid / Scanline Depth", Range(0.0, 0.3)) = 0.04
        _HoverGlow ("Interactive Hover Glow", Range(0.0, 1.5)) = 0.0
        _DropShadowStrength ("Drop Shadow Ambient", Range(0.0, 1.0)) = 0.25

        // UGUI 系统参数 (Required for UI.Mask & Canvas compatibility)
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
            Name "ModernWorkbenchGlassPass"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile __ UNITY_UI_CLIP_RECT

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
                float2 uv            : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;

            fixed4 _GlassBgColor;
            fixed4 _BorderColor;
            fixed4 _AccentColor;
            fixed4 _GlowColor;

            float _BorderWidth;
            float _CornerRadius;
            float _GlassGradientStrength;
            float _ScanlineStrength;
            float _HoverGlow;
            float _DropShadowStrength;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.uv = v.texcoord;
                OUT.color = v.color * _Color;
                return OUT;
            }

            // 2D 矩形有向距离场 (Signed Distance Field)
            float sdRoundedBox(float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + float2(r, r);
                return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 uv = IN.uv;

                // 将 UV 居中并映射为 [-1, 1] 坐标系进行 SDF 圆角计算
                float2 p = uv * 2.0 - 1.0;
                float2 halfSize = float2(1.0, 1.0);
                float r = clamp(_CornerRadius * 2.0, 0.0, 0.9);

                float dist = sdRoundedBox(p, halfSize, r);

                // 亚像素抗锯齿边缘平滑 (fwidth)
                float edgeWidth = fwidth(dist);
                float insideAlpha = 1.0 - smoothstep(-edgeWidth, edgeWidth, dist);

                if (insideAlpha <= 0.001)
                {
                    discard;
                }

                // 基础纹理采样（支持 Sprite 贴图与纯白遮罩）
                fixed4 tex = tex2D(_MainTex, uv) + _TextureSampleAdd;
                fixed4 baseTint = tex * IN.color;

                // 1. 深度暗晶背景 + 垂直漫反射高光渐变
                fixed4 glass = _GlassBgColor;
                float topSheen = (1.0 - uv.y) * _GlassGradientStrength;
                glass.rgb += topSheen;

                // 2. 细微航空座舱扫描线与网格质感 (Scanlines)
                if (_ScanlineStrength > 0.001)
                {
                    float scan = sin(uv.y * 300.0) * _ScanlineStrength;
                    glass.rgb += scan * 0.5;
                }

                // 3. 悬浮光晕与交互增益 (Hover Aura)
                if (_HoverGlow > 0.001)
                {
                    glass.rgb = lerp(glass.rgb, glass.rgb + _AccentColor.rgb * 0.15, _HoverGlow);
                    glass.a = saturate(glass.a + _HoverGlow * 0.08);
                }

                fixed4 finalCol = glass * baseTint;

                // 4. 程序化发光边框 (SDF Border & Outer Glow)
                if (_BorderWidth > 0.001)
                {
                    float innerDist = dist + _BorderWidth * 2.0;
                    float borderMask = smoothstep(-edgeWidth, edgeWidth, innerDist);
                    borderMask = saturate(borderMask);

                    // 顶部微妙强调色浸润
                    float topAccent = smoothstep(0.85, 1.0, uv.y) * 0.6;
                    fixed4 targetBorder = lerp(_BorderColor, _AccentColor, topAccent);

                    if (_HoverGlow > 0.001)
                    {
                        targetBorder = lerp(targetBorder, _AccentColor, _HoverGlow * 0.7);
                    }

                    finalCol.rgb = lerp(finalCol.rgb, targetBorder.rgb, borderMask);
                    finalCol.a = max(finalCol.a, targetBorder.a * borderMask);
                }

                finalCol.a *= insideAlpha;

                #ifdef UNITY_UI_CLIP_RECT
                finalCol.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                return finalCol;
            }
            ENDCG
        }
    }
}
