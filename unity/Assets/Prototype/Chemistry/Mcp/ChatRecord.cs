// One prompt's record (docs/design/chat-telemetry.md, #59): fed each stream-json line of one claude
// process with the clock it arrived at, it builds the JSON line chat-report reads. The view owns the
// process and the clock. Lines come on its reading thread and the game's stamps on the main thread,
// so every method takes the lock.
//
// What the lines really carry (claude 2.1.312): `result` has duration_ms, duration_api_ms, num_turns,
// total_cost_usd, usage (the whole run's tokens) and modelUsage (costUSD and tokens per model), and
// says success with is_error true on an API failure. One reply comes as several assistant lines with
// the same message id and the same usage, written before its output is counted, so adding those up
// is wrong: they're only used when a run was cut off before its result. A refused tool is a
// tool_result with is_error, or for a room's refusal plain text with " refused:" in it. apply's
// input is a draft's id, so the patch is never in the stream: Patched brings it from Workbench.Applied.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chem.Mcp
{
    /// <summary>How a prompt ended. Answered is a question: nothing to change and nothing it couldn't do.</summary>
    public enum ChatOutcome
    {
        Applied,
        Refused,
        Answered,
        Failed,
        TimedOut,
        Stopped,
    }

    /// <summary>The record's Who group that only the game knows; model and claude version come from the stream.</summary>
    public sealed class ChatWho
    {
        public string Id = "", GameVersion = "", Commit = "", RoomMode = "";
        public int Players;
        public IReadOnlyList<string> ModsOn = Array.Empty<string>();
    }

    /// <summary>How far a run has got, for a progress bar: tools, since a run's first words often come last.</summary>
    public struct ChatProgress
    {
        public bool Started, Ready, Applying, Applied, InWorld, Done;
        public int Tools, ToolsDone;
    }

    public sealed class ChatRecorder
    {
        const int MostReasonChars = 300;

        const string ToolError = "An error occurred invoking '";

        /// <summary>The tools that change the world for everyone: a success of any of these is Applied.</summary>
        static readonly HashSet<string> WorldTools = new HashSet<string> { "apply", "revert", "set_time", "set_weather" };

        sealed class Call
        {
            public string Id, Name, Mod, Title, Reason;
            public bool Game;
            public bool? Ok;
            public long From;
            public long? To;
        }

        readonly object gate = new object();
        readonly ChatWho who;
        readonly string prompt, source;
        readonly long sent;
        readonly List<Call> calls = new List<Call>();
        readonly List<(string mod, string patch)> patches = new List<(string, string)>();
        readonly Dictionary<string, (string model, JToken usage)> replies = new Dictionary<string, (string, JToken)>();

        long? started, ready, firstWord, firstTool, firstApply, applied, inWorld, done, apiMs, claudeMs;
        string model, claude, reply, lastWords, failure, endedBy, harness, costBasis;
        int? turns;
        double? cost;
        JToken usage, modelUsage;
        JObject gameCost;
        ChatOutcome? ended;
        bool sawResult, failed;

        /// <summary>Source is typed, voice or bench; sent is the clock at the send, and every time in the record is from there.</summary>
        public ChatRecorder(string prompt, string source, long sent, ChatWho who = null)
        {
            this.prompt = prompt;
            this.source = source;
            this.sent = sent;
            this.who = who ?? new ChatWho();
        }

        /// <summary>How far it has got now.</summary>
        public ChatProgress Progress()
        {
            lock (gate)
                return new ChatProgress
                {
                    Started = started != null,
                    Ready = ready != null,
                    Applying = firstApply != null,
                    Applied = applied != null,
                    InWorld = inWorld != null,
                    Done = done != null,
                    Tools = calls.Count,
                    ToolsDone = calls.Count(c => c.To != null),
                };
        }

        /// <summary>The process has started.</summary>
        public void Started(long clock)
        {
            lock (gate) started ??= clock;
        }

        /// <summary>One line of the process's stdout. Takes anything: what isn't known is left alone.</summary>
        public void Line(string line, long clock)
        {
            JObject o;
            try
            {
                o = JObject.Parse(line);
            }
            catch (JsonException)
            {
                return;
            }
            lock (gate)
                switch ((string)o["type"])
                {
                    case "system":
                        if ((string)o["subtype"] == "init") Init(o, clock);
                        break;
                    case "assistant":
                        Assistant(o, clock);
                        break;
                    case "user":
                        Results(o, clock);
                        break;
                    case "result":
                        Result(o, clock);
                        break;
                    case "first_word":
                        firstWord ??= clock; // the API loop's stream, before a reply's block has ended
                        break;
                }
        }

        /// <summary>A patch that went to the room, from Workbench.Applied, by the key apply was given. In either order with its line.</summary>
        public void Patched(string mod, string patch)
        {
            lock (gate) patches.Add((mod, patch));
        }

        /// <summary>This peer has every peer's rules switched and the view finished drawing. With several applies, the last stamp stands.</summary>
        public void InWorld(long clock)
        {
            lock (gate) inWorld = clock;
        }

        /// <summary>The frames and work the change cost this peer, from send to in the world.</summary>
        public void GameCost(double worstFrameMs, int hitches, double composeMs, double drawMs)
        {
            lock (gate)
                gameCost = new JObject
                {
                    ["worstFrameMs"] = worstFrameMs,
                    ["hitches"] = hitches,
                    ["composeMs"] = composeMs,
                    ["drawMs"] = drawMs,
                };
        }

        /// <summary>The model asked for was refused (#64): the record names it, whatever the stream called the model. The reply keeps claude's words.</summary>
        public void ModelUnavailable(string asked)
        {
            lock (gate)
            {
                model = asked;
                failure = "model unavailable: " + asked;
            }
        }

        /// <summary>The process has gone. With how, it was stopped or timed out; a result that came first still stands for how it ended.</summary>
        public void Ended(long clock, ChatOutcome? how = null)
        {
            lock (gate)
            {
                done ??= clock;
                if (!sawResult) ended = how ?? ChatOutcome.Failed;
                foreach (var call in calls.Where(c => c.To == null))
                {
                    call.To = clock;
                    call.Ok = false;
                    call.Reason = "no answer before it ended";
                }
            }
        }

        void Init(JObject o, long clock)
        {
            ready ??= clock;
            model ??= (string)o["model"];
            claude ??= (string)o["claude_code_version"];
            harness ??= (string)o["harness"];
        }

        void Assistant(JObject o, long clock)
        {
            if (!(o["message"] is JObject message)) return;
            string id = (string)message["id"];
            if (id != null && message["usage"] != null) replies[id] = ((string)message["model"], message["usage"]);
            if (!(message["content"] is JArray content)) return;
            foreach (var part in content.OfType<JObject>())
            {
                string type = (string)part["type"];
                if (type == "text" && !string.IsNullOrWhiteSpace((string)part["text"]))
                {
                    firstWord ??= clock;
                    lastWords = ((string)part["text"]).Trim();
                }
                else if (type == "tool_use") Tool(part, clock);
            }
        }

        void Tool(JObject part, long clock)
        {
            string name = (string)part["name"] ?? "";
            var call = new Call { Id = (string)part["id"], Name = ClaudeRun.ToolName(name), Game = name.StartsWith(ClaudeRun.ToolPrefix, StringComparison.Ordinal), From = clock };
            firstTool ??= clock;
            if (call.Game && call.Name == "apply")
            {
                firstApply ??= clock;
                call.Mod = (string)part["input"]?["mod"];
                call.Title = (string)part["input"]?["title"];
            }
            calls.Add(call);
        }

        void Results(JObject o, long clock)
        {
            if (!(o["message"]?["content"] is JArray content)) return;
            foreach (var part in content.OfType<JObject>().Where(p => (string)p["type"] == "tool_result"))
            {
                string id = (string)part["tool_use_id"];
                var call = calls.Find(c => c.Id == id);
                if (call == null) continue;
                call.To = clock;
                string text = Text(part["content"]);
                bool roomRefused = call.Game && WorldTools.Contains(call.Name) && text.Contains(" refused:", StringComparison.Ordinal);
                call.Ok = (bool?)part["is_error"] != true && !roomRefused;
                if (call.Ok == false) call.Reason = ReasonOf(text);
                else if (call.Game && WorldTools.Contains(call.Name)) applied ??= clock;
            }
        }

        // A tool result's content: a string, or a list of text parts.
        static string Text(JToken content) =>
            content is JArray parts ? string.Concat(parts.OfType<JObject>().Select(p => (string)p["text"])) : (string)content ?? "";

        static string ReasonOf(string text)
        {
            int prefix = text.IndexOf(ToolError, StringComparison.Ordinal);
            if (prefix >= 0)
            {
                int colon = text.IndexOf("': ", prefix, StringComparison.Ordinal);
                if (colon >= 0) text = text.Substring(colon + 3);
            }
            text = text.Trim().Split('\n')[0];
            return text.Length <= MostReasonChars ? text : text.Substring(0, MostReasonChars);
        }

        void Result(JObject o, long clock)
        {
            sawResult = true;
            done = clock;
            failed = (bool?)o["is_error"] == true || ((string)o["subtype"] ?? "success") != "success";
            reply = (string)o["result"];
            if (failed) failure = reply ?? "Claude stopped.";
            endedBy = (string)o["terminal_reason"];
            costBasis = (string)o["cost_basis"];
            claudeMs = (long?)o["duration_ms"];
            apiMs = (long?)o["duration_api_ms"];
            turns = (int?)o["num_turns"];
            cost = (double?)o["total_cost_usd"];
            usage = o["usage"];
            modelUsage = o["modelUsage"];
        }

        ChatOutcome Outcome()
        {
            if (ended != null) return ended.Value;
            if (!sawResult || failed) return ChatOutcome.Failed;
            if (calls.Any(c => c.Game && WorldTools.Contains(c.Name) && c.Ok == true)) return ChatOutcome.Applied;
            return calls.Any(c => c.Game && (c.Ok == false || c.Name == "outside_rules")) ? ChatOutcome.Refused : ChatOutcome.Answered;
        }

        // What it couldn't do, for the list that feeds the backlog: a tool's refusal if the last word was one, else its answer's first line.
        string Couldnt() =>
            calls.LastOrDefault(c => c.Game && c.Ok == false)?.Reason ?? (reply ?? lastWords ?? "").Trim().Split('\n')[0];

        long? FromSend(long? clock) => clock - sent;

        // The time tools were running: calls can overlap, so it's their union.
        long ToolsMs()
        {
            long total = 0, reached = long.MinValue;
            foreach (var call in calls.Where(c => c.To != null).OrderBy(c => c.From))
            {
                long from = Math.Max(call.From, reached), to = call.To.Value;
                if (to <= from) continue;
                total += to - from;
                reached = to;
            }
            return total;
        }

        JObject Split()
        {
            long? up = ready - sent, total = done - sent;
            long tools = ToolsMs();
            return new JObject
            {
                ["startingUp"] = up,
                ["model"] = apiMs,
                ["tools"] = tools,
                ["total"] = total,
                ["leftOver"] = total - up - apiMs - tools,
                ["claudeTotal"] = claudeMs,
            };
        }

        static JObject Tokens(long input, long cacheWrite, long cacheRead, long output) => new JObject
        {
            ["input"] = input,
            ["cacheWrite"] = cacheWrite,
            ["cacheRead"] = cacheRead,
            ["output"] = output,
        };

        static long Count(JToken from, string name) => (long?)from?[name] ?? 0;

        static JObject TokensOf(JToken u) =>
            Tokens(Count(u, "input_tokens"), Count(u, "cache_creation_input_tokens"), Count(u, "cache_read_input_tokens"), Count(u, "output_tokens"));

        JObject TokenGroup()
        {
            var perModel = new JObject();
            JObject all;
            if (sawResult)
            {
                all = TokensOf(usage);
                foreach (var m in (modelUsage as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                {
                    var one = Tokens(Count(m.Value, "inputTokens"), Count(m.Value, "cacheCreationInputTokens"), Count(m.Value, "cacheReadInputTokens"), Count(m.Value, "outputTokens"));
                    one["cost"] = (double?)m.Value["costUSD"];
                    perModel[m.Name] = one;
                }
            }
            else
            {
                // Cut off: the most that can be said, a floor on the output as each reply's count came before it was written.
                all = Tokens(0, 0, 0, 0);
                foreach (var (name, counted) in replies.Values)
                {
                    var one = TokensOf(counted);
                    foreach (var p in one.Properties()) all[p.Name] = (long)all[p.Name] + (long)p.Value;
                    var key = name ?? "";
                    if (perModel[key] is JObject seen) foreach (var p in one.Properties()) seen[p.Name] = (long)seen[p.Name] + (long)p.Value;
                    else perModel[key] = one;
                }
            }
            all["cost"] = cost;
            if (costBasis != null) all["costBasis"] = costBasis; // the API loop's, which are spent: Claude Code's records stay as they were
            all["perModel"] = perModel;
            all["turns"] = turns ?? replies.Count;
            all["cutOff"] = !sawResult;
            return all;
        }

        JArray Changes()
        {
            var left = patches.ToList();
            var changes = new JArray();
            foreach (var call in calls.Where(c => c.Game && c.Name == "apply"))
            {
                // Each apply takes the oldest patch given for its key: a draft can be applied, edited and applied again.
                int at = left.FindIndex(p => p.mod == call.Mod);
                string patch = call.Mod != null && call.Mod.TrimStart().StartsWith('{') ? call.Mod : null;
                if (at >= 0)
                {
                    patch = left[at].patch;
                    left.RemoveAt(at);
                }
                changes.Add(new JObject { ["title"] = call.Title, ["mod"] = call.Mod, ["ok"] = call.Ok, ["patch"] = Parsed(patch) });
            }
            return changes;
        }

        static JToken Parsed(string patch)
        {
            if (patch == null) return JValue.CreateNull();
            try
            {
                return JToken.Parse(patch);
            }
            catch (JsonException)
            {
                return patch;
            }
        }

        /// <summary>
        /// The record so far, whole: take it after the process has ended and the world has it (or never will).
        /// Afterwards isn't here: it's known later, so it's its own line (ChatLog.Afterwards).
        /// </summary>
        public JObject Record()
        {
            lock (gate)
            {
                var outcome = Outcome();
                var record = new JObject
                {
                    ["id"] = who.Id,
                    ["game"] = new JObject { ["version"] = who.GameVersion, ["commit"] = who.Commit },
                    ["room"] = new JObject { ["mode"] = who.RoomMode, ["players"] = who.Players, ["modsOn"] = new JArray(who.ModsOn.ToArray<object>()) },
                    ["model"] = model,
                    ["claude"] = claude,
                    ["prompt"] = prompt,
                    ["source"] = source,
                    ["reply"] = reply ?? lastWords,
                    ["at"] = new JObject
                    {
                        ["processStarted"] = FromSend(started),
                        ["claudeReady"] = FromSend(ready),
                        ["firstWord"] = FromSend(firstWord),
                        ["firstTool"] = FromSend(firstTool),
                        ["firstApply"] = FromSend(firstApply),
                        ["applied"] = FromSend(applied),
                        ["inWorld"] = FromSend(inWorld),
                        ["done"] = FromSend(done),
                    },
                    ["split"] = Split(),
                    ["tokens"] = TokenGroup(),
                    ["tools"] = new JArray(calls.Select(c => new JObject { ["name"] = c.Name, ["game"] = c.Game, ["ms"] = c.To - c.From, ["ok"] = c.Ok, ["reason"] = c.Reason })),
                    ["outcome"] = outcome.ToString().ToLowerInvariant(),
                    ["why"] = outcome == ChatOutcome.Refused ? Couldnt() : failure,
                    ["endedBy"] = endedBy,
                    ["changes"] = Changes(),
                    ["gameCost"] = gameCost,
                };
                if (harness != null) record["harness"] = harness;
                return record;
            }
        }
    }

    /// <summary>The chat-log files: one JSON line per prompt, in a folder of their own, the newest files kept.</summary>
    public static class ChatLog
    {
        public const int MostFiles = 30;

        const string Prefix = "chat-log-", Suffix = ".jsonl";

        /// <summary>The player's data folder's subfolder for the records. Players are told to send Player.log, by name, and the records hold their prompts.</summary>
        public const string RecordsFolder = "chat-records";

        /// <summary>
        /// Where the records and the MCP log go. A log the process was given (-logFile, or the Editor's own) means a test, and they stay beside it,
        /// as an Editor shares its data folder with the player's game; logFolder is null otherwise.
        /// </summary>
        public static string Home(string dataFolder, string logFolder) =>
            string.IsNullOrEmpty(logFolder) ? Path.Combine(dataFolder, RecordsFolder) : logFolder;

        // One write at a time: a record and a later line go off on threads of their own, and the second to open the file would fail.
        static readonly object Writing = new object();

        public static void Append(string folder, DateTime day, JObject line) => Append(folder, day, line, Prefix);

        /// <summary>The same, in another log's files (the MCP server's).</summary>
        public static void Append(string folder, DateTime day, JObject line, string prefix)
        {
            string file = prefix + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + Suffix;
            lock (Writing)
            {
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, file), line.ToString(Formatting.None) + "\n");
                foreach (string old in Directory.GetFiles(folder, prefix + "*" + Suffix).OrderByDescending(f => f, StringComparer.Ordinal).Skip(MostFiles))
                    File.Delete(old);
            }
        }

        /// <summary>
        /// A later line about the record with this id, which chat-report folds into it: how long it stayed on before
        /// it was reverted (null if it's still on at the end of the session), and the record that fixes it, if one did.
        /// </summary>
        public static JObject Afterwards(string id, long? revertedAfterMs, string fixedBy = null) => new JObject
        {
            ["of"] = id,
            ["afterwards"] = new JObject { ["reverted"] = revertedAfterMs != null, ["afterMs"] = revertedAfterMs, ["fixedBy"] = fixedBy },
        };
    }
}
