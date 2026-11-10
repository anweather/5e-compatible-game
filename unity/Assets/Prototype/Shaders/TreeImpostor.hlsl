// A tree drawn as an octahedral impostor (docs/design/tree-impostors.md): one camera-facing quad a tree, coloured
// from an atlas of the tree baked from N x N directions over the upper hemisphere (TreeImpostorBaker). Each quad
// picks the three baked views nearest the way it is seen, finds where the eye's ray crosses each view's plane, and
// blends the three by how near each is. The normal atlas holds the tree's normal (object space) and, in alpha, how far
// in front of or behind its view plane the surface stood, which moves the drawn depth onto the tree's real surface.
//
// The passes include this with IMPOSTOR_SHADOW (the eye is the main light, far off) or neither define (the eye is the
// camera). Object space is the tree prefab's own: its base at the origin, as the terrain stands it.

#ifndef TREE_IMPOSTOR_INCLUDED
#define TREE_IMPOSTOR_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

TEXTURE2D(_AlbedoAtlas); SAMPLER(sampler_AlbedoAtlas);
TEXTURE2D(_NormalAtlas); SAMPLER(sampler_NormalAtlas);

CBUFFER_START(UnityPerMaterial)
    float _Frames;      // views along the atlas's side (N)
    float _FrameSize;   // one view's side as a share of the atlas (tile px / atlas px)
    float _Radius;      // the tree's bounding sphere, object space (m)
    float4 _Centre;     // its centre, object space
    float _Cutoff;      // alpha clip
    float _Wrap;        // how far round the light wraps onto the side away from it (leaves let light through)
    float _Translucency; // light through the leaves, looking toward the sun
    float _ShadowStrength; // how much of the shadows falling on it it takes (its own, cast from its centre plane, overshadow it)
    float4 _Tint;
    float _Debug;
CBUFFER_END

// Hemi-octahedral mapping: a direction of the upper hemisphere to a point of [-1, 1]^2 and back. The diamond
// |x| + |z| <= 1 that the direction makes (its L1 length 1) turned 45 degrees fills the square.
float2 ImpostorEncode(float3 d)
{
    d.y = max(d.y, 0.0);
    d /= max(abs(d.x) + d.y + abs(d.z), 1e-5);
    return float2(d.x + d.z, d.z - d.x);
}

float3 ImpostorDecode(float2 c)
{
    float x = (c.x - c.y) * 0.5, z = (c.x + c.y) * 0.5;
    return normalize(float3(x, 1.0 - abs(x) - abs(z), z));
}

// The direction view (i, j) of the grid was baked from (toward the camera that baked it).
float3 ImpostorFrameDir(float2 cell)
{
    return ImpostorDecode(cell / max(_Frames - 1.0, 1.0) * 2.0 - 1.0);
}

// A view's picture plane: right and up as Unity's LookRotation(-d, up) gives them (TreeImpostorBaker.Basis).
void ImpostorBasis(float3 d, out float3 right, out float3 up)
{
    float3 ref = abs(d.y) > 0.999 ? float3(0, 0, 1) : float3(0, 1, 0);
    right = normalize(cross(ref, -d));
    up = cross(-d, right);
}

// The three views nearest direction v and their weights (summing to 1).
void ImpostorFrames(float3 v, out float2 a, out float2 b, out float2 c, out float3 w)
{
    float n1 = max(_Frames - 1.0, 1.0);
    float2 g = (ImpostorEncode(v) * 0.5 + 0.5) * n1;
    g = clamp(g, 0.0, n1 - 1e-3);
    float2 base = floor(g), f = g - base;
    a = base;
    b = base + 1.0;
    if (f.x > f.y)
    {
        c = base + float2(1, 0);
        w = float3(1.0 - f.x, f.y, f.x - f.y);
    }
    else
    {
        c = base + float2(0, 1);
        w = float3(1.0 - f.y, f.x, f.y - f.x);
    }
}

