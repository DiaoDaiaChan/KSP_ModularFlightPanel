Shader "ModularFlightPanel/AvionicsProceduralUI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        // 统一底层图元模式 (0 = LinearTape, 1 = RadialArc, 2 = SegmentedBar, 3 = PrecisionGlass)
        _PrimitiveMode ("Primitive Mode", Float) = 0.0

        // 核心动态遥测驱动参数
        _Value ("Current Primary Value", Float) = 0.0
        _ValueMin ("Minimum Value Clamp", Float) = 0.0
        _ValueMax ("Maximum Value Clamp", Float) = 100.0
        _WindowRange ("Window Value Span", Float) = 60.0
        _MajorStep ("Major Tick Step", Float) = 10.0
        _MediumStep ("Medium Tick Step", Float) = 5.0
        _MinorStep ("Minor Tick Step", Float) = 1.0

        // 通用多模式自适应几何参数 (Vector4 x,y,z,w)
        // Tape:   x=Alignment(0=Left,1=Right), y=GroundLevel(-1=Off), z=TickLenScale, w=Vignette
        // Arc:    x=StartAngle, y=SweepAngle, z=InnerRadius, w=OuterRadius
        // Bar:    x=SegmentCount, y=GapRatio, z=Orientation(0=H,1=V), w=CornerRound
        // Glass:  x=CornerChamfer, y=BorderWidth, z=InnerShadow, w=GradientStr
        _AvionicsParams ("Avionics Geometry Parameters", Vector) = (0, -1, 1, 0.15)

        // 航电语义统一调色板
        _GlassBgColor ("Glass Background", Color) = (0.05, 0.08, 0.12, 0.78)
        _RailColor ("Rail & Line Color", Color) = (0.35, 0.75, 1.0, 0.65)
        _AccentColor ("Major Accent Color", Color) = (0.90, 0.96, 1.0, 0.90)
        _WarningColor ("Warning Color", Color) = (1.0, 0.68, 0.05, 0.85)
        _DangerColor ("Danger Color", Color) = (0.95, 0.18, 0.18, 0.85)

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
            Name "AvionicsProceduralCore"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float4 _ClipRect;

            float _PrimitiveMode;
            float _Value;
            float _ValueMin;
            float _ValueMax;
            float _WindowRange;
            float _MajorStep;
            float _MediumStep;
            float _MinorStep;
            float4 _AvionicsParams;

            fixed4 _GlassBgColor;
            fixed4 _RailColor;
            fixed4 _AccentColor;
            fixed4 _WarningColor;
            fixed4 _DangerColor;

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(o.worldPosition);
                o.uv = v.texcoord;
                o.color = v.color * _Color;
                return o;
            }

            // 模式 0: 线性滚动标尺带 (Linear Tape Scale)
            fixed4 RenderLinearTape(float2 uv)
            {
                fixed4 col = _GlassBgColor;

                float vignetteStr = max(_AvionicsParams.w, 0.02);
                float topFade = smoothstep(1.0, 1.0 - vignetteStr, uv.y);
                float botFade = smoothstep(0.0, vignetteStr, uv.y);
                float vignette = topFade * botFade;

                float isRightAlign = step(0.5, _AvionicsParams.x);
                float railDist = isRightAlign ? uv.x : (1.0 - uv.x);

                float windowSpan = max(_WindowRange, 1.0);
                float currentVal = _Value + (uv.y - 0.5) * windowSpan;
                float groundLevel = _AvionicsParams.y;

                // 地面贴地警示斑马带
                if (groundLevel > -90000.0 && currentVal <= groundLevel)
                {
                    float stripeCoord = (uv.x * 6.0 + uv.y * 18.0);
                    float stripeFrac = frac(stripeCoord);
                    float stripeAA = max(fwidth(stripeFrac) * 1.5, 0.04);
                    float isStripeA = smoothstep(0.5 - stripeAA, 0.5 + stripeAA, stripeFrac);
                    fixed4 hazardCol = lerp(_WarningColor, _DangerColor, isStripeA);
                    col = lerp(col, hazardCol, 0.72);
                }

                // 垂直导轨基准线
                float railWidth = 0.025;
                float railAA = max(fwidth(railDist) * 1.5, 0.004);
                float isRail = 1.0 - smoothstep(railWidth - railAA, railWidth + railAA, railDist);
                col = lerp(col, _RailColor, isRail * _RailColor.a);

                // 周期性精密刻度线求值
                float valAA = max(fwidth(currentVal) * 1.4, 0.02 * windowSpan);

                // 主刻度 (Major)
                float majorStep = max(_MajorStep, 0.1);
                float majorMod = abs(fmod(abs(currentVal) + majorStep * 0.5, majorStep) - majorStep * 0.5);
                float isMajorVal = 1.0 - smoothstep(0.0, valAA, majorMod);
                float majorLen = 0.35 * _AvionicsParams.z;
                float isMajorX = 1.0 - smoothstep(majorLen - railAA, majorLen + railAA, railDist);
                float majorTick = isMajorVal * isMajorX;

                // 中刻度 (Medium)
                float medStep = max(_MediumStep, 0.05);
                float medMod = abs(fmod(abs(currentVal) + medStep * 0.5, medStep) - medStep * 0.5);
                float isMedVal = (1.0 - smoothstep(0.0, valAA, medMod)) * (1.0 - isMajorVal);
                float medLen = 0.22 * _AvionicsParams.z;
                float isMedX = 1.0 - smoothstep(medLen - railAA, medLen + railAA, railDist);
                float medTick = isMedVal * isMedX;

                // 微刻度 (Minor)
                float minStep = max(_MinorStep, 0.01);
                float minMod = abs(fmod(abs(currentVal) + minStep * 0.5, minStep) - minStep * 0.5);
                float isMinVal = (1.0 - smoothstep(0.0, valAA, minMod)) * (1.0 - isMajorVal) * (1.0 - isMedVal);
                float minLen = 0.12 * _AvionicsParams.z;
                float isMinX = 1.0 - smoothstep(minLen - railAA, minLen + railAA, railDist);
                float minTick = isMinVal * isMinX;

                float totalTick = saturate(majorTick + medTick * 0.82 + minTick * 0.55);
                fixed4 tickColor = lerp(_RailColor, _AccentColor, majorTick);
                col = lerp(col, tickColor, totalTick * tickColor.a * vignette);

                // 外缘微倒角光边
                float outerDist = isRightAlign ? (1.0 - uv.x) : uv.x;
                float isOuterRim = 1.0 - smoothstep(0.015 - railAA, 0.015 + railAA, outerDist);
                col.rgb += _AccentColor.rgb * isOuterRim * 0.18;

                col.a *= vignette;
                return col;
            }

            // 模式 1: 圆弧与环形仪表 (Radial Arc Meter)
            fixed4 RenderRadialArc(float2 uv)
            {
                fixed4 col = _GlassBgColor;
                float2 p = (uv - 0.5) * 2.0;
                float r = length(p);
                float angle = atan2(p.y, p.x) * 57.2957795; // [-180, 180]
                if (angle < 0.0) angle += 360.0;

                float startAngle = _AvionicsParams.x;
                float sweepAngle = max(_AvionicsParams.y, 1.0);
                float innerR = clamp(_AvionicsParams.z, 0.1, 0.9);
                float outerR = clamp(_AvionicsParams.w, innerR + 0.05, 1.0);

                // 归一化圆弧内角 [0, 1]
                float arcRelAngle = fmod(angle - startAngle + 360.0, 360.0);
                float normAngle = arcRelAngle / sweepAngle;

                float rAA = max(fwidth(r) * 1.5, 0.005);
                float isInsideRing = smoothstep(innerR - rAA, innerR + rAA, r) * (1.0 - smoothstep(outerR - rAA, outerR + rAA, r));
                float isInsideSector = step(normAngle, 1.0);

                float fillRatio = saturate((_Value - _ValueMin) / max(_ValueMax - _ValueMin, 0.001));
                float isFilled = step(normAngle, fillRatio);

                // 轨道底色与激活填充色
                fixed4 trackCol = lerp(_GlassBgColor, _RailColor * 0.35, 0.5);
                fixed4 activeCol = (fillRatio > 0.85) ? _DangerColor : ((fillRatio > 0.65) ? _WarningColor : _AccentColor);

                col = lerp(col, trackCol, isInsideRing * isInsideSector);
                col = lerp(col, activeCol, isInsideRing * isInsideSector * isFilled);

                // 准星发丝指针 (Needle Ray)
                float needleAngleDist = abs(normAngle - fillRatio) * sweepAngle;
                float aAA = max(fwidth(needleAngleDist) * 1.5, 0.5);
                float isNeedle = (1.0 - smoothstep(0.8 - aAA, 0.8 + aAA, needleAngleDist)) * isInsideRing;
                col = lerp(col, fixed4(1,1,1,1), isNeedle);

                // 环圈内外边缘线
                float isInnerBorder = (1.0 - smoothstep(0.015, 0.015 + rAA, abs(r - innerR))) * isInsideSector;
                float isOuterBorder = (1.0 - smoothstep(0.015, 0.015 + rAA, abs(r - outerR))) * isInsideSector;
                col = lerp(col, _RailColor, saturate(isInnerBorder + isOuterBorder));

                return col;
            }

            // 模式 2: 分段发光柱 (Segmented Bar)
            fixed4 RenderSegmentedBar(float2 uv)
            {
                fixed4 col = _GlassBgColor;
                int segCount = max((int)round(_AvionicsParams.x), 1);
                float gapRatio = clamp(_AvionicsParams.y, 0.02, 0.45);
                bool isVert = (_AvionicsParams.z > 0.5);

                float t = isVert ? uv.y : uv.x;
                float crossT = isVert ? uv.x : uv.y;

                float fillRatio = saturate((_Value - _ValueMin) / max(_ValueMax - _ValueMin, 0.001));

                float segIndexF = floor(t * (float)segCount);
                float segRatio = (segIndexF + 0.5) / (float)segCount;
                float segFrac = frac(t * (float)segCount);

                float tAA = max(fwidth(segFrac) * 1.5, 0.02);
                float crossAA = max(fwidth(crossT) * 1.5, 0.02);

                float insideSeg = smoothstep(gapRatio * 0.5 - tAA, gapRatio * 0.5 + tAA, segFrac) *
                                  (1.0 - smoothstep((1.0 - gapRatio * 0.5) - tAA, (1.0 - gapRatio * 0.5) + tAA, segFrac));
                float insideCross = smoothstep(0.05 - crossAA, 0.05 + crossAA, crossT) *
                                    (1.0 - smoothstep(0.95 - crossAA, 0.95 + crossAA, crossT));

                float isSegment = insideSeg * insideCross;
                bool isActive = (segRatio <= fillRatio);

                fixed4 segColor = isActive ? lerp(_AccentColor, _WarningColor, smoothstep(0.6, 0.9, segRatio)) : (_RailColor * 0.25);
                col = lerp(col, segColor, isSegment);

                return col;
            }

            // 模式 3: 精密航空玻璃背板与沉降槽 (Precision Glass & Inset)
            fixed4 RenderPrecisionGlass(float2 uv)
            {
                fixed4 col = _GlassBgColor;
                float chamfer = clamp(_AvionicsParams.x, 0.0, 0.2);
                float borderW = clamp(_AvionicsParams.y, 0.005, 0.08);

                // 2D 矩形距离场
                float2 d = abs(uv - 0.5) - (0.5 - chamfer);
                float dist = length(max(d, 0.0)) + min(max(d.x, d.y), 0.0) - chamfer;

                float dAA = max(fwidth(dist) * 1.5, 0.003);
                float insidePanel = 1.0 - smoothstep(0.0 - dAA, 0.0 + dAA, dist);
                if (insidePanel <= 0.0) discard;

                // 边框描边
                float isBorder = smoothstep(-borderW - dAA, -borderW + dAA, dist) * insidePanel;
                col = lerp(col, _RailColor, isBorder * _RailColor.a);

                // 顶部向下微弱光泽梯度 (Top-Down Glass Gradient)
                col.rgb += _AccentColor.rgb * uv.y * _AvionicsParams.w * 0.15;
                col.a *= insidePanel;

                return col;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col;

                if (_PrimitiveMode < 0.5)
                {
                    col = RenderLinearTape(i.uv);
                }
                else if (_PrimitiveMode < 1.5)
                {
                    col = RenderRadialArc(i.uv);
                }
                else if (_PrimitiveMode < 2.5)
                {
                    col = RenderSegmentedBar(i.uv);
                }
                else
                {
                    col = RenderPrecisionGlass(i.uv);
                }

                // UGUI 裁剪矩形与复合 Alpha
                #ifdef UNITY_UI_CLIP_RECT
                col.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif

                col.a *= i.color.a;
                return col;
            }
            ENDCG
        }
    }
    FallBack "UI/Default"
}
