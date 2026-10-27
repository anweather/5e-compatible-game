using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// The probe's blink scenario (docs/FAST_LOOP.md): Misty Step aimed the way a
// player aims it, from the camera through the crosshair, at open ground all
// round the wizard in the meadow, near and far within its range. Tallies what
// it would say about each spot (MistyStep.Check), then blinks to a few of
// them for real (MistyStep.Blink) and checks the wizard arrives.
public class BlinkProbe : ProbeScenario
{
    protected override IEnumerator Run()
    {
        var spell = World.Rules.Spell(SpellIds.MistyStep);
        Check("spell_found", spell != null, SpellIds.MistyStep);
        if (spell == null) yield break;
        var caster = wizard.GetComponent<SpellCaster>();
        var start = wizard.transform.position;
        var cam = Camera.main;
        var why = new Dictionary<string, int>();
        var good = new List<(Vector3 landing, Vector2Int cell, Vector3 home)>();
        int tried = 0, exact = 0;
        // From the yard, and out in the meadow by the trees.
        foreach (var home in new[] { start, new Vector3(27f, grid.GroundAt(new Vector3(27f, 0f, 26f)) + 0.05f, 26f) })
        {
            Teleport(home);
            yield return null;
            yield return null;
            // Every direction, from a few metres out to past the range, as a player aiming across the open valley does (those land short).
            float reach = spell.Range * grid.cellSize * 2f;
            for (int a = 0; a < 16; a++)
                for (float d = 3f; d <= reach; d += 4.5f)
                {
                    var dir = Quaternion.Euler(0f, a * 22.5f, 0f) * Vector3.forward;
                    var at = home + dir * d;
                    at.y = grid.GroundAt(at);
                    // The camera stands behind the wizard looking at the spot, as a player turns to aim.
                    cam.transform.position = home - dir * 4f + Vector3.up * 2.2f;
                    cam.transform.LookAt(at);
                    if (!Aiming.PastSelf(new Ray(cam.transform.position, at - cam.transform.position), wizard.transform, out var hit)) { Tally(why, "the crosshair hit nothing"); continue; }
                    tried++;
                    if (MistyStep.Check(grid, spell, wizard.transform, hit, out _) == null) exact++;
                    string said = MistyStep.Reach(grid, spell, wizard.transform, hit, out var landing);
                    Tally(why, said == null ? "ok" : $"{said} (hit {hit.collider.name})");
                    if (said == null) good.Add((landing, grid.CellAt(landing), home));
                }
        }
        int ok = why.TryGetValue("ok", out var n) ? n : 0;
        Metric("blink_ok_share", tried > 0 ? (double)ok / tried : 0);
        Check("aims_land", tried > 0 && ok >= tried * 0.8f,
            $"{ok}/{tried} spots ({exact} on the exact spot aimed at): " + string.Join("; ", why.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Value}x {kv.Key}")));

        // And for real, a few of them: the cast's own path (BlinkPoint, then Blink re-checking).
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
        Teleport(start);
    }

    static void Tally(Dictionary<string, int> into, string key) => into[key] = into.TryGetValue(key, out var n) ? n + 1 : 1;
}
