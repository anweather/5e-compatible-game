using System.Collections.Generic;
using NUnit.Framework;
using Playtest;
using Playtest.Runner;

namespace Chem.Tests
{
    /// <summary>
    /// The playtest runner's decisions (core/Playtest.Runner, playtest/DESIGN.md): the preflight,
    /// coverage, which player it runs, how a run's ending becomes a candidate, and the crash
    /// events it reads. The parts that launch players or Unity are checked by running them.
    /// </summary>
    public class RunnerTests
    {
        static Preflight.State State(double gb, params string[] unity) => new Preflight.State { FreeGB = gb, Unity = unity };

        [Test]
        public void ThePreflightWaitsForMemoryABuildOrAVirtualPlayer()
        {
            Assert.That(Preflight.Why(State(16), 8), Is.Null, "clear");
            Assert.That(Preflight.Why(State(5.2, @"Unity.exe -projectpath C:\x\unity"), 8), Does.StartWith("only 5.2 GB"));
            Assert.That(Preflight.Why(State(16, @"Unity.exe -batchmode -quit -projectPath C:\x"), 8), Does.Contain("batch job"));
            Assert.That(Preflight.Why(State(16, @"Unity.exe -projectpath C:\x\Library\VP\mppmcd4f2e91"), 8), Does.Contain("Multiplayer Play Mode"));
            Assert.That(Preflight.Why(State(16, @"Unity.exe -projectpath C:\x\unity"), 8), Is.Null, "one Editor open is fine");
            Assert.That(Preflight.IsEditor(@"Unity.exe -projectpath C:\x"), Is.True);
            Assert.That(Preflight.IsEditor(@"Unity.exe -projectpath C:\x AssetImportWorker1"), Is.False);
        }

        [Test]
        public void CoverageFadesByAFifthAndCountsTheRun()
        {
            var before = new Dictionary<string, int> { ["Visit:tower"] = 10, ["Cast:fire-bolt"] = 1 };
            var after = Coverage.After(before, new[] { "Visit:tower", "Sit:seat" });
            Assert.That(after, Is.EqualTo(new SortedDictionary<string, int> { ["Sit:seat"] = 1, ["Visit:tower"] = 9 }), "1 faded to 0 and went");
        }

        [Test]
        public void TheBotsOwnBuildIsPreferred()
        {
            Assert.That(Paths.ChoosePlayer(@"C:\pb", @"C:\here", _ => true), Does.StartWith(@"C:\pb"));
            Assert.That(Paths.ChoosePlayer(@"C:\pb", @"C:\here", _ => false), Does.StartWith(@"C:\here"));
        }

