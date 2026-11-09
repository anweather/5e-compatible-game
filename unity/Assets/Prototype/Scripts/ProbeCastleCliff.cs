using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's castlecliff scenario: the castle on its cliff and the canyon beside the river north of it. The Hold's pad
// reaches north of the castle at its own height (buildings.json, hold#1's pad "more") and drops off as a crag, with the
// castle's stable on it; the canyon is the map's own land stamp (world/maps/valley/stamps.json). Seen from the town, along
// the walk from the castle's gate to the cliff's lip, from the cliff's foot and from the canyon's floor. The ground from the
// gate to the lip is one height, the face is a rock wall the wizard can't walk up, the canyon is cut deep with a rock floor,
// a dirt path along it and rock walls, the trees are off the faces and the floor, and the Hold's pad and road are as they were.
public class CastleCliffProbe : ProbeScenario
{
    // The canyon's floor west from its mouth, round a dog-leg.
    protected static readonly Vector2Int[] CanyonPath = { new(186, 274), new(160, 274), new(146, 264), new(128, 276) };

    // The cliff's east face runs north along x 151 to 153 from y 349 to 302, its lip the cell west of it; the stable stands
    // west of the walk. The land is one height from the gate to the lip (MapStampTests); the drawn ground keeps within its
    // grain of it, a few centimetres either way.
    protected const int LipX = 149, FaceTop = 302, FaceFoot = 349;

    /// <summary>The castle's walk: out of its gate, north along its east wall onto the pad beyond, and east to the cliff's lip.</summary>
    protected static IEnumerable<Vector2Int> Walk()
    {
        for (int x = 142; x <= 145; x++) yield return new Vector2Int(x, 366);
        for (int y = 365; y >= 330; y--) yield return new Vector2Int(145, y);
        for (int x = 146; x <= LipX; x++) yield return new Vector2Int(x, 330);
    }

