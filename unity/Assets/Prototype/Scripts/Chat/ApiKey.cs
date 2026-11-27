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

    public static IKeyVault Vault { get; set; } = PlatformVault();

    public static string TestFile { get; set; }

    public static void FindFile() => ownFile ??= ChemGrid.Folders.ApiKeyFile;

    static string KeyFile => TestFile ?? ownFile;

    public static bool Saved => Vault != null && KeyFile != null && File.Exists(KeyFile);

    public static string Current => Read() ?? Environment.GetEnvironmentVariable(ClaudeRun.GameKey);

    public static bool InPlay => Saved || (Vault == null && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ClaudeRun.GameKey)));

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

    public static void Remove()
    {
        if (KeyFile != null && File.Exists(KeyFile)) File.Delete(KeyFile);
        PlayerPrefs.DeleteKey(ChosenModel);
        PlayerPrefs.DeleteKey(ListedModels);
        PlayerPrefs.Save();
    }

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

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        TestFile = null;
        session = null;
    }

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
