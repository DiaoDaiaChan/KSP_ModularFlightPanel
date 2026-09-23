Shader "ModularFlightPanel/NavballProcedural"
{
    Properties
    {
        _MainTex ("Texture (Unused in Procedural)", 2D) = "white" {}
        
        // 天空渐变配色 (Aero Sky Gradient)
        _SkyZenithColor ("Sky Zenith (Deep Space Blue)", Color) = (0.04, 0.16, 0.36, 1.0)
        _SkyHorizonColor ("Sky Horizon (Aero Cyan)", Color) = (0.08, 0.46, 0.72, 1.0)
        
        // 地面渐变配色 (Ground Slate Gradient)
        _GroundHorizonColor ("Ground Horizon (Cockpit Slate)", Color) = (0.18, 0.16, 0.15, 1.0)
        _GroundNadirColor ("Ground Nadir (Deep Charcoal Nadir)", Color) = (0.07, 0.06, 0.06, 1.0)
        
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

        Pass
        {
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
            };

            float SegmentDistance(float2 p, float2 center, float2 halfSize)
            {
                float2 d = abs(p - center) - halfSize;
                return max(d.x, d.y);
            }

            // Compact seven-segment vector glyphs keep numbers independent from the selected mode texture.
            float DigitDistance(float2 p, float digit)
            {
                float d = 100.0;

                if (digit != 1.0 && digit != 4.0)
                    d = min(d, SegmentDistance(p, float2(0.0, 1.86), float2(1.08, 0.16)));
                if (digit != 0.0 && digit != 1.0 && digit != 7.0)
                    d = min(d, SegmentDistance(p, float2(0.0, 0.0), float2(1.08, 0.16)));
                if (digit != 1.0 && digit != 4.0 && digit != 7.0)
                    d = min(d, SegmentDistance(p, float2(0.0, -1.86), float2(1.08, 0.16)));

                if (digit == 0.0 || digit == 4.0 || digit == 5.0 || digit == 6.0 || digit == 8.0 || digit == 9.0)
                    d = min(d, SegmentDistance(p, float2(-0.92, 0.90), float2(0.16, 0.76)));
                if (digit != 5.0 && digit != 6.0)
                    d = min(d, SegmentDistance(p, float2(0.92, 0.90), float2(0.16, 0.76)));
                if (digit == 0.0 || digit == 2.0 || digit == 6.0 || digit == 8.0)
                    d = min(d, SegmentDistance(p, float2(-0.92, -0.90), float2(0.16, 0.76)));
                if (digit != 2.0)
                    d = min(d, SegmentDistance(p, float2(0.92, -0.90), float2(0.16, 0.76)));

                return d;
            }

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

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.localPos = v.vertex.xyz; // 本地单位球坐标
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // 单位化本地球向量 (精准 3D 几何，无 UV 极点畸变)
                float3 p = normalize(i.localPos);
                float3 normal = normalize(i.worldNormal);
                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - i.worldPos);

                // 1. 俯仰角与航向角解算 (度数)
                float pitchDeg = asin(clamp(p.y, -1.0, 1.0)) * 57.2957795; // -90 ~ +90
                float headDeg = atan2(p.x, p.z) * 57.2957795;               // -180 ~ +180
                if (headDeg < 0.0) headDeg += 360.0;

                float absY = abs(p.y);
                float absPitch = abs(pitchDeg);

                // 2. 天空与地面平滑梯度 (Aero Horizon Gradient)
                fixed4 col;
                if (p.y >= 0.0)
                {
                    col = lerp(_SkyHorizonColor, _SkyZenithColor, pow(p.y, 0.88));
                }
                else
                {
                    col = lerp(_GroundHorizonColor, _GroundNadirColor, pow(-p.y, 0.88));
                }

                // 收敛高饱和度，让多参考系颜色更接近 Principia 的哑光质感。
                float baseLuma = dot(col.rgb, float3(0.299, 0.587, 0.114));
                col.rgb = lerp(float3(baseLuma, baseLuma, baseLuma), col.rgb, 0.84);

                // 3. 赤道分割基准线 (Pure Crisp Horizon Line)
                float eqAA = fwidth(p.y) * 1.5;
                float isEquator = 1.0 - smoothstep(_EquatorWidth, _EquatorWidth + eqAA, absY);
                col = lerp(col, _EquatorColor, isEquator);

                // 极点渐隐防聚集保护 (Polar Ring-Bunching Protection):
                // 俯仰角超过 70° 时平滑淡出纬度线，彻底杜绝极点同心靶蜘蛛网
                float polarLadderFade = 1.0 - smoothstep(68.0, 78.0, absPitch);

                // 4. 俯仰梯级：每 15° 一道细线，30° 线稍强，负俯仰侧采用短虚线。
                float pMod15 = abs(fmod(pitchDeg + 367.5, 15.0) - 7.5);
                float pMod30 = abs(fmod(pitchDeg + 375.0, 30.0) - 15.0);
                float pLadderAA = max(fwidth(pitchDeg) * 1.15, 0.025);
                float line15 = 1.0 - smoothstep(_PitchLadderWidth * 34.0, _PitchLadderWidth * 34.0 + pLadderAA, pMod15);
                float line30 = 1.0 - smoothstep(_PitchLadderWidth * 34.0, _PitchLadderWidth * 34.0 + pLadderAA, pMod30);
                float majorPitch = lerp(0.48, 0.78, line30);

                float pitchHeadingCenter = floor(headDeg / 45.0 + 0.5) * 45.0;
                float pitchHeadingOffset = headDeg - pitchHeadingCenter;
                float pitchLabelLevel = round(absPitch / 15.0) * 15.0;
                float pitchLabelCenter = (pitchDeg < 0.0 ? -pitchLabelLevel : pitchLabelLevel);
                float pitchLabelOffset = pitchDeg - pitchLabelCenter;
                float pitchTens = floor(pitchLabelLevel / 10.0);
                float pitchOnes = fmod(pitchLabelLevel, 10.0);
                float pitchGlyphDistance = min(
                    DigitDistance(float2(pitchHeadingOffset + 1.42, pitchLabelOffset), pitchTens),
                    DigitDistance(float2(pitchHeadingOffset - 1.42, pitchLabelOffset), pitchOnes));
                float pitchGlyphEnabled = step(14.0, pitchLabelLevel) * (1.0 - step(61.0, pitchLabelLevel)) * polarLadderFade;
                float pitchLabelGap = (abs(pitchHeadingOffset) < 3.2 && abs(pitchLabelOffset) < 2.5) ? pitchGlyphEnabled : 0.0;

                float negativeDash = 1.0 - smoothstep(0.70, 0.74, frac((headDeg + 22.5) / 45.0));
                float isPitchLine = line15 * majorPitch * polarLadderFade;
                isPitchLine *= smoothstep(3.0, 6.0, absPitch);
                isPitchLine *= (pitchDeg < 0.0 ? negativeDash : 1.0) * (1.0 - pitchLabelGap);
                float pitchLineOpacity = 0.35 + 0.45 * _PitchLadderColor.a;
                col = lerp(col, _PitchLadderColor, saturate(isPitchLine * pitchLineOpacity));

                // 5. 经线每 45° 一道，主方向 90° 线略强。
                float headMod45 = abs(fmod(headDeg + 382.5, 45.0) - 22.5);
                float headMod90 = abs(fmod(headDeg + 405.0, 90.0) - 45.0);
                float hAA = max(fwidth(headDeg), 0.025);
                float isMeridian45 = 1.0 - smoothstep(0.0, hAA * 1.5, headMod45);
                float isMeridian90 = 1.0 - smoothstep(0.0, hAA * 1.7, headMod90);
                float isMeridian = max(isMeridian45 * 0.24, isMeridian90 * 0.42) * polarLadderFade;
                col = lerp(col, _HeadingLineColor, saturate(isMeridian * _HeadingLineColor.a));

                // 6. 赤道航向刻度与角度数字。
                if (absY < 0.024)
                {
                    float isEqTick45 = (1.0 - smoothstep(0.0, hAA * 1.8, headMod45)) * 0.48;
                    col = lerp(col, _EquatorColor, saturate(isEqTick45));
                }

                // 七段数字：俯仰 15° 至 60° 标注，以及赤道 45° 航向标注。
                float glyphAA = max(max(fwidth(pitchHeadingOffset), fwidth(pitchLabelOffset)), 0.05);
                float pitchTextOutline = (1.0 - smoothstep(-glyphAA, glyphAA, pitchGlyphDistance - 0.24)) * pitchGlyphEnabled;
                float pitchTextFill = (1.0 - smoothstep(-glyphAA, glyphAA, pitchGlyphDistance)) * pitchGlyphEnabled;

                float headingCenter = floor(headDeg / 45.0 + 0.5) * 45.0;
                float headingOffset = headDeg - headingCenter;
                float headingNumber = fmod(headingCenter + 360.0, 360.0);
                float headingHundreds = floor(headingNumber / 100.0);
                float headingTens = floor(fmod(headingNumber, 100.0) / 10.0);
                float headingOnes = fmod(headingNumber, 10.0);
                float headingTwoDigitDistance = min(
                    DigitDistance(float2(headingOffset + 1.42, pitchDeg - 2.75), headingTens),
                    DigitDistance(float2(headingOffset - 1.42, pitchDeg - 2.75), headingOnes));
                float headingThreeDigitDistance = min(
                    DigitDistance(float2(headingOffset + 2.84, pitchDeg - 2.75), headingHundreds),
                    min(DigitDistance(float2(headingOffset, pitchDeg - 2.75), headingTens),
                        DigitDistance(float2(headingOffset - 2.84, pitchDeg - 2.75), headingOnes)));
                float headingGlyphDistance = (headingNumber < 1.0)
                    ? DigitDistance(float2(headingOffset, pitchDeg - 2.75), headingOnes)
                    : (headingNumber < 100.0 ? headingTwoDigitDistance : headingThreeDigitDistance);
                float headingGlyphAA = max(max(fwidth(headingOffset), fwidth(pitchDeg)), 0.05);
                float headingTextOutline = (1.0 - smoothstep(-headingGlyphAA, headingGlyphAA, headingGlyphDistance - 0.24));
                float headingTextFill = (1.0 - smoothstep(-headingGlyphAA, headingGlyphAA, headingGlyphDistance));
                float headingTextEnabled = 1.0 - smoothstep(5.0, 7.0, abs(pitchDeg));

                float textOutline = max(pitchTextOutline, headingTextOutline * headingTextEnabled);
                float textFill = max(pitchTextFill, headingTextFill * headingTextEnabled);
                col.rgb = lerp(col.rgb, _LabelOutlineColor.rgb, saturate(textOutline * _LabelOutlineColor.a));
                col.rgb = lerp(col.rgb, _LabelColor.rgb, saturate(textFill * _LabelColor.a));

                // 6. 天顶 (+90°) 与天底 (-90°) 极点专属航电十字标 (Celestial Pole Cross)
                // 替代传统密集同心圆，以极简十字标定天顶/天底
                if (absY > 0.970)
                {
                    float armX = (abs(p.x) < 0.0035 && abs(p.z) < 0.08) ? 1.0 : 0.0;
                    float armZ = (abs(p.z) < 0.0035 && abs(p.x) < 0.08) ? 1.0 : 0.0;
                    float isPoleCross = max(armX, armZ) * 0.55;
                    col = lerp(col, (p.y > 0.0 ? _PitchLadderColor : _EquatorColor), isPoleCross);
                }

                // 7. 3D 球面深度边缘衰减 (Limb Darkening - 营造真实球体体积感)
                float NdotV = saturate(dot(normal, viewDir));
                float limbFalloff = pow(NdotV, _LimbPower);
                float limbShade = lerp(1.0 - _LimbIntensity * 0.45, 1.0, limbFalloff);
                col.rgb *= limbShade;

                // 8. 边缘高科技航电微光 (Atmospheric Rim Glow)
                float rim = pow(1.0 - NdotV, _RimPower);
                col.rgb += _RimColor.rgb * rim * _RimIntensity * 0.20;

                // 9. 仪表盘曲面玻璃高光 (Curved Cockpit Glass Lens Reflection)
                float3 lightDir = normalize(float3(-0.35, 0.6, 0.7));
                float3 halfDir = normalize(lightDir + viewDir);
                float specAngle = saturate(dot(normal, halfDir));
                float spec = pow(specAngle, _Glossiness) * _SpecIntensity;
                col.rgb += _SpecularColor.rgb * spec * 0.20;

                // 10. 亚像素硬件多重采样边缘抗锯齿 (Subpixel Silhouette Limb Anti-Aliasing)
                float edgeAA = max(fwidth(NdotV) * 1.5, 0.005);
                col.a = saturate(NdotV / edgeAA);
                return col;
            }
            ENDCG
        }
    }
    FallBack "Unlit/Texture"
}
