// Adapted from Punchy by Zachary Franklin (https://github.com/Skeleman/Punchy),
// used with permission. Punchy's atmosphere swapped for URP's fog.
Shader "Punchy/Creature"
{
    // Animals (CreatureRenderSystem): every body segment is an instance of one unit capsule mesh, stretched to the
    // segment's length, tapered from its start radius to its end radius, flattened, then placed and rotated.
    // Soft wrapped lighting, shadows, cloud shadows, wetness and the atmosphere.
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // Must match Punchy.Fauna.SegmentInstance.
        struct Segment
        {
            float3 position;
            float length;
            float4 rotation;   // quaternion xyzw
            float radiusStart;
            float radiusEnd;
            float flatten;
            float pad;
            float4 color;
        };

        StructuredBuffer<Segment> _Segments;

        struct CreatureAttributes
        {
            float3 shape : POSITION;      // cos, sin around the axis; t along it
            float2 cap : TEXCOORD0;       // cap offset along the axis, radius factor (× local radius)
            uint instanceID : SV_InstanceID;
        };

        float3 Rotate(float4 q, float3 v)
        {
            float3 t = 2 * cross(q.xyz, v);
            return v + q.w * t + cross(q.xyz, t);
        }

        void SegmentVertex(CreatureAttributes input, out float3 positionWS, out float3 normalWS, out float4 color)
        {
            Segment s = _Segments[input.instanceID];
            float t = input.shape.z;
            float r = lerp(s.radiusStart, s.radiusEnd, t);
            float2 around = input.shape.xy;
            float3 local = float3(around.x * r * input.cap.y, around.y * r * input.cap.y * s.flatten, t * s.length + input.cap.x * r);
            // Normal of the (flattened, tapered) surface, approximately.
            float slope = (s.radiusStart - s.radiusEnd) / max(s.length + r, 1e-3);
            float3 n = float3(around.x * input.cap.y, around.y * input.cap.y / max(s.flatten, 0.05), input.cap.x + slope * input.cap.y);
            positionWS = s.position + Rotate(s.rotation, local);
            normalWS = Rotate(s.rotation, normalize(n));
            color = s.color;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Sky.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 color : TEXCOORD2;
                half fogFactor : TEXCOORD3;
            };

            Varyings Vert(CreatureAttributes input)
            {
                Varyings o;
                float3 n;
                float4 c;
                SegmentVertex(input, o.positionWS, n, c);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = n;
                o.color = c;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 n = normalize(input.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half3 albedo = input.color.rgb * lerp(1, 0.75, PunchyWetnessAt(input.positionWS.xz).r);
                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half shadow = light.shadowAttenuation * (1 - PunchyCloudShadow(input.positionWS));
                half diffuse = saturate(dot(n, light.direction) * 0.75 + 0.25);
                // A little soft sheen at the silhouette, like fur or feathers catching the light.
                half rim = pow(1 - saturate(dot(n, v)), 3) * 0.25;
                half3 color = albedo * (light.color * shadow * diffuse + SampleSH(n) * (1 + rim));
                // Glowing parts (alpha): embers, toxic markings, eye sockets. Bright enough to bloom.
                color += input.color.rgb * input.color.a * (2.2 + 0.4 * sin(_Time.y * 2.1 + input.positionWS.x * 3.0));
                return half4(MixFog(color, input.fogFactor), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            float4 Vert(CreatureAttributes input) : SV_POSITION
            {
                float3 p, n;
                float4 c;
                SegmentVertex(input, p, n, c);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirection = normalize(_LightPosition - p);
            #else
                float3 lightDirection = _LightDirection;
            #endif
                return ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(p, n, lightDirection)));
            }

            half4 Frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            float4 Vert(CreatureAttributes input) : SV_POSITION
            {
                float3 p, n;
                float4 c;
                SegmentVertex(input, p, n, c);
                return TransformWorldToHClip(p);
            }

            half Frag(float4 positionCS : SV_POSITION) : SV_Target { return positionCS.z; }
            ENDHLSL
        }
    }
}
