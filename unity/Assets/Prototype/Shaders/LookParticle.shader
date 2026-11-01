Shader "Prototype/LookParticle"
{
    // A Particle Pack effect in a spell's look (LookTint.Dress): what the pack's unlit particle material
    // draws (its sheet, flipbook blend, emission, soft edge against the ground), then turned round the
    // colour wheel. Some of the pack's sheets are coloured (fireball.tif is orange), so tinting can't
    // make them blue; turning can. Every variant is built, as a player has no material of this shader
    // for Unity to see what's needed from. Only an effect turned from its own colour is drawn with it.
    Properties
    {
        _BaseMap ("Sheet", 2D) = "white" {}
        [HDR] _BaseColor ("Colour", Color) = (1, 1, 1, 1)
        _EmissionMap ("Emission", 2D) = "white" {}
        [HDR] _EmissionColor ("Emission colour", Color) = (0, 0, 0, 1)
        _SoftParticleFadeParams ("Soft edge (near, 1 / range)", Vector) = (0, 1, 0, 0)
        _Turn ("Turn (radians)", Float) = 0
        [HideInInspector] _SrcBlend ("Source", Float) = 5
        [HideInInspector] _DstBlend ("Destination", Float) = 10
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Pass
        {
            Name "LookParticle"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_local _ _FLIPBOOKBLENDING_ON
            #pragma multi_compile_local _ _EMISSION
            #pragma multi_compile_local _ _SOFTPARTICLES_ON
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "HueTurn.hlsl"

            TEXTURE2D(_BaseMap);
            TEXTURE2D(_EmissionMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor, _EmissionColor, _SoftParticleFadeParams;
                float _Turn;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float4 uv : TEXCOORD0; // the frame, and the next frame in .zw when a flipbook blends
                float blend : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float4 uv : TEXCOORD0;
                float blend : TEXCOORD1;
                half fogFactor : TEXCOORD2;
                float4 screen : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.color = input.color;
                o.uv = float4(TRANSFORM_TEX(input.uv.xy, _BaseMap), TRANSFORM_TEX(input.uv.zw, _BaseMap));
                o.blend = input.blend;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                o.screen = ComputeScreenPos(o.positionCS);
                return o;
            }

            half4 Sheet(TEXTURE2D_PARAM(map, sampler_map), Varyings input)
            {
                half4 c = SAMPLE_TEXTURE2D(map, sampler_map, input.uv.xy);
            #if defined(_FLIPBOOKBLENDING_ON)
                c = lerp(c, SAMPLE_TEXTURE2D(map, sampler_map, input.uv.zw), input.blend);
            #endif
                return c;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 c = Sheet(TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap), input) * _BaseColor * input.color;
            #if defined(_EMISSION)
                c.rgb += Sheet(TEXTURE2D_ARGS(_EmissionMap, sampler_BaseMap), input).rgb * _EmissionColor.rgb;
            #endif
            #if defined(_SOFTPARTICLES_ON)
                float2 screen = input.screen.xy / input.screen.w;
                float ground = LinearEyeDepth(SampleSceneDepth(screen), _ZBufferParams);
                float near = _SoftParticleFadeParams.x, range = _SoftParticleFadeParams.y;
                c.a *= saturate(range * (ground - near - LinearEyeDepth(input.positionCS.z, _ZBufferParams)));
            #endif
                c.rgb = HueTurn(c.rgb, _Turn);
                return half4(MixFog(c.rgb, input.fogFactor), saturate(c.a));
            }
            ENDHLSL
        }
    }
}
