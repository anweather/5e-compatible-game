// Adapted from Punchy by Zachary Franklin (https://github.com/Skeleman/Punchy),
// used with permission. Unchanged but for this header.
Shader "Punchy/Sky"
{
    // Procedural skybox: gradient, sun, moon and stars, cirrus, volumetric clouds, then rain and dust haze, all
    // driven by WeatherSystem globals and world weather maps.
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Sky.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.direction = input.positionOS.xyz;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Distant sandstorms and rain curtains show against the sky too.
                float3 dir = normalize(input.direction);
                half3 sky = PunchySkyColor(dir, true, false);
                sky = PunchyVolumetricClouds(sky, dir, input.positionCS.xy);
                // (Same step count as the terrain haze target, so distant land and sky converge exactly.)
                return half4(PunchyWeatherVolumes(sky, _WorldSpaceCameraPos, dir, 8000, 8), 1);
            }
            ENDHLSL
        }
    }
}
