using Chem;
using UnityEngine;

// The one door from physics into the chemistry. A physics prop or a carried
// item never edits cells itself: it asks here ("the barrel blew up at cell
// (4, 7)", "the lantern smashed there") with a Chem.EffectCommand, which names
// the prop by its stable id (ThingId, from world/things.json).
//
// - Solo, the effect is applied to the grid's world at once.
// - Online, every machine has to change its world on the same tick. Only the
//   host simulates props (its physics decides, D4 in docs/DECISIONS.md), so
//   only the host asks: NetChem stamps the effect into the next tick batch
//   next to that tick's casts, and every machine, the host included, applies
//   it when it runs that batch. A late joiner replays it from the history.
//   Clients never apply effects themselves (their props are puppets that
//   follow the host's), so a request from a client is dropped.
public static class ChemEffects
{
    /// <summary>How far off a wizard can work a gate, in cells.</summary>
    public const int SwingReachCells = 3;

    /// <summary>
    /// An effect of <paramref name="kind"/> by <paramref name="prop"/> on <paramref name="cell"/>: its rules
    /// come from the prop's kind, and it names the prop by its stable id (world/things.json), not by an object id.
    /// </summary>
    public static EffectCommand By(PhysicsProp prop, EffectKind kind, Vector2Int cell) => new EffectCommand(kind, cell.x, cell.y, prop.kind, prop.Thing);

    /// <summary>
    /// A wizard hit a pane cell (a punch), the same hit as a heavy thing's. Unlike a prop's, it can start
    /// on a client, which asks the host to stamp it (NetChem.RequestStrike).
    /// </summary>
    public static void Strike(ChemGrid grid, Vector2Int cell)
    {
        if (grid == null || grid.World == null) return;
        if (grid.TryGetComponent<NetChem>(out var net) && net.Online && !net.IsServer) net.RequestStrike(cell);
        else Request(grid, new EffectCommand(EffectKind.Shatter, cell.x, cell.y, "", ThingId.None));
    }

    /// <summary>A wizard's hand on the nearest gate in reach (materials.json "swing"), if there is one. Like a punch, it can start on a client.</summary>
    public static void SwingNear(ChemGrid grid, Vector3 at)
    {
        if (grid == null || grid.World == null) return;
        var from = grid.CellAt(at);
        for (int ring = 0; ring <= SwingReachCells; ring++)
            for (int dy = -ring; dy <= ring; dy++)
                for (int dx = -ring; dx <= ring; dx++)
                {
                    var cell = new Vector2Int(from.x + dx, from.y + dy);
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != ring || !grid.World.InBounds(cell.x, cell.y) || grid.World.TerrainOf(grid.World.At(cell.x, cell.y)).SwingsTo == null) continue;
                    if (grid.TryGetComponent<NetChem>(out var net) && net.Online && !net.IsServer) net.RequestSwing(cell);
                    else Request(grid, new EffectCommand(EffectKind.Swing, cell.x, cell.y, "", ThingId.None));
                    return;
                }
    }

    /// <summary>Asks for an effect on the grid's world (see the top of the file).</summary>
    public static void Request(ChemGrid grid, EffectCommand effect)
    {
        if (grid == null || grid.World == null) return;
        if (grid.TryGetComponent<NetChem>(out var net) && net.Online)
        {
            if (net.IsServer) net.StampEffect(effect);
            return;
        }
        grid.World.ApplyEffect(effect);
    }
}
