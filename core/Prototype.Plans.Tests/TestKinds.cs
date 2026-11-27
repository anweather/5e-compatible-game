// Every fixture is Unit or Simulation (docs/TESTING_LAYERS.md). The plans' tests build on Prototype.Plans alone, so this
// one can't derive from Chem.Shared.Tests' EveryFixtureHasAKind: it is that check, for this assembly.
using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Prototype.Plans.Tests
{
    [Category("Unit")]
    public sealed class PlansTestKinds
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
}
