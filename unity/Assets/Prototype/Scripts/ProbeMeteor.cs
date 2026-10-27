using System.Collections;
using UnityEngine;

// The probe's meteor scenario (docs/FAST_LOOP.md): Meteor cast from the bar on
// the meadow east of the yard, as a player casts it (SpellCaster, MeteorFall).
// Shots as it falls, as it lands and after. Checks the meteor came down from
// the sky with its light, the spell landed when it hit (not at the cast), its
// crater (three steps down in the middle) and the ring of fire round it.
public class MeteorProbe : ProbeScenario
{
    protected override IEnumerator Run()
    {
        Place(4, 10);
        var at = Yard(14, 3);
        var cell = grid.CellAt(at);
        at.y = grid.GroundAt(at);
        // From high behind the wizard, over the trees, looking past it at the target and the sky beyond.
        var me = wizard.transform.position;
        var back = me - at;
        back.y = 0f;
        Cut(me + back.normalized * 10f + V(0f, 14f, 0f), at + V(0f, 12f, 0f));
        yield return Wait(0.5f);
        Check("bar_has_meteor", caster.Select(SpellIds.Meteor));
        puppet.Face(at);
        caster.Cast(SpellIds.Meteor, cell.x, cell.y, 1);
        // It comes down at the gesture's pull (CastPose, CastGestures), not at the cast.
        yield return Wait(CastGestures.Of(caster.GestureOf(SpellIds.Meteor)).release);
        yield return null;
        var meteor = GameObject.Find("Meteor");
        Check("meteor_falls", meteor != null && meteor.transform.position.y > at.y + 50f, meteor != null ? $"it starts {meteor.transform.position.y - at.y:0} m up" : "no Meteor");
        Check("meteor_glows", meteor != null && meteor.GetComponentInChildren<Light>() != null);
        yield return Wait(MeteorFall.Seconds * 0.45f);
        Check("not_landed_yet", World.At(cell.x, cell.y).Height == 0, "the ground waits for it to land");
        Shot("falling");
        yield return Wait(MeteorFall.Seconds * 0.55f + 0.12f);
        Shot("impact");
        yield return Wait(2.5f);
        int middle = World.At(cell.x, cell.y).Height;
        Check("crater", middle == -3, $"the middle {middle} steps");
        int burning = 0;
        // The ring past the crater (which digs out to two squares): burning, or already burnt to dirt.
        // How many still burn at this moment depends on the frame rate, so burnt ones count too.
        for (int dy = -4; dy <= 4; dy++)
            for (int dx = -4; dx <= 4; dx++)
            {
                if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy)) < 3) continue;
                var c = World.At(cell.x + dx, cell.y + dy);
                if (c.Fire > 0 || c.T == TerrainIds.Dirt) burning++;
            }
        Metric("burning_round_it", burning);
        Check("ring_of_fire", burning >= 8, $"{burning} squares round the crater burning or burnt");
        Shot("after");
    }
}
