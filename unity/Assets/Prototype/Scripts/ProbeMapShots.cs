using System.Collections;

// The probe's mapshots scenario: the same three views of the valley each time, from the town toward the castle, of the
// castle, and along the shore out to sea, at 11 in the morning, for telling a before from an after when the map's data
// moves (docs/design/maps-as-data.md, phase 7). Checks nothing but that it shot them.
public class MapShotsProbe : ProbeScenario
{
    protected override IEnumerator Run()
    {
        yield return Switch(At(11f));
        PlaceCell(222, 330);
        yield return Wait(2f);
        Look(226, 318, 30f, 140, 336, 6f);
        yield return Wait(3f);
        Shot("town");
        yield return Wait(0.5f);
        Look(176, 384, 22f, 140, 356, 8f);
        yield return Wait(3f);
        Shot("castle");
        yield return Wait(0.5f);
        PlaceCell(398, 398);
        yield return Wait(2f);
        Look(468, 480, 10f, 414, 414, 2f);
        yield return Wait(3f);
        Shot("shore");
        yield return Wait(1f);
        Check("shot", true, "town, castle, shore");
    }
}
