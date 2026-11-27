using Chem;
using NUnit.Framework;
using UnityEngine;

// IPeers, the view's door to the other peers (IPeers.cs): the solo adapter answers as solo play always did, a grid
// with no NetChem gets it, and NetChem (the NGO adapter) answers the same until its lockstep session is up. The
// online half (Host, Client, the sends) needs a running session: the probes' online, roomopen and roomruin.
public class PeersTests
{
    readonly System.Collections.Generic.List<GameObject> made = new();

    [TearDown]
    public void TearDown()
    {
        foreach (var go in made)
            if (go != null) Object.DestroyImmediate(go);
        made.Clear();
    }

    GameObject NewGrid(bool withNet)
    {
        var go = new GameObject("Grid under test");
        made.Add(go);
        go.AddComponent<ChemGrid>();
        if (withNet) go.AddComponent<NetChem>();
        return go;
    }

    [Test]
    public void SoloIsSoloCaughtUpAndTheFirstPeer()
    {
        var solo = Peers.Solo;
        Assert.AreEqual(PeerRole.Solo, solo.Role);
        Assert.IsFalse(solo.Online());
        Assert.IsFalse(solo.IsClient());
        Assert.IsTrue(solo.CaughtUp);
        Assert.AreEqual("Player 1", solo.Me);
        Assert.AreEqual(NetChem.PeerName(0), solo.Me);
        Assert.AreEqual(SpellCaster.SoloPlayer, solo.Player);
    }

    [Test]
    public void SoloSendsNothingAndNeverThrows()
    {
        var solo = Peers.Solo;
        Assert.DoesNotThrow(() =>
        {
            solo.RequestCast(new CastCommand("fire_bolt", 1, 2, 0), 3, Vector3.zero);
            solo.RequestRoom(RoomCommand.Revert(solo.Me, "a_mod"));
            solo.RequestStrike(new Vector2Int(1, 2));
            solo.RequestSwing(new Vector2Int(1, 2));
            solo.StampEffect(new EffectCommand(EffectKind.Shatter, 1, 2, "", ThingId.None));
        });
    }

    [Test]
    public void NoGridOrAGridWithoutNetChemIsSolo()
    {
        Assert.AreSame(Peers.Solo, Peers.Of(null));
        Assert.AreSame(Peers.Solo, Peers.Of(NewGrid(withNet: false).transform));
    }

    [Test]
    public void AGridWithNetChemIsAnsweredByIt()
    {
        var go = NewGrid(withNet: true);
        Assert.AreSame(go.GetComponent<NetChem>(), Peers.Of(go.GetComponent<ChemGrid>()));
    }

    [Test]
    public void NetChemBeforeItsSessionAnswersAsSolo()
    {
        var peers = Peers.Of(NewGrid(withNet: true).transform);
        Assert.AreEqual(PeerRole.Solo, peers.Role);
        Assert.IsFalse(peers.Online());
        Assert.IsTrue(peers.CaughtUp);
        Assert.AreEqual(Peers.Solo.Me, peers.Me);
        Assert.AreEqual(Peers.Solo.Player, peers.Player);
    }

    [Test]
    public void APeersNameBySlotIsNetChemsByClientId()
    {
        for (int slot = 0; slot < 8; slot++) Assert.AreEqual(NetChem.PeerName((ulong)slot), Peers.NameOf(slot));
        Assert.AreEqual("Player 3", Peers.NameOf(2));
    }

    [Test]
    public void OnlineAndClientFollowTheRole()
    {
        Assert.IsTrue(new Fixed(PeerRole.Host).Online());
        Assert.IsFalse(new Fixed(PeerRole.Host).IsClient());
        Assert.IsTrue(new Fixed(PeerRole.Client).Online());
        Assert.IsTrue(new Fixed(PeerRole.Client).IsClient());
    }

    // A role and nothing else, for the helpers.
    sealed class Fixed : IPeers
    {
        public Fixed(PeerRole role) => Role = role;
        public PeerRole Role { get; }
        public string Me => "";
        public string Player => "";
        public bool CaughtUp => true;
        public void RequestCombo(ComboCommand command) => Assert.Fail("not sent here");
        public void RequestCast(CastCommand command, int delayTicks, Vector3 from) => Assert.Fail("not sent here");
        public void RequestRoom(RoomCommand command) => Assert.Fail("not sent here");
        public void RequestStrike(Vector2Int cell) => Assert.Fail("not sent here");
        public void RequestSwing(Vector2Int cell) => Assert.Fail("not sent here");
        public void StampEffect(EffectCommand effect) => Assert.Fail("not sent here");
        public void RequestDefense(DefenseCommand command) => Assert.Fail("not sent here");
        public void RequestStrikeCommand(StrikeCommand command) => Assert.Fail("not sent here");
    }
}
