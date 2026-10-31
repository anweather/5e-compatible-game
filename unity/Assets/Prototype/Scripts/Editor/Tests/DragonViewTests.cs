using System.IO;
using System.Linq;
using Chem;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine;

// The Dragon's seams to the view, on the real valley's map. The order at the start of a world
// is the one that bites: whatever isn't laid when ChemGrid.StartWorld keeps the starting
// state, the dragon wipes. And the flight drawn must stay in one piece at any frame rate.
public class DragonViewTests
{
    const string Yard = "Assets/Prototype/Scenes/ChemistryYard.unity";
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
    static string ValleyMap()
    {
        var scene = EditorSceneManager.OpenScene(Yard, OpenSceneMode.Additive);
        try
        {
            return scene.GetRootGameObjects().Select(g => g.GetComponentInChildren<ChemGrid>()).First(g => g != null).map;
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    static void FlyAFlight(World world)
    {
        world.ApplyDragon(new DragonCommand());
        for (int i = 0; i < 2000 && world.Dragon != null; i++) world.Step();
        Assert.That(world.Dragon, Is.Null, "the flight ends");
    }

    [Test]
    public void AFlightPutsBackWhatTheValleyLaidAtItsStart()
    {
        var world = ChemGrid.StartWorld(Quick(), ValleyMap(), 1u, out _);
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
