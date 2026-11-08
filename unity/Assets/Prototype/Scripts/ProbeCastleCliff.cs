using System.Collections;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's castlecliff scenario: the valley's cliff below the Hold and the canyon beside the river north of it, the
// map's own land stamps (world/maps/valley/stamps.json), seen from the town, from the cliff's foot and from the canyon's
// floor. The cliff is a wall the wizard can't walk up, the canyon is cut deep with a rock floor, a dirt path along it and
// rock walls, the trees are off the faces and the floor, and the Hold's pad and road are as they were.
public class CastleCliffProbe : ProbeScenario
{
    // The cliff's face runs north along x 170 from y 327; the canyon's floor west from its mouth, round a dog-leg.
    protected static readonly Vector2Int[] CanyonPath = { new(186, 274), new(160, 274), new(146, 264), new(128, 276) };
    protected const int CliffX = 170, CliffTop = 296, CliffFoot = 327;

    protected override IEnumerator Run()
    {
        var valley = FindAnyObjectByType<ValleyChemistry>();
        var world = grid.World;
        yield return Switch(At(11f));
        Check("both_stamps_laid", world.MapStampsLaid.Count == 2, $"{world.MapStampsLaid.Count} of the map's stamps laid");
        Check("pad_and_road_as_they_were", world.LandLaidCells().All(c => world.LandStampedAt(c.x, c.y) == 0), "no laid cell stamped");
        Check("no_tree_on_rock", !Cells(100, 240, 220, 345).Any(c => world.IsStampedRock(c.x, c.y) && world.At(c.x, c.y).T == "tree"), "tree cells on stamped rock");

        int steepest = Enumerable.Range(CliffTop, CliffFoot - CliffTop).Max(y => Enumerable.Range(CliffX - 12, 24).Max(x => world.LandDrop(x, y)));
        Metric("cliff_steepest_m_a_cell", steepest / 8f);
        Check("the_cliff_is_a_wall", steepest >= 40, $"{steepest / 8f:0.0} m in a cell");
        int rockFloor = CanyonFloor().Count(c => CellPaint.Mix(world, c.x, c.y)[CellPaint.Layers.ToList().IndexOf(CellPaint.Rock)] > 0.5f);
        int earth = CellPaint.Layers.ToList().IndexOf(CellPaint.Earth);
        int path = Cells(120, 255, 192, 290).Count(c => world.At(c.x, c.y).T == "dirt" && world.LandStampedAt(c.x, c.y) < 0 && CellPaint.Mix(world, c.x, c.y)[earth] > 0.5f);
        Metric("canyon_rock_floor_cells", rockFloor);
        Metric("canyon_path_cells", path);
        Check("a_dirt_path_along_the_floor", path >= 80, $"{path} dirt cells cut into and painted earth");

        PlaceCell(222, 330);
        yield return Wait(2f);
        Look(226, 318, 30f, 160, 384, 5f);
        yield return Wait(3f);
        Shot("from-the-town");
        yield return Wait(1f);
        Look(226, 318, 30f, 150, 330, 5f);
        yield return Wait(3f);
        Shot("town-right");
        yield return Wait(1f);

        // At the foot, walked into the face: it doesn't take the wizard up.
        PlaceCell(184, 312);
        yield return Wait(1f);
        float foot = wizard.transform.position.y;
        yield return Push(new Vector3(-1f, 0f, 0f), 40f);
        float climbed = wizard.transform.position.y - foot;
        Metric("cliff_climbed_m", climbed);
        Metric("cliff_rocks", valley.Rocks.Count);
        Check("no_way_up_the_cliff", climbed < 6f, $"{climbed:0.0} m up after walking 40 m at it");
        Check("rocks_on_the_faces", valley.Rocks.Count > 100, $"{valley.Rocks.Count} rocks");
        PlaceCell(196, 316);
        Look(198, 318, 3f, 168, 310, 14f);
        yield return Wait(3f);
        Shot("cliff-foot");
        yield return Wait(1f);

        // On the floor, looking up the canyon to its bend.
        var floor = CanyonPath[1];
        PlaceCell(floor.x + 8, floor.y);
        yield return Wait(2f);
        float rim = Enumerable.Range(-18, 37).Max(o => grid.GroundAt(grid.CellCenter(floor.x, floor.y + o)));
        float down = valley.Ground.ShownGround(floor.x + 8, floor.y);
        Metric("canyon_floor_below_rim_m", rim - down);
        Check("the_canyon_is_deep", rim - down > 15f, $"the floor {rim - down:0.0} m below its rim");
        Check("a_rock_floor", rockFloor > CanyonFloor().Count() / 2, $"{rockFloor} of {CanyonFloor().Count()} floor cells rock");
        Look(floor.x + 10, floor.y, 2f, CanyonPath[2].x, CanyonPath[2].y, 2f);
        yield return Wait(3f);
        Shot("canyon-floor");
        yield return Wait(1f);
        // From above, north up: the trail round the dog-leg.
        var mid = grid.CellCenter(CanyonPath[1].x, CanyonPath[1].y + 2);
        Cut(mid + Vector3.up * 90f, mid + (grid.CellCenter(CanyonPath[1].x, CanyonPath[1].y + 1) - grid.CellCenter(CanyonPath[1].x, CanyonPath[1].y + 2)).normalized * 0.05f);
        yield return Wait(3f);
        Shot("canyon-above");
    }

    // Walks the wizard's body this way for so many metres, as its controller would.
    IEnumerator Push(Vector3 way, float metres)
    {
        var body = wizard.GetComponent<CharacterController>();
        var mover = wizard.GetComponent("ThirdPersonController") as Behaviour;
        if (mover != null) mover.enabled = false;
        for (float gone = 0f; gone < metres; gone += 0.1f)
        {
            body.Move(way.normalized * 0.1f + Vector3.down * 0.2f);
            if ((int)(gone * 10f) % 20 == 0) yield return null;
        }
        if (mover != null) mover.enabled = true;
    }

    protected static System.Collections.Generic.IEnumerable<Vector2Int> Cells(int x0, int y0, int x1, int y1) =>
        Enumerable.Range(y0, y1 - y0 + 1).SelectMany(y => Enumerable.Range(x0, x1 - x0 + 1).Select(x => new Vector2Int(x, y)));

    // The canyon's floor: cut a crag deep along its path.
    System.Collections.Generic.IEnumerable<Vector2Int> CanyonFloor() =>
        Cells(120, 250, 192, 295).Where(c => grid.World.LandStampedAt(c.x, c.y) <= -World.LandCrag);

    /// <summary>The camera at a cell, this high over its ground, looking at another cell this high over its own.</summary>
    protected void Look(int x, int y, float up, int atX, int atY, float atUp)
    {
        var from = grid.CellCenter(x, y);
        var to = grid.CellCenter(atX, atY);
        Cut(new Vector3(from.x, grid.GroundAt(from) + up, from.z), new Vector3(to.x, grid.GroundAt(to) + atUp, to.z));
    }
}
