// The Hold's waves (docs/design/the-hold.md, phase 4): what rules/hold.json makes of a wave number, on the map's own rows.
using System.Linq;
using NUnit.Framework;
using static Chem.Tests.HoldTests;

namespace Chem.Tests
{
    public partial class HoldWaveTests
    {
        static Rules R => ChemistryTests.R;
        static HoldParams P => R.Sim.Hold;

        static void Moment(World w, string id) => Assert.That(w.ApplyCombo(ComboCommand.Moment(Me, id)), id);

        // In a moment's wave with its raiders all out, a wizard out of harm's way.
        static World InMoment(string id, int seed = 7)
        {
            var w = Field(seed);
            Away(w);
            Moment(w, id);
            Until(w, () => w.Hold.ToSpawn == 0, "they all come");
            return w;
        }

        [Test]
        public void TheFormulaGrowsWithTheWave()
        {
            Assert.That(World.HoldWaveSize(P, 1), Is.EqualTo(P.First));
            Assert.That(World.HoldWaveSize(P, 6), Is.EqualTo(P.First + 5 * P.Growth));
            var w = Field();
            Assert.That(w.HoldPlan(1, 1, null).Kinds, Is.EqualTo(1));
            Assert.That(w.HoldPlan(1 + P.KindEvery, 1, null).Kinds, Is.EqualTo(2));
            Assert.That(w.HoldPlan(P.BossEvery, 1, null).Boss);
            Assert.That(w.HoldPlan(P.BossEvery + 1, 1, null).Boss, Is.False);
        }

        [Test]
        public void PeersAtTheStartScaleTheWaves()
        {
            int one = World.HoldWaveSize(P, 4), two = World.HoldWaveSize(P, 4, 2), lots = World.HoldWaveSize(P, 4, 40);
            Assert.That(two, Is.EqualTo(one * P.PeerPercent[1] / 100));
            Assert.That(lots, Is.EqualTo(one * P.PeerPercent[P.PeerPercent.Count - 1] / 100), "the last row for more");
            var w = Field();
            Stand(w, Me, w.HoldSite.Plate);
            Stand(w, "Bo", (w.HoldSite.Rise.x - 40, w.HoldSite.Rise.y - 30));
            Ticks(w, P.PlateTicks + 1);
            Assert.That(w.Hold.Peers, Is.EqualTo(2));
            Assert.That(w.Hold.ToSpawn, Is.EqualTo(World.HoldWaveSize(P, 1, 2)));
        }

        [Test]
        public void NoThemeComesTwiceRunning()
        {
            for (int seed = 1; seed <= 30; seed++)
            {
                var w = new World(R, 8, 8, (uint)seed);
                string last = null;
                for (int wave = P.ThemeFrom; wave < P.ThemeFrom + 40; wave++)
                {
                    string theme = w.ThemeOfWave(wave);
                    Assert.That(theme, Is.Not.Null.And.Not.EqualTo(last), $"seed {seed} wave {wave}");
                    last = theme;
                }
            }
            Assert.That(new World(R, 8, 8, 1).ThemeOfWave(P.ThemeFrom - 1), Is.Null);
        }

        [Test]
        public void EveryThemeHasAHandThatsGoodAndOneThatsBad()
        {
            foreach (var theme in P.Themes)
            {
                var rows = theme.Adjectives.Select(P.Adjective).ToList();
                Assert.That(rows, Is.Not.Empty.And.All.Not.Null, theme.Id);
                Assert.That(rows.Any(a => a.Weak.Count > 0 || a.Keeps != null), theme.Id);
            }
            var weak = P.CommonAdjectives.SelectMany(a => a.Weak).ToList();
            var resist = P.CommonAdjectives.SelectMany(a => a.Resist).ToList();
            foreach (var element in new[] { "fire", "cold", "thorns", "lightning" })
            {
                Assert.That(weak.Contains(element), "something is weak to " + element);
                Assert.That(resist.Contains(element), "something resists " + element);
            }
        }

        [Test]
        public void AThemedWaveLeansOnItsAdjectives()
        {
            var w = InMoment("cinders-right");
            var theme = P.Theme("cinders");
            var marked = w.Raiders.Where(r => r.Kind.Contains(Rules.AdjectiveMark)).ToList();
            int themed = marked.Count(r => theme.Adjectives.Contains(r.Kind.Substring(r.Kind.IndexOf(Rules.AdjectiveMark) + 1)));
            Assert.That(themed * 2, Is.GreaterThan(w.Raiders.Count), "more than half");
        }

