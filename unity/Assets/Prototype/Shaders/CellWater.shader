Shader "Prototype/CellWater"
{
    // The water on the cells (CellWaterMesh: the river, ponds, puddles, a runoff down a hillside),
    // seen to flow (Andrew's playtest #32). Each vertex carries the water's flow there (uv1, world
    // x and z in m/s) and its foam (uv2.x); the vertex colour's alpha is the outline's fade. The
    // ripples are noise carried along the flow, two layers half a cycle apart cross-faded so the
    // pattern never stretches (a flow map); foam is streaks carried the same way, where the mesh
    // asks for it (a bank of moving water, round a rock). Lit like Prototype/Ocean: the sky's
    // reflection at grazing angles and the sun's glint. No textures of its own; the cells' effects
    // (CellFxTexture, a global texel a cell) add a lightning flash over charged water and an oil
    // slick's dark rainbow sheen where oil floats.
    Properties
    {
        _Color ("Water (alpha: how clear)", Color) = (0.12, 0.32, 0.42, 0.8)
        _Foam ("Foam", Color) = (0.93, 0.96, 0.97, 1)
        _RippleMetres ("Ripple size (m)", Float) = 1.1
        _Cycle ("Flow cycle (s)", Float) = 1.6
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
            half4 _Color, _Foam;
            float _RippleMetres, _Cycle;
            CBUFFER_END

            // The cells' effects (CellFxTexture): r flash, g slick. Global, set by ChemGrid; unset (no
            // grid yet) _CellFxSize is 0 and nothing shows.
            TEXTURE2D(_CellFxTex);
            SAMPLER(sampler_CellFxTex);
            float4 _CellFxMap, _CellFxSize;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 flow : TEXCOORD1; // m/s, world x and z
                float2 foam : TEXCOORD2; // x: foam, 0 to 1
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 flowFoam : TEXCOORD2; // flow x, z; foam; the outline's alpha
                half fogFactor : TEXCOORD3;
            };

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }

            // The ripples' slope at a point (in ripple units): two octaves of noise.
            float2 RippleSlope(float2 q)
            {
                const float e = 0.08;
                float n = Noise(q) + 0.5 * Noise(q * 2.1 + 7.3);
                float nx = Noise(q + float2(e, 0)) + 0.5 * Noise((q + float2(e, 0)) * 2.1 + 7.3);
                float nz = Noise(q + float2(0, e)) + 0.5 * Noise((q + float2(0, e)) * 2.1 + 7.3);
                return float2(nx - n, nz - n) / e;
            }

            half4 CellFx(float3 p)
            {
                if (_CellFxSize.x <= 0.0) return 0;
                float2 cell = float2(p.x * _CellFxMap.x + _CellFxMap.y, _CellFxMap.z - p.z * _CellFxMap.x);
                return SAMPLE_TEXTURE2D(_CellFxTex, sampler_CellFxTex, (cell + 0.5) * _CellFxSize.zw);
            }

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.flowFoam = float4(input.flow, input.foam.x, input.color.a);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 p = input.positionWS;
                float2 flow = input.flowFoam.xy;
                float speed = length(flow);
                float t = _Time.y;
                // A flow map: two layers carried downstream, each restarting every cycle, half a cycle
                // apart, each faded out as it restarts. Still water drifts its ripples slowly instead.
                float2 q = p.xz / _RippleMetres;
                float2 drift = float2(0.05, 0.03) * t;
                float phase0 = frac(t / _Cycle), phase1 = frac(t / _Cycle + 0.5);
                float2 carry = flow * _Cycle / _RippleMetres;
                float2 s0 = RippleSlope(q - carry * phase0 + drift);
                float2 s1 = RippleSlope(q - carry * phase1 + drift + 0.37);
                float w0 = 1.0 - abs(2.0 * phase0 - 1.0);
                float2 slope = (s0 * w0 + s1 * (1.0 - w0)) * (0.05 + 0.05 * saturate(speed));
                half3 n = normalize(input.normalWS + float3(-slope.x, 0, -slope.y));

                float3 v = GetWorldSpaceNormalizeViewDir(p);
                Light light = GetMainLight(TransformWorldToShadowCoord(p));
                half3 sun = light.color * (0.35 + 0.65 * light.shadowAttenuation);
                half3 color = _Color.rgb * (SampleSH(n) * 0.8 + sun * saturate(dot(n, light.direction)) * 0.35);
                half fresnel = 0.03 + 0.97 * pow(1.0 - saturate(dot(n, v)), 5.0);
                half3 sky = GlossyEnvironmentReflection(reflect(-v, n), 0.1, 1.0);
                color = lerp(color, sky, fresnel * 0.6);
                float3 h = normalize(light.direction + v);
                color += sun * pow(saturate(dot(n, h)), 300.0) * 2.0;

                // Oil afloat: near black, with a thin film's rainbow drifting in it, glossier than water.
                half4 fx = CellFx(p);
                float film = Noise(p.xz * 0.6 + drift * 3.0) + 0.5 * Noise(p.xz * 1.7 - drift * 2.0);
                half3 rainbow = 0.5 + 0.5 * cos(6.2831 * (film + fresnel * 1.5 + float3(0.0, 0.33, 0.67)));
                half3 slick = half3(0.02, 0.02, 0.025) + rainbow * 0.05 + sky * fresnel * 0.5 + sun * pow(saturate(dot(n, h)), 90.0) * 0.8;
                color = lerp(color, slick, fx.g * 0.9);
                // A lightning flash: a pale blue glow over the charged water, crackling as it fades.
                float crackle = pow(1.0 - abs(2.0 * Noise(p.xz * 1.1 + t * 37.0) - 1.0), 14.0) * smoothstep(0.3, 0.6, Noise(p.xz * 0.5 - t * 23.0));
                color += half3(0.5, 0.75, 1.0) * fx.r * (0.18 + 3.0 * crackle);

                // Foam: streaks drawn out along the flow and carried with it, the same two layers.
                float2 along = speed > 0.01 ? flow / speed : float2(1, 0);
                float2 across = float2(-along.y, along.x);
                float2 f = float2(dot(p.xz, along) * 0.9, dot(p.xz, across) * 3.2);
                float c0 = Noise(f - float2(speed * _Cycle * phase0 * 0.9, 0)) * Noise(f * 1.9 + 3.1 - float2(speed * _Cycle * phase0 * 1.7, 0));
                float c1 = Noise(f + 11.0 - float2(speed * _Cycle * phase1 * 0.9, 0)) * Noise(f * 1.9 + 7.7 - float2(speed * _Cycle * phase1 * 1.7, 0));
                float streak = c0 * w0 + c1 * (1.0 - w0);
                float foam = saturate(input.flowFoam.z * 1.2) * smoothstep(0.2, 0.42, streak + input.flowFoam.z * 0.2);
                color = lerp(color, _Foam.rgb * (SampleSH(half3(0, 1, 0)) + sun * 0.6), foam);

                half alpha = input.flowFoam.w * lerp(_Color.a, 1.0, max(max(fresnel * 0.5, foam), max(fx.g * 0.85, fx.r * 0.7)));
                color = MixFog(color, input.fogFactor);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
