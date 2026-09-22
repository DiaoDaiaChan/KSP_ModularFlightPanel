Shader "ModularFlightPanel/NavballProcedural"
{
    Properties
    {
        _MainTex ("Reference Texture (Optional)", 2D) = "white" {}
        
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
        _LimbIntensity ("Limb Darkening Intensity", Range(0.0, 1.0)) = 0.32
        _RimColor ("Rim Glow Color", Color) = (0.2, 0.8, 1.0, 1.0)
        _RimPower ("Rim Power", Range(1.0, 8.0)) = 3.2
        _RimIntensity ("Rim Intensity", Range(0.0, 2.0)) = 0.45
        
        // 玻璃反光 (Glass Reflection)
        _SpecularColor ("Glass Specular Color", Color) = (1.0, 1.0, 1.0, 0.5)
        _Glossiness ("Glossiness", Range(4.0, 64.0)) = 24.0
        _SpecIntensity ("Specular Intensity", Range(0.0, 1.0)) = 0.22
    }

    SubShader
    {
        Tags 
        { 
            "Queue" = "Geometry" 
            "RenderType" = "Opaque" 
            "IgnoreProjector" = "True"
        }
        LOD 200
        Cull Back

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

                // 4. 俯仰梯级线 (Pitch Ladder: 每 10° 主刻度，每 5° 次刻度)
                // 计算当前度数与最近 5°/10° 的距离
                float pitchMod5 = abs(fmod(pitchDeg + 360.0 + 2.5, 5.0) - 2.5);
                float pitchMod10 = abs(fmod(pitchDeg + 360.0 + 5.0, 10.0) - 5.0);

                float pLadderAA = fwidth(pitchDeg) * 1.2;
                bool isNear10 = pitchMod10 < (_PitchLadderWidth * 120.0 + pLadderAA);
                bool isNear5  = pitchMod5  < (_PitchLadderWidth * 80.0 + pLadderAA);

                // 经度水平跨度限制 (梯级线宽度：主刻度跨 14°，次刻度跨 7°)
                float headMod30 = abs(fmod(headDeg + 360.0 + 15.0, 30.0) - 15.0);
                float ladderSpan10 = step(headMod30, 8.0);
                float ladderSpan5  = step(headMod30, 4.5);

                float isPitchLine = 0.0;
                if (abs(pitchDeg) > 2.0 && abs(pitchDeg) < 88.0)
                {
                    if (isNear10 && ladderSpan10 > 0.5)
                    {
                        // 地面虚线刻度，天空实线刻度
                        float dash = (pitchDeg < 0.0) ? step(0.3, frac(headDeg * 1.2)) : 1.0;
                        isPitchLine = (1.0 - smoothstep(0.0, pLadderAA * 2.0, pitchMod10)) * dash;
                    }
                    else if (isNear5 && ladderSpan5 > 0.5)
                    {
                        float dash = (pitchDeg < 0.0) ? step(0.35, frac(headDeg * 1.5)) : 1.0;
                        isPitchLine = (1.0 - smoothstep(0.0, pLadderAA * 2.0, pitchMod5)) * 0.7 * dash;
                    }
                }
                col = lerp(col, _PitchLadderColor, saturate(isPitchLine));

                // 5. 航向方位基准线 (Meridians: 0°, 45°, 90°, 135°, 180°, 225°, 270°, 315°)
                float headMod45 = abs(fmod(headDeg + 360.0 + 22.5, 45.0) - 22.5);
                float headMod10 = abs(fmod(headDeg + 360.0 + 5.0, 10.0) - 5.0);
                float hAA = fwidth(headDeg) * 1.2;

                // 每 45° 的细经线
                float isMeridian45 = (1.0 - smoothstep(0.0, hAA * 2.5, headMod45)) * 0.5;
                col = lerp(col, _HeadingLineColor, saturate(isMeridian45 * step(abs(pitchDeg), 75.0)));

                // 赤道附近的航向刻度齿 (Equatorial Ticks)
                if (absY < 0.035)
                {
                    float isTick10 = 1.0 - smoothstep(0.0, hAA * 2.0, headMod10);
                    col = lerp(col, _EquatorColor, saturate(isTick10 * 0.9));
                }

                // 6. 天顶 (+90°) 与天底 (-90°) 极点几何标记
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

                col.a = 1.0;
                return col;
            }
            ENDCG
        }
    }
    FallBack "Unlit/Texture"
}
