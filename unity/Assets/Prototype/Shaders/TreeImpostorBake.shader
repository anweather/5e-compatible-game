Shader "Hidden/Prototype/TreeImpostorBake"
{
    // What TreeImpostorBaker draws a tree's full mesh with, one view at a time: its albedo (and coverage) into the first
    // target, and into the second its normal in the tree's own space with, in alpha, how far toward the view the surface
    // stands from the plane through the tree's centre (TreeImpostor.hlsl reads both). Unlit, so the light is the
    // impostor's own at run time. The "world" here is the tree's own space: the baker draws the prefab at the origin.
    Properties
    {
        _MainTex("Albedo", 2D) = "white" {}
        _BumpMap("Normal", 2D) = "bump" {}
        _Color("Colour", Color) = (1, 1, 1, 1)
        _Cutoff("Alpha cutoff", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            float4 _Color;
            float _Cutoff;
            float4 _BakeCentre; // xyz the tree's centre, w its radius
            float4 _BakeDir;    // toward the view

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float4 tangentWS : TEXCOORD3;
                float3 ao : TEXCOORD4;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = v.uv;
                // SpeedTree keeps its baked occlusion in the vertex colour, and its shaders darken the albedo by it.
                o.ao = v.color.rgb;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.tangentWS = float4(TransformObjectToWorldDir(v.tangentOS.xyz), v.tangentOS.w);
                return o;
            }

            void Frag(Varyings i, bool front : SV_IsFrontFace, out float4 albedo : SV_Target0, out float4 normalDepth : SV_Target1)
            {
                float4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * _Color;
                if (_Cutoff > 0) clip(c.a - _Cutoff);
                float3 n = normalize(i.normalWS);
                float3 t = normalize(i.tangentWS.xyz - n * dot(i.tangentWS.xyz, n));
                float3 b = cross(n, t) * (i.tangentWS.w < 0 ? -1.0 : 1.0);
                float3 ts = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv));
                float3 nm = normalize(t * ts.x + b * ts.y + n * ts.z);
                if (!front) nm = -nm;
                float depth = dot(i.positionWS - _BakeCentre.xyz, _BakeDir.xyz) / _BakeCentre.w;
                albedo = float4(c.rgb * i.ao, 1);
                normalDepth = float4(nm * 0.5 + 0.5, saturate(depth * 0.5 + 0.5));
            }
            ENDHLSL
        }
    }
}
