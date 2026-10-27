using System.Collections;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's charges scenario (docs/FAST_LOOP.md): every spell is stronger
// held. Casts a few spells through the wizard's caster at a tap (charge 1)
// and held to the full (World.MaxCharge) and compares what each did: Create
// Water's water and Grease's slick in the cells, Ray of Frost's beam (its
// length), Thunderwave's shove on the crate, Levitate's lift; and the reach
// of Misty Step and Mage Hand. Also lists every spell's squares at both
// charges as metrics, and checks every spell on the bar charges.
public class ChargesProbe : ProbeScenario
{
    const int Held = World.MaxCharge;

    protected override IEnumerator Run()
    {
        var spells = World.Rules.Spells.ToList();
        Check("every_spell_chargeable", spells.All(SpellCaster.Chargeable),
            string.Join(", ", spells.Where(s => !SpellCaster.Chargeable(s)).Select(s => s.Id)));
        foreach (var s in spells)
        {
            Metric($"cells_{s.Id}_tap", World.SpellCells(s, 40, 40, 1, 1).Count);
            Metric($"cells_{s.Id}_held", World.SpellCells(s, 40, 40, 1, Held).Count);
        }

        Place(4, 10);
        yield return Wait(0.5f);
        Compare("create_water", Cells(SpellIds.CreateWater, 6, 13, 1), Cells(SpellIds.CreateWater, 13, 13, Held), "water units");
        Compare("grease", Cells(SpellIds.Grease, 6, 17, 1), Cells(SpellIds.Grease, 13, 17, Held), "greased squares");
        yield return Beams();
        yield return Thunderwaves();
        yield return Levitates();
        Compare("misty_step_reach", MistyStep.Range(World.Rules.Spell(SpellIds.MistyStep), 1), MistyStep.Range(World.Rules.Spell(SpellIds.MistyStep), Held), "squares");
        var hand = World.Rules.Spell(SpellIds.MageHand);
        Compare("mage_hand_reach", MageHand.Range(hand, 1), MageHand.Range(hand, Held), "squares");
        Compare("mage_hand_lift", (float)MageHand.MaxMass(hand, 1), (float)MageHand.MaxMass(hand, Held), "kg");
    }

    // A tap against a held cast: the held one does half as much again or more.
    void Compare(string name, float tap, float held, string unit)
    {
        Metric($"{name}_tap", tap);
        Metric($"{name}_held", held);
        Check($"{name}_stronger_held", tap > 0f && held >= tap * 1.5f, $"{tap:0.##} {unit} tapped, {held:0.##} held");
    }

    // Casts a cell spell at a yard cell and returns how much it left in its held footprint at once
    // (it lands at once without a projectile): water units for water, else squares with its cover.
    float Cells(string spellId, int x, int y, int charge)
    {
        var spell = World.Rules.Spell(spellId);
        var at = grid.YardCell(x, y);
        var area = World.SpellCells(spell, at.x, at.y, 1, Held);
        float Measure() => spell.Element == "water"
            ? area.Sum(c => World.At(c.x, c.y).Water)
            : area.Count(c => World.At(c.x, c.y).Cover == spell.Element);
        float before = Measure();
        caster.Select(spellId);
        caster.Cast(new CastCommand(spellId, at.x, at.y, 1, charge));
        return Measure() - before;
    }

    // Ray of Frost from beside the wizard along its longest open way, tapped then held: the beam's length.
    IEnumerator Beams()
    {
        var spell = World.Rules.Spell(SpellIds.RayOfFrost);
        var me = grid.CellAt(wizard.transform.position);
        int dir = Enumerable.Range(0, 4).OrderByDescending(d => World.SpellCells(spell, me.x + World.DirX(d), me.y + World.DirY(d), d, Held).Count).First();
        float[] lengths = new float[2];
        for (int i = 0; i < 2; i++)
        {
            caster.Select(SpellIds.RayOfFrost);
            caster.Cast(new CastCommand(SpellIds.RayOfFrost, me.x + World.DirX(dir), me.y + World.DirY(dir), dir, i == 0 ? 1 : Held));
            yield return Wait(0.25f);
            var beam = FindObjectsByType<FrostBeam>(FindObjectsSortMode.None).FirstOrDefault(b => b.enabled);
            lengths[i] = beam != null ? Vector3.Distance(beam.From, beam.To) : 0f;
            yield return Wait(1.2f);
        }
        Compare("ray_of_frost_beam_m", lengths[0], lengths[1], "m");
    }

    // Thunderwave beside the crate (as the smoke probe casts it), tapped then held from the same side: how far it went.
    IEnumerator Thunderwaves()
    {
        Place(15, 6);
        yield return null;
        var crate = Nearest(wizard.transform.position, null);
        if (crate == null)
        {
            Check("thunderwave_stronger_held", false, "no prop near yard (15, 6)");
            yield break;
        }
        var offset = wizard.transform.position - crate.transform.position;
        offset.y = 0f;
        float[] moved = new float[2];
        for (int i = 0; i < 2; i++)
        {
            var at = crate.transform.position + offset;
            at.y = grid.GroundAt(at) + 0.05f;
            Teleport(at);
            yield return Wait(0.2f);
            var from = crate.transform.position;
            var me = grid.CellAt(wizard.transform.position);
            caster.Select(SpellIds.Thunderwave);
            caster.Cast(new CastCommand(SpellIds.Thunderwave, me.x, me.y, 1, i == 0 ? 1 : Held));
            yield return Wait(2.5f);
            var d = crate.transform.position - from;
            d.y = 0f;
            moved[i] = d.magnitude;
        }
        Compare("thunderwave_shove_m", moved[0], moved[1], "m");
    }

    // Levitate on two props near yard (17, 6), the first tapped, the second held: how high each rose in 2 s.
    IEnumerator Levitates()
    {
        var near = grid.YardCellCenter(17, 6);
        var first = Nearest(near, null);
        var second = first != null ? Nearest(first.transform.position, first) : null;
        if (second == null)
        {
            Check("levitate_stronger_held", false, "fewer than two props near yard (17, 6)");
            yield break;
        }
        float[] rose = new float[2];
        var props = new[] { first, second };
        for (int i = 0; i < 2; i++)
        {
            var prop = props[i];
            var cell = grid.CellAt(prop.transform.position);
            float start = prop.transform.position.y, top = start;
            caster.Select(SpellIds.Levitate);
            caster.Cast(new CastCommand(SpellIds.Levitate, cell.x, cell.y, 1, i == 0 ? 1 : Held));
            for (float t = 0f; t < 2f; t += Time.deltaTime)
            {
                top = Mathf.Max(top, prop.transform.position.y);
                yield return null;
            }
            rose[i] = top - start;
        }
        Compare("levitate_rise_m", rose[0], rose[1], "m");
    }

    // The free-standing physics prop nearest a point (not <paramref name="except"/>), or null.
    static PhysicsProp Nearest(Vector3 at, PhysicsProp except) =>
        FindObjectsByType<PhysicsProp>(FindObjectsSortMode.None)
            .Where(p => p != except && p.State != null && p.TryGetComponent<Rigidbody>(out var body) && !body.isKinematic)
            .OrderBy(p => (p.transform.position - at).sqrMagnitude)
            .FirstOrDefault();
}
