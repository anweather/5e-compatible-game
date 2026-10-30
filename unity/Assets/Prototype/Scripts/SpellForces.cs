using System.Linq;
using Chem;
using UnityEngine;

// What a spell does to things that move, beside what it does to cells (that
// is World.Cast): Thunderwave throws everything around the caster away from
// it, Gust of Wind shoves everything along its line, Levitate lifts the prop
// nearest its target, Misty Step moves the caster (MistyStep), Mage Hand
// fetches or sets down something light (MageHand) and Shapeshift gives the
// caster an animal's shape (Shapeshift). A spell with damage hurts the
// wizards in its squares (Vitals, which an animal shape takes first). The
// strengths are the spell's push and duration in rules/spells.json (see
// Forces for the units). A spell with an element and no push catches the
// wizards in its squares (Hit): fire throws them back from the blast, frost
// slows them, lightning jolts (a moment's stop and a knock), water shoves;
// a charged cast hits that much harder. Enough to matter, not to stop the fun.
// Held, every one is stronger: a push half again as hard per level (a burst
// reaching its grown squares), Levitate up longer and higher, Misty Step and
// Mage Hand further (MistyStep.Range, MageHand.Range).
//
// Solo: SpellCaster calls Apply when a cast lands, and it moves everything.
// Online: NetChem calls ApplyOnline on every machine when the cast's tick
// batch runs. Physics props move only on the host (clients' props follow the
// host's), and each player moves only their own wizard (owner-authoritative
// movement), from the caster's position carried in the cast.
public static class SpellForces
{
    /// <summary>Solo: applies the forces of a cast that just landed to everything but the caster. Returns how many things it moved.</summary>
    public static int Apply(ChemGrid grid, string spellId, int x, int y, int dir, Transform caster, int charge = 1) =>
        Apply(grid, new CastCommand(spellId, x, y, dir, charge), caster);

