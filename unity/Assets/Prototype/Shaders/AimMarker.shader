Shader "Prototype/AimMarker"
{
    // The aim marker (AimMarker): ribbons on the ground. No textures: a bright core with a dark rim, so
    // it reads on sand, snow and water alike. UV x is metres along the ribbon, y its side (-1 to 1), z
    // its dash length (0 solid; negative marches the other way). The first pass shows what's hidden
    // behind a hill or a wall, faintly; the second what can be seen. The glow runs past 1 and pulses.
    Properties { }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+100" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        HLSLINCLUDE
        #pragma target 3.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        struct Attributes
        {
            float4 positionOS : POSITION;
            half4 color : COLOR;
            float3 uv : TEXCOORD0;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            half4 color : COLOR;
            float3 uv : TEXCOORD0;
        };

        Varyings Vert(Attributes input)
        {
            Varyings o;
            o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            o.color = input.color;
            o.uv = input.uv;
            return o;
        }

        half4 Shade(Varyings i, half strength)
        {
            const float DashSpeed = 1.1, Pulse = 0.2, DashOn = 0.6;
            float across = abs(i.uv.y), soft = max(fwidth(i.uv.y), 0.02);
            float rim = 1.0 - smoothstep(1.0 - soft, 1.0, across);
            float core = 1.0 - smoothstep(0.55 - soft, 0.55 + soft, across);
            if (abs(i.uv.z) > 0.001)
            {
                float length = abs(i.uv.z);
                float phase = frac((i.uv.x - sign(i.uv.z) * _Time.y * DashSpeed) / length);
                float blur = max(fwidth(i.uv.x) / length, 0.02);
                float on = smoothstep(0.0, blur, phase) * (1.0 - smoothstep(DashOn, DashOn + blur, phase));
                rim *= on;
                core *= on;
            }
            float glow = 1.5 + Pulse * sin(_Time.y * 4.0);
            half3 colour = lerp(half3(0.02, 0.02, 0.03), i.color.rgb * glow, core);
            return half4(colour, max(rim * 0.6, core) * i.color.a * strength);
        }
        ENDHLSL

        Pass
        {
            Name "Hidden"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZTest Greater

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target { return Shade(i, 0.3); }
            ENDHLSL
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target { return Shade(i, 1.0); }
            ENDHLSL
        }
    }
}
