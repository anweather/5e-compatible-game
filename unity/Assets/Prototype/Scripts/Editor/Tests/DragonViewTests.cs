using System.IO;
using System.Linq;
using Chem;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.SceneManagement;
using UnityEngine;

// The Dragon's seams to the view, on the real valley's map. The order at the start of a world
// is the one that bites: whatever isn't laid when ChemGrid.StartWorld keeps the starting
// state, the dragon wipes. And the flight drawn must stay in one piece at any frame rate.
[OnTheValley]
public class DragonViewTests
{
    // The Gauntlet's lava pit, its first booth (world/gauntlet.json, puzzles.json).
    const int LavaX = 303, LavaY = 433;

    static Rules Quick()
    {
        var rules = Rules.Load(Path.GetFullPath("../rules"));
        var p = rules.Sim.Dragon;
        (p.WarningTicks, p.TurnTicks, p.LeavingTicks, p.Speed, p.ChunksPerTick) = (1, 1, 1, 256, 64);
        return rules;
    }

    // The whole valley's map is on the built yard's ChemGrid (ValleyBuilder writes it there).
    static void FlyAFlight(World world)
    {
        world.ApplyDragon(new DragonCommand());
        for (int i = 0; i < 2000 && world.Dragon != null; i++) world.Step();
        Assert.That(world.Dragon, Is.Null, "the flight ends");
    }

    [Test]
    public void AFlightPutsBackWhatTheValleyLaidAtItsStart()
    {
        var world = ChemGrid.StartWorld(Quick(), ChemGrid.CellMap(), 1u, out _);
        Assert.That(world.HasStart, "a world to play in keeps its start");
        Assert.That(world.At(LavaX, LavaY).T, Is.EqualTo(TerrainIds.Lava), "the Gauntlet is laid");
        var puzzleCoils = world.Cells.Count(c => c.T == TerrainIds.Coil);
        Assert.That(puzzleCoils, Is.GreaterThan(0), "the puzzles are laid");

        world.Paint(LavaX, LavaY, TerrainIds.Stone);
        FlyAFlight(world);

        Assert.That(world.At(LavaX, LavaY).T, Is.EqualTo(TerrainIds.Lava), "the Gauntlet's lava comes back, not the map's meadow");
        Assert.That(world.Cells.Count(c => c.T == TerrainIds.Coil), Is.EqualTo(puzzleCoils), "the puzzles come back");
        Assert.That(world.Golf, Is.Not.Null, "the ball is on its tee");
    }

    // A stage the perf counts didn't know threw on every frame of a perf run, and no tick ran.
    [Test]
    public void ThePerfCountsNameEveryStageOfATick()
    {
        var stages = typeof(World).GetNestedType("TickStage", System.Reflection.BindingFlags.NonPublic);
        Assert.That(PerfCounts.StageCount, Is.EqualTo(System.Enum.GetValues(stages).Length));
    }

    [Test]
    public void TheDragonIsDrawnInOnePieceAtAnyFrameRate()
    {
        var rules = Rules.Load(Path.GetFullPath("../rules"));
        var world = new World(rules, 256, 256, 3u);
        world.KeepStart();
        world.ApplyDragon(new DragonCommand());
        var perch = new Vector2(128f, 128f);
        var p = rules.Sim.Dragon;
        DragonFlight.Pose last = default;
        float worst = 0f;
        for (int i = 0; i < 5000 && world.Dragon != null; i++)
        {
            for (int part = 0; part < 4; part++)
            {
                var now = DragonFlight.At(world, part / 4f, perch);
                if (last.Seen && now.Seen && now.Lift < DragonFlight.Clouds * 0.5f && last.Lift < DragonFlight.Clouds * 0.5f)
                    worst = Mathf.Max(worst, (now.Cell - last.Cell).magnitude);
                last = now;
            }
            world.Step();
        }
        Assert.That(world.Dragon, Is.Null, "the flight ends");
        // A quarter of a tick at the pass's speed, or a little more on the wheel round; a jump would be a chunk or more.
        Assert.That(worst, Is.LessThan(p.Speed * 2f), "cells between quarter ticks");
    }

