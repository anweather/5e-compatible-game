using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Playtest;

// Tests for the playtest bot's Unity-free logic (playtest/DESIGN.md): the
// dice and goal choice replay from a seed, the detectors fire once on what
// they should and not on what they shouldn't, known issues aren't filed, and
// a replay is checked against its original. Run by Unity's EditMode runner
// and by dotnet test core (core/Game.Core.Tests links them).
public class PlaytestLogicTests
{
    static GoalOptions Options() => new GoalOptions
    {
        Places = new[] { "tower", "ball court", "defense field" },
        Spells = new[] { "fire-bolt", "create-water", "gust-of-wind" },
        Combos = new[] { ("grease", "fire-bolt") },
        MenuPages = new[] { "mods" },
        HasSeats = true,
        HasItems = true,
    };

    // A wizard on the ground at height 10 (the ground's height), at (x, y, z).
    static WizardWatch.Sample W(float x, float y, float z, bool pushing, bool botMoved, string place) =>
        new WizardWatch.Sample { X = x, Y = y, Z = z, Ground = 10, Grounded = true, Pushing = pushing, BotMoved = botMoved, Place = place };

    // A prop at (10, 1, 10) that spawned there, awake, at a speed, in an active chunk or not.
    static PropWatch.Sample P(string id, string kind, float speed, bool chunkActive) =>
        new PropWatch.Sample { Id = id, Kind = kind, X = 10, Y = 1, Z = 10, Speed = speed, SpawnX = 10, SpawnZ = 10, ChunkActive = chunkActive };

    static List<string> Goals(ulong seed, int n, IReadOnlyDictionary<string, int> done = null)
    {
        var dice = new PlaytestRandom(seed);
        return Enumerable.Range(0, n).Select(_ => GoalPicker.Next(dice, Options(), done).Key).ToList();
    }

    [Test]
    public void TheSameSeedPicksTheSameGoals()
    {
        Assert.That(Goals(42, 30), Is.EqualTo(Goals(42, 30)));
        Assert.That(Goals(42, 30), Is.Not.EqualTo(Goals(43, 30)), "another seed, another run");
    }

    [Test]
    public void GoalsLeanTowardsWhatWasntDoneLately()
    {
        var done = new Dictionary<string, int> { ["Visit:tower"] = 50 };
        int fresh = Goals(7, 2000).Count(k => k == "Visit:tower");
        int stale = Goals(7, 2000, done).Count(k => k == "Visit:tower");
        Assert.That(stale, Is.LessThan(fresh / 5), $"the tower came up {stale} times after being done a lot, {fresh} times fresh");
        Assert.That(Goals(7, 2000).Distinct().Count(), Is.EqualTo(3 + 3 + 1 + 1 + 1 + 1 + 1), "every goal comes up in a long run");
    }

    [Test]
    public void TheDiceStayInRange()
    {
        var dice = new PlaytestRandom(1);
        for (int i = 0; i < 1000; i++)
        {
            Assert.That(dice.Range(3, 7), Is.InRange(3, 6));
            Assert.That(dice.Value(), Is.GreaterThanOrEqualTo(0.0).And.LessThan(1.0));
        }
    }

    [Test]
    public void AWizardUnderTheGroundIsReportedOnce()
    {
        var watch = new WizardWatch(0, 0, 300, 300);
        var seen = new List<Candidate>();
        for (int f = 0; f < 10; f++) seen.AddRange(watch.Frame(f, W(50, f < 5 ? 10 : 5, 50, false, false, "meadow")));
        Assert.That(seen.Select(c => c.Signature), Is.EqualTo(new[] { "wizard-fell:meadow" }));
        Assert.That(seen[0].Category, Is.EqualTo(Category.GameBreaking));
    }

    [Test]
    public void AWizardPushingWithoutGettingAnywhereIsStuckButABotTeleportIsnt()
    {
        var watch = new WizardWatch(0, 0, 300, 300);
        var seen = new List<Candidate>();
        for (int f = 0; f < WizardWatch.StuckFrames + 5; f++) seen.AddRange(watch.Frame(f, W(50, 10, 50, true, false, "tower")));
        Assert.That(seen.Single().Signature, Is.EqualTo("wizard-stuck:tower"));
        Assert.That(seen.Single().LowConfidence, Is.True, "often the bot's own steering");

        var moved = new WizardWatch(0, 0, 300, 300);
        var none = new List<Candidate>();
        for (int f = 0; f < WizardWatch.StuckFrames + 5; f++) none.AddRange(moved.Frame(f, W(50, 10, 50, true, f % 100 == 0, "tower")));
        Assert.That(none, Is.Empty, "the bot moved it itself now and then");
    }

