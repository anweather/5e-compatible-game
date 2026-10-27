using System.Collections;
using UnityEngine;

// A scripted tour of the Chemistry Yard for demo clips: walks the wizard
// and casts spells on a fixed schedule, so every recording plays the same
// chain reactions. Prototype > Record Demo Clip adds it and records.
public class DemoDirector : MonoBehaviour
{
    public bool Done { get; private set; }

    static readonly WaitForSeconds SettleTime = new(1f);

    ChemGrid grid;
    WizardPuppet puppet;
    SpellCaster caster;

    IEnumerator Start()
    {
        grid = FindAnyObjectByType<ChemGrid>();
        var wizard = WizardRig.FindLocal();
        puppet = new WizardPuppet(wizard);
        caster = wizard.GetComponent<SpellCaster>();
        caster.enabled = false;
        yield return SettleTime;

        // The schedule is in yard coordinates (the world may extend beyond the yard).
        // Directions: 0 N, 1 E, 2 S, 3 W.
        // East to the oil spill and the pond.
        yield return puppet.WalkTo(grid.YardCellCenter(20, 11));
        yield return Cast(SpellIds.FireBolt, 19, 3, 0, 1.2f);
        yield return Cast(SpellIds.CreateWater, 19, 3, 0, 2.5f);     // water splashes burning oil
        yield return Cast(SpellIds.RayOfFrost, 20, 10, 0, 2f);       // a beam north across the pond
        yield return Cast(SpellIds.ShockingGrasp, 24, 9, 1, 2f);     // down the metal strip into the water

        // West to the meadow: grease, then light it, then fan it.
        yield return puppet.WalkTo(grid.YardCellCenter(14, 12));
        yield return Cast(SpellIds.Grease, 10, 12, 3, 1.2f);
        yield return Cast(SpellIds.FireBolt, 10, 12, 3, 2.5f);
        yield return Cast(SpellIds.GustOfWind, 13, 12, 3, 4f);
        Done = true;
    }

    IEnumerator Cast(string spell, int x, int y, int dir, float wait)
    {
        var cell = grid.YardCell(x, y);
        puppet.Face(grid.CellCenter(cell.x, cell.y));
        caster.Cast(spell, cell.x, cell.y, dir);
        yield return new WaitForSeconds(wait);
    }
}
