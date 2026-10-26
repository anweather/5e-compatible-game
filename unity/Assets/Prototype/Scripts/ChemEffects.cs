using Chem;
using UnityEngine;

// The one door from physics into the chemistry. A physics prop or a carried
// item never edits cells itself: it asks here ("the barrel blew up at cell
// (4, 7)", "the lantern smashed there") with a Chem.EffectCommand.
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
    /// <summary>An effect of <paramref name="kind"/> by <paramref name="entity"/> on <paramref name="cell"/>.</summary>
    public static EffectCommand At(EffectKind kind, Vector2Int cell, string entity) => new EffectCommand(kind, cell.x, cell.y, entity);

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
