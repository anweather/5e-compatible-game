using Chem;
using UnityEngine;

// Mage Hand: a spectral hand that fetches something light from up to the
// spell's range (6 squares, 30 ft) into the wizard's hand, or, when the
// wizard is holding something light, sets it down on a square within range.
// The web sandbox's two taps (pick it up, put it down) as two casts. Things
// to carry are physics, not chemistry (World.Cast ignores "carry"), so this
// runs on the caster's machine only, through the ItemHolder's own pick-up
// and let-go, which online ask the host (NetItem) like picking up by hand.
// Held (charged), the hand reaches half as far again (twice at full charge)
// and lifts that many times the weight.
public static class MageHand
{
    /// <summary>Why a cast at (x, y) from <paramref name="from"/> can't fetch or set down, or null if it can.</summary>
    public static string Check(ChemGrid grid, SpellDef spell, ItemHolder hands, Vector2Int from, int x, int y, int charge = 1)
    {
        if (hands == null) return $"{spell.Name} needs a free hand to work with.";
        int range = Range(spell, charge);
        if (Mathf.Max(Mathf.Abs(x - from.x), Mathf.Abs(y - from.y)) > range)
            return $"Too far. {spell.Name} reaches {range * 5} ft ({range} squares).";
        if (hands.Held != null)
        {
            if (TooHeavy(spell, hands.Held, charge)) return $"The {Lower(hands.Held)} is too heavy for the spectral hand.";
            var world = grid.World;
            return world.InBounds(x, y) && !world.IsBlocking(world.At(x, y)) ? null : "The spectral hand needs open ground to set it down on.";
        }
        var item = ItemAt(grid, x, y);
        if (item == null) return "Nothing there for the spectral hand to pick up.";
        return TooHeavy(spell, item, charge)
            ? $"The {Lower(item)} is too heavy ({item.Def.Mass:0.#} kg); the spectral hand lifts {MaxMass(spell, charge):0.#} kg."
            : null;
    }

    /// <summary>
    /// Fetches the light thing at (x, y) into <paramref name="caster"/>'s hand, or sets down what it
    /// holds there, checking again first. Returns whether the hand did anything.
    /// </summary>
    public static bool Cast(ChemGrid grid, SpellDef spell, Transform caster, int x, int y, int charge = 1)
    {
        caster.TryGetComponent<ItemHolder>(out var hands);
        var why = Check(grid, spell, hands, grid.CellAt(caster.position), x, y, charge);
        if (why != null)
        {
            EventLog.Add($"{spell.Name} fizzles: {why}");
            return false;
        }
        if (hands.Held != null)
        {
            string name = Lower(hands.Held);
            hands.SetDownAt(grid.CellCenter(x, y) + Vector3.up * (grid.SurfaceY(x, y) + 0.3f));
            EventLog.Add($"The spectral hand sets the {name} down");
        }
        else
        {
            var item = ItemAt(grid, x, y);
            hands.PickUpFromAfar(item, (Range(spell, charge) + 1) * grid.cellSize);
            EventLog.Add($"The spectral hand brings you the {Lower(item)}");
        }
        return true;
    }

    /// <summary>How many squares the hand reaches at a charge: the spell's range, half again held, twice at full charge.</summary>
    public static int Range(SpellDef spell, int charge = 1) => (spell.Range > 0 ? spell.Range : 6) * (1 + World.ClampCharge(charge)) / 2;
    /// <summary>The most the hand lifts at a charge, in kg: the spell's maxMass times the charge.</summary>
    public static double MaxMass(SpellDef spell, int charge = 1) => (spell.MaxMass > 0 ? spell.MaxMass : 1) * World.ClampCharge(charge);
    static bool TooHeavy(SpellDef spell, Carryable item, int charge) => item.Def != null && item.Def.Mass > MaxMass(spell, charge);
    static string Lower(Carryable item) => item.Name.ToLowerInvariant();

    // The thing to carry lying on (or just over) the square: not in anyone's hand.
    static Carryable ItemAt(ChemGrid grid, int x, int y)
    {
        var centre = grid.CellCenter(x, y) + Vector3.up * grid.SurfaceY(x, y);
        Carryable best = null;
        float bestDistance = float.MaxValue;
        foreach (var item in Forces.InSphere<Carryable>(centre, grid.cellSize * 0.9f, null))
        {
            if (item.Holder != null || !item.isActiveAndEnabled) continue;
            float d = (item.transform.position - centre).sqrMagnitude;
            if (d >= bestDistance) continue;
            bestDistance = d;
            best = item;
        }
        return best;
    }
}
