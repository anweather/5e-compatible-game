using System.Collections;
using UnityEngine;

// The probe's startupshots scenario: the same views of a map's start each time, for a before and an after when what the
// view makes at the start changes (start-up by chunk, #206, #207): the spawn, the land from 200 m up, and a patch by the
// spawn laid burnt with a pool of oily water beside it (the ground's paint and the water's slick, CellFxTexture), at 11 in
// the morning with the world's ticks held so nothing moves between the shots. Checks: the patch laid and shown, shot.
public class StartupShotsProbe : ProbeScenario
{
    protected override IEnumerator Run()
    {
        yield return Switch(At(11f));
        var s = grid.PlayerStart;
        PlaceCell(s.x, s.y);
        yield return Wait(3f);
        grid.ExternalTicks = true;
        Look(s.x - 18, s.y + 18, 9f, s.x, s.y, 1f);
        yield return Wait(2f);
        Shot("spawn");
        yield return Wait(0.5f);
        Look(s.x - 60, s.y + 120, 200f, s.x + 40, s.y - 80, 0f);
        yield return Wait(3f);
        Shot("high");
        yield return Wait(0.5f);
        var burnt = new Vector2Int(s.x + 6, s.y - 10);
        Patch(burnt, 8, 6, TerrainIds.Dirt);
        for (int y = 0; y < 6; y++)
            for (int x = 0; x < 8; x++) World.SetCover(burnt.x + x, burnt.y + y, CoverIds.Ash);
        var pool = new Vector2Int(burnt.x + 9, burnt.y);
        Patch(pool, 6, 6, TerrainIds.Pit);
        for (int y = 0; y < 6; y++)
            for (int x = 0; x < 6; x++)
            {
                World.AddWater(pool.x + x, pool.y + y, 6);
                World.SetCover(pool.x + x, pool.y + y, CoverIds.Oil);
            }
        yield return Wait(2f);
        Look(burnt.x + 7, burnt.y + 16, 8f, burnt.x + 7, burnt.y + 3, 0f);
        yield return Wait(2f);
        Shot("patch");
        bool laid = World.At(pool.x + 2, pool.y + 2).Water > 0 && World.At(burnt.x + 2, burnt.y + 2).Cover == CoverIds.Ash;
        Check("patch_laid", laid, $"ash at {burnt}, oily water at {pool}");
        grid.ExternalTicks = false;
        Check("shot", true, "spawn, high, patch");
    }
}
