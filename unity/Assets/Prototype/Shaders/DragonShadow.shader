Shader "Prototype/DragonShadow"
{
    // The dragon's shadow (DragonShadow): a soft dark shape on a sheet laid over the ground, worked out here
    // from the sheet's uv with no texture: a body, a tail and two swept wings, its nose toward +v.
    Properties
    {
        _Strength ("How dark", Range(0, 1)) = 0.6
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "DragonShadow"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float _Strength;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv * 2.0 - 1.0;
                return o;
            }

            float Soft(float distance) { return 1.0 - smoothstep(-0.05, 0.07, distance); }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv;
                float x = abs(p.x);
                // The body: narrow, to a long tail behind.
                float body = x - 0.13 * saturate(1.0 - abs(p.y - 0.25) / 0.75);
                // A wing: from the shoulder out to its tip, swept back, its trailing edge scalloped.
                float span = x / 0.92;
                float lead = 0.42 - span * 0.75, trail = lead - 0.46 * (1.0 - span) - 0.05 * abs(sin(span * 12.0));
                float wing = max(max(p.y - lead, trail - p.y), span - 1.0);
                float shape = max(Soft(body), Soft(wing * 0.8));
                float edge = saturate((1.0 - length(p)) * 6.0);
                return half4(0.02, 0.015, 0.02, shape * edge * _Strength);
            }
            ENDHLSL
        }
    }
}