        [Test]
        public void ABossLeadsItsWaveWithABiggerRow()
        {
            var w = Field();
            Away(w);
            Moment(w, "first-boss");
            Until(w, () => w.Raiders.Count > 0, "it comes");
            var boss = w.Raiders[0];
            var row = R.AdjectiveOf(boss.Kind);
            Assert.That(row?.Boss == true, boss.Kind);
            Assert.That(R.Entity(boss.Kind).Name, Does.StartWith(row.Name));
            Assert.That(boss.MaxHp, Is.GreaterThan(R.Entity(Rules.PlainKind(boss.Kind)).Hp * 3));
            Until(w, () => w.Hold.ToSpawn == 0, "the rest");
            Assert.That(w.Raiders.Count(r => R.AdjectiveOf(r.Kind)?.Boss == true), Is.EqualTo(1));
        }

        [Test]
        public void TheWarningSaysWhatsComingLongEnoughAhead()
        {
            var w = Field();
            Away(w);
            Moment(w, "first-wave");
            Until(w, () => w.Hold.ToSpawn == 0, "they come");
            Assert.That(w.HoldWarning, Is.Null, "none with a wave under way");
            foreach (var r in w.Raiders) r.Hp = 1;
            foreach (var r in w.Raiders) r.Status["burning"] = 30;
            Until(w, () => w.HoldWarning != null, "the wave is beaten");
            var (plan, ticks) = w.HoldWarning.Value;
            Assert.That(plan.Wave, Is.EqualTo(2));
            Assert.That(plan.Count, Is.EqualTo(World.HoldWaveSize(P, 2)));
            Assert.That(plan.Theme, Is.EqualTo(w.Hold.Theme));
            Assert.That(ticks, Is.EqualTo(P.BetweenWaves).Within(1));
            Ticks(w, ticks - 1);
            Assert.That(w.Hold.InWave, Is.Zero, "nothing yet");
            Ticks(w, 2);
            Assert.That(w.Hold.InWave, Is.EqualTo(1));
        }

        [Test]
        public void AMomentIsTheWavePlayWouldHaveReached()
        {
            var moment = Field();
            Away(moment);
            Moment(moment, "first-boss");
            var played = Field();
            Away(played);
            int wave = moment.Hold.Wave;
            Assert.That(moment.Hold.Theme, Is.EqualTo(played.ThemeOfWave(wave)));
            Stand(played, Me, played.HoldSite.Plate);
            Ticks(played, P.PlateTicks + 1);
            Away(played);
            Hurry(played, wave);
            Assert.That(played.Hold.Wave, Is.EqualTo(wave));
            Assert.That(played.Hold.Theme, Is.EqualTo(moment.Hold.Theme));
            Assert.That(played.Hold.ToSpawn, Is.EqualTo(moment.Hold.ToSpawn));
            var one = KindsOf(moment);
            var two = KindsOf(played);
            Assert.That(two, Is.EqualTo(one));
        }

        // To a wave as fast as a run gets there: its raiders burn out at once, and no warning is waited for.
        static void Hurry(World w, int wave)
        {
            for (int left = 4000; w.Hold.Wave < wave && left > 0; left--)
            {
                foreach (var r in w.Raiders) r.Hp = 1;
                foreach (var r in w.Raiders) r.Status["burning"] = 30;
                w.Hold.Health = P.Health;
                w.Hold.NextSpawn = System.Math.Min(w.Hold.NextSpawn, w.Tick);
                w.Step();
            }
            Assert.That(w.Hold.Wave, Is.EqualTo(wave));
        }

        // The wave's raiders in the order they come, each caught as it spawns.
        static string[] KindsOf(World w)
        {
            var kinds = new System.Collections.Generic.Dictionary<int, string>();
            for (int i = 0; i < 4000 && w.Hold.ToSpawn > 0; i++)
            {
                w.Hold.Health = P.Health;
                w.Step();
                foreach (var r in w.Raiders) kinds[r.Id] = r.Kind;
                foreach (var r in w.Raiders) r.Wait = 99;
            }
            return kinds.OrderBy(k => k.Key).Select(k => k.Value).ToArray();
        }

        [Test]
        public void AMomentSetsTheHandsAndTheKeep()
        {
            var w = Field();
            Stand(w, Me, (w.HoldSite.Rise.x - 40, w.HoldSite.Rise.y - 30));
            Moment(w, "late-wave");
            Assert.That(w.HoldRunning);
            var m = P.Moment("late-wave");
            Assert.That(w.Hold.Wave, Is.EqualTo(m.Wave));
            Assert.That(w.Hold.Health, Is.EqualTo(m.Health));
            Assert.That(w.HandsOf(Me).Holds, Is.EqualTo(m.Holds));
            Assert.That(BrokenCells(w), Is.GreaterThan(0), "the castle is hurt");
            Assert.That(w.ApplyCombo(ComboCommand.Moment(Me, "no-such")), Is.False);
        }

        static int BrokenCells(World w) =>
            BuildingRaster.Cells(BuildingRow.Read(w.HoldSite.Castle, w.HoldSite.Row, R.Buildings), R.Buildings).Count(c => w.At(c.X, c.Y).T != c.Terrain);

