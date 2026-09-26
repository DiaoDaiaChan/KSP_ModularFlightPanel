Shader "ModularFlightPanel/NavballRaymarch"
{
    Properties
    {
        [PerRendererData] _MainTex ("Navball Texture", 2D) = "white" {}
        _RenderMode ("Render Mode (0=Stock, 1=Vector, 2=Bake)", Float) = 0.0

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

        _PitchLadderColor ("Pitch Ladder Color", Color) = (0.90, 0.96, 1.0, 0.65)
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
        _TrendRotation ("Predicted Attitude Delta", Vector) = (0, 0, 0, 1)
        _TrendStrength ("Attitude Trend Strength", Range(0.0, 1.0)) = 0.0
        _GroundHazardAlert ("Ground Hazard Alert", Range(0.0, 1.0)) = 0.0
        _VernierScaleDetail ("Vernier Scale Detail", Range(0.0, 1.0)) = 0.0

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
            float4x4 _SphereInvRotation;

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
            float4 _TrendRotation;
            float _TrendStrength;
            float _GroundHazardAlert;
            float _VernierScaleDetail;

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

            // SDF 紧凑七段矢量字形生成器
            float SegmentDistance(float2 p, float2 center, float2 halfSize)
            {
                float2 d = abs(p - center) - halfSize;
                return max(d.x, d.y);
            }

            float DigitDistance(float2 p, float digitF)
            {
                int digit = (int)round(digitF);
                float d = 100.0;
                float strokeH = 0.42;
                float2 horizSize = float2(1.30, strokeH);
                float2 vertSize = float2(strokeH, 0.95);

                if (digit != 1 && digit != 4)
                    d = min(d, SegmentDistance(p, float2(0.0, 2.0), horizSize));
                if (digit != 0 && digit != 1 && digit != 7)
                    d = min(d, SegmentDistance(p, float2(0.0, 0.0), horizSize));
                if (digit != 1 && digit != 4 && digit != 7)
                    d = min(d, SegmentDistance(p, float2(0.0, -2.0), horizSize));

                if (digit == 0 || digit == 4 || digit == 5 || digit == 6 || digit == 8 || digit == 9)
                    d = min(d, SegmentDistance(p, float2(-1.10, 1.0), vertSize));
                if (digit != 5 && digit != 6)
                    d = min(d, SegmentDistance(p, float2(1.10, 1.0), vertSize));
                if (digit == 0 || digit == 2 || digit == 6 || digit == 8)
                    d = min(d, SegmentDistance(p, float2(-1.10, -1.0), vertSize));
                if (digit != 2)
                    d = min(d, SegmentDistance(p, float2(1.10, -1.0), vertSize));

                return d;
            }

            float3 RotateByQuaternion(float3 v, float4 q)
            {
                return v + 2.0 * cross(q.xyz, cross(q.xyz, v) + q.w * v);
            }

            fixed4 EvaluateNavballSurface(float3 p, float pitchDeg, float headDeg, float absPitch, float absY, float NdotV)
            {
                // 1. 天空与地面平滑梯度 (Aero Horizon Gradient)
                fixed4 col;
                if (p.y >= 0.0)
                {
                    col = lerp(_SkyHorizonColor, _SkyZenithColor, pow(p.y, 0.88));
                }
                else
                {
                    col = lerp(_GroundHorizonColor, _GroundNadirColor, pow(-p.y, 0.88));

                    // 赤道地面侧高反差切光暗带 (-0.055 < p.y < 0)
                    float horizonTrench = smoothstep(-0.055, -0.001, p.y) * 0.24;
                    col.rgb = lerp(col.rgb, _GroundNadirColor.rgb * 0.50, horizonTrench);

                    // 地面深邃重力沉降
                    float nadirDepth = pow(-p.y, 1.45) * 0.22;
                    col.rgb = lerp(col.rgb, _GroundNadirColor.rgb * 0.60, nadirDepth);

                    // GPWS / 近地大下沉率防撞动态斑马纹
                    if (_GroundHazardAlert > 0.01)
                    {
                        float stripe = sin((p.x * 18.0 + p.y * 28.0 + p.z * 18.0) + _Time.y * 5.5);
                        float stripeAA = max(fwidth(stripe) * 1.5, 0.06);
                        float isStripe = smoothstep(-stripeAA, stripeAA, stripe - 0.10) * _GroundHazardAlert;
                        fixed3 hazardCol = fixed3(1.0, 0.82, 0.05);
                        col.rgb = lerp(col.rgb, hazardCol, isStripe * 0.55);
                    }
                }

                // 参考系纹理签名
                float frameDetail = 0.0;
                if (_FramePattern > 0.5 && _FramePattern < 1.5 && p.y > 0.0)
                {
                    float starLat = abs(frac((pitchDeg + 82.5) / 15.0 + 0.5) - 0.5);
                    float starLon = abs(frac((headDeg + 15.0) / 30.0 + 0.5) - 0.5);
                    frameDetail = (starLat < 0.035 && starLon < 0.035) ? 0.48 : 0.0;
                }
                else if (_FramePattern > 1.5 && _FramePattern < 2.5)
                {
                    float diagonalA = abs(frac((headDeg + pitchDeg * 0.72) / 24.0 + 0.5) - 0.5);
                    float diagonalB = abs(frac((headDeg - pitchDeg * 0.72) / 24.0 + 0.5) - 0.5);
                    frameDetail = max(1.0 - smoothstep(0.015, 0.055, diagonalA), 1.0 - smoothstep(0.015, 0.055, diagonalB)) * 0.22;
                }
                else if (_FramePattern > 2.5 && _FramePattern < 3.5)
                {
                    float targetRange = acos(clamp(-p.z, -1.0, 1.0)) * 57.2957795;
                    float ringOffset = abs(fmod(targetRange + 7.5, 15.0) - 7.5);
                    frameDetail = (1.0 - smoothstep(0.20, 0.85, ringOffset)) * 0.24;
                }
                else if (_FramePattern > 3.5 && _FramePattern < 4.5)
                {
                    float directionMeridian = abs(frac(headDeg / 45.0 + 0.5) - 0.5);
                    frameDetail = (1.0 - smoothstep(0.01, 0.04, directionMeridian)) * 0.18;
                }
                else if (_FramePattern > 4.5)
                {
                    float bodyContour = abs(frac((absPitch + 7.5) / 15.0 + 0.5) - 0.5);
                    frameDetail = (1.0 - smoothstep(0.01, 0.045, bodyContour)) * 0.18;
                }
                frameDetail *= smoothstep(0.05, 0.52, _DetailScale);
                col.rgb = lerp(col.rgb, _HeadingLineColor.rgb, frameDetail * _HeadingLineColor.a);

                // 2. 赤道分割基准线 (Pure Crisp Horizon Line)
                float eqAA = fwidth(p.y) * 1.5;
                float isEquator = 1.0 - smoothstep(_EquatorWidth, _EquatorWidth + eqAA, absY);
                col = lerp(col, _EquatorColor, isEquator);

                // 极点渐隐防聚集保护
                float polarLadderFade = 1.0 - smoothstep(68.0, 78.0, absPitch);

                // 3. 俯仰梯级计算
                float pitchHeadingCenter = floor(headDeg / 30.0 + 0.5) * 30.0;
                float pitchHeadingOffset = headDeg - pitchHeadingCenter;
                if (pitchHeadingOffset > 180.0) pitchHeadingOffset -= 360.0;
                if (pitchHeadingOffset < -180.0) pitchHeadingOffset += 360.0;
                float absHOffset = abs(pitchHeadingOffset);

                float pitchLabelLevel = round(absPitch / 15.0) * 15.0;
                float pitchLabelCenter = (pitchDeg < 0.0 ? -pitchLabelLevel : pitchLabelLevel);
                float pitchLabelOffset = pitchDeg - pitchLabelCenter;
                float absLabelOffset = abs(pitchLabelOffset);

                float tangentAspect = lerp(1.0, clamp(1.0 / max(NdotV, 0.58), 1.0, 1.28), _NumeralTangentComp);
                float numRoll = -_NumeralRollAngle * _NumeralUprightMode;
                float cosNR = cos(numRoll);
                float sinNR = sin(numRoll);

                float pitchGlyphDistance = 100.0;
                float pitchGlyphEnabled = 0.0;

                if (absPitch >= 12.0 && absPitch <= 78.0)
                {
                    pitchGlyphEnabled = polarLadderFade * smoothstep(0.12, 0.42, NdotV) * smoothstep(0.08, 0.34, _DetailScale);

                    if (absHOffset < 6.8 && absLabelOffset < 3.8)
                    {
                        float pitchTens = floor(pitchLabelLevel / 10.0);
                        float pitchOnes = fmod(pitchLabelLevel, 10.0);

                        float2 pitchCenterOffset = float2(pitchHeadingOffset * tangentAspect, pitchLabelOffset);
                        float2 rotPitchOffset = float2(
                            pitchCenterOffset.x * cosNR - pitchCenterOffset.y * sinNR,
                            pitchCenterOffset.x * sinNR + pitchCenterOffset.y * cosNR
                        );

                        pitchGlyphDistance = min(
                            DigitDistance(rotPitchOffset + float2(2.0, 0.0), pitchTens),
                            DigitDistance(rotPitchOffset - float2(2.0, 0.0), pitchOnes));

                        if (pitchDeg < 0.0)
                        {
                            float pitchSignDistance = SegmentDistance(rotPitchOffset, float2(-4.45, 0.0), float2(0.58, 0.10));
                            pitchGlyphDistance = min(pitchGlyphDistance, pitchSignDistance);
                        }
                    }
                }

                // 15° 主横杠 (横跨 ±10.0°，两端带有垂直指示末梢)
                float majorLadder15 = 0.0;
                if (absHOffset <= 12.0 && pitchLabelLevel >= 12.0 && pitchLabelLevel <= 78.0 && absLabelOffset < 2.5)
                {
                    float barPitchAA = max(fwidth(absLabelOffset) * 1.4, 0.04);
                    float barHAA = max(fwidth(absHOffset) * 1.4, 0.08);
                    float isMajorBar15 = (1.0 - smoothstep(0.30 - barPitchAA, 0.30 + barPitchAA, absLabelOffset)) *
                                         smoothstep(4.8 - barHAA, 4.8 + barHAA, absHOffset) *
                                         (1.0 - smoothstep(10.0 - barHAA, 10.0 + barHAA, absHOffset));

                    float isTip15 = 0.0;
                    if (absHOffset >= 9.0 && absHOffset < 10.2)
                    {
                        float tipHMask = smoothstep(9.2 - barHAA, 9.2 + barHAA, absHOffset) * (1.0 - smoothstep(10.0 - barHAA, 10.0 + barHAA, absHOffset));
                        if (pitchDeg > 0.0)
                        {
                            float tipVMask = (1.0 - smoothstep(0.0 - barPitchAA, 0.0 + barPitchAA, pitchLabelOffset)) * smoothstep(-1.6 - barPitchAA, -1.6 + barPitchAA, pitchLabelOffset);
                            isTip15 = tipHMask * tipVMask;
                        }
                        else
                        {
                            float tipVMask = smoothstep(0.0 - barPitchAA, 0.0 + barPitchAA, pitchLabelOffset) * (1.0 - smoothstep(1.6 - barPitchAA, 1.6 + barPitchAA, pitchLabelOffset));
                            isTip15 = tipHMask * tipVMask;
                        }
                    }
                    majorLadder15 = max(isMajorBar15, isTip15) * pitchGlyphEnabled;

                    if (pitchDeg < 0.0)
                    {
                        float dashVal = fmod(absHOffset - 4.8, 2.2);
                        float dashAA = max(fwidth(dashVal) * 1.5, 0.08);
                        float dashMask = 1.0 - smoothstep(1.15 - dashAA, 1.15 + dashAA, dashVal);
                        majorLadder15 *= dashMask;
                    }
                }

                // 中间 10° 梯级中杠 (横跨 ±5.5°)
                float isTick10 = 0.0;
                if (absHOffset <= 6.0 && absPitch > 4.0 && absPitch < 80.0)
                {
                    float pMod10 = abs(pitchDeg - round(pitchDeg / 10.0) * 10.0);
                    float pLevel10 = round(absPitch / 10.0) * 10.0;
                    bool isPure10 = (fmod(pLevel10, 30.0) > 4.0) && (fmod(pLevel10, 15.0) > 4.0);
                    if (isPure10 && pMod10 < 0.6)
                    {
                        float tickPitchAA = max(fwidth(pMod10) * 1.5, 0.04);
                        float tickHAA = max(fwidth(absHOffset) * 1.5, 0.08);
                        isTick10 = (1.0 - smoothstep(0.22 - tickPitchAA, 0.22 + tickPitchAA, pMod10)) *
                                   (1.0 - smoothstep(5.5 - tickHAA, 5.5 + tickHAA, absHOffset));
                        if (pitchDeg < 0.0)
                        {
                            float dash10 = fmod(absHOffset, 1.8);
                            float dashAA = max(fwidth(dash10) * 1.5, 0.08);
                            isTick10 *= (1.0 - smoothstep(0.95 - dashAA, 0.95 + dashAA, dash10));
                        }
                        isTick10 *= polarLadderFade;
                    }
                }

                // 精细 5° 短刻度线 (横跨 ±2.8°)
                float isTick5 = 0.0;
                if (absHOffset <= 3.5 && absPitch > 2.0 && absPitch < 82.0)
                {
                    float pMod5 = abs(pitchDeg - round(pitchDeg / 5.0) * 5.0);
                    float pLevel5 = round(absPitch / 5.0) * 5.0;
                    bool isPure5 = (fmod(pLevel5, 10.0) > 2.0);
                    if (isPure5 && pMod5 < 0.5)
                    {
                        float tickPitchAA = max(fwidth(pMod5) * 1.5, 0.04);
                        float tickHAA = max(fwidth(absHOffset) * 1.5, 0.08);
                        isTick5 = (1.0 - smoothstep(0.18 - tickPitchAA, 0.18 + tickPitchAA, pMod5)) *
                                  (1.0 - smoothstep(2.8 - tickHAA, 2.8 + tickHAA, absHOffset));
                        if (pitchDeg < 0.0)
                        {
                            float dash5 = fmod(absHOffset, 1.4);
                            float dashAA = max(fwidth(dash5) * 1.5, 0.08);
                            isTick5 *= (1.0 - smoothstep(0.7 - dashAA, 0.7 + dashAA, dash5));
                        }
                        isTick5 *= polarLadderFade;
                    }
                }

                // 近地平游标 2.5° 精密微刻度 (Vernier Scale Detail)
                float isVernierTick = 0.0;
                if (_VernierScaleDetail > 0.01 && absHOffset <= 1.8 && absPitch > 0.5 && absPitch < 6.2)
                {
                    float pMod25 = abs(pitchDeg - round(pitchDeg / 2.5) * 2.5);
                    float pLevel25 = round(absPitch / 2.5) * 2.5;
                    bool isPure25 = (fmod(pLevel25, 5.0) > 1.0);
                    if (isPure25 && pMod25 < 0.4)
                    {
                        float tickPitchAA = max(fwidth(pMod25) * 1.5, 0.035);
                        float tickHAA = max(fwidth(absHOffset) * 1.5, 0.06);
                        isVernierTick = (1.0 - smoothstep(0.14 - tickPitchAA, 0.14 + tickPitchAA, pMod25)) *
                                        (1.0 - smoothstep(1.5 - tickHAA, 1.5 + tickHAA, absHOffset)) * _VernierScaleDetail;
                    }
                }

                float ladderColorIntensity = saturate(majorLadder15 + isTick10 + isTick5 + isVernierTick);
                col = lerp(col, _PitchLadderColor, ladderColorIntensity * _PitchLadderColor.a);

                // 数字笔画与抗锯齿轮廓绘制
                if (pitchGlyphEnabled > 0.01 && pitchGlyphDistance < 2.0)
                {
                    float glyphAA = max(fwidth(pitchGlyphDistance) * 1.35, 0.06);
                    float glyphFill = 1.0 - smoothstep(0.08 - glyphAA, 0.08 + glyphAA, pitchGlyphDistance);
                    float glyphOutline = (1.0 - smoothstep(0.38 - glyphAA, 0.38 + glyphAA, pitchGlyphDistance)) * (1.0 - glyphFill);

                    col = lerp(col, _LabelOutlineColor, glyphOutline * _LabelOutlineColor.a * pitchGlyphEnabled);
                    col = lerp(col, _LabelColor, glyphFill * _LabelColor.a * pitchGlyphEnabled);
                }

                // 4. 经度主子午线与航向刻度数字 (Meridian Cardinals & Heading Labels)
                float meridian45 = abs(fmod(headDeg + 22.5, 45.0) - 22.5);
                float meridianAA = max(fwidth(meridian45) * 1.4, 0.06);
                float isMeridian = 1.0 - smoothstep(_PitchLadderWidth * 55.0 - meridianAA, _PitchLadderWidth * 55.0 + meridianAA, meridian45);
                float meridianFade = 1.0 - smoothstep(72.0, 84.0, absPitch);
                col = lerp(col, _HeadingLineColor, isMeridian * _HeadingLineColor.a * 0.45 * meridianFade);

                // 90° 主经线与四向航向角标签 (000°, 090°, 180°, 270°)
                float headingMajorCenter = round(headDeg / 90.0) * 90.0;
                float headingMajorOffset = headDeg - headingMajorCenter;
                if (headingMajorOffset > 180.0) headingMajorOffset -= 360.0;
                if (headingMajorOffset < -180.0) headingMajorOffset += 360.0;

                float absHMajorOffset = abs(headingMajorOffset);
                if (absPitch <= 6.5 && absHMajorOffset < 8.5)
                {
                    int hdgVal = (int)fmod(headingMajorCenter + 360.0, 360.0);
                    float dHundreds = floor(hdgVal / 100.0);
                    float dTens = floor(fmod(hdgVal, 100.0) / 10.0);
                    float dOnes = fmod(hdgVal, 10.0);

                    float2 hCenterOffset = float2(headingMajorOffset, pitchDeg);
                    float headingGlyphDist = min(
                        DigitDistance(hCenterOffset + float2(3.5, 0.0), dHundreds),
                        min(DigitDistance(hCenterOffset, dTens), DigitDistance(hCenterOffset - float2(3.5, 0.0), dOnes))
                    );

                    float hGlyphAA = max(fwidth(headingGlyphDist) * 1.35, 0.06);
                    float hGlyphFill = 1.0 - smoothstep(0.08 - hGlyphAA, 0.08 + hGlyphAA, headingGlyphDist);
                    float hGlyphOutline = (1.0 - smoothstep(0.38 - hGlyphAA, 0.38 + hGlyphAA, headingGlyphDist)) * (1.0 - hGlyphFill);
                    float hGlyphAlpha = smoothstep(0.15, 0.45, NdotV) * smoothstep(0.08, 0.34, _DetailScale);

                    col = lerp(col, _LabelOutlineColor, hGlyphOutline * _LabelOutlineColor.a * hGlyphAlpha);
                    col = lerp(col, _LabelColor, hGlyphFill * _LabelColor.a * hGlyphAlpha);
                }

                // 5. 天顶 (Zenith) 与天底 (Nadir) 极地十字符号
                if (absPitch > 82.0)
                {
                    float2 polarCoord = float2(p.x, p.z);
                    float polarLen = length(polarCoord);
                    float polarAngle = atan2(polarCoord.y, polarCoord.x);

                    float polarCrossMod = abs(fmod(polarAngle + 0.785398, 1.570796) - 0.785398) * polarLen;
                    float crossAA = max(fwidth(polarCrossMod) * 1.5, 0.005);
                    float isCrossArm = (1.0 - smoothstep(0.012 - crossAA, 0.012 + crossAA, polarCrossMod)) *
                                       (1.0 - smoothstep(0.14 - crossAA, 0.14 + crossAA, polarLen));

                    float isCrossRing = abs(polarLen - 0.085);
                    float ringAA = max(fwidth(isCrossRing) * 1.5, 0.005);
                    float isCrossCircle = 1.0 - smoothstep(0.008 - ringAA, 0.008 + ringAA, isCrossRing);

                    fixed4 markColor = (p.y > 0.0) ? _EquatorColor : fixed4(_EquatorColor.rgb, 0.75);
                    col = lerp(col, markColor, saturate(isCrossArm + isCrossCircle) * markColor.a);
                }

                return col;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // 1. 屏幕空间以 [0.5, 0.5] 为原点归一化到 [-1, 1] 坐标
                float2 coord = (i.uv - 0.5) * 2.0;
                float r2 = dot(coord, coord);
                float r = sqrt(r2);

                // 2. 硬件导数亚像素完美抗锯齿边缘 (Subpixel Silhouette AA)
                float edgeAA = max(fwidth(r) * 1.5, 0.004);
                float circleAlpha = saturate((1.0 - r) / edgeAA);
                if (circleAlpha <= 0.0) discard;

                // 3. 逆向求解正交视线与单位球相交：正面球体深度 Z
                float z = sqrt(max(0.0, 1.0 - r2));

                // 视线空间正面单位球面朝向观察者向量：X 右，Y 上，Z 深入屏幕 (-z)
                float3 viewRay = float3(coord.x, coord.y, -z);

                // 4. 将视线空间坐标乘以姿态逆旋转矩阵，瞬间求得球体本地三维坐标 p！
                float3 p = mul((float3x3)_SphereInvRotation, viewRay);
                p = normalize(p);

                // 在正交相机投影下，视线法线点积 NdotV 恒等于几何深度 z
                float NdotV = z;

                fixed4 col;

                // 5. 模式选择分发 (0 = StockTexture 原版贴图采样, 1 = ProceduralVector 纯程序化数学矢量, 2 = ProceduralBake 烘焙贴图采样)
                if (_RenderMode < 0.5)
                {
                    // StockTexture: 球面坐标转标准等距柱状 UV
                    float pitch = asin(clamp(p.y, -1.0, 1.0));
                    float head = atan2(p.x, -p.z);
                    if (head < 0.0) head += 6.28318530718;

                    float u = head * 0.159154943;          // [0, 2PI] -> [0, 1]
                    float v = pitch * 0.318309886 + 0.5;   // [-PI/2, PI/2] -> [0, 1]

                    float2 stockUV = float2(u, v) * _MainTex_ST.xy + _MainTex_ST.zw;

                    // 消除经度 0°/360° 接缝处由于硬件导数突变引起的 Mipmap 极低阶模糊
                    float2 ddx_uv = ddx(stockUV);
                    float2 ddy_uv = ddy(stockUV);
                    if (abs(ddx_uv.x) > 0.4) ddx_uv.x = 0.0;
                    if (abs(ddy_uv.x) > 0.4) ddy_uv.x = 0.0;

                    col = tex2Dgrad(_MainTex, stockUV, ddx_uv, ddy_uv);
                }
                else if (_RenderMode > 1.5)
                {
                    // ProceduralBake: 采样离屏烘焙纹理
                    float pitch = asin(clamp(p.y, -1.0, 1.0));
                    float head = atan2(p.x, -p.z);
                    if (head < 0.0) head += 6.28318530718;

                    float u = head * 0.159154943;
                    float v = pitch * 0.318309886 + 0.5;

                    float2 bakeUV = float2(u, v);
                    float2 ddx_uv = ddx(bakeUV);
                    float2 ddy_uv = ddy(bakeUV);
                    if (abs(ddx_uv.x) > 0.4) ddx_uv.x = 0.0;
                    if (abs(ddy_uv.x) > 0.4) ddy_uv.x = 0.0;

                    col = tex2Dgrad(_MainTex, bakeUV, ddx_uv, ddy_uv);
                }
                else
                {
                    // ProceduralVector: 纯数学解析矢量求值 (极致锐利、任意分辨率无损)
                    float pitchDeg = asin(clamp(p.y, -1.0, 1.0)) * 57.2957795;
                    float headDeg = atan2(p.x, -p.z) * 57.2957795;
                    if (headDeg < 0.0) headDeg += 360.0;
                    float absY = abs(p.y);
                    float absPitch = abs(pitchDeg);

                    col = EvaluateNavballSurface(p, pitchDeg, headDeg, absPitch, absY, NdotV);

                    // 姿态趋势预测动态虚线地平线
                    if (_TrendStrength > 0.01)
                    {
                        float3 futureP = normalize(RotateByQuaternion(p, float4(-_TrendRotation.x, -_TrendRotation.y, -_TrendRotation.z, _TrendRotation.w)));
                        float trendAA = max(fwidth(futureP.y) * 2.2, 0.008);
                        float futureHorizon = 1.0 - smoothstep(0.003, 0.003 + trendAA, abs(futureP.y));
                        float dashVal = frac(headDeg / 24.0);
                        float dashAA = max(fwidth(dashVal) * 1.5, 0.04);
                        float trendDash = smoothstep(0.24 - dashAA, 0.24 + dashAA, dashVal);
                        float trendOpacity = futureHorizon * trendDash * _TrendStrength * _HeadingLineColor.a * 0.78;
                        col.rgb = lerp(col.rgb, _HeadingLineColor.rgb, trendOpacity);
                    }
                }

                // 6. 3D 球面深度边缘衰减 (Limb Darkening)
                float limbFalloff = pow(NdotV, _LimbPower);
                float limbShade = lerp(1.0 - _LimbIntensity * 0.45, 1.0, limbFalloff);
                col.rgb *= limbShade;

                // 7. 边缘航电微光 (Atmospheric Rim Glow)
                float rim = pow(1.0 - NdotV, _RimPower);
                col.rgb += _RimColor.rgb * rim * _RimIntensity * 0.20;

                // 8. 仪表盘曲面玻璃高光 (Curved Cockpit Glass Lens Reflection)
                float3 lightDir = normalize(float3(-0.35, 0.6, 0.7));
                float3 viewDir = float3(0, 0, 1);
                float3 halfDir = normalize(lightDir + viewDir);
                float3 viewNormal = float3(coord.x, coord.y, z);
                float specAngle = saturate(dot(viewNormal, halfDir));
                float spec = pow(specAngle, _Glossiness) * _SpecIntensity;
                col.rgb += _SpecularColor.rgb * spec * 0.20;

                // 9. 结合 UGUI 裁剪矩形与次像素羽化 Alpha
                #ifdef UNITY_UI_CLIP_RECT
                col.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif

                col.a *= circleAlpha * i.color.a;
                return col;
            }
            ENDCG
        }
    }
    FallBack "UI/Default"
}
