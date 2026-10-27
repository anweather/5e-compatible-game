using System.IO;

// The runner's side of run.json: loading what the bot wrote (or standing in
// for a report the bot never wrote), and the candidate for how the run ended
// when the player didn't finish or crashed.
namespace Playtest.Runner
{
    public static class Report
    {
        /// <summary>The bot's run.json, or an empty report saying it never wrote one.</summary>
        public static RunReport Load(string file, string id)
        {
            if (File.Exists(file)) return RunReport.FromJson(File.ReadAllText(file));
            return new RunReport { RunId = id, Ended = "no report" };
        }

        /// <summary>
        /// A hang, a crash (quit or running), or the player exiting before it finished; null for a
        /// clean finish. "quit" when the bot had finished and the player failed on the way out.
        /// </summary>
        public static Candidate Ending(RunReport run, bool hung, string crash, int exitCode, int seconds)
        {
            bool finished = run.Ended == "done" || run.Ended == "until-frame";
            string when = finished ? "quit" : "running";
            if (hung) return Make(run, "hang", when, $"the player stopped beating for a minute (or overran) after {seconds} s");
            if (crash != null) return Make(run, "crash", when, crash);
            if (!finished) return Make(run, "crash", "running", $"the player exited with code {exitCode} before finishing (no crash report)");
            return null;
        }

        static Candidate Make(RunReport run, string detector, string key, string message) =>
            new Candidate(detector, key, Category.GameBreaking, run.Frames, message) { Goal = run.Goals.Count > 0 ? run.Goals[run.Goals.Count - 1] : null };
    }
}
