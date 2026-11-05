using System.Collections;
using System.IO;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's hold scenario (docs/design/the-hold.md, phase 1): the valley's laid row, walked as a wizard walks it. The row's
// id and the cells it's walked by are this map's; nothing here is in the game. Its pictures come from the camera, so they're
// taken in a batch player too.
public class HoldProbe : ProbeScenario
{
    const string Row = "hold#1", Glade = "hold-glade#1";
    const float MostWalk = 40f, Frames = 4f, OnTop = 0.7f;
    const int PictureWidth = 1280, PictureHeight = 720;

    BuildingRow row;
    float walkTop;

    Vector3 At(int x, int y)
    {
        var p = grid.CellCenter(row.X + x, row.Y + y);
        p.y = grid.GroundAt(p);
        return p;
    }

    bool OnCell(int x, int y) => grid.CellAt(wizard.transform.position) == new Vector2Int(row.X + x, row.Y + y);

    IEnumerator Walk(int x, int y)
    {
        var walk = puppet.WalkTo(At(x, y));
        for (float t = 0f; t < MostWalk && walk.MoveNext(); t += Time.deltaTime) yield return walk.Current;
        puppet.Move(Vector2.zero);
    }

    IEnumerator WalkAndCheck(string name, int x, int y)
    {
        yield return Walk(x, y);
        Check(name, OnCell(x, y), $"at {grid.CellAt(wizard.transform.position)}, y {wizard.transform.position.y:0.0}");
    }

    void Stand(int x, int y)
    {
        var p = At(x, y);
        Teleport(p + Vector3.up * 0.05f);
    }

    // Across a stretch of the road and down the hill beside it: the bench is level where the hill isn't.
    void LevelAcross()
    {
        float Ground(int x, int y) => grid.GroundAt(grid.CellCenter(row.X + x, row.Y + y));
        float across = Mathf.Abs(Ground(54, 16) - Ground(56, 16));
        float hill = Mathf.Abs(Ground(46, 16) - Ground(48, 16));
        float bank = Mathf.Abs(Ground(50, 16) - Ground(58, 16));
        Metric("road_across_m", across);
        Metric("hill_across_m", hill);
        Metric("road_bank_m", bank);
        Check("the_road_is_level_across", across < 0.3f, $"{across:0.00} m over two cells, the hill beside it {hill:0.00} m");
    }

    IEnumerator FramesHere(string name)
    {
        yield return Wait(1f);
        int frames = 0;
        float took = 0f, worst = 0f;
        for (float t = 0f; t < Frames; t += Time.unscaledDeltaTime)
        {
            frames++;
            took += Time.unscaledDeltaTime;
            worst = Mathf.Max(worst, Time.unscaledDeltaTime);
            yield return null;
        }
        Metric($"{name}_frame_ms", took / Mathf.Max(1, frames) * 1000f);
        Metric($"{name}_worst_ms", worst * 1000f);
    }

    IEnumerator Picture(string label, Vector3 from, Vector3 to)
    {
        Cut(from, to);
        yield return Wait(0.6f);
        if (string.IsNullOrEmpty(ScreenshotDir)) yield break;
        Directory.CreateDirectory(ScreenshotDir);
        var cam = Camera.main;
        var shot = RenderTexture.GetTemporary(PictureWidth, PictureHeight, 24);
        var was = cam.targetTexture;
        cam.targetTexture = shot;
        cam.Render();
        cam.targetTexture = was;
        var active = RenderTexture.active;
        RenderTexture.active = shot;
        var copy = new Texture2D(PictureWidth, PictureHeight, TextureFormat.RGB24, false);
        copy.ReadPixels(new Rect(0, 0, PictureWidth, PictureHeight), 0, 0);
        RenderTexture.active = active;
        RenderTexture.ReleaseTemporary(shot);
        File.WriteAllBytes(Path.Combine(ScreenshotDir, $"hold-{label}.png"), copy.EncodeToPNG());
        Destroy(copy);
    }

