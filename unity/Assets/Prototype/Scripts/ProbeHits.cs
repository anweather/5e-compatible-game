using System.Collections;
using UnityEngine;

// The probe's hits scenario (docs/FAST_LOOP.md): spells catching a wizard
// (SpellForces.Hit). A caster never catches itself, so the spells are applied
// as if cast by someone standing a few metres off: Fire Bolt on the wizard's
// square throws it back, Ray of Frost through it slows it, Shocking Grasp on
// it jolts it (a moment's stop and a knock).
public class HitsProbe : ProbeScenario
{
    protected override IEnumerator Run()
    {
        Place(13, 9);
        var other = new GameObject("Probe caster").transform;
        other.position = Yard(13, 13);
        var knock = wizard.GetComponent<Knockback>();
        var feet = wizard.GetComponent<SurfaceEffects>();
        try
        {
            Cut(wizard.transform.position + V(6f, 5f, -6f), wizard.transform.position);
            yield return Wait(0.5f);

            var cell = grid.CellAt(wizard.transform.position);
            var before = wizard.transform.position;
            int hit = SpellForces.Apply(grid, SpellIds.FireBolt, cell.x, cell.y, 0, other);
            yield return Wait(1f);
            float thrown = Vector3.Distance(Flat(before), Flat(wizard.transform.position));
            Metric("fire_thrown_m", thrown);
            Check("fire_bolt_throws_wizard", hit == 1 && thrown > 0.5f, $"{hit} wizard caught, thrown {thrown:0.00} m");

            cell = grid.CellAt(wizard.transform.position);
            // From the square south of the wizard, north through it.
            hit = SpellForces.Apply(grid, SpellIds.RayOfFrost, cell.x, cell.y + 1, 0, other);
            yield return null;
            Check("ray_of_frost_slows_wizard", hit == 1 && feet != null && feet.Slowed, $"{hit} wizard caught, slowed {(feet != null && feet.Slowed)}");

            yield return Wait(3f);
            cell = grid.CellAt(wizard.transform.position);
            hit = SpellForces.Apply(grid, SpellIds.ShockingGrasp, cell.x, cell.y, 0, other);
            yield return null;
            Check("shocking_grasp_jolts_wizard", hit == 1 && feet != null && feet.Slowed && knock.Velocity.sqrMagnitude > 0f,
                $"{hit} wizard caught, stopped {(feet != null && feet.Slowed)}, knocked {knock.Velocity.magnitude:0.0} m/s");
        }
        finally
        {
            Object.Destroy(other.gameObject);
        }
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
