// Every fixture is Unit or Simulation (docs/TESTING_LAYERS.md), so `--filter TestCategory=Unit` is the quick ones and
// nothing falls between. Each core test project has a fixture of this (one line), which checks its own assembly.
using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Chem.Tests
{
    public abstract class EveryFixtureHasAKind
    {
        static readonly string[] Kinds = { "Unit", "Simulation" };

        [Test]
        public void EveryFixtureIsUnitOrSimulation()
        {
            var untold = GetType().Assembly.GetTypes()
                .Where(t => !t.IsAbstract && t.GetMethods().Any(m => m.IsDefined(typeof(TestAttribute)) || m.IsDefined(typeof(TestCaseAttribute)) || m.IsDefined(typeof(TestCaseSourceAttribute))))
                .Where(t => t.GetCustomAttributes<CategoryAttribute>().Count(a => Array.IndexOf(Kinds, a.Name) >= 0) != 1)
                .Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            Assert.That(untold, Is.Empty, "give each [Category(\"Unit\")] (a rule, a condition, an effect; at most a small made-up world) or [Category(\"Simulation\")] (a world's ticks, chunks, peers, generation)");
        }
    }

    [Category("Unit")]
    public sealed class GameCoreTestKinds : EveryFixtureHasAKind
    {
    }
}
