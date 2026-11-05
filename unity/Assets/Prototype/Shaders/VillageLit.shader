Shader "Prototype/VillageLit"
{
    // Generated things built with MeshKit (D12), drawn one by one or instanced (AmbientLife): the colour is in the vertices, alpha is glow (a
    // window lit from inside, brighter the darker the sky: the global _LampGlow, which Village sets
    // from SkyLight). Soft wrapped lighting, main-light shadows and URP's fog, like the creatures.
    // A house between the camera and the wizard is dithered away round it (OccluderDither.hlsl).
    // A vertex marked in its first uv is stone in courses: Coursed lays the blocks from where it stands in the world.
    Properties
    {
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        // 1 for a glow that's carried about (a wizard's staff): steady, the same by day and night, no
        // window flicker (that's phased by world position, so on a swinging staff it pulsed with each step)
        // and no bloom.
        _SteadyGlow ("Steady glow", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
        half4 _Tint;
        half _SteadyGlow;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            half4 color : COLOR;
            float2 mark : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "OccluderDither.hlsl"

            float _LampGlow;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 color : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                half coursed : TEXCOORD4;
            };

            float Hashed(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            // Blocks a course high, of uneven lengths and each its own tone, with thin joints, a lit top edge and a rough face.
            // sharp fades the fine detail as it nears a pixel, so a far wall keeps the weathering and doesn't shimmer. Facing
            // east or west, a wall runs along z.
            half Coursed(float3 at, half3 n, half sharp)
            {
                const float course = 0.75, block = 1.7, joint = 0.035;
                float along = abs(n.x) > abs(n.z) ? at.z : at.x;
                float row = floor(at.y / course);
                float u = along / block + Hashed(float2(row, 3.0));
                float i = floor(u);
                float start = i + (Hashed(float2(i, row)) - 0.5) * 0.3;
                if (u < start) { i -= 1.0; start = i + (Hashed(float2(i, row)) - 0.5) * 0.3; }
                float end = i + 1.0 + (Hashed(float2(i + 1.0, row)) - 0.5) * 0.3;
                float2 stone = float2(i, row);
                float high = frac(at.y / course) * course;
                float edge = min(min(u - start, end - u) * block, min(high, course - high));
                half tone = 1.0 + (Hashed(stone) - 0.5) * 0.42 * sharp;
                half joints = 1.0 - 0.42 * sharp * (1.0 - smoothstep(joint * 0.4, joint * 1.6, edge));
                half lip = 1.0 + 0.1 * sharp * (1.0 - smoothstep(0.0, 0.09, course - high));
                half rough = 1.0 + (Hashed(floor(float2(along, at.y) * 9.0)) - 0.5) * 0.12 * sharp * sharp;
                float2 patch = float2(along, at.y) / float2(9.0, 5.0);
                half weather = 0.86 + 0.28 * lerp(Hashed(floor(patch)), Hashed(floor(patch + 0.5)), 0.5);
                return tone * joints * lip * rough * weather;
            }

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.color = input.color * _Tint;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                o.coursed = input.mark.x;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                OccluderDitherClip(input.positionCS);
                // Guarded: at a needle tip (a crystal staff's) the normal comes to nothing, and normalising it
                // gives a pixel of NaN, which the bloom spreads into a great flare that pops as the tip moves.
                float3 raw = input.normalWS;
                float length2 = dot(raw, raw);
                half3 n = length2 > 1e-8 ? raw / sqrt(length2) : half3(0, 1, 0);
                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half diffuse = saturate(dot(n, light.direction) * 0.8 + 0.2);
                half3 albedo = input.color.rgb;
                half sharp = saturate(1.0 - fwidth(input.positionWS.y) * 6.0);
                UNITY_BRANCH if (input.coursed > 0.5) albedo *= Coursed(input.positionWS, n, sharp);
                half3 color = albedo * (light.color * light.shadowAttenuation * diffuse + SampleSH(n));
                // Lit windows: a dim warmth by day, bright enough to bloom at night, with a slow flicker.
                half lamp = lerp(lerp(0.15, 2.4, saturate(_LampGlow)), 1.2, _SteadyGlow);
                half flicker = lerp(0.9 + 0.1 * sin(_Time.y * 3.1 + input.positionWS.x * 2.0 + input.positionWS.z), 1.0, _SteadyGlow);
                half glow = input.color.a * lamp;
                color += albedo * glow * flicker;
                // A steady glow is one flat colour, unlit and under the bloom's threshold (1): lit, its facets
                // crossed that threshold as it swung.
                color = lerp(color, albedo * 0.95, _SteadyGlow * saturate(input.color.a * 2.0));
                return half4(MixFog(color, input.fogFactor), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            float4 Vert(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 p = TransformObjectToWorld(input.positionOS.xyz);
                float3 n = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirection = normalize(_LightPosition - p);
            #else
                float3 lightDirection = _LightDirection;
            #endif
                return ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(p, n, lightDirection)));
            }

            half4 Frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "OccluderDither.hlsl"

            float4 Vert(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return TransformWorldToHClip(TransformObjectToWorld(input.positionOS.xyz));
            }

            half Frag(float4 positionCS : SV_POSITION) : SV_Target
            {
                OccluderDitherClip(positionCS);
                return positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "OccluderDither.hlsl"

            struct V
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
            };

            V Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                V o;
                o.positionCS = TransformWorldToHClip(TransformObjectToWorld(input.positionOS.xyz));
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half4 Frag(V input) : SV_Target
            {
                OccluderDitherClip(input.positionCS);
                return half4(normalize(input.normalWS), 0);
            }
            ENDHLSL
        }
    }
}
