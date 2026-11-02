using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chem.Mcp;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>The in-game chat's one headless process a message (ClaudeRun, J-060).</summary>
    public class ClaudeRunTests
    {
        [Test]
        public void ItCanOnlyUseTheGamesToolsAndTheNotes()
        {
            var args = ClaudeRun.Arguments();
            Assert.That(args, Does.Contain("--strict-mcp-config"), "the player's other MCP servers stay out of it");
            Assert.That(args[args.ToList().IndexOf("--permission-mode") + 1], Is.EqualTo("dontAsk"), "nobody is there to answer a prompt");
            Assert.That(args[args.ToList().IndexOf("--tools") + 1], Is.EqualTo("Read,Edit,Write"), "no shell");
            Assert.That(args, Does.Contain("mcp__spell-valley").And.Contain("Write(./notes.md)"));
            Assert.That(args.Where(a => a.Contains('(')), Has.All.Contain("./notes.md"), "the file tools reach only the notes");
        }

        [Test]
        public void ACommandLineQuotesWhatNeedsIt() =>
            Assert.That(ClaudeRun.CommandLine(new[] { "-p", "Read(./notes.md)" }), Is.EqualTo("-p \"Read(./notes.md)\""));

        [Test]
        public void ItReadsWordsToolsAndTheEnd()
        {
            const string say = "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"Blue it is.\"},{\"type\":\"tool_use\",\"name\":\"mcp__spell-valley__set_look\",\"input\":{}}]}}";
            Assert.That(ClaudeRun.Read(say).ToList(), Is.EqualTo(new[] { (ClaudeSaid.Words, "Blue it is."), (ClaudeSaid.Tool, "set_look") }));
            Assert.That(ClaudeRun.Read("{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"result\":\"Done\"}").Single(), Is.EqualTo((ClaudeSaid.Done, "Done")));
            Assert.That(ClaudeRun.Read("{\"type\":\"system\",\"subtype\":\"init\"}"), Is.Empty);
            Assert.That(ClaudeRun.Read("not json"), Is.Empty);
        }

        [TestCase("{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":true,\"result\":\"Not logged in · Please run /login\"}", true)]
        [TestCase("{\"type\":\"result\",\"subtype\":\"error_during_execution\",\"is_error\":true,\"result\":\"It stopped\"}", false)]
        public void AFailureSaysWhyAndWhetherItsTheSignIn(string line, bool signIn)
        {
            var (said, text) = ClaudeRun.Read(line).Single();
            Assert.That(said, Is.EqualTo(ClaudeSaid.Failed));
            Assert.That(ClaudeRun.NeedsSignIn(text), Is.EqualTo(signIn));
        }

        [Test]
        public void ItFindsClaudeOnThePathThenInLocalBin()
        {
            string home = Path.Combine("c", "me"), onPath = Path.Combine("c", "tools");
            string local = Path.Combine(home, ".local", "bin", "claude.exe"), path = Path.Combine(onPath, "claude.exe");
            var there = new HashSet<string> { local };
            Assert.That(ClaudeRun.Find(onPath, home, there.Contains), Is.EqualTo(local), "only in ~/.local/bin, as a game from the desktop sees it");
            there.Add(path);
            Assert.That(ClaudeRun.Find(onPath, home, there.Contains), Is.EqualTo(path), "the PATH first");
            Assert.That(ClaudeRun.Find(onPath, home, _ => false), Is.Null);
        }

        [Test]
        public void NotesKeepTheirNewestLines()
        {
            string notes = string.Join("\n", Enumerable.Range(1, 100)) + "\n";
            var kept = ClaudeRun.Trimmed(notes, 3).TrimEnd('\n').Split('\n');
            Assert.That(kept, Is.EqualTo(new[] { "98", "99", "100" }));
            Assert.That(ClaudeRun.Trimmed("a\nb\n", 3), Is.EqualTo("a\nb\n"));
            Assert.That(ClaudeRun.Trimmed(null), Is.Empty);
        }
    }
}
