// Adapted from Punchy by Zachary Franklin (https://github.com/Skeleman/Punchy),
// used with permission. Unchanged but for this header.
Shader "Punchy/Precipitation"
{
    // Rain streaks (stretched billboards) or snowflakes (soft dots), drawn by particle systems. No textures needed.
    Properties
    {
        [Enum(Snow, 0, Rain, 1, Veil, 2)] _Shape("Shape", Float) = 1
        _Glow("Glow", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Sky.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Shape;
                half _Glow;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; half fog : TEXCOORD1; };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.color = input.color;
                o.uv = input.uv;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 c = input.uv - 0.5;
                half alpha = _Shape > 1.5
                    ? pow(smoothstep(0.5, 0.0, length(c)), 2)                                      // veil: very soft blob
                    : _Shape > 0.5
                        ? smoothstep(0.5, 0.0, abs(c.x) * 2) * smoothstep(0.5, 0.2, abs(c.y))    // rain streak
                        : smoothstep(0.5, 0.15, length(c));                                     // flake / stone
                // Lit roughly like the sky around it, so drops/flakes darken under storm clouds.
                half3 color = input.color.rgb * (0.35 + 0.65 * _PunchySkyHorizon);
                // Glowing flakes (sparks, embers) shine by themselves, bright enough to bloom.
                color = lerp(color, input.color.rgb * 2.5, _Glow);
                color = MixFog(color, input.fog);
                return half4(color, alpha * input.color.a);
            }
            ENDHLSL
        }
    }
}
