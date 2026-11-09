// Two peers with different Hold points stay in lockstep (D4; docs/design/the-hold.md, phase 5). A player's points are their own save,
// but they reach the world only as a Bring command, as picks do as a Pick: so every peer runs the same world from the same commands.
// Each peer here plays its own wizard from its own copy of the world, like a real one: what it sends goes to every peer, in one order.
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static Chem.HandsDef;
using static Chem.Tests.HoldTests;

namespace Chem.Tests
{
    public class HoldLockstepTests
    {
        const string Bo = "Bo", Cy = "Cy";
        const int Seed = 17, MostTicks = 20000;

        // The rules with short, small waves, so a test plays several: the curve isn't what's checked here.
        static Rules rules;
        static Rules R => rules ??= Short(Rules.Load(ChemistryTests.RulesDir()));
        static HoldParams P => R.Sim.Hold;

        static Rules Short(Rules r)
        {
            var p = r.Sim.Hold;
            (p.First, p.Growth, p.HpPerWave, p.Warmup, p.BetweenWaves, p.SpawnEvery, p.BossEvery) = (2, 1, 0, 12, 18, 3, 0);
            return r;
        }

        // What the net checks (NetChem.Checksum): everything but the cells each tick, and the cells a slice a tick (RollingHash).
        static string Others(World w) => w.SkyHash() + "/" + w.DefenseHash() + "/" + w.CastHash() + "/" + w.DragonHash() + "/" + w.LandHash();

        static string Checksum(World w) => w.CellHash() + "/" + Others(w);

        static void Apply(World w, object command)
        {
            switch (command)
            {
                case ComboCommand c: w.ApplyCombo(c); break;
                case StrikeCommand s: w.ApplyStrike(s); break;
                case CastCommand c: w.Cast(c); break;
                default: throw new ArgumentException("not a command: " + command);
            }
        }

        // The wall cells nearest the heart, where the wizards stand to fight (as the holdcurve bots do).
        static List<(int x, int y)> Posts(World w)
        {
            var s = w.HoldSite;
            return BuildingRaster.Cells(BuildingRow.Read(s.Castle, s.Row, R.Buildings), R.Buildings)
                .Where(c => c.Part == BuildingPart.Wall && w.At(c.X, c.Y).T == "stone")
                .OrderBy(c => Math.Abs(c.X - s.Heart.x) + Math.Abs(c.Y - s.Heart.y)).ThenBy(c => c.X).ThenBy(c => c.Y)
                .Select(c => (c.X, c.Y)).Where((_, i) => i % 3 == 0).Take(3).ToList();
        }

        // One player's copy of the game: its world, and the one wizard it plays.
        sealed class Peer
        {
            public World W;
            public string Me;
            public int Points;
            public (int x, int y) Post;
            // Which of an offer it takes, by the wave; -1 leaves it for the seed's pick as the next wave starts.
            public Func<int, int> Choose;
            public readonly RollingHash Rolling = new RollingHash();
            bool brought, posted;

            Raider Target(int range) => W.Raiders
                .Where(r => Math.Abs(r.X - Post.x) <= range && Math.Abs(r.Y - Post.y) <= range)
                .OrderBy(r => Math.Abs(r.X - W.HoldSite.Heart.x) + Math.Abs(r.Y - W.HoldSite.Heart.y)).ThenBy(r => r.Id).FirstOrDefault();

