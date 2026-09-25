Shader "ModularFlightPanel/MinimalistAttitudeSphere"
{
    Properties
    {
        _SkyColor ("Sky Color", Color) = (0.05, 0.14, 0.28, 0.75)
        _GroundColor ("Ground Color", Color) = (0.12, 0.08, 0.05, 0.75)
        _EquatorColor ("Equator Line Color", Color) = (0.2, 0.85, 1.0, 1.0)
        _EquatorWidth ("Equator Width", Range(0.001, 0.015)) = 0.004
        _PitchLadderColor ("Pitch Bar Color", Color) = (0.8, 0.95, 1.0, 0.6)
        _MeridianColor ("Meridian Line Color", Color) = (0.3, 0.7, 0.9, 0.3)
        _RimColor ("Rim Highlight", Color) = (0.3, 0.8, 1.0, 0.6)
        _RimPower ("Rim Falloff", Range(1.0, 8.0)) = 3.0
    }

    SubShader
    {
        Tags 
        { 
            "Queue" = "Transparent" 
            "RenderType" = "Transparent" 
            "IgnoreProjector" = "True"
        }
        LOD 150
        Cull Back
        ZWrite Off
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
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 localPos : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
            };

            float4 _SkyColor;
            float4 _GroundColor;
            float4 _EquatorColor;
            float _EquatorWidth;
            float4 _PitchLadderColor;
            float4 _MeridianColor;
            float4 _RimColor;
            float _RimPower;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.localPos = v.vertex.xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 p = normalize(i.localPos);

                // 1. 纯净双色穹顶渐变 (无外部素材，低饱和度半透明科技感)
                fixed4 col;
                if (p.y >= 0.0)
                {
                    col = lerp(_SkyColor * 0.75, _SkyColor, pow(p.y, 0.8));
                }
                else
                {
                    col = lerp(_GroundColor * 0.75, _GroundColor, pow(-p.y, 0.8));
                    // 地平线切光对比暗槽
                    float trench = smoothstep(-0.055, -0.001, p.y) * 0.22;
                    col.rgb = lerp(col.rgb, _GroundColor.rgb * 0.40, trench);
                }

                // 2. 纤细激光地平线 (Equator Dividing Line)
                float eqAA = fwidth(p.y) * 1.5;
                float isEquator = 1.0 - smoothstep(_EquatorWidth, _EquatorWidth + eqAA, abs(p.y));
                col = lerp(col, _EquatorColor, isEquator);

                // 3. 极简俯仰阶梯标尺 (0 数字字面量，纯纯现代航电短横杆)
                float pitchDeg = asin(clamp(p.y, -1.0, 1.0)) * 57.2957795;
                float headDeg = atan2(p.x, -p.z) * 57.2957795;
                if (headDeg < 0.0) headDeg += 360.0;

                // 每 90° 一组主轴标尺 (正前、右、后、左)
                float hOffset = fmod(headDeg + 45.0, 90.0) - 45.0;
                float absH = abs(hOffset);

                float absPitch = abs(pitchDeg);
                float ladderFade = 1.0 - smoothstep(65.0, 75.0, absPitch);

                // 30° 主梯级 (±30°, ±60°): 宽横杆
                float pMod30 = abs(pitchDeg - round(pitchDeg / 30.0) * 30.0);
                float isMajorRung = (absPitch >= 25.0 && pMod30 < 0.35 && absH < 7.0) ? 1.0 : 0.0;

                // 10° 次梯级 (±10°, ±20°, ±40°, ±50°): 短横杠
                float pMod10 = abs(pitchDeg - round(pitchDeg / 10.0) * 10.0);
                float isMinorRung = (absPitch >= 8.0 && absPitch < 70.0 && pMod30 >= 5.0 && pMod10 < 0.25 && absH < 3.5) ? 0.75 : 0.0;

                float rung = max(isMajorRung, isMinorRung) * ladderFade;
                // 地面侧（负俯仰）使用现代虚线短划 (Dashed)
                if (pitchDeg < 0.0 && rung > 0.0)
                {
                    if (fmod(absH, 2.0) > 1.1) rung = 0.0;
                }

                col = lerp(col, _PitchLadderColor, saturate(rung * _PitchLadderColor.a));

                // 4. 四向主子午线微光 (Subtle Cardinal Meridians)
                if (absH < 0.20 && absPitch < 72.0)
                {
                    col = lerp(col, _MeridianColor, saturate(_MeridianColor.a * 0.75 * ladderFade));
                }

                // 5. 3D 半透明球体边缘 Fresnel 辉光 (定义立体球形空间轮廓)
                float3 V = normalize(UnityWorldSpaceViewDir(mul(unity_ObjectToWorld, float4(i.localPos, 1.0)).xyz));
                float NdotV = saturate(dot(normalize(i.worldNormal), V));
                float rim = pow(1.0 - NdotV, _RimPower);
                col.rgb += _RimColor.rgb * rim * _RimColor.a;
                col.a = saturate(col.a + rim * 0.4);

                return col;
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}
