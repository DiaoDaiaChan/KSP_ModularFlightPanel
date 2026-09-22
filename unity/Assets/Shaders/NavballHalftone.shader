Shader "ModularFlightPanel/NavballHalftone"
{
    Properties
    {
        _MainTex ("Navball Texture", 2D) = "white" {}
        _SkyColor ("Sky Color", Color) = (0.0, 0.88, 0.85, 1.0)
        _GroundColor ("Ground Color", Color) = (0.0, 0.33, 0.50, 1.0)
        _GridColor ("Grid Color", Color) = (0.0, 1.0, 0.8, 0.8)
        _EquatorColor ("Equator Line Color", Color) = (1.0, 0.2, 0.2, 1.0)
        _DotColor ("Halftone Dot Color", Color) = (0.0, 0.1, 0.2, 0.85)
        
        _DotDensity ("Dot Density", Float) = 40.0
        _DotMinRadius ("Dot Min Radius", Range(0.0, 0.5)) = 0.05
        _DotMaxRadius ("Dot Max Radius", Range(0.0, 0.5)) = 0.48
        
        _GridLineWidth ("Grid Line Width", Range(0.001, 0.05)) = 0.008
        _EquatorWidth ("Equator Width", Range(0.001, 0.05)) = 0.015
        
        _RimColor ("Rim Glow Color", Color) = (0.0, 1.0, 0.9, 0.5)
        _RimPower ("Rim Power", Range(0.5, 8.0)) = 2.5
        
        _EmissionIntensity ("Emission Intensity", Range(0.0, 3.0)) = 1.2
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
                float4 screenPos : TEXCOORD3;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _SkyColor;
            fixed4 _GroundColor;
            fixed4 _GridColor;
            fixed4 _EquatorColor;
            fixed4 _DotColor;
            
            float _DotDensity;
            float _DotMinRadius;
            float _DotMaxRadius;
            float _GridLineWidth;
            float _EquatorWidth;
            
            fixed4 _RimColor;
            float _RimPower;
            float _EmissionIntensity;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.screenPos = ComputeScreenPos(o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 normal = normalize(i.worldNormal);
                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - i.worldPos);
                
                // UV坐标系基础颜色
                float pitch = (i.uv.y - 0.5) * 2.0; // -1 (天底) ~ +1 (天顶)
                fixed4 baseColor = (pitch >= 0.0) ? _SkyColor : _GroundColor;
                
                // 采样外部高精贴图（如果绑定了详细纹理，与基础色彩相乘融合）
                fixed4 texSample = tex2D(_MainTex, i.uv);
                baseColor = lerp(baseColor, baseColor * texSample, texSample.a);

                // 赤道线 (Pitch == 0)
                float equatorDist = abs(i.uv.y - 0.5);
                float isEquator = 1.0 - smoothstep(_EquatorWidth * 0.5, _EquatorWidth, equatorDist);
                baseColor = lerp(baseColor, _EquatorColor, isEquator);

                // 经纬度线框 (Latitude & Longitude procedural lines)
                float latGrid = abs(frac(i.uv.y * 18.0) - 0.5) * 2.0; // 每 10 度一条纬线
                float lonGrid = abs(frac(i.uv.x * 36.0) - 0.5) * 2.0; // 每 10 度一条经线
                float isGrid = step(1.0 - _GridLineWidth * 10.0, latGrid) + step(1.0 - _GridLineWidth * 10.0, lonGrid);
                isGrid = saturate(isGrid);
                baseColor = lerp(baseColor, _GridColor, isGrid * 0.6);

                // 半色调网点阴影 (Screen-Space Halftone Dither)
                // 基于屏幕坐标生成周期性网格
                float2 screenUV = i.screenPos.xy / max(i.screenPos.w, 0.0001) * _ScreenParams.xy;
                float2 cellUV = frac(screenUV / max(_DotDensity, 1.0)) - 0.5;
                float distToCenter = length(cellUV);

                // 阴影强度根据观察角度与表面法线夹角渐变 (Fresnel + NdotV)
                float NdotV = saturate(dot(normal, viewDir));
                float shadowFactor = 1.0 - NdotV;
                float targetRadius = lerp(_DotMinRadius, _DotMaxRadius, shadowFactor);

                // 点阵遮罩
                float dotMask = 1.0 - smoothstep(targetRadius - 0.03, targetRadius + 0.03, distToCenter);
                baseColor = lerp(baseColor, _DotColor, dotMask * _DotColor.a * shadowFactor);

                // 边缘发光 (Rim Light / Neon Glow)
                float rim = pow(1.0 - NdotV, _RimPower);
                fixed4 rimGlow = _RimColor * rim;

                fixed4 finalColor = (baseColor + rimGlow) * _EmissionIntensity;
                finalColor.a = 1.0;
                return finalColor;
            }
            ENDCG
        }
    }
    FallBack "Unlit/Texture"
}