    [Test]
    public void LeavingTheWorldAndNaNAreGameBreaking()
    {
        var watch = new WizardWatch(0, 0, 300, 300);
        Assert.That(watch.Frame(1, W(-5, 10, 50, false, false, "edge")).Single().Signature, Is.EqualTo("wizard-out:edge"));
        Assert.That(watch.Frame(2, W(float.NaN, 10, 50, false, false, "edge")).Single().Signature, Is.EqualTo("wizard-nan:edge"));
    }

    [Test]
    public void FrameSpikesCountOncePerPlaceAndNotWhileLoading()
    {
        var watch = new FrameWatch();
        var seen = watch.Frame(10, 500, "yard").Concat(watch.Frame(100, 150, "yard")).Concat(watch.Frame(101, 150, "yard")).Concat(watch.Frame(102, 20, "tower")).ToList();
        Assert.That(seen.Single().Signature, Is.EqualTo("frame-spike:yard"));
        Assert.That(watch.Percentile(0.5), Is.EqualTo(150f));
    }

    [Test]
    public void ExceptionsAreKeyedByTypeAndWhereTheyHappened()
    {
        var a = LogWatch.Line(5, "NullReferenceException: Object reference not set", "PhysicsProp.Sleep () (at Assets/x.cs:10)\nThingSpawner.Update ()", true);
        var b = LogWatch.Line(9, "NullReferenceException: something else", "PhysicsProp.Sleep () (at Assets/x.cs:12)", true);
        Assert.That(a.Signature, Is.EqualTo("exception:NullReferenceException@PhysicsProp.Sleep"));
        Assert.That(b.Signature, Is.EqualTo(a.Signature), "the same paper cut, whatever the details");
        Assert.That(LogWatch.Line(5, "just a log", "", false), Is.Null);
    }

    // playtest/known-issues.json, as the runner reads it.
    static IReadOnlyList<KnownIssue> KnownFile() =>
        KnownIssues.FromJson(System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot(), "playtest", "known-issues.json")));

    // The repository's root: above the Unity project in the Editor, else up from the test's folder.
    // (Chem.Tests.ChemistryTests.RulesDir is in another assembly in Unity, so it can't be used here.)
    static string RepoRoot()
    {
#if UNITY_5_3_OR_NEWER
        return System.IO.Path.GetFullPath(System.IO.Path.Combine(UnityEngine.Application.dataPath, "..", ".."));
#else
        for (var dir = new System.IO.DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
            if (System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "playtest", "known-issues.json"))) return dir.FullName;
        throw new System.IO.FileNotFoundException("playtest/known-issues.json not found above the tests");
