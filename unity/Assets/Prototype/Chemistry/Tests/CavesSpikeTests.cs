using System;
using System.Diagnostics;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// THROWAWAY spike for docs/CAVES.md (branch caves-spike, never merged): option C, a level
    /// below the surface as its own chunked 2.5D world under part of the map, linked to the surface
    /// where a column is open (dug down to the cave's ceiling). Checks water falls down a dug
    /// shaft into the cave, the pair stays deterministic, and what the level and the links cost.
    /// </summary>
    public class CavesSpikeTests
    {
        static Rules R => ChemistryTests.R;

        /// <summary>A level under the surface: its own world over [OX, OX+W) × [OY, OY+H) of the surface's cells.</summary>
        sealed class Level
        {
            public World World;
            public int OX, OY;
            /// <summary>A surface cell dug to this Height or lower opens into the level (sim.height.min: the bottom).</summary>
            public int OpenAt;
            public int Opened;
        }

        /// <summary>
        /// One tick of the pair, in a fixed order (surface, level, links), as one World.Step would be:
        /// water falls down every open shaft, as much as the cave cell can hold; gas would rise the
        /// same way (not in the spike).
        /// </summary>
        static void Step(World surface, Level level)
        {
            surface.Step();
            level.World.Step();
            Link(surface, level);
        }

        static void Link(World surface, Level level)
        {
            int max = R.Sim.WaterMaxLevel;
            level.Opened = 0;
            for (int y = 0; y < level.World.H; y++)
                for (int x = 0; x < level.World.W; x++)
                {
                    var top = surface.At(level.OX + x, level.OY + y);
                    var below = level.World.At(x, y);
                    if (top.Height > level.OpenAt || level.World.IsBlocking(below)) continue;
                    level.Opened++;
                    int n = Math.Min(top.Water, max - below.Water);
                    if (n <= 0 || top.Frozen) continue;
                    top.Water -= n;
                    below.Water += n;
                }
        }

        // The surface: grass, a pond held in by a bank to the west of the shaft.
        static World Surface()
        {
            var w = ChemistryTests.Room(128, 128);
            for (int y = 1; y < 127; y++)
                for (int x = 1; x < 127; x++) w.At(x, y).T = "grass";
            for (int y = 50; y < 60; y++)
                for (int x = 40; x < 50; x++) w.At(x, y).Water = 8;
            return w;
        }

        // The cave, one chunk under the surface's (32..95, 32..95): rock with a tunnel and a hollow for a lake.
        static Level Cave()
        {
            var w = new World(R, 64, 64, 9, "bedrock");
            for (int x = 8; x < 40; x++)
                for (int y = 20; y < 24; y++) w.At(x, y).T = "dirt";            // a tunnel east-west
            for (int y = 24; y < 44; y++)
                for (int x = 12; x < 32; x++) { var c = w.At(x, y); c.T = "dirt"; c.Height = -2; } // the hollow, under the shaft's end of the tunnel
            return new Level { World = w, OX = 32, OY = 32, OpenAt = R.Sim.Height.Min };
        }

        static (World surface, Level cave) Dug()
        {
            var s = Surface();
            var cave = Cave();
            // A shaft from the pond's east edge down to the tunnel (surface 50..51, 53..54 is cave 18..19, 21..22).
            for (int y = 53; y < 55; y++)
                for (int x = 50; x < 52; x++) s.At(x, y).Height = R.Sim.Height.Min;
            // And a trench from the pond to the shaft, so the pond drains into it.
            for (int y = 53; y < 55; y++) s.At(49, y).Height = -3;
            return (s, cave);
        }

        [Test]
        public void WaterRunsDownADugShaftIntoTheCave()
        {
            var (s, cave) = Dug();
            int pond = Total(s), before = Total(cave.World);
            for (int t = 0; t < 400; t++) Step(s, cave);
            Assert.That(cave.Opened, Is.EqualTo(4), "the 2×2 shaft opens into the tunnel");
            Assert.That(Total(cave.World), Is.GreaterThan(before + pond / 8), "a good part of the pond has fallen into the cave");
            Assert.That(Total(s) + Total(cave.World), Is.EqualTo(pond + before), "no water made or lost by the link");
            int inHollow = 0;
            for (int y = 24; y < 44; y++)
                for (int x = 12; x < 32; x++) inHollow += cave.World.At(x, y).Water;
            Assert.That(inHollow, Is.GreaterThan(0), "it ran along the tunnel into the hollow: an underground lake");

            var (s2, cave2) = Dug();
            for (int t = 0; t < 400; t++) Step(s2, cave2);
            Assert.That((s2.CellHash(), cave2.World.CellHash()), Is.EqualTo((s.CellHash(), cave.World.CellHash())), "deterministic");
        }

        static int Total(World w)
        {
            int n = 0;
            foreach (var c in w.Cells) n += c.Water;
            return n;
        }

        [Test]
        public void WhatALevelAndItsLinksCost()
        {
            var (s, cave) = Dug();
            for (int t = 0; t < 100; t++) Step(s, cave); // warm up (JIT), and water on its way down
            var surface = new Stopwatch();
            var level = new Stopwatch();
            var links = new Stopwatch();
            const int ticks = 300;
            for (int t = 0; t < ticks; t++)
            {
                surface.Start();
                s.Step();
                surface.Stop();
                level.Start();
                cave.World.Step();
                level.Stop();
                links.Start();
                Link(s, cave);
                links.Stop();
            }
            string line = $"per tick: the surface (2×2 chunks, a pond draining) {surface.Elapsed.TotalMilliseconds / ticks:0.000} ms; " +
                $"the cave level (1 chunk, water arriving) {level.Elapsed.TotalMilliseconds / ticks:0.000} ms; the links {links.Elapsed.TotalMilliseconds / ticks:0.000} ms";
            TestContext.WriteLine(line);
            Assert.Pass(line);
        }
    }
}
