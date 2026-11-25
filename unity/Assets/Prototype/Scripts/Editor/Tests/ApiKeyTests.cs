using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using Chem.Mcp;
using NUnit.Framework;
using UnityEngine;

// A player's own API key (J-071), against ApiStandIn: no real key and no real call. A check of a good key, a bad one and
// no network, Remove, a restart keeping the key, and where the key may and may not go.
public class ApiKeyTests
{
    const string Good = ApiStandIn.GoodKey + "-tests", Bad = ApiStandIn.BadKey + "-tests", Offline = ApiStandIn.OfflineKey + "-tests";

    string folder, envWas;
    IKeyVault vaultWas;
    Func<IKeyLister> listerWas;
    string[] modelsWas;

    [SetUp]
    public void OwnFile()
    {
        folder = Path.Combine(Path.GetTempPath(), "api-key-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        // The tests' own vault, so they run alike on every machine: the Mac has no vault yet (ApiKey.PlatformVault).
        vaultWas = ApiKey.Vault;
        ApiKey.Vault = new TestVault();
        ApiKey.TestFile = Path.Combine(folder, "api-key.dat");
        ChatSetup.TestFile = Path.Combine(folder, "chat-setup.dat");
        ChatSetup.Reload();
        listerWas = ApiKeyCheck.Lister;
        ApiKeyCheck.Lister = () => new ApiStandIn();
        modelsWas = ApiKey.Models;
        envWas = Environment.GetEnvironmentVariable(ClaudeRun.GameKey);
    }

    [TearDown]
    public void PutBack()
    {
        ApiKey.Remove();
        ApiKey.Vault = vaultWas;
        if (modelsWas.Length > 0) ApiKey.Models = modelsWas;
        ApiKey.TestFile = null;
        ChatSetup.TestFile = null;
        ChatSetup.Reload();
        ApiKeyCheck.Lister = listerWas;
        Environment.SetEnvironmentVariable(ClaudeRun.GameKey, envWas);
        Directory.Delete(folder, true);
    }

    // Seals by a header and the bytes turned round and mixed, so the plain key is never on disk and a file it didn't write
    // won't open; it holds nothing in memory, so a new one opens what an old one sealed (as after a restart).
    sealed class TestVault : IKeyVault
    {
        static readonly byte[] Header = Encoding.ASCII.GetBytes("test-vault:");

        public byte[] Seal(byte[] plain) => Header.Concat(Mixed(plain)).ToArray();

        public byte[] Open(byte[] sealedKey)
        {
            if (sealedKey.Length < Header.Length || !sealedKey.Take(Header.Length).SequenceEqual(Header))
                throw new IOException("Not sealed by the test vault.");
            return Mixed(sealedKey.Skip(Header.Length).ToArray());
        }

        static byte[] Mixed(byte[] bytes) => bytes.Reverse().Select(b => (byte)(b ^ 0x5A)).ToArray();
    }

    [Test]
    public void TheMachinesOwnVaultSealsAndOpens()
    {
        if (vaultWas == null) Assert.Ignore("No vault on this platform yet (ApiKey.PlatformVault): its players use the environment variable.");
        byte[] plain = Encoding.UTF8.GetBytes(Good);
        byte[] sealedKey = vaultWas.Seal(plain);
        Assert.That(Encoding.GetEncoding(28591).GetString(sealedKey), Does.Not.Contain(Good));
        Assert.That(vaultWas.Open(sealedKey), Is.EqualTo(plain));
        Assert.That(((IKeyVault)Activator.CreateInstance(vaultWas.GetType())).Open(sealedKey), Is.EqualTo(plain), "a new vault opens it, as after a restart");
    }

    static KeyCheck Checked(string typed)
    {
        var check = new ApiKeyCheck();
        check.Start(typed);
        for (int waited = 0; waited < 5000; waited += 10)
        {
            var result = check.Take();
            if (result != null) return result;
            Thread.Sleep(10);
        }
        throw new TimeoutException("The check never came back.");
    }

    [Test]
    public void AGoodKeyIsSavedSealedAndFillsTheModels()
    {
        var result = Checked(Good);
        Assert.That(result.Answer, Is.EqualTo(KeyAnswer.Good), result.Says);
        Assert.That(ApiKey.Saved && ApiKey.InPlay, Is.True);
        Assert.That(ApiKey.Read(), Is.EqualTo(Good));
        Assert.That(ApiKey.Models, Is.EqualTo(new[] { "claude-opus-5-5", "claude-haiku-5-5" }), "only what the key can use, and the game can run");
        Assert.That(ApiKey.Model, Is.EqualTo(ClaudeRun.ChatModel));
        string onDisk = Encoding.GetEncoding(28591).GetString(File.ReadAllBytes(ApiKey.TestFile));
        Assert.That(onDisk, Does.Not.Contain(Good));
        Assert.That(onDisk, Does.Not.Contain(ApiStandIn.GoodKey));
    }

    [Test]
    public void ABadKeyIsNeverSaved()
    {
        var result = Checked(Bad);
        Assert.That(result.Answer, Is.EqualTo(KeyAnswer.Refused));
        Assert.That(result.Says, Does.Contain("didn't work").And.Not.Contain(Bad));
        Assert.That(ApiKey.Saved, Is.False);
        Assert.That(File.Exists(ApiKey.TestFile), Is.False);
    }

    [Test]
    public void WithNoNetworkItSaysSoAndSavesNothing()
    {
        var result = Checked(Offline);
        Assert.That(result.Answer, Is.EqualTo(KeyAnswer.Unreached));
        Assert.That(result.Says, Does.Contain("Couldn't reach").And.Not.Contain(Offline));
        Assert.That(ApiKey.Saved, Is.False);
    }

    [Test]
    public void SomethingThatIsntAKeyIsNeverSent()
    {
        var result = Checked("two words");
        Assert.That(result.Answer, Is.EqualTo(KeyAnswer.NotSent));
        Assert.That(ApiKey.Saved, Is.False);
    }

    [Test]
    public void TheSavedKeyCanBeCheckedAgain()
    {
        Checked(Good);
        Assert.That(Checked(null).Answer, Is.EqualTo(KeyAnswer.Good));
        Assert.That(ApiKey.Read(), Is.EqualTo(Good));
    }

    [Test]
    public void RemoveForgetsTheKeyAndItsModels()
    {
        Checked(Good);
        ApiKey.Model = "claude-opus-5-5";
        ApiKey.Remove();
        Assert.That(ApiKey.Saved || ApiKey.InPlay, Is.False);
        Assert.That(File.Exists(ApiKey.TestFile), Is.False);
        Assert.That(ApiKey.Read(), Is.Null);
        Assert.That(ApiKey.Models, Is.Empty);
        Assert.That(ApiKey.Model, Is.EqualTo(ClaudeRun.ChatModel));
    }

    [Test]
    public void ARestartKeepsTheKey()
    {
        Checked(Good);
        ApiKey.Model = "claude-opus-5-5";
        var was = ApiKey.Vault;
        ApiKey.Vault = (IKeyVault)Activator.CreateInstance(was.GetType()); // nothing is held in memory: it's read from the file
        try
        {
            Assert.That(ApiKey.Read(), Is.EqualTo(Good));
            Assert.That(ApiKey.Model, Is.EqualTo("claude-opus-5-5"));
        }
        finally
        {
            ApiKey.Vault = was;
        }
    }

    [Test]
    public void AFileThatWontOpenIsNoKey()
    {
        File.WriteAllBytes(ApiKey.TestFile, new byte[] { 1, 2, 3 });
        Assert.That(ApiKey.Read(), Is.Null);
        Assert.That(Checked(null).Says, Does.Contain("can't be read"));
    }

    [Test]
    public void ASavedKeyWinsOverTheEnvironment()
    {
        Environment.SetEnvironmentVariable(ClaudeRun.GameKey, "from-the-environment");
        Assert.That(ApiKey.Current, Is.EqualTo("from-the-environment"));
        Assert.That(ApiKey.InPlay, Is.False, "the variable alone doesn't turn the chat over to the API where a vault could hold a key");
        Checked(Good);
        Assert.That(ApiKey.Current, Is.EqualTo(Good));
    }

    [Test]
    public void WithNoVaultTheEnvironmentRunsTheChat()
    {
        var was = ApiKey.Vault;
        ApiKey.Vault = null;
        try
        {
            Environment.SetEnvironmentVariable(ClaudeRun.GameKey, null);
            Assert.That(ApiKey.InPlay, Is.False);
            Environment.SetEnvironmentVariable(ClaudeRun.GameKey, "from-the-environment");
            Assert.That(ApiKey.InPlay, Is.True);
            Assert.That(ApiKey.Saved, Is.False);
        }
        finally
        {
            ApiKey.Vault = was;
        }
    }

    [Test]
    public void TheKeyGoesOnlyToAnthropicInItsHeader()
    {
        using var request = ApiWire.Signed(HttpMethod.Get, ApiWire.ModelsAddress, Good);
        Assert.That(request.RequestUri.Host, Is.EqualTo("api.anthropic.com"));
        Assert.That(request.RequestUri.Scheme, Is.EqualTo("https"));
        Assert.That(request.RequestUri.ToString(), Does.Not.Contain(Good));
        Assert.That(request.Headers.GetValues("x-api-key").Single(), Is.EqualTo(Good));
        Assert.That(request.Headers.Contains("Authorization"), Is.False);
        Assert.That(ApiWire.FollowsRedirects, Is.False, "a redirect would carry the header to wherever it points");
        Assert.Throws<InvalidOperationException>(() => ApiWire.Signed(HttpMethod.Get, "https://example.com/v1/models", Good));
        Assert.Throws<InvalidOperationException>(() => ApiWire.Signed(HttpMethod.Get, "http://api.anthropic.com/v1/models", Good));
    }

    // The ways out are few enough to hold to a list: anything else reading the key is a new way to leak it.
    [Test]
    public void OnlyTheWireTheLoopAndTheCheckReadTheKey()
    {
        var scripts = Path.Combine(Application.dataPath, "Prototype", "Scripts");
        var readers = Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "Tests" + Path.DirectorySeparatorChar))
            .Where(f => File.ReadAllText(f).Contains("ApiKey.Read(") || File.ReadAllText(f).Contains("ApiKey.Current"))
            .Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal);
        Assert.That(readers, Is.EqualTo(new[] { "ApiChat.cs", "ApiKeyCheck.cs", "ApiWire.cs", "ChatSetup.cs" }));
    }

    // That path runs a program on the player's PC: nothing but the local screen may set it.
    [Test]
    public void OnlyTheSettingsScreenWritesTheSetup()
    {
        string[] writes = { "ChatSetup.Choose(", "ChatSetup.SetPath(", "ChatSetup.SetVariable(", "ChatSetup.RemoveVariable(" };
        var scripts = Path.Combine(Application.dataPath, "Prototype", "Scripts");
        var writers = Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "Tests" + Path.DirectorySeparatorChar))
            .Where(f => writes.Any(File.ReadAllText(f).Contains))
            .Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal);
        Assert.That(writers, Is.EqualTo(new[] { "ClaudeCheck.cs", "ClaudeSetupSettings.cs" }), "ClaudeCheck keeps a path only for the screen's Check");
        // The core is the package now (packages/com.anweather.chemistry-core, beside unity/); its tools stay in Prototype/Chemistry.
        var core = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "packages", "com.anweather.chemistry-core"));
        Assert.That(Directory.Exists(core), Is.True, core);
        var coreFiles = Directory.GetFiles(core, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(Application.dataPath, "Prototype", "Chemistry"), "*.cs", SearchOption.AllDirectories));
        Assert.That(coreFiles.Where(f => File.ReadAllText(f).Contains("ChatSetup.")), Is.Empty, "the rules, mods and saves are the core's");
    }

    [Test]
    public void TheSetupIsSealedAndComesBackAfterARestart()
    {
        ChatSetup.SetPath(@"C:\elsewhere\claude.exe");
        ChatSetup.SetVariable("CLAUDE_CONFIG_DIR", "secret-value-of-mine");
        ChatSetup.Choose(ChatProfile.ClaudeCode);
        string onDisk = Encoding.GetEncoding(28591).GetString(File.ReadAllBytes(ChatSetup.TestFile));
        Assert.That(onDisk, Does.Not.Contain("secret-value-of-mine").And.Not.Contain("elsewhere"));
        ChatSetup.Reload();
        Assert.That(ChatSetup.ProgramPath, Is.EqualTo(@"C:\elsewhere\claude.exe"));
        Assert.That(ChatSetup.VariableNames, Is.EqualTo(new[] { "CLAUDE_CONFIG_DIR" }));
        Assert.That(ChatSetup.Chosen, Is.EqualTo(ChatProfile.ClaudeCode));
    }

    [Test]
    public void ARunIsHandedWhatWasListedAndNeverTheGamesKey()
    {
        Checked(Good);
        ChatSetup.SetVariable("CLAUDE_CONFIG_DIR", "somewhere");
        ChatSetup.SetVariable("SAME_AS_THE_KEY", Good);
        ChatSetup.SetVariable(ClaudeRun.GameKey, "listed-by-hand");
        var environment = new System.Collections.Generic.Dictionary<string, string> { ["PATH"] = "x", [ClaudeRun.GameKey] = "inherited" };
        ChatSetup.Hand(environment);
        Assert.That(environment.Keys.OrderBy(k => k, StringComparer.Ordinal), Is.EqualTo(new[] { "CLAUDE_CONFIG_DIR", "PATH" }));
        Assert.That(environment["CLAUDE_CONFIG_DIR"], Is.EqualTo("somewhere"));
    }

    [Test]
    public void TheProfileFollowsTheKeyUntilThePlayerPicks()
    {
        Assert.That(ChatSetup.Chosen, Is.EqualTo(ChatProfile.ClaudeCode));
        Assert.That(ChatSetup.OnApi, Is.False);
        Checked(Good);
        Assert.That(ChatSetup.OnApi, Is.True, "a saved key turns the chat to the API");
        ChatSetup.Choose(ChatProfile.ClaudeCode);
        Assert.That(ChatSetup.OnApi, Is.False, "the player's pick wins");
        ChatSetup.Choose(ChatProfile.Api);
        ApiKey.Remove();
        Assert.That(ChatSetup.OnApi, Is.False, "no key: the chat stays as it was");
    }

    static string CheckedClaude(ClaudeCheck check, string path)
    {
        check.Start(path);
        for (int waited = 0; waited < 20000; waited += 10)
        {
            string says = check.Take();
            if (says != null) return says;
            Thread.Sleep(10);
        }
        throw new TimeoutException("The check never came back.");
    }

    // A stand-in Claude Code install that prints its version and one variable from its environment: a .cmd on Windows,
    // a shell script elsewhere, so the check runs it the way it would run the real one on that machine.
    string StandInClaude(string version, string variable)
    {
        if (Application.platform == RuntimePlatform.WindowsEditor)
        {
            string cmd = Path.Combine(folder, "my-claude.cmd");
            File.WriteAllText(cmd, $"@echo {version} %{variable}%\r\n");
            return cmd;
        }
        string script = Path.Combine(folder, "my-claude");
        File.WriteAllText(script, $"#!/bin/sh\necho \"{version} ${variable}\"\n");
        using (var chmod = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/bin/chmod", $"+x \"{script}\"") { UseShellExecute = false }))
            chmod.WaitForExit();
        return script;
    }

    [Test]
    public void AClaudeCodePathIsFoundAndKept()
    {
        string install = StandInClaude("9.9.9 (Claude Code)", "CLAUDE_CONFIG_DIR");
        ChatSetup.SetVariable("CLAUDE_CONFIG_DIR", "hidden-value");
        var check = new ClaudeCheck();
        string says = CheckedClaude(check, install);
        Assert.That(check.Found, Is.True, says);
        Assert.That(says, Does.Contain("9.9.9").And.Not.Contain("hidden-value"));
        Assert.That(ChatSetup.ProgramPath, Is.EqualTo(install));
        Assert.That(ClaudeChat.Locate(ChatSetup.ProgramPath), Is.EqualTo(install));
    }

    [Test]
    public void AClaudeCodePathNotFoundIsSaidAndNotKept()
    {
        string was = ChatSetup.ProgramPath;
        var check = new ClaudeCheck();
        string says = CheckedClaude(check, Path.Combine(folder, "no-such-claude.exe"));
        Assert.That(check.Found, Is.False);
        Assert.That(says, Does.Contain("Couldn't find"));
        Assert.That(ChatSetup.ProgramPath, Is.EqualTo(was));
    }

    [Test]
    public void TheKeyFileIsOutsideTheChatsFolder()
    {
        var folders = new Chem.GameFolders("rules", "data");
        Assert.That(folders.ApiKeyFile, Does.Not.StartWith(folders.Claude.Path), "the chat's folder is claude's working folder");
    }
}
