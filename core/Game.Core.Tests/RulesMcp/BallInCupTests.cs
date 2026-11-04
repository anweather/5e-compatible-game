using System.Linq;
using Chem.Mcp;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// The ball in the cup (docs/design/world-acts.md): anyone places a ball and a cup with an act, any spell moves the ball,
    /// players at the course take turns, and the ball going in raises the holed event.
    /// </summary>
    public class BallInCupTests
    {
        const string Ana = "p0", Bo = "p1";

        static World Room()
        {
            var w = new World(ChemistryTests.R, 64, 64, 7, "grass");
            w.OpenRoom(ChangeMode.Open, "Ana", ChemistryTests.R, ModBudgets.Load(ChemistryTests.RulesDir()));
            return w;
        }

        static void Place(World w, string what, int x, int y) =>
            Assert.That(w.ApplyRoom(RoomCommand.Act("Ana", $@"{{""kind"":""golf"",""what"":""{what}"",""cells"":[[{x},{y}]]}}")).Done, Is.True);

        static World Round()
        {
            var w = Room();
            Place(w, World.GolfBall, 20, 20);
            Place(w, World.GolfCup, 40, 20);
            return w;
        }

        static void Run(World w, int ticks)
        {
            for (int i = 0; i < ticks; i++) w.Step();
        }

        static void Stand(World w, string player, int x, int y) => w.ApplyStrike(StrikeCommand.Stand(player, x, y, 10));

        [Test]
        public void ABallAndACupMakeARoundInAnEmptyWorld()
        {
            var w = Room();
            Assert.That(w.Golf, Is.Null);
            Place(w, World.GolfCup, 30, 20);
            Assert.That(w.Golf.NoBall, "a cup alone, nothing to hit");
            w.Cast("fire-bolt", 30, 20, 1);
            Assert.That(w.Golf.Strokes, Is.Zero);
            Place(w, World.GolfBall, 20, 20);
            Assert.That((w.Golf.X, w.Golf.Y, w.Golf.HoleX, w.Golf.HoleY, w.Golf.InPlay), Is.EqualTo((20, 20, 30, 20, true)));
        }

        [Test]
        public void ABallCantGoWhereTheCupIs()
        {
            var w = Room();
            Place(w, World.GolfCup, 30, 20);
            Assert.That(w.ApplyRoom(RoomCommand.Act("Ana", @"{""kind"":""golf"",""what"":""golf-ball"",""cells"":[[30,20]]}")).Done, Is.False);
        }

        [Test]
        public void AnySpellMovesTheBallAndAHigherOneFurther()
        {
            int Far(string spell)
            {
                var w = Round();
                w.Cast(spell, 20, 20, 1);
                Run(w, 40);
                Assert.That(w.Golf.Strokes, Is.EqualTo(1), spell);
                return w.Golf.X - 20;
            }
            int bolt = Far("fire-bolt"), ball = Far("fireball");
            Assert.That(bolt, Is.GreaterThan(0));
            Assert.That(ball, Is.GreaterThan(bolt));
        }

        [Test]
        public void ABlastBesideTheBallSendsItAway()
        {
            var w = Round();
            w.Cast("fireball", 18, 20, 3);
            Run(w, 40);
            Assert.That(w.Golf.X, Is.GreaterThan(20), "from the blast, not along the cast's way");
        }

        [Test]
        public void ABlastFurtherOffIsASofterTap()
        {
            var near = Round();
            var far = Round();
            near.Cast("fireball", 19, 20, 1);
            far.Cast("fireball", 17, 20, 1);
            Run(near, 40);
            Run(far, 40);
            Assert.That(far.Golf.X, Is.LessThan(near.Golf.X));
        }

        [Test]
        public void APlacedCupTakesABallPassingBeside()
        {
            var w = Room();
            Place(w, World.GolfBall, 20, 20);
            Place(w, World.GolfCup, 23, 21);
            w.Cast("fire-bolt", 20, 20, 1);
            Run(w, 12);
            Assert.That(w.Golf.Holed);
            Assert.That((w.Golf.X, w.Golf.Y), Is.EqualTo((23, 21)));
        }

        [Test]
        public void HoldingAChargeHitsItHarder()
        {
            var a = Round();
            var b = Round();
            a.Cast("fire-bolt", 20, 20, 1);
            b.Cast("fire-bolt", 20, 20, 1, 2);
            Run(a, 40);
            Run(b, 40);
            Assert.That(b.Golf.X, Is.GreaterThan(a.Golf.X));
        }

        [Test]
        public void PlayersAtTheCourseTakeTurns()
        {
            var w = Round();
            Stand(w, Ana, 19, 20);
            Stand(w, Bo, 19, 21);
            w.Cast(new CastCommand("gust-of-wind", 19, 20, 1, by: Ana));
            Run(w, 12);
            int at = w.Golf.X;
            Assert.That(w.GolfWaiting(w.Golf));
            w.Cast(new CastCommand("gust-of-wind", at - 1, 20, 1, by: Ana));
            Run(w, 12);
            Assert.That((w.Golf.X, w.Golf.Strokes), Is.EqualTo((at, 1)), "not twice running");
            Assert.That(w.Log.Any(l => l.Text.Contains("someone else's shot")));
            w.Cast(new CastCommand("gust-of-wind", at - 1, 20, 1, by: Bo));
            Run(w, 12);
            Assert.That(w.Golf.Strokes, Is.EqualTo(2), "the other player's shot");
        }

        [Test]
        public void AloneOrWithNobodyAtTheCourseThereIsNoWaiting()
        {
            var w = Round();
            Stand(w, Ana, 19, 20);
            Stand(w, Bo, 60, 60);
            w.Cast(new CastCommand("fire-bolt", 20, 20, 1, by: Ana));
            Run(w, 12);
            w.Cast(new CastCommand("fire-bolt", w.Golf.X, 20, 1, by: Ana));
            Assert.That(w.Golf.Strokes, Is.EqualTo(2));
        }

        [Test]
        public void WhoWalksOffStallsNobodyForLong()
        {
            var w = Round();
            Stand(w, Ana, 19, 20);
            Stand(w, Bo, 19, 21);
            w.Cast(new CastCommand("fire-bolt", 20, 20, 1, by: Ana));
            Run(w, w.Rules.Sim.Golf.TurnTicks + 1);
            w.Cast(new CastCommand("fire-bolt", w.Golf.X, 20, 1, by: Ana));
            Assert.That(w.Golf.Strokes, Is.EqualTo(2));
        }

        [Test]
        public void ARoundIsTheSameOnEveryPeerAndComesThroughASave()
        {
            var a = Round();
            var b = Round();
            foreach (var w in new[] { a, b })
            {
                Stand(w, Ana, 19, 20);
                w.Cast(new CastCommand("fire-bolt", 20, 20, 1, by: Ana));
                Run(w, 2);
            }
            Assert.That(b.CastHash(), Is.EqualTo(a.CastHash()));
            var back = World.Load(a.Save(new ComboTests.Blank()), a.Rules, new ComboTests.Blank(), ModBudgets.Load(ChemistryTests.RulesDir()));
            Assert.That(back.GolfText(), Is.EqualTo(a.GolfText()));
            Assert.That((back.Golf.LastBy, back.Golf.HoleX, back.Golf.Placed), Is.EqualTo((Ana, 40, true)));
        }

        [Test]
        public void TheToolsPlaceThemAndUndoLeavesThem()
        {
            var b = new Workbench(ChemistryTests.Folders);
            var tools = new RulesTools(b);
            int before = b.LiveWorld.Room.Mods.Count;
            StringAssert.Contains("cup", tools.Act("cup", near: "lake"));
            StringAssert.Contains("Any spell", tools.Act("golf-ball", near: "lake"));
            Assert.That(b.LiveWorld.Golf.InPlay && !b.LiveWorld.Golf.NoCup);
            Assert.That(b.LiveWorld.Room.Mods, Has.Count.EqualTo(before));
            StringAssert.Contains("can't be taken back", tools.UndoAct());
            Assert.Throws<RulesToolException>(() => tools.Act("cup", 2));
        }

        [Test]
        public void AHoledTriggerCastsAtTheCupForWhoeverSankIt()
        {
            var b = new Workbench(ChemistryTests.Folders);
            var tools = new RulesTools(b);
            string said = tools.AddTrigger("cup-fire", @"[{""cast"": ""fire-bolt"", ""at"": ""here""}]", on: "holed");
            StringAssert.Contains("goes in the cup", said);
            string draft = said.Split('\n').First(l => l.StartsWith("Draft ", System.StringComparison.Ordinal)).Replace(":", " ").Split(' ')[1];
            tools.Apply(draft, title: "Fire at the cup");
            var w = b.LiveWorld;
            Assert.That(w.ApplyRoom(RoomCommand.Act("Player 1", @"{""kind"":""golf"",""what"":""cup"",""cells"":[[300,240]]}")).Done, Is.True);
            Assert.That(w.ApplyRoom(RoomCommand.Act("Player 1", @"{""kind"":""golf"",""what"":""golf-ball"",""cells"":[[297,240]]}")).Done, Is.True);
            int casts = w.TriggeredCasts;
            w.Cast("gust-of-wind", w.Golf.X - 1, w.Golf.Y, 1);
            Run(w, 12);
            Assert.That(w.Golf.Holed);
            Assert.That(w.TriggeredCasts, Is.EqualTo(casts + 1));
            var cast = w.TriggeredAfter(casts).Single();
            Assert.That((cast.X, cast.Y), Is.EqualTo((w.Golf.HoleX, w.Golf.HoleY)));
        }
    }
}
