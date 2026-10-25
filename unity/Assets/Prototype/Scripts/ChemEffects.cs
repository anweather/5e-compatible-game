using Chem;
using UnityEngine;

// The one door from physics into the chemistry. A physics prop never edits
// cells itself: it asks here ("the barrel blew up at cell (4, 7)") and the
// effect is applied to the grid's Chem.World.
//
// Solo, a request is applied at once. Online, every machine has to change its
// world on the same tick, so a request will travel like a cast: to the host
// (as NetChem.RequestCast does), which stamps the tick and broadcasts it with
// that tick's casts, and every machine runs Apply (D4 in docs/DECISIONS.md:
// the acting machine's physics decides, and only its quantised result is
// shared). Until NetChem carries them, an online yard ignores requests, so its
// worlds stay identical.

public enum ChemEffectKind
{
    /// <summary>A burning prop sets its cell alight (like a burning entity in the sandbox).</summary>
    Burn,
    /// <summary>A prop burned away and left its fire behind.</summary>
    Embers,
    /// <summary>A prop blew up (entities.json "explodes").</summary>
    Explode,
}

/// <summary>A prop's effect on one cell: a plain value, small enough to send like a CastIntent.</summary>
public readonly struct ChemEffect
{
    public ChemEffect(ChemEffectKind kind, Vector2Int cell, string entity)
    {
        Kind = kind;
        X = cell.x;
        Y = cell.y;
        Entity = entity;
    }

    public ChemEffectKind Kind { get; }
    public int X { get; }
    public int Y { get; }
    /// <summary>The prop's id in rules/entities.json.</summary>
    public string Entity { get; }
}

public static class ChemEffects
{
    /// <summary>Asks for an effect on the grid's world (applied at once when solo; see the top of the file).</summary>
    public static void Request(ChemGrid grid, ChemEffect effect)
    {
        if (grid == null || grid.World == null) return;
        if (grid.TryGetComponent<NetChem>(out var net) && net.Online) return;
        Apply(grid.World, effect);
    }

    /// <summary>Changes the world's cells. Deterministic, so every machine can run it for the same effect.</summary>
    public static void Apply(World world, ChemEffect effect)
    {
        var def = world.Rules.Entity(effect.Entity);
        switch (effect.Kind)
        {
            case ChemEffectKind.Burn:
                world.Apply(effect.X, effect.Y, ElementIds.Fire, 1, -1, false);
                break;
            case ChemEffectKind.Embers:
                world.LeaveEmbers(effect.X, effect.Y, def);
                break;
            case ChemEffectKind.Explode:
                world.Explode(effect.X, effect.Y, def);
                break;
        }
    }
}
