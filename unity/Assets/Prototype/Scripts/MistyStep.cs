using Chem;
using UnityEngine;

// Misty Step: blink up to the spell's range (6 squares, 30 ft) to an empty
// square you can see, the same rules as the web sandbox's mistyStep. The
// chemistry has nothing to do with it (World.Cast only logs it); this moves
// the caster's own wizard, so online only the caster's machine runs it and
// the wizard's owner-authoritative NetworkTransform carries the jump to
// everyone else (NetWizard.Teleport).
public static class MistyStep
{
    /// <summary>Why the caster at <paramref name="from"/> can't blink to (x, y), or null if it can.</summary>
    public static string Check(ChemGrid grid, SpellDef spell, Vector2Int from, int x, int y)
    {
        var world = grid.World;
        int range = spell.Range > 0 ? spell.Range : 6;
        if (Mathf.Max(Mathf.Abs(x - from.x), Mathf.Abs(y - from.y)) > range)
            return $"Too far. {spell.Name} reaches {range * 5} ft ({range} squares).";
        if (!world.InBounds(x, y) || world.IsBlocking(world.At(x, y)) || Occupied(grid, x, y))
            return $"{spell.Name} needs an empty square to land on.";
        if (!world.LineOfSight(from.x, from.y, x, y))
            return "You can't see that square. Walls block sight; gate bars don't.";
        return null;
    }

    /// <summary>
    /// Moves <paramref name="wizard"/> to (x, y) (the mist at both ends is the cast's show, on every
    /// machine: SpellCaster.PlayCastEffect), checking again
    /// first (the square may have filled up since the cast was aimed). Returns whether it moved.
    /// </summary>
    public static bool Blink(ChemGrid grid, SpellDef spell, Transform wizard, int x, int y)
    {
        var why = Check(grid, spell, grid.CellAt(wizard.position), x, y);
        if (why != null)
        {
            EventLog.Add($"{spell.Name} fizzles: {why}");
            return false;
        }
        var to = grid.CellCenter(x, y) + Vector3.up * grid.SurfaceY(x, y);
        bool hasBody = wizard.TryGetComponent<CharacterController>(out var body);
        if (hasBody) body.enabled = false; // a controller would undo a direct move
        if (wizard.TryGetComponent<NetWizard>(out var net) && net.IsSpawned) net.Teleport(to);
        else wizard.position = to;
        if (hasBody) body.enabled = true;
        EventLog.Add("You reappear in a swirl of silver mist");
        return true;
    }

    // A crate, barrel or boulder standing on the square.
    static bool Occupied(ChemGrid grid, int x, int y) =>
        Forces.Nearest<PhysicsProp>(grid.CellCenter(x, y) + Vector3.up * grid.SurfaceY(x, y), grid.cellSize * 0.6f) != null;
}