    /// <summary>Solo: as above, for a cast that may be aimed off the grid's four ways.</summary>
    public static int Apply(ChemGrid grid, CastCommand cast, Transform caster) =>
        Apply(grid, cast, caster.position, caster, null, levitate: true, caster);

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
        int charge = World.ClampCharge(cast.Charge);
        var moved = MovesSomeone(grid, spell, cast, levitate, ownCaster);
        if (moved.HasValue) return moved.Value;
        HurtWizards(grid, spell, cast, except, only);
        if (spell.ForceThrow > 0 && spell.Shape == SpellShapes.Burst) return Jet(grid, spell, cast, except, only);
        if (spell.Push <= 0) return HitWizards(grid, spell, cast, except, only);
        // Held, a push is half again as hard per level, and a burst reaches as far as its grown squares.
        float impulse = spell.Push * Forces.ImpulsePerPush * (1f + 0.5f * (charge - 1));
        return spell.Shape switch
        {
            SpellShapes.Burst => Forces.Radial(from, (spell.Radius + World.ChargedGrowth(spell, charge) + 1) * grid.cellSize, impulse, except, only),
            SpellShapes.Line => Forces.Along(grid, grid.World.SpellCells(spell, x, y, dir, cast.Charge, cast.AimX, cast.AimY), Along(cast), impulse, except, only),
            _ => 0,
        };
    }

    // How hard a jet throws, in impulses and in a wizard's m/s, for each tick of its force.throw.
    const float JetImpulse = 55f, JetSpeed = 1.5f;

    // A Geyser's jet throws what's on its water straight up; on dry ground it fizzled and throws nothing.
    static int Jet(ChemGrid grid, SpellDef spell, CastCommand cast, Transform except, System.Predicate<IPushable> only)
    {
        var c = grid.World.At(cast.X, cast.Y);
        if (c == null || (c.Water == 0 && c.Gas != GasIds.Steam)) return 0;
        var at = grid.CellCenter(cast.X, cast.Y) + Vector3.up * grid.SurfaceY(cast.X, cast.Y);
        float reach = (spell.Radius + World.ChargedGrowth(spell, World.ClampCharge(cast.Charge)) + 1) * grid.cellSize;
        return Forces.Up(at, reach, spell.ForceThrow * JetImpulse, spell.ForceThrow * JetSpeed, except, only);
    }

    // The spells that move or change one thing rather than push: Levitate (a prop), Misty Step and
    // Mage Hand (the caster's own), Shapeshift (the caster becomes an animal); null for the rest.
    static int? MovesSomeone(ChemGrid grid, SpellDef spell, CastCommand cast, bool levitate, Transform ownCaster)
    {
        int charge = World.ClampCharge(cast.Charge);
        return spell.Effect switch
        {
            SpellShapes.LevitateEffect => levitate && Levitate(grid, spell, cast.X, cast.Y, charge) ? 1 : 0,
            SpellShapes.TeleportEffect => ownCaster != null && MistyStep.Blink(grid, spell, ownCaster, cast.X, cast.Y, charge) ? 1 : 0,
            SpellShapes.CarryEffect => ownCaster != null && MageHand.Cast(grid, spell, ownCaster, cast.X, cast.Y, charge) ? 1 : 0,
            SpellShapes.ShapeshiftEffect => Shift(ownCaster, spell, cast),
            _ => null,
        };
    }

    // Shapeshift: the caster's own wizard takes the animal's shape (on the caster's machine only).
    static int Shift(Transform ownCaster, SpellDef spell, CastCommand cast) =>
        ownCaster != null && ownCaster.TryGetComponent<Shapeshift>(out var shape) && shape.Land(spell, cast.X, cast.Y, cast.Charge) ? 1 : 0;

    // The wizards standing in the spell's squares (not its caster) feel its element.
    static int HitWizards(ChemGrid grid, SpellDef spell, CastCommand cast, Transform except, System.Predicate<IPushable> only)
    {
        if (spell.Element == null) return 0;
        var centre = grid.CellCenter(cast.X, cast.Y);
        int hit = 0;
        foreach (var wizard in WizardsIn(grid, spell, cast, except, only))
            if (Hit(wizard, spell, centre, Along(cast), World.ClampCharge(cast.Charge))) hit++;
        return hit;
    }

    // A spell with damage hurts the wizards in its squares (not its caster): its dice's average,
    // times the charge (Vitals; an animal shape takes it first).
    static void HurtWizards(ChemGrid grid, SpellDef spell, CastCommand cast, Transform except, System.Predicate<IPushable> only)
    {
        if (spell.Damage == null || spell.Damage.Length < 2) return;
        float damage = spell.Damage[0] * (spell.Damage[1] + 1) * 0.5f * World.ClampCharge(cast.Charge);
        foreach (var wizard in WizardsIn(grid, spell, cast, except, only))
            if (wizard.TryGetComponent<Vitals>(out var vitals)) vitals.TakeDamage(damage);
    }

    // The wizards (this machine's to move, with `only`) standing in a cast's squares, not `except`.
    static System.Collections.Generic.List<Knockback> WizardsIn(ChemGrid grid, SpellDef spell, CastCommand cast, Transform except, System.Predicate<IPushable> only)
    {
        var found = new System.Collections.Generic.List<Knockback>();
        var cells = new System.Collections.Generic.HashSet<(int x, int y)>(grid.World.SpellCells(spell, cast.X, cast.Y, cast.Dir, cast.Charge, cast.AimX, cast.AimY));
        foreach (var wizard in Knockback.All.ToArray())
        {
            if (wizard == null || (except != null && wizard.transform == except) || (only != null && !only(wizard))) continue;
            var at = grid.CellAt(wizard.transform.position);
            if (cells.Contains((at.x, at.y))) found.Add(wizard);
        }
        return found;
    }

    // What an element does to a wizard it catches.
    static bool Hit(Knockback wizard, SpellDef spell, Vector3 centre, Vector3 along, int charge)
    {
        var away = wizard.transform.position - centre;
        away.y = 0f;
        away = away.sqrMagnitude > 0.01f ? away.normalized : along;
        wizard.TryGetComponent<SurfaceEffects>(out var feet);
        bool me = wizard.TryGetComponent<PlayerControls>(out var controls) && controls.enabled;
        switch (spell.Element)
        {
            case "fire":
                wizard.Push(1.5f * Forces.ImpulsePerPush * charge * away);
                if (me) EventLog.Add($"{spell.Name} throws you back");
                return true;
            case "cold":
                if (feet != null) feet.Slow(0.45f, 2.5f * charge);
                if (me) EventLog.Add($"{spell.Name} chills you: slowed");
                return true;
            case "lightning":
                if (feet != null) feet.Slow(0.05f, 0.35f * charge);
                wizard.Push(Forces.ImpulsePerPush * charge * away);
                if (me) EventLog.Add($"{spell.Name} jolts you");
                return true;
            case "water":
                wizard.Push(Forces.ImpulsePerPush * charge * along);
                if (me) EventLog.Add($"{spell.Name} knocks you about");
                return true;
            default:
                return false;
        }
    }

    // The sandbox's directions on the ground: north (y-1) is +z.
    static Vector3 Direction(int dir) => new Vector3(World.DirX(dir), 0f, -World.DirY(dir));

    // Which way a cast points on the ground: along its aim when it has one off the grid's four ways.
    static Vector3 Along(CastCommand cast)
    {
        var (ax, ay) = World.SnapAim(cast.AimX, cast.AimY);
        return ax != 0 && ay != 0 ? new Vector3(ax, 0f, -ay).normalized : Direction(cast.Dir);
    }

    // Held, it stays up that many times as long and rises half again as fast per level.
    static bool Levitate(ChemGrid grid, SpellDef spell, int x, int y, int charge)
    {
        var target = grid.CellCenter(x, y) + Vector3.up * grid.SurfaceY(x, y);
        var prop = Forces.Nearest<PhysicsProp>(target, grid.cellSize * 1.5f);
        if (prop == null)
        {
            EventLog.Add($"{spell.Name}: nothing there to lift");
            return false;
        }
        prop.Levitate(spell.Duration * charge, 1f + 0.5f * (charge - 1));
        return true;
    }
}
