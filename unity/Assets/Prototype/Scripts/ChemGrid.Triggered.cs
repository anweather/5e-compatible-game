using System.Collections.Generic;
using Chem;
using UnityEngine;

// Triggered casts' shows (docs/design/events-and-placing.md 2c): each cast a rule
// made on a player's step (World.TriggeredAfter, counted by N as World.SummonedAfter
// is) plays once, from the hand of that player's wizard (SpellCaster.PlayTriggered),
// with the spell's look (SpellVisuals), landing when the core does: a meteor with a
// delay falls for that long. A delayed one is also one of the core's stars, which
// RenderStars would drop as a small falling star; it's held here so it shows once.
// Only fresh ones play: a late joiner's replay, or a cast more than
// TriggeredShowTicks old, plays nothing, so nobody sees old shows.
public partial class ChemGrid
{
    /// <summary>How old a triggered cast may be and still play its show, in ticks (2 s at 6 a second).</summary>
    public const int TriggeredShowTicks = 12;

    World triggeredWorld;
    int triggeredSeen;
    readonly List<SpellCaster> casters = new();

    /// <summary>The shows played since the game began: N, spell, cell, the tick it played on and the tick it lands (for the probe).</summary>
    public IReadOnlyList<(int n, string spell, int x, int y, int tick, int lands, string player)> TriggeredShown => triggeredShown;
    readonly List<(int n, string spell, int x, int y, int tick, int lands, string player)> triggeredShown = new();

    void RenderTriggered()
    {
        if (!ReferenceEquals(triggeredWorld, World))
        {
            triggeredWorld = World;
            triggeredSeen = 0;
        }
        if (World.TriggeredCasts <= triggeredSeen) return;
        bool replaying = TryGetComponent<NetChem>(out var net) && net.Online && !net.CaughtUp;
        int tps = Mathf.Max(1, World.Rules.Sim.TicksPerSecond);
        foreach (var cast in World.TriggeredAfter(triggeredSeen))
        {
            triggeredSeen = cast.N;
            HoldStar(cast);
            // A cast lands between ticks, stamped with the tick already seen (as a flash): one tick's grace.
            if (replaying || World.Tick - cast.Tick > TriggeredShowTicks || cast.Lands < World.Tick - 1) continue;
            var caster = CasterOf(cast.Player);
            if (caster == null) caster = Showman;
            if (caster == null) continue;
            caster.PlayTriggered(cast, Mathf.Max(0, cast.Lands - World.Tick) / (float)tps);
            triggeredShown.Add((cast.N, cast.Spell, cast.X, cast.Y, World.Tick, cast.Lands, cast.Player));
            if (triggeredShown.Count > 256) triggeredShown.RemoveAt(0);
        }
    }

    // A delayed triggered cast's star: marked as falling, so RenderStars leaves it to this show.
    void HoldStar(TriggeredCast cast)
    {
        if (cast.Lands <= cast.Tick) return;
        foreach (var star in World.Stars)
            if (star.Spell == cast.Spell && star.X == cast.X && star.Y == cast.Y && star.Lands == cast.Lands && starsFalling.Add(star)) return;
    }

    // The wizard standing for a player in the core's stands, or null when none is here.
    SpellCaster CasterOf(string player)
    {
        casters.Clear();
        casters.AddRange(FindObjectsByType<SpellCaster>(FindObjectsSortMode.None));
        foreach (var c in casters)
            if (c.isActiveAndEnabled && c.StandPlayer == player) return c;
        return null;
    }
}
