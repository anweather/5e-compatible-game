using System.Collections;
using UnityEngine;

// The probe's life scenario (docs/FAST_LOOP.md): the coast's small life
// (AmbientLife). Checks gulls, crabs and shoals are out, the gulls fly (move
// between looks), and a crab the wizard walks up to bolts or burrows.
public class LifeProbe : ProbeScenario
{
    protected override IEnumerator Run()
    {
        var life = FindAnyObjectByType<AmbientLife>();
        Check("life_built", life != null, "AmbientLife on the coast");
        if (life == null) yield break;
        Metric("crab_spots", life.crabSpots.Length);
        Metric("shoals", life.shoals.Length);
        Check("creatures_placed", life.gullAnchors.Length >= 3 && life.crabSpots.Length >= 15 && life.shoals.Length >= 10,
            $"{life.gullAnchors.Length} gull anchors, {life.crabSpots.Length} crab spots, {life.shoals.Length} shoals");

        // A crab: stand back from it, then walk right up to it.
        var crab = life.crabSpots[0];
        Teleport(crab + V(6f, 0.3f, 0f));
        Cut(crab + V(4f, 3f, 4f), crab);
        yield return Wait(2f);
        var before = CrabNearest(life, crab);
        Shot("crabs");
        Teleport(crab + V(0.8f, 0.3f, 0f));
        yield return Wait(1.5f);
        var after = CrabNearest(life, crab);
        Check("crab_shies", after.buried || (after.pos - before.pos).magnitude > 0.8f,
            after.buried ? "it burrowed into the sand" : $"it moved {(after.pos - before.pos).magnitude:0.0} m");
    }

    // The crab whose home is nearest a spot: where it is and whether it's buried (read from AmbientLife).
    static (Vector3 pos, bool buried) CrabNearest(AmbientLife life, Vector3 spot)
    {
        var field = typeof(AmbientLife).GetField("crabs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var crabs = field?.GetValue(life) as System.Array;
        if (crabs == null || crabs.Length == 0) return (Vector3.zero, false);
        var type = crabs.GetValue(0).GetType();
        object best = null;
        float bestD = float.MaxValue;
        foreach (var c in crabs)
        {
            var home = (Vector3)type.GetField("home").GetValue(c);
            float d = (home - spot).sqrMagnitude;
            if (d >= bestD) continue;
            bestD = d;
            best = c;
        }
        return ((Vector3)type.GetField("pos").GetValue(best), (float)type.GetField("buried").GetValue(best) > 0f);
    }
}