    // A run with today's spells: the wizard on the walls, a bolt now and then, until the keep falls.
    IEnumerator PlayToALoss(System.Collections.Generic.List<BuildingCell> cells, System.Collections.Generic.List<BuildingCell> gate, RowHouses houses)
    {
        var world = grid.World;
        var site = world.HoldSite;
        var show = grid.GetComponent<HoldShow>();
        var defense = FindAnyObjectByType<DefenseField>();
        Check("the_plate_is_there", site != null && show != null && GameObject.Find("Hold plate") != null);
        if (site == null) yield break;
        var plate = grid.CellCenter(site.Plate.x, site.Plate.y);
        Teleport(new Vector3(plate.x, grid.GroundAt(plate) + 0.05f, plate.z));
        for (float t = 0f; t < 8f && !world.HoldRunning; t += Time.deltaTime) yield return null;
        Check("standing_on_the_plate_starts_a_run", world.HoldRunning, show.HudLine());
        if (!world.HoldRunning) yield break;
        for (float t = 0f; t < 40f && world.Raiders.Count == 0; t += Time.deltaTime) yield return null;
        Check("raiders_come", world.Raiders.Count > 0);
        yield return Wait(0.5f);
        Check("they_are_drawn", defense != null && defense.DrawnRaiders > 0, $"{defense?.DrawnRaiders}");
        Stand(21, 3);
        yield return Wait(0.5f);
        var armoured = world.Raiders[0];
        var marked = world.SpawnRaider(Rules.PlainKind(armoured.Kind) + Rules.AdjectiveMark + "armoured", armoured.X, armoured.Y + 2, 40);
        Check("an_adjective_raider_stands", marked != null && world.Rules.AdjectiveOf(marked.Kind) != null, marked?.Kind);
        yield return Breach(houses, cells);
        int casts = 0;
        float took = 0f, worst = 0f;
        int frames = 0, most = 0, wave = 1, inVillage = 0, backEast = 0, westmost = int.MaxValue;
        var village = WorldPlaces.Load(ChemGrid.Folders.World).Find("hollowmere").Cells().ToHashSet();
        var started = new System.Collections.Generic.Dictionary<int, int>();
        for (float t = 0f; t < 300f && world.HoldRunning; t += Time.unscaledDeltaTime)
        {
            took += Time.unscaledDeltaTime;
            worst = Mathf.Max(worst, Time.unscaledDeltaTime);
            frames++;
            most = Mathf.Max(most, world.Raiders.Count);
            wave = world.Hold.Wave;
            foreach (var r in world.Raiders)
            {
                inVillage += village.Contains((r.X, r.Y)) ? 1 : 0;
                if (!started.ContainsKey(r.Id)) started[r.Id] = r.X;
                backEast = Mathf.Max(backEast, r.X - started[r.Id]);
                westmost = Mathf.Min(westmost, r.X);
            }
            if (frames % 900 == 1 && world.Raiders.Count > 0 && wave <= 2)
            {
                var r = world.Raiders[0];
                caster.Cast(caster.SelectedId, r.X, r.Y, 0);
                casts++;
            }
            if (frames == 2400) yield return Picture("run", At(40, 6) + Vector3.up * 12f, At(28, 15) + Vector3.up * 2f);
            yield return null;
        }
        Metric("run_frame_ms", took / Mathf.Max(1, frames) * 1000f);
        Metric("run_worst_ms", worst * 1000f);
        Metric("run_most_raiders", most);
        Metric("run_seconds", took);
        Metric("run_casts", casts);
        Metric("run_score", world.Hold.Wave);
        Check("raiders_stay_out_of_hollowmere", inVillage == 0, $"{inVillage} sightings");
        Check("raiders_climb_west", backEast <= 2 && westmost <= site.Heart.x + 2, $"at most {backEast} cells back east, west to x {westmost}");
        Check("played_to_a_loss", world.Hold.Phase == HoldPhase.Lost, show.HudLine());
        Check("raiders_cleared", world.Raiders.Count == 0);
        yield return Wait(1f);
        Check("the_castle_is_mended", cells.All(c => world.At(c.X, c.Y).T == c.Terrain), $"{cells.Count(c => world.At(c.X, c.Y).T != c.Terrain)} cells off");
        Metric("ruined_stretches_after", houses.Ruin(Row).Down);
        for (float t = 0f; t < 5f && houses.Ruin(Row).Down > 0; t += Time.deltaTime) yield return null;
        Check("its_look_follows", houses.Ruin(Row).Down == 0, houses.Ruins);
        Check("the_hud_says_so", show.HudLine().Contains("fell"), show.HudLine());
    }

