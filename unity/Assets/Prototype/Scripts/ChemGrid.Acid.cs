using System.Collections.Generic;
using Chem;
using UnityEngine;

// Acid's mark on the ground: where Acid Splash eats something away (brambles
// to dirt, oil, grease or thorns gone), the cell keeps a dark, sour-green
// stain for a while (CellSlickMesh's eaten film), so you can see what the
// acid took. The core doesn't say which cells it dissolved, so the cast's
// cells that acid could eat are noted as the spell leaves the hand
// (SpellCaster, on every machine) and watched until they change or a moment
// passes; online the cast lands a tick or two after the show.
public partial class ChemGrid
{
    // How long a noted cell is watched (s), and how long its stain lasts.
    const float AcidWatchSeconds = 4f, EatenSeconds = 40f;
    readonly List<(int index, string terrain, string cover, float until)> acidWatch = new();

    /// <summary>An acid spell is on its way: notes its cells that acid can eat, to stain the ones it does.</summary>
    public void WatchAcid(CastCommand cast)
    {
        if (World == null) return;
        var spell = World.Rules.Spell(cast.Spell);
        if (spell == null) return;
        foreach (var (x, y) in World.SpellCells(spell, cast.X, cast.Y, cast.Dir, cast.Charge, cast.AimX, cast.AimY))
        {
            if (!World.InBounds(x, y)) continue;
            var c = World.At(x, y);
            bool eats = World.TerrainOf(c).DissolvedBy == spell.Element
                || (c.Cover != null && World.Rules.Covers.TryGetValue(c.Cover, out var cover) && cover.DissolvedBy == spell.Element);
            if (eats) acidWatch.Add((World.Idx(x, y), c.T, c.Cover, Time.time + AcidWatchSeconds));
        }
    }

    // Once a frame: a watched cell whose terrain or cover changed was eaten.
    void WatchAcidCells()
    {
        for (int i = acidWatch.Count - 1; i >= 0; i--)
        {
            var (index, terrain, cover, until) = acidWatch[i];
            var c = World.Cells[index];
            bool eaten = c.T != terrain || c.Cover != cover;
            if (!eaten && Time.time < until) continue;
            if (eaten) slicks.Eaten(index % World.W, index / World.W, Time.time + EatenSeconds);
            acidWatch.RemoveAt(i);
        }
        slicks.Expire(Time.time);
    }
}
