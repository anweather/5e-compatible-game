// The air between the eye and what it sees (docs/design/hills.md "One air", after the boat game's B166/B167): one model
// for every shader that draws the land, its water and what stands far off. AirInPlay alone writes it, once a frame, from
// Air.cs (the same sums: AirTests holds the two together). How much of a shape is left at a distance is clear air thinning
// it exponentially over kilometres (_Air.x per metre) times the weather's haze as a gaussian (_Air.y per metre), or, on a
// map whose haze is linear (the valley) and under water, a straight fade from _Air.z to _Air.w metres; past _AirFar.x it
// gives way wholly by _AirFar.y, where nothing more is drawn. What's left of it goes toward the colour of the sky at the
// horizon on its bearing (AirToward: the skybox's own horizon there, its colour and the sun's glow, Punchy's Sky.hlsl),
// so a far shape fades into the sky behind it and never stands out against it. The distance is from the eye, not the
// depth URP's fog measures, so the haze is the same at the edges of the view as in its middle.
#ifndef PROTOTYPE_AIR
#define PROTOTYPE_AIR

float4 _Air;
float4 _AirFar;
half3 _AirHorizon;
half3 _AirGlow;
float3 _AirSun;

// Punchy's skybox at the horizon: the glow round the sun falls off as the 12th power of how near the sun the bearing is.
static const float AirGlowPower = 12.0;

half AirSeen(float dist)
{
    float thick = dist * _Air.y;
    float seen = exp(-dist * _Air.x) * exp(-thick * thick);
    if (_AirFar.y > 0.0) seen *= 1.0 - smoothstep(_AirFar.x, _AirFar.y, dist);
    float faded = saturate((_Air.w - dist) / max(_Air.w - _Air.z, 1e-3));
    return (half)(_Air.w > 0.0 ? faded : seen);
}

half3 AirToward(float3 toward)
{
    float2 flat = toward.xz / max(length(toward.xz), 1e-3);
    float sun = saturate(dot(flat, _AirSun.xz));
    return _AirHorizon + _AirGlow * (half)pow(sun, AirGlowPower);
}

half3 Aired(half3 color, float3 positionWS)
{
    float3 toward = positionWS - _WorldSpaceCameraPos;
    return lerp(AirToward(toward), color, AirSeen(length(toward)));
}

// A far shape's own colour paled toward the sky's on its bearing at its own brightness, more the farther it stands past
// DullFrom (the boat game's I17): the far hills lose their green before the haze has taken them.
static const float DullFrom = 3000.0, DullSpan = 8000.0;
static const half3 DullLuma = half3(0.2126, 0.7152, 0.0722);

half3 DulledFar(half3 color, float3 positionWS, half dulls)
{
    float3 toward = positionWS - _WorldSpaceCameraPos;
    half dull = dulls * (half)(1.0 - exp(-max(length(toward.xz) - DullFrom, 0.0) / DullSpan));
    half3 sky = AirToward(toward);
    half3 asBright = sky * (dot(color, DullLuma) / max(dot(sky, DullLuma), 1e-4));
    return lerp(color, asBright, dull);
}

#endif