            // What this peer sends before the tick, from what its own world shows.
            public IEnumerable<object> Think()
            {
                if (!brought)
                {
                    brought = true;
                    var s = W.HoldSite;
                    var water = s.Towers.First(t => t.Element == "water");
                    var at = W.TowerGround(water)[0];
                    yield return StrikeCommand.Stand(Me, at.x, at.y, 1000000);
                    yield return ComboCommand.Bring(Me, Points);
                    yield return ComboCommand.Attune(Me, Left, "water");
                }
                if (!W.HoldRunning) yield break;
                if (!posted)
                {
                    posted = true;
                    yield return StrikeCommand.Stand(Me, Post.x, Post.y, 1000000);
                }
                foreach (var fired in W.CombosFired.Where(f => f.Player == Me && f.Spell != null))
                {
                    var target = Target(Math.Max(fired.Range, 1));
                    if (target == null) continue;
                    var spell = R.Spell(fired.Spell);
                    int aimX = target.X - Post.x, aimY = target.Y - Post.y, dir = World.DirOf(aimX, aimY);
                    if (spell.Origin != "self") yield return new CastCommand(fired.Spell, target.X, target.Y, dir, by: Me);
                    else if (spell.Shape == "burst") yield return new CastCommand(fired.Spell, Post.x, Post.y, dir, by: Me);
                    else yield return new CastCommand(fired.Spell, Post.x + Math.Sign(aimX), Post.y + Math.Sign(aimY), dir, 1, aimX, aimY, Me);
                }
                var run = W.RunOf(Me);
                if (run != null && run.Offer.Count > 0)
                {
                    int pick = Choose(W.Hold.Wave);
                    if (pick >= 0) yield return ComboCommand.Pick(Me, run.Offer[pick % run.Offer.Count]);
                }
                if (W.CastOf(Me)?.Casting == true || Target(HandRules.RangeOf(Throw)) == null) yield break;
                // Every hand and gesture it would like, whether it has them or not: a locked one is refused alike everywhere.
                for (int hand = Right; hand >= Left; hand--)
                    if (W.HandCooling(Me, hand) == 0)
                    {
                        yield return ComboCommand.Hand(Me, hand, bumper: W.Tick % 2 == 0);
                        yield break;
                    }
            }

            HandsDef HandRules => W.Rules.Sim.Hold.Hands;
        }

        sealed class Room
        {
            public readonly List<Peer> Peers = new List<Peer>();
            public readonly List<(int tick, object command)> History = new List<(int, object)>();
            public int Checked;

            // One tick of lockstep: every peer's commands, in the peers' order, reach every world; they all step; their checksums agree.
            public void Tick()
            {
                var sent = Peers.SelectMany(p => p.Think().ToList()).ToList();
                int tick = Peers[0].W.Tick;
                foreach (var c in sent)
                {
                    History.Add((tick, c));
                    foreach (var p in Peers) Apply(p.W, c);
                }
                foreach (var p in Peers) p.W.Step();
                string others = Others(Peers[0].W), cells = Peers[0].Rolling.Add(Peers[0].W, Peers[0].W.Tick);
                foreach (var p in Peers.Skip(1))
                {
                    Assert.That(Others(p.W), Is.EqualTo(others), $"{p.Me}'s peer disagrees at tick {p.W.Tick}");
                    string mine = p.Rolling.Add(p.W, p.W.Tick);
                    if (mine != null && cells != null) Assert.That(mine, Is.EqualTo(cells), $"{p.Me}'s peer's cells disagree by tick {p.W.Tick}");
                }
                Checked++;
            }

            public void Until(Func<World, bool> done, string what)
            {
                for (int i = 0; i < MostTicks && !done(Peers[0].W); i++) Tick();
                Assert.That(done(Peers[0].W), what);
            }

            // A peer joining late replays the history on a fresh world (NetChem), then plays along.
            public Peer Join(Peer p)
            {
                var host = Peers[0].W;
                foreach (var (tick, c) in History)
                {
                    while (p.W.Tick < tick) p.W.Step();
                    Apply(p.W, c);
                }
                while (p.W.Tick < host.Tick) p.W.Step();
                Assert.That(Checksum(p.W), Is.EqualTo(Checksum(host)), "the joiner's replay");
                Peers.Add(p);
                return p;
            }
        }

        static Peer PeerOf(string me, int points, int post, Func<int, int> choose)
        {
            var w = Field(Seed, R);
            return new Peer { W = w, Me = me, Points = points, Post = Posts(w)[post], Choose = choose };
        }

