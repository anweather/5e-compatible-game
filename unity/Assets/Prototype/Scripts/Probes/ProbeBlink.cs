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
// wizard arrives. Then casts it the whole way, as a player's cast goes
// (SpellCaster.Select and Cast, with the mist shown at both ends): six times
// in a row two frames apart, one of them straight after a Thunderwave, as a
// player spamming it does; and held to full charge, which reaches further.
public class BlinkProbe : ProbeScenario
{
    const int SpamCasts = 6;

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
        yield return null;
        yield return null;
        yield return CastInARow();
        Teleport(start);
        yield return null;
        yield return null;
        yield return CastCharged();
        Teleport(start);
    }

    // Misty Step cast through the caster again and again, each two frames after the last, so the mist
    // of the one before is still there: out, then back onto the spot it just left (its mist), turning
    // a quarter each pair. The fourth comes straight after a Thunderwave.
    IEnumerator CastInARow()
    {
        Check("bar_has_misty_step", caster.Select(SpellIds.MistyStep));
        int arrived = 0, casts = 0;
        var log = new List<string>();
        for (int i = 0; i < SpamCasts; i++)
        {
            if (i == 3)
            {
                var me = grid.CellAt(wizard.transform.position);
                caster.Select(SpellIds.Thunderwave);
                caster.Cast(SpellIds.Thunderwave, me.x, me.y, 1);
                caster.Select(SpellIds.MistyStep);
            }
            var dir = Quaternion.Euler(0f, i / 2 * 90f + i % 2 * 180f + 15f, 0f) * Vector3.forward;
            if (!Spot(wizard.transform.position, dir, 4f * grid.cellSize, 1, out var landing))
            {
                log.Add($"#{i}: nowhere to aim at");
                continue;
            }
            casts++;
            int mark = ConsoleMark;
            CastBlink(landing, 1);
            yield return null;
            yield return null;
            float off = Vector3.Distance(wizard.transform.position, landing);
            if (off < 0.6f) arrived++;
            else
            {
                log.Add($"#{i}: {off:0.0} m off, {string.Join(" / ", ConsoleSince(mark))}");
            }
        }
        Metric("spam_casts", casts);
        Metric("spam_arrived", arrived);
        Check("spam_casts_arrive", casts >= SpamCasts - 1 && arrived == casts, $"{arrived}/{casts} cast in a row arrived. " + string.Join("; ", log));
    }

    // The same aim, past a tap's range, at a tap and held to full charge: the held one lands further, and arrives.
    IEnumerator CastCharged()
    {
        var from = wizard.transform.position;
        float far = (spell.Range + 6) * grid.cellSize;
        for (int a = 0; a < 16; a++)
        {
            var dir = Quaternion.Euler(0f, a * 22.5f, 0f) * Vector3.forward;
            if (!Spot(from, dir, far, 1, out var tap) || !Spot(from, dir, far, World.MaxCharge, out var held)) continue;
            float tapped = Flat(tap - from).magnitude, charged = Flat(held - from).magnitude;
            if (charged < tapped + 3f) continue;
            Metric("blink_tap_m", tapped);
            Metric("blink_held_m", charged);
            int mark = ConsoleMark;
            CastBlink(held, World.MaxCharge);
            yield return null;
            yield return null;
            float off = Vector3.Distance(wizard.transform.position, held);
            Check("held_blink_reaches_further", off < 0.6f,
                $"a tap lands {tapped:0.0} m out, held {charged:0.0} m; the held cast arrived {off:0.0} m off {string.Join(" / ", ConsoleSince(mark))}");
            yield break;
        }
        Check("held_blink_reaches_further", false, $"no aim {far:0} m out landed further held than tapped");
    }

    // A cast as SpellCaster.Update makes it on release: the spot found from the crosshair, then the cast in its square.
    void CastBlink(Vector3 landing, int charge)
    {
        caster.BlinkPoint = landing;
        var cell = grid.CellAt(landing);
        caster.Cast(new CastCommand(SpellIds.MistyStep, cell.x, cell.y, 1, charge));
    }

    // Where an aim at the ground <paramref name="distance"/> metres off along <paramref name="dir"/> would land, at a charge.
    bool Spot(Vector3 home, Vector3 dir, float distance, int charge, out Vector3 landing)
    {
        landing = default;
        var at = home + dir * distance;
        at.y = grid.GroundAt(at);
        var eye = home - dir * 4f + Vector3.up * 2.2f;
        return Aiming.PastSelf(new Ray(eye, at - eye), wizard.transform, out var hit)
            && MistyStep.Reach(grid, spell, wizard.transform, hit, out landing, charge) == null;
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

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
