using Chem;
using UnityEngine;

// The view's one door to the other peers (docs/design/architecture-review.md step 7): whether this machine plays
// solo, hosts or is a client, its peer name in the room, whether its own wizard is spawned there (NetWizard.Local),
// and the intents it sends the host (asking the host for a reset goes through that wizard). Two adapters: SoloPeers,
// and NetChem (Prototype.Net, over NGO), which is Solo until its lockstep session is up. Find the grid's with Peers.Of(grid).
//
// A send is made only online (Role is Host or Client), in the same place and order the view made it before: each
// caller keeps its own solo path and runs that on its own world instead. Solo's sends do nothing.
public interface IPeers
{
    PeerRole Role { get; }

    string Me { get; }

    string Player { get; }

    bool CaughtUp { get; }

    bool Resetting { get; }

    bool WizardSpawned { get; }

    void RequestCast(CastCommand command, int delayTicks, Vector3 from);

    void RequestCombo(ComboCommand command);

    void RequestRoom(RoomCommand command);

    void RequestStrike(Vector2Int cell);

    void RequestSwing(Vector2Int cell);

    void StampEffect(EffectCommand effect);

    void RequestDefense(DefenseCommand command);

    void RequestStrikeCommand(StrikeCommand command);

    void StampFound(string peer, ThingId thing);

    void SummonDragon();

    void StampReset();

    void AskForReset();
}

public enum PeerRole
{
    Solo,
    Host,
    Client,
}
