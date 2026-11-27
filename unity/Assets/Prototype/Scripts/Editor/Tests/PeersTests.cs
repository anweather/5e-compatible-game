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
        Assert.IsFalse(solo.Resetting);
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
            solo.StampFound(solo.Me, ThingId.None);
            solo.SummonDragon();
            solo.StampReset();
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
        Assert.IsFalse(peers.Resetting);
        Assert.IsFalse(OnlineReset.Happened(peers));
    }

    [Test]
    public void APeersNameBySlotIsNetChemsByClientId()
    {
        for (int slot = 0; slot < 8; slot++) Assert.AreEqual(NetChem.PeerName((ulong)slot), Peers.NameOf(slot));
        Assert.AreEqual("Player 3", Peers.NameOf(2));
    }

    [Test]
    public void APlayersNameBySlotIsNetChemsByClientId()
    {
        for (int slot = 0; slot < 8; slot++) Assert.AreEqual(NetChem.PlayerName((ulong)slot), Peers.PlayerOf(slot));
        Assert.AreEqual("p2", Peers.PlayerOf(2));
    }

    [Test]
    public void OutOfPlayTheResetDoorsAreSoloAndSendNothing()
    {
        Assert.AreSame(Peers.Solo, Peers.InPlay);
        Assert.IsFalse(OnlineReset.Summon());
        Assert.IsFalse(OnlineReset.Ask());
    }

    static readonly string[] GameAssemblies =
    {
        "Prototype.Punchy", "Prototype.Kit", "Prototype.World", "Prototype.Land", "Prototype.Village", "Prototype.Game",
    };

    [Test]
    public void TheGameDoesNotReferenceNet()
    {
        var net = typeof(NetChem).Assembly;
        Assert.AreEqual("Prototype.World", typeof(ChemGrid).Assembly.GetName().Name);
        Assert.AreEqual("Prototype.Net", net.GetName().Name);
        CollectionAssert.Contains(System.Array.ConvertAll(net.GetReferencedAssemblies(), a => a.Name), "Prototype.World");
        var loaded = System.AppDomain.CurrentDomain.GetAssemblies();
        var compiled = UnityEditor.Compilation.CompilationPipeline.GetAssemblies(UnityEditor.Compilation.AssembliesType.PlayerWithoutTestAssemblies);
        foreach (var name in GameAssemblies)
        {
            var game = System.Array.Find(loaded, a => a.GetName().Name == name);
            Assert.IsNotNull(game, name);
            CollectionAssert.DoesNotContain(System.Array.ConvertAll(game.GetReferencedAssemblies(), a => a.Name), "Prototype.Net", name);
            var built = System.Array.Find(compiled, a => a.name == name);
            Assert.IsNotNull(built, name);
            CollectionAssert.DoesNotContain(System.Array.ConvertAll(built.assemblyReferences, a => a.name), "Prototype.Net", name);
        }
    }

    [Test]
    public void OnlineAndClientFollowTheRole()
    {
        Assert.IsTrue(new Fixed(PeerRole.Host).Online());
        Assert.IsFalse(new Fixed(PeerRole.Host).IsClient());
        Assert.IsTrue(new Fixed(PeerRole.Client).Online());
        Assert.IsTrue(new Fixed(PeerRole.Client).IsClient());
    }

    sealed class Fixed : IPeers
    {
        public Fixed(PeerRole role) => Role = role;
        public PeerRole Role { get; }
        public string Me => "";
        public string Player => "";
        public bool CaughtUp => true;
        public bool Resetting => false;
        public bool WizardSpawned => false;
        public void AskForReset() => Assert.Fail("not sent here");
        public void StampFound(string peer, ThingId thing) => Assert.Fail("not sent here");
        public void SummonDragon() => Assert.Fail("not sent here");
        public void StampReset() => Assert.Fail("not sent here");
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
