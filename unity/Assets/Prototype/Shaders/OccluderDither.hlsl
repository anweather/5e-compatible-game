// Keeps the local wizard in sight: whatever stands between the camera and the
// wizard is cut to a screen-door pattern (an ordered 4x4 dither) inside a soft
// circle on screen round the wizard. Only fragments nearer the camera than the
// wizard are cut, so the ground it stands on and anything behind it stay whole.
// It's a global setting, not a per-object one: OccluderFade sets it each frame
// and eases it in and out, and every shader that includes this and calls
// OccluderDitherClip in its colour and depth passes takes part (VillageLit,
// DitherLit). Shadows are left whole. Opaque throughout: no blending, so depth
// and sorting are untouched.
#ifndef PROTOTYPE_OCCLUDER_DITHER_INCLUDED
#define PROTOTYPE_OCCLUDER_DITHER_INCLUDED

// x, y: the wizard's chest on screen (0 to 1, from the bottom left); z: its distance from the
// camera along the view (metres); w: how much of the pattern is cut (0 off, up to about 0.75).
float4 _OccluderDither;
// x: the circle's radius, as a fraction of the screen's height; y: the screen's width over its
// height; z: how far in front of the wizard (metres) something has to stand to be cut.
float4 _OccluderDitherShape;

// The 4x4 ordered-dither threshold of a pixel, in (0, 1): bit-reversed interleaving of x^y and y.
float OccluderBayer(float2 pixel)
{
    uint2 p = uint2(pixel) & 3u;
    uint v = p.x ^ p.y;
    uint b = ((v & 1u) << 3) | ((p.y & 1u) << 2) | (v & 2u) | ((p.y & 2u) >> 1);
    return (b + 0.5) / 16.0;
}

// How much of the pattern to cut at this fragment (0 where it stays whole).
float OccluderCut(float4 positionCS)
{
    float2 uv = GetNormalizedScreenSpaceUV(positionCS);
    float2 d = uv - _OccluderDither.xy;
    d.x *= _OccluderDitherShape.y;
    float r = length(d) / max(_OccluderDitherShape.x, 1e-4);
    float inside = 1.0 - smoothstep(0.55, 1.0, r);
    float eye = LinearEyeDepth(positionCS.z, _ZBufferParams);
    float nearer = saturate((_OccluderDither.z - _OccluderDitherShape.z - eye) * 2.0);
    return _OccluderDither.w * inside * nearer;
}

// Discards this fragment if it's one of the pattern's holes.
void OccluderDitherClip(float4 positionCS)
{
    // The same for every fragment of a frame, so the branch is cheap; off, it costs one compare.
    if (_OccluderDither.w <= 0.0) return;
    clip(OccluderBayer(positionCS.xy) - OccluderCut(positionCS));
}

#endif
