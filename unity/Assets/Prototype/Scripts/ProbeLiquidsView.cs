using System;
using System.Collections;
using Chem;
using UnityEngine;

// The probe's liquidsview scenario (docs/design/primitives.md, P1): a mod's honey and red paint poured
// beside plain water on bare dirt, and the colours read back from the water tiles' own meshes: each
// liquid in its look's colour, water still white with nothing of a liquid in it. Its look changed by
// a rule switch, the honey is redrawn with no cell moving; the mod off, no vertex keeps a colour.
// With --shots it also films the pours: frames for ffmpeg (ClipFilm).
public class LiquidsViewProbe : SpellLooksProbe
{
    const string Mod = "liquids-view";
    // Honey heaps where it lands; paint and water run, so they get one pour in ThinEvery or flood the stage.
    const int Wide = 30, High = 12, Side = 4, Pours = 14, ThinEvery = 5;
    const float PourEvery = 0.35f, Near = 0.02f;
    static readonly Color Honey = new(0.85f, 0.6f, 0.1f), Paint = new(0.8f, 0.1f, 0.1f), Blue = new(0.1f, 0.2f, 0.9f);

    static string ModOf(Color honey) =>
        "{ 'patch': { 'liquids': { " +
        "'honey': { 'name': 'Honey', 'thick': 4, 'heap': 6, 'soaks': false, 'floats': false, 'speed': 0.4, 'look': { 'color': " + Rgb(honey) + " } }, " +
        "'red-paint': { 'name': 'Red paint', 'soaks': false, 'look': { 'color': " + Rgb(Paint) + " } } } } }";

    static string Rgb(Color c) => FormattableString.Invariant($"[{c.r}, {c.g}, {c.b}]");

    protected override IEnumerator Run()
    {
        var o = Spot(Wide, High);
        Patch(o, Wide, High, TerrainIds.Dirt);
        PlaceCell(o.x + Wide / 2, o.y + High + 3);
        var honey = new Vector2Int(o.x + 7, o.y + 4);
        var paint = new Vector2Int(o.x + 15, o.y + 4);
        var water = new Vector2Int(o.x + 23, o.y + 4);

        var plain = grid.CurrentRules;
        Pour(water, "water");
        yield return Wait(1.5f);
        var (before, wornBefore) = grid.WornWaterVertices;
        Check("plain_water_is_white", before > 0 && wornBefore == 0, $"{wornBefore} of {before} vertices wear a colour with only water about");

        grid.QueueRules(plain.WithMods(new[] { new ActiveMod(Mod, ModOf(Honey)) }));
        yield return Wait(0.5f);
        var mid = Mid(o, Wide, High);
        Cut(mid + new Vector3(0f, 17f, -21f), mid);
        CellWaterMesh.CheckKeptPoints = true;
        int checkedFrom = CellWaterMesh.KeptPointsChecked.different;

        var film = string.IsNullOrEmpty(ScreenshotDir) ? null : new ClipFilm(this, ScreenshotDir);
        film?.Roll();
        yield return Wait(0.6f);
        for (int n = 0; n < Pours; n++)
        {
            Pour(honey, "honey");
            if (n % ThinEvery == 0) Pour(paint, "red-paint");
            if (n % ThinEvery == 0) Pour(water, "water");
            yield return Wait(PourEvery);
        }
        yield return Wait(2.5f);
        if (film != null)
        {
            yield return film.Stop();
            Metric("seconds", film.Seconds);
            Check("filmed", film.Saved > 30, $"{film.Saved} of {film.Frames} frames");
        }
        Shot("poured");

        var middle = new Vector2Int(Side / 2, Side / 2);
        CheckDrawn("honey_is_honey", honey + middle, Honey.linear);
        CheckDrawn("paint_is_paint", paint + middle, Paint.linear);
        var drawn = grid.WaterDrawnAt(water.x + middle.x, water.y + middle.y);
        Check("water_beside_them_is_water", drawn is { } d && CellWaterMesh.IsPlain(d.color, d.own), $"{drawn}");
        int different = CellWaterMesh.KeptPointsChecked.different - checkedFrom;
        Check("both_ways_of_drawing_agree", different == 0, $"{different} tiles came out different with their cells read once");
        CellWaterMesh.CheckKeptPoints = false;

        grid.QueueRules(plain.WithMods(new[] { new ActiveMod(Mod, ModOf(Blue)) }));
        yield return Wait(1f);
        CheckDrawn("look_change_redraws", honey + middle, Blue.linear);

        grid.QueueRules(plain);
        yield return Wait(2f);
        var (after, wornAfter) = grid.WornWaterVertices;
        Check("mod_off_leaves_no_colour", wornAfter == 0, $"{wornAfter} of {after} vertices still wear a colour");
    }

    void CheckDrawn(string name, Vector2Int cell, Color want)
    {
        var drawn = grid.WaterDrawnAt(cell.x, cell.y);
        bool near = drawn is { } d && d.own > 1f - Near && Mathf.Abs(d.color.r - want.r) < Near && Mathf.Abs(d.color.g - want.g) < Near && Mathf.Abs(d.color.b - want.b) < Near;
        Check(name, near, $"{drawn}, wanted {want}");
    }

    // Two units of it on each of a Side by Side patch.
    void Pour(Vector2Int from, string liquid)
    {
        for (int y = 0; y < Side; y++)
            for (int x = 0; x < Side; x++) World.AddLiquid(from.x + x, from.y + y, liquid, 2);
    }
}