    protected override IEnumerator Run()
    {
        var valley = FindAnyObjectByType<ValleyChemistry>();
        var world = grid.World;
        yield return Switch(At(11f));
        Check("the_canyon_laid", world.MapStampsLaid.Count == 1, $"{world.MapStampsLaid.Count} of the map's stamps laid");
        Check("pad_and_road_as_they_were", world.LandLaidCells().All(c => world.LandStampedAt(c.x, c.y) == 0), "no laid cell stamped");
        Check("no_tree_on_rock", !Cells(100, 240, 220, 360).Any(c => world.IsStampedRock(c.x, c.y) && world.At(c.x, c.y).T == "tree"), "tree cells on rock");
        int crag = world.LandCragCells().Count();
        Metric("crag_cells", crag);
        Check("the_pad_drops_off_as_a_crag", crag > 150, $"{crag} crag cells");

        // From the gate to the lip: one height, by the ground and by the wizard walking it.
        var cells = Walk().ToList();
        var heights = cells.Select(c => grid.GroundAt(grid.CellCenter(c.x, c.y))).ToList();
        float span = heights.Max() - heights.Min();
        var highest = cells[heights.IndexOf(heights.Max())];
        var lowest = cells[heights.IndexOf(heights.Min())];
        Metric("walk_ground_span_m", span);
        Check("flat_from_the_gate_to_the_lip", span < 0.4f, $"{span:0.00} m between the highest ({highest}) and lowest ({lowest}) of {heights.Count} cells");
        var lip = grid.CellCenter(LipX, 330);
        float below = grid.GroundAt(lip) - grid.GroundAt(grid.CellCenter(LipX + 8, 330));
        Metric("drop_below_the_lip_m", below);
        Check("a_drop_below_the_lip", below > 20f, $"{below:0.0} m down 8 cells past the lip");
        int steepest = Enumerable.Range(FaceTop, FaceFoot - FaceTop).Max(y => Enumerable.Range(LipX - 1, 8).Max(x => world.LandDrop(x, y)));
        Metric("cliff_steepest_m_a_cell", steepest / 8f);
        Check("the_cliff_is_a_wall", steepest >= 40, $"{steepest / 8f:0.0} m in a cell");
        int rock = Enumerable.Range(FaceTop, FaceFoot - FaceTop).Count(y => Enumerable.Range(LipX + 2, 3).Any(x => world.IsStampedRock(x, y)));
        Check("the_face_is_rock", rock > (FaceFoot - FaceTop) * 3 / 4, $"{rock} of {FaceFoot - FaceTop} rows of the face bare rock");
        int stable = Cells(114, 318, 133, 325).Count(c => world.At(c.x, c.y).T == "stone");
        Check("the_stable_stands", stable >= 30, $"{stable} stone wall cells");

        int rockFloor = CanyonFloor().Count(c => CellPaint.Mix(world, c.x, c.y)[CellPaint.Layers.ToList().IndexOf(CellPaint.Rock)] > 0.5f);
        int earth = CellPaint.Layers.ToList().IndexOf(CellPaint.Earth);
        int path = Cells(120, 255, 192, 290).Count(c => world.At(c.x, c.y).T == "dirt" && world.LandStampedAt(c.x, c.y) < 0 && CellPaint.Mix(world, c.x, c.y)[earth] > 0.5f);
        Metric("canyon_rock_floor_cells", rockFloor);
        Metric("canyon_path_cells", path);
        Check("a_dirt_path_along_the_floor", path >= 80, $"{path} dirt cells cut into and painted earth");

        PlaceCell(222, 330);
        yield return Wait(2f);
        Look(226, 318, 30f, 140, 336, 6f);
        yield return Wait(3f);
        Shot("from-the-town");
        yield return Wait(1f);
        // What the land's rocks cost a frame (LandRocks, up to 1400): from the town, then on the castle's north wall, each
        // with them shown and hidden.
        yield return RockCost("town");
        var wall = grid.CellCenter(134, 356);
        var cliff = grid.CellCenter(LipX + 6, 322);
        Cut(new Vector3(wall.x, grid.GroundAt(wall) + 9.5f, wall.z), new Vector3(cliff.x, grid.GroundAt(cliff), cliff.z));
        yield return RockCost("walls");
        PlaceCell(222, 330);
        Look(226, 318, 30f, 140, 336, 6f);

        var overhead = grid.CellCenter(128, 340);
        Cut(new Vector3(overhead.x + 60f, grid.GroundAt(overhead) + 70f, overhead.z - 40f), new Vector3(overhead.x, grid.GroundAt(overhead), overhead.z));
        yield return Wait(3f);
        Shot("above");

        // The walk, with the wizard: out of the gate, north to the pad, east to the lip.
        var walk = Walk().ToList();
        PlaceCell(walk[0].x, walk[0].y);
        yield return Wait(1.5f);
        float low = wizard.transform.position.y, high = low;
        Look(147, 373, 2.5f, 145, 330, 1.5f);
        yield return Wait(2f);
        Shot("walk-from-the-gate");
        for (int i = 1; i < walk.Count; i++)
        {
            var to = grid.CellCenter(walk[i].x, walk[i].y);
            var way = to - wizard.transform.position;
            way.y = 0f;
            yield return Push(way, way.magnitude);
            low = Mathf.Min(low, wizard.transform.position.y);
            high = Mathf.Max(high, wizard.transform.position.y);
        }
        Metric("walk_wizard_span_m", high - low);
        Check("the_wizard_walks_it_level", high - low < 0.4f, $"{high - low:0.00} m up or down on the walk");
        // At eye height along the pad to the lip, the town beyond; then over the lip, down the face.
        var east = (grid.CellCenter(LipX + 1, 330) - grid.CellCenter(LipX, 330)).normalized;
        var south = (grid.CellCenter(LipX, 331) - grid.CellCenter(LipX, 330)).normalized;
        var stand = wizard.transform.position;
        PlaceCell(LipX - 4, 336);
        yield return Wait(0.5f);
        Vector3 alongFrom = stand - east * 12f + south * 2f + Vector3.up * 2.2f, alongTo = stand + east * 60f - Vector3.up * 4f;
        yield return Hold(alongFrom, alongTo, 3f);
        Shot("walk-at-the-lip");
        yield return Hold(alongFrom, alongTo, 1.5f);
        Vector3 overFrom = stand - east * 1f + south * 3f + Vector3.up * 3f, overTo = stand + east * 12f + south * 6f - Vector3.up * 30f;
        yield return Hold(overFrom, overTo, 3f);
        Shot("view-down");
        yield return Hold(overFrom, overTo, 1.5f);
        yield return Wait(1f);

        // At the foot, walked into the face: it doesn't take the wizard up.
        PlaceCell(162, 326);
        yield return Wait(1f);
        float start = wizard.transform.position.y;
        yield return Push(new Vector3(-1f, 0f, 0f), 30f);
        float climbed = wizard.transform.position.y - start;
        Metric("cliff_climbed_m", climbed);
        Metric("cliff_rocks", valley.Rocks.Count);
        float shortOfLip = grid.GroundAt(lip) - wizard.transform.position.y;
        Metric("cliff_short_of_the_lip_m", shortOfLip);
        Check("no_way_up_the_cliff", shortOfLip > 15f, $"{climbed:0.0} m up the hill's foot after walking 30 m at it, {shortOfLip:0.0} m short of the lip");
        Check("rocks_on_the_faces", valley.Rocks.Count > 100, $"{valley.Rocks.Count} rocks");
        PlaceCell(176, 326);
        Look(178, 330, 3f, LipX, 326, 18f);
        yield return Wait(3f);
        Shot("cliff-foot");
        yield return Wait(1f);

        // On the floor, looking up the canyon to its bend, and up at the cliff.
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
        var onFloor = grid.CellCenter(186, 276);
        var up = grid.CellCenter(150, 304);
        Vector3 floorFrom = new Vector3(onFloor.x, grid.GroundAt(onFloor) + 1.8f, onFloor.z), floorTo = new Vector3(up.x, grid.GroundAt(up) + 12f, up.z);
        yield return Hold(floorFrom, floorTo, 3f);
        Shot("canyon-floor-up");
        yield return Hold(floorFrom, floorTo, 1.5f);
        yield return Wait(1f);
        // From above, north up: the trail round the dog-leg.
        var mid = grid.CellCenter(CanyonPath[1].x, CanyonPath[1].y + 2);
        Cut(mid + Vector3.up * 90f, mid + (grid.CellCenter(CanyonPath[1].x, CanyonPath[1].y + 1) - grid.CellCenter(CanyonPath[1].x, CanyonPath[1].y + 2)).normalized * 0.05f);
        yield return Wait(3f);
        Shot("canyon-above");
    }