        [Test]
        public void EveryMomentGoesIn()
        {
            foreach (var m in P.Moments)
            {
                var w = Field();
                Away(w);
                Moment(w, m.Id);
                Assert.That(w.Hold.Wave, Is.EqualTo(m.Wave), m.Id);
                if (m.Theme != null) Assert.That(w.Hold.Theme, Is.EqualTo(m.Theme), m.Id);
            }
        }

        [Test]
        public void TwoWorldsMatchOverSeveralWaves()
        {
            var one = InMoment("first-adjectives", 11);
            var other = InMoment("first-adjectives", 11);
            Hurry(one, 5);
            Hurry(other, 5);
            Assert.That(other.HoldText(), Is.EqualTo(one.HoldText()));
            Assert.That(other.CastHash(), Is.EqualTo(one.CastHash()));
        }

        // ---------- routes ----------

        static World WithRaider(string kind)
        {
            var w = Field();
            Away(w);
            Moment(w, "first-wave");
            w.Hold.ToSpawn = 0;
            w.Hold.InWave = 1;
            var (x, y) = w.HoldSite.Spawners[0].Cells[0];
            var r = w.SpawnRaider(kind, x, y, 500);
            Assert.That(r, Is.Not.Null);
            return w;
        }

        // The share of the cells it walks that are road, by the time it's at the heart or a limit.
        static (int onRoad, int cells) Walk(World w)
        {
            var road = BuildingRaster.Cells(BuildingRow.Read(w.HoldSite.Castle, w.HoldSite.Row, R.Buildings), R.Buildings)
                .Where(c => c.Part == BuildingPart.Road && c.Terrain == w.HoldSite.RoadTerrain).Select(c => (c.X, c.Y)).ToHashSet();
            var seen = new System.Collections.Generic.HashSet<(int, int)>();
            for (int i = 0; i < 2500 && w.Raiders.Count > 0 && w.Hold.Health == P.Health; i++)
            {
                var r = w.Raiders[0];
                seen.Add((r.X, r.Y));
                w.Step();
            }
            return (seen.Count(road.Contains), seen.Count);
        }

        [Test]
        public void ARoadRaiderTakesTheRoad()
        {
            var (onRoad, cells) = Walk(WithRaider("bone-hound"));
            Assert.That(onRoad * 2, Is.GreaterThan(cells), $"{onRoad} of {cells}");
        }

        [Test]
        public void AClimberIgnoresIt()
        {
            var road = Walk(WithRaider("bone-hound"));
            var climb = Walk(WithRaider("cliff-stalker"));
            Assert.That(climb.cells, Is.LessThan(road.cells), "it's the short way");
            Assert.That(climb.onRoad * 2, Is.LessThan(climb.cells), $"{climb.onRoad} of {climb.cells}");
        }

        [Test]
        public void TwoRaidersMeetingHeadOnPass()
        {
            var w = WithRaider("bone-hound");
            var hound = w.Raiders[0];
            Ticks(w, 8); // the ways are worked out
            hound.Wait = 100000;
            (int x, int y)? meet = null;
            for (int y = w.HoldSite.Heart.y - 30; y < w.HoldSite.Heart.y + 30 && meet == null; y++)
                for (int x = w.HoldSite.Heart.x + 12; x < w.HoldSite.Heart.x + 60 && meet == null; x++)
                    if (w.HoldNext(x, y, true) is { } on && w.HoldNext(on.x, on.y, false) == (x, y)) meet = (x, y);
            Assert.That(meet, Is.Not.Null, "somewhere the road's way and the straight way run opposite");
            var (ax, ay) = meet.Value;
            var (bx, by) = w.HoldNext(ax, ay, true).Value;
            var road = w.SpawnRaider("bone-hound", ax, ay, 500);
            var climb = w.SpawnRaider("cliff-stalker", bx, by, 500);
            Ticks(w, 8);
            Assert.That((road.X, road.Y), Is.Not.EqualTo((ax, ay)), "the road raider got by");
            Assert.That((climb.X, climb.Y), Is.Not.EqualTo((bx, by)), "and so did the climber");
        }

        [TestCase("pillar")]
        [TestCase("ice-wall")]
        public void AWizardsWallRoundTheHeartIsBatteredDown(string terrain)
        {
            var w = WithRaider("bone-hound");
            var heart = w.HoldSite.Heart;
            for (int y = heart.y - 2; y <= heart.y + 2; y++)
                for (int x = heart.x - 2; x <= heart.x + 2; x++)
                    if (!(x == heart.x && y == heart.y)) w.Paint(x, y, terrain);
            Until(w, () => w.Hold.Health < P.Health, "it gets through");
        }