    // How big it's drawn, and what follows from that, comes from sim.json: a constant fails this.
    [Test]
    public void ItsSizeIsTheDatas()
    {
        var rules = Rules.Load(Path.GetFullPath("../rules"));
        double inTheFile = (double)JObject.Parse(File.ReadAllText(Path.GetFullPath("../rules/sim.json")))["dragon"]["scale"];
        Assert.That(rules.Sim.Dragon.Scale, Is.EqualTo(inTheFile));
        Assert.That(DragonFlight.Wingspan(rules.Sim.Dragon), Is.EqualTo(DragonFlight.SpanAtOne * (float)inTheFile).Within(0.001f));

        var modded = rules.WithMods(new[] { ModPatch.Parse("{ 'title': 'Wyrm', 'patch': { 'sim': { 'dragon': { 'scale': 2.5 } } } }".Replace('\'', '"')) }).Sim.Dragon;
        Assert.That(DragonFlight.Wingspan(modded), Is.EqualTo(DragonFlight.SpanAtOne * 2.5f).Within(0.001f), "a mod sets it");
        Assert.That(DragonFlight.Cruise(modded), Is.EqualTo(DragonFlight.CruiseAtOne * 2.5f).Within(0.001f), "the flight's height goes with it");
    }

    // On a pass it flies its scale's distance behind the core's breath, down the strip's middle.
    [Test]
    public void ABiggerDragonFliesFurtherBehindTheSameBreath()
    {
        var rules = Quick();
        var p = rules.Sim.Dragon;
        p.Speed = 6;
        var world = new World(rules, 512, 512, 3u);
        world.KeepStart();
        world.ApplyDragon(new DragonCommand());
        while (!world.BreathBox(out _, out _, out _, out _)) world.Step();
        for (int i = 0; i < 40; i++) world.Step();
        var flight = world.Dragon;
        var pose = DragonFlight.At(world, 0f, Vector2.zero);
        float along = flight.AlongX ? pose.Cell.x : pose.Cell.y;
        Assert.That(Mathf.Abs(along - pose.Lead), Is.EqualTo(DragonFlight.Behind(p)).Within(0.01f));
        p.Scale *= 2.0;
        var bigger = DragonFlight.At(world, 0f, Vector2.zero);
        along = flight.AlongX ? bigger.Cell.x : bigger.Cell.y;
        Assert.That(Mathf.Abs(along - bigger.Lead), Is.EqualTo(DragonFlight.Behind(p)).Within(0.01f));
        Assert.That(bigger.Lead, Is.EqualTo(pose.Lead), "the fire stays where the core has it");
    }

    // The funny bit: a whole roll or none, by the flight's hash, so every peer sees the same turns.
    [Test]
    public void ABarrelRollComesRoundToLevelAndIsTheSameOnEveryPeer()
    {
        var a = Rolls(3u);
        Assert.That(a.Select(r => Mathf.Abs(r.last)), Is.All.EqualTo(360f).Within(0.01f).Or.EqualTo(0f).Within(0.01f), "a whole roll or none");
        Assert.That(a.All(r => Mathf.Abs(r.first) < 0.01f), "each starts level");
        Assert.That(Enumerable.Range(1, 12).Any(seed => Rolls((uint)seed).Any(r => r.last != 0f)), "some flights roll");
        CollectionAssert.AreEqual(a, Rolls(3u));
    }

    static System.Collections.Generic.List<(float first, float last)> Rolls(uint seed)
    {
        var world = new World(Rules.Load(Path.GetFullPath("../rules")), 256, 256, seed);
        world.KeepStart();
        world.ApplyDragon(new DragonCommand());
        var rolls = new System.Collections.Generic.List<(float first, float last)>();
        bool turning = false;
        for (int i = 0; i < 5000 && world.Dragon != null; i++)
        {
            bool now = world.Dragon.Phase == DragonPhase.Passes && world.Dragon.Until > 0;
            if (now)
            {
                float roll = DragonFlight.At(world, 0f, Vector2.zero).Roll;
                if (!turning) rolls.Add((roll, roll));
                else rolls[^1] = (rolls[^1].first, roll);
            }
            turning = now;
            world.Step();
        }
        return rolls;
    }

    [Test]
    public void TheSkyDarkensThroughTheWarningAndLiftsAsItLeaves()
    {
        var rules = Rules.Load(Path.GetFullPath("../rules"));
        var world = new World(rules, 128, 128, 3u);
        world.KeepStart();
        Assert.That(DragonFlight.Darkness(world), Is.EqualTo(0f));
        world.ApplyDragon(new DragonCommand());
        for (int i = 0; i < rules.Sim.Dragon.WarningTicks * 0.8f; i++) world.Step();
        Assert.That(DragonFlight.Darkness(world), Is.EqualTo(1f).Within(0.01f), "dark before the dive");
        for (int i = 0; i < 5000 && world.Dragon != null; i++) world.Step();
        Assert.That(DragonFlight.Darkness(world), Is.EqualTo(0f));
    }
}
