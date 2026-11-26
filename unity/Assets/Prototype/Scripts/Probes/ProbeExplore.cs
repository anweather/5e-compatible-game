using System.Collections;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's explore scenario (docs/FAST_LOOP.md): the chemistry covers the
// whole valley and is active around the player (ChunkStreaming). The wizard
// goes far from the yard, to meadow outside the old chemistry area, and casts
// Fire Bolt on the grass there. Checks that the chunks around them activated
// and the yard's deactivated, that the fire took and spreads there, and that
// the yard is as it was left when the wizard comes back, while the fire left
// behind lingers (ChunkPlanner) and burns on.
public class ExploreProbe : ProbeScenario
{
    protected override IEnumerator Run()
    {
        var yard = grid.yardCells;
        var home = new Vector2Int(yard.x + yard.width / 2, yard.y + yard.height / 2);
        int homeChunk = Chunks.ChunkOf(home.x), homeRow = Chunks.ChunkOf(home.y);
        Metric("world_cells", World.W * World.H);
        Check("whole_valley", World.W >= 4 * Chunks.Size && World.H >= 4 * Chunks.Size, $"{World.W}x{World.H} cells");

        if (!FindMeadow(home, out var far))
        {
            Check("far_meadow_found", false, $"no grass 200 to 420 cells from the yard, more than {World.Rules.Sim.Streaming.Keep} chunks away");
            yield break;
        }
        Check("far_meadow_found", true, $"cell {far}, {Vector2Int.Distance(far, home) * grid.cellSize:0} m from the yard");

        // Go there (a few cells short, so the bolt flies over grass) and let the chunks follow.
        Place(far.x - 3 - yard.x, far.y - yard.y);
        var at = grid.CellCenter(far.x, far.y);
        at.y = grid.GroundAt(at);
        Cut(at + V(-12f, 9f, -10f), at);
        yield return Wait(1f);
        int farChunk = Chunks.ChunkOf(far.x), farRow = Chunks.ChunkOf(far.y);
        Metric("active_chunks", World.ActiveChunks.Count);
        // Besides chunks lingering, at most the square the keep distance spans round the player stays active (7×7 at keep 3).
        int keep = World.Rules.Sim.Streaming.Keep, most = (2 * keep + 1) * (2 * keep + 1);
        int held = World.ActiveChunks.Count - grid.ChunkPlanner.Lingering.Count;
        Check("chunks_activate_around_player", World.IsActive(farChunk, farRow) && held <= most,
            $"chunk ({farChunk}, {farRow}) active: {World.IsActive(farChunk, farRow)}; {World.ActiveChunks.Count} of {World.ChunkCoords.Count} active, {grid.ChunkPlanner.Lingering.Count} of them lingering (at most {most} besides)");

        Check("bar_has_fire_bolt", caster.Select(SpellIds.FireBolt));
        puppet.Face(at);
        caster.Cast(SpellIds.FireBolt, far.x, far.y, 1);
        yield return Wait(6f);
        int burning = Burning(far, 12);
        Metric("far_burning_t6", burning);
        Check("far_fire_spreads", burning > 1 || World.Log.Any(l => l.Text.StartsWith("Fire takes hold", System.StringComparison.Ordinal) && l.Tick > 0),
            $"{burning} burning squares within 12 of the bolt");
        Shot("far-fire");

        // By now the yard has settled (it may linger a moment after the wizard leaves, if something
        // there was live) and deactivated; inactive, it stays exactly as it is.
        Check("yard_inactive", !World.IsActive(homeChunk, homeRow),
            $"the yard's chunk ({homeChunk}, {homeRow}), last live {World.Tick - World.LastLive(homeChunk, homeRow)} ticks ago");
        string yardBefore = World.ChunkHash(homeChunk, homeRow);
        yield return Wait(2f);
        Check("yard_unchanged_while_inactive", World.ChunkHash(homeChunk, homeRow) == yardBefore);
        Place(yard.width / 2, yard.height / 2);
        yield return Wait(1f);
        Check("yard_activates_on_return", World.IsActive(homeChunk, homeRow));

        // The fire left behind lingers: its chunk stays active, out of reach, and burns on.
        bool lingers = grid.ChunkPlanner.Lingering.Contains((farChunk, farRow));
        Metric("lingering_chunks", grid.ChunkPlanner.Lingering.Count);
        Check("far_fire_lingers", World.IsActive(farChunk, farRow) && lingers,
            $"chunk ({farChunk}, {farRow}) active: {World.IsActive(farChunk, farRow)}, lingering: {lingers}; {grid.ChunkPlanner.Lingering.Count} lingering");
        string farBefore = World.ChunkHash(farChunk, farRow);
        yield return Wait(2f);
        Check("far_fire_burns_on", World.ChunkHash(farChunk, farRow) != farBefore, "the far chunk changed over 2 s with no one near");
    }

    int Burning(Vector2Int around, int r)
    {
        int n = 0;
        for (int y = around.y - r; y <= around.y + r; y++)
            for (int x = around.x - r; x <= around.x + r; x++)
                if (World.InBounds(x, y) && World.At(x, y).Fire > 0) n++;
        return n;
    }

    // Grass 200 to 420 cells from the yard and more than the keep ring of chunks away (so the yard
    // sleeps), with grass around it to burn: the first found going round from the north-west, so
    // the run is the same every time.
    bool FindMeadow(Vector2Int home, out Vector2Int found)
    {
        for (int d = 200; d <= 420; d += 10)
        {
            foreach (var dir in new[] { new Vector2Int(-1, -1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(1, 1), new Vector2Int(-1, 0), new Vector2Int(1, 0) })
            {
                var c = home + dir * (int)(d / Mathf.Sqrt(dir.sqrMagnitude));
                // Where the wizard will stand (3 cells short of it, see Run) is beyond keep from the yard.
                var stand = c - new Vector2Int(3, 0);
                bool beyondKeep = Mathf.Max(Mathf.Abs(Chunks.ChunkOf(stand.x) - Chunks.ChunkOf(home.x)), Mathf.Abs(Chunks.ChunkOf(stand.y) - Chunks.ChunkOf(home.y))) > World.Rules.Sim.Streaming.Keep;
                if (beyondKeep && GrassAround(c, 4)) { found = c; return true; }
            }
        }
        found = default;
        return false;
    }

    bool GrassAround(Vector2Int c, int r)
    {
        for (int y = c.y - r; y <= c.y + r; y++)
            for (int x = c.x - r; x <= c.x + r; x++)
                if (!World.InBounds(x, y) || World.At(x, y).T != TerrainIds.Grass) return false;
        return true;
    }
}
