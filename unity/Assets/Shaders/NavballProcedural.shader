Shader "ModularFlightPanel/NavballProcedural"
{
    Properties
    {
        _MainTex ("Texture (Unused in Procedural)", 2D) = "white" {}
        
        // 天空渐变配色 (Aero Sky Gradient)
        _SkyZenithColor ("Sky Zenith (Deep Space Blue)", Color) = (0.04, 0.16, 0.36, 1.0)
        _SkyHorizonColor ("Sky Horizon (Aero Cyan)", Color) = (0.08, 0.46, 0.72, 1.0)
        
        // 地面渐变配色 (Aero Ground Brown Gradient)
        _GroundHorizonColor ("Ground Horizon (Earth Brown)", Color) = (0.45, 0.25, 0.12, 1.0)
        _GroundNadirColor ("Ground Nadir (Deep Nadir Brown)", Color) = (0.22, 0.12, 0.05, 1.0)
        
        // 地平线与赤道线 (Equator Dividing Line)
        _EquatorColor ("Equator Line Color", Color) = (1.0, 1.0, 1.0, 1.0)
        _EquatorWidth ("Equator Width", Range(0.001, 0.015)) = 0.004
        _LabelColor ("Numeral Color", Color) = (0.94, 0.97, 1.0, 0.94)
        _LabelOutlineColor ("Numeral Outline", Color) = (0.015, 0.025, 0.04, 0.78)
        
        // 俯仰阶梯线 (Pitch Ladder - 30° 主刻度)
        _PitchLadderColor ("Pitch Ladder Color", Color) = (0.90, 0.96, 1.0, 0.65)
        _PitchLadderWidth ("Pitch Ladder Thickness", Range(0.001, 0.015)) = 0.003
        
        // 经度子午线 (Heading Cardinals - 4 向主经线)
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

        // 动态数字滚转自适应与人机工效 (Dynamic Numeral Roll Alignment & Ergonomics)
        _NumeralRollAngle ("Numeral Roll Angle (Rad)", Float) = 0.0
        _NumeralUprightMode ("Numeral Upright Mode", Range(0.0, 1.0)) = 1.0
        _NumeralTangentComp ("Tangent Foreshortening Comp", Range(0.0, 1.0)) = 1.0
        _DetailScale ("Screen-Size Detail", Range(0.0, 1.0)) = 1.0
        _FramePattern ("Reference Frame Pattern", Range(0.0, 5.0)) = 0.0
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
    }

    SubShader
    {
        Tags 
        { 
            "Queue" = "Transparent" 
            "RenderType" = "Transparent" 
            "IgnoreProjector" = "True"
        }
        LOD 200
        Cull Back
        Blend SrcAlpha OneMinusSrcAlpha

        // =========================================================================================
        // Pass 0: 3D 旋转球面渲染通道 (3D Rotating Sphere Shading)
        // 姿态旋转时仅执行单次硬件纹理采样 + 极轻量 Limb Darkening / Rim Glow / Specular 反光
        // =========================================================================================
        Pass
        {
            Name "NavballSphere3D"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 localPos : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
                float2 viewPos : TEXCOORD3;
            };

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
            float4 _MarkerAvoid0;
            float4 _MarkerAvoid1;
            float4 _MarkerAvoid2;
            float4 _MarkerAvoid3;
            float _GroundHazardAlert;
            float _VernierScaleDetail;

            float SegmentDistance(float2 p, float2 center, float2 halfSize)
            {
                float2 d = abs(p - center) - halfSize;
                return max(d.x, d.y);
            }

            // Compact seven-segment vector glyphs keep numbers independent from the selected mode texture.
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

            float MarkerClearance(float2 screenPoint, float4 marker)
            {
                if (marker.w < 0.01) return 1.0;
                float markerDistance = length(screenPoint - marker.xy);
                float clearAtRadius = smoothstep(marker.z * 0.62, marker.z * 1.08, markerDistance);
                return lerp(1.0, clearAtRadius, saturate(marker.w));
            }

            // 核心程序化曲面求值函数 (共享于实时备用通道与离屏烘焙通道)
            fixed4 EvaluateNavballSurface(float3 p, float pitchDeg, float headDeg, float absPitch, float absY, float NdotV, float markerClearance)
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

                    // 1. 赤道地面侧高反差切光暗带 (-0.055 < p.y < 0)，极大提升白色地平线边缘对比度，天地一刀分明
                    float horizonTrench = smoothstep(-0.055, -0.001, p.y) * 0.24;
                    col.rgb = lerp(col.rgb, _GroundNadirColor.rgb * 0.50, horizonTrench);

                    // 2. 地面深邃重力沉降：越接近天底 (-Y)，大地沉稳感越强，与轻盈明亮的天穹形成强烈的自然直觉对比
                    float nadirDepth = pow(-p.y, 1.45) * 0.22;
                    col.rgb = lerp(col.rgb, _GroundNadirColor.rgb * 0.60, nadirDepth);

                    // GPWS / 近地大下沉率防撞动态斑马纹 (Ground Terrain Hazard Pull-Up Stripes)
                    if (_GroundHazardAlert > 0.01)
                    {
                        float stripe = sin((p.x * 18.0 + p.y * 28.0 + p.z * 18.0) + _Time.y * 5.5);
                        float stripeAA = max(fwidth(stripe) * 1.5, 0.06);
                        float isStripe = smoothstep(-stripeAA, stripeAA, stripe - 0.10) * _GroundHazardAlert;
                        fixed3 hazardCol = fixed3(1.0, 0.82, 0.05); // 琥珀黄警戒色
                        col.rgb = lerp(col.rgb, hazardCol, isStripe * 0.55);
                    }
                }

                // 参考系纹理签名：让模式切换有可见的几何差异，而不只依赖颜色。
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

                // 收敛高饱和度，让多参考系颜色更接近 Principia 的哑光质感。
                float baseLuma = dot(col.rgb, float3(0.299, 0.587, 0.114));
                col.rgb = lerp(float3(baseLuma, baseLuma, baseLuma), col.rgb, 0.84);

                // 2. 赤道分割基准线 (Pure Crisp Horizon Line)
                float eqAA = fwidth(p.y) * 1.5;
                float isEquator = 1.0 - smoothstep(_EquatorWidth, _EquatorWidth + eqAA, absY);
                col = lerp(col, _EquatorColor, isEquator);

                // 极点渐隐防聚集保护 (Polar Ring-Bunching Protection)
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

                // 动态字形滚转正向对齐与切向反畸变展开
                float tangentAspect = lerp(1.0, clamp(1.0 / max(NdotV, 0.58), 1.0, 1.28), _NumeralTangentComp);
                float numRoll = -_NumeralRollAngle * _NumeralUprightMode;
                float cosNR = cos(numRoll);
                float sinNR = sin(numRoll);

                // 包围盒裁剪：俯仰数字仅在 ±4.8° 经度范围内且属于 15°~75° 梯级才执行 SDF 计算
                float pitchGlyphDistance = 100.0;
                float pitchGlyphEnabled = 0.0;
                float pitchLabelGap = 0.0;

                if (absPitch >= 14.0 && absPitch <= 76.0)
                {
                    pitchGlyphEnabled = polarLadderFade * smoothstep(0.12, 0.42, NdotV) * smoothstep(0.08, 0.34, _DetailScale) * markerClearance;

                    if (absHOffset < 4.8 && absLabelOffset < 3.0)
                    {
                        float pitchTens = floor(pitchLabelLevel / 10.0);
                        float pitchOnes = fmod(pitchLabelLevel, 10.0);

                        float2 pitchCenterOffset = float2(pitchHeadingOffset * tangentAspect, pitchLabelOffset);
                        float2 rotPitchOffset = float2(
                            pitchCenterOffset.x * cosNR - pitchCenterOffset.y * sinNR,
                            pitchCenterOffset.x * sinNR + pitchCenterOffset.y * cosNR
                        );

                        float pitchRadialSq = pitchHeadingOffset * pitchHeadingOffset + pitchLabelOffset * pitchLabelOffset;
                        pitchLabelGap = (pitchRadialSq < 26.0) ? pitchGlyphEnabled : 0.0;

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
                if (absHOffset <= 10.5 && pitchLabelLevel >= 14.0 && pitchLabelLevel <= 76.0 && absLabelOffset < 2.0)
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
                        float tick10AA = max(fwidth(pMod10) * 1.3, 0.04);
                        float tick10HAA = max(fwidth(absHOffset) * 1.3, 0.08);
                        isTick10 = (1.0 - smoothstep(0.26 - tick10AA, 0.26 + tick10AA, pMod10)) * (1.0 - smoothstep(5.5 - tick10HAA, 5.5 + tick10HAA, absHOffset)) * 0.72 * smoothstep(0.0, 0.32, _DetailScale);
                        if (pitchDeg < 0.0)
                        {
                            float dash10 = fmod(absHOffset, 2.0);
                            float dash10AA = max(fwidth(dash10) * 1.4, 0.08);
                            isTick10 *= (1.0 - smoothstep(1.10 - dash10AA, 1.10 + dash10AA, dash10));
                        }
                    }
                }

                // 中间 5° 梯级短杠 (横跨 ±3.2°)
                float isTick5 = 0.0;
                if (absHOffset <= 3.8 && absPitch > 2.0 && absPitch < 80.0)
                {
                    float pMod5 = abs(pitchDeg - round(pitchDeg / 5.0) * 5.0);
                    float pLevel5 = round(absPitch / 5.0) * 5.0;
                    bool isPure5 = (fmod(pLevel5, 10.0) > 2.0);
                    if (isPure5 && pMod5 < 0.5)
                    {
                        float tick5AA = max(fwidth(pMod5) * 1.3, 0.04);
                        float tick5HAA = max(fwidth(absHOffset) * 1.3, 0.08);
                        isTick5 = (1.0 - smoothstep(0.20 - tick5AA, 0.20 + tick5AA, pMod5)) * (1.0 - smoothstep(3.2 - tick5HAA, 3.2 + tick5HAA, absHOffset)) * 0.50 * smoothstep(0.22, 0.62, _DetailScale);
                        if (pitchDeg < 0.0)
                        {
                            float dash5 = fmod(absHOffset, 1.6);
                            float dash5AA = max(fwidth(dash5) * 1.4, 0.08);
                            isTick5 *= (1.0 - smoothstep(0.85 - dash5AA, 0.85 + dash5AA, dash5));
                        }
                    }
                }

                float combinedLadder = max(majorLadder15, max(isTick10, isTick5)) * polarLadderFade;

                // 2.5° 游标微调刻度 (Vernier Scale)
                if (_VernierScaleDetail > 0.01 && absPitch < 8.0 && absHOffset <= 2.5)
                {
                    float pMod25 = abs(pitchDeg - round(pitchDeg / 2.5) * 2.5);
                    float pLevel25 = round(absPitch / 2.5) * 2.5;
                    bool isPure25 = (fmod(pLevel25, 5.0) > 1.0);
                    if (isPure25 && pMod25 < 0.4)
                    {
                        float tick25AA = max(fwidth(pMod25) * 1.2, 0.035);
                        float tick25HAA = max(fwidth(absHOffset) * 1.2, 0.06);
                        float isTick25 = (1.0 - smoothstep(0.18 - tick25AA, 0.18 + tick25AA, pMod25)) * (1.0 - smoothstep(2.0 - tick25HAA, 2.0 + tick25HAA, absHOffset)) * 0.65;
                        combinedLadder = max(combinedLadder, isTick25 * _VernierScaleDetail * smoothstep(0.35, 0.75, _DetailScale));
                    }
                }

                col = lerp(col, _PitchLadderColor, saturate(combinedLadder * _PitchLadderColor.a * 0.92));

                // 4. 经度子午线：每 30° 一道经线，90° 主方向加亮
                float headMod90 = abs(fmod(headDeg + 405.0, 90.0) - 45.0);
                if (absHOffset <= 1.0 || headMod90 <= 1.0)
                {
                    float m30AA = max(fwidth(absHOffset) * 1.3, 0.05);
                    float isMeridian30 = (1.0 - smoothstep(0.24 - m30AA, 0.24 + m30AA, absHOffset)) * 0.38;
                    float m90AA = max(fwidth(headMod90) * 1.3, 0.05);
                    float isMeridian90 = (1.0 - smoothstep(0.34 - m90AA, 0.34 + m90AA, headMod90)) * 0.65;
                    float isMeridian = max(isMeridian30, isMeridian90) * polarLadderFade;

                    float headingRadialSq = pitchHeadingOffset * pitchHeadingOffset + (pitchDeg - 3.8) * (pitchDeg - 3.8);
                    float headingNumberMask = (headingRadialSq < 42.0) ? (1.0 - smoothstep(5.5, 7.5, absPitch)) : 0.0;
                    isMeridian *= (1.0 - max(pitchLabelGap, headingNumberMask));
                    col = lerp(col, _HeadingLineColor, saturate(isMeridian * _HeadingLineColor.a));
                }

                // 5. 赤道航向刻度线 (Equator Minor Ticks)
                if (absPitch < 1.8)
                {
                    float m30AA = max(fwidth(absHOffset) * 1.3, 0.05);
                    float eqPitchAA = max(fwidth(absPitch) * 1.3, 0.05);
                    float eqPitchMask30 = 1.0 - smoothstep(1.7 - eqPitchAA, 1.7 + eqPitchAA, absPitch);
                    float eqPitchMask10 = 1.0 - smoothstep(1.0 - eqPitchAA, 1.0 + eqPitchAA, absPitch);
                    float eqTick30 = (1.0 - smoothstep(0.32 - m30AA, 0.32 + m30AA, absHOffset)) * 0.90 * eqPitchMask30;
                    float eq10Mod = abs(pitchHeadingOffset - round(pitchHeadingOffset / 10.0) * 10.0);
                    float eq10AA = max(fwidth(eq10Mod) * 1.3, 0.05);
                    float eqTick10 = (1.0 - smoothstep(0.24 - eq10AA, 0.24 + eq10AA, eq10Mod)) * 0.60 * eqPitchMask10;
                    col = lerp(col, _EquatorColor, saturate(max(eqTick30, eqTick10)));
                }

                // 6. 高精度七段数码管排版渲染 (俯仰数字 + 赤道航向数字)
                float glyphAA = clamp(max(fwidth(pitchHeadingOffset), fwidth(pitchLabelOffset)), 0.08, 0.32);
                float pitchTextOutline = (1.0 - smoothstep(-glyphAA, glyphAA, pitchGlyphDistance - 0.45)) * pitchGlyphEnabled;
                float pitchTextFill = (1.0 - smoothstep(-glyphAA, glyphAA, pitchGlyphDistance)) * pitchGlyphEnabled;

                float headingGlyphDistance = 100.0;
                float headingTextEnabled = 0.0;
                float headingGlyphAA = 0.15;

                if (absPitch < 7.5 && abs(pitchDeg - 3.8) < 3.2)
                {
                    float headingCenter = floor(headDeg / 30.0 + 0.5) * 30.0;
                    float headingOffset = headDeg - headingCenter;
                    if (headingOffset > 180.0) headingOffset -= 360.0;
                    if (headingOffset < -180.0) headingOffset += 360.0;

                    if (abs(headingOffset) < 7.5)
                    {
                        float headingNumber = fmod(headingCenter + 360.0, 360.0);
                        float headingHundreds = floor(headingNumber / 100.0);
                        float headingTens = floor(fmod(headingNumber, 100.0) / 10.0);
                        float headingOnes = fmod(headingNumber, 10.0);

                        float2 headCenterOffset = float2(headingOffset * tangentAspect, pitchDeg - 3.8);
                        float2 rotHeadOffset = float2(
                            headCenterOffset.x * cosNR - headCenterOffset.y * sinNR,
                            headCenterOffset.x * sinNR + headCenterOffset.y * cosNR
                        );

                        bool isOddHeading = (fmod(headingNumber, 60.0) > 10.0);
                        float oddHeadingFade = isOddHeading ? smoothstep(0.36, 0.58, _DetailScale) : 1.0;
                        headingTextEnabled = (1.0 - smoothstep(5.5, 7.5, absPitch)) * smoothstep(0.12, 0.42, NdotV) * markerClearance * oddHeadingFade;

                        if (headingTextEnabled > 0.001)
                        {
                            headingGlyphDistance = min(
                                DigitDistance(rotHeadOffset + float2(3.8, 0.0), headingHundreds),
                                min(DigitDistance(rotHeadOffset, headingTens),
                                    DigitDistance(rotHeadOffset - float2(3.8, 0.0), headingOnes)));
                        }
                        headingGlyphAA = clamp(max(fwidth(headingOffset), fwidth(pitchDeg)), 0.08, 0.32);
                    }
                }

                float headingTextOutline = (1.0 - smoothstep(-headingGlyphAA, headingGlyphAA, headingGlyphDistance - 0.45)) * headingTextEnabled;
                float headingTextFill = (1.0 - smoothstep(-headingGlyphAA, headingGlyphAA, headingGlyphDistance)) * headingTextEnabled;

                float textOutline = max(pitchTextOutline, headingTextOutline);
                float textFill = max(pitchTextFill, headingTextFill);
                fixed4 labelCol = (_LabelColor.a > 0.01) ? _LabelColor : fixed4(0.96, 0.98, 1.0, 1.0);
                fixed4 outlineCol = (_LabelOutlineColor.a > 0.01) ? _LabelOutlineColor : fixed4(0.02, 0.03, 0.05, 0.92);
                col.rgb = lerp(col.rgb, outlineCol.rgb, saturate(textOutline * outlineCol.a));
                col.rgb = lerp(col.rgb, labelCol.rgb, saturate(textFill * labelCol.a));

                // 7. 天顶与天底专属极点标
                if (absY > 0.965)
                {
                    float poleR = sqrt(p.x * p.x + p.z * p.z);
                    float poleAA = max(fwidth(poleR) * 1.4, 0.003);
                    float armX = (abs(p.x) < 0.0032 && abs(p.z) < 0.075) ? 1.0 : 0.0;
                    float armZ = (abs(p.z) < 0.0032 && abs(p.x) < 0.075) ? 1.0 : 0.0;
                    float crossArm = max(armX, armZ);

                    if (p.y > 0.0)
                    {
                        float haloDist = abs(poleR - 0.042);
                        float zenithHalo = 1.0 - smoothstep(0.0028 - poleAA, 0.0028 + poleAA, haloDist);
                        float isZenith = max(crossArm * 0.50, zenithHalo * 0.70);
                        col.rgb = lerp(col.rgb, _PitchLadderColor.rgb, isZenith * _PitchLadderColor.a);
                    }
                    else
                    {
                        float nadirDot = 1.0 - smoothstep(0.016 - poleAA, 0.016 + poleAA, poleR);
                        float isNadir = max(crossArm * 0.65, nadirDot * 0.85);
                        col.rgb = lerp(col.rgb, _EquatorColor.rgb, isNadir * _EquatorColor.a);
                    }
                }

                return col;
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.localPos = v.vertex.xyz; // 本地单位球坐标
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.viewPos = UnityObjectToViewPos(v.vertex).xy;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 p = normalize(i.localPos);
                float3 normal = normalize(i.worldNormal);
                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - i.worldPos);
                float NdotV = saturate(dot(normal, viewDir));

                float pitchDeg = asin(clamp(p.y, -1.0, 1.0)) * 57.2957795;
                float headDeg = atan2(p.x, -p.z) * 57.2957795;
                if (headDeg < 0.0) headDeg += 360.0;

                float absY = abs(p.y);
                float absPitch = abs(pitchDeg);

                float markerClearance = 1.0;
                if (_MarkerAvoid0.w > 0.01 || _MarkerAvoid1.w > 0.01 || _MarkerAvoid2.w > 0.01 || _MarkerAvoid3.w > 0.01)
                {
                    float2 screenPoint = i.viewPos;
                    markerClearance = min(MarkerClearance(screenPoint, _MarkerAvoid0), MarkerClearance(screenPoint, _MarkerAvoid1));
                    markerClearance = min(markerClearance, min(MarkerClearance(screenPoint, _MarkerAvoid2), MarkerClearance(screenPoint, _MarkerAvoid3)));
                }

                fixed4 col = EvaluateNavballSurface(p, pitchDeg, headDeg, absPitch, absY, NdotV, markerClearance);

                // 姿态趋势预测动态地平线
                if (_TrendStrength > 0.01)
                {
                    float3 futureP = normalize(RotateByQuaternion(p, float4(-_TrendRotation.xyz, _TrendRotation.w)));
                    float trendAA = max(fwidth(futureP.y) * 2.2, 0.008);
                    float futureHorizon = 1.0 - smoothstep(0.003, 0.003 + trendAA, abs(futureP.y));
                    float dashVal = frac(headDeg / 24.0);
                    float dashAA = max(fwidth(dashVal) * 1.5, 0.04);
                    float trendDash = smoothstep(0.24 - dashAA, 0.24 + dashAA, dashVal);
                    float trendOpacity = futureHorizon * trendDash * _TrendStrength * _HeadingLineColor.a * 0.78;
                    col.rgb = lerp(col.rgb, _HeadingLineColor.rgb, trendOpacity);
                }

                // 3D 球面深度边缘衰减 (Limb Darkening)
                float limbFalloff = pow(NdotV, _LimbPower);
                float limbShade = lerp(1.0 - _LimbIntensity * 0.45, 1.0, limbFalloff);
                col.rgb *= limbShade;

                // 边缘航电微光 (Atmospheric Rim Glow)
                float rim = pow(1.0 - NdotV, _RimPower);
                col.rgb += _RimColor.rgb * rim * _RimIntensity * 0.20;

                // 仪表盘曲面玻璃高光 (Curved Cockpit Glass Lens Reflection)
                float3 lightDir = normalize(float3(-0.35, 0.6, 0.7));
                float3 halfDir = normalize(lightDir + viewDir);
                float specAngle = saturate(dot(normal, halfDir));
                float spec = pow(specAngle, _Glossiness) * _SpecIntensity;
                col.rgb += _SpecularColor.rgb * spec * 0.20;

                // 亚像素轮廓抗锯齿 (Subpixel Silhouette Limb Anti-Aliasing)
                float edgeAA = max(fwidth(NdotV) * 1.5, 0.005);
                col.a = saturate(NdotV / edgeAA);
                return col;
            }
            ENDCG
        }
    }
    FallBack "Unlit/Texture"
}
