using System.Collections;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's snow scenario (docs/FAST_LOOP.md): heavy snow at noon (fast
// settling, a probe mod over sim.json), then the wizard walks through it and
// a Fire Bolt lands in it. Checks that snow lies on the ground and drifts into
// banks against walls and trees, that a bank raises the ground, that the walk
// leaves footprints, and that the bolt melts the snow rather than lighting the
// grass. Shots of the snowy yard, the footprints and a bank.
public class SnowProbe : ProbeScenario
{
    static bool Snowy(Cell c) => CellPaint.IsSnowy(c);

    protected override IEnumerator Run()
    {
        Place(13, 10);
        Cut(V(40f, 10f, 1f), V(28f, 0f, 15f));
        yield return Switch(At(12f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, " +
            "'weights': { 'clear': 0, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 1 }, " +
            "'snow': { 'freezeEvery': 2, 'coverChance': 0.5, 'bankChance': 0.5 } }");
        yield return Wait(10f);

        int snowy = World.Cells.Count(Snowy);
        int banks = World.Cells.Count(c => c.Cover == World.BankCover);
        Metric("snowy_squares", snowy);
        Metric("banks", banks);
        Check("snow_lies", snowy > 500, $"{snowy} squares under snow");
        Check("banks_drift", banks > 20, $"{banks} snow banks against walls and trees");

        // A bank stands over the ground beside it (the terrain eases toward its new height).
        var bank = BankCell();
        if (bank.HasValue)
        {
            var (bx, by) = bank.Value;
            var at = grid.CellCenter(bx, by);
            float rise = grid.GroundAt(at) - grid.GroundTop(bx, by, World.At(bx, by));
            Metric("bank_rise_m", rise);
            Check("bank_raises_ground", rise > 0.25f, $"the ground stands {rise:0.00} m over a bank's square");
            Cut(at + V(-3f, 2.5f, -4f), at);
            yield return Wait(0.3f);
            Shot("bank");
            yield return null;
            yield return null;
        }
        else Check("bank_raises_ground", false, "no bank found in the open");

        Shot("yard");
        yield return null;
        yield return null;

        // The wizard walks south through the yard's snow.
        var from = wizard.transform.position;
        yield return WalkFor(13, 3, 3f);
        var prints = FindFirstObjectByType<Footprints>();
        int count = prints != null ? prints.WizardPrints : 0;
        Metric("footprints", count);
        Check("footprints", count >= 4, $"{count} prints along {Vector3.Distance(from, wizard.transform.position):0.0} m of walking");
        var mid = (from + wizard.transform.position) / 2f;
        Cut(mid + V(3f, 3f, 0f), mid);
        yield return Wait(0.4f);
        Shot("footprints");
        yield return null;
        yield return null;

        // A Fire Bolt into the snow: it melts where it lands; nothing catches.
        var target = grid.YardCell(20, 8);
        bool before = Snowy(World.At(target.x, target.y));
        yield return CastFromBar(SpellIds.FireBolt, 20, 8, 0, 1f);
        var hit = World.At(target.x, target.y);
        Check("fire_melts_snow", before && !Snowy(hit) && hit.Fire == 0,
            $"snow before {before}, after {Snowy(hit)} (cover {hit.Cover ?? "none"}), water {hit.Water}, fire {hit.Fire}");
    }

    // A snow bank on open ground (a wall or tree beside it), nearest the yard.
    (int x, int y)? BankCell()
    {
        var yard = grid.YardCell(13, 6);
        (int x, int y)? best = null;
        int bestD = int.MaxValue;
        for (int y = yard.y - 40; y <= yard.y + 40; y++)
            for (int x = yard.x - 40; x <= yard.x + 40; x++)
            {
                var c = World.At(x, y);
                if (c == null || c.Cover != World.BankCover) continue;
                int d = (x - yard.x) * (x - yard.x) + (y - yard.y) * (y - yard.y);
                if (d < bestD) (best, bestD) = ((x, y), d);
            }
        return best;
    }
}
