// The Hold's run (docs/design/the-hold.md, phase 2), on the map's own rows: nothing here reads where the castle stands
// but those rows and hold.json, so the site can move.
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    public partial class HoldTests
    {
        internal const string Me = "Ana";
        const int MostTicks = 12000;

        static Rules R => ChemistryTests.R;
        static WorldHold Site => WorldHold.Load(ChemistryTests.WorldDir());

        // Grass round the castle and the spawners, the rows laid, and the map's Hold.
        internal static World Field(int seed = 7)
        {
            var s = Site;
            var w = new World(R, 576, 576, (uint)seed);
            var cells = s.Spawners.SelectMany(p => p.Cells).Concat(new[] { s.Heart }).ToList();
            for (int y = cells.Min(c => c.y) - 40; y < cells.Max(c => c.y) + 40; y++)
                for (int x = cells.Min(c => c.x) - 40; x < cells.Max(c => c.x) + 40; x++) w.Paint(x, y, "grass");
            WorldBuildings.Load(ChemistryTests.WorldDir()).LayInto(w);
            w.SetHold(s);
            return w;
        }

        internal static void Stand(World w, string who, (int x, int y) at, int hp = 12) => w.ApplyStrike(StrikeCommand.Stand(who, at.x, at.y, hp));

        internal static void Ticks(World w, int n)
        {
            for (int i = 0; i < n; i++) w.Step();
        }

        internal static World Running(int seed = 7)
        {
            var w = Field(seed);
            Stand(w, Me, w.HoldSite.Plate);
            Ticks(w, R.Sim.Hold.PlateTicks + 1);
            return w;
        }

        internal static void Until(World w, System.Func<bool> done, string what)
        {
            for (int i = 0; i < MostTicks && !done(); i++) w.Step();
            Assert.That(done(), what);
        }

        // A wizard out of the way, on no cell a raider reaches.
        internal static void Away(World w, string who = Me) => Stand(w, who, (w.HoldSite.Rise.x - 40, w.HoldSite.Rise.y - 30));

        // The map's own trees on the field, as they stand in cells.txt.
        static void Trees(World w)
        {
            var rows = MapFile.ReadCells(ChemistryTests.WorldDir()).Replace("\r", "").Trim('\n').Split('\n');
            for (int y = 0; y < rows.Length; y++)
                for (int x = 0; x < rows[y].Length; x++)
                    if (rows[y][x] == 'T' && w.At(x, y).T == "grass") w.Paint(x, y, "tree");
        }

        [Test]
        public void OnTheMapTheyClimbWestAndStayOutOfTheVillage()
        {
            var w = Field();
            Trees(w);
            Stand(w, Me, w.HoldSite.Plate);
            Ticks(w, R.Sim.Hold.PlateTicks + 1);
            Away(w);
            var village = WorldPlaces.Load(ChemistryTests.WorldDir()).Find("hollowmere").Cells().ToHashSet();
            var eastmost = new System.Collections.Generic.Dictionary<int, int>();
            var startedAt = new System.Collections.Generic.Dictionary<int, int>();
            Until(w, () =>
            {
                foreach (var r in w.Raiders)
                {
                    Assert.That(village.Contains((r.X, r.Y)), Is.False, $"{r.Kind} at {r.X}, {r.Y}");
                    if (!startedAt.ContainsKey(r.Id)) startedAt[r.Id] = r.X;
                    eastmost[r.Id] = eastmost.TryGetValue(r.Id, out int far) ? System.Math.Max(far, r.X) : r.X;
                }
                return w.Hold.Health < R.Sim.Hold.Health;
            }, "a raider reaches the heart past the trees");
            Assert.That(startedAt, Is.Not.Empty);
            Assert.That(eastmost.All(e => e.Value <= startedAt[e.Key] + 2), "none turns back east");
        }

        [Test]
        public void TheRunIsInTheChecksum()
        {
            var one = Running();
            var other = Running();
            Assert.That(other.CastHash(), Is.EqualTo(one.CastHash()));
            other.Hold.Health--;
            Assert.That(other.CastHash(), Is.Not.EqualTo(one.CastHash()), "a peer whose keep is hurt differently is seen to differ");
        }

        [Test]
        public void TheMapsHoldReads()
        {
            var s = Site;
            Assert.That(s.Check(R), Is.Empty);
            Assert.That(s.Spawners.Sum(p => p.Cells.Count), Is.GreaterThan(10));
        }

        [Test]
        public void AWorldWithNoHoldHashesAsItDid()
        {
            var w = new World(R, 64, 64, 7);
            string hash = w.CastHash();
            Ticks(w, 30);
            Assert.That(w.HoldText(), Is.Empty);
            Assert.That(w.CastHash(), Is.EqualTo(hash));
            var f = Field();
            Ticks(f, 30);
            Assert.That(f.HoldText(), Is.Empty, "a Hold no one has started says nothing");
        }

        [Test]
        public void StandingOnThePlateStartsARun()
        {
            var w = Field();
            Stand(w, Me, w.HoldSite.Plate);
            Ticks(w, R.Sim.Hold.PlateTicks - 2);
            Assert.That(w.Hold, Is.Null, "it takes a moment");
            Ticks(w, 3);
            Assert.That(w.HoldRunning);
            Assert.That(w.Hold.Wave, Is.EqualTo(1));
            Assert.That(w.Hold.Health, Is.EqualTo(R.Sim.Hold.Health));
        }

        [Test]
        public void SteppingOffThePlateOrLyingOnItStartsNothing()
        {
            var w = Field();
            Stand(w, Me, w.HoldSite.Plate);
            Ticks(w, R.Sim.Hold.PlateTicks - 2);
            Away(w);
            Ticks(w, 20);
            Assert.That(w.Hold, Is.Null);
            Stand(w, Me, w.HoldSite.Plate, 0);
            Ticks(w, 20);
            Assert.That(w.Hold, Is.Null, "a downed wizard doesn't press it");
        }

        [Test]
        public void RaidersComeFromTheSpawnersInTheirWave()
        {
            var w = Running();
            Away(w);
            Until(w, () => w.Raiders.Count > 0, "a raider comes");
            var first = w.Raiders[0];
            Assert.That(Site.Spawners.SelectMany(s => s.Cells).Any(c => System.Math.Abs(c.x - first.X) + System.Math.Abs(c.y - first.Y) <= 2));
            Until(w, () => w.Hold.ToSpawn == 0, "the wave's all out");
            Assert.That(w.Hold.Spawned, Is.EqualTo(World.HoldWaveSize(R.Sim.Hold, 1)));
        }

        [Test]
        public void WithTheGateOpenTheyWalkInAndTheKeepFalls()
        {
            var w = Running();
            Away(w);
            Until(w, () => w.Hold.Health < R.Sim.Hold.Health, "a raider reaches the heart");
            Until(w, () => !w.HoldRunning, "the keep falls");
            Assert.That(w.Hold.Phase, Is.EqualTo(HoldPhase.Lost));
            Assert.That(w.Hold.Wave, Is.EqualTo(1), "the score is the wave reached");
            Assert.That(w.Raiders, Is.Empty, "raiders are cleared");
        }

        [Test]
        public void AShutGateIsBatteredDownAndMendedAfter()
        {
            var w = Running();
            Away(w);
            var row = BuildingRow.Read(w.HoldSite.Castle, w.HoldSite.Row, R.Buildings);
            var gate = BuildingRaster.Cells(row, R.Buildings).Where(c => c.Terrain == "timber-gate-open").ToList();
            w.ApplyEffect(new EffectCommand(EffectKind.Swing, gate[0].X, gate[0].Y, null));
            Until(w, () => gate.Any(c => w.BlowsOn(c.X, c.Y) > 0), "they batter it");
            Assert.That(gate.All(c => w.At(c.X, c.Y).T == "timber-gate"));
            Until(w, () => gate.Any(c => w.At(c.X, c.Y).T == "rubble"), "it gives way");
            Until(w, () => !w.HoldRunning, "the keep falls");
            Assert.That(gate.All(c => w.At(c.X, c.Y).T == "timber-gate-open"), "mended, as the map has it");
            Assert.That(gate.All(c => w.BlowsOn(c.X, c.Y) == 0));
        }

        [Test]
        public void TheyGoThroughTheGateBeforeTheStone()
        {
            var w = Running();
            Away(w);
            var row = BuildingRow.Read(w.HoldSite.Castle, w.HoldSite.Row, R.Buildings);
            Until(w, () => w.Hold.Health < R.Sim.Hold.Health, "a raider reaches the heart");
            Assert.That(BuildingRaster.Cells(row, R.Buildings).Where(c => c.Terrain == "stone").All(c => w.At(c.X, c.Y).T == "stone"), "no wall's been touched");
        }

        [Test]
        public void ARaiderBesideAWizardHitsItByTheRoll()
        {
            var w = Running();
            Away(w);
            Until(w, () => w.Raiders.Count > 0, "a raider comes");
            var r = w.Raiders[0];
            Stand(w, Me, (r.X - 1, r.Y));
            Until(w, () => w.Stands[Me].Hp < 12, "it hits");
            var hits = w.TakeStrikeHits();
            Assert.That(hits.Any(h => h.TargetPlayer == Me && h.Hit && h.Damage > 0));
            Assert.That(hits.All(h => h.Roll >= 1 && h.Roll <= 20));
            Assert.That(hits.Any(h => h.TargetPlayer == Me && h.Attacker == R.Entity(r.Kind).Name));
        }

        [Test]
        public void EveryBlowGoesByTheFifthEditionRoll()
        {
            var w = Running();
            Away(w);
            Until(w, () => w.Raiders.Count > 0, "a raider comes");
            var r = w.Raiders[0];
            Stand(w, Me, (r.X - 1, r.Y), 100000);
            var hits = new System.Collections.Generic.List<StrikeHit>();
            Until(w, () =>
            {
                hits.AddRange(w.TakeStrikeHits().Where(h => h.TargetPlayer == Me));
                return hits.Count >= 60;
            }, "sixty blows");
            int ac = R.Entity("wizard").Ac;
            Assert.That(hits.All(h => h.Hit == (h.Roll == 20 || (h.Roll != 1 && h.Total >= ac))));
            Assert.That(hits.Any(h => h.Hit && h.Roll < 20), "a plain roll can hit");
            Assert.That(hits.Any(h => !h.Hit), "and miss");
            Assert.That(hits.Where(h => !h.Hit).All(h => h.Damage == 0));
        }

        [Test]
        public void AWizardCanBeDownedAndGetsUpAtTheCastle()
        {
            var w = Running();
            Away(w);
            Until(w, () => w.Raiders.Count > 0, "a raider comes");
            var r = w.Raiders[0];
            Stand(w, Me, (r.X - 1, r.Y), 1);
            Until(w, () => w.Stands[Me].Down, "it goes down");
            Assert.That(w.HoldRespawn(), Is.EqualTo(((int, int)?)w.HoldSite.Rise));
            Assert.That(Field().HoldRespawn(), Is.Null, "no run, no castle to get up at");
        }

        [Test]
        public void NoRaiderReachesAWizardOnTheWalls()
        {
            var w = Running();
            var row = BuildingRow.Read(w.HoldSite.Castle, w.HoldSite.Row, R.Buildings);
            var wall = BuildingRaster.Cells(row, R.Buildings).Where(c => c.Terrain == "stone").OrderByDescending(c => c.X).First();
            Stand(w, Me, (wall.X, wall.Y));
            Until(w, () => w.Raiders.Count > 0, "a raider comes");
            w.Raiders[0].X = wall.X + 1;
            w.Raiders[0].Y = wall.Y;
            Ticks(w, 60);
            Assert.That(w.Stands[Me].Hp, Is.EqualTo(12));
        }

        [Test]
        public void RaidersAtTheSpawnersTickWithEveryWizardOnTheWalls()
        {
            var w = Running();
            var row = BuildingRow.Read(w.HoldSite.Castle, w.HoldSite.Row, R.Buildings);
            var wall = BuildingRaster.Cells(row, R.Buildings).First(c => c.Terrain == "stone");
            Stand(w, Me, (wall.X, wall.Y));
            Until(w, () => w.Raiders.Count > 0, "a raider comes");
            var r = w.Raiders[0];
            int x = r.X, y = r.Y;
            Ticks(w, 30);
            Assert.That((r.X, r.Y), Is.Not.EqualTo((x, y)));
        }

        [Test]
        public void TheWavesGoOnPastAnyLimit()
        {
            var w = Running();
            Away(w);
            for (int wave = 1; wave <= 7; wave++)
            {
                Until(w, () => w.Hold.Wave > wave || w.Raiders.Count > 0, "raiders come");
                int left = MostTicks;
                while (w.Hold.Wave == wave)
                {
                    Assert.That(left-- > 0, "the wave ends");
                    foreach (var r in w.Raiders) r.Hp = 1;
                    foreach (var r in w.Raiders) r.Status["burning"] = 30;
                    w.Step();
                    Assert.That(w.HoldRunning);
                }
            }
            Assert.That(w.Hold.Wave, Is.EqualTo(8));
            Assert.That(w.Hold.Health, Is.GreaterThan(0));
        }

        [Test]
        public void AnAdjectiveChangesItsRaider()
        {
            var plain = R.Entity("bone-hound");
            var armoured = R.Entity("bone-hound~armoured");
            Assert.That(armoured.Name, Is.EqualTo("Armoured bone hound"));
            Assert.That(armoured.Raider.Resist, Does.Contain("earth"));
            Assert.That(armoured.Raider.Weak, Does.Contain("lightning"));
            Assert.That(armoured.Hp, Is.EqualTo(plain.Hp * 160 / 100));
            Assert.That(R.Entity("bone-hound~nothing"), Is.Null);
            Assert.That(R.Entity("grass~armoured"), Is.Null);
            Assert.That(Rules.PlainKind("bone-hound~armoured"), Is.EqualTo("bone-hound"));
            Assert.That(new[] { "armoured", "burning", "soaked" }.All(id => R.Sim.Hold.Adjective(id) != null));
        }

        [Test]
        public void ABurningRaiderLightsTheGroundItCrosses()
        {
            var w = Running();
            Away(w);
            Until(w, () => w.Raiders.Count > 0, "a raider comes");
            var r = w.SpawnRaider("bone-hound~burning", w.Raiders[0].X, w.Raiders[0].Y + 3, 500);
            Assert.That(r, Is.Not.Null);
            int was = w.Cells.Count(c => c.Fire > 0);
            Ticks(w, 12);
            Assert.That(w.Cells.Count(c => c.Fire > 0), Is.GreaterThan(was));
        }

        [Test]
        public void ASoakedRaiderStaysWet()
        {
            var w = Running();
            Away(w);
            Until(w, () => w.Raiders.Count > 0, "a raider comes");
            var r = w.SpawnRaider("bone-hound~soaked", w.Raiders[0].X, w.Raiders[0].Y + 3, 50);
            Ticks(w, 60);
            Assert.That(r.StatusOf("wet"), Is.GreaterThan(0));
        }

        [Test]
        public void AdjectivesComeFromTheirWaveOn()
        {
            var w = Running();
            Away(w);
            bool seen = false, early = false;
            for (int i = 0; i < MostTicks && w.Hold.Wave < 6; i++)
            {
                foreach (var r in w.Raiders)
                {
                    seen |= r.Kind.Contains(Rules.AdjectiveMark);
                    early |= w.Hold.Wave < R.Sim.Hold.AdjectiveFrom && r.Kind.Contains(Rules.AdjectiveMark);
                    r.Hp = 1;
                    r.Status["burning"] = 30;
                }
                w.Step();
            }
            Assert.That(seen);
            Assert.That(early, Is.False);
        }

        [Test]
        public void TwoPeersRunAlikeToALoss()
        {
            var a = Running(11);
            var b = Running(11);
            foreach (var w in new[] { a, b }) Away(w);
            for (int i = 0; i < MostTicks && a.HoldRunning; i++)
            {
                a.Step();
                b.Step();
                if (i % 200 == 0) Assert.That(b.CastHash(), Is.EqualTo(a.CastHash()), $"tick {i}");
            }
            Assert.That(a.HoldRunning, Is.False);
            Assert.That(b.CastHash(), Is.EqualTo(a.CastHash()));
            Assert.That(b.CellHash(), Is.EqualTo(a.CellHash()));
        }

        // A late joiner replays the history on a fresh world (NetChem): every stand of a run is in it (StandKeeper).
        [Test]
        public void ALateJoinerReplayingTheHistoryGetsTheRunInProgress()
        {
            var stands = new System.Collections.Generic.List<(int tick, (int x, int y) at)>();
            var w = Field();
            void Say((int x, int y) at)
            {
                stands.Add((w.Tick, at));
                Stand(w, Me, at);
            }
            Say(w.HoldSite.Plate);
            Ticks(w, R.Sim.Hold.PlateTicks + 1);
            Say((w.HoldSite.Rise.x - 40, w.HoldSite.Rise.y - 30));
            Until(w, () => w.Raiders.Count >= 2, "raiders come");
            var r = w.Raiders[0];
            Say((r.X - 1, r.Y));
            Ticks(w, 40);
            var joined = Field();
            foreach (var (tick, at) in stands)
            {
                while (joined.Tick < tick) joined.Step();
                Stand(joined, Me, at);
            }
            while (joined.Tick < w.Tick) joined.Step();
            Assert.That(joined.HoldRunning);
            Assert.That(joined.HoldText(), Is.EqualTo(w.HoldText()));
            Assert.That(joined.CastHash(), Is.EqualTo(w.CastHash()));
        }

        internal sealed class Grass : IChunkSource
        {
            public string Version => "grass";
            public void Make(int x, int y, Cell cell) => cell.T = "grass";
        }

        [Test]
        public void ASaveKeepsTheRun()
        {
            var source = new Grass();
            var w = new World(R, 576, 576, 7);
            w.MakeFrom(source);
            WorldBuildings.Load(ChemistryTests.WorldDir()).LayInto(w);
            w.SetHold(Site);
            Stand(w, Me, w.HoldSite.Plate);
            Ticks(w, R.Sim.Hold.PlateTicks + 1);
            Away(w);
            Until(w, () => w.Raiders.Count >= 2, "raiders come");
            var back = World.Load(w.Save(source), R, source);
            back.SetHold(Site);
            Assert.That(back.HoldText(), Is.EqualTo(w.HoldText()));
            Ticks(w, 100);
            Ticks(back, 100);
            Assert.That(back.HoldText(), Is.EqualTo(w.HoldText()));
        }
    }
}
