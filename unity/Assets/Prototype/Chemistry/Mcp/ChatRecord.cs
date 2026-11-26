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
    public enum ChatOutcome
    {
        Applied,
        Refused,
        Answered,
        Failed,
        TimedOut,
        Stopped,
    }

    public sealed class ChatWho
    {
        public string Id = "", GameVersion = "", Commit = "", RoomMode = "";
        public int Players;
        public IReadOnlyList<string> ModsOn = Array.Empty<string>();
    }

    public struct ChatProgress
    {
        public bool Started, Ready, Applying, Applied, InWorld, Done;
        public int Tools, ToolsDone;
    }

    public sealed class ChatRecorder
    {
        const int MostReasonChars = 300;

        const string ToolError = "An error occurred invoking '";

        static readonly HashSet<string> WorldTools = new HashSet<string> { "apply", "revert", "act", "call_act", "undo_act", "set_time", "set_weather" };

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

        public ChatRecorder(string prompt, string source, long sent, ChatWho who = null)
        {
            this.prompt = prompt;
            this.source = source;
            this.sent = sent;
            this.who = who ?? new ChatWho();
        }

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

        public void Started(long clock)
        {
            lock (gate) started ??= clock;
        }

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
                        firstWord ??= clock;
                        break;
                }
        }

        public void Patched(string mod, string patch)
        {
            lock (gate) patches.Add((mod, patch));
        }

        public void InWorld(long clock)
        {
            lock (gate) inWorld = clock;
        }

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

        public void ModelUnavailable(string asked)
        {
            lock (gate)
            {
                model = asked;
                failure = "model unavailable: " + asked;
            }
        }

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

        string Couldnt() =>
            calls.LastOrDefault(c => c.Game && c.Ok == false)?.Reason ?? (reply ?? lastWords ?? "").Trim().Split('\n')[0];

        long? FromSend(long? clock) => clock - sent;

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
            if (costBasis != null) all["costBasis"] = costBasis;
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

    public static class ChatLog
    {
        public const int MostFiles = 30;

        const string Prefix = "chat-log-", Suffix = ".jsonl";

        public const string RecordsFolder = "chat-records";

        public static string Home(string dataFolder, string logFolder) =>
            string.IsNullOrEmpty(logFolder) ? Path.Combine(dataFolder, RecordsFolder) : logFolder;

        static readonly object Writing = new object();

        public static void Append(string folder, DateTime day, JObject line) => Append(folder, day, line, Prefix);

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

        public static JObject Afterwards(string id, long? revertedAfterMs, string fixedBy = null) => new JObject
        {
            ["of"] = id,
            ["afterwards"] = new JObject { ["reverted"] = revertedAfterMs != null, ["afterMs"] = revertedAfterMs, ["fixedBy"] = fixedBy },
        };
    }
}
