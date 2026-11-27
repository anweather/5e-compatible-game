using Chem;
using UnityEngine;

// The view's one door to the other peers (docs/design/architecture-review.md step 7): whether this machine plays
// solo, hosts or is a client, its peer name in the room, and the intents it sends the host. Two adapters: SoloPeers,
// and NetChem (Prototype.Net, over NGO), which is Solo until its lockstep session is up. Find the grid's with Peers.Of(grid).
//
// A send is made only online (Role is Host or Client), in the same place and order the view made it before: each
// caller keeps its own solo path and runs that on its own world instead. Solo's sends do nothing.
public interface IPeers
{
    /// <summary>Solo, until the lockstep session is up; then Host or Client.</summary>
    PeerRole Role { get; }

    /// <summary>This peer's name in the room ("Player 1" solo and for the host).</summary>
    string Me { get; }

    /// <summary>This player's name in the core's casts and combos: SpellCaster.SoloPlayer solo, the client's ("p0", "p1") online.</summary>
    string Player { get; }

    /// <summary>False only online while a joiner is still replaying the game so far.</summary>
    bool CaughtUp { get; }

    /// <summary>Online, and the world is being rebuilt by the host's reset (OnlineReset.Happened); false solo.</summary>
    bool Resetting { get; }

    /// <summary>A cast, landing <paramref name="delayTicks"/> after the host takes it; <paramref name="from"/> is where its caster stood.</summary>
    void RequestCast(CastCommand command, int delayTicks, Vector3 from);

    /// <summary>A spell combo (a held element joined with another).</summary>
    void RequestCombo(ComboCommand command);

    /// <summary>A room change (a mod on or off, a revert).</summary>
    void RequestRoom(RoomCommand command);

    /// <summary>Client: a wizard punched a pane cell; the host checks the reach and stamps the shatter.</summary>
    void RequestStrike(Vector2Int cell);

    /// <summary>Client: a wizard swung a gate cell.</summary>
    void RequestSwing(Vector2Int cell);

    /// <summary>Host: a physics prop's effect, stamped into the next tick batch.</summary>
    void StampEffect(EffectCommand effect);

    /// <summary>A defense command (start the waves, place a ward).</summary>
    void RequestDefense(DefenseCommand command);

    /// <summary>A strike on a wizard or a creature.</summary>
    void RequestStrikeCommand(StrikeCommand command);

    /// <summary>Host: someone was granted a thing a mod added (NetItem), stamped straight into the room.</summary>
    void StampFound(string peer, ThingId thing);

    /// <summary>Host: the dragon comes for everyone, on the same tick on every machine (OnlineReset.Summon).</summary>
    void SummonDragon();

    /// <summary>Host: the world is rebuilt for everyone after the reset countdown (OnlineReset.Ask).</summary>
    void StampReset();
}

/// <summary>What this machine is in the game: alone, the host, or a client of the host.</summary>
public enum PeerRole
{
    Solo,
    Host,
    Client,
}
