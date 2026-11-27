using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Chem.Mcp;
using UnityEngine;

// A player's own Anthropic API key (J-071): sealed for this Windows user in the player's data folder, read only
// when it's about to be sent. It goes nowhere else: not PlayerPrefs (plain text in the registry), not a process's
// environment, not a log, and only ApiWire, ApiChat and ApiKeyCheck ever read it (ApiKeyTests holds them to that).
// A platform with no vault yet has none of this, and its players use the environment variable.

/// <summary>What seals a key on this machine, for this user; throws IOException if it can't.</summary>
public interface IKeyVault
{
    byte[] Seal(byte[] plain);
    byte[] Open(byte[] sealedKey);
}

public static class ApiKey
{
    const string ChosenModel = "chat.api-model", ListedModels = "chat.api-models", CapPref = "chat.api-cap";
    const float DefaultCap = 1f, LeastCap = 0.5f, MostCap = 100f;

    static string ownFile;

    /// <summary>What seals the key here; null on a platform that has no vault yet.</summary>
    public static IKeyVault Vault { get; set; } = PlatformVault();

    /// <summary>A test's or a probe's file in place of the player's.</summary>
    public static string TestFile { get; set; }

    /// <summary>Works out the player's file, as only the main thread can. Called at the start.</summary>
    public static void FindFile() => ownFile ??= ChemGrid.Folders.ApiKeyFile;

    static string KeyFile => TestFile ?? ownFile;

    /// <summary>Whether a key is saved. Says no more than that, and opens nothing, so it's fine on the main thread.</summary>
    public static bool Saved => Vault != null && KeyFile != null && File.Exists(KeyFile);

    /// <summary>The key to send: a saved one, else the environment variable. Off the main thread: opening it takes a moment.</summary>
    public static string Current => Read() ?? Environment.GetEnvironmentVariable(ClaudeRun.GameKey);

    /// <summary>
    /// Whether the chat runs on the API. A saved key does that; with no vault here, the environment variable does.
    /// Where a vault could hold one, the variable alone doesn't, so nobody starts paying for messages unasked.
    /// </summary>
    public static bool InPlay => Saved || (Vault == null && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ClaudeRun.GameKey)));

    /// <summary>The saved key, or null with none or one this user can't open.</summary>
    public static string Read()
    {
        if (!Saved) return null;
        try
        {
            return Encoding.UTF8.GetString(Vault.Open(File.ReadAllBytes(KeyFile)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Saves a key, sealed. Throws IOException or UnauthorizedAccessException if it can't, saying nothing of the key.</summary>
    public static void Save(string key)
    {
        if (Vault == null || KeyFile == null) throw new IOException("There's nowhere safe to keep a key on this computer.");
        byte[] sealedKey = Vault.Seal(Encoding.UTF8.GetBytes(key));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(KeyFile)));
        string fresh = KeyFile + ".new";
        File.WriteAllBytes(fresh, sealedKey);
        File.Copy(fresh, KeyFile, true);
        File.Delete(fresh);
    }

    /// <summary>Forgets the key and the models it could use.</summary>
    public static void Remove()
    {
        if (KeyFile != null && File.Exists(KeyFile)) File.Delete(KeyFile);
        PlayerPrefs.DeleteKey(ChosenModel);
        PlayerPrefs.DeleteKey(ListedModels);
        PlayerPrefs.Save();
    }

    /// <summary>The models the key could use when it was last checked: the player picks from these.</summary>
    public static string[] Models
    {
        get
        {
            string listed = PlayerPrefs.GetString(ListedModels, "");
            return listed.Length == 0 ? new string[0] : listed.Split(',');
        }
        set
        {
            PlayerPrefs.SetString(ListedModels, string.Join(",", value));
            PlayerPrefs.Save();
        }
    }

    /// <summary>The model the chat uses: the player's pick while the key can use it, else the chat's own, else the first it can.</summary>
    public static string Model
    {
        get
        {
            var listed = Models;
            string chosen = PlayerPrefs.GetString(ChosenModel, "");
            if (listed.Contains(chosen)) return chosen;
            return listed.Length == 0 || listed.Contains(ClaudeRun.ChatModel) ? ClaudeRun.ChatModel : listed[0];
        }
        set
        {
            PlayerPrefs.SetString(ChosenModel, value);
            PlayerPrefs.Save();
        }
    }

    /// <summary>The most a session may spend through the key, in dollars: the player's to change.</summary>
    public static float Cap
    {
        get => PlayerPrefs.GetFloat(CapPref, DefaultCap);
        set
        {
            float cap = Mathf.Clamp(value, LeastCap, MostCap);
            PlayerPrefs.SetFloat(CapPref, cap);
            PlayerPrefs.Save();
            Session.Limit = cap;
        }
    }

    static ApiBudget session;

    // Each Play mode starts from these, with the domain reload off too (Enter Play Mode Options): a probe sets them.
    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        TestFile = null;
        session = null;
    }

    /// <summary>What this sitting has spent, and its limit: gone when the game closes.</summary>
    public static ApiBudget Session => session ??= ApiBudget.ForSession(Cap);

    static IKeyVault PlatformVault()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        return new WindowsVault();
#else
        return null;
#endif
    }
}

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
/// <summary>
/// DPAPI, straight from crypt32: sealed with this Windows user's own secret, so another user on the PC, or a copy of
/// the file on another PC, can't open it. Through P/Invoke as System.Security.Cryptography's ProtectedData isn't in
/// this profile on every backend.
/// </summary>
public sealed class WindowsVault : IKeyVault
{
    const int NoPrompts = 1;
    static readonly byte[] Purpose = Encoding.UTF8.GetBytes("spell-chemistry-valley.api-key");

    [StructLayout(LayoutKind.Sequential)]
    struct Blob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CryptProtectData(ref Blob input, string description, ref Blob entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CryptUnprotectData(ref Blob input, IntPtr description, ref Blob entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);

    [DllImport("kernel32.dll")]
    static extern IntPtr LocalFree(IntPtr memory);

    public byte[] Seal(byte[] plain) => Through(plain, (ref Blob input, ref Blob entropy, out Blob output) =>
        CryptProtectData(ref input, null, ref entropy, IntPtr.Zero, IntPtr.Zero, NoPrompts, out output));

    public byte[] Open(byte[] sealedKey) => Through(sealedKey, (ref Blob input, ref Blob entropy, out Blob output) =>
        CryptUnprotectData(ref input, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, NoPrompts, out output));

    delegate bool Call(ref Blob input, ref Blob entropy, out Blob output);

    static byte[] Through(byte[] bytes, Call call)
    {
        var pinnedBytes = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        var pinnedPurpose = GCHandle.Alloc(Purpose, GCHandleType.Pinned);
        try
        {
            var input = new Blob { Size = bytes.Length, Data = pinnedBytes.AddrOfPinnedObject() };
            var entropy = new Blob { Size = Purpose.Length, Data = pinnedPurpose.AddrOfPinnedObject() };
            if (!call(ref input, ref entropy, out var output)) throw new IOException("Windows couldn't seal or open the key.");
            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, output.Size);
            LocalFree(output.Data);
            return result;
        }
        finally
        {
            pinnedBytes.Free();
            pinnedPurpose.Free();
        }
    }
}
#endif
