Shader "Prototype/FarLand"
{
    // The far ring (FarRing, docs/design/world-squares.md section 4): the known chunks' coarse land, a hills map's far land
    // out to the horizon (FarRing.FarLand.cs, drawn instanced at each copy of the torus), the sea plane to the horizon and
    // the far landmarks' stand-in meshes. The colour is in the vertices (linear), lit softly by the main light and the
    // ambient, no shadows. Everything takes the one air (Air.hlsl, docs/design/hills.md "One air"): toward the sky's
    // horizon on its bearing, by its distance from the eye. The far land pales toward the sky's colour past 3 km (_Dulls,
    // DulledFar) and isn't drawn nearer than _NearClip, where the known meshes and the chunk terrains are the land.
    // A far landmark (_OwnHaze 1) on a map with no far land past its known ring (the nine, the world maps) still sees
    // through the air past sight, about 90% at its seenFrom (LandmarkSeen: there's no land drawn under that air, so the
    // air there is as thin as the landmark asks; it goes when those maps draw far land), and fades wholly into the air
    // over its last _FadeMetres. Its horizon.json "far" look comes in past _FarStretchFrom (where it can no longer have
    // given way to the land): it stands _Stretch times as tall and its rim dips _Notch of its height where it faces the
    // eye, a crater's notch. The sea plane (_SkyFadeTo above 0) is drawn after the sky and goes clear between
    // _SkyFadeFrom and _SkyFadeTo, so it gives way to the sky as it's drawn there, not with a hard line against it. Over
    // the map itself (vertex alpha 1) it's also clear within _FarKnownMetres, the known ring, where the map's own land and
    // water draw, and there past it, where nothing else is: there the sky below the horizon, paler than the haze, showed
    // through as flat white wedges.
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
        _NearClip ("Not drawn nearer than (m), 0 for everywhere", Float) = 0
        _Dulls ("Pales toward the sky far off, 0..1", Float) = 0
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
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Air.hlsl"

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
            float _NearClip;
            half _Dulls;
            CBUFFER_END

            // Set by FarRing each frame: where the generated ring ends (m) and where the known ring ends (m, the view dial's).
            float _FarSightMetres;
            float _FarKnownMetres;
            float _FarStretchFrom;
            float _FarStretchTo;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 color : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
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
                return o;
            }

            // How much of a far landmark is left at distance d on a map with no far land (see the top): within sight the
            // one air; past it, over the next two chunks (192 m), the air in front of it thins to its own, exponential-
            // squared reaching _HazeAtSeen at _SeenMetres in clear weather and thickened as the air's gaussian is by rain
            // and fog; and none of it over the last _FadeMetres.
            half LandmarkSeen(float d)
            {
                float root = sqrt(-log(max(1e-4, 1.0 - _HazeAtSeen)));
                float own = d * root / max(1.0, _SeenMetres) * max(1.0, _Air.y / max(1e-9, _AirFar.z));
                float x = lerp(d * _Air.y, own, smoothstep(_FarSightMetres, _FarSightMetres + 192.0, d));
                half fade = saturate((d - (_SeenMetres - _FadeMetres)) / max(1.0, _FadeMetres));
                return (half)(exp(-x * x) * exp(-d * _Air.x)) * (1.0 - fade);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 n = normalize(input.normalWS);
                Light light = GetMainLight();
                half diffuse = saturate(dot(n, light.direction) * 0.75 + 0.25);
                half3 albedo = input.color.rgb;
                half3 color = albedo * (light.color * diffuse + SampleSH(n));
                UNITY_BRANCH if (_NearClip > 0)
                {
                    // A far mesh of land the chunk terrains and known meshes draw nearer (the hills' far land).
                    if (length(input.positionWS.xz - _WorldSpaceCameraPos.xz) < _NearClip) discard;
                }
                float3 toward = input.positionWS - _WorldSpaceCameraPos;
                UNITY_BRANCH if (_OwnHaze > 0.5)
                {
                    // Far off, a landmark reads as a silhouette: the sun only shades it a little.
                    color = albedo * (light.color * diffuse * 0.35 + SampleSH(n));
                    return half4(lerp(AirToward(toward), color, LandmarkSeen(length(toward))), 1);
                }
                UNITY_BRANCH if (_Dulls > 0) color = DulledFar(color, input.positionWS, _Dulls);
                half alpha = 1;
                UNITY_BRANCH if (_SkyFadeTo > 0)
                {
                    float d = length(input.positionWS - _WorldSpaceCameraPos);
                    alpha = 1 - smoothstep(_SkyFadeFrom, _SkyFadeTo, d);
                    if (input.color.a > 0.5) alpha *= smoothstep(_FarKnownMetres * 0.85, _FarKnownMetres, d);
                }
                return half4(Aired(color, input.positionWS), alpha);
            }
            ENDHLSL
        }
    }
}
