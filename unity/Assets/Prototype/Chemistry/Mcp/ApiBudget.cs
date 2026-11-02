// What a message costs on the Claude API, and the ceiling on real money (#59, backlog 27). Prices are the
// API's own per million tokens, so unlike Claude Code's list price on a plan, these dollars are really spent.
// A budget keeps its running total in a file, so it holds across runs, and refuses with no file at all.
using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chem.Mcp
{
    /// <summary>Dollars per million tokens for one model.</summary>
    public sealed class ApiPrice
    {
        public double Input, Write5m, Write1h, Read, Output;

        /// <summary>Haiku's rates change past this many prompt tokens; zero for none.</summary>
        public long LongPrompt;

        /// <summary>What a long prompt multiplies every rate by.</summary>
        public double LongFactor = 1;

        /// <summary>The prices of the models the loop may use; null for any other, which it refuses, as it couldn't count the money.</summary>
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

        /// <summary>The dollars one reply's usage comes to. A write that doesn't say which cache it went to counts as the dearer.</summary>
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

    /// <summary>A ceiling on real money across every run that shares a file.</summary>
    public sealed class ApiBudget
    {
        /// <summary>No prompt starts once this much is spent (the evaluation's own limit, J-064).</summary>
        public const double StopAt = 9;

        readonly object gate = new object();
        readonly string file;
        readonly double stopAt;

        public ApiBudget(string file, double stopAt = StopAt)
        {
            this.file = file;
            this.stopAt = stopAt;
        }

        /// <summary>Dollars spent so far; infinity if the file can't be read, so nothing starts on a total nobody knows.</summary>
        public double Spent
        {
            get
            {
                lock (gate)
                {
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

        /// <summary>Whether another request may start.</summary>
        public bool HasRoom => Spent < stopAt;

        /// <summary>Adds what a reply cost, as soon as it's known.</summary>
        public void Add(double dollars)
        {
            lock (gate)
            {
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
