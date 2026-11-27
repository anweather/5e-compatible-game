using System.Collections;
using Chem;
using UnityEngine;

// A short film of the new casts chaining through the materials (recorded with
// DemoRecorder.RecordShowcase("ChainShowcase") for the demo reel, UX pass 2):
// Forge bakes mud to brick, a Fire Bolt goes through a dust cloud, a Falling
// Star lands, and a whirlwind crosses burning grass into a fire tornado.
public class ChainShowcase : ShowcaseScript
{
    protected override IEnumerator Play()
    {
        yield return Switch(At(12f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': " +
            "{ 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 } }");
        var o = grid.YardCell(24, 30);
        yield return ForgeMud(o);
        yield return DustAndFire(new Vector2Int(o.x + 10, o.y));
        yield return Star(new Vector2Int(o.x + 10, o.y + 9));
        yield return FireTornado(new Vector2Int(o.x, o.y + 8));
    }

    void Stand(Vector2Int o, int w, int h, Vector3 from)
    {
        var mid = Mid(o, w, h);
        var stand = grid.CellAt(mid + V(-6f, 0f, -1.5f));
        PlaceCell(stand.x, stand.y);
        puppet.Face(mid);
        Cut(mid + from, mid + Vector3.up * 0.6f);
    }

    IEnumerator ForgeMud(Vector2Int o)
    {
        Patch(o, 6, 3, TerrainIds.Mud);
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 6; x++) World.At(o.x + x, o.y + y).DampUntil = World.Tick + 600;
        Stand(o, 6, 3, V(-4.5f, 3.6f, -5.5f));
        yield return Wait(1.2f);
        caster.Cast(SpellIds.Forge, o.x + 1, o.y + 1, 1);
        yield return Wait(1.3f);
        caster.Cast(SpellIds.Forge, o.x + 4, o.y + 1, 1);
        yield return Wait(2.2f);
    }

    IEnumerator DustAndFire(Vector2Int o)
    {
        Patch(o, 7, 7, TerrainIds.Dirt);
        Stand(o, 7, 7, V(-7f, 4.5f, -8.5f));
        var hit = new Vector2Int(o.x + 3, o.y + 3);
        yield return Wait(0.6f);
        caster.Cast(SpellIds.DustStorm, hit.x, hit.y, 1);
        yield return Wait(2.2f);
        caster.Cast(SpellIds.FireBolt, hit.x, hit.y, 1);
        yield return Wait(3.5f);
        PutOutFires();
    }

    IEnumerator Star(Vector2Int o)
    {
        Patch(o, 7, 7, TerrainIds.Dirt);
        Stand(o, 7, 7, V(-6.5f, 5f, -7.5f));
        yield return Wait(0.6f);
        caster.Cast(SpellIds.FallingStar, o.x + 3, o.y + 3, 1);
        yield return Wait(4.2f);
        PutOutFires();
    }

    IEnumerator FireTornado(Vector2Int o)
    {
        Patch(o, 5, 5, TerrainIds.Grass);
        for (int y = 0; y < 5; y++)
            for (int x = 0; x < 5; x++) World.Apply(o.x + x, o.y + y, "fire", 3, -1, false);
        Stand(o, 5, 5, V(-8f, 3.2f, -9f));
        yield return Wait(0.8f);
        caster.Cast(SpellIds.Whirlwind, o.x + 2, o.y + 2, 1);
        yield return Wait(5f);
    }
}
