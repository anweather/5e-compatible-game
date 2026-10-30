Shader "Prototype/WindowGlass"
{
    // Window panes (WindowPanes): a see-through tint from the vertex colour, its alpha how much it
    // hides, with the sky's reflection at grazing angles and the sun's glint, so the lit window behind
    // glows through it. A shattered pane's vertices turn opaque and dark (alpha 1), the gap it left,
    // and show no reflection. One pass, no textures; both faces, so it reads from inside a wrecked house.
    // uv.z draws a spiderweb crack (CrackWeb.hlsl): 0 none, 1 and up over the glass, 2 and up the web
    // alone, in the vertex colour (a cell's decal, CellCrackMesh); its fraction seeds the web.
    Properties { }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "CrackWeb.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float3 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half4 color : COLOR;
                half fogFactor : TEXCOORD2;
                float3 uv : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.color = input.color;
                o.uv = input.uv;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half glass = 1.0 - saturate((input.color.a - 0.9) * 10.0);
                half3 n = normalize(input.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
                if (dot(n, v) < 0.0) n = -n;
                half fresnel = 0.16 + 0.84 * pow(1.0 - saturate(dot(n, v)), 5.0);
                half3 sky = GlossyEnvironmentReflection(reflect(-v, n), 0.05, 1.0);
                Light light = GetMainLight();
                half glint = pow(saturate(dot(n, normalize(light.direction + v))), 400.0);
                half3 color = input.color.rgb * lerp(half3(1, 1, 1), SampleSH(n), glass);
                color = lerp(color, sky, fresnel * 0.75 * glass) + light.color * glint * 2.0 * glass;
                half alpha = lerp(1.0, saturate(input.color.a + fresnel * 0.6 + glint), glass);
                half web = input.uv.z >= 1.0 ? CrackWeb(input.uv.xy, frac(input.uv.z)) : 0.0;
                bool decal = input.uv.z >= 2.0;
                color = lerp(color, decal ? input.color.rgb : half3(0.92, 0.96, 1.0), web);
                alpha = decal ? web : max(alpha, web * 0.9);
                return half4(MixFog(color, input.fogFactor), alpha);
            }
            ENDHLSL
        }
    }
}
