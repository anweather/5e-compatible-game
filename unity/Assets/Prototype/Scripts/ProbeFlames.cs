using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's flames scenario (issue #19): a field of grass alight at noon, a
// close view and one from above, and a look at what FlameBank holds for it: each
// cell's flames must lie about the cell, not in its middle, and differ from cell
// to cell in place, size, speed and seed. A row each of thatch, log wall and oil
// beside it must keep their own looks, and each level its own flipbook. The
// camera's own picture is checked for flames, so a shader that draws nothing fails.
public class FlamesProbe : ProbeScenario
{
    const int Across = 10, Down = 6;
    static readonly (string name, string terrain, string cover)[] Rows =
    {
        ("thatch", "thatch", null), ("wood", TerrainIds.LogWall, null), ("oil", TerrainIds.Dirt, CoverIds.Oil),
    };

    protected override IEnumerator Run()
    {
        yield return Switch(At(12f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': " +
            "{ 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 } }");
        var o = grid.YardCell(24, 30);
        for (int x = 0; x < Across; x++)
            for (int y = 0; y < Down; y++)
            {
                World.Paint(o.x + x, o.y + y, TerrainIds.Grass);
                World.Apply(o.x + x, o.y + y, "fire", x % 3 + 1, -1, false);
            }
        LayTypes(o);
        var mid = grid.CellCenter(o.x + Across / 2, o.y + Down / 2);
        mid.y = grid.GroundAt(mid);
        Teleport(mid + new Vector3(-16f, 0.1f, 0f));
        Cut(mid + new Vector3(-6f, 3f, -7f), mid + Vector3.up * 0.6f);
        yield return Wait(0.9f);
        Shot("field-close");
        yield return FlamesSeen();
        Measure(o);
        yield return null;
        Cut(mid + new Vector3(-9f, 13f, -9f), mid);
        yield return Wait(0.1f);
        Shot("field-from-above");
        yield return null;
        yield return null;
    }

    // What the bank holds, while it all burns.
    void Measure(Vector2Int o)
    {
        var bank = grid.Flames;
        Check("flame_bank", bank != null && bank.CellsBurning > 0, bank != null ? $"{bank.CellsBurning} cells drawn" : "no bank");
        if (bank == null) return;
        var cells = World.Cells.Select((c, i) => (c, i)).Where(p => p.c.Fire > 0).Select(p => p.i).ToList();
        var all = cells.SelectMany(Standing).ToList();
        Metric("cells_burning", cells.Count);
        Metric("flames", all.Count);
        Check("every_cell_drawn", cells.All(i => bank.FlamesOn(i).Any()), $"{cells.Count} burning cells");

        // How far each cell's first flame stands from its middle, in cells.
        var off = cells.Select(i => (i, ts: bank.FlamesOn(i).ToList())).Where(p => p.ts.Count > 0).Select(p =>
        {
            var c = grid.CellCenter(p.i % World.W, p.i / World.W);
            return p.ts[0].at - new Vector3(c.x, p.ts[0].at.y, c.z);
        }).ToList();
        float spread = off.Average(v => v.magnitude) / grid.cellSize;
        Metric("mean_offset_cells", spread);
        Check("flames_off_centre", spread > 0.2f, $"the first flame of a cell stands {spread:0.00} cells from its middle on average");
        // Within a cell the level is the same, so what differs in height is the flame's own size.
        float sizes = cells.Select(i => Standing(i).ToList()).Where(t => t.Count > 1).Average(t => t.Max(f => f.tall) / t.Min(f => f.tall));
        Metric("size_ratio", sizes);
        Check("sizes_vary", sizes > 1.25f, $"a cell's tallest flame is {sizes:0.0} times its shortest on average");
        Metric("speed_ratio", all.Max(t => t.speed) / all.Min(t => t.speed));
        Check("timing_varies", all.Max(t => t.speed) / all.Min(t => t.speed) > 1.5f && all.Select(t => Mathf.Round(t.seed * 50f)).Distinct().Count() > 30, "speeds and seeds differ");
        Check("no_two_cells_alike", off.Select(v => (Mathf.Round(v.x * 20f), Mathf.Round(v.z * 20f))).Distinct().Count() > off.Count * 0.8f, $"{off.Count} cells");
        TypesDiffer(o);
        Check("no_fire_effects", !grid.GetComponentsInChildren<ParticleSystem>().Any(p => p.name.Contains("Flames")), "no particle system a flame");
    }

    IEnumerable<FlameBank.Flame> Standing(int index) => grid.Flames.FlamesOn(index).Where(f => f.sheet >= 0f);

    // Rows of what burns differently, north of the field, all at the top level.
    void LayTypes(Vector2Int o)
    {
        for (int row = 0; row < Rows.Length; row++)
            for (int x = 0; x < Across; x++)
            {
                int cx = o.x + x, cy = o.y + Down + 1 + row;
                World.Paint(cx, cy, Rows[row].terrain);
                if (Rows[row].cover != null) World.SetCover(cx, cy, Rows[row].cover);
                World.Apply(cx, cy, "fire", 3, -1, false);
            }
    }

    // Each type of fire keeps its own look, and each level its own flipbook.
    void TypesDiffer(Vector2Int o)
    {
        var looks = Rows.Select((_, row) => Enumerable.Range(0, Across)
            .SelectMany(x => Standing(World.Idx(o.x + x, o.y + Down + 1 + row))).ToList()).ToList();
        float Tall(int row) => looks[row].Count > 0 ? looks[row].Average(f => f.tall) : 0f;
        float Green(int row) => looks[row].Count > 0 ? looks[row].Average(f => f.tint.y) : 0f;
        float Quick(int row) => looks[row].Count > 0 ? looks[row].Average(f => f.speed) : 0f;
        for (int row = 0; row < Rows.Length; row++) Metric($"{Rows[row].name}_tall_m", Tall(row));
        bool burning = looks.All(l => l.Count > 0);
        Check("each_type_burns", burning, string.Join(", ", looks.Select(l => l.Count)));
        Check("types_differ", burning && Tall(0) > Tall(1) * 1.3f && Green(0) > Green(1) && Green(1) > Green(2) && Quick(0) > Quick(1),
            $"thatch taller, paler and quicker than wood, wood paler than oil ({Tall(0):0.00}, {Tall(1):0.00}, {Tall(2):0.00} m tall)");
        // What burns as the top level has the large sheet, and a cell's sheet always follows its fire.
        var cells = World.Cells.Select((c, i) => (c, i)).Where(p => p.c.Fire > 0).ToList();
        int wrong = cells.Count(p => Standing(p.i).Any(f => (int)f.sheet != Mathf.Min(p.c.Fire, 3) - 1));
        Check("levels_keep_their_sheets", wrong == 0, $"{wrong} of {cells.Count} cells drawn with another level's flipbook");
    }

    // The picture the player sees has flames in it.
    IEnumerator FlamesSeen()
    {
        yield return new WaitForEndOfFrame();
        var picture = ScreenCapture.CaptureScreenshotAsTexture();
        int flames = picture.GetPixels32().Count(c => c.r > 200 && c.g > 70 && c.g < 235 && c.b < 150);
        Destroy(picture);
        Metric("flame_pixels", flames);
        Check("flames_drawn", flames > 2000, $"{flames} flame-coloured pixels");
    }
}
