// The game's own tool loop over the Claude Messages API, an evaluation behind the chat's seam (#59, backlog 27).
// It connects to the game's MCP server as a client, offers its tools and Instructions to the API unchanged, and runs
// the loop itself. Without Claude Code there's no dontAsk and no allow-list, so this decides what runs: only the tools
// named in Allowed, by name, and add_note, which appends to notes.md and nothing else. Nothing here reaches a shell.
// It writes the lines Claude Code's stream-json would, so ChatRecorder, chat-check and chat-strict read it as they are.
// No key and no HTTP here: IChatWire is the view's. Nothing it does decides shared state: only the server's tools do, as room commands.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chem.Mcp
{
    /// <summary>How the Messages API answered: 200 with every event given, or why not.</summary>
    public struct WireAnswer
    {
        public int Status;
        public double? RetryAfterSeconds;
        public string Failure;
    }

    /// <summary>The loop's two ways out, which the view owns: the game's local MCP server and the API.</summary>
    public interface IChatWire
    {
        /// <summary>One JSON-RPC message to the MCP server; its answer's body.</summary>
        string Mcp(string url, string body, CancellationToken token);

        /// <summary>One streaming request. Each server-sent event's data goes to data as it arrives.</summary>
        WireAnswer Messages(string body, Action<string> data, CancellationToken token);
    }

    public sealed class ApiLoop
    {
        public const int MostTurns = 16, MostOutput = 16000, MostTries = 4, MostNoteChars = 300;
        const double MostRetryWait = 30;

        /// <summary>The tool that stands for writing notes.md: nothing else here touches a file, and it can't name one.</summary>
        public const string NoteTool = "add_note";

        /// <summary>In the record: these dollars are the API's own prices, spent, not a list price a plan doesn't pay.</summary>
        public const string RealMoney = "api-real";

        const string Protocol = "2025-06-18";

        /// <summary>The only game tools the loop runs or offers, by name. A tool the server adds later stays out until it's read and named here.</summary>
        public static readonly IReadOnlyCollection<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "add_building", "add_building_part", "add_entity", "add_item", "add_material", "add_reaction", "add_spell", "add_thing", "add_wing", "apply",
            "describe", "explain", "find", "find_site", "here", "list", "outside_rules", "places", "remove", "revert", "room",
            "save", "set_building", "set_look", "set_material", "set_reaction", "set_spell", "set_time", "set_weather",
            "simulate", "time", "tune", "validate", "weather", "whats_on",
        };

        /// <summary>The system prompt: ClaudeRun.ChatPrompt's job with no shell, no files and notes that come with the message. No game knowledge.</summary>
        public const string Prompt = "You are the chat inside a running game, Spell Chemistry Valley, answering one message from the player while they play. " +
            "Use only the tools you are given. You have no shell and no files, and this chat is for the game alone: if a message is about anything else, " +
            "say so in a line and offer what the tools can do. Each message starts fresh and remembers nothing. Notes from earlier messages, if any, come just before the message. " +
            "After you change something, use " + NoteTool + " to add a line or two saying what you changed and what's on now. Answer in a line or two, in plain words.";

        static readonly JObject NoteDefinition = new JObject
        {
            ["name"] = NoteTool,
            ["description"] = "Adds one line to the notes the next message will see. Say what you changed and what's on now.",
            ["input_schema"] = new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject { ["note"] = new JObject { ["type"] = "string", ["description"] = "The line, in plain words." } },
                ["required"] = new JArray("note"),
            },
        };

        readonly IChatWire wire;
        readonly ApiBudget budget;
        readonly string model;
        readonly ApiPrice price;
        readonly Action<string> say;
        readonly Func<long> clock;
        readonly List<JObject> messages = new List<JObject>();
        readonly JObject total = new JObject { ["input_tokens"] = 0L, ["cache_creation_input_tokens"] = 0L, ["cache_read_input_tokens"] = 0L, ["output_tokens"] = 0L };

        JArray tools;
        string instructions = "", folder, url;
        double cost;
        long apiMs, began;
        int turns, rpcId;
        bool wordSeen;

        /// <summary>The first wait before a retry, in ms, doubling after; a Retry-After wins.</summary>
        public int RetryBaseMs { get; set; } = 1000;

        /// <summary>How long the prefix stays cached: tools, system prompt and Instructions. 1h or 5m.</summary>
        public string PrefixTtl { get; set; } = "1h";

        /// <summary>One message's loop. Say is told each stream-json line as it happens.</summary>
        public ApiLoop(IChatWire wire, ApiBudget budget, string model, Action<string> say, Func<long> clock)
        {
            this.wire = wire;
            this.budget = budget;
            this.model = model;
            this.say = say;
            this.clock = clock;
            price = ApiPrice.Of(model);
        }

        /// <summary>The whole of one message: returns once it's done, failed or stopped. A stop ends it with no result line.</summary>
        public void Run(string message, string folder, string url, CancellationToken stop)
        {
            this.folder = folder;
            this.url = url;
            began = clock();
            if (price == null)
            {
                Finish($"The loop has no prices for {model}, so it can't count the money.", "budget");
                return;
            }
            if (!budget.HasRoom)
            {
                Finish("The API budget is spent, so this message wasn't sent.", "budget");
                return;
            }
            try
            {
                Connect(stop);
                Say(new JObject { ["type"] = "system", ["subtype"] = "init", ["model"] = model, ["claude_code_version"] = null, ["harness"] = "api", ["tools"] = new JArray(tools.Select(t => t["name"])) });
                messages.Add(FirstMessage(message));
                Loop(stop);
            }
            catch (OperationCanceledException)
            {
                // Cancel and the timeout: the caller says which, and no result line follows.
            }
            catch (ChatWireException e)
            {
                Finish(e.Message, "wire");
            }
        }

        void Connect(CancellationToken stop)
        {
            var answer = Rpc("initialize", new JObject { ["protocolVersion"] = Protocol, ["capabilities"] = new JObject(), ["clientInfo"] = new JObject { ["name"] = "spell-valley-chat", ["version"] = "1" } }, stop);
            instructions = (string)answer["instructions"] ?? "";
            tools = Offered(Rpc("tools/list", new JObject(), stop)["tools"] as JArray ?? new JArray());
        }

        /// <summary>The server's tools as the Messages API takes them, unchanged but for the name of the schema's key; only those in Allowed, and add_note; in name order so the bytes never move.</summary>
        public static JArray Offered(JArray server)
        {
            var offered = server.OfType<JObject>().Where(t => Allowed.Contains((string)t["name"]))
                .Select(t => new JObject { ["name"] = t["name"], ["description"] = t["description"], ["input_schema"] = t["inputSchema"].DeepClone() })
                .Concat(new[] { (JObject)NoteDefinition.DeepClone() })
                .OrderBy(t => (string)t["name"], StringComparer.Ordinal);
            return new JArray(offered);
        }

        JObject Rpc(string method, JObject given, CancellationToken stop)
        {
            string body = new JObject { ["jsonrpc"] = "2.0", ["id"] = ++rpcId, ["method"] = method, ["params"] = given }.ToString(Formatting.None);
            JObject answer;
            try
            {
                answer = JObject.Parse(wire.Mcp(url, body, stop));
            }
            catch (JsonException)
            {
                throw new ChatWireException("The game's tools didn't answer in JSON.");
            }
            if (answer["error"] != null) throw new ChatWireException((string)answer["error"]["message"] ?? "The game's tools refused that.");
            return answer["result"] as JObject ?? new JObject();
        }

        // What came before, then the message: notes change, so they sit after the cached prefix.
        JObject FirstMessage(string message)
        {
            var content = new JArray();
            string file = Path.Combine(folder, ClaudeRun.NotesFile);
            string notes = File.Exists(file) ? ClaudeRun.Trimmed(File.ReadAllText(file)).Trim() : "";
            if (notes.Length > 0) content.Add(new JObject { ["type"] = "text", ["text"] = "Notes from earlier messages:\n" + notes });
            content.Add(new JObject { ["type"] = "text", ["text"] = message });
            return new JObject { ["role"] = "user", ["content"] = content };
        }

        void Loop(CancellationToken stop)
        {
            while (true)
            {
                if (turns >= MostTurns)
                {
                    Finish($"It took more than {MostTurns} turns, so it was stopped.", "turns");
                    return;
                }
                if (!budget.HasRoom)
                {
                    Finish("The API budget ran out partway through this message.", "budget");
                    return;
                }
                var reply = Reply(stop);
                turns++;
                var content = reply.Content();
                messages.Add(new JObject { ["role"] = "assistant", ["content"] = content });
                Say(new JObject { ["type"] = "assistant", ["message"] = new JObject { ["id"] = reply.Id, ["model"] = model, ["usage"] = reply.Usage, ["content"] = Shown(content) } });
                switch (reply.StopReason)
                {
                    case "tool_use":
                        RunTools(content, stop);
                        break;
                    case "pause_turn":
                        break; // the same request again, and it carries on
                    case "end_turn":
                    case "stop_sequence":
                        Finish(null, "completed", TextOf(content));
                        return;
                    case "max_tokens":
                        Finish("The reply ran past its length, so its tools weren't run.", "max_tokens");
                        return;
                    case "refusal":
                        Finish($"Claude declined that{(reply.RefusalCategory == null ? "" : $" ({reply.RefusalCategory})")}, so nothing was run.", "refusal");
                        return;
                    default:
                        Finish($"It stopped for a reason this loop doesn't know: {reply.StopReason}.", "unknown");
                        return;
                }
            }
        }

        static string TextOf(JArray content) =>
            string.Join("\n", content.OfType<JObject>().Where(b => (string)b["type"] == "text").Select(b => (string)b["text"]));

        // The stream's own view of a reply: game tools under the name Claude Code gives them, and no thinking.
        static JArray Shown(JArray content)
        {
            var shown = new JArray();
            foreach (var block in content.OfType<JObject>().Where(b => (string)b["type"] != "thinking" && (string)b["type"] != "redacted_thinking"))
            {
                var copy = (JObject)block.DeepClone();
                if ((string)copy["type"] == "tool_use" && (string)copy["name"] != NoteTool) copy["name"] = ClaudeRun.ToolPrefix + (string)copy["name"];
                shown.Add(copy);
            }
            return shown;
        }

        Streamed Reply(CancellationToken stop)
        {
            string body = RequestBody();
            string lastFailure = "The API didn't answer.";
            for (int attempt = 0; attempt < MostTries; attempt++)
            {
                if (attempt > 0) Wait(lastRetryAfter, attempt, stop);
                var streamed = new Streamed();
                long from = clock();
                var answer = wire.Messages(body, data => Heard(streamed, data), stop);
                apiMs += clock() - from;
                stop.ThrowIfCancellationRequested();
                Spend(streamed.Usage);
                if (answer.Status == 200 && streamed.Complete) return streamed;
                lastRetryAfter = answer.RetryAfterSeconds;
                lastFailure = streamed.Failure ?? answer.Failure ?? $"The API answered {answer.Status}.";
                if (!Retries(answer.Status, streamed)) throw new ChatWireException(lastFailure);
            }
            throw new ChatWireException(lastFailure);
        }

        double? lastRetryAfter;

        // Rate limits, overload, the service's own errors and a cut connection are worth another go; anything else would only fail the same way.
        static bool Retries(int status, Streamed streamed) =>
            streamed.Failure != null ? streamed.FailureRetries : status == 0 || status == 200 || status == 408 || status == 429 || status >= 500;

        void Wait(double? retryAfter, int attempt, CancellationToken stop)
        {
            int ms = retryAfter != null ? (int)(Math.Min(retryAfter.Value, MostRetryWait) * 1000) : RetryBaseMs << (attempt - 1);
            stop.WaitHandle.WaitOne(ms);
            stop.ThrowIfCancellationRequested();
        }

        void Spend(JObject usage)
        {
            if (usage == null) return;
            double dollars = price.Cost(usage);
            cost += dollars;
            budget.Add(dollars);
            foreach (var key in total.Properties().Select(p => p.Name).ToList()) total[key] = (long)total[key] + ((long?)usage[key] ?? 0);
        }

        /// <summary>The request as bytes: the system prompt ends the cached prefix, and the last block of the last message marks the tail for the next turn.</summary>
        public string RequestBody()
        {
            var sent = new JArray(messages.Select(m => m.DeepClone()));
            var last = ((JObject)sent.Last)["content"] as JArray;
            ((JObject)last.Last)["cache_control"] = new JObject { ["type"] = "ephemeral" };
            return new JObject
            {
                ["model"] = model,
                ["max_tokens"] = MostOutput,
                ["stream"] = true,
                ["system"] = new JArray(
                    new JObject { ["type"] = "text", ["text"] = Prompt },
                    new JObject { ["type"] = "text", ["text"] = instructions, ["cache_control"] = PrefixCache() }),
                ["tools"] = tools,
                ["messages"] = sent,
            }.ToString(Formatting.None);
        }

        JObject PrefixCache()
        {
            var cache = new JObject { ["type"] = "ephemeral" };
            if (PrefixTtl == "1h") cache["ttl"] = "1h";
            return cache;
        }

        void Heard(Streamed reply, string data)
        {
            reply.Take(data);
            if (reply.SawWords && !wordSeen)
            {
                wordSeen = true;
                Say(new JObject { ["type"] = "first_word" });
            }
        }

        void RunTools(JArray content, CancellationToken stop)
        {
            var results = new JArray();
            foreach (var use in content.OfType<JObject>().Where(b => (string)b["type"] == "tool_use"))
            {
                stop.ThrowIfCancellationRequested();
                string id = (string)use["id"], name = (string)use["name"];
                var (text, error) = Use(name, use["input"] as JObject ?? new JObject(), stop);
                var result = new JObject { ["type"] = "tool_result", ["tool_use_id"] = id, ["content"] = text };
                if (error) result["is_error"] = true;
                results.Add(result);
                Say(new JObject { ["type"] = "user", ["message"] = new JObject { ["role"] = "user", ["content"] = new JArray(result.DeepClone()) } });
            }
            messages.Add(new JObject { ["role"] = "user", ["content"] = results });
        }

        (string text, bool error) Use(string name, JObject input, CancellationToken stop)
        {
            if (name == NoteTool) return AddNote((string)input["note"]);
            if (!Allowed.Contains(name)) return ($"There's no tool {name} here.", true);
            JObject answer;
            try
            {
                answer = Rpc("tools/call", new JObject { ["name"] = name, ["arguments"] = input }, stop);
            }
            catch (ChatWireException e)
            {
                return (e.Message, true);
            }
            string said = string.Concat((answer["content"] as JArray ?? new JArray()).OfType<JObject>().Select(c => (string)c["text"]));
            return (said, (bool?)answer["isError"] == true);
        }

        (string, bool) AddNote(string note)
        {
            string line = string.Join(" ", (note ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
            if (line.Length == 0) return ("The note was empty.", true);
            if (line.Length > MostNoteChars) line = line.Substring(0, MostNoteChars);
            File.AppendAllText(Path.Combine(folder, ClaudeRun.NotesFile), "- " + line + "\n");
            return ("Noted.", false);
        }

        void Finish(string failure, string reason, string text = null)
        {
            var usage = (JObject)total.DeepClone();
            var perModel = new JObject();
            if (turns > 0)
                perModel[model] = new JObject
                {
                    ["inputTokens"] = total["input_tokens"],
                    ["outputTokens"] = total["output_tokens"],
                    ["cacheReadInputTokens"] = total["cache_read_input_tokens"],
                    ["cacheCreationInputTokens"] = total["cache_creation_input_tokens"],
                    ["costUSD"] = cost,
                    ["costBasis"] = RealMoney,
                };
            Say(new JObject
            {
                ["type"] = "result",
                ["subtype"] = failure == null ? "success" : "error_during_execution",
                ["is_error"] = failure != null,
                ["result"] = failure ?? text,
                ["terminal_reason"] = reason,
                ["duration_ms"] = clock() - began,
                ["duration_api_ms"] = apiMs,
                ["num_turns"] = turns,
                ["total_cost_usd"] = cost,
                ["cost_basis"] = RealMoney,
                ["usage"] = usage,
                ["modelUsage"] = perModel,
            });
        }

        void Say(JObject line) => say(line.ToString(Formatting.None));

        // One reply as the events build it. Blocks go back to the API as they came, thinking and its signature included.
        sealed class Streamed
        {
            readonly SortedDictionary<int, JObject> blocks = new SortedDictionary<int, JObject>();
            readonly Dictionary<int, string> inputs = new Dictionary<int, string>();

            public string Id, StopReason, RefusalCategory, Failure;
            public bool Complete, FailureRetries, SawWords;
            public JObject Usage;

            public void Take(string data)
            {
                JObject e;
                try
                {
                    e = JObject.Parse(data);
                }
                catch (JsonException)
                {
                    return;
                }
                int index = (int?)e["index"] ?? 0;
                switch ((string)e["type"])
                {
                    case "message_start":
                        Id = (string)e["message"]?["id"];
                        Usage = e["message"]?["usage"]?.DeepClone() as JObject;
                        break;
                    case "content_block_start":
                        blocks[index] = (JObject)e["content_block"].DeepClone();
                        break;
                    case "content_block_delta":
                        Delta(index, e["delta"] as JObject);
                        break;
                    case "message_delta":
                        StopReason = (string)e["delta"]?["stop_reason"] ?? StopReason;
                        RefusalCategory = (string)e["delta"]?["stop_details"]?["category"];
                        Merge(e["usage"] as JObject);
                        break;
                    case "message_stop":
                        Complete = true;
                        break;
                    case "error":
                        string kind = (string)e["error"]?["type"];
                        Failure = (string)e["error"]?["message"] ?? "The API stopped the reply.";
                        FailureRetries = kind == "overloaded_error" || kind == "rate_limit_error" || kind == "api_error";
                        break;
                }
            }

            void Merge(JObject more)
            {
                if (more == null) return;
                Usage ??= new JObject();
                foreach (var p in more.Properties()) if (p.Value.Type != JTokenType.Null) Usage[p.Name] = p.Value.DeepClone();
            }

            void Delta(int index, JObject delta)
            {
                if (delta == null || !blocks.TryGetValue(index, out var block)) return;
                switch ((string)delta["type"])
                {
                    case "text_delta":
                        string words = (string)delta["text"] ?? "";
                        block["text"] = (string)block["text"] + words;
                        SawWords |= words.Trim().Length > 0;
                        break;
                    case "input_json_delta":
                        inputs[index] = (inputs.TryGetValue(index, out string so) ? so : "") + (string)delta["partial_json"];
                        break;
                    case "thinking_delta":
                        block["thinking"] = (string)block["thinking"] + (string)delta["thinking"];
                        break;
                    case "signature_delta":
                        block["signature"] = delta["signature"];
                        break;
                }
            }

            public JArray Content()
            {
                var content = new JArray();
                foreach (var (index, block) in blocks.Select(b => (b.Key, b.Value)))
                {
                    if ((string)block["type"] == "text" && string.IsNullOrEmpty((string)block["text"])) continue;
                    if ((string)block["type"] == "tool_use") block["input"] = Input(inputs.TryGetValue(index, out string json) ? json : null);
                    content.Add(block);
                }
                return content;
            }

            // Bad JSON here reaches the server as no arguments, and the tool's own refusal says what it needs.
            static JObject Input(string json)
            {
                if (string.IsNullOrEmpty(json)) return new JObject();
                try
                {
                    return JToken.Parse(json) as JObject ?? new JObject();
                }
                catch (JsonException)
                {
                    return new JObject();
                }
            }
        }
    }

    /// <summary>A way out failed: the sentence is what the player reads, so it never holds a key.</summary>
    public sealed class ChatWireException : Exception
    {
        public ChatWireException(string message) : base(message)
        {
        }

        public ChatWireException()
        {
        }

        public ChatWireException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}
