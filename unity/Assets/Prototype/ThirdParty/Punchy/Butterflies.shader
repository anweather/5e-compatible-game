// Adapted from Punchy by Zachary Franklin (https://github.com/Skeleman/Punchy),
// used with permission. Punchy's sky (cloud shadows, atmosphere) swapped for URP's fog.
Shader "Punchy/Butterflies"
{
    // Butterflies fluttering in loose, bobbing loops around their home spots. Two procedurally shaped wings per
    // butterfly, hinged on the body and flapping; coloured wings with darker veins and margins.
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Cull Off

        Pass
        {
            Name "Butterflies"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Critters.hlsl"

            // Per wing: a quad from the body (x = 0) out to the tip (x = 1), front (z = 1) to back (z = -1).
            static const float2 WingCorner[6] = { float2(0, -1), float2(1, -1), float2(1, 1), float2(0, -1), float2(1, 1), float2(0, 1) };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half4 color : TEXCOORD3;
                half fogFactor : TEXCOORD4;
            };

            Varyings Vert(uint vertex : SV_VertexID, uint instance : SV_InstanceID)
            {
                Critter c = _CritterBuffer[instance];
                float r = c.random;
                float t = _Time.y * (0.8 + 0.3 * r) + r * 91.0;
                float turn = r > 0.5 ? 1 : -1;

                // Loose loops around home, with a fluttery bob; facing along the path.
                float angle = t * 0.45 * turn + r * 6.28;
                float radius = 2.2 + sin(t * 0.37 + r * 4.0) * 1.2;
                float3 p = c.position;
                p.xz += float2(cos(angle), sin(angle)) * radius + float2(sin(t * 1.9), cos(t * 2.3)) * 0.15;
                p.y += 0.95 + 0.4 * sin(t * 0.9 + r * 5.0) + 0.12 * sin(t * 6.1);
                float2 heading = normalize(float2(-sin(angle), cos(angle)) * turn + float2(cos(t * 1.7), sin(t * 2.1)) * 0.25);
                float3 forward = float3(heading.x, 0.15 * cos(t * 0.9), heading.y);
                float3 side = normalize(float3(heading.y, 0, -heading.x));
                float3 up = cross(forward, side);

                // Wings flap between nearly closed above the back and slightly below level.
                float flap = sin(_Time.y * (13.0 + 5.0 * r) + r * 30.0);
                float lift = lerp(-0.25, 1.35, 0.5 + 0.5 * flap);
                uint wing = vertex / 6;
                float s = wing == 0 ? 1 : -1;
                float2 corner = WingCorner[vertex % 6];
                float span = 0.09 * (0.8 + 0.4 * CritterHash(r * 3.1));
                float3 spanDir = side * s * cos(lift) + up * sin(lift);
                float presence = CritterPresence(r, _PunchyCritters.y) * CritterDistanceFade(c.position);
                float3 local = spanDir * corner.x * span + forward * corner.y * span * 0.8;

                Varyings o;
                o.positionWS = p + local * presence;
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = corner;
                o.normalWS = normalize(cross(spanDir, forward)) * s;
                o.color = half4(c.color.rgb, r);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Wing outline: a big forewing and a smaller, rounder hindwing.
                float2 uv = input.uv;
                float fore = length((uv - float2(0.55, 0.38)) / float2(0.48, 0.58));
                float hind = length((uv - float2(0.38, -0.45)) / float2(0.38, 0.5));
                float shape = min(fore, hind);
                clip(1 - shape);

                half3 albedo = input.color.rgb;
                // Dark margin, dark veins radiating from the body, a small pale spot near the tip.
                half margin = smoothstep(0.72, 0.92, shape);
                half veins = smoothstep(0.85, 0.97, abs(sin(atan2(uv.y, uv.x + 0.05) * 7.0)));
                albedo = lerp(albedo, half3(0.05, 0.04, 0.03), saturate(margin + veins * 0.6));
                albedo = lerp(albedo, half3(0.95, 0.93, 0.85), (1 - smoothstep(0.0, 0.08, length(uv - float2(0.8, 0.55)))) * 0.8);
                albedo = lerp(albedo, half3(0.06, 0.05, 0.04), 1 - smoothstep(0.02, 0.08, uv.x));   // body

                half3 n = normalize(input.normalWS);
                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half shadow = light.shadowAttenuation;
                half3 color = albedo * (light.color * shadow * (abs(dot(n, light.direction)) * 0.7 + 0.3) + SampleSH(n));
                return half4(MixFog(color, input.fogFactor), 1);
            }
            ENDHLSL
        }
    }
}
