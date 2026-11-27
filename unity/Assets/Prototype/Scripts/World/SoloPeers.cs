using Chem;
using UnityEngine;

// IPeers when there are no other peers: a grid with no NetChem (an EditMode test, a scene built without
// multiplayer). Always Solo, always caught up, named as the first peer, and it sends nothing: solo, every caller
// runs its intent on its own world. NetChem before its session is up answers the same.
public sealed class SoloPeers : IPeers
{
    public PeerRole Role => PeerRole.Solo;

    public string Me => Peers.NameOf(0);

    public string Player => Caster.SoloPlayer;

    public bool CaughtUp => true;

    public bool Resetting => false;

    public bool WizardSpawned => false;

    public void RequestCast(CastCommand command, int delayTicks, Vector3 from)
    {
    }

    public void RequestCombo(ComboCommand command)
    {
    }

    public void RequestRoom(RoomCommand command)
    {
    }

    public void RequestStrike(Vector2Int cell)
    {
    }

    public void RequestSwing(Vector2Int cell)
    {
    }

    public void StampEffect(EffectCommand effect)
    {
    }

    public void RequestDefense(DefenseCommand command)
    {
    }

    public void RequestStrikeCommand(StrikeCommand command)
    {
    }

    public void StampFound(string peer, ThingId thing)
    {
    }

    public void SummonDragon()
    {
    }

    public void StampReset()
    {
    }

    public void AskForReset()
    {
    }
}
