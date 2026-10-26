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
    /// <summary>
    /// An effect of <paramref name="kind"/> by <paramref name="prop"/> on <paramref name="cell"/>: its rules
    /// come from the prop's kind, and it names the prop by its stable id (world/things.json), not by an object id.
    /// </summary>
    public static EffectCommand By(PhysicsProp prop, EffectKind kind, Vector2Int cell) => new EffectCommand(kind, cell.x, cell.y, prop.kind, prop.Thing);

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
