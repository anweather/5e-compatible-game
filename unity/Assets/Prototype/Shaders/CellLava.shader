Shader "Prototype/CellLava"
{
    // Lava on the ground (CellLavaMesh): plates of dark crust with glowing cracks between them, the
    // cracks crawling slowly. No textures: the plates are a Voronoi pattern in world space. Vertex red
    // is how hot it is (1 molten, wide bright cracks; 0 cooled, closed and dark); vertex alpha fades
    // the edge. The glow runs past 1, so it blooms.
    Properties { }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half4 color : COLOR;
                half fogFactor : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.color = input.color;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float2 Hash2(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453);
            }

            // How far a point is from the nearest edge between Voronoi cells, about 0 to 0.5.
            float EdgeDistance(float2 p, float time)
            {
                float2 cell = floor(p), f = frac(p), nearest = 0.0;
                float best = 8.0;
                for (int y = -1; y <= 1; y++)
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 g = float2(x, y);
                        float2 o = Hash2(cell + g);
                        float2 r = g + 0.5 + 0.42 * sin(time + 6.2831 * o) - f;
                        float d = dot(r, r);
                        if (d < best) { best = d; nearest = r; }
                    }
                float edge = 8.0;
                for (int y = -2; y <= 2; y++)
                    for (int x = -2; x <= 2; x++)
                    {
                        float2 g = float2(x, y);
                        float2 o = Hash2(cell + g);
                        float2 r = g + 0.5 + 0.42 * sin(time + 6.2831 * o) - f;
                        float2 between = r - nearest;
                        if (dot(between, between) > 0.0001) edge = min(edge, dot(0.5 * (nearest + r), normalize(between)));
                    }
                return edge;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float hot = input.color.r;
                float edge = EdgeDistance(input.positionWS.xz * 1.9, _Time.y * 0.12);
                float crack = 1.0 - smoothstep(0.0, lerp(0.012, 0.13, hot), edge);
                Light light = GetMainLight();
                half3 n = normalize(input.normalWS);
                half3 crust = half3(0.05, 0.04, 0.036) * (saturate(dot(n, light.direction)) * light.color + SampleSH(n));
                half3 glow = lerp(half3(1.6, 0.16, 0.02), half3(6.0, 2.2, 0.35), crack * crack) * (0.25 + 0.75 * hot);
                half3 color = lerp(crust, glow, saturate(crack * (0.35 + 0.65 * hot)));
                return half4(MixFog(color, input.fogFactor), input.color.a);
            }
            ENDHLSL
        }
    }
}
