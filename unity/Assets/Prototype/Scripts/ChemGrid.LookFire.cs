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
// fire element's colour (ChemGrid.WorldLook.cs), as all fire does. With a look
// "by" a player on, the note says who cast it too, on what a cast leaves as well
// as its fire, and a new fire caught from two different colours keeps their mix
// like paint (ChemGrid.LookScopes.cs).
public partial class ChemGrid
{
    /// <summary>What a cast left on a cell: the spell, who cast it (-1 not noted), and a colour it caught.</summary>
    readonly struct LitNote
    {
        public readonly string Spell;
        public readonly int Caster;
        public readonly LookColour? Colour;

        public LitNote(string spell, int caster, LookColour? colour = null)
        {
            Spell = spell;
            Caster = caster;
            Colour = colour;
        }
    }

    // How long a noted cell waits to catch: online a cast's fire comes at its tick, and the note before or after it.
    const float LookFireWait = 3f;

    readonly Dictionary<int, LitNote> litBy = new();
    readonly Queue<(float until, int index)> litByCheck = new();

    /// <summary>
    /// A spell in a look of its own lands: fire on its cells, and on what it scatters a few cells out, burns in that look.
    /// </summary>
    public void LightInLook(SpellCaster caster, SpellDef spell, SpellVisuals.Entry look, CastCommand cast)
    {
        if (spell == null || look == null || (!look.KeepsFire && !noteCasters)) return;
        showman = caster;
        var note = new LitNote(look.KeepsFire ? spell.Id : null, noteCasters ? SlotOf(caster) : -1);
        int reach = spell.Scatter?.Reach ?? 0;
        foreach (var (x, y) in World.SpellCells(spell, cast.X, cast.Y, cast.Dir, cast.Charge, cast.AimX, cast.AimY))
            for (int dy = -reach; dy <= reach; dy++)
                for (int dx = -reach; dx <= reach; dx++)
                    if (World.InBounds(x + dx, y + dy)) Note(World.Idx(x + dx, y + dy), note);
    }

    void Note(int index, LitNote note)
    {
        if (litBy.TryAdd(index, note)) litByCheck.Enqueue((Time.time + LookFireWait, index));
        else litBy[index] = note;
        if (views != null) views[index].key = int.MinValue; // a fire already there redraws in it
    }

    /// <summary>The look the fire on a cell burns in: its spell's if it chose a colour, else the fire element's.</summary>
    public LookTint FireTint(int x, int y)
    {
        if (anyScoped) return ScopedFireTint(x, y);
        return SpellFire(x, y, out var tint) ? tint : fireLook;
    }

    // Whether a spell that chose a colour lit a cell, and how far that turns its fire: maybe not at all, for a colour near the art's.
    bool SpellFire(int x, int y, out LookTint tint)
    {
        tint = default;
        if (litBy.Count == 0 || !litBy.TryGetValue(World.Idx(x, y), out var note)) return false;
        if (note.Colour.HasValue)
        {
            tint = LookTint.Between(ToColor(fireArt), ToColor(note.Colour.Value));
            return true;
        }
        if (note.Spell == null || Showman == null || Showman.LookOf(note.Spell) is not { KeepsFire: true } look) return false;
        tint = look.Tint;
        return true;
    }

    /// <summary>How far the embers on a cell are turned round the wheel, 0 to 1, for the probe.</summary>
    public float EmberTurnOn(int x, int y) => lava.TurnOn(x, y);

    // A new fire beside a fire in a look caught from it.
    void CatchLookFire(Cell c, int x, int y, bool isNew)
    {
        if (!isNew || c.Fire <= 0 || litBy.Count == 0 || litBy.ContainsKey(World.Idx(x, y))) return;
        caught = default;
        catches = 0;
        mixes = false;
        CatchFrom(x + 1, y);
        CatchFrom(x - 1, y);
        CatchFrom(x, y + 1);
        CatchFrom(x, y - 1);
        if (catches == 0) return;
        Note(World.Idx(x, y), mixes ? new LitNote(null, -1, (fireLooks ?? scopes.Of("elements", ElementIds.Fire)).Caught(caught)) : firstCaught);
    }

    // The neighbours a new fire caught from: their mix, and the one note if they all agree.
    ColourMixer caught;
    LitNote firstCaught;
    LookColour firstColour;
    int catches;
    bool mixes;

    void CatchFrom(int fromX, int fromY)
    {
        if (!World.InBounds(fromX, fromY) || World.At(fromX, fromY).Fire <= 0) return;
        int from = World.Idx(fromX, fromY);
        if (!litBy.TryGetValue(from, out var note)) return;
        var own = NoteOn(from, out var caster);
        var colour = anyScoped ? fireLooks.At(fromX, fromY, spots, own, caster) : own ?? fireArt;
        if (catches == 0)
        {
            firstCaught = note;
            firstColour = colour;
        }
        else if (!colour.Near(firstColour)) mixes = true;
        catches++;
        caught.Add(colour);
    }

    // Lets go of the cells that never caught or have burnt out, a few a frame at most.
    void ForgetLookFire()
    {
        while (litByCheck.Count > 0 && litByCheck.Peek().until <= Time.time)
        {
            int index = litByCheck.Dequeue().index;
            var c = World.Cells[index];
            if (c != null && (c.Fire > 0 || c.Cover == CoverIds.Embers || (noteCasters && (c.Gas != null || c.Cover != null)))) litByCheck.Enqueue((Time.time + LookFireWait, index));
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