        // The landing's short one: two peers with different points through a wave and its picks, and a joiner caught up.
        [Test]
        public void TwoPeersWithDifferentPointsStayInLockstepThroughAWave()
        {
            var room = new Room();
            var fresh = PeerOf(Me, 0, 0, wave => 0);
            room.Peers.Add(fresh);
            room.Peers.Add(PeerOf(Bo, World.AllPoints, 1, wave => 1));
            var w = fresh.W;
            room.Tick();
            var plate = StrikeCommand.Stand(Me, w.HoldSite.Plate.x, w.HoldSite.Plate.y, 1000000);
            room.History.Add((w.Tick, plate));
            foreach (var p in room.Peers) Apply(p.W, plate);
            room.Until(x => x.HoldRunning, "the run starts");
            room.Until(x => x.Hold.Wave >= 2 && x.RunOf(Me)?.Picks.Count > 0, "a wave beaten and picked from");
            var late = room.Join(PeerOf(Cy, 9, 2, wave => 0));
            for (int i = 0; i < 30; i++) room.Tick();
            Assert.That(w.HandsOf(Me).Holds[Left], Is.Null, "a fresh wizard's attuning is refused on every peer");
            foreach (var p in room.Peers.Skip(1)) Assert.That(Checksum(p.W), Is.EqualTo(Checksum(w)), p.Me + "'s peer at the end");
            Assert.That(late.W.HoldText(), Is.EqualTo(w.HoldText()));
            Assert.That(late.W.HandsText(), Is.EqualTo(w.HandsText()));
        }

        // Five waves with three peers: the sweep runs it (docs/TESTING_LAYERS.md), the one above and the control below land.
        [Test, Category("Sweep")]
        public void TwoPeersWithDifferentPointsStayInLockstepThroughARunAndALateJoiner()
        {
            var room = new Room();
            var fresh = PeerOf(Me, 0, 0, wave => 0);
            var all = PeerOf(Bo, World.AllPoints, 1, wave => wave % 2 == 0 ? 2 : -1);
            room.Peers.Add(fresh);
            room.Peers.Add(all);
            room.Tick();
            // Attuning at the water tower takes its moment.
            for (int i = 0; i < R.Sim.Hold.Hands.AttuneTicks; i++) room.Tick();

            var w = fresh.W;
            Assert.That(w.PointsOf(Me), Is.Zero);
            Assert.That(w.PointsOf(Bo), Is.EqualTo(World.AllPoints));
            Assert.That(w.HandOpen(Me, Left), Is.False, "a fresh wizard has one hand");
            Assert.That(w.HandOpen(Bo, Left));
            Assert.That(w.HandsOf(Me).Holds[Left], Is.Null, "its attuning was refused, on every peer");
            Assert.That(all.W.HandsOf(Bo).Holds[Left], Is.EqualTo("water"));

            // Ana's peer starts the run from the plate; both wizards go to the wall.
            var plate = StrikeCommand.Stand(Me, w.HoldSite.Plate.x, w.HoldSite.Plate.y, 1000000);
            room.History.Add((w.Tick, plate));
            foreach (var p in room.Peers) Apply(p.W, plate);
            room.Until(x => x.HoldRunning, "the run starts");
            room.Until(x => x.Hold.Wave >= 3, "two waves beaten");

            var late = room.Join(PeerOf(Cy, 9, 2, wave => 1));
            room.Until(x => x.Hold.Wave >= 5, "two more, with three peers");

            foreach (var p in room.Peers)
            {
                Assert.That(p.W.RunOf(Me).Picks, Has.Count.GreaterThanOrEqualTo(3), "Ana's picks");
                Assert.That(p.W.RunOf(Bo).Picks, Has.Count.GreaterThanOrEqualTo(3), "Bo's picks and the seed's for him");
                Assert.That(p.W.PointsOf(Cy), Is.EqualTo(9));
            }
            Assert.That(late.W.RunOf(Cy).Picks, Is.Not.Empty, "the joiner picked too");
            foreach (var p in room.Peers.Skip(1)) Assert.That(Checksum(p.W), Is.EqualTo(Checksum(w)), p.Me + "'s peer at the end");
            Assert.That(late.W.HoldText(), Is.EqualTo(w.HoldText()));
            Assert.That(late.W.HandsText(), Is.EqualTo(w.HandsText()));
            TestContext.WriteLine($"{room.Checked} ticks checked, wave {w.Hold.Wave}, {w.ActiveChunks.Count} chunks active");
        }

        // The control: a point either way is a different world, so the checksum would catch a peer that brought its own.
        [Test]
        public void APeerThatDisagreesOnPointsIsCaught()
        {
            var one = Field(Seed, R);
            var other = Field(Seed, R);
            one.ApplyCombo(ComboCommand.Bring(Me, 0));
            other.ApplyCombo(ComboCommand.Bring(Me, P.Unlocks[0].Cost));
            one.Step();
            other.Step();
            Assert.That(Checksum(other), Is.Not.EqualTo(Checksum(one)));
        }
    }
}
