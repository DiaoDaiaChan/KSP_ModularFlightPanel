Shader "ModularFlightPanel/NavballProcedural"
{
    Properties
    {
        _MainTex ("Reference Texture (Optional)", 2D) = "gray" {}
        
        // 天空配色 (Aero Sky Gradient)
        _SkyZenithColor ("Sky Zenith (Deep Space Blue)", Color) = (0.04, 0.18, 0.38, 1.0)
        _SkyHorizonColor ("Sky Horizon (Aero Cyan)", Color) = (0.08, 0.52, 0.78, 1.0)
        
        // 地面配色 (Ground Slate Gradient)
        _GroundHorizonColor ("Ground Horizon (Charcoal Earth)", Color) = (0.24, 0.18, 0.15, 1.0)
        _GroundNadirColor ("Ground Nadir (Deep Nadir Black)", Color) = (0.08, 0.07, 0.06, 1.0)
        
        // 地平线与赤道线 (Equator Line)
        _EquatorColor ("Equator Line Color", Color) = (1.0, 1.0, 1.0, 1.0)
        _EquatorWidth ("Equator Width", Range(0.001, 0.02)) = 0.005
        
        // 俯仰阶梯线 (Pitch Ladder)
        _PitchLadderColor ("Pitch Ladder Color", Color) = (1.0, 1.0, 1.0, 0.95)
        _PitchLadderWidth ("Pitch Ladder Thickness", Range(0.001, 0.02)) = 0.004
        
        // 经度刻度线 (Heading Lines & Ticks)
        _HeadingLineColor ("Heading Line Color", Color) = (0.3, 0.85, 1.0, 0.8)
        
        // 3D 深度与边缘微光 (Limb Darkening & Rim Glow)
        _LimbPower ("Limb Darkening Power", Range(0.5, 4.0)) = 1.35
        _LimbIntensity ("Limb Darkening Intensity", Range(0.0, 1.0)) = 0.30
        _RimColor ("Rim Glow Color", Color) = (0.2, 0.8, 1.0, 1.0)
        _RimPower ("Rim Power", Range(1.0, 8.0)) = 3.2
        _RimIntensity ("Rim Intensity", Range(0.0, 2.0)) = 0.35
        
        // 玻璃反光 (Glass Reflection)
        _SpecularColor ("Glass Specular Color", Color) = (1.0, 1.0, 1.0, 0.4)
        _Glossiness ("Glossiness", Range(4.0, 64.0)) = 28.0
        _SpecIntensity ("Specular Intensity", Range(0.0, 1.0)) = 0.16
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
                float2 uv : TEXCOORD3;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            fixed4 _SkyZenithColor;
            fixed4 _SkyHorizonColor;
            fixed4 _GroundHorizonColor;
            fixed4 _GroundNadirColor;

            fixed4 _EquatorColor;
            float _EquatorWidth;

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
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
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

                // 2. 天空与地面平滑梯度 (Aero Horizon Gradient)
                float pNorm = p.y;
                fixed4 col;
                if (pNorm >= 0.0)
                {
                    col = lerp(_SkyHorizonColor, _SkyZenithColor, pow(pNorm, 0.75));
                }
                else
                {
                    col = lerp(_GroundHorizonColor, _GroundNadirColor, pow(-pNorm, 0.75));
                }

                // 3. 赤道分割基准线 (Pure Horizon Dividing Line)
                float absY = abs(p.y);
                float eqAA = fwidth(p.y) * 1.5;
                float isEquator = 1.0 - smoothstep(_EquatorWidth, _EquatorWidth + eqAA, absY);
                col = lerp(col, _EquatorColor, isEquator);

                // 4. 俯仰梯级线 (Pitch Ladder: 全球面连续主刻度 10°，次刻度 5°)
                float pitchMod10 = abs(fmod(pitchDeg + 360.0 + 5.0, 10.0) - 5.0);
                float pitchMod5  = abs(fmod(pitchDeg + 360.0 + 2.5, 5.0) - 2.5);

                float pLadderAA = fwidth(pitchDeg) * 1.2;
                float isPitchLine = 0.0;

                if (abs(pitchDeg) > 1.8 && abs(pitchDeg) < 88.5)
                {
                    // 10° 主纬度线：天空实线，地面虚线 (全球面贯通，无死角视野)
                    float dash10 = (pitchDeg < 0.0) ? step(0.35, frac(headDeg / 6.0)) : 1.0;
                    float line10 = (1.0 - smoothstep(_PitchLadderWidth * 60.0, _PitchLadderWidth * 60.0 + pLadderAA * 2.0, pitchMod10)) * dash10;

                    // 5° 次纬度线：精细刻度
                    float dash5 = (pitchDeg < 0.0) ? step(0.40, frac(headDeg / 4.0)) : 1.0;
                    float line5 = (1.0 - smoothstep(_PitchLadderWidth * 35.0, _PitchLadderWidth * 35.0 + pLadderAA * 1.8, pitchMod5)) * dash5 * 0.55;

                    isPitchLine = max(line10, line5);
                }
                col = lerp(col, _PitchLadderColor, saturate(isPitchLine));

                // 5. 航向方位基准经线 (Meridians)
                // 90° 主子午线 (0° 北, 90° 东, 180° 南, 270° 西)
                float headMod90 = abs(fmod(headDeg + 360.0 + 45.0, 90.0) - 45.0);
                // 45° 次子午线
                float headMod45 = abs(fmod(headDeg + 360.0 + 22.5, 45.0) - 22.5);
                // 10° 刻度齿
                float headMod10 = abs(fmod(headDeg + 360.0 + 5.0, 10.0) - 5.0);

                float hAA = fwidth(headDeg) * 1.2;

                // 90° 主经线 (贯通南北)
                float isMeridian90 = (1.0 - smoothstep(0.0, hAA * 2.5, headMod90)) * 0.75;
                // 45° 次经线
                float isMeridian45 = (1.0 - smoothstep(0.0, hAA * 2.0, headMod45)) * 0.40;
                float isMeridian = max(isMeridian90, isMeridian45) * step(abs(pitchDeg), 82.0);
                col = lerp(col, _HeadingLineColor, saturate(isMeridian));

                // 赤道附近的航向刻度齿 (Equatorial Ticks)
                if (absY < 0.035)
                {
                    float isTick10 = 1.0 - smoothstep(0.0, hAA * 2.0, headMod10);
                    col = lerp(col, _EquatorColor, saturate(isTick10 * 0.9));
                }

                // 6. 天顶 (+90°) 与天底 (-90°) 极点同心几何标记
                if (p.y > 0.985)
                {
                    float rTop = length(p.xz);
                    float isZenithRing = 1.0 - smoothstep(0.004, 0.015, abs(rTop - 0.08));
                    col = lerp(col, _PitchLadderColor, isZenithRing);
                }
                else if (p.y < -0.985)
                {
                    float rBot = length(p.xz);
                    float isNadirRing = 1.0 - smoothstep(0.004, 0.015, abs(rBot - 0.08));
                    col = lerp(col, _GroundHorizonColor * 1.8, isNadirRing);
                }

                // 6.5. 融合官方贴图角度数字刻度与航向文字 (Pitch Degree Digits & Compass Text)
                fixed4 markTex = tex2D(_MainTex, i.uv);
                float markLuma = dot(markTex.rgb, float3(0.299, 0.587, 0.114));
                float maxC = max(markTex.r, max(markTex.g, markTex.b));
                float minC = min(markTex.r, min(markTex.g, markTex.b));
                // 提取灰度反差明显的数字和文字笔画 (避免提取底色)
                float isDigit = smoothstep(0.68, 0.95, markLuma) * step(maxC - minC, 0.28);
                float isDarkOutline = (1.0 - smoothstep(0.04, 0.22, markLuma)) * step(maxC - minC, 0.28);
                col.rgb = lerp(col.rgb, float3(0.02, 0.04, 0.06), isDarkOutline * 0.72);
                col.rgb = lerp(col.rgb, float3(0.98, 0.98, 1.0), isDigit * 0.92);

                // 7. 3D 球面深度边缘衰减 (Limb Darkening)
                float NdotV = saturate(dot(normal, viewDir));
                float limbFalloff = pow(NdotV, _LimbPower);
                float limbShade = lerp(1.0 - _LimbIntensity, 1.0, limbFalloff);
                col.rgb *= limbShade;

                // 8. 边缘高科技航电微光 (Atmospheric Rim Glow)
                float rim = pow(1.0 - NdotV, _RimPower);
                col.rgb += _RimColor.rgb * rim * _RimIntensity;

                // 9. 仪表盘曲面玻璃高光 (Curved Cockpit Lens Reflection)
                float3 lightDir = normalize(float3(-0.35, 0.6, 0.7));
                float3 halfDir = normalize(lightDir + viewDir);
                float specAngle = saturate(dot(normal, halfDir));
                float spec = pow(specAngle, _Glossiness) * _SpecIntensity;
                col.rgb += _SpecularColor.rgb * spec;

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
