using System.Collections;
using System.Linq;
using UnityEngine;

// The stand scenario (-perf-scenario stand -perf-at CX,CY): the wizard stands on the nearest walkable cell to a chunk's
// middle with the game's own camera behind it, and the run starts once the chunks round it are drawn. For a cost that
// follows the place, not the walk (#184: physics at the nine's (4,23) and (6,19)-(6,21)). -perf-camera-off turns the
// camera off as measuring starts, to tell what the view costs there from what the world does. -perf-cell X,Y stands on
// that cell instead (a road's, for a still); -perf-look X,Y turns the camera toward that cell; -perf-eye-up M puts the camera M metres above the wizard, looking on along
// the way it faces, once the run starts (the 60 m stills of docs/design/hills.md).
public partial class PerfStage
{
    const int StandSearch = 24;

    IEnumerator Stand()
    {
        var chunk = StandChunk();
        int mx = chunk.x * ChunkCells + ChunkCells / 2, my = chunk.y * ChunkCells + ChunkCells / 2;
        if (Pair("-perf-cell") is { } exact) (mx, my) = (exact.x, exact.y);
        var cell = NearestStanding(mx, my);
        PlaceCell(cell.x, cell.y);
        Debug.Log($"PERF STAND chunk={chunk.x},{chunk.y} cell={cell.x},{cell.y}");
        yield return null;
        while (grid.ChunksLeftToDraw > 0) yield return null;
        Ready = true;
        while (!Go) yield return null;
        if (Asked("-perf-camera-off") && Camera.main != null)
        {
            Camera.main.enabled = false;
            Debug.Log("PERF STAND camera off");
        }
        var eyeUp = System.Environment.GetCommandLineArgs().SkipWhile(a => a != "-perf-eye-up").Skip(1).FirstOrDefault();
        var look = Pair("-perf-look");
        bool high = float.TryParse(eyeUp, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float up);
        if (high || look != null)
        {
            if (!high) up = 4f;
            // Let the follow camera settle behind the wizard first, then rise from where it looks (or toward -perf-look's cell).
            yield return new WaitForSecondsRealtime(1f);
            var at = wizard.transform.position;
            var ahead = Camera.main != null ? Camera.main.transform.forward : wizard.transform.forward;
            if (look is { } l) ahead = CellGround(l.x, l.y) - at;
            ahead.y = 0f;
            ahead = ahead.sqrMagnitude > 0.01f ? ahead.normalized : Vector3.forward;
            CutTo(at + Vector3.up * up - ahead * up * 0.5f, at + ahead * Mathf.Max(up * 2.5f, 40f));
            Debug.Log($"PERF STAND eye up={up:0} m");
        }
    }

    // A command-line "X,Y" pair after the flag, or null.
    static Vector2Int? Pair(string flag)
    {
        var args = System.Environment.GetCommandLineArgs();
        int at = System.Array.IndexOf(args, flag);
        if (at < 0 || at + 1 >= args.Length) return null;
        var parts = args[at + 1].Split(',');
        return parts.Length == 2 && int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int y) ? new Vector2Int(x, y) : null;
    }

    static Vector2Int StandChunk()
    {
        var args = System.Environment.GetCommandLineArgs();
        int at = System.Array.IndexOf(args, "-perf-at");
        if (at >= 0 && at + 1 < args.Length)
        {
            var parts = args[at + 1].Split(',');
            if (parts.Length == 2 && int.TryParse(parts[0], out int cx) && int.TryParse(parts[1], out int cy)) return new Vector2Int(cx, cy);
        }
        return new Vector2Int(6, 20);
    }

    // The cell nearest (x, y), ring by ring, that is dry open ground.
    Vector2Int NearestStanding(int x, int y)
    {
        for (int r = 0; r <= StandSearch; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r || !World.InBounds(x + dx, y + dy)) continue;
                    var c = World.At(x + dx, y + dy);
                    if (c != null && c.Water == 0 && c.T is TerrainIds.Grass or TerrainIds.Dirt or TerrainIds.Brush) return new Vector2Int(x + dx, y + dy);
                }
        return new Vector2Int(x, y);
    }
}
