using System.Collections.Generic;
using System.Linq;
using Chem.Mcp;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>A player's own key (J-071): reading the model list, the session's spend limit, and what a child process gets.</summary>
    [Category("Unit")]
    public class ApiModelsTests
    {
        const string Listed = @"{ ""data"": [ { ""id"": ""claude-opus-5-5"" }, { ""id"": ""claude-unpriced-1"" }, { ""id"": ""claude-haiku-5-5"" } ], ""has_more"": false }";

        [Test]
        public void AGoodKeyListsOnlyWhatTheLoopCanRun()
        {
            var check = ApiModels.Read(200, Listed);
            Assert.That(check.Answer, Is.EqualTo(KeyAnswer.Good));
            Assert.That(check.Models, Is.EqualTo(new[] { "claude-opus-5-5", "claude-haiku-5-5" }));
        }

        [Test]
        public void NoModelTheGameCanRunIsSaid()
        {
            var check = ApiModels.Read(200, @"{ ""data"": [ { ""id"": ""claude-unpriced-1"" } ] }");
            Assert.That(check.Answer, Is.EqualTo(KeyAnswer.NoModels));
            Assert.That(check.Models, Is.Empty);
        }

        [TestCase(401, KeyAnswer.Refused)]
        [TestCase(403, KeyAnswer.Refused)]
        [TestCase(429, KeyAnswer.Failed)]
        [TestCase(500, KeyAnswer.Failed)]
        public void AFailureNeverRepeatsWhatTheApiSaid(int status, KeyAnswer answer)
        {
            var check = ApiModels.Read(status, @"{ ""error"": { ""message"": ""bad key sk-ant-secret"" } }");
            Assert.That(check.Answer, Is.EqualTo(answer));
            Assert.That(check.Says, Does.Not.Contain("sk-ant-secret"));
        }

        [Test]
        public void AnAnswerThatIsntAListIsAFailure()
        {
            Assert.That(ApiModels.Read(200, "<html>").Answer, Is.EqualTo(KeyAnswer.Failed));
            Assert.That(ApiModels.Read(200, "{}").Answer, Is.EqualTo(KeyAnswer.Failed));
        }

        [TestCase("sk-ant-abc_DEF-123", true)]
        [TestCase("", false)]
        [TestCase(null, false)]
        [TestCase("two words", false)]
        [TestCase("line\nbreak", false)]
        [TestCase("caf\u00e9", false)]
        public void WhatCanGoInAHeader(string key, bool fits) => Assert.That(ApiModels.LooksLikeKey(key), Is.EqualTo(fits));

        [Test]
        public void ASessionsBudgetStopsAtItsLimitAndSaysSo()
        {
            var budget = ApiBudget.ForSession(1);
            Assert.That(budget.HasRoom, Is.True);
            budget.Add(0.6);
            Assert.That(budget.HasRoom, Is.True);
            budget.Add(0.6);
            Assert.That(budget.HasRoom, Is.False);
            Assert.That(budget.Refusal, Does.Contain("$1.00").And.Contain("Settings"));
            budget.Limit = 2;
            Assert.That(budget.HasRoom, Is.True, "raising it lets the chat go on");
        }

        [Test]
        public void AFileBudgetKeepsItsOwnWords() =>
            Assert.That(new ApiBudget("x").Refusal, Is.EqualTo("The API budget is spent, so this message wasn't sent."));

        [Test]
        public void AChildIsNeverHandedTheKey()
        {
            var environment = new Dictionary<string, string> { [ClaudeRun.GameKey] = "a", ["OTHER"] = "the-key", ["KEEP"] = "b" };
            ClaudeRun.Scrub(environment, "the-key");
            Assert.That(environment.Keys, Is.EqualTo(new[] { "KEEP" }));
        }

        [Test]
        public void ThePathIsThePlayersOrThePlainName()
        {
            string onPath = System.IO.Path.Combine("bin", "claude.exe");
            bool Exists(string p) => p == @"C:\mine\claude.exe" || p == onPath;
            Assert.That(ClaudeRun.Resolve("claude", "bin", null, Exists), Is.EqualTo(onPath));
            Assert.That(ClaudeRun.Resolve("", "bin", null, Exists), Is.EqualTo(onPath));
            Assert.That(ClaudeRun.Resolve("\"C:\\mine\\claude.exe\"", "", null, Exists), Is.EqualTo(@"C:\mine\claude.exe"));
            Assert.That(ClaudeRun.Resolve(@"C:\gone\claude.exe", "bin", null, Exists), Is.Null);
        }

        [Test]
        public void TheProfilesAreOneList()
        {
            Assert.That(ChatProfile.All.Select(p => p.Id), Is.EqualTo(new[] { "api", "claude-code" }));
            Assert.That(ChatProfile.Of("claude-code"), Is.SameAs(ChatProfile.ClaudeCode));
            Assert.That(ChatProfile.Of("nope"), Is.Null);
            Assert.That(ChatProfile.IsVariableName("CLAUDE_CONFIG_DIR"), Is.True);
            Assert.That(ChatProfile.IsVariableName("1A"), Is.False);
            Assert.That(ChatProfile.IsVariableName("A=B"), Is.False);
        }

        [Test]
        public void TheKeyFilesAreOutOfTheChatsFolder()
        {
            var folders = new GameFolders("rules", "data");
            Assert.That(folders.ApiKeyFile, Does.Not.StartWith(folders.Claude.Path));
            Assert.That(folders.ChatSetupFile, Does.Not.StartWith(folders.Claude.Path));
            Assert.That(new GameFolders("rules").ApiKeyFile, Is.Null);
        }
    }
}
