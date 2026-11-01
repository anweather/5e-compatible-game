using System.IO;
using Chem.Mcp;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// The forest-fire scenario to burn-out, pinned: its fingerprints (cells, random stream, active
    /// chunks, log, dampness, chunks' last live ticks) were taken on the core before any of the
    /// work to make its tick cheaper, so a fix that changes a result fails here.
    /// </summary>
    public class ForestFireTests
    {
        static string RulesDir => ChemistryTests.RulesDir();
        static string Root => Path.GetDirectoryName(RulesDir);

        static void AssertRun(ForestFire fire, params string[] expected)
        {
            Assert.That(fire.Fingerprint(), Is.EqualTo(expected[0]), "as the Fireball lands");
            for (int k = 1; k < expected.Length; k++)
            {
                for (int i = 0; i < Every; i++)
                {
                    fire.PlanChunks();
                    fire.World.Step();
                }
                Assert.That(fire.Fingerprint(), Is.EqualTo(expected[k]));
            }
        }

        const int Every = 126;

        [Test]
        public void AFireballInTheForestBurnsOutTheSame() =>
            AssertRun(new ForestFire(ChemistryTests.R, Root),
                "tick 60 cells a44fccf rng 695bcfa9 active 42 log 4 hidden 7ae2f4a9",
                "tick 186 cells b7c8eeb2 rng f1ba2a73 active 42 log 4 hidden ce3a196b",
                "tick 312 cells a832da25 rng c569d80a active 42 log 4 hidden aabacaa");

        [Test]
        public void TheWildfireModBurnsOutTheSame()
        {
            var wildfire = ChemistryTests.R.WithMods(new[] { ModPatch.Parse(File.ReadAllText(Path.Combine(RulesDir, "mods", "wildfire.json"))) });
            AssertRun(new ForestFire(wildfire, Root),
                "tick 60 cells a44fccf rng 695bcfa9 active 42 log 4 hidden 7ae2f4a9",
                "tick 186 cells d9084f36 rng e0c3b4b8 active 42 log 4 hidden ce3a196b",
                "tick 312 cells 637a0bbe rng 51b4d644 active 42 log 4 hidden d61db7bb");
        }

        [Test]
        public void WalkingAwayFromItBurnsOutTheSame() =>
            AssertRun(new ForestFire(ChemistryTests.R, Root, ForestFire.Seed, walks: true),
                "tick 60 cells a44fccf rng 695bcfa9 active 42 log 4 hidden 7ae2f4a9",
                "tick 186 cells b7c8eeb2 rng f1ba2a73 active 42 log 4 hidden ce3a196b",
                "tick 312 cells a832da25 rng c569d80a active 35 log 4 hidden 1a171c9");
    }
}