    // What the fight does to the castle, shown: a stretch of the north walls, a tower and the top of a stair, gone by hand
    // (a raider's blows are the core's tests); the end of the run mends them.
    IEnumerator Breach(RowHouses houses, System.Collections.Generic.List<BuildingCell> cells)
    {
        var world = grid.World;
        var ruin = houses.Ruin(Row);
        var walkOn = houses.Root(Row).transform.Find("Walk").GetComponent<MeshCollider>();
        var railOn = houses.Root(Row).transform.Find("Rail").GetComponent<MeshCollider>();
        uint triangles = walkOn.sharedMesh.GetIndexCount(0), railed = railOn.sharedMesh.GetIndexCount(0);
        int down = ruin.Down;
        var tower = cells.Where(c => c.Terrain == "stone" && c.X - row.X >= 26 && c.Y - row.Y <= 5).ToList();
        foreach (var c in cells.Where(c => c.Terrain == "stone" && c.Y - row.Y <= 4 && c.X - row.X >= 18 && c.X - row.X <= 23).Concat(tower)) world.At(c.X, c.Y).T = "rubble";
        for (float t = 0f; t < 6f && ruin.Down == down; t += Time.deltaTime) yield return null;
        yield return Wait(0.5f);
        Check("the_walls_fall_with_their_cells", ruin.Down > down, $"{ruin.Down} of {ruin.Stretches} stretches down");
        Check("what_stood_on_them_goes", walkOn.sharedMesh.GetIndexCount(0) < triangles, $"{walkOn.sharedMesh.GetIndexCount(0)} of {triangles} walk indices");
        Check("the_rail_goes_with_them", railOn.sharedMesh.GetIndexCount(0) < railed, $"{railOn.sharedMesh.GetIndexCount(0)} of {railed} rail indices");
        yield return Picture("breach", At(32, -14) + Vector3.up * 16f, At(24, 4) + Vector3.up * 3f);
        Metric("breach_stretches_down", ruin.Down);
    }

