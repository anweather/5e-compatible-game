// The Dragon and golf (D22): the reset puts the ball back on its tee with the play areas, and the ball as built comes
// through a save taken mid-flight. Were DragonTests' until golf was a mode of its own.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Simulation")]
    public class GolfDragonTests
    {
        static Rules R => ChemistryTests.R;

        static void Run(World w, int ticks)
        {
            for (int i = 0; i < ticks; i++) w.Step();
        }

        static void FlyOut(World w)
        {
            for (int i = 0; w.Dragon != null && i < 20000; i++) w.Step();
            Assert.That(w.Dragon, Is.Null, "the dragon should have left");
        }

        sealed class Stripes : IChunkSource
        {
            public string Version => "test-stripes-1";

            public void Make(int x, int y, Cell cell)
            {
                if (y % 7 == 0) cell.T = "stone";
                else cell.T = x % 5 == 0 ? "grass" : "dirt";
            }
        }

        [Test]
        public void PlayAreasComeBackAsBuilt()
        {
            var w = GolfMode.Install(new World(R, 64, 64, 2));
            w.ApplyGolf(new GolfCommand(5, 5, 20, 5));
            w.At(30, 30).T = "gate";
            w.At(32, 30).T = "wheel";
            w.LinkGate(30, 30, 32, 30);
            w.KeepStart();
            w.Golf().X = 9;
            w.Golf().Strokes = 3;
            w.At(30, 30).T = "gate_open";
            w.GateLinks[0].Open = true;
            w.ApplyEffect(new EffectCommand(EffectKind.Crack, 0, 0, "", new ThingId(0, 0, 3)));
            w.ApplyDragon(new DragonCommand());
            var seen = new List<ChunkFresh>();
            w.ChunkFreshened += seen.Add;
            FlyOut(w);
            Assert.That(w.Golf().X, Is.EqualTo(5));
            Assert.That(w.Golf().Strokes, Is.EqualTo(0));
            Assert.That(w.At(30, 30).T, Is.EqualTo("gate"));
            Assert.That(w.GateLinks[0].Open, Is.False);
            Assert.That(w.IsCracked(new ThingId(0, 0, 3)), Is.False);
            Assert.That(seen.Select(s => (s.CX, s.CY)), Does.Contain((0, 0)), "the view hears, to bring its props back");
        }

        [Test]
        public void ASaveMidFlightLoadsAndGoesOnTheSame()
        {
            var source = new Stripes();
            var w = GolfMode.Install(new World(R, 192, 128, 5));
            w.MakeFrom(source);
            w.ApplyGolf(new GolfCommand(5, 5, 20, 5));
            w.KeepStart();
            for (int x = 0; x < 192; x += 2) w.At(x, 30).Height = 2;
            Run(w, 70);
            w.Golf().Strokes = 2;
            w.ApplyDragon(new DragonCommand());
            Run(w, R.Sim.Dragon.WarningTicks + 30);
            string save = w.Save(source);
            var back = GolfMode.Install(World.Load(save, R, source));
            Assert.That(back.DragonSystem.Text(), Is.EqualTo(w.DragonSystem.Text()));
            Assert.That(back.Save(source), Is.EqualTo(save));
            FlyOut(w);
            FlyOut(back);
            Assert.That(back.CellHash(), Is.EqualTo(w.CellHash()));
            Assert.That(back.DragonSystem.Text(), Is.EqualTo(w.DragonSystem.Text()));
            Assert.That(back.Golf().Strokes, Is.EqualTo(0), "the ball as built came through the save");
        }
    }
}
