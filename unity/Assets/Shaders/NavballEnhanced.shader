Shader "ModularFlightPanel/NavballEnhanced"
{
    Properties
    {
        _MainTex ("Navball Texture", 2D) = "gray" {}
        _Color ("Color Tint", Color) = (1.0, 1.0, 1.0, 1.0)
        
        // 3D 深度与球面微光 (Spherical Limb Darkening & Depth)
        _LimbPower ("Limb Darkening Power", Range(0.5, 4.0)) = 1.4
        _LimbIntensity ("Limb Darkening Intensity", Range(0.0, 1.0)) = 0.32
        
        // 边缘高科技发光 (Rim Light / Atmospheric Limb Glow)
        _RimColor ("Rim Glow Color", Color) = (0.2, 0.75, 1.0, 1.0)
        _RimPower ("Rim Power", Range(1.0, 8.0)) = 3.2
        _RimIntensity ("Rim Intensity", Range(0.0, 2.0)) = 0.28
        
        // 弧面防眩玻璃反光 (Curved Anti-Reflective Glass Reflection)
        _SpecularColor ("Glass Specular Color", Color) = (1.0, 1.0, 1.0, 0.4)
        _Glossiness ("Glossiness", Range(4.0, 64.0)) = 28.0
        _SpecIntensity ("Specular Intensity", Range(0.0, 1.0)) = 0.16
        
        // 对比度与通透度增强 (Contrast & Dynamic Range)
        _Contrast ("Contrast Boost", Range(0.8, 1.5)) = 1.02
        _Brightness ("Overall Brightness", Range(0.5, 1.5)) = 1.0
        _Saturation ("Saturation Boost", Range(0.5, 1.5)) = 1.05
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
            fixed4 _Color;

            float _LimbPower;
            float _LimbIntensity;

            fixed4 _RimColor;
            float _RimPower;
            float _RimIntensity;

            fixed4 _SpecularColor;
            float _Glossiness;
            float _SpecIntensity;

            float _Contrast;
            float _Brightness;
            float _Saturation;

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

                // 1. 原始官方高保真贴图采样 (完全保留原版天底/天顶、俯仰角数字、刻度线与真北航向标记)
                fixed4 col = tex2D(_MainTex, i.uv) * _Color;

                // 2. 色彩通透度与饱和度微调 (优化原版稍显灰暗平淡的色调)
                float luminance = dot(col.rgb, float3(0.299, 0.587, 0.114));
                col.rgb = lerp(float3(luminance, luminance, luminance), col.rgb, _Saturation);
                // 对比度适度调校
                col.rgb = ((col.rgb - 0.5) * _Contrast) + 0.5;
                col.rgb *= _Brightness;

                // 3. 3D 球面深度边缘衰减 (Limb Darkening)
                float NdotV = saturate(dot(normal, viewDir));
                float limbFalloff = pow(NdotV, _LimbPower);
                float limbShade = lerp(1.0 - _LimbIntensity, 1.0, limbFalloff);
                col.rgb *= limbShade;

                // 4. 边缘高科技航电微光 (Atmospheric Rim Glow)
                float rim = pow(1.0 - NdotV, _RimPower);
                col.rgb += _RimColor.rgb * rim * _RimIntensity;

                // 5. 顶部微弱弧面玻璃反光 (Curved Cockpit Lens Reflection)
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
