using Chem;
using UnityEngine;

// What a spell does to things that move, beside what it does to cells (that
// is World.Cast): Thunderwave throws everything around the caster away from
// it, Gust of Wind shoves everything along its line, and Levitate lifts the
// prop nearest its target. The strengths are the spell's push and duration in
// rules/spells.json (see Forces for the units).
//
// Solo only for now: SpellCaster calls it when a cast lands. Online, casts
// reach every machine through NetChem, and props stay put (see ChemEffects).
public static class SpellForces
{
    /// <summary>Applies the forces of a cast that just landed. Returns how many things it moved.</summary>
    public static int Apply(ChemGrid grid, string spellId, int x, int y, int dir, Transform caster)
    {
        var spell = grid.World.Rules.Spell(spellId);
        if (spell == null) return 0;
        if (spell.Effect == SpellShapes.LevitateEffect) return Levitate(grid, spell, x, y) ? 1 : 0;
        if (spell.Push <= 0) return 0;
        float impulse = spell.Push * Forces.ImpulsePerPush;
        return spell.Shape switch
        {
            SpellShapes.Burst => Forces.Radial(caster.position, (spell.Radius + 1) * grid.cellSize, impulse, caster),
            SpellShapes.Line => Forces.Along(grid, grid.World.SpellCells(spell, x, y, dir), Direction(dir), impulse, caster),
            _ => 0,
        };
    }

    // The sandbox's directions on the ground: north (y-1) is +z.
    static Vector3 Direction(int dir) => new Vector3(World.DirX(dir), 0f, -World.DirY(dir));

    static bool Levitate(ChemGrid grid, SpellDef spell, int x, int y)
    {
        var target = grid.CellCenter(x, y) + Vector3.up * grid.SurfaceY(x, y);
        var prop = Forces.Nearest<PhysicsProp>(target, grid.cellSize * 1.5f);
        if (prop == null)
        {
            EventLog.Add($"{spell.Name}: nothing there to lift");
            return false;
        }
        prop.Levitate(spell.Duration);
        return true;
    }
}