    // Mean frame time over three seconds with the land's rocks shown, then hidden, as metrics.
    IEnumerator RockCost(string where)
    {
        var rocks = grid.transform.Find("Land rocks");
        foreach (bool shown in new[] { true, false })
        {
            if (rocks != null) rocks.gameObject.SetActive(shown);
            yield return Wait(1f);
            int frames = 0;
            float seconds = 0f;
            while (seconds < 3f)
            {
                yield return null;
                seconds += Time.unscaledDeltaTime;
                frames++;
            }
            Metric($"frame_ms_{where}_rocks_{(shown ? "shown" : "hidden")}", 1000.0 * seconds / frames);
        }
        if (rocks != null) rocks.gameObject.SetActive(true);
    }

    // The camera held at a shot for so long, whatever else would move it.
    protected IEnumerator Hold(Vector3 position, Vector3 lookAt, float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            Cut(position, lookAt);
            yield return null;
        }
        Cut(position, lookAt);
    }

    // Walks the wizard's body this way for so many metres, as its controller would.
    protected IEnumerator Push(Vector3 way, float metres)
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

    protected static IEnumerable<Vector2Int> Cells(int x0, int y0, int x1, int y1) =>
        Enumerable.Range(y0, y1 - y0 + 1).SelectMany(y => Enumerable.Range(x0, x1 - x0 + 1).Select(x => new Vector2Int(x, y)));

    // The canyon's floor: cut a crag deep along its path.
    IEnumerable<Vector2Int> CanyonFloor() =>
        Cells(120, 250, 192, 295).Where(c => grid.World.LandStampedAt(c.x, c.y) <= -World.LandCrag);
}
