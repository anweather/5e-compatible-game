using System.Collections;
using UnityEngine;

// A short film of fire from above, for the trailer page (Prototype > Record
// Showcase, DemoRecorder):
//
//   1. Fireball: it flies to the meadow east of the yard and goes off with a
//      bang, throwing a crater out of the ground.
//   2. Meteor: a burning rock falls out of the sky on a long trail, lighting
//      the land as it comes, and lands in a blast, a deeper crater ringed
//      with fire.
//   3. Meteor shower: night falls on a clear sky full of falling stars, the
//      weather's own meteors coming down over the mountains.
public class SkyFireShowcase : ShowcaseScript
{
    protected override IEnumerator Play()
    {
        // Late afternoon: the low sun, and the fire and the meteor still bright against it.
        yield return Switch(At(16f));
        yield return Fireball();
        yield return Meteor();
        yield return Shower();
    }

    IEnumerator Fireball()
    {
        Chapter("Fireball");
        Place(4, 10);
        var at = Yard(14, 4);
        at.y = TerrainGround(at);
        puppet.Face(at);
        // From the side, the wizard on the left and the meadow it lands on to the right.
        var me = wizard.transform.position;
        var mid = (me + at) / 2f;
        var across = Vector3.Cross(Vector3.up, (at - me).normalized);
        Cut(mid + across * 14f + V(0f, 5f, 0f), mid);
        yield return Wait(0.8f);
        yield return Cast(SpellIds.Fireball, 14, 4, 1, 1.6f);
        var flight = Fly(W(at + across * 9f + V(0f, 6f, 0f), at, 3f));
        yield return new WaitUntil(() => flight.Done);
        yield return Wait(0.8f);
    }

    IEnumerator Meteor()
    {
        Chapter("Meteor");
        Place(2, 14);
        var target = Yard(16, -4);
        target.y = TerrainGround(target);
        puppet.Face(target);
        var me = wizard.transform.position;
        // Wide, from off to the side, looking up over the target: the meteor comes in across the sky
        // beyond it (MeteorFall) and lands in the middle of the shot.
        var ahead = (target - me).normalized;
        var side = Vector3.Cross(Vector3.up, ahead);
        Cut(target - ahead * 22f - side * 30f + V(0f, 6f, 0f), target + ahead * 10f + V(0f, 16f, 0f));
        yield return Wait(1f);
        var cell = grid.CellAt(target);
        caster.Cast(SpellIds.Meteor, cell.x, cell.y, 1);
        yield return Wait(MeteorFall.Seconds + 1.5f);
        var flight = Fly(W(target - ahead * 12f - side * 16f + V(0f, 7f, 0f), target, 4f));
        yield return new WaitUntil(() => flight.Done);
        yield return Wait(0.5f);
    }

    IEnumerator Shower()
    {
        Chapter("Meteor shower");
        PutOutFires();
        yield return Switch(At(23f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': " +
            "{ 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0 }, " +
            "'meteors': { 'showerChance': 1, 'every': 2, 'chance': 1, 'fallTicks': 14 } }");
        // Low on the meadow looking north over the forest at the mountain and the sky.
        Cut(V(40f, 6f, 1f), V(28f, 30f, 60f));
        var flight = Fly(W(V(38f, 7f, 3f), V(10f, 34f, 60f), 10f));
        yield return new WaitUntil(() => flight.Done);
    }
}
