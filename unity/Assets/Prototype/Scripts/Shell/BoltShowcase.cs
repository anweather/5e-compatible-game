using System.Collections;
using Chem;
using UnityEngine;

// A short film of the day's casts chaining (recorded with
// DemoRecorder.RecordShowcase("BoltShowcase") for the demo reel, UX pass 3):
// Lightning jumps to a puddle and runs through it to a coil that opens a gate, a
// boulder rolls through glass, and a wave carries a crate into the mud it leaves.
public class BoltShowcase : ShowcaseScript
{
    protected override IEnumerator Play()
    {
        yield return Switch(At(12f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': " +
            "{ 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 } }");
        var o = grid.YardCell(24, 30);
        yield return BoltToGate(o);
        yield return BoulderThroughGlass(new Vector2Int(o.x, o.y + 8));
    }

    void Stand(Vector2Int o, int w, int h, Vector3 from)
    {
        var mid = Mid(o, w, h);
        var stand = grid.CellAt(mid + V(-8f, 0f, -1.5f));
        PlaceCell(stand.x, stand.y);
        puppet.Face(mid);
        Cut(mid + from, mid + V(2f, 0.5f, 0f));
    }

    IEnumerator BoltToGate(Vector2Int o)
    {
        Patch(o, 13, 6, TerrainIds.Dirt);
        for (int x = 3; x < 11; x++)
        {
            World.At(o.x + x, o.y + 2).Water = 3;
            World.At(o.x + x, o.y + 3).Water = 3;
        }
        World.Paint(o.x + 11, o.y + 2, TerrainIds.Coil);
        for (int y = 0; y < 6; y++) World.Paint(o.x + 12, o.y + y, TerrainIds.Gate);
        Stand(o, 13, 6, V(-8f, 5f, -8f));
        yield return Wait(1.4f);
        caster.Cast(SpellIds.LightningBolt, o.x + 1, o.y + 2, 1);
        yield return Wait(4.5f);
        for (int y = 2; y < 4; y++)
            for (int x = 3; x < 11; x++) World.At(o.x + x, o.y + y).Water = 0;
    }

    IEnumerator BoulderThroughGlass(Vector2Int o)
    {
        Patch(o, 14, 5, TerrainIds.Dirt);
        Patch(new Vector2Int(o.x + 8, o.y), 1, 5, TerrainIds.Glass);
        Stand(o, 14, 5, V(-6f, 3.5f, -9f));
        yield return Wait(0.8f);
        caster.Cast(SpellIds.RollingBoulder, o.x, o.y + 2, 1);
        yield return Wait(5f);
        caster.Cast(SpellIds.RollingBoulder, o.x, o.y + 3, 1);
        yield return Wait(5f);
    }
}
