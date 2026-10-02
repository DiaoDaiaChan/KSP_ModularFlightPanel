Shader "ModularFlightPanel/NavballRaymarch"
{
    Properties
    {
        [PerRendererData] _MainTex ("Navball Texture", 2D) = "white" {}
        _RenderMode ("Render Mode (0=Stock, 1=Vector, 2=Bake)", Float) = 0.0
        _SphereInvRotation ("Sphere Inverse Rotation", Vector) = (0, 0, 0, 1)

        // 现代玻璃座舱配色与渐变 (Aero Glass Cockpit Palette)
        _SkyZenithColor ("Sky Zenith", Color) = (0.04, 0.16, 0.36, 1.0)
        _SkyHorizonColor ("Sky Horizon", Color) = (0.08, 0.46, 0.72, 1.0)
        _GroundHorizonColor ("Ground Horizon", Color) = (0.45, 0.25, 0.12, 1.0)
        _GroundNadirColor ("Ground Nadir", Color) = (0.22, 0.12, 0.05, 1.0)

        // 地平线与标线
        _EquatorColor ("Equator Line Color", Color) = (1.0, 1.0, 1.0, 1.0)
        _EquatorWidth ("Equator Width", Range(0.001, 0.015)) = 0.004
        _LabelColor ("Numeral Color", Color) = (0.94, 0.97, 1.0, 0.94)
        _LabelOutlineColor ("Numeral Outline", Color) = (0.015, 0.025, 0.04, 0.78)

        _PitchLadderColor ("Pitch Ladder Color", Color) = (0.94, 0.97, 1.0, 0.88)
        _PitchLadderWidth ("Pitch Ladder Thickness", Range(0.001, 0.015)) = 0.003
        _HeadingLineColor ("Heading Line Color", Color) = (0.35, 0.80, 1.0, 0.38)

        // 3D 深度与边缘微光 (Limb Darkening & Rim Glow)
        _LimbPower ("Limb Darkening Power", Range(0.5, 4.0)) = 1.35
        _LimbIntensity ("Limb Darkening Intensity", Range(0.0, 1.0)) = 0.32
        _RimColor ("Rim Glow Color", Color) = (0.2, 0.78, 1.0, 1.0)
        _RimPower ("Rim Power", Range(1.0, 8.0)) = 3.2
        _RimIntensity ("Rim Intensity", Range(0.0, 2.0)) = 0.32

        // 弧面防眩玻璃反光 (Curved Anti-Reflective Lens Specular)
        _SpecularColor ("Glass Specular Color", Color) = (1.0, 1.0, 1.0, 0.35)
        _Glossiness ("Glossiness", Range(4.0, 64.0)) = 28.0
        _SpecIntensity ("Specular Intensity", Range(0.0, 1.0)) = 0.18

        // 航电动态特性驱动 (Dynamic Avionics Parameters)
        _NumeralRollAngle ("Numeral Roll Angle (Rad)", Float) = 0.0
        _NumeralUprightMode ("Numeral Upright Mode", Range(0.0, 1.0)) = 1.0
        _NumeralTangentComp ("Tangent Foreshortening Comp", Range(0.0, 1.0)) = 1.0
        _DetailScale ("Screen-Size Detail", Range(0.0, 1.0)) = 1.0
        _FramePattern ("Reference Frame Pattern", Range(0.0, 5.0)) = 0.0
        _FramePatternOld ("Old Reference Frame Pattern", Range(0.0, 5.0)) = 0.0
        _FrameTransitionProgress ("Frame Transition Progress", Range(0.0, 1.0)) = 1.0
        _TrendRotation ("Predicted Attitude Delta", Vector) = (0, 0, 0, 1)
        _TrendStrength ("Attitude Trend Strength", Range(0.0, 1.0)) = 0.0
        _MarkerAvoid0 ("Marker Avoidance 0", Vector) = (0, 0, 0, 0)
        _MarkerAvoid1 ("Marker Avoidance 1", Vector) = (0, 0, 0, 0)
        _MarkerAvoid2 ("Marker Avoidance 2", Vector) = (0, 0, 0, 0)
        _MarkerAvoid3 ("Marker Avoidance 3", Vector) = (0, 0, 0, 0)

        // 合成视景近地防撞警示 (GPWS Ground Hazard Warning)
        _GroundHazardAlert ("Ground Hazard Alert", Range(0.0, 1.0)) = 0.0

        // 近地平精细游标阶梯 (Vernier Fine Scale Detail)
        _VernierScaleDetail ("Vernier Scale Detail", Range(0.0, 1.0)) = 0.0

        // 视口孔径与几何长宽比 (Aperture Shape & Aspect Ratio)
        _ApertureShape ("Aperture Shape (0=Circle, 1=Rectangle)", Float) = 0.0
        _PfdMode ("PFD Dimension-Reduced Mode (0=3D Sphere, 1=PFD)", Float) = 1.0
        _AspectRatio ("Aspect Ratio (Width / Height)", Float) = 1.0
        _CornerRadius ("Corner Radius", Range(0.0, 0.5)) = 0.05
        _FovScale ("FOV Scale", Range(0.4, 2.5)) = 1.0

        // UGUI 系统参数 (Required for UI.Mask & Canvas compatibility)
        _Color ("Tint", Color) = (1,1,1,1)
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
            Name "NavballRaymarchUI"

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
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            float _RenderMode;
            float4 _SphereInvRotation;

            fixed4 _SkyZenithColor;
            fixed4 _SkyHorizonColor;
            fixed4 _GroundHorizonColor;
            fixed4 _GroundNadirColor;

            fixed4 _EquatorColor;
            float _EquatorWidth;
            fixed4 _LabelColor;
            fixed4 _LabelOutlineColor;

            fixed4 _PitchLadderColor;
            float _PitchLadderWidth;
            fixed4 _HeadingLineColor;

            float _LimbPower;
            float _LimbIntensity;
            fixed4 _RimColor;
            float _RimPower;
            float _RimIntensity;

            fixed4 _SpecularColor;
            float _Glossiness;
            float _SpecIntensity;

            float _NumeralRollAngle;
            float _NumeralUprightMode;
            float _NumeralTangentComp;
            float _DetailScale;
            float _FramePattern;
            float _FramePatternOld;
            float _FrameTransitionProgress;
            float4 _TrendRotation;
            float _TrendStrength;
            float4 _MarkerAvoid0;
            float4 _MarkerAvoid1;
            float4 _MarkerAvoid2;
            float4 _MarkerAvoid3;
            float _GroundHazardAlert;
            float _VernierScaleDetail;
            float _ApertureShape;
            float _PfdMode;
            float _AspectRatio;
            float _CornerRadius;
            float _FovScale;

            fixed4 _Color;
            float4 _ClipRect;

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

            float SegmentDistance(float2 p, float2 center, float2 halfSize)
            {
                float2 d = abs(p - center) - halfSize;
                return max(d.x, d.y);
            }

            float SegmentSDF(float2 p, float2 a, float2 b)
            {
                float2 pa = p - a, ba = b - a;
                float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
                return length(pa - ba * h);
            }

            // 现代航电高精度微雕刻矢量字模 (DIN 1451 / ICAO 高清晰航电规范)
            // 拓扑开口大、字怀开阔、无混淆异构，极低分辨率下依然 100% 锐利可辨
            float DigitDistance(float2 p, float digitF)
            {
                int digit = (int)round(digitF);
                float d = 100.0;
                float r = 0.32; // 纯正航电笔画微半径 (全宽 0.64)

                // 核心控制尺寸与拓扑骨架 (开阔易辨)
                float w = 1.05;
                float h = 2.25;

                if (digit == 0)
                {
                    // 0: 饱满开阔椭圆，上下端平直收敛
                    d = min(d, SegmentSDF(p, float2(-w + 0.35, h), float2(w - 0.35, h)));
                    d = min(d, SegmentSDF(p, float2(-w + 0.35, -h), float2(w - 0.35, -h)));
                    d = min(d, SegmentSDF(p, float2(-w, -h + 0.40), float2(-w, h - 0.40)));
                    d = min(d, SegmentSDF(p, float2(w, -h + 0.40), float2(w, h - 0.40)));
                    d = min(d, SegmentSDF(p, float2(-w, h - 0.40), float2(-w + 0.35, h)));
                    d = min(d, SegmentSDF(p, float2(w - 0.35, h), float2(w, h - 0.40)));
                    d = min(d, SegmentSDF(p, float2(-w, -h + 0.40), float2(-w + 0.35, -h)));
                    d = min(d, SegmentSDF(p, float2(w - 0.35, -h), float2(w, -h + 0.40)));
                }
                else if (digit == 1)
                {
                    // 1: 航电标准立柱 + 锐利顶尖翼标 + 稳固底座
                    d = min(d, SegmentSDF(p, float2(0.20, -h), float2(0.20, h)));
                    d = min(d, SegmentSDF(p, float2(-0.75, h - 0.85), float2(0.20, h)));
                    d = min(d, SegmentSDF(p, float2(-0.65, -h), float2(0.85, -h)));
                }
                else if (digit == 2)
                {
                    // 2: 拱顶圆滑过渡 + 坚实对角斜撑 + 稳健底横
                    d = min(d, SegmentSDF(p, float2(-w + 0.20, h), float2(w - 0.20, h)));
                    d = min(d, SegmentSDF(p, float2(w - 0.20, h), float2(w, h - 0.45)));
                    d = min(d, SegmentSDF(p, float2(w, h - 0.45), float2(w, 0.45)));
                    d = min(d, SegmentSDF(p, float2(w, 0.45), float2(-w, -h)));
                    d = min(d, SegmentSDF(p, float2(-w, -h), float2(w, -h)));
                }
                else if (digit == 3)
                {
                    // 3: 上下双弧环，中梁右缩，左侧完全通畅开放
                    d = min(d, SegmentSDF(p, float2(-w + 0.15, h), float2(w - 0.20, h)));
                    d = min(d, SegmentSDF(p, float2(w - 0.20, h), float2(w, 0.35)));
                    d = min(d, SegmentSDF(p, float2(w, 0.35), float2(0.05, 0.0)));
                    d = min(d, SegmentSDF(p, float2(0.05, 0.0), float2(w, -0.35)));
                    d = min(d, SegmentSDF(p, float2(w, -0.35), float2(w, -h + 0.35)));
                    d = min(d, SegmentSDF(p, float2(w, -h + 0.35), float2(w - 0.30, -h)));
                    d = min(d, SegmentSDF(p, float2(w - 0.30, -h), float2(-w + 0.15, -h)));
                }
                else if (digit == 4)
                {
                    // 4: 开放式航空 4，右立柱贯穿，横梁开阔
                    d = min(d, SegmentSDF(p, float2(0.50, -h), float2(0.50, h)));
                    d = min(d, SegmentSDF(p, float2(0.50, h), float2(-w, -0.25)));
                    d = min(d, SegmentSDF(p, float2(-w, -0.25), float2(w, -0.25)));
                }
                else if (digit == 5)
                {
                    // 5: 顶部直横 + 锐利左立折 + 下半饱满弧
                    d = min(d, SegmentSDF(p, float2(-w, h), float2(w, h)));
                    d = min(d, SegmentSDF(p, float2(-w, h), float2(-w, 0.15)));
                    d = min(d, SegmentSDF(p, float2(-w, 0.15), float2(w - 0.20, 0.15)));
                    d = min(d, SegmentSDF(p, float2(w - 0.20, 0.15), float2(w, -0.15)));
                    d = min(d, SegmentSDF(p, float2(w, -0.15), float2(w, -h + 0.35)));
                    d = min(d, SegmentSDF(p, float2(w, -h + 0.35), float2(w - 0.30, -h)));
                    d = min(d, SegmentSDF(p, float2(w - 0.30, -h), float2(-w + 0.15, -h)));
                }
                else if (digit == 6)
                {
                    // 6: 严谨航空 6，右上角彻底敞开 (绝无顶横挡板)，杜绝与 8 混淆！
                    d = min(d, SegmentSDF(p, float2(0.40, h), float2(-w + 0.40, h)));
                    d = min(d, SegmentSDF(p, float2(-w + 0.40, h), float2(-w, h - 0.45)));
                    d = min(d, SegmentSDF(p, float2(-w, h - 0.45), float2(-w, -h + 0.40)));
                    d = min(d, SegmentSDF(p, float2(-w, -h + 0.40), float2(-w + 0.40, -h)));
                    d = min(d, SegmentSDF(p, float2(-w + 0.40, -h), float2(w - 0.40, -h)));
                    d = min(d, SegmentSDF(p, float2(w - 0.40, -h), float2(w, -h + 0.40)));
                    d = min(d, SegmentSDF(p, float2(w, -h + 0.40), float2(w, 0.0)));
                    d = min(d, SegmentSDF(p, float2(w, 0.0), float2(-w, 0.0)));
                }
                else if (digit == 7)
                {
                    // 7: 顶部直横 + 优雅斜撑
                    d = min(d, SegmentSDF(p, float2(-w, h), float2(w, h)));
                    d = min(d, SegmentSDF(p, float2(w, h), float2(-0.25, -h)));
                }
                else if (digit == 8)
                {
                    // 8: 经典双环结构，腰部收窄 (0.65*w)，与 6 / 9 / 0 形成鲜明反差
                    d = min(d, SegmentSDF(p, float2(-w + 0.35, h), float2(w - 0.35, h)));
                    d = min(d, SegmentSDF(p, float2(-w + 0.35, -h), float2(w - 0.35, -h)));
                    d = min(d, SegmentSDF(p, float2(-0.65, 0.0), float2(0.65, 0.0)));
                    // 上环两侧
                    d = min(d, SegmentSDF(p, float2(-w, 0.30), float2(-w, h - 0.35)));
                    d = min(d, SegmentSDF(p, float2(w, 0.30), float2(w, h - 0.35)));
                    // 下环两侧
                    d = min(d, SegmentSDF(p, float2(-w, -h + 0.35), float2(-w, -0.30)));
                    d = min(d, SegmentSDF(p, float2(w, -h + 0.35), float2(w, -0.30)));
                }
                else if (digit == 9)
                {
                    // 9: 严谨航空 9，左下角彻底敞开 (绝无底横)，上环饱满闭合，杜绝与 8 混淆！
                    d = min(d, SegmentSDF(p, float2(-w + 0.40, h), float2(w - 0.40, h)));
                    d = min(d, SegmentSDF(p, float2(-w, h - 0.40), float2(-w + 0.40, h)));
                    d = min(d, SegmentSDF(p, float2(-w, 0.0), float2(-w, h - 0.40)));
                    d = min(d, SegmentSDF(p, float2(-w, 0.0), float2(w, 0.0)));
                    d = min(d, SegmentSDF(p, float2(w - 0.40, h), float2(w, h - 0.40)));
                    d = min(d, SegmentSDF(p, float2(w, h - 0.40), float2(w, -h + 0.45)));
                    d = min(d, SegmentSDF(p, float2(w, -h + 0.45), float2(w - 0.40, -h)));
                    d = min(d, SegmentSDF(p, float2(w - 0.40, -h), float2(-0.40, -h)));
                }
                else if (digit == 10)
                {
                    // 航电小写字母 'h' (用于赤经 24h 时角标识)
                    d = min(d, SegmentSDF(p, float2(-w + 0.25, -h), float2(-w + 0.25, h)));
                    d = min(d, SegmentSDF(p, float2(-w + 0.25, 0.40), float2(0.40, 0.40)));
                    d = min(d, SegmentSDF(p, float2(0.40, 0.40), float2(w, -0.10)));
                    d = min(d, SegmentSDF(p, float2(w, -0.10), float2(w, -h)));
                }
                else if (digit == 11)
                {
                    // 航电大写字母 'N' (正北基准方位标)
                    d = min(d, SegmentSDF(p, float2(-w, -h), float2(-w, h)));
                    d = min(d, SegmentSDF(p, float2(-w, h), float2(w, -h)));
                    d = min(d, SegmentSDF(p, float2(w, -h), float2(w, h)));
                }
                else if (digit == 12)
                {
                    // 罗马数字 'I' (主天体靶盘 Primary Body)
                    d = min(d, SegmentSDF(p, float2(0.0, -h + 0.15), float2(0.0, h - 0.15)));
                    d = min(d, SegmentSDF(p, float2(-0.65, h - 0.15), float2(0.65, h - 0.15)));
                    d = min(d, SegmentSDF(p, float2(-0.65, -h + 0.15), float2(0.65, -h + 0.15)));
                }
                else if (digit == 13)
                {
                    // 罗马数字 'II' (次天体靶盘 Secondary Body)
                    d = min(d, SegmentSDF(p, float2(-0.55, -h + 0.15), float2(-0.55, h - 0.15)));
                    d = min(d, SegmentSDF(p, float2(0.55, -h + 0.15), float2(0.55, h - 0.15)));
                    d = min(d, SegmentSDF(p, float2(-0.95, h - 0.15), float2(0.95, h - 0.15)));
                    d = min(d, SegmentSDF(p, float2(-0.95, -h + 0.15), float2(0.95, -h + 0.15)));
                }
                else if (digit == 14)
                {
                    // 天球春分点黄金符号 ♈ (Vernal Equinox Aries Sign)
                    d = min(d, SegmentSDF(p, float2(0.0, -h), float2(0.0, 0.45)));
                    d = min(d, SegmentSDF(p, float2(0.0, 0.45), float2(-0.75, h - 0.35)));
                    d = min(d, SegmentSDF(p, float2(-0.75, h - 0.35), float2(-1.15, h - 0.85)));
                    d = min(d, SegmentSDF(p, float2(0.0, 0.45), float2(0.75, h - 0.35)));
                    d = min(d, SegmentSDF(p, float2(0.75, h - 0.35), float2(1.15, h - 0.85)));
                }

                return d - r;
            }

            float3 RotateByQuaternion(float3 v, float4 q)
            {
                return v + 2.0 * cross(q.xyz, cross(q.xyz, v) + q.w * v);
            }

            float MarkerClearance(float2 screenPoint, float4 marker)
            {
                if (marker.w < 0.01) return 1.0;
                float markerDistance = length(screenPoint - marker.xy);
                float clearAtRadius = smoothstep(marker.z * 0.62, marker.z * 1.08, markerDistance);
                return lerp(1.0, clearAtRadius, saturate(marker.w));
            }

            float DistanceToSegment2D(float2 p, float2 a, float2 b)
            {
                float2 pa = p - a;
                float2 ba = b - a;
                float h = saturate(dot(pa, ba) / max(0.00001, dot(ba, ba)));
                return length(pa - ba * h);
            }

            // 多参考系赤道特征腰带色彩映射函数 (Principia / Stock 多参考系规范)
            fixed4 GetBeltColorForPattern(float pat)
            {
                // Mode 1: INERTIAL - 经典天球赤道天青带 (Celestial Equator Horizon Blue: Principia 规范)
                if (pat > 0.5 && pat < 1.5)
                {
                    return fixed4(0.24, 0.44, 0.72, 0.88);
                }
                // Mode 2: LAGRANGE - 雅可比势能深紫罗兰带 (Lagrangian Violet)
                else if (pat > 1.5 && pat < 2.5)
                {
                    return fixed4(0.32, 0.16, 0.48, 0.88);
                }
                // Mode 3: TARGET - 进近雷达走廊暗铅灰带 (Docking Corridor Slate)
                else if (pat > 2.5 && pat < 3.5)
                {
                    return fixed4(0.14, 0.18, 0.24, 0.85);
                }
                // Mode 4: ORBIT - 轨道面钛灰深石墨流光带 (Orbital Titanium Slate: Principia navball_body_direction 规范)
                else if (pat > 3.5 && pat < 4.5)
                {
                    return fixed4(0.24, 0.26, 0.30, 0.90);
                }
                // Mode 5: BODY_FIXED - 大地赤道地平蓝带 (Geographic Horizon Blue: Principia navball_surface 规范)
                else if (pat > 4.5)
                {
                    return fixed4(0.40, 0.54, 0.80, 0.88);
                }
                // Mode 0: SURFACE - 经典航空深邃海天分界带 (Aero Horizon Ribbon)
                else
                {
                    return fixed4(0.16, 0.36, 0.65, 0.85);
                }
            }

            // ── 高保真物理保底线宽与亚像素边缘抗锯齿 (Resolution-Invariant Sharp Line Filter) ──
            // 保持线条在任意缩放/小尺寸下的实体感 (零发虚半透，坚挺不透明底限 0.90)，紧凑锐利抗锯齿
            float EvalConservativeLine(float dist, float nominalHalfWidth, float pixelDeriv)
            {
                float pixelRadius = max(pixelDeriv * 0.5, 0.0001);
                // 物理保底：屏幕上单边半宽永不低于 0.85 像素 (整宽约 1.7 像素)，杜绝断线与走样
                float minHalfWidth = pixelRadius * 0.85;
                float effectiveHalfWidth = max(nominalHalfWidth, minHalfWidth);

                // 紧凑锐利抗锯齿羽化带 (0.60 像素)，杜绝宽羽化带来的灰暗发软
                float aa = pixelRadius * 0.60;
                float edge = 1.0 - smoothstep(effectiveHalfWidth - aa, effectiveHalfWidth + aa, dist);

                // 实体不透明度保底：线条即使在极端低分辨率/小尺寸下，也坚挺保持可读性 (底限 0.92)
                float widthRatio = saturate(nominalHalfWidth / effectiveHalfWidth);
                float alphaFloor = lerp(0.92, 1.0, widthRatio);
                return edge * alphaFloor;
            }

            // SDF 字符笔画高反差超锐利边缘 (实心高亮纯白 + 独立深黑描边，零半透衰减)
            void EvalConservativeSDF(float dist, float outlineThick, float pixelDeriv, out float fill, out float outline)
            {
                float pixelRadius = max(pixelDeriv * 0.5, 0.0001);
                // 紧凑锐利边缘羽化 (0.55 像素)
                float aa = max(pixelRadius * 0.55, 0.0001);

                // 实心高亮纯白字芯 (100% 不透明实心，彻底移除 energyAlpha 灰暗衰减)
                fill = 1.0 - smoothstep(-aa, aa, dist);

                // 独立高反差外扩描边
                outline = 1.0 - smoothstep(-aa, aa, dist - outlineThick);
            }

            // 核心程序化曲面求值函数 (共享于实时备用通道与离屏烘焙通道)
            fixed4 EvaluateNavballSurface(float3 p, float pitchDeg, float headDeg, float absPitch, float absY, float NdotV, float markerClearance, float signH, float lateralDeg)
            {
                // 1. 天空与地面平滑梯度 (Aero Horizon Gradient)
                fixed4 col;
                if (p.y >= 0.0)
                {
                    // 瑞利散射天穹梯度：地平线通透亮空蓝向天顶深邃蓝非线性自然过渡
                    float rayleigh = pow(saturate(p.y), 0.72);
                    col = lerp(_SkyHorizonColor, _SkyZenithColor, rayleigh);
                }
                else
                {
                    // 大地深邃重力沉降与等高微网格
                    float nadirGrad = pow(saturate(-p.y), 0.85);
                    col = lerp(_GroundHorizonColor, _GroundNadirColor, nadirGrad);

                    // 赤道地面侧高反差切光暗带 (-0.048 < p.y < 0)，极大提升白色地平线边缘对比度，天地一刀分明
                    float horizonTrench = smoothstep(-0.048, -0.001, p.y) * 0.36;
                    col.rgb = lerp(col.rgb, _GroundNadirColor.rgb * 0.40, horizonTrench);

                    // 地面深邃重力下沉晕
                    float nadirDepth = pow(-p.y, 1.45) * 0.22;
                    col.rgb = lerp(col.rgb, _GroundNadirColor.rgb * 0.60, nadirDepth);

                    // 地面微细等高线/板块微纹理
                    float groundRings = abs(frac(p.y * 12.0) - 0.5);
                    float ringAA = clamp(fwidth(p.y) * 1.5, 0.0005, 0.25);
                    float groundGrid = (1.0 - smoothstep(0.04, 0.04 + ringAA, groundRings)) * 0.10 * smoothstep(0.1, 0.5, _DetailScale);
                    col.rgb = lerp(col.rgb, _GroundHorizonColor.rgb, groundGrid);

                    // GPWS / 近地大下沉率防撞动态斑马纹 (Ground Terrain Hazard Pull-Up Stripes: 动态沿重力滚动光栅)
                    if (_GroundHazardAlert > 0.01)
                    {
                        float rollSpeed = _Time.y * (6.0 + _GroundHazardAlert * 8.0);
                        float stripe = sin((p.x * 16.0 + p.y * 24.0 + p.z * 16.0) + rollSpeed);
                        float stripeAA = clamp(fwidth(stripe) * 0.75, 0.001, 0.50);
                        float isStripe = smoothstep(-stripeAA, stripeAA, stripe - 0.10) * _GroundHazardAlert;
                        fixed3 hazardCol = fixed3(1.0, 0.78, 0.08); // 琥珀黄警戒色
                        col.rgb = lerp(col.rgb, hazardCol, isStripe * 0.65);
                    }
                }

                // 参考系专属微视觉签名与动态流光 (Mode-Specific Signatures & Flow)
                float frameDetail = 0.0;
                
                // Mode 0: SURFACE (地表飞行系) - 跑道水平翼与航空等高微环
                if (_FramePattern < 0.5)
                {
                    // 航向 0°, 90°, 180°, 270° 正向水平对准导引羽翼 (Runway Datum Wings)
                    float cardMod90 = abs(fmod(headDeg + 45.0, 90.0) - 45.0);
                    if (cardMod90 < 5.0 && absPitch < 1.6)
                    {
                        float wingAA = clamp(fwidth(absPitch) * 0.75, 0.005, 1.5);
                        float wingMask = (1.0 - smoothstep(0.28 - wingAA, 0.28 + wingAA, absPitch)) *
                                         smoothstep(1.5, 4.5, cardMod90);
                        frameDetail += wingMask * 0.75;
                    }
                    // 大地等高微环线 (-15°, -30°, -45°)
                    if (p.y < 0.0)
                    {
                        float srfContour = abs(frac((absPitch + 7.5) / 15.0 + 0.5) - 0.5);
                        float contourAA = clamp(fwidth(srfContour) * 0.75, 0.001, 0.25);
                        frameDetail += (1.0 - smoothstep(0.015 - contourAA, 0.015 + contourAA, srfContour)) * 0.22;
                    }
                }
                // Mode 1: INERTIAL (天球惯性系) - 程序化恒星微闪烁与 15°(1h) 赤经时角微刻度
                else if (_FramePattern > 0.5 && _FramePattern < 1.5)
                {
                    // 伪随机天球微星点阵 (Procedural Celestial Starfield with Twinkle)
                    float3 starCell = floor(p * 32.0);
                    float starRand = frac(sin(dot(starCell, float3(12.9898, 78.233, 45.164))) * 43758.5453);
                    if (starRand > 0.940)
                    {
                        float3 starPos = (starCell + 0.5) / 32.0;
                        float starDist = length(p - starPos);
                        float twinkle = 0.65 + 0.35 * sin(_Time.y * 3.8 + starRand * 6.28);
                        float starIntensity = (1.0 - smoothstep(0.002, 0.011, starDist)) * twinkle;
                        frameDetail += starIntensity * 0.90;
                    }
                    // 天球赤道 15° (1h) 赤经时角微刻度
                    if (absPitch < 2.2)
                    {
                        float raMod15 = abs(fmod(headDeg + 7.5, 15.0) - 7.5);
                        float raTick = (1.0 - smoothstep(0.25, 0.35, raMod15)) * (1.0 - smoothstep(1.0, 2.0, absPitch));
                        frameDetail += raTick * 0.45;
                    }
                    // 0h 春分点黄金菱标 (Vernal Equinox)
                    if ((headDeg < 2.5 || headDeg > 357.5) && absPitch < 2.5)
                    {
                        float equinoxDist = abs(headDeg > 180.0 ? headDeg - 360.0 : headDeg) + absPitch;
                        float isEquinox = (1.0 - smoothstep(1.2, 1.8, equinoxDist)) * smoothstep(0.4, 0.8, equinoxDist);
                        frameDetail += isEquinox * 0.85;
                    }
                }
                // Mode 2: LAGRANGE (拉格朗日系) - 雅可比双曲势能等势线与 L1~L5 驻标、主/次天体靶盘
                else if (_FramePattern > 1.5 && _FramePattern < 2.5)
                {
                    // 雅可比势能双曲鞍点等势线 (Jacobi Equipotential Contours)
                    float saddle = (p.x * p.x - p.z * p.z) * 3.2;
                    float saddleMod = abs(frac(saddle) - 0.5);
                    float isSaddle = (1.0 - smoothstep(0.02, 0.085, saddleMod)) * 0.32;
                    frameDetail += isSaddle;

                    // L1~L5 关键引力鞍点微型空心菱标
                    float lagDist = abs(fmod(headDeg + 30.0, 60.0) - 30.0) + absPitch;
                    float isLagAnchor = (1.0 - smoothstep(1.5, 2.2, lagDist)) * smoothstep(0.6, 1.1, lagDist);
                    frameDetail += isLagAnchor * 0.55;

                    // Principia 科学基准：0° (Secondary II) 与 180° (Primary I) 实体天体靶盘底衬
                    if (absPitch < 5.2)
                    {
                        float d0 = abs(headDeg > 180.0 ? headDeg - 360.0 : headDeg);
                        float d180 = abs(headDeg - 180.0);
                        float diskDist = min(sqrt(d0 * d0 + pitchDeg * pitchDeg), sqrt(d180 * d180 + pitchDeg * pitchDeg));
                        float diskAA = clamp(fwidth(diskDist) * 0.75, 0.005, 1.2);
                        float isDiskCore = 1.0 - smoothstep(4.0 - diskAA, 4.0 + diskAA, diskDist);
                        float isDiskRing = (1.0 - smoothstep(0.40 - diskAA, 0.40 + diskAA, abs(diskDist - 4.0))) * 0.85;
                        col.rgb = lerp(col.rgb, fixed3(0.08, 0.05, 0.15), isDiskCore * 0.75);
                        frameDetail += isDiskRing;
                    }
                }
                // Mode 3: TARGET (目标相对交会系) - 同心雷达距标环、脉冲扫描波与正交对接引导轴
                else if (_FramePattern > 2.5 && _FramePattern < 3.5)
                {
                    float targetAngle = acos(clamp(p.z, -1.0, 1.0)) * 57.2957795;
                    float ringMod15 = abs(fmod(targetAngle, 15.0) - 7.5);
                    float isRangeRing = (1.0 - smoothstep(0.18, 0.75, ringMod15)) * 0.32;

                    // 主动探测雷达脉冲扫描波 (Radar Pulse Wave: 沿前向扩散)
                    float radarPing = frac(targetAngle / 50.0 - _Time.y * 0.75);
                    float isRadarWave = (1.0 - smoothstep(0.0, 0.15, abs(radarPing - 0.5))) * 0.45 * smoothstep(0.0, 0.25, p.z);
                    frameDetail += isRangeRing + isRadarWave;

                    // 正交十字激光对接瞄准轴线
                    float crossX = 1.0 - smoothstep(0.003, 0.012, abs(p.x));
                    float crossY = 1.0 - smoothstep(0.003, 0.012, abs(p.y));
                    frameDetail += max(crossX, crossY) * 0.40 * smoothstep(0.15, 0.85, p.z);

                    // 退行背向半球警戒斜纹条纹 (p.z < -0.15)
                    if (p.z < -0.15)
                    {
                        float rearStripe = frac((p.x + p.y) * 8.0);
                        float rearAA = clamp(fwidth(rearStripe) * 0.75, 0.001, 0.35);
                        float isRearWarning = (1.0 - smoothstep(0.40 - rearAA, 0.40 + rearAA, abs(rearStripe - 0.5))) * 0.25 * smoothstep(-0.15, -0.65, p.z);
                        frameDetail += isRearWarning;
                    }
                }
                // Mode 4: ORBIT (开普勒轨道系) - 轨道面双导轨、顺行前向微箭头恒速流光与引力线
                else if (_FramePattern > 3.5 && _FramePattern < 4.5)
                {
                    // 顺行前向流动微箭头 (Chevrons flowing in prograde direction: > > >)
                    float flowPhase = frac(headDeg / 15.0 - _Time.y * 1.20);
                    float chevronShape = abs(flowPhase - 0.5) * 2.2 + abs(pitchDeg) * 0.45;
                    float isChevron = (1.0 - smoothstep(0.16, 0.38, chevronShape)) * (1.0 - smoothstep(1.5, 3.2, absPitch)) * 0.85;
                    frameDetail += isChevron;

                    // 轨道面双导轨 (Dual Orbital Rails) at pitch ±1.4°
                    float railAA = clamp(fwidth(absPitch) * 0.75, 0.005, 1.5);
                    float isRail = (1.0 - smoothstep(0.22 - railAA, 0.22 + railAA, abs(absPitch - 1.4))) * 0.65;
                    frameDetail += isRail;

                    // 离心 (上半球 Radial Out) 与向心 (下半球 Radial In) 导轨引力线
                    float radLines = abs(frac(headDeg / 30.0 + 0.5) - 0.5);
                    float radLineAA = clamp(fwidth(radLines) * 0.75, 0.001, 0.25);
                    frameDetail += (1.0 - smoothstep(0.015 - radLineAA, 0.015 + radLineAA, radLines)) * 0.22;
                }
                // Mode 5: BODY-FIXED (星体体固系) - 15° 大地经纬十字准星、0° 本初子午线平行双轨与基准脉冲
                else if (_FramePattern > 4.5)
                {
                    // 0° 本初子午线平行双轨特显 (Prime Meridian Dual Track) + 微弱自转基准脉冲
                    float primeDist = abs(headDeg > 180.0 ? headDeg - 360.0 : headDeg);
                    if (primeDist < 2.0)
                    {
                        float isDualTrack = (1.0 - smoothstep(0.10, 0.32, abs(primeDist - 0.85))) * 0.80;
                        float primePulse = 0.80 + 0.20 * sin(_Time.y * 2.5);
                        frameDetail += isDualTrack * primePulse;
                    }
                    // 15° 经纬度网格交点微型大地测量十字标校准星 (+) (限制在 62° 以下，采用球面弧长防高纬极区畸变)
                    if (absPitch > 6.0 && absPitch < 62.0)
                    {
                        float latMod15 = abs(fmod(absPitch, 15.0));
                        float lonDeg15 = abs(fmod(headDeg + 7.5, 15.0) - 7.5);
                        float lonArc15 = lonDeg15 * cos(radians(absPitch));
                        if (latMod15 < 1.4 && lonArc15 < 1.4)
                        {
                            float crossH = (1.0 - smoothstep(0.18, 0.38, lonArc15)) * (1.0 - smoothstep(0.9, 1.3, latMod15));
                            float crossV = (1.0 - smoothstep(0.18, 0.38, latMod15)) * (1.0 - smoothstep(0.9, 1.3, lonArc15));
                            frameDetail += max(crossH, crossV) * 0.55;
                        }
                    }
                }

                frameDetail *= smoothstep(0.05, 0.52, _DetailScale);
                col.rgb = lerp(col.rgb, _HeadingLineColor.rgb, frameDetail * _HeadingLineColor.a);

                // 收敛高饱和度，让多参考系颜色更接近 Principia 的哑光质感。
                float baseLuma = dot(col.rgb, float3(0.299, 0.587, 0.114));
                col.rgb = lerp(float3(baseLuma, baseLuma, baseLuma), col.rgb, 0.84);

                // 2. 复合赤道地平线系统 (Equatorial Horizon System)
                float eqAA = clamp(fwidth(p.y) * 0.75, 0.0003, 0.06);

                if (_FramePattern < 0.5)
                {
                    // ── Mode 0: SURFACE (地表飞行系) ──
                    // 经典航空地平仪规范：天地一刀分明，零泥泞杂带阻隔，直观判断平飞姿态
                    // 极细锐利纯白地平线 (带微暗反差衬底，在天穹与地表中线均具最高锐度与可读性)
                    float isCoreEquator = EvalConservativeLine(absY, _EquatorWidth * 1.15, eqAA);
                    float isHaloEquator = (1.0 - smoothstep(_EquatorWidth * 2.5 - eqAA, _EquatorWidth * 2.5 + eqAA, absY)) * 0.45;
                    col.rgb = lerp(col.rgb, fixed3(0.01, 0.02, 0.03), isHaloEquator);
                    col = lerp(col, _EquatorColor, saturate(isCoreEquator * _EquatorColor.a));
                }
                else
                {
                    // ── 太空多参考系腰带系统 (ORBIT, INERTIAL, LAGRANGE, etc.) ──
                    float beltPitchAA = clamp(fwidth(absPitch) * 0.75, 0.005, 2.5);
                    float beltHalfWidth = 4.8;
                    float inBelt = 1.0 - smoothstep(beltHalfWidth - beltPitchAA, beltHalfWidth + beltPitchAA, absPitch);

                    if (inBelt > 0.001)
                    {
                        fixed4 beltCol;
                        if (_FrameTransitionProgress < 0.999)
                        {
                            fixed4 colOld = GetBeltColorForPattern(_FramePatternOld);
                            fixed4 colNew = GetBeltColorForPattern(_FramePattern);
                            beltCol = lerp(colOld, colNew, _FrameTransitionProgress);
                        }
                        else
                        {
                            beltCol = GetBeltColorForPattern(_FramePattern);
                        }

                        // 腰带上下边界高反差嵌边微线 (Belt Edge Rims at ±4.8°)
                        float edgeDist = abs(absPitch - beltHalfWidth);
                        float isBeltRim = EvalConservativeLine(edgeDist, 0.20, beltPitchAA) * 0.60;

                        // 中心赤道基准白线 (Core Equator Line at 0°)
                        float isCoreEquator = EvalConservativeLine(absY, _EquatorWidth * 1.10, eqAA);
                        float isHaloEquator = (1.0 - smoothstep(_EquatorWidth * 2.5 - eqAA, _EquatorWidth * 2.5 + eqAA, absY)) * 0.35;
                        float combinedEquator = max(isCoreEquator, isHaloEquator);

                        col.rgb = lerp(col.rgb, beltCol.rgb, inBelt * beltCol.a);
                        col = lerp(col, _EquatorColor, saturate(max(combinedEquator, isBeltRim) * _EquatorColor.a));
                    }
                    else
                    {
                        // 赤道线外晕微弱扩展
                        float isCoreEquator = EvalConservativeLine(absY, _EquatorWidth * 1.10, eqAA);
                        float isHaloEquator = (1.0 - smoothstep(_EquatorWidth * 2.5 - eqAA, _EquatorWidth * 2.5 + eqAA, absY)) * 0.35;
                        col = lerp(col, _EquatorColor, saturate(max(isCoreEquator, isHaloEquator) * _EquatorColor.a));
                    }
                }

                // 坐标系切变光学微光扫描波 (Coordinate Frame Alignment Sweep Ripple)
                if (_FrameTransitionProgress < 0.995)
                {
                    float sweepNorm = _FrameTransitionProgress;
                    float dSweep = abs(absPitch / 90.0 - sweepNorm);
                    float isSweepRing = (1.0 - smoothstep(0.0, 0.09, dSweep)) * (1.0 - sweepNorm);
                    fixed3 sweepCol = GetBeltColorForPattern(_FramePattern).rgb;
                    col.rgb += sweepCol * isSweepRing * 0.45;
                }

                // 极点渐隐防聚集保护 (Polar Ring-Bunching Protection)
                // 75° 梯级中心位于 75.0°，渐隐区调整至 78.0°~83.0°，确保 75° 梯级 100% 清晰呈现，且在 85° 极标前干净隐退
                float polarLadderFade = 1.0 - smoothstep(78.0, 83.0, absPitch);

                float pitchGlyphDistance = 100.0;
                float pitchGlyphEnabled = 0.0;
                float pitchLabelGap = 0.0;
                float headingGlyphDistance = 100.0;
                float headingTextEnabled = 0.0;
                float headingGap = 0.0;
                float headingGlyphAA = 0.45;
                float glyphAA = 0.45;
                bool isEqHeadingZone = false;
                float combinedRulerAndLadder = 0.0;

                float pAA = clamp(fwidth(pitchDeg) * 0.75, 0.005, 2.5);
                float cosP = (_ApertureShape >= 0.5) ? 1.0 : max(cos(radians(absPitch)), 0.06);

                // 动态自适应屏幕分辨率与字模缩放 (低分辨率下等比放大字形并扩充包络，确保小尺寸/远视距下清晰易读)
                float degPerPix = max(fwidth(pitchDeg), 0.0004);
                float fontScale = clamp(0.95 + smoothstep(0.28, 1.10, degPerPix) * 0.55, 0.95, 1.50);

                if (_ApertureShape >= 0.5 && _PfdMode >= 0.5)
                {
                    // ══════════════════════════════════════════════════════════════════════════
                    // 现代矩形平直 PFD / ADI 航电度规系统 (True Aviation PFD Orthonormal Grid)
                    // 俯仰阶梯恒定居中、水平平直横杠、严格线性等距；航向解耦消解，纯净天地二分地平线
                    // ══════════════════════════════════════════════════════════════════════════
                    float absLat = abs(lateralDeg);
                    float latAA = clamp(fwidth(absLat) * 0.75, 0.005, 2.5);

                    // 1. 居中俯仰阶梯系统 (Centered Pitch Ladder)
                    float pLevel10 = round(absPitch / 10.0) * 10.0;
                    float pCenter10 = (pitchDeg < 0.0 ? -pLevel10 : pLevel10);
                    float pOffset10 = pitchDeg - pCenter10;
                    float absOffset10 = abs(pOffset10);

                    // 10° 主阶梯横杠 (半宽 ±7.2°，中心留有 ±3.2° 准星瞄准窗口)
                    float rungHalfW = 7.2;
                    float isMajorRung = 0.0;
                    float isTip = 0.0;

                    if (pLevel10 >= 8.0 && pLevel10 <= 82.0 && absOffset10 < 2.5)
                    {
                        isMajorRung = EvalConservativeLine(absOffset10, 0.28, pAA) *
                                      (1.0 - smoothstep(rungHalfW - latAA, rungHalfW + latAA, absLat)) *
                                      smoothstep(3.0 - latAA, 3.6 + latAA, absLat);

                        // 阶梯端部垂标小折角 (向下折角指向地平线；负半球向上折角指向地平线)
                        if (absLat >= (rungHalfW - 0.9) && absLat < (rungHalfW + 0.1))
                        {
                            float tipHMask = smoothstep(rungHalfW - 0.9 - latAA, rungHalfW - 0.9 + latAA, absLat) *
                                             (1.0 - smoothstep(rungHalfW - latAA, rungHalfW + latAA, absLat));
                            if (pitchDeg > 0.0)
                            {
                                float tipVMask = (1.0 - smoothstep(0.0 - pAA, 0.0 + pAA, pOffset10)) *
                                                 smoothstep(-1.5 - pAA, -1.5 + pAA, pOffset10);
                                isTip = tipHMask * tipVMask;
                            }
                            else
                            {
                                float tipVMask = smoothstep(0.0 - pAA, 0.0 + pAA, pOffset10) *
                                                 (1.0 - smoothstep(1.5 - pAA, 1.5 + pAA, pOffset10));
                                isTip = tipHMask * tipVMask;
                            }
                        }

                        // 负半球地平俯仰梯级采用标准航空虚线
                        if (pitchDeg < 0.0)
                        {
                            float dashVal = fmod(absLat - 3.2, 1.8);
                            float dashAA = clamp(fwidth(dashVal) * 0.75, 0.001, 0.50);
                            isMajorRung *= (1.0 - smoothstep(0.95 - dashAA, 0.95 + dashAA, dashVal));
                        }
                    }

                    // 5° 细分短杠 (半宽 ±3.5°，更短更细，无折角)
                    float isMinor5 = 0.0;
                    float pLevel5 = round(absPitch / 5.0) * 5.0;
                    bool isPure5 = (fmod(pLevel5, 10.0) > 2.0);
                    if (isPure5 && absPitch >= 3.0 && absPitch <= 82.0)
                    {
                        float pMod5 = abs(pitchDeg - (pitchDeg < 0.0 ? -pLevel5 : pLevel5));
                        if (pMod5 < 1.5)
                        {
                            isMinor5 = EvalConservativeLine(pMod5, 0.20, pAA) *
                                       (1.0 - smoothstep(3.2 - latAA, 3.8 + latAA, absLat)) *
                                       smoothstep(1.4 - latAA, 2.0 + latAA, absLat) * 0.75;
                        }
                    }

                    combinedRulerAndLadder = max(max(isMajorRung, isTip), isMinor5);

                    // 2. 俯仰梯级数字 (在阶梯左右两侧 absLat ~ 9.5° 呈现，字体大小恒定且自适应分辨率)
                    if (pLevel10 >= 8.0 && pLevel10 <= 82.0)
                    {
                        float signLat = lateralDeg >= 0.0 ? 1.0 : -1.0;
                        float latCenter = signLat * (9.5 + (fontScale - 1.0) * 1.5);
                        float latNumOffset = lateralDeg - latCenter;
                        if (abs(latNumOffset) < (4.6 * fontScale) && absOffset10 < (4.0 * fontScale))
                        {
                            float tens = floor(pLevel10 / 10.0);
                            float ones = fmod(pLevel10, 10.0);
                            float2 numPos = float2(latNumOffset, pOffset10) / fontScale;

                            float dDigit = min(
                                DigitDistance(numPos + float2(1.70, 0.0), tens),
                                DigitDistance(numPos - float2(1.70, 0.0), ones));

                            if (pitchDeg < 0.0)
                            {
                                float signDist = SegmentDistance(numPos, float2(-3.65, 0.0), float2(0.55, 0.12));
                                dDigit = min(dDigit, signDist);
                            }

                            pitchGlyphDistance = dDigit * fontScale;
                            pitchGlyphEnabled = polarLadderFade * markerClearance;
                            pitchLabelGap = (dDigit < 2.5) ? pitchGlyphEnabled : 0.0;
                        }
                    }
                    glyphAA = clamp(fwidth(pitchDeg) * 0.75, 0.005, 2.5);
                }
                else
                {
                    // ══════════════════════════════════════════════════════════════════════════
                    // 经典圆形 3D 导航球骨架与经纬网 (Classic Circular Navball System)
                    // ══════════════════════════════════════════════════════════════════════════
                    // 3. 几何坐标与度规系统 (45° 八向主经线骨架 + 22.5° 交错俯仰通道: 对标 Principia 规范)
                    // 3.1 主经线系统 (0°, 45°, 90°, 135°, 180°, 225°, 270°, 315°)
                    float mainHeadCenter = floor(headDeg / 45.0 + 0.5) * 45.0;
                    float mainHeadOffset = headDeg - mainHeadCenter;
                    if (mainHeadOffset > 180.0) mainHeadOffset -= 360.0;
                    if (mainHeadOffset < -180.0) mainHeadOffset += 360.0;
                    float normMainH = fmod(mainHeadCenter + 360.0, 360.0);

                    float mainHeadArc = mainHeadOffset * cosP;
                    float absMainArc = abs(mainHeadArc);

                    // 3.2 俯仰数字交错通道 (22.5°, 67.5°, 112.5°, 157.5°, 202.5°, 247.5°, 292.5°, 337.5°)
                    float pitchColCenter = floor((headDeg - 22.5) / 45.0 + 0.5) * 45.0 + 22.5;
                    float pitchColOffset = headDeg - pitchColCenter;
                    if (pitchColOffset > 180.0) pitchColOffset -= 360.0;
                    if (pitchColOffset < -180.0) pitchColOffset += 360.0;
                    float normColCenter = fmod(pitchColCenter + 360.0, 360.0);

                    float pitchLabelLevel = round(absPitch / 15.0) * 15.0;
                    float pitchLabelCenter = (pitchDeg < 0.0 ? -pitchLabelLevel : pitchLabelLevel);
                    float pitchLabelOffset = pitchDeg - pitchLabelCenter;
                    float absLabelOffset = abs(pitchLabelOffset);

                    float cosPitchLevel = max(cos(radians(pitchLabelLevel)), 0.08);
                    float pitchColArc = pitchColOffset * cosPitchLevel;
                    float absColArc = abs(pitchColArc);

                    // 动态字形滚转正向对齐与切向反畸变展开
                    float tangentAspect = lerp(1.0, clamp(1.0 / max(NdotV, 0.58), 1.0, 1.28), _NumeralTangentComp);
                    float numRoll = -_NumeralRollAngle * _NumeralUprightMode;
                    float cosNR = cos(numRoll);
                    float sinNR = sin(numRoll);

                    // 高纬扇区自适应疏化：75° 极区仅在 4 个开阔扇区 (22.5°, 112.5°, 202.5°, 292.5°) 居中显示，彻底解决极区重叠
                    bool isPitchColActive = (pitchLabelLevel <= 65.0) || (fmod(round((normColCenter - 22.5) / 45.0), 2.0) < 0.5);

                    // 4. 俯仰数字排版求值 (在 22.5° 交错通道)
                    if (absPitch >= 12.0 && absPitch <= 82.0 && isPitchColActive)
                    {
                        pitchGlyphEnabled = polarLadderFade * smoothstep(0.10, 0.36, NdotV) * markerClearance;

                        if (absColArc < (5.8 * fontScale) && absLabelOffset < (4.2 * fontScale))
                        {
                            float pitchTens = floor(pitchLabelLevel / 10.0);
                            float pitchOnes = fmod(pitchLabelLevel, 10.0);

                            float2 pitchCenterOffset = float2(pitchColArc * signH * tangentAspect, pitchLabelOffset);
                            float2 rotPitchOffset = float2(
                                pitchCenterOffset.x * cosNR - pitchCenterOffset.y * sinNR,
                                pitchCenterOffset.x * sinNR + pitchCenterOffset.y * cosNR
                            ) / fontScale;

                            float pitchRadialSq = (pitchColArc * pitchColArc + pitchLabelOffset * pitchLabelOffset) / (fontScale * fontScale);
                            pitchLabelGap = (pitchRadialSq < 26.0) ? pitchGlyphEnabled : 0.0;

                            pitchGlyphDistance = min(
                                DigitDistance(rotPitchOffset + float2(1.85, 0.0), pitchTens),
                                DigitDistance(rotPitchOffset - float2(1.85, 0.0), pitchOnes)) * fontScale;

                            // 地表系负半球添加航空负号
                            if (_FramePattern < 0.5 && pitchDeg < 0.0)
                            {
                                float pitchSignDistance = SegmentDistance(rotPitchOffset, float2(-3.90, 0.0), float2(0.55, 0.12)) * fontScale;
                                pitchGlyphDistance = min(pitchGlyphDistance, pitchSignDistance);
                            }
                        }
                    }

                // 5. 标尺骨架与纬线圈 (Continuous Ruler Backbone & Latitude Parallels)
                float mAA = clamp(fwidth(absMainArc) * 0.75, 0.005, 2.5);

                // 5.1 主经线连续标尺骨架 (0°, 45°, 90°, 135°, 180°, 225°, 270°, 315°)
                bool isCardinalMeridian = (fmod(normMainH + 1.0, 90.0) < 2.0);
                float minorMeridianFade = 1.0 - smoothstep(58.0, 72.0, absPitch);
                float meridianFade = isCardinalMeridian ? polarLadderFade : minorMeridianFade;
                float isMainMeridianLine = EvalConservativeLine(absMainArc, 0.24, mAA) * 0.65 * meridianFade;

                // 主经线 5° 细分短杠 (±1.6° arc)
                float isTick5 = 0.0;
                if (absMainArc <= 2.2 && absPitch > 2.0 && absPitch < 80.0)
                {
                    float pMod5 = abs(pitchDeg - round(pitchDeg / 5.0) * 5.0);
                    isTick5 = EvalConservativeLine(pMod5, 0.20, pAA) * 
                              (1.0 - smoothstep(1.6 - mAA, 1.6 + mAA, absMainArc)) * 
                              0.55 * smoothstep(0.18, 0.55, _DetailScale) * meridianFade;
                }

                // 主经线 10° 细分中杠 (±2.6° arc)
                float isTick10 = 0.0;
                if (absMainArc <= 3.2 && absPitch > 4.0 && absPitch < 80.0)
                {
                    float pMod10 = abs(pitchDeg - round(pitchDeg / 10.0) * 10.0);
                    float pLevel10 = round(absPitch / 10.0) * 10.0;
                    bool isPure10 = (fmod(pLevel10, 15.0) > 2.0);
                    if (isPure10)
                    {
                        isTick10 = EvalConservativeLine(pMod10, 0.24, pAA) * 
                                   (1.0 - smoothstep(2.6 - mAA, 2.6 + mAA, absMainArc)) * 
                                   0.72 * smoothstep(0.05, 0.35, _DetailScale) * meridianFade;
                    }
                }

                // 主经线 15° 正交大刻度横杠 (±4.2° arc)
                float isCrossbar15 = 0.0;
                if (absMainArc <= 5.0 && pitchLabelLevel >= 12.0 && pitchLabelLevel <= 78.0)
                {
                    float pMod15 = abs(pitchDeg - pitchLabelCenter);
                    isCrossbar15 = EvalConservativeLine(pMod15, 0.28, pAA) * 
                                   (1.0 - smoothstep(4.2 - mAA, 4.2 + mAA, absMainArc)) * 
                                   0.85 * meridianFade;
                }

                // 5.2 次级中间经线 (22.5° Sub-Meridians: 在 54° 以下呈现精细点虚线)
                float colAA = clamp(fwidth(absColArc) * 0.75, 0.005, 2.5);
                float isSubMeridian = EvalConservativeLine(absColArc, 0.18, colAA) * 
                                      (1.0 - smoothstep(46.0, 56.0, absPitch)) * 0.32;
                float dashSub = frac(absPitch / 3.0);
                isSubMeridian *= (1.0 - smoothstep(0.38, 0.62, abs(dashSub - 0.5)));
                isSubMeridian *= (1.0 - pitchLabelGap);

                // 5.3 纬线圈与航空梯级系统
                float combinedParallel = 0.0;
                if (_FramePattern > 0.5)
                {
                    // 太空参考系 (ORBIT, INERTIAL, LAGRANGE, TARGET, BODY_FIXED): 贯通全周的精细纬度圈 (15°, 30°, 45°, 60°, 75°)
                    if (pitchLabelLevel >= 12.0 && pitchLabelLevel <= 78.0)
                    {
                        float pMod15 = abs(pitchDeg - pitchLabelCenter);
                        float isFullParallel = EvalConservativeLine(pMod15, 0.22, pAA) * 0.52 * polarLadderFade;
                        // 扣除俯仰数字与航向数字窗口
                        isFullParallel *= (1.0 - pitchLabelGap);

                        // 矩形平直 PFD 模式：将太空参考系整周大圆弧收拢为标准化航电梯级横杠 (±9.5° 宽度)，
                        // 彻底消除整屏贯穿大圆弧在矩形视口内产生的碗状球弧畸变，保持 100% 视觉平直与现代 PFD 质感
                        if (_ApertureShape >= 0.5)
                        {
                            float rungMaskCol = (1.0 - smoothstep(8.5 - colAA, 9.8 + colAA, absColArc));
                            float rungMaskMain = (1.0 - smoothstep(7.0 - mAA, 8.5 + mAA, absMainArc));
                            isFullParallel *= max(rungMaskCol, rungMaskMain);
                        }
                        combinedParallel = isFullParallel;
                    }
                }
                else
                {
                    // 地表系 (SURFACE): 经典航空 HUD 俯仰梯级 (下折垂尾与虚线负半球)
                    if (absColArc <= 10.5 && pitchLabelLevel >= 12.0 && pitchLabelLevel <= 78.0 && absLabelOffset < 2.5)
                    {
                        float isMajorBar15 = EvalConservativeLine(absLabelOffset, 0.30, pAA) *
                                             smoothstep(4.0 - colAA, 4.0 + colAA, absColArc) *
                                             (1.0 - smoothstep(9.0 - colAA, 9.0 + colAA, absColArc));
                        float isTip15 = 0.0;
                        if (absColArc >= 8.2 && absColArc < 9.2)
                        {
                            float tipHMask = smoothstep(8.3 - colAA, 8.3 + colAA, absColArc) * (1.0 - smoothstep(9.0 - colAA, 9.0 + colAA, absColArc));
                            if (pitchDeg > 0.0)
                            {
                                float tipVMask = (1.0 - smoothstep(0.0 - pAA, 0.0 + pAA, pitchLabelOffset)) * smoothstep(-1.5 - pAA, -1.5 + pAA, pitchLabelOffset);
                                isTip15 = tipHMask * tipVMask;
                            }
                            else
                            {
                                float tipVMask = smoothstep(0.0 - pAA, 0.0 + pAA, pitchLabelOffset) * (1.0 - smoothstep(1.5 - pAA, 1.5 + pAA, pitchLabelOffset));
                                isTip15 = tipHMask * tipVMask;
                            }
                        }
                        combinedParallel = max(isMajorBar15, isTip15) * pitchGlyphEnabled;
                        if (pitchDeg < 0.0)
                        {
                            float dashVal = fmod(absColArc - 4.0, 2.0);
                            float dashAA = clamp(fwidth(dashVal) * 0.75, 0.001, 0.50);
                            combinedParallel *= (1.0 - smoothstep(1.05 - dashAA, 1.05 + dashAA, dashVal));
                        }
                    }
                }

                // 游标微调刻度 (Vernier Scale at pitch < 8°)
                float isTick25 = 0.0;
                if (_VernierScaleDetail > 0.01 && absPitch < 8.0 && absMainArc <= 2.2)
                {
                    float pMod25 = abs(pitchDeg - round(pitchDeg / 2.5) * 2.5);
                    float pLevel25 = round(absPitch / 2.5) * 2.5;
                    bool isPure25 = (fmod(pLevel25, 5.0) > 1.0);
                    if (isPure25 && pMod25 < 0.4)
                    {
                        isTick25 = EvalConservativeLine(pMod25, 0.18, pAA) * (1.0 - smoothstep(1.8 - mAA, 1.8 + mAA, absMainArc)) * 0.65;
                    }
                }

                // 合成经线与标尺刻度
                float rulerTrack = max(isMainMeridianLine, max(isCrossbar15, max(isTick10, isTick5)));
                rulerTrack = max(rulerTrack, max(isSubMeridian, isTick25 * _VernierScaleDetail));
                float combinedRulerAndLadder = max(rulerTrack, combinedParallel);

                // 6. 航向与赤经数字系统 (在赤道 0° 与 ±45° 纬线上双重呈现: 对标 Principia 规范)
                float headingGlyphDistance = 100.0;
                float headingTextEnabled = 0.0;
                float headingGap = 0.0;
                float headingGlyphAA = 0.45;

                // 6.1 赤道航向/时角检测 (pitch ~ 3.8°)
                bool isEqHeadingZone = (absPitch < 8.5 && abs(pitchDeg - 3.8) < (4.5 * fontScale) && absMainArc < (10.0 * fontScale));
                // 6.2 ±45° 纬线航向/时角复现检测 (pitch ~ +45° 或 -45°)
                float h45PitchCenter = (pitchDeg > 0.0 ? 45.0 : -45.0);
                float h45PitchOffset = pitchDeg - h45PitchCenter;
                bool is45HeadingZone = (abs(h45PitchOffset) < (4.5 * fontScale) && absMainArc < (10.0 * fontScale));

                if (isEqHeadingZone || is45HeadingZone)
                {
                    float hYOffset = isEqHeadingZone ? (pitchDeg - 3.8) : h45PitchOffset;
                    float2 headCenterOffset = float2(mainHeadArc * signH * tangentAspect, hYOffset);
                    float2 rotHeadOffset = float2(
                        headCenterOffset.x * cosNR - headCenterOffset.y * sinNR,
                        headCenterOffset.x * sinNR + headCenterOffset.y * cosNR
                    ) / fontScale;

                    float hRadialSq = (mainHeadArc * mainHeadArc + hYOffset * hYOffset) / (fontScale * fontScale);
                    float hFade = isEqHeadingZone ? 
                        ((1.0 - smoothstep(5.5, 7.8, absPitch)) * smoothstep(0.10, 0.38, NdotV) * markerClearance) :
                        (polarLadderFade * smoothstep(0.10, 0.38, NdotV) * markerClearance);

                    headingTextEnabled = hFade;
                    headingGap = (hRadialSq < 30.0) ? hFade : 0.0;

                    if (headingTextEnabled > 0.001)
                    {
                        // Mode 1: INERTIAL - 天球赤经 24 小时制时角 (0, 3, 6, 9, 12, 15, 18, 21)
                        if (_FramePattern > 0.5 && _FramePattern < 1.5)
                        {
                            float raHour = fmod(floor(normMainH / 15.0 + 0.5), 24.0);
                            if (isEqHeadingZone && raHour < 0.5)
                            {
                                // 赤道 0h: 绘制春分点黄金符号 ♈
                                headingGlyphDistance = DigitDistance(rotHeadOffset, 14.0);
                            }
                            else if (raHour < 9.5)
                            {
                                // 单数字 (0, 3, 6, 9) 完美绝对居中排布
                                headingGlyphDistance = DigitDistance(rotHeadOffset, raHour);
                            }
                            else
                            {
                                // 双数字 (12, 15, 18, 21)
                                float raTens = floor(raHour / 10.0);
                                float raOnes = fmod(raHour, 10.0);
                                headingGlyphDistance = min(
                                    DigitDistance(rotHeadOffset + float2(1.85, 0.0), raTens),
                                    DigitDistance(rotHeadOffset - float2(1.85, 0.0), raOnes));
                            }
                        }
                        // Mode 2: LAGRANGE - 0° 为次天体 "II", 180° 为主天体 "I"
                        else if (_FramePattern > 1.5 && _FramePattern < 2.5 && normMainH < 1.0)
                        {
                            headingGlyphDistance = DigitDistance(rotHeadOffset, 13.0); // II
                        }
                        else if (_FramePattern > 1.5 && _FramePattern < 2.5 && abs(normMainH - 180.0) < 1.0)
                        {
                            headingGlyphDistance = DigitDistance(rotHeadOffset, 12.0); // I
                        }
                        // Mode 0 / 5: SURFACE & BODY_FIXED 000° 航向显示航电大写 "N"
                        else if ((_FramePattern < 0.5 || _FramePattern > 4.5) && normMainH < 1.0)
                        {
                            headingGlyphDistance = DigitDistance(rotHeadOffset, 11.0); // N
                        }
                        else if (normMainH < 95.0)
                        {
                            // 两位数航向 (45, 90)
                            float hTens = floor(normMainH / 10.0);
                            float hOnes = fmod(normMainH, 10.0);
                            headingGlyphDistance = min(
                                DigitDistance(rotHeadOffset + float2(1.85, 0.0), hTens),
                                DigitDistance(rotHeadOffset - float2(1.85, 0.0), hOnes));
                        }
                        else
                        {
                            // 三位数航向 (135, 180, 225, 270, 315)
                            float hHundreds = floor(normMainH / 100.0);
                            float hTens = floor(fmod(normMainH, 100.0) / 10.0);
                            float hOnes = fmod(normMainH, 10.0);
                            headingGlyphDistance = min(
                                DigitDistance(rotHeadOffset + float2(3.5, 0.0), hHundreds),
                                min(DigitDistance(rotHeadOffset, hTens),
                                    DigitDistance(rotHeadOffset - float2(3.5, 0.0), hOnes)));
                        }
                        headingGlyphDistance *= fontScale;
                    }
                    headingGlyphAA = clamp(max(fwidth(mainHeadArc * tangentAspect), fwidth(pitchDeg)) * 0.75, 0.005, 2.5);
                }

                // 6.3 赤道航向微刻度线 (Equator Minor Ticks at pitch < 1.8°)
                if (absPitch < 1.8)
                {
                    float eqPitchAA = clamp(fwidth(absPitch) * 0.75, 0.005, 2.0);
                    float eqPitchMask45 = 1.0 - smoothstep(1.7 - eqPitchAA, 1.7 + eqPitchAA, absPitch);
                    float eqTick45 = EvalConservativeLine(absMainArc, 0.32, mAA) * 0.90 * eqPitchMask45;
                    float eqSubMod = abs(pitchColOffset);
                    float eqSubAA = clamp(fwidth(eqSubMod) * 0.75, 0.005, 2.0);
                    float eqTickSub = EvalConservativeLine(eqSubMod, 0.24, eqSubAA) * 0.60 * (1.0 - smoothstep(1.0 - eqPitchAA, 1.0 + eqPitchAA, absPitch));
                    col = lerp(col, _EquatorColor, saturate(max(eqTick45, eqTickSub)));
                }
                glyphAA = clamp(max(fwidth(pitchColArc * tangentAspect), fwidth(pitchLabelOffset)) * 0.75, 0.005, 2.5);
                }

                // 数字区域清空标尺线条，保持高反差整洁性
                combinedRulerAndLadder *= (1.0 - max(pitchLabelGap, headingGap));

                // 高反差微暗衬 (Subtle Dark Backing): 消除标线在浅蓝天穹或明亮地平带上的发虚感
                fixed4 ladderOutlineCol = (_LabelOutlineColor.a > 0.01) ? _LabelOutlineColor : fixed4(0.015, 0.025, 0.04, 0.78);
                float ladderUnderlay = smoothstep(0.01, 0.60, combinedRulerAndLadder) * 0.40;
                col.rgb = lerp(col.rgb, ladderOutlineCol.rgb, ladderUnderlay * ladderOutlineCol.a);
                col = lerp(col, _PitchLadderColor, saturate(combinedRulerAndLadder * _PitchLadderColor.a * 0.95));

                // 7. 0° (Prime) 与 180° (Anti) 醒目全周子午分界线 (Red & Green Dividers: 对标 Principia 规范)
                float primeAngle = abs(headDeg > 180.0 ? headDeg - 360.0 : headDeg);
                float antiAngle  = abs(headDeg - 180.0);

                if ((_ApertureShape < 0.5 || _PfdMode < 0.5) && (primeAngle < 3.0 || antiAngle < 3.0))
                {
                    float primeArc = primeAngle * cosP;
                    float antiArc  = antiAngle * cosP;

                    float splitAA = clamp(fwidth(primeArc) * 0.75, 0.005, 2.0);
                    // 锐利精致的 0.22° 半宽基准线，移除 1.5° 弥散粗晕，保持航电仪表清晰度
                    float isPrime = EvalConservativeLine(primeArc, 0.22, splitAA);
                    float isAnti  = EvalConservativeLine(antiArc, 0.22, splitAA);

                    // 中心视场准星净空保护 (Facing reticle clearance: 当航向为 000° 且俯仰靠近地平时，清空中心 2.8° 视场，防止红线直切瞄准十字准星)
                    float reticleClearance = smoothstep(1.2, 2.8, length(float2(primeArc, absPitch)));
                    isPrime *= reticleClearance;

                    float dividerFade = 1.0 - smoothstep(84.0, 87.5, absPitch);

                    fixed3 primeColor;
                    fixed3 antiColor;

                    // 参考系天文学与航电分色：
                    if (_FramePattern > 3.5 && _FramePattern < 4.5)
                    {
                        primeColor = fixed3(0.08, 0.98, 0.28); // ORBIT: 顺行鲜绿 (Prograde Green)
                        antiColor  = fixed3(0.98, 0.16, 0.16); // ORBIT: 逆行鲜红 (Retrograde Red)
                    }
                    else if (_FramePattern > 0.5 && _FramePattern < 1.5)
                    {
                        primeColor = fixed3(0.96, 0.18, 0.18); // INERTIAL: 春分点红 (Vernal Equinox)
                        antiColor  = fixed3(0.12, 0.90, 0.32); // INERTIAL: 秋分点绿 (Autumnal Equinox)
                    }
                    else if (_FramePattern > 1.5 && _FramePattern < 2.5)
                    {
                        primeColor = fixed3(0.15, 0.92, 0.40); // LAGRANGE: 次天体绿 (Secondary II)
                        antiColor  = fixed3(0.92, 0.20, 0.25); // LAGRANGE: 主天体红 (Primary I)
                    }
                    else if (_FramePattern > 2.5 && _FramePattern < 3.5)
                    {
                        primeColor = fixed3(0.10, 0.96, 0.32); // TARGET: 进近通道绿
                        antiColor  = fixed3(0.96, 0.16, 0.16); // TARGET: 背向撤离红
                    }
                    else
                    {
                        primeColor = fixed3(0.96, 0.18, 0.18); // SURFACE/BODY_FIXED: 真北/本初子午线红
                        antiColor  = fixed3(0.12, 0.90, 0.32); // SURFACE/BODY_FIXED: 真南/国际日界线绿
                    }

                    float dividerMask = (1.0 - max(pitchLabelGap, headingGap)) * dividerFade;
                    col.rgb = lerp(col.rgb, primeColor, saturate(isPrime * dividerMask * 0.95));
                    col.rgb = lerp(col.rgb, antiColor,  saturate(isAnti  * dividerMask * 0.95));
                }


                // 9. 字符描边与填充合成 (俯仰数字 + 航向数字，应用 SDF 笔画物理保底与超锐利边缘)
                float pitchTextFill, pitchTextOutline;
                EvalConservativeSDF(pitchGlyphDistance, 0.42 * fontScale, glyphAA, pitchTextFill, pitchTextOutline);
                pitchTextOutline *= pitchGlyphEnabled;
                pitchTextFill *= pitchGlyphEnabled;

                float headingTextFill, headingTextOutline;
                EvalConservativeSDF(headingGlyphDistance, 0.42 * fontScale, headingGlyphAA, headingTextFill, headingTextOutline);
                headingTextOutline *= headingTextEnabled;
                headingTextFill *= headingTextEnabled;

                float textOutline = max(pitchTextOutline, headingTextOutline);
                float textFill = max(pitchTextFill, headingTextFill);
                fixed4 labelCol = (_LabelColor.a > 0.01) ? _LabelColor : fixed4(0.98, 0.99, 1.0, 1.0);
                if (_FramePattern > 0.5 && _FramePattern < 1.5 && isEqHeadingZone && (headDeg < 2.5 || headDeg > 357.5) && headingTextFill > 0.01)
                {
                    labelCol.rgb = fixed3(1.0, 0.86, 0.30); // 0h 春分点黄金符号
                }
                fixed4 outlineCol = (_LabelOutlineColor.a > 0.01) ? _LabelOutlineColor : fixed4(0.01, 0.015, 0.025, 0.95);
                col.rgb = lerp(col.rgb, outlineCol.rgb, saturate(textOutline * outlineCol.a));
                col.rgb = lerp(col.rgb, labelCol.rgb, saturate(textFill * labelCol.a));

                // 7. 天顶与天底专属高精矢量极标 (Zenith & Nadir Precision Icons)
                if (absY > 0.985)
                {
                    float poleR = sqrt(p.x * p.x + p.z * p.z);
                    float poleAA = clamp(fwidth(poleR) * 0.75, 0.0004, 0.06);

                    // 主十字与对角十字标
                    float armX = (abs(p.x) < 0.0028 && abs(p.z) < 0.082) ? 1.0 : 0.0;
                    float armZ = (abs(p.z) < 0.0028 && abs(p.x) < 0.082) ? 1.0 : 0.0;
                    float diag1 = (abs(p.x - p.z) < 0.0035 && poleR < 0.055) ? 1.0 : 0.0;
                    float diag2 = (abs(p.x + p.z) < 0.0035 && poleR < 0.055) ? 1.0 : 0.0;
                    float octoArm = max(max(armX, armZ), max(diag1, diag2));

                    if (p.y > 0.0)
                    {
                        // 天顶 (+90° Zenith): 八芒星瞄准环与外周同心刻线
                        float haloDist = abs(poleR - 0.044);
                        float innerRing = abs(poleR - 0.022);
                        float zenithHalo = EvalConservativeLine(haloDist, 0.0025, poleAA);
                        float zenithInner = EvalConservativeLine(innerRing, 0.0020, poleAA);
                        float isZenith = max(octoArm * 0.60, max(zenithHalo * 0.85, zenithInner * 0.65));
                        col.rgb = lerp(col.rgb, _PitchLadderColor.rgb, isZenith * _PitchLadderColor.a);
                    }
                    else
                    {
                        // 天底 (-90° Nadir): 重力捕获同心双圆靶盘与向心核心
                        float nadirR1 = abs(poleR - 0.025);
                        float nadirR2 = abs(poleR - 0.050);
                        float ring1 = EvalConservativeLine(nadirR1, 0.0024, poleAA);
                        float ring2 = EvalConservativeLine(nadirR2, 0.0024, poleAA);
                        float nadirCore = 1.0 - smoothstep(0.009 - poleAA, 0.009 + poleAA, poleR);
                        float isNadir = max(max(ring1 * 0.80, ring2 * 0.65), max(nadirCore * 0.95, (armX + armZ) * 0.45));
                        col.rgb = lerp(col.rgb, _EquatorColor.rgb, isNadir * _EquatorColor.a);
                    }
                }

                return col;
            }

            fixed4 SampleProceduralNavball(float2 subCoord)
            {
                if (_ApertureShape < 0.5)
                {
                    float subR2 = dot(subCoord, subCoord);
                    float subZ = sqrt(max(0.0, 1.0 - subR2));
                    float3 subViewRay = float3(subCoord.x, subCoord.y, subZ);
                    float3 subP = RotateByQuaternion(subViewRay, _SphereInvRotation);
                    subP = normalize(subP);

                    float3 procP = subP;
                    float signH = 1.0;

                    // Mode 1: INERTIAL (Principia 地心惯性参考系)
                    if (_FramePattern > 0.5 && _FramePattern < 1.5)
                    {
                        procP.x = -procP.x;
                        signH = -1.0;
                    }

                    float pitchDeg = asin(clamp(procP.y, -1.0, 1.0)) * 57.2957795;
                    float headDeg = atan2(procP.x, procP.z) * 57.2957795;
                    if (headDeg < 0.0) headDeg += 360.0;
                    float absY = abs(procP.y);
                    float absPitch = abs(pitchDeg);
                    float markerClearance = 1.0;
                    if (_MarkerAvoid0.w > 0.01 || _MarkerAvoid1.w > 0.01 || _MarkerAvoid2.w > 0.01 || _MarkerAvoid3.w > 0.01)
                    {
                        float2 screenPoint = subCoord;
                        markerClearance = min(MarkerClearance(screenPoint, _MarkerAvoid0), MarkerClearance(screenPoint, _MarkerAvoid1));
                        markerClearance = min(markerClearance, min(MarkerClearance(screenPoint, _MarkerAvoid2), MarkerClearance(screenPoint, _MarkerAvoid3)));
                    }

                    return EvaluateNavballSurface(procP, pitchDeg, headDeg, absPitch, absY, subZ, markerClearance, signH, 0.0);
                }
                else if (_PfdMode < 0.5)
                {
                    // 3D 矩形姿态球模式 (3D Spherical Navball in Rectangular Frame)
                    float ar = max(_AspectRatio, 0.1);
                    float rSphere = sqrt(ar * ar + 1.0) * 1.02;
                    float u = (subCoord.x * ar) / rSphere;
                    float v = subCoord.y / rSphere;
                    float subR2 = u * u + v * v;
                    float subZ = sqrt(max(0.0, 1.0 - subR2));
                    float3 subViewRay = float3(u, v, subZ);
                    float3 subP = RotateByQuaternion(subViewRay, _SphereInvRotation);
                    subP = normalize(subP);

                    float3 procP = subP;
                    float signH = 1.0;

                    if (_FramePattern > 0.5 && _FramePattern < 1.5)
                    {
                        procP.x = -procP.x;
                        signH = -1.0;
                    }

                    float pitchDeg = asin(clamp(procP.y, -1.0, 1.0)) * 57.2957795;
                    float headDeg = atan2(procP.x, procP.z) * 57.2957795;
                    if (headDeg < 0.0) headDeg += 360.0;
                    float absY = abs(procP.y);
                    float absPitch = abs(pitchDeg);
                    float markerClearance = 1.0;
                    if (_MarkerAvoid0.w > 0.01 || _MarkerAvoid1.w > 0.01 || _MarkerAvoid2.w > 0.01 || _MarkerAvoid3.w > 0.01)
                    {
                        float2 screenPoint = subCoord;
                        markerClearance = min(MarkerClearance(screenPoint, _MarkerAvoid0), MarkerClearance(screenPoint, _MarkerAvoid1));
                        markerClearance = min(markerClearance, min(MarkerClearance(screenPoint, _MarkerAvoid2), MarkerClearance(screenPoint, _MarkerAvoid3)));
                    }

                    return EvaluateNavballSurface(procP, pitchDeg, headDeg, absPitch, absY, subZ, markerClearance, signH, 0.0);
                }
                else
                {
                    // 降维民航 PFD 模式 (2-DOF Pitch & Roll: 航向解耦，平直度规)
                    float ar = max(_AspectRatio, 0.1);
                    float fovMul = clamp(_FovScale, 0.3, 2.5);
                    float degPerUnit = 27.5 * fovMul;

                    float3 fwdRef = RotateByQuaternion(float3(0.0, 0.0, 1.0), _SphereInvRotation);
                    float3 upRef  = RotateByQuaternion(float3(0.0, 1.0, 0.0), _SphereInvRotation);
                    float3 rgtRef = RotateByQuaternion(float3(1.0, 0.0, 0.0), _SphereInvRotation);

                    float pitch0 = asin(clamp(fwdRef.y, -1.0, 1.0)) * 57.2957795;

                    float2 vSky = float2(rgtRef.y, upRef.y);
                    float lenSky = length(vSky);
                    float2 nSky = (lenSky > 0.001) ? (vSky / lenSky) : float2(0.0, 1.0);
                    float2 nHrz = float2(nSky.y, -nSky.x);

                    float2 pos = float2(subCoord.x * ar, subCoord.y);
                    float pAxis = dot(pos, nSky);
                    float lAxis = dot(pos, nHrz);

                    float pitchDeg = clamp(pitch0 + pAxis * degPerUnit, -89.99, 89.99);
                    float lateralDeg = lAxis * degPerUnit;

                    float radP = radians(pitchDeg);
                    float cp = cos(radP);
                    float3 procP = float3(0.0, sin(radP), cp);

                    float absY = abs(procP.y);
                    float absPitch = abs(pitchDeg);
                    float markerClearance = 1.0;
                    if (_MarkerAvoid0.w > 0.01 || _MarkerAvoid1.w > 0.01 || _MarkerAvoid2.w > 0.01 || _MarkerAvoid3.w > 0.01)
                    {
                        float2 screenPoint = subCoord;
                        markerClearance = min(MarkerClearance(screenPoint, _MarkerAvoid0), MarkerClearance(screenPoint, _MarkerAvoid1));
                        markerClearance = min(markerClearance, min(MarkerClearance(screenPoint, _MarkerAvoid2), MarkerClearance(screenPoint, _MarkerAvoid3)));
                    }

                    return EvaluateNavballSurface(procP, pitchDeg, 0.0, absPitch, absY, 1.0, markerClearance, 1.0, lateralDeg);
                }
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // 1. 屏幕空间以 [0.5, 0.5] 为原点归一化到 [-1, 1] 坐标
                float2 coord = (i.uv - 0.5) * 2.0;

                float circleAlpha = 1.0;
                float rectAlpha = 1.0;
                float z = 1.0;
                float3 viewRay;
                float NdotV = 1.0;

                float3 p;
                if (_ApertureShape < 0.5)
                {
                    // 2. 经典圆形导航球 (Classic Circular Navball)
                    float r2 = dot(coord, coord);
                    float r = sqrt(r2);
                    float edgeAA = clamp(fwidth(r) * 0.75, 0.0004, 0.05);
                    circleAlpha = saturate((1.0 - r) / edgeAA);
                    if (circleAlpha <= 0.0) discard;

                    // 3. 逆向求解正交视线与单位球相交：正面球体深度 Z
                    z = sqrt(max(0.0, 1.0 - r2));

                    // 视线空间正面单位球面向量：X 右 (+x)，Y 上 (+y)，面向观察者的正面半球满足 Z = +z (前向沿 +Z)
                    viewRay = float3(coord.x, coord.y, z);
                    NdotV = z;

                    p = RotateByQuaternion(viewRay, _SphereInvRotation);
                    p = normalize(p);
                }
                else
                {
                    // 现代矩形姿态仪 / 导航球 (Modern Rectangular ADI / Navball)
                    float2 halfBox = float2(1.0, 1.0);
                    float rCorner = clamp(_CornerRadius, 0.001, 0.45);
                    float2 dBox = abs(coord) - (halfBox - rCorner);
                    float distBox = length(max(dBox, 0.0)) + min(max(dBox.x, dBox.y), 0.0) - rCorner;
                    float edgeAA = clamp(fwidth(distBox) * 0.75, 0.0004, 0.05);
                    rectAlpha = saturate(-distBox / edgeAA);
                    if (rectAlpha <= 0.0) discard;

                    if (_PfdMode < 0.5)
                    {
                        // 3D 矩形姿态球模式 (3D Spherical Navball in Rectangular Frame)
                        float ar = max(_AspectRatio, 0.1);
                        float rSphere = sqrt(ar * ar + 1.0) * 1.02;
                        float u = (coord.x * ar) / rSphere;
                        float v = coord.y / rSphere;
                        float r2 = u * u + v * v;
                        z = sqrt(max(0.0, 1.0 - r2));
                        viewRay = float3(u, v, z);
                        NdotV = z;

                        p = RotateByQuaternion(viewRay, _SphereInvRotation);
                        p = normalize(p);
                    }
                    else
                    {
                        // 降维民航 PFD 模式 (2-DOF Pitch & Roll: 航向解耦，平直度规)
                        float ar = max(_AspectRatio, 0.1);
                        float fovMul = clamp(_FovScale, 0.3, 2.5);
                        float degPerUnit = 27.5 * fovMul;

                        float3 fwdRef = RotateByQuaternion(float3(0.0, 0.0, 1.0), _SphereInvRotation);
                        float3 upRef  = RotateByQuaternion(float3(0.0, 1.0, 0.0), _SphereInvRotation);
                        float3 rgtRef = RotateByQuaternion(float3(1.0, 0.0, 0.0), _SphereInvRotation);

                        float pitch0 = asin(clamp(fwdRef.y, -1.0, 1.0)) * 57.2957795;

                        float2 vSky = float2(rgtRef.y, upRef.y);
                        float lenSky = length(vSky);
                        float2 nSky = (lenSky > 0.001) ? (vSky / lenSky) : float2(0.0, 1.0);
                        float2 nHrz = float2(nSky.y, -nSky.x);

                        float2 pos = float2(coord.x * ar, coord.y);
                        float pAxis = dot(pos, nSky);
                        float lAxis = dot(pos, nHrz);

                        float pitchDeg = clamp(pitch0 + pAxis * degPerUnit, -89.99, 89.99);
                        float lateralDeg = lAxis * degPerUnit;

                        float radP = radians(pitchDeg);
                        float cp = cos(radP);
                        p = float3(0.0, sin(radP), cp);
                        viewRay = float3(0.0, 0.0, 1.0);
                        z = 1.0;
                        NdotV = 1.0;
                    }

                fixed4 col;

                // 5. 模式选择分发 (0 = StockTexture 原版贴图采样, 1 = ProceduralVector 纯程序化数学矢量)
                if (_RenderMode < 0.5)
                {
                    // StockTexture: 球面坐标转标准等距柱状 UV
                    float pitch = asin(clamp(p.y, -1.0, 1.0));
                    float head = atan2(p.x, p.z);
                    if (head < 0.0) head += 6.28318530718;

                    // 原版 KSP 姿态球贴图坐标系校正：对齐地平线半球(天顶天底)与真航向
                    float u = frac(head * 0.159154943 + 0.5);
                    float v = clamp(0.5 + pitch * 0.318309886, 0.001, 0.999);

                    float2 stockUV = float2(u, v) * _MainTex_ST.xy + _MainTex_ST.zw;

                    // 消除经度 0°/360° 接缝处由于硬件导数突变引起的 Mipmap 极低阶模糊
                    float2 ddx_uv = ddx(stockUV);
                    float2 ddy_uv = ddy(stockUV);
                    if (abs(ddx_uv.x) > 0.4) ddx_uv = float2(0.0001, ddx_uv.y);
                    if (abs(ddy_uv.x) > 0.4) ddy_uv = float2(0.0001, ddy_uv.y);

                    // 负 Mipmap 偏置：强制 GPU 硬件采样最锐利 Mip 0 级别
                    ddx_uv *= 0.5;
                    ddy_uv *= 0.5;

                    fixed4 baseCol = tex2Dgrad(_MainTex, stockUV, ddx_uv, ddy_uv);

                    // 亚像素级微锐化卷积 (Unsharp Mask)，极大提升原版低清贴图的数字与标线锐度
                    float2 texel = _MainTex_TexelSize.xy * 0.55;
                    fixed4 blurCol = (tex2Dgrad(_MainTex, stockUV + float2(texel.x, 0), ddx_uv, ddy_uv) +
                                      tex2Dgrad(_MainTex, stockUV - float2(texel.x, 0), ddx_uv, ddy_uv) +
                                      tex2Dgrad(_MainTex, stockUV + float2(0, texel.y), ddx_uv, ddy_uv) +
                                      tex2Dgrad(_MainTex, stockUV - float2(0, texel.y), ddx_uv, ddy_uv)) * 0.25;
                    col = saturate(baseCol + (baseCol - blurCol) * 0.70);
                }
                else
                {
                    // ProceduralVector: 纯数学解析矢量求值 (极致锐利、任意分辨率无损)
                    // 内部各图元采用屏幕空间一阶导数 (fwidth) 精确解析 SDF 保底抗锯齿，直接单次求值呈现 100% 刀锋锐利度与高对比度
                    col = SampleProceduralNavball(coord);

                    // 姿态运动趋势全向 3D 前瞻预测引导系统 (Full 3-DOF Flight Path Trend Lead System)
                    // 完美覆盖俯仰 (Pitch / Y 轴位移)、偏航 (Yaw / X 轴横移) 与滚转 (Roll / 姿态倾角)
                    if (_TrendStrength > 0.01)
                    {
                        float4 qTrend = _TrendRotation;

                        // 1. 基准几何顶点在正交视线空间 (固定瞄准准星参考点)
                        // 中心准星点 (0, 0), 左机翼内端/外端, 右机翼内端/外端
                        float3 centerV   = float3( 0.00, 0.0, 1.0);
                        float3 leftInV   = float3(-0.16, 0.0, 1.0);
                        float3 leftOutV  = float3(-0.36, 0.0, 1.0);
                        float3 rightInV  = float3( 0.16, 0.0, 1.0);
                        float3 rightOutV = float3( 0.36, 0.0, 1.0);

                        // 2. 经三维四元数 qTrend 旋转变换至未来姿态位置
                        float3 predC  = RotateByQuaternion(centerV,   qTrend);
                        float3 predLI = RotateByQuaternion(leftInV,   qTrend);
                        float3 predLO = RotateByQuaternion(leftOutV,  qTrend);
                        float3 predRI = RotateByQuaternion(rightInV,  qTrend);
                        float3 predRO = RotateByQuaternion(rightOutV, qTrend);

                        // 投影到屏幕 2D 坐标 (X 右, Y 上)
                        float2 c2D  = predC.xy;
                        float2 li2D = predLI.xy;
                        float2 lo2D = predLO.xy;
                        float2 ri2D = predRI.xy;
                        float2 ro2D = predRO.xy;

                        // 翼尖微型小翼折角端点 (Winglet Fences: 垂直于机翼且指向天顶)
                        float2 wingDirL = normalize(lo2D - li2D + 0.0001);
                        float2 wingNormalL = float2(-wingDirL.y, wingDirL.x);
                        if (wingNormalL.y < 0.0) wingNormalL = -wingNormalL;
                        float2 fenceTopL = lo2D + wingNormalL * 0.024;

                        float2 wingDirR = normalize(ro2D - ri2D + 0.0001);
                        float2 wingNormalR = float2(-wingDirR.y, wingDirR.x);
                        if (wingNormalR.y < 0.0) wingNormalR = -wingNormalR;
                        float2 fenceTopR = ro2D + wingNormalR * 0.024;

                        float aa = clamp(fwidth(coord.x) * 0.85, 0.0008, 0.05);

                        // 3. 计算片元到前瞻机翼线段与翼尖小翼的精确距离
                        float dWingL = DistanceToSegment2D(coord, li2D, lo2D);
                        float dWingR = DistanceToSegment2D(coord, ri2D, ro2D);
                        float dFenceL = DistanceToSegment2D(coord, lo2D, fenceTopL);
                        float dFenceR = DistanceToSegment2D(coord, ro2D, fenceTopR);

                        float dWings = min(min(dWingL, dWingR), min(dFenceL, dFenceR));

                        // 机翼主干锐利核芯与微光晕
                        float isWingCore = 1.0 - smoothstep(0.0055 - aa, 0.0055 + aa, dWings);
                        float isWingHalo = (1.0 - smoothstep(0.0160 - aa, 0.0160 + aa, dWings)) * 0.38;
                        float isWings = max(isWingCore, isWingHalo);

                        // 4. 前瞻瞄准中心微菱形标 (Center Flight Path Lead Diamond Pip)
                        float2 dC = abs(coord - c2D);
                        float dDiamond = dC.x + dC.y;
                        float isDiamondCore = 1.0 - smoothstep(0.012 - aa, 0.012 + aa, dDiamond);
                        float isDiamondHalo = (1.0 - smoothstep(0.024 - aa, 0.024 + aa, dDiamond)) * 0.40;
                        float isCenterDiamond = max(isDiamondCore, isDiamondHalo);

                        // 5. 3 维运动动态拉杆带与速度航向引导虚线 (Motion Trend Tapes & Lead Vector)
                        // A. 翼端趋势拉杆带 (连接当前固定翼端与预测翼端)
                        float dTapeL = DistanceToSegment2D(coord, float2(-0.36, 0.0), lo2D);
                        float dTapeR = DistanceToSegment2D(coord, float2( 0.36, 0.0), ro2D);
                        float isTapeL = (1.0 - smoothstep(0.0040 - aa, 0.0040 + aa, dTapeL)) * 0.65;
                        float isTapeR = (1.0 - smoothstep(0.0040 - aa, 0.0040 + aa, dTapeR)) * 0.65;
                        float isWingTapes = max(isTapeL, isTapeR);

                        // B. 中心速度/角动量动态前瞻虚线 (Dashed Center Flight Path Trail)
                        float dCenterTrail = DistanceToSegment2D(coord, float2(0.0, 0.0), c2D);
                        float distFromOrigin = length(coord);
                        float dashPulse = sin(distFromOrigin * 90.0 - _Time.y * 12.0) * 0.5 + 0.5;
                        float isCenterTrail = (1.0 - smoothstep(0.0035 - aa, 0.0035 + aa, dCenterTrail)) * (0.30 + 0.45 * dashPulse);

                        // 6. 综合渲染融合 (纯净发光青蓝复合航电色彩)
                        float trendPrimary = max(isWings, isCenterDiamond);
                        float trendSecondary = max(isWingTapes, isCenterTrail);
                        float trendTotal = max(trendPrimary, trendSecondary);

                        float trendAlpha = trendTotal * _TrendStrength * 0.95;

                        fixed3 trendCoreCol = fixed3(0.18, 0.96, 1.0);  // 电光青蓝主色
                        fixed3 trendTapeCol = fixed3(0.06, 0.70, 0.88);  // 动量带稍深青蓝
                        fixed3 finalTrendCol = lerp(trendTapeCol, trendCoreCol, saturate(trendPrimary));

                        col.rgb = lerp(col.rgb, finalTrendCol, trendAlpha);
                    }
                }

                if (_ApertureShape < 0.5 || (_ApertureShape >= 0.5 && _PfdMode < 0.5))
                {
                    // 6. 3D 球面深度边缘衰减 (Limb Darkening)
                    float limbFalloff = pow(NdotV, _LimbPower);
                    float limbShade = lerp(1.0 - _LimbIntensity * 0.45, 1.0, limbFalloff);
                    col.rgb *= limbShade;

                    // 7. 边缘航电微光 (Atmospheric Rim Glow)
                    float rim = pow(1.0 - NdotV, _RimPower);
                    col.rgb += _RimColor.rgb * rim * _RimIntensity * 0.20;
                }
                else
                {
                    // 矩形平直 PFD 采用微弱航电 LCD 边缘防眩微渐变，纯净通透，杜绝球面暗角
                    float edgeDist = max(abs(coord.x), abs(coord.y));
                    float lcdShade = lerp(1.0, 0.94, smoothstep(0.85, 1.0, edgeDist));
                    col.rgb *= lcdShade;
                }

                // 8. 仪表盘曲面玻璃高光 (Curved Cockpit Glass Lens Reflection)
                float3 lightDir = normalize(float3(-0.35, 0.6, 0.7));
                float3 viewDir = float3(0, 0, 1);
                float3 halfDir = normalize(lightDir + viewDir);
                float3 viewNormal = (_ApertureShape < 0.5 || (_ApertureShape >= 0.5 && _PfdMode < 0.5)) ? float3(coord.x, coord.y, z) : float3(0, 0, 1);
                float specAngle = saturate(dot(viewNormal, halfDir));
                float spec = pow(specAngle, _Glossiness) * _SpecIntensity;
                col.rgb += _SpecularColor.rgb * spec * 0.20;

                // 9. 结合 UGUI 裁剪矩形与次像素羽化 Alpha
                #ifdef UNITY_UI_CLIP_RECT
                col.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif

                float shapeAlpha = (_ApertureShape < 0.5) ? circleAlpha : rectAlpha;
                col.a *= shapeAlpha * i.color.a;
                return col;
            }
            ENDCG
        }
    }
    FallBack "UI/Default"
}
