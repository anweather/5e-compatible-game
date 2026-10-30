// A spiderweb crack, procedural: spokes out from a point near the middle of a quad and rings of
// threads running from spoke to spoke, wobbled by a seed so no two quads match. No textures, one
// small function: WindowGlass draws it over a pane and over the cells' crack decals (CellCrackMesh).
// BrickCourses is the same decal's other pattern, the mortar of a brick cell.
#ifndef PROTOTYPE_CRACK_WEB_INCLUDED
#define PROTOTYPE_CRACK_WEB_INCLUDED

float CrackHash(float n) { return frac(sin(n * 12.9898) * 43758.5453); }

// How far a spoke strays from its even place, in spokes.
float CrackStray(float spoke, float seed) { return (CrackHash(spoke + seed * 7.0) - 0.5) * 0.6; }

// How far out ring j crosses a spoke, 0 to 0.5.
float CrackRing(float j, float spoke, float seed) { return (j + 0.55 + (CrackHash(j * 5.0 + spoke * 3.0 + seed) - 0.5) * 0.6) * 0.1; }

// How much crack at uv (0 to 1 across the quad), 0 to 1. seed is 0 to 1.
float CrackWeb(float2 uv, float seed)
{
    const float Spokes = 10.0, TwoPi = 6.28318, Width = 0.007;
    float2 p = uv - (0.5 + (float2(CrackHash(seed + 1.0), CrackHash(seed + 2.0)) - 0.5) * 0.24);
    float r = length(p);
    float line_ = max(Width, fwidth(r) * 1.1);
    float turn = (atan2(p.y, p.x) / TwoPi + 0.5) * Spokes + seed * 13.0;
    float spoke = floor(turn), f = frac(turn);
    float next = fmod(spoke + 1.0, Spokes);
    spoke = fmod(spoke, Spokes);
    float arc = TwoPi * r / Spokes;
    float toSpoke = min(abs(f - CrackStray(spoke, seed)), abs(f - 1.0 - CrackStray(next, seed))) * arc;
    float crack = 1.0 - smoothstep(0.0, line_, toSpoke - (0.03 - r * 0.05) * 0.1);
    for (float j = 0.0; j < 5.0; j += 1.0)
    {
        if (CrackHash(j * 11.0 + spoke * 7.0 + seed) > 0.78) continue;
        float ring = lerp(CrackRing(j, spoke, seed), CrackRing(j, next, seed), f) - sin(f * 3.14159) * 0.012;
        crack = max(crack, 1.0 - smoothstep(0.0, line_, abs(r - ring)));
    }
    return crack * (1.0 - smoothstep(0.42, 0.5, r));
}

// How much mortar at uv: four courses of bricks, every other one offset by half a brick.
float BrickCourses(float2 uv)
{
    const float Courses = 4.0, Bricks = 2.0, Width = 0.035;
    float y = uv.y * Courses;
    float x = uv.x * Bricks + 0.5 * fmod(floor(y), 2.0);
    float2 edge = abs(frac(float2(x, y)) - 0.5) / float2(Bricks, Courses);
    float w = max(Width * 0.5, fwidth(uv.y) * 1.2);
    float mortar = max(smoothstep(0.5 / Courses - w, 0.5 / Courses, edge.y), smoothstep(0.5 / Bricks - w, 0.5 / Bricks, edge.x));
    return mortar * 0.6;
}

#endif