        [Test]
        public void AGivenPlayerIsFoundInItsFolder()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "playtest-player-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                string deep = System.IO.Path.Combine(dir, "SpellChemistryValley-win", "Player");
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(deep, "Extra"));
                string exe = System.IO.Path.Combine(deep, Paths.PlayerName + ".exe");
                System.IO.File.WriteAllText(exe, "");
                System.IO.File.WriteAllText(System.IO.Path.Combine(deep, "Extra", Paths.PlayerName + ".exe"), "");
                Assert.That(Paths.GivenPlayer(dir), Is.EqualTo(exe), "the unpacked zip: the shallowest player in it");
                Assert.That(Paths.GivenPlayer(exe), Is.EqualTo(exe), "an exe as given");
                string empty = System.IO.Path.Combine(dir, "empty");
                System.IO.Directory.CreateDirectory(empty);
                Assert.That(Paths.GivenPlayer(empty), Is.EqualTo(System.IO.Path.Combine(empty, Paths.PlayerName + ".exe")), "none there: run says it is missing");
            }
            finally
            {
                System.IO.Directory.Delete(dir, true);
            }
        }

        static RunReport Run(string ended) => new RunReport { Ended = ended, Frames = 900, Goals = { "Visit:tower" } };

        [Test]
        public void ARunsEndingBecomesACandidate()
        {
            Assert.That(Report.Ending(Run("done"), false, null, 0, 100), Is.Null, "a clean finish");
            Assert.That(Report.Ending(Run("done"), false, "APPCRASH in D3D12Core.dll", 0, 100).Signature, Is.EqualTo("crash:quit"));
            Assert.That(Report.Ending(Run("no report"), false, null, -1073741819, 30).Signature, Is.EqualTo("crash:running"));
            var hang = Report.Ending(Run("no report"), true, null, -1, 400);
            Assert.That((hang.Signature, hang.Category, hang.Frame, hang.Goal), Is.EqualTo(("hang:running", Category.GameBreaking, 900, "Visit:tower")));
        }

        [Test]
        public void ACrashEventCountsOnlyForThePlayerTheRunnerStarted()
        {
            const string message = "Faulting application name: SpellChemistryValley.exe, version: 6000.3.27.0\r\n" +
                                   "Faulting module name: D3D12Core.dll, version: 1.618.1.0, time stamp: 0x68d311e6\r\n" +
                                   "Faulting process id: 0xE088\r\n";
            Assert.That(Crashes.IsAbout(message, "SpellChemistryValley.exe", 0xE088), Is.True);
            Assert.That(Crashes.IsAbout(message, "SpellChemistryValley.exe", 0x1234), Is.False, "another session's test player");
            Assert.That(Crashes.Module(message), Is.EqualTo("D3D12Core.dll"));
        }

        [Test]
        public void OptionsAreReadAfterTheCommand()
        {
            var o = Program.Options(new[] { "run", "--seed", "1234", "--force", "--seconds", "90" });
            Assert.That(o, Is.EqualTo(new Dictionary<string, string> { ["seed"] = "1234", ["force"] = "true", ["seconds"] = "90" }));
            Assert.That(Program.IntOption(o, "seconds", 480), Is.EqualTo(90));
            Assert.That(Program.IntOption(o, "until-frame", 7), Is.EqualTo(7));
        }

        static RunReport Synced(params string[] syncs)
        {
            var r = new RunReport { Seed = 5, Frames = syncs.Length * 30 };
            r.Trace.Add(new TraceStep { Frame = 1, Action = "goal", Detail = "go to the tower" });
            for (int i = 0; i < syncs.Length; i++) r.Trace.Add(new TraceStep { Frame = (i + 1) * 30, Action = ReplayCheck.Sync, Detail = syncs[i] });
            return r;
        }

        [Test]
        public void CompareSaysWhereTwoRunsParted()
        {
            var a = Synced("tick 3 at 1.00,0.00,1.00", "tick 6 at 2.00,0.00,2.00", "tick 9 at 3.00,0.00,3.00");
            var (same, text) = Compare.Describe(a, Synced("tick 3 at 1.00,0.00,1.00", "tick 6 at 2.00,0.00,2.00", "tick 9 at 3.00,0.00,3.00"));
            Assert.That(same, Is.True);
            Assert.That(text, Does.Contain("all 90 frames").And.Contain("3 sync steps"));

            (same, text) = Compare.Describe(a, Synced("tick 3 at 1.00,0.00,1.00", "tick 7 at 2.00,0.00,2.00", "tick 9 at 3.00,0.00,3.00"));
            Assert.That(same, Is.False);
            Assert.That(text, Does.StartWith("parted at frame 60 (2.0 s)").And.Contain("tick 7").And.Contain("while: go to the tower"));

            var other = Synced("tick 3 at 1.00,0.00,1.00");
            other.Seed = 6;
            Assert.That(Compare.Describe(a, other).same, Is.False);
        }

        [Test]
        public void APcWithNoAudioOutputRunsAsMuted()
        {
            // Windows answers "Element not found" for the default output on a PC or VM with no audio device.
            Assert.That(AppMute.Silenced(AppMute.NoOutput), Is.True, "nothing can play aloud");
            Assert.That(AppMute.Silenced(0), Is.False, "no sound session yet: keep trying");
            Assert.That(AppMute.Silenced(2), Is.True);
        }
    }
}
