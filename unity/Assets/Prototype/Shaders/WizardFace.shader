Shader "Prototype/WizardFace"
{
    // The wizard's face (WizardOutfit): a black ball with a fuzzy edge and two red vertical slits for
    // eyes. No textures: on a Unity sphere, the object's +z is the way the head faces. The fuzz is
    // noise cutting holes in the silhouette, with a faint sheen on the hairs; the eyes glow past 1, so
    // they bloom. The shadow and depth passes are URP Lit's, through the Fallback.
    Properties { }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                half fogFactor : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionOS = input.positionOS.xyz;
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            // An eye: a thin upright slit, widest in its middle, soft at its edge.
            half Slit(float3 p, float side)
            {
                const float Across = 0.15, Up = 0.05, Tall = 0.2, Wide = 0.026;
                float y = (p.y - Up) / Tall;
                if (abs(y) >= 1.0) return 0.0;
                float wide = Wide * (1.0 - y * y);
                float d = abs(p.x - side * Across) - wide;
                return 1.0 - smoothstep(0.0, clamp(fwidth(p.x) * 1.5, 0.004, 0.012), d);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float rim = 1.0 - saturate(dot(n, v));
                float hair = Hash(floor(input.positionOS * 42.0));
                clip(1.0 - rim * rim * (0.35 + 1.1 * hair));
                half3 color = half3(0.008, 0.008, 0.01) + (0.015 + 0.06 * hair) * rim * half3(1.0, 1.0, 1.1);
                if (input.positionOS.z > 0.2)
                {
                    half eyes = max(Slit(input.positionOS, -1.0), Slit(input.positionOS, 1.0));
                    color = lerp(color, half3(2.4, 0.05, 0.03), eyes);
                }
                return half4(MixFog(color, input.fogFactor), 1.0);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Lit"
}