#endif
    }

    [Test]
    public void AKnownIssueIsCountedButNotFiledTwice()
    {
        var log = new CandidateLog(new KnownIssues(KnownFile()));
        var crash = new Candidate("crash", "quit", Category.GameBreaking, 900, "fault in D3D12Core.dll on quit");
        Assert.That(log.Add(crash), Is.True);
        Assert.That(crash.KnownIssue, Is.EqualTo("quit-crash-d3d12"), "on a build from before its fix");
        Assert.That(log.Add(new Candidate("crash", "quit", Category.GameBreaking, 950, "again")), Is.False);
        Assert.That(log.CountOf("crash:quit"), Is.EqualTo(2));
        Assert.That(log.Candidates.Count, Is.EqualTo(1));
    }

    [Test]
    public void AKnownIssueOnABuildWithItsFixIsARegression()
    {
        var fixedBuild = new KnownIssues(KnownFile(), commit => commit == "4a61ee3");
        var crash = new Candidate("crash", "quit", Category.GameBreaking, 900, "fault in D3D12Core.dll on quit");
        Assert.That(fixedBuild.Mark(crash), Is.False, "filed: the fix is in this build");
        Assert.That(crash.KnownIssue, Is.Null);
    }

    [Test]
    public void SoundThatClipsOrGoesSilentIsReported()
    {
        var watch = new AudioWatch();
        var seen = new List<Candidate>();
        seen.AddRange(watch.Frame(1, 1.0f, false, true, "yard"));
        for (int f = 2; f < AudioWatch.SilentFrames + 10; f++) seen.AddRange(watch.Frame(f, 0f, false, true, "yard"));
        Assert.That(seen.Select(c => c.Detector), Is.EqualTo(new[] { "audio-clip", "audio-silent" }));
        Assert.That(new AudioWatch().Frame(1, 0f, false, false, "yard"), Is.Empty, "silence with nothing playing is fine");
    }

    [Test]
    public void MagentaAndBlackPicturesAreReported()
    {
        byte[] Fill(byte r, byte g, byte b, int n) => Enumerable.Range(0, n).SelectMany(_ => new[] { r, g, b }).ToArray();
        Assert.That(ImageCheck.Pixels(1, Fill(255, 0, 255, 100), "x").Select(c => c.Detector), Does.Contain("image-magenta"));
        Assert.That(ImageCheck.Pixels(1, Fill(0, 0, 0, 100), "x").Single().Detector, Is.EqualTo("image-black"));
        Assert.That(ImageCheck.Pixels(1, Fill(90, 120, 60, 100), "x"), Is.Empty, "a meadow");
        Assert.That(ImageCheck.Pixels(1, Fill(0, 0, 0, 100), "x", darkExpected: true), Is.Empty, "night is dark");
    }

    [Test]
    public void PropsFlungOrAwakeWhereTheyShouldSleepAreReportedOncePerProp()
    {
        var watch = new PropWatch();
        var seen = watch.Frame(1, P("3,4#0", "crate", 80, true))
            .Concat(watch.Frame(2, P("3,4#0", "crate", 80, true)))
            .Concat(watch.Frame(3, P("3,4#1", "barrel", 0, false))).ToList();
        Assert.That(seen.Select(c => c.Signature), Is.EqualTo(new[] { "prop-flung:crate", "prop-awake-inactive:barrel" }));
    }

    [Test]
    public void AReplayMatchesItsOriginalUntilItDoesSomethingElse()
    {
        RunReport Run(params (int f, string a, string d)[] steps) => new RunReport
        {
            Trace = steps.Select(s => new TraceStep { Frame = s.f, Action = s.a, Detail = s.d }).ToList(),
        };
        var original = Run((10, "goal", "go to the tower"), (12, "move", "0.0,1.0"), (400, "cast", "fire-bolt at 120,80"));
        var same = Run((10, "goal", "go to the tower"), (12, "move", "0.0,1.0"), (400, "cast", "fire-bolt at 120,80"), (900, "jump", ""));
        var other = Run((10, "goal", "go to the tower"), (13, "move", "0.0,1.0"));
        Assert.That(ReplayCheck.FirstDifference(original, same, 500), Is.Null, "the same up to frame 500");
        Assert.That(ReplayCheck.FirstDifference(original, other, 500).Frame, Is.EqualTo(13));

        var seen = new Candidate("exception", "X@Y", Category.GameBreaking, 405, "boom");
        same.Candidates.Add(new Candidate("exception", "X@Y", Category.GameBreaking, 410, "boom"));
        Assert.That(ReplayCheck.Reproduced(seen, same), Is.True);
        Assert.That(ReplayCheck.Reproduced(seen, other), Is.False);

        // A replay that drifted still reproduces it if it met it doing the same thing.
        var air = new Candidate("wizard-airborne", "waterfall", Category.Gameplay, 1955, "") { Goal = "go to the waterfall" };
        other.Candidates.Add(new Candidate("wizard-airborne", "waterfall", Category.Gameplay, 1786, "") { Goal = "go to the waterfall" });
        Assert.That(ReplayCheck.Reproduced(air, other), Is.True);
    }

    [Test]
    public void RunReportsRoundTripThroughJson()
    {
        var run = new RunReport { RunId = "r1", Seed = 123456789012UL, Build = "abc1234", Goals = { "go to the tower" } };
        run.Trace.Add(new TraceStep { Frame = 3, Action = "goal", Detail = "go to the tower" });
        run.Candidates.Add(new Candidate("wizard-stuck", "tower", Category.Gameplay, 40, "stuck", lowConfidence: true) { Screenshot = "shots/000040.jpg" });
        string json = run.ToJson();
        Assert.That(json, Does.Contain("\"Gameplay\""), "categories are written by name");
        var back = RunReport.FromJson(json);
        Assert.That(back.Seed, Is.EqualTo(run.Seed));
        var c = back.Candidates.Single();
        Assert.That((c.Signature, c.Category, c.Frame, c.LowConfidence, c.Screenshot), Is.EqualTo(("wizard-stuck:tower", Category.Gameplay, 40, true, "shots/000040.jpg")));
        Assert.That(back.Trace.Single().Detail, Is.EqualTo("go to the tower"));
        Assert.That(ReplayCheck.Steps(back, 100).First(), Does.Contain("go to the tower"));
    }

    // A run with a scripted body instead of the game: the same logic as PlaytestBot (goals from the
    // dice and coverage, every frame through the detectors, a sync step every second), fed by a
    // wizard that walks to a dice-picked spot each goal. bumpAt nudges the wizard on one frame, as a
    // real-time leak would.
    static RunReport ScriptedRun(ulong seed, IReadOnlyDictionary<string, int> coverage, int bumpAt = -1)
    {
        var dice = new PlaytestRandom(seed);
        var run = new RunReport { Seed = seed, Coverage = new Dictionary<string, int>(coverage) };
        var wizard = new WizardWatch(-100, -100, 100, 100);
        var frames = new FrameWatch();
        float x = 0, z = 0, tx = 0, tz = 0;
        int goalEnds = 0;
        string place = "start";
        for (int frame = 1; frame <= 1800; frame++)
        {
            if (frame >= goalEnds)
            {
                var g = GoalPicker.Next(dice, Options(), run.Coverage);
                run.Goals.Add(g.Key);
                run.Trace.Add(new TraceStep { Frame = frame, Action = "goal", Detail = g.ToString() });
                (tx, tz) = (dice.Range(-50f, 50f), dice.Range(-50f, 50f));
                goalEnds = frame + dice.Range(60, 300);
                place = g.Target ?? "start";
            }
            x += System.Math.Sign(tx - x) * 0.1f;
            z += System.Math.Sign(tz - z) * 0.1f;
            if (frame == bumpAt) x += 1f;
            run.Candidates.AddRange(wizard.Frame(frame, W(x, 10, z, true, false, place)));
            run.Candidates.AddRange(frames.Frame(frame, dice.Range(10f, 40f), place));
            if (frame % ReplayCheck.SyncEvery == 0)
                run.Trace.Add(new TraceStep { Frame = frame, Action = ReplayCheck.Sync, Detail = ReplayCheck.SyncDetail(frame / 3, x, 10, z) });
        }
        return run;
    }

    static readonly Dictionary<string, int> Lately = new() { ["Visit:tower"] = 3, ["Cast:fire-bolt"] = 2 };

    [Test]
    public void TheSameSeedAndFramesGiveTheSameTrace()
    {
        var a = ScriptedRun(7, Lately);
        var b = ScriptedRun(7, Lately);
        Assert.That(ReplayCheck.FirstDifference(a, b, int.MaxValue), Is.Null);
        Assert.That(b.ToJson(), Is.EqualTo(a.ToJson()), "goals, trace and candidates alike");
        Assert.That(a.Trace.Count(s => s.Action == ReplayCheck.Sync), Is.EqualTo(1800 / ReplayCheck.SyncEvery));
        Assert.That(ScriptedRun(8, Lately).Goals, Is.Not.EqualTo(a.Goals), "another seed, another run");
    }

    [Test]
    public void TheFirstSyncStepThatDiffersIsWhereARunDrifted()
    {
        var a = ScriptedRun(7, Lately);
        var drifted = ScriptedRun(7, Lately, bumpAt: 700);
        var d = ReplayCheck.FirstDifference(a, drifted, int.MaxValue);
        Assert.That((d.Frame, d.Action), Is.EqualTo((720, ReplayCheck.Sync)), "the sync after the nudge, not the next goal");
        Assert.That(ReplayCheck.FirstDifference(a, drifted, 719), Is.Null);
        Assert.That(ReplayCheck.Steps(a, 1800).Any(s => s.Contains(ReplayCheck.Sync)), Is.False, "sync steps aren't steps to reproduce");
    }

    [Test]
    public void AReplayPicksItsGoalsAgainstTheOriginalsCoverage()
    {
        var original = ScriptedRun(11, Lately);
        var saved = RunReport.FromJson(original.ToJson());
        Assert.That(saved.Coverage, Is.EqualTo(Lately));
        Assert.That(ScriptedRun(11, saved.Coverage).Goals, Is.EqualTo(original.Goals));
        // coverage.json moves on after every run; picking against today's changes the run.
        var today = original.Goals.GroupBy(g => g).ToDictionary(g => g.Key, g => g.Count());
        Assert.That(ScriptedRun(11, today).Goals, Is.Not.EqualTo(original.Goals));
    }

    [Test]
    public void UnitysRandomIsSeededFromTheRun()
    {
        Assert.That(ReplayCheck.UnitySeed(0x0123456789ABCDEFUL), Is.EqualTo(unchecked((int)0x88888888)));
        Assert.That(ReplayCheck.UnitySeed(42), Is.EqualTo(42));
        Assert.That(ReplayCheck.UnitySeed(1UL << 32), Is.EqualTo(1), "the high half counts too");
    }

    [Test]
    public void ScreenshotsDontCountWhenTwoRunsAreCompared()
    {
        var a = ScriptedRun(7, Lately);
        var b = ScriptedRun(7, Lately);
        // A frame spike's picture is timed by the wall clock: one run took it, the other didn't.
        b.Trace.Insert(5, new TraceStep { Frame = b.Trace[5].Frame, Action = ReplayCheck.Shot, Detail = "shots/000123-frame-spike.jpg" });
        Assert.That(ReplayCheck.FirstDifference(a, b, int.MaxValue), Is.Null);
    }

    [Test]
    public void AGoalCanBeAskedForByItsKey()
    {
        var dice = new PlaytestRandom(3);
        var combo = GoalPicker.ByKey(dice, Options(), "Combo:grease+fire-bolt");
        Assert.That((combo.Kind, combo.Spell, combo.Spell2), Is.EqualTo((GoalKind.Combo, "grease", "fire-bolt")));
        Assert.That(GoalPicker.ByKey(dice, Options(), "Visit:tower").Target, Is.EqualTo("tower"));
        Assert.That(GoalPicker.ByKey(dice, Options(), "Visit:the moon"), Is.Null, "not a place here");
        Assert.That(GoalPicker.ByKey(dice, Options(), "Idle").Kind, Is.EqualTo(GoalKind.Idle));
    }

    [Test]
    public void CastsByElementsAreSometimesHeldToCharge()
    {
        var dice = new PlaytestRandom(5);
        var charges = Enumerable.Range(0, 300).Select(_ => Chains.ChargeFor(dice)).ToList();
        Assert.That(charges.Distinct().OrderBy(c => c), Is.EqualTo(new[] { 1, 2, 3 }));
        Assert.That(charges.Count(c => c == 1), Is.InRange(100, 200), "about half are tapped");
        Assert.That(Chains.ById("growth-on-burnt").TriggerCharge, Is.EqualTo(3), "one chain needs the full charge");
    }

    [Test]
    public void CastsByElementsLeanTowardsTheOnesNotCastLately()
    {
        var options = new GoalOptions { ElementCasts = new[] { "fire", "water+earth", "air+fire" }, ChainIds = new[] { "forge-on-mud" } };
        List<string> Run(IReadOnlyDictionary<string, int> done)
        {
            var dice = new PlaytestRandom(5);
            return Enumerable.Range(0, 3000).Select(_ => GoalPicker.Next(dice, options, done).Key).ToList();
        }
        var fresh = Run(null);
        Assert.That(fresh, Does.Contain("Elements:water+earth").And.Contain("Chain:forge-on-mud"));
        var stale = Run(new Dictionary<string, int> { ["Elements:water+earth"] = 9 });
        Assert.That(stale.Count(k => k == "Elements:water+earth"), Is.LessThan(fresh.Count(k => k == "Elements:water+earth") / 3));
        var chain = GoalPicker.ByKey(new PlaytestRandom(1), options, "Chain:forge-on-mud");
        Assert.That((chain.Kind, chain.Target, chain.ToString()), Is.EqualTo((GoalKind.Chain, "forge-on-mud", "play the chain forge-on-mud")));
        Assert.That(GoalPicker.ByKey(new PlaytestRandom(1), options, "Elements:air+fire").ToString(), Is.EqualTo("cast air then fire by elements"));
    }

    [Test]
    public void TheWorstFrameLeavesOutLoadingAndTheBotsScreenshots()
    {
        var watch = new FrameWatch();
        Assert.That(watch.Frame(0, 930f, "yard"), Is.Empty);
        Assert.That(watch.Frame(1, 700f, "yard"), Is.Empty);
        Assert.That(watch.Frame(FrameWatch.LoadingFrames + 1, 40f, "yard"), Is.Empty);
        watch.ShotTaken(100);
        Assert.That(watch.Frame(101, 260f, "yard"), Is.Empty);
        Assert.That(watch.Frame(200, 55f, "yard"), Is.Empty);
        Assert.That(watch.Worst, Is.EqualTo(55f));
    }

    [Test]
    public void ASpikeJustAfterTheBotsOwnScreenshotIsntTheGames()
    {
        var watch = new FrameWatch();
        watch.ShotTaken(100);
        Assert.That(watch.Frame(102, 140f, "tower"), Is.Empty, "the screenshot's readback");
        Assert.That(watch.Frame(103, 140f, "tower"), Is.Empty);
        Assert.That(watch.Frame(104, 140f, "tower").Single().Detector, Is.EqualTo("frame-spike"), "past the shot's frames, a spike counts");
    }
}
