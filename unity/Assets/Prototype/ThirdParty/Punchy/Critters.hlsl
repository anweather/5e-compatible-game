// Adapted from Punchy by Zachary Franklin (https://github.com/Skeleman/Punchy),
// used with permission. Unchanged but for this header.
#ifndef PUNCHY_CRITTERS_INCLUDED
#define PUNCHY_CRITTERS_INCLUDED

// Shared by the firefly and butterfly shaders (CritterSystem). Must match CritterSystem.Critter.

struct Critter
{
    float3 position;   // home, on the ground
    float random;
    float4 color;
};

StructuredBuffer<Critter> _CritterBuffer;
float4 _PunchyCritters;   // x fireflies out (0..1), y butterflies out

// 0..1: whether this critter is out, given how many of its kind are (fades in over a band so they don't pop).
float CritterPresence(float random, float amount)
{
    return saturate((amount - random * 0.95) / 0.05);
}

// Fades critters out towards the edge of CritterSystem's radius.
float CritterDistanceFade(float3 positionWS)
{
    return 1 - smoothstep(50.0, 68.0, length(positionWS - GetCameraPositionWS()));
}

float CritterHash(float n) { return frac(sin(n * 12.9898) * 43758.5453); }

#endif
