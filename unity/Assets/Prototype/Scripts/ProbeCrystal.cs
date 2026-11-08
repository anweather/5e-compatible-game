using System.Collections;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's crystal scenario (hold-batch 3): the keep's crystal at the Hold's heart (HoldCrystal). A run starts from the plate,
// raiders are put beside the heart and strike it, and the keep's health is stepped down past each crack stage: the crystal flashes
// on their blows, cracks in stages, shatters at 0 and comes back whole for the next run. With --shots, pictures from up on the
// walls at each stage. The raiders are put down in the core directly, as only a solo probe may; nothing here is in the game.
public class CrystalProbe : ProbeScenario
{
    const float MostWait = 20f;

    World Core => grid.World;

    void Stand((int x, int y) cell)
    {
        var p = grid.CellCenter(cell.x, cell.y);
        p.y = grid.GroundAt(p);
        Teleport(p + Vector3.up * 0.05f);
    }

    IEnumerator StartRun()
    {
        Stand(Core.HoldSite.Plate);
        for (float t = 0f; t < MostWait && !Core.HoldRunning; t += Time.deltaTime) yield return null;
        Stand(Core.HoldSite.Rise);
        yield return Wait(0.5f);
    }

    // Three raiders on the heart's cells, standing ready to strike.
    void Strikers()
    {
        var (hx, hy) = Core.HoldSite.Heart;
        int put = 0;
        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1) })
            if (put < 3 && Core.SpawnRaider(RaiderIds.BoneHound, hx + dx, hy + dy, 4000) is { } r)
            {
                r.Wait = put * 3;
                put++;
            }
        Check("strikers_put", put == 3, $"{put} beside the heart");
    }

    // From high in the open keep, over the plate's room, looking down at the crystal (the keep has no roof).
    void FromTheWalls()
    {
        var (hx, hy) = Core.HoldSite.Heart;
        var heart = grid.CellCenter(hx, hy);
        heart.y = grid.GroundAt(heart);
        var (px, py) = Core.HoldSite.Plate;
        var plate = grid.CellCenter(px, py);
        var toward = new Vector3(plate.x - heart.x, 0f, plate.z - heart.z).normalized;
        Cut(heart + toward * 9f + Vector3.up * 11f + Vector3.Cross(toward, Vector3.up) * 3f, heart + Vector3.up * 3f);
    }

    protected override IEnumerator Run()
    {
        var show = grid.GetComponent<HoldShow>();
        var crystal = show != null ? show.Crystal : null;
        Check("a_crystal_at_the_heart", crystal != null && crystal.isActiveAndEnabled, crystal == null ? "none" : crystal.transform.position.ToString());
        if (crystal == null || Core.HoldSite == null) yield break;
        yield return StartRun();
        Check("a_run_starts", Core.HoldRunning);
        if (!Core.HoldRunning) yield break;
        int full = Core.Hold.Health;
        Core.Hold.ToSpawn = 0; // no wave walks in on the pictures
        FromTheWalls();
        Strikers();
        yield return Wait(1.5f);
        Shot("whole");
        Metric("whole_light", crystal.LightNow);
        Metric("whole_glow", crystal.GlowNow.maxColorComponent);
        Check("whole_at_first", crystal.Stage == 0 && !crystal.Shattered, $"stage {crystal.Stage}");
        for (float t = 0f; t < MostWait && crystal.Blows == 0; t += Time.deltaTime) yield return null;
        Check("the_raiders_strike_it", crystal.Blows > 0 && Core.Hold.Health < full, $"{crystal.Blows} blows, the keep {Core.Hold.Health}/{full}");

        yield return Cracks(crystal, full);
        Core.Hold.Health = 2;
        for (float t = 0f; t < MostWait && Core.HoldRunning; t += Time.deltaTime) yield return null;
        yield return Wait(0.3f);
        Shot("shattered");
        Check("shatters_as_the_keep_falls", crystal.Shattered && !Core.HoldRunning, $"shattered {crystal.Shattered}, {Core.Hold.Phase}");

        yield return StartRun();
        yield return Wait(0.5f);
        Check("whole_again_for_the_next_run", Core.HoldRunning && crystal.Stage == 0 && !crystal.Shattered, $"stage {crystal.Stage}, shattered {crystal.Shattered}");
        Metric("crystal_blows", crystal.Blows);
    }

    // The keep's health stepped down past each stage: a crack more at each.
    IEnumerator Cracks(HoldCrystal crystal, int full)
    {
        for (int stage = 1; stage <= HoldCrystal.Stages; stage++)
        {
            Core.Hold.Health = full * (HoldCrystal.Stages + 1 - stage) / (HoldCrystal.Stages + 1) - 1;
            yield return Wait(1.2f);
            Shot($"cracked-{stage}");
            Metric($"cracked_{stage}_light", crystal.LightNow);
            Metric($"cracked_{stage}_glow", crystal.GlowNow.maxColorComponent);
            Check($"cracked_{stage}", crystal.Stage == stage && !crystal.Shattered, $"stage {crystal.Stage} at {Core.Hold.Health}/{full}");
        }
    }
}
