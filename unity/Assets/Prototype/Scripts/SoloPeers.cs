using Chem;
using UnityEngine;

// IPeers when there are no other peers: a grid with no NetChem (an EditMode test, a scene built without
// multiplayer). Always Solo, always caught up, named as the first peer, and it sends nothing: solo, every caller
// runs its intent on its own world. NetChem before its session is up answers the same.
public sealed class SoloPeers : IPeers
{
    public PeerRole Role => PeerRole.Solo;

    public string Me => Peers.NameOf(0);

    public string Player => SpellCaster.SoloPlayer;

    public bool CaughtUp => true;

    public void RequestCast(CastCommand command, int delayTicks, Vector3 from)
    {
        // Solo sends nothing: the caller runs it on its own world.
    }

    public void RequestCombo(ComboCommand command)
    {
        // Solo sends nothing: the caller runs it on its own world.
    }

    public void RequestRoom(RoomCommand command)
    {
        // Solo sends nothing: the caller runs it on its own world.
    }

    public void RequestStrike(Vector2Int cell)
    {
        // Solo sends nothing: the caller runs it on its own world.
    }

    public void RequestSwing(Vector2Int cell)
    {
        // Solo sends nothing: the caller runs it on its own world.
    }

    public void StampEffect(EffectCommand effect)
    {
        // Solo sends nothing: the caller runs it on its own world.
    }

    public void RequestDefense(DefenseCommand command)
    {
        // Solo sends nothing: the caller runs it on its own world.
    }

    public void RequestStrikeCommand(StrikeCommand command)
    {
        // Solo sends nothing: the caller runs it on its own world.
    }
}
