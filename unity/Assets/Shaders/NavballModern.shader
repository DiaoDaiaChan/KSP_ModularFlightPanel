Shader "ModularFlightPanel/NavballModern"
{
    Properties
    {
        _MainTex ("Navball Texture", 2D) = "white" {}
        
        // 现代航空玻璃座舱配色 (Modern Glass Cockpit Palette)
        _SkyZenithColor ("Sky Zenith (Deep Aero Blue)", Color) = (0.05, 0.22, 0.45, 1.0)
        _SkyHorizonColor ("Sky Horizon (Aero Cyan)", Color) = (0.12, 0.58, 0.88, 1.0)
        _GroundHorizonColor ("Ground Horizon (Dark Slate Brown)", Color) = (0.28, 0.22, 0.18, 1.0)
        _GroundNadirColor ("Ground Nadir (Deep Charcoal Ground)", Color) = (0.10, 0.08, 0.07, 1.0)
        
        _HorizonLineColor ("Horizon Dividing Line (Pure White)", Color) = (1.0, 1.0, 1.0, 1.0)
        _HorizonLineWidth ("Horizon Line Width", Range(0.001, 0.02)) = 0.005
        
        _PitchLadderColor ("Pitch Ladder (Crisp White/Cyan)", Color) = (0.9, 0.98, 1.0, 0.9)
        _PitchLadderWidth ("Pitch Ladder Thickness", Range(0.001, 0.02)) = 0.004
        
        _RollPointerColor ("Roll Pointer Indicator Color", Color) = (1.0, 0.85, 0.1, 1.0)
        
        // 极简微弱边缘抗锯齿与发光
        _AtmosphereGlowColor ("Limb Glow Color", Color) = (0.3, 0.75, 1.0, 0.3)
        _AtmosphereGlowPower ("Limb Glow Power", Range(1.0, 10.0)) = 3.5
        
        _Brightness ("Overall Brightness", Range(0.5, 2.5)) = 1.1
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
                float2 uv : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            
            fixed4 _SkyZenithColor;
            fixed4 _SkyHorizonColor;
            fixed4 _GroundHorizonColor;
            fixed4 _GroundNadirColor;
            fixed4 _HorizonLineColor;
            float _HorizonLineWidth;
            
            fixed4 _PitchLadderColor;
            float _PitchLadderWidth;
            fixed4 _RollPointerColor;
            
            fixed4 _AtmosphereGlowColor;
            float _AtmosphereGlowPower;
            float _Brightness;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 normal = normalize(i.worldNormal);
                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - i.worldPos);
                
                // 俯仰角归一化：-1.0 (天底 Nadir) ~ 0.0 (地平线 Horizon) ~ +1.0 (天顶 Zenith)
                float pitch = (i.uv.y - 0.5) * 2.0;

                // 现代航空平滑渐变地平线 (Smooth Glass Cockpit Horizon Gradient)
                fixed4 baseColor;
                if (pitch >= 0.0)
                {
                    // 天空区：从地平线浅青向天顶深蓝平滑过渡
                    float skyT = pow(saturate(pitch), 0.7);
                    baseColor = lerp(_SkyHorizonColor, _SkyZenithColor, skyT);
                }
                else
                {
                    // 地面区：从地平线岩石灰褐向深炭灰平滑过渡
                    float groundT = pow(saturate(-pitch), 0.7);
                    baseColor = lerp(_GroundHorizonColor, _GroundNadirColor, groundT);
                }

                // 极细锐利地平线 (Horizon Reference Line)
                float distToHorizon = abs(i.uv.y - 0.5);
                float isHorizon = 1.0 - smoothstep(0.0, _HorizonLineWidth, distToHorizon);
                baseColor = lerp(baseColor, _HorizonLineColor, isHorizon);

                // 现代俯仰梯级微线 (Modern Precision Pitch Ladder Ticks)
                // 每 5 度细线、每 10 度长线
                float pitchDeg = pitch * 90.0;
                float tickDist = abs(frac(pitchDeg / 5.0 + 0.5) - 0.5);
                float isPitchTick = 1.0 - smoothstep(0.0, _PitchLadderWidth * 2.0, tickDist);

                // 限制俯仰梯只在中央视野 ±30 度航向角范围内展示，避免杂乱
                float lonFromCenter = abs(frac(i.uv.x) - 0.5) * 360.0;
                float pitchLadderMask = 1.0 - smoothstep(18.0, 22.0, lonFromCenter);
                baseColor = lerp(baseColor, _PitchLadderColor, isPitchTick * pitchLadderMask * 0.75);

                // 采样贴图（如果用户提供高清贴图，混合叠加）
                fixed4 texSample = tex2D(_MainTex, i.uv);
                baseColor = lerp(baseColor, baseColor * texSample, texSample.a * 0.4);

                // 现代大气边缘菲涅尔微发光 (Subtle Aerodynamic Fresnel Glow)
                float NdotV = saturate(dot(normal, viewDir));
                float limb = pow(1.0 - NdotV, _AtmosphereGlowPower);
                fixed4 limbGlow = _AtmosphereGlowColor * limb;

                fixed4 finalColor = (baseColor + limbGlow) * _Brightness;
                finalColor.a = 1.0;
                return finalColor;
            }
            ENDCG
        }
    }
    FallBack "Unlit/Texture"
}