    protected override IEnumerator Run()
    {
        var houses = grid.GetComponent<RowHouses>();
        for (float t = 0f; t < 5f && houses.Root(Row) == null; t += Time.deltaTime) yield return null;
        var world = grid.World;
        row = world.BuildingRowOf(Row);
        Check("the_row_stands", row != null && houses.Root(Row) != null);
        if (row == null || houses.Root(Row) == null) yield break;
        walkTop = houses.Root(Row).transform.position.y + RowMesher.WalkTop(row.Json);
        Metric("walk_top_m", walkTop);
        var cells = world.BuildingCellsOf(row);
        Check("its_cells_are_laid", cells.All(c => world.At(c.X, c.Y).T == c.Terrain));
        var valley = FindAnyObjectByType<ValleyChemistry>();
        Check("no_tree_stands_on_it", cells.All(c => !valley.TreeStands(c.X, c.Y)), $"{cells.Count(c => valley.TreeStands(c.X, c.Y))} of {cells.Count}");
        var unseen = cells.Where(c => !Enumerable.Range(-1, 3).Any(dy => Enumerable.Range(-1, 3).Any(dx => valley.TreeStands(c.X + dx, c.Y + dy))) && TreeColliderAt(grid.CellCenter(c.X, c.Y))).ToList();
        Check("no_trunk_stands_unseen_on_it", unseen.Count == 0, string.Join(" ", unseen.Take(8).Select(c => $"[{c.X}, {c.Y}]")));
        var glade = world.BuildingCellsOf(world.BuildingRowOf(Glade));
        var ghosts = glade.Where(c => !Enumerable.Range(-1, 3).Any(dy => Enumerable.Range(-1, 3).Any(dx => valley.TreeStands(c.X + dx, c.Y + dy))) && TreeColliderAt(grid.CellCenter(c.X, c.Y))).ToList();
        Metric("clearing_cells", glade.Count);
        Check("no_tree_stands_in_the_clearing", glade.All(c => !valley.TreeStands(c.X, c.Y)), $"{glade.Count(c => valley.TreeStands(c.X, c.Y))} of {glade.Count}");
        Check("no_trunk_stands_unseen_in_the_clearing", ghosts.Count == 0, string.Join(" ", ghosts.Take(8).Select(c => $"[{c.X}, {c.Y}]")));
        var body = houses.Root(Row).GetComponentsInChildren<MeshFilter>().Sum(f => f.sharedMesh.vertexCount);
        Metric("vertices", body);

        LevelAcross();
        var middle = At(16, 16);
        Stand(82, 19);
        yield return Picture("valley-floor", At(90, 19) + Vector3.up * 3f, middle + Vector3.up * 8f);
        yield return Picture("from-hollowmere", At(78, -40) + Vector3.up * 1.7f, middle + Vector3.up * 14f);
        yield return Picture("the-clearing", At(90, -40) + Vector3.up * 30f, At(62, -8));
        yield return Picture("from-the-square", At(102, -52) + Vector3.up * 1.7f, middle + Vector3.up * 14f);
        yield return Picture("from-the-foot", At(72, 8) + Vector3.up * 1.7f, middle + Vector3.up * 14f);
        yield return Picture("road-from-the-lane", At(100, -26) + Vector3.up * 1.7f, middle + Vector3.up * 14f);
        yield return Picture("road-in-the-woods", At(76, -18) + Vector3.up * 1.7f, middle + Vector3.up * 14f);
        yield return Picture("the-bends", At(52, -6) + Vector3.up * 1.7f, At(62, 30));
        yield return Picture("on-the-road", At(53, 8) + Vector3.up * 1.7f, At(60, 28) + Vector3.up * 1.5f);
        yield return Picture("across-the-bench", At(62, 16) + Vector3.up * 3f, At(50, 16) + Vector3.up * 2f);
        yield return Picture("the-hill-from-above", At(80, -30) + Vector3.up * 70f, At(50, 25));
        yield return Picture("from-the-shore", At(124, 20) + Vector3.up * 1.7f, middle + Vector3.up * 14f);
        yield return FramesHere("floor");
        yield return Picture("approach", At(46, 12) + Vector3.up * 3f, At(28, 15) + Vector3.up * 5f);
        yield return Picture("from-above", At(46, -24) + Vector3.up * 60f, middle);
        yield return Picture("from-the-brook", At(-20, 30) + Vector3.up * 2f, middle + Vector3.up * 8f);

        Stand(34, 15);
        yield return WalkAndCheck("through_the_gate", 22, 15);
        yield return Picture("court", At(22, 15) + Vector3.up * 2f, At(10, 15) + Vector3.up * 6f);
        yield return WalkAndCheck("to_the_keep_door", 18, 19);
        yield return WalkAndCheck("into_the_hall", 12, 19);
        yield return WalkAndCheck("through_to_the_door", 12, 17);
        yield return WalkAndCheck("into_the_room", 12, 13);
        yield return Picture("room", At(12, 11) + Vector3.up * 1.8f, At(12, 16) + Vector3.up * 1.5f);

        yield return WalkAndCheck("back_out", 12, 19);
        yield return WalkAndCheck("out_of_the_keep", 18, 19);
        yield return Walk(20, 7);
        yield return Walk(8, 7);
        yield return Walk(8, 5);
        yield return Walk(21, 5);
        Metric("stair_top_y", wizard.transform.position.y);
        Check("up_the_stair", wizard.transform.position.y > walkTop - 2f, $"y {wizard.transform.position.y:0.0}, the walk's at {walkTop:0.0}");
        yield return Walk(21, 3);
        Check("on_the_wall_walk", wizard.transform.position.y > walkTop - OnTop, $"y {wizard.transform.position.y:0.0}, the walk's at {walkTop:0.0}");
        yield return Picture("wall-walk", wizard.transform.position + Vector3.up * 1.6f, At(28, 3) + Vector3.up * 8f);
        yield return Walk(28, 3);
        yield return Walk(28, 15);
        Check("along_it_over_the_gate", OnCell(28, 15) && wizard.transform.position.y > walkTop - OnTop, $"at {grid.CellAt(wizard.transform.position)}, y {wizard.transform.position.y:0.0}");
        var outward = puppet.WalkTo(At(36, 15));
        for (float t = 0f; t < 3f && outward.MoveNext(); t += Time.deltaTime) yield return outward.Current;
        puppet.Move(Vector2.zero);
        Check("the_rail_stops_a_wizard", wizard.transform.position.y > walkTop - OnTop, $"at {grid.CellAt(wizard.transform.position)}, y {wizard.transform.position.y:0.0}");
        yield return Picture("from-the-walls", wizard.transform.position + Vector3.up * 1.7f, At(60, 19));
        yield return FramesHere("walls");

        var target = new Vector2Int(row.X + 40, row.Y + 8);
        int tick = world.Tick;
        Check("bar_has_fire", caster.Select("fire-bolt"));
        puppet.Face(grid.CellCenter(target.x, target.y));
        caster.Cast(caster.SelectedId, target.x, target.y, 0);
        yield return Wait(3f);
        int burning = BurningCells();
        Metric("burning_after_the_cast", burning);
        Check("a_cast_from_the_walls_lands", burning > 0, $"log: {string.Join(" / ", LogSince(tick).Take(3))}");
        PutOutFires();

        var gate = cells.Where(c => c.Part == BuildingPart.Opening && world.Rules.Terrain[c.Terrain].SwingsTo != null).ToList();
        Check("the_gate_is_open_at_the_start", gate.Count == 3 && gate.All(c => !world.IsBlocking(world.At(c.X, c.Y))));
        Stand(31, 15);
        yield return Wait(0.3f);
        ChemEffects.SwingNear(grid, wizard.transform.position);
        yield return Wait(0.5f);
        Check("a_hand_shuts_it", gate.All(c => world.IsBlocking(world.At(c.X, c.Y))));
        yield return Walk(25, 15);
        Check("shut_it_stops_a_wizard", grid.CellAt(wizard.transform.position).x > row.X + 28, $"at {grid.CellAt(wizard.transform.position)}");
        yield return Picture("gate-shut", At(36, 13) + Vector3.up * 2f, At(28, 15) + Vector3.up * 3f);
        foreach (var c in gate) ChemEffects.Request(grid, new EffectCommand(EffectKind.Burn, c.X, c.Y, null));
        for (float t = 0f; t < 45f && gate.Any(c => world.IsBlocking(world.At(c.X, c.Y))); t += 1f)
        {
            yield return Wait(1f);
            foreach (var c in gate.Where(c => world.At(c.X, c.Y).Fire == 0 && world.IsBlocking(world.At(c.X, c.Y)))) ChemEffects.Request(grid, new EffectCommand(EffectKind.Burn, c.X, c.Y, null));
        }
        Check("the_gate_burns_through", gate.All(c => !world.IsBlocking(world.At(c.X, c.Y))), string.Join(", ", gate.Select(c => world.At(c.X, c.Y).T)));
        yield return Picture("gate-burnt", At(36, 13) + Vector3.up * 2f, At(28, 15) + Vector3.up * 3f);
        yield return PlayToALoss(cells, gate, houses);
    }
}