        [Test]
        public void AMoltenRaiderWontBurn()
        {
            var w = WithRaider("bone-hound~molten");
            var r = w.Raiders[0];
            r.Status["burning"] = 20;
            Ticks(w, 2);
            Assert.That(r.StatusOf("burning"), Is.Zero);
        }

        [Test]
        public void AFlyerGoesOverEverything()
        {
            var w = WithRaider("ash-wing");
            var r = w.Raiders[0];
            Assert.That(w.Flying(r));
            Until(w, () => w.Hold.Health < P.Health, "it reaches the heart");
            Assert.That(System.Math.Max(System.Math.Abs(r.X - w.HoldSite.Heart.x), System.Math.Abs(r.Y - w.HoldSite.Heart.y)), Is.LessThanOrEqualTo(1));
        }

        [Test]
        public void AFlyerReachesAWizardOnTheWalls()
        {
            var w = WithRaider("ash-wing");
            var r = w.Raiders[0];
            var wall = BuildingRaster.Cells(BuildingRow.Read(w.HoldSite.Castle, w.HoldSite.Row, R.Buildings), R.Buildings).First(c => c.Part == BuildingPart.Wall && w.At(c.X, c.Y).T == "stone");
            Stand(w, Me, (wall.X, wall.Y));
            (r.X, r.Y) = (wall.X + 1, wall.Y);
            r.Wait = 0;
            Until(w, () => w.Stands[Me].Hp < 12, "it strikes");
        }

        [TestCase("frozen")]
        [TestCase("wet")]
        public void AFlyerComesDownForGood(string status)
        {
            var w = WithRaider("ash-wing");
            var r = w.Raiders[0];
            Ticks(w, 8);
            r.Status[status] = 5;
            Ticks(w, 2);
            Assert.That(w.Flying(r), Is.False);
            Ticks(w, 12);
            Assert.That(w.Flying(r), Is.False, "the status has gone and it stays down");
            Assert.That(r.Alive);
        }

        [Test]
        public void ASpellHitsAFlyerOnItsCell()
        {
            var w = WithRaider("ash-wing");
            var r = w.Raiders[0];
            int hp = r.Hp;
            w.Cast(new CastCommand("fire-bolt", r.X, r.Y, 0));
            Ticks(w, 1);
            Assert.That(r.Hp, Is.LessThan(hp));
        }

        // ---------- barricades ----------

        static System.Collections.Generic.List<(int x, int y)> Barricades(World w)
        {
            var found = new System.Collections.Generic.List<(int, int)>();
            foreach (var cell in BuildingRaster.Cells(BuildingRow.Read(w.HoldSite.Castle, w.HoldSite.Row, R.Buildings), R.Buildings))
                if (w.At(cell.X, cell.Y).T == P.BarricadeTerrain) found.Add((cell.X, cell.Y));
            return found;
        }

        // A wave with barricades, at its first raider.
        static World Barricaded(string moment = "late-wave")
        {
            var w = Field();
            Away(w);
            Moment(w, moment);
            Until(w, () => w.Hold.InWave > 0, "the wave starts");
            return w;
        }

        [Test]
        public void BarricadesRiseWithAWaveAndNotBefore()
        {
            var w = Field();
            Away(w);
            Moment(w, "late-wave");
            Assert.That(Barricades(w), Is.Empty);
            Until(w, () => w.Hold.InWave > 0, "the wave starts");
            Assert.That(Barricades(w), Is.Not.Empty);
            Assert.That(World.BarricadesOf(P, 1), Is.Zero);
            Assert.That(World.BarricadesOf(P, 200), Is.EqualTo(P.BarricadeMost));
            Assert.That(Field().HoldSite.Barricades, Is.Not.Empty);
        }

        [Test]
        public void ARaiderBreaksThroughABarricade()
        {
            var w = Barricaded();
            int was = Barricades(w).Count;
            Until(w, () => Barricades(w).Count < was, "they break through");
            Assert.That(w.BlowsOn(0, 0), Is.Zero);
        }

        [Test]
        public void ABarricadeBurns()
        {
            var w = Barricaded();
            foreach (var r in w.Raiders) r.Wait = 100000;
            var (x, y) = Barricades(w)[0];
            int was = Barricades(w).Count;
            w.At(x, y).Fire = 5;
            for (int i = 0; i < 400 && Barricades(w).Count == was; i++)
            {
                foreach (var r in w.Raiders) r.Wait = 100000;
                w.Hold.ToSpawn = 0;
                w.Step();
            }
            Assert.That(Barricades(w).Count, Is.LessThan(was), "the fire takes it");
        }

        [Test]
        public void TheMendingClearsThem()
        {
            var w = Barricaded();
            Assert.That(Barricades(w), Is.Not.Empty);
            w.Hold.Health = 1;
            Until(w, () => !w.HoldRunning, "the keep falls");
            Assert.That(Barricades(w), Is.Empty);
        }
    }
}