struct ImpostorAttributes
{
    float4 positionOS : POSITION; // the quad's corner, xy in [-1, 1]
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct ImpostorVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionOS : TEXCOORD0;               // the quad's point, object space
    nointerpolation float3 eyeOS : TEXCOORD1;    // the eye, object space
    nointerpolation float4 cellsAB : TEXCOORD2;  // two of the three views
    nointerpolation float2 cellC : TEXCOORD3;
    nointerpolation float3 weights : TEXCOORD4;
    float fog : TEXCOORD5;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

#ifdef IMPOSTOR_SHADOW
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
float3 _LightDirection;
#endif

float3 ImpostorEye()
{
#ifdef IMPOSTOR_SHADOW
    // The main light, a long way off along its direction: the rays come in all but parallel, as the shadow map sees them.
    float3 dirOS = normalize(TransformWorldToObjectDir(_LightDirection));
    return _Centre.xyz + dirOS * (_Radius * 200.0);
#else
    return TransformWorldToObject(GetCameraPositionWS());
#endif
}

ImpostorVaryings ImpostorVertex(ImpostorAttributes input)
{
    ImpostorVaryings o = (ImpostorVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, o);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

    float3 centre = _Centre.xyz;
    float3 eye = ImpostorEye();
    float3 v = normalize(eye - centre);
    float2 a, b, c;
    float3 w;
    ImpostorFrames(v, a, b, c, w);

    float3 right, up;
    ImpostorBasis(v, right, up);
    float3 p = centre + (right * input.positionOS.x + up * input.positionOS.y) * (_Radius * 1.05);

    o.positionOS = p;
    o.eyeOS = eye;
    o.cellsAB = float4(a, b);
    o.cellC = c;
    o.weights = w;
    float3 positionWS = TransformObjectToWorld(p);
#ifdef IMPOSTOR_SHADOW
    float3 lightWS = normalize(_LightDirection);
    o.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, lightWS, lightWS));
    #if UNITY_REVERSED_Z
        o.positionCS.z = min(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
    #else
        o.positionCS.z = max(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
    #endif
#else
    o.positionCS = TransformWorldToHClip(positionWS);
    o.fog = ComputeFogFactor(o.positionCS.z);
#endif
    return o;
}

// One view's albedo and normal-and-depth where the eye's ray (from o along r) crosses its plane; alpha 0 off it.
void ImpostorSample(float2 cell, float3 o, float3 r, out float4 albedo, out float4 normalDepth)
{
    float3 d = ImpostorFrameDir(cell);
    float3 right, up;
    ImpostorBasis(d, right, up);
    float3 centre = _Centre.xyz;
    float denom = dot(r, d);
    float t = dot(centre - o, d) / (abs(denom) > 1e-4 ? denom : -1e-4);
    float3 x = o + r * t - centre;
    float2 uv = float2(dot(x, right), dot(x, up)) / (2.0 * _Radius) + 0.5;
    float inside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
    float2 atlas = (cell + saturate(uv)) * _FrameSize;
    albedo = SAMPLE_TEXTURE2D(_AlbedoAtlas, sampler_AlbedoAtlas, atlas);
    normalDepth = SAMPLE_TEXTURE2D(_NormalAtlas, sampler_NormalAtlas, atlas);
    albedo.a *= inside;
}

struct ImpostorSurface
{
    float3 albedo;
    float alpha;
    float3 normalOS;
    float3 positionOS; // on the tree's surface, the depth moved in
};

ImpostorSurface ImpostorSurfaceOf(ImpostorVaryings input)
{
    float3 o = input.eyeOS;
    float3 r = normalize(input.positionOS - o);
    float4 a0, a1, a2, n0, n1, n2;
    ImpostorSample(input.cellsAB.xy, o, r, a0, n0);
    ImpostorSample(input.cellsAB.zw, o, r, a1, n1);
    ImpostorSample(input.cellC, o, r, a2, n2);
    float3 w = input.weights * float3(a0.a, a1.a, a2.a);
    float sum = w.x + w.y + w.z;
    ImpostorSurface s;
    s.alpha = dot(input.weights, float3(a0.a, a1.a, a2.a));
    float k = 1.0 / max(sum, 1e-4);
    s.albedo = (a0.rgb * w.x + a1.rgb * w.y + a2.rgb * w.z) * k * _Tint.rgb;
    float3 n = (n0.xyz * w.x + n1.xyz * w.y + n2.xyz * w.z) * k * 2.0 - 1.0;
    s.normalOS = normalize(n + float3(0, 1e-4, 0));
    // How far toward the eye the surface stood from the quad's plane, blended; the quad's point moved along the ray.
    float depth = ((n0.w * w.x + n1.w * w.y + n2.w * w.z) * k * 2.0 - 1.0) * _Radius;
    float3 v = normalize(o - _Centre.xyz);
    s.positionOS = input.positionOS - r * (depth / max(dot(-r, v), 0.2));
    return s;
}

// The clip-space depth of an object-space point, as SV_Depth wants it.
float ImpostorDepth(float3 positionOS)
{
    float4 cs = TransformWorldToHClip(TransformObjectToWorld(positionOS));
    return cs.z / cs.w;
}

#endif
