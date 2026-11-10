Shader "Prototype/TreeImpostor"
{
    // A tree as an octahedral impostor (TreeImpostor.hlsl, docs/design/tree-impostors.md): one quad a tree, drawn
    // instanced by TreeImpostors from the atlases TreeImpostorBaker bakes. Lit by the main light (with its shadows and
    // a wrap for the leaves) and the ambient, fogged, its depth moved onto the tree's surface; it casts its shadow
    // from the light's own three views. Forward and Forward+ only.
    Properties
    {
        [NoScaleOffset] _AlbedoAtlas("Albedo atlas", 2D) = "clear" {}
        [NoScaleOffset] _NormalAtlas("Normal and depth atlas", 2D) = "bump" {}
        _Frames("Views a side", Float) = 12
        _FrameSize("A view's share of the atlas", Float) = 0.0833
        _Radius("Bounding radius", Float) = 1
        _Centre("Bounding centre", Vector) = (0, 1, 0, 0)
        _Cutoff("Alpha cutoff", Range(0, 1)) = 0.5
        _Wrap("Light wrap", Range(0, 1)) = 0.5
        _Translucency("Translucency", Range(0, 2)) = 0.5
        _ShadowStrength("Shadow strength", Range(0, 1)) = 0.7
        _Tint("Tint", Color) = (1, 1, 1, 1)
        [HideInInspector] _Debug("Debug: 1 the light, 2 the ambient, 3 the normal", Float) = 0
        [HideInInspector] _BakeVersion("The baker's version (TreeImpostorBaker.BakeVersion)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ImpostorVertex
            #pragma fragment ImpostorForward
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "TreeImpostor.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            half4 ImpostorForward(ImpostorVaryings input, out float depth : SV_Depth) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                ImpostorSurface s = ImpostorSurfaceOf(input);
                clip(s.alpha - _Cutoff);
                float3 positionWS = TransformObjectToWorld(s.positionOS);
                float3 normalWS = TransformObjectToWorldNormal(s.normalOS);
                Light light = GetMainLight(TransformWorldToShadowCoord(positionWS));
                float ndl = saturate((dot(normalWS, light.direction) + _Wrap) / (1.0 + _Wrap));
                float3 viewWS = normalize(GetCameraPositionWS() - positionWS);
                float through = pow(saturate(dot(viewWS, -light.direction)), 4.0) * _Translucency;
                float shadow = lerp(1.0, light.shadowAttenuation, _ShadowStrength);
                // Not light.distanceAttenuation: for a directional light that's unity_LightData.z, per renderer, which an
                // instanced draw (RenderMeshInstanced) leaves 0.
                float3 lit = light.color * ((ndl + through) * shadow);
                float3 colour = s.albedo * (SampleSH(normalWS) + lit);
                if (_Debug > 0.5 && _Debug < 1.5) colour = lit;
                else if (_Debug > 1.5 && _Debug < 2.5) colour = SampleSH(normalWS);
                else if (_Debug > 2.5) colour = normalWS * 0.5 + 0.5;
                else colour = MixFog(colour, input.fog);
                depth = ImpostorDepth(s.positionOS);
                return half4(colour, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ImpostorVertex
            #pragma fragment ImpostorShadow
            #pragma multi_compile_instancing
            #define IMPOSTOR_SHADOW
            #include "TreeImpostor.hlsl"

            half4 ImpostorShadow(ImpostorVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                ImpostorSurface s = ImpostorSurfaceOf(input);
                clip(s.alpha - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ImpostorVertex
            #pragma fragment ImpostorDepthOnly
            #pragma multi_compile_instancing
            #include "TreeImpostor.hlsl"

            half ImpostorDepthOnly(ImpostorVaryings input, out float depth : SV_Depth) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                ImpostorSurface s = ImpostorSurfaceOf(input);
                clip(s.alpha - _Cutoff);
                depth = ImpostorDepth(s.positionOS);
                return 0;
            }
            ENDHLSL
        }
    }
}
