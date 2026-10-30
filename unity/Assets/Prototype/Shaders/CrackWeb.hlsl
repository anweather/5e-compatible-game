// A spiderweb crack, procedural: spokes out from a point near the middle of a quad and the
// threads between them, wobbled by a seed so no two quads match. No textures, one small
// function: WindowGlass draws it over a pane and over the cells' crack decals (CellCrackMesh).
#ifndef PROTOTYPE_CRACK_WEB_INCLUDED
#define PROTOTYPE_CRACK_WEB_INCLUDED

float CrackHash(float n) { return frac(sin(n * 12.9898) * 43758.5453); }

// How much crack at uv (0 to 1 across the quad), 0 to 1. seed is 0 to 1.
float CrackWeb(float2 uv, float seed)
{
    const float Spokes = 9.0, Rings = 6.0, Width = 0.012, TwoPi = 6.28318;
    float2 p = uv - (0.5 + (float2(CrackHash(seed + 1.0), CrackHash(seed + 2.0)) - 0.5) * 0.3);
    float r = length(p);
    float turn = atan2(p.y, p.x) / TwoPi * Spokes + seed * 13.0;
    float sector = floor(turn);
    float wobble = (CrackHash(sector + seed) - 0.5) * 0.7 + sin(r * 21.0 + sector) * 0.08;
    float spoke = 1.0 - smoothstep(0.0, Width, abs(frac(turn) - 0.5 - wobble * 0.5) * TwoPi * r / Spokes);
    float ring = r * Rings + CrackHash(sector * 3.0 + seed);
    float thread = 1.0 - smoothstep(0.0, Width, abs(frac(ring) - 0.5) / Rings);
    thread *= step(CrackHash(floor(ring) + sector * 7.0 + seed), 0.45);
    return max(spoke, thread) * (1.0 - smoothstep(0.42, 0.55, r));
}

#endif
