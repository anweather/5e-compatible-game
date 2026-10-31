Shader "Prototype/Flame"
{
    // A burning cell's flames (FlameBank): one quad an instance, drawn with no mesh from the flames'
    // buffer, turned about its upright to the camera and leaning partway toward it, so a fire still
    // reads from high up. Each plays a flipbook of real flame (the Particle Pack's tiny, medium or large
    // fire sheet, by the fire's level) on its own clock. Each time round it starts a little elsewhere,
    // at a little another size and perhaps mirrored, as a new particle would, so nothing keeps time or
    // stays stamped in place. A sheet below zero marks embers: a spark climbing and fading, then again.
    Properties
    {
        [NoScaleOffset] _SheetTiny ("Tiny flames (11 x 11)", 2D) = "black" {}
        [NoScaleOffset] _SheetMedium ("Medium flames (10 x 10)", 2D) = "black" {}
        [NoScaleOffset] _SheetLarge ("Large flames (8 x 8)", 2D) = "black" {}
    }
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

            struct Flame
            {
                float3 at;
                float seed;
                float wide, tall, speed, born;
                float4 tint;
                float gone, sheet, wander, pad;
            };

            StructuredBuffer<Flame> _Flames;
            float _FlameTime;
            TEXTURE2D(_SheetTiny);
            TEXTURE2D(_SheetMedium);
            TEXTURE2D(_SheetLarge);
            SAMPLER(sampler_SheetTiny);

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 tint : TEXCOORD1;
                nointerpolation float sheet : TEXCOORD2;
                nointerpolation float frame : TEXCOORD3;
                float fade : TEXCOORD4;
                half fogFactor : TEXCOORD5;
            };

            float Hash(float a, float b)
            {
                return frac(sin(a * 127.1 + b * 311.7) * 43758.5453);
            }

            // Frames across a sheet, and how many times it plays in one go round.
            float Tiles(float sheet) { return sheet < 0.5 ? 11.0 : sheet < 1.5 ? 10.0 : 8.0; }
            float Plays(float sheet) { return sheet < 1.5 ? 1.0 : 2.5; }

            Varyings Vert(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
            {
                static const float2 Corners[6] =
                {
                    float2(-1, 0), float2(1, 0), float2(1, 1), float2(-1, 0), float2(1, 1), float2(-1, 1),
                };
                Flame f = _Flames[instanceID];
                float2 corner = Corners[vertexID];
                float clock = _FlameTime * f.speed + f.seed * 61.0;
                float round = floor(clock), life = clock - round;
                float fade = saturate((_FlameTime - f.born) * 2.5);
                if (f.gone > 0.0) fade *= saturate(1.0 - (_FlameTime - f.gone) * 1.7);

                float3 up = normalize(float3(0, 1, 0) + UNITY_MATRIX_V[1].xyz * 0.6);
                float3 side = normalize(cross(up, _WorldSpaceCameraPos - f.at));
                float3 at = f.at;
                float wide = f.wide, tall = f.tall;
                float frame = 0.0, mirror = 1.0;
                if (f.sheet < 0.0)
                {
                    // Embers: a spark on its way up, wandering, drawn out along its climb.
                    float rise = frac(clock);
                    at += up * rise * f.tall + side * sin(clock * 5.0 + f.seed * 40.0) * 0.3 * rise;
                    at += cross(up, side) * cos(clock * 3.7) * 0.3 * rise;
                    tall = wide * 5.0;
                    corner.y -= 0.5;
                    fade *= 1.0 - rise;
                }
                else
                {
                    // Each go round: a new spot about its place, a new size, perhaps mirrored.
                    float spread = f.wander;
                    at.x += (Hash(round, f.seed) - 0.5) * spread;
                    at.z += (Hash(round + 7.0, f.seed) - 0.5) * spread;
                    float size = lerp(0.82, 1.2, Hash(round + 13.0, f.seed));
                    wide *= size;
                    tall *= size;
                    mirror = Hash(round + 19.0, f.seed) < 0.5 ? -1.0 : 1.0;
                    float tiles = Tiles(f.sheet);
                    frame = floor(frac(life * Plays(f.sheet)) * tiles * tiles);
                    // The large sheet's flames loop, so they rise out of nothing and die back; the others' frames do it themselves.
                    if (f.sheet > 1.5) fade *= saturate(life * 8.0) * saturate((1.0 - life) * 4.5);
                }
                float3 world = at + side * corner.x * wide + up * corner.y * tall;

                Varyings o;
                o.positionCS = TransformWorldToHClip(world);
                o.uv = float2(corner.x * mirror * 0.5 + 0.5, Corners[vertexID].y);
                o.tint = f.tint;
                o.sheet = f.sheet;
                o.frame = frame;
                o.fade = fade;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                if (input.sheet < 0.0)
                {
                    float2 p = input.uv * 2.0 - 1.0;
                    float spark = saturate(1.0 - length(float2(p.x, input.uv.y * 2.0 - 1.0)));
                    float glow = spark * spark * input.fade;
                    return half4(half3(1.0, 0.42, 0.08) * glow * 3.0, glow);
                }
                float tiles = Tiles(input.sheet);
                float row = floor(input.frame / tiles), column = input.frame - row * tiles;
                float2 uv = (float2(column, tiles - 1.0 - row) + input.uv * 0.992 + 0.004) / tiles;
                float2 dx = ddx(input.uv) / tiles, dy = ddy(input.uv) / tiles;
                half4 c;
                if (input.sheet < 0.5) c = SAMPLE_TEXTURE2D_GRAD(_SheetTiny, sampler_SheetTiny, uv, dx, dy);
                else if (input.sheet < 1.5) c = SAMPLE_TEXTURE2D_GRAD(_SheetMedium, sampler_SheetTiny, uv, dx, dy);
                else c = SAMPLE_TEXTURE2D_GRAD(_SheetLarge, sampler_SheetTiny, uv, dx, dy);
                // The frames run off their bottom edge; the pack's soft particles hid that where they met the ground.
                float alpha = c.a * input.tint.a * input.fade * smoothstep(0.0, 0.16, input.uv.y);
                // As the pack's materials: half again as bright as the sheet, which is what blooms.
                half3 color = c.rgb * input.tint.rgb * 1.5;
                return half4(MixFog(color * alpha, input.fogFactor), alpha);
            }
            ENDHLSL
        }
    }
}
