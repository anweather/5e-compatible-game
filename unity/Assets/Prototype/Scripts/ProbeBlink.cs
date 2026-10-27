using Chem;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// The probe's blink scenario (docs/FAST_LOOP.md): Misty Step aimed the way a
// player aims it, from the camera through the crosshair, at open ground all
// round the wizard, in the yard and out in the meadow, near and far, past its
// range too. Tallies what it would do with each aim (MistyStep.Reach), then
// blinks to a few of the spots for real (MistyStep.Blink) and checks the
// wizard arrives.
public class BlinkProbe : ProbeScenario
{
    SpellDef spell;
    readonly Dictionary<string, int> why = new();
    readonly List<(Vector3 landing, Vector2Int cell, Vector3 home)> good = new();
    int tried, exact;

    protected override IEnumerator Run()
    {
        spell = World.Rules.Spell(SpellIds.MistyStep);
        Check("spell_found", spell != null, SpellIds.MistyStep);
        if (spell == null) yield break;
        var start = wizard.transform.position;
        // From the yard, and out in the meadow by the trees.
        foreach (var home in new[] { start, new Vector3(27f, grid.GroundAt(new Vector3(27f, 0f, 26f)) + 0.05f, 26f) })
        {
            Teleport(home);
            yield return null;
            yield return null;
            AimAllRound(home);
        }
        int ok = why.TryGetValue("ok", out var n) ? n : 0;
        Metric("blink_ok_share", tried > 0 ? (double)ok / tried : 0);
        Check("aims_land", tried > 0 && ok >= tried * 0.8f,
            $"{ok}/{tried} aims land ({exact} on the exact spot aimed at): " + string.Join("; ", why.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Value}x {kv.Key}")));
        yield return BlinkForReal();
        Teleport(start);
    }

    // Every direction, from a few metres out to twice the range, as a player aiming across the open
    // valley does (those land short). The camera stands behind the wizard looking at the spot, as a
    // player turns to aim.
    void AimAllRound(Vector3 home)
    {
        var cam = Camera.main.transform;
        float reach = spell.Range * grid.cellSize * 2f;
        for (int a = 0; a < 16; a++)
            for (float d = 3f; d <= reach; d += 4.5f)
            {
                var dir = Quaternion.Euler(0f, a * 22.5f, 0f) * Vector3.forward;
                var at = home + dir * d;
                at.y = grid.GroundAt(at);
                cam.position = home - dir * 4f + Vector3.up * 2.2f;
                cam.LookAt(at);
                Aim(new Ray(cam.position, at - cam.position), home);
            }
    }

    void Aim(Ray ray, Vector3 home)
    {
        if (!Aiming.PastSelf(ray, wizard.transform, out var hit))
        {
            Tally("the crosshair hit nothing");
            return;
        }
        tried++;
        if (MistyStep.Check(grid, spell, wizard.transform, hit, out _) == null) exact++;
        string said = MistyStep.Reach(grid, spell, wizard.transform, hit, out var landing);
        Tally(said == null ? "ok" : $"{said} (hit {hit.collider.name})");
        if (said == null) good.Add((landing, grid.CellAt(landing), home));
    }

    // A few of the good spots, the cast's own way: BlinkPoint, then Blink checking again.
    IEnumerator BlinkForReal()
    {
        var caster = wizard.GetComponent<SpellCaster>();
        int arrived = 0, blinks = 0;
        var log = new List<string>();
        foreach (var (landing, cell, home) in good.Where((_, i) => i % 7 == 0).Take(6))
        {
            Teleport(home);
            yield return null;
            blinks++;
            caster.BlinkPoint = landing;
            int mark = ConsoleMark;
            bool moved = MistyStep.Blink(grid, spell, wizard.transform, cell.x, cell.y);
            yield return null;
            float off = Vector3.Distance(wizard.transform.position, landing);
            if (moved && off < 0.6f) arrived++;
            else log.Add($"{cell}: moved {moved}, {off:0.0} m off, {string.Join(" / ", ConsoleSince(mark))}");
        }
        Check("blinks_arrive", blinks > 0 && arrived == blinks, $"{arrived}/{blinks} arrived. " + string.Join("; ", log));
    }

    void Tally(string key) => why[key] = why.TryGetValue(key, out var n) ? n + 1 : 1;
}
