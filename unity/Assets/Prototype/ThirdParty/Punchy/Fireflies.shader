// Adapted from Punchy by Zachary Franklin (https://github.com/Skeleman/Punchy),
// used with permission. Unchanged but for this header.
Shader "Punchy/Fireflies"
{
    // Glowing specks drifting lazily around their home spots, each flashing on its own rhythm: a quick brightening,
    // a slower fade, then dark for a few seconds. Additive and HDR, so bloom gives them a halo.
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Fireflies"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Critters.hlsl"

            static const float2 Corner[6] = { float2(-1, -1), float2(1, -1), float2(1, 1), float2(-1, -1), float2(1, 1), float2(-1, 1) };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half3 glow : TEXCOORD1;
            };

            Varyings Vert(uint vertex : SV_VertexID, uint instance : SV_InstanceID)
            {
                Critter c = _CritterBuffer[instance];
                float r = c.random;
                float t = _Time.y + r * 137.0;

                // Meandering drift: a few incommensurate swirls around home, between knee and head height.
                float3 p = c.position;
                p.x += sin(t * 0.31 + r * 6.0) * 1.8 + sin(t * 0.83 + r * 17.0) * 0.5;
                p.z += cos(t * 0.27 + r * 11.0) * 1.8 + cos(t * 0.71 + r * 3.0) * 0.5;
                p.y += 0.5 + 1.2 * (0.5 + 0.5 * sin(t * 0.23 + r * 9.0)) + sin(t * 1.3) * 0.1;

                // Flash: rise in ~0.15 s, fade over ~0.6 s, then dark until the next one (every 2.5-6 s).
                float period = 2.5 + CritterHash(r * 7.3) * 3.5;
                float phase = frac(t / period) * period;
                float flash = smoothstep(0.0, 0.15, phase) * (1 - smoothstep(0.15, 0.75, phase));
                float presence = CritterPresence(r, _PunchyCritters.x) * CritterDistanceFade(p);

                float3 right = UNITY_MATRIX_V[0].xyz, up = UNITY_MATRIX_V[1].xyz;
                float size = 0.22 * step(0.001, flash * presence);
                float2 corner = Corner[vertex];
                Varyings o;
                o.positionCS = TransformWorldToHClip(p + (right * corner.x + up * corner.y) * size);
                o.uv = corner;
                o.glow = c.color.rgb * flash * presence * 6.0;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float d2 = dot(input.uv, input.uv);
                half intensity = exp(-d2 * 40.0) * 3.0 + exp(-d2 * 7.0) * 0.25;
                return half4(input.glow * intensity, 0);
            }
            ENDHLSL
        }
    }
}
