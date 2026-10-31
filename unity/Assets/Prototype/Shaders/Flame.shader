Shader "Prototype/Flame"
{
    // A burning cell's tongues of flame (FlameBank): one quad an instance, drawn with no mesh from the
    // tongues' buffer, turned about its upright to the camera and leaning partway toward it, so a fire
    // still reads from high up. Everything that moves is here: each tongue rises and dies back on its
    // own clock, flickers, sways and tears apart along noise of its own, so no two look alike or keep
    // time. A negative width marks embers: a spark climbing and fading, then starting again.
    Properties { }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Flame"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Tongue
            {
                float3 at;
                float seed;
                float wide, tall, speed, born;
                float3 tint;
                float gone;
            };

            StructuredBuffer<Tongue> _Flames;
            float _FlameTime;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 tint : TEXCOORD1;
                float clock : TEXCOORD2;
                float seed : TEXCOORD3;
                float fade : TEXCOORD4;
                float embers : TEXCOORD5;
                half fogFactor : TEXCOORD6;
            };

            Varyings Vert(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
            {
                static const float2 Corners[6] =
                {
                    float2(-1, 0), float2(1, 0), float2(1, 1), float2(-1, 0), float2(1, 1), float2(-1, 1),
                };
                Tongue f = _Flames[instanceID];
                float2 corner = Corners[vertexID];
                float clock = _FlameTime * f.speed + f.seed * 61.0;
                float fade = saturate((_FlameTime - f.born) * 2.5);
                if (f.gone > 0.0) fade *= saturate(1.0 - (_FlameTime - f.gone) * 1.7);

                float3 up = normalize(float3(0, 1, 0) + UNITY_MATRIX_V[1].xyz * 0.6);
                float3 side = normalize(cross(up, _WorldSpaceCameraPos - f.at));
                float3 at = f.at;
                float wide = f.wide, tall = f.tall;
                if (wide < 0.0)
                {
                    // Embers: a spark on its way up, wandering.
                    float rise = frac(clock * 0.8);
                    at += up * rise * f.tall + side * sin(clock * 5.0 + f.seed * 40.0) * 0.25 * rise;
                    at += cross(up, side) * cos(clock * 3.7) * 0.25 * rise;
                    wide = -wide;
                    tall = wide * 2.0;
                    corner.y -= 0.5;
                    fade *= 1.0 - rise;
                }
                else
                {
                    // Each tongue swells and dies back on its own slow clock, and flickers.
                    float life = frac(clock * 0.23);
                    float swell = lerp(0.5, 1.05, sin(3.14159 * sqrt(life)));
                    tall *= swell * (0.92 + 0.1 * sin(clock * 5.3) + 0.07 * sin(clock * 8.9 + f.seed * 6.0));
                    up = normalize(up + side * (f.seed - 0.5) * 0.45);
                }
                float3 world = at + side * corner.x * wide + up * corner.y * tall;

                Varyings o;
                o.positionCS = TransformWorldToHClip(world);
                o.uv = float2(corner.x, Corners[vertexID].y);
                o.tint = f.tint;
                o.clock = clock;
                o.seed = f.seed;
                o.fade = fade;
                o.embers = f.wide < 0.0 ? 1.0 : 0.0;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float fade = input.fade;
                if (input.embers > 0.5)
                {
                    float spark = saturate(1.0 - length(float2(input.uv.x, input.uv.y * 2.0 - 1.0)));
                    float glow = spark * spark * fade;
                    return half4(half3(1.0, 0.45, 0.12) * glow * 3.0, glow);
                }
                float y = input.uv.y, clock = input.clock, seed = input.seed;
                // The tongue sways along noise, more toward its tip; a seed gives it its own lean and width.
                float sway = Noise(float2(input.uv.x * 1.3 + seed * 9.0, y * 2.4 - clock * 1.7)) - 0.5;
                float x = input.uv.x + sway * 1.2 * y + (seed - 0.5) * 0.3 * y;
                // A slim teardrop, widest a quarter of the way up, drawn in at the ground.
                float width = pow(saturate(1.0 - y), 1.15) * (0.35 + 0.65 * sqrt(saturate(y * 4.0)));
                float body = saturate(1.0 - abs(x) / max(width, 0.001));
                // Torn into licks toward the tip, and at its edges, by faster noise.
                float2 torn = float2(x * 2.8 + seed * 17.0, y * 4.2 - clock * 2.9);
                float tear = 0.62 * Noise(torn) + 0.38 * Noise(torn * 2.4 + 7.0);
                float heat = body * (1.2 - y * 0.75) - tear * (0.3 + 0.7 * y);
                float alpha = smoothstep(0.02, 0.36, heat) * smoothstep(0.0, 0.16, y) * fade;
                half3 color = lerp(half3(0.8, 0.1, 0.01), half3(1.0, 0.36, 0.03), smoothstep(0.08, 0.38, heat));
                color = lerp(color, half3(1.0, 0.68, 0.16), smoothstep(0.42, 0.8, heat));
                color *= input.tint * lerp(0.95, 1.5, saturate(heat));
                return half4(MixFog(color * alpha, input.fogFactor), alpha * 0.9);
            }
            ENDHLSL
        }
    }
}
