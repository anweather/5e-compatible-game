using System.Collections;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's flames scenario (issue #19): a field of grass alight at noon, a
// close view and one from above, and a look at what FlameBank holds for it: each
// cell's tongues must lie about the cell, not in its middle, and differ from
// cell to cell in place, size, speed and seed. The camera's own picture is
// checked for flames, so a shader that draws nothing fails.
public class FlamesProbe : ProbeScenario
{
    const int Across = 10, Down = 6;

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
        var mid = grid.CellCenter(o.x + Across / 2, o.y + Down / 2);
        mid.y = grid.GroundAt(mid);
        Teleport(mid + new Vector3(-16f, 0.1f, 0f));
        Cut(mid + new Vector3(-6f, 3f, -7f), mid + Vector3.up * 0.6f);
        yield return Wait(0.9f);
        Shot("field-close");
        yield return FlamesSeen();
        Measure();
        yield return null;
        Cut(mid + new Vector3(-9f, 13f, -9f), mid);
        yield return Wait(0.1f);
        Shot("field-from-above");
        yield return null;
        yield return null;
    }

    // What the bank holds, while it all burns.
    void Measure()
    {
        var bank = grid.Flames;
        Check("flame_bank", bank != null && bank.CellsBurning > 0, bank != null ? $"{bank.CellsBurning} cells drawn" : "no bank");
        if (bank == null) return;
        var cells = World.Cells.Select((c, i) => (c, i)).Where(p => p.c.Fire > 0).Select(p => p.i).ToList();
        var all = cells.SelectMany(i => bank.TonguesOf(i).Where(t => t.wide > 0f)).ToList();
        Metric("cells_burning", cells.Count);
        Metric("tongues", all.Count);
        Check("every_cell_drawn", cells.All(i => bank.TonguesOf(i).Any()), $"{cells.Count} burning cells");

        // How far each cell's first tongues stand from its middle, in cells.
        var off = cells.Select(i => (i, ts: bank.TonguesOf(i).ToList())).Where(p => p.ts.Count > 0).Select(p =>
        {
            var c = grid.CellCenter(p.i % World.W, p.i / World.W);
            return p.ts[0].at - new Vector3(c.x, p.ts[0].at.y, c.z);
        }).ToList();
        float spread = off.Average(v => v.magnitude) / grid.cellSize;
        Metric("mean_offset_cells", spread);
        Check("tongues_off_centre", spread > 0.2f, $"the first tongue of a cell stands {spread:0.00} cells from its middle on average");
        // Within a cell the level is the same, so what differs in height is the tongue's own size.
        float sizes = cells.Select(i => bank.TonguesOf(i).Where(t => t.wide > 0f).ToList()).Where(t => t.Count > 1).Average(t => t.Max(f => f.tall) / t.Min(f => f.tall));
        Metric("size_ratio", sizes);
        Check("sizes_vary", sizes > 1.4f, $"a cell's tallest tongue is {sizes:0.0} times its shortest on average");
        Metric("speed_ratio", all.Max(t => t.speed) / all.Min(t => t.speed));
        Check("timing_varies", all.Max(t => t.speed) / all.Min(t => t.speed) > 1.5f && all.Select(t => Mathf.Round(t.seed * 50f)).Distinct().Count() > 30, "speeds and seeds differ");
        Check("no_two_cells_alike", off.Select(v => (Mathf.Round(v.x * 20f), Mathf.Round(v.z * 20f))).Distinct().Count() > off.Count * 0.8f, $"{off.Count} cells");
        Check("no_fire_effects", !grid.GetComponentsInChildren<ParticleSystem>().Any(p => p.name.Contains("Flames")), "no particle system a flame");
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
