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
    Func<IKeyLister> listerWas;
    string[] modelsWas;

    [SetUp]
    public void OwnFile()
    {
        folder = Path.Combine(Path.GetTempPath(), "api-key-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
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
        if (modelsWas.Length > 0) ApiKey.Models = modelsWas;
        ApiKey.TestFile = null;
        ChatSetup.TestFile = null;
        ChatSetup.Reload();
        ApiKeyCheck.Lister = listerWas;
        Environment.SetEnvironmentVariable(ClaudeRun.GameKey, envWas);
        Directory.Delete(folder, true);
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
        var core = Path.Combine(Application.dataPath, "Prototype", "Chemistry");
        Assert.That(Directory.GetFiles(core, "*.cs", SearchOption.AllDirectories).Where(f => File.ReadAllText(f).Contains("ChatSetup.")), Is.Empty, "the rules, mods and saves are the core's");
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

    [Test]
    public void AClaudeCodePathIsFoundAndKept()
    {
        string install = Path.Combine(folder, "my-claude.cmd");
        File.WriteAllText(install, "@echo 9.9.9 (Claude Code) %CLAUDE_CONFIG_DIR%\r\n");
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
