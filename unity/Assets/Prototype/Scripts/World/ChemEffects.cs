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
    public const int SwingReachCells = 3;

    public static EffectCommand By(PhysicsProp prop, EffectKind kind, Vector2Int cell) => new EffectCommand(kind, cell.x, cell.y, prop.kind, prop.Thing);

    public static void Strike(ChemGrid grid, Vector2Int cell)
    {
        if (grid == null || grid.World == null) return;
        var peers = Peers.Of(grid);
        if (peers.IsClient()) peers.RequestStrike(cell);
        else Request(grid, new EffectCommand(EffectKind.Shatter, cell.x, cell.y, "", ThingId.None));
    }

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
                    var peers = Peers.Of(grid);
                    if (peers.IsClient()) peers.RequestSwing(cell);
                    else Request(grid, new EffectCommand(EffectKind.Swing, cell.x, cell.y, "", ThingId.None));
                    return;
                }
    }

    public static void Request(ChemGrid grid, EffectCommand effect)
    {
        if (grid == null || grid.World == null) return;
        var peers = Peers.Of(grid);
        if (peers.Online())
        {
            if (peers.Role == PeerRole.Host) peers.StampEffect(effect);
            return;
        }
        grid.World.ApplyEffect(effect);
    }
}
