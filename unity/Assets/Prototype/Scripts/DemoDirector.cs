using System.Collections;
using UnityEngine;

// A scripted tour of the Chemistry Yard for demo clips: walks the wizard
// and casts spells on a fixed schedule, so every recording plays the same
// chain reactions. Prototype > Record Demo Clip adds it and records.
public class DemoDirector : MonoBehaviour
{
    public bool Done { get; private set; }

    ChemGrid grid;
    WizardPuppet puppet;
    SpellCaster caster;

    IEnumerator Start()
    {
        grid = FindFirstObjectByType<ChemGrid>();
        var wizard = GameObject.Find("Wizard");
        puppet = new WizardPuppet(wizard);
        caster = wizard.GetComponent<SpellCaster>();
        caster.enabled = false;
        yield return new WaitForSeconds(1f);

        // East to the oil spill and the pond.
        yield return puppet.WalkTo(grid.CellCenter(20, 11));
        yield return Cast("fire-bolt", 19, 3, 0, 1.2f);
        yield return Cast("create-water", 19, 3, 0, 2.5f);   // water splashes burning oil
        yield return Cast("ray-of-frost", 20, 10, 0, 2f);     // a beam north across the pond
        yield return Cast("shocking-grasp", 24, 9, 1, 2f);    // down the metal strip into the water

        // West to the meadow: grease, then light it, then fan it.
        yield return puppet.WalkTo(grid.CellCenter(14, 12));
        yield return Cast("grease", 10, 12, 3, 1.2f);
        yield return Cast("fire-bolt", 10, 12, 3, 2.5f);
        yield return Cast("gust-of-wind", 13, 12, 3, 4f);
        Done = true;
    }

    IEnumerator Cast(string spell, int x, int y, int dir, float wait)
    {
        puppet.Face(grid.CellCenter(x, y));
        caster.Cast(spell, x, y, dir);
        yield return new WaitForSeconds(wait);
    }
}
