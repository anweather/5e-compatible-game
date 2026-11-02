using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Chem.Mcp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// One prompt's record (docs/design/chat-telemetry.md). The fixtures are real stream-json from claude -p with
    /// the private fields taken out: blue (an apply), refuse (nothing it can do), denied (a tool denied, an apply
    /// refused, then one that went in), badmodel (an API failure). Each line "arrives" 100 ms after the last.
    /// </summary>
    public class ChatRecordTests
    {
        const int Step = 100;

        static string Fixtures => Path.Combine(Path.GetDirectoryName(ChemistryTests.RulesDir().TrimEnd('/', '\\')), "core", "Game.Core.Tests", "RulesMcp", "chat-fixtures");

        static string[] Lines(string name) => File.ReadAllLines(Path.Combine(Fixtures, name + ".jsonl"));

        static ChatRecorder Fed(string[] lines)
        {
            var recorder = new ChatRecorder("a prompt", "typed", 1000, new ChatWho { Id = "r1", GameVersion = "0.1.2", Commit = "abc", RoomMode = "open", Players = 2, ModsOn = new[] { "bigger" } });
            recorder.Started(1000 + Step);
            for (int i = 0; i < lines.Length; i++) recorder.Line(lines[i], 1000 + Step * (i + 2));
            return recorder;
        }

        static JObject Record(string name) => Fed(Lines(name)).Record();

        [Test]
        public void APlayersRecordsGoOutsideWhatTheyAreToldToSend()
        {
            // On Windows the data folder is the one Player.log is in; on a Mac they're apart.
            string data = Path.Combine("c", "LocalLow", "Spell Chemistry Valley");
            string home = ChatLog.Home(data, null);
            Assert.That(home, Is.EqualTo(Path.Combine(data, ChatLog.RecordsFolder)));
            Assert.That(Path.GetDirectoryName(home), Is.EqualTo(data), "under the data folder, so it stays the player's");
            Assert.That(home, Is.Not.EqualTo(data), "not the folder Player.log is in");
            string root = Path.GetDirectoryName(ChemistryTests.RulesDir().TrimEnd('/', '\\'));
            foreach (string readme in new[] { Path.Combine(root, "unity", "ReleaseReadme.txt"), Path.Combine(root, "tools", "README-LINUX.txt") })
            {
                var told = File.ReadAllLines(readme).Where(l => l.Contains("Spell Chemistry Valley" + "\\", StringComparison.Ordinal) || l.Contains("Spell Chemistry Valley/", StringComparison.Ordinal)).ToList();
                Assert.That(told, Is.Not.Empty, readme);
                Assert.That(told, Has.All.EndsWith("Player.log"), "players are told one file by name: telling them a folder would send their prompts");
            }
        }

        [Test]
        public void ATestsRecordsStayBesideItsLog() =>
            Assert.That(ChatLog.Home("data", "scratch"), Is.EqualTo("scratch"), "an Editor shares its data folder with the player's game");

        [Test]
        public void AModelThatsRefusedIsAFailedRecordThatNamesIt()
        {
            const string asked = "claude-haiku-5-5";
            var before = Record("badmodel");
            var recorder = Fed(Lines("badmodel"));
            recorder.ModelUnavailable(asked);
            recorder.Ended(9000);
            var r = recorder.Record();
            Assert.That((string)r["outcome"], Is.EqualTo("failed"));
            Assert.That((string)r["why"], Is.EqualTo("model unavailable: " + asked));
            Assert.That((string)r["model"], Is.EqualTo(asked), "the one asked for: nothing else ran");
            Assert.That((string)r["reply"], Is.EqualTo((string)before["reply"]), "claude's own words are still there to read");
            Assert.That(r["tokens"].ToString(), Is.EqualTo(before["tokens"].ToString()));
        }

        [Test]
        public void ARecordWithNoModelFailureSaysNothingOfOne() =>
            Assert.That(Record("blue").ToString(), Does.Not.Contain("unavailable"));

        static int LineOf(string[] lines, string has) => Array.FindIndex(lines, l => l.Contains(has, StringComparison.Ordinal));

        [Test]
        public void TheTimesAreFromTheSendAndTheFirstOfEach()
        {
            var lines = Lines("blue");
            var at = Record("blue")["at"];
            Assert.That((long)at["processStarted"], Is.EqualTo(Step));
            Assert.That((long)at["claudeReady"], Is.EqualTo(2 * Step), "the init line");
            Assert.That((long)at["firstTool"], Is.EqualTo((LineOf(lines, "\"type\":\"tool_use\"") + 2) * Step));
            Assert.That((long)at["firstApply"], Is.EqualTo((LineOf(lines, "\"name\":\"mcp__spell-valley__apply\"") + 2) * Step));
            Assert.That((long)at["applied"], Is.EqualTo((LineOf(lines, "host switched on") + 2) * Step), "the room's answer");
            Assert.That((long)at["firstWord"], Is.EqualTo((LineOf(lines, "\"type\":\"text\",\"text\":\"All fire") + 2) * Step));
            Assert.That((long)at["done"], Is.EqualTo((lines.Length + 1) * Step));
            Assert.That(at["inWorld"].Type, Is.EqualTo(JTokenType.Null), "only the game knows that");
        }

        [Test]
        public void TokensAndCostComeFromTheResultNotAddedUpFromItsLines()
        {
            var r = Record("blue");
            var tokens = r["tokens"];
            Assert.That((long)tokens["input"], Is.EqualTo(8));
            Assert.That((long)tokens["cacheWrite"], Is.EqualTo(18798));
            Assert.That((long)tokens["cacheRead"], Is.EqualTo(53110));
            Assert.That((long)tokens["output"], Is.EqualTo(764));
            Assert.That((double)tokens["cost"], Is.EqualTo(0.0046735).Within(1e-9));
            Assert.That((int)tokens["turns"], Is.EqualTo(6));
            Assert.That((long)tokens["perModel"]["claude-haiku-5-5"]["output"], Is.EqualTo(764));
            Assert.That((string)r["model"], Is.EqualTo("claude-haiku-5-5"));
            Assert.That((string)r["claude"], Is.EqualTo("2.1.312"));
            Assert.That((bool)tokens["cutOff"], Is.False);
        }

        [Test]
        public void TheSplitSaysWhatsLeftOver()
        {
            var split = Record("blue")["split"];
            Assert.That((long)split["model"], Is.EqualTo(8250), "duration_api_ms");
            Assert.That((long)split["claudeTotal"], Is.EqualTo(8511), "duration_ms");
            Assert.That((long)split["leftOver"], Is.EqualTo((long)split["total"] - (long)split["startingUp"] - (long)split["model"] - (long)split["tools"]));
        }

        [Test]
        public void ToolsThatOverlapCountOnce()
        {
            string Use(string id) => $"{{\"type\":\"assistant\",\"message\":{{\"id\":\"m\",\"content\":[{{\"type\":\"tool_use\",\"id\":\"{id}\",\"name\":\"mcp__spell-valley__list\",\"input\":{{}}}}]}}}}";
            string Back(string id) => $"{{\"type\":\"user\",\"message\":{{\"content\":[{{\"type\":\"tool_result\",\"tool_use_id\":\"{id}\",\"content\":\"ok\"}}]}}}}";
            var recorder = new ChatRecorder("p", "typed", 0);
            recorder.Line(Use("a"), 100);
            recorder.Line(Use("b"), 110);
            recorder.Line(Back("a"), 300);
            recorder.Line(Back("b"), 400);
            var r = recorder.Record();
            Assert.That((long)r["split"]["tools"], Is.EqualTo(300));
            Assert.That(r["tools"].Select(t => (long)t["ms"]), Is.EqualTo(new long[] { 200, 290 }));
        }

        [Test]
        public void EachToolCallSaysOkOrRefusedAndWhy()
        {
            var r = Record("denied");
            var tools = r["tools"].ToList();
            Assert.That(tools.Select(t => (string)t["name"]), Is.EqualTo(new[] { "Read", "apply", "set_look", "set_look", "apply" }), "short names, in order");
            Assert.That(tools.Select(t => (bool)t["ok"]), Is.EqualTo(new[] { false, false, false, true, true }));
            Assert.That((string)tools[0]["reason"], Does.StartWith("Permission to use Read has been denied"));
            Assert.That((string)tools[1]["reason"], Is.EqualTo("nonsense is neither a draft, a mod in rules/mods nor a patch."), "the SDK's wrapper taken off");
            Assert.That((bool)tools[0]["game"], Is.False);
            Assert.That((bool)tools[1]["game"], Is.True);
        }

        [Test]
        public void ARoomsRefusalIsPlainTextButStillARefusal()
        {
            var lines = Lines("blue");
            int answer = LineOf(lines, "host switched on");
            var o = JObject.Parse(lines[answer]);
            o["message"]["content"][0]["content"] = "Switch on refused: only the host can change this room.\nLocked room.";
            lines[answer] = o.ToString(Formatting.None);
            var r = Fed(lines).Record();
            Assert.That((string)r["outcome"], Is.EqualTo("refused"));
            Assert.That((string)r["why"], Is.EqualTo("Switch on refused: only the host can change this room."));
            Assert.That((bool)r["changes"][0]["ok"], Is.False);
            Assert.That(r["at"]["applied"].Type, Is.EqualTo(JTokenType.Null));
        }

        [TestCase("blue", "applied")]
        [TestCase("denied", "applied")]
        [TestCase("refuse", "refused")]
        [TestCase("badmodel", "failed")]
        public void TheOutcomeOfARun(string name, string outcome) => Assert.That((string)Record(name)["outcome"], Is.EqualTo(outcome));

        [Test]
        public void ARefusalSaysWhatItCouldntDo() => Assert.That((string)Record("refuse")["why"], Does.StartWith("I can't make the player twice as tall"));

        [Test]
        public void AnAPIFailureSaysSoThoughItsSubtypeIsSuccess()
        {
            var r = Record("badmodel");
            Assert.That((string)r["why"], Does.Contain("no-such-model"));
            Assert.That((string)r["endedBy"], Is.EqualTo("api_error"));
        }

        [Test]
        public void AQuestionIsNeitherAppliedNorRefused()
        {
            var recorder = new ChatRecorder("what time is it?", "typed", 0);
            recorder.Line("{\"type\":\"assistant\",\"message\":{\"id\":\"m\",\"content\":[{\"type\":\"tool_use\",\"id\":\"a\",\"name\":\"mcp__spell-valley__time\",\"input\":{}}]}}", 1);
            recorder.Line("{\"type\":\"user\",\"message\":{\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"a\",\"content\":\"09:00\"}]}}", 2);
            recorder.Line("{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"result\":\"Nine.\"}", 3);
            Assert.That((string)recorder.Record()["outcome"], Is.EqualTo("answered"));
        }

        [TestCase(ChatOutcome.TimedOut, "timedout")]
        [TestCase(ChatOutcome.Stopped, "stopped")]
        [TestCase(null, "failed")]
        public void ACutOffRunKeepsWhatItHad(ChatOutcome? how, string outcome)
        {
            var lines = Lines("blue");
            var cut = lines.Take(LineOf(lines, "\"name\":\"mcp__spell-valley__apply\"") + 1).ToArray();
            var recorder = Fed(cut);
            recorder.Ended(99000, how);
            var r = recorder.Record();
            Assert.That((string)r["outcome"], Is.EqualTo(outcome));
            Assert.That((long)r["at"]["done"], Is.EqualTo(98000));
            Assert.That((bool)r["tokens"]["cutOff"], Is.True);
            Assert.That((long)r["tokens"]["cacheRead"], Is.EqualTo(16764 + 17939), "each reply once, though it came on two lines");
            Assert.That((int)r["tokens"]["turns"], Is.EqualTo(3));
            var last = r["tools"].Last();
            Assert.That((string)last["name"], Is.EqualTo("apply"));
            Assert.That((bool)last["ok"], Is.False, "it never answered");
            Assert.That(r["split"]["leftOver"].Type, Is.EqualTo(JTokenType.Null), "no result, so no model time to split out");
        }

        [Test]
        public void AnEndAfterTheResultChangesNothing()
        {
            var recorder = Fed(Lines("blue"));
            long done = (long)recorder.Record()["at"]["done"];
            recorder.Ended(99000, ChatOutcome.Stopped);
            var r = recorder.Record();
            Assert.That((string)r["outcome"], Is.EqualTo("applied"));
            Assert.That((long)r["at"]["done"], Is.EqualTo(done));
        }

        [Test]
        public void ThePatchComesFromTheWorkbenchByTheKeyApplyWasGiven()
        {
            Assert.That(Record("blue")["changes"].Single()["patch"].Type, Is.EqualTo(JTokenType.Null), "it isn't in the stream");
            var recorder = Fed(Lines("blue"));
            recorder.Patched("d1", "{\"elements\":{\"fire\":{\"look\":{\"color\":[0.25,0.45,1]}}}}");
            var change = recorder.Record()["changes"].Single();
            Assert.That((string)change["title"], Is.EqualTo("All fire is blue"));
            Assert.That((bool)change["ok"], Is.True);
            Assert.That((double)change["patch"]["elements"]["fire"]["look"]["color"][2], Is.EqualTo(1));
        }

        [Test]
        public void AWorkbenchSaysWhatWentToTheRoomAndNothingThatDidnt()
        {
            string mods = Path.Combine(Path.GetTempPath(), "chat-record-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(mods);
            try
            {
                var bench = new Workbench(ChemistryTests.RulesDir(), mods);
                string told = null, key = null;
                bench.Applied = (mod, patch) => (key, told) = (mod, patch);
                string draft = Regex.Match(new RulesTools(bench).SetLook("fire", "color", "[0.25, 0.45, 1]", "element"), @"\b(d\d+)\b").Groups[1].Value;
                Assert.Throws<RulesToolException>(() => bench.Apply(draft), "no title, so nothing went to the room");
                Assert.That(told, Is.Null);
                bench.Apply(draft, title: "Blue fire");
                Assert.That(key, Is.EqualTo(draft));
                Assert.That((double)JObject.Parse(told)["elements"]["fire"]["look"]["color"][2], Is.EqualTo(1));
            }
            finally
            {
                Directory.Delete(mods, true);
            }
        }

        [Test]
        public void TheRecordIsOneLineOfJsonAndTheLogKeepsItsNewestFiles()
        {
            string folder = Path.Combine(Path.GetTempPath(), "chat-log-" + Guid.NewGuid().ToString("N"));
            try
            {
                var first = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                for (int i = 0; i < ChatLog.MostFiles + 2; i++) ChatLog.Append(folder, first.AddDays(i), Record("blue"));
                ChatLog.Append(folder, first.AddDays(ChatLog.MostFiles + 1), ChatLog.Afterwards("r1", 5000, "r2"));
                var files = Directory.GetFiles(folder).Select(Path.GetFileName).OrderBy(f => f, StringComparer.Ordinal).ToList();
                Assert.That(files, Has.Count.EqualTo(ChatLog.MostFiles));
                Assert.That(files[0], Is.EqualTo("chat-log-2026-01-03.jsonl"));
                var lines = File.ReadAllLines(Path.Combine(folder, files[files.Count - 1]));
                Assert.That(lines, Has.Length.EqualTo(2));
                Assert.That((string)JObject.Parse(lines[1])["of"], Is.EqualTo("r1"));
                Assert.That((bool)JObject.Parse(lines[1])["afterwards"]["reverted"], Is.True);
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }

        // A prompt that fixes an earlier one writes that line and its own record at once: the first real follow-up lost its record that way.
        [Test]
        public void LinesWrittenAtOnceAllLand()
        {
            string folder = Path.Combine(Path.GetTempPath(), "chat-log-" + Guid.NewGuid().ToString("N"));
            const int many = 200;
            try
            {
                var day = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                System.Threading.Tasks.Parallel.For(0, many, _ => ChatLog.Append(folder, day, Record("blue")));
                Assert.That(File.ReadAllLines(Path.Combine(folder, "chat-log-2026-01-01.jsonl")), Has.Length.EqualTo(many));
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }

        // chat-report's test reads this log: the bench's five records, then one record of each stream fixture and a
        // later line. Set CHAT_FIXTURES_WRITE to write the fixture part again after changing the record.
        [Test]
        public void TheReportsLogIsWhatTheRecorderBuilds()
        {
            string log = Path.Combine(Fixtures, "log", "chat-log-2026-11-02.jsonl");
            var built = new[] { "blue", "denied", "refuse", "badmodel" }.Select(name => Fed(Lines(name)));
            var ends = built.Select((recorder, i) =>
            {
                var r = recorder.Record();
                r["id"] = "fixture-" + i;
                r["prompt"] = "fixture " + i;
                return r.ToString(Formatting.None);
            }).Append(ChatLog.Afterwards("fixture-0", 60000).ToString(Formatting.None)).ToArray();
            var kept = File.ReadAllLines(log);
            if (Environment.GetEnvironmentVariable("CHAT_FIXTURES_WRITE") != null)
            {
                File.WriteAllLines(log, kept.Take(5).Concat(ends));
                return;
            }
            Assert.That(kept.Skip(5), Is.EqualTo(ends));
        }

        [Test]
        public void TheBenchRowAndTableSayTheDesignsNumbers()
        {
            var r = Record("blue");
            string row = Chem.Bench.Program.Row(r);
            Assert.That(row, Does.Contain("applied").And.Contain("$0.0047").And.Contain("out 764").And.Contain("6 turns"));
            Assert.That(Chem.Bench.Program.Table(new[] { r, Record("blue") }), Does.Contain("a prompt, 2, applied,"));
            Assert.That(Chem.Bench.Program.Prompts, Has.Length.EqualTo(5));
        }
    }
}
