using System.Collections;
using System.Linq;
using UnityEngine;

// A short flight round what's new in the valley, for the trailer page
// (Prototype > Record Showcase, DemoRecorder):
//
//   1. The river: down its course from the foothills to the lake, past the
//      cabin on its bank.
//   2. Cabins and the court: the cabin on the lake shore, then the ball court
//      of logs, where the wizard runs at the ball and kicks it.
//   3. Places to go: the ring of standing stones on the rise, with the cabin
//      near it, and the grass the meadows grow now.
public class ValleyTourShowcase : ShowcaseScript
{
    protected override IEnumerator Play()
    {
        yield return Switch(At(11f));
        yield return River();
        yield return Court();
        yield return Stones();
    }

    static Vector3 Over(float x, float z, float height) => new Vector3(x, TerrainGround(new Vector3(x, 0f, z)) + height, z);

    IEnumerator River()
    {
        Chapter("The river");
        Cut(Over(150f, 280f, 34f), Over(140f, 210f, 0f));
        var flight = Fly(
            W(Over(146f, 200f, 26f), Over(132f, 130f, 0f), 4f),
            W(Over(150f, 110f, 20f), Over(130f, 50f, 0f), 3.5f),
            W(Over(140f, 45f, 14f), Over(110f, -30f, 0f), 3.5f),
            W(Over(118f, -20f, 18f), Over(60f, -90f, 0f), 3.5f));
        yield return new WaitUntil(() => flight.Done);
    }

    IEnumerator Court()
    {
        Chapter("Cabins and the court");
        var cabin = GameObject.Find("Cabin 2");
        if (cabin != null)
        {
            var p = cabin.transform.position;
            Cut(p + V(14f, 6f, -10f), p + V(0f, 2f, 0f));
            var flight = Fly(W(p + V(6f, 4f, -13f), p + V(0f, 2f, 0f), 4f));
            yield return new WaitUntil(() => flight.Done);
        }
        var ball = FindObjectsByType<PhysicsProp>(FindObjectsSortMode.None).FirstOrDefault(b => b.kind == ItemIds.Ball);
        if (ball == null) yield break;
        var start = ball.transform.position;
        var from = start - Vector3.right * 7f;
        Teleport(new Vector3(from.x, grid.GroundAt(from) + 0.05f, from.z));
        puppet.Face(start);
        Cut(from + V(-5f, 5f, -6f), start);
        yield return Wait(0.6f);
        puppet.Move(Vector2.up);
        var follow = Fly(W(start + V(-4f, 6f, -10f), start + V(4f, 0f, 0f), 3.5f));
        yield return new WaitUntil(() => follow.Done);
        puppet.Move(Vector2.zero);
        yield return Wait(1.5f);
    }

    IEnumerator Stones()
    {
        Chapter("Places to go");
        Cut(Over(-60f, 120f, 12f), Over(-120f, 175f, 2f));
        var flight = Fly(
            W(Over(-95f, 150f, 9f), Over(-120f, 175f, 1f), 4f),
            W(Over(-135f, 160f, 6f), Over(-95f, 140f, 1f), 4f));
        yield return new WaitUntil(() => flight.Done);
        // Down low in the meadow grass.
        Cut(Over(-30f, 20f, 1.4f), Over(-10f, 40f, 0.5f));
        flight = Fly(W(Over(-24f, 26f, 1.6f), Over(-6f, 46f, 0.8f), 4f));
        yield return new WaitUntil(() => flight.Done);
    }
}
