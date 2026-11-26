// What a message costs on the Claude API, and the ceiling on real money (#59, backlog 27). Prices are the
// API's own per million tokens, so unlike Claude Code's list price on a plan, these dollars are really spent.
// A budget keeps its running total in a file, so it holds across runs, and refuses with no file at all. A player's own
// key has a session's budget instead: in memory, gone when the game closes, its limit theirs to change (J-071).
using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chem.Mcp
{
    public sealed class ApiPrice
    {
        public double Input, Write5m, Write1h, Read, Output;

        public long LongPrompt;

        public double LongFactor = 1;

        public static ApiPrice Of(string model)
        {
            switch (model)
            {
                case "claude-haiku-5-5":
                    return new ApiPrice { Input = 0.10, Write5m = 0.125, Write1h = 0.20, Read = 0.01, Output = 0.50, LongPrompt = 100_000, LongFactor = 5 };
                case "claude-opus-5-5":
                    return new ApiPrice { Input = 4, Write5m = 5, Write1h = 8, Read = 0.20, Output = 20 };
                default:
                    return null;
            }
        }

        public double Cost(JToken usage)
        {
            long input = (long?)usage?["input_tokens"] ?? 0, write = (long?)usage?["cache_creation_input_tokens"] ?? 0;
            long read = (long?)usage?["cache_read_input_tokens"] ?? 0, output = (long?)usage?["output_tokens"] ?? 0;
            var written = usage?["cache_creation"] as JObject;
            long write5m = (long?)written?["ephemeral_5m_input_tokens"] ?? 0;
            long write1h = (long?)written?["ephemeral_1h_input_tokens"] ?? 0;
            long unsaid = Math.Max(0, write - write5m - write1h);
            double factor = LongPrompt > 0 && input + write + read > LongPrompt ? LongFactor : 1;
            return factor * (input * Input + write5m * Write5m + (write1h + unsaid) * Write1h + read * Read + output * Output) / 1_000_000;
        }
    }

    public sealed class ApiBudget
    {
        public const double StopAt = 9;

        readonly object gate = new object();
        readonly string file;
        readonly bool session;
        double spentInSession;

        public ApiBudget(string file, double stopAt = StopAt)
        {
            this.file = file;
            Limit = stopAt;
        }

        ApiBudget(double limit)
        {
            session = true;
            Limit = limit;
        }

        public static ApiBudget ForSession(double limit) => new ApiBudget(limit);

        public double Limit { get; set; }

        public string Refusal => session ? SessionWords() : "The API budget is spent, so this message wasn't sent.";

        public string Stopped => session ? SessionWords() : "The API budget ran out partway through this message.";

        string SessionWords() =>
            $"That's this session's spend limit of ${Limit.ToString("0.00", CultureInfo.InvariantCulture)}, so Claude has stopped. Raise it in Settings, or restart the game.";

        public double Spent
        {
            get
            {
                lock (gate)
                {
                    if (session) return spentInSession;
                    if (string.IsNullOrEmpty(file)) return double.PositiveInfinity;
                    if (!File.Exists(file)) return 0;
                    try
                    {
                        return (double?)JObject.Parse(File.ReadAllText(file))["spent"] ?? double.PositiveInfinity;
                    }
                    catch (Exception e) when (e is IOException || e is JsonException || e is UnauthorizedAccessException)
                    {
                        return double.PositiveInfinity;
                    }
                }
            }
        }

        public bool HasRoom => Spent < Limit;

        public void Add(double dollars)
        {
            lock (gate)
            {
                if (session) spentInSession += dollars;
                if (string.IsNullOrEmpty(file)) return;
                double spent = Spent + dollars;
                string temporary = file + ".new";
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));
                File.WriteAllText(temporary, new JObject { ["spent"] = spent, ["updated"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) }.ToString());
                File.Copy(temporary, file, true);
                File.Delete(temporary);
            }
        }
    }
}
