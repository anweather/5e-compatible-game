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
    const string Row = "hold#1";
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
        var body = houses.Root(Row).GetComponentsInChildren<MeshFilter>().Sum(f => f.sharedMesh.vertexCount);
        Metric("vertices", body);

        var middle = At(16, 16);
        Stand(58, 19);
        yield return Picture("valley-floor", At(135, 19) + Vector3.up * 3f, middle + Vector3.up * 8f);
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
    }
}
