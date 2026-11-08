using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Chem.Mcp;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>The API loop behind the chat's seam, on a wire with nobody at the other end: what runs, what it costs, how it stops.</summary>
    public class ApiLoopTests
    {
        const string Model = "claude-haiku-5-5", Url = "http://127.0.0.1:1/mcp";

        sealed class Wire : IChatWire
        {
            public readonly Queue<(WireAnswer answer, string[] events)> Replies = new Queue<(WireAnswer, string[])>();
            public readonly List<string> Called = new List<string>();
            public readonly List<string> Bodies = new List<string>();
            public JArray Tools = new JArray(ServerTool("whats_on"), ServerTool("apply"));

            public string Mcp(string url, string body, CancellationToken token)
            {
                var o = JObject.Parse(body);
                var result = new JObject();
                switch ((string)o["method"])
                {
                    case "initialize":
                        result["instructions"] = "How to go about it.";
                        break;
                    case "tools/list":
                        result["tools"] = Tools;
                        break;
                    case "tools/call":
                        Called.Add((string)o["params"]["name"]);
                        result["content"] = new JArray(new JObject { ["type"] = "text", ["text"] = "ok" });
                        result["isError"] = false;
                        break;
                }
                return new JObject { ["jsonrpc"] = "2.0", ["id"] = o["id"], ["result"] = result }.ToString();
            }

            public WireAnswer Messages(string body, Action<string> data, CancellationToken token)
            {
                Bodies.Add(body);
                var (answer, events) = Replies.Dequeue();
                foreach (string e in events) data(e);
                return answer;
            }

            public void Reply(params string[] events) => Replies.Enqueue((new WireAnswer { Status = 200 }, events));

            public void Fails(int status, double? retryAfter = null) => Replies.Enqueue((new WireAnswer { Status = status, RetryAfterSeconds = retryAfter, Failure = $"status {status}" }, Array.Empty<string>()));
        }

        static JObject ServerTool(string name) => new JObject
        {
            ["name"] = name,
            ["description"] = name + " says",
            ["inputSchema"] = new JObject { ["type"] = "object", ["properties"] = new JObject() },
            ["annotations"] = new JObject { ["readOnlyHint"] = true },
        };

        static string Start(string id = "m1") =>
            "{\"type\":\"message_start\",\"message\":{\"id\":\"" + id + "\",\"usage\":{\"input_tokens\":100,\"cache_creation_input_tokens\":1000,\"cache_read_input_tokens\":10000,\"output_tokens\":1,\"cache_creation\":{\"ephemeral_5m_input_tokens\":1000,\"ephemeral_1h_input_tokens\":0}}}}";

        static string Tool(int index, string name, string id, string json = "{}") =>
            "{\"type\":\"content_block_start\",\"index\":" + index + ",\"content_block\":{\"type\":\"tool_use\",\"id\":\"" + id + "\",\"name\":\"" + name + "\"}}\n" +
            "{\"type\":\"content_block_delta\",\"index\":" + index + ",\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":" + new JValue(json).ToString(Newtonsoft.Json.Formatting.None) + "}}";

        static string Words(int index, string text) =>
            "{\"type\":\"content_block_start\",\"index\":" + index + ",\"content_block\":{\"type\":\"text\",\"text\":\"\"}}\n" +
            "{\"type\":\"content_block_delta\",\"index\":" + index + ",\"delta\":{\"type\":\"text_delta\",\"text\":\"" + text + "\"}}";

        static string Stop(string reason, string extra = "") =>
            "{\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"" + reason + "\"" + extra + "},\"usage\":{\"output_tokens\":200}}\n{\"type\":\"message_stop\"}";

        static string[] Events(params string[] parts) => string.Join("\n", parts).Split('\n');

        string folder, budgetFile;

        [SetUp]
        public void Fresh()
        {
            folder = Path.Combine(Path.GetTempPath(), "api-loop-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            budgetFile = Path.Combine(folder, "budget.json");
        }

        [TearDown]
        public void Gone() => Directory.Delete(folder, true);

        (List<JObject> lines, ChatRecorder recorder) Run(Wire wire, string message = "make it rain", double stopAt = ApiBudget.StopAt, CancellationToken stop = default)
        {
            long now = 0;
            var lines = new List<JObject>();
            var recorder = new ChatRecorder(message, "typed", 0);
            void Say(string line)
            {
                lines.Add(JObject.Parse(line));
                recorder.Line(line, ++now);
            }
            new ApiLoop(wire, new ApiBudget(budgetFile, stopAt), Model, Say, () => now) { RetryBaseMs = 1 }.Run(message, folder, Url, stop);
            recorder.Ended(++now);
            return (lines, recorder);
        }

        static JObject Result(List<JObject> lines) => lines.Single(l => (string)l["type"] == "result");

        [Test]
        public void TheListIsEveryToolTheServerHasToday()
        {
            var server = (JArray)JObject.Parse(new McpRpc(null, "n", "1").Handle("{\"id\":1,\"method\":\"tools/list\"}"))["result"]["tools"];
            Assert.That(ApiLoop.Allowed, Is.EquivalentTo(server.Select(t => (string)t["name"])), "a new tool is read and named here before the loop offers it");
        }

        [Test]
        public void ToolsGoToTheApiUnchangedAndInOrder()
        {
            var server = new JArray(ServerTool("whats_on"), ServerTool("apply"), ServerTool("a_new_tool_nobody_read"));
            var offered = ApiLoop.Offered(server);
            Assert.That(offered.Select(t => (string)t["name"]), Is.EqualTo(new[] { ApiLoop.NoteTool, "apply", "whats_on" }));
            var apply = (JObject)offered[1];
            Assert.That((string)apply["description"], Is.EqualTo("apply says"));
            Assert.That(JToken.DeepEquals(apply["input_schema"], server[1]["inputSchema"]), Is.True);
            Assert.That(apply["annotations"], Is.Null);
        }

        [Test]
        public void AToolOutsideTheListCantRun()
        {
            var wire = new Wire();
            wire.Reply(Events(Start(), Tool(0, "Bash", "t1", "{\"command\":\"del /s *\"}"), Tool(1, "Read", "t2"), Tool(2, "whats_on", "t3"), Stop("tool_use")));
            wire.Reply(Events(Start("m2"), Words(0, "No."), Stop("end_turn")));
            var (lines, recorder) = Run(wire);
            Assert.That(wire.Called, Is.EqualTo(new[] { "whats_on" }), "only what's listed reaches the server");
            var results = (JArray)JObject.Parse(wire.Bodies[1])["messages"].Last["content"];
            Assert.That((bool?)results[0]["is_error"], Is.True);
            StringAssert.Contains("no tool Bash", (string)results[0]["content"]);
            Assert.That((bool?)results[1]["is_error"], Is.True);
            Assert.That(recorder.Record()["tools"].Select(t => (bool?)t["ok"]), Is.EqualTo(new bool?[] { false, false, true }));
            Assert.That(lines.Count(l => (string)l["type"] == "user"), Is.EqualTo(3));
        }

        [Test]
        public void NotesComeWithTheMessageAndOnlyAppend()
        {
            string notes = Path.Combine(folder, ClaudeRun.NotesFile), elsewhere = Path.Combine(folder, "other.txt");
            File.WriteAllText(notes, "- Blue fire is on.\n");
            var wire = new Wire();
            wire.Reply(Events(Start(), Tool(0, ApiLoop.NoteTool, "t1", "{\"note\":\"Green\\nnow.\",\"path\":\"" + elsewhere.Replace("\\", "\\\\") + "\"}"), Stop("tool_use")));
            wire.Reply(Events(Start("m2"), Words(0, "Done."), Stop("end_turn")));
            Run(wire);
            var first = (JArray)JObject.Parse(wire.Bodies[0])["messages"][0]["content"];
            Assert.That(first.Count, Is.EqualTo(1), "one block: two side by side reach the model as one run of text");
            Assert.That((string)first[0]["text"], Is.EqualTo(ClaudeRun.Message("- Blue fire is on.", "make it rain")), "built as Claude Code's is");
            Assert.That(File.ReadAllText(notes), Is.EqualTo("- Blue fire is on.\n- Green now.\n"), "one line, on the end");
            Assert.That(File.Exists(elsewhere), Is.False, "the tool has no path to take");
            Assert.That(wire.Called, Is.Empty, "and it isn't a server tool");
        }

        [Test]
        public void ANoteStaysShort()
        {
            var wire = new Wire();
            wire.Reply(Events(Start(), Tool(0, ApiLoop.NoteTool, "t1", new JObject { ["note"] = new string('x', 5000) }.ToString()), Stop("tool_use")));
            wire.Reply(Events(Start("m2"), Words(0, "Done."), Stop("end_turn")));
            Run(wire);
            Assert.That(File.ReadAllText(Path.Combine(folder, ClaudeRun.NotesFile)), Is.EqualTo("- " + new string('x', ApiLoop.MostNoteChars) + "…\n"));
        }

        [Test]
        public void ANoteCutShortEndsAtAWordAndSaysSo()
        {
            string note = string.Concat(Enumerable.Repeat("Reverted the night sky. ", 12)) + "Say undo to bring it back.";
            var wire = new Wire();
            wire.Reply(Events(Start(), Tool(0, ApiLoop.NoteTool, "t1", new JObject { ["note"] = note }.ToString()), Stop("tool_use")));
            wire.Reply(Events(Start("m2"), Words(0, "Done."), Stop("end_turn")));
            Run(wire);
            string kept = File.ReadAllText(Path.Combine(folder, ClaudeRun.NotesFile)).TrimEnd('\n');
            Assert.That(kept, Does.EndWith("…"), "it says it was cut");
            string body = kept.Substring(2).TrimEnd('…');
            Assert.That(note, Does.StartWith(body));
            Assert.That(note[body.Length], Is.EqualTo(' '), "and the cut falls between words");
        }

        // Playtest 2026-11-08: after an undo, a note cut mid-word ran into "Make it daytime." and Claude read "UnMake it daytime" as a note.
        [Test]
        public void TheNewRequestArrivesWholeAfterAnUndo()
        {
            File.WriteAllText(Path.Combine(folder, ClaudeRun.NotesFile), "- Night sky mod on.\n- Undid the night sky mod. Say Un\n");
            var wire = new Wire();
            wire.Reply(Events(Start(), Words(0, "It's day."), Stop("end_turn")));
            Run(wire, "Make it daytime.");
            var first = (JArray)JObject.Parse(wire.Bodies[0])["messages"][0]["content"];
            var lines = ((string)first.Last["text"]).Split('\n');
            Assert.That(lines[^1], Is.EqualTo("Make it daytime."), "whole, and on a line of its own");
            Assert.That(lines[^2], Is.Empty);
            Assert.That(lines[^3], Is.EqualTo(ClaudeRun.NotesClose), "marked as what the player says now");
            Assert.That(lines[^4], Is.EqualTo("> - Undid the night sky mod. Say Un"), "the last note stays a quoted note");
        }

        [Test]
        public void ABudgetSpentStartsNothing()
        {
            new ApiBudget(budgetFile).Add(ApiBudget.StopAt);
            var wire = new Wire();
            var (lines, recorder) = Run(wire);
            Assert.That(wire.Bodies, Is.Empty, "no request");
            Assert.That((bool)Result(lines)["is_error"], Is.True);
            Assert.That((string)recorder.Record()["outcome"], Is.EqualTo("failed"));
        }

        [Test]
        public void ABudgetStopsPartWay()
        {
            var wire = new Wire();
            wire.Reply(Events(Start(), Tool(0, "whats_on", "t1"), Stop("tool_use")));
            var (lines, _) = Run(wire, stopAt: 0.0001);
            Assert.That(wire.Bodies.Count, Is.EqualTo(1), "the second turn didn't go");
            Assert.That((string)Result(lines)["terminal_reason"], Is.EqualTo("budget"));
        }

        [Test]
        public void ABudgetKeepsItsTotalAcrossRunsAndNoFileMeansNo()
        {
            new ApiBudget(budgetFile).Add(1.5);
            new ApiBudget(budgetFile).Add(2);
            Assert.That(new ApiBudget(budgetFile).Spent, Is.EqualTo(3.5).Within(1e-9));
            Assert.That(new ApiBudget(null).HasRoom, Is.False);
            File.WriteAllText(budgetFile, "not json");
            Assert.That(new ApiBudget(budgetFile).HasRoom, Is.False, "a total nobody can read isn't room");
        }

        [Test]
        public void AModelWithNoPriceIsRefused()
        {
            var wire = new Wire();
            var lines = new List<string>();
            new ApiLoop(wire, new ApiBudget(budgetFile), "claude-sonnet-5-5", lines.Add, () => 0).Run("hi", folder, Url, default);
            Assert.That(wire.Bodies, Is.Empty);
            StringAssert.Contains("no prices", lines.Single());
        }

        [Test]
        public void CostComesFromUsageAtTheApisPrices()
        {
            var usage = JObject.Parse("{\"input_tokens\":1000000,\"cache_creation_input_tokens\":3000000,\"cache_read_input_tokens\":1000000,\"output_tokens\":1000000,\"cache_creation\":{\"ephemeral_5m_input_tokens\":1000000,\"ephemeral_1h_input_tokens\":1000000}}");
            Assert.That(ApiPrice.Of("claude-opus-5-5").Cost(usage), Is.EqualTo(4 + 5 + 8 + 8 + 0.2 + 20).Within(1e-9), "the write that says neither counts as the dearer");
            Assert.That(ApiPrice.Of(Model).Cost(JObject.Parse("{\"input_tokens\":100000,\"output_tokens\":100000}")), Is.EqualTo(0.06).Within(1e-9));
            Assert.That(ApiPrice.Of(Model).Cost(JObject.Parse("{\"input_tokens\":200000}")), Is.EqualTo(0.1).Within(1e-9), "past 100K it's five times the rate");
        }

        [Test]
        public void TheRecordReadsAsClaudeCodesDoes()
        {
            var wire = new Wire();
            wire.Reply(Events(Start(), Tool(0, "whats_on", "t1"), Stop("tool_use")));
            wire.Reply(Events(Start("m2"), Words(0, "It rains."), Stop("end_turn")));
            var (lines, recorder) = Run(wire);
            var r = recorder.Record();
            Assert.That((string)r["model"], Is.EqualTo(Model));
            Assert.That((string)r["harness"], Is.EqualTo("api"));
            Assert.That((string)r["reply"], Is.EqualTo("It rains."));
            Assert.That((string)r["outcome"], Is.EqualTo("answered"));
            Assert.That((string)r["tokens"]["costBasis"], Is.EqualTo(ApiLoop.RealMoney), "real money, labelled");
            Assert.That((long)r["tokens"]["output"], Is.EqualTo(400));
            Assert.That((long)r["tokens"]["cacheRead"], Is.EqualTo(20000));
            Assert.That((int)r["tokens"]["turns"], Is.EqualTo(2));
            Assert.That(r["at"]["firstWord"].Type, Is.EqualTo(JTokenType.Integer));
            Assert.That((double)r["tokens"]["cost"], Is.EqualTo(new ApiBudget(budgetFile).Spent).Within(1e-12), "what the record says is what the budget counted");
            var game = lines.First(l => (string)l["type"] == "assistant")["message"]["content"][0];
            Assert.That((string)game["name"], Is.EqualTo("mcp__spell-valley__whats_on"), "the name chat-strict reads");
        }

        [Test]
        public void TheCacheMarksAndTheirBytesHold()
        {
            var wire = new Wire();
            wire.Reply(Events(Start(), Tool(0, "whats_on", "t1"), Stop("tool_use")));
            wire.Reply(Events(Start("m2"), Words(0, "Done."), Stop("end_turn")));
            Run(wire);
            var one = JObject.Parse(wire.Bodies[0]);
            var two = JObject.Parse(wire.Bodies[1]);
            Assert.That((string)one["system"].Last["cache_control"]["ttl"], Is.EqualTo("1h"), "the prefix waits out a player's gap");
            Assert.That((string)one["system"].Last["text"], Is.EqualTo("How to go about it."), "Instructions as the server has them");
            Assert.That(one["system"].First["cache_control"], Is.Null);
            Assert.That(two["messages"].Last["content"].Last["cache_control"], Is.Not.Null, "the tail moves");
            Assert.That(two["messages"].First["content"].Last["cache_control"], Is.Null, "and leaves no mark behind");
            Assert.That(one["system"].ToString() + one["tools"], Is.EqualTo(two["system"].ToString() + two["tools"]), "the prefix doesn't move between turns");
            Assert.That(wire.Bodies[0], Does.Not.Contain("temperature"));
        }

        [TestCase("max_tokens")]
        [TestCase("refusal")]
        public void ACutOffReplysToolsNeverRun(string reason)
        {
            var wire = new Wire();
            wire.Reply(Events(Start(), Tool(0, "apply", "t1"), Stop(reason)));
            var (lines, recorder) = Run(wire);
            Assert.That(wire.Called, Is.Empty);
            Assert.That((string)Result(lines)["terminal_reason"], Is.EqualTo(reason));
            Assert.That((string)recorder.Record()["outcome"], Is.EqualTo("failed"));
        }

        [Test]
        public void ARefusalSaysItsCategory()
        {
            var wire = new Wire();
            wire.Reply(Events(Start(), Stop("refusal", ",\"stop_details\":{\"type\":\"refusal\",\"category\":\"cyber\"}")));
            StringAssert.Contains("cyber", (string)Result(Run(wire).lines)["result"]);
        }

        [Test]
        public void TheApisNullsAreNoFault()
        {
            var wire = new Wire();
            wire.Reply(Events(Start(), Words(0, "Fine."), "{\"type\":\"ping\"}", Stop("end_turn", ",\"stop_details\":null,\"stop_sequence\":null")));
            Assert.That((string)Result(Run(wire).lines)["result"], Is.EqualTo("Fine."));
            Assert.That(ApiPrice.Of(Model).Cost(JObject.Parse("{\"output_tokens\":1000,\"cache_creation\":null}")), Is.EqualTo(0.0005).Within(1e-9));
        }

        [TestCase(429)]
        [TestCase(529)]
        [TestCase(500)]
        [TestCase(0)]
        public void WhatPassesIsTriedAgain(int status)
        {
            var wire = new Wire();
            wire.Fails(status, 0);
            wire.Reply(Events(Start(), Words(0, "Fine."), Stop("end_turn")));
            Assert.That((string)Result(Run(wire).lines)["result"], Is.EqualTo("Fine."));
            Assert.That(wire.Bodies.Count, Is.EqualTo(2));
            Assert.That(wire.Bodies[0], Is.EqualTo(wire.Bodies[1]));
        }

        [Test]
        public void AnOverloadMidStreamIsTriedAgainAndItsTokensCount()
        {
            var wire = new Wire();
            wire.Reply(Start(), "{\"type\":\"error\",\"error\":{\"type\":\"overloaded_error\",\"message\":\"Overloaded\"}}");
            wire.Reply(Events(Start(), Words(0, "Fine."), Stop("end_turn")));
            var (lines, _) = Run(wire);
            Assert.That((string)Result(lines)["result"], Is.EqualTo("Fine."));
            Assert.That((long)Result(lines)["usage"]["cache_read_input_tokens"], Is.EqualTo(20000));
        }

        [Test]
        public void ItGivesUpAfterItsTries()
        {
            var wire = new Wire();
            for (int i = 0; i < ApiLoop.MostTries; i++) wire.Fails(529, 0);
            var (lines, recorder) = Run(wire);
            Assert.That(wire.Bodies.Count, Is.EqualTo(ApiLoop.MostTries));
            StringAssert.Contains("529", (string)Result(lines)["result"]);
            Assert.That((string)recorder.Record()["outcome"], Is.EqualTo("failed"));
        }

        [TestCase(400)]
        [TestCase(401)]
        [TestCase(404)]
        public void WhatWontPassIsntTriedAgain(int status)
        {
            var wire = new Wire();
            wire.Fails(status);
            Run(wire);
            Assert.That(wire.Bodies.Count, Is.EqualTo(1));
        }

        [Test]
        public void TurnsAreCapped()
        {
            var wire = new Wire();
            for (int i = 0; i < ApiLoop.MostTurns + 2; i++) wire.Reply(Events(Start("m" + i), Tool(0, "whats_on", "t" + i), Stop("tool_use")));
            var (lines, _) = Run(wire);
            Assert.That(wire.Bodies.Count, Is.EqualTo(ApiLoop.MostTurns));
            Assert.That((string)Result(lines)["terminal_reason"], Is.EqualTo("turns"));
        }

        [Test]
        public void ACancelEndsItWithNoResult()
        {
            using var stop = new CancellationTokenSource();
            var wire = new Wire();
            wire.Reply(Events(Start(), Tool(0, "apply", "t1"), Stop("tool_use")));
            stop.Cancel();
            var (lines, recorder) = Run(wire, stop: stop.Token);
            Assert.That(wire.Called, Is.Empty);
            Assert.That(lines.Any(l => (string)l["type"] == "result"), Is.False);
            Assert.That((string)recorder.Record()["outcome"], Is.EqualTo("failed"), "the view says stopped or timed out");
        }

        [Test]
        public void TheGamesKeyStaysOutOfClaudesProcess()
        {
            var environment = new Dictionary<string, string> { [ClaudeRun.GameKey] = "secret", ["PATH"] = "here", ["ANTHROPIC_API_KEY"] = "the player's own" };
            ClaudeRun.Scrub(environment);
            Assert.That(environment.Keys, Is.EquivalentTo(new[] { "PATH", "ANTHROPIC_API_KEY" }), "a player's own setting is theirs: the Claude path stays as it was");
        }

        [Test]
        public void AKeyIsCutFromAnyText()
        {
            Assert.That(ClaudeRun.Without("bad key abc123 here", "abc123"), Is.EqualTo("bad key [key] here"));
            Assert.That(ClaudeRun.Without("nothing", ""), Is.EqualTo("nothing"));
            Assert.That(ClaudeRun.Without(null, "abc123"), Is.Null);
        }

        [Test]
        public void TheChatsAskToSignInStaysOutOfTheLoopsWords() =>
            Assert.That(ClaudeRun.NeedsSignIn(ApiLoop.Prompt), Is.False);
    }
}
