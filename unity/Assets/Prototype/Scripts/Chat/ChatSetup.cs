using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Chem.Mcp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// How the game reaches Claude (J-071): the profile the player picked, and for Claude Code the install to run and the
// environment variables for that run. That path runs a program on the player's PC and the variables may be secrets, so
// the whole of it is sealed as the key is (ApiKey.Vault) and only the Settings screen writes it (ClaudeSetupSettings;
// ApiKeyTests holds it to that): no rule, mod, save or peer reaches it. On the main thread, as the Settings and GameChat are.
public static class ChatSetup
{
    const string ProfileField = "profile", PathField = "path", VariablesField = "variables";

    static string ownFile;
    static JObject held;

    public static string TestFile { get; set; }

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        TestFile = null;
        held = null;
    }

    public static void FindFile() => ownFile ??= ChemGrid.Folders.ChatSetupFile;

    static string SetupFile => TestFile ?? ownFile;

    public static void Reload() => held = null;

    static JObject Held => held ??= Load();

    static JObject Load()
    {
        if (ApiKey.Vault == null || SetupFile == null || !File.Exists(SetupFile)) return new JObject();
        try
        {
            return JObject.Parse(Encoding.UTF8.GetString(ApiKey.Vault.Open(File.ReadAllBytes(SetupFile))));
        }
        catch (System.Exception e) when (e is IOException or System.UnauthorizedAccessException or JsonException)
        {
            return new JObject();
        }
    }

    static void Store()
    {
        if (ApiKey.Vault == null || SetupFile == null) throw new IOException("There's nowhere safe to keep this on this computer.");
        byte[] sealedSetup = ApiKey.Vault.Seal(Encoding.UTF8.GetBytes(Held.ToString(Formatting.None)));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(SetupFile)));
        string fresh = SetupFile + ".new";
        File.WriteAllBytes(fresh, sealedSetup);
        File.Copy(fresh, SetupFile, true);
        File.Delete(fresh);
    }

    public static ChatProfile Chosen => ChatProfile.Of((string)Held[ProfileField]) ?? (ApiKey.InPlay ? ChatProfile.Api : ChatProfile.ClaudeCode);

    public static bool OnApi => Chosen == ChatProfile.Api && ApiKey.InPlay;

    public static void Choose(ChatProfile profile)
    {
        Held[ProfileField] = profile.Id;
        Store();
    }

    public static string ProgramPath => (string)Held[PathField] ?? ChatProfile.ClaudeCode.Program;

    public static void SetPath(string path)
    {
        Held[PathField] = path.Trim().Trim('"');
        Store();
    }

    static JObject Variables => Held[VariablesField] as JObject ?? new JObject();

    public static string[] VariableNames => Variables.Properties().Select(p => p.Name).ToArray();

    public static string[] Secrets => Variables.Properties().Select(p => (string)p.Value).Where(v => !string.IsNullOrEmpty(v)).ToArray();

    public static void SetVariable(string name, string value)
    {
        var variables = Variables;
        variables[name] = value;
        Held[VariablesField] = variables;
        Store();
    }

    public static void RemoveVariable(string name)
    {
        var variables = Variables;
        variables.Remove(name);
        Held[VariablesField] = variables;
        Store();
    }

    public static void Hand(IDictionary<string, string> environment)
    {
        foreach (var p in Variables.Properties()) environment[p.Name] = (string)p.Value;
        ClaudeRun.Scrub(environment, Variables.Count > 0 ? ApiKey.Read() : null);
    }
}
