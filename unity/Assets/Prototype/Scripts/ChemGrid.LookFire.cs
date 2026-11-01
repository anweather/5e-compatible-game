using System.Collections.Generic;
using Chem;
using UnityEngine;

// Fire in a spell's look (LookTint): a blue Fireball burns blue after it lands.
// The core's cells only know they burn, so this is the view's own note of which
// spell lit which cell. It reads the spell's look from the rules in force each
// time, so a look that changes redraws what's already burning (RedrawLookFire).
// A fire spreading from such a cell takes its look along: a new fire beside one
// in a look catches that look, and a fire already burning keeps the one it has
// (J-052). Every machine notes the same casts, near enough; nothing here reaches
// the cells. A plain look notes nothing and all of this costs nothing. A spell
// that didn't choose a colour notes nothing either, and its fire burns in the
// fire element's colour (ChemGrid.WorldLook.cs), as all fire does.
public partial class ChemGrid
{
    // How long a noted cell waits to catch: online a cast's fire comes at its tick, and the note before or after it.
    const float LookFireWait = 3f;

    readonly Dictionary<int, string> litBy = new();
    readonly Queue<(float until, int index)> litByCheck = new();

    /// <summary>
    /// A spell in a look of its own lands: fire on its cells, and on what it scatters a few cells out, burns in that look.
    /// </summary>
    public void LightInLook(SpellCaster caster, SpellDef spell, SpellVisuals.Entry look, CastCommand cast)
    {
        if (spell == null || look == null || !look.Tint.Any || look.Tint.Washes) return;
        showman = caster;
        int reach = spell.Scatter?.Reach ?? 0;
        foreach (var (x, y) in World.SpellCells(spell, cast.X, cast.Y, cast.Dir, cast.Charge, cast.AimX, cast.AimY))
            for (int dy = -reach; dy <= reach; dy++)
                for (int dx = -reach; dx <= reach; dx++)
                    if (World.InBounds(x + dx, y + dy)) Note(World.Idx(x + dx, y + dy), spell.Id);
    }

    void Note(int index, string spell)
    {
        if (litBy.TryAdd(index, spell)) litByCheck.Enqueue((Time.time + LookFireWait, index));
        else litBy[index] = spell;
        if (views != null) views[index].key = int.MinValue; // a fire already there redraws in it
    }

    /// <summary>The look the fire on a cell burns in: its spell's if it chose a colour, else the fire element's.</summary>
    public LookTint FireTint(int x, int y)
    {
        var spell = SpellFireTint(x, y);
        return spell.Any ? spell : fireLook;
    }

    // The look of the spell that lit a cell, if one in a look of its own did.
    LookTint SpellFireTint(int x, int y) =>
        litBy.Count > 0 && Showman != null && litBy.TryGetValue(World.Idx(x, y), out var spell) ? Showman.TintOf(spell) : default;

    /// <summary>How far the embers on a cell are turned round the wheel, 0 to 1, for the probe.</summary>
    public float EmberTurnOn(int x, int y) => lava.TurnOn(x, y);

    // A new fire beside a fire in a look caught from it.
    void CatchLookFire(Cell c, int x, int y, bool isNew)
    {
        if (!isNew || c.Fire <= 0 || litBy.Count == 0 || litBy.ContainsKey(World.Idx(x, y))) return;
        CatchFrom(x, y, x + 1, y);
        CatchFrom(x, y, x - 1, y);
        CatchFrom(x, y, x, y + 1);
        CatchFrom(x, y, x, y - 1);
    }

    void CatchFrom(int x, int y, int fromX, int fromY)
    {
        if (!World.InBounds(fromX, fromY) || World.At(fromX, fromY).Fire <= 0) return;
        if (litBy.TryGetValue(World.Idx(fromX, fromY), out var spell)) Note(World.Idx(x, y), spell);
    }

    // Lets go of the cells that never caught or have burnt out, a few a frame at most.
    void ForgetLookFire()
    {
        while (litByCheck.Count > 0 && litByCheck.Peek().until <= Time.time)
        {
            int index = litByCheck.Dequeue().index;
            var c = World.Cells[index];
            if (c != null && (c.Fire > 0 || c.Cover == CoverIds.Embers)) litByCheck.Enqueue((Time.time + LookFireWait, index));
            else litBy.Remove(index);
        }
    }

    // The rules switched: what's burning in a look is drawn again from the look now in force.
    void RedrawLookFire()
    {
        if (views == null) return;
        foreach (int index in litBy.Keys) views[index].key = int.MinValue;
    }
}
