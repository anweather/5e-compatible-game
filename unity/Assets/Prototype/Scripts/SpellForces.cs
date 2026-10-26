using Chem;
using UnityEngine;

// What a spell does to things that move, beside what it does to cells (that
// is World.Cast): Thunderwave throws everything around the caster away from
// it, Gust of Wind shoves everything along its line, Levitate lifts the prop
// nearest its target, Misty Step moves the caster (MistyStep) and Mage Hand
// fetches or sets down something light (MageHand). The
// strengths are the spell's push and duration in rules/spells.json (see
// Forces for the units).
//
// Solo: SpellCaster calls Apply when a cast lands, and it moves everything.
// Online: NetChem calls ApplyOnline on every machine when the cast's tick
// batch runs. Physics props move only on the host (clients' props follow the
// host's), and each player moves only their own wizard (owner-authoritative
// movement), from the caster's position carried in the cast.
public static class SpellForces
{
    /// <summary>Solo: applies the forces of a cast that just landed to everything but the caster. Returns how many things it moved.</summary>
    public static int Apply(ChemGrid grid, string spellId, int x, int y, int dir, Transform caster) =>
        Apply(grid, new CastCommand(spellId, x, y, dir), caster.position, caster, null, levitate: true, caster);

    /// <summary>
    /// Online: the forces of a cast whose tick batch just ran. <paramref name="props"/> on the host
    /// moves the physics props; <paramref name="wizard"/>, the local player's wizard unless they cast it,
    /// is shoved too; <paramref name="ownCaster"/>, the local player's wizard when they cast it, is the
    /// one a spell that moves its caster moves. Returns how many things it moved here.
    /// </summary>
    public static int ApplyOnline(ChemGrid grid, CastCommand cast, Vector3 from, bool props, Knockback wizard, Transform ownCaster)
    {
        bool Mine(IPushable p) => (props && p is PhysicsProp) || (wizard != null && ReferenceEquals(p, wizard));
        return Apply(grid, cast, from, null, Mine, levitate: props, ownCaster);
    }

    static int Apply(ChemGrid grid, CastCommand cast, Vector3 from, Transform except, System.Predicate<IPushable> only, bool levitate, Transform ownCaster)
    {
        var (x, y, dir) = (cast.X, cast.Y, cast.Dir);
        var spell = grid.World.Rules.Spell(cast.Spell);
        if (spell == null) return 0;
        if (spell.Effect == SpellShapes.LevitateEffect) return levitate && Levitate(grid, spell, x, y) ? 1 : 0;
        if (spell.Effect == SpellShapes.TeleportEffect) return ownCaster != null && MistyStep.Blink(grid, spell, ownCaster, x, y) ? 1 : 0;
        if (spell.Effect == SpellShapes.CarryEffect) return ownCaster != null && MageHand.Cast(grid, spell, ownCaster, x, y) ? 1 : 0;
        if (spell.Push <= 0) return 0;
        float impulse = spell.Push * Forces.ImpulsePerPush;
        return spell.Shape switch
        {
            SpellShapes.Burst => Forces.Radial(from, (spell.Radius + 1) * grid.cellSize, impulse, except, only),
            SpellShapes.Line => Forces.Along(grid, grid.World.SpellCells(spell, x, y, dir), Direction(dir), impulse, except, only),
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
