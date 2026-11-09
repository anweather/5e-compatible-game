Shader "Prototype/FarLand"
{
    // The far ring (FarRing, docs/design/world-squares.md section 4): the known chunks' coarse land, the sea plane to the
    // horizon and the far landmarks' stand-in meshes. The colour is in the vertices, lit softly by the main light and the
    // ambient, no shadows. The land and the sea take URP's fog (the haze, exponential-squared on a generated map). A far
    // landmark (_OwnHaze 1) takes its own haze instead, the land's within sight and thinner past it (about 90% at its
    // seenFrom) so it shows through the fog, and fades into the sky's colour where it's seen (the skybox's gradient, Punchy's Sky.hlsl),
    // not the fog's, so it never stands out against a bluer sky; over the last _FadeMetres it goes wholly into the sky.
    // Its horizon.json "far" look comes in past _FarStretchFrom (where it can no longer have given way to the land): it
    // stands _Stretch times as tall and its rim dips _Notch of its height where it faces the eye, a crater's notch.
    // The sea plane (_SkyFadeTo above 0) is drawn after the sky and goes clear between _SkyFadeFrom and _SkyFadeTo, so it
    // gives way to the sky as it's drawn there (its sun, cirrus and clouds near the horizon are brighter than the haze),
    // not with a hard line against it. Over the map itself (vertex alpha 1) it's also clear within _SkyClearNear (m), the
    // known ring, where the map's own land and water draw, and there past it, where nothing else is: there the sky below
    // the horizon, paler than the haze, showed through as flat white wedges.
    Properties
    {
        _OwnHaze ("Own haze (a far landmark)", Float) = 0
        _SeenMetres ("Seen from (m)", Float) = 1344
        _HazeAtSeen ("Haze at seen from", Float) = 0.9
        _FadeMetres ("Fade into the sky over (m)", Float) = 144
        _Stretch ("Far stretch (x its height)", Float) = 1
        _Notch ("Rim notch (of its height)", Float) = 0
        _Top ("Its height (m)", Float) = 1
        _SkyFadeFrom ("Into the sky from (m)", Float) = 0
        _SkyFadeTo ("Into the sky by (m), 0 for never", Float) = 0
        _SkyClearNear ("Over the map, clear within (m)", Float) = 0
        [HideInInspector] _SrcBlend ("", Float) = 1
        [HideInInspector] _DstBlend ("", Float) = 0
        [HideInInspector] _ZWrite ("", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
            half _OwnHaze;
            float _SeenMetres;
            half _HazeAtSeen;
            float _FadeMetres;
            float _Stretch;
            float _Notch;
            float _Top;
            float _SkyFadeFrom;
            float _SkyFadeTo;
            float _SkyClearNear;
            CBUFFER_END

            // Set by FarRing each frame: the land's haze density now and in clear weather (per metre), where the generated
            // ring ends (m), and the sky's colours.
            float _FarHazeDensity;
            float _FarHazeClear;
            float _FarSightMetres;
            half3 _FarSkyZenith;
            half3 _FarSkyHorizon;
            float _FarStretchFrom;
            float _FarStretchTo;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 color : TEXCOORD2;
                half fogFactor : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                float3 p = input.positionOS.xyz;
                float3 n = input.normalOS;
                UNITY_BRANCH if (_OwnHaze > 0.5 && p.y > 0.0)
                {
                    // A far landmark's far look, none where it gives way to the land. Its mesh isn't turned, so object
                    // x and z are the world's.
                    float2 toEye = _WorldSpaceCameraPos.xz - GetObjectToWorldMatrix()._m03_m23;
                    float d = length(toEye);
                    float t = smoothstep(_FarStretchFrom, _FarStretchTo, d);
                    float r = length(p.xz);
                    float facing = r > 1e-3 ? dot(p.xz / r, toEye / max(1e-3, d)) : 0.0;
                    float rim = saturate((p.y / max(1e-3, _Top) - 0.84) / 0.16);
                    float stretch = lerp(1.0, _Stretch, t);
                    p.y *= stretch * (1.0 - _Notch * t * facing * facing * rim);
                    n = float3(n.x, n.y / stretch, n.z);
                }
                o.positionWS = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(normalize(n));
                o.color = input.color;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            // How much of a far landmark the haze hides at distance d: its own, exponential-squared reaching _HazeAtSeen at
            // _SeenMetres in clear weather (the paper's), thickened as the land's is by rain and fog; within sight the
            // land's, blended to its own over the next two chunks (192 m), so where it gives way to the chunk terrains
            // it's at their haze; and all of it over the last _FadeMetres.
            half LandmarkHaze(float d, out half fade)
            {
                float land = d * _FarHazeDensity;
                float root = sqrt(-log(max(1e-4, 1.0 - _HazeAtSeen)));
                float own = d * root / max(1.0, _SeenMetres) * max(1.0, _FarHazeDensity / max(1e-6, _FarHazeClear));
                float x = lerp(land, own, smoothstep(_FarSightMetres, _FarSightMetres + 192.0, d));
                half haze = 1.0 - exp(-x * x);
                fade = saturate((d - (_SeenMetres - _FadeMetres)) / max(1.0, _FadeMetres));
                return lerp(haze, 1.0, fade);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 n = normalize(input.normalWS);
                Light light = GetMainLight();
                half diffuse = saturate(dot(n, light.direction) * 0.75 + 0.25);
                half3 albedo = input.color.rgb;
                half3 color = albedo * (light.color * diffuse + SampleSH(n));
                UNITY_BRANCH if (_OwnHaze > 0.5)
                {
                    // Far off, a landmark reads as a silhouette: the sun only shades it a little.
                    color = albedo * (light.color * diffuse * 0.35 + SampleSH(n));
                    float3 toPixel = input.positionWS - _WorldSpaceCameraPos;
                    float d = length(toPixel);
                    half h = toPixel.y / max(1e-3, d);
                    half3 sky = lerp(_FarSkyHorizon, _FarSkyZenith, pow(saturate(h), 0.45));
                    half fade;
                    half haze = LandmarkHaze(d, fade);
                    // The air in front of a far mountain is bluer than the sky at the horizon behind it (the sky's blue
                    // scattered in), so it greys blue, not to the horizon's pale; wholly the sky again as it fades out.
                    half3 air = lerp(sky, _FarSkyZenith, 0.5 * haze * (1.0 - fade));
                    return half4(lerp(color, air, haze), 1);
                }
                half alpha = 1;
                UNITY_BRANCH if (_SkyFadeTo > 0)
                {
                    float d = length(input.positionWS - _WorldSpaceCameraPos);
                    alpha = 1 - smoothstep(_SkyFadeFrom, _SkyFadeTo, d);
                    if (input.color.a > 0.5) alpha *= smoothstep(_SkyClearNear * 0.85, _SkyClearNear, d);
                }
                return half4(MixFog(color, input.fogFactor), alpha);
            }
            ENDHLSL
        }
    }
}
