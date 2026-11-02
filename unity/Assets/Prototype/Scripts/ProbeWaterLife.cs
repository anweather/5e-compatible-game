using System.Collections;
using Unity.Mathematics;
using UnityEngine;

// The probe's waterlife scenario (docs/FAST_LOOP.md): the valley's water from its rows. Finds water deep
// enough to swim in near the wizard, puts ducks, fish, lily pads and reeds there and films them from the shore.
public class WaterLifeProbe : ClipProbe
{
    static readonly string[] Life = { "duck", "duck", "trout", "trout", "koi", "lily-pad", "lily-pad", "lily-pad", "reed", "reed", "reed" };
    const float FilmSeconds = 8f, SearchMetres = 260f;

    protected override IEnumerator Run()
    {
        var fauna = FindAnyObjectByType<ValleyFauna>();
        Check("fauna_built", fauna != null, "ValleyFauna");
        if (fauna == null || string.IsNullOrEmpty(ScreenshotDir))
        {
            Check("needs_shots", ScreenshotDir != null, "waterlife films into --shots <folder>");
            yield break;
        }
        yield return Wait(1f);
        var w = wizard.transform.position;
        if (!FindWater(fauna, new float2(w.x, w.z), out var pool))
        {
            Check("water_found", false, "no water deep enough in reach");
            yield break;
        }
        Check("water_found", true, $"{fauna.WaterDepth(pool):0.0} m deep at {pool}");
        var at = new Unity.Mathematics.Random(0x7A7E5u);
        foreach (string id in Life)
        {
            var p = pool + at.NextFloat2Direction() * at.NextFloat(1f, 5f);
            int kind = fauna.SpeciesIndex(id);
            Check("row_" + id, kind >= 0, id);
            if (kind >= 0 && fauna.WaterDepth(p) >= 0.3f) fauna.Spawn(kind, new float3(p.x, grid.GroundAt(new Vector3(p.x, 0f, p.y)), p.y));
        }
        var eye = new Vector3(pool.x - 7f, 0f, pool.y - 7f);
        Teleport(new Vector3(pool.x + 40f, grid.GroundAt(new Vector3(pool.x + 40f, 0f, pool.y)) + 0.1f, pool.y));
        float top = grid.GroundAt(new Vector3(pool.x, 0f, pool.y)) + fauna.WaterDepth(pool);
        Cut(new Vector3(eye.x, top + 3.5f, eye.z), new Vector3(pool.x, top, pool.y));
        yield return Wait(1f);
        Shot("water");
        StartFilm();
        yield return Wait(FilmSeconds);
        yield return EndFilm(sounds: false);
        Metric("animals", fauna.Count);
    }

    bool FindWater(ValleyFauna fauna, float2 from, out float2 pool)
    {
        pool = default;
        for (float r = 0f; r < SearchMetres; r += 4f)
            for (int k = 0; k < 32; k++)
            {
                float a = k / 32f * 2f * math.PI;
                var p = from + new float2(math.cos(a), math.sin(a)) * r;
                if (fauna.WaterDepth(p) < 1f) continue;
                pool = p;
                return true;
            }
        return false;
    }
}
