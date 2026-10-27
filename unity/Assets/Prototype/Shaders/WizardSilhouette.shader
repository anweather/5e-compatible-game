Shader "Prototype/WizardSilhouette"
{
    // The local wizard seen through what hides it, where that can't be dithered away (the
    // terrain's trees, a hill, a pack's rock): a copy of its body drawn after everything opaque,
    // in a screen-door pattern with a soft rim, only where the scene's depth shows something
    // standing well in front of it (so it never draws over the wizard's own body). OccluderFade
    // switches the copy on while something occludes; the pattern eases with the same global
    // fade as the occluder dither (OccluderDither.hlsl). Needs the camera's depth texture (on in
    // the PC render pipeline asset).
    Properties
    {
        _Color ("Colour", Color) = (0.62, 0.82, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+100" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Silhouette"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest Always
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "OccluderDither.hlsl"

            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
                float scene = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float self = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                // Only where something stands well in front of this part of the body.
                clip(self - scene - _OccluderDitherShape.z);
                // About half the pixels at full fade, offset from the occluder's own pattern.
                clip(_OccluderDither.w * 0.67 - OccluderBayer(input.positionCS.xy + float2(2, 1)));
                half3 n = normalize(input.normalWS);
                half3 v = normalize(GetWorldSpaceViewDir(input.positionWS));
                half rim = 1.0 - saturate(dot(n, v));
                return half4(_Color.rgb * (0.45 + 1.1 * rim * rim), 1);
            }
            ENDHLSL
        }
    }
}
