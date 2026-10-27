using System.Collections;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's frost scenario (docs/FAST_LOOP.md): Ray of Frost from the lake's
// shore out over the water, as a player casts it. The wizard stands on the
// dry square nearest the yard with lake water south of it and casts south.
// Checks that the cast shows one beam of frost along the line (FrostBeam: no
// ice sphere, no projectile), that the water froze, that the ice is drawn above the lake's own
// surface (not hidden under it) and that it can be stood on.
public class FrostProbe : ProbeScenario
{
    protected override IEnumerator Run()
    {
        if (!FindShore(out var shore))
        {
            Check("lake_shore_found", false, "no dry square with lake water south of it");
            yield break;
        }
        Check("lake_shore_found", true, $"cell {shore}");
        var yard = grid.yardCells;
        Place(shore.x - yard.x, shore.y - yard.y);
        var target = new Vector2Int(shore.x, shore.y + 3);
        var at = grid.CellCenter(target.x, target.y);
        Cut(at + V(9f, 7f, 9f), at);
        yield return Wait(0.5f);
        Check("bar_has_ray_of_frost", caster.Select(SpellIds.RayOfFrost));
        puppet.Face(at);
        caster.Cast(SpellIds.RayOfFrost, shore.x, shore.y + 1, 2);
        var line = World.SpellCells(World.Rules.Spell(SpellIds.RayOfFrost), shore.x, shore.y + 1, 2);
        // The thrust releases the beam (0.14 s), it reaches the end (0.12 s), then holds 0.4 s.
        yield return Wait(0.4f);
        CheckBeam(line[line.Count - 1]);
        Shot("beam");
        yield return Wait(1.75f);

        int wet = line.Count(c => World.At(c.x, c.y).Water > 0);
        int frozen = line.Count(c => World.At(c.x, c.y).Frozen && World.At(c.x, c.y).Water > 0);
        Metric("line_cells_wet", wet);
        Metric("line_cells_frozen", frozen);
        Check("water_froze", wet > 0 && frozen == wet, $"{frozen}/{wet} wet squares in the beam frozen");

        // The ice: a collider at the frozen squares, at or above the lake's drawn surface.
        var lake = GameObject.Find("Lake");
        float lakeY = lake != null ? lake.transform.position.y : float.NegativeInfinity;
        var ice = line.FirstOrDefault(c => World.At(c.x, c.y).Frozen);
        var top = grid.CellCenter(ice.x, ice.y) + Vector3.up * 30f;
        bool hit = Physics.Raycast(top, Vector3.down, out var h, 60f, ~0, QueryTriggerInteraction.Ignore);
        Metric("ice_y", hit ? h.point.y : -99f);
        Metric("lake_y", lakeY);
        Check("ice_stands_on_lake", hit && h.collider.name == "Ice" && h.point.y >= lakeY - 0.02f,
            hit ? $"ray hits '{h.collider.name}' at {h.point.y:0.00} m, the lake's surface at {lakeY:0.00} m" : "nothing under the ray");
        Shot("ice");

        // Sleet Storm out on the open water: a wide circle of ice.
        var far = new Vector2Int(shore.x, shore.y + 9);
        Check("bar_has_sleet_storm", caster.Select(SpellIds.SleetStorm));
        caster.Cast(SpellIds.SleetStorm, far.x, far.y, 2);
        yield return Wait(2f);
        var circle = World.SpellCells(World.Rules.Spell(SpellIds.SleetStorm), far.x, far.y, 2);
        int iced = circle.Count(c => World.At(c.x, c.y).Frozen && World.At(c.x, c.y).Water > 0);
        Metric("sleet_cells_frozen", iced);
        Check("sleet_storm_freezes_a_circle", iced >= 40, $"{iced} of {circle.Count} squares frozen");
        Cut(grid.CellCenter(far.x, far.y) + V(14f, 12f, 14f), grid.CellCenter(far.x, far.y));
        yield return Wait(0.5f);
        Shot("sleet");
    }

    // While the cast plays: one beam of frost from the wizard's hand to the end of the line, and no
    // ice sphere (the old IceLance impact) or projectile ball anywhere.
    void CheckBeam((int x, int y) end)
    {
        var beam = FindAnyObjectByType<FrostBeam>();
        var endAt = grid.CellCenter(end.x, end.y);
        if (beam != null)
        {
            var off = beam.To - endAt;
            off.y = 0f;
            float fromWizard = Vector3.Distance(beam.From, wizard.transform.position);
            float length = Vector3.Distance(beam.From, beam.To);
            bool lines = beam.GetComponentsInChildren<LineRenderer>().Any(l => l.enabled && l.widthMultiplier > 0.01f);
            bool solid = beam.GetComponentsInChildren<MeshFilter>().Length > 0 || beam.GetComponentsInChildren<Collider>().Length > 0;
            Metric("beam_length_m", length);
            Metric("beam_end_off_m", off.magnitude);
            Check("frost_beam_along_line", beam.Reached >= 1f && off.magnitude < 0.9f && fromWizard < 2.5f && lines && !solid,
                $"reached {beam.Reached:0.00}, {length:0.0} m long, its end {off.magnitude:0.00} m from the line's last square, " +
                $"its start {fromWizard:0.0} m from the wizard, drawn {lines}, mesh or collider {solid}");
        }
        else Check("frost_beam_along_line", false, "no FrostBeam while the cast plays");
        bool lance = FindObjectsByType<Transform>(FindObjectsSortMode.None).Any(t => t.name.StartsWith("IceLance", System.StringComparison.Ordinal));
        int flying = FindObjectsByType<SpellProjectile>(FindObjectsSortMode.None).Length;
        Check("no_ice_sphere_or_projectile", !lance && flying == 0, $"IceLance {(lance ? "shown" : "none")}, {flying} projectile(s)");
    }

    // The dry, open square outside the yard nearest its middle with lake water two squares south.
    bool FindShore(out Vector2Int shore)
    {
        var yard = grid.yardCells;
        var middle = new Vector2Int(yard.x + yard.width / 2, yard.y + yard.height / 2);
        shore = default;
        float best = float.MaxValue;
        for (int y = 0; y < World.H - 3; y++)
            for (int x = 0; x < World.W; x++)
            {
                var c = World.At(x, y);
                if (yard.Contains(new Vector2Int(x, y)) || c.Water > 0 || World.IsBlocking(c) || World.At(x, y + 2).Water == 0) continue;
                float d = (new Vector2Int(x, y) - middle).sqrMagnitude;
                if (y > middle.y && d < best)
                {
                    best = d;
                    shore = new Vector2Int(x, y);
                }
            }
        return best < float.MaxValue;
    }
}
