Shader "Prototype/Ocean"
{
    // The sea (Ocean.cs): a mesh at sea level whose vertices carry the water's depth there (uv1.x,
    // metres to the sea bed) and how far out to sea they are (uv1.y). Swell rolls in toward the
    // shore (_ShoreDir), growing as the water shoals and breaking into foam over the last few
    // metres, then running up the sand as swash and draining back. Deep water is dark blue, the
    // shallows turquoise and clear over the sand; the sky's reflection comes in at grazing angles
    // and the sun glints. No textures: the ripples and foam are noise worked out here.
    Properties
    {
        _Deep ("Deep", Color) = (0.03, 0.17, 0.32, 1)
        _Shallow ("Shallow", Color) = (0.1, 0.62, 0.66, 1)
        _Foam ("Foam", Color) = (0.95, 0.97, 0.97, 1)
        _ShoreDir ("Toward the shore (xz)", Vector) = (-0.707, 0.707, 0, 0)
        _Swell ("Swell height (m)", Float) = 0.6
        _Wavelength ("Swell wavelength (m)", Float) = 18
        _Speed ("Swell speed (m/s)", Float) = 4.2
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" }

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
            half4 _Deep, _Shallow, _Foam;
            float4 _ShoreDir;
            float _Swell, _Wavelength, _Speed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 water : TEXCOORD1; // depth to the bed (m), distance out to sea (m)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 surf : TEXCOORD2; // depth, the swell's phase, swash, crest
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

            // One Gerstner wave: adds its displacement and its slope (dy/dx, dy/dz).
            void Gerstner(float2 xz, float2 dir, float wavelength, float height, float steep, float speed, float t,
                inout float3 offset, inout float2 slope)
            {
                float k = 6.2831853 / wavelength;
                float f = k * (dot(dir, xz) - speed * t);
                float a = height * 0.5;
                offset.xz += dir * (steep / k) * cos(f) * saturate(a * k * 2.0);
                offset.y += a * sin(f);
                slope += dir * a * k * cos(f);
            }

            Varyings Vert(Attributes input)
            {
                Varyings o;
                float3 p = TransformObjectToWorld(input.positionOS.xyz);
                float depth = input.water.x, out_ = input.water.y;
                float t = _Time.y;
                float2 shore = normalize(_ShoreDir.xy);
                float2 side = float2(-shore.y, shore.x);

                // The swell grows as the sea shoals (to about 1.6 times at 2 m) and is spent by the
                // water's edge; far out it settles to a long gentle roll.
                float shoal = lerp(1.0, 2.4, saturate((5.5 - depth) / 3.5)) * saturate(depth / 0.5 + 0.1);
                float3 offset = 0;
                float2 slope = 0;
                Gerstner(p.xz, shore, _Wavelength, _Swell * shoal, 0.55, _Speed, t, offset, slope);
                Gerstner(p.xz, normalize(shore + side * 0.35), _Wavelength * 0.61, _Swell * 0.45 * shoal, 0.4, _Speed * 0.8, t, offset, slope);
                Gerstner(p.xz, normalize(shore - side * 0.5), _Wavelength * 0.37, _Swell * 0.22, 0.3, _Speed * 0.6, t, offset, slope);
                Gerstner(p.xz, normalize(side + shore * 0.2), _Wavelength * 2.3, _Swell * 0.35 * saturate(out_ / 60.0), 0.2, _Speed * 1.3, t, offset, slope);

                // Swash: after each breaker the water surges up the sand and drains back.
                float k = 6.2831853 / _Wavelength;
                float phase = k * (dot(shore, p.xz) - _Speed * t);
                float pulse = 0.5 + 0.5 * sin(phase - 1.2);
                float swashZone = saturate(1.0 - depth / 1.5);
                offset.y += swashZone * (pulse * 0.32 - 0.08);
                offset.xz += shore * swashZone * pulse * 1.2;

                // Breaking: over the bar the crest heaps up and pitches forward before it spills.
                float crestLift = pow(saturate(0.5 + 0.5 * sin(phase + 0.6)), 4.0);
                float bar = saturate((3.6 - depth) / 1.6) * saturate(depth / 0.6);
                offset.y += bar * crestLift * _Swell * 0.6;
                offset.xz += shore * bar * crestLift * 0.5;

                p += offset;
                o.positionWS = p;
                o.positionCS = TransformWorldToHClip(p);
                o.normalWS = normalize(float3(-slope.x, 1.0, -slope.y));
                // The fragment works out the breakers' foam from where each point is in the swell.
                o.surf = float4(depth, phase, swashZone * pulse, crestLift);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 p = input.positionWS;
                float depth = input.surf.x;
                float t = _Time.y;

                // Ripples: two drifting noise layers bend the normal a little.
                float2 rp = p.xz * 0.35;
                float n1 = Noise(rp + float2(t * 0.21, t * 0.13));
                float n2 = Noise(rp * 2.3 - float2(t * 0.17, -t * 0.27));
                float dx = Noise(rp + float2(0.07, 0) + float2(t * 0.21, t * 0.13)) - n1;
                float dz = Noise(rp + float2(0, 0.07) + float2(t * 0.21, t * 0.13)) - n1;
                half3 n = normalize(input.normalWS + float3(-dx, 0, -dz) * 2.2 + float3(n2 - 0.5, 0, 0.5 - n2) * 0.06);

                float3 v = GetWorldSpaceNormalizeViewDir(p);
                Light light = GetMainLight(TransformWorldToShadowCoord(p));
                half3 sun = light.color * (0.35 + 0.65 * light.shadowAttenuation);

                // Body colour: turquoise over the sand, blue as it deepens.
                half shallow = saturate(1.0 - depth / 7.0);
                half3 body = lerp(_Deep.rgb, _Shallow.rgb, shallow * shallow);
                half3 color = body * (SampleSH(n) * 0.8 + sun * saturate(dot(n, light.direction)) * 0.35);

                // The sky in the water at a glancing angle, and the sun's glint.
                half fresnel = 0.03 + 0.97 * pow(1.0 - saturate(dot(n, v)), 5.0);
                half3 sky = GlossyEnvironmentReflection(reflect(-v, n), 0.08, 1.0);
                color = lerp(color, sky, fresnel * 0.85);
                float3 h = normalize(light.direction + v);
                color += sun * pow(saturate(dot(n, h)), 420.0) * 3.0;

                // Foam: the breakers, the swash's lacy edge, a fringe where the sea meets the sand, and
                // the odd whitecap out at sea.
                float lace = Noise(p.xz * 1.7 + float2(t * 0.6, -t * 0.4)) * Noise(p.xz * 4.1 - t * 0.3);
                float fringe = saturate((0.55 - depth) / 0.55) * (0.55 + 0.9 * lace);
                // Breakers: once the swell reaches the bar (under 3.5 m), each crest is a white line with
                // a band of whitewater rolling in behind it, torn into streaks along the shore.
                float wave = frac((input.surf.y + 0.6 - 1.5707963) / 6.2831853);
                float crestLine = smoothstep(0.9, 1.0, wave) + 1.0 - smoothstep(0.0, 0.04, wave);
                float trail = pow(smoothstep(0.5, 0.97, wave), 1.6);
                float bar = saturate((4.5 - depth) / 3.0) * saturate(depth / 0.2 + 0.15);
                float2 shoreDir = normalize(_ShoreDir.xy);
                float2 streakUV = float2(dot(p.xz, float2(-shoreDir.y, shoreDir.x)) * 0.25, dot(p.xz, shoreDir) * 1.4);
                float tear = 0.45 + 0.9 * Noise(streakUV + float2(t * 0.15, 0)) * (0.6 + 0.6 * lace);
                float breakers = bar * saturate(crestLine * 1.5 + trail * 1.0) * tear;
                float swash = input.surf.z * saturate((1.0 - depth) / 1.0) * (0.4 + lace);
                float caps = pow(input.surf.w, 3.0) * saturate(depth / 10.0 - 0.5) * step(0.8, Noise(p.xz * 0.12 + t * 0.05));
                float foam = saturate(fringe + breakers + swash + caps * 0.7);
                color = lerp(color, _Foam.rgb * (0.75 + 0.35 * sun), foam);

                // Clear over the sand at the edge, opaque in deep water.
                half alpha = saturate(0.35 + depth * 0.22 + fresnel * 0.4);
                alpha = max(alpha, foam);
                alpha *= saturate(depth * 3.0 + 0.45);
                return half4(MixFog(color, input.fogFactor), alpha);
            }
            ENDHLSL
        }
    }
}
