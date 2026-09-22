Shader "ModularFlightPanel/RadialSegmentedMeter"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _ActiveColor ("Active Color", Color) = (0.14, 1.0, 0.0, 1.0) // 亮绿
        _InactiveColor ("Inactive Color", Color) = (0.05, 0.2, 0.1, 0.6) // 暗底
        _BorderColor ("Border/Outline Color", Color) = (0.0, 1.0, 0.8, 0.9)

        _FillAmount ("Fill Amount", Range(0.0, 1.0)) = 0.75
        _StartAngle ("Start Angle (Degrees)", Float) = 110.0
        _EndAngle ("End Angle (Degrees)", Float) = 250.0
        
        _InnerRadius ("Inner Radius", Range(0.0, 1.0)) = 0.65
        _OuterRadius ("Outer Radius", Range(0.0, 1.0)) = 0.85
        
        _SegmentCount ("Segment Count", Float) = 10.0
        _SegmentGap ("Segment Gap (Ratio)", Range(0.0, 0.5)) = 0.18
        
        _DitherDensity ("Inner Dither Density", Float) = 24.0
        _DitherStrength ("Inner Dither Strength", Range(0.0, 1.0)) = 0.35
        _Clockwise ("Clockwise (0=CCW, 1=CW)", Float) = 0.0

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

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;
            fixed4 _ActiveColor;
            fixed4 _InactiveColor;
            fixed4 _BorderColor;
            float _FillAmount;
            float _StartAngle;
            float _EndAngle;
            float _InnerRadius;
            float _OuterRadius;
            float _SegmentCount;
            float _SegmentGap;
            float _DitherDensity;
            float _DitherStrength;
            float _Clockwise;
            float4 _ClipRect;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = v.texcoord;
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // 以 (0.5, 0.5) 为中心计算极坐标
                float2 centered = IN.texcoord - float2(0.5, 0.5);
                float radius = length(centered) * 2.0; // 0.0 ~ 1.0 (归一化半径)

                // 半径裁剪
                if (radius < _InnerRadius || radius > _OuterRadius)
                {
                    discard;
                }

                // 角度计算：0度指向正右(X+)，逆时针递增，转换为 0~360 度
                float rawAngle = atan2(centered.y, centered.x) * 57.29578;
                if (rawAngle < 0.0) rawAngle += 360.0;

                // 规范化角度范围 [_StartAngle, _EndAngle]，支持顺时针与逆时针
                float startA = _StartAngle;
                float endA = _EndAngle;
                float span, relAngle;

                if (_Clockwise > 0.5)
                {
                    span = startA - endA;
                    if (span < 0.0) span += 360.0;
                    relAngle = startA - rawAngle;
                    if (relAngle < 0.0) relAngle += 360.0;
                }
                else
                {
                    span = endA - startA;
                    if (span < 0.0) span += 360.0;
                    relAngle = rawAngle - startA;
                    if (relAngle < 0.0) relAngle += 360.0;
                }

                // 超出扇形角度则舍弃
                if (relAngle > span)
                {
                    discard;
                }

                // 计算当前角度在总弧长中的归一化进度 [0.0, 1.0]
                float progress = relAngle / max(span, 0.001);

                // 分段计算
                float segProgress = progress * _SegmentCount;
                float segIndex = floor(segProgress);
                float segFrac = frac(segProgress); // 0.0 ~ 1.0 在当前单格内的位置

                // 裁剪格间缝隙
                if (segFrac > (1.0 - _SegmentGap))
                {
                    discard;
                }

                // 边框描边检测
                float isRadialBorder = step(segFrac, 0.06) + step(1.0 - _SegmentGap - 0.06, segFrac);
                float isRingBorder = step(radius - _InnerRadius, 0.02) + step(_OuterRadius - radius, 0.02);
                float isBorder = saturate(isRadialBorder + isRingBorder);

                // 激活状态判定 (分段与 FillAmount 关系)
                float activeThreshold = _FillAmount * _SegmentCount;
                bool isActive = (segIndex < activeThreshold);

                fixed4 segColor = isActive ? _ActiveColor : _InactiveColor;

                // 内部点阵/方格纹理 (赛博特色点阵背景)
                float2 cellDither = frac(IN.texcoord * _DitherDensity) - 0.5;
                float ditherMask = step(length(cellDither), 0.28);
                segColor.rgb = lerp(segColor.rgb, segColor.rgb * (1.0 + _DitherStrength), ditherMask * isActive);

                // 叠加高亮边框
                fixed4 finalColor = lerp(segColor, _BorderColor, isBorder * 0.75);
                finalColor *= IN.color;

                #ifdef UNITY_UI_CLIP_RECT
                finalColor.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip (finalColor.a - 0.001);
                #endif

                return finalColor;
            }
            ENDCG
        }
    }
}
