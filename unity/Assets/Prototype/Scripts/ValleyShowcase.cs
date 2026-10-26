using System.Collections;
using UnityEngine;

// A short tour of the chunked valley (docs/WORLD_STREAMING.md, part 1), for
// trailer recordings (Prototype > Record Valley Showcase). Four chapters of
// about ten seconds, on a fixed schedule so every take plays the same:
//
//   1. Flyover: from the lake over the meadow to the pond, one seamless
//      terrain with its forest and the pond's natural hollow.
//   2. Water: low at the pond's edge, an Earthen Trench cut west from the
//      bank, and the pond runs into it.
//   3. Earth: a Wall of Stone rises across the trench in its dust, then Move
//      Earth heaves a hill under a crate.
//   4. Fire: Fire Bolt on the meadow west of the old yard, and the camera
//      pulls up as the fire spreads into the valley's grass.
public class ValleyShowcase : ShowcaseScript
{
    protected override IEnumerator Play()
    {
        yield return Flyover();
        yield return Water();
        yield return Earth();
        yield return Fire();
    }

    IEnumerator Flyover()
    {
        Chapter("Flyover");
        Place(16, 5);   // on the pond's west bank, ready for the trench
        puppet.Face(Yard(10, 5));
        Cut(V(45f, 42f, -165f), V(28f, 0f, -20f));
        var flight = Fly(
            W(V(42f, 30f, -45f), V(29f, 0f, 10f), 5f),
            W(V(40f, 10f, 1f), V(28f, 0f, 15f), 4f));
        yield return new WaitUntil(() => flight.Done);
        yield return Wait(0.8f);
    }

    IEnumerator Water()
    {
        Chapter("Water");
        // Down low on the south bank: the pond to the right, the wizard on its west bank.
        var flight = Fly(W(V(19f, 2.2f, 6.5f), V(24f, 0f, 15f), 2.5f));
        yield return new WaitUntil(() => flight.Done);
        yield return Cast(SpellIds.EarthenTrench, 15, 5, 3, 1.5f);   // west from the bank; the pond runs in
        flight = Fly(W(V(17f, 2.6f, 7.5f), V(20f, 0f, 15f), 5f));
        yield return new WaitUntil(() => flight.Done);
    }

    IEnumerator Earth()
    {
        Chapter("Earth");
        // Up to the south-west: a Wall of Stone across the trench, north from (12, 7).
        Place(10, 6);
        var flight = Fly(W(V(10f, 6f, 5f), V(18.5f, 0.5f, 15f), 2f));
        yield return new WaitUntil(() => flight.Done);
        yield return Cast(SpellIds.WallOfStone, 12, 7, 0, 3f);

        // Cut to the south-east, sun behind the camera: Move Earth under the crate by the gully.
        Place(14, 11);
        Cut(V(21f, 3.6f, 2.5f), V(14.5f, 0.8f, 6.5f));
        flight = Fly(W(V(19.5f, 3f, 3.8f), V(14.5f, 0.8f, 6.5f), 4.5f));
        yield return Wait(0.5f);
        yield return Cast(SpellIds.MoveEarth, 10, 11, 2, 0f);
        yield return new WaitUntil(() => flight.Done);
    }

    IEnumerator Fire()
    {
        Chapter("Fire");
        // Low over the meadow at the old yard's west edge, then up and back as the fire runs west
        // into the valley's grass.
        Place(5, 13);
        var target = Yard(2, 12);
        Cut(target + V(6f, 3f, -7f), target + V(-1f, 0f, 1f));
        var flight = Fly(W(target + V(5.5f, 2.8f, -6.5f), target + V(-1f, 0f, 1f), 1.5f));
        yield return new WaitUntil(() => flight.Done);
        yield return Cast(SpellIds.FireBolt, 2, 12, 3, 1.5f);
        flight = Fly(W(target + V(8f, 13f, -16f), target + V(-6f, 0f, 3f), 6.5f));
        yield return new WaitUntil(() => flight.Done);
    }
}
