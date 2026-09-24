Shader "ModularFlightPanel/Vessel3DTechnical"
{
    Properties
    {
        _Color ("Hull Base Color", Color) = (0.2, 0.45, 0.7, 1.0)
        _RimColor ("Limb Glow / Rim Highlight", Color) = (0.4, 0.85, 1.0, 1.0)
        _AmbientColor ("Ambient Fill Color", Color) = (0.05, 0.1, 0.18, 1.0)
        _LightDir ("Virtual Light Direction", Vector) = (0.5, 0.8, -0.6, 0.0)
        _RimPower ("Rim Exponent", Range(0.5, 8.0)) = 2.5
        _RimIntensity ("Rim Strength", Range(0.0, 3.0)) = 1.2
        _DiffuseWrap ("Half-Lambert Wrap Factor", Range(0.0, 1.0)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100

        Pass
        {
            Name "Forward3D"
            Cull Back
            ZWrite On
            ZTest LEqual
            Blend Off

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
                float3 worldNormal : TEXCOORD0;
                float3 worldViewDir : TEXCOORD1;
            };

            float4 _Color;
            float4 _RimColor;
            float4 _AmbientColor;
            float4 _LightDir;
            float _RimPower;
            float _RimIntensity;
            float _DiffuseWrap;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldViewDir = normalize(_WorldSpaceCameraPos - worldPos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 N = normalize(i.worldNormal);
                float3 V = normalize(i.worldViewDir);
                float3 L = normalize(_LightDir.xyz);

                // Half-Lambert wrap lighting (smooth volumetric shading across rocket hull & tanks)
                float NdotL = dot(N, L);
                float halfLambert = saturate(NdotL * (1.0 - _DiffuseWrap) + _DiffuseWrap);
                float3 diffuse = _Color.rgb * halfLambert;

                // Fresnel / Rim lighting (crisp edge definition for every 3D stage and wing)
                float NdotV = saturate(dot(N, V));
                float rim = pow(1.0 - NdotV, _RimPower) * _RimIntensity;
                float3 rimGlow = _RimColor.rgb * rim;

                float3 finalColor = _AmbientColor.rgb + diffuse + rimGlow;
                return fixed4(finalColor, 1.0);
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}
